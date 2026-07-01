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

        public AirExecutionSystem(
            GameManager gameManager,
            AirTaskingSystem airTaskingSystem,
            ModuleDefinition module)
        {
            this.gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            this.airTaskingSystem = airTaskingSystem
                                    ?? throw new ArgumentNullException(nameof(airTaskingSystem));
            aircraftTypes = (module?.AircraftTypeDefinitions
                             ?? new List<AircraftTypeDefinition>())
                .Where(definition => definition != null)
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
        }

        public void GameTurn(DateTime previousTime, DateTime currentTime)
        {
            ResolveAirbaseOverruns();
            var states = new Dictionary<Guid, FlightTickState>();
            foreach (var package in airTaskingSystem.GetPackages()
                         .OrderBy(candidate => candidate.PackageId))
            {
                foreach (var flight in (package.Flights ?? new List<AirFlight>())
                             .Where(candidate => candidate != null)
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
                             .Where(candidate => candidate.HasRendezvous)
                             .OrderBy(candidate => candidate.PackageId))
                {
                    var required = (package.Flights ?? new List<AirFlight>())
                        .Where(flight => flight != null && flight.IsRequired)
                        .ToList();
                    if (required.Count == 0
                        || required.Any(flight => !flight.IsWaitingAtRendezvous)
                        || required.Any(flight => !states.ContainsKey(flight.FlightId)))
                        continue;

                    foreach (var flight in required)
                    {
                        flight.IsWaitingAtRendezvous = false;
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
                FailFlight(flight, currentTime, "Flight context became unavailable.");
                return null;
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
                var takeoff = flight.Route?.FirstOrDefault();
                if (takeoff == null || takeoff.Action != AirWaypointAction.Takeoff)
                {
                    FailFlight(flight, takeoffTime, "Flight route has no takeoff waypoint.");
                    return null;
                }

                flight.PositionFeet = takeoff.PositionFeet;
                flight.HasPosition = true;
                flight.CurrentWaypointIndex = 1;
                flight.LifecycleState = AirTaskingLifecycleState.Active;
                flight.ExecutionPhase = FlightExecutionPhase.Outbound;
                flight.HeadingDegrees = GetInitialHeading(flight);
                RecordEvent(flight, takeoff, takeoffTime, "Flight took off.");
                cursor = takeoffTime;
            }

            if (!flight.IsAirborne)
                return null;

            if (flight.LifecycleState == AirTaskingLifecycleState.Aborted)
                DirectToReturn(flight, cursor);
            return new FlightTickState(
                cursor,
                Math.Max(0d, (currentTime - cursor).TotalSeconds));
        }

        private void AdvanceFlight(
            AirPackage package,
            AirFlight flight,
            FlightTickState state)
        {
            if (!TryGetFlightContext(flight, out _, out var aircraftType))
            {
                FailFlight(flight, state.Cursor, "Aircraft performance became unavailable.");
                return;
            }

            while (state.RemainingSeconds > 0.0001d
                   && flight.IsAirborne
                   && !flight.IsWaitingAtRendezvous)
            {
                if (flight.Route == null
                    || flight.CurrentWaypointIndex < 0
                    || flight.CurrentWaypointIndex >= flight.Route.Count)
                {
                    FailFlight(flight, state.Cursor, "Flight exhausted its route before landing.");
                    return;
                }

                var waypoint = flight.Route[flight.CurrentWaypointIndex];
                if (waypoint == null)
                {
                    FailFlight(flight, state.Cursor, "Flight encountered an invalid waypoint.");
                    return;
                }

                var speedKnots = GetGuidanceSpeedKnots(package, flight, aircraftType);
                if (HasReached(flight.PositionFeet, waypoint.PositionFeet))
                {
                    flight.PositionFeet = waypoint.PositionFeet;
                    HandleWaypoint(package, flight, waypoint, state.Cursor);
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
                    flight.PositionFeet = waypoint.PositionFeet;
                    state.Advance(secondsToReach);
                    HandleWaypoint(package, flight, waypoint, state.Cursor);
                    continue;
                }

                IntegrateMotion(flight, waypoint.PositionFeet, aircraftType, speedKnots, step);
                state.Advance(step);
            }
        }

        private void HandleWaypoint(
            AirPackage package,
            AirFlight flight,
            AirWaypoint waypoint,
            DateTime occurredAt)
        {
            switch (waypoint.Action)
            {
                case AirWaypointAction.Rendezvous:
                    RecordEvent(flight, waypoint, occurredAt, "Flight reached package rendezvous.");
                    flight.CurrentWaypointIndex++;
                    flight.IsWaitingAtRendezvous = true;
                    return;

                case AirWaypointAction.StationEntry:
                    if (flight.ExecutionPhase != FlightExecutionPhase.Executing)
                    {
                        flight.ExecutionPhase = FlightExecutionPhase.Executing;
                        RecordEvent(flight, waypoint, occurredAt, "Flight entered station.");
                    }
                    flight.CurrentWaypointIndex++;
                    return;

                case AirWaypointAction.StationEndpoint:
                    if (waypoint.HasRepeat && occurredAt < waypoint.RepeatUntil)
                    {
                        var repeatIndex = flight.Route.FindIndex(
                            candidate => candidate != null
                                         && candidate.WaypointId == waypoint.RepeatFromWaypointId);
                        if (repeatIndex < 0)
                        {
                            FailFlight(flight, occurredAt, "Station loop target is missing.");
                            return;
                        }

                        flight.CurrentWaypointIndex = repeatIndex;
                        return;
                    }

                    flight.MissionAchieved = true;
                    RecordEvent(flight, waypoint, occurredAt, "Flight exited station.");
                    flight.CurrentWaypointIndex++;
                    return;

                case AirWaypointAction.MissionAction:
                    flight.ExecutionPhase = FlightExecutionPhase.Executing;
                    flight.MissionAchieved = true;
                    RecordEvent(flight, waypoint, occurredAt, "Flight completed its mission action.");
                    flight.CurrentWaypointIndex++;
                    return;

                case AirWaypointAction.ReturnToBase:
                    flight.ExecutionPhase = FlightExecutionPhase.Returning;
                    RecordEvent(flight, waypoint, occurredAt, "Flight began recovery.");
                    flight.CurrentWaypointIndex++;
                    if (!EnsureRecoveryRoute(package, flight, occurredAt))
                        LoseAirborneFlight(flight, occurredAt, "No friendly recovery airport remains.");
                    return;

                case AirWaypointAction.Approach:
                    flight.ExecutionPhase = FlightExecutionPhase.Landing;
                    RecordEvent(flight, waypoint, occurredAt, "Flight reached approach.");
                    flight.CurrentWaypointIndex++;
                    return;

                case AirWaypointAction.Land:
                    RecordEvent(flight, waypoint, occurredAt, "Flight landed.");
                    CompleteLanding(flight, occurredAt);
                    return;

                case AirWaypointAction.Transit:
                    flight.CurrentWaypointIndex++;
                    return;

                case AirWaypointAction.Takeoff:
                    flight.CurrentWaypointIndex++;
                    return;

                default:
                    flight.CurrentWaypointIndex++;
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
                return false;

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
                            gameManager.SimulationSettings?.TileDistanceKM
                            ?? SimulationSettings.DefaultTileDistanceKM)
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
            flight.RecoveryAirportBuildingId = recoveryAirportId;
            ReplaceRecoveryTail(
                flight,
                aircraftType,
                AirspaceGeometry.TileCenterFeet(
                    recovery.TileId,
                    gameManager.SimulationSettings?.TileDistanceKM
                    ?? SimulationSettings.DefaultTileDistanceKM),
                currentTime);
            return true;
        }

        private static void ReplaceRecoveryTail(
            AirFlight flight,
            AircraftTypeDefinition aircraftType,
            Vector3 basePosition,
            DateTime currentTime)
        {
            flight.Route.RemoveRange(
                flight.CurrentWaypointIndex,
                flight.Route.Count - flight.CurrentWaypointIndex);
            var from = flight.PositionFeet;
            var horizontal = new Vector3(from.x - basePosition.x, 0f, from.z - basePosition.z);
            var distance = horizontal.magnitude;
            var descentMinutes = from.y / Math.Max(1f, aircraftType.DescentRateFeetPerMinute);
            var descentDistance = aircraftType.CruiseSpeedKnots
                                  * AirspaceGeometry.FeetPerNauticalMile
                                  * descentMinutes / 60f;
            var approach = basePosition;
            if (distance > 0.01f)
                approach += horizontal.normalized * Math.Min(distance, descentDistance);
            approach.y = from.y;
            var approachTime = currentTime + TimeSpan.FromSeconds(
                AirspaceGeometry.TravelSeconds(
                    from,
                    approach,
                    aircraftType.CruiseSpeedKnots,
                    aircraftType.ClimbRateFeetPerMinute,
                    aircraftType.DescentRateFeetPerMinute));
            var landingTime = approachTime + TimeSpan.FromSeconds(
                AirspaceGeometry.TravelSeconds(
                    approach,
                    basePosition,
                    aircraftType.CruiseSpeedKnots,
                    aircraftType.ClimbRateFeetPerMinute,
                    aircraftType.DescentRateFeetPerMinute));
            flight.Route.Add(new AirWaypoint
            {
                PositionFeet = approach,
                Action = AirWaypointAction.Approach,
                PlannedArrivalTime = approachTime
            });
            flight.Route.Add(new AirWaypoint
            {
                PositionFeet = basePosition,
                Action = AirWaypointAction.Land,
                PlannedArrivalTime = landingTime
            });
        }

        private void CompleteLanding(AirFlight flight, DateTime occurredAt)
        {
            if (!TryGetFlightContext(flight, out var squadron, out _))
            {
                FailFlight(flight, occurredAt, "Squadron disappeared during recovery.");
                return;
            }

            foreach (var aircraft in squadron.Aircraft ?? new List<CampaignAircraft>())
            {
                if (aircraft == null
                    || aircraft.AssignedFlightId != flight.FlightId
                    || aircraft.Status == CampaignAircraftStatus.Lost)
                    continue;
                aircraft.ClearLoadout();
                aircraft.ReleaseFromFlight(flight.FlightId);
            }

            squadron.AirportBuildingId = flight.RecoveryAirportBuildingId;
            gameManager.squadronSystem.RebuildIndex();
            flight.HasPosition = false;
            flight.ExecutionPhase = FlightExecutionPhase.Ended;
            if (flight.LifecycleState != AirTaskingLifecycleState.Aborted)
            {
                flight.LifecycleState = flight.MissionAchieved
                    ? AirTaskingLifecycleState.Completed
                    : AirTaskingLifecycleState.Failed;
            }
        }

        private void ResolvePackageOutcomes(DateTime currentTime)
        {
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var commander = airTaskingSystem.GetCommander(alliance);
                if (commander == null)
                    continue;
                foreach (var package in (commander.Packages ?? Array.Empty<AirPackage>())
                             .Where(candidate => candidate != null && candidate.HasPhysicallyEnded))
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

        private void ResolveAirbaseOverruns()
        {
            var flights = airTaskingSystem.GetPackages()
                .SelectMany(package => package.Flights ?? new List<AirFlight>())
                .Where(flight => flight != null)
                .ToList();
            var activeFlightIds = flights
                .Where(flight => flight.IsAirborne)
                .Select(flight => flight.FlightId)
                .ToHashSet();
            foreach (var squadron in gameManager.squadronSystem.Squadrons ?? new List<Squadron>())
            {
                if (squadron == null)
                    continue;
                var alliance = gameManager.GetCountryAlliance(squadron.CountryId);
                if (IsAirportFriendly(squadron.AirportBuildingId, alliance))
                    continue;

                foreach (var flight in flights.Where(flight =>
                             flight.SquadronId == squadron.SquadronId
                             && flight.ExecutionPhase == FlightExecutionPhase.AwaitingTakeoff))
                {
                    flight.LifecycleState = AirTaskingLifecycleState.Cancelled;
                    flight.ExecutionPhase = FlightExecutionPhase.Ended;
                }

                foreach (var aircraft in squadron.Aircraft ?? new List<CampaignAircraft>())
                {
                    if (aircraft == null || activeFlightIds.Contains(aircraft.AssignedFlightId))
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
            var landTile = (gameManager.Tiles ?? new List<TileData>())
                .OfType<LandTileData>()
                .FirstOrDefault(tile => tile.TileId == building.TileId);
            return landTile != null && landTile.Controller == alliance;
        }

        private IEnumerable<Airport> GetFriendlyAirports(Alliance alliance)
        {
            return gameManager.buildingSystem.GetBuildings<Airport>()
                .Where(airport =>
                {
                    var landTile = (gameManager.Tiles ?? new List<TileData>())
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
                   && squadron != null
                   && aircraftTypes.TryGetValue(
                       squadron.AircraftTypeDefinitionId,
                       out aircraftType);
        }

        private float GetGuidanceSpeedKnots(
            AirPackage package,
            AirFlight flight,
            AircraftTypeDefinition ownType)
        {
            if (!package.HasRendezvous
                || flight.ExecutionPhase == FlightExecutionPhase.Returning
                || flight.ExecutionPhase == FlightExecutionPhase.Landing
                || !flight.ExecutionEvents.Any(entry =>
                    entry.Action == AirWaypointAction.Rendezvous))
                return Math.Max(1f, ownType.CruiseSpeedKnots);

            return (package.Flights ?? new List<AirFlight>())
                .Where(candidate => candidate != null && candidate.IsRequired)
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
            flight.HeadingDegrees = Mathf.MoveTowardsAngle(
                flight.HeadingDegrees,
                desiredHeading,
                aircraftType.TurnRateDegreesPerSecond * (float)seconds);
            var radians = flight.HeadingDegrees * Mathf.Deg2Rad;
            var feetPerSecond = speedKnots * AirspaceGeometry.FeetPerNauticalMile / 3600f;
            var horizontalStep = feetPerSecond * (float)seconds;
            var horizontalRemaining = Vector2.Distance(
                new Vector2(flight.PositionFeet.x, flight.PositionFeet.z),
                new Vector2(target.x, target.z));
            horizontalStep = Math.Min(horizontalStep, horizontalRemaining);
            flight.PositionFeet += new Vector3(
                Mathf.Sin(radians) * horizontalStep,
                0f,
                Mathf.Cos(radians) * horizontalStep);

            var verticalRate = (target.y >= flight.PositionFeet.y
                                   ? aircraftType.ClimbRateFeetPerMinute
                                   : aircraftType.DescentRateFeetPerMinute) / 60f;
            flight.PositionFeet = new Vector3(
                flight.PositionFeet.x,
                Mathf.MoveTowards(
                    flight.PositionFeet.y,
                    target.y,
                    Math.Max(1f, verticalRate) * (float)seconds),
                flight.PositionFeet.z);
        }

        private static bool HasReached(Vector3 current, Vector3 target)
        {
            return Vector3.Distance(current, target) <= WaypointCaptureFeet;
        }

        private static float GetInitialHeading(AirFlight flight)
        {
            if (flight.Route == null
                || flight.CurrentWaypointIndex >= flight.Route.Count)
                return 0f;
            return HeadingTo(
                flight.PositionFeet,
                flight.Route[flight.CurrentWaypointIndex].PositionFeet);
        }

        private static float HeadingTo(Vector3 from, Vector3 to)
        {
            return Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
        }

        private static void RecordEvent(
            AirFlight flight,
            AirWaypoint waypoint,
            DateTime occurredAt,
            string detail)
        {
            flight.ExecutionEvents ??= new List<FlightExecutionEvent>();
            flight.ExecutionEvents.Add(new FlightExecutionEvent
            {
                WaypointId = waypoint.WaypointId,
                Action = waypoint.Action,
                OccurredAt = occurredAt,
                Detail = detail ?? string.Empty
            });
        }

        private static void DirectToReturn(AirFlight flight, DateTime currentTime)
        {
            if (!flight.IsAirborne)
                return;
            var returnIndex = flight.Route?.FindIndex(
                flight.CurrentWaypointIndex,
                waypoint => waypoint != null
                            && waypoint.Action == AirWaypointAction.ReturnToBase) ?? -1;
            if (returnIndex < 0)
            {
                FailFlight(flight, currentTime, "Aborted flight has no return waypoint.");
                return;
            }

            flight.CurrentWaypointIndex = returnIndex;
            flight.ExecutionPhase = FlightExecutionPhase.Returning;
            flight.IsWaitingAtRendezvous = false;
        }

        private static void LoseGroundedFlight(
            AirFlight flight,
            Squadron squadron,
            DateTime occurredAt)
        {
            foreach (var aircraft in squadron.Aircraft ?? new List<CampaignAircraft>())
            {
                if (aircraft?.AssignedFlightId != flight.FlightId)
                    continue;
                aircraft.Status = CampaignAircraftStatus.Lost;
                aircraft.AssignedFlightId = Guid.Empty;
                aircraft.ClearLoadout();
            }
            FailFlight(flight, occurredAt, "Launch airport was overrun before takeoff.");
        }

        private void LoseAirborneFlight(
            AirFlight flight,
            DateTime occurredAt,
            string reason)
        {
            if (gameManager.squadronSystem.TryGetSquadron(flight.SquadronId, out var squadron))
            {
                foreach (var aircraft in squadron.Aircraft ?? new List<CampaignAircraft>())
                {
                    if (aircraft?.AssignedFlightId != flight.FlightId)
                        continue;
                    aircraft.Status = CampaignAircraftStatus.Lost;
                    aircraft.AssignedFlightId = Guid.Empty;
                    aircraft.ClearLoadout();
                }
            }
            FailFlight(flight, occurredAt, reason);
        }

        private static void FailFlight(
            AirFlight flight,
            DateTime occurredAt,
            string reason)
        {
            flight.LifecycleState = AirTaskingLifecycleState.Failed;
            flight.ExecutionPhase = FlightExecutionPhase.Ended;
            flight.HasPosition = false;
            flight.IsWaitingAtRendezvous = false;
            flight.ExecutionEvents ??= new List<FlightExecutionEvent>();
            flight.ExecutionEvents.Add(new FlightExecutionEvent
            {
                OccurredAt = occurredAt,
                Action = AirWaypointAction.ReturnToBase,
                Detail = reason ?? string.Empty
            });
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
