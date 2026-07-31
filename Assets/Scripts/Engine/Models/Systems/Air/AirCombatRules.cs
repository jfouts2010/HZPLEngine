using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Models
{
    internal sealed class AirCombatFlightView
    {
        public Alliance Alliance;
        public AirPackage Package;
        public AirFlight Flight;
        public Squadron Squadron;
        public AircraftTypeDefinition AircraftType;
        public List<CampaignAircraft> LiveAircraft;
        public List<CampaignAircraft> WvrAircraft;
        public Guid PreviousTargetFlightId;
    }

    internal sealed class AirCombatFrame
    {
        public DateTime Time;
        public float TileDistanceKm;
        public IReadOnlyDictionary<Guid, AirCombatFlightView> Flights;
        public IReadOnlyDictionary<Guid, AircraftTypeDefinition> AircraftTypes;
        public IReadOnlyDictionary<Alliance, AllianceAirTaskingCommander> AirCommanders;
        public IReadOnlyDictionary<Alliance, IReadOnlyDictionary<Guid, IADSTrack>>
            CurrentTracksByAlliance;
        public IReadOnlyList<ActiveOrdnanceEmploymentPass> ActivePasses;
        public IReadOnlyList<PendingOrdnanceEffect> PendingEffects;
        public IReadOnlyDictionary<Guid, Guid> BarcapTargetByFlightId;
        public IReadOnlyDictionary<Alliance, IReadOnlyList<KnownSamThreatEnvelope>>
            KnownSamThreatsByAlliance;

        public bool TryGetCurrentTrack(
            Alliance observingAlliance,
            Guid hostileFlightId,
            out IADSTrack track)
        {
            track = null;
            return CurrentTracksByAlliance != null
                   && CurrentTracksByAlliance.TryGetValue(
                       observingAlliance,
                       out var tracks)
                   && tracks != null
                   && tracks.TryGetValue(hostileFlightId, out track)
                   && track != null
                   && !track.IsStale;
        }

        public IReadOnlyList<KnownSamThreatEnvelope> GetKnownSamThreats(
            Alliance observingAlliance)
        {
            return KnownSamThreatsByAlliance != null
                   && KnownSamThreatsByAlliance.TryGetValue(
                       observingAlliance,
                       out var threats)
                ? threats
                : Array.Empty<KnownSamThreatEnvelope>();
        }
    }

    internal enum AirToAirShotStatus
    {
        Ready = 0,
        NeedsPointing = 1,
        LowQuality = 2,
        TooFar = 3,
        TooClose = 4,
        Unavailable = 5
    }

    internal sealed class AirToAirShotAssessment
    {
        public OrdnanceTypeDefinition Weapon { get; }
        public AirToAirShotStatus Status { get; }
        public float DistanceKm { get; }
        public float MaximumRangeKm { get; }
        public float OffNoseDegrees { get; }
        public float LaunchQuality { get; }
        public bool HasValidLaunchGeometry =>
            Status == AirToAirShotStatus.Ready
            || Status == AirToAirShotStatus.LowQuality;

        public AirToAirShotAssessment(
            OrdnanceTypeDefinition weapon,
            AirToAirShotStatus status,
            float distanceKm,
            float maximumRangeKm,
            float offNoseDegrees,
            float launchQuality)
        {
            Weapon = weapon;
            Status = status;
            DistanceKm = Math.Max(0f, distanceKm);
            MaximumRangeKm = Math.Max(0f, maximumRangeKm);
            OffNoseDegrees = Math.Max(0f, offNoseDegrees);
            LaunchQuality = Mathf.Clamp01(launchQuality);
        }
    }

    internal static class AirCombatRules
    {
        private const float PreferredRangeFraction = 0.78f;
        private const float MaximumClosingRangeBonus = 0.15f;
        private const float MaximumOpeningRangePenalty = 0.30f;
        private const float RadarBeamAspectQuality = 0.75f;
        private const float WvrDecisionRangeKm = 8f;
        private const float TacticalAimDistanceKm = 80f;
        private const float CrankOffsetDegrees = 55f;
        internal const float TerminalDefenseSeconds = 45f;
        private const float HotThreatAspectDegrees = 30f;
        private const float BarcapThreatLookaheadMinutes = 20f;
        private const float BarcapResponsePaddingTiles = 2f;
        private const float BarcapCommitMarginMinutes = 1.5f;
        private const float BarcapReleaseMarginMinutes = 5f;
        private const float BarcapBoundaryToleranceTiles = 0.1f;
        private const float BarcapDefensiveBufferTiles = 0.15f;
        private const float EscortThreatLookaheadMinutes = 10f;
        private const float EscortCommitMarginMinutes = 1.5f;
        private const float ThreateningCoursePersistenceSeconds = 30f;
        private const float ThreateningCourseMaximumGapSeconds = 30f;
        private const float EmergencyThreatEntryMinutes = 2f;
        private const float ProtectedFlightEnvelopeBufferKm = 5f;
        private const float CloseEscortLeadDistanceKm = 5f;
        private const float CloseEscortAltitudeOffsetFeet = 5000f;
        private const double LaunchSupportPredictionStepSeconds = 2d;
        private const int MaximumLaunchSupportPredictionSteps = 256;

        public static AirCombatCommand Decide(
            AirCombatFlightView source,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            AllianceAirDoctrine doctrine)
        {
            var command = DecideCore(
                source,
                frame,
                ordnanceTypes,
                doctrine);
            command = EnforceBarcapDefensiveBoundary(source, frame, command);
            command = EnforceKnownSamAvoidance(
                source,
                frame,
                command,
                ordnanceTypes);
            command.ObservedThreatCandidateFlightId =
                SelectObservedThreatCandidate(
                    source,
                    frame,
                    ordnanceTypes);
            return command;
        }

        private static AirCombatCommand DecideCore(
            AirCombatFlightView source,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            AllianceAirDoctrine doctrine)
        {
            var flight = source.Flight;
            var state = flight.TacticalState;
            if (flight.ExecutionPhase == FlightExecutionPhase.Returning
                || flight.ExecutionPhase == FlightExecutionPhase.Landing)
            {
                return RouteCommand(
                    source,
                    frame.Time,
                    AirCombatIntent.Recover,
                    "Flight is recovering.");
            }

            var incoming = frame.PendingEffects
                .Where(effect => effect.TargetFlightId == flight.FlightId
                                 && effect.ResolveAt > frame.Time)
                .OrderBy(effect => effect.ResolveAt)
                .ThenBy(effect => effect.PendingEffectId)
                .FirstOrDefault();
            if (incoming != null)
            {
                var secondsToImpact = Math.Max(
                    0d,
                    (incoming.ResolveAt - frame.Time).TotalSeconds);
                ordnanceTypes.TryGetValue(
                    incoming.OrdnanceTypeDefinitionId,
                    out var incomingOrdnance);
                if (IsCounterAirFlight(flight)
                    || secondsToImpact <= TerminalDefenseSeconds
                    || IsOutsideNoEscapeRange(
                        incoming,
                        incomingOrdnance))
                {
                    var threatFlightId = Guid.Empty;
                    var threatPosition = incoming.SourcePositionFeet;
                    if (incoming.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight
                        && frame.Flights.TryGetValue(
                            incoming.SourceFlightId,
                            out var attacker))
                    {
                        threatFlightId = attacker.Flight.FlightId;
                        threatPosition = attacker.Flight.PositionFeet;
                    }
                    return DefensiveCommand(
                        source,
                        threatPosition,
                        threatFlightId,
                        incoming,
                        incomingOrdnance,
                        frame.Time);
                }
            }

            var supported = frame.PendingEffects
                .Where(effect => effect.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight
                                 && effect.SourceFlightId == flight.FlightId
                                 && effect.SupportRequired
                                 && (effect.AutonomousAt == default
                                     || frame.Time < effect.AutonomousAt)
                                 && effect.ResolveAt > frame.Time)
                .OrderBy(effect => effect.ResolveAt)
                .ThenBy(effect => effect.PendingEffectId)
                .FirstOrDefault();
            if (supported != null
                && frame.Flights.TryGetValue(supported.TargetFlightId, out var supportedTarget))
            {
                return CrankCommand(source, supportedTarget, supported, frame.Time);
            }

            var activePass = frame.ActivePasses
                .Where(pass => pass.SourceFlightId == flight.FlightId)
                .OrderBy(pass => pass.ReleaseAt)
                .ThenBy(pass => pass.EmploymentPassId)
                .FirstOrDefault();
            if (activePass != null && flight.IsFighterEscort)
            {
                var preferredEscortTarget = SelectEscortTarget(
                    source,
                    frame,
                    ordnanceTypes,
                    doctrine);
                var protectedIds = GetProtectedFlightIds(source);
                if (preferredEscortTarget != null
                    && preferredEscortTarget.Flight.FlightId
                    != activePass.TargetFlightId
                    && IsCommittedAgainstFlights(
                        preferredEscortTarget,
                        protectedIds,
                        frame))
                {
                    var escortTargetDistanceKm = DistanceKm(
                        flight.PositionFeet,
                        preferredEscortTarget.Flight.PositionFeet);
                    var maximumRangeKm = GetAvailableAirToAirWeapons(
                            source,
                            ordnanceTypes)
                        .Select(weapon => EffectiveLaunchEnvelopeKm(
                            weapon,
                            flight,
                            preferredEscortTarget.Flight))
                        .DefaultIfEmpty(0f)
                        .Max();
                    var retarget = AimAtTargetCommand(
                        source,
                        preferredEscortTarget,
                        AirCombatIntent.EngageTarget,
                        escortTargetDistanceKm > maximumRangeKm
                            ? AirCombatManeuver.Intercept
                            : AirCombatManeuver.Press,
                        frame.Time,
                        frame.Time.AddSeconds(15),
                        preferredEscortTarget.Flight.FlightId,
                        Guid.Empty,
                        "Cancelling the lower-priority shot to defend a protected "
                        + "flight against an active attack.");
                    retarget.RequestsAirToAirPassCancellation = true;
                    return retarget;
                }
            }
            if (activePass != null
                && flight.IsFighterEscort
                && (!frame.Flights.TryGetValue(
                        activePass.TargetFlightId,
                        out var authorizedPassTarget)
                    || !IsEligibleTarget(
                        source,
                        authorizedPassTarget,
                        frame,
                        ordnanceTypes,
                        doctrine)))
            {
                var route = RouteCommand(
                    source,
                    frame.Time,
                    AirCombatIntent.FollowMission,
                    "The air threat is no longer authorized against the protected element; "
                    + "resuming escort coverage.");
                route.RequestsAirToAirPassCancellation = true;
                return route;
            }
            if (activePass != null
                && flight.MissionType
                == AirMissionRequestType.BarrierCombatAirPatrol
                && (!frame.Flights.TryGetValue(
                        activePass.TargetFlightId,
                        out var authorizedBarcapPassTarget)
                    || !IsEligibleTarget(
                        source,
                        authorizedBarcapPassTarget,
                        frame,
                        ordnanceTypes,
                        doctrine)))
            {
                var route = RouteCommand(
                    source,
                    frame.Time,
                    AirCombatIntent.FollowMission,
                    "The contact no longer threatens the assigned barrier; "
                    + "resuming the BARCAP station route.");
                route.RequestsAirToAirPassCancellation = true;
                return route;
            }
            if (activePass != null
                && frame.Flights.TryGetValue(activePass.TargetFlightId, out var passTarget))
            {
                if (flight.IsDeadAttackFlight)
                {
                    var route = DeadDefensiveShotCommand(
                        source,
                        frame.Time,
                        activePass.ReleaseAt,
                        "Preparing a defensive air-to-air shot while remaining on the DEAD route.");
                    if (!IsSelfDefenseTarget(
                            source,
                            passTarget,
                            frame,
                            ordnanceTypes))
                    {
                        route.RequestsAirToAirPassCancellation = true;
                        route.Reason =
                            "The defensive air threat is no longer authorized; "
                            + "resuming the DEAD route.";
                    }
                    return route;
                }

                return AimAtTargetCommand(
                    source,
                    passTarget,
                    AirCombatIntent.EngageTarget,
                    AirCombatManeuver.LaunchSetup,
                    frame.Time,
                    activePass.ReleaseAt,
                    activePass.TargetFlightId,
                    Guid.Empty,
                    "Holding launch geometry while ordnance preparation completes.");
            }

            if (state.MinimumManeuverEndAt > frame.Time
                && state.HasTacticalAimPoint
                && (state.Maneuver == AirCombatManeuver.BeamLeft
                    || state.Maneuver == AirCombatManeuver.BeamRight
                    || state.Maneuver == AirCombatManeuver.BreakLeft
                    || state.Maneuver == AirCombatManeuver.BreakRight
                    || state.Maneuver == AirCombatManeuver.Drag
                    || state.Maneuver == AirCombatManeuver.Extend))
            {
                return Command(
                    source,
                    state.Intent,
                    state.Maneuver,
                    state.TargetFlightId,
                    state.SupportedPendingEffectId,
                    frame.Time,
                    state.MinimumManeuverEndAt,
                    state.PreferredSide,
                    state.TacticalAimPointFeet,
                    Math.Max(1f, source.AircraftType.CombatSpeedKnots),
                    $"Continuing committed {state.Maneuver} maneuver.");
            }

            // BuildBvrManeuverCommand asks for a minimum commit on Intercept and
            // Press so a pursuit is not re-derived from scratch every tactical
            // step. Honour it while the target still exists and the merge
            // decision has not come due.
            if (state.MinimumManeuverEndAt > frame.Time
                && state.HasTacticalAimPoint
                && state.Intent == AirCombatIntent.EngageTarget
                && (state.Maneuver == AirCombatManeuver.Intercept
                    || state.Maneuver == AirCombatManeuver.Press)
                && state.TargetFlightId != Guid.Empty
                && frame.Flights.TryGetValue(
                    state.TargetFlightId,
                    out var committedTarget)
                && DistanceKm(
                    flight.PositionFeet,
                    committedTarget.Flight.PositionFeet)
                > WvrDecisionRangeKm)
            {
                return Command(
                    source,
                    state.Intent,
                    state.Maneuver,
                    state.TargetFlightId,
                    state.SupportedPendingEffectId,
                    frame.Time,
                    state.MinimumManeuverEndAt,
                    state.PreferredSide,
                    state.TacticalAimPointFeet,
                    Math.Max(1f, source.AircraftType.CombatSpeedKnots),
                    $"Continuing committed {state.Maneuver} maneuver.");
            }

            if (flight.MissionType == AirMissionRequestType.OffensiveCounterAirSweep
                && state.TargetFlightId != Guid.Empty
                && state.Intent == AirCombatIntent.EngageTarget
                && frame.Flights.TryGetValue(state.TargetFlightId, out var retainedTarget)
                && !IsSelfDefenseTarget(source, retainedTarget, frame, ordnanceTypes)
                && !IsOcaProactiveTargetAuthorized(
                    source,
                    retainedTarget,
                    frame,
                    doctrine,
                    ordnanceTypes,
                    out var disengageReason))
            {
                var disengage = AimAtTargetCommand(
                    source,
                    retainedTarget,
                    AirCombatIntent.Disengage,
                    AirCombatManeuver.Extend,
                    frame.Time,
                    frame.Time.AddSeconds(60),
                    Guid.Empty,
                    Guid.Empty,
                    disengageReason,
                    awayFromTarget: true);
                disengage.TargetFlightId = Guid.Empty;
                return disengage;
            }

            var target = SelectTarget(source, frame, ordnanceTypes, doctrine);
            if (target == null)
                return ContinueAssignedMission(
                    source,
                    frame,
                    "No authorized air target.");

            if (state.ProactiveEngagementExhausted)
            {
                return ContinueAssignedMission(
                    source,
                    frame,
                    "Recommit limit reached; resuming the assigned mission route.");
            }
            if ((state.Maneuver == AirCombatManeuver.Extend
                 || state.Maneuver == AirCombatManeuver.Drag)
                && state.RecommitCount >= doctrine.MaximumRecommits)
            {
                var exhausted = ContinueAssignedMission(
                    source,
                    frame,
                    "Recommit limit reached; resuming the assigned mission route.");
                exhausted.ExhaustProactiveEngagement = true;
                return exhausted;
            }
            if (state.Intent == AirCombatIntent.EngageTarget
                && state.IntentStartedAt != default
                && (frame.Time - state.IntentStartedAt).TotalMinutes
                >= doctrine.MaximumPursuitMinutes)
            {
                var disengage = AimAtTargetCommand(
                    source,
                    target,
                    AirCombatIntent.Disengage,
                    AirCombatManeuver.Extend,
                    frame.Time,
                    frame.Time.AddSeconds(60),
                    Guid.Empty,
                    Guid.Empty,
                    "Maximum pursuit time reached; extending before reassessment.",
                    awayFromTarget: true);
                disengage.TargetFlightId = Guid.Empty;
                return disengage;
            }

            var distanceKm = DistanceKm(flight.PositionFeet, target.Flight.PositionFeet);
            var isInsideWvrDecisionRange = distanceKm <= WvrDecisionRangeKm;
            if (isInsideWvrDecisionRange)
            {
                if (!ShouldContinueIntoWvr(
                        source,
                        target,
                        frame,
                        ordnanceTypes,
                        out var wvrReason))
                {
                    return AimAtTargetCommand(
                        source,
                        target,
                        AirCombatIntent.Disengage,
                        AirCombatManeuver.Extend,
                        frame.Time,
                        frame.Time.AddSeconds(45),
                        target.Flight.FlightId,
                        Guid.Empty,
                        wvrReason,
                        awayFromTarget: true);
                }

                var merge = AimAtTargetCommand(
                    source,
                    target,
                    AirCombatIntent.EngageTarget,
                    AirCombatManeuver.Dogfight,
                    frame.Time,
                    frame.Time.AddSeconds(20),
                    target.Flight.FlightId,
                    Guid.Empty,
                    wvrReason);
                merge.RequestsWvrEngagement = true;
                return merge;
            }

            var shot = SelectShotAssessment(
                source,
                target,
                ordnanceTypes,
                doctrine);
            if (shot == null)
            {
                if (flight.IsDeadAttackFlight)
                {
                    return RouteCommand(
                        source,
                        frame.Time,
                        AirCombatIntent.FollowMission,
                        "No expendable air-to-air ordnance remains; "
                        + "continuing the assigned mission.");
                }

                return AimAtTargetCommand(
                    source,
                    target,
                    AirCombatIntent.Disengage,
                    AirCombatManeuver.Extend,
                    frame.Time,
                    frame.Time.AddSeconds(30),
                    target.Flight.FlightId,
                    Guid.Empty,
                    "No expendable air-to-air ordnance remains; extending from the threat.",
                    awayFromTarget: true);
            }
            var weapon = shot.Weapon;

            if (shot.Status == AirToAirShotStatus.Ready)
            {
                var launchQuality = shot.LaunchQuality;
                var totalRounds = CountRounds(source, weapon.OrdnanceTypeDefinitionId);
                var pendingShots = frame.PendingEffects
                    .Where(effect => effect.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight
                                     && effect.TargetFlightId == target.Flight.FlightId
                                     && effect.ResolveAt > frame.Time)
                    .Sum(effect => effect.Quantity);
                var targetCapacity = Math.Max(
                    1,
                    target.LiveAircraft.Count
                    * doctrine.MaximumSimultaneousMissilesPerTargetAircraft);
                var desiredShots = Math.Max(
                    1,
                    Mathf.CeilToInt(
                        doctrine.DesiredExpectedKillsPerHostileFlight
                        / Math.Max(0.05f, weapon.HitProbability * launchQuality)));
                var availableShots = Math.Max(
                    0,
                    totalRounds - (weapon.EmploymentCategory
                                   == OrdnanceEmploymentCategory.Gun
                        ? 0
                        : doctrine.MinimumAirToAirWeaponReserve));
                var quantity = Math.Min(
                    availableShots,
                    Math.Max(
                        0,
                        Math.Min(targetCapacity, desiredShots) - pendingShots));
                if (quantity > 0)
                {
                    var employment = new AirCombatEmploymentProposal
                    {
                        SourceFlightId = flight.FlightId,
                        TargetFlightId = target.Flight.FlightId,
                        OrdnanceTypeDefinitionId = weapon.OrdnanceTypeDefinitionId,
                        Quantity = quantity,
                        LaunchQuality = launchQuality
                    };
                    if (flight.IsDeadAttackFlight)
                    {
                        var defensiveShot = DeadDefensiveShotCommand(
                            source,
                            frame.Time,
                            frame.Time.AddSeconds(Math.Max(
                                5f,
                                weapon.PreparationSeconds)),
                            $"Preparing a defensive {weapon.Name} shot from the DEAD route "
                            + $"at launch quality {launchQuality:0.00}.");
                        defensiveShot.Employment = employment;
                        return defensiveShot;
                    }

                    var command = AimAtTargetCommand(
                        source,
                        target,
                        AirCombatIntent.EngageTarget,
                        AirCombatManeuver.LaunchSetup,
                        frame.Time,
                        frame.Time.AddSeconds(Math.Max(5f, weapon.PreparationSeconds)),
                        target.Flight.FlightId,
                        Guid.Empty,
                        $"Valid {weapon.Name} opportunity at launch quality {launchQuality:0.00}.");
                    command.Employment = employment;
                    return command;
                }
            }

            if (flight.IsDeadAttackFlight)
            {
                return RouteCommand(
                    source,
                    frame.Time,
                    AirCombatIntent.FollowMission,
                    "Tracked air threat cannot be engaged from the current DEAD route; "
                    + "continuing the assigned mission.");
            }

            return BuildBvrManeuverCommand(source, target, shot, frame.Time);
        }

        private static AirCombatCommand BuildBvrManeuverCommand(
            AirCombatFlightView source,
            AirCombatFlightView target,
            AirToAirShotAssessment shot,
            DateTime currentTime)
        {
            if (shot.Weapon == null)
            {
                return AimAtTargetCommand(
                    source,
                    target,
                    AirCombatIntent.Disengage,
                    AirCombatManeuver.Extend,
                    currentTime,
                    currentTime.AddSeconds(30),
                    target.Flight.FlightId,
                    Guid.Empty,
                    "No usable air-to-air weapon envelope against the target; extending.",
                    awayFromTarget: true);
            }

            switch (shot.Status)
            {
                case AirToAirShotStatus.TooFar:
                {
                    var preferredRange = Math.Max(
                        1f,
                        shot.MaximumRangeKm * PreferredRangeFraction);
                    return Command(
                        source,
                        AirCombatIntent.EngageTarget,
                        AirCombatManeuver.Intercept,
                        target.Flight.FlightId,
                        Guid.Empty,
                        currentTime,
                        currentTime.AddSeconds(15),
                        AirCombatManeuverSide.None,
                        PredictStandoffIntercept(
                            source,
                            target,
                            preferredRange),
                        Math.Max(1f, source.AircraftType.CombatSpeedKnots),
                        $"Closing to enter the {shot.Weapon.Name} launch envelope "
                        + $"from {shot.DistanceKm:0} km "
                        + $"(current maximum {shot.MaximumRangeKm:0} km).");
                }
                case AirToAirShotStatus.NeedsPointing:
                    return AimAtTargetCommand(
                        source,
                        target,
                        AirCombatIntent.EngageTarget,
                        AirCombatManeuver.Press,
                        currentTime,
                        currentTime.AddSeconds(15),
                        target.Flight.FlightId,
                        Guid.Empty,
                        $"Turning to bring the target inside {shot.Weapon.Name} launch boresight "
                        + $"from {shot.OffNoseDegrees:0} degrees off nose.");
                case AirToAirShotStatus.LowQuality:
                    return AimAtTargetCommand(
                        source,
                        target,
                        AirCombatIntent.EngageTarget,
                        AirCombatManeuver.Press,
                        currentTime,
                        currentTime.AddSeconds(15),
                        target.Flight.FlightId,
                        Guid.Empty,
                        $"Pressing to improve the {shot.Weapon.Name} shot from "
                        + $"launch quality {shot.LaunchQuality:0.00}.");
                case AirToAirShotStatus.TooClose:
                    return AimAtTargetCommand(
                        source,
                        target,
                        AirCombatIntent.Disengage,
                        AirCombatManeuver.Extend,
                        currentTime,
                        currentTime.AddSeconds(30),
                        target.Flight.FlightId,
                        Guid.Empty,
                        $"Inside {shot.Weapon.Name} minimum range at "
                        + $"{shot.DistanceKm:0.0} km; extending for separation.",
                        awayFromTarget: true);
                case AirToAirShotStatus.Unavailable:
                    return AimAtTargetCommand(
                        source,
                        target,
                        AirCombatIntent.Disengage,
                        AirCombatManeuver.Extend,
                        currentTime,
                        currentTime.AddSeconds(30),
                        target.Flight.FlightId,
                        Guid.Empty,
                        $"No valid {shot.Weapon.Name} envelope against the target; extending.",
                        awayFromTarget: true);
                default:
                    return AimAtTargetCommand(
                        source,
                        target,
                        AirCombatIntent.EngageTarget,
                        AirCombatManeuver.Press,
                        currentTime,
                        currentTime.AddSeconds(15),
                        target.Flight.FlightId,
                        Guid.Empty,
                        "Holding offensive geometry while existing shots resolve.");
            }
        }

        public static bool EvaluateLaunch(
            AirCombatFlightView source,
            AirCombatFlightView target,
            OrdnanceTypeDefinition ordnance,
            out float launchQuality)
        {
            return EvaluateLaunch(
                source.Flight,
                source.AircraftType,
                target.Flight,
                ordnance,
                out launchQuality);
        }

        public static bool EvaluateLaunch(
            AirFlight source,
            AircraftTypeDefinition sourceType,
            AirFlight target,
            OrdnanceTypeDefinition ordnance,
            out float launchQuality)
        {
            var assessment = AssessShot(
                source,
                sourceType,
                target,
                ordnance,
                minimumLaunchQuality: 0f);
            launchQuality = assessment.LaunchQuality;
            return assessment.HasValidLaunchGeometry;
        }

        public static float EffectiveMaximumRangeKm(
            OrdnanceTypeDefinition ordnance,
            AirFlight source)
        {
            if (ordnance.EmploymentCategory != OrdnanceEmploymentCategory.AirToAirRadar)
                return ordnance.MaximumRangeKm;

            var altitudeMultiplier = 1f + Mathf.Clamp(
                (source.PositionFeet.y - 10000f) / 100000f,
                0f,
                0.3f);
            var speedMultiplier = 1f + Mathf.Clamp(
                (source.SpeedKnots - 400f) / 2000f,
                -0.05f,
                0.2f);
            return ordnance.MaximumRangeKm * altitudeMultiplier * speedMultiplier;
        }

        public static float EffectiveLaunchEnvelopeKm(
            OrdnanceTypeDefinition ordnance,
            AirFlight source,
            AirFlight target)
        {
            if (target == null)
                return EffectiveLaunchEnvelopeKm(
                    ordnance,
                    source,
                    Vector3.zero,
                    0f,
                    0f);

            return EffectiveLaunchEnvelopeKm(
                ordnance,
                source,
                target.PositionFeet,
                target.HeadingDegrees,
                target.SpeedKnots);
        }

        public static float EffectiveLaunchEnvelopeKm(
            OrdnanceTypeDefinition ordnance,
            AirFlight source,
            IADSTrack target)
        {
            if (target == null)
                return EffectiveLaunchEnvelopeKm(
                    ordnance,
                    source,
                    Vector3.zero,
                    0f,
                    0f);

            return EffectiveLaunchEnvelopeKm(
                ordnance,
                source,
                target.LastKnownPositionFeet,
                target.EstimatedHeadingDegrees,
                target.EstimatedSpeedKnots);
        }

        public static float EffectiveLaunchEnvelopeKm(
            OrdnanceTypeDefinition ordnance,
            AirFlight source,
            Vector3 targetPositionFeet,
            float targetHeadingDegrees,
            float targetSpeedKnots)
        {
            if (ordnance == null || source == null)
                return 0f;

            var shooterAdjustedRange = EffectiveMaximumRangeKm(ordnance, source);
            if (ordnance.EmploymentCategory
                != OrdnanceEmploymentCategory.AirToAirRadar)
                return shooterAdjustedRange;

            var lineOfSight = targetPositionFeet - source.PositionFeet;
            lineOfSight.y = 0f;
            if (lineOfSight.sqrMagnitude <= 1f)
                return shooterAdjustedRange;

            var targetVelocityKnots = Direction(targetHeadingDegrees)
                                      * targetSpeedKnots;
            var targetRadialSpeedKnots = Vector3.Dot(
                targetVelocityKnots,
                lineOfSight.normalized);
            var radialSpeedFraction = targetRadialSpeedKnots
                                      / Math.Max(1f, ordnance.EffectSpeedKnots);
            var rangeAdjustment = 1f - Mathf.Clamp(
                radialSpeedFraction,
                -MaximumClosingRangeBonus,
                MaximumOpeningRangePenalty);
            return shooterAdjustedRange * rangeAdjustment;
        }

        private static AirToAirShotAssessment AssessShot(
            AirCombatFlightView source,
            AirCombatFlightView target,
            OrdnanceTypeDefinition ordnance,
            float minimumLaunchQuality)
        {
            return AssessShot(
                source?.Flight,
                source?.AircraftType,
                target?.Flight,
                ordnance,
                minimumLaunchQuality);
        }

        private static AirToAirShotAssessment AssessShot(
            AirFlight source,
            AircraftTypeDefinition sourceType,
            AirFlight target,
            OrdnanceTypeDefinition ordnance,
            float minimumLaunchQuality)
        {
            if (source == null || sourceType == null || target == null || ordnance == null)
            {
                return new AirToAirShotAssessment(
                    ordnance,
                    AirToAirShotStatus.Unavailable,
                    0f,
                    0f,
                    0f,
                    0f);
            }

            var distanceKm = DistanceKm(source.PositionFeet, target.PositionFeet);
            var maximumRange = EffectiveLaunchEnvelopeKm(ordnance, source, target);
            var offNose = AngleOffNose(source, target.PositionFeet);
            var targetAltitude = target.PositionFeet.y;
            var status = AirToAirShotStatus.Ready;
            if (maximumRange <= 0f
                || targetAltitude < ordnance.MinimumTargetAltitudeFeet
                || targetAltitude > ordnance.MaximumTargetAltitudeFeet)
            {
                status = AirToAirShotStatus.Unavailable;
            }
            else if (distanceKm < ordnance.MinimumRangeKm)
            {
                status = AirToAirShotStatus.TooClose;
            }
            else if (distanceKm > maximumRange)
            {
                status = AirToAirShotStatus.TooFar;
            }
            else if (offNose > ordnance.MaximumLaunchOffBoresightDegrees)
            {
                status = AirToAirShotStatus.NeedsPointing;
            }

            var launchQuality = status == AirToAirShotStatus.Ready
                ? CalculateLaunchQuality(
                    source,
                    sourceType,
                    target,
                    ordnance,
                    distanceKm,
                    maximumRange,
                    offNose)
                : 0f;
            if (status == AirToAirShotStatus.Ready
                && launchQuality < minimumLaunchQuality)
                status = AirToAirShotStatus.LowQuality;

            return new AirToAirShotAssessment(
                ordnance,
                status,
                distanceKm,
                maximumRange,
                offNose,
                launchQuality);
        }

        private static float CalculateLaunchQuality(
            AirFlight source,
            AircraftTypeDefinition sourceType,
            AirFlight target,
            OrdnanceTypeDefinition ordnance,
            float distanceKm,
            float maximumRangeKm,
            float offNoseDegrees)
        {
            var rangeRatio = Mathf.Clamp01(
                distanceKm / Math.Max(0.01f, maximumRangeKm));
            var noEscapeRatio = Mathf.Clamp01(ordnance.NoEscapeRangeFraction);
            var rangeQuality = rangeRatio <= noEscapeRatio
                ? 1f
                : Mathf.Lerp(
                    1f,
                    0.25f,
                    (rangeRatio - noEscapeRatio)
                    / Math.Max(0.01f, 1f - noEscapeRatio));
            var noseQuality = 1f - 0.35f * Mathf.Clamp01(
                offNoseDegrees
                / Math.Max(1f, ordnance.MaximumLaunchOffBoresightDegrees));
            var targetAspect = TargetAspect(source, target);
            var aspectQuality = IsRadarGuided(ordnance)
                ? RadarNotchQuality(targetAspect)
                : Mathf.Lerp(0.7f, 1f, targetAspect / 180f);
            var sensorQuality = ordnance.GuidanceMode == OrdnanceGuidanceMode.Infrared
                ? 1f
                : 0.65f + 0.35f * Mathf.Clamp01(sourceType.RadarQuality);
            return Mathf.Clamp01(
                rangeQuality
                * noseQuality
                * aspectQuality
                * sensorQuality);
        }

        private static float RadarNotchQuality(float targetAspectDegrees)
        {
            var beamProximity = 1f - Math.Abs(targetAspectDegrees - 90f) / 90f;
            return Mathf.Lerp(
                1f,
                RadarBeamAspectQuality,
                Mathf.Clamp01(beamProximity));
        }

        public static float AngleOffNose(AirFlight source, Vector3 targetPosition)
        {
            var bearing = HeadingTo(source.PositionFeet, targetPosition);
            return Math.Abs(Mathf.DeltaAngle(source.HeadingDegrees, bearing));
        }

        public static float HeadingTo(Vector3 from, Vector3 to)
        {
            return Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
        }

        public static Vector3 Direction(float headingDegrees)
        {
            var radians = headingDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        public static IReadOnlyDictionary<Guid, Guid> BuildBarcapAssignments(
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            Func<Alliance, AllianceAirDoctrine> doctrineForAlliance)
        {
            var assignments = new Dictionary<Guid, Guid>();
            var defenders = frame.Flights.Values
                .Where(view => view.Flight.MissionType
                               == AirMissionRequestType.BarrierCombatAirPatrol
                               && view.Flight.LifecycleState == AirTaskingLifecycleState.Active
                               && view.Flight.ExecutionPhase == FlightExecutionPhase.Executing
                               && !view.Flight.MissionAchieved
                               && frame.Time < view.Flight.EffectEnd
                               && CalculateFlightAirCombatPower(view, ordnanceTypes) > 0f)
                .OrderBy(view => view.Flight.FlightId)
                .ToList();

            var assignedDefenders = new HashSet<Guid>();
            var threats = frame.Flights.Values
                .Where(target => target.Flight.IsAirborne
                                 && target.Flight.ExecutionPhase
                                 != FlightExecutionPhase.Landing
                                 && target.LiveAircraft.Count > 0)
                .Select(target => new
                {
                    Target = target,
                    Eligible = defenders
                        .Where(defender => AreHostile(defender.Alliance, target.Alliance))
                        .Select(defender =>
                        {
                            var minutes = float.MaxValue;
                            var interceptMinutes = float.MaxValue;
                            var authorized = frame.TryGetCurrentTrack(
                                                 defender.Alliance,
                                                 target.Flight.FlightId,
                                                 out var track)
                                             && TryGetBarcapThreatMinutes(
                                                 defender,
                                                 track,
                                                 frame,
                                                 ordnanceTypes,
                                                 out minutes);
                            if (authorized
                                && TryGetBarcapThreatGeometry(
                                    defender,
                                    track,
                                    frame,
                                    ordnanceTypes,
                                    out var crossingFeet,
                                    out _))
                            {
                                interceptMinutes =
                                    CalculateBarcapInterceptMinutes(
                                        defender,
                                        crossingFeet,
                                        ordnanceTypes);
                            }
                            return new
                            {
                                Defender = defender,
                                Track = track,
                                Authorized = authorized,
                                Minutes = authorized ? minutes : float.MaxValue,
                                InterceptMinutes = interceptMinutes
                            };
                        })
                        .Where(candidate => candidate.Authorized)
                        .ToList()
                })
                .Where(candidate => candidate.Eligible.Count > 0)
                .Select(candidate => new
                {
                    candidate.Target,
                    candidate.Eligible,
                    Minutes = candidate.Eligible.Min(eligible => eligible.Minutes),
                    Power = CalculateThreatPower(candidate.Eligible[0].Track)
                })
                .OrderBy(candidate => candidate.Minutes)
                .ThenByDescending(candidate => candidate.Power)
                .ThenBy(candidate => candidate.Target.Flight.FlightId)
                .ToList();

            foreach (var threat in threats)
            {
                var requiredPower = Math.Max(
                    0.1f,
                    threat.Power
                    * doctrineForAlliance(threat.Eligible[0].Defender.Alliance)
                        .DesiredAirCombatAdvantage);
                var accumulatedPower = 0f;
                foreach (var candidate in threat.Eligible
                             .Where(candidate => !assignedDefenders.Contains(
                                 candidate.Defender.Flight.FlightId))
                              .OrderByDescending(candidate =>
                                  candidate.Defender.Flight.TacticalState.TargetFlightId
                                  == threat.Target.Flight.FlightId)
                              .ThenBy(candidate => candidate.InterceptMinutes)
                              .ThenByDescending(candidate =>
                                  candidate.Defender.Flight.TacticalState.FuelFraction)
                              .ThenByDescending(candidate => CalculateFlightAirCombatPower(
                                  candidate.Defender,
                                 ordnanceTypes))
                             .ThenBy(candidate => candidate.Defender.Flight.FlightId))
                {
                    var defenderId = candidate.Defender.Flight.FlightId;
                    assignments[defenderId] = threat.Target.Flight.FlightId;
                    assignedDefenders.Add(defenderId);
                    accumulatedPower += CalculateFlightAirCombatPower(
                        candidate.Defender,
                        ordnanceTypes);
                    if (accumulatedPower >= requiredPower)
                        break;
                }
            }

            return assignments;
        }

        private static AirCombatFlightView SelectTarget(
            AirCombatFlightView source,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            AllianceAirDoctrine doctrine)
        {
            if (source.Flight.MissionType
                == AirMissionRequestType.BarrierCombatAirPatrol)
            {
                var selfDefenseTarget = frame.Flights.Values
                    .Where(candidate => AreHostile(source.Alliance, candidate.Alliance)
                                        && IsSelfDefenseTarget(
                                            source,
                                            candidate,
                                            frame,
                                            ordnanceTypes))
                    .OrderBy(candidate => DistanceKm(
                        source.Flight.PositionFeet,
                        candidate.Flight.PositionFeet))
                    .ThenBy(candidate => candidate.Flight.FlightId)
                    .FirstOrDefault();
                if (selfDefenseTarget != null)
                    return selfDefenseTarget;

                if (frame.BarcapTargetByFlightId != null
                    && frame.BarcapTargetByFlightId.TryGetValue(
                        source.Flight.FlightId,
                        out var assignedTargetId)
                    && frame.Flights.TryGetValue(assignedTargetId, out var assignedTarget)
                    && IsEligibleTarget(
                        source,
                        assignedTarget,
                        frame,
                        ordnanceTypes,
                        doctrine))
                    return assignedTarget;
            }

            if (source.Flight.IsFighterEscort)
            {
                return SelectEscortTarget(
                    source,
                    frame,
                    ordnanceTypes,
                    doctrine);
            }

            var retainedId = source.Flight.TacticalState.TargetFlightId;
            if (retainedId != Guid.Empty
                && frame.Flights.TryGetValue(retainedId, out var retained)
                && IsEligibleTarget(
                    source,
                    retained,
                    frame,
                    ordnanceTypes,
                    doctrine))
                return retained;

            return frame.Flights.Values
                .Where(candidate => IsEligibleTarget(
                    source,
                    candidate,
                    frame,
                    ordnanceTypes,
                    doctrine))
                .Select(candidate => new
                {
                    Flight = candidate,
                    AssignedByPackage = source.Package.Flights.Any(flight =>
                        flight.FlightId != source.Flight.FlightId
                        && flight.TacticalState.TargetFlightId
                        == candidate.Flight.FlightId),
                    PendingPackageAttack = frame.PendingEffects.Any(effect =>
                        effect.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight
                        && effect.SourceFlightId == candidate.Flight.FlightId
                        && source.Package.Flights.Any(flight =>
                            flight.FlightId == effect.TargetFlightId)),
                    HotThreat = IsHotThreatWithinSelfDefenseEnvelope(
                        source,
                        candidate,
                        frame,
                        ordnanceTypes),
                    Distance = DistanceKm(
                        source.Flight.PositionFeet,
                        GetObservedTargetPosition(source, candidate, frame)),
                    EstimatedAircraftCount = GetObservedAircraftCount(
                        source,
                        candidate,
                        frame)
                })
                .OrderByDescending(candidate => candidate.PendingPackageAttack)
                .ThenByDescending(candidate => candidate.HotThreat)
                .ThenByDescending(candidate => candidate.AssignedByPackage)
                .ThenBy(candidate => candidate.Distance)
                .ThenByDescending(candidate => candidate.EstimatedAircraftCount)
                .ThenBy(candidate => candidate.Flight.Flight.FlightId)
                .Select(candidate => candidate.Flight)
                .FirstOrDefault();
        }

        private static AirCombatFlightView SelectEscortTarget(
            AirCombatFlightView source,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            AllianceAirDoctrine doctrine)
        {
            var protectedIds = GetProtectedFlightIds(source);
            var escortIds = new HashSet<Guid> { source.Flight.FlightId };
            var retainedId = source.Flight.TacticalState.TargetFlightId;

            return frame.Flights.Values
                .Where(candidate => IsEligibleTarget(
                    source,
                    candidate,
                    frame,
                    ordnanceTypes,
                    doctrine))
                .Select(candidate =>
                {
                    var hasThreatTiming = IsEscortTargetAuthorized(
                        source,
                        candidate,
                        frame,
                        ordnanceTypes,
                        out var minutesToThreat);
                    return new
                    {
                        Flight = candidate,
                        AttackingProtectedFlight = IsCommittedAgainstFlights(
                            candidate,
                            protectedIds,
                            frame),
                        AttackingEscort = IsCommittedAgainstFlights(
                            candidate,
                            escortIds,
                            frame),
                        MinutesToThreat = hasThreatTiming
                            ? minutesToThreat
                            : float.MaxValue,
                        Retained = candidate.Flight.FlightId == retainedId,
                        Distance = DistanceKm(
                            source.Flight.PositionFeet,
                            GetObservedTargetPosition(
                                source,
                                candidate,
                                frame))
                    };
                })
                // Protection commitments outrank target retention. Retention is
                // only a tie-breaker so a distant contact cannot become sticky
                // when another hostile attacks the protected element.
                .OrderByDescending(candidate =>
                    candidate.AttackingProtectedFlight)
                .ThenByDescending(candidate => candidate.AttackingEscort)
                .ThenBy(candidate => candidate.MinutesToThreat)
                .ThenByDescending(candidate => candidate.Retained)
                .ThenBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Flight.Flight.FlightId)
                .Select(candidate => candidate.Flight)
                .FirstOrDefault();
        }

        private static Vector3 GetObservedTargetPosition(
            AirCombatFlightView source,
            AirCombatFlightView target,
            AirCombatFrame frame)
        {
            return frame.TryGetCurrentTrack(
                source.Alliance,
                target.Flight.FlightId,
                out var track)
                ? track.LastKnownPositionFeet
                : target.Flight.PositionFeet;
        }

        private static int GetObservedAircraftCount(
            AirCombatFlightView source,
            AirCombatFlightView target,
            AirCombatFrame frame)
        {
            return frame.TryGetCurrentTrack(
                source.Alliance,
                target.Flight.FlightId,
                out var track)
                ? Math.Max(0, track.EstimatedAircraftCount)
                : target.LiveAircraft.Count;
        }

        private static bool IsEligibleTarget(
            AirCombatFlightView source,
            AirCombatFlightView target,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            AllianceAirDoctrine doctrine)
        {
            if (target == null
                || !AreHostile(source.Alliance, target.Alliance)
                || !target.Flight.IsAirborne
                || target.Flight.ExecutionPhase == FlightExecutionPhase.Landing
                || target.LiveAircraft.Count == 0)
                return false;

            if (IsCommittedAgainstFlights(
                    target,
                    new[] { source.Flight.FlightId },
                    frame))
                return true;

            if (source.Flight.IsFighterEscort)
            {
                return IsEscortTargetAuthorized(
                    source,
                    target,
                    frame,
                    ordnanceTypes,
                    out _);
            }

            if (IsHotThreatWithinSelfDefenseEnvelope(
                    source,
                    target,
                    frame,
                    ordnanceTypes))
                return true;

            if (!IsCounterAirFlight(source.Flight))
            {
                return IsHotThreatWithinSelfDefenseEnvelope(
                    source,
                    target,
                    frame,
                    ordnanceTypes);
            }

            if (!frame.TryGetCurrentTrack(
                    source.Alliance,
                    target.Flight.FlightId,
                    out var track))
                return false;

            if (source.Flight.LifecycleState != AirTaskingLifecycleState.Active
                || source.Flight.MissionAchieved
                || frame.Time >= source.Flight.EffectEnd)
                return false;

            if (source.Flight.MissionType == AirMissionRequestType.BarrierCombatAirPatrol)
            {
                return source.Flight.ExecutionPhase == FlightExecutionPhase.Executing
                       && frame.BarcapTargetByFlightId != null
                       && frame.BarcapTargetByFlightId.TryGetValue(
                           source.Flight.FlightId,
                           out var assignedTargetId)
                       && assignedTargetId == target.Flight.FlightId
                       && TryGetBarcapThreatMinutes(
                           source,
                           track,
                           frame,
                           ordnanceTypes,
                           out _);
            }

            if (source.Flight.MissionType == AirMissionRequestType.OffensiveCounterAirSweep)
            {
                return source.Flight.ExecutionPhase == FlightExecutionPhase.Executing
                       && IsOcaProactiveTargetAuthorized(
                           source,
                           target,
                           frame,
                           doctrine,
                           ordnanceTypes,
                           out _);
            }

            return false;
        }

        private static bool IsEscortTargetAuthorized(
            AirCombatFlightView source,
            AirCombatFlightView target,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            out float minutesToThreat)
        {
            minutesToThreat = float.MaxValue;
            if (!source.Flight.IsFighterEscort
                || !frame.TryGetCurrentTrack(
                    source.Alliance,
                    target.Flight.FlightId,
                    out var track))
                return false;

            var protectedIds = GetProtectedFlightIds(source);
            var protectedFlights = GetActiveProtectedFlights(
                source,
                frame,
                executingOnly: false);
            var defendedIds = protectedIds
                .Append(source.Flight.FlightId)
                .ToHashSet();

            if (frame.PendingEffects.Any(effect =>
                    effect.SourceKind
                    == OrdnanceEmploymentSourceKind.AircraftFlight
                    && effect.SourceFlightId == target.Flight.FlightId
                    && defendedIds.Contains(effect.TargetFlightId)
                    && effect.ResolveAt > frame.Time))
            {
                minutesToThreat = 0f;
                return true;
            }

            if (frame.ActivePasses.Any(pass =>
                    pass.SourceFlightId == target.Flight.FlightId
                    && defendedIds.Contains(pass.TargetFlightId)
                    && pass.ReleaseAt > frame.Time))
            {
                minutesToThreat = 0f;
                return true;
            }

            var committedTargetId = target.Flight.TacticalState.TargetFlightId;
            if (target.Flight.TacticalState.Intent
                    == AirCombatIntent.EngageTarget
                && (committedTargetId == source.Flight.FlightId
                    || protectedIds.Contains(committedTargetId)))
            {
                minutesToThreat = 0f;
                return true;
            }
            if (target.Flight.TacticalState.Intent
                    == AirCombatIntent.EngageTarget
                && committedTargetId != Guid.Empty
                && committedTargetId != source.Flight.FlightId
                && !protectedIds.Contains(committedTargetId))
            {
                return false;
            }

            if (!TryResolvePotentialAirThreatCapability(
                    track,
                    frame,
                    ordnanceTypes,
                    AirThreatAssessmentPurpose.ProtectedAircraft,
                    out var threatCapability))
                return false;

            if (IsHotThreatWithinSelfDefenseEnvelope(
                    source,
                    target,
                    frame,
                    ordnanceTypes))
            {
                minutesToThreat = 0f;
                return true;
            }

            if (protectedFlights.Count == 0)
                return false;

            foreach (var protectedFlight in protectedFlights)
            {
                if (!TryCalculateMovingThreatEnvelopeEntry(
                        track,
                        protectedFlight.Flight,
                        threatCapability,
                        frame,
                        out var threatMinutes,
                        out var threatEntryPointFeet))
                    continue;

                if (threatMinutes > EscortThreatLookaheadMinutes
                    || !IsThreatCoursePersistentOrUrgent(
                        source.Flight,
                        target.Flight.FlightId,
                        frame.Time,
                        threatMinutes))
                    continue;

                var escortMinutes = CalculateAirInterceptMinutes(
                    source,
                    threatEntryPointFeet,
                    ordnanceTypes);
                if (threatMinutes
                    > escortMinutes + EscortCommitMarginMinutes)
                    continue;

                minutesToThreat = Math.Min(minutesToThreat, threatMinutes);
            }

            return minutesToThreat < float.MaxValue;
        }

        private static bool TryResolvePotentialAirThreatCapability(
            IADSTrack track,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            AirThreatAssessmentPurpose purpose,
            out PotentialAirThreatCapability capability)
        {
            capability = default;
            if (track == null || track.IsStale)
                return false;

            if (track.HasIdentifiedAircraftType
                && frame.AircraftTypes != null
                && frame.AircraftTypes.TryGetValue(
                    track.IdentifiedAircraftTypeDefinitionId,
                    out var identifiedType))
            {
                return TryBuildPotentialAirThreatCapability(
                    identifiedType,
                    ordnanceTypes,
                    purpose,
                    out capability);
            }

            var possibleThreatTypes = (frame.AircraftTypes?.Values
                                       ?? Array.Empty<AircraftTypeDefinition>())
                .Select(type => TryBuildPotentialAirThreatCapability(
                    type,
                    ordnanceTypes,
                    purpose,
                    out var candidate)
                    ? (PotentialAirThreatCapability?)candidate
                    : null)
                .Where(candidate => candidate.HasValue)
                .Select(candidate => candidate.Value)
                .ToList();
            capability = new PotentialAirThreatCapability(
                possibleThreatTypes
                    .Select(candidate => candidate.MaximumPotentialReachKm)
                    .DefaultIfEmpty(Math.Max(20f, frame.TileDistanceKm))
                    .Max(),
                possibleThreatTypes
                    .Select(candidate => candidate.CombatSpeedKnots)
                    .DefaultIfEmpty(Math.Max(300f, track.EstimatedSpeedKnots))
                    .Max(),
                useConservativeSpeed: true);
            return true;
        }

        private static bool TryBuildPotentialAirThreatCapability(
            AircraftTypeDefinition aircraftType,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            AirThreatAssessmentPurpose purpose,
            out PotentialAirThreatCapability capability)
        {
            capability = default;
            if (aircraftType == null
                || aircraftType.SupportCapability != AirSupportCapability.None)
                return false;

            var potentialWeapons = aircraftType
                .CompatibleOrdnanceTypeDefinitionIds
                .Where(ordnanceTypes.ContainsKey)
                .Select(id => ordnanceTypes[id])
                .Where(weapon => purpose
                                     == AirThreatAssessmentPurpose
                                         .ProtectedAircraft
                    ? IsAirToAir(weapon)
                    : IsAirToAir(weapon) || IsAirToSurface(weapon))
                .ToList();
            if (aircraftType.AirInterferenceCapability <= 0f
                && potentialWeapons.Count == 0)
                return false;

            capability = new PotentialAirThreatCapability(
                potentialWeapons
                    .Select(weapon => weapon.MaximumRangeKm)
                    .DefaultIfEmpty(1f)
                    .Max(),
                Math.Max(1f, aircraftType.CombatSpeedKnots),
                useConservativeSpeed: false);
            return true;
        }

        private static bool TryCalculateMovingThreatEnvelopeEntry(
            IADSTrack track,
            AirFlight protectedFlight,
            PotentialAirThreatCapability capability,
            AirCombatFrame frame,
            out float minutesToEntry,
            out Vector3 entryPointFeet)
        {
            minutesToEntry = float.MaxValue;
            entryPointFeet = default;
            if (track == null || protectedFlight == null)
                return false;

            var hostilePosition = new Vector2(
                track.LastKnownPositionFeet.x,
                track.LastKnownPositionFeet.z);
            var protectedPosition = new Vector2(
                protectedFlight.PositionFeet.x,
                protectedFlight.PositionFeet.z);
            var hostileSpeedKnots = capability.UseConservativeSpeed
                ? Math.Max(
                    track.EstimatedSpeedKnots,
                    capability.CombatSpeedKnots)
                : track.EstimatedSpeedKnots > 0f
                    ? track.EstimatedSpeedKnots
                    : capability.CombatSpeedKnots;
            var hostileDirection = Direction(track.EstimatedHeadingDegrees);
            var protectedDirection = Direction(protectedFlight.HeadingDegrees);
            var hostileVelocity = new Vector2(
                                      hostileDirection.x,
                                      hostileDirection.z)
                                  * hostileSpeedKnots * 1.852f / 60f;
            var protectedVelocity = new Vector2(
                                        protectedDirection.x,
                                        protectedDirection.z)
                                    * protectedFlight.SpeedKnots * 1.852f / 60f;
            var relativePositionKm = (hostilePosition - protectedPosition)
                                     / AirspaceGeometry.FeetPerKilometer;
            var relativeVelocityKmPerMinute = hostileVelocity
                                               - protectedVelocity;
            var uncertaintyKm = Math.Max(0f, 1f - track.Quality)
                                * Math.Max(0f, frame.TileDistanceKm);
            var threatRadiusKm = capability.MaximumPotentialReachKm
                                 + ProtectedFlightEnvelopeBufferKm
                                 + uncertaintyKm;
            var c = relativePositionKm.sqrMagnitude
                    - threatRadiusKm * threatRadiusKm;
            if (c <= 0f)
            {
                minutesToEntry = 0f;
                entryPointFeet = track.LastKnownPositionFeet;
                return true;
            }

            var a = relativeVelocityKmPerMinute.sqrMagnitude;
            if (a < 0.0001f)
                return false;
            var b = 2f * Vector2.Dot(
                relativePositionKm,
                relativeVelocityKmPerMinute);
            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
                return false;

            var firstEntryMinutes = (-b - Mathf.Sqrt(discriminant))
                                    / (2f * a);
            if (firstEntryMinutes < 0f
                || firstEntryMinutes > EscortThreatLookaheadMinutes)
                return false;

            minutesToEntry = firstEntryMinutes;
            var hostileDisplacementKm = hostileVelocity * firstEntryMinutes;
            entryPointFeet = new Vector3(
                track.LastKnownPositionFeet.x
                + hostileDisplacementKm.x
                * AirspaceGeometry.FeetPerKilometer,
                track.LastKnownPositionFeet.y,
                track.LastKnownPositionFeet.z
                + hostileDisplacementKm.y
                * AirspaceGeometry.FeetPerKilometer);
            return true;
        }

        private static bool IsThreatCoursePersistentOrUrgent(
            AirFlight defender,
            Guid threatFlightId,
            DateTime currentTime,
            float minutesToThreat)
        {
            return minutesToThreat <= EmergencyThreatEntryMinutes
                   || defender.TacticalState.HasPersistentThreatObservation(
                       threatFlightId,
                       currentTime,
                       TimeSpan.FromSeconds(
                           ThreateningCoursePersistenceSeconds),
                       TimeSpan.FromSeconds(
                           ThreateningCourseMaximumGapSeconds));
        }

        private static Guid SelectObservedThreatCandidate(
            AirCombatFlightView source,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
            if (source?.Flight == null
                || frame?.Flights == null
                || (!source.Flight.IsFighterEscort
                    && source.Flight.MissionType
                    != AirMissionRequestType.BarrierCombatAirPatrol))
                return Guid.Empty;

            if (source.Flight.IsFighterEscort)
            {
                var protectedFlights = GetActiveProtectedFlights(
                    source,
                    frame,
                    executingOnly: false);
                if (protectedFlights.Count == 0)
                    return Guid.Empty;

                return frame.Flights.Values
                    .Where(target => target != null
                                     && AreHostile(
                                         source.Alliance,
                                         target.Alliance)
                                     && frame.TryGetCurrentTrack(
                                         source.Alliance,
                                         target.Flight.FlightId,
                                         out _))
                    .Select(target =>
                    {
                        frame.TryGetCurrentTrack(
                            source.Alliance,
                            target.Flight.FlightId,
                            out var track);
                        var bestMinutes = float.MaxValue;
                        if (TryResolvePotentialAirThreatCapability(
                                track,
                                frame,
                                ordnanceTypes,
                                AirThreatAssessmentPurpose.ProtectedAircraft,
                                out var capability))
                        {
                            foreach (var protectedFlight in protectedFlights)
                            {
                                if (TryCalculateMovingThreatEnvelopeEntry(
                                        track,
                                        protectedFlight.Flight,
                                        capability,
                                        frame,
                                        out var minutes,
                                        out _))
                                {
                                    bestMinutes = Math.Min(
                                        bestMinutes,
                                        minutes);
                                }
                            }
                        }

                        return new
                        {
                            target.Flight.FlightId,
                            Minutes = bestMinutes
                        };
                    })
                    .Where(candidate => candidate.Minutes
                                        < float.MaxValue)
                    .OrderBy(candidate => candidate.Minutes)
                    .ThenBy(candidate => candidate.FlightId)
                    .Select(candidate => candidate.FlightId)
                    .FirstOrDefault();
            }

            return frame.Flights.Values
                .Where(target => target != null
                                 && AreHostile(source.Alliance, target.Alliance)
                                 && frame.TryGetCurrentTrack(
                                     source.Alliance,
                                     target.Flight.FlightId,
                                     out _))
                .Select(target =>
                {
                    frame.TryGetCurrentTrack(
                        source.Alliance,
                        target.Flight.FlightId,
                        out var track);
                    var minutes = float.MaxValue;
                    var isThreat = TryResolvePotentialAirThreatCapability(
                                       track,
                                       frame,
                                       ordnanceTypes,
                                       AirThreatAssessmentPurpose.BarrierAssets,
                                       out _)
                                   && TryGetBarcapThreatGeometry(
                                       source,
                                       track,
                                       frame,
                                       ordnanceTypes,
                                       out _,
                                       out minutes);
                    return new
                    {
                        target.Flight.FlightId,
                        Minutes = isThreat ? minutes : float.MaxValue
                    };
                })
                .Where(candidate => candidate.Minutes < float.MaxValue)
                .OrderBy(candidate => candidate.Minutes)
                .ThenBy(candidate => candidate.FlightId)
                .Select(candidate => candidate.FlightId)
                .FirstOrDefault();
        }

        private static bool IsCommittedAgainstFlights(
            AirCombatFlightView attacker,
            IReadOnlyCollection<Guid> defendedFlightIds,
            AirCombatFrame frame)
        {
            if (attacker == null
                || defendedFlightIds == null
                || defendedFlightIds.Count == 0)
                return false;

            return frame.PendingEffects.Any(effect =>
                       effect.SourceKind
                       == OrdnanceEmploymentSourceKind.AircraftFlight
                       && effect.SourceFlightId == attacker.Flight.FlightId
                       && defendedFlightIds.Contains(effect.TargetFlightId)
                       && effect.ResolveAt > frame.Time)
                   || frame.ActivePasses.Any(pass =>
                       pass.SourceFlightId == attacker.Flight.FlightId
                       && defendedFlightIds.Contains(pass.TargetFlightId)
                       && pass.ReleaseAt > frame.Time)
                   || (attacker.Flight.TacticalState.Intent
                       == AirCombatIntent.EngageTarget
                       && defendedFlightIds.Contains(
                           attacker.Flight.TacticalState.TargetFlightId));
        }

        private static AirCombatCommand ContinueAssignedMission(
            AirCombatFlightView source,
            AirCombatFrame frame,
            string reason)
        {
            if (TryBuildCloseEscortCommand(source, frame, reason, out var command))
                return command;

            return RouteCommand(
                source,
                frame.Time,
                AirCombatIntent.FollowMission,
                reason);
        }

        private static bool TryBuildCloseEscortCommand(
            AirCombatFlightView source,
            AirCombatFrame frame,
            string reason,
            out AirCombatCommand command)
        {
            command = null;
            if (!source.Flight.IsCloseEscortActive
                || source.Flight.MissionAchieved
                || frame.Time >= source.Flight.EffectEnd)
                return false;

            var protectedFlights = GetActiveProtectedFlights(
                source,
                frame,
                executingOnly: true);
            if (protectedFlights.Count == 0)
                return false;

            var protectedCenter = protectedFlights.Aggregate(
                                      Vector3.zero,
                                      (sum, protectedFlight) =>
                                          sum + protectedFlight.Flight.PositionFeet)
                                  / protectedFlights.Count;
            var leadFlight = protectedFlights
                .OrderBy(protectedFlight => protectedFlight.Flight.FlightId)
                .First();
            var headingRadians = leadFlight.Flight.HeadingDegrees
                                 * Mathf.Deg2Rad;
            var leadDirection = new Vector3(
                Mathf.Sin(headingRadians),
                0f,
                Mathf.Cos(headingRadians));
            var aimPoint = protectedCenter
                           + leadDirection
                           * CloseEscortLeadDistanceKm
                           * AirspaceGeometry.FeetPerKilometer;
            aimPoint.y = Mathf.Clamp(
                protectedCenter.y + CloseEscortAltitudeOffsetFeet,
                1000f,
                source.AircraftType.ServiceCeilingFeet);
            var distanceKm = DistanceKm(
                source.Flight.PositionFeet,
                aimPoint);
            command = Command(
                source,
                AirCombatIntent.FollowMission,
                distanceKm > CloseEscortLeadDistanceKm
                    ? AirCombatManeuver.Intercept
                    : AirCombatManeuver.Press,
                Guid.Empty,
                Guid.Empty,
                frame.Time,
                frame.Time.AddSeconds(15),
                AirCombatManeuverSide.None,
                aimPoint,
                Math.Max(1f, source.AircraftType.CombatSpeedKnots),
                reason + " Maintaining close cover over the protected element.");
            return true;
        }

        private static HashSet<Guid> GetProtectedFlightIds(
            AirCombatFlightView source)
        {
            return source.Flight.ProtectedFlightIds.Count > 0
                ? source.Flight.ProtectedFlightIds.ToHashSet()
                : source.Package.Flights
                    .Where(flight => !flight.IsFighterEscort)
                    .Select(flight => flight.FlightId)
                    .ToHashSet();
        }

        private static List<AirCombatFlightView> GetActiveProtectedFlights(
            AirCombatFlightView source,
            AirCombatFrame frame,
            bool executingOnly)
        {
            return GetProtectedFlightIds(source)
                .Select(id => frame.Flights.TryGetValue(id, out var view)
                    ? view
                    : null)
                .Where(view => view != null
                               && view.Flight.IsAirborne
                               && view.Flight.ExecutionPhase
                               != FlightExecutionPhase.Landing
                               && (!executingOnly
                                   || view.Flight.ExecutionPhase
                                   == FlightExecutionPhase.Executing)
                               && view.LiveAircraft.Count > 0)
                .ToList();
        }

        private static bool TryGetBarcapThreatMinutes(
            AirCombatFlightView source,
            IADSTrack track,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            out float minutesToEntry)
        {
            minutesToEntry = float.MaxValue;
            if (!TryResolvePotentialAirThreatCapability(
                    track,
                    frame,
                    ordnanceTypes,
                    AirThreatAssessmentPurpose.BarrierAssets,
                    out _)
                || !TryGetBarcapThreatGeometry(
                    source,
                    track,
                    frame,
                    ordnanceTypes,
                    out var crossingFeet,
                    out minutesToEntry))
                return false;

            if (!IsThreatCoursePersistentOrUrgent(
                    source.Flight,
                    track.FlightId,
                    frame.Time,
                    minutesToEntry))
                return false;

            if (source.Flight.ActiveBarcapCoverage
                is not BarcapStationCoverage)
                return true;

            var interceptMinutes = CalculateBarcapInterceptMinutes(
                source,
                crossingFeet,
                ordnanceTypes);
            // Committing shortens the run to the crossing point, which would
            // immediately fail a single-threshold gate and send the flight back
            // to station. Release on a wider margin than we commit on so an
            // engagement that has already started stays committed.
            var isCommittedToTrack =
                source.Flight.TacticalState.Intent
                == AirCombatIntent.EngageTarget
                && source.Flight.TacticalState.TargetFlightId
                == track.FlightId;
            var marginMinutes = isCommittedToTrack
                ? BarcapReleaseMarginMinutes
                : BarcapCommitMarginMinutes;
            return minutesToEntry <= interceptMinutes + marginMinutes;
        }

        private static bool TryGetBarcapThreatGeometry(
            AirCombatFlightView source,
            IADSTrack track,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            out Vector3 crossingFeet,
            out float minutesToEntry)
        {
            crossingFeet = default;
            minutesToEntry = float.MaxValue;
            if (track == null || track.IsStale)
                return false;

            var coverage = source.Flight.ActiveBarcapCoverage;
            if (coverage?.CoveredBarrierTileIds?.Count >= 1)
            {
                var currentResponseDepthKm =
                    CalculateCurrentBarcapResponseDepthKm(
                        source,
                        coverage,
                        ordnanceTypes);
                if (BarcapInterceptGeometry.IsOnDefendedSide(
                        track.LastKnownPositionFeet,
                        coverage.CoveredBarrierTileIds,
                        coverage.ThreatReferenceTileId,
                        frame.TileDistanceKm,
                        coverage.WeaponReleaseStandoffKm)
                    && DistanceToBarrierKm(
                           track.LastKnownPositionFeet,
                           coverage.CoveredBarrierTileIds,
                           coverage.ThreatReferenceTileId,
                           frame.TileDistanceKm,
                           coverage.WeaponReleaseStandoffKm)
                       <= currentResponseDepthKm)
                {
                    minutesToEntry = 0f;
                    crossingFeet = track.LastKnownPositionFeet;
                    return true;
                }

                if (!BarcapInterceptGeometry.TryPredictBarrierCrossing(
                        track.LastKnownPositionFeet,
                        track.EstimatedHeadingDegrees,
                        track.EstimatedSpeedKnots,
                        coverage.CoveredBarrierTileIds,
                        coverage.ThreatReferenceTileId,
                        frame.TileDistanceKm,
                        coverage.WeaponReleaseStandoffKm,
                        BarcapThreatLookaheadMinutes,
                        out crossingFeet,
                        out var minutesToCrossing))
                    return false;

                minutesToEntry = minutesToCrossing;
                return true;
            }

            // Legacy or authored BARCAP routes without a materialized barrier
            // assignment retain bounded circular-area authorization.
            var area = source.Flight.MissionArea;
            var centerFeet = AirspaceGeometry.TileCenterFeet(
                area.CenterTileId,
                frame.TileDistanceKm);
            var center = new Vector2(centerFeet.x, centerFeet.z);
            var position = new Vector2(
                track.LastKnownPositionFeet.x,
                track.LastKnownPositionFeet.z);
            var radiusFeet = area.RadiusKm
                              * AirspaceGeometry.FeetPerKilometer;
            var distance = Vector2.Distance(center, position);
            if (distance <= radiusFeet)
            {
                minutesToEntry = 0f;
                crossingFeet = track.LastKnownPositionFeet;
                return true;
            }

            var responseRadiusFeet = (area.RadiusKm
                                      + BarcapResponsePaddingTiles
                                      * frame.TileDistanceKm)
                                     * AirspaceGeometry.FeetPerKilometer;
            if (distance > responseRadiusFeet)
                return false;

            var velocity3 = Direction(track.EstimatedHeadingDegrees)
                            * Math.Max(0f, track.EstimatedSpeedKnots)
                            * 1.68781f;
            var velocity = new Vector2(velocity3.x, velocity3.z);
            var relative = position - center;
            var a = velocity.sqrMagnitude;
            if (a < 1f)
                return false;
            var b = 2f * Vector2.Dot(relative, velocity);
            var c = relative.sqrMagnitude - radiusFeet * radiusFeet;
            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
                return false;
            var secondsToEntry = (-b - Mathf.Sqrt(discriminant)) / (2f * a);
            if (secondsToEntry < 0f
                || secondsToEntry > BarcapThreatLookaheadMinutes * 60f)
                return false;

            minutesToEntry = secondsToEntry / 60f;
            crossingFeet = new Vector3(
                position.x + velocity.x * secondsToEntry,
                track.LastKnownPositionFeet.y,
                position.y + velocity.y * secondsToEntry);
            return true;
        }

        private static float CalculateFlightAirCombatPower(
            AirCombatFlightView view,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
            if (view == null
                || view.LiveAircraft.Count == 0
                || view.AircraftType.AirInterferenceCapability <= 0f
                || !view.LiveAircraft.Any(aircraft => aircraft.Loadout.Any(item =>
                    item.Count > 0
                    && ordnanceTypes.TryGetValue(
                        item.OrdnanceTypeDefinitionId,
                        out var ordnance)
                    && IsAirToAir(ordnance))))
                return 0f;

            return view.LiveAircraft.Count * view.AircraftType.AirInterferenceCapability;
        }

        private static float CalculateThreatPower(IADSTrack track)
        {
            return Mathf.Max(0f, track?.EstimatedAirCombatPower ?? 0f);
        }

        private static bool IsSelfDefenseTarget(
            AirCombatFlightView source,
            AirCombatFlightView target,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
            return frame.PendingEffects.Any(effect =>
                       effect.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight
                       && effect.SourceFlightId == target.Flight.FlightId
                       && effect.TargetFlightId == source.Flight.FlightId
                       && effect.ResolveAt > frame.Time)
                   || IsHotThreatWithinSelfDefenseEnvelope(
                       source,
                       target,
                       frame,
                       ordnanceTypes);
        }

        private static bool IsOcaProactiveTargetAuthorized(
            AirCombatFlightView source,
            AirCombatFlightView target,
            AirCombatFrame frame,
            AllianceAirDoctrine doctrine,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            out string reason)
        {
            if (!frame.TryGetCurrentTrack(
                    source.Alliance,
                    target.Flight.FlightId,
                    out var track))
            {
                reason = "IADS contact was lost; extending toward the sweep route.";
                return false;
            }

            var observedTargetPosition = track.LastKnownPositionFeet;
            if (!IsInsideSweepCorridor(source.Flight, observedTargetPosition, frame))
            {
                reason = "Target left the assigned sweep corridor; extending toward the sweep route.";
                return false;
            }

            var friendlyPower = CalculateLocalAirCombatPower(
                frame,
                source.Alliance,
                source.Alliance,
                source.Package.PackageId,
                ordnanceTypes,
                observedTargetPosition);
            var hostilePower = CalculateLocalAirCombatPower(
                frame,
                source.Alliance,
                target.Alliance,
                Guid.Empty,
                ordnanceTypes,
                observedTargetPosition);
            var liveRatio = friendlyPower / Math.Max(0.1f, hostilePower);
            var requiredRatio = Mathf.Lerp(
                Math.Max(1f, doctrine.DesiredAirCombatAdvantage),
                0.75f,
                Mathf.Clamp01(doctrine.RiskTolerance));
            if (liveRatio < requiredRatio)
            {
                reason = $"Local combat odds fell to {liveRatio:0.00}:1; extending from the engagement.";
                return false;
            }

            // TODO: Recalculate OCA background-interference risk when OCA
            // tactical behavior is reworked. The legacy relative air-control
            // gate is intentionally removed; live local combat odds above
            // remain authoritative for now.

            reason = string.Empty;
            return true;
        }

        private static float CalculateLocalAirCombatPower(
            AirCombatFrame frame,
            Alliance observingAlliance,
            Alliance alliance,
            Guid packageId,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            Vector3 centerFeet)
        {
            var radiusKm = Math.Max(1f, frame.TileDistanceKm * 2f);
            var totalPower = 0f;
            foreach (var view in frame.Flights.Values)
            {
                if (view.Alliance != alliance
                    || packageId != Guid.Empty && view.Package.PackageId != packageId)
                    continue;

                Vector3 position;
                float combatPower;
                if (view.Alliance == observingAlliance)
                {
                    if (!view.Flight.IsAirborne
                        || view.LiveAircraft.Count == 0
                        || view.AircraftType.AirInterferenceCapability <= 0f
                        || !view.LiveAircraft.Any(aircraft => aircraft.Loadout.Any(item =>
                            item.Count > 0
                            && ordnanceTypes.TryGetValue(
                                item.OrdnanceTypeDefinitionId,
                                out var ordnance)
                            && IsAirToAir(ordnance))))
                        continue;

                    position = view.Flight.PositionFeet;
                    combatPower = view.LiveAircraft.Count
                                  * view.AircraftType.AirInterferenceCapability;
                }
                else
                {
                    if (!frame.TryGetCurrentTrack(
                            observingAlliance,
                            view.Flight.FlightId,
                            out var track))
                        continue;

                    position = track.LastKnownPositionFeet;
                    combatPower = track.EstimatedAirCombatPower;
                }

                if (combatPower <= 0f || DistanceKm(position, centerFeet) > radiusKm)
                    continue;

                totalPower += combatPower;
            }

            return totalPower;
        }

        private static bool IsInsideSweepCorridor(
            AirFlight source,
            Vector3 targetPosition,
            AirCombatFrame frame)
        {
            var entry = source.Route.FirstOrDefault(waypoint =>
                waypoint.Action == AirWaypointAction.StationEntry);
            var endpoint = entry == null
                ? null
                : source.Route.FirstOrDefault(waypoint =>
                    waypoint.Action == AirWaypointAction.StationEndpoint
                    && waypoint.RepeatFromWaypointId == entry.WaypointId);
            if (entry == null || endpoint == null)
            {
                return IsInsideMissionArea(
                    source.MissionArea,
                    targetPosition,
                    frame.TileDistanceKm,
                    0f);
            }

            var start = new Vector2(entry.PositionFeet.x, entry.PositionFeet.z);
            var end = new Vector2(endpoint.PositionFeet.x, endpoint.PositionFeet.z);
            var target = new Vector2(targetPosition.x, targetPosition.z);
            var segment = end - start;
            var segmentLengthSquared = segment.sqrMagnitude;
            if (segmentLengthSquared < 1f)
                return Vector2.Distance(start, target) <= frame.TileDistanceKm
                       * AirspaceGeometry.FeetPerKilometer;

            var progress = Vector2.Dot(target - start, segment) / segmentLengthSquared;
            if (progress < -0.25f || progress > 1.15f)
                return false;

            var closest = start + segment * Mathf.Clamp01(progress);
            var corridorHalfWidthFeet = frame.TileDistanceKm
                                        * AirspaceGeometry.FeetPerKilometer;
            return Vector2.Distance(closest, target) <= corridorHalfWidthFeet;
        }

        private static bool IsHotThreatWithinSelfDefenseEnvelope(
            AirCombatFlightView source,
            AirCombatFlightView target,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
            if (!frame.TryGetCurrentTrack(
                    source.Alliance,
                    target.Flight.FlightId,
                    out var track))
                return false;

            if (source.Flight.MissionType
                == AirMissionRequestType.BarrierCombatAirPatrol)
            {
                if (!TryResolvePotentialAirThreatCapability(
                        track,
                        frame,
                        ordnanceTypes,
                        AirThreatAssessmentPurpose.BarrierAssets,
                        out _))
                    return false;

                if (source.Flight.ActiveBarcapCoverage
                        is BarcapStationCoverage coverage
                    && coverage.CoveredBarrierTileIds != null
                    && coverage.CoveredBarrierTileIds.Count > 0
                    && !IsBarcapDefensiveContact(
                        source,
                        track,
                        frame,
                        ordnanceTypes))
                    return false;
            }

            if (TargetAspect(source.Flight, track) > HotThreatAspectDegrees)
                return false;

            var distanceKm = DistanceKm(
                source.Flight.PositionFeet,
                track.LastKnownPositionFeet);
            var targetAltitudeFeet = track.LastKnownPositionFeet.y;
            return source.LiveAircraft
                .SelectMany(aircraft => aircraft.Loadout)
                .Any(item => item.Count > 0
                             && ordnanceTypes.TryGetValue(
                                 item.OrdnanceTypeDefinitionId,
                                 out var ordnance)
                             && IsAirToAir(ordnance)
                             && distanceKm >= ordnance.MinimumRangeKm
                             && distanceKm <= EffectiveLaunchEnvelopeKm(
                                 ordnance,
                                 source.Flight,
                                 track)
                             && targetAltitudeFeet >= ordnance.MinimumTargetAltitudeFeet
                             && targetAltitudeFeet <= ordnance.MaximumTargetAltitudeFeet);
        }

        private static bool IsBarcapDefensiveContact(
            AirCombatFlightView source,
            IADSTrack track,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
            return TryGetBarcapThreatMinutes(
                source,
                track,
                frame,
                ordnanceTypes,
                out _);
        }

        private static AirCombatCommand EnforceBarcapDefensiveBoundary(
            AirCombatFlightView source,
            AirCombatFrame frame,
            AirCombatCommand command)
        {
            var coverage = source.Flight.ActiveBarcapCoverage;
            if (source.Flight.MissionType
                    != AirMissionRequestType.BarrierCombatAirPatrol
                || command == null
                || coverage?.CoveredBarrierTileIds == null
                || coverage.CoveredBarrierTileIds.Count < 1
                || !command.HasAimPoint
                || command.Maneuver == AirCombatManeuver.FollowRoute)
                return command;

            var toleranceKm = frame.TileDistanceKm
                              * BarcapBoundaryToleranceTiles;
            if (command.Intent == AirCombatIntent.EngageTarget
                && !BarcapInterceptGeometry.IsOnDefendedSide(
                    source.Flight.PositionFeet,
                    coverage.CoveredBarrierTileIds,
                    coverage.ThreatReferenceTileId,
                    frame.TileDistanceKm,
                    coverage.WeaponReleaseStandoffKm,
                    toleranceKm))
            {
                return RouteCommand(
                    source,
                    frame.Time,
                    AirCombatIntent.FollowMission,
                    "Defensive barrier reached; returning to the BARCAP station.");
            }

            var clampedAim = BarcapInterceptGeometry.ClampToDefendedSide(
                command.AimPointFeet,
                coverage.CoveredBarrierTileIds,
                coverage.ThreatReferenceTileId,
                frame.TileDistanceKm,
                coverage.WeaponReleaseStandoffKm,
                frame.TileDistanceKm * BarcapDefensiveBufferTiles);
            if ((clampedAim - command.AimPointFeet).sqrMagnitude < 1f)
                return command;

            command.AimPointFeet = clampedAim;
            command.Reason = string.IsNullOrWhiteSpace(command.Reason)
                ? "Holding on the defended side of the BARCAP barrier."
                : command.Reason
                  + " Holding on the defended side of the BARCAP barrier.";
            return command;
        }

        private static AirCombatCommand EnforceKnownSamAvoidance(
            AirCombatFlightView source,
            AirCombatFrame frame,
            AirCombatCommand command,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
            if (source?.Flight == null
                || command == null
                || command.Intent == AirCombatIntent.Defend)
                return command;

            var threats = frame.GetKnownSamThreats(source.Alliance);
            if (source.Flight.ClearedSurfaceThreatSiteIds.Count > 0)
            {
                threats = threats
                    .Where(threat => !source.Flight
                        .ClearedSurfaceThreatSiteIds.Contains(threat.SiteId))
                    .ToList();
            }
            if (ShouldIgnoreAuthorizedSurfaceThreat(source, frame))
            {
                threats = threats
                    .Where(threat => threat.SiteId
                                     != source.Flight
                                         .AuthorizedSurfaceThreatSiteId)
                    .ToList();
            }
            if (threats.Count == 0)
                return command;

            var currentPosition = source.Flight.PositionFeet;
            var maneuverClearanceFeet = AirspaceGeometry
                .ConservativeSamManeuverClearanceFeet(
                    source.AircraftType);
            var activePass = frame.ActivePasses
                .Where(pass => pass.SourceFlightId == source.Flight.FlightId
                               && pass.TargetKind
                               == OrdnanceEmploymentTargetKind.AirFlight)
                .OrderBy(pass => pass.ReleaseAt)
                .ThenBy(pass => pass.EmploymentPassId)
                .FirstOrDefault();
            var currentlyInsideThreat = threats.Any(threat =>
                threat != null && threat.Contains(currentPosition));
            if (KnownSamThreatGeometry.TryCreateEgressAimPoint(
                    currentPosition,
                    threats,
                    source.Flight.FlightId,
                    maneuverClearanceFeet,
                    out var egressAimPoint))
            {
                var egress = Command(
                    source,
                    AirCombatIntent.Disengage,
                    AirCombatManeuver.AvoidSurfaceThreat,
                    Guid.Empty,
                    Guid.Empty,
                    frame.Time,
                    frame.Time.AddSeconds(15),
                    AirCombatManeuverSide.None,
                    egressAimPoint,
                    Math.Max(1f, source.AircraftType.CombatSpeedKnots),
                    "Leaving a known SAM engagement envelope.");
                egress.RequestsAirToAirPassCancellation = activePass != null;
                return egress;
            }
            if (currentlyInsideThreat)
            {
                command.RequestsSurfaceThreatRecovery = true;
                command.RequestsWvrEngagement = false;
                command.Employment = null;
                command.RequestsAirToAirPassCancellation = activePass != null;
                command.Reason =
                    "Unable to find a safe egress from overlapping known SAM coverage; recovering.";
                return command;
            }

            var desiredAimPoint = command.Maneuver == AirCombatManeuver.FollowRoute
                ? source.Flight.CurrentWaypoint?.PositionFeet
                  ?? command.AimPointFeet
                : command.AimPointFeet;
            if (!command.HasAimPoint && source.Flight.CurrentWaypoint == null)
                return command;

            var desiredInsideThreat = threats.Any(threat =>
                threat != null && threat.Contains(desiredAimPoint));
            if (desiredInsideThreat
                && command.Intent != AirCombatIntent.EngageTarget)
            {
                command.RequestsSurfaceThreatRecovery = true;
                command.RequestsWvrEngagement = false;
                command.Employment = null;
                command.RequestsAirToAirPassCancellation = activePass != null;
                command.Reason =
                    "Assigned route now terminates inside known SAM coverage; recovering.";
                return command;
            }

            var hasPendingEmployment =
                command.Employment != null || activePass != null;
            var blockingLaunchSiteId = Guid.Empty;
            var launchSupportPlanSafe = !hasPendingEmployment
                                        || IsLaunchSupportPlanSafe(
                                            source,
                                            frame,
                                            command,
                                            activePass,
                                            threats,
                                            ordnanceTypes,
                                            out blockingLaunchSiteId);
            if (hasPendingEmployment && launchSupportPlanSafe)
                return command;
            if (hasPendingEmployment)
            {
                command = RouteCommand(
                    source,
                    frame.Time,
                    AirCombatIntent.FollowMission,
                    $"Withholding air-to-air employment because the required "
                    + $"launch-support maneuver would enter known SAM coverage "
                    + $"from site {ShortId(blockingLaunchSiteId)}.");
                command.RequestsAirToAirPassCancellation = activePass != null;
                desiredAimPoint = source.Flight.CurrentWaypoint?.PositionFeet
                                  ?? command.AimPointFeet;
                desiredInsideThreat = threats.Any(threat =>
                    threat != null && threat.Contains(desiredAimPoint));
            }

            if (KnownSamThreatGeometry.TryCreateAvoidanceAimPoint(
                    currentPosition,
                    desiredAimPoint,
                    threats,
                    source.Flight.FlightId,
                    maneuverClearanceFeet,
                    out var avoidanceAimPoint,
                    out var blockingSiteId))
            {
                var avoidance = Command(
                    source,
                    command.Intent == AirCombatIntent.Recover
                        ? AirCombatIntent.Recover
                        : AirCombatIntent.FollowMission,
                    AirCombatManeuver.AvoidSurfaceThreat,
                    command.TargetFlightId,
                    Guid.Empty,
                    frame.Time,
                    frame.Time.AddSeconds(10),
                    AirCombatManeuverSide.None,
                    avoidanceAimPoint,
                    Math.Max(1f, source.AircraftType.CombatSpeedKnots),
                    command.Intent == AirCombatIntent.EngageTarget
                        ? $"Declining pursuit through known SAM coverage from site "
                          + $"{ShortId(blockingSiteId)} while preserving a safe intercept."
                        : $"Routing around known SAM coverage from site "
                          + $"{ShortId(blockingSiteId)}.");
                avoidance.RequestsAirToAirPassCancellation =
                    command.RequestsAirToAirPassCancellation;
                return avoidance;
            }

            if (KnownSamThreatGeometry.IsPathSafe(
                    new[] { currentPosition, desiredAimPoint },
                    threats,
                    maneuverClearanceFeet,
                    out _))
                return command;

            command.RequestsSurfaceThreatRecovery = true;
            command.RequestsWvrEngagement = false;
            command.Employment = null;
            command.RequestsAirToAirPassCancellation =
                command.RequestsAirToAirPassCancellation || activePass != null;
            command.Reason =
                "No flyable route around known SAM coverage could be found; recovering.";
            return command;
        }

        private static bool ShouldIgnoreAuthorizedSurfaceThreat(
            AirCombatFlightView source,
            AirCombatFrame frame)
        {
            var flight = source.Flight;
            var siteId = flight.AuthorizedSurfaceThreatSiteId;
            if (siteId == Guid.Empty
                || !flight.IsDeadAttackFlight
                || (flight.ExecutionPhase != FlightExecutionPhase.Outbound
                    && flight.ExecutionPhase
                    != FlightExecutionPhase.Executing))
                return false;

            if (flight.AuthorizedSurfaceThreatPenetrationGranted)
                return true;

            return frame.ActivePasses.Any(pass =>
                       pass.SourceFlightId == flight.FlightId
                       && pass.TargetKind
                       == OrdnanceEmploymentTargetKind.AirDefenseComponent
                       && pass.TargetSiteId == siteId)
                   || frame.PendingEffects.Any(effect =>
                       effect.SourceFlightId == flight.FlightId
                       && effect.TargetKind
                       == OrdnanceEmploymentTargetKind.AirDefenseComponent
                       && effect.TargetSiteId == siteId
                       && effect.ResolveAt > frame.Time);
        }

        private static bool IsLaunchSupportPlanSafe(
            AirCombatFlightView source,
            AirCombatFrame frame,
            AirCombatCommand command,
            ActiveOrdnanceEmploymentPass activePass,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            out Guid blockingSiteId)
        {
            blockingSiteId = Guid.Empty;
            var ordnanceId = command.Employment?.OrdnanceTypeDefinitionId
                             ?? activePass?.OrdnanceTypeDefinitionId
                             ?? Guid.Empty;
            var targetFlightId = command.Employment?.TargetFlightId
                                 ?? activePass?.TargetFlightId
                                 ?? command.TargetFlightId;
            if (ordnanceId == Guid.Empty
                || targetFlightId == Guid.Empty
                || !ordnanceTypes.TryGetValue(ordnanceId, out var ordnance)
                || !frame.Flights.TryGetValue(
                    targetFlightId,
                    out var target))
                return true;

            var preparationSeconds = Math.Max(
                0d,
                activePass != null
                    ? (activePass.ReleaseAt - frame.Time).TotalSeconds
                    : ordnance.PreparationSeconds
                      / Math.Max(
                          0.01f,
                          source.AircraftType.OrdnanceEmploymentEfficiency));
            if (double.IsNaN(preparationSeconds)
                || double.IsInfinity(preparationSeconds))
                return false;

            var sourceFeetPerSecond =
                Math.Max(1f, source.AircraftType.CombatSpeedKnots)
                * AirspaceGeometry.FeetPerNauticalMile / 3600f;
            var maneuverClearanceFeet = AirspaceGeometry
                .ConservativeSamManeuverClearanceFeet(
                    source.AircraftType);
            var predictedSource = source.Flight.PositionFeet;
            var predictedHeading = source.Flight.HeadingDegrees;
            var predictionSeconds = 0d;
            while (predictionSeconds < preparationSeconds)
            {
                var stepSeconds = GetLaunchSupportPredictionStepSeconds(
                    preparationSeconds,
                    predictionSeconds);
                var predictedTarget = PredictPosition(
                    target.Flight,
                    (float)(predictionSeconds + stepSeconds));
                var desiredHeading = HeadingTo(
                    predictedSource,
                    predictedTarget);
                var nextSource = PredictManeuverStep(
                    predictedSource,
                    ref predictedHeading,
                    desiredHeading,
                    predictedTarget.y,
                    source.AircraftType,
                    sourceFeetPerSecond,
                    stepSeconds);
                if (TryGetBlockingThreat(
                        predictedSource,
                        nextSource,
                        threats,
                        maneuverClearanceFeet,
                        out blockingSiteId))
                    return false;

                predictedSource = nextSource;
                predictionSeconds += stepSeconds;
            }

            if (!ordnance.RequiresSupportUntilAutonomous
                && ordnance.GuidanceMode
                != OrdnanceGuidanceMode.SemiActiveRadar)
                return true;

            var predictedTargetAtRelease = PredictPosition(
                target.Flight,
                (float)preparationSeconds);
            var side = StableSide(
                source.Flight.FlightId,
                target.Flight.FlightId);
            var missileTravelSeconds =
                AirspaceGeometry.HorizontalTravelSeconds(
                    Vector3.Distance(
                        predictedSource,
                        predictedTargetAtRelease),
                    ordnance.EffectSpeedKnots);
            var supportSeconds = ordnance.GuidanceMode
                                 == OrdnanceGuidanceMode.SemiActiveRadar
                ? missileTravelSeconds
                : Math.Min(
                    missileTravelSeconds,
                    ordnance.SecondsUntilAutonomous);
            if (double.IsNaN(supportSeconds)
                || double.IsInfinity(supportSeconds))
                return false;

            var supportElapsedSeconds = 0d;
            while (supportElapsedSeconds < supportSeconds)
            {
                var stepSeconds = GetLaunchSupportPredictionStepSeconds(
                    supportSeconds,
                    supportElapsedSeconds);
                var predictedTarget = PredictPosition(
                    target.Flight,
                    (float)preparationSeconds
                    + (float)supportElapsedSeconds
                    + stepSeconds);
                var crankHeading = HeadingTo(
                                       predictedSource,
                                       predictedTarget)
                                   + (side == AirCombatManeuverSide.Left
                                       ? -CrankOffsetDegrees
                                       : CrankOffsetDegrees);
                var nextSource = PredictManeuverStep(
                    predictedSource,
                    ref predictedHeading,
                    crankHeading,
                    predictedSource.y,
                    source.AircraftType,
                    sourceFeetPerSecond,
                    stepSeconds);
                if (TryGetBlockingThreat(
                        predictedSource,
                        nextSource,
                        threats,
                        maneuverClearanceFeet,
                        out blockingSiteId))
                    return false;

                predictedSource = nextSource;
                supportElapsedSeconds += stepSeconds;
            }

            return true;
        }

        private static float GetLaunchSupportPredictionStepSeconds(
            double totalSeconds,
            double elapsedSeconds)
        {
            var remainingSeconds = Math.Max(0d, totalSeconds - elapsedSeconds);
            var boundedStepSeconds = Math.Max(
                LaunchSupportPredictionStepSeconds,
                totalSeconds / MaximumLaunchSupportPredictionSteps);
            return (float)Math.Min(remainingSeconds, boundedStepSeconds);
        }

        private static Vector3 PredictPosition(
            AirFlight flight,
            float seconds)
        {
            var feetPerSecond = Math.Max(0f, flight.SpeedKnots)
                                * AirspaceGeometry.FeetPerNauticalMile / 3600f;
            var predicted = flight.PositionFeet
                            + Direction(flight.HeadingDegrees)
                            * feetPerSecond
                            * Math.Max(0f, seconds);
            predicted.y = flight.PositionFeet.y;
            return predicted;
        }

        private static Vector3 PredictManeuverStep(
            Vector3 position,
            ref float headingDegrees,
            float desiredHeadingDegrees,
            float desiredAltitudeFeet,
            AircraftTypeDefinition aircraftType,
            float feetPerSecond,
            float seconds)
        {
            headingDegrees = Mathf.MoveTowardsAngle(
                headingDegrees,
                desiredHeadingDegrees,
                aircraftType.TurnRateDegreesPerSecond * seconds);
            var next = position
                       + Direction(headingDegrees)
                       * feetPerSecond
                       * seconds;
            var verticalRate = (desiredAltitudeFeet >= position.y
                                   ? aircraftType.ClimbRateFeetPerMinute
                                   : aircraftType.DescentRateFeetPerMinute) / 60f;
            next.y = Mathf.MoveTowards(
                position.y,
                desiredAltitudeFeet,
                Math.Max(1f, verticalRate) * seconds);
            return next;
        }

        private static bool TryGetBlockingThreat(
            Vector3 startFeet,
            Vector3 endFeet,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            float maneuverClearanceFeet,
            out Guid blockingSiteId)
        {
            var blocking = threats
                .Where(threat => threat != null
                                 && threat.IntersectsSegment(
                                     startFeet,
                                     endFeet,
                                     maneuverClearanceFeet))
                .OrderBy(threat => threat.SiteId)
                .FirstOrDefault();
            blockingSiteId = blocking?.SiteId ?? Guid.Empty;
            return blocking != null;
        }

        private static float DistanceToBarrierKm(
            Vector3 positionFeet,
            IReadOnlyList<Vector3Int> coveredBarrierTiles,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm,
            float weaponReleaseStandoffKm)
        {
            if (coveredBarrierTiles == null || coveredBarrierTiles.Count == 0)
                return float.MaxValue;

            return BarcapInterceptGeometry.DistanceToOperationalBarrierKm(
                positionFeet,
                coveredBarrierTiles,
                threatReferenceTileId,
                tileDistanceKm,
                weaponReleaseStandoffKm);
        }

        private static float CalculateCurrentBarcapResponseDepthKm(
            AirCombatFlightView source,
            BarcapStationCoverage coverage,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
            var currentPreferredLaunchRangeKm = source.LiveAircraft
                .SelectMany(aircraft => aircraft.Loadout)
                .Where(item => item.Count > 0
                               && ordnanceTypes.TryGetValue(
                                   item.OrdnanceTypeDefinitionId,
                                   out var definition)
                               && IsAirToAir(definition))
                .Select(item => ordnanceTypes[item.OrdnanceTypeDefinitionId])
                .Select(definition =>
                    EffectiveMaximumRangeKm(
                        definition,
                        source.Flight)
                    * PreferredRangeFraction)
                .DefaultIfEmpty(0f)
                .Max();
            var plannedKinematicResponseKm = Math.Max(
                0f,
                coverage.PlannedResponseRadiusKm
                - coverage.PlannedPreferredLaunchRangeKm);
            return Mathf.Clamp(
                plannedKinematicResponseKm + currentPreferredLaunchRangeKm,
                0f,
                coverage.PlannedResponseRadiusKm);
        }

        private static float CalculateBarcapInterceptMinutes(
            AirCombatFlightView source,
            Vector3 crossingFeet,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
            return CalculateAirInterceptMinutes(
                source,
                crossingFeet,
                ordnanceTypes);
        }

        private static float CalculateAirInterceptMinutes(
            AirCombatFlightView source,
            Vector3 interceptPointFeet,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
            var weapon = source.LiveAircraft
                .SelectMany(aircraft => aircraft.Loadout)
                .Where(item => item.Count > 0
                               && ordnanceTypes.TryGetValue(
                                   item.OrdnanceTypeDefinitionId,
                                   out var definition)
                               && IsAirToAir(definition))
                .Select(item => ordnanceTypes[item.OrdnanceTypeDefinitionId])
                .OrderByDescending(definition =>
                    EffectiveMaximumRangeKm(
                        definition,
                        source.Flight))
                .ThenBy(definition => definition.PreparationSeconds)
                .ThenBy(definition => definition.OrdnanceTypeDefinitionId)
                .FirstOrDefault();
            var preferredLaunchRangeKm = weapon == null
                ? 0f
                : EffectiveMaximumRangeKm(weapon, source.Flight)
                  * PreferredRangeFraction;
            var travelDistanceKm = Math.Max(
                0f,
                DistanceKm(source.Flight.PositionFeet, interceptPointFeet)
                - preferredLaunchRangeKm);
            var travelMinutes = travelDistanceKm
                                / Math.Max(
                                    1f,
                                    source.AircraftType.CombatSpeedKnots
                                    * 1.852f)
                                * 60f;
            var preparationMinutes = weapon == null
                ? 0f
                : weapon.PreparationSeconds
                  / Math.Max(
                      0.01f,
                      source.AircraftType.OrdnanceEmploymentEfficiency)
                  / 60f;
            return travelMinutes + preparationMinutes;
        }

        private static bool IsCounterAirFlight(AirFlight flight)
        {
            return flight != null
                   && (flight.IsFighterEscort
                       || flight.MissionType
                       == AirMissionRequestType.BarrierCombatAirPatrol
                       || flight.MissionType
                       == AirMissionRequestType.OffensiveCounterAirSweep);
        }

        private static bool IsInsideMissionArea(
            AirMissionArea area,
            Vector3 positionFeet,
            float tileDistanceKm,
            float paddingTiles)
        {
            var center = AirspaceGeometry.TileCenterFeet(area.CenterTileId, tileDistanceKm);
            var horizontalDistance = Vector2.Distance(
                new Vector2(center.x, center.z),
                new Vector2(positionFeet.x, positionFeet.z));
            var radiusFeet = (area.RadiusKm + paddingTiles * tileDistanceKm)
                             * AirspaceGeometry.FeetPerKilometer;
            return horizontalDistance <= radiusFeet;
        }

        private static AirToAirShotAssessment SelectShotAssessment(
            AirCombatFlightView source,
            AirCombatFlightView target,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            AllianceAirDoctrine doctrine)
        {
            var distanceKm = DistanceKm(source.Flight.PositionFeet, target.Flight.PositionFeet);
            return GetAvailableAirToAirWeapons(source, ordnanceTypes)
                .Select(definition =>
                {
                    var assessment = AssessShot(
                        source,
                        target,
                        definition,
                        doctrine.MinimumLaunchQuality);
                    var reserve = definition.EmploymentCategory
                                  == OrdnanceEmploymentCategory.Gun
                        ? 0
                        : doctrine.MinimumAirToAirWeaponReserve;
                    var hasExpendableRounds = CountRounds(
                                                  source,
                                                  definition.OrdnanceTypeDefinitionId)
                                              > reserve;
                    return new
                    {
                        Assessment = assessment,
                        HasExpendableRounds = hasExpendableRounds,
                        Definition = definition
                    };
                })
                .Where(candidate => candidate.HasExpendableRounds)
                .OrderBy(candidate => GetShotSetupPriority(
                    candidate.Assessment.Status))
                .ThenBy(candidate =>
                    candidate.Assessment.Status == AirToAirShotStatus.Ready
                    && candidate.Definition.EmploymentCategory
                    == OrdnanceEmploymentCategory.Gun
                        ? 1
                        : 0)
                .ThenByDescending(candidate =>
                    candidate.Assessment.Status == AirToAirShotStatus.Ready
                    || candidate.Assessment.Status == AirToAirShotStatus.LowQuality
                        ? candidate.Assessment.LaunchQuality
                    : 0f)
                .ThenBy(candidate => GetWeaponSetupPriority(
                    candidate.Definition,
                    distanceKm))
                .ThenByDescending(candidate => candidate.Assessment.MaximumRangeKm)
                .ThenByDescending(candidate => candidate.Definition.HitProbability)
                .ThenBy(candidate => candidate.Definition.OrdnanceTypeDefinitionId)
                .Select(candidate => candidate.Assessment)
                .FirstOrDefault();
        }

        private static int GetShotSetupPriority(AirToAirShotStatus status)
        {
            switch (status)
            {
                case AirToAirShotStatus.Ready:
                    return 0;
                case AirToAirShotStatus.NeedsPointing:
                    return 1;
                case AirToAirShotStatus.LowQuality:
                    return 2;
                case AirToAirShotStatus.TooFar:
                    return 3;
                case AirToAirShotStatus.TooClose:
                    return 4;
                default:
                    return 5;
            }
        }

        private static bool ShouldContinueIntoWvr(
            AirCombatFlightView source,
            AirCombatFlightView target,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            out string reason)
        {
            if (frame.PendingEffects.Any(effect =>
                    effect.SourceKind
                    == OrdnanceEmploymentSourceKind.AircraftFlight
                    && effect.SourceFlightId == source.Flight.FlightId
                    && effect.TargetFlightId == target.Flight.FlightId
                    && effect.ResolveAt > frame.Time
                    && ordnanceTypes.TryGetValue(
                        effect.OrdnanceTypeDefinitionId,
                        out var definition)
                    && IsAirToAir(definition)))
            {
                reason =
                    "An air-to-air effect is already pending against the target; "
                    + "extending rather than accepting an unnecessary merge.";
                return false;
            }

            var availableWeapons = GetAvailableAirToAirWeapons(
                source,
                ordnanceTypes);
            if (!availableWeapons.Any(IsWvrWeapon))
            {
                reason =
                    "No WVR-capable weapon remains; extending from the close-range threat.";
                return false;
            }

            if (source.Flight.MissionType
                == AirMissionRequestType.BarrierCombatAirPatrol)
            {
                reason =
                    "Continuing into WVR to stop an authorized threat to the BARCAP barrier.";
                return true;
            }

            if (source.Flight.IsFighterEscort)
            {
                reason =
                    "Continuing into WVR to stop an authorized threat to the protected package.";
                return true;
            }

            if (source.Flight.MissionType
                != AirMissionRequestType.OffensiveCounterAirSweep)
            {
                reason =
                    "The assigned mission does not authorize a discretionary WVR merge; "
                    + "extending from the threat.";
                return false;
            }

            if (availableWeapons.Any(definition =>
                    definition.EmploymentCategory
                    == OrdnanceEmploymentCategory.AirToAirRadar))
            {
                reason =
                    "A standoff air-to-air weapon remains; extending rather than "
                    + "accepting a discretionary merge.";
                return false;
            }

            reason =
                "No standoff air-to-air weapon remains; continuing into WVR "
                + "with available close-range weapons.";
            return true;
        }

        private static List<OrdnanceTypeDefinition> GetAvailableAirToAirWeapons(
            AirCombatFlightView source,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
            return source.LiveAircraft
                .SelectMany(aircraft => aircraft.Loadout)
                .Where(item => item.Count > 0
                               && ordnanceTypes.TryGetValue(
                                   item.OrdnanceTypeDefinitionId,
                                   out var definition)
                               && IsAirToAir(definition))
                .Select(item => ordnanceTypes[item.OrdnanceTypeDefinitionId])
                .GroupBy(definition => definition.OrdnanceTypeDefinitionId)
                .Select(group => group.First())
                .ToList();
        }

        private static int GetWeaponSetupPriority(
            OrdnanceTypeDefinition definition,
            float distanceKm)
        {
            if (distanceKm <= WvrDecisionRangeKm)
            {
                if (definition.EmploymentCategory
                    == OrdnanceEmploymentCategory.AirToAirInfrared)
                    return 0;
                if (definition.EmploymentCategory
                    == OrdnanceEmploymentCategory.Gun)
                    return 1;
                return 2;
            }

            if (distanceKm <= 18f)
            {
                return definition.EmploymentCategory
                       == OrdnanceEmploymentCategory.AirToAirInfrared
                    ? 0
                    : 1;
            }

            return definition.EmploymentCategory
                   == OrdnanceEmploymentCategory.AirToAirRadar
                ? 0
                : 1;
        }

        private static bool IsWvrWeapon(OrdnanceTypeDefinition definition)
        {
            return definition.EmploymentCategory
                   == OrdnanceEmploymentCategory.AirToAirInfrared
                   || (definition.EmploymentCategory
                       == OrdnanceEmploymentCategory.Gun
                       && definition.GetEffectiveness(
                           OrdnanceTargetCategory.Aircraft) > 0f);
        }

        private static AirCombatCommand DefensiveCommand(
            AirCombatFlightView source,
            Vector3 threatPositionFeet,
            Guid threatFlightId,
            PendingOrdnanceEffect effect,
            OrdnanceTypeDefinition ordnance,
            DateTime currentTime)
        {
            var secondsToImpact = Math.Max(0d, (effect.ResolveAt - currentTime).TotalSeconds);
            var outsideNoEscapeRange =
                IsOutsideNoEscapeRange(effect, ordnance);
            if (outsideNoEscapeRange
                || secondsToImpact > TerminalDefenseSeconds)
            {
                var dragReferencePosition = outsideNoEscapeRange
                    ? effect.SourcePositionFeet
                    : threatPositionFeet;
                var direction =
                    source.Flight.PositionFeet - dragReferencePosition;
                direction.y = 0f;
                if (direction.sqrMagnitude <= 1f)
                    direction = Direction(source.Flight.HeadingDegrees + 180f);
                var dragAim = source.Flight.PositionFeet
                              + direction.normalized * TacticalAimDistanceKm
                              * AirspaceGeometry.FeetPerKilometer;
                dragAim.y = source.Flight.PositionFeet.y;
                return Command(
                    source,
                    AirCombatIntent.Defend,
                    AirCombatManeuver.Drag,
                    threatFlightId,
                    Guid.Empty,
                    currentTime,
                    currentTime.AddSeconds(20),
                    AirCombatManeuverSide.None,
                    dragAim,
                    Math.Max(1f, source.AircraftType.CombatSpeedKnots),
                    outsideNoEscapeRange
                        ? $"Dragging a radar missile released outside its no-escape range "
                          + $"with {secondsToImpact:0} seconds to impact."
                        : $"Dragging an incoming missile with "
                          + $"{secondsToImpact:0} seconds to impact.");
            }

            var side = StableSide(source.Flight.FlightId, effect.PendingEffectId);
            var threatBearing = HeadingTo(source.Flight.PositionFeet, threatPositionFeet);
            var heading = threatBearing + (side == AirCombatManeuverSide.Left ? -90f : 90f);
            var aim = source.Flight.PositionFeet
                      + Direction(heading) * TacticalAimDistanceKm
                      * AirspaceGeometry.FeetPerKilometer;
            aim.y = Math.Max(1000f, source.Flight.PositionFeet.y - 5000f);
            var isInfrared = ordnance?.GuidanceMode ==
                             OrdnanceGuidanceMode.Infrared;
            return Command(
                source,
                AirCombatIntent.Defend,
                SelectTerminalDefensiveManeuver(side, ordnance),
                threatFlightId,
                Guid.Empty,
                currentTime,
                effect.ResolveAt,
                side,
                aim,
                Math.Max(1f, source.AircraftType.CombatSpeedKnots),
                isInfrared
                    ? $"Breaking against an infrared missile with "
                      + $"{secondsToImpact:0} seconds to impact."
                    : $"Beaming a terminal missile with "
                      + $"{secondsToImpact:0} seconds to impact.");
        }

        internal static AirCombatManeuver SelectTerminalDefensiveManeuver(
            AirCombatManeuverSide side,
            OrdnanceTypeDefinition ordnance)
        {
            var isInfrared = ordnance?.GuidanceMode ==
                             OrdnanceGuidanceMode.Infrared;
            if (side == AirCombatManeuverSide.Left)
            {
                return isInfrared
                    ? AirCombatManeuver.BreakLeft
                    : AirCombatManeuver.BeamLeft;
            }
            return isInfrared
                ? AirCombatManeuver.BreakRight
                : AirCombatManeuver.BeamRight;
        }

        private static bool IsRadarGuided(OrdnanceTypeDefinition ordnance)
        {
            return ordnance != null
                   && (ordnance.GuidanceMode == OrdnanceGuidanceMode.Radar
                       || ordnance.GuidanceMode == OrdnanceGuidanceMode.ActiveRadar
                       || ordnance.GuidanceMode == OrdnanceGuidanceMode.SemiActiveRadar);
        }

        private static bool IsOutsideNoEscapeRange(
            PendingOrdnanceEffect effect,
            OrdnanceTypeDefinition ordnance)
        {
            if (effect == null || !IsRadarGuided(ordnance))
                return false;

            var maximumRangeKm = effect.MaximumRangeKmAtRelease > 0f
                ? effect.MaximumRangeKmAtRelease
                : ordnance.MaximumRangeKm;
            return maximumRangeKm > 0f
                   && effect.ReleaseRangeKm
                   > maximumRangeKm * ordnance.NoEscapeRangeFraction;
        }

        private static AirCombatCommand CrankCommand(
            AirCombatFlightView source,
            AirCombatFlightView target,
            PendingOrdnanceEffect effect,
            DateTime currentTime)
        {
            var side = source.Flight.TacticalState.PreferredSide != AirCombatManeuverSide.None
                ? source.Flight.TacticalState.PreferredSide
                : StableSide(source.Flight.FlightId, target.Flight.FlightId);
            var targetBearing = HeadingTo(source.Flight.PositionFeet, target.Flight.PositionFeet);
            var heading = targetBearing + (side == AirCombatManeuverSide.Left
                ? -CrankOffsetDegrees
                : CrankOffsetDegrees);
            var aim = source.Flight.PositionFeet
                      + Direction(heading) * TacticalAimDistanceKm
                      * AirspaceGeometry.FeetPerKilometer;
            aim.y = source.Flight.PositionFeet.y;
            return Command(
                source,
                AirCombatIntent.EngageTarget,
                side == AirCombatManeuverSide.Left
                    ? AirCombatManeuver.CrankLeft
                    : AirCombatManeuver.CrankRight,
                target.Flight.FlightId,
                effect.PendingEffectId,
                currentTime,
                effect.AutonomousAt > currentTime ? effect.AutonomousAt : currentTime.AddSeconds(10),
                side,
                aim,
                Math.Max(1f, source.AircraftType.CombatSpeedKnots),
                "Cranking while maintaining required missile support.");
        }

        private static AirCombatCommand RouteCommand(
            AirCombatFlightView source,
            DateTime currentTime,
            AirCombatIntent intent,
            string reason)
        {
            var waypoint = source.Flight.CurrentWaypoint;
            return Command(
                source,
                intent,
                AirCombatManeuver.FollowRoute,
                Guid.Empty,
                Guid.Empty,
                currentTime,
                currentTime,
                AirCombatManeuverSide.None,
                waypoint?.PositionFeet ?? source.Flight.PositionFeet,
                Math.Max(1f, source.AircraftType.CruiseSpeedKnots),
                reason);
        }

        private static AirCombatCommand DeadDefensiveShotCommand(
            AirCombatFlightView source,
            DateTime currentTime,
            DateTime minimumEndAt,
            string reason)
        {
            return Command(
                source,
                AirCombatIntent.FollowMission,
                AirCombatManeuver.LaunchSetup,
                Guid.Empty,
                Guid.Empty,
                currentTime,
                minimumEndAt,
                AirCombatManeuverSide.None,
                source.Flight.PositionFeet,
                Math.Max(1f, source.AircraftType.CruiseSpeedKnots),
                reason);
        }

        private static AirCombatCommand AimAtTargetCommand(
            AirCombatFlightView source,
            AirCombatFlightView target,
            AirCombatIntent intent,
            AirCombatManeuver maneuver,
            DateTime currentTime,
            DateTime minimumEndAt,
            Guid targetFlightId,
            Guid supportedEffectId,
            string reason,
            bool awayFromTarget = false)
        {
            var direction = awayFromTarget
                ? (source.Flight.PositionFeet - target.Flight.PositionFeet).normalized
                : (target.Flight.PositionFeet - source.Flight.PositionFeet).normalized;
            direction.y = 0f;
            var aim = source.Flight.PositionFeet
                      + direction * TacticalAimDistanceKm
                      * AirspaceGeometry.FeetPerKilometer;
            aim.y = awayFromTarget
                ? source.Flight.PositionFeet.y
                : Mathf.Clamp(
                    target.Flight.PositionFeet.y,
                    1000f,
                    source.AircraftType.ServiceCeilingFeet);
            return Command(
                source,
                intent,
                maneuver,
                targetFlightId,
                supportedEffectId,
                currentTime,
                minimumEndAt,
                AirCombatManeuverSide.None,
                aim,
                Math.Max(1f, source.AircraftType.CombatSpeedKnots),
                reason);
        }

        private static AirCombatCommand Command(
            AirCombatFlightView source,
            AirCombatIntent intent,
            AirCombatManeuver maneuver,
            Guid targetFlightId,
            Guid supportedEffectId,
            DateTime currentTime,
            DateTime minimumEndAt,
            AirCombatManeuverSide side,
            Vector3 aimPoint,
            float speedKnots,
            string reason)
        {
            return new AirCombatCommand
            {
                FlightId = source.Flight.FlightId,
                Intent = intent,
                Maneuver = maneuver,
                TargetFlightId = targetFlightId,
                SupportedPendingEffectId = supportedEffectId,
                PreferredSide = side,
                AimPointFeet = aimPoint,
                HasAimPoint = true,
                DesiredSpeedKnots = speedKnots,
                MinimumManeuverEndAt = minimumEndAt < currentTime ? currentTime : minimumEndAt,
                Reason = reason
            };
        }

        private static Vector3 PredictStandoffIntercept(
            AirCombatFlightView source,
            AirCombatFlightView target,
            float preferredRangeKm)
        {
            var distanceFeet = Vector3.Distance(
                source.Flight.PositionFeet,
                target.Flight.PositionFeet);
            var standoffFeet = preferredRangeKm * AirspaceGeometry.FeetPerKilometer;
            var sourceFeetPerSecond = Math.Max(
                1f,
                source.AircraftType.CombatSpeedKnots
                * AirspaceGeometry.FeetPerNauticalMile / 3600f);
            var seconds = Math.Max(0f, (distanceFeet - standoffFeet) / sourceFeetPerSecond);
            seconds = Math.Min(seconds, 300f);
            var targetVelocity = Direction(target.Flight.HeadingDegrees)
                                 * target.Flight.SpeedKnots
                                 * AirspaceGeometry.FeetPerNauticalMile / 3600f;
            var predicted = target.Flight.PositionFeet + targetVelocity * seconds;
            var line = predicted - source.Flight.PositionFeet;
            line.y = 0f;
            if (line.sqrMagnitude > 1f)
                predicted -= line.normalized * standoffFeet;
            predicted.y = Mathf.Clamp(
                target.Flight.PositionFeet.y,
                1000f,
                source.AircraftType.ServiceCeilingFeet);
            return predicted;
        }

        private static float TargetAspect(AirFlight source, AirFlight target)
        {
            var bearingToSource = HeadingTo(target.PositionFeet, source.PositionFeet);
            return Math.Abs(Mathf.DeltaAngle(target.HeadingDegrees, bearingToSource));
        }

        private static float TargetAspect(AirFlight source, IADSTrack target)
        {
            var bearingToSource = HeadingTo(
                target.LastKnownPositionFeet,
                source.PositionFeet);
            return Math.Abs(Mathf.DeltaAngle(
                target.EstimatedHeadingDegrees,
                bearingToSource));
        }

        private static int CountRounds(AirCombatFlightView flight, Guid ordnanceId)
        {
            return flight.LiveAircraft
                .SelectMany(aircraft => aircraft.Loadout)
                .Where(item => item.OrdnanceTypeDefinitionId == ordnanceId)
                .Sum(item => Math.Max(0, item.Count));
        }

        private static float DistanceKm(Vector3 first, Vector3 second)
        {
            return Vector3.Distance(first, second) / AirspaceGeometry.FeetPerKilometer;
        }

        private static AirCombatManeuverSide StableSide(Guid first, Guid second)
        {
            unchecked
            {
                var seed = 17;
                foreach (var value in first.ToByteArray())
                    seed = seed * 31 + value;
                foreach (var value in second.ToByteArray())
                    seed = seed * 31 + value;
                return (seed & 1) == 0
                    ? AirCombatManeuverSide.Left
                    : AirCombatManeuverSide.Right;
            }
        }

        private static bool IsAirToAir(OrdnanceTypeDefinition definition)
        {
            return definition.EmploymentCategory == OrdnanceEmploymentCategory.AirToAirRadar
                   || definition.EmploymentCategory == OrdnanceEmploymentCategory.AirToAirInfrared
                   || (definition.EmploymentCategory == OrdnanceEmploymentCategory.Gun
                       && definition.GetEffectiveness(
                           OrdnanceTargetCategory.Aircraft) > 0f);
        }

        private static bool IsAirToSurface(OrdnanceTypeDefinition definition)
        {
            return definition.EmploymentCategory
                       == OrdnanceEmploymentCategory.AirToGroundPrecision
                   || definition.EmploymentCategory
                       == OrdnanceEmploymentCategory.AirToGroundUnguided
                   || definition.EmploymentCategory
                       == OrdnanceEmploymentCategory.AntiRadiation;
        }

        private static bool AreHostile(Alliance first, Alliance second)
        {
            return first == Alliance.Bluefor && second == Alliance.Redfor
                   || first == Alliance.Redfor && second == Alliance.Bluefor;
        }

        private static string ShortId(Guid id)
        {
            return id == Guid.Empty
                ? "none"
                : id.ToString("N").Substring(0, 8);
        }

        private readonly struct PotentialAirThreatCapability
        {
            public readonly float MaximumPotentialReachKm;
            public readonly float CombatSpeedKnots;
            public readonly bool UseConservativeSpeed;

            public PotentialAirThreatCapability(
                float maximumPotentialReachKm,
                float combatSpeedKnots,
                bool useConservativeSpeed)
            {
                MaximumPotentialReachKm = Math.Max(
                    0f,
                    maximumPotentialReachKm);
                CombatSpeedKnots = Math.Max(0f, combatSpeedKnots);
                UseConservativeSpeed = useConservativeSpeed;
            }
        }

        private enum AirThreatAssessmentPurpose
        {
            ProtectedAircraft = 0,
            BarrierAssets = 1
        }
    }
}
