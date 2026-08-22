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
    public sealed partial class AirExecutionSystem
    {
        private const double TacticalDecisionStepSeconds = 5d;
        private const double SeadOpportunityRetrySeconds = 5d;
        private const float SeadMinimumScreenLeadKm = 15f;
        private const float SeadMaximumScreenLeadKm = 40f;
        private const float SeadScreenLateralOffsetKm = 5f;
        private const double SeadTimingSafetyMarginSeconds = 90d;

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
        private readonly GroundAttackOpportunityService
            groundAttackOpportunityService;
        private readonly GroundAttackDecisionService groundAttackDecisionService;
        private readonly FlightMovementSystem flightMovementSystem;
        private readonly IReadOnlyList<IFlightMissionBehavior> missionBehaviors;
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
            groundAttackOpportunityService =
                new GroundAttackOpportunityService(
                    airDefenseComponentDefinitions);
            groundAttackDecisionService = new GroundAttackDecisionService();
            flightMovementSystem = new FlightMovementSystem(
                GetGuidanceSpeedKnots,
                HandleWaypoint);
            missionBehaviors = new IFlightMissionBehavior[]
            {
                new DeadFlightMissionBehavior(ProcessDeadMissions),
                new StrikeFlightMissionBehavior(ProcessStrikeMissions)
            };
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
            ordnanceEmploymentSystem.SetAirToAirEmploymentValidator(
                IsAirToAirEmploymentAuthorized);
        }

        private bool IsAirToAirEmploymentAuthorized(
            ActiveOrdnanceEmploymentPass pass,
            DateTime currentTime)
        {
            if (pass == null
                || pass.SourceFlightId == Guid.Empty
                || pass.TargetFlightId == Guid.Empty)
                return false;

            var frame = BuildAirCombatFrame(currentTime);
            return frame.Flights.TryGetValue(
                       pass.SourceFlightId,
                       out var source)
                   && frame.Flights.TryGetValue(
                       pass.TargetFlightId,
                       out var target)
                   && AirCombatRules.IsAirToAirEmploymentAuthorized(
                       source,
                       target,
                       frame,
                       pass,
                       ordnanceTypes,
                       GetDoctrine(source.Alliance),
                       out _);
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

        /// <summary>
        /// Formats a tactical state change for the simulation log. Written here
        /// rather than on the flight because the target's alliance is only known
        /// from the combat frame.
        /// </summary>
        private static string DescribeTacticalTransition(
            AirCombatIntent previousIntent,
            AirCombatManeuver previousManeuver,
            AirCombatCommand command,
            AirCombatFrame frame)
        {
            var detail =
                $"stage {command.DecisionStage}"
                + $" | intent {previousIntent} -> {command.Intent}"
                + $" | maneuver {previousManeuver} -> {command.Maneuver}";

            if (command.TargetFlightId != Guid.Empty)
            {
                detail += " | target "
                          + (frame.Flights.TryGetValue(
                              command.TargetFlightId,
                              out var targetView)
                              ? SimLogNames.FlightLabel(
                                  targetView.Alliance,
                                  command.TargetFlightId)
                              : SimLogNames.ShortId(command.TargetFlightId));
            }

            if (!string.IsNullOrWhiteSpace(command.Reason))
                detail += $" | {command.Reason}";

            return detail;
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
                CoordinateAirPackages(cursor);

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
                ProcessMissionBehaviors(cursor);
                frame = BuildAirCombatFrame(cursor);
                ProcessSeadEscorts(frame, cursor);
                frame = BuildAirCombatFrame(cursor);

                var commands = frame.Flights.Values
                    .Where(view => view.Flight.IsAirborne
                                   && !wvrEngagementSystem.IsFlightEngaged(
                                       view.Flight.FlightId))
                    .OrderBy(view => view.Flight.FlightId)
                    .Select(view => AirCombatRules.Decide(
                        view,
                        frame,
                        ordnanceTypes,
                        GetDoctrine(view.Alliance)))
                    .ToList();
                ApplySeadScreenCommands(commands, frame);
                ApplyPackageAbortDecisions(commands, frame, cursor);

                foreach (var command in commands)
                {
                    if (!frame.Flights.TryGetValue(command.FlightId, out var view))
                        continue;
                    if (command.RequestsSurfaceThreatRecovery)
                    {
                        var relocationReason = string.Empty;
                        if (command.RequestsBarcapStationRelocation
                            && TryRelocateBarcapStation(
                                view.Package,
                                view.Flight,
                                view.Squadron,
                                view.AircraftType,
                                cursor,
                                command.Reason,
                                out relocationReason))
                        {
                            ContinueRelocatedBarcapCommand(
                                command,
                                view.Flight,
                                view.AircraftType,
                                cursor,
                                relocationReason);
                        }
                        else if (TryAbortToImmediateRecovery(
                                view.Package,
                                view.Flight,
                                view.Squadron,
                                view.AircraftType,
                                cursor,
                                string.IsNullOrWhiteSpace(relocationReason)
                                    ? command.Reason
                                    : relocationReason))
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
                    var previousIntent = view.Flight.TacticalState.Intent;
                    var previousManeuver = view.Flight.TacticalState.Maneuver;
                    view.Flight.TacticalState.DecisionStage =
                        command.DecisionStage;
                    if (view.Flight.TacticalState.Apply(
                            command.Intent,
                            command.Maneuver,
                            cursor,
                            command.MinimumManeuverEndAt,
                            command.TargetFlightId,
                            command.SupportedPendingEffectId,
                            command.PreferredSide,
                            command.AimPointFeet,
                            command.HasAimPoint,
                            command.Reason))
                    {
                        view.Flight.RecordTacticalTransition(
                            cursor,
                            DescribeTacticalTransition(
                                previousIntent,
                                previousManeuver,
                                command,
                                frame));
                    }

                    view.Flight.TacticalState.ObserveThreatCandidate(
                        command.ObservedThreatCandidateFlightId,
                        cursor,
                        TimeSpan.FromSeconds(
                            TacticalDecisionStepSeconds * 2d));
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
                ProcessMissionBehaviors(cursor);
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
                        if (TryCancelUnsafeBarcapBeforeTakeoff(
                                package,
                                flight,
                                aircraftType,
                                currentTime))
                            continue;
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

        private void CoordinateAirPackages(DateTime currentTime)
        {
            foreach (var package in airTaskingSystem.GetPackages()
                         .OrderBy(candidate => candidate.PackageId))
            {
                if (package.IsTerminal)
                {
                    var completed = package.LifecycleState
                                    == AirTaskingLifecycleState.Completed;
                    package.UpdateExecutionPhase(completed
                        ? AirPackageExecutionPhase.Completed
                        : AirPackageExecutionPhase.Aborted);
                    if (!completed)
                    {
                        AbortPackageFlights(
                            package,
                            currentTime,
                            "Mission commander terminated the package after a required flight could no longer continue.");
                    }
                    continue;
                }

                var required = package.Flights
                    .Where(flight => flight.IsRequired)
                    .ToList();
                if (required.Count == 0)
                    continue;

                var unavailableCriticalFlight = required
                    .Where(flight => !flight.IsFighterEscort
                                     && flight.IsTerminal
                                     && flight.LifecycleState
                                     != AirTaskingLifecycleState.Completed
                                     && (!flight.IsSeadEscort
                                         || !required.Any(other =>
                                             other.FlightId != flight.FlightId
                                             && other.IsSeadEscort
                                             && !other.IsTerminal)))
                    .OrderBy(flight => flight.FlightId)
                    .FirstOrDefault();
                if (unavailableCriticalFlight != null)
                {
                    package.UpdateExecutionPhase(
                        AirPackageExecutionPhase.Aborted);
                    AbortPackageFlights(
                        package,
                        currentTime,
                        "Mission commander terminated the package because critical flight "
                        + SimLogNames.ShortId(
                            unavailableCriticalFlight.FlightId)
                        + " could no longer perform its assigned role.");
                    continue;
                }

                if (required.All(flight =>
                        flight.ExecutionPhase == FlightExecutionPhase.Returning
                        || flight.ExecutionPhase == FlightExecutionPhase.Landing
                        || flight.ExecutionPhase == FlightExecutionPhase.Ended))
                {
                    package.UpdateExecutionPhase(
                        AirPackageExecutionPhase.Egressing);
                    continue;
                }

                if (required.Any(flight =>
                        flight.ExecutionPhase == FlightExecutionPhase.Executing))
                {
                    package.UpdateExecutionPhase(
                        AirPackageExecutionPhase.Executing);
                    continue;
                }

                if (package.RendezvousWaypoint == null)
                {
                    package.UpdateExecutionPhase(required.Any(flight =>
                            flight.IsAirborne)
                        ? AirPackageExecutionPhase.Pushing
                        : AirPackageExecutionPhase.Forming);
                    continue;
                }

                if (required.All(flight =>
                        flight.RendezvousState == AirRendezvousState.Released))
                {
                    ReleaseHoldingPackageFlights(package, currentTime);
                    package.UpdateExecutionPhase(
                        AirPackageExecutionPhase.Pushing);
                    continue;
                }

                if (required.Any(flight =>
                        flight.RendezvousState != AirRendezvousState.Holding))
                {
                    package.UpdateExecutionPhase(
                        AirPackageExecutionPhase.Forming);
                    continue;
                }

                package.UpdateExecutionPhase(AirPackageExecutionPhase.Ready);
                ReleaseHoldingPackageFlights(package, currentTime);
                package.UpdateExecutionPhase(AirPackageExecutionPhase.Pushing);
            }
        }

        private static void ReleaseHoldingPackageFlights(
            AirPackage package,
            DateTime currentTime)
        {
            foreach (var flight in package.Flights
                         .Where(flight => flight.RendezvousState
                                          == AirRendezvousState.Holding)
                         .OrderBy(flight => flight.FlightId))
            {
                flight.ReleaseRendezvous(
                    currentTime,
                    "The mission commander released the assembled package from rendezvous.");
            }
        }

        private void AbortPackageFlights(
            AirPackage package,
            DateTime currentTime,
            string reason)
        {
            foreach (var flight in package.Flights
                         .Where(flight => !flight.IsTerminal)
                         .OrderBy(flight => flight.FlightId))
            {
                if (!flight.IsAirborne)
                {
                    flight.Cancel(currentTime, reason);
                    continue;
                }

                if (!TryGetFlightContext(
                        flight,
                        out var squadron,
                        out var aircraftType))
                    continue;
                if (!TryAbortToImmediateRecovery(
                        package,
                        flight,
                        squadron,
                        aircraftType,
                        currentTime,
                        reason))
                {
                    flight.Cancel(currentTime, reason);
                }
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
                Flights = flights,
                AircraftTypes = aircraftTypes,
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
                CounterAirOwnerByProtectedFlightId =
                    new Dictionary<Guid, Guid>(),
                KnownSamThreatsByAlliance =
                    new Dictionary<Alliance, IReadOnlyList<KnownSamThreatEnvelope>>
                    {
                        { Alliance.Bluefor, GetKnownSamThreats(Alliance.Bluefor) },
                        { Alliance.Redfor, GetKnownSamThreats(Alliance.Redfor) }
                    }
            };
            frame.CounterAirOwnerByProtectedFlightId =
                BuildPackageCounterAirOwnership(frame);
            ApplyKnownSamEngagementOverrides(frame);
            ApplyPackageSeadProtection(frame);
            frame.BarcapTargetByFlightId = AirCombatRules.BuildBarcapAssignments(
                frame,
                ordnanceTypes,
                GetDoctrine);
            return frame;
        }

        private IReadOnlyDictionary<Guid, Guid>
            BuildPackageCounterAirOwnership(AirCombatFrame frame)
        {
            var ownership = new Dictionary<Guid, Guid>();
            foreach (var packageGroup in frame.Flights.Values
                         .Where(view => view?.Package != null)
                         .GroupBy(view => view.Package.PackageId)
                         .OrderBy(group => group.Key))
            {
                var packageFlights = packageGroup.ToDictionary(
                    view => view.Flight.FlightId);
                var escorts = packageGroup
                    .Where(IsViableCounterAirOwner)
                    .OrderBy(view => view.Flight.FlightId)
                    .ToList();
                foreach (var protectedFlight in packageGroup
                             .Where(view => !view.Flight.IsFighterEscort)
                             .OrderBy(view => view.Flight.FlightId))
                {
                    var owner = escorts
                        .Where(escort => escort.Flight.ProtectedFlightIds.Count == 0
                                         || escort.Flight.ProtectedFlightIds.Contains(
                                             protectedFlight.Flight.FlightId))
                        .OrderBy(escort => Vector3.Distance(
                            escort.Flight.PositionFeet,
                            protectedFlight.Flight.PositionFeet))
                        .ThenBy(escort => escort.Flight.FlightId)
                        .FirstOrDefault();
                    if (owner != null
                        && packageFlights.ContainsKey(owner.Flight.FlightId))
                    {
                        ownership[protectedFlight.Flight.FlightId] =
                            owner.Flight.FlightId;
                    }
                }
            }
            return ownership;
        }

        private bool IsViableCounterAirOwner(AirCombatFlightView view)
        {
            if (view?.Flight == null
                || !view.Flight.IsFighterEscort
                || view.Flight.LifecycleState
                != AirTaskingLifecycleState.Active
                || !view.Flight.IsAirborne
                || view.Flight.ExecutionPhase == FlightExecutionPhase.Returning
                || view.Flight.ExecutionPhase == FlightExecutionPhase.Landing
                || view.Flight.ExecutionPhase == FlightExecutionPhase.Ended
                || view.LiveAircraft == null
                || view.LiveAircraft.Count == 0)
                return false;

            return view.LiveAircraft
                .SelectMany(aircraft => aircraft.Loadout)
                .Where(item => item != null && item.Count > 0)
                .Any(item => ordnanceTypes.TryGetValue(
                                 item.OrdnanceTypeDefinitionId,
                                 out var ordnance)
                             && (ordnance.EmploymentCategory
                                 == OrdnanceEmploymentCategory.AirToAirRadar
                                 || ordnance.EmploymentCategory
                                 == OrdnanceEmploymentCategory.AirToAirInfrared
                                 || ordnance.EmploymentCategory
                                 == OrdnanceEmploymentCategory.Gun)
                             && ordnance.GetEffectiveness(
                                 OrdnanceTargetCategory.Aircraft) > 0f);
        }

        internal static void ApplyKnownSamEngagementOverrides(
            AirCombatFrame frame)
        {
            if (frame?.Flights == null)
                return;

            foreach (var source in frame.Flights.Values)
            {
                // Engagement permission belongs to the current tactical
                // situation, not the consumer's mission type. A coordinated
                // companion covers only its specifically authorized site and
                // only while its penetration/suppression protection is active.
                var coveredSiteIds = frame.Flights.Values
                    .Where(provider => provider != null
                                       && provider.Flight != null
                                       && provider.Flight.FlightId
                                       != source.Flight.FlightId
                                       && provider.Alliance == source.Alliance
                                       && provider.Package?.PackageId
                                       == source.Package?.PackageId
                                       && provider.Flight.LifecycleState
                                       == AirTaskingLifecycleState.Active
                                       && provider.Flight.IsAirborne
                                       && provider.Flight.ExecutionPhase
                                       != FlightExecutionPhase.Returning
                                       && provider.Flight.ExecutionPhase
                                       != FlightExecutionPhase.Landing
                                       && provider.LiveAircraft.Count > 0
                                       && provider.Flight
                                           .AuthorizedSurfaceThreatSiteId
                                       != Guid.Empty)
                    .Where(provider => provider.Flight
                                           .AuthorizedSurfaceThreatPenetrationGranted
                                       || frame.ActivePasses.Any(pass =>
                                           pass.SourceFlightId
                                           == provider.Flight.FlightId
                                           && pass.TargetKind
                                           == OrdnanceEmploymentTargetKind
                                               .AirDefenseComponent
                                           && pass.TargetSiteId
                                           == provider.Flight
                                               .AuthorizedSurfaceThreatSiteId)
                                       || frame.PendingEffects.Any(effect =>
                                           effect.SourceFlightId
                                           == provider.Flight.FlightId
                                           && effect.TargetKind
                                           == OrdnanceEmploymentTargetKind
                                               .AirDefenseComponent
                                           && effect.TargetSiteId
                                           == provider.Flight
                                               .AuthorizedSurfaceThreatSiteId
                                           && effect.ResolveAt > frame.Time))
                    .Select(provider => provider.Flight
                        .AuthorizedSurfaceThreatSiteId)
                    .Distinct()
                    .OrderBy(siteId => siteId)
                    .ToList();

                source.AllowKnownSamEngagementOverride =
                    coveredSiteIds.Count > 0;
                source.KnownSamEngagementOverrideSiteIds = coveredSiteIds;
            }
        }

        private void ApplyPackageSeadProtection(AirCombatFrame frame)
        {
            if (frame?.Flights == null)
                return;

            foreach (var view in frame.Flights.Values)
                view.HasPackageSeadProtection = false;

            foreach (var provider in frame.Flights.Values
                         .Where(view => CanProvidePackageSeadProtection(
                             view,
                             frame))
                         .OrderBy(view => view.Flight.FlightId))
            {
                var protectedFlights = GetProtectedFlightViews(
                    provider,
                    frame,
                    includeReturning: false);
                if (protectedFlights.Count == 0)
                    continue;

                foreach (var packageView in frame.Flights.Values.Where(view =>
                             view?.Flight != null
                             && view.Package?.PackageId
                             == provider.Package?.PackageId
                             && view.Alliance == provider.Alliance
                             && view.Flight.IsAirborne
                             && view.Flight.HasPackageRelease
                             && view.Flight.ExecutionPhase
                             != FlightExecutionPhase.Returning
                             && view.Flight.ExecutionPhase
                             != FlightExecutionPhase.Landing
                             && view.Flight.ExecutionPhase
                             != FlightExecutionPhase.Ended))
                {
                    packageView.HasPackageSeadProtection = true;
                }
            }
        }

        private void ProcessSeadEscorts(
            AirCombatFrame frame,
            DateTime currentTime)
        {
            if (frame?.Flights == null)
                return;

            foreach (var provider in frame.Flights.Values
                         .Where(view => IsAvailableSeadEscort(view))
                         .OrderBy(view => view.Flight.FlightId))
            {
                var flight = provider.Flight;
                if (flight.TacticalState.Intent == AirCombatIntent.Defend
                    || wvrEngagementSystem.IsFlightEngaged(flight.FlightId)
                    || !flight.CanEvaluateGroundAttackOpportunity(currentTime))
                    continue;

                var protectedFlights = GetProtectedFlightViews(
                    provider,
                    frame,
                    includeReturning: false);
                if (protectedFlights.Count == 0)
                    continue;

                ConfirmPermanentlyClearedSeadThreats(
                    provider,
                    protectedFlights,
                    frame,
                    currentTime);

                flight.ConsumeGroundAttackOpportunity(
                    currentTime,
                    SeadOpportunityRetrySeconds);
                var emitter = DetectSeadEmitters(
                        provider,
                        protectedFlights,
                        frame,
                        currentTime)
                    .OrderBy(contact => contact.ThreatPriority)
                    .ThenBy(contact =>
                        contact.NearestProtectedFlightDistanceFeet)
                    .ThenBy(contact => contact.SiteId)
                    .ThenBy(contact => contact.RadarComponentId)
                    .FirstOrDefault();
                if (emitter == null
                    || !gameManager.airDefenseSiteSystem.TryGetSite(
                        emitter.SiteId,
                        out var site)
                    || !gameManager.airDefenseSiteSystem.TryGetTileId(
                        site,
                        out var siteTileId))
                    continue;

                var radar = site.Components
                    .OfType<RadarAirDefenseComponent>()
                    .FirstOrDefault(component =>
                        component.ComponentId == emitter.RadarComponentId);
                if (radar == null)
                    continue;

                var opportunity = groundAttackOpportunityService
                    .CreateSeadEmitterOpportunity(
                        emitter,
                        site,
                        radar,
                        siteTileId,
                        currentTime);
                var sourceAircraft = provider.Squadron.Aircraft
                    .Where(aircraft => aircraft.AssignedFlightId
                                       == flight.FlightId
                                       && aircraft.Status
                                       != CampaignAircraftStatus.Lost
                                       && aircraft.Status
                                       != CampaignAircraftStatus.Damaged)
                    .ToList();
                if (!groundAttackDecisionService.TryPlan(
                        opportunity,
                        sourceAircraft,
                        ordnanceTypes,
                        (_, ordnance) => IsAntiRadiationOrdnance(ordnance),
                        out var passPlan))
                    continue;

                ordnanceEmploymentSystem.TryStartGroundAttackPass(
                    flight.FlightId,
                    passPlan,
                    currentTime);
            }
        }

        private void ConfirmPermanentlyClearedSeadThreats(
            AirCombatFlightView provider,
            IReadOnlyCollection<AirCombatFlightView> protectedFlights,
            AirCombatFrame frame,
            DateTime currentTime)
        {
            var clearedAny = false;
            var relevantSiteIds = frame.GetKnownSamThreats(provider.Alliance)
                .Where(threat => threat != null
                                 && protectedFlights.Any(view =>
                                     TryGetRouteThreatWindow(
                                         threat,
                                         view,
                                         frame.Time,
                                         out _)))
                .Select(threat => threat.SiteId)
                .Distinct()
                .OrderBy(siteId => siteId);
            foreach (var siteId in relevantSiteIds)
            {
                if (!gameManager.airDefenseSiteSystem.TryGetSite(
                        siteId,
                        out var site)
                    || HasPermanentSamShooterChain(site))
                    continue;

                foreach (var protectedView in protectedFlights)
                {
                    clearedAny |= protectedView.Flight
                        .ConfirmSurfaceThreatCleared(
                            siteId,
                            currentTime,
                            "SEAD confirmed that the site's functional shooter chain was permanently broken.");
                }
            }
            if (clearedAny)
                knownSamThreatCache.Clear();
        }

        private IReadOnlyList<DetectedEmitter> DetectSeadEmitters(
            AirCombatFlightView provider,
            IReadOnlyCollection<AirCombatFlightView> protectedFlights,
            AirCombatFrame frame,
            DateTime currentTime)
        {
            var contacts = new List<DetectedEmitter>();
            var weapons = GetAvailableAntiRadiationInventory(provider, frame)
                .Where(entry => entry.Value > 0
                                && ordnanceTypes.ContainsKey(entry.Key))
                .Select(entry => ordnanceTypes[entry.Key])
                .ToList();
            if (weapons.Count == 0)
                return contacts;

            var threatGroups = frame.GetKnownSamThreats(provider.Alliance)
                .Where(threat => threat != null
                                 && protectedFlights.Any(protectedView =>
                                     TryGetRouteThreatWindow(
                                         threat,
                                         protectedView,
                                         frame.Time,
                                         out _)))
                .GroupBy(threat => threat.SiteId)
                .OrderBy(group => group.Key);
            foreach (var threatGroup in threatGroups)
            {
                if (!gameManager.airDefenseSiteSystem.TryGetSite(
                        threatGroup.Key,
                        out var site)
                    || site.IsDisabled
                    || site.IsDestroyed
                    || site.IsSuppressed
                    || gameManager.airDefenseSiteSystem.GetEffectiveAlliance(site)
                    == provider.Alliance
                    || !gameManager.airDefenseSiteSystem.TryGetPositionFeet(
                        site,
                        out var sitePosition))
                    continue;

                if (!TryGetEarliestRouteThreatWindow(
                        threatGroup,
                        protectedFlights,
                        frame.Time,
                        out var earliestThreatWindow))
                    continue;

                var threatenedFlights = protectedFlights
                    .Where(view => threatGroup.Any(threat =>
                        TryGetRouteThreatWindow(
                            threat,
                            view,
                            frame.Time,
                            out _)))
                    .Select(view => view.Flight.FlightId)
                    .OrderBy(flightId => flightId)
                    .ToList();
                foreach (var radar in site.Components
                             .OfType<RadarAirDefenseComponent>()
                             .Where(component => component.IsEmitting
                                                 && !component.IsDamaged)
                             .OrderBy(component => component.ComponentId))
                {
                    if (ordnanceEmploymentSystem.HasActiveOrPendingEffect(
                            radar.ComponentId)
                        || !airDefenseComponentDefinitions.TryGetValue(
                            radar.SamComponentDefinitionId,
                            out var componentDefinition)
                        || weapons.All(ordnance =>
                            !CanEmploySeadWeapon(
                                provider,
                                ordnance,
                                componentDefinition,
                                sitePosition)
                            || !IsSeadEngagementDue(
                                provider,
                                ordnance,
                                sitePosition,
                                earliestThreatWindow,
                                currentTime)))
                        continue;

                    contacts.Add(new DetectedEmitter
                    {
                        SiteId = site.SiteId,
                        RadarComponentId = radar.ComponentId,
                        PositionFeet = sitePosition,
                        DetectedAt = currentTime,
                        ThreatPriority = GetSeadEmitterThreatPriority(
                            site.SiteId,
                            radar.ComponentId,
                            componentDefinition,
                            provider.Alliance,
                            threatenedFlights,
                            frame),
                        NearestProtectedFlightDistanceFeet = protectedFlights
                            .Min(view => Vector3.Distance(
                                view.Flight.PositionFeet,
                                sitePosition)),
                        ThreatenedFlightIds = threatenedFlights
                    });
                }
            }
            return contacts;
        }

        private static bool TryGetEarliestRouteThreatWindow(
            IEnumerable<KnownSamThreatEnvelope> threats,
            IEnumerable<AirCombatFlightView> protectedFlights,
            DateTime currentTime,
            out RouteThreatWindow earliestWindow)
        {
            earliestWindow = default;
            var found = false;
            foreach (var threat in threats.Where(candidate => candidate != null))
            {
                foreach (var protectedFlight in protectedFlights.Where(
                             candidate => candidate?.Flight != null))
                {
                    if (!TryGetRouteThreatWindow(
                            threat,
                            protectedFlight,
                            currentTime,
                            out var candidateWindow))
                        continue;
                    if (found
                        && candidateWindow.EntryTime
                        >= earliestWindow.EntryTime)
                        continue;

                    earliestWindow = candidateWindow;
                    found = true;
                }
            }
            return found;
        }

        private static bool IsSeadEngagementDue(
            AirCombatFlightView provider,
            OrdnanceTypeDefinition ordnance,
            Vector3 sitePosition,
            RouteThreatWindow threatWindow,
            DateTime currentTime)
        {
            if (threatWindow.AlreadyInside)
                return true;

            var distanceKm = HorizontalDistanceKm(
                provider.Flight.PositionFeet,
                sitePosition);
            var preparationSeconds = ordnance.PreparationSeconds
                                     / Math.Max(
                                         0.01f,
                                         provider.AircraftType
                                             .OrdnanceEmploymentEfficiency);
            var effectTravelSeconds = distanceKm
                                      / Math.Max(
                                          1f,
                                          ordnance.EffectSpeedKnots * 1.852f)
                                      * 3600d;
            var latestUsefulStart = currentTime.AddSeconds(
                preparationSeconds
                + effectTravelSeconds
                + SeadTimingSafetyMarginSeconds);
            return threatWindow.EntryTime <= latestUsefulStart;
        }

        private void ApplySeadScreenCommands(
            IEnumerable<AirCombatCommand> commands,
            AirCombatFrame frame)
        {
            if (commands == null || frame?.Flights == null)
                return;

            foreach (var command in commands.OrderBy(item => item.FlightId))
            {
                if (!frame.Flights.TryGetValue(command.FlightId, out var provider)
                    || !IsAvailableSeadEscort(provider)
                    || command.Intent != AirCombatIntent.FollowMission
                    || command.Maneuver != AirCombatManeuver.FollowRoute
                    || command.RequestsSurfaceThreatRecovery
                    || command.RequestsWvrEngagement
                    || command.Employment != null
                    || wvrEngagementSystem.IsFlightEngaged(command.FlightId))
                    continue;

                var protectedFlights = GetProtectedFlightViews(
                    provider,
                    frame,
                    includeReturning: false);
                if (protectedFlights.Count == 0)
                    continue;

                var screenWeaponIds = GetAvailableAntiRadiationInventory(
                            provider,
                            frame)
                        .Where(entry => entry.Value > 0)
                        .Select(entry => entry.Key)
                    .Concat(frame.ActivePasses
                        .Where(pass => pass.SourceFlightId
                                       == provider.Flight.FlightId
                                       && IsAntiRadiationOrdnance(
                                           pass.OrdnanceTypeDefinitionId))
                        .Select(pass => pass.OrdnanceTypeDefinitionId))
                    .Concat(frame.PendingEffects
                        .Where(effect => effect.SourceFlightId
                                         == provider.Flight.FlightId
                                         && effect.ResolveAt > frame.Time
                                         && IsAntiRadiationOrdnance(
                                             effect.OrdnanceTypeDefinitionId))
                        .Select(effect => effect.OrdnanceTypeDefinitionId))
                    .Distinct()
                    .ToList();
                var maximumHarmRangeKm = screenWeaponIds
                    .Where(ordnanceTypes.ContainsKey)
                    .Select(ordnanceId =>
                        ordnanceTypes[ordnanceId].MaximumRangeKm)
                    .DefaultIfEmpty(0f)
                    .Max();
                if (maximumHarmRangeKm <= 0f)
                    continue;

                var protectedCenter = new Vector3(
                    protectedFlights.Average(view => view.Flight.PositionFeet.x),
                    protectedFlights.Average(view => view.Flight.PositionFeet.y),
                    protectedFlights.Average(view => view.Flight.PositionFeet.z));
                var forward = protectedFlights
                    .Select(view => AirCombatRules.Direction(
                        view.Flight.HeadingDegrees))
                    .Aggregate(Vector3.zero, (sum, direction) => sum + direction);
                if (forward.sqrMagnitude < 0.01f)
                {
                    forward = AirCombatRules.Direction(
                        protectedFlights.First().Flight.HeadingDegrees);
                }
                forward.Normalize();
                var lateralSign = (provider.Flight.FlightId.ToByteArray()[0]
                                   & 1) == 0
                    ? -1f
                    : 1f;
                var lateral = new Vector3(-forward.z, 0f, forward.x)
                              * lateralSign;
                var leadKm = Mathf.Clamp(
                    maximumHarmRangeKm * 0.2f,
                    SeadMinimumScreenLeadKm,
                    SeadMaximumScreenLeadKm);
                var aimPoint = protectedCenter
                               + forward * leadKm
                               * AirspaceGeometry.FeetPerKilometer
                               + lateral * SeadScreenLateralOffsetKm
                               * AirspaceGeometry.FeetPerKilometer;
                aimPoint.y = Math.Min(
                    provider.AircraftType.ServiceCeilingFeet,
                    protectedCenter.y + 5000f);

                command.Maneuver = AirCombatManeuver.SeadScreen;
                command.AimPointFeet = aimPoint;
                command.HasAimPoint = true;
                command.DesiredSpeedKnots = Math.Max(
                    1f,
                    provider.AircraftType.CombatSpeedKnots);
                command.MinimumManeuverEndAt = frame.Time.AddSeconds(
                    TacticalDecisionStepSeconds);
                command.Reason =
                    $"Maintaining a forward SEAD screen for {protectedFlights.Count} protected flight(s).";
            }
        }

        private void ApplyPackageAbortDecisions(
            IReadOnlyCollection<AirCombatCommand> commands,
            AirCombatFrame frame,
            DateTime currentTime)
        {
            if (commands == null || frame?.Flights == null)
                return;

            var abortedPackageIds = new HashSet<Guid>();
            foreach (var packageGroup in frame.Flights.Values
                         .Where(view => view?.Package != null)
                         .GroupBy(view => view.Package.PackageId)
                         .OrderBy(group => group.Key))
            {
                var seadFlights = packageGroup
                    .Where(view => view.Flight.IsSeadEscort)
                    .ToList();
                if (seadFlights.Count == 0)
                    continue;

                var protectedFlightIds = seadFlights
                    .SelectMany(view => view.Flight.ProtectedFlightIds)
                    .ToHashSet();
                var protectionStillRequired = packageGroup.Any(view =>
                    protectedFlightIds.Contains(view.Flight.FlightId)
                    && view.Flight.LifecycleState
                    == AirTaskingLifecycleState.Active
                    && view.Flight.IsAirborne
                    && view.Flight.HasPackageRelease
                    && view.Flight.ExecutionPhase
                    != FlightExecutionPhase.Returning
                    && view.Flight.ExecutionPhase
                    != FlightExecutionPhase.Landing
                    && view.Flight.ExecutionPhase
                    != FlightExecutionPhase.Ended);
                if (!protectionStillRequired
                    || seadFlights.Any(view =>
                        CanProvidePackageSeadProtection(view, frame)))
                    continue;

                var package = packageGroup.First().Package;
                var reason = "Mission commander aborted the package because "
                             + "the assigned SEAD element can no longer protect "
                             + "the package.";
                package.UpdateExecutionPhase(
                    AirPackageExecutionPhase.Aborted);
                AbortPackageFlights(package, currentTime, reason);
                abortedPackageIds.Add(package.PackageId);
                SetPackageRecoveryCommands(
                    package,
                    commands,
                    frame,
                    currentTime);
            }

            var recoveryRequests = commands
                .Where(command => command.RequestsSurfaceThreatRecovery)
                .Select(command => frame.Flights.TryGetValue(
                    command.FlightId,
                    out var view)
                    ? view
                    : null)
                .Where(view => view?.Package != null
                               && !abortedPackageIds.Contains(
                                   view.Package.PackageId))
                .GroupBy(view => view.Package.PackageId)
                .OrderBy(group => group.Key);
            foreach (var requestGroup in recoveryRequests)
            {
                var package = requestGroup.First().Package;
                var primaryBlocked = requestGroup.Any(view =>
                    view.Flight.IsGroundAttackFlight);
                if (!primaryBlocked)
                    continue;

                var trigger = requestGroup
                    .OrderBy(view => view.Flight.FlightId)
                    .First();
                var triggerCommand = commands.First(command =>
                    command.FlightId == trigger.Flight.FlightId);
                var reason = "Mission commander aborted the package because "
                             + "the primary attack route has no SEAD protection. "
                             + triggerCommand.Reason;
                package.UpdateExecutionPhase(
                    AirPackageExecutionPhase.Aborted);
                AbortPackageFlights(package, currentTime, reason);

                SetPackageRecoveryCommands(
                    package,
                    commands,
                    frame,
                    currentTime);
            }
        }

        private static void SetPackageRecoveryCommands(
            AirPackage package,
            IReadOnlyCollection<AirCombatCommand> commands,
            AirCombatFrame frame,
            DateTime currentTime)
        {
            foreach (var view in frame.Flights.Values
                         .Where(view => view.Package?.PackageId
                                        == package.PackageId)
                         .OrderBy(view => view.Flight.FlightId))
            {
                var command = commands.FirstOrDefault(candidate =>
                    candidate.FlightId == view.Flight.FlightId);
                if (command != null
                    && (view.Flight.ExecutionPhase
                        == FlightExecutionPhase.Returning
                        || view.Flight.ExecutionPhase
                        == FlightExecutionPhase.Landing))
                {
                    ContinueRecoveryCommand(
                        command,
                        view.Flight,
                        view.AircraftType,
                        currentTime);
                }
            }
        }

        private static bool IsAvailableSeadEscort(AirCombatFlightView view)
        {
            return view?.Flight != null
                   && view.Flight.IsSeadEscort
                   && view.Flight.LifecycleState
                   == AirTaskingLifecycleState.Active
                   && view.Flight.IsAirborne
                   && view.Flight.HasPackageRelease
                   && view.Flight.ExecutionPhase
                   != FlightExecutionPhase.Returning
                   && view.Flight.ExecutionPhase
                   != FlightExecutionPhase.Landing
                   && view.Flight.ExecutionPhase
                   != FlightExecutionPhase.Ended
                   && view.LiveAircraft != null
                   && view.LiveAircraft.Count > 0;
        }

        private bool CanProvidePackageSeadProtection(
            AirCombatFlightView view,
            AirCombatFrame frame)
        {
            if (!IsAvailableSeadEscort(view))
                return false;

            if (GetAvailableAntiRadiationInventory(view, frame)
                .Any(entry => entry.Value > 0))
                return true;

            return frame.ActivePasses.Any(pass =>
                       pass.SourceFlightId == view.Flight.FlightId
                       && IsAntiRadiationOrdnance(
                           pass.OrdnanceTypeDefinitionId))
                   || frame.PendingEffects.Any(effect =>
                       effect.SourceFlightId == view.Flight.FlightId
                       && effect.ResolveAt > frame.Time
                       && IsAntiRadiationOrdnance(
                           effect.OrdnanceTypeDefinitionId));
        }

        private static List<AirCombatFlightView> GetProtectedFlightViews(
            AirCombatFlightView provider,
            AirCombatFrame frame,
            bool includeReturning)
        {
            var protectedIds = provider.Flight.ProtectedFlightIds.ToHashSet();
            return frame.Flights.Values
                .Where(view => view?.Flight != null
                               && protectedIds.Contains(view.Flight.FlightId)
                               && view.Alliance == provider.Alliance
                               && view.Package?.PackageId
                               == provider.Package?.PackageId
                               && view.Flight.IsAirborne
                               && view.Flight.ExecutionPhase
                               != FlightExecutionPhase.Landing
                               && view.Flight.ExecutionPhase
                               != FlightExecutionPhase.Ended
                               && (includeReturning
                                   || view.Flight.ExecutionPhase
                                   != FlightExecutionPhase.Returning))
                .OrderBy(view => view.Flight.FlightId)
                .ToList();
        }

        private static bool TryGetRouteThreatWindow(
            KnownSamThreatEnvelope threat,
            AirCombatFlightView flightView,
            DateTime currentTime,
            out RouteThreatWindow window)
        {
            window = default;
            var flight = flightView?.Flight;
            if (threat == null
                || flight == null
                || !flight.IsAirborne
                || !flight.HasPosition)
                return false;

            var speedKnots = Math.Max(
                1f,
                flight.SpeedKnots > 1f
                    ? flight.SpeedKnots
                    : Math.Max(
                        flightView.AircraftType.CruiseSpeedKnots,
                        flightView.AircraftType.CombatSpeedKnots));
            var segmentStart = flight.PositionFeet;
            var segmentStartTime = currentTime;
            var found = false;
            var entryTime = default(DateTime);
            var exitTime = default(DateTime);
            var entryPoint = default(Vector3);
            var exitPoint = default(Vector3);
            var alreadyInside = threat.Contains(segmentStart);

            foreach (var waypoint in flight.Route
                         .Skip(Math.Max(0, flight.CurrentWaypointIndex))
                         .Where(candidate => candidate != null))
            {
                var segmentEnd = waypoint.PositionFeet;
                var distanceFeet = Vector3.Distance(segmentStart, segmentEnd);
                var durationSeconds = distanceFeet
                                      / AirspaceGeometry.FeetPerNauticalMile
                                      / speedKnots
                                      * 3600d;
                if (threat.TryGetSegmentIntersectionInterval(
                        segmentStart,
                        segmentEnd,
                        out var entryParameter,
                        out var exitParameter))
                {
                    var candidateEntry = segmentStartTime.AddSeconds(
                        durationSeconds * entryParameter);
                    var candidateExit = segmentStartTime.AddSeconds(
                        durationSeconds * exitParameter);
                    if (!found)
                    {
                        found = true;
                        entryTime = alreadyInside
                            ? currentTime
                            : candidateEntry;
                        entryPoint = Vector3.Lerp(
                            segmentStart,
                            segmentEnd,
                            entryParameter);
                    }

                    exitTime = candidateExit;
                    exitPoint = Vector3.Lerp(
                        segmentStart,
                        segmentEnd,
                        exitParameter);
                }

                segmentStart = segmentEnd;
                segmentStartTime = segmentStartTime.AddSeconds(durationSeconds);
                if (waypoint.Action == AirWaypointAction.ReturnToBase)
                    break;
            }

            if (!found)
                return false;

            window = new RouteThreatWindow(
                threat.SiteId,
                entryTime,
                exitTime,
                entryPoint,
                exitPoint,
                alreadyInside);
            return true;
        }

        private Dictionary<Guid, int> GetAvailableAntiRadiationInventory(
            AirCombatFlightView provider,
            AirCombatFrame frame)
        {
            var inventory = provider.LiveAircraft
                .SelectMany(aircraft => aircraft.Loadout)
                .Where(item => item != null
                               && item.Count > 0
                               && IsAntiRadiationOrdnance(
                                   item.OrdnanceTypeDefinitionId))
                .GroupBy(item => item.OrdnanceTypeDefinitionId)
                .ToDictionary(group => group.Key, group => group.Sum(item =>
                    item.Count));
            foreach (var pass in frame.ActivePasses.Where(pass =>
                         pass.SourceFlightId == provider.Flight.FlightId
                         && IsAntiRadiationOrdnance(
                             pass.OrdnanceTypeDefinitionId)))
            {
                if (inventory.TryGetValue(
                        pass.OrdnanceTypeDefinitionId,
                        out var available))
                {
                    inventory[pass.OrdnanceTypeDefinitionId] = Math.Max(
                        0,
                        available - Math.Max(1, pass.PlannedQuantity));
                }
            }
            return inventory;
        }

        private bool CanEmploySeadWeapon(
            AirCombatFlightView provider,
            OrdnanceTypeDefinition ordnance,
            AirDefenseComponentDefinition componentDefinition,
            Vector3 targetPosition)
        {
            if (!IsAntiRadiationOrdnance(ordnance)
                || !DeadLoadoutPlanner.CanAttackComponent(
                    ordnance,
                    componentDefinition))
                return false;

            var distanceKm = HorizontalDistanceKm(
                provider.Flight.PositionFeet,
                targetPosition);
            return distanceKm >= ordnance.MinimumRangeKm
                   && distanceKm <= ordnance.MaximumRangeKm;
        }

        private bool IsAntiRadiationOrdnance(Guid ordnanceId)
        {
            return ordnanceTypes.TryGetValue(ordnanceId, out var ordnance)
                   && IsAntiRadiationOrdnance(ordnance);
        }

        private static bool IsAntiRadiationOrdnance(
            OrdnanceTypeDefinition ordnance)
        {
            return ordnance != null
                   && (ordnance.EmploymentCategory
                       == OrdnanceEmploymentCategory.AntiRadiation
                       || ordnance.GuidanceMode
                       == OrdnanceGuidanceMode.AntiRadiation);
        }

        private int GetSeadEmitterThreatPriority(
            Guid siteId,
            Guid radarComponentId,
            AirDefenseComponentDefinition definition,
            Alliance friendlyAlliance,
            IEnumerable<Guid> protectedFlightIds,
            AirCombatFrame frame)
        {
            var protectedIds = protectedFlightIds.ToHashSet();
            if (frame.PendingEffects.Any(effect =>
                    effect.SourceKind == OrdnanceEmploymentSourceKind.SamLauncher
                    && effect.SourceSiteId == siteId
                    && effect.SupportSourceComponentId == radarComponentId
                    && protectedIds.Contains(effect.TargetFlightId)
                    && effect.ResolveAt > frame.Time))
                return 0;

            var hostileAlliance = friendlyAlliance == Alliance.Bluefor
                ? Alliance.Redfor
                : Alliance.Bluefor;
            if (gameManager.GetAllianceIADS(hostileAlliance)
                    ?.CurrentEngagementAssignments.Any(assignment =>
                        assignment.SiteId == siteId
                        && assignment.FireControlRadarComponentId
                        == radarComponentId
                        && protectedIds.Contains(
                            assignment.TargetFlightId)) == true)
                return 1;

            return definition is RadarAirDefenseComponentDefinition
                   {
                       ProvidesWeaponQualityTrack: true
                   }
                ? 2
                : 3;
        }

        private IReadOnlyList<KnownSamThreatEnvelope> GetKnownSamThreats(
            Alliance alliance)
        {
            if (knownSamThreatCache.TryGetValue(alliance, out var cached))
                return cached;

            var threats = knownSamThreatAssessment.BuildKnownThreats(
                gameManager.intelligenceSystem?.GetPicture(alliance));
            knownSamThreatCache[alliance] = threats;
            return threats;
        }

        private IReadOnlyList<KnownSamThreatEnvelope> RefreshKnownSamThreats(
            Alliance alliance)
        {
            var threats = knownSamThreatAssessment.BuildKnownThreats(
                gameManager.intelligenceSystem?.GetPicture(alliance));
            knownSamThreatCache[alliance] = threats;
            return threats;
        }

        private bool TryCancelUnsafeBarcapBeforeTakeoff(
            AirPackage package,
            AirFlight flight,
            AircraftTypeDefinition aircraftType,
            DateTime currentTime)
        {
            if (package == null
                || flight?.TaskType
                != AirFlightTaskType.Barcap
                || flight.IsFighterEscort
                || aircraftType == null
                || package.Flights.Any(candidate => candidate.IsAirborne))
                return false;

            var threats = RefreshKnownSamThreats(package.Alliance);
            var maneuverClearanceFeet = AirspaceGeometry
                .ConservativeSamManeuverClearanceFeet(aircraftType);
            var coverage = flight.PlannedBarcapCoverage;
            var commander = airTaskingSystem.GetCommander(package.Alliance);
            var planningAgeMinutes = Math.Max(
                0d,
                (currentTime - package.CreatedAt).TotalMinutes);
            var plannedKnownSites = (coverage?.PlannedKnownSamSiteIds
                                     ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .ToHashSet();
            var values = new Dictionary<string, float>
            {
                { "planningAgeMinutes", (float)planningAgeMinutes },
                { "currentKnownSamSiteCount", threats.Select(threat => threat.SiteId).Distinct().Count() },
                { "plannedKnownSamSiteCount", plannedKnownSites.Count },
                { "routeWaypointCount", flight.Route.Count }
            };
            AddBarcapStationDiagnosticValues(values, coverage);

            if (!TryFindFirstKnownSamRouteConflict(
                    flight.Route,
                    threats,
                    maneuverClearanceFeet,
                    out var blockingSiteId,
                    out var fromIndex,
                    out var toIndex))
            {
                commander?.AddDiagnostic(new AirTaskingDiagnostic
                {
                    RecordedAt = currentTime,
                    PlanId = package.PlanId,
                    PackageId = package.PackageId,
                    Code = "barcap-preflight-sam-clear",
                    Message = "Current-intelligence BARCAP route revalidation passed "
                              + $"{planningAgeMinutes:0.0} minutes after commitment.",
                    Values = values
                });
                return false;
            }

            var absentFromPlanningThreatSet =
                !plannedKnownSites.Contains(blockingSiteId);
            values["blockingSamAbsentFromPlanningThreatSet"] =
                absentFromPlanningThreatSet ? 1f : 0f;
            values["blockingRouteFromIndex"] = fromIndex;
            values["blockingRouteToIndex"] = toIndex;
            var conflict = DescribeSamRouteConflict(
                flight.Route,
                blockingSiteId,
                fromIndex,
                toIndex);
            var reason = "BARCAP cancelled before takeoff because current SAM "
                         + $"intelligence invalidated its committed route: {conflict}; "
                         + $"station={FormatAirPosition(coverage?.StationCenterFeet ?? Vector3.zero)}; "
                         + $"planningAge={planningAgeMinutes:0.0}min; "
                         + "blockingSamWasInPlanningThreatSet="
                         + $"{!absentFromPlanningThreatSet}. "
                         + "The explicit plan will not be retried automatically.";
            commander?.AddDiagnostic(new AirTaskingDiagnostic
            {
                RecordedAt = currentTime,
                PlanId = package.PlanId,
                PackageId = package.PackageId,
                Code = "barcap-preflight-sam-blocked",
                Message = reason,
                Values = values
            });
            if (!airTaskingSystem.CancelPackage(
                    package.Alliance,
                    package.PackageId,
                    currentTime,
                    reason))
            {
                throw new InvalidOperationException(
                    $"Unsafe BARCAP package {package.PackageId} could not be "
                    + "cancelled before takeoff.");
            }

            return true;
        }

        private static bool TryFindFirstKnownSamRouteConflict(
            IReadOnlyList<AirWaypoint> route,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            float maneuverClearanceFeet,
            out Guid blockingSiteId,
            out int fromIndex,
            out int toIndex)
        {
            blockingSiteId = Guid.Empty;
            fromIndex = -1;
            toIndex = -1;
            if (route == null || route.Count == 0 || threats == null)
                return false;

            for (var index = 0; index < route.Count; index++)
            {
                var pointThreat = threats
                    .Where(threat => threat != null
                                     && threat.Contains(
                                         route[index].PositionFeet,
                                         maneuverClearanceFeet))
                    .OrderBy(threat => threat.SiteId)
                    .FirstOrDefault();
                if (pointThreat != null)
                {
                    blockingSiteId = pointThreat.SiteId;
                    fromIndex = index;
                    toIndex = index;
                    return true;
                }

                if (index + 1 >= route.Count)
                    continue;
                var segmentThreat = threats
                    .Where(threat => threat != null
                                     && threat.IntersectsSegment(
                                         route[index].PositionFeet,
                                         route[index + 1].PositionFeet,
                                         maneuverClearanceFeet))
                    .OrderBy(threat => threat.SiteId)
                    .FirstOrDefault();
                if (segmentThreat == null)
                    continue;

                blockingSiteId = segmentThreat.SiteId;
                fromIndex = index;
                toIndex = index + 1;
                return true;
            }

            foreach (var endpoint in route
                         .Select((waypoint, index) => new { Waypoint = waypoint, Index = index })
                         .Where(item => item.Waypoint.HasRepeat))
            {
                var repeatIndex = route.ToList().FindIndex(candidate =>
                    candidate.WaypointId == endpoint.Waypoint.RepeatFromWaypointId);
                if (repeatIndex < 0)
                    continue;
                var repeatThreat = threats
                    .Where(threat => threat != null
                                     && threat.IntersectsSegment(
                                         endpoint.Waypoint.PositionFeet,
                                         route[repeatIndex].PositionFeet,
                                         maneuverClearanceFeet))
                    .OrderBy(threat => threat.SiteId)
                    .FirstOrDefault();
                if (repeatThreat == null)
                    continue;
                blockingSiteId = repeatThreat.SiteId;
                fromIndex = endpoint.Index;
                toIndex = repeatIndex;
                return true;
            }

            return false;
        }

        private static string DescribeSamRouteConflict(
            IReadOnlyList<AirWaypoint> route,
            Guid siteId,
            int fromIndex,
            int toIndex)
        {
            if (route == null
                || fromIndex < 0
                || fromIndex >= route.Count
                || toIndex < 0
                || toIndex >= route.Count)
                return $"site={SimLogNames.ShortId(siteId)} routeLeg=unknown";

            var from = route[fromIndex];
            if (fromIndex == toIndex)
            {
                return $"site={SimLogNames.ShortId(siteId)} waypoint={fromIndex}:"
                       + $"{from.Action} position={FormatAirPosition(from.PositionFeet)}";
            }

            var to = route[toIndex];
            return $"site={SimLogNames.ShortId(siteId)} leg={fromIndex}->{toIndex}:"
                   + $"{from.Action}->{to.Action} "
                   + $"from={FormatAirPosition(from.PositionFeet)} "
                   + $"to={FormatAirPosition(to.PositionFeet)}";
        }

        private static void AddBarcapStationDiagnosticValues(
            IDictionary<string, float> values,
            BarcapStationCoverage coverage)
        {
            if (values == null || coverage == null)
                return;
            values["barcapStationXFeet"] = coverage.StationCenterFeet.x;
            values["barcapStationAltitudeFeet"] = coverage.StationCenterFeet.y;
            values["barcapStationZFeet"] = coverage.StationCenterFeet.z;
            values["barcapStationHeadingDegrees"] =
                coverage.StationHeadingDegrees;
            values["barcapCoveredTileCount"] =
                coverage.CoveredBarrierTileIds?.Count ?? 0;
            values["barcapTrackHalfLengthKm"] =
                coverage.StationTrackHalfLengthKm;
            values["barcapInterceptSlackKm"] =
                coverage.PlannedMinimumInterceptSlackKm;
        }

        private static string FormatAirPosition(Vector3 positionFeet)
        {
            return $"({positionFeet.x / AirspaceGeometry.FeetPerKilometer:0.0}km,"
                   + $"{positionFeet.z / AirspaceGeometry.FeetPerKilometer:0.0}km,"
                   + $"{positionFeet.y:0}ft)";
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
                || command.RequestsWvrEngagement
                || wvrEngagementSystem.IsFlightEngaged(flight.FlightId)
                || !TryGetFlightContext(
                    flight,
                    out var squadron,
                    out var aircraftType))
                return;
            flightMovementSystem.Advance(
                package,
                flight,
                squadron,
                aircraftType,
                command,
                intervalStart,
                elapsedSeconds);
        }

        private void ResolveDamageRecovery(DateTime currentTime)
        {
            foreach (var package in airTaskingSystem.GetPackages()
                         .OrderBy(candidate => candidate.PackageId))
            {
                if (package.Flights.All(flight =>
                        flight.ExecutionPhase
                        == FlightExecutionPhase.AwaitingTakeoff)
                    && (TryGetDeadPreflightInvalidationReason(
                            package,
                            out var preflightInvalidationReason)
                        || TryGetStrikePreflightInvalidationReason(
                            package,
                            out preflightInvalidationReason)))
                {
                    airTaskingSystem.CancelPackage(
                        package.Alliance,
                        package.PackageId,
                        currentTime,
                        preflightInvalidationReason);
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
                FlightMovementSystem.BurnFuel(
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

        private void ProcessMissionBehaviors(DateTime currentTime)
        {
            foreach (var behavior in missionBehaviors)
                behavior.Process(currentTime);
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

        private bool TryRelocateBarcapStation(
            AirPackage package,
            AirFlight flight,
            Squadron squadron,
            AircraftTypeDefinition aircraftType,
            DateTime currentTime,
            string triggerReason,
            out string reason)
        {
            reason = string.Empty;
            if (package == null
                || flight?.TaskType
                != AirFlightTaskType.Barcap
                || flight.LifecycleState != AirTaskingLifecycleState.Active
                || flight.ExecutionPhase == FlightExecutionPhase.Returning
                || flight.ExecutionPhase == FlightExecutionPhase.Landing
                || aircraftType == null
                || !TryGetAssignedBarcapStation(
                    flight,
                    out var assignedStationCenterFeet))
                return false;

            var barrier = package.BarcapBarrier;
            var originalCoverage = flight.PlannedBarcapCoverage;
            if (barrier?.BarrierTileIds == null
                || barrier.BarrierTileIds.Count == 0
                || originalCoverage?.CoveredBarrierTileIds == null
                || originalCoverage.CoveredBarrierTileIds.Count == 0)
                return false;

            var orderedCoveredTiles = barrier.BarrierTileIds
                .Where(originalCoverage.CoveredBarrierTileIds.Contains)
                .ToList();
            if (orderedCoveredTiles.Count == 0)
                return false;
            var coverageCenterTile = orderedCoveredTiles[
                orderedCoveredTiles.Count / 2];
            var tileDistanceKm = CampaignMapCoordinates
                .TileCenterSpacingKilometers;
            var currentStationDepthKm = BarcapInterceptGeometry
                .GetDefensiveStationDepthKm(
                    assignedStationCenterFeet,
                    coverageCenterTile,
                    barrier.ThreatReferenceTileId,
                    tileDistanceKm);
            var threats = GetKnownSamThreats(package.Alliance);
            var maneuverClearanceFeet = AirspaceGeometry
                .ConservativeSamManeuverClearanceFeet(aircraftType);
            var effectEnd = flight.EffectEnd;
            if (effectEnd <= currentTime)
                return false;

            var candidates = BarcapInterceptGeometry
                .GetDefensiveStationPositions(
                    coverageCenterTile,
                    barrier.ThreatReferenceTileId,
                    tileDistanceKm,
                    originalCoverage.StationCenterFeet.y,
                    originalCoverage.PlannedResponseRadiusKm)
                .Select(position =>
                {
                    var heading = BarcapInterceptGeometry
                        .GetStationHeadingDegrees(
                            position,
                            barrier.ThreatReferenceTileId,
                            tileDistanceKm);
                    var racetrack = BarcapInterceptGeometry.BuildRacetrack(
                        position,
                        heading,
                        originalCoverage.StationTrackHalfLengthKm,
                        aircraftType);
                    return new
                    {
                        Position = position,
                        Heading = heading,
                        Racetrack = racetrack,
                        DepthKm = BarcapInterceptGeometry
                            .GetDefensiveStationDepthKm(
                                position,
                                coverageCenterTile,
                                barrier.ThreatReferenceTileId,
                                tileDistanceKm),
                        Covered = BarcapInterceptGeometry
                        .GetContiguousCoveredBarrierRun(
                            barrier.BarrierTileIds,
                            coverageCenterTile,
                            racetrack.LoopPointsFeet,
                            originalCoverage,
                            tileDistanceKm)
                    };
                })
                .Where(candidate => candidate.DepthKm
                                    > currentStationDepthKm + 0.001f
                                    && candidate.Covered.Count > 0)
                .OrderByDescending(candidate => candidate.Covered.Count)
                .ThenBy(candidate => Vector3.Distance(
                    candidate.Position,
                    assignedStationCenterFeet))
                .ThenBy(candidate => candidate.Position.x)
                .ThenBy(candidate => candidate.Position.z)
                .ToList();

            var trackThreatRejections = 0;
            var defendedSideRejections = 0;
            var transitRejections = 0;
            var effectWindowRejections = 0;
            var recoveryRejections = 0;
            var fuelRejections = 0;
            var amendmentRejections = 0;
            var blockingTrackSites = new HashSet<Guid>();
            foreach (var candidate in candidates)
            {
                if (!KnownSamThreatGeometry.IsPathSafe(
                        candidate.Racetrack.GetClosedLoopPoints(),
                        threats,
                        maneuverClearanceFeet,
                        out var blockingTrackSiteId))
                {
                    trackThreatRejections++;
                    if (blockingTrackSiteId != Guid.Empty)
                        blockingTrackSites.Add(blockingTrackSiteId);
                    continue;
                }
                if (candidate.Racetrack.LoopPointsFeet.Any(point =>
                        !BarcapInterceptGeometry.IsOnDefendedSide(
                            point,
                            candidate.Covered,
                            originalCoverage.ThreatReferenceTileId,
                            tileDistanceKm,
                            originalCoverage.WeaponReleaseStandoffKm)))
                {
                    defendedSideRejections++;
                    continue;
                }
                if (!TryBuildSafeTransitPoints(
                        flight.PositionFeet,
                        candidate.Racetrack.LoopPointsFeet[0],
                        threats,
                        flight.FlightId,
                        maneuverClearanceFeet,
                        out var transitPoints))
                {
                    transitRejections++;
                    continue;
                }

                var replacement = new List<AirWaypoint>();
                var position = flight.PositionFeet;
                var time = currentTime;
                if (flight.CurrentWaypointIndex > 0)
                {
                    var precedingWaypointTime = flight.Route[
                        Math.Min(
                            flight.CurrentWaypointIndex - 1,
                            flight.Route.Count - 1)].PlannedArrivalTime;
                    if (precedingWaypointTime > time)
                        time = precedingWaypointTime;
                }
                foreach (var point in transitPoints.Take(
                             Math.Max(0, transitPoints.Count - 1)))
                {
                    time += TimeSpan.FromSeconds(
                        AirspaceGeometry.TravelSeconds(
                            position,
                            point,
                            aircraftType.CruiseSpeedKnots,
                            aircraftType.ClimbRateFeetPerMinute,
                            aircraftType.DescentRateFeetPerMinute));
                    replacement.Add(new AirWaypoint(
                        point,
                        AirWaypointAction.Transit,
                        time));
                    position = point;
                }

                time += TimeSpan.FromSeconds(
                    AirspaceGeometry.TravelSeconds(
                        position,
                        candidate.Racetrack.LoopPointsFeet[0],
                        aircraftType.CruiseSpeedKnots,
                        aircraftType.ClimbRateFeetPerMinute,
                        aircraftType.DescentRateFeetPerMinute));
                var stationEntryTime = time;
                var firstCircuitEnd = stationEntryTime + TimeSpan.FromSeconds(
                    AirspaceGeometry.HorizontalTravelSeconds(
                        candidate.Racetrack.CircuitLengthFeet,
                        aircraftType.CruiseSpeedKnots));
                if (firstCircuitEnd > effectEnd)
                {
                    effectWindowRejections++;
                    continue;
                }

                var relocatedCoverage = originalCoverage.Clone();
                relocatedCoverage.CoveredBarrierTileIds =
                    candidate.Covered.ToList();
                relocatedCoverage.StationCenterFeet = candidate.Position;
                relocatedCoverage.StationHeadingDegrees = candidate.Heading;
                relocatedCoverage.PlannedMinimumInterceptSlackKm =
                    candidate.Covered
                        .Select(tile => originalCoverage.PlannedResponseRadiusKm
                                        - BarcapInterceptGeometry
                                            .GetWorstStationDistanceToOperationalBarrierKm(
                                                candidate.Racetrack.LoopPointsFeet,
                                                tile,
                                                originalCoverage
                                                    .ThreatReferenceTileId,
                                                tileDistanceKm,
                                                originalCoverage
                                                    .WeaponReleaseStandoffKm))
                        .DefaultIfEmpty(float.NegativeInfinity)
                        .Min();
                var effectArea = new AirMissionArea(
                    AirspaceGeometry.TileCoordinateFromPositionFeet(
                        candidate.Position),
                    relocatedCoverage.PlannedResponseRadiusKm);
                var stationEntry = new AirWaypoint(
                    candidate.Racetrack.LoopPointsFeet[0],
                    AirWaypointAction.StationEntry,
                    stationEntryTime,
                    effectArea,
                    barcapCoverage: relocatedCoverage);
                replacement.Add(stationEntry);
                var stationPointTime = stationEntryTime;
                var previousStationPoint = candidate.Racetrack.LoopPointsFeet[0];
                for (var index = 1;
                     index < candidate.Racetrack.LoopPointsFeet.Count;
                     index++)
                {
                    var stationPoint = candidate.Racetrack.LoopPointsFeet[index];
                    stationPointTime += TimeSpan.FromSeconds(
                        AirspaceGeometry.TravelSeconds(
                            previousStationPoint,
                            stationPoint,
                            aircraftType.CruiseSpeedKnots,
                            aircraftType.ClimbRateFeetPerMinute,
                            aircraftType.DescentRateFeetPerMinute));
                    var isEndpoint = index
                                     == candidate.Racetrack.LoopPointsFeet.Count - 1;
                    replacement.Add(new AirWaypoint(
                        stationPoint,
                        isEndpoint
                            ? AirWaypointAction.StationEndpoint
                            : AirWaypointAction.Transit,
                        stationPointTime,
                        hasRepeat: isEndpoint,
                        repeatFromWaypointId: isEndpoint
                            ? stationEntry.WaypointId
                            : default,
                        repeatUntil: isEndpoint ? effectEnd : default));
                    previousStationPoint = stationPoint;
                }
                var stationExitPosition = candidate.Racetrack.LoopPointsFeet[
                    candidate.Racetrack.LoopPointsFeet.Count - 1];
                replacement.Add(new AirWaypoint(
                    stationExitPosition,
                    AirWaypointAction.ReturnToBase,
                    effectEnd));

                if (!TryBuildRecoveryRouteFrom(
                        package,
                        flight,
                        squadron,
                        aircraftType,
                        stationExitPosition,
                        effectEnd,
                        out var recoveryRoute))
                {
                    recoveryRejections++;
                    continue;
                }
                replacement.AddRange(recoveryRoute);
                if (!HasFuelForBarcapReplacement(
                        flight,
                        aircraftType,
                        currentTime,
                        effectEnd,
                        recoveryRoute[recoveryRoute.Count - 1]
                            .PlannedArrivalTime))
                {
                    fuelRejections++;
                    continue;
                }

                reason = $"BARCAP station displaced rearward from "
                         + $"{FormatAirPosition(assignedStationCenterFeet)} to "
                         + $"{FormatAirPosition(candidate.Position)}; "
                         + $"depth={currentStationDepthKm:0.0}->{candidate.DepthKm:0.0}km; "
                         + $"coveredTiles={candidate.Covered.Count}; "
                         + $"knownSamSites={threats.Select(threat => threat.SiteId).Distinct().Count()}; "
                         + $"trigger={triggerReason}";
                if (!flight.TryReplaceUnflownBarcapStationRoute(
                        currentTime,
                        reason,
                        replacement))
                {
                    amendmentRejections++;
                    continue;
                }

                airTaskingSystem.RevalidateAirportOperations(currentTime);
                return true;
            }

            var blockingSites = blockingTrackSites.Count == 0
                ? "none"
                : string.Join(
                    ",",
                    blockingTrackSites.OrderBy(id => id)
                        .Select(SimLogNames.ShortId));
            reason = "BARCAP relocation failed; "
                     + $"station={FormatAirPosition(assignedStationCenterFeet)}; "
                     + $"stationDepthKm={currentStationDepthKm:0.0}; "
                     + $"knownSamSites={threats.Select(threat => threat.SiteId).Distinct().Count()}; "
                     + $"candidates={candidates.Count}; "
                     + $"trackThreatRejected={trackThreatRejections}; "
                     + $"trackBlockingSites={blockingSites}; "
                     + $"defendedSideRejected={defendedSideRejections}; "
                     + $"transitRejected={transitRejections}; "
                     + $"effectWindowRejected={effectWindowRejections}; "
                     + $"recoveryRejected={recoveryRejections}; "
                     + $"fuelRejected={fuelRejections}; "
                     + $"amendmentRejected={amendmentRejections}; "
                     + $"trigger={triggerReason}";
            return false;
        }

        private static bool TryGetAssignedBarcapStation(
            AirFlight flight,
            out Vector3 stationCenterFeet)
        {
            stationCenterFeet = default;
            var coverage = flight?.PlannedBarcapCoverage;
            if (coverage == null)
                return false;
            stationCenterFeet = coverage.StationCenterFeet;
            return true;
        }

        private static bool TryBuildSafeTransitPoints(
            Vector3 origin,
            Vector3 destination,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            Guid routeKey,
            float maneuverClearanceFeet,
            out IReadOnlyList<Vector3> pointsIncludingDestination)
        {
            pointsIncludingDestination = Array.Empty<Vector3>();
            var routeStart = origin;
            var points = new List<Vector3>();
            if (KnownSamThreatGeometry.TryCreateEgressAimPoint(
                    routeStart,
                    threats,
                    routeKey,
                    maneuverClearanceFeet,
                    out var egressAimPoint))
            {
                points.Add(egressAimPoint);
                routeStart = egressAimPoint;
            }
            else if (threats.Any(threat =>
                         threat != null
                         && threat.Contains(routeStart, maneuverClearanceFeet)))
            {
                return false;
            }

            if (!KnownSamThreatGeometry.TryBuildAvoidingWaypoints(
                    routeStart,
                    destination,
                    threats,
                    routeKey,
                    maneuverClearanceFeet,
                    out var transitPath))
                return false;

            points.AddRange(transitPath);
            pointsIncludingDestination = points;
            return points.Count > 0;
        }

        private static bool HasFuelForBarcapReplacement(
            AirFlight flight,
            AircraftTypeDefinition aircraftType,
            DateTime currentTime,
            DateTime effectEnd,
            DateTime landingTime)
        {
            if (flight == null
                || aircraftType == null
                || aircraftType.EnduranceHours <= 0f
                || landingTime < effectEnd
                || effectEnd < currentTime)
                return false;

            var missionSeconds = (effectEnd - currentTime).TotalSeconds;
            var recoverySeconds = (landingTime - effectEnd).TotalSeconds;
            var enduranceSeconds = aircraftType.EnduranceHours * 3600d;
            var requiredFuelFraction =
                (missionSeconds + recoverySeconds * 0.9d + 60d)
                / enduranceSeconds;
            return requiredFuelFraction
                   <= flight.TacticalState.FuelFraction + 0.0001f;
        }

        private static void ContinueRelocatedBarcapCommand(
            AirCombatCommand command,
            AirFlight flight,
            AircraftTypeDefinition aircraftType,
            DateTime currentTime,
            string reason)
        {
            command.RequestsSurfaceThreatRecovery = false;
            command.RequestsBarcapStationRelocation = false;
            command.Intent = AirCombatIntent.FollowMission;
            command.Maneuver = AirCombatManeuver.FollowRoute;
            command.TargetFlightId = Guid.Empty;
            command.SupportedPendingEffectId = Guid.Empty;
            command.PreferredSide = AirCombatManeuverSide.None;
            command.AimPointFeet = flight.CurrentWaypoint?.PositionFeet
                                   ?? flight.PositionFeet;
            command.HasAimPoint = true;
            command.DesiredSpeedKnots = Math.Max(
                1f,
                aircraftType.CruiseSpeedKnots);
            command.MinimumManeuverEndAt = currentTime;
            command.RequestsWvrEngagement = false;
            command.Employment = null;
            command.Reason = reason;
        }

        private void AbortIfMissionUsefulOrdnanceExhausted(
            AirPackage package,
            AirFlight flight,
            Squadron squadron,
            AircraftTypeDefinition aircraftType,
            DateTime occurredAt)
        {
            if (!IsTimeBasedAirCombatFlight(flight)
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
            command.RequestsBarcapStationRelocation = false;
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
            command.RequestsBarcapStationRelocation = false;
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
                    if (flight.IsStrikeFlight
                        && HasPendingGroundEffect(flight.FlightId))
                        return;
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
            return TryBuildRecoveryRouteFrom(
                package,
                flight,
                squadron,
                aircraftType,
                flight.PositionFeet,
                currentTime,
                out recoveryRoute);
        }

        private bool TryBuildRecoveryRouteFrom(
            AirPackage package,
            AirFlight flight,
            Squadron squadron,
            AircraftTypeDefinition aircraftType,
            Vector3 originPosition,
            DateTime currentTime,
            out IReadOnlyList<AirWaypoint> recoveryRoute)
        {
            recoveryRoute = null;
            var threats = GetKnownSamThreats(package.Alliance);
            var maneuverClearanceFeet = AirspaceGeometry
                .ConservativeSamManeuverClearanceFeet(
                    aircraftType);
            var routeStart = originPosition;
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
                         squadron,
                         originPosition))
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
                var position = originPosition;
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
                    squadron,
                    flight.PositionFeet)
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
            Squadron squadron,
            Vector3 referencePosition)
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
                    Position = airport.PositionFeet
                })
                .OrderBy(candidate => Vector2.Distance(
                    new Vector2(referencePosition.x, referencePosition.z),
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
                position = building.PositionFeet;
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

        private static bool IsTimeBasedAirCombatFlight(AirFlight flight)
        {
            return flight != null
                   && (flight.IsFighterEscort
                       || flight.TaskType
                       == AirFlightTaskType.Barcap
                       || flight.TaskType
                       == AirFlightTaskType.OcaSweep);
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

        internal static float GetManeuverTurnRateDegreesPerSecond(
            AircraftTypeDefinition aircraftType,
            AirCombatManeuver maneuver)
        {
            return FlightMovementSystem
                .GetManeuverTurnRateDegreesPerSecond(
                    aircraftType,
                    maneuver);
        }

        internal static bool ShouldAdvanceRouteWaypoint(
            Vector3 routeSegmentStart,
            Vector3 previous,
            Vector3 current,
            Vector3 target,
            float arrivalCorridorFeet)
        {
            return FlightMovementSystem.ShouldAdvanceRouteWaypoint(
                routeSegmentStart,
                previous,
                current,
                target,
                arrivalCorridorFeet);
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
