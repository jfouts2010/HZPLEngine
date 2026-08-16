using System;
using System.Collections.Generic;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
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
        public Vector3 OriginPositionFeet;
        public Vector3 DestinationPositionFeet;
        public Vector3 RecoveryPositionFeet;
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
