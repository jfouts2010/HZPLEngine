using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    /// <summary>
    /// Deterministically materializes an explicit package plan. Strategic choices
    /// such as target, package composition, squadron, strength, and route geometry
    /// must already be present in the plan.
    /// </summary>
    public sealed class AirPackageBuilder
    {
        private readonly GameManager gameManager;
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition>
            aircraftTypes;
        private readonly AirLoadoutPlanner loadoutPlanner;

        public AirPackageBuilder(GameManager gameManager, ModuleDefinition module)
        {
            this.gameManager = gameManager
                               ?? throw new ArgumentNullException(nameof(gameManager));
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            aircraftTypes = module.AircraftTypeDefinitions.ToDictionary(
                definition => definition.AircraftTypeDefinitionId);
            loadoutPlanner = new AirLoadoutPlanner(
                module,
                alliance => gameManager.OrdnanceAllowances.TryGetValue(
                    alliance,
                    out var allowed)
                    ? allowed
                    : Array.Empty<Guid>());
        }

        public bool TryBuild(
            AirPackagePlan plan,
            DateTime currentTime,
            out AirPackage package,
            out string reason)
        {
            package = null;
            reason = ValidatePlan(plan, currentTime);
            if (!string.IsNullOrEmpty(reason))
                return false;

            var candidate = new AirPackage
            {
                PackageId = plan.PlanId,
                PlanId = plan.PlanId,
                Alliance = plan.Alliance,
                OperationType = plan.OperationType,
                OperationArea = new AirMissionArea(
                    plan.OperationArea.CenterTileId,
                    plan.OperationArea.RadiusKm),
                CreatedAt = currentTime,
                BarcapBarrier = plan.BarcapBarrier?.Clone(),
                DeadPlan = plan.DeadPlan?.Clone(),
                StrikePlan = plan.StrikePlan?.Clone(),
                Rationale = plan.Rationale ?? string.Empty
            };
            var selectedAircraftIds = new HashSet<Guid>();
            var flightsByPlanId = new Dictionary<Guid, AirFlight>();

            foreach (var flightPlan in plan.Flights)
            {
                if (!TryCreateFlight(
                        plan,
                        flightPlan,
                        currentTime,
                        selectedAircraftIds,
                        out var flight,
                        out reason))
                {
                    return false;
                }

                candidate.Flights.Add(flight);
                flightsByPlanId.Add(flightPlan.FlightPlanId, flight);
            }

            for (var index = 0; index < plan.Flights.Count; index++)
            {
                var flightPlan = plan.Flights[index];
                var flight = candidate.Flights[index];
                if (!flight.IsEscort)
                    continue;

                var protectedIds = (flightPlan.ProtectedFlightPlanIds
                                    ?? new List<Guid>())
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();
                if (protectedIds.Count == 0
                    || protectedIds.Any(id =>
                        id == flightPlan.FlightPlanId
                        || !flightsByPlanId.ContainsKey(id)))
                {
                    reason = $"Escort flight plan {flightPlan.FlightPlanId} has an invalid protected-flight assignment.";
                    return false;
                }

                flight.ProtectedFlightIds.AddRange(
                    protectedIds.Select(id => flightsByPlanId[id].FlightId));
            }

            if (candidate.Flights.All(flight => flight.IsEscort)
                || !candidate.Flights.Any(flight =>
                    IsPrimaryTaskForOperation(
                        candidate.OperationType,
                        flight.TaskType)))
            {
                reason = "The package plan has no primary flight for its operation.";
                return false;
            }

            package = candidate;
            return true;
        }

        private string ValidatePlan(AirPackagePlan plan, DateTime currentTime)
        {
            if (plan == null)
                return "An air package plan is required.";
            if (plan.PlanId == Guid.Empty)
                return "An air package plan requires a stable identifier.";
            if (plan.Alliance != Alliance.Bluefor
                && plan.Alliance != Alliance.Redfor)
                return "An air package plan requires an active alliance.";
            if (plan.AvailableAt > currentTime)
                return "The air package plan is not available yet.";
            if (plan.EffectStart < currentTime + AirPackage.PreparationDelay)
                return "The air package plan does not allow the required preparation delay.";
            if (plan.EffectEnd < plan.EffectStart)
                return "The air package plan has an invalid effect window.";
            if (plan.OperationArea == null)
                return "The air package plan requires an operation area.";
            if (plan.Flights == null || plan.Flights.Count == 0)
                return "The air package plan requires at least one flight.";
            if (plan.Flights.Any(flight => flight == null))
                return "The air package plan contains a null flight plan.";
            if (plan.Flights.Any(flight => flight.FlightPlanId == Guid.Empty)
                || plan.Flights.Select(flight => flight.FlightPlanId)
                    .Distinct().Count() != plan.Flights.Count)
                return "Flight plans require unique, stable identifiers.";
            if (plan.OperationType == AirOperationType.Barcap
                && (plan.BarcapBarrier?.BarrierTileIds == null
                    || plan.BarcapBarrier.BarrierTileIds.Count == 0))
                return "A BARCAP package plan requires an explicit barrier.";
            if (plan.OperationType == AirOperationType.Barcap
                && plan.Flights.Any(flight =>
                    flight.TaskType == AirFlightTaskType.Barcap
                    && (flight.BarcapCoverage == null
                        || flight.BarcapCoverage.BarrierId
                        != plan.BarcapBarrier.BarrierId
                        || flight.BarcapCoverage.CoveredBarrierTileIds == null
                        || flight.BarcapCoverage.CoveredBarrierTileIds.Count == 0)))
            {
                return "Every BARCAP flight requires explicit coverage for the package barrier.";
            }
            if (plan.OperationType == AirOperationType.Dead
                && (plan.DeadPlan == null
                    || plan.DeadPlan.TargetSiteId == Guid.Empty))
                return "A DEAD package plan requires an explicit target site.";
            if (plan.OperationType == AirOperationType.Strike)
            {
                if (plan.StrikePlan == null
                    || plan.StrikePlan.TargetAirportBuildingId == Guid.Empty)
                    return "A Strike package plan requires an explicit target airport.";
                if (plan.StrikePlan.Purpose != StrikePurpose.OffensiveCounterAir)
                    return "The current Strike implementation requires an offensive-counter-air purpose.";
                if (plan.StrikePlan.DesiredRunwayDamagePerChannel < 1
                    || plan.StrikePlan.DesiredRunwayDamagePerChannel
                    > AirportRunwayChannel.MaximumDamageLevel)
                    return $"A Strike package plan requires runway damage from 1 to {AirportRunwayChannel.MaximumDamageLevel}.";
                if (!gameManager.buildingSystem.TryGetBuilding(
                        plan.StrikePlan.TargetAirportBuildingId,
                        out var targetBuilding)
                    || targetBuilding is not Airport targetAirport
                    || !gameManager.tileSystem.TryGetLand(
                        targetAirport.TileId,
                        out var targetTile)
                    || targetTile.Controller == Alliance.Neutral
                    || targetTile.Controller == plan.Alliance)
                    return "A Strike package plan requires a currently hostile target airport.";
                if (!plan.OperationArea.Contains(targetAirport.TileId))
                    return "The target airport must lie inside the Strike operation area.";
                if (plan.Flights.Any(flight =>
                        flight.TaskType == AirFlightTaskType.Strike
                        && flight.StrikeAssignment == StrikeAssignment.None))
                    return "Every Strike flight requires an explicit strike assignment.";
                if (plan.Flights.Any(flight =>
                        flight.TaskType == AirFlightTaskType.Strike
                        && flight.StrikeAssignment
                        == StrikeAssignment.RunwayDenial)
                    && targetAirport.NominalRunwayChannelCount == 0)
                    return "A runway-denial Strike flight requires an airport with at least one runway channel.";
                if (plan.Flights.Any(flight =>
                        flight.TaskType == AirFlightTaskType.Strike
                        && flight.StrikeAssignment
                        == StrikeAssignment.AirbaseFacilities)
                    && !(plan.StrikePlan.AuthorizedFacilityTargetIds
                         ?? new List<Guid>()).Any(id => id != Guid.Empty))
                    return "An airbase-facilities Strike flight requires explicitly authorized facility targets.";
                if ((plan.StrikePlan.AuthorizedFacilityTargetIds
                     ?? new List<Guid>())
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .Any(id => !gameManager.buildingSystem.TryGetBuilding(
                                   id,
                                   out var facility)
                               || facility.BuildingId == targetAirport.BuildingId
                               || facility.TileId != targetAirport.TileId))
                    return "Every authorized airbase facility must be a building in the target airport's tile.";
            }

            return string.Empty;
        }

        private bool TryCreateFlight(
            AirPackagePlan packagePlan,
            AirFlightPlan flightPlan,
            DateTime currentTime,
            ISet<Guid> packageAircraftIds,
            out AirFlight flight,
            out string reason)
        {
            flight = null;
            reason = string.Empty;
            if (flightPlan.SquadronId == Guid.Empty
                || !gameManager.squadronSystem.TryGetSquadron(
                    flightPlan.SquadronId,
                    out var squadron))
            {
                reason = $"Flight plan {flightPlan.FlightPlanId} references an unavailable squadron.";
                return false;
            }
            if (gameManager.GetCountryAlliance(squadron.CountryId)
                != packagePlan.Alliance)
            {
                reason = $"Flight plan {flightPlan.FlightPlanId} references a squadron from another alliance.";
                return false;
            }
            if (!aircraftTypes.TryGetValue(
                    squadron.AircraftTypeDefinitionId,
                    out var aircraftType))
            {
                reason = $"Flight plan {flightPlan.FlightPlanId} references an unavailable aircraft type.";
                return false;
            }

            if (!TrySelectAircraft(
                    flightPlan,
                    squadron,
                    packageAircraftIds,
                    out var aircraft,
                    out reason))
                return false;
            if (!TryResolveLoadout(
                    flightPlan,
                    aircraftType,
                    packagePlan.Alliance,
                    out var loadout,
                    out reason))
                return false;

            flight = new AirFlight
            {
                FlightId = flightPlan.FlightPlanId,
                SquadronId = squadron.SquadronId,
                TaskType = flightPlan.TaskType,
                StrikeAssignment = flightPlan.StrikeAssignment,
                AuthorizedSurfaceThreatSiteId =
                    flightPlan.TaskType == AirFlightTaskType.DeadAttack
                    || flightPlan.TaskType == AirFlightTaskType.SeadEscort
                        ? packagePlan.DeadPlan?.TargetSiteId ?? Guid.Empty
                        : Guid.Empty,
                IsRequired = flightPlan.IsRequired
            };
            flight.AircraftIds.AddRange(aircraft.Select(item => item.AircraftId));
            foreach (var item in aircraft)
            {
                flight.PlannedAircraftLoadouts.Add(
                    new PlannedAircraftLoadout(item.AircraftId, loadout));
            }

            if (!TryMaterializeRoute(
                    packagePlan,
                    flightPlan,
                    flight,
                    squadron,
                    aircraftType,
                    currentTime,
                    out reason))
            {
                flight = null;
                return false;
            }

            return true;
        }

        private static bool TrySelectAircraft(
            AirFlightPlan plan,
            Squadron squadron,
            ISet<Guid> packageAircraftIds,
            out List<CampaignAircraft> selected,
            out string reason)
        {
            selected = new List<CampaignAircraft>();
            reason = string.Empty;
            var requestedIds = (plan.AircraftIds ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .ToList();
            var desiredCount = requestedIds.Count > 0
                ? requestedIds.Count
                : Math.Max(0, plan.AircraftCount);
            if (desiredCount <= 0
                || requestedIds.Distinct().Count() != requestedIds.Count)
            {
                reason = $"Flight plan {plan.FlightPlanId} has an invalid aircraft selection.";
                return false;
            }

            if (requestedIds.Count > 0)
            {
                var byId = squadron.Aircraft.ToDictionary(item => item.AircraftId);
                foreach (var id in requestedIds)
                {
                    if (!byId.TryGetValue(id, out var aircraft)
                        || aircraft.Status != CampaignAircraftStatus.Ready
                        || aircraft.AssignedFlightId != Guid.Empty
                        || !packageAircraftIds.Add(id))
                    {
                        reason = $"Flight plan {plan.FlightPlanId} references an unavailable aircraft.";
                        return false;
                    }
                    selected.Add(aircraft);
                }
                return true;
            }

            foreach (var aircraft in squadron.Aircraft)
            {
                if (selected.Count >= desiredCount)
                    break;
                if (aircraft.Status != CampaignAircraftStatus.Ready
                    || aircraft.AssignedFlightId != Guid.Empty
                    || !packageAircraftIds.Add(aircraft.AircraftId))
                    continue;
                selected.Add(aircraft);
            }
            if (selected.Count == desiredCount)
                return true;

            foreach (var aircraft in selected)
                packageAircraftIds.Remove(aircraft.AircraftId);
            selected.Clear();
            reason = $"Flight plan {plan.FlightPlanId} requires {desiredCount} ready aircraft from its named squadron.";
            return false;
        }

        private bool TryResolveLoadout(
            AirFlightPlan flightPlan,
            AircraftTypeDefinition aircraftType,
            Alliance alliance,
            out List<AircraftLoadoutItem> loadout,
            out string reason)
        {
            loadout = CloneLoadout(flightPlan.Loadout);
            reason = string.Empty;
            if (loadout.Count > 0)
            {
                return loadoutPlanner.TryValidateLoadout(
                    aircraftType,
                    alliance,
                    loadout,
                    out reason);
            }

            if (IsAirCombatTask(flightPlan.TaskType))
            {
                return loadoutPlanner.TryPlanAirCombatLoadout(
                    aircraftType,
                    alliance,
                    out loadout,
                    out reason);
            }
            if (flightPlan.TaskType == AirFlightTaskType.AirborneC2
                || flightPlan.TaskType == AirFlightTaskType.AerialRefueling)
                return true;

            reason = $"Flight task {flightPlan.TaskType} requires an explicit loadout.";
            return false;
        }

        private bool TryMaterializeRoute(
            AirPackagePlan packagePlan,
            AirFlightPlan flightPlan,
            AirFlight flight,
            Squadron squadron,
            AircraftTypeDefinition aircraftType,
            DateTime currentTime,
            out string reason)
        {
            reason = string.Empty;
            var missionPoints = flightPlan.MissionWaypointsFeet
                                ?? new List<Vector3>();
            if (!HasRequiredMissionGeometry(flightPlan.TaskType, missionPoints))
            {
                reason = $"Flight plan {flightPlan.FlightPlanId} does not provide the geometry required by {flightPlan.TaskType}.";
                return false;
            }
            if (!TryGetAirportPosition(
                    squadron.AirportBuildingId,
                    packagePlan.Alliance,
                    out var launchPosition,
                    out reason))
                return false;

            var recoveryAirportId = flightPlan.RecoveryAirportBuildingId
                                    == Guid.Empty
                ? squadron.AirportBuildingId
                : flightPlan.RecoveryAirportBuildingId;
            if (!TryGetAirportPosition(
                    recoveryAirportId,
                    packagePlan.Alliance,
                    out var recoveryPosition,
                    out reason))
                return false;

            var ingress = flightPlan.IngressWaypointsFeet ?? new List<Vector3>();
            var egress = flightPlan.EgressWaypointsFeet ?? new List<Vector3>();
            var effectStart = packagePlan.EffectStart;
            var effectEnd = packagePlan.EffectEnd;
            var route = new List<AirWaypoint>();
            DateTime takeoffTime;
            DateTime rendezvousTime = default;
            if (packagePlan.UseRendezvous)
            {
                var postRendezvous = ingress.Concat(
                    new[] { missionPoints[0] }).ToList();
                rendezvousTime = effectStart - TimeSpan.FromSeconds(
                    TravelSecondsAlong(
                        packagePlan.RendezvousPositionFeet,
                        postRendezvous,
                        aircraftType));
                takeoffTime = rendezvousTime - TimeSpan.FromSeconds(
                    TravelSecondsAlong(
                        launchPosition,
                        new[] { packagePlan.RendezvousPositionFeet },
                        aircraftType));
            }
            else
            {
                takeoffTime = effectStart - TimeSpan.FromSeconds(
                    TravelSecondsAlong(
                        launchPosition,
                        ingress.Concat(new[] { missionPoints[0] }).ToList(),
                        aircraftType));
            }

            if (takeoffTime < currentTime + AirPackage.PreparationDelay)
            {
                reason = $"Flight plan {flightPlan.FlightPlanId} cannot meet its effect time after preparation and transit.";
                return false;
            }

            route.Add(NewWaypoint(
                launchPosition,
                AirWaypointAction.Takeoff,
                takeoffTime,
                airportBuildingId: squadron.AirportBuildingId));
            var routePosition = launchPosition;
            var routeTime = takeoffTime;
            if (packagePlan.UseRendezvous)
            {
                route.Add(NewWaypoint(
                    packagePlan.RendezvousPositionFeet,
                    AirWaypointAction.Rendezvous,
                    rendezvousTime));
                routePosition = packagePlan.RendezvousPositionFeet;
                routeTime = rendezvousTime;
            }
            routeTime = AppendTransitRoute(
                route,
                routePosition,
                routeTime,
                ingress,
                aircraftType);

            BuildMissionRoute(
                route,
                packagePlan,
                flightPlan,
                aircraftType,
                effectStart,
                effectEnd,
                out routePosition,
                out routeTime);
            route.Add(NewWaypoint(
                routePosition,
                AirWaypointAction.ReturnToBase,
                routeTime));
            routeTime = AppendTransitRoute(
                route,
                routePosition,
                routeTime,
                egress,
                aircraftType);
            if (egress.Count > 0)
                routePosition = egress[egress.Count - 1];
            route.AddRange(AirRecoveryRouteBuilder.Build(
                routePosition,
                aircraftType,
                recoveryAirportId,
                recoveryPosition,
                routeTime));

            try
            {
                flight.MaterializeRoute(route);
                return true;
            }
            catch (ArgumentException exception)
            {
                reason = $"Flight plan {flightPlan.FlightPlanId} produced an invalid route: {exception.Message}";
                return false;
            }
        }

        private static void BuildMissionRoute(
            ICollection<AirWaypoint> route,
            AirPackagePlan packagePlan,
            AirFlightPlan flightPlan,
            AircraftTypeDefinition aircraftType,
            DateTime effectStart,
            DateTime effectEnd,
            out Vector3 returnPosition,
            out DateTime returnTime)
        {
            var points = flightPlan.MissionWaypointsFeet;
            var area = new AirMissionArea(
                packagePlan.OperationArea.CenterTileId,
                packagePlan.OperationArea.RadiusKm);
            switch (flightPlan.TaskType)
            {
                case AirFlightTaskType.Barcap:
                case AirFlightTaskType.AirborneC2:
                case AirFlightTaskType.AerialRefueling:
                case AirFlightTaskType.FighterEscort:
                case AirFlightTaskType.SeadEscort:
                {
                    var station = NewWaypoint(
                        points[0],
                        AirWaypointAction.StationEntry,
                        effectStart,
                        area,
                        flightPlan.TaskType == AirFlightTaskType.Barcap
                            ? flightPlan.BarcapCoverage
                            : null);
                    route.Add(station);
                    var stationTime = effectStart;
                    var previous = points[0];
                    for (var index = 1; index < points.Count; index++)
                    {
                        stationTime += TimeSpan.FromSeconds(
                            TravelSeconds(previous, points[index], aircraftType));
                        var endpoint = index == points.Count - 1;
                        route.Add(NewWaypoint(
                            points[index],
                            endpoint
                                ? AirWaypointAction.StationEndpoint
                                : AirWaypointAction.Transit,
                            stationTime,
                            hasRepeat: endpoint,
                            repeatFromWaypointId: endpoint
                                ? station.WaypointId
                                : default,
                            repeatUntil: endpoint ? effectEnd : default));
                        previous = points[index];
                    }
                    returnPosition = points[points.Count - 1];
                    returnTime = effectEnd;
                    if (flightPlan.TaskType == AirFlightTaskType.FighterEscort
                        || flightPlan.TaskType == AirFlightTaskType.SeadEscort)
                    {
                        route.Add(NewWaypoint(
                            returnPosition,
                            AirWaypointAction.MissionAction,
                            returnTime,
                            area));
                    }
                    return;
                }
                case AirFlightTaskType.OcaSweep:
                {
                    var station = NewWaypoint(
                        points[0],
                        AirWaypointAction.StationEntry,
                        effectStart,
                        area);
                    route.Add(station);
                    var time = effectStart;
                    var previous = points[0];
                    for (var index = 1; index < points.Count - 1; index++)
                    {
                        time += TimeSpan.FromSeconds(
                            TravelSeconds(previous, points[index], aircraftType));
                        var endpoint = index == points.Count - 2;
                        route.Add(NewWaypoint(
                            points[index],
                            endpoint
                                ? AirWaypointAction.StationEndpoint
                                : AirWaypointAction.Transit,
                            time,
                            repeatFromWaypointId: endpoint
                                ? station.WaypointId
                                : default));
                        previous = points[index];
                    }
                    time += TimeSpan.FromSeconds(
                        TravelSeconds(previous, points[points.Count - 1], aircraftType));
                    route.Add(NewWaypoint(
                        points[points.Count - 1],
                        AirWaypointAction.MissionAction,
                        time,
                        area));
                    returnPosition = points[points.Count - 1];
                    returnTime = time;
                    return;
                }
                case AirFlightTaskType.DeadAttack:
                case AirFlightTaskType.Strike:
                {
                    route.Add(NewWaypoint(
                        points[0],
                        AirWaypointAction.MissionAction,
                        effectStart,
                        area));
                    var stationTime = effectStart + TimeSpan.FromSeconds(
                        TravelSeconds(points[0], points[1], aircraftType));
                    var station = NewWaypoint(
                        points[1],
                        AirWaypointAction.StationEntry,
                        stationTime,
                        area);
                    route.Add(station);
                    var previous = points[1];
                    for (var index = 2; index < points.Count; index++)
                    {
                        stationTime += TimeSpan.FromSeconds(
                            TravelSeconds(previous, points[index], aircraftType));
                        var endpoint = index == points.Count - 1;
                        route.Add(NewWaypoint(
                            points[index],
                            endpoint
                                ? AirWaypointAction.StationEndpoint
                                : AirWaypointAction.Transit,
                            stationTime,
                            hasRepeat: endpoint,
                            repeatFromWaypointId: endpoint
                                ? station.WaypointId
                                : default,
                            repeatUntil: endpoint ? effectEnd : default));
                        previous = points[index];
                    }
                    returnPosition = points[points.Count - 1];
                    returnTime = effectEnd;
                    return;
                }
                default:
                {
                    var time = effectStart;
                    var previous = points[0];
                    route.Add(NewWaypoint(
                        previous,
                        AirWaypointAction.MissionAction,
                        time,
                        area));
                    for (var index = 1; index < points.Count; index++)
                    {
                        time += TimeSpan.FromSeconds(
                            TravelSeconds(previous, points[index], aircraftType));
                        route.Add(NewWaypoint(
                            points[index],
                            AirWaypointAction.MissionAction,
                            time,
                            area));
                        previous = points[index];
                    }
                    returnPosition = previous;
                    returnTime = time;
                    return;
                }
            }
        }

        private bool TryGetAirportPosition(
            Guid airportId,
            Alliance alliance,
            out Vector3 positionFeet,
            out string reason)
        {
            positionFeet = default;
            reason = string.Empty;
            if (airportId == Guid.Empty
                || !gameManager.buildingSystem.TryGetBuilding(
                    airportId,
                    out var building)
                || !(building is Airport)
                || gameManager.tileSystem.TryGetLand(
                    building.TileId,
                    out var tile) == false
                || tile.Controller != alliance)
            {
                reason = $"Airport {airportId} is unavailable to {alliance}.";
                return false;
            }

            positionFeet = building.PositionFeet;
            return true;
        }

        private static bool HasRequiredMissionGeometry(
            AirFlightTaskType task,
            IReadOnlyCollection<Vector3> points)
        {
            var count = points?.Count ?? 0;
            switch (task)
            {
                case AirFlightTaskType.Barcap:
                case AirFlightTaskType.AirborneC2:
                case AirFlightTaskType.AerialRefueling:
                case AirFlightTaskType.FighterEscort:
                case AirFlightTaskType.SeadEscort:
                    return count >= 2;
                case AirFlightTaskType.OcaSweep:
                case AirFlightTaskType.DeadAttack:
                case AirFlightTaskType.Strike:
                    return count >= 3;
                default:
                    return count >= 1;
            }
        }

        private static bool IsPrimaryTaskForOperation(
            AirOperationType operation,
            AirFlightTaskType task)
        {
            return operation == AirOperationType.Barcap
                   && task == AirFlightTaskType.Barcap
                   || operation == AirOperationType.OcaSweep
                   && task == AirFlightTaskType.OcaSweep
                   || operation == AirOperationType.AirborneC2
                   && task == AirFlightTaskType.AirborneC2
                   || operation == AirOperationType.AerialRefueling
                   && task == AirFlightTaskType.AerialRefueling
                   || operation == AirOperationType.Dead
                   && task == AirFlightTaskType.DeadAttack
                   || operation == AirOperationType.Strike
                   && task == AirFlightTaskType.Strike;
        }

        private static bool IsAirCombatTask(AirFlightTaskType task)
        {
            return task == AirFlightTaskType.Barcap
                   || task == AirFlightTaskType.OcaSweep
                   || task == AirFlightTaskType.FighterEscort;
        }

        private static List<AircraftLoadoutItem> CloneLoadout(
            IEnumerable<AircraftLoadoutItem> loadout)
        {
            return (loadout ?? Enumerable.Empty<AircraftLoadoutItem>())
                .Where(item => item != null)
                .Select(item => new AircraftLoadoutItem(
                    item.AircraftLoadoutStationDefinitionId,
                    item.AircraftCarriageConfigurationDefinitionId,
                    item.OrdnanceTypeDefinitionId,
                    item.Count))
                .ToList();
        }

        private static AirWaypoint NewWaypoint(
            Vector3 positionFeet,
            AirWaypointAction action,
            DateTime plannedArrivalTime,
            AirMissionArea effectArea = null,
            BarcapStationCoverage barcapCoverage = null,
            bool hasRepeat = false,
            Guid repeatFromWaypointId = default,
            DateTime repeatUntil = default,
            Guid airportBuildingId = default)
        {
            return new AirWaypoint(
                positionFeet,
                action,
                plannedArrivalTime,
                effectArea,
                barcapCoverage: barcapCoverage,
                hasRepeat: hasRepeat,
                repeatFromWaypointId: repeatFromWaypointId,
                repeatUntil: repeatUntil,
                airportBuildingId: airportBuildingId);
        }

        private static DateTime AppendTransitRoute(
            ICollection<AirWaypoint> route,
            Vector3 start,
            DateTime startTime,
            IReadOnlyList<Vector3> waypoints,
            AircraftTypeDefinition aircraftType)
        {
            var current = start;
            var time = startTime;
            foreach (var waypoint in waypoints ?? Array.Empty<Vector3>())
            {
                time += TimeSpan.FromSeconds(
                    TravelSeconds(current, waypoint, aircraftType));
                route.Add(NewWaypoint(
                    waypoint,
                    AirWaypointAction.Transit,
                    time));
                current = waypoint;
            }
            return time;
        }

        private static double TravelSecondsAlong(
            Vector3 start,
            IReadOnlyList<Vector3> waypoints,
            AircraftTypeDefinition aircraftType)
        {
            var seconds = 0d;
            var current = start;
            foreach (var waypoint in waypoints ?? Array.Empty<Vector3>())
            {
                seconds += TravelSeconds(current, waypoint, aircraftType);
                current = waypoint;
            }
            return seconds;
        }

        private static double TravelSeconds(
            Vector3 start,
            Vector3 end,
            AircraftTypeDefinition aircraftType)
        {
            return AirspaceGeometry.TravelSeconds(
                start,
                end,
                aircraftType.CruiseSpeedKnots,
                aircraftType.ClimbRateFeetPerMinute,
                aircraftType.DescentRateFeetPerMinute);
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
        public IReadOnlyList<KnownSamThreatEnvelope> KnownSamThreats { get; }
        public float ManeuverClearanceFeet { get; }

        public AirRouteGeometryPlanningContext(
            Vector3 ingressOrigin,
            Vector3 missionEntry,
            Vector3 missionExit,
            Vector3 recoveryDestination,
            float tileDistanceFeet,
            Guid routeKey,
            IReadOnlyList<KnownSamThreatEnvelope> knownSamThreats = null,
            float maneuverClearanceFeet = 0f)
        {
            IngressOrigin = ingressOrigin;
            MissionEntry = missionEntry;
            MissionExit = missionExit;
            RecoveryDestination = recoveryDestination;
            TileDistanceFeet = Math.Max(0f, tileDistanceFeet);
            RouteKey = routeKey;
            KnownSamThreats = knownSamThreats
                              ?? Array.Empty<KnownSamThreatEnvelope>();
            ManeuverClearanceFeet = Math.Max(0f, maneuverClearanceFeet);
        }
    }

    public sealed class AirRouteGeometry
    {
        public IReadOnlyList<Vector3> IngressWaypoints { get; }
        public IReadOnlyList<Vector3> EgressWaypoints { get; }
        public bool IsThreatSafe { get; }

        public AirRouteGeometry(
            IReadOnlyList<Vector3> ingressWaypoints,
            IReadOnlyList<Vector3> egressWaypoints,
            bool isThreatSafe = true)
        {
            IngressWaypoints = ingressWaypoints ?? Array.Empty<Vector3>();
            EgressWaypoints = egressWaypoints ?? Array.Empty<Vector3>();
            IsThreatSafe = isThreatSafe;
        }
    }

    public sealed class SeparatedIngressEgressRouteGeometryPlanner
        : IAirRouteGeometryPlanner
    {
        private const float MaximumOffsetLegFraction = 0.25f;

        public AirRouteGeometry Plan(AirRouteGeometryPlanningContext context)
        {
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
            var ingressPoints = new List<Vector3> { context.IngressOrigin };
            if (ingress.HasValue)
                ingressPoints.Add(ingress.Value);
            ingressPoints.Add(context.MissionEntry);
            var egressPoints = new List<Vector3> { context.MissionExit };
            if (egress.HasValue)
                egressPoints.Add(egress.Value);
            egressPoints.Add(context.RecoveryDestination);

            if (!KnownSamThreatGeometry.TryBuildAvoidingPath(
                    ingressPoints,
                    context.KnownSamThreats,
                    context.RouteKey,
                    context.ManeuverClearanceFeet,
                    out var safeIngress)
                || !KnownSamThreatGeometry.TryBuildAvoidingPath(
                    egressPoints,
                    context.KnownSamThreats,
                    context.RouteKey,
                    context.ManeuverClearanceFeet,
                    out var safeEgress))
            {
                return new AirRouteGeometry(
                    Array.Empty<Vector3>(),
                    Array.Empty<Vector3>(),
                    false);
            }

            return new AirRouteGeometry(
                safeIngress.Skip(1)
                    .Take(Math.Max(0, safeIngress.Count - 2)).ToList(),
                safeEgress.Skip(1)
                    .Take(Math.Max(0, safeEgress.Count - 2)).ToList());
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
