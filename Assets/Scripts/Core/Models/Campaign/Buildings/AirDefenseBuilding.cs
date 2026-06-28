using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class AirDefenseBuilding : Building
    {
        public Guid CountryId;

        public override BuildingType Type
        {
            get { return BuildingType.AirDefense; }
        }

        public AirDefenseBuilding()
        {
        }

        public AirDefenseBuilding(BuildingStartingCondition startingCondition) : base(startingCondition)
        {
            CountryId = startingCondition.CountryId;
        }
    }
}
