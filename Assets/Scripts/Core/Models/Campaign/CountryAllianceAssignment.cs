using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class CountryAllianceAssignment
    {
        public Guid CountryId;
        public Alliance Alliance = Alliance.Neutral;
    }
}
