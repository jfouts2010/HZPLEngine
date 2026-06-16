using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class SupplyHub : Building
    {
        public override BuildingType Type
        {
            get { return BuildingType.SupplyHub; }
        }

        public SupplyHub()
        {
        }

        public SupplyHub(BuildingStartingCondition startingCondition) : base(startingCondition)
        {
        }
    }
}
