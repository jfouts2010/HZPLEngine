using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class BuildingCollection
    {
        [SerializeReference]
        public List<Building> Buildings = new List<Building>();

        private Dictionary<Vector3Int, List<Building>> buildingsByTileId;

        public List<Building> GetBuildingsOnTile(Vector3Int tileId)
        {
            EnsureIndex();
            return buildingsByTileId.TryGetValue(tileId, out var buildings)
                ? buildings
                : new List<Building>();
        }

        public List<Building> GetBuildingsOnTile(Vector3Int tileId, BuildingType type)
        {
            return GetBuildingsOnTile(tileId)
                .Where(building => building.Type == type)
                .ToList();
        }

        public void RebuildIndex()
        {
            buildingsByTileId = (Buildings ?? new List<Building>())
                .Where(building => building != null)
                .GroupBy(building => building.TileId)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        private void EnsureIndex()
        {
            if (buildingsByTileId == null)
                RebuildIndex();
        }
    }
}
