using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    public enum AirWaypointAction
    {
        Takeoff = 0,
        Transit = 1,
        Rendezvous = 2,
        StationEntry = 3,
        StationEndpoint = 4,
        MissionAction = 5,
        ReturnToBase = 6,
        Approach = 7,
        Land = 8
    }

    [Serializable]
    public sealed class AirWaypoint
    {
        public Guid WaypointId = Guid.NewGuid();
        public Vector3 PositionFeet;
        public AirWaypointAction Action = AirWaypointAction.Transit;
        public DateTime PlannedArrivalTime;
        public AirMissionArea EffectArea;
        public bool HasRepeat;
        public Guid RepeatFromWaypointId;
        public DateTime RepeatUntil;
    }
}
