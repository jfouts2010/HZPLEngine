using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class MobileSamSiteStartingCondition
    {
        public Guid MobileSamSiteId = Guid.NewGuid();
        public Guid SamSiteTemplateId;
        public Guid HostDivisionId;
        public Alliance Alliance;
    }
}
