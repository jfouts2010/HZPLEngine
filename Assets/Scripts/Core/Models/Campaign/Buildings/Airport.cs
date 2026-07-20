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

        public int MaximumRunwayIntegrity => Level?.BuildLevel ?? 0;
        public int RunwayIntegrity => Level?.FunctionalLevel ?? 0;

        public Airport()
        {
        }

        public Airport(BuildingStartingCondition startingCondition) : base(startingCondition)
        {
            TargetToughness = 3;
        }
    }
}
