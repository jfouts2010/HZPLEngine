using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class Railroad : Building
    {
        public override BuildingType Type
        {
            get { return BuildingType.Railroad; }
        }

        public Railroad()
        {
        }

        public Railroad(BuildingStartingCondition startingCondition) : base(startingCondition)
        {
        }
    }
}
