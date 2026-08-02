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
            ordnanceEmploymentSystem.SetAirToAirSamEmploymentValidator(
                IsAirToAirSamEmploymentAuthorized);
        }

        private bool IsAirToAirSamEmploymentAuthorized(
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
                   && AirCombatRules.IsAirToAirEmploymentSamSafe(
                       source,
                       target,
                       frame,
                       pass,
                       ordnanceTypes,
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
                $"intent {previousIntent} -> {command.Intent}"
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
                KnownSamThreatsByAlliance =
                    new Dictionary<Alliance, IReadOnlyList<KnownSamThreatEnvelope>>
                    {
                        { Alliance.Bluefor, GetKnownSamThreats(Alliance.Bluefor) },
                        { Alliance.Redfor, GetKnownSamThreats(Alliance.Redfor) }
                    }
            };
            ApplyKnownSamEngagementOverrides(frame);
            frame.BarcapTargetByFlightId = AirCombatRules.BuildBarcapAssignments(
                frame,
                ordnanceTypes,
                GetDoctrine);
            return frame;
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
                || flight?.MissionType
                != AirMissionRequestType.BarrierCombatAirPatrol
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
                    MissionRequestId = package.MissionRequestId,
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
                         + "The request remains actionable for rematerialization.";
            commander?.AddDiagnostic(new AirTaskingDiagnostic
            {
                RecordedAt = currentTime,
                MissionRequestId = package.MissionRequestId,
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

                var routeSegmentStart = default(Vector3);
                var hasRouteSegment = followingRoute
                                      && TryGetCurrentRouteSegmentStart(
                                          flight,
                                          out routeSegmentStart);
                if (followingRoute
                    && HasReached(
                        flight.PositionFeet,
                        target,
                        WaypointCaptureFeet))
                {
                    var maximumReachSeconds = Math.Min(
                        MaximumIntegrationStepSeconds,
                        remaining);
                    var reachSeconds = EstimateReachSeconds(
                        flight,
                        target,
                        aircraftType,
                        speedKnots,
                        maximumReachSeconds);
                    if (reachSeconds >= 0d)
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

                        if (reachSeconds > 0.0001d)
                        {
                            IntegrateMotion(
                                flight,
                                target,
                                aircraftType,
                                command.Maneuver,
                                speedKnots,
                                reachSeconds);
                            remaining -= reachSeconds;
                            localTime = localTime.AddSeconds(reachSeconds);
                            BurnFuel(
                                flight,
                                aircraftType,
                                command.Intent,
                                reachSeconds);
                            waypointsCrossedWithoutTime?.Clear();
                        }
                        HandleWaypoint(package, flight, localTime);
                        continue;
                    }
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
                    || !ShouldAdvanceRouteWaypoint(
                        hasRouteSegment ? routeSegmentStart : previous,
                        previous,
                        flight.PositionFeet,
                        target,
                        GetWaypointArrivalCorridorFeet(
                            aircraftType,
                            speedKnots)))
                    continue;

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
                        .Where(flight => flight.IsDeadAttackFlight
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
                if (!siteNoLongerHostile
                    && minimumEffectAchievedByPackage
                    && !hasFunctionalShooterChain)
                {
                    var protectedDeadFlightIds = package.Flights
                        .Where(flight => flight.IsDeadAttackFlight)
                        .Select(flight => flight.FlightId)
                        .ToHashSet();
                    var coverageChanged = package.Flights
                        .Where(flight => flight.IsFighterEscort
                                         && flight.ProtectedFlightIds.Any(
                                             protectedDeadFlightIds.Contains))
                        .OrderBy(flight => flight.FlightId)
                        .Aggregate(
                            false,
                            (changed, escort) =>
                            {
                                var modeChanged = escort.UpdateEscortCoverageMode(
                                    AirEscortCoverageMode.CloseCover,
                                    currentTime,
                                    "The protected objective can no longer fire; "
                                    + "escort collapsed its forward screen to close cover.");
                                var clearanceChanged =
                                    escort.ConfirmSurfaceThreatCleared(
                                        site.SiteId,
                                        currentTime,
                                        "The protected package permanently broke the "
                                        + "target site's shooter chain; its former "
                                        + "envelope is cleared for close cover.");
                                return changed
                                       || modeChanged
                                       || clearanceChanged;
                            });
                    if (coverageChanged)
                    {
                        // The target envelope is derived from the now-broken
                        // shooter chain. Rebuild the tactical picture before
                        // the close-cover command is evaluated.
                        knownSamThreatCache.Clear();
                    }
                }
                foreach (var flight in package.Flights
                             .Where(candidate => candidate.IsDeadAttackFlight
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
            var threats = knownSamThreatAssessment.BuildKnownThreats(picture);
            var targetThreats = threats
                .Where(threat => threat.SiteId == plan.TargetSiteId)
                .ToList();
            if (targetThreats.Count == 0)
                return false;

            aircraftTypes.TryGetValue(
                plan.SupportedCorridor.RepresentativeAircraftTypeDefinitionId,
                out var representativeType);
            var clearanceFeet = AirspaceGeometry
                .ConservativeSamManeuverClearanceFeet(
                    representativeType);
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
                    CampaignMapCoordinates.TileCenterSpacingFeet,
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
            if (flight == null
                || squadron == null
                || site == null
                || !flight.CanEvaluateGroundAttackOpportunity(currentTime)
                || ordnanceEmploymentSystem.ActivePasses.Any(pass =>
                    pass.SourceFlightId == flight.FlightId
                    && pass.TargetKind
                    != OrdnanceEmploymentTargetKind.AirFlight)
                || ordnanceEmploymentSystem.PendingEffects.Any(effect =>
                    effect.SourceFlightId == flight.FlightId
                    && effect.TargetKind
                    != OrdnanceEmploymentTargetKind.AirFlight))
                return;

            if (!gameManager.airDefenseSiteSystem.TryGetPositionFeet(
                    site,
                    out var sitePositionFeet))
                return;

            var sequence = flight.ConsumeGroundAttackOpportunity(
                currentTime,
                retrySeconds: 60d);
            var opportunity = groundAttackOpportunityService
                .RollDeadOpportunity(
                    flight.FlightId,
                    sequence,
                    site,
                    siteTileId,
                    authorizedComponentIds,
                    currentTime,
                    ordnanceEmploymentSystem.HasActiveOrPendingEffect);
            if (!opportunity.HasTargets)
                return;

            var sourceAircraft = squadron.Aircraft
                .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Lost
                                   && aircraft.Status
                                   != CampaignAircraftStatus.Damaged)
                .ToList();
            if (!groundAttackDecisionService.TryPlan(
                    opportunity,
                    sourceAircraft,
                    ordnanceTypes,
                    (target, ordnance) =>
                        IsSuitableDeadOpportunityTarget(
                            site,
                            target,
                            ordnance)
                        && IsWithinGroundReleaseRange(
                            flight,
                            sitePositionFeet,
                            ordnance),
                    out var plan))
                return;

            ordnanceEmploymentSystem.TryStartGroundAttackPass(
                flight.FlightId,
                plan,
                currentTime);
        }

        private bool IsSuitableDeadOpportunityTarget(
            SamSite site,
            GroundAttackOpportunityTarget target,
            OrdnanceTypeDefinition ordnance)
        {
            if (site == null
                || target?.Target?.Kind
                != GroundAttackTargetKind.AirDefenseComponent)
                return false;

            var component = site.Components.FirstOrDefault(candidate =>
                candidate != null
                && candidate.ComponentId == target.Target.EntityId
                && !candidate.IsDamaged);
            if (component == null
                || !airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                || !DeadLoadoutPlanner.CanAttackComponent(
                    ordnance,
                    definition))
                return false;

            var isAntiRadiation = ordnance.EmploymentCategory
                                  == OrdnanceEmploymentCategory.AntiRadiation
                                  || ordnance.GuidanceMode
                                  == OrdnanceGuidanceMode.AntiRadiation;
            return !isAntiRadiation
                   || component is RadarAirDefenseComponent
                   {
                       IsEmitting: true
                   };
        }

        private bool IsWithinGroundReleaseRange(
            AirFlight flight,
            Vector3 targetPositionFeet,
            OrdnanceTypeDefinition ordnance)
        {
            if (flight == null || ordnance == null)
                return false;

            var distanceKm = HorizontalDistanceKm(
                flight.PositionFeet,
                targetPositionFeet);
            return distanceKm >= ordnance.MinimumRangeKm
                   && distanceKm <= ordnance.MaximumRangeKm;
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
                    || !view.Flight.IsDeadAttackFlight
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
            var componentsHitByPackage = GetHitAirDefenseComponentIds(
                ordnanceEmploymentSystem.Records,
                site.SiteId,
                packageFlightIds);
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

        internal static HashSet<Guid> GetHitAirDefenseComponentIds(
            IEnumerable<OrdnanceEmploymentRecord> records,
            Guid siteId,
            IReadOnlyCollection<Guid> sourceFlightIds)
        {
            var hits = new HashSet<Guid>();
            if (records == null
                || siteId == Guid.Empty
                || sourceFlightIds == null)
                return hits;

            foreach (var record in records.Where(record =>
                         record != null
                         && record.Stage
                         == OrdnanceEmploymentRecordStage.EffectResolved
                         && record.TargetKind
                         == OrdnanceEmploymentTargetKind.AirDefenseComponent
                         && record.TargetSiteId == siteId
                         && sourceFlightIds.Contains(record.SourceFlightId)))
            {
                foreach (var shot in record.Shots
                             ?? new List<OrdnanceShotDiagnostic>())
                {
                    if (shot == null || shot.Result != OrdnanceShotResult.Hit)
                        continue;

                    var groundTarget = shot.GroundTarget;
                    if (groundTarget != null)
                    {
                        if (groundTarget.Kind
                            == GroundAttackTargetKind.AirDefenseComponent
                            && groundTarget.ParentEntityId == siteId
                            && groundTarget.EntityId != Guid.Empty)
                        {
                            hits.Add(groundTarget.EntityId);
                        }
                    }
                    else if (record.TargetComponentId != Guid.Empty)
                    {
                        hits.Add(record.TargetComponentId);
                    }
                }
            }
            return hits;
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
                || flight?.MissionType
                != AirMissionRequestType.BarrierCombatAirPatrol
                || flight.LifecycleState != AirTaskingLifecycleState.Active
                || flight.ExecutionPhase == FlightExecutionPhase.Returning
                || flight.ExecutionPhase == FlightExecutionPhase.Landing
                || aircraftType == null
                || !TryGetAssignedBarcapStation(
                    flight,
                    out var assignedStationCenterFeet))
                return false;

            var commander = airTaskingSystem.GetCommander(package.Alliance);
            var request = commander?.GetRequest(package.MissionRequestId);
            var barrier = request?.BarcapBarrier;
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

        private static bool IsTimeBasedAirCombatFlight(AirFlight flight)
        {
            return flight != null
                   && (flight.IsFighterEscort
                       || flight.MissionType
                       == AirMissionRequestType.BarrierCombatAirPatrol
                       || flight.MissionType
                       == AirMissionRequestType.OffensiveCounterAirSweep);
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

        private static bool HasReached(
            Vector3 current,
            Vector3 target,
            float captureFeet)
        {
            return Vector3.Distance(current, target) <= captureFeet;
        }

        internal static bool ShouldAdvanceRouteWaypoint(
            Vector3 routeSegmentStart,
            Vector3 previous,
            Vector3 current,
            Vector3 target,
            float arrivalCorridorFeet)
        {
            if (Vector3.Distance(current, target) <= WaypointCaptureFeet)
                return true;

            var travel = current - previous;
            var travelMagnitudeSquared = travel.sqrMagnitude;
            if (travelMagnitudeSquared > 0.01f)
            {
                var previousToTarget = target - previous;
                var projection = Vector3.Dot(previousToTarget, travel)
                                 / travelMagnitudeSquared;
                if (projection >= 0f && projection <= 1f)
                {
                    var closest = previous + travel * projection;
                    if (Vector3.Distance(closest, target)
                        <= WaypointCaptureFeet)
                        return true;
                }
            }

            var routeLeg = target - routeSegmentStart;
            var routeLegMagnitudeSquared = routeLeg.sqrMagnitude;
            if (routeLegMagnitudeSquared <= 0.01f)
                return false;
            var progress = Vector3.Dot(
                current - routeSegmentStart,
                routeLeg) / routeLegMagnitudeSquared;
            return progress >= 1f
                   && Vector3.Distance(current, target)
                   <= Math.Max(WaypointCaptureFeet, arrivalCorridorFeet);
        }

        private static float GetWaypointArrivalCorridorFeet(
            AircraftTypeDefinition aircraftType,
            float speedKnots)
        {
            var turnRadiusFeet = AirspaceGeometry.TurnRadiusFeet(
                speedKnots,
                aircraftType.TurnRateDegreesPerSecond);
            var integrationStepFeet = speedKnots
                                      * AirspaceGeometry.FeetPerNauticalMile
                                      / 3600f
                                      * (float)MaximumIntegrationStepSeconds;
            return Math.Max(
                WaypointCaptureFeet,
                turnRadiusFeet + integrationStepFeet);
        }

        private static bool TryGetCurrentRouteSegmentStart(
            AirFlight flight,
            out Vector3 segmentStart)
        {
            segmentStart = default;
            if (flight?.CurrentWaypoint == null)
                return false;

            var waypointIndex = flight.CurrentWaypointIndex;
            var route = flight.Route;
            if (flight.CurrentWaypoint.Action == AirWaypointAction.StationEntry
                && flight.ExecutionPhase == FlightExecutionPhase.Executing)
            {
                var repeatEndpoint = route.FirstOrDefault(waypoint =>
                    waypoint.HasRepeat
                    && waypoint.RepeatFromWaypointId
                    == flight.CurrentWaypoint.WaypointId);
                if (repeatEndpoint == null)
                    return false;
                segmentStart = repeatEndpoint.PositionFeet;
                return true;
            }
            if (waypointIndex <= 0 || waypointIndex >= route.Count)
                return false;

            segmentStart = route[waypointIndex - 1].PositionFeet;
            return true;
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
