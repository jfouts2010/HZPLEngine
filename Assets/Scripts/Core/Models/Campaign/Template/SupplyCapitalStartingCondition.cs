using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class SupplyCapitalStartingCondition
    {
        public Alliance Alliance;
        public Vector3Int TileId;
    }
}
