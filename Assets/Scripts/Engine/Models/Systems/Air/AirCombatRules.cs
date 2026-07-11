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
        public IReadOnlyList<ActiveOrdnanceEmploymentPass> ActivePasses;
        public IReadOnlyList<PendingOrdnanceEffect> PendingEffects;
    }

    internal static class AirCombatRules
    {
        private const float PreferredRangeFraction = 0.78f;
        private const float CloseCombatBoundaryKm = 8f;
        private const float TacticalAimDistanceKm = 80f;
        private const float CrankOffsetDegrees = 55f;
        private const float TerminalDefenseSeconds = 45f;
        private const float HotThreatAspectDegrees = 30f;

        public static AirCombatCommand Decide(
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
                .Where(effect => effect.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight
                                 && effect.TargetFlightId == flight.FlightId
                                 && effect.ResolveAt > frame.Time)
                .OrderBy(effect => effect.ResolveAt)
                .ThenBy(effect => effect.PendingEffectId)
                .FirstOrDefault();
            if (incoming != null
                && frame.Flights.TryGetValue(incoming.SourceFlightId, out var attacker))
            {
                var secondsToImpact = Math.Max(
                    0d,
                    (incoming.ResolveAt - frame.Time).TotalSeconds);
                if (IsCounterAirMission(flight.MissionType)
                    || secondsToImpact <= TerminalDefenseSeconds)
                {
                    return DefensiveCommand(source, attacker, incoming, frame.Time);
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

            var target = SelectTarget(source, frame, ordnanceTypes);
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

            var weapon = SelectWeapon(source, target, ordnanceTypes);
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

            var distanceKm = DistanceKm(flight.PositionFeet, target.Flight.PositionFeet);
            if (distanceKm <= CloseCombatBoundaryKm)
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
                    "Flights crossed the deferred WVR boundary; extending before possible recommit.",
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
                    totalRounds - doctrine.MinimumAirToAirWeaponReserve);
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
                distanceKm > maximumRange
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

        private static AirCombatFlightView SelectTarget(
            AirCombatFlightView source,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
            var retainedId = source.Flight.TacticalState.TargetFlightId;
            if (retainedId != Guid.Empty
                && frame.Flights.TryGetValue(retainedId, out var retained)
                && IsEligibleTarget(source, retained, frame, ordnanceTypes))
                return retained;

            return frame.Flights.Values
                .Where(candidate => IsEligibleTarget(
                    source,
                    candidate,
                    frame,
                    ordnanceTypes))
                .Select(candidate => new
                {
                    Flight = candidate,
                    PendingAttack = frame.PendingEffects.Any(effect =>
                        effect.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight
                        && effect.SourceFlightId == candidate.Flight.FlightId
                        && effect.TargetFlightId == source.Flight.FlightId),
                    Distance = DistanceKm(
                        source.Flight.PositionFeet,
                        candidate.Flight.PositionFeet)
                })
                .OrderByDescending(candidate => candidate.PendingAttack)
                .ThenBy(candidate => candidate.Distance)
                .ThenByDescending(candidate => candidate.Flight.LiveAircraft.Count)
                .ThenBy(candidate => candidate.Flight.Flight.FlightId)
                .Select(candidate => candidate.Flight)
                .FirstOrDefault();
        }

        private static bool IsEligibleTarget(
            AirCombatFlightView source,
            AirCombatFlightView target,
            AirCombatFrame frame,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
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

            if (!IsCounterAirMission(source.Flight.MissionType))
            {
                return IsHotThreatWithinSelfDefenseEnvelope(
                    source,
                    target,
                    ordnanceTypes);
            }

            if (source.Flight.LifecycleState != AirTaskingLifecycleState.Active
                || source.Flight.MissionAchieved
                || frame.Time >= source.Flight.EffectEnd)
                return false;

            if (source.Flight.MissionType == AirMissionRequestType.DefensiveCounterAirPatrol)
            {
                return source.Flight.ExecutionPhase == FlightExecutionPhase.Executing
                       && IsInsideMissionArea(
                           source.Flight.MissionArea,
                           target.Flight.PositionFeet,
                           frame.TileDistanceKm,
                           0f);
            }

            if (source.Flight.MissionType == AirMissionRequestType.OffensiveCounterAirSweep)
            {
                return (source.Flight.ExecutionPhase == FlightExecutionPhase.Outbound
                        || source.Flight.ExecutionPhase == FlightExecutionPhase.Executing)
                       && IsInsideMissionArea(
                           source.Flight.MissionArea,
                           target.Flight.PositionFeet,
                           frame.TileDistanceKm,
                           2f);
            }

            return false;
        }

        private static bool IsHotThreatWithinSelfDefenseEnvelope(
            AirCombatFlightView source,
            AirCombatFlightView target,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
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

        private static bool IsCounterAirMission(AirMissionRequestType missionType)
        {
            return missionType == AirMissionRequestType.DefensiveCounterAirPatrol
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
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes)
        {
            var distanceKm = DistanceKm(source.Flight.PositionFeet, target.Flight.PositionFeet);
            var available = source.LiveAircraft
                .SelectMany(aircraft => aircraft.Loadout)
                .Where(item => item.Count > 0
                               && ordnanceTypes.TryGetValue(
                                   item.OrdnanceTypeDefinitionId,
                                   out var definition)
                               && IsAirToAir(definition))
                .Select(item => ordnanceTypes[item.OrdnanceTypeDefinitionId])
                .Distinct()
                .ToList();
            return available
                .Select(definition => new
                {
                    Definition = definition,
                    CanLaunch = EvaluateLaunch(
                        source,
                        target,
                        definition,
                        out var quality),
                    LaunchQuality = quality
                })
                .OrderByDescending(candidate => candidate.CanLaunch)
                .ThenByDescending(candidate => candidate.LaunchQuality)
                .ThenBy(candidate => distanceKm <= 18f
                    ? candidate.Definition.EmploymentCategory == OrdnanceEmploymentCategory.AirToAirInfrared ? 0 : 1
                    : candidate.Definition.EmploymentCategory == OrdnanceEmploymentCategory.AirToAirRadar ? 0 : 1)
                .ThenByDescending(candidate => EffectiveMaximumRangeKm(
                    candidate.Definition,
                    source.Flight))
                .ThenByDescending(candidate => candidate.Definition.HitProbability)
                .ThenBy(candidate => candidate.Definition.OrdnanceTypeDefinitionId)
                .Select(candidate => candidate.Definition)
                .FirstOrDefault();
        }

        private static AirCombatCommand DefensiveCommand(
            AirCombatFlightView source,
            AirCombatFlightView attacker,
            PendingOrdnanceEffect effect,
            DateTime currentTime)
        {
            var secondsToImpact = Math.Max(0d, (effect.ResolveAt - currentTime).TotalSeconds);
            if (secondsToImpact > TerminalDefenseSeconds)
            {
                return AimAtTargetCommand(
                    source,
                    attacker,
                    AirCombatIntent.Defend,
                    AirCombatManeuver.Drag,
                    currentTime,
                    currentTime.AddSeconds(20),
                    attacker.Flight.FlightId,
                    Guid.Empty,
                    $"Dragging an incoming missile with {secondsToImpact:0} seconds to impact.",
                    awayFromTarget: true);
            }

            var side = StableSide(source.Flight.FlightId, effect.PendingEffectId);
            var threatBearing = HeadingTo(source.Flight.PositionFeet, attacker.Flight.PositionFeet);
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
                attacker.Flight.FlightId,
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
                   || definition.EmploymentCategory == OrdnanceEmploymentCategory.AirToAirInfrared;
        }

        private static bool AreHostile(Alliance first, Alliance second)
        {
            return first == Alliance.Bluefor && second == Alliance.Redfor
                   || first == Alliance.Redfor && second == Alliance.Bluefor;
        }
    }
}
