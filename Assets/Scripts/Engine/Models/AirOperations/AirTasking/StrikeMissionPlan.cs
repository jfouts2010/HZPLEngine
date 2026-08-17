using System;
using System.Collections.Generic;

namespace Models.Gameplay.Campaign
{
    public enum StrikePurpose
    {
        None = 0,
        OffensiveCounterAir = 1
    }

    public enum StrikeAssignment
    {
        None = 0,
        RunwayDenial = 1,
        AircraftOnGround = 2,
        AirbaseFacilities = 3
    }

    [Serializable]
    public sealed class StrikeMissionPlan
    {
        public StrikePurpose Purpose = StrikePurpose.OffensiveCounterAir;
        public Guid TargetAirportBuildingId;
        public int DesiredRunwayDamagePerChannel = 1;
        public List<Guid> AuthorizedFacilityTargetIds = new List<Guid>();

        public StrikeMissionPlan Clone()
        {
            return new StrikeMissionPlan
            {
                Purpose = Purpose,
                TargetAirportBuildingId = TargetAirportBuildingId,
                DesiredRunwayDamagePerChannel = DesiredRunwayDamagePerChannel,
                AuthorizedFacilityTargetIds = AuthorizedFacilityTargetIds == null
                    ? new List<Guid>()
                    : new List<Guid>(AuthorizedFacilityTargetIds)
            };
        }
    }
}
