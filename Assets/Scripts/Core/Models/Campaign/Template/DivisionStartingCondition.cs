using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class DivisionStartingCondition
    {
        public Guid DivisionId = Guid.NewGuid();
        public Guid DivisionTemplateId;
        public Guid CountryId;
        public Guid TileId;
        public string Name = string.Empty;
    }
}
