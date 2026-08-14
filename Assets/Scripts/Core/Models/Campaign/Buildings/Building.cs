using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public abstract class Building
    {
        public Guid BuildingId;
        public string ThirdPartyId = string.Empty;
        [SerializeField]
        private Vector3 positionFeet;
        public BuildingLevel Level = new BuildingLevel();
        public int TargetToughness = 1;

        [NonSerialized]
        private Vector3Int tileId;

        public abstract BuildingType Type { get; }

        public Vector3 PositionFeet => positionFeet;
        public Vector3Int TileId => tileId;

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
            ThirdPartyId = startingCondition.ThirdPartyId ?? string.Empty;
            positionFeet = startingCondition.PositionFeet;
            Level = startingCondition.Level == null
                ? new BuildingLevel()
                : new BuildingLevel(startingCondition.Level.BuildLevel, startingCondition.Level.Damage);
        }

        internal void SetDerivedTileId(Vector3Int value)
        {
            tileId = value;
        }
    }
}
