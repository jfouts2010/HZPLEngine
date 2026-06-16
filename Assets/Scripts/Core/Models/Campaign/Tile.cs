using System;
using System.Collections.Generic;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class Tile
    {
        public Guid TileId = Guid.NewGuid();
        public Vector3Int Coordinates;
        [NonSerialized]
        public List<Guid> NeighborTileIds = new List<Guid>();
        public List<Guid> RiverNeighborTileIds = new List<Guid>();
        public TileSurface Surface = TileSurface.Land;
        public TileTerrain Terrain = TileTerrain.Plains;
        public Urbanization Urbanization = Urbanization.Rural;
        public ForestCover ForestCover = ForestCover.None;
    }
}
