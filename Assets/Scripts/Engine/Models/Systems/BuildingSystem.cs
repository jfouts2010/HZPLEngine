using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
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
        [NonSerialized] private TileSystem tileSystem;

        public void Configure(TileSystem tiles)
        {
            tileSystem = tiles ?? throw new ArgumentNullException(nameof(tiles));
            buildingsByTileId = null;
            buildingsById = null;
        }

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

        public bool ApplyDamage(Guid buildingId, int damage)
        {
            if (damage <= 0
                || !TryGetBuilding(buildingId, out var building)
                || building.Level == null
                || building.FunctionalLevel <= 0)
                return false;

            building.Level.Damage = Math.Min(
                building.Level.BuildLevel,
                building.Level.Damage + damage);
            return true;
        }

        public void RebuildIndex()
        {
            if (tileSystem == null)
                throw new InvalidOperationException(
                    "BuildingSystem must be configured before rebuilding its indexes.");
            if (Buildings == null)
                throw new InvalidOperationException("Building collection is required.");

            var byTile = new Dictionary<Vector3Int, List<Building>>();
            var byId = new Dictionary<Guid, Building>();
            var placements = new List<(Building Building, Vector3Int TileId)>();
            for (var index = 0; index < Buildings.Count; index++)
            {
                var building = Buildings[index];
                if (building == null)
                    throw new InvalidOperationException($"Building at index {index} is null.");
                if (building.BuildingId == Guid.Empty)
                    throw new InvalidOperationException($"Building at index {index} has an empty ID.");
                if (byId.ContainsKey(building.BuildingId))
                    throw new InvalidOperationException($"Building ID {building.BuildingId} is duplicated.");
                byId.Add(building.BuildingId, building);
                if (!IsFinite(building.PositionFeet))
                {
                    throw new InvalidOperationException(
                        $"Building {building.BuildingId} has a non-finite position {building.PositionFeet}.");
                }
                if (!Mathf.Approximately(building.PositionFeet.y, 0f))
                {
                    throw new InvalidOperationException(
                        $"Building {building.BuildingId} must be at ground altitude, but Y is {building.PositionFeet.y} feet.");
                }

                var tileId = CampaignMapCoordinates.TileCoordinateFromPositionFeet(
                    building.PositionFeet);
                if (!tileSystem.TryGetLand(tileId, out _))
                {
                    throw new InvalidOperationException(
                        $"Building {building.BuildingId} at {building.PositionFeet} projects to missing or non-land tile {tileId}.");
                }

                placements.Add((building, tileId));
            }

            foreach (var placement in placements)
            {
                placement.Building.SetDerivedTileId(placement.TileId);
                if (!byTile.TryGetValue(placement.TileId, out var tileBuildings))
                {
                    tileBuildings = new List<Building>();
                    byTile[placement.TileId] = tileBuildings;
                }
                tileBuildings.Add(placement.Building);
            }

            buildingsByTileId = byTile;
            buildingsById = byId;
        }

        private void EnsureIndex()
        {
            if (buildingsByTileId == null || buildingsById == null)
                RebuildIndex();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x)
                   && !float.IsInfinity(value.x)
                   && !float.IsNaN(value.y)
                   && !float.IsInfinity(value.y)
                   && !float.IsNaN(value.z)
                   && !float.IsInfinity(value.z);
        }
    }
}
