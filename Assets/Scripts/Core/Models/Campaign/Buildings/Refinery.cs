using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class Refinery : Building
    {
        public override BuildingType Type
        {
            get { return BuildingType.Refinery; }
        }

        public Refinery()
        {
        }

        public Refinery(BuildingStartingCondition startingCondition) : base(startingCondition)
        {
        }
    }
}
