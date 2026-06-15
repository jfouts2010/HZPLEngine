using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class Airport : Building
    {
        public override BuildingType Type
        {
            get { return BuildingType.Airport; }
        }

        public Airport()
        {
        }

        public Airport(BuildingStartingCondition startingCondition) : base(startingCondition)
        {
        }
    }
}
