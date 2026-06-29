using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class SupportDemandSample
    {
        public DateTime RecordedAt;
        public AirMissionRequestType SupportType;
        public AirMissionArea MissionArea = new AirMissionArea();
        public int RequestedSlots;
    }
}
