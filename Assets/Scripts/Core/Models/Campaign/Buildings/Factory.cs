using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class Factory : Building
    {
        public override BuildingType Type
        {
            get { return BuildingType.Factory; }
        }

        public Factory()
        {
        }

        public Factory(BuildingStartingCondition startingCondition) : base(startingCondition)
        {
        }
    }
}
