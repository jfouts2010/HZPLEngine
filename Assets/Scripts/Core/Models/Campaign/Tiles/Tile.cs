using System;
using System.Collections.Generic;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class Tile
    {
        public Vector3Int Coordinates;
        [NonSerialized]
        public List<Vector3Int> NeighborTileIds = new List<Vector3Int>();
        public List<Vector3Int> RiverNeighborTileIds = new List<Vector3Int>();
        public TileSurface Surface = TileSurface.Land;
        public TileTerrain Terrain = TileTerrain.Plains;
        public Urbanization Urbanization = Urbanization.Rural;
        public ForestCover ForestCover = ForestCover.None;
    }
}
