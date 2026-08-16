using System;
using System.Collections.Generic;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AirTaskingDiagnostic
    {
        public DateTime RecordedAt;
        public Guid PlanId;
        public Guid PackageId;
        public string Code = string.Empty;
        public string Message = string.Empty;
        public Dictionary<string, float> Values = new Dictionary<string, float>();
    }
}
