using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    public enum AirCombatIntent
    {
        FollowMission = 0,
        EngageTarget = 1,
        Defend = 2,
        Disengage = 3,
        Recover = 4
    }

    public enum AirCombatManeuver
    {
        FollowRoute = 0,
        Intercept = 1,
        Press = 2,
        LaunchSetup = 3,
        CrankLeft = 4,
        CrankRight = 5,
        BeamLeft = 6,
        BeamRight = 7,
        Drag = 8,
        Extend = 9,
        Recommit = 10,
        Dogfight = 11,
        BreakLeft = 12,
        BreakRight = 13,
        AvoidSurfaceThreat = 14
    }

    public enum AirCombatManeuverSide
    {
        None = 0,
        Left = 1,
        Right = 2
    }

    public enum OrdnanceGuidanceStage
    {
        Midcourse = 0,
        Autonomous = 1,
        Terminal = 2,
        Resolved = 3
    }

    [Serializable]
    public sealed class FlightTacticalState
    {
        public AirCombatIntent Intent = AirCombatIntent.FollowMission;
        public AirCombatManeuver Maneuver = AirCombatManeuver.FollowRoute;
        public DateTime IntentStartedAt;
        public DateTime ManeuverStartedAt;
        public DateTime MinimumManeuverEndAt;
        public Guid TargetFlightId;
        public Guid SupportedPendingEffectId;
        public AirCombatManeuverSide PreferredSide;
        public int RecommitCount;
        public bool ProactiveEngagementExhausted;
        public float FuelFraction = 1f;
        public bool HasTacticalAimPoint;
        public Vector3 TacticalAimPointFeet;
        public string DecisionReason = string.Empty;
        public Guid MonitoredThreatFlightId;
        public DateTime ThreateningCourseFirstObservedAt;
        public DateTime ThreateningCourseLastObservedAt;
        public int ConsecutiveThreateningCourseObservations;

        public void ObserveThreatCandidate(
            Guid threatFlightId,
            DateTime observedAt,
            TimeSpan maximumObservationGap)
        {
            if (threatFlightId == Guid.Empty)
            {
                MonitoredThreatFlightId = Guid.Empty;
                ThreateningCourseFirstObservedAt = default;
                ThreateningCourseLastObservedAt = default;
                ConsecutiveThreateningCourseObservations = 0;
                return;
            }

            if (MonitoredThreatFlightId != threatFlightId
                || ThreateningCourseLastObservedAt == default
                || observedAt < ThreateningCourseLastObservedAt
                || observedAt - ThreateningCourseLastObservedAt
                > maximumObservationGap)
            {
                MonitoredThreatFlightId = threatFlightId;
                ThreateningCourseFirstObservedAt = observedAt;
                ThreateningCourseLastObservedAt = observedAt;
                ConsecutiveThreateningCourseObservations = 1;
                return;
            }

            ThreateningCourseLastObservedAt = observedAt;
            ConsecutiveThreateningCourseObservations++;
        }

        public bool HasPersistentThreatObservation(
            Guid threatFlightId,
            DateTime observedAt,
            TimeSpan requiredDuration,
            TimeSpan maximumObservationGap)
        {
            return threatFlightId != Guid.Empty
                   && MonitoredThreatFlightId == threatFlightId
                   && ConsecutiveThreateningCourseObservations >= 2
                   && ThreateningCourseFirstObservedAt != default
                   && ThreateningCourseLastObservedAt != default
                   && observedAt >= ThreateningCourseLastObservedAt
                   && observedAt - ThreateningCourseLastObservedAt
                   <= maximumObservationGap
                   && observedAt - ThreateningCourseFirstObservedAt
                   >= requiredDuration;
        }

        /// <summary>
        /// Returns true when the intent or maneuver actually changed, so callers
        /// can record the transition instead of every repeated decision.
        /// </summary>
        public bool Apply(
            AirCombatIntent nextIntent,
            AirCombatManeuver nextManeuver,
            DateTime occurredAt,
            DateTime minimumManeuverEndAt,
            Guid targetFlightId,
            Guid supportedPendingEffectId,
            AirCombatManeuverSide preferredSide,
            Vector3 aimPointFeet,
            bool hasAimPoint,
            string reason)
        {
            var intentChanged = Intent != nextIntent;
            var maneuverChanged = Maneuver != nextManeuver;
            var engagementChanged = TargetFlightId != targetFlightId
                                    || SupportedPendingEffectId != supportedPendingEffectId;
            if (intentChanged)
                IntentStartedAt = occurredAt;
            if (maneuverChanged)
                ManeuverStartedAt = occurredAt;
            if ((Maneuver == AirCombatManeuver.Extend
                 || Maneuver == AirCombatManeuver.Drag)
                && nextIntent == AirCombatIntent.EngageTarget
                && (nextManeuver == AirCombatManeuver.Intercept
                    || nextManeuver == AirCombatManeuver.Press
                    || nextManeuver == AirCombatManeuver.Recommit))
                RecommitCount++;
            Intent = nextIntent;
            Maneuver = nextManeuver;
            if (maneuverChanged || engagementChanged)
                MinimumManeuverEndAt = minimumManeuverEndAt;
            TargetFlightId = targetFlightId;
            SupportedPendingEffectId = supportedPendingEffectId;
            PreferredSide = preferredSide;
            TacticalAimPointFeet = aimPointFeet;
            HasTacticalAimPoint = hasAimPoint;
            DecisionReason = reason ?? string.Empty;
            return intentChanged || maneuverChanged;
        }

        public void ClearCombat(DateTime occurredAt, string reason)
        {
            RecommitCount = 0;
            ProactiveEngagementExhausted = false;
            Apply(
                AirCombatIntent.FollowMission,
                AirCombatManeuver.FollowRoute,
                occurredAt,
                occurredAt,
                Guid.Empty,
                Guid.Empty,
                AirCombatManeuverSide.None,
                default,
                false,
                reason);
        }
    }

    public sealed class AirCombatEmploymentProposal
    {
        public Guid SourceFlightId;
        public Guid TargetFlightId;
        public Guid OrdnanceTypeDefinitionId;
        public int Quantity;
        public float LaunchQuality;
    }

    public sealed class AirCombatCommand
    {
        public Guid FlightId;
        public AirCombatIntent Intent;
        public AirCombatManeuver Maneuver;
        public Guid TargetFlightId;
        public Guid SupportedPendingEffectId;
        public AirCombatManeuverSide PreferredSide;
        public Vector3 AimPointFeet;
        public bool HasAimPoint;
        public float DesiredSpeedKnots;
        public DateTime MinimumManeuverEndAt;
        public string Reason = string.Empty;
        public bool ExhaustProactiveEngagement;
        public bool RequestsWvrEngagement;
        public bool RequestsSurfaceThreatRecovery;
        public bool RequestsBarcapStationRelocation;
        public bool RequestsAirToAirPassCancellation;
        public Guid ObservedThreatCandidateFlightId;
        public AirCombatEmploymentProposal Employment;
    }
}
