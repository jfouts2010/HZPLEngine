using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class BuildingStartingCondition
    {
        public Guid BuildingId = Guid.NewGuid();
        public Vector3Int TileId;
        public BuildingType Type;
        public BuildingLevel Level = new BuildingLevel();
    }
}
