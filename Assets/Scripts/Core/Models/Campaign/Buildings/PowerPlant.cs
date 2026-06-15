using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class PowerPlant : Building
    {
        public override BuildingType Type
        {
            get { return BuildingType.PowerPlant; }
        }

        public PowerPlant()
        {
        }

        public PowerPlant(BuildingStartingCondition startingCondition) : base(startingCondition)
        {
        }
    }
}
