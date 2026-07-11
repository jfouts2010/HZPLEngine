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
        private const double TacticalDecisionStepSeconds = 5d;
        private const float WaypointCaptureFeet = 100f;
        private const float MaximumDynamicWaypointCaptureFeet = 25000f;

        private readonly GameManager gameManager;
        private readonly AirTaskingSystem airTaskingSystem;
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes;
        private readonly AirLoadoutPlanner loadoutPlanner;
        private OrdnanceEmploymentSystem ordnanceEmploymentSystem;

        public AirExecutionSystem(
            GameManager gameManager,
            AirTaskingSystem airTaskingSystem,
            ModuleDefinition module)
        {
            this.gameManager = gameManager;
            this.airTaskingSystem = airTaskingSystem;
            aircraftTypes = module.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
            loadoutPlanner = new AirLoadoutPlanner(
                module,
                alliance =>
                    gameManager.OrdnanceAllowances.TryGetValue(alliance, out var allowed)
                        ? allowed
                        : Array.Empty<Guid>());
        }

        public void AttachOrdnanceEmploymentSystem(
            OrdnanceEmploymentSystem employmentSystem)
        {
            ordnanceEmploymentSystem = employmentSystem
                ?? throw new ArgumentNullException(nameof(employmentSystem));
        }

        public void GameTurn(DateTime previousTime, DateTime currentTime)
        {
            ResolveAirbaseOverruns(currentTime);
            if (ordnanceEmploymentSystem == null)
                throw new InvalidOperationException(
                    "Air execution requires an attached ordnance employment system.");

            var cursor = previousTime;
            while (cursor <= currentTime)
            {
                ordnanceEmploymentSystem.UpdateAirToAirGuidance(cursor);
                ordnanceEmploymentSystem.AdvanceScheduledEvents(cursor);
                PrepareFlightsAt(cursor);
                ReleaseReadyRendezvousFlights();

                if (cursor >= currentTime)
                    break;

                var frame = BuildAirCombatFrame(cursor);
                var commands = frame.Flights.Values
                    .Where(view => view.Flight.IsAirborne
                                   && !view.Flight.IsWaitingAtRendezvous)
                    .OrderBy(view => view.Flight.FlightId)
                    .Select(view => AirCombatRules.Decide(
                        view,
                        frame,
                        ordnanceTypes,
                        GetDoctrine(view.Alliance)))
                    .ToList();

                foreach (var command in commands)
                {
                    if (!frame.Flights.TryGetValue(command.FlightId, out var view))
                        continue;
                    view.Flight.TacticalState.Apply(
                        command.Intent,
                        command.Maneuver,
                        cursor,
                        command.MinimumManeuverEndAt,
                        command.TargetFlightId,
                        command.SupportedPendingEffectId,
                        command.PreferredSide,
                        command.AimPointFeet,
                        command.HasAimPoint,
                        command.Reason);
                    if (command.ExhaustProactiveEngagement)
                        view.Flight.TacticalState.ProactiveEngagementExhausted = true;
                }

                foreach (var proposal in commands
                             .Where(command => command.Employment != null)
                             .Select(command => command.Employment)
                             .OrderBy(proposal => proposal.SourceFlightId)
                             .ThenBy(proposal => proposal.TargetFlightId))
                {
                    ordnanceEmploymentSystem.TryStartAirToAirPass(proposal, cursor);
                }
                ordnanceEmploymentSystem.AdvanceScheduledEvents(cursor);

                var next = NextTacticalBoundary(cursor, currentTime);
                var elapsedSeconds = Math.Max(0d, (next - cursor).TotalSeconds);
                foreach (var command in commands.OrderBy(command => command.FlightId))
                    AdvanceFlightCommand(command, cursor, elapsedSeconds);
                cursor = next;
            }

            ResolvePackageOutcomes(currentTime);
        }

        private void PrepareFlightsAt(DateTime currentTime)
        {
            foreach (var package in airTaskingSystem.GetPackages()
                         .OrderBy(candidate => candidate.PackageId))
            {
                foreach (var flight in package.Flights
                             .OrderBy(candidate => candidate.FlightId))
                {
                    if (flight.HasPhysicallyEnded
                        || !TryGetFlightContext(flight, out var squadron, out var aircraftType))
                        continue;

                    if (flight.ExecutionPhase == FlightExecutionPhase.AwaitingTakeoff)
                    {
                        if (flight.LifecycleState != AirTaskingLifecycleState.Committed
                            || flight.PlannedTakeoffTime > currentTime)
                            continue;
                        if (!IsAirportFriendly(flight.LaunchAirportBuildingId, package.Alliance))
                        {
                            LoseGroundedFlight(flight, squadron, currentTime);
                            continue;
                        }
                        if (!flight.TryTakeOff(flight.PlannedTakeoffTime))
                        {
                            throw new InvalidOperationException(
                                $"Flight {flight.FlightId} could not transition to takeoff.");
                        }
                    }

                    if (!flight.IsAirborne)
                        continue;

                    flight.ContinueAbortRecovery(currentTime);
                    AbortIfMissionUsefulOrdnanceExhausted(
                        package,
                        flight,
                        squadron,
                        aircraftType,
                        currentTime);
                    var doctrine = GetDoctrine(package.Alliance);
                    if (flight.LifecycleState == AirTaskingLifecycleState.Active
                        && flight.ExecutionPhase != FlightExecutionPhase.Returning
                        && flight.ExecutionPhase != FlightExecutionPhase.Landing
                        && flight.TacticalState.FuelFraction <= doctrine.JokerFuelFraction)
                    {
                        AbortToImmediateRecovery(
                            package,
                            flight,
                            squadron,
                            aircraftType,
                            currentTime,
                            flight.TacticalState.FuelFraction <= doctrine.BingoFuelFraction
                                ? "Flight reached bingo fuel."
                                : "Flight reached joker fuel.");
                    }
                }
            }
        }

        private void ReleaseReadyRendezvousFlights()
        {
            foreach (var package in airTaskingSystem.GetPackages()
                         .Where(candidate => candidate.RendezvousWaypoint != null)
                         .OrderBy(candidate => candidate.PackageId))
            {
                var required = package.Flights
                    .Where(flight => flight.IsRequired)
                    .ToList();
                if (required.Count == 0
                    || required.Any(flight => !flight.IsWaitingAtRendezvous))
                    continue;
                foreach (var flight in required)
                    flight.ReleaseRendezvous();
            }
        }

        private AirCombatFrame BuildAirCombatFrame(DateTime currentTime)
        {
            var flights = new Dictionary<Guid, AirCombatFlightView>();
            foreach (var package in airTaskingSystem.GetPackages()
                         .OrderBy(candidate => candidate.PackageId))
            {
                foreach (var flight in package.Flights.OrderBy(candidate => candidate.FlightId))
                {
                    if (!flight.IsAirborne
                        || !TryGetFlightContext(flight, out var squadron, out var aircraftType))
                        continue;
                    var liveAircraft = squadron.Aircraft
                        .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                           && aircraft.Status != CampaignAircraftStatus.Lost
                                           && aircraft.Status != CampaignAircraftStatus.Damaged)
                        .OrderBy(aircraft => aircraft.AircraftId)
                        .ToList();
                    flights[flight.FlightId] = new AirCombatFlightView
                    {
                        Alliance = package.Alliance,
                        Package = package,
                        Flight = flight,
                        Squadron = squadron,
                        AircraftType = aircraftType,
                        LiveAircraft = liveAircraft
                    };
                }
            }

            return new AirCombatFrame
            {
                Time = currentTime,
                TileDistanceKm = gameManager.SimulationSettings.TileDistanceKM,
                Flights = flights,
                ActivePasses = ordnanceEmploymentSystem.ActivePasses.ToList(),
                PendingEffects = ordnanceEmploymentSystem.PendingEffects.ToList()
            };
        }

        private DateTime NextTacticalBoundary(DateTime cursor, DateTime tickEnd)
        {
            var next = cursor.AddSeconds(TacticalDecisionStepSeconds);
            if (next > tickEnd)
                next = tickEnd;

            var takeoff = airTaskingSystem.GetPackages()
                .SelectMany(package => package.Flights)
                .Where(flight => flight.ExecutionPhase == FlightExecutionPhase.AwaitingTakeoff
                                 && flight.LifecycleState == AirTaskingLifecycleState.Committed
                                 && flight.PlannedTakeoffTime > cursor
                                 && flight.PlannedTakeoffTime < next)
                .Select(flight => (DateTime?)flight.PlannedTakeoffTime)
                .DefaultIfEmpty()
                .Min();
            if (takeoff.HasValue)
                next = takeoff.Value;

            var ordnanceEvent = ordnanceEmploymentSystem.GetNextScheduledEvent(cursor, next);
            if (ordnanceEvent.HasValue)
                next = ordnanceEvent.Value;
            return next;
        }

        private void AdvanceFlightCommand(
            AirCombatCommand command,
            DateTime intervalStart,
            double elapsedSeconds)
        {
            var package = airTaskingSystem.GetPackages()
                .FirstOrDefault(candidate => candidate.Flights.Any(
                    flight => flight.FlightId == command.FlightId));
            var flight = package?.Flights.FirstOrDefault(candidate =>
                candidate.FlightId == command.FlightId);
            if (flight == null
                || !flight.IsAirborne
                || flight.IsWaitingAtRendezvous
                || !TryGetFlightContext(flight, out _, out var aircraftType))
                return;

            var remaining = elapsedSeconds;
            var localTime = intervalStart;
            while (remaining > 0.0001d && flight.IsAirborne && !flight.IsWaitingAtRendezvous)
            {
                var followingRoute = command.Maneuver == AirCombatManeuver.FollowRoute;
                var target = followingRoute
                    ? flight.CurrentWaypoint?.PositionFeet ?? flight.PositionFeet
                    : command.AimPointFeet;
                var speedKnots = followingRoute
                    ? GetGuidanceSpeedKnots(package, flight, aircraftType)
                    : Math.Max(1f, command.DesiredSpeedKnots);

                if (followingRoute
                    && HasReached(flight.PositionFeet, target, aircraftType, speedKnots))
                {
                    flight.UpdateKinematics(target, flight.HeadingDegrees, speedKnots);
                    HandleWaypoint(package, flight, localTime);
                    continue;
                }

                var step = Math.Min(MaximumIntegrationStepSeconds, remaining);
                var previous = flight.PositionFeet;
                IntegrateMotion(flight, target, aircraftType, speedKnots, step);
                remaining -= step;
                localTime = localTime.AddSeconds(step);
                BurnFuel(flight, aircraftType, command.Intent, step);

                if (!followingRoute
                    || !ShouldCaptureTarget(
                        previous,
                        flight.PositionFeet,
                        target,
                        aircraftType,
                        speedKnots))
                    continue;

                flight.UpdateKinematics(target, flight.HeadingDegrees, speedKnots);
                HandleWaypoint(package, flight, localTime);
            }
        }

        private static void BurnFuel(
            AirFlight flight,
            AircraftTypeDefinition aircraftType,
            AirCombatIntent intent,
            double seconds)
        {
            if (aircraftType.EnduranceHours <= 0f || seconds <= 0d)
                return;
            var multiplier = intent switch
            {
                AirCombatIntent.EngageTarget => 1.8f,
                AirCombatIntent.Defend => 2.5f,
                AirCombatIntent.Disengage => 1.4f,
                AirCombatIntent.Recover => 0.9f,
                _ => 1f
            };
            var consumed = (float)(seconds / (aircraftType.EnduranceHours * 3600d))
                           * multiplier;
            flight.TacticalState.FuelFraction = Mathf.Clamp01(
                flight.TacticalState.FuelFraction - consumed);
        }

        private AllianceAirDoctrine GetDoctrine(Alliance alliance)
        {
            if (gameManager.CampaignTemplate?.AirDoctrineByAlliance != null
                && gameManager.CampaignTemplate.AirDoctrineByAlliance.TryGetValue(
                    alliance,
                    out var doctrine))
                return doctrine;
            return AllianceAirDoctrine.CreateDefault();
        }

        private void AbortIfMissionUsefulOrdnanceExhausted(
            AirPackage package,
            AirFlight flight,
            Squadron squadron,
            AircraftTypeDefinition aircraftType,
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

            AbortToImmediateRecovery(
                package,
                flight,
                squadron,
                aircraftType,
                occurredAt,
                "Flight exhausted mission-useful air-to-air ordnance.");
        }

        private void AbortToImmediateRecovery(
            AirPackage package,
            AirFlight flight,
            Squadron squadron,
            AircraftTypeDefinition aircraftType,
            DateTime occurredAt,
            string reason)
        {
            if (!TryBuildRecoveryRoute(
                    package,
                    flight,
                    squadron,
                    aircraftType,
                    occurredAt,
                    out var recoveryRoute))
            {
                LoseAirborneFlight(flight, occurredAt, "No friendly recovery airport remains.");
                return;
            }

            flight.AbortAndReplaceRecoveryRoute(occurredAt, reason, recoveryRoute);
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

            if (!TryBuildRecoveryRoute(
                    package,
                    flight,
                    squadron,
                    aircraftType,
                    currentTime,
                    out var recoveryTail))
                return false;
            flight.ReplaceRecoveryRoute(recoveryTail);
            return true;
        }

        private bool TryBuildRecoveryRoute(
            AirPackage package,
            AirFlight flight,
            Squadron squadron,
            AircraftTypeDefinition aircraftType,
            DateTime currentTime,
            out IReadOnlyList<AirWaypoint> recoveryRoute)
        {
            recoveryRoute = null;
            if (!TrySelectRecoveryAirport(
                    package,
                    flight,
                    squadron,
                    out var recoveryAirportId,
                    out var recoveryPosition))
                return false;

            recoveryRoute = AirRecoveryRouteBuilder.Build(
                flight.PositionFeet,
                aircraftType,
                recoveryAirportId,
                recoveryPosition,
                currentTime);
            return true;
        }

        private bool TrySelectRecoveryAirport(
            AirPackage package,
            AirFlight flight,
            Squadron squadron,
            out Guid recoveryAirportId,
            out Vector3 recoveryPosition)
        {
            recoveryAirportId = Guid.Empty;
            recoveryPosition = default;
            if (IsAirportFriendly(flight.RecoveryAirportBuildingId, package.Alliance)
                && TryGetAirportPosition(flight.RecoveryAirportBuildingId, out recoveryPosition))
            {
                recoveryAirportId = flight.RecoveryAirportBuildingId;
                return true;
            }

            if (IsAirportFriendly(squadron.AirportBuildingId, package.Alliance)
                && TryGetAirportPosition(squadron.AirportBuildingId, out recoveryPosition))
            {
                recoveryAirportId = squadron.AirportBuildingId;
                return true;
            }

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
            recoveryPosition = nearest.Position;
            return true;
        }

        private bool TryGetAirportPosition(Guid airportId, out Vector3 position)
        {
            if (gameManager.buildingSystem.TryGetBuilding(airportId, out var building)
                && building is Airport)
            {
                position = AirspaceGeometry.TileCenterFeet(
                    building.TileId,
                    gameManager.SimulationSettings.TileDistanceKM);
                return true;
            }

            position = default;
            return false;
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
            Vector3 target,
            AircraftTypeDefinition aircraftType,
            float speedKnots,
            double maximumStep)
        {
            var from = flight.PositionFeet;
            var to = target;
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
            flight.UpdateKinematics(position, heading, speedKnots);
        }

        private static bool HasReached(Vector3 current, Vector3 target)
        {
            return Vector3.Distance(current, target) <= WaypointCaptureFeet;
        }

        private static bool HasReached(
            Vector3 current,
            Vector3 target,
            AircraftTypeDefinition aircraftType,
            float speedKnots)
        {
            return Vector3.Distance(current, target)
                   <= GetDynamicWaypointCaptureFeet(aircraftType, speedKnots);
        }

        private static bool ShouldCaptureTarget(
            Vector3 previous,
            Vector3 current,
            Vector3 target,
            AircraftTypeDefinition aircraftType,
            float speedKnots)
        {
            var captureFeet = GetDynamicWaypointCaptureFeet(aircraftType, speedKnots);
            if (Vector3.Distance(current, target) <= captureFeet)
                return true;

            var travel = current - previous;
            var travelMagnitudeSquared = travel.sqrMagnitude;
            if (travelMagnitudeSquared <= 0.01f)
                return false;

            var previousToTarget = target - previous;
            var projection = Vector3.Dot(previousToTarget, travel) / travelMagnitudeSquared;
            if (projection < 0f || projection > 1f)
                return false;

            var closest = previous + travel * projection;
            return Vector3.Distance(closest, target) <= captureFeet;
        }

        private static float GetDynamicWaypointCaptureFeet(
            AircraftTypeDefinition aircraftType,
            float speedKnots)
        {
            var feetPerSecond = Math.Max(1f, speedKnots)
                                * AirspaceGeometry.FeetPerNauticalMile / 3600f;
            var turnRateRadians = Math.Max(0.1f, aircraftType.TurnRateDegreesPerSecond)
                                  * Mathf.Deg2Rad;
            var turnRadiusFeet = feetPerSecond / turnRateRadians;
            return Mathf.Clamp(
                Math.Max(WaypointCaptureFeet, turnRadiusFeet),
                WaypointCaptureFeet,
                MaximumDynamicWaypointCaptureFeet);
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

    }
}
