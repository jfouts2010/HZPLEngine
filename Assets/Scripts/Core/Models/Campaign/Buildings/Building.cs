using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public abstract class Building
    {
        public Guid BuildingId;
        public Vector3Int TileId;
        public BuildingLevel Level = new BuildingLevel();
        public int TargetToughness = 1;

        public abstract BuildingType Type { get; }

        public int FunctionalLevel
        {
            get { return Level.FunctionalLevel; }
        }

        protected Building()
        {
        }

        protected Building(BuildingStartingCondition startingCondition)
        {
            BuildingId = startingCondition.BuildingId;
            TileId = startingCondition.TileId;
            Level = startingCondition.Level == null
                ? new BuildingLevel()
                : new BuildingLevel(startingCondition.Level.BuildLevel, startingCondition.Level.Damage);
        }
    }
}
