using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models
{
    public static class SupplyStrategicValueService
    {
        private const float HubSupplyValueWeight = 0.25f;
        private const float HubDependentDivisionWeight = 1.5f;
        private const float RailCutHubSupplyWeight = 0.2f;
        private const float DistributionCutBaseWeight = 2f;
        private const float DistributionCutConsumptionWeight = 0.5f;

        public static Dictionary<Vector3Int, float> BuildSupplyStrategicValueLookup(GameManager gameManager)
        {
            var lookup = new Dictionary<Vector3Int, float>();
            if (gameManager == null)
                return lookup;

            var analysis = SupplyNetworkAnalysis.Build(gameManager);
            foreach (var tileEntry in analysis.LandTilesById)
            {
                var tileId = tileEntry.Key;
                var alliance = tileEntry.Value.Controller;
                if (alliance == Alliance.Neutral
                    || !analysis.SupplyCapitalByAlliance.TryGetValue(alliance, out var capitalTileId))
                {
                    continue;
                }

                var value = GetHubOperationalValue(analysis, alliance, capitalTileId, tileId)
                            + GetRailCutValue(analysis, alliance, capitalTileId, tileId)
                            + GetDistributionCutValue(analysis, alliance, tileId);

                if (value > 0f)
                    lookup[tileId] = value;
            }

            return lookup;
        }

        private static float GetHubOperationalValue(
            SupplyNetworkAnalysis analysis,
            Alliance alliance,
            Vector3Int capitalTileId,
            Vector3Int tileId)
        {
            if (!analysis.HubsByTileId.TryGetValue(tileId, out var hub) || hub.FunctionalLevel <= 0)
                return 0f;

            var railBottleneck = analysis.FindBestRailBottleneck(capitalTileId, tileId, alliance);
            if (railBottleneck <= 0)
                return 0f;

            var effectiveLevel = Mathf.Min(hub.FunctionalLevel, railBottleneck);
            var hubSupply = SupplyNetworkAnalysis.GetHubSupply(effectiveLevel);
            if (hubSupply <= 0f)
                return 0f;

            var dependentDivisions = analysis.DivisionsByHubId.TryGetValue(hub.BuildingId, out var divisions)
                ? divisions.Count
                : 0;

            return hubSupply * HubSupplyValueWeight
                   + dependentDivisions * HubDependentDivisionWeight;
        }

        private static float GetRailCutValue(
            SupplyNetworkAnalysis analysis,
            Alliance alliance,
            Vector3Int capitalTileId,
            Vector3Int blockedTileId)
        {
            var value = 0f;
            foreach (var hubOption in analysis.HubOptions.Where(option => option.Alliance == alliance))
            {
                if (hubOption.Hub.TileId == blockedTileId)
                    continue;

                var connectedWithoutBlock = analysis.FindBestRailBottleneck(
                    capitalTileId,
                    hubOption.Hub.TileId,
                    alliance);

                if (connectedWithoutBlock <= 0)
                    continue;

                var connectedWithBlock = analysis.FindBestRailBottleneck(
                    capitalTileId,
                    hubOption.Hub.TileId,
                    alliance,
                    blockedTileId);

                if (connectedWithBlock > 0)
                    continue;

                value += hubOption.HubSupply * RailCutHubSupplyWeight;
            }

            return value;
        }

        private static float GetDistributionCutValue(
            SupplyNetworkAnalysis analysis,
            Alliance alliance,
            Vector3Int blockedTileId)
        {
            var value = 0f;
            foreach (var assignment in analysis.DivisionAssignments.Where(candidate => candidate.Alliance == alliance))
            {
                if (assignment.Division.TileId == blockedTileId)
                    continue;

                var connectedWithoutBlock = analysis.FindControlledLandDistance(
                    assignment.Hub.TileId,
                    assignment.Division.TileId,
                    alliance);

                if (connectedWithoutBlock < 0)
                    continue;

                var connectedWithBlock = analysis.FindControlledLandDistance(
                    assignment.Hub.TileId,
                    assignment.Division.TileId,
                    alliance,
                    SupplyNetworkAnalysis.MaxHubDistributionDistance,
                    blockedTileId);

                if (connectedWithBlock >= 0)
                    continue;

                value += DistributionCutBaseWeight
                         + assignment.Division.SupplyConsumption * DistributionCutConsumptionWeight;
            }

            return value;
        }
    }
}
