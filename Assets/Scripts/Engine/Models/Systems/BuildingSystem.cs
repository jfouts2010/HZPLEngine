using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class BuildingSystem
    {
        [SerializeReference]
        public List<Building> Buildings = new List<Building>();

        private Dictionary<Vector3Int, List<Building>> buildingsByTileId;
        private Dictionary<Guid, Building> buildingsById;

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

        public bool TryGetBuilding(Guid buildingId, out Building building)
        {
            EnsureIndex();
            return buildingsById.TryGetValue(buildingId, out building);
        }

        public List<TBuilding> GetBuildings<TBuilding>() where TBuilding : Building
        {
            EnsureIndex();
            return Buildings
                .OfType<TBuilding>()
                .ToList();
        }

        public void RebuildIndex()
        {
            buildingsByTileId = (Buildings ?? new List<Building>())
                .GroupBy(building => building.TileId)
                .ToDictionary(group => group.Key, group => group.ToList());

            buildingsById = (Buildings ?? new List<Building>())
                .GroupBy(building => building.BuildingId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        private void EnsureIndex()
        {
            if (buildingsByTileId == null || buildingsById == null)
                RebuildIndex();
        }
    }
}
