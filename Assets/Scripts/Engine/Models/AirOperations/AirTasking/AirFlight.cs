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

    public enum AirEscortCoverageMode
    {
        ForwardScreen = 0,
        CloseCover = 1
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
        [SerializeField]
        private string code = string.Empty;

        public Guid EventId => eventId;
        public Guid WaypointId => waypointId;
        public AirWaypointAction Action => action;
        public DateTime OccurredAt => occurredAt;
        public string Detail => detail;

        /// <summary>
        /// Stable uppercase log vocabulary for this event. Empty means the code
        /// is derived from <see cref="Action"/> when the event is written out.
        /// </summary>
        public string Code => code;

        public FlightExecutionEvent()
        {
        }

        internal FlightExecutionEvent(
            Guid waypointId,
            AirWaypointAction action,
            DateTime occurredAt,
            string detail,
            string code = "")
        {
            this.waypointId = waypointId;
            this.action = action;
            this.occurredAt = occurredAt;
            this.detail = detail;
            this.code = code ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class AerialRefuelingRecord
    {
        public Guid RecordId = Guid.NewGuid();
        public Guid TankerFlightId;
        public Guid ReceiverFlightId;
        public DateTime OccurredAt;
        public float FuelBefore;
        public float FuelAfter;
    }

    [Serializable]
    public sealed class PlannedAircraftLoadout
    {
        public Guid AircraftId;
        public List<AircraftLoadoutItem> Loadout = new List<AircraftLoadoutItem>();

        public PlannedAircraftLoadout()
        {
        }

        public PlannedAircraftLoadout(
            Guid aircraftId,
            IEnumerable<AircraftLoadoutItem> loadout)
        {
            AircraftId = aircraftId;
            Loadout = loadout
                .Select(item => new AircraftLoadoutItem(
                    item.AircraftLoadoutStationDefinitionId,
                    item.AircraftCarriageConfigurationDefinitionId,
                    item.OrdnanceTypeDefinitionId,
                    item.Count))
                .ToList();
        }
    }

    [Serializable]
    public sealed class AirFlight
    {
        public const int MaximumExecutionEvents = 1024;
        public const string TacticalTransitionCode = "DECIDE";

        public Guid FlightId = Guid.NewGuid();
        public Guid SquadronId;
        public AirFlightTaskType TaskType;
        public StrikeAssignment StrikeAssignment;
        private AirEscortCoverageMode escortCoverageMode =
            AirEscortCoverageMode.ForwardScreen;
        public Guid AuthorizedSurfaceThreatSiteId;
        public bool IsRequired = true;
        private List<Guid> aircraftIds = new List<Guid>();
        private List<Guid> protectedFlightIds = new List<Guid>();
        private List<Guid> clearedSurfaceThreatSiteIds = new List<Guid>();
        private List<PlannedAircraftLoadout> plannedAircraftLoadouts =
            new List<PlannedAircraftLoadout>();
        private AirTaskingLifecycleState lifecycleState = AirTaskingLifecycleState.Committed;
        private FlightExecutionPhase executionPhase = FlightExecutionPhase.AwaitingTakeoff;
        private List<AirWaypoint> route = new List<AirWaypoint>();
        private int currentWaypointIndex;
        private bool hasPosition;
        private Vector3 positionFeet;
        private float headingDegrees;
        private float speedKnots;
        private FlightTacticalState tacticalState = new FlightTacticalState();
        private AirRendezvousState rendezvousState =
            AirRendezvousState.NotRequired;
        private bool missionAchieved;
        private bool authorizedSurfaceThreatPenetrationGranted;
        private DateTime nextGroundAttackOpportunityAt;
        private int groundAttackOpportunitySequence;
        private List<FlightExecutionEvent> executionEvents =
            new List<FlightExecutionEvent>();
        private int droppedExecutionEventCount;
        private List<AerialRefuelingRecord> aerialRefuelingRecords =
            new List<AerialRefuelingRecord>();
        [NonSerialized] private ReadOnlyCollection<AirWaypoint> routeView;
        [NonSerialized] private ReadOnlyCollection<FlightExecutionEvent> executionEventView;
        [NonSerialized] private ReadOnlyCollection<AerialRefuelingRecord>
            aerialRefuelingRecordView;

        public int ProvidedSupportSlots;
        private List<AirSupportReservation> supportReservations =
            new List<AirSupportReservation>();

        public List<Guid> AircraftIds => aircraftIds;
        public List<Guid> ProtectedFlightIds =>
            protectedFlightIds ??= new List<Guid>();
        public IReadOnlyCollection<Guid> ClearedSurfaceThreatSiteIds =>
            (clearedSurfaceThreatSiteIds ??= new List<Guid>()).AsReadOnly();
        public List<PlannedAircraftLoadout> PlannedAircraftLoadouts => plannedAircraftLoadouts;
        public List<AirSupportReservation> SupportReservations => supportReservations;
        public AirTaskingLifecycleState LifecycleState => lifecycleState;
        public FlightExecutionPhase ExecutionPhase => executionPhase;
        public IReadOnlyList<AirWaypoint> Route =>
            routeView ??= route.AsReadOnly();
        public int CurrentWaypointIndex => currentWaypointIndex;
        public bool HasPosition => hasPosition;
        public Vector3 PositionFeet => positionFeet;
        public float HeadingDegrees => headingDegrees;
        public float SpeedKnots => speedKnots;
        public FlightTacticalState TacticalState =>
            tacticalState ??= new FlightTacticalState();
        public AirRendezvousState RendezvousState => rendezvousState;
        public bool IsWaitingAtRendezvous =>
            rendezvousState == AirRendezvousState.Holding;
        public bool HasPackageRelease =>
            rendezvousState == AirRendezvousState.NotRequired
            || rendezvousState == AirRendezvousState.Released;
        public bool MissionAchieved => missionAchieved;
        public bool IsFighterEscort =>
            TaskType == AirFlightTaskType.FighterEscort;
        public bool IsSeadEscort =>
            TaskType == AirFlightTaskType.SeadEscort;
        public bool IsEscort => IsFighterEscort || IsSeadEscort;
        public AirEscortCoverageMode EscortCoverageMode => escortCoverageMode;
        public bool IsCloseEscortActive =>
            IsEscort
            && escortCoverageMode == AirEscortCoverageMode.CloseCover;
        public bool IsDeadAttackFlight =>
            TaskType == AirFlightTaskType.DeadAttack;
        public bool IsStrikeFlight =>
            TaskType == AirFlightTaskType.Strike;
        public bool IsGroundAttackFlight =>
            IsDeadAttackFlight || IsStrikeFlight;
        public bool AuthorizedSurfaceThreatPenetrationGranted =>
            authorizedSurfaceThreatPenetrationGranted;
        public DateTime NextGroundAttackOpportunityAt =>
            nextGroundAttackOpportunityAt;
        public int GroundAttackOpportunitySequence =>
            groundAttackOpportunitySequence;
        public DateTime PlannedTakeoffTime =>
            GetRequiredWaypoint(AirWaypointAction.Takeoff).PlannedArrivalTime;
        public DateTime EffectStart => EffectWaypoints.First().PlannedArrivalTime;
        public bool HasSustainedEffect =>
            route.Any(waypoint => waypoint.Action == AirWaypointAction.StationEndpoint
                                  && waypoint.HasRepeat);
        public DateTime EffectEnd
        {
            get
            {
                var effectWaypoints = EffectWaypoints;
                if (effectWaypoints.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Flight {FlightId} route has no effect waypoint.");
                }

                var first = effectWaypoints[0];
                var repeatedEndpoint = route.LastOrDefault(waypoint =>
                    waypoint.Action == AirWaypointAction.StationEndpoint
                    && waypoint.HasRepeat);
                if (repeatedEndpoint != null)
                    return repeatedEndpoint.RepeatUntil;
                var lastMissionAction = effectWaypoints
                    .LastOrDefault(waypoint =>
                        waypoint.Action == AirWaypointAction.MissionAction);
                if (lastMissionAction != null)
                    return lastMissionAction.PlannedArrivalTime;

                var endpoint = route
                    .LastOrDefault(waypoint => waypoint.Action == AirWaypointAction.StationEndpoint
                                               && waypoint.RepeatFromWaypointId == first.WaypointId);
                if (endpoint == null)
                {
                    throw new InvalidOperationException(
                        $"Flight {FlightId} station has no endpoint.");
                }

                return endpoint.HasRepeat
                    ? endpoint.RepeatUntil
                    : endpoint.PlannedArrivalTime;
            }
        }
        public AirMissionArea MissionArea =>
            EffectWaypoints.First().EffectArea
            ?? throw new InvalidOperationException(
                $"Flight {FlightId} effect waypoint has no mission area.");
        public Guid LaunchAirportBuildingId =>
            GetRequiredWaypoint(AirWaypointAction.Takeoff).AirportBuildingId;
        public Guid RecoveryAirportBuildingId =>
            GetRequiredWaypoint(AirWaypointAction.Land, last: true).AirportBuildingId;
        public IReadOnlyList<FlightExecutionEvent> ExecutionEvents =>
            executionEventView ??= executionEvents.AsReadOnly();
        /// <summary>
        /// Events discarded by <see cref="MaximumExecutionEvents"/>, so readers
        /// can tell a short history from a truncated one.
        /// </summary>
        public int DroppedExecutionEventCount => droppedExecutionEventCount;
        public IReadOnlyList<AerialRefuelingRecord> AerialRefuelingRecords =>
            aerialRefuelingRecordView ??=
                (aerialRefuelingRecords ??= new List<AerialRefuelingRecord>())
                .AsReadOnly();

        private IReadOnlyList<AirWaypoint> EffectWaypoints =>
            route
            .Where(waypoint => waypoint.Action == AirWaypointAction.StationEntry
                               || waypoint.Action == AirWaypointAction.MissionAction)
            .ToList();

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
                    if (waypoint.Action == AirWaypointAction.StationEntry)
                        return waypoint.EffectArea;
                    if (waypoint.Action == AirWaypointAction.ReturnToBase)
                        return null;
                }

                return null;
            }
        }

        public BarcapStationCoverage ActiveBarcapCoverage
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
                    if (waypoint.Action == AirWaypointAction.StationEntry)
                        return waypoint.BarcapCoverage;
                    if (waypoint.Action == AirWaypointAction.ReturnToBase)
                        return null;
                }

                return null;
            }
        }

        public BarcapStationCoverage PlannedBarcapCoverage =>
            EffectWaypoints
                .Select(waypoint => waypoint.BarcapCoverage)
                .LastOrDefault(coverage => coverage != null);

        public void MaterializeRoute(
            IEnumerable<AirWaypoint> waypoints)
        {
            if (lifecycleState != AirTaskingLifecycleState.Committed
                || executionPhase != FlightExecutionPhase.AwaitingTakeoff
                || route.Count > 0)
            {
                throw new InvalidOperationException(
                    "A flight route can only be materialized once before takeoff.");
            }

            var materializedRoute = waypoints?.ToList();
            if (!TryValidateRoute(materializedRoute, out var reason))
            {
                throw new ArgumentException(
                    reason,
                    nameof(waypoints));
            }

            route = materializedRoute;
            routeView = null;
            currentWaypointIndex = 0;
            hasPosition = false;
            rendezvousState = route.Any(waypoint =>
                    waypoint.Action == AirWaypointAction.Rendezvous)
                ? AirRendezvousState.Enroute
                : AirRendezvousState.NotRequired;
            missionAchieved = false;
        }

        public bool TryValidateRoute(out string reason)
        {
            return TryValidateRoute(route, out reason);
        }

        internal bool TryShiftPlannedRoute(
            TimeSpan shift,
            out string reason)
        {
            reason = string.Empty;
            if (shift == TimeSpan.Zero)
                return true;
            if (executionPhase != FlightExecutionPhase.AwaitingTakeoff
                || route.Count == 0
                || executionEvents?.Count > 0)
            {
                reason =
                    "Only an unexecuted materialized route may be shifted.";
                return false;
            }

            foreach (var waypoint in route)
                waypoint.ShiftPlannedTime(shift);
            if (TryValidateRoute(route, out reason))
                return true;

            foreach (var waypoint in route)
                waypoint.ShiftPlannedTime(-shift);
            return false;
        }

        public bool TryTakeOff(DateTime occurredAt)
        {
            if (lifecycleState != AirTaskingLifecycleState.Committed
                || executionPhase != FlightExecutionPhase.AwaitingTakeoff
                || route.Count < 2
                || route[0].Action != AirWaypointAction.Takeoff)
                return false;

            var takeoff = route[0];
            positionFeet = takeoff.PositionFeet;
            hasPosition = true;
            currentWaypointIndex = 1;
            if (!TryTransitionState(FlightStateEvent.Takeoff))
                return false;
            headingDegrees = HeadingTo(positionFeet, route[1].PositionFeet);
            TacticalState.FuelFraction = 1f;
            TacticalState.ClearCombat(occurredAt, "Flight began route execution.");
            RecordEvent(takeoff, occurredAt, "Flight took off.");
            return true;
        }

        public bool TryReceiveAerialRefueling(
            Guid tankerFlightId,
            DateTime occurredAt)
        {
            if (tankerFlightId == Guid.Empty
                || lifecycleState != AirTaskingLifecycleState.Active
                || executionPhase != FlightExecutionPhase.Executing
                || TacticalState.FuelFraction >= 1f)
                return false;

            var fuelBefore = TacticalState.FuelFraction;
            TacticalState.FuelFraction = 1f;
            aerialRefuelingRecords ??= new List<AerialRefuelingRecord>();
            aerialRefuelingRecords.Add(new AerialRefuelingRecord
            {
                TankerFlightId = tankerFlightId,
                ReceiverFlightId = FlightId,
                OccurredAt = occurredAt,
                FuelBefore = fuelBefore,
                FuelAfter = TacticalState.FuelFraction
            });
            aerialRefuelingRecordView = null;
            return true;
        }

        public void UpdateKinematics(
            Vector3 position,
            float heading,
            float currentSpeedKnots = -1f)
        {
            if (!IsAirborne)
                throw new InvalidOperationException(
                    "Only an airborne flight can update its position.");

            positionFeet = position;
            headingDegrees = heading;
            if (currentSpeedKnots >= 0f)
                speedKnots = currentSpeedKnots;
            hasPosition = true;
        }

        public FlightWaypointTransition CrossCurrentWaypoint(DateTime occurredAt)
        {
            var waypoint = CurrentWaypoint;
            if (!IsAirborne || waypoint == null)
            {
                throw new InvalidOperationException(
                    $"Flight {FlightId} cannot cross a waypoint in its current state.");
            }

            switch (waypoint.Action)
            {
                case AirWaypointAction.Rendezvous:
                    RecordEvent(
                        waypoint,
                        occurredAt,
                        "Flight reached package rendezvous.");
                    currentWaypointIndex++;
                    rendezvousState = AirRendezvousState.Holding;
                    return FlightWaypointTransition.HoldingAtRendezvous;

                case AirWaypointAction.StationEntry:
                    if (executionPhase != FlightExecutionPhase.Executing)
                    {
                        RequireTransitionState(FlightStateEvent.EnterMission);
                        RecordEvent(waypoint, occurredAt, "Flight entered station.");
                    }
                    currentWaypointIndex++;
                    return FlightWaypointTransition.Advanced;

                case AirWaypointAction.StationEndpoint:
                    if (!waypoint.HasRepeat)
                    {
                        RecordEvent(
                            waypoint,
                            occurredAt,
                            "Flight reached its sweep push point.");
                        currentWaypointIndex++;
                        return FlightWaypointTransition.Advanced;
                    }

                    if (waypoint.HasRepeat && occurredAt < waypoint.RepeatUntil)
                    {
                        var repeatIndex = route.FindIndex(candidate =>
                            candidate.WaypointId == waypoint.RepeatFromWaypointId);
                        if (repeatIndex < 0)
                        {
                            Fail(occurredAt, "Station loop target is missing.");
                            return FlightWaypointTransition.Failed;
                        }

                        currentWaypointIndex = repeatIndex;
                        return FlightWaypointTransition.Advanced;
                    }

                    if (!IsGroundAttackFlight && !IsEscort)
                    {
                        missionAchieved = true;
                    }
                    RecordEvent(waypoint, occurredAt, "Flight exited station.");
                    currentWaypointIndex++;
                    return FlightWaypointTransition.Advanced;

                case AirWaypointAction.MissionAction:
                    RequireTransitionState(FlightStateEvent.EnterMission);
                    var isGroundAttackStandoff = IsGroundAttackFlight;
                    if (!isGroundAttackStandoff)
                        missionAchieved = true;
                    RecordEvent(
                        waypoint,
                        occurredAt,
                        isGroundAttackStandoff
                            ? "Flight reached its ground-attack standoff position."
                            : "Flight completed its mission action.");
                    currentWaypointIndex++;
                    return FlightWaypointTransition.Advanced;

                case AirWaypointAction.ReturnToBase:
                    RequireTransitionState(FlightStateEvent.BeginRecovery);
                    RecordEvent(waypoint, occurredAt, "Flight began recovery.");
                    currentWaypointIndex++;
                    return FlightWaypointTransition.RecoveryStarted;

                case AirWaypointAction.Approach:
                    RequireTransitionState(FlightStateEvent.BeginApproach);
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

        public bool ReleaseRendezvous(
            DateTime occurredAt,
            string reason)
        {
            if (rendezvousState != AirRendezvousState.Holding)
                return false;

            rendezvousState = AirRendezvousState.Released;
            var rendezvous = currentWaypointIndex > 0
                ? route[currentWaypointIndex - 1]
                : null;
            RecordEvent(
                rendezvous,
                occurredAt,
                string.IsNullOrWhiteSpace(reason)
                    ? "Package released from rendezvous."
                    : reason,
                AirWaypointAction.Rendezvous);
            return true;
        }

        public void UpdateMissionOutcome(
            bool achieved,
            DateTime occurredAt,
            string reason)
        {
            if (!IsGroundAttackFlight
                || !IsAirborne
                || missionAchieved == achieved)
                return;

            missionAchieved = achieved;
            RecordEvent(
                CurrentWaypoint,
                occurredAt,
                reason,
                AirWaypointAction.MissionAction);
        }

        public void UpdateSurfaceThreatPenetrationAuthorization(bool granted)
        {
            authorizedSurfaceThreatPenetrationGranted =
                IsDeadAttackFlight
                && IsAirborne
                && granted;
        }

        public bool CanEvaluateGroundAttackOpportunity(DateTime occurredAt)
        {
            return IsAirborne
                   && occurredAt >= nextGroundAttackOpportunityAt;
        }

        public int ConsumeGroundAttackOpportunity(
            DateTime occurredAt,
            double retrySeconds)
        {
            groundAttackOpportunitySequence++;
            nextGroundAttackOpportunityAt = occurredAt.AddSeconds(
                Math.Max(0d, retrySeconds));
            return groundAttackOpportunitySequence;
        }

        public bool UpdateEscortCoverageMode(
            AirEscortCoverageMode mode,
            DateTime occurredAt,
            string reason)
        {
            if (!IsEscort
                || !IsAirborne
                || executionPhase == FlightExecutionPhase.Returning
                || executionPhase == FlightExecutionPhase.Landing
                || executionPhase == FlightExecutionPhase.Ended
                || escortCoverageMode == mode)
                return false;

            escortCoverageMode = mode;
            RecordEvent(
                CurrentWaypoint,
                occurredAt,
                string.IsNullOrWhiteSpace(reason)
                    ? $"Escort changed coverage mode to {mode}."
                    : reason);
            return true;
        }

        public bool ConfirmSurfaceThreatCleared(
            Guid siteId,
            DateTime occurredAt,
            string reason)
        {
            if (siteId == Guid.Empty
                || !IsAirborne
                || (clearedSurfaceThreatSiteIds ??= new List<Guid>())
                    .Contains(siteId))
                return false;

            clearedSurfaceThreatSiteIds.Add(siteId);
            RecordEvent(
                CurrentWaypoint,
                occurredAt,
                string.IsNullOrWhiteSpace(reason)
                    ? $"Surface threat {siteId:N} was confirmed cleared."
                    : reason);
            return true;
        }

        public bool EndDeadAttackAndBeginRecovery(
            DateTime occurredAt,
            bool achieved,
            string reason)
        {
            if (!IsDeadAttackFlight
                || !IsAirborne
                || executionPhase == FlightExecutionPhase.Returning
                || executionPhase == FlightExecutionPhase.Landing)
                return false;

            var returnIndex = route.FindIndex(
                Math.Max(0, currentWaypointIndex),
                waypoint => waypoint.Action == AirWaypointAction.ReturnToBase);
            if (returnIndex < 0)
            {
                Fail(
                    occurredAt,
                    "DEAD flight could not begin recovery because its route has no remaining return waypoint.");
                return false;
            }

            missionAchieved = achieved;
            currentWaypointIndex = returnIndex;
            rendezvousState = AirRendezvousState.NotRequired;
            authorizedSurfaceThreatPenetrationGranted = false;
            RequireTransitionState(FlightStateEvent.BeginRecovery);
            RecordEvent(
                CurrentWaypoint,
                occurredAt,
                reason,
                AirWaypointAction.ReturnToBase);
            return true;
        }

        public bool EndStrikeAttackAndBeginRecovery(
            DateTime occurredAt,
            bool achieved,
            string reason)
        {
            if (!IsStrikeFlight
                || !IsAirborne
                || executionPhase == FlightExecutionPhase.Returning
                || executionPhase == FlightExecutionPhase.Landing)
                return false;

            var returnIndex = route.FindIndex(
                Math.Max(0, currentWaypointIndex),
                waypoint => waypoint.Action == AirWaypointAction.ReturnToBase);
            if (returnIndex < 0)
            {
                Fail(
                    occurredAt,
                    "Strike flight could not begin recovery because its route has no remaining return waypoint.");
                return false;
            }

            missionAchieved = achieved;
            currentWaypointIndex = returnIndex;
            rendezvousState = AirRendezvousState.NotRequired;
            RequireTransitionState(FlightStateEvent.BeginRecovery);
            RecordEvent(
                CurrentWaypoint,
                occurredAt,
                reason,
                AirWaypointAction.ReturnToBase);
            return true;
        }

        public FlightCancellationResult Cancel(DateTime occurredAt, string reason)
        {
            if (IsTerminal)
                return FlightCancellationResult.None;

            authorizedSurfaceThreatPenetrationGranted = false;
            if (!IsAirborne)
            {
                RequireTransitionState(FlightStateEvent.CancelBeforeTakeoff);
                hasPosition = false;
                rendezvousState = AirRendezvousState.NotRequired;
                return FlightCancellationResult.Cancelled;
            }

            BeginAbortRecovery(occurredAt, reason);
            RequireTransitionState(FlightStateEvent.AbortAirborne);

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

        public void ReplaceRecoveryRoute(IEnumerable<AirWaypoint> recoveryWaypoints)
        {
            if (!IsAirborne
                || (executionPhase != FlightExecutionPhase.Returning
                    && executionPhase != FlightExecutionPhase.Landing)
                || currentWaypointIndex < 0
                || currentWaypointIndex > route.Count)
            {
                throw new InvalidOperationException(
                    "Only a returning flight can replace its unflown recovery route.");
            }

            var replacement = recoveryWaypoints?.ToList();
            if (replacement.Count == 0
                || replacement[replacement.Count - 1].Action != AirWaypointAction.Land)
            {
                throw new ArgumentException(
                    "A recovery route must end with a landing waypoint.",
                    nameof(recoveryWaypoints));
            }

            var amendedRoute = route.Take(currentWaypointIndex)
                .Concat(replacement)
                .ToList();
            if (!TryValidateRoute(amendedRoute, out var reason))
                throw new ArgumentException(reason, nameof(recoveryWaypoints));

            route = amendedRoute;
            routeView = null;
            RequireTransitionState(FlightStateEvent.BeginRecovery);
        }

        public bool TryReplaceUnflownBarcapStationRoute(
            DateTime occurredAt,
            string reason,
            IEnumerable<AirWaypoint> replacementWaypoints)
        {
            if (TaskType != AirFlightTaskType.Barcap
                || lifecycleState != AirTaskingLifecycleState.Active
                || !IsAirborne
                || executionPhase == FlightExecutionPhase.Returning
                || executionPhase == FlightExecutionPhase.Landing
                || currentWaypointIndex < 0
                || currentWaypointIndex > route.Count)
                return false;

            var replacement = replacementWaypoints?.ToList();
            if (replacement == null
                || replacement.Count == 0
                || replacement[replacement.Count - 1].Action
                != AirWaypointAction.Land
                || !replacement.Any(waypoint =>
                    waypoint.Action == AirWaypointAction.StationEntry
                    && waypoint.BarcapCoverage != null))
                return false;

            var previousWaypoint = CurrentWaypoint;
            var amendedRoute = route
                .Take(Mathf.Clamp(currentWaypointIndex, 0, route.Count))
                .ToList();
            var openStation = amendedRoute
                .LastOrDefault(waypoint =>
                    waypoint.Action == AirWaypointAction.StationEntry
                    && !amendedRoute.Any(candidate =>
                        candidate.Action == AirWaypointAction.StationEndpoint
                        && candidate.RepeatFromWaypointId == waypoint.WaypointId));
            if (openStation != null)
            {
                var stationExitTime = amendedRoute.Count == 0
                    ? occurredAt
                    : amendedRoute[amendedRoute.Count - 1].PlannedArrivalTime > occurredAt
                        ? amendedRoute[amendedRoute.Count - 1].PlannedArrivalTime
                        : occurredAt;
                amendedRoute.Add(new AirWaypoint(
                    positionFeet,
                    AirWaypointAction.StationEndpoint,
                    stationExitTime,
                    hasRepeat: true,
                    repeatFromWaypointId: openStation.WaypointId,
                    repeatUntil: stationExitTime));
            }

            var replacementStartIndex = amendedRoute.Count;
            amendedRoute.AddRange(replacement);
            if (!TryValidateRoute(amendedRoute, out _))
                return false;

            route = amendedRoute;
            routeView = null;
            currentWaypointIndex = replacementStartIndex;
            RequireTransitionState(FlightStateEvent.RelocateMission);
            rendezvousState = AirRendezvousState.NotRequired;
            missionAchieved = false;
            RecordEvent(
                previousWaypoint,
                occurredAt,
                string.IsNullOrWhiteSpace(reason)
                    ? "BARCAP station was displaced rearward."
                    : reason,
                AirWaypointAction.Transit);
            return true;
        }

        public FlightCancellationResult AbortAndReplaceRecoveryRoute(
            DateTime occurredAt,
            string reason,
            IEnumerable<AirWaypoint> recoveryWaypoints)
        {
            if (lifecycleState == AirTaskingLifecycleState.Completed
                || lifecycleState == AirTaskingLifecycleState.Failed
                || lifecycleState == AirTaskingLifecycleState.Cancelled
                || lifecycleState == AirTaskingLifecycleState.Aborted && !IsAirborne)
                return FlightCancellationResult.None;
            if (!IsAirborne)
                return Cancel(occurredAt, reason);

            var replacement = recoveryWaypoints?.ToList();
            if (replacement == null
                || replacement.Count == 0
                || replacement[replacement.Count - 1].Action != AirWaypointAction.Land)
            {
                throw new ArgumentException(
                    "A recovery route must end with a landing waypoint.",
                    nameof(recoveryWaypoints));
            }

            var currentWaypoint = CurrentWaypoint;
            var amendedRoute = route
                .Take(Mathf.Clamp(currentWaypointIndex, 0, route.Count))
                .ToList();
            EnsureAbortRouteHasMissionSemantics(amendedRoute, occurredAt);
            var recoveryStartIndex = amendedRoute.Count;
            amendedRoute.AddRange(replacement);

            route = amendedRoute;
            routeView = null;
            currentWaypointIndex = recoveryStartIndex;
            RequireTransitionState(FlightStateEvent.AbortAirborne);
            rendezvousState = AirRendezvousState.NotRequired;
            authorizedSurfaceThreatPenetrationGranted = false;
            RecordEvent(currentWaypoint, occurredAt, reason, AirWaypointAction.ReturnToBase);
            return FlightCancellationResult.Aborted;
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
            rendezvousState = AirRendezvousState.NotRequired;
            authorizedSurfaceThreatPenetrationGranted = false;
            RequireTransitionState(
                FlightStateEvent.Land,
                missionAchieved);
        }

        public void Fail(DateTime occurredAt, string reason)
        {
            RequireTransitionState(FlightStateEvent.Fail);
            hasPosition = false;
            rendezvousState = AirRendezvousState.NotRequired;
            authorizedSurfaceThreatPenetrationGranted = false;
            RecordEvent(
                CurrentWaypoint,
                occurredAt,
                reason,
                AirWaypointAction.ReturnToBase);
        }

        /// <summary>
        /// Records a tactical intent or maneuver change. The caller formats the
        /// detail because it holds the doctrine and target context; this type
        /// only owns the event list.
        /// </summary>
        public void RecordTacticalTransition(DateTime occurredAt, string detail)
        {
            RecordEvent(
                CurrentWaypoint,
                occurredAt,
                detail,
                AirWaypointAction.Transit,
                TacticalTransitionCode);
        }

        private void RecordEvent(
            AirWaypoint waypoint,
            DateTime occurredAt,
            string detail,
            AirWaypointAction fallbackAction = AirWaypointAction.Transit,
            string code = "")
        {
            executionEvents.Add(new FlightExecutionEvent(
                waypoint?.WaypointId ?? Guid.Empty,
                waypoint?.Action ?? fallbackAction,
                occurredAt,
                detail,
                code));

            // A long-lived station flight can outlive its own history. Drop the
            // oldest entries but remember how many, so the log can say so
            // instead of silently starting mid-sortie.
            var excess = executionEvents.Count - MaximumExecutionEvents;
            if (excess <= 0)
                return;

            executionEvents.RemoveRange(0, excess);
            droppedExecutionEventCount += excess;
        }

        private void EnsureAbortRouteHasMissionSemantics(
            ICollection<AirWaypoint> amendedRoute,
            DateTime occurredAt)
        {
            var originalEffect = EffectWaypoints.FirstOrDefault();
            if (originalEffect == null)
                return;

            if (!amendedRoute.Any(IsEffectWaypoint))
            {
                amendedRoute.Add(new AirWaypoint(
                    positionFeet,
                    AirWaypointAction.MissionAction,
                    occurredAt,
                    originalEffect.EffectArea));
                return;
            }

            var stationEntries = amendedRoute
                .Where(waypoint => waypoint.Action == AirWaypointAction.StationEntry)
                .ToList();
            foreach (var station in stationEntries)
            {
                var hasEndpoint = amendedRoute.Any(waypoint =>
                    waypoint.Action == AirWaypointAction.StationEndpoint
                    && waypoint.HasRepeat
                    && waypoint.RepeatFromWaypointId == station.WaypointId);
                if (hasEndpoint)
                    continue;

                amendedRoute.Add(new AirWaypoint(
                    positionFeet,
                    AirWaypointAction.StationEndpoint,
                    occurredAt,
                    hasRepeat: true,
                    repeatFromWaypointId: station.WaypointId,
                    repeatUntil: occurredAt));
            }
        }

        private static bool IsEffectWaypoint(AirWaypoint waypoint)
        {
            return waypoint.Action == AirWaypointAction.StationEntry
                   || waypoint.Action == AirWaypointAction.MissionAction;
        }

        private AirWaypoint GetRequiredWaypoint(
            AirWaypointAction action,
            bool last = false)
        {
            var waypoint = last
                ? route.LastOrDefault(candidate => candidate.Action == action)
                : route.FirstOrDefault(candidate => candidate.Action == action);
            return waypoint ?? throw new InvalidOperationException(
                $"Flight {FlightId} route has no {action} waypoint.");
        }

        private void BeginAbortRecovery(DateTime occurredAt, string reason)
        {
            rendezvousState = AirRendezvousState.NotRequired;
            authorizedSurfaceThreatPenetrationGranted = false;
            if (executionPhase == FlightExecutionPhase.Returning
                || executionPhase == FlightExecutionPhase.Landing)
                return;

            var returnIndex = route.FindIndex(
                Math.Max(0, currentWaypointIndex),
                waypoint => waypoint.Action == AirWaypointAction.ReturnToBase);
            if (returnIndex < 0)
            {
                Fail(
                    occurredAt,
                    string.IsNullOrWhiteSpace(reason)
                        ? "Aborted flight has no return waypoint."
                        : reason);
                return;
            }

            RequireTransitionState(FlightStateEvent.BeginRecovery);
            currentWaypointIndex = returnIndex;
        }

        private bool TryTransitionState(
            FlightStateEvent stateEvent,
            bool achieved = false)
        {
            if (!FlightStateMachine.TryResolve(
                    lifecycleState,
                    executionPhase,
                    stateEvent,
                    achieved,
                    out var transition))
                return false;

            lifecycleState = transition.LifecycleState;
            executionPhase = transition.ExecutionPhase;
            return true;
        }

        private void RequireTransitionState(
            FlightStateEvent stateEvent,
            bool achieved = false)
        {
            if (TryTransitionState(stateEvent, achieved))
                return;

            throw new InvalidOperationException(
                $"Flight {FlightId} cannot apply {stateEvent} while "
                + $"{lifecycleState}/{executionPhase}.");
        }

        private static float HeadingTo(Vector3 from, Vector3 to)
        {
            return Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
        }

        private static bool TryValidateRoute(
            IReadOnlyList<AirWaypoint> waypoints,
            out string reason)
        {
            reason = string.Empty;
            if (waypoints == null
                || waypoints.Count < 2
                || waypoints[0]?.Action != AirWaypointAction.Takeoff
                || waypoints[waypoints.Count - 1]?.Action != AirWaypointAction.Land)
            {
                reason = "A materialized flight route must begin with takeoff and end with landing.";
                return false;
            }

            if (waypoints.Any(waypoint =>
                    waypoint == null || waypoint.WaypointId == Guid.Empty)
                || waypoints.Select(waypoint => waypoint.WaypointId).Distinct().Count()
                != waypoints.Count)
            {
                reason = "A materialized flight route must contain unique, valid waypoints.";
                return false;
            }

            if (waypoints[0].AirportBuildingId == Guid.Empty
                || waypoints[waypoints.Count - 1].AirportBuildingId == Guid.Empty)
            {
                reason = "Takeoff and landing waypoints must identify their airports.";
                return false;
            }
            if (waypoints.Count(waypoint => waypoint.Action == AirWaypointAction.Takeoff) != 1
                || waypoints.Count(waypoint => waypoint.Action == AirWaypointAction.Land) != 1)
            {
                reason = "A flight route must contain exactly one takeoff and one landing.";
                return false;
            }

            for (var index = 1; index < waypoints.Count; index++)
            {
                if (waypoints[index].PlannedArrivalTime
                    < waypoints[index - 1].PlannedArrivalTime)
                {
                    reason = "Flight waypoint times must be ordered.";
                    return false;
                }
            }

            var effects = waypoints
                .Where(waypoint => waypoint.Action == AirWaypointAction.StationEntry
                                   || waypoint.Action == AirWaypointAction.MissionAction)
                .ToList();
            if (effects.Count == 0 || effects.Any(waypoint => waypoint.EffectArea == null))
            {
                reason = "A flight route must identify its mission area on a semantic effect waypoint.";
                return false;
            }
            foreach (var station in effects.Where(waypoint =>
                         waypoint.Action == AirWaypointAction.StationEntry))
            {
                if (!waypoints.Any(waypoint =>
                        waypoint.Action == AirWaypointAction.StationEndpoint
                        && waypoint.RepeatFromWaypointId == station.WaypointId))
                {
                    reason = "Every station entry must have a station endpoint.";
                    return false;
                }
            }

            foreach (var endpoint in waypoints.Where(waypoint =>
                         waypoint.Action == AirWaypointAction.StationEndpoint))
            {
                var stationIndex = waypoints
                    .Select((waypoint, index) => new { Waypoint = waypoint, Index = index })
                    .Where(entry => entry.Waypoint.Action == AirWaypointAction.StationEntry
                                    && entry.Waypoint.WaypointId
                                    == endpoint.RepeatFromWaypointId)
                    .Select(entry => entry.Index)
                    .DefaultIfEmpty(-1)
                    .First();
                var endpointIndex = waypoints
                    .Select((waypoint, index) => new { Waypoint = waypoint, Index = index })
                    .Where(entry => ReferenceEquals(entry.Waypoint, endpoint))
                    .Select(entry => entry.Index)
                    .DefaultIfEmpty(-1)
                    .First();
                if (stationIndex < 0
                    || endpointIndex <= stationIndex
                    || endpoint.HasRepeat
                    && endpoint.RepeatUntil < endpoint.PlannedArrivalTime
                    || !endpoint.HasRepeat
                    && !waypoints.Skip(endpointIndex + 1).Any(waypoint =>
                        waypoint.Action == AirWaypointAction.MissionAction))
                {
                    reason = "A station endpoint must follow a valid entry and either repeat or lead to a mission action.";
                    return false;
                }
            }

            return true;
        }
    }
}
