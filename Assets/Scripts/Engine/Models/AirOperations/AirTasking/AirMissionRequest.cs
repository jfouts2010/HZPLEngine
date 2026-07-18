using System;
using System.Collections.Generic;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AirMissionRequest
    {
        public Guid MissionRequestId = Guid.NewGuid();
        public Alliance Alliance;
        public AirMissionRequestType RequestType;
        public AirMissionRequestFulfillmentPattern FulfillmentPattern;
        public AirMissionRequestState State = AirMissionRequestState.Actionable;
        public AirMissionArea MissionArea = new AirMissionArea();
        public DateTime CreatedAt;
        public DateTime EffectStart;
        public DateTime EffectEnd;
        public int PlanningCycle;
        public int DesiredAircraftStrength;
        public int DesiredSupportSlots;
        public BarcapBarrierPlan BarcapBarrier;
        public float Priority;
        public Dictionary<string, float> PriorityComponents = new Dictionary<string, float>();
        public string Rationale = string.Empty;

        public bool IsSupportRequest =>
            RequestType == AirMissionRequestType.ProvideAirborneC2
            || RequestType == AirMissionRequestType.ProvideAerialRefueling;

        public bool IsTerminal =>
            State == AirMissionRequestState.Fulfilled
            || State == AirMissionRequestState.Purged;
    }
}
