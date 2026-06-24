using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models.Ground;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models
{
    public static class SupplyStrategicValueService
    {
        private const int MaxHubDistributionDistance = 4;
        private const float HubSupplyValueWeight = 0.25f;
        private const float HubDependentDivisionWeight = 1.5f;
        private const float RailCutHubSupplyWeight = 0.2f;
        private const float DistributionCutBaseWeight = 2f;
        private const float DistributionCutConsumptionWeight = 0.5f;

        private static readonly int[] HubSupplyByEffectiveLevel =
        {
            0, 3, 6, 9, 12, 15, 18, 21, 24, 27, 30
        };

        public static Dictionary<Vector3Int, float> BuildSupplyStrategicValueLookup(GameManager gameManager)
        {
            var lookup = new Dictionary<Vector3Int, float>();
            if (gameManager == null)
                return lookup;

            var context = BuildContext(gameManager);
            foreach (var tileEntry in context.LandTilesById)
            {
                var tileId = tileEntry.Key;
                var alliance = tileEntry.Value.Controller;
                if (alliance == Alliance.Neutral
                    || !context.SupplyCapitalByAlliance.TryGetValue(alliance, out var capitalTileId))
                {
                    continue;
                }

                var value = GetHubOperationalValue(context, alliance, capitalTileId, tileId)
                            + GetRailCutValue(context, alliance, capitalTileId, tileId)
                            + GetDistributionCutValue(context, alliance, tileId);

                if (value > 0f)
                    lookup[tileId] = value;
            }

            return lookup;
        }

        private static float GetHubOperationalValue(
            SupplyContext context,
            Alliance alliance,
            Vector3Int capitalTileId,
            Vector3Int tileId)
        {
            if (!context.HubsByTileId.TryGetValue(tileId, out var hub) || hub.FunctionalLevel <= 0)
                return 0f;

            var railBottleneck = FindBestRailBottleneck(
                context,
                capitalTileId,
                tileId,
                alliance);

            if (railBottleneck <= 0)
                return 0f;

            var effectiveLevel = Mathf.Min(hub.FunctionalLevel, railBottleneck);
            var hubSupply = GetHubSupply(effectiveLevel);
            if (hubSupply <= 0f)
                return 0f;

            var dependentDivisions = context.DivisionsByHubId.TryGetValue(hub.BuildingId, out var divisions)
                ? divisions.Count
                : 0;

            return hubSupply * HubSupplyValueWeight
                   + dependentDivisions * HubDependentDivisionWeight;
        }

        private static float GetRailCutValue(
            SupplyContext context,
            Alliance alliance,
            Vector3Int capitalTileId,
            Vector3Int blockedTileId)
        {
            var value = 0f;
            foreach (var hubOption in context.HubOptions.Where(option => option.Alliance == alliance))
            {
                if (hubOption.Hub.TileId == blockedTileId)
                    continue;

                var connectedWithoutBlock = FindBestRailBottleneck(
                    context,
                    capitalTileId,
                    hubOption.Hub.TileId,
                    alliance);

                if (connectedWithoutBlock <= 0)
                    continue;

                var connectedWithBlock = FindBestRailBottleneck(
                    context,
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
            SupplyContext context,
            Alliance alliance,
            Vector3Int blockedTileId)
        {
            var value = 0f;
            foreach (var assignment in context.DivisionAssignments.Where(candidate => candidate.Alliance == alliance))
            {
                if (assignment.Division.TileId == blockedTileId)
                    continue;

                var connectedWithoutBlock = FindControlledLandDistance(
                    context,
                    assignment.Hub.TileId,
                    assignment.Division.TileId,
                    alliance,
                    MaxHubDistributionDistance);

                if (connectedWithoutBlock < 0)
                    continue;

                var connectedWithBlock = FindControlledLandDistance(
                    context,
                    assignment.Hub.TileId,
                    assignment.Division.TileId,
                    alliance,
                    MaxHubDistributionDistance,
                    blockedTileId);

                if (connectedWithBlock >= 0)
                    continue;

                value += DistributionCutBaseWeight
                         + assignment.Division.SupplyConsumption * DistributionCutConsumptionWeight;
            }

            return value;
        }

        private static SupplyContext BuildContext(GameManager gameManager)
        {
            var landTilesById = BuildLandTileLookup(gameManager);
            var neighborsByTileId = BuildNeighborLookup(gameManager);
            var supplyCapitalByAlliance = BuildSupplyCapitalLookup(gameManager, landTilesById);
            var hubOptions = BuildHubOptions(gameManager, landTilesById, neighborsByTileId, supplyCapitalByAlliance);
            var hubsByTileId = hubOptions
                .GroupBy(option => option.Hub.TileId)
                .ToDictionary(group => group.Key, group => group.First().Hub);

            var divisionAssignments = new List<DivisionHubAssignment>();
            var divisionsByHubId = new Dictionary<Guid, List<Division>>();

            foreach (var division in gameManager.divisionSystem?.Divisions ?? new List<Division>())
            {
                if (division == null || division.SupplyConsumption <= 0f)
                    continue;

                if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var divisionAlliance))
                    continue;

                var bestAssignment = ChooseBestHubForDivision(
                    gameManager,
                    division,
                    divisionAlliance,
                    hubOptions,
                    neighborsByTileId,
                    landTilesById);

                if (bestAssignment == null)
                    continue;

                divisionAssignments.Add(bestAssignment);

                if (!divisionsByHubId.TryGetValue(bestAssignment.Hub.BuildingId, out var divisions))
                {
                    divisions = new List<Division>();
                    divisionsByHubId[bestAssignment.Hub.BuildingId] = divisions;
                }

                divisions.Add(division);
            }

            return new SupplyContext(
                gameManager,
                landTilesById,
                neighborsByTileId,
                supplyCapitalByAlliance,
                hubOptions,
                hubsByTileId,
                divisionAssignments,
                divisionsByHubId);
        }

        private static DivisionHubAssignment ChooseBestHubForDivision(
            GameManager gameManager,
            Division division,
            Alliance alliance,
            IEnumerable<HubSupplyOption> hubOptions,
            IReadOnlyDictionary<Vector3Int, List<Vector3Int>> neighborsByTileId,
            IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById)
        {
            DivisionHubAssignment bestAssignment = null;
            foreach (var hubOption in hubOptions.Where(option => option.Alliance == alliance))
            {
                var distance = FindControlledLandDistance(
                    landTilesById,
                    neighborsByTileId,
                    hubOption.Hub.TileId,
                    division.TileId,
                    alliance,
                    MaxHubDistributionDistance);

                if (distance < 0)
                    continue;

                var falloff = GetDistributionFalloff(distance);
                var supplyAfterFalloff = hubOption.HubSupply * falloff;
                if (supplyAfterFalloff <= 0f)
                    continue;

                var assignment = new DivisionHubAssignment(
                    division,
                    hubOption.Hub,
                    alliance,
                    supplyAfterFalloff,
                    distance);

                if (IsBetterAssignment(assignment, bestAssignment))
                    bestAssignment = assignment;
            }

            return bestAssignment;
        }

        private static bool IsBetterAssignment(DivisionHubAssignment candidate, DivisionHubAssignment current)
        {
            if (current == null)
                return true;

            if (!Mathf.Approximately(candidate.AvailableSupply, current.AvailableSupply))
                return candidate.AvailableSupply > current.AvailableSupply;

            if (candidate.Distance != current.Distance)
                return candidate.Distance < current.Distance;

            return candidate.Hub.BuildingId.CompareTo(current.Hub.BuildingId) < 0;
        }

        private static List<HubSupplyOption> BuildHubOptions(
            GameManager gameManager,
            IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById,
            IReadOnlyDictionary<Vector3Int, List<Vector3Int>> neighborsByTileId,
            IReadOnlyDictionary<Alliance, Vector3Int> supplyCapitalByAlliance)
        {
            var hubOptions = new List<HubSupplyOption>();
            foreach (var hub in (gameManager.buildingSystem?.Buildings ?? new List<Building>()).OfType<SupplyHub>())
            {
                if (hub.FunctionalLevel <= 0)
                    continue;

                if (!landTilesById.TryGetValue(hub.TileId, out var hubTileData))
                    continue;

                var alliance = hubTileData.Controller;
                if (alliance == Alliance.Neutral)
                    continue;

                if (!supplyCapitalByAlliance.TryGetValue(alliance, out var capitalTileId))
                    continue;

                var railBottleneck = FindBestRailBottleneck(
                    gameManager,
                    landTilesById,
                    neighborsByTileId,
                    capitalTileId,
                    hub.TileId,
                    alliance);

                if (railBottleneck <= 0)
                    continue;

                var effectiveLevel = Mathf.Min(hub.FunctionalLevel, railBottleneck);
                var hubSupply = GetHubSupply(effectiveLevel);
                if (hubSupply <= 0f)
                    continue;

                hubOptions.Add(new HubSupplyOption(hub, alliance, hubSupply));
            }

            return hubOptions;
        }

        private static Dictionary<Alliance, Vector3Int> BuildSupplyCapitalLookup(
            GameManager gameManager,
            IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById)
        {
            var result = new Dictionary<Alliance, Vector3Int>();
            foreach (var capital in gameManager.SupplyCapitals ?? new List<SupplyCapitalStartingCondition>())
            {
                if (capital == null || capital.Alliance == Alliance.Neutral)
                    continue;

                if (!landTilesById.TryGetValue(capital.TileId, out var landTileData))
                    continue;

                if (landTileData.Controller != capital.Alliance)
                    continue;

                result[capital.Alliance] = capital.TileId;
            }

            return result;
        }

        private static Dictionary<Vector3Int, LandTileData> BuildLandTileLookup(GameManager gameManager)
        {
            return (gameManager.Tiles ?? new List<TileData>())
                .OfType<LandTileData>()
                .GroupBy(tileData => tileData.TileId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        private static Dictionary<Vector3Int, List<Vector3Int>> BuildNeighborLookup(GameManager gameManager)
        {
            return (gameManager.CampaignTiles ?? new List<Tile>())
                .GroupBy(tile => tile.Coordinates)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().NeighborTileIds ?? new List<Vector3Int>());
        }

        private static int FindBestRailBottleneck(
            SupplyContext context,
            Vector3Int startTileId,
            Vector3Int targetTileId,
            Alliance alliance,
            Vector3Int? blockedTileId = null)
        {
            return FindBestRailBottleneck(
                context.GameManager,
                context.LandTilesById,
                context.NeighborsByTileId,
                startTileId,
                targetTileId,
                alliance,
                blockedTileId);
        }

        private static int FindBestRailBottleneck(
            GameManager gameManager,
            IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById,
            IReadOnlyDictionary<Vector3Int, List<Vector3Int>> neighborsByTileId,
            Vector3Int startTileId,
            Vector3Int targetTileId,
            Alliance alliance,
            Vector3Int? blockedTileId = null)
        {
            if (blockedTileId.HasValue && startTileId == blockedTileId.Value)
                return 0;

            if (!TryGetRailLevel(gameManager, landTilesById, startTileId, alliance, blockedTileId, out var startRailLevel))
                return 0;

            if (startTileId == targetTileId)
                return startRailLevel;

            var bestBottleneckByTileId = new Dictionary<Vector3Int, int>
            {
                [startTileId] = startRailLevel
            };
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(startTileId);

            while (queue.Count > 0)
            {
                var currentTileId = queue.Dequeue();
                var currentBottleneck = bestBottleneckByTileId[currentTileId];

                if (!neighborsByTileId.TryGetValue(currentTileId, out var neighbors))
                    continue;

                foreach (var neighborTileId in neighbors)
                {
                    if (blockedTileId.HasValue && neighborTileId == blockedTileId.Value)
                        continue;

                    if (!TryGetRailLevel(
                            gameManager,
                            landTilesById,
                            neighborTileId,
                            alliance,
                            blockedTileId,
                            out var neighborRailLevel))
                    {
                        continue;
                    }

                    var candidateBottleneck = Mathf.Min(currentBottleneck, neighborRailLevel);
                    if (bestBottleneckByTileId.TryGetValue(neighborTileId, out var knownBottleneck)
                        && knownBottleneck >= candidateBottleneck)
                    {
                        continue;
                    }

                    bestBottleneckByTileId[neighborTileId] = candidateBottleneck;
                    queue.Enqueue(neighborTileId);
                }
            }

            return bestBottleneckByTileId.TryGetValue(targetTileId, out var bestBottleneck)
                ? bestBottleneck
                : 0;
        }

        private static bool TryGetRailLevel(
            GameManager gameManager,
            IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById,
            Vector3Int tileId,
            Alliance alliance,
            Vector3Int? blockedTileId,
            out int railLevel)
        {
            railLevel = 0;
            if (blockedTileId.HasValue && tileId == blockedTileId.Value)
                return false;

            if (!landTilesById.TryGetValue(tileId, out var landTileData) || landTileData.Controller != alliance)
                return false;

            railLevel = gameManager.buildingSystem
                ?.GetBuildingsOnTile(tileId, BuildingType.Railroad)
                .Select(building => building.FunctionalLevel)
                .DefaultIfEmpty(0)
                .Max() ?? 0;

            return railLevel > 0;
        }

        private static bool TryGetRailLevel(
            SupplyContext context,
            Vector3Int tileId,
            Alliance alliance,
            Vector3Int? blockedTileId,
            out int railLevel)
        {
            return TryGetRailLevel(context.GameManager, context.LandTilesById, tileId, alliance, blockedTileId, out railLevel);
        }

        private static int FindControlledLandDistance(
            SupplyContext context,
            Vector3Int startTileId,
            Vector3Int targetTileId,
            Alliance alliance,
            int maxDistance,
            Vector3Int? blockedTileId = null)
        {
            return FindControlledLandDistance(
                context.LandTilesById,
                context.NeighborsByTileId,
                startTileId,
                targetTileId,
                alliance,
                maxDistance,
                blockedTileId);
        }

        private static int FindControlledLandDistance(
            IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById,
            IReadOnlyDictionary<Vector3Int, List<Vector3Int>> neighborsByTileId,
            Vector3Int startTileId,
            Vector3Int targetTileId,
            Alliance alliance,
            int maxDistance,
            Vector3Int? blockedTileId = null)
        {
            if (blockedTileId.HasValue
                && (startTileId == blockedTileId.Value || targetTileId == blockedTileId.Value))
            {
                return -1;
            }

            if (!IsControlledLandTile(landTilesById, startTileId, alliance, blockedTileId)
                || !IsControlledLandTile(landTilesById, targetTileId, alliance, blockedTileId))
            {
                return -1;
            }

            if (startTileId == targetTileId)
                return 0;

            var distanceByTileId = new Dictionary<Vector3Int, int>
            {
                [startTileId] = 0
            };
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(startTileId);

            while (queue.Count > 0)
            {
                var currentTileId = queue.Dequeue();
                var currentDistance = distanceByTileId[currentTileId];
                if (currentDistance >= maxDistance)
                    continue;

                if (!neighborsByTileId.TryGetValue(currentTileId, out var neighbors))
                    continue;

                foreach (var neighborTileId in neighbors)
                {
                    if (distanceByTileId.ContainsKey(neighborTileId)
                        || !IsControlledLandTile(landTilesById, neighborTileId, alliance, blockedTileId))
                    {
                        continue;
                    }

                    var nextDistance = currentDistance + 1;
                    if (neighborTileId == targetTileId)
                        return nextDistance;

                    distanceByTileId[neighborTileId] = nextDistance;
                    queue.Enqueue(neighborTileId);
                }
            }

            return -1;
        }

        private static bool IsControlledLandTile(
            SupplyContext context,
            Vector3Int tileId,
            Alliance alliance,
            Vector3Int? blockedTileId)
        {
            return IsControlledLandTile(context.LandTilesById, tileId, alliance, blockedTileId);
        }

        private static bool IsControlledLandTile(
            IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById,
            Vector3Int tileId,
            Alliance alliance,
            Vector3Int? blockedTileId)
        {
            if (blockedTileId.HasValue && tileId == blockedTileId.Value)
                return false;

            return landTilesById.TryGetValue(tileId, out var landTileData)
                   && landTileData.Controller == alliance;
        }

        private static float GetHubSupply(int effectiveHubLevel)
        {
            var level = Mathf.Clamp(effectiveHubLevel, 0, HubSupplyByEffectiveLevel.Length - 1);
            return HubSupplyByEffectiveLevel[level];
        }

        private static float GetDistributionFalloff(int distance)
        {
            if (distance <= 2)
                return 1f;

            if (distance == 3)
                return 0.75f;

            if (distance == 4)
                return 0.5f;

            return 0f;
        }

        private sealed class SupplyContext
        {
            public readonly GameManager GameManager;
            public readonly IReadOnlyDictionary<Vector3Int, LandTileData> LandTilesById;
            public readonly IReadOnlyDictionary<Vector3Int, List<Vector3Int>> NeighborsByTileId;
            public readonly IReadOnlyDictionary<Alliance, Vector3Int> SupplyCapitalByAlliance;
            public readonly IReadOnlyList<HubSupplyOption> HubOptions;
            public readonly IReadOnlyDictionary<Vector3Int, SupplyHub> HubsByTileId;
            public readonly IReadOnlyList<DivisionHubAssignment> DivisionAssignments;
            public readonly IReadOnlyDictionary<Guid, List<Division>> DivisionsByHubId;

            public SupplyContext(
                GameManager gameManager,
                IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById,
                IReadOnlyDictionary<Vector3Int, List<Vector3Int>> neighborsByTileId,
                IReadOnlyDictionary<Alliance, Vector3Int> supplyCapitalByAlliance,
                IReadOnlyList<HubSupplyOption> hubOptions,
                IReadOnlyDictionary<Vector3Int, SupplyHub> hubsByTileId,
                IReadOnlyList<DivisionHubAssignment> divisionAssignments,
                IReadOnlyDictionary<Guid, List<Division>> divisionsByHubId)
            {
                GameManager = gameManager;
                LandTilesById = landTilesById;
                NeighborsByTileId = neighborsByTileId;
                SupplyCapitalByAlliance = supplyCapitalByAlliance;
                HubOptions = hubOptions;
                HubsByTileId = hubsByTileId;
                DivisionAssignments = divisionAssignments;
                DivisionsByHubId = divisionsByHubId;
            }
        }

        private sealed class HubSupplyOption
        {
            public readonly SupplyHub Hub;
            public readonly Alliance Alliance;
            public readonly float HubSupply;

            public HubSupplyOption(SupplyHub hub, Alliance alliance, float hubSupply)
            {
                Hub = hub;
                Alliance = alliance;
                HubSupply = hubSupply;
            }
        }

        private sealed class DivisionHubAssignment
        {
            public readonly Division Division;
            public readonly SupplyHub Hub;
            public readonly Alliance Alliance;
            public readonly float AvailableSupply;
            public readonly int Distance;

            public DivisionHubAssignment(
                Division division,
                SupplyHub hub,
                Alliance alliance,
                float availableSupply,
                int distance)
            {
                Division = division;
                Hub = hub;
                Alliance = alliance;
                AvailableSupply = availableSupply;
                Distance = distance;
            }
        }
    }
}
