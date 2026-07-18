using System;
using UnityEngine.Serialization;
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
        [SerializeField, FormerlySerializedAs("WaypointId")]
        private Guid waypointId = Guid.NewGuid();
        [SerializeField, FormerlySerializedAs("PositionFeet")]
        private Vector3 positionFeet;
        [SerializeField, FormerlySerializedAs("Action")]
        private AirWaypointAction action = AirWaypointAction.Transit;
        [SerializeField, FormerlySerializedAs("PlannedArrivalTime")]
        private DateTime plannedArrivalTime;
        [SerializeField, FormerlySerializedAs("EffectArea")]
        private AirMissionArea effectArea;
        [SerializeField]
        private BarcapStationCoverage barcapCoverage;
        [SerializeField, FormerlySerializedAs("HasRepeat")]
        private bool hasRepeat;
        [SerializeField, FormerlySerializedAs("RepeatFromWaypointId")]
        private Guid repeatFromWaypointId;
        [SerializeField, FormerlySerializedAs("RepeatUntil")]
        private DateTime repeatUntil;
        [SerializeField]
        private Guid airportBuildingId;

        public Guid WaypointId => waypointId;
        public Vector3 PositionFeet => positionFeet;
        public AirWaypointAction Action => action;
        public DateTime PlannedArrivalTime => plannedArrivalTime;
        public AirMissionArea EffectArea => effectArea == null
            ? null
            : new AirMissionArea(effectArea.CenterTileId, effectArea.RadiusTiles);
        public BarcapStationCoverage BarcapCoverage => barcapCoverage?.Clone();
        public bool HasRepeat => hasRepeat;
        public Guid RepeatFromWaypointId => repeatFromWaypointId;
        public DateTime RepeatUntil => repeatUntil;
        public Guid AirportBuildingId => airportBuildingId;

        public AirWaypoint()
        {
        }

        internal AirWaypoint(
            Vector3 positionFeet,
            AirWaypointAction action,
            DateTime plannedArrivalTime,
            AirMissionArea effectArea = null,
            bool hasRepeat = false,
            Guid repeatFromWaypointId = default,
            DateTime repeatUntil = default,
            Guid airportBuildingId = default,
            BarcapStationCoverage barcapCoverage = null)
        {
            this.positionFeet = positionFeet;
            this.action = action;
            this.plannedArrivalTime = plannedArrivalTime;
            this.effectArea = effectArea == null
                ? null
                : new AirMissionArea(effectArea.CenterTileId, effectArea.RadiusTiles);
            this.barcapCoverage = barcapCoverage?.Clone();
            this.hasRepeat = hasRepeat;
            this.repeatFromWaypointId = repeatFromWaypointId;
            this.repeatUntil = repeatUntil;
            this.airportBuildingId = airportBuildingId;
        }
    }
}
