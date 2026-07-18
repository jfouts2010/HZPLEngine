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

        private readonly AirPlanningIntelligence planningIntelligence;
        private readonly AirMissionPriorityService priorityService;
        private readonly AirControlAssessmentService airControlAssessmentService;
        private readonly AirMissionRequestGenerator requestGenerator;
        private readonly AirPackageBuilder packageBuilder;
        private readonly ProjectedAirEffectService projectedEffects;
        private readonly AircraftReservationService aircraftReservations;
        private readonly AllianceAirTaskingCommander blueforCommander;
        private readonly AllianceAirTaskingCommander redforCommander;
        private readonly GameManager gameManager;

        public AirTaskingSystem(
            GameManager gameManager,
            ModuleDefinition module)
        {
            this.gameManager = gameManager;
            planningIntelligence = new AirPlanningIntelligence(gameManager);
            projectedEffects = new ProjectedAirEffectService(
                gameManager,
                module);
            priorityService = new AirMissionPriorityService(module);
            airControlAssessmentService = new AirControlAssessmentService(
                gameManager.CampaignTiles,
                gameManager.SimulationSettings.TileDistanceKM,
                gameManager.Tiles
                    .OfType<LandTileData>()
                    .Where(tile => tile.Controller == Alliance.Neutral)
                    .Select(tile => tile.TileId)
                    .Distinct()
                    .ToList());
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

        public bool RetainsProjectedBarcapCoverage(AirFlight flight)
        {
            var coverage = flight?.PlannedBarcapCoverage;
            return coverage != null
                   && projectedEffects.RetainsPlannedBarcapWeaponCapability(
                       flight,
                       coverage);
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
            RecordAirControlObservations(currentTime);
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
            var airborneAircraftIds = GetAirborneFlights()
                .SelectMany(flight => flight.AircraftIds)
                .ToHashSet();
            var snapshot = planningIntelligence.CreateSnapshot(
                commander.Alliance,
                airborneAircraftIds);
            var cadenceHours = gameManager.SimulationSettings.OperationalCadenceHours;
            var generatedRequests = requestGenerator.Generate(
                commander,
                snapshot,
                cadenceHours);
            commander.AddMissionRequests(generatedRequests, gameManager.CurrentTime);
        }

        private void RecordAirControlObservations(DateTime currentTime)
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
                        || !flight.HasPosition)
                        continue;

                    foreach (var commander in GetCommanders())
                    {
                        Guid contactId;
                        Vector3 observedPosition;
                        int estimatedAircraftCount;
                        float combatPower;
                        IReadOnlyList<AirCombatProjection> combatProjections;
                        float observationQuality;
                        if (commander.Alliance == package.Alliance)
                        {
                            if (!gameManager.squadronSystem.TryGetSquadron(
                                    flight.SquadronId,
                                    out var squadron))
                                continue;

                            var airborneAircraftCount = squadron.Aircraft.Count(aircraft =>
                                aircraft.AssignedFlightId == flight.FlightId
                                && aircraft.Status != CampaignAircraftStatus.Lost);
                            if (airborneAircraftCount <= 0)
                                continue;

                            contactId = flight.FlightId;
                            observedPosition = flight.PositionFeet;
                            estimatedAircraftCount = airborneAircraftCount;
                            combatProjections = priorityService
                                .CalculateAirborneAirCombatProjections(
                                    flight,
                                    squadron);
                            combatPower = combatProjections.Sum(
                                projection => projection.Power);
                            observationQuality = 1f;
                        }
                        else
                        {
                            var track = gameManager.GetAllianceIADS(commander.Alliance)?
                                .GetTrackForFlight(flight.FlightId);
                            if (track == null || track.IsStale)
                                continue;

                            contactId = track.TrackId;
                            observedPosition = track.LastKnownPositionFeet;
                            estimatedAircraftCount = track.EstimatedAircraftCount;
                            combatPower = track.EstimatedAirCombatPower;
                            combatProjections = priorityService
                                .CalculateTrackedAirCombatProjections(track);
                            observationQuality = track.Quality;
                        }

                        var tileId = AirspaceGeometry.TileCoordinateFromPositionFeet(
                            observedPosition,
                            gameManager.SimulationSettings.TileDistanceKM);
                        if (!airControlAssessmentService.ContainsTile(tileId))
                            continue;

                        airControlAssessmentService.RecordContact(
                            commander.Alliance,
                            package.Alliance,
                            contactId,
                            tileId,
                            estimatedAircraftCount,
                            combatPower,
                            combatProjections,
                            observationQuality,
                            currentTime);
                        observedContactIdsByAlliance[commander.Alliance].Add(
                            contactId);
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
            ReopenFulfilledBarcapCoverageGaps(commander);
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
            requests = OrderBarcapCoverageFirst(commander, requests);

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

        private void ReopenFulfilledBarcapCoverageGaps(
            AllianceAirTaskingCommander commander)
        {
            var planningStart =
                gameManager.CurrentTime + AirPackage.PreparationDelay;
            var requestsToReopen = commander.MissionRequests
                .Where(request =>
                    request.State == AirMissionRequestState.Fulfilled
                    && request.PlanningCycle == commander.PlanningCycle
                    && request.EffectEnd > planningStart
                    && request.RequestType ==
                        AirMissionRequestType.BarrierCombatAirPatrol
                    && request.BarcapBarrier?.BarrierTileIds?.Count > 0)
                .Where(request => projectedEffects.TryFindFirstBarcapCoverageGap(
                    commander,
                    request,
                    planningStart,
                    out _,
                    out _))
                .ToList();
            foreach (var request in requestsToReopen)
            {
                commander.ReopenFulfilledRequest(
                    request.MissionRequestId,
                    gameManager.CurrentTime,
                    "Projected BARCAP coverage developed a spatial or temporal gap.");
            }
        }

        private List<AirMissionRequest> OrderBarcapCoverageFirst(
            AllianceAirTaskingCommander commander,
            IReadOnlyList<AirMissionRequest> normallyOrdered)
        {
            var planningStart = gameManager.CurrentTime + AirPackage.PreparationDelay;
            var barcaps = normallyOrdered
                .Where(request => request.RequestType
                                  == AirMissionRequestType.BarrierCombatAirPatrol
                                  && request.BarcapBarrier?.BarrierTileIds?.Count > 0)
                .OrderBy(request => projectedEffects
                    .GetProjectedBarcapCoverageFraction(
                        commander,
                        request,
                        planningStart))
                .ThenByDescending(request => request.Priority)
                .ThenBy(request => request.MissionArea.CenterTileId.x)
                .ThenBy(request => request.MissionArea.CenterTileId.y)
                .ThenBy(request => request.MissionArea.CenterTileId.z)
                .ToList();
            if (barcaps.Count < 2)
                return normallyOrdered.ToList();

            var nextBarcap = 0;
            return normallyOrdered
                .Select(request =>
                    request.RequestType
                    == AirMissionRequestType.BarrierCombatAirPatrol
                    && request.BarcapBarrier?.BarrierTileIds?.Count > 0
                        ? barcaps[nextBarcap++]
                        : request)
                .ToList();
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
