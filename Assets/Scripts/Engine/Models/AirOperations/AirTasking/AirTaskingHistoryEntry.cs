using System;
using System.Collections.Generic;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AirTaskingHistoryEntry
    {
        public DateTime RecordedAt;
        public Guid MissionRequestId;
        public AirMissionRequestType RequestType;
        public AirMissionRequestState RequestState;
        public AirMissionRequest RequestSnapshot;
        public List<AirPackage> PackageSnapshots = new List<AirPackage>();
        public string Summary = string.Empty;
    }
}
