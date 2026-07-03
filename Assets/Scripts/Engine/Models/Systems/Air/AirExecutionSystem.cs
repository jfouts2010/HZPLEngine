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
    public sealed class AirExecutionSystem
    {
        private const double MaximumIntegrationStepSeconds = 1d;
        private const float WaypointCaptureFeet = 100f;

        private readonly GameManager gameManager;
        private readonly AirTaskingSystem airTaskingSystem;
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;
        private readonly AirLoadoutPlanner loadoutPlanner;

        public AirExecutionSystem(
            GameManager gameManager,
            AirTaskingSystem airTaskingSystem,
            ModuleDefinition module)
        {
            this.gameManager = gameManager;
            this.airTaskingSystem = airTaskingSystem;
            aircraftTypes = module.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            loadoutPlanner = new AirLoadoutPlanner(
                module,
                alliance =>
                    gameManager.OrdnanceAllowances.TryGetValue(alliance, out var allowed)
                        ? allowed
                        : Array.Empty<Guid>());
        }

        public void GameTurn(DateTime previousTime, DateTime currentTime)
        {
            ResolveAirbaseOverruns(currentTime);
            var states = new Dictionary<Guid, FlightTickState>();
            foreach (var package in airTaskingSystem.GetPackages()
                         .OrderBy(candidate => candidate.PackageId))
            {
                foreach (var flight in package.Flights
                             .OrderBy(candidate => candidate.FlightId))
                {
                    var state = BeginTick(package, flight, previousTime, currentTime);
                    if (state == null)
                        continue;

                    states[flight.FlightId] = state;
                    AdvanceFlight(package, flight, state);
                }
            }

            var releasedAny = true;
            while (releasedAny)
            {
                releasedAny = false;
                foreach (var package in airTaskingSystem.GetPackages()
                             .Where(candidate => candidate.RendezvousWaypoint != null)
                             .OrderBy(candidate => candidate.PackageId))
                {
                    var required = package.Flights
                        .Where(flight => flight.IsRequired)
                        .ToList();
                    if (required.Count == 0
                        || required.Any(flight => !flight.IsWaitingAtRendezvous)
                        || required.Any(flight => !states.ContainsKey(flight.FlightId)))
                        continue;

                    foreach (var flight in required)
                    {
                        flight.ReleaseRendezvous();
                        AdvanceFlight(package, flight, states[flight.FlightId]);
                    }

                    releasedAny = true;
                }
            }

            ResolvePackageOutcomes(currentTime);
        }

        private FlightTickState BeginTick(
            AirPackage package,
            AirFlight flight,
            DateTime previousTime,
            DateTime currentTime)
        {
            if (flight.HasPhysicallyEnded)
                return null;
            if (!TryGetFlightContext(flight, out var squadron, out var aircraftType))
            {
                throw new InvalidOperationException(
                    $"Flight {flight.FlightId} context became unavailable.");
            }

            var cursor = previousTime;
            if (flight.ExecutionPhase == FlightExecutionPhase.AwaitingTakeoff)
            {
                if (flight.LifecycleState != AirTaskingLifecycleState.Committed
                    || flight.PlannedTakeoffTime > currentTime)
                    return null;
                if (!IsAirportFriendly(flight.LaunchAirportBuildingId, package.Alliance))
                {
                    LoseGroundedFlight(flight, squadron, currentTime);
                    return null;
                }

                var takeoffTime = flight.PlannedTakeoffTime > previousTime
                    ? flight.PlannedTakeoffTime
                    : previousTime;
                var takeoff = flight.Route.FirstOrDefault();
                if (takeoff == null)
                {
                    throw new InvalidOperationException(
                        $"Flight {flight.FlightId} route has no takeoff waypoint.");
                }

                if (!flight.TryTakeOff(takeoffTime))
                {
                    throw new InvalidOperationException(
                        $"Flight {flight.FlightId} could not transition to takeoff.");
                }
                cursor = takeoffTime;
            }

            if (!flight.IsAirborne)
                return null;

            flight.ContinueAbortRecovery(cursor);
            AbortIfMissionUsefulOrdnanceExhausted(flight, squadron, cursor);
            return new FlightTickState(
                cursor,
                Math.Max(0d, (currentTime - cursor).TotalSeconds));
        }

        private void AbortIfMissionUsefulOrdnanceExhausted(
            AirFlight flight,
            Squadron squadron,
            DateTime occurredAt)
        {
            if (!IsTimeBasedAirCombatMission(flight.MissionType)
                || flight.LifecycleState != AirTaskingLifecycleState.Active
                || flight.ExecutionPhase == FlightExecutionPhase.Returning
                || flight.ExecutionPhase == FlightExecutionPhase.Landing
                || flight.ExecutionPhase == FlightExecutionPhase.Ended
                || flight.MissionAchieved)
                return;

            var hasMissionUsefulOrdnance = squadron.Aircraft
                .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                   && aircraft.Status != CampaignAircraftStatus.Lost)
                .Any(loadoutPlanner.HasMissionUsefulAirCombatOrdnance);
            if (hasMissionUsefulOrdnance)
                return;

            flight.Cancel(
                occurredAt,
                "Flight exhausted mission-useful air-to-air ordnance.");
        }

        private void AdvanceFlight(
            AirPackage package,
            AirFlight flight,
            FlightTickState state)
        {
            if (!TryGetFlightContext(flight, out _, out var aircraftType))
            {
                throw new InvalidOperationException(
                    $"Flight {flight.FlightId} aircraft performance became unavailable.");
            }

            while (state.RemainingSeconds > 0.0001d
                   && flight.IsAirborne
                   && !flight.IsWaitingAtRendezvous)
            {
                var waypoint = flight.CurrentWaypoint;
                if (waypoint == null)
                {
                    throw new InvalidOperationException(
                        $"Flight {flight.FlightId} exhausted its route before landing.");
                }

                var speedKnots = GetGuidanceSpeedKnots(package, flight, aircraftType);
                if (HasReached(flight.PositionFeet, waypoint.PositionFeet))
                {
                    flight.UpdateKinematics(waypoint.PositionFeet, flight.HeadingDegrees);
                    HandleWaypoint(package, flight, state.Cursor);
                    continue;
                }

                var step = Math.Min(MaximumIntegrationStepSeconds, state.RemainingSeconds);
                var secondsToReach = EstimateReachSeconds(
                    flight,
                    waypoint,
                    aircraftType,
                    speedKnots,
                    step);
                if (secondsToReach >= 0d)
                {
                    flight.UpdateKinematics(waypoint.PositionFeet, flight.HeadingDegrees);
                    state.Advance(secondsToReach);
                    HandleWaypoint(package, flight, state.Cursor);
                    continue;
                }

                IntegrateMotion(flight, waypoint.PositionFeet, aircraftType, speedKnots, step);
                state.Advance(step);
            }
        }

        private void HandleWaypoint(
            AirPackage package,
            AirFlight flight,
            DateTime occurredAt)
        {
            switch (flight.CrossCurrentWaypoint(occurredAt))
            {
                case FlightWaypointTransition.RecoveryStarted:
                    if (!EnsureRecoveryRoute(package, flight, occurredAt))
                        LoseAirborneFlight(flight, occurredAt, "No friendly recovery airport remains.");
                    return;

                case FlightWaypointTransition.LandingRequired:
                    CompleteLanding(flight, occurredAt);
                    return;

                default:
                    return;
            }
        }

        private bool EnsureRecoveryRoute(
            AirPackage package,
            AirFlight flight,
            DateTime currentTime)
        {
            if (IsAirportFriendly(flight.RecoveryAirportBuildingId, package.Alliance))
                return true;
            if (!TryGetFlightContext(flight, out var squadron, out var aircraftType))
            {
                throw new InvalidOperationException(
                    $"Flight {flight.FlightId} context became unavailable during recovery.");
            }

            Guid recoveryAirportId;
            if (IsAirportFriendly(squadron.AirportBuildingId, package.Alliance))
            {
                recoveryAirportId = squadron.AirportBuildingId;
            }
            else
            {
                var nearest = GetFriendlyAirports(package.Alliance)
                    .Select(airport => new
                    {
                        Airport = airport,
                        Position = AirspaceGeometry.TileCenterFeet(
                            airport.TileId,
                            gameManager.SimulationSettings.TileDistanceKM)
                    })
                    .OrderBy(candidate => Vector2.Distance(
                        new Vector2(flight.PositionFeet.x, flight.PositionFeet.z),
                        new Vector2(candidate.Position.x, candidate.Position.z)))
                    .ThenBy(candidate => candidate.Airport.BuildingId)
                    .FirstOrDefault();
                if (nearest == null)
                    return false;
                recoveryAirportId = nearest.Airport.BuildingId;
            }

            if (!gameManager.buildingSystem.TryGetBuilding(recoveryAirportId, out var recovery))
                return false;
            var recoveryTail = AirRecoveryRouteBuilder.Build(
                flight.PositionFeet,
                aircraftType,
                recoveryAirportId,
                AirspaceGeometry.TileCenterFeet(
                    recovery.TileId,
                    gameManager.SimulationSettings.TileDistanceKM),
                currentTime);
            flight.ReplaceRecoveryRoute(recoveryTail);
            return true;
        }

        private void CompleteLanding(AirFlight flight, DateTime occurredAt)
        {
            if (!TryGetFlightContext(flight, out var squadron, out _))
            {
                throw new InvalidOperationException(
                    $"Flight {flight.FlightId} squadron disappeared during recovery.");
            }

            foreach (var aircraft in squadron.Aircraft)
            {
                if (aircraft.AssignedFlightId != flight.FlightId
                    || aircraft.Status == CampaignAircraftStatus.Lost)
                    continue;
                aircraft.ClearLoadout();
                aircraft.ReleaseFromFlight(flight.FlightId);
            }

            squadron.AirportBuildingId = flight.RecoveryAirportBuildingId;
            gameManager.squadronSystem.RebuildIndex();
            flight.Land(occurredAt);
        }

        private void ResolvePackageOutcomes(DateTime currentTime)
        {
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var commander = airTaskingSystem.GetCommander(alliance);
                foreach (var package in commander.Packages
                             .Where(candidate => candidate.HasPhysicallyEnded))
                {
                    if (package.LifecycleState == AirTaskingLifecycleState.Completed)
                    {
                        commander.MarkRequestFulfilled(
                            package.MissionRequestId,
                            currentTime,
                            "All package flights completed their routes and recovered.");
                    }
                }
            }
        }

        private void ResolveAirbaseOverruns(DateTime currentTime)
        {
            var flights = airTaskingSystem.GetPackages()
                .SelectMany(package => package.Flights)
                .ToList();
            var activeFlightIds = flights
                .Where(flight => flight.IsAirborne)
                .Select(flight => flight.FlightId)
                .ToHashSet();
            foreach (var squadron in gameManager.squadronSystem.Squadrons)
            {
                var alliance = gameManager.GetCountryAlliance(squadron.CountryId);
                if (IsAirportFriendly(squadron.AirportBuildingId, alliance))
                    continue;

                foreach (var flight in flights.Where(flight =>
                             flight.SquadronId == squadron.SquadronId
                             && flight.ExecutionPhase == FlightExecutionPhase.AwaitingTakeoff))
                {
                    flight.Cancel(
                        currentTime,
                        "Launch airport was overrun before takeoff.");
                }

                foreach (var aircraft in squadron.Aircraft)
                {
                    if (activeFlightIds.Contains(aircraft.AssignedFlightId))
                        continue;
                    aircraft.Status = CampaignAircraftStatus.Lost;
                    aircraft.AssignedFlightId = Guid.Empty;
                    aircraft.ClearLoadout();
                }
            }
        }

        private bool IsAirportFriendly(Guid airportId, Alliance alliance)
        {
            if (airportId == Guid.Empty
                || !gameManager.buildingSystem.TryGetBuilding(airportId, out var building)
                || building is not Airport)
                return false;
            var landTile = gameManager.Tiles
                .OfType<LandTileData>()
                .FirstOrDefault(tile => tile.TileId == building.TileId);
            return landTile != null && landTile.Controller == alliance;
        }

        private IEnumerable<Airport> GetFriendlyAirports(Alliance alliance)
        {
            return gameManager.buildingSystem.GetBuildings<Airport>()
                .Where(airport =>
                {
                    var landTile = gameManager.Tiles
                        .OfType<LandTileData>()
                        .FirstOrDefault(tile => tile.TileId == airport.TileId);
                    return landTile != null && landTile.Controller == alliance;
                });
        }

        private bool TryGetFlightContext(
            AirFlight flight,
            out Squadron squadron,
            out AircraftTypeDefinition aircraftType)
        {
            aircraftType = null;
            return gameManager.squadronSystem.TryGetSquadron(flight.SquadronId, out squadron)
                   && aircraftTypes.TryGetValue(
                       squadron.AircraftTypeDefinitionId,
                       out aircraftType);
        }

        private static bool IsTimeBasedAirCombatMission(AirMissionRequestType missionType)
        {
            return missionType == AirMissionRequestType.DefensiveCounterAirPatrol
                   || missionType == AirMissionRequestType.OffensiveCounterAirSweep;
        }

        private float GetGuidanceSpeedKnots(
            AirPackage package,
            AirFlight flight,
            AircraftTypeDefinition ownType)
        {
            if (flight.ExecutionPhase == FlightExecutionPhase.Returning
                || flight.ExecutionPhase == FlightExecutionPhase.Landing
                || !flight.ExecutionEvents.Any(entry =>
                    entry.Action == AirWaypointAction.Rendezvous))
                return Math.Max(1f, ownType.CruiseSpeedKnots);

            return package.Flights
                .Where(candidate => candidate.IsRequired)
                .Select(candidate =>
                    TryGetFlightContext(candidate, out _, out var type)
                        ? Math.Max(1f, type.CruiseSpeedKnots)
                        : Math.Max(1f, ownType.CruiseSpeedKnots))
                .DefaultIfEmpty(Math.Max(1f, ownType.CruiseSpeedKnots))
                .Min();
        }

        private static double EstimateReachSeconds(
            AirFlight flight,
            AirWaypoint waypoint,
            AircraftTypeDefinition aircraftType,
            float speedKnots,
            double maximumStep)
        {
            var from = flight.PositionFeet;
            var to = waypoint.PositionFeet;
            var horizontal = Vector2.Distance(
                new Vector2(from.x, from.z),
                new Vector2(to.x, to.z));
            var desiredHeading = HeadingTo(from, to);
            var headingDifference = Math.Abs(Mathf.DeltaAngle(flight.HeadingDegrees, desiredHeading));
            var feetPerSecond = speedKnots * AirspaceGeometry.FeetPerNauticalMile / 3600f;
            var altitudeDelta = to.y - from.y;
            var verticalRate = (altitudeDelta >= 0f
                                   ? aircraftType.ClimbRateFeetPerMinute
                                   : aircraftType.DescentRateFeetPerMinute) / 60f;
            var horizontalSeconds = horizontal / Math.Max(1f, feetPerSecond);
            var verticalSeconds = Math.Abs(altitudeDelta) / Math.Max(1f, verticalRate);
            var needed = Math.Max(horizontalSeconds, verticalSeconds);
            return headingDifference <= 5f && needed <= maximumStep
                ? needed
                : -1d;
        }

        private static void IntegrateMotion(
            AirFlight flight,
            Vector3 target,
            AircraftTypeDefinition aircraftType,
            float speedKnots,
            double seconds)
        {
            var desiredHeading = HeadingTo(flight.PositionFeet, target);
            var heading = Mathf.MoveTowardsAngle(
                flight.HeadingDegrees,
                desiredHeading,
                aircraftType.TurnRateDegreesPerSecond * (float)seconds);
            var radians = heading * Mathf.Deg2Rad;
            var feetPerSecond = speedKnots * AirspaceGeometry.FeetPerNauticalMile / 3600f;
            var horizontalStep = feetPerSecond * (float)seconds;
            var position = flight.PositionFeet;
            var horizontalRemaining = Vector2.Distance(
                new Vector2(position.x, position.z),
                new Vector2(target.x, target.z));
            horizontalStep = Math.Min(horizontalStep, horizontalRemaining);
            position += new Vector3(
                Mathf.Sin(radians) * horizontalStep,
                0f,
                Mathf.Cos(radians) * horizontalStep);

            var verticalRate = (target.y >= position.y
                                   ? aircraftType.ClimbRateFeetPerMinute
                                   : aircraftType.DescentRateFeetPerMinute) / 60f;
            position = new Vector3(
                position.x,
                Mathf.MoveTowards(
                    position.y,
                    target.y,
                    Math.Max(1f, verticalRate) * (float)seconds),
                position.z);
            flight.UpdateKinematics(position, heading);
        }

        private static bool HasReached(Vector3 current, Vector3 target)
        {
            return Vector3.Distance(current, target) <= WaypointCaptureFeet;
        }

        private static float HeadingTo(Vector3 from, Vector3 to)
        {
            return Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
        }

        private static void LoseGroundedFlight(
            AirFlight flight,
            Squadron squadron,
            DateTime occurredAt)
        {
            foreach (var aircraft in squadron.Aircraft)
            {
                if (aircraft.AssignedFlightId != flight.FlightId)
                    continue;
                aircraft.Status = CampaignAircraftStatus.Lost;
                aircraft.AssignedFlightId = Guid.Empty;
                aircraft.ClearLoadout();
            }
            flight.Fail(occurredAt, "Launch airport was overrun before takeoff.");
        }

        private void LoseAirborneFlight(
            AirFlight flight,
            DateTime occurredAt,
            string reason)
        {
            if (gameManager.squadronSystem.TryGetSquadron(flight.SquadronId, out var squadron))
            {
                foreach (var aircraft in squadron.Aircraft)
                {
                    if (aircraft.AssignedFlightId != flight.FlightId)
                        continue;
                    aircraft.Status = CampaignAircraftStatus.Lost;
                    aircraft.AssignedFlightId = Guid.Empty;
                    aircraft.ClearLoadout();
                }
            }
            flight.Fail(occurredAt, reason);
        }

        private sealed class FlightTickState
        {
            public DateTime Cursor;
            public double RemainingSeconds;

            public FlightTickState(DateTime cursor, double remainingSeconds)
            {
                Cursor = cursor;
                RemainingSeconds = remainingSeconds;
            }

            public void Advance(double seconds)
            {
                var consumed = Math.Min(Math.Max(0d, seconds), RemainingSeconds);
                Cursor = Cursor.AddSeconds(consumed);
                RemainingSeconds -= consumed;
            }
        }
    }
}
