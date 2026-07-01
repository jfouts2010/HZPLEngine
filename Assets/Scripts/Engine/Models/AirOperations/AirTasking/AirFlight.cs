using System;
using System.Collections.Generic;
using UnityEngine;

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

    [Serializable]
    public sealed class FlightExecutionEvent
    {
        public Guid EventId = Guid.NewGuid();
        public Guid WaypointId;
        public AirWaypointAction Action;
        public DateTime OccurredAt;
        public string Detail = string.Empty;
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
        public AirTaskingLifecycleState LifecycleState = AirTaskingLifecycleState.Committed;
        public FlightExecutionPhase ExecutionPhase = FlightExecutionPhase.AwaitingTakeoff;
        public List<AirWaypoint> Route = new List<AirWaypoint>();
        public int CurrentWaypointIndex;
        public bool HasPosition;
        public Vector3 PositionFeet;
        public float HeadingDegrees;
        public bool IsWaitingAtRendezvous;
        public bool MissionAchieved;
        public Guid LaunchAirportBuildingId;
        public Guid RecoveryAirportBuildingId;
        public List<FlightExecutionEvent> ExecutionEvents = new List<FlightExecutionEvent>();

        // Derived planning summaries retained for air-tasking queries.
        public DateTime PlannedTakeoffTime;
        public DateTime EffectStart;
        public DateTime EffectEnd;
        public AirMissionArea MissionArea = new AirMissionArea();
        public int ProvidedSupportSlots;
        public List<AirSupportReservation> SupportReservations = new List<AirSupportReservation>();

        public bool IsTerminal =>
            LifecycleState == AirTaskingLifecycleState.Completed
            || LifecycleState == AirTaskingLifecycleState.Failed
            || LifecycleState == AirTaskingLifecycleState.Cancelled
            || LifecycleState == AirTaskingLifecycleState.Aborted;

        public bool IsAirborne =>
            ExecutionPhase == FlightExecutionPhase.Outbound
            || ExecutionPhase == FlightExecutionPhase.Executing
            || ExecutionPhase == FlightExecutionPhase.Returning
            || ExecutionPhase == FlightExecutionPhase.Landing;

        public bool HasPhysicallyEnded =>
            ExecutionPhase == FlightExecutionPhase.Ended
            || LifecycleState == AirTaskingLifecycleState.Cancelled;

        public AirMissionArea ActiveEffectArea
        {
            get
            {
                if (ExecutionPhase != FlightExecutionPhase.Executing
                    || Route == null
                    || Route.Count == 0)
                    return null;

                for (var index = Math.Min(CurrentWaypointIndex, Route.Count - 1);
                     index >= 0;
                     index--)
                {
                    var waypoint = Route[index];
                    if (waypoint?.Action == AirWaypointAction.StationEntry)
                        return waypoint.EffectArea;
                    if (waypoint?.Action == AirWaypointAction.ReturnToBase)
                        return null;
                }

                return null;
            }
        }
    }
}
