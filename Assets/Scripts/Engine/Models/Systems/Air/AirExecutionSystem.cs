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
        private readonly IReadOnlyDictionary<Guid, AirDefenseComponentDefinition>
            airDefenseComponentDefinitions;
        private readonly AirLoadoutPlanner loadoutPlanner;
        private readonly WvrEngagementSystem wvrEngagementSystem;
        private readonly KnownSamThreatAssessment knownSamThreatAssessment;
        private readonly IAirRouteGeometryPlanner deadCorridorRoutePlanner;
        private readonly AirportOperationsService airportOperations;
        private readonly Dictionary<Alliance, IReadOnlyList<KnownSamThreatEnvelope>>
            knownSamThreatCache =
                new Dictionary<Alliance, IReadOnlyList<KnownSamThreatEnvelope>>();
        private OrdnanceEmploymentSystem ordnanceEmploymentSystem;

        public AirExecutionSystem(
            GameManager gameManager,
            AirTaskingSystem airTaskingSystem,
            ModuleDefinition module)
        {
            this.gameManager = gameManager;
            this.airTaskingSystem = airTaskingSystem;
            airportOperations = airTaskingSystem.AirportOperations;
            aircraftTypes = module.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
            airDefenseComponentDefinitions = module.SamComponentDefinitions
                .ToDictionary(definition => definition.SamComponentDefinitionId);
            knownSamThreatAssessment = new KnownSamThreatAssessment(
                module.SamComponentDefinitions,
                module.OrdnanceTypeDefinitions);
            deadCorridorRoutePlanner =
                new SeparatedIngressEgressRouteGeometryPlanner();
            loadoutPlanner = new AirLoadoutPlanner(
                module,
                alliance =>
                    gameManager.OrdnanceAllowances.TryGetValue(alliance, out var allowed)
                        ? allowed
                        : Array.Empty<Guid>());
            wvrEngagementSystem = new WvrEngagementSystem(
                ordnanceTypes,
                siteId => gameManager.airDefenseSiteSystem.TryGetSite(
                              siteId,
                              out var site)
                    ? gameManager.airDefenseSiteSystem.GetEffectiveAlliance(site)
                    : Alliance.Neutral,
                (alliance, targetFlightId) =>
                    gameManager.GetAllianceIADS(alliance)
                        ?.CurrentEngagementAssignments.Any(assignment =>
                            assignment.TargetFlightId == targetFlightId)
                    == true);
        }

        public void AttachOrdnanceEmploymentSystem(
            OrdnanceEmploymentSystem employmentSystem)
        {
            ordnanceEmploymentSystem = employmentSystem
                ?? throw new ArgumentNullException(nameof(employmentSystem));
        }

        public bool IsFlightInWvrEngagement(Guid flightId)
        {
            return wvrEngagementSystem.IsFlightEngaged(flightId);
        }

        public bool TryGetLatestWvrRound(
            Guid flightId,
            out WvrRoundDiagnostic diagnostic)
        {
            return wvrEngagementSystem.TryGetLatestRound(
                flightId,
                out diagnostic);
        }

        public void GameTurn(DateTime previousTime, DateTime currentTime)
        {
            knownSamThreatCache.Clear();
            ResolveAirbaseOverruns(currentTime);
            if (ordnanceEmploymentSystem == null)
                throw new InvalidOperationException(
                    "Air execution requires an attached ordnance employment system.");

            var cursor = previousTime;
            while (cursor <= currentTime)
            {
                ordnanceEmploymentSystem.UpdateOrdnanceGuidance(cursor);
                ordnanceEmploymentSystem.AdvanceScheduledEvents(cursor);
                ResolveDamageRecovery(cursor);
                PrepareFlightsAt(cursor);
                ordnanceEmploymentSystem.CancelAirToGroundPasses(
                    airTaskingSystem.GetPackages()
                        .SelectMany(package => package.Flights)
                        .Where(flight => flight.ExecutionPhase
                                             == FlightExecutionPhase.Returning
                                         || flight.ExecutionPhase
                                             == FlightExecutionPhase.Landing
                                         || flight.LifecycleState
                                             == AirTaskingLifecycleState.Aborted)
                        .Select(flight => flight.FlightId),
                    cursor,
                    "Ground-attack preparation was cancelled when the flight began recovery.");
                ReleaseReadyRendezvousFlights();

                if (cursor >= currentTime)
                    break;

                var frame = BuildAirCombatFrame(cursor);
                wvrEngagementSystem.AdvanceDueRounds(
                    frame,
                    GetDoctrine,
                    ordnanceEmploymentSystem,
                    cursor);
                ordnanceEmploymentSystem.AdvanceScheduledEvents(cursor);
                frame = BuildAirCombatFrame(cursor);
                wvrEngagementSystem.Reconcile(frame, cursor);
                ResolveDamageRecovery(cursor);
                ProcessDeadMissions(cursor);
                frame = BuildAirCombatFrame(cursor);

                var commands = frame.Flights.Values
                    .Where(view => view.Flight.IsAirborne
                                   && !view.Flight.IsWaitingAtRendezvous
                                   && !wvrEngagementSystem.IsFlightEngaged(
                                       view.Flight.FlightId))
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
                    if (command.RequestsSurfaceThreatRecovery)
                    {
                        if (TryAbortToImmediateRecovery(
                                view.Package,
                                view.Flight,
                                view.Squadron,
                                view.AircraftType,
                                cursor,
                                command.Reason))
                        {
                            ContinueRecoveryCommand(
                                command,
                                view.Flight,
                                view.AircraftType,
                                cursor);
                        }
                        else
                        {
                            ContinueSurfaceThreatEscape(
                                command,
                                view.Flight,
                                view.AircraftType,
                                cursor);
                        }
                    }
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

                ordnanceEmploymentSystem.CancelAirToAirPasses(
                    commands
                        .Where(command =>
                            command.RequestsAirToAirPassCancellation)
                        .Select(command => command.FlightId),
                    cursor,
                    "Air-to-air employment preparation was cancelled by current "
                    + "tactical authorization.");
                ordnanceEmploymentSystem.CancelAirToGroundPasses(
                    commands
                        .Where(command => command.RequestsSurfaceThreatRecovery)
                        .Select(command => command.FlightId),
                    cursor,
                    "Ground-attack preparation was cancelled for immediate surface-threat recovery.");
                var airToAirProposals = commands
                             .Where(command =>
                                 !command.RequestsSurfaceThreatRecovery
                                 && command.Employment != null)
                             .Select(command => command.Employment)
                             .OrderBy(proposal => proposal.SourceFlightId)
                             .ThenBy(proposal => proposal.TargetFlightId)
                             .ToList();
                ordnanceEmploymentSystem.CancelAirToGroundPasses(
                    airToAirProposals.Select(proposal => proposal.SourceFlightId),
                    cursor,
                    "Ground-attack preparation was interrupted to answer an immediate air threat.");
                foreach (var proposal in airToAirProposals)
                {
                    ordnanceEmploymentSystem.TryStartAirToAirPass(proposal, cursor);
                }
                ProcessDeadMissions(cursor);
                wvrEngagementSystem.ProcessRequests(
                    commands.Where(command =>
                        !command.RequestsSurfaceThreatRecovery),
                    frame,
                    cursor);
                ordnanceEmploymentSystem.CancelAirToAirPasses(
                    frame.Flights.Keys.Where(
                        wvrEngagementSystem.IsFlightEngaged),
                    cursor,
                    "Employment preparation aborted when the source entered WVR combat.");
                ordnanceEmploymentSystem.CancelAirToGroundPasses(
                    frame.Flights.Keys.Where(
                        wvrEngagementSystem.IsFlightEngaged),
                    cursor,
                    "Ground-attack preparation aborted when the source entered WVR combat.");
                wvrEngagementSystem.AdvanceDueRounds(
                    frame,
                    GetDoctrine,
                    ordnanceEmploymentSystem,
                    cursor);
                ordnanceEmploymentSystem.AdvanceScheduledEvents(cursor);
                frame = BuildAirCombatFrame(cursor);
                wvrEngagementSystem.Reconcile(frame, cursor);
                ResolveDamageRecovery(cursor);
                ApplyDeadPostLaunchManeuvers(commands, frame, cursor);

                var next = NextTacticalBoundary(cursor, currentTime);
                var elapsedSeconds = Math.Max(0d, (next - cursor).TotalSeconds);
                foreach (var command in commands
                             .Where(command =>
                                 !command.RequestsSurfaceThreatRecovery)
                             .OrderBy(command => command.FlightId))
                    AdvanceFlightCommand(command, cursor, elapsedSeconds);
                BurnWvrFuel(frame, commands, elapsedSeconds);
                cursor = next;
            }

            ResolvePackageOutcomes(currentTime);
        }

        private void PrepareFlightsAt(DateTime currentTime)
        {
            RefreshActiveSupportCapacities();
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
                        if (package.SupportingFlightIds.Count > 0
                            && package.Flights.All(candidate =>
                                candidate.ExecutionPhase
                                == FlightExecutionPhase.AwaitingTakeoff)
                            && !HasContinuousReservedTankerCoverage(package))
                        {
                            airTaskingSystem.CancelPackage(
                                package.Alliance,
                                package.PackageId,
                                currentTime,
                                "Reserved tanker coverage became unavailable before takeoff.");
                            continue;
                        }
                        if (!IsAirportControlledBy(
                                flight.LaunchAirportBuildingId,
                                package.Alliance))
                        {
                            LoseGroundedFlight(flight, squadron, currentTime);
                            continue;
                        }
                        if (!CanAirportConductAirOperations(
                                flight.LaunchAirportBuildingId,
                                package.Alliance))
                        {
                            airTaskingSystem.CancelPackage(
                                package.Alliance,
                                package.PackageId,
                                currentTime,
                                "Launch airport runway system closed before takeoff.");
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

                    if ((flight.ExecutionPhase
                         == FlightExecutionPhase.Returning
                         || flight.ExecutionPhase
                         == FlightExecutionPhase.Landing)
                        && !CanAirportConductAirOperations(
                            flight.RecoveryAirportBuildingId,
                            package.Alliance)
                        && !EnsureRecoveryRoute(
                            package,
                            flight,
                            currentTime))
                    {
                        LoseAirborneFlight(
                            flight,
                            currentTime,
                            "No operational friendly recovery airport remains.");
                        continue;
                    }

                    if (wvrEngagementSystem.IsFlightEngaged(flight.FlightId))
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
                        if (TryRefuelFromReservedTanker(
                                package,
                                flight,
                                aircraftType,
                                currentTime))
                            continue;

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
                    var wvrAircraft = squadron.Aircraft
                        .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                           && aircraft.Status
                                           != CampaignAircraftStatus.Lost)
                        .OrderBy(aircraft => aircraft.AircraftId)
                        .ToList();
                    flights[flight.FlightId] = new AirCombatFlightView
                    {
                        Alliance = package.Alliance,
                        Package = package,
                        Flight = flight,
                        Squadron = squadron,
                        AircraftType = aircraftType,
                        LiveAircraft = liveAircraft,
                        WvrAircraft = wvrAircraft,
                        PreviousTargetFlightId =
                            flight.TacticalState.TargetFlightId
                    };
                }
            }

            var frame = new AirCombatFrame
            {
                Time = currentTime,
                TileDistanceKm = gameManager.SimulationSettings.TileDistanceKM,
                Flights = flights,
                AirCommanders = new Dictionary<Alliance, AllianceAirTaskingCommander>
                {
                    { Alliance.Bluefor, airTaskingSystem.GetCommander(Alliance.Bluefor) },
                    { Alliance.Redfor, airTaskingSystem.GetCommander(Alliance.Redfor) }
                },
                CurrentTracksByAlliance = new Dictionary<Alliance, IReadOnlyDictionary<Guid, IADSTrack>>
                {
                    { Alliance.Bluefor, GetCurrentTracks(Alliance.Bluefor) },
                    { Alliance.Redfor, GetCurrentTracks(Alliance.Redfor) }
                },
                ActivePasses = ordnanceEmploymentSystem.ActivePasses.ToList(),
                PendingEffects = ordnanceEmploymentSystem.PendingEffects
                    .Where(effect => !effect.IsDefeated)
                    .ToList(),
                BarcapTargetByFlightId = new Dictionary<Guid, Guid>(),
                KnownSamThreatsByAlliance =
                    new Dictionary<Alliance, IReadOnlyList<KnownSamThreatEnvelope>>
                    {
                        { Alliance.Bluefor, GetKnownSamThreats(Alliance.Bluefor) },
                        { Alliance.Redfor, GetKnownSamThreats(Alliance.Redfor) }
                    }
            };
            frame.BarcapTargetByFlightId = AirCombatRules.BuildBarcapAssignments(
                frame,
                ordnanceTypes,
                GetDoctrine);
            return frame;
        }

        private IReadOnlyList<KnownSamThreatEnvelope> GetKnownSamThreats(
            Alliance alliance)
        {
            if (knownSamThreatCache.TryGetValue(alliance, out var cached))
                return cached;

            var threats = knownSamThreatAssessment.BuildKnownThreats(
                gameManager.intelligenceSystem?.GetPicture(alliance),
                gameManager.SimulationSettings.TileDistanceKM);
            knownSamThreatCache[alliance] = threats;
            return threats;
        }

        private IReadOnlyDictionary<Guid, IADSTrack> GetCurrentTracks(Alliance alliance)
        {
            var iads = gameManager.GetAllianceIADS(alliance);
            if (iads == null)
                return new Dictionary<Guid, IADSTrack>();

            return iads.CurrentTracks
                .Where(track => track != null
                                && !track.IsStale
                                && track.FlightId != Guid.Empty)
                .GroupBy(track => track.FlightId)
                .ToDictionary(group => group.Key, group => group.First());
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
            var wvrEvent = wvrEngagementSystem.GetNextScheduledEvent(cursor, next);
            if (wvrEvent.HasValue)
                next = wvrEvent.Value;
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
                || command.RequestsWvrEngagement
                || wvrEngagementSystem.IsFlightEngaged(flight.FlightId)
                || !TryGetFlightContext(
                    flight,
                    out var squadron,
                    out var aircraftType))
                return;

            var remaining = elapsedSeconds;
            var localTime = intervalStart;
            HashSet<Guid> waypointsCrossedWithoutTime = null;
            while (remaining > 0.0001d && flight.IsAirborne && !flight.IsWaitingAtRendezvous)
            {
                var followingRoute = command.Maneuver == AirCombatManeuver.FollowRoute;
                var target = followingRoute
                    ? flight.CurrentWaypoint?.PositionFeet ?? flight.PositionFeet
                    : command.AimPointFeet;
                var speedKnots = followingRoute
                    ? GetGuidanceSpeedKnots(package, flight, aircraftType)
                    : Math.Max(1f, command.DesiredSpeedKnots);
                if (squadron.Aircraft.Any(aircraft =>
                        aircraft.AssignedFlightId == flight.FlightId
                        && aircraft.Status == CampaignAircraftStatus.Damaged))
                {
                    speedKnots *=
                        WvrEngagementSystem.DamagedAircraftSpeedMultiplier;
                }

                if (followingRoute
                    && HasReached(flight.PositionFeet, target, aircraftType, speedKnots))
                {
                    var waypoint = flight.CurrentWaypoint;
                    if (waypoint != null
                        && !(waypointsCrossedWithoutTime ??= new HashSet<Guid>())
                            .Add(waypoint.WaypointId))
                    {
                        var progressSeconds = Math.Min(
                            MaximumIntegrationStepSeconds,
                            remaining);
                        remaining -= progressSeconds;
                        localTime = localTime.AddSeconds(progressSeconds);
                        BurnFuel(
                            flight,
                            aircraftType,
                            command.Intent,
                            progressSeconds);
                        waypointsCrossedWithoutTime.Clear();
                        continue;
                    }

                    flight.UpdateKinematics(target, flight.HeadingDegrees, speedKnots);
                    HandleWaypoint(package, flight, localTime);
                    continue;
                }

                waypointsCrossedWithoutTime?.Clear();
                var step = Math.Min(MaximumIntegrationStepSeconds, remaining);
                var previous = flight.PositionFeet;
                IntegrateMotion(
                    flight,
                    target,
                    aircraftType,
                    command.Maneuver,
                    speedKnots,
                    step);
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
            var consumed = AirFuelRules.CalculateBurnFraction(
                aircraftType,
                intent,
                seconds);
            flight.TacticalState.FuelFraction = Mathf.Clamp01(
                flight.TacticalState.FuelFraction - consumed);
        }

        private void ResolveDamageRecovery(DateTime currentTime)
        {
            foreach (var package in airTaskingSystem.GetPackages()
                         .OrderBy(candidate => candidate.PackageId))
            {
                if (package.Flights.All(flight =>
                        flight.ExecutionPhase
                        == FlightExecutionPhase.AwaitingTakeoff)
                    && TryGetDeadPreflightInvalidationReason(
                        package,
                        out var deadInvalidationReason))
                {
                    airTaskingSystem.CancelPackage(
                        package.Alliance,
                        package.PackageId,
                        currentTime,
                        deadInvalidationReason);
                    continue;
                }

                foreach (var flight in package.Flights
                             .Where(candidate => candidate.IsAirborne)
                             .OrderBy(candidate => candidate.FlightId))
                {
                    if (wvrEngagementSystem.IsFlightEngaged(flight.FlightId)
                        || flight.ExecutionPhase == FlightExecutionPhase.Returning
                        || flight.ExecutionPhase == FlightExecutionPhase.Landing
                        || !TryGetFlightContext(flight, out var squadron, out _)
                        || !squadron.Aircraft.Any(aircraft =>
                            aircraft.AssignedFlightId == flight.FlightId
                            && aircraft.Status
                            == CampaignAircraftStatus.Damaged))
                        continue;

                    flight.Cancel(
                        currentTime,
                        "Aircraft damage required recovery after leaving combat.");
                }
            }
        }

        private void BurnWvrFuel(
            AirCombatFrame frame,
            IReadOnlyCollection<AirCombatCommand> commands,
            double elapsedSeconds)
        {
            if (elapsedSeconds <= 0d)
                return;
            var requestedFlightIds = commands
                .Where(command => command.RequestsWvrEngagement)
                .Select(command => command.FlightId)
                .ToHashSet();
            foreach (var view in frame.Flights.Values
                         .Where(view =>
                             requestedFlightIds.Contains(view.Flight.FlightId)
                             || wvrEngagementSystem.IsFlightEngaged(
                                 view.Flight.FlightId)))
            {
                BurnFuel(
                    view.Flight,
                    view.AircraftType,
                    AirCombatIntent.EngageTarget,
                    elapsedSeconds);
            }
        }

        private bool TryRefuelFromReservedTanker(
            AirPackage receiverPackage,
            AirFlight receiver,
            AircraftTypeDefinition receiverType,
            DateTime currentTime)
        {
            if (receiverPackage == null
                || receiver == null
                || receiverType == null
                || !receiverType.CanReceiveAerialRefueling
                || receiver.ExecutionPhase != FlightExecutionPhase.Executing
                || receiverPackage.SupportingFlightIds.Count == 0)
                return false;

            var supportingFlightIds = receiverPackage.SupportingFlightIds.ToHashSet();
            var doctrine = GetDoctrine(receiverPackage.Alliance);
            foreach (var tanker in airTaskingSystem.GetPackages()
                         .Where(package => package.Alliance == receiverPackage.Alliance)
                         .SelectMany(package => package.Flights)
                         .Where(flight => supportingFlightIds.Contains(flight.FlightId))
                         .OrderBy(flight => flight.FlightId))
            {
                if (!TryGetFlightContext(tanker, out _, out var tankerType))
                    continue;
                if (tanker.TacticalState.FuelFraction
                    <= doctrine.JokerFuelFraction)
                    continue;

                var reservation = tanker.SupportReservations
                    .Where(candidate =>
                        candidate.ConsumingPackageId == receiverPackage.PackageId
                        && candidate.SupportingFlightId == tanker.FlightId
                        && candidate.StartTime <= currentTime
                        && candidate.EndTime > currentTime)
                    .OrderBy(candidate => candidate.StartTime)
                    .FirstOrDefault();
                if (!AirSupportCoveragePlanner.HasCapacityForReservation(
                        tanker,
                        reservation,
                        currentTime))
                    continue;
                if (!AirFuelRules.CanReceiveFuel(
                        receiver,
                        receiverType,
                        tanker,
                        tankerType,
                        reservation,
                        receiverPackage.PackageId,
                        currentTime))
                    continue;

                return receiver.TryReceiveAerialRefueling(
                    tanker.FlightId,
                    currentTime);
            }

            return false;
        }

        private void RefreshActiveSupportCapacities()
        {
            foreach (var flight in airTaskingSystem.GetPackages()
                         .SelectMany(package => package.Flights)
                         .Where(flight => flight.IsAirborne
                                          && flight.ProvidedSupportSlots > 0))
            {
                if (!TryGetFlightContext(
                        flight,
                        out var squadron,
                        out var aircraftType)
                    || aircraftType.SupportCapability
                    == AirSupportCapability.None)
                    continue;

                var survivingAircraft = squadron.Aircraft.Count(aircraft =>
                    aircraft.AssignedFlightId == flight.FlightId
                    && aircraft.Status != CampaignAircraftStatus.Lost);
                flight.ProvidedSupportSlots =
                    survivingAircraft * aircraftType.SupportSlotCapacity;
            }
        }

        private bool HasContinuousReservedTankerCoverage(
            AirPackage receiverPackage)
        {
            var supportingIds = receiverPackage.SupportingFlightIds.ToHashSet();
            var supportingFlights = airTaskingSystem.GetPackages()
                .Where(package => package.Alliance == receiverPackage.Alliance)
                .SelectMany(package => package.Flights)
                .Where(flight => supportingIds.Contains(flight.FlightId))
                .ToList();
            if (supportingFlights.Select(flight => flight.FlightId)
                    .Distinct()
                    .Count() != supportingIds.Count)
                return false;

            return AirSupportCoveragePlanner.HasContinuousReservedCoverage(
                supportingFlights,
                receiverPackage.PackageId,
                receiverPackage.Flights.Sum(flight => flight.AircraftIds.Count),
                receiverPackage.EffectStart,
                receiverPackage.SupportWindowEnd);
        }

        private void ProcessDeadMissions(DateTime currentTime)
        {
            foreach (var package in airTaskingSystem.GetPackages()
                         .Where(candidate => candidate.LifecycleState
                                             == AirTaskingLifecycleState.Active)
                         .OrderBy(candidate => candidate.PackageId))
            {
                var request = airTaskingSystem.GetCommander(package.Alliance)
                    ?.GetRequest(package.MissionRequestId);
                if (request?.RequestType
                        != AirMissionRequestType.DestructionOfEnemyAirDefenses
                    || request.DeadPlan == null
                    || !gameManager.airDefenseSiteSystem.TryGetSite(
                        request.DeadPlan.TargetSiteId,
                        out var site))
                    continue;

                var currentReport = gameManager.intelligenceSystem
                    ?.GetPicture(package.Alliance)
                    ?.HostileAirDefenseSites
                    ?.FirstOrDefault(report => report != null
                                               && report.SiteId == site.SiteId
                                               && report.InformationQuality > 0f);
                if (currentReport == null)
                {
                    var invalidatedFlightIds = package.Flights
                        .Where(flight => flight.MissionType
                                         == AirMissionRequestType
                                             .DestructionOfEnemyAirDefenses
                                         && flight.IsAirborne)
                        .Select(flight => flight.FlightId)
                        .ToList();
                    ordnanceEmploymentSystem.CancelAirToGroundPasses(
                        invalidatedFlightIds,
                        currentTime,
                        "DEAD preparation aborted because the assigned site is no longer known.");
                    foreach (var flight in package.Flights.Where(flight =>
                                 invalidatedFlightIds.Contains(flight.FlightId)))
                    {
                        flight.Cancel(
                            currentTime,
                            "The assigned SAM site is no longer known; the flight will not retarget airborne.");
                    }
                    continue;
                }

                request.DeadPlan.TargetComponentIds = (currentReport.Components
                                                        ?? new List<AirDefenseComponentIntelligenceReport>())
                    .Where(component => component != null
                                        && !component.IsDamaged)
                    .Select(component => component.ComponentId)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList();

                var effectiveSiteAlliance = gameManager.airDefenseSiteSystem
                    .GetEffectiveAlliance(site);
                var siteNoLongerHostile = site.IsDisabled
                                          || site.IsDestroyed
                                          || effectiveSiteAlliance
                                          == Alliance.Neutral
                                          || effectiveSiteAlliance
                                          == package.Alliance;
                var hasFunctionalShooterChain = !siteNoLongerHostile
                                                && HasPermanentSamShooterChain(site);
                var minimumEffectAchievedByPackage =
                    DidPackageAchieveDeadMinimumEffect(package, site);
                var corridorStillBlocked = siteNoLongerHostile
                                           || IsDeadCorridorStillBlocked(
                                               package.Alliance,
                                               request.DeadPlan);
                foreach (var flight in package.Flights
                             .Where(candidate => candidate.MissionType
                                                 == AirMissionRequestType
                                                     .DestructionOfEnemyAirDefenses
                                                 && candidate.IsAirborne)
                             .OrderBy(candidate => candidate.FlightId))
                {
                    var canApproachTargetFireControlRadar =
                        TryGetFlightContext(
                            flight,
                            out var probingSquadron,
                            out _)
                        && CanApproachDeadFireControlRadar(
                            flight,
                            probingSquadron,
                            site,
                            request.DeadPlan.TargetComponentIds);
                    flight.UpdateSurfaceThreatPenetrationAuthorization(
                        (minimumEffectAchievedByPackage
                         && !hasFunctionalShooterChain)
                        || canApproachTargetFireControlRadar);
                    flight.UpdateMissionOutcome(
                        minimumEffectAchievedByPackage,
                        currentTime,
                        hasFunctionalShooterChain
                            ? "The target SAM still has a functional shooter chain."
                            : minimumEffectAchievedByPackage
                                ? "The package permanently removed the target SAM's functional shooter chain."
                                : "The target SAM's shooter chain ended without a qualifying package effect.");

                    var targetInsideFixedArea = request.MissionArea.Contains(
                        currentReport.TileId);
                    if (siteNoLongerHostile
                        || !targetInsideFixedArea
                        || (!corridorStillBlocked
                            && !minimumEffectAchievedByPackage))
                    {
                        ordnanceEmploymentSystem.CancelAirToGroundPasses(
                            new[] { flight.FlightId },
                            currentTime,
                            siteNoLongerHostile
                                ? "DEAD preparation aborted because the site is no longer hostile."
                                : !corridorStillBlocked
                                    ? "DEAD preparation aborted because the supported corridor is now open."
                                : "DEAD preparation aborted because the mobile site left the fixed mission area.");
                        if (!minimumEffectAchievedByPackage)
                        {
                            flight.Cancel(
                                currentTime,
                                !targetInsideFixedArea
                                    ? "The assigned SAM left the fixed DEAD mission area; the flight will not pursue it."
                                    : "The assigned SAM no longer blocks the supported corridor; the flight will not retarget airborne.");
                        }
                        else
                        {
                            flight.EndDeadAttackAndBeginRecovery(
                                currentTime,
                                true,
                                "The DEAD minimum effect is complete; ending the attack sequence.");
                        }
                        continue;
                    }

                    if (flight.ExecutionPhase != FlightExecutionPhase.Executing
                        || currentTime >= flight.EffectEnd
                        || !gameManager.airDefenseSiteSystem.TryGetTileId(
                            site,
                            out var siteTileId)
                        || !TryGetFlightContext(
                            flight,
                            out var squadron,
                            out _))
                        continue;

                    TryStartNextDeadAttack(
                        flight,
                        squadron,
                        site,
                        siteTileId,
                        request.DeadPlan.TargetComponentIds,
                        currentTime);

                    var unresolvedGroundEffect = ordnanceEmploymentSystem
                        .ActivePasses.Any(pass =>
                            pass.SourceFlightId == flight.FlightId
                            && pass.TargetKind
                            == OrdnanceEmploymentTargetKind.AirDefenseComponent)
                        || ordnanceEmploymentSystem.PendingEffects.Any(effect =>
                            effect.SourceFlightId == flight.FlightId
                            && effect.TargetKind
                            == OrdnanceEmploymentTargetKind.AirDefenseComponent);
                    if (!unresolvedGroundEffect
                        && !HasDeadMissionUsefulOrdnance(
                            flight,
                            squadron,
                            site,
                            request.DeadPlan.TargetComponentIds))
                    {
                        flight.EndDeadAttackAndBeginRecovery(
                            currentTime,
                            minimumEffectAchievedByPackage,
                            hasFunctionalShooterChain
                                ? "Mission-useful DEAD ordnance is exhausted before the minimum effect."
                                : minimumEffectAchievedByPackage
                                    ? "The DEAD minimum effect is complete and no useful cleanup ordnance remains."
                                    : "The target was invalidated without a qualifying package effect.");
                    }
                }
            }
        }

        private bool TryGetDeadPreflightInvalidationReason(
            AirPackage package,
            out string reason)
        {
            reason = string.Empty;
            var request = airTaskingSystem.GetCommander(package.Alliance)
                ?.GetRequest(package.MissionRequestId);
            if (request?.RequestType
                    != AirMissionRequestType.DestructionOfEnemyAirDefenses
                || request.DeadPlan == null)
                return false;

            var report = gameManager.intelligenceSystem
                ?.GetPicture(package.Alliance)
                ?.HostileAirDefenseSites
                ?.FirstOrDefault(candidate => candidate != null
                                              && candidate.SiteId
                                              == request.DeadPlan.TargetSiteId
                                              && candidate.InformationQuality > 0f);
            if (report == null)
            {
                reason = "The assigned SAM site is no longer known before takeoff.";
                return true;
            }

            if (report.IsDisabled || report.IsDestroyed)
            {
                reason = "The assigned SAM site no longer requires a DEAD attack before takeoff.";
                return true;
            }

            var refreshedComponentIds = (report.Components
                                         ?? new List<AirDefenseComponentIntelligenceReport>())
                .Where(component => component != null && !component.IsDamaged)
                .Select(component => component.ComponentId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            var plannedComponentIds = (request.DeadPlan.TargetComponentIds
                                       ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            request.DeadPlan.TargetComponentIds = refreshedComponentIds;
            if (report.TileId != request.MissionArea.CenterTileId)
            {
                request.MissionArea.CenterTileId = report.TileId;
                reason =
                    "The assigned mobile SAM moved before takeoff; "
                    + "the package must be rematerialized around its current position.";
                return true;
            }
            if (!plannedComponentIds.SequenceEqual(refreshedComponentIds))
            {
                reason =
                    "The known DEAD component set changed before takeoff; "
                    + "the package must be rematerialized with current target coverage.";
                return true;
            }
            if (!IsDeadCorridorStillBlocked(package.Alliance, request.DeadPlan))
            {
                reason = "The supported corridor opened before the DEAD package took off.";
                return true;
            }
            return false;
        }

        private bool IsDeadCorridorStillBlocked(
            Alliance alliance,
            DeadMissionPlan plan)
        {
            if (plan?.SupportedCorridor == null
                || plan.TargetSiteId == Guid.Empty)
                return false;
            var picture = gameManager.intelligenceSystem?.GetPicture(alliance);
            var threats = knownSamThreatAssessment.BuildKnownThreats(
                picture,
                gameManager.SimulationSettings.TileDistanceKM);
            var targetThreats = threats
                .Where(threat => threat.SiteId == plan.TargetSiteId)
                .ToList();
            if (targetThreats.Count == 0)
                return false;

            aircraftTypes.TryGetValue(
                plan.SupportedCorridor.RepresentativeAircraftTypeDefinitionId,
                out var representativeType);
            var clearanceFeet = representativeType == null
                ? 0f
                : AirspaceGeometry.SamManeuverClearanceFeet(
                    representativeType,
                    representativeType.CruiseSpeedKnots);
            if (!targetThreats.Any(threat =>
                    threat.IntersectsSegment(
                        plan.SupportedCorridor.OriginPositionFeet,
                        plan.SupportedCorridor.DestinationPositionFeet,
                        clearanceFeet)
                    || threat.IntersectsSegment(
                        plan.SupportedCorridor.DestinationPositionFeet,
                        plan.SupportedCorridor.RecoveryPositionFeet,
                        clearanceFeet)))
                return false;

            var geometry = deadCorridorRoutePlanner.Plan(
                new AirRouteGeometryPlanningContext(
                    plan.SupportedCorridor.OriginPositionFeet,
                    plan.SupportedCorridor.DestinationPositionFeet,
                    plan.SupportedCorridor.DestinationPositionFeet,
                    plan.SupportedCorridor.RecoveryPositionFeet,
                    gameManager.SimulationSettings.TileDistanceKM
                    * AirspaceGeometry.FeetPerKilometer,
                    plan.SupportedCorridor
                        .RepresentativeAircraftTypeDefinitionId,
                    threats,
                    clearanceFeet));
            return !geometry.IsThreatSafe;
        }

        private void TryStartNextDeadAttack(
            AirFlight flight,
            Squadron squadron,
            SamSite site,
            Vector3Int siteTileId,
            IReadOnlyCollection<Guid> authorizedComponentIds,
            DateTime currentTime)
        {
            var authorized = new HashSet<Guid>(
                authorizedComponentIds ?? Array.Empty<Guid>());
            var targetPosition = AirspaceGeometry.TileCenterFeet(
                siteTileId,
                gameManager.SimulationSettings.TileDistanceKM);
            var distanceKm = HorizontalDistanceKm(
                flight.PositionFeet,
                targetPosition);
            var functionalFireControlRemains = site.Components.Any(component =>
                component != null
                && !component.IsDamaged
                && authorized.Contains(component.ComponentId)
                && airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                && definition is RadarAirDefenseComponentDefinition
                {
                    ProvidesWeaponQualityTrack: true
                });
            foreach (var target in site.Components
                         .Where(component => component != null
                                             && !component.IsDamaged
                                             && authorized.Contains(
                                                 component.ComponentId)
                                             && !ordnanceEmploymentSystem
                                                 .HasActiveOrPendingEffect(
                                                     component.ComponentId))
                         .Select(component => new
                         {
                             Component = component,
                             Definition = airDefenseComponentDefinitions
                                 .TryGetValue(
                                     component.SamComponentDefinitionId,
                                     out var definition)
                                 ? definition
                                 : null
                         })
                         .Where(candidate => candidate.Definition != null)
                         .Where(candidate => !functionalFireControlRemains
                                             || candidate.Definition
                                             is RadarAirDefenseComponentDefinition
                                             {
                                                 ProvidesWeaponQualityTrack: true
                                             })
                         .OrderBy(candidate => GetDeadTargetPriority(
                             candidate.Definition))
                         .ThenBy(candidate => ordnanceEmploymentSystem.Records.Count(
                             record => record.TargetKind
                                       == OrdnanceEmploymentTargetKind
                                           .AirDefenseComponent
                                       && record.TargetComponentId
                                       == candidate.Component.ComponentId
                                       && record.Stage
                                       == OrdnanceEmploymentRecordStage
                                           .OrdnanceReleased))
                         .ThenBy(candidate => candidate.Component.ComponentId))
            {
                var weaponId = squadron.Aircraft
                    .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                       && aircraft.Status
                                       != CampaignAircraftStatus.Lost
                                       && aircraft.Status
                                       != CampaignAircraftStatus.Damaged)
                    .SelectMany(aircraft => aircraft.Loadout)
                    .Where(item => item.Count > 0
                                   && ordnanceTypes.TryGetValue(
                                       item.OrdnanceTypeDefinitionId,
                                       out var ordnance)
                                   && DeadLoadoutPlanner.CanAttackComponent(
                                       ordnance,
                                       target.Definition)
                                   && distanceKm >= ordnance.MinimumRangeKm
                                   && distanceKm <= ordnance.MaximumRangeKm)
                    .Select(item => ordnanceTypes[
                        item.OrdnanceTypeDefinitionId])
                    .OrderByDescending(ordnance =>
                        target.Definition.TargetCategory
                        == OrdnanceTargetCategory.Radar
                        && ordnance.EmploymentCategory
                        == OrdnanceEmploymentCategory.AntiRadiation)
                    .ThenByDescending(ordnance => ordnance.MaximumRangeKm)
                    .ThenByDescending(ordnance => ordnance.GetEffectiveness(
                        target.Definition.TargetCategory))
                    .ThenBy(ordnance => ordnance.OrdnanceTypeDefinitionId)
                    .Select(ordnance => ordnance.OrdnanceTypeDefinitionId)
                    .FirstOrDefault();
                if (weaponId == Guid.Empty)
                    continue;

                if (ordnanceEmploymentSystem.TryStartAirToGroundPass(
                        flight.FlightId,
                        site.SiteId,
                        target.Component.ComponentId,
                        weaponId,
                        currentTime))
                    return;
            }
        }

        private bool CanApproachDeadFireControlRadar(
            AirFlight flight,
            Squadron squadron,
            SamSite site,
            IReadOnlyCollection<Guid> authorizedComponentIds)
        {
            if (flight == null
                || squadron == null
                || site == null
                || ordnanceEmploymentSystem.ActivePasses.Any(pass =>
                    pass.SourceFlightId == flight.FlightId
                    && pass.TargetKind
                    == OrdnanceEmploymentTargetKind.AirDefenseComponent)
                || ordnanceEmploymentSystem.PendingEffects.Any(effect =>
                    effect.SourceFlightId == flight.FlightId
                    && effect.TargetKind
                    == OrdnanceEmploymentTargetKind.AirDefenseComponent))
                return false;

            var authorized = new HashSet<Guid>(
                authorizedComponentIds ?? Array.Empty<Guid>());
            var fireControlRadars = site.Components
                .OfType<RadarAirDefenseComponent>()
                .Where(radar => !radar.IsDamaged
                                && authorized.Contains(radar.ComponentId)
                                && airDefenseComponentDefinitions.TryGetValue(
                                    radar.SamComponentDefinitionId,
                                    out var definition)
                                && definition
                                is RadarAirDefenseComponentDefinition
                                {
                                    ProvidesWeaponQualityTrack: true
                                })
                .ToList();
            if (fireControlRadars.Count == 0)
                return false;

            return squadron.Aircraft
                .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Lost
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Damaged)
                .SelectMany(aircraft => aircraft.Loadout)
                .Any(item => item.Count > 0
                             && ordnanceTypes.TryGetValue(
                                 item.OrdnanceTypeDefinitionId,
                                 out var ordnance)
                             && (ordnance.EmploymentCategory
                                 == OrdnanceEmploymentCategory.AntiRadiation
                                 || ordnance.GuidanceMode
                                 == OrdnanceGuidanceMode.AntiRadiation)
                             && fireControlRadars.Any(radar =>
                                 airDefenseComponentDefinitions.TryGetValue(
                                     radar.SamComponentDefinitionId,
                                     out var definition)
                                 && DeadLoadoutPlanner.CanAttackComponent(
                                     ordnance,
                                     definition)));
        }

        private void ApplyDeadPostLaunchManeuvers(
            IReadOnlyCollection<AirCombatCommand> commands,
            AirCombatFrame frame,
            DateTime currentTime)
        {
            foreach (var command in commands.OrderBy(item => item.FlightId))
            {
                if (!frame.Flights.TryGetValue(command.FlightId, out var view)
                    || view.Flight.MissionType
                    != AirMissionRequestType.DestructionOfEnemyAirDefenses
                    || view.Flight.ExecutionPhase != FlightExecutionPhase.Executing
                    || view.Flight.AuthorizedSurfaceThreatSiteId == Guid.Empty
                    || command.Intent != AirCombatIntent.FollowMission
                    || command.Employment != null
                    || command.TargetFlightId != Guid.Empty
                    || command.RequestsWvrEngagement
                    || command.RequestsSurfaceThreatRecovery
                    || wvrEngagementSystem.IsFlightEngaged(command.FlightId)
                    || !gameManager.airDefenseSiteSystem.TryGetSite(
                        view.Flight.AuthorizedSurfaceThreatSiteId,
                        out var site)
                    || !HasPermanentSamShooterChain(site))
                    continue;

                var pendingEffect = ordnanceEmploymentSystem.PendingEffects
                    .Where(effect => effect != null
                                     && !effect.IsDefeated
                                     && effect.SourceFlightId
                                     == view.Flight.FlightId
                                     && effect.TargetKind
                                     == OrdnanceEmploymentTargetKind
                                         .AirDefenseComponent
                                     && effect.TargetSiteId == site.SiteId
                                     && effect.ResolveAt > currentTime
                                     && IsWeaponQualityRadarComponent(
                                         site,
                                         effect.TargetComponentId)
                                     && ordnanceTypes.TryGetValue(
                                         effect.OrdnanceTypeDefinitionId,
                                         out var ordnance)
                                     && (ordnance.EmploymentCategory
                                         == OrdnanceEmploymentCategory
                                             .AntiRadiation
                                         || ordnance.GuidanceMode
                                         == OrdnanceGuidanceMode
                                             .AntiRadiation))
                    .OrderBy(effect => effect.ResolveAt)
                    .FirstOrDefault();
                if (pendingEffect == null)
                    continue;

                var away = view.Flight.PositionFeet
                           - pendingEffect.TargetPositionFeet;
                away.y = 0f;
                if (away.sqrMagnitude < 1f)
                    away = pendingEffect.SourcePositionFeet
                           - pendingEffect.TargetPositionFeet;
                away.y = 0f;
                if (away.sqrMagnitude < 1f)
                    away = Vector3.forward;
                away.Normalize();

                var turnLeft = (view.Flight.FlightId.ToByteArray()[0] & 1) == 0;
                var tangent = turnLeft
                    ? new Vector3(-away.z, 0f, away.x)
                    : new Vector3(away.z, 0f, -away.x);
                var offsetDirection = (tangent + away * 0.35f).normalized;
                var secondsRemaining = Math.Max(
                    TacticalDecisionStepSeconds,
                    (pendingEffect.ResolveAt - currentTime).TotalSeconds);
                var cruiseFeetPerSecond =
                    Math.Max(1f, view.AircraftType.CruiseSpeedKnots)
                    * AirspaceGeometry.FeetPerNauticalMile / 3600f;
                var offsetDistanceFeet = Mathf.Clamp(
                    cruiseFeetPerSecond * (float)secondsRemaining,
                    10f * AirspaceGeometry.FeetPerKilometer,
                    25f * AirspaceGeometry.FeetPerKilometer);

                command.Intent = AirCombatIntent.FollowMission;
                command.Maneuver = AirCombatManeuver.Extend;
                command.TargetFlightId = Guid.Empty;
                command.SupportedPendingEffectId = pendingEffect.PendingEffectId;
                command.PreferredSide = turnLeft
                    ? AirCombatManeuverSide.Left
                    : AirCombatManeuverSide.Right;
                command.AimPointFeet = view.Flight.PositionFeet
                                       + offsetDirection * offsetDistanceFeet;
                command.AimPointFeet.y = view.Flight.PositionFeet.y;
                command.HasAimPoint = true;
                command.DesiredSpeedKnots = view.AircraftType.CruiseSpeedKnots;
                command.MinimumManeuverEndAt = currentTime.AddSeconds(
                    TacticalDecisionStepSeconds);
                command.Reason =
                    "Offsetting from the emitter while the anti-radiation missile resolves.";
                view.Flight.TacticalState.Apply(
                    command.Intent,
                    command.Maneuver,
                    currentTime,
                    command.MinimumManeuverEndAt,
                    command.TargetFlightId,
                    command.SupportedPendingEffectId,
                    command.PreferredSide,
                    command.AimPointFeet,
                    command.HasAimPoint,
                    command.Reason);
            }
        }

        private bool IsWeaponQualityRadarComponent(
            SamSite site,
            Guid componentId)
        {
            var component = site.Components.FirstOrDefault(candidate =>
                candidate != null && candidate.ComponentId == componentId);
            return component != null
                   && airDefenseComponentDefinitions.TryGetValue(
                       component.SamComponentDefinitionId,
                       out var definition)
                   && definition is RadarAirDefenseComponentDefinition
                   {
                       ProvidesWeaponQualityTrack: true
                   };
        }

        private bool HasDeadMissionUsefulOrdnance(
            AirFlight flight,
            Squadron squadron,
            SamSite site,
            IReadOnlyCollection<Guid> authorizedComponentIds)
        {
            var authorized = new HashSet<Guid>(
                authorizedComponentIds ?? Array.Empty<Guid>());
            var survivingTargets = site.Components
                .Where(component => component != null
                                    && !component.IsDamaged
                                    && authorized.Contains(component.ComponentId))
                .Select(component => airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                    ? definition
                    : null)
                .Where(definition => definition != null)
                .ToList();
            if (survivingTargets.Count == 0)
                return false;

            return squadron.Aircraft
                .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                   && aircraft.Status != CampaignAircraftStatus.Lost
                                   && aircraft.Status != CampaignAircraftStatus.Damaged)
                .SelectMany(aircraft => aircraft.Loadout)
                .Any(item => item.Count > 0
                             && ordnanceTypes.TryGetValue(
                                 item.OrdnanceTypeDefinitionId,
                                 out var ordnance)
                             && survivingTargets.Any(target =>
                                 DeadLoadoutPlanner.CanAttackComponent(
                                     ordnance,
                                     target)));
        }

        private bool HasPermanentSamShooterChain(SamSite site)
        {
            var fireControlRadar = site.Components.Any(component =>
                component is RadarAirDefenseComponent
                && !component.IsDamaged
                && airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                && definition is RadarAirDefenseComponentDefinition
                {
                    ProvidesWeaponQualityTrack: true
                });
            var launcher = site.Components.Any(component =>
                component is LauncherAirDefenseComponent
                && !component.IsDamaged);
            return fireControlRadar && launcher;
        }

        private bool DidPackageAchieveDeadMinimumEffect(
            AirPackage package,
            SamSite site)
        {
            if (HasPermanentSamShooterChain(site))
                return false;

            var packageFlightIds = package.Flights
                .Select(flight => flight.FlightId)
                .ToHashSet();
            var componentsHitByPackage = ordnanceEmploymentSystem.Records
                .Where(record => record.Stage
                                 == OrdnanceEmploymentRecordStage.EffectResolved
                                 && record.TargetKind
                                 == OrdnanceEmploymentTargetKind
                                     .AirDefenseComponent
                                 && record.TargetSiteId == site.SiteId
                                 && packageFlightIds.Contains(record.SourceFlightId)
                                 && record.Shots.Any(shot =>
                                     shot.Result == OrdnanceShotResult.Hit))
                .Select(record => record.TargetComponentId)
                .ToHashSet();
            if (componentsHitByPackage.Count == 0)
                return false;

            var wouldHaveFireControlRadar = site.Components.Any(component =>
                component is RadarAirDefenseComponent
                && (!component.IsDamaged
                    || componentsHitByPackage.Contains(component.ComponentId))
                && airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                && definition is RadarAirDefenseComponentDefinition
                {
                    ProvidesWeaponQualityTrack: true
                });
            var wouldHaveLauncher = site.Components.Any(component =>
                component is LauncherAirDefenseComponent
                && (!component.IsDamaged
                    || componentsHitByPackage.Contains(component.ComponentId)));
            return wouldHaveFireControlRadar && wouldHaveLauncher;
        }

        private static int GetDeadTargetPriority(
            AirDefenseComponentDefinition definition)
        {
            return definition switch
            {
                RadarAirDefenseComponentDefinition
                {
                    ProvidesWeaponQualityTrack: true
                } => 0,
                LauncherAirDefenseComponentDefinition => 1,
                RadarAirDefenseComponentDefinition => 2,
                CommandAirDefenseComponentDefinition => 3,
                _ => 4
            };
        }

        private static float HorizontalDistanceKm(Vector3 first, Vector3 second)
        {
            return Vector2.Distance(
                       new Vector2(first.x, first.z),
                       new Vector2(second.x, second.z))
                   / AirspaceGeometry.FeetPerKilometer;
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
            if (!TryAbortToImmediateRecovery(
                    package,
                    flight,
                    squadron,
                    aircraftType,
                    occurredAt,
                    reason))
            {
                if (TryBuildDirectRecoveryRoute(
                        package,
                        flight,
                        squadron,
                        aircraftType,
                        occurredAt,
                        out var recoveryRoute))
                {
                    flight.AbortAndReplaceRecoveryRoute(
                        occurredAt,
                        reason
                        + " No threat-safe recovery route was available; "
                        + "continuing toward the best friendly airport.",
                        recoveryRoute);
                    airTaskingSystem.RevalidateAirportOperations(
                        occurredAt);
                    return;
                }

                LoseAirborneFlight(
                    flight,
                    occurredAt,
                    "No friendly recovery airport remains.");
            }
        }

        private bool TryAbortToImmediateRecovery(
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
                return false;

            flight.AbortAndReplaceRecoveryRoute(occurredAt, reason, recoveryRoute);
            airTaskingSystem.RevalidateAirportOperations(occurredAt);
            return true;
        }

        private static void ContinueRecoveryCommand(
            AirCombatCommand command,
            AirFlight flight,
            AircraftTypeDefinition aircraftType,
            DateTime currentTime)
        {
            command.RequestsSurfaceThreatRecovery = false;
            command.Intent = AirCombatIntent.Recover;
            command.Maneuver = AirCombatManeuver.FollowRoute;
            command.TargetFlightId = Guid.Empty;
            command.SupportedPendingEffectId = Guid.Empty;
            command.PreferredSide = AirCombatManeuverSide.None;
            command.AimPointFeet =
                flight.CurrentWaypoint?.PositionFeet ?? flight.PositionFeet;
            command.HasAimPoint = true;
            command.DesiredSpeedKnots =
                Math.Max(1f, aircraftType.CruiseSpeedKnots);
            command.MinimumManeuverEndAt = currentTime;
            command.RequestsWvrEngagement = false;
            command.Employment = null;
        }

        private static void ContinueSurfaceThreatEscape(
            AirCombatCommand command,
            AirFlight flight,
            AircraftTypeDefinition aircraftType,
            DateTime currentTime)
        {
            var speedKnots = Math.Max(1f, aircraftType.CombatSpeedKnots);
            var escapeDistanceFeet = speedKnots
                                     * AirspaceGeometry.FeetPerNauticalMile
                                     / 3600f
                                     * (float)TacticalDecisionStepSeconds
                                     * 6f;
            var escapeAimPoint = flight.PositionFeet
                                 + AirCombatRules.Direction(
                                     flight.HeadingDegrees)
                                 * escapeDistanceFeet;
            escapeAimPoint.y = flight.PositionFeet.y;

            command.RequestsSurfaceThreatRecovery = false;
            command.Intent = AirCombatIntent.Disengage;
            command.Maneuver = AirCombatManeuver.AvoidSurfaceThreat;
            command.TargetFlightId = Guid.Empty;
            command.SupportedPendingEffectId = Guid.Empty;
            command.PreferredSide = AirCombatManeuverSide.None;
            command.AimPointFeet = escapeAimPoint;
            command.HasAimPoint = true;
            command.DesiredSpeedKnots = speedKnots;
            command.MinimumManeuverEndAt =
                currentTime.AddSeconds(TacticalDecisionStepSeconds);
            command.RequestsWvrEngagement = false;
            command.Employment = null;
            command.Reason =
                "No threat-safe recovery route is currently available; "
                + "maintaining course and reassessing.";
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
            if (CanAirportConductAirOperations(
                    flight.RecoveryAirportBuildingId,
                    package.Alliance))
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
            {
                if (!TryBuildDirectRecoveryRoute(
                        package,
                        flight,
                        squadron,
                        aircraftType,
                        currentTime,
                        out recoveryTail))
                    return false;
            }

            flight.ReplaceRecoveryRoute(recoveryTail);
            airTaskingSystem.RevalidateAirportOperations(currentTime);
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
            var threats = GetKnownSamThreats(package.Alliance);
            var maneuverClearanceFeet =
                AirspaceGeometry.SamManeuverClearanceFeet(
                    aircraftType,
                    aircraftType.CruiseSpeedKnots);
            var routeStart = flight.PositionFeet;
            var initialWaypoints = new List<Vector3>();
            if (KnownSamThreatGeometry.TryCreateEgressAimPoint(
                    routeStart,
                    threats,
                    flight.FlightId,
                    maneuverClearanceFeet,
                    out var egressAimPoint))
            {
                initialWaypoints.Add(egressAimPoint);
                routeStart = egressAimPoint;
            }
            else if (threats.Any(threat =>
                         threat != null && threat.Contains(routeStart)))
            {
                return false;
            }

            foreach (var candidate in GetRecoveryAirportCandidates(
                         package,
                         flight,
                         squadron))
            {
                if (!KnownSamThreatGeometry.TryBuildAvoidingWaypoints(
                        routeStart,
                        candidate.Position,
                        threats,
                        flight.FlightId,
                        maneuverClearanceFeet,
                        out var transitPath))
                    continue;

                var navigationPoints = initialWaypoints
                    .Concat(transitPath.Take(Math.Max(0, transitPath.Count - 1)))
                    .ToList();
                var route = new List<AirWaypoint>();
                var position = flight.PositionFeet;
                var time = currentTime;
                foreach (var point in navigationPoints)
                {
                    time += TimeSpan.FromSeconds(
                        AirspaceGeometry.TravelSeconds(
                            position,
                            point,
                            aircraftType.CruiseSpeedKnots,
                            aircraftType.ClimbRateFeetPerMinute,
                            aircraftType.DescentRateFeetPerMinute));
                    route.Add(new AirWaypoint(
                        point,
                        AirWaypointAction.Transit,
                        time));
                    position = point;
                }

                route.AddRange(AirRecoveryRouteBuilder.Build(
                    position,
                    aircraftType,
                    candidate.AirportId,
                    candidate.Position,
                    time));
                var actualRoutePoints = new List<Vector3> { routeStart };
                actualRoutePoints.AddRange(
                    route.Select(waypoint => waypoint.PositionFeet));
                if (!KnownSamThreatGeometry.IsPathSafe(
                        actualRoutePoints,
                        threats,
                        maneuverClearanceFeet,
                        out _))
                    continue;

                recoveryRoute = route;
                return true;
            }

            return false;
        }

        private bool TryBuildDirectRecoveryRoute(
            AirPackage package,
            AirFlight flight,
            Squadron squadron,
            AircraftTypeDefinition aircraftType,
            DateTime currentTime,
            out IReadOnlyList<AirWaypoint> recoveryRoute)
        {
            recoveryRoute = null;
            var candidate = GetRecoveryAirportCandidates(
                    package,
                    flight,
                    squadron)
                .FirstOrDefault();
            if (candidate.AirportId == Guid.Empty)
                return false;

            recoveryRoute = AirRecoveryRouteBuilder.Build(
                flight.PositionFeet,
                aircraftType,
                candidate.AirportId,
                candidate.Position,
                currentTime);
            return true;
        }

        private IEnumerable<(Guid AirportId, Vector3 Position)>
            GetRecoveryAirportCandidates(
            AirPackage package,
            AirFlight flight,
            Squadron squadron)
        {
            var yielded = new HashSet<Guid>();
            if (CanAirportConductAirOperations(
                    flight.RecoveryAirportBuildingId,
                    package.Alliance)
                && TryGetAirportPosition(
                    flight.RecoveryAirportBuildingId,
                    out var assignedPosition))
            {
                yielded.Add(flight.RecoveryAirportBuildingId);
                yield return (
                    flight.RecoveryAirportBuildingId,
                    assignedPosition);
            }

            if (CanAirportConductAirOperations(
                    squadron.AirportBuildingId,
                    package.Alliance)
                && yielded.Add(squadron.AirportBuildingId)
                && TryGetAirportPosition(
                    squadron.AirportBuildingId,
                    out var squadronPosition))
            {
                yield return (
                    squadron.AirportBuildingId,
                    squadronPosition);
            }

            foreach (var candidate in GetFriendlyAirports(package.Alliance)
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
                .ThenBy(candidate => candidate.Airport.BuildingId))
            {
                if (!yielded.Add(candidate.Airport.BuildingId))
                    continue;
                yield return (
                    candidate.Airport.BuildingId,
                    candidate.Position);
            }
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
                        var request = commander.GetRequest(
                            package.MissionRequestId);
                        if (request?.FulfillmentPattern
                            == AirMissionRequestFulfillmentPattern.Sustained)
                        {
                            // Sustained request fulfillment is owned by
                            // projected coverage across rotations, not the
                            // outcome of any one completed flight.
                            continue;
                        }

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
                if (IsAirportControlledBy(
                        squadron.AirportBuildingId,
                        alliance))
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

        private bool CanAirportConductAirOperations(
            Guid airportId,
            Alliance alliance)
        {
            return airportOperations.CanConductAirOperations(
                airportId,
                alliance);
        }

        private bool IsAirportControlledBy(
            Guid airportId,
            Alliance alliance)
        {
            return airportOperations.IsAirportControlledBy(
                airportId,
                alliance);
        }

        private IEnumerable<Airport> GetFriendlyAirports(Alliance alliance)
        {
            return gameManager.buildingSystem.GetBuildings<Airport>()
                .Where(airport =>
                {
                    return gameManager.tileSystem.TryGetLand(
                               airport.TileId,
                               out var landTile)
                           && landTile.Controller == alliance
                           && AirportOperationsRules.IsOperational(airport);
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
            return missionType == AirMissionRequestType.BarrierCombatAirPatrol
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
            AirCombatManeuver maneuver,
            float speedKnots,
            double seconds)
        {
            var desiredHeading = HeadingTo(flight.PositionFeet, target);
            var heading = Mathf.MoveTowardsAngle(
                flight.HeadingDegrees,
                desiredHeading,
                GetManeuverTurnRateDegreesPerSecond(aircraftType, maneuver)
                * (float)seconds);
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

        internal static float GetManeuverTurnRateDegreesPerSecond(
            AircraftTypeDefinition aircraftType,
            AirCombatManeuver maneuver)
        {
            if (aircraftType == null)
                return 0f;

            return maneuver == AirCombatManeuver.BeamLeft
                   || maneuver == AirCombatManeuver.BeamRight
                   || maneuver == AirCombatManeuver.BreakLeft
                   || maneuver == AirCombatManeuver.BreakRight
                   || maneuver == AirCombatManeuver.Drag
                ? aircraftType.DefensiveTurnRateDegreesPerSecond
                : aircraftType.TurnRateDegreesPerSecond;
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
            var turnRadiusFeet = AirspaceGeometry.TurnRadiusFeet(
                speedKnots,
                aircraftType.TurnRateDegreesPerSecond);
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
