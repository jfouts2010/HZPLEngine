using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class Fort : Building
    {
        public override BuildingType Type
        {
            get { return BuildingType.Fort; }
        }

        public Fort()
        {
        }

        public Fort(BuildingStartingCondition startingCondition) : base(startingCondition)
        {
        }
    }
}
