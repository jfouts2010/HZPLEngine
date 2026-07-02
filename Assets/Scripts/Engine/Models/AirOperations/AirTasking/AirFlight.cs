using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

namespace Models.Gameplay.Campaign
{
    public enum FlightExecutionPhase
    {
        AwaitingTakeoff = 0,
        Outbound = 1,
        Executing = 2,
        Returning = 3,
        Landing = 4,
        Ended = 5
    }

    public enum FlightCancellationResult
    {
        None = 0,
        Cancelled = 1,
        Aborted = 2
    }

    public enum FlightWaypointTransition
    {
        Advanced = 0,
        HoldingAtRendezvous = 1,
        RecoveryStarted = 2,
        LandingRequired = 3,
        Failed = 4
    }

    [Serializable]
    public sealed class FlightExecutionEvent
    {
        [SerializeField, FormerlySerializedAs("EventId")]
        private Guid eventId = Guid.NewGuid();
        [SerializeField, FormerlySerializedAs("WaypointId")]
        private Guid waypointId;
        [SerializeField, FormerlySerializedAs("Action")]
        private AirWaypointAction action;
        [SerializeField, FormerlySerializedAs("OccurredAt")]
        private DateTime occurredAt;
        [SerializeField, FormerlySerializedAs("Detail")]
        private string detail = string.Empty;

        public Guid EventId => eventId;
        public Guid WaypointId => waypointId;
        public AirWaypointAction Action => action;
        public DateTime OccurredAt => occurredAt;
        public string Detail => detail;

        public FlightExecutionEvent()
        {
        }

        internal FlightExecutionEvent(
            Guid waypointId,
            AirWaypointAction action,
            DateTime occurredAt,
            string detail)
        {
            this.waypointId = waypointId;
            this.action = action;
            this.occurredAt = occurredAt;
            this.detail = detail ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class AirFlight
    {
        public Guid FlightId = Guid.NewGuid();
        public Guid OwningPackageId;
        public Guid SquadronId;
        public AirMissionRequestType MissionType;
        public bool IsRequired = true;
        public List<Guid> AircraftIds = new List<Guid>();

        [SerializeField, FormerlySerializedAs("LifecycleState")]
        private AirTaskingLifecycleState lifecycleState = AirTaskingLifecycleState.Committed;
        [SerializeField, FormerlySerializedAs("ExecutionPhase")]
        private FlightExecutionPhase executionPhase = FlightExecutionPhase.AwaitingTakeoff;
        [SerializeField, FormerlySerializedAs("Route")]
        private List<AirWaypoint> route = new List<AirWaypoint>();
        [SerializeField, FormerlySerializedAs("CurrentWaypointIndex")]
        private int currentWaypointIndex;
        [SerializeField, FormerlySerializedAs("HasPosition")]
        private bool hasPosition;
        [SerializeField, FormerlySerializedAs("PositionFeet")]
        private Vector3 positionFeet;
        [SerializeField, FormerlySerializedAs("HeadingDegrees")]
        private float headingDegrees;
        [SerializeField, FormerlySerializedAs("IsWaitingAtRendezvous")]
        private bool isWaitingAtRendezvous;
        [SerializeField, FormerlySerializedAs("MissionAchieved")]
        private bool missionAchieved;
        [SerializeField, FormerlySerializedAs("LaunchAirportBuildingId")]
        private Guid launchAirportBuildingId;
        [SerializeField, FormerlySerializedAs("RecoveryAirportBuildingId")]
        private Guid recoveryAirportBuildingId;
        [SerializeField, FormerlySerializedAs("ExecutionEvents")]
        private List<FlightExecutionEvent> executionEvents =
            new List<FlightExecutionEvent>();
        [NonSerialized] private ReadOnlyCollection<AirWaypoint> routeView;
        [NonSerialized] private ReadOnlyCollection<FlightExecutionEvent> executionEventView;

        // Derived planning summaries retained for air-tasking queries.
        public DateTime PlannedTakeoffTime;
        public DateTime EffectStart;
        public DateTime EffectEnd;
        public AirMissionArea MissionArea = new AirMissionArea();
        public int ProvidedSupportSlots;
        public List<AirSupportReservation> SupportReservations = new List<AirSupportReservation>();

        public AirTaskingLifecycleState LifecycleState => lifecycleState;
        public FlightExecutionPhase ExecutionPhase => executionPhase;
        public IReadOnlyList<AirWaypoint> Route =>
            routeView ??= (route ??= new List<AirWaypoint>()).AsReadOnly();
        public int CurrentWaypointIndex => currentWaypointIndex;
        public bool HasPosition => hasPosition;
        public Vector3 PositionFeet => positionFeet;
        public float HeadingDegrees => headingDegrees;
        public bool IsWaitingAtRendezvous => isWaitingAtRendezvous;
        public bool MissionAchieved => missionAchieved;
        public Guid LaunchAirportBuildingId => launchAirportBuildingId;
        public Guid RecoveryAirportBuildingId => recoveryAirportBuildingId;
        public IReadOnlyList<FlightExecutionEvent> ExecutionEvents =>
            executionEventView ??=
                (executionEvents ??= new List<FlightExecutionEvent>()).AsReadOnly();

        public bool IsTerminal =>
            lifecycleState == AirTaskingLifecycleState.Completed
            || lifecycleState == AirTaskingLifecycleState.Failed
            || lifecycleState == AirTaskingLifecycleState.Cancelled
            || lifecycleState == AirTaskingLifecycleState.Aborted;

        public bool IsAirborne =>
            executionPhase == FlightExecutionPhase.Outbound
            || executionPhase == FlightExecutionPhase.Executing
            || executionPhase == FlightExecutionPhase.Returning
            || executionPhase == FlightExecutionPhase.Landing;

        public bool HasPhysicallyEnded =>
            executionPhase == FlightExecutionPhase.Ended;

        public AirWaypoint CurrentWaypoint =>
            currentWaypointIndex >= 0 && currentWaypointIndex < route.Count
                ? route[currentWaypointIndex]
                : null;

        public AirMissionArea ActiveEffectArea
        {
            get
            {
                if (executionPhase != FlightExecutionPhase.Executing
                    || route.Count == 0)
                    return null;

                for (var index = Math.Min(currentWaypointIndex, route.Count - 1);
                     index >= 0;
                     index--)
                {
                    var waypoint = route[index];
                    if (waypoint?.Action == AirWaypointAction.StationEntry)
                        return waypoint.EffectArea;
                    if (waypoint?.Action == AirWaypointAction.ReturnToBase)
                        return null;
                }

                return null;
            }
        }

        public void MaterializeRoute(
            IEnumerable<AirWaypoint> waypoints,
            Guid launchAirportId,
            DateTime takeoffTime)
        {
            if (lifecycleState != AirTaskingLifecycleState.Committed
                || executionPhase != FlightExecutionPhase.AwaitingTakeoff
                || route.Count > 0)
            {
                throw new InvalidOperationException(
                    "A flight route can only be materialized once before takeoff.");
            }

            var materializedRoute = waypoints?.ToList()
                                    ?? throw new ArgumentNullException(nameof(waypoints));
            if (materializedRoute.Count < 2
                || materializedRoute[0]?.Action != AirWaypointAction.Takeoff
                || materializedRoute[materializedRoute.Count - 1]?.Action
                != AirWaypointAction.Land)
            {
                throw new ArgumentException(
                    "A materialized flight route must begin with takeoff and end with landing.",
                    nameof(waypoints));
            }

            route = materializedRoute;
            routeView = null;
            currentWaypointIndex = 0;
            hasPosition = false;
            isWaitingAtRendezvous = false;
            missionAchieved = false;
            launchAirportBuildingId = launchAirportId;
            recoveryAirportBuildingId = launchAirportId;
            PlannedTakeoffTime = takeoffTime;
        }

        public bool TryTakeOff(DateTime occurredAt)
        {
            if (lifecycleState != AirTaskingLifecycleState.Committed
                || executionPhase != FlightExecutionPhase.AwaitingTakeoff
                || route.Count < 2
                || route[0]?.Action != AirWaypointAction.Takeoff)
                return false;

            var takeoff = route[0];
            positionFeet = takeoff.PositionFeet;
            hasPosition = true;
            currentWaypointIndex = 1;
            lifecycleState = AirTaskingLifecycleState.Active;
            executionPhase = FlightExecutionPhase.Outbound;
            headingDegrees = HeadingTo(positionFeet, route[1].PositionFeet);
            RecordEvent(takeoff, occurredAt, "Flight took off.");
            return true;
        }

        public void UpdateKinematics(Vector3 position, float heading)
        {
            if (!IsAirborne)
                throw new InvalidOperationException(
                    "Only an airborne flight can update its position.");

            positionFeet = position;
            headingDegrees = heading;
            hasPosition = true;
        }

        public FlightWaypointTransition CrossCurrentWaypoint(DateTime occurredAt)
        {
            var waypoint = CurrentWaypoint;
            if (!IsAirborne || waypoint == null)
            {
                Fail(occurredAt, "Flight encountered an invalid waypoint.");
                return FlightWaypointTransition.Failed;
            }

            switch (waypoint.Action)
            {
                case AirWaypointAction.Rendezvous:
                    RecordEvent(
                        waypoint,
                        occurredAt,
                        "Flight reached package rendezvous.");
                    currentWaypointIndex++;
                    isWaitingAtRendezvous = true;
                    return FlightWaypointTransition.HoldingAtRendezvous;

                case AirWaypointAction.StationEntry:
                    if (executionPhase != FlightExecutionPhase.Executing)
                    {
                        executionPhase = FlightExecutionPhase.Executing;
                        RecordEvent(waypoint, occurredAt, "Flight entered station.");
                    }
                    currentWaypointIndex++;
                    return FlightWaypointTransition.Advanced;

                case AirWaypointAction.StationEndpoint:
                    if (waypoint.HasRepeat && occurredAt < waypoint.RepeatUntil)
                    {
                        var repeatIndex = route.FindIndex(candidate =>
                            candidate != null
                            && candidate.WaypointId == waypoint.RepeatFromWaypointId);
                        if (repeatIndex < 0)
                        {
                            Fail(occurredAt, "Station loop target is missing.");
                            return FlightWaypointTransition.Failed;
                        }

                        currentWaypointIndex = repeatIndex;
                        return FlightWaypointTransition.Advanced;
                    }

                    missionAchieved = true;
                    RecordEvent(waypoint, occurredAt, "Flight exited station.");
                    currentWaypointIndex++;
                    return FlightWaypointTransition.Advanced;

                case AirWaypointAction.MissionAction:
                    executionPhase = FlightExecutionPhase.Executing;
                    missionAchieved = true;
                    RecordEvent(
                        waypoint,
                        occurredAt,
                        "Flight completed its mission action.");
                    currentWaypointIndex++;
                    return FlightWaypointTransition.Advanced;

                case AirWaypointAction.ReturnToBase:
                    executionPhase = FlightExecutionPhase.Returning;
                    RecordEvent(waypoint, occurredAt, "Flight began recovery.");
                    currentWaypointIndex++;
                    return FlightWaypointTransition.RecoveryStarted;

                case AirWaypointAction.Approach:
                    executionPhase = FlightExecutionPhase.Landing;
                    RecordEvent(waypoint, occurredAt, "Flight reached approach.");
                    currentWaypointIndex++;
                    return FlightWaypointTransition.Advanced;

                case AirWaypointAction.Land:
                    return FlightWaypointTransition.LandingRequired;

                default:
                    currentWaypointIndex++;
                    return FlightWaypointTransition.Advanced;
            }
        }

        public bool ReleaseRendezvous()
        {
            if (!isWaitingAtRendezvous)
                return false;

            isWaitingAtRendezvous = false;
            return true;
        }

        public FlightCancellationResult Cancel(DateTime occurredAt, string reason)
        {
            if (IsTerminal)
                return FlightCancellationResult.None;

            if (!IsAirborne)
            {
                lifecycleState = AirTaskingLifecycleState.Cancelled;
                executionPhase = FlightExecutionPhase.Ended;
                hasPosition = false;
                isWaitingAtRendezvous = false;
                return FlightCancellationResult.Cancelled;
            }

            lifecycleState = AirTaskingLifecycleState.Aborted;
            BeginAbortRecovery(occurredAt, reason);

            return FlightCancellationResult.Aborted;
        }

        public bool ContinueAbortRecovery(DateTime occurredAt)
        {
            if (lifecycleState != AirTaskingLifecycleState.Aborted
                || !IsAirborne
                || executionPhase == FlightExecutionPhase.Returning
                || executionPhase == FlightExecutionPhase.Landing)
                return false;

            BeginAbortRecovery(occurredAt, "Flight continued its aborted recovery.");
            return true;
        }

        public void ReplaceRecoveryRoute(
            Guid recoveryAirportId,
            IEnumerable<AirWaypoint> recoveryWaypoints)
        {
            if (!IsAirborne
                || executionPhase != FlightExecutionPhase.Returning
                || currentWaypointIndex < 0
                || currentWaypointIndex > route.Count)
            {
                throw new InvalidOperationException(
                    "Only a returning flight can replace its unflown recovery route.");
            }

            var replacement = recoveryWaypoints?.ToList()
                              ?? throw new ArgumentNullException(nameof(recoveryWaypoints));
            if (replacement.Count == 0
                || replacement[replacement.Count - 1]?.Action != AirWaypointAction.Land)
            {
                throw new ArgumentException(
                    "A recovery route must end with a landing waypoint.",
                    nameof(recoveryWaypoints));
            }

            route.RemoveRange(currentWaypointIndex, route.Count - currentWaypointIndex);
            route.AddRange(replacement);
            recoveryAirportBuildingId = recoveryAirportId;
        }

        public void Land(DateTime occurredAt)
        {
            var waypoint = CurrentWaypoint;
            if (!IsAirborne || waypoint?.Action != AirWaypointAction.Land)
                throw new InvalidOperationException(
                    "A flight can only land at its current landing waypoint.");

            RecordEvent(waypoint, occurredAt, "Flight landed.");
            currentWaypointIndex++;
            hasPosition = false;
            isWaitingAtRendezvous = false;
            executionPhase = FlightExecutionPhase.Ended;
            if (lifecycleState != AirTaskingLifecycleState.Aborted)
            {
                lifecycleState = missionAchieved
                    ? AirTaskingLifecycleState.Completed
                    : AirTaskingLifecycleState.Failed;
            }
        }

        public void Fail(DateTime occurredAt, string reason)
        {
            lifecycleState = AirTaskingLifecycleState.Failed;
            executionPhase = FlightExecutionPhase.Ended;
            hasPosition = false;
            isWaitingAtRendezvous = false;
            RecordEvent(
                CurrentWaypoint,
                occurredAt,
                reason ?? string.Empty,
                AirWaypointAction.ReturnToBase);
        }

        private void RecordEvent(
            AirWaypoint waypoint,
            DateTime occurredAt,
            string detail,
            AirWaypointAction fallbackAction = AirWaypointAction.Transit)
        {
            executionEvents ??= new List<FlightExecutionEvent>();
            executionEvents.Add(new FlightExecutionEvent(
                waypoint?.WaypointId ?? Guid.Empty,
                waypoint?.Action ?? fallbackAction,
                occurredAt,
                detail));
        }

        private void BeginAbortRecovery(DateTime occurredAt, string reason)
        {
            isWaitingAtRendezvous = false;
            if (executionPhase == FlightExecutionPhase.Returning
                || executionPhase == FlightExecutionPhase.Landing)
                return;

            var returnIndex = route.FindIndex(
                Math.Max(0, currentWaypointIndex),
                waypoint => waypoint?.Action == AirWaypointAction.ReturnToBase);
            if (returnIndex < 0)
            {
                Fail(
                    occurredAt,
                    string.IsNullOrWhiteSpace(reason)
                        ? "Aborted flight has no return waypoint."
                        : reason);
                return;
            }

            executionPhase = FlightExecutionPhase.Returning;
            currentWaypointIndex = returnIndex;
        }

        private static float HeadingTo(Vector3 from, Vector3 to)
        {
            return Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
        }
    }
}
