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
        public DeadMissionPlan DeadPlan;
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

    [Serializable]
    public sealed class DeadMissionPlan
    {
        public Guid TargetSiteId;
        public List<Guid> TargetComponentIds = new List<Guid>();
        public DeadAirAccessCorridor SupportedCorridor =
            new DeadAirAccessCorridor();

        public DeadMissionPlan Clone()
        {
            return new DeadMissionPlan
            {
                TargetSiteId = TargetSiteId,
                TargetComponentIds = TargetComponentIds == null
                    ? new List<Guid>()
                    : new List<Guid>(TargetComponentIds),
                SupportedCorridor = SupportedCorridor?.Clone()
                                    ?? new DeadAirAccessCorridor()
            };
        }
    }

    [Serializable]
    public sealed class DeadAirAccessCorridor
    {
        public UnityEngine.Vector3 OriginPositionFeet;
        public UnityEngine.Vector3 DestinationPositionFeet;
        public UnityEngine.Vector3 RecoveryPositionFeet;
        public float RepresentativeAltitudeFeet;
        public Guid RepresentativeAircraftTypeDefinitionId;

        public DeadAirAccessCorridor Clone()
        {
            return new DeadAirAccessCorridor
            {
                OriginPositionFeet = OriginPositionFeet,
                DestinationPositionFeet = DestinationPositionFeet,
                RecoveryPositionFeet = RecoveryPositionFeet,
                RepresentativeAltitudeFeet = RepresentativeAltitudeFeet,
                RepresentativeAircraftTypeDefinitionId =
                    RepresentativeAircraftTypeDefinitionId
            };
        }
    }
}
