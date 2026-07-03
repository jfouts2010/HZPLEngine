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
            this.planningIntelligence = planningIntelligence;
            var projectedEffects = new ProjectedAirEffectService();
            var priorityService = new AirMissionPriorityService(module);
            requestGenerator = new AirMissionRequestGenerator(priorityService);
            packageBuilder = new AirPackageBuilder(
                gameManager,
                module,
                projectedEffects,
                priorityService);
            aircraftReservations = new AircraftReservationService(gameManager.squadronSystem);
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
            foreach (var commander in GetCommanders())
            {
                RebuildGlobalPlan(commander);
                FulfillRequests(commander);
            }
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
