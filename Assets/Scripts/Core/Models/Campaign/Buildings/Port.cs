using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class Port : Building
    {
        public override BuildingType Type
        {
            get { return BuildingType.Port; }
        }

        public Port()
        {
        }

        public Port(BuildingStartingCondition startingCondition) : base(startingCondition)
        {
        }
    }
}
