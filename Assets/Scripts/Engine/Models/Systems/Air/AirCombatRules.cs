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
    }

    internal sealed class AirCombatFrame
    {
        public DateTime Time;
        public float TileDistanceKm;
        public IReadOnlyDictionary<Guid, AirCombatFlightView> Flights;
        public IReadOnlyDictionary<Alliance, AllianceAirTaskingCommander> AirCommanders;
        public IReadOnlyDictionary<Alliance, IReadOnlyDictionary<Guid, IADSTrack>>
            CurrentTracksByAlliance;
        public IReadOnlyList<ActiveOrdnanceEmploymentPass> ActivePasses;
        public IReadOnlyList<PendingOrdnanceEffect> PendingEffects;
        public IReadOnlyDictionary<Guid, Guid> BarcapTargetByFlightId;

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
    }

    internal static class AirCombatRules
    {
        private const float PreferredRangeFraction = 0.78f;
        private const float WvrDecisionRangeKm = 8f;
        private const float TacticalAimDistanceKm = 80f;
        private const float CrankOffsetDegrees = 55f;
        private const float TerminalDefenseSeconds = 45f;
        private const float HotThreatAspectDegrees = 30f;
        private const float BarcapThreatLookaheadMinutes = 20f;
        private const float BarcapResponsePaddingTiles = 2f;
        private const float BarcapCommitMarginMinutes = 1.5f;
        private const float BarcapBoundaryToleranceTiles = 0.1f;
        private const float BarcapDefensiveBufferTiles = 0.15f;

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
            return EnforceBarcapDefensiveBoundary(source, frame, command);
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
                if (IsCounterAirMission(flight.MissionType)
                    || secondsToImpact <= TerminalDefenseSeconds)
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
            if (activePass != null
                && frame.Flights.TryGetValue(activePass.TargetFlightId, out var passTarget))
            {
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
                return RouteCommand(source, frame.Time, AirCombatIntent.FollowMission, "No authorized air target.");

            if (state.ProactiveEngagementExhausted)
            {
                return RouteCommand(
                    source,
                    frame.Time,
                    AirCombatIntent.FollowMission,
                    "Recommit limit reached; resuming the assigned mission route.");
            }
            if ((state.Maneuver == AirCombatManeuver.Extend
                 || state.Maneuver == AirCombatManeuver.Drag)
                && state.RecommitCount >= doctrine.MaximumRecommits)
            {
                var exhausted = RouteCommand(
                    source,
                    frame.Time,
                    AirCombatIntent.FollowMission,
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

            var weapon = SelectWeapon(
                source,
                target,
                ordnanceTypes,
                doctrine);
            if (weapon == null)
            {
                return AimAtTargetCommand(
                    source,
                    target,
                    AirCombatIntent.Disengage,
                    AirCombatManeuver.Extend,
                    frame.Time,
                    frame.Time.AddSeconds(30),
                    target.Flight.FlightId,
                    Guid.Empty,
                    "No air-to-air ordnance remains; extending from the threat.",
                    awayFromTarget: true);
            }

            if (EvaluateLaunch(source, target, weapon, out var launchQuality)
                && launchQuality >= doctrine.MinimumLaunchQuality)
            {
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
                    command.Employment = new AirCombatEmploymentProposal
                    {
                        SourceFlightId = flight.FlightId,
                        TargetFlightId = target.Flight.FlightId,
                        OrdnanceTypeDefinitionId = weapon.OrdnanceTypeDefinitionId,
                        Quantity = quantity,
                        LaunchQuality = launchQuality
                    };
                    return command;
                }
            }

            var distanceKm = DistanceKm(flight.PositionFeet, target.Flight.PositionFeet);
            var isInsideWvrDecisionRange = distanceKm <= WvrDecisionRangeKm;
            var wvrReason = string.Empty;
            if (isInsideWvrDecisionRange
                && !ShouldContinueIntoWvr(
                    source,
                    target,
                    frame,
                    ordnanceTypes,
                    out wvrReason))
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

            var maximumRange = EffectiveMaximumRangeKm(weapon, flight);
            var preferredRange = Math.Max(1f, maximumRange * PreferredRangeFraction);
            var interceptPoint = PredictStandoffIntercept(
                source,
                target,
                preferredRange);
            return Command(
                source,
                AirCombatIntent.EngageTarget,
                distanceKm > maximumRange
                    ? AirCombatManeuver.Intercept
                    : AirCombatManeuver.Press,
                target.Flight.FlightId,
                Guid.Empty,
                frame.Time,
                frame.Time.AddSeconds(15),
                AirCombatManeuverSide.None,
                interceptPoint,
                Math.Max(1f, source.AircraftType.CombatSpeedKnots),
                isInsideWvrDecisionRange
                    ? wvrReason
                    : distanceKm > maximumRange
                        ? "Closing toward a predicted standoff intercept."
                        : "Pressing to improve launch geometry.");
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
            launchQuality = 0f;
            if (source == null || target == null || ordnance == null)
                return false;

            var distanceKm = DistanceKm(source.PositionFeet, target.PositionFeet);
            var maximumRange = EffectiveMaximumRangeKm(ordnance, source);
            var offNose = AngleOffNose(source, target.PositionFeet);
            var targetAltitude = target.PositionFeet.y;
            if (maximumRange <= 0f
                || distanceKm < ordnance.MinimumRangeKm
                || distanceKm > maximumRange
                || targetAltitude < ordnance.MinimumTargetAltitudeFeet
                || targetAltitude > ordnance.MaximumTargetAltitudeFeet
                || offNose > ordnance.MaximumLaunchOffBoresightDegrees)
                return false;

            var rangeRatio = Mathf.Clamp01(distanceKm / Math.Max(0.01f, maximumRange));
            var noEscapeRatio = Mathf.Clamp01(ordnance.NoEscapeRangeFraction);
            var rangeQuality = rangeRatio <= noEscapeRatio
                ? 1f
                : Mathf.Lerp(
                    1f,
                    0.25f,
                    (rangeRatio - noEscapeRatio) / Math.Max(0.01f, 1f - noEscapeRatio));
            var noseQuality = 1f - 0.35f * Mathf.Clamp01(
                offNose / Math.Max(1f, ordnance.MaximumLaunchOffBoresightDegrees));
            var targetAspect = TargetAspect(source, target);
            var aspectQuality = Mathf.Lerp(0.7f, 1f, targetAspect / 180f);
            var radarQuality = ordnance.GuidanceMode == OrdnanceGuidanceMode.Infrared
                ? 1f
                : 0.65f + 0.35f * Mathf.Clamp01(sourceType.RadarQuality);
            launchQuality = Mathf.Clamp01(
                rangeQuality * noseQuality * aspectQuality * radarQuality);
            return true;
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
                            return new
                            {
                                Defender = defender,
                                Track = track,
                                Authorized = authorized,
                                Minutes = authorized ? minutes : float.MaxValue
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
                             .ThenBy(candidate => DistanceKm(
                                 candidate.Defender.Flight.PositionFeet,
                                 candidate.Track.LastKnownPositionFeet))
                             .ThenByDescending(candidate =>
                                 candidate.Defender.Flight.TacticalState.FuelFraction)
                             .ThenBy(candidate => CalculateFlightAirCombatPower(
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

            var isSelfDefenseTarget = frame.PendingEffects.Any(effect =>
                effect.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight
                && effect.SourceFlightId == target.Flight.FlightId
                && effect.TargetFlightId == source.Flight.FlightId
                && effect.ResolveAt > frame.Time);
            if (isSelfDefenseTarget)
                return true;

            if (IsHotThreatWithinSelfDefenseEnvelope(
                    source,
                    target,
                    frame,
                    ordnanceTypes))
                return true;

            if (!IsCounterAirMission(source.Flight.MissionType))
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

        private static bool TryGetBarcapThreatMinutes(
            AirCombatFlightView source,
            IADSTrack track,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            out float minutesToEntry)
        {
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
                        out var crossingFeet,
                        out var minutesToCrossing))
                    return false;

                var interceptMinutes = CalculateBarcapInterceptMinutes(
                    source,
                    crossingFeet,
                    ordnanceTypes);
                if (minutesToCrossing
                    > interceptMinutes + BarcapCommitMarginMinutes)
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
            var radiusFeet = (area.RadiusTiles + 0.55f)
                              * frame.TileDistanceKm
                              * AirspaceGeometry.FeetPerKilometer;
            var distance = Vector2.Distance(center, position);
            if (distance <= radiusFeet)
            {
                minutesToEntry = 0f;
                return true;
            }

            var responseRadiusFeet = (area.RadiusTiles
                                      + BarcapResponsePaddingTiles
                                      + 0.55f)
                                     * frame.TileDistanceKm
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
            if (source.Flight.MissionType
                    == AirMissionRequestType.BarrierCombatAirPatrol
                && source.Flight.ActiveBarcapCoverage is BarcapStationCoverage coverage
                && coverage.CoveredBarrierTileIds != null
                && coverage.CoveredBarrierTileIds.Count > 0
                && (!frame.TryGetCurrentTrack(
                        source.Alliance,
                        target.Flight.FlightId,
                        out var track)
                    || !IsBarcapDefensiveContact(
                        source,
                        track,
                        frame,
                        ordnanceTypes)))
                return false;

            if (TargetAspect(source.Flight, target.Flight) > HotThreatAspectDegrees)
                return false;

            var distanceKm = DistanceKm(
                source.Flight.PositionFeet,
                target.Flight.PositionFeet);
            var targetAltitudeFeet = target.Flight.PositionFeet.y;
            return source.LiveAircraft
                .SelectMany(aircraft => aircraft.Loadout)
                .Any(item => item.Count > 0
                             && ordnanceTypes.TryGetValue(
                                 item.OrdnanceTypeDefinitionId,
                                 out var ordnance)
                             && IsAirToAir(ordnance)
                             && distanceKm >= ordnance.MinimumRangeKm
                             && distanceKm <= EffectiveMaximumRangeKm(
                                 ordnance,
                                 source.Flight)
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
                DistanceKm(source.Flight.PositionFeet, crossingFeet)
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

        private static bool IsCounterAirMission(AirMissionRequestType missionType)
        {
            return missionType == AirMissionRequestType.BarrierCombatAirPatrol
                   || missionType == AirMissionRequestType.OffensiveCounterAirSweep;
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
            var radiusFeet = (area.RadiusTiles + paddingTiles + 0.55f)
                             * tileDistanceKm
                             * AirspaceGeometry.FeetPerKilometer;
            return horizontalDistance <= radiusFeet;
        }

        private static OrdnanceTypeDefinition SelectWeapon(
            AirCombatFlightView source,
            AirCombatFlightView target,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes,
            AllianceAirDoctrine doctrine)
        {
            var distanceKm = DistanceKm(source.Flight.PositionFeet, target.Flight.PositionFeet);
            return GetAvailableAirToAirWeapons(source, ordnanceTypes)
                .Select(definition =>
                {
                    var canLaunch = EvaluateLaunch(
                        source,
                        target,
                        definition,
                        out var quality);
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
                        Definition = definition,
                        CanEmploy = canLaunch
                                    && quality >= doctrine.MinimumLaunchQuality
                                    && hasExpendableRounds,
                        HasExpendableRounds = hasExpendableRounds,
                        LaunchQuality = quality
                    };
                })
                .OrderByDescending(candidate => candidate.CanEmploy)
                .ThenBy(candidate =>
                    candidate.CanEmploy
                    && candidate.Definition.EmploymentCategory
                    == OrdnanceEmploymentCategory.Gun
                        ? 1
                        : 0)
                .ThenByDescending(candidate => candidate.CanEmploy
                    ? candidate.LaunchQuality
                    : 0f)
                .ThenByDescending(candidate => candidate.HasExpendableRounds)
                .ThenBy(candidate => GetWeaponSetupPriority(
                    candidate.Definition,
                    distanceKm))
                .ThenByDescending(candidate => EffectiveMaximumRangeKm(
                    candidate.Definition,
                    source.Flight))
                .ThenByDescending(candidate => candidate.Definition.HitProbability)
                .ThenBy(candidate => candidate.Definition.OrdnanceTypeDefinitionId)
                .Select(candidate => candidate.Definition)
                .FirstOrDefault();
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
            DateTime currentTime)
        {
            var secondsToImpact = Math.Max(0d, (effect.ResolveAt - currentTime).TotalSeconds);
            if (secondsToImpact > TerminalDefenseSeconds)
            {
                var direction = source.Flight.PositionFeet - threatPositionFeet;
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
                    $"Dragging an incoming missile with {secondsToImpact:0} seconds to impact.");
            }

            var side = StableSide(source.Flight.FlightId, effect.PendingEffectId);
            var threatBearing = HeadingTo(source.Flight.PositionFeet, threatPositionFeet);
            var heading = threatBearing + (side == AirCombatManeuverSide.Left ? -90f : 90f);
            var aim = source.Flight.PositionFeet
                      + Direction(heading) * TacticalAimDistanceKm
                      * AirspaceGeometry.FeetPerKilometer;
            aim.y = Math.Max(1000f, source.Flight.PositionFeet.y - 5000f);
            return Command(
                source,
                AirCombatIntent.Defend,
                side == AirCombatManeuverSide.Left
                    ? AirCombatManeuver.BeamLeft
                    : AirCombatManeuver.BeamRight,
                threatFlightId,
                Guid.Empty,
                currentTime,
                effect.ResolveAt,
                side,
                aim,
                Math.Max(1f, source.AircraftType.CombatSpeedKnots),
                $"Beaming a terminal missile with {secondsToImpact:0} seconds to impact.");
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

        private static bool AreHostile(Alliance first, Alliance second)
        {
            return first == Alliance.Bluefor && second == Alliance.Redfor
                   || first == Alliance.Redfor && second == Alliance.Bluefor;
        }
    }
}
