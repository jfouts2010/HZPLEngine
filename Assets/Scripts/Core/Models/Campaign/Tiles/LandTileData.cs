using System;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class LandTileData : TileData
    {
        public Alliance Controller = Alliance.Neutral;
        public BuildingLevel Infrastructure = new BuildingLevel();
        public int InfrastructureTargetToughness = 2;
    }
}
