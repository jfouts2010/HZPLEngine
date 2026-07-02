using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    public enum AirPackageBuildOutcome
    {
        Built,
        AlreadySatisfied,
        EquivalentCommitment,
        Deferred
    }

    public sealed class AirPackageBuilder
    {
        private const float DcaAndOcaAltitudeFeet = 40000f;
        private const float AwacsAltitudeFeet = 35000f;
        private const float TankerAltitudeFeet = 25000f;

        private readonly GameManager gameManager;
        private readonly ProjectedAirEffectService projectedEffects;
        private readonly AirMissionPriorityService priorityService;
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;
        private readonly IAirRouteGeometryPlanner routeGeometryPlanner;

        public AirPackageBuilder(
            GameManager gameManager,
            ModuleDefinition module,
            ProjectedAirEffectService projectedEffects,
            AirMissionPriorityService priorityService,
            IAirRouteGeometryPlanner routeGeometryPlanner = null)
        {
            this.gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            if (module == null)
                throw new ArgumentNullException(nameof(module));
            this.projectedEffects = projectedEffects
                                    ?? throw new ArgumentNullException(nameof(projectedEffects));
            this.priorityService = priorityService
                                   ?? throw new ArgumentNullException(nameof(priorityService));
            this.routeGeometryPlanner = routeGeometryPlanner
                                        ?? new SeparatedIngressEgressRouteGeometryPlanner();
            aircraftTypes = module.AircraftTypeDefinitions
                .Where(definition => definition != null)
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
        }

        public AirPackageBuildOutcome TryBuild(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime currentTime,
            out AirPackage package,
            out string reason)
        {
            package = null;
            reason = string.Empty;
            if (commander == null || request == null)
            {
                reason = "Commander and request are required.";
                return AirPackageBuildOutcome.Deferred;
            }

            return request.IsSupportRequest
                ? TryBuildSupportPackage(commander, request, currentTime, out package, out reason)
                : TryBuildCombatPackage(commander, request, currentTime, out package, out reason);
        }

        private AirPackageBuildOutcome TryBuildSupportPackage(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime currentTime,
            out AirPackage package,
            out string reason)
        {
            package = null;
            reason = string.Empty;
            var planningStart = currentTime + AirPackage.PreparationDelay;
            if (!projectedEffects.TryFindFirstCoverageGap(
                    commander,
                    request,
                    planningStart,
                    out var gapStart,
                    out var projectedSlots))
            {
                reason = "Desired support capacity is already projected.";
                return AirPackageBuildOutcome.AlreadySatisfied;
            }

            var requiredCapability = request.RequestType == AirMissionRequestType.ProvideAirborneC2
                ? AirSupportCapability.AirborneC2
                : AirSupportCapability.AerialRefueling;
            var remainingSlots = Math.Max(1, request.DesiredSupportSlots - projectedSlots);
            var candidates = GetFriendlySquadrons(commander.Alliance)
                .Where(squadron =>
                    aircraftTypes.TryGetValue(squadron.AircraftTypeDefinitionId, out var aircraftType)
                    && aircraftType.SupportCapability == requiredCapability
                    && aircraftType.SupportSlotCapacity > 0)
                .Select(squadron => new
                {
                    Squadron = squadron,
                    AircraftType = aircraftTypes[squadron.AircraftTypeDefinitionId],
                    AvailableAircraft = GetAvailableAircraft(squadron)
                })
                .Where(candidate => candidate.AvailableAircraft.Count > 0)
                .OrderBy(candidate => GetAirportDistance(candidate.Squadron, request.MissionArea.CenterTileId))
                .ThenBy(candidate => candidate.Squadron.SquadronId)
                .FirstOrDefault();
            if (candidates == null)
            {
                reason = $"No ready {requiredCapability} aircraft are available.";
                return AirPackageBuildOutcome.Deferred;
            }

            var aircraftCount = Math.Min(
                candidates.AvailableAircraft.Count,
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        remainingSlots / (double)candidates.AircraftType.SupportSlotCapacity)));
            var selectedAircraft = candidates.AvailableAircraft.Take(aircraftCount).ToList();
            var effectStart = gapStart > planningStart ? gapStart : planningStart;
            var effectEnd = request.EffectEnd;
            package = CreatePackage(request, currentTime, effectStart, effectEnd);
            var flight = CreateFlight(
                package,
                request,
                candidates.Squadron,
                selectedAircraft,
                effectStart,
                effectEnd);
            flight.ProvidedSupportSlots =
                selectedAircraft.Count * candidates.AircraftType.SupportSlotCapacity;
            package.Flights.Add(flight);
            if (!TryMaterializeRoutes(package, request, out reason))
            {
                package = null;
                return AirPackageBuildOutcome.Deferred;
            }

            reason = $"Proposed {flight.ProvidedSupportSlots} support slots.";
            return AirPackageBuildOutcome.Built;
        }

        private AirPackageBuildOutcome TryBuildCombatPackage(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime currentTime,
            out AirPackage package,
            out string reason)
        {
            package = null;
            reason = string.Empty;
            if (request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Discrete
                && projectedEffects.HasEquivalentDiscreteCommitment(commander, request))
            {
                reason = "An equivalent discrete effect is already committed.";
                return AirPackageBuildOutcome.EquivalentCommitment;
            }

            var planningStart = currentTime + AirPackage.PreparationDelay;
            DateTime effectStart;
            if (request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained)
            {
                if (!projectedEffects.TryFindFirstCoverageGap(
                        commander,
                        request,
                        planningStart,
                        out effectStart,
                        out _))
                {
                    reason = "Desired combat coverage is already projected.";
                    return AirPackageBuildOutcome.AlreadySatisfied;
                }
            }
            else
            {
                effectStart = planningStart > request.EffectStart
                    ? planningStart
                    : request.EffectStart;
            }

            var desiredStrength = Math.Max(1, request.DesiredAircraftStrength);
            var squadronCandidates = GetFriendlySquadrons(commander.Alliance)
                .Where(squadron =>
                    aircraftTypes.TryGetValue(squadron.AircraftTypeDefinitionId, out var aircraftType)
                    && priorityService.CanPerformAirCombat(aircraftType))
                .Select(squadron => new CombatSquadronCandidate(
                    squadron,
                    aircraftTypes[squadron.AircraftTypeDefinitionId],
                    GetAvailableAircraft(squadron),
                    GetAirportDistance(squadron, request.MissionArea.CenterTileId)))
                .Where(candidate => candidate.AvailableAircraft.Count > 0)
                .OrderBy(candidate => candidate.DistanceTiles)
                .ThenBy(candidate => candidate.Squadron.AirportBuildingId)
                .ThenBy(candidate => candidate.Squadron.SquadronId)
                .ToList();

            var selectedCandidates = SelectCombatAircraft(
                squadronCandidates,
                desiredStrength);
            if (selectedCandidates.Sum(candidate => candidate.Aircraft.Count) < desiredStrength)
            {
                reason = $"Only {selectedCandidates.Sum(candidate => candidate.Aircraft.Count)}"
                         + $" of {desiredStrength} required combat aircraft are feasible.";
                return AirPackageBuildOutcome.Deferred;
            }

            var effectEnd = request.EffectEnd;
            package = CreatePackage(request, currentTime, effectStart, effectEnd);

            foreach (var selected in selectedCandidates)
            {
                var flight = CreateFlight(
                    package,
                    request,
                    selected.Squadron,
                    selected.Aircraft,
                    effectStart,
                    effectEnd);
                package.Flights.Add(flight);
            }

            if (!TryMaterializeRoutes(package, request, out reason))
            {
                package = null;
                return AirPackageBuildOutcome.Deferred;
            }

            reason = $"Proposed {desiredStrength} combat aircraft.";
            return AirPackageBuildOutcome.Built;
        }

        private static List<SelectedCombatAircraft> SelectCombatAircraft(
            IReadOnlyList<CombatSquadronCandidate> candidates,
            int desiredStrength)
        {
            var sameAirportGroup = candidates
                .GroupBy(candidate => candidate.Squadron.AirportBuildingId)
                .Select(group => new
                {
                    Candidates = group.ToList(),
                    Count = group.Sum(candidate => candidate.AvailableAircraft.Count),
                    Distance = group.Min(candidate => candidate.DistanceTiles)
                })
                .Where(group => group.Count >= desiredStrength)
                .OrderBy(group => group.Distance)
                .ThenBy(group => group.Candidates[0].Squadron.AirportBuildingId)
                .FirstOrDefault();
            var orderedCandidates = sameAirportGroup?.Candidates ?? candidates.ToList();
            return TakeAircraft(orderedCandidates, desiredStrength);
        }

        private static List<SelectedCombatAircraft> TakeAircraft(
            IEnumerable<CombatSquadronCandidate> candidates,
            int desiredStrength)
        {
            var remaining = desiredStrength;
            var selected = new List<SelectedCombatAircraft>();
            foreach (var candidate in candidates)
            {
                if (remaining <= 0)
                    break;

                var aircraft = candidate.AvailableAircraft.Take(remaining).ToList();
                if (aircraft.Count == 0)
                    continue;

                selected.Add(new SelectedCombatAircraft(
                    candidate.Squadron,
                    candidate.AircraftType,
                    aircraft));
                remaining -= aircraft.Count;
            }

            return selected;
        }

        private AirPackage CreatePackage(
            AirMissionRequest request,
            DateTime currentTime,
            DateTime effectStart,
            DateTime effectEnd)
        {
            return new AirPackage
            {
                MissionRequestId = request.MissionRequestId,
                Alliance = request.Alliance,
                CreatedAt = currentTime,
                EarliestTakeoffTime = currentTime + AirPackage.PreparationDelay,
                EffectStart = effectStart,
                EffectEnd = effectEnd,
                HasRendezvous = false,
                Rationale = request.Rationale
            };
        }

        private static AirFlight CreateFlight(
            AirPackage package,
            AirMissionRequest request,
            Squadron squadron,
            IReadOnlyCollection<CampaignAircraft> aircraft,
            DateTime effectStart,
            DateTime effectEnd)
        {
            return new AirFlight
            {
                OwningPackageId = package.PackageId,
                SquadronId = squadron.SquadronId,
                MissionType = request.RequestType,
                IsRequired = true,
                AircraftIds = aircraft.Select(candidate => candidate.AircraftId).ToList(),
                PlannedTakeoffTime = package.EarliestTakeoffTime,
                EffectStart = effectStart,
                EffectEnd = effectEnd,
                MissionArea = new AirMissionArea(
                    request.MissionArea.CenterTileId,
                    request.MissionArea.RadiusTiles)
            };
        }

        private List<Squadron> GetFriendlySquadrons(Alliance alliance)
        {
            return (gameManager.squadronSystem.Squadrons ?? new List<Squadron>())
                .Where(squadron => squadron != null
                                   && gameManager.GetCountryAlliance(squadron.CountryId) == alliance)
                .OrderBy(squadron => squadron.SquadronId)
                .ToList();
        }

        private static List<CampaignAircraft> GetAvailableAircraft(Squadron squadron)
        {
            return (squadron.Aircraft ?? new List<CampaignAircraft>())
                .Where(aircraft => aircraft != null
                                   && aircraft.Status == CampaignAircraftStatus.Ready
                                   && aircraft.AssignedFlightId == Guid.Empty)
                .ToList();
        }

        private int GetAirportDistance(Squadron squadron, Vector3Int targetTile)
        {
            if (!gameManager.buildingSystem.TryGetBuilding(squadron.AirportBuildingId, out var building))
                return int.MaxValue;

            return AirMissionArea.HexDistance(building.TileId, targetTile);
        }

        private bool TryMaterializeRoutes(
            AirPackage package,
            AirMissionRequest request,
            out string reason)
        {
            reason = string.Empty;
            var plans = new List<RoutePlan>();
            foreach (var flight in package.Flights ?? new List<AirFlight>())
            {
                if (!gameManager.squadronSystem.TryGetSquadron(flight.SquadronId, out var squadron)
                    || !gameManager.buildingSystem.TryGetBuilding(squadron.AirportBuildingId, out var airport)
                    || !aircraftTypes.TryGetValue(squadron.AircraftTypeDefinitionId, out var aircraftType))
                {
                    reason = "A flight route could not resolve its squadron, airport, or aircraft type.";
                    return false;
                }

                plans.Add(new RoutePlan(
                    flight,
                    squadron,
                    aircraftType,
                    AirspaceGeometry.TileCenterFeet(
                        airport.TileId,
                        gameManager.SimulationSettings?.TileDistanceKM
                        ?? SimulationSettings.DefaultTileDistanceKM)));
            }

            var desiredMissionAltitude = GetMissionAltitudeFeet(request.RequestType);
            var missionAltitude = plans.Min(plan =>
                Math.Min(desiredMissionAltitude, plan.AircraftType.ServiceCeilingFeet));
            var missionCenter = AirspaceGeometry.TileCenterFeet(
                request.MissionArea.CenterTileId,
                gameManager.SimulationSettings?.TileDistanceKM
                ?? SimulationSettings.DefaultTileDistanceKM,
                missionAltitude);
            var tileDistanceFeet = (gameManager.SimulationSettings?.TileDistanceKM
                                    ?? SimulationSettings.DefaultTileDistanceKM)
                                   * AirspaceGeometry.FeetPerKilometer;
            var missionEntry = request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained
                ? missionCenter - Vector3.right * tileDistanceFeet * 0.5f
                : missionCenter;
            var missionExit = request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained
                ? missionCenter + Vector3.right * tileDistanceFeet * 0.5f
                : missionCenter;
            var combat = request.RequestType == AirMissionRequestType.DefensiveCounterAirPatrol
                         || request.RequestType == AirMissionRequestType.OffensiveCounterAirSweep;
            var hasRendezvous = combat && plans.Count > 1;
            var rendezvousPosition = Vector3.zero;
            var coordinatedSpeed = plans.Min(plan => Math.Max(1f, plan.AircraftType.CruiseSpeedKnots));
            if (hasRendezvous)
            {
                var baseCentroid = plans.Aggregate(
                    Vector3.zero,
                    (sum, plan) => sum + plan.BasePositionFeet) / plans.Count;
                rendezvousPosition = (baseCentroid + missionCenter) * 0.5f;
                rendezvousPosition.y = missionAltitude;
            }

            foreach (var plan in plans)
            {
                plan.RouteGeometry = routeGeometryPlanner.Plan(new AirRouteGeometryPlanningContext(
                    hasRendezvous ? rendezvousPosition : plan.BasePositionFeet,
                    missionEntry,
                    missionExit,
                    plan.BasePositionFeet,
                    tileDistanceFeet,
                    package.PackageId));
            }

            var earliestTakeoff = package.EarliestTakeoffTime;
            var plannedEffectStart = package.EffectStart;
            var rendezvousTime = plannedEffectStart;
            if (hasRendezvous)
            {
                rendezvousTime -= TimeSpan.FromSeconds(
                    TravelSecondsAlong(
                        rendezvousPosition,
                        plans[0].RouteGeometry.IngressWaypoints,
                        missionEntry,
                        coordinatedSpeed,
                        plans.Min(plan => plan.AircraftType.ClimbRateFeetPerMinute),
                        plans.Min(plan => plan.AircraftType.DescentRateFeetPerMinute)));
            }

            var requiredShift = TimeSpan.Zero;
            foreach (var plan in plans)
            {
                var takeoff = hasRendezvous
                    ? rendezvousTime - TimeSpan.FromSeconds(
                        AirspaceGeometry.TravelSeconds(
                            plan.BasePositionFeet,
                            rendezvousPosition,
                            plan.AircraftType.CruiseSpeedKnots,
                            plan.AircraftType.ClimbRateFeetPerMinute,
                            plan.AircraftType.DescentRateFeetPerMinute))
                    : plannedEffectStart - TimeSpan.FromSeconds(
                        TravelSecondsAlong(
                            plan.BasePositionFeet,
                            plan.RouteGeometry.IngressWaypoints,
                            missionEntry,
                            plan.AircraftType.CruiseSpeedKnots,
                            plan.AircraftType.ClimbRateFeetPerMinute,
                            plan.AircraftType.DescentRateFeetPerMinute));
                plan.PlannedTakeoff = takeoff;
                if (takeoff < earliestTakeoff && earliestTakeoff - takeoff > requiredShift)
                    requiredShift = earliestTakeoff - takeoff;
            }

            if (requiredShift > TimeSpan.Zero)
            {
                plannedEffectStart += requiredShift;
                rendezvousTime += requiredShift;
                foreach (var plan in plans)
                    plan.PlannedTakeoff += requiredShift;
            }

            if (plannedEffectStart >= package.EffectEnd)
            {
                reason = "Preparation and transit leave no time for the requested effect.";
                return false;
            }

            package.EffectStart = plannedEffectStart;
            package.HasRendezvous = hasRendezvous;
            foreach (var plan in plans)
            {
                BuildRoute(
                    plan,
                    request,
                    missionCenter,
                    plannedEffectStart,
                    package.EffectEnd,
                    hasRendezvous,
                    rendezvousPosition,
                    rendezvousTime,
                    coordinatedSpeed);
            }

            return true;
        }

        private void BuildRoute(
            RoutePlan plan,
            AirMissionRequest request,
            Vector3 missionCenter,
            DateTime effectStart,
            DateTime effectEnd,
            bool hasRendezvous,
            Vector3 rendezvousPosition,
            DateTime rendezvousTime,
            float coordinatedSpeed)
        {
            var flight = plan.Flight;
            var route = new List<AirWaypoint>();
            route.Add(NewWaypoint(plan.BasePositionFeet, AirWaypointAction.Takeoff, plan.PlannedTakeoff));
            if (hasRendezvous)
                route.Add(NewWaypoint(rendezvousPosition, AirWaypointAction.Rendezvous, rendezvousTime));

            AppendTransitRoute(
                route,
                hasRendezvous ? rendezvousPosition : plan.BasePositionFeet,
                hasRendezvous ? rendezvousTime : plan.PlannedTakeoff,
                plan.RouteGeometry.IngressWaypoints,
                plan.AircraftType,
                hasRendezvous ? coordinatedSpeed : plan.AircraftType.CruiseSpeedKnots);

            if (request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained)
            {
                var tileDistanceFeet = (gameManager.SimulationSettings?.TileDistanceKM
                                        ?? SimulationSettings.DefaultTileDistanceKM)
                                       * AirspaceGeometry.FeetPerKilometer;
                var trackOffset = Vector3.right * tileDistanceFeet * 0.5f;
                var stationEntry = NewWaypoint(
                    missionCenter - trackOffset,
                    AirWaypointAction.StationEntry,
                    effectStart);
                stationEntry.EffectArea = new AirMissionArea(
                    request.MissionArea.CenterTileId,
                    request.MissionArea.RadiusTiles);
                var stationEnd = NewWaypoint(
                    missionCenter + trackOffset,
                    AirWaypointAction.StationEndpoint,
                    effectStart + TimeSpan.FromSeconds(
                        AirspaceGeometry.HorizontalTravelSeconds(
                            tileDistanceFeet,
                            plan.AircraftType.CruiseSpeedKnots)));
                stationEnd.HasRepeat = true;
                stationEnd.RepeatFromWaypointId = stationEntry.WaypointId;
                stationEnd.RepeatUntil = effectEnd;
                route.Add(stationEntry);
                route.Add(stationEnd);
            }
            else
            {
                route.Add(NewWaypoint(
                    missionCenter,
                    AirWaypointAction.MissionAction,
                    effectStart));
            }

            var returnTime = request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained
                ? effectEnd
                : effectStart;
            var returnPosition = route[route.Count - 1].PositionFeet;
            route.Add(NewWaypoint(returnPosition, AirWaypointAction.ReturnToBase, returnTime));
            returnTime = AppendTransitRoute(
                route,
                returnPosition,
                returnTime,
                plan.RouteGeometry.EgressWaypoints,
                plan.AircraftType,
                plan.AircraftType.CruiseSpeedKnots);
            if (plan.RouteGeometry.EgressWaypoints.Count > 0)
                returnPosition = plan.RouteGeometry.EgressWaypoints[
                    plan.RouteGeometry.EgressWaypoints.Count - 1];
            AppendRecoveryRoute(route, plan, returnPosition, returnTime);

            flight.MaterializeRoute(
                route,
                plan.Squadron.AirportBuildingId,
                plan.PlannedTakeoff);
            flight.EffectStart = effectStart;
            flight.EffectEnd = effectEnd;
            flight.MissionArea = new AirMissionArea(
                request.MissionArea.CenterTileId,
                request.MissionArea.RadiusTiles);
        }

        private static void AppendRecoveryRoute(
            ICollection<AirWaypoint> route,
            RoutePlan plan,
            Vector3 fromPosition,
            DateTime returnTime)
        {
            var basePosition = plan.BasePositionFeet;
            var horizontal = new Vector3(
                fromPosition.x - basePosition.x,
                0f,
                fromPosition.z - basePosition.z);
            var distance = horizontal.magnitude;
            var descentMinutes = fromPosition.y
                                 / Math.Max(1f, plan.AircraftType.DescentRateFeetPerMinute);
            var descentDistance = plan.AircraftType.CruiseSpeedKnots
                                  * AirspaceGeometry.FeetPerNauticalMile
                                  * (descentMinutes / 60f);
            var approachDistance = Math.Min(distance, descentDistance);
            var approach = basePosition;
            if (distance > 0.01f)
                approach += horizontal.normalized * approachDistance;
            approach.y = fromPosition.y;
            var approachTime = returnTime + TimeSpan.FromSeconds(
                AirspaceGeometry.TravelSeconds(
                    fromPosition,
                    approach,
                    plan.AircraftType.CruiseSpeedKnots,
                    plan.AircraftType.ClimbRateFeetPerMinute,
                    plan.AircraftType.DescentRateFeetPerMinute));
            var landingTime = approachTime + TimeSpan.FromSeconds(
                AirspaceGeometry.TravelSeconds(
                    approach,
                    basePosition,
                    plan.AircraftType.CruiseSpeedKnots,
                    plan.AircraftType.ClimbRateFeetPerMinute,
                    plan.AircraftType.DescentRateFeetPerMinute));
            route.Add(NewWaypoint(approach, AirWaypointAction.Approach, approachTime));
            route.Add(NewWaypoint(basePosition, AirWaypointAction.Land, landingTime));
        }

        private static AirWaypoint NewWaypoint(
            Vector3 positionFeet,
            AirWaypointAction action,
            DateTime plannedArrivalTime)
        {
            return new AirWaypoint
            {
                PositionFeet = positionFeet,
                Action = action,
                PlannedArrivalTime = plannedArrivalTime
            };
        }

        private static DateTime AppendTransitRoute(
            ICollection<AirWaypoint> route,
            Vector3 start,
            DateTime startTime,
            IReadOnlyList<Vector3> transitPoints,
            AircraftTypeDefinition aircraftType,
            float speedKnots)
        {
            var position = start;
            var time = startTime;
            foreach (var point in transitPoints)
            {
                time += TimeSpan.FromSeconds(AirspaceGeometry.TravelSeconds(
                    position,
                    point,
                    speedKnots,
                    aircraftType.ClimbRateFeetPerMinute,
                    aircraftType.DescentRateFeetPerMinute));
                route.Add(NewWaypoint(point, AirWaypointAction.Transit, time));
                position = point;
            }

            return time;
        }

        private static double TravelSecondsAlong(
            Vector3 start,
            IReadOnlyList<Vector3> transitPoints,
            Vector3 end,
            float speedKnots,
            float climbRateFeetPerMinute,
            float descentRateFeetPerMinute)
        {
            var seconds = 0d;
            var position = start;
            foreach (var point in transitPoints)
            {
                seconds += AirspaceGeometry.TravelSeconds(
                    position,
                    point,
                    speedKnots,
                    climbRateFeetPerMinute,
                    descentRateFeetPerMinute);
                position = point;
            }

            return seconds + AirspaceGeometry.TravelSeconds(
                position,
                end,
                speedKnots,
                climbRateFeetPerMinute,
                descentRateFeetPerMinute);
        }

        private static float GetMissionAltitudeFeet(AirMissionRequestType missionType)
        {
            return missionType switch
            {
                AirMissionRequestType.ProvideAirborneC2 => AwacsAltitudeFeet,
                AirMissionRequestType.ProvideAerialRefueling => TankerAltitudeFeet,
                _ => DcaAndOcaAltitudeFeet
            };
        }

        private static DateTime Min(DateTime first, DateTime second)
        {
            return first <= second ? first : second;
        }

        private sealed class CombatSquadronCandidate
        {
            public Squadron Squadron { get; }
            public AircraftTypeDefinition AircraftType { get; }
            public List<CampaignAircraft> AvailableAircraft { get; }
            public int DistanceTiles { get; }

            public CombatSquadronCandidate(
                Squadron squadron,
                AircraftTypeDefinition aircraftType,
                List<CampaignAircraft> availableAircraft,
                int distanceTiles)
            {
                Squadron = squadron;
                AircraftType = aircraftType;
                AvailableAircraft = availableAircraft;
                DistanceTiles = distanceTiles;
            }
        }

        private sealed class RoutePlan
        {
            public readonly AirFlight Flight;
            public readonly Squadron Squadron;
            public readonly AircraftTypeDefinition AircraftType;
            public readonly Vector3 BasePositionFeet;
            public DateTime PlannedTakeoff;
            public AirRouteGeometry RouteGeometry;

            public RoutePlan(
                AirFlight flight,
                Squadron squadron,
                AircraftTypeDefinition aircraftType,
                Vector3 basePositionFeet)
            {
                Flight = flight;
                Squadron = squadron;
                AircraftType = aircraftType;
                BasePositionFeet = basePositionFeet;
            }
        }

        private sealed class SelectedCombatAircraft
        {
            public Squadron Squadron { get; }
            public AircraftTypeDefinition AircraftType { get; }
            public List<CampaignAircraft> Aircraft { get; }

            public SelectedCombatAircraft(
                Squadron squadron,
                AircraftTypeDefinition aircraftType,
                List<CampaignAircraft> aircraft)
            {
                Squadron = squadron;
                AircraftType = aircraftType;
                Aircraft = aircraft;
            }
        }
    }

    public interface IAirRouteGeometryPlanner
    {
        AirRouteGeometry Plan(AirRouteGeometryPlanningContext context);
    }

    public sealed class AirRouteGeometryPlanningContext
    {
        public Vector3 IngressOrigin { get; }
        public Vector3 MissionEntry { get; }
        public Vector3 MissionExit { get; }
        public Vector3 RecoveryDestination { get; }
        public float TileDistanceFeet { get; }
        public Guid RouteKey { get; }

        public AirRouteGeometryPlanningContext(
            Vector3 ingressOrigin,
            Vector3 missionEntry,
            Vector3 missionExit,
            Vector3 recoveryDestination,
            float tileDistanceFeet,
            Guid routeKey)
        {
            IngressOrigin = ingressOrigin;
            MissionEntry = missionEntry;
            MissionExit = missionExit;
            RecoveryDestination = recoveryDestination;
            TileDistanceFeet = tileDistanceFeet;
            RouteKey = routeKey;
        }
    }

    public sealed class AirRouteGeometry
    {
        public IReadOnlyList<Vector3> IngressWaypoints { get; }
        public IReadOnlyList<Vector3> EgressWaypoints { get; }

        public AirRouteGeometry(
            IReadOnlyList<Vector3> ingressWaypoints,
            IReadOnlyList<Vector3> egressWaypoints)
        {
            IngressWaypoints = ingressWaypoints ?? Array.Empty<Vector3>();
            EgressWaypoints = egressWaypoints ?? Array.Empty<Vector3>();
        }
    }

    public sealed class SeparatedIngressEgressRouteGeometryPlanner : IAirRouteGeometryPlanner
    {
        private const float MaximumOffsetLegFraction = 0.25f;

        public AirRouteGeometry Plan(AirRouteGeometryPlanningContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var side = SelectSide(context.RouteKey);
            var ingress = CreateOffsetMidpoint(
                context.IngressOrigin,
                context.MissionEntry,
                context.TileDistanceFeet,
                side);
            var egress = CreateOffsetMidpoint(
                context.MissionExit,
                context.RecoveryDestination,
                context.TileDistanceFeet,
                side);
            return new AirRouteGeometry(
                ingress.HasValue ? new[] { ingress.Value } : Array.Empty<Vector3>(),
                egress.HasValue ? new[] { egress.Value } : Array.Empty<Vector3>());
        }

        private static Vector3? CreateOffsetMidpoint(
            Vector3 start,
            Vector3 end,
            float desiredOffsetFeet,
            float side)
        {
            var horizontal = new Vector2(end.x - start.x, end.z - start.z);
            var distance = horizontal.magnitude;
            if (distance <= 0.01f)
                return null;

            var direction = horizontal / distance;
            var perpendicular = new Vector2(-direction.y, direction.x) * side;
            var offset = Math.Min(
                Math.Max(0f, desiredOffsetFeet),
                distance * MaximumOffsetLegFraction);
            var midpoint = (start + end) * 0.5f;
            midpoint.x += perpendicular.x * offset;
            midpoint.z += perpendicular.y * offset;
            return midpoint;
        }

        private static float SelectSide(Guid routeKey)
        {
            var parity = 0;
            foreach (var value in routeKey.ToByteArray())
                parity ^= value;
            return (parity & 1) == 0 ? 1f : -1f;
        }
    }

}
