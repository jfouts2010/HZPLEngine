using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Models
{
    public sealed class AirTaskingSystem
    {
        public const int MaximumRequestEvaluationsPerAlliancePerTick = 8;
        public const int MaximumPackageCreationsPerAlliancePerTick = 4;

        private readonly IAirPlanningIntelligence planningIntelligence;
        private readonly AirMissionPriorityService priorityService;
        private readonly AirControlAssessmentService airControlAssessmentService;
        private readonly AirMissionRequestGenerator requestGenerator;
        private readonly AirPackageBuilder packageBuilder;
        private readonly AircraftReservationService aircraftReservations;
        private readonly AllianceAirTaskingCommander blueforCommander;
        private readonly AllianceAirTaskingCommander redforCommander;
        private readonly GameManager gameManager;

        public AirTaskingSystem(
            GameManager gameManager,
            ModuleDefinition module,
            IAirPlanningIntelligence planningIntelligence = null)
        {
            this.gameManager = gameManager;
            this.planningIntelligence = planningIntelligence
                                        ?? new PerfectAirPlanningIntelligence(gameManager);
            var projectedEffects = new ProjectedAirEffectService();
            priorityService = new AirMissionPriorityService(module);
            airControlAssessmentService = new AirControlAssessmentService(
                gameManager.CampaignTiles,
                gameManager.SimulationSettings.TileDistanceKM);
            requestGenerator = new AirMissionRequestGenerator(priorityService);
            packageBuilder = new AirPackageBuilder(
                gameManager,
                module,
                projectedEffects,
                priorityService);
            aircraftReservations = new AircraftReservationService(
                gameManager.squadronSystem,
                module,
                gameManager.GetCountryAlliance,
                alliance =>
                    gameManager.OrdnanceAllowances.TryGetValue(alliance, out var allowed)
                        ? allowed
                        : Array.Empty<Guid>());
            blueforCommander = new AllianceAirTaskingCommander(
                Alliance.Bluefor,
                GetDoctrine(Alliance.Bluefor));
            redforCommander = new AllianceAirTaskingCommander(
                Alliance.Redfor,
                GetDoctrine(Alliance.Redfor));
        }

        public AllianceAirTaskingCommander GetCommander(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => blueforCommander,
                Alliance.Redfor => redforCommander,
                _ => null
            };
        }

        public IEnumerable<AirPackage> GetPackages()
        {
            return GetCommanders()
                .SelectMany(commander => commander.Packages);
        }

        public IEnumerable<AirFlight> GetAirborneFlights()
        {
            return GetPackages()
                .SelectMany(package => package.Flights)
                .Where(flight => flight.IsAirborne && !flight.HasPhysicallyEnded);
        }

        public void Initialize()
        {
            airControlAssessmentService.Initialize(
                gameManager.CurrentTime,
                blueforCommander,
                redforCommander);
            foreach (var commander in GetCommanders())
            {
                RebuildGlobalPlan(commander);
                FulfillRequests(commander);
            }
        }

        public void AdvanceAirControl(DateTime currentTime)
        {
            airControlAssessmentService.RefreshIfDue(
                currentTime,
                blueforCommander,
                redforCommander);
            RecordPerfectAirControlObservations(currentTime);
        }

        public void GameTurn(bool crossedOperationalCadenceBoundary)
        {
            foreach (var commander in GetCommanders())
            {
                commander.ValidatePackageIntegrity(
                    aircraftReservations,
                    gameManager.CurrentTime);
                if (crossedOperationalCadenceBoundary)
                    RebuildGlobalPlan(commander);
                FulfillRequests(commander);
            }
        }

        public bool CancelPackage(
            Alliance alliance,
            Guid packageId,
            string reason)
        {
            var commander = GetCommander(alliance);
            return commander != null && commander.CancelPackage(
                packageId,
                aircraftReservations,
                gameManager.CurrentTime,
                reason);
        }

        private void RebuildGlobalPlan(AllianceAirTaskingCommander commander)
        {
            commander.BeginPlanningCycle(gameManager.CurrentTime);
            var snapshot = planningIntelligence.CreateSnapshot(commander.Alliance);
            var cadenceHours = gameManager.SimulationSettings.OperationalCadenceHours;
            var generatedRequests = requestGenerator.Generate(
                commander,
                snapshot,
                cadenceHours);
            commander.AddMissionRequests(generatedRequests, gameManager.CurrentTime);
        }

        private void RecordPerfectAirControlObservations(DateTime currentTime)
        {
            var observedContactIdsByAlliance = GetCommanders()
                .ToDictionary(
                    commander => commander.Alliance,
                    _ => new HashSet<Guid>());

            foreach (var package in GetPackages())
            {
                if (package == null || package.IsTerminal)
                    continue;

                foreach (var flight in package.Flights)
                {
                    if (flight == null
                        || !flight.IsAirborne
                        || flight.HasPhysicallyEnded
                        || !flight.HasPosition
                        || !gameManager.squadronSystem.TryGetSquadron(
                            flight.SquadronId,
                            out var squadron))
                        continue;

                    var airborneAircraftCount = squadron.Aircraft.Count(aircraft =>
                        aircraft.AssignedFlightId == flight.FlightId
                        && aircraft.Status != CampaignAircraftStatus.Lost);
                    if (airborneAircraftCount <= 0)
                        continue;

                    var tileId = AirspaceGeometry.TileCoordinateFromPositionFeet(
                        flight.PositionFeet,
                        gameManager.SimulationSettings.TileDistanceKM);
                    if (!airControlAssessmentService.ContainsTile(tileId))
                        continue;

                    var combatProjections = priorityService
                        .CalculateAirborneAirCombatProjections(
                            flight,
                            squadron);
                    var combatPower = combatProjections.Sum(
                        projection => projection.Power);
                    foreach (var commander in GetCommanders())
                    {
                        airControlAssessmentService.RecordContact(
                            commander.Alliance,
                            package.Alliance,
                            flight.FlightId,
                            tileId,
                            airborneAircraftCount,
                            combatPower,
                            combatProjections,
                            1f,
                            currentTime);
                        observedContactIdsByAlliance[commander.Alliance].Add(
                            flight.FlightId);
                    }
                }
            }

            foreach (var commander in GetCommanders())
            {
                airControlAssessmentService.EndContactsNotObserved(
                    commander.Alliance,
                    observedContactIdsByAlliance[commander.Alliance],
                    currentTime);
            }
        }

        private void FulfillRequests(AllianceAirTaskingCommander commander)
        {
            var evaluations = 0;
            var packagesCreated = 0;
            var requests = commander.MissionRequests
                .Where(request => !request.IsTerminal
                                  && request.PlanningCycle == commander.PlanningCycle
                                  && request.EffectEnd > gameManager.CurrentTime)
                .OrderBy(request => request.IsSupportRequest ? 0 : 1)
                .ThenByDescending(request => request.Priority)
                .ThenBy(request => request.RequestType)
                .ThenBy(request => request.MissionArea.CenterTileId.x)
                .ThenBy(request => request.MissionArea.CenterTileId.y)
                .ThenBy(request => request.MissionArea.CenterTileId.z)
                .ToList();

            foreach (var request in requests)
            {
                if (evaluations >= MaximumRequestEvaluationsPerAlliancePerTick
                    || packagesCreated >= MaximumPackageCreationsPerAlliancePerTick)
                    break;

                evaluations++;
                var outcome = packageBuilder.TryBuild(
                    commander,
                    request,
                    gameManager.CurrentTime,
                    out var package,
                    out var reason);
                if (outcome != AirPackageBuildOutcome.Built)
                {
                    switch (outcome)
                    {
                        case AirPackageBuildOutcome.AlreadySatisfied:
                            commander.MarkRequestFulfilled(
                                request.MissionRequestId,
                                gameManager.CurrentTime,
                                reason);
                            break;
                        case AirPackageBuildOutcome.EquivalentCommitment:
                            commander.MarkRequestInProgress(
                                request.MissionRequestId,
                                gameManager.CurrentTime,
                                reason);
                            break;
                        default:
                            commander.RecordRequestDeferred(
                                request.MissionRequestId,
                                gameManager.CurrentTime,
                                reason);
                            break;
                    }
                    continue;
                }

                if (commander.TryCommitPackage(
                        package,
                        aircraftReservations,
                        gameManager.CurrentTime,
                        out var commitReason))
                {
                    packagesCreated++;
                    continue;
                }

                commander.RecordRequestDeferred(
                    request.MissionRequestId,
                    gameManager.CurrentTime,
                    commitReason);
            }
        }

        private AllianceAirDoctrine GetDoctrine(Alliance alliance)
        {
            if (gameManager.CampaignTemplate?.AirDoctrineByAlliance != null
                && gameManager.CampaignTemplate.AirDoctrineByAlliance.TryGetValue(alliance, out var doctrine))
                return doctrine;

            return AllianceAirDoctrine.CreateDefault();
        }

        private IEnumerable<AllianceAirTaskingCommander> GetCommanders()
        {
            yield return blueforCommander;
            yield return redforCommander;
        }
    }

}
