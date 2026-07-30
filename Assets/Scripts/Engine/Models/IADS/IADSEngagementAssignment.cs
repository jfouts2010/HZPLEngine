using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class IADSEngagementAssignment
    {
        public Guid AssignmentId = Guid.NewGuid();
        public Guid SiteId;
        public Guid TrackId;
        public Guid TargetFlightId;
        public Guid FireControlRadarComponentId;
        public DateTime AssignedAt;
    }
}
