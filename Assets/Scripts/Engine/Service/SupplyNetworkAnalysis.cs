using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models.Ground;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models
{
    public sealed class SupplyNetworkAnalysis
    {
        public const int MaxHubDistributionDistance = 4;

        private static readonly int[] HubSupplyByEffectiveLevel =
        {
            0, 3, 6, 9, 12, 15, 18, 21, 24, 27, 30
        };

        private readonly GameManager gameManager;

        private SupplyNetworkAnalysis(
            GameManager gameManager,
            IReadOnlyDictionary<Vector3Int, RuntimeLandTile> landTilesById,
            IReadOnlyDictionary<Vector3Int, IReadOnlyList<Vector3Int>> neighborsByTileId,
            IReadOnlyDictionary<Alliance, Vector3Int> supplyCapitalByAlliance,
            IReadOnlyList<HubSupplyOption> hubOptions,
            IReadOnlyDictionary<Vector3Int, SupplyHub> hubsByTileId,
            IReadOnlyList<DivisionSupplyAssignment> divisionAssignments,
            IReadOnlyDictionary<Guid, List<Division>> divisionsByHubId)
        {
            this.gameManager = gameManager;
            LandTilesById = landTilesById;
            NeighborsByTileId = neighborsByTileId;
            SupplyCapitalByAlliance = supplyCapitalByAlliance;
            HubOptions = hubOptions;
            HubsByTileId = hubsByTileId;
            DivisionAssignments = divisionAssignments;
            DivisionsByHubId = divisionsByHubId;
        }

        public IReadOnlyDictionary<Vector3Int, RuntimeLandTile> LandTilesById { get; }
        public IReadOnlyDictionary<Vector3Int, IReadOnlyList<Vector3Int>> NeighborsByTileId { get; }
        public IReadOnlyDictionary<Alliance, Vector3Int> SupplyCapitalByAlliance { get; }
        public IReadOnlyList<HubSupplyOption> HubOptions { get; }
        public IReadOnlyDictionary<Vector3Int, SupplyHub> HubsByTileId { get; }
        public IReadOnlyList<DivisionSupplyAssignment> DivisionAssignments { get; }
        public IReadOnlyDictionary<Guid, List<Division>> DivisionsByHubId { get; }

        public static SupplyNetworkAnalysis Build(GameManager gameManager)
        {
            var landTilesById = gameManager.tileSystem.LandTiles
                .ToDictionary(tile => tile.TileId);
            var neighborsByTileId = gameManager.tileSystem.Tiles
                .ToDictionary(
                    tile => tile.TileId,
                    tile => tile.NeighborTileIds);
            var supplyCapitalByAlliance = BuildSupplyCapitalLookup(gameManager, landTilesById);
            var analysis = new SupplyNetworkAnalysis(
                gameManager,
                landTilesById,
                neighborsByTileId,
                supplyCapitalByAlliance,
                new List<HubSupplyOption>(),
                new Dictionary<Vector3Int, SupplyHub>(),
                new List<DivisionSupplyAssignment>(),
                new Dictionary<Guid, List<Division>>());

            var hubOptions = analysis.BuildHubOptions();
            var hubsByTileId = hubOptions
                .GroupBy(option => option.Hub.TileId)
                .ToDictionary(group => group.Key, group => group.First().Hub);
            var divisionAssignments = analysis.BuildDivisionAssignments(hubOptions, out var divisionsByHubId);

            return new SupplyNetworkAnalysis(
                gameManager,
                landTilesById,
                neighborsByTileId,
                supplyCapitalByAlliance,
                hubOptions,
                hubsByTileId,
                divisionAssignments,
                divisionsByHubId);
        }

        public int FindBestRailBottleneck(
            Vector3Int startTileId,
            Vector3Int targetTileId,
            Alliance alliance,
            Vector3Int? blockedTileId = null)
        {
            if (blockedTileId.HasValue && startTileId == blockedTileId.Value)
                return 0;

            if (!TryGetRailLevel(startTileId, alliance, blockedTileId, out var startRailLevel))
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

                if (!NeighborsByTileId.TryGetValue(currentTileId, out var neighbors))
                    continue;

                foreach (var neighborTileId in neighbors)
                {
                    if (blockedTileId.HasValue && neighborTileId == blockedTileId.Value)
                        continue;

                    if (!TryGetRailLevel(neighborTileId, alliance, blockedTileId, out var neighborRailLevel))
                        continue;

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

        public int FindControlledLandDistance(
            Vector3Int startTileId,
            Vector3Int targetTileId,
            Alliance alliance,
            int maxDistance = MaxHubDistributionDistance,
            Vector3Int? blockedTileId = null)
        {
            if (blockedTileId.HasValue
                && (startTileId == blockedTileId.Value || targetTileId == blockedTileId.Value))
            {
                return -1;
            }

            if (!IsControlledLandTile(startTileId, alliance, blockedTileId)
                || !IsControlledLandTile(targetTileId, alliance, blockedTileId))
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

                if (!NeighborsByTileId.TryGetValue(currentTileId, out var neighbors))
                    continue;

                foreach (var neighborTileId in neighbors)
                {
                    if (distanceByTileId.ContainsKey(neighborTileId)
                        || !IsControlledLandTile(neighborTileId, alliance, blockedTileId))
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

        public static float GetHubSupply(int effectiveHubLevel)
        {
            var level = Mathf.Clamp(effectiveHubLevel, 0, HubSupplyByEffectiveLevel.Length - 1);
            return HubSupplyByEffectiveLevel[level];
        }

        public static float GetDistributionFalloff(int distance)
        {
            if (distance <= 2)
                return 1f;

            if (distance == 3)
                return 0.75f;

            if (distance == 4)
                return 0.5f;

            return 0f;
        }

        private static Dictionary<Alliance, Vector3Int> BuildSupplyCapitalLookup(
            GameManager gameManager,
            IReadOnlyDictionary<Vector3Int, RuntimeLandTile> landTilesById)
        {
            var result = new Dictionary<Alliance, Vector3Int>();
            foreach (var capital in gameManager.SupplyCapitals)
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

        private List<HubSupplyOption> BuildHubOptions()
        {
            var hubOptions = new List<HubSupplyOption>();
            foreach (var hub in gameManager.buildingSystem.Buildings.OfType<SupplyHub>())
            {
                if (hub.FunctionalLevel <= 0)
                    continue;

                if (!LandTilesById.TryGetValue(hub.TileId, out var hubTileData))
                    continue;

                var alliance = hubTileData.Controller;
                if (alliance == Alliance.Neutral)
                    continue;

                if (!SupplyCapitalByAlliance.TryGetValue(alliance, out var capitalTileId))
                    continue;

                var railBottleneck = FindBestRailBottleneck(capitalTileId, hub.TileId, alliance);
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

        private List<DivisionSupplyAssignment> BuildDivisionAssignments(
            IEnumerable<HubSupplyOption> hubOptions,
            out Dictionary<Guid, List<Division>> divisionsByHubId)
        {
            var divisionAssignments = new List<DivisionSupplyAssignment>();
            divisionsByHubId = new Dictionary<Guid, List<Division>>();

            foreach (var division in gameManager.divisionSystem.Divisions)
            {
                if (division == null || division.SupplyConsumption <= 0f)
                    continue;

                if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var alliance))
                    continue;

                var bestAssignment = ChooseBestHubForDivision(division, alliance, hubOptions);
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

            return divisionAssignments;
        }

        private DivisionSupplyAssignment ChooseBestHubForDivision(
            Division division,
            Alliance alliance,
            IEnumerable<HubSupplyOption> hubOptions)
        {
            DivisionSupplyAssignment bestAssignment = null;
            foreach (var hubOption in hubOptions.Where(option => option.Alliance == alliance))
            {
                var distance = FindControlledLandDistance(hubOption.Hub.TileId, division.TileId, alliance);
                if (distance < 0)
                    continue;

                var falloff = GetDistributionFalloff(distance);
                var availableSupply = hubOption.HubSupply * falloff;
                if (availableSupply <= 0f)
                    continue;

                var assignment = new DivisionSupplyAssignment(
                    division,
                    hubOption.Hub,
                    alliance,
                    hubOption.HubSupply,
                    falloff,
                    availableSupply,
                    distance);
                if (IsBetterAssignment(assignment, bestAssignment))
                    bestAssignment = assignment;
            }

            return bestAssignment;
        }

        private static bool IsBetterAssignment(DivisionSupplyAssignment candidate, DivisionSupplyAssignment current)
        {
            if (current == null)
                return true;

            if (!Mathf.Approximately(candidate.AvailableSupply, current.AvailableSupply))
                return candidate.AvailableSupply > current.AvailableSupply;

            if (candidate.Distance != current.Distance)
                return candidate.Distance < current.Distance;

            return candidate.Hub.BuildingId.CompareTo(current.Hub.BuildingId) < 0;
        }

        private bool TryGetRailLevel(
            Vector3Int tileId,
            Alliance alliance,
            Vector3Int? blockedTileId,
            out int railLevel)
        {
            railLevel = 0;
            if (blockedTileId.HasValue && tileId == blockedTileId.Value)
                return false;

            if (!LandTilesById.TryGetValue(tileId, out var landTileData) || landTileData.Controller != alliance)
                return false;

            railLevel = gameManager.buildingSystem
                .GetBuildingsOnTile(tileId, BuildingType.Railroad)
                .Select(building => building.FunctionalLevel)
                .DefaultIfEmpty(0)
                .Max();

            return railLevel > 0;
        }

        private bool IsControlledLandTile(Vector3Int tileId, Alliance alliance, Vector3Int? blockedTileId)
        {
            if (blockedTileId.HasValue && tileId == blockedTileId.Value)
                return false;

            return LandTilesById.TryGetValue(tileId, out var landTileData)
                   && landTileData.Controller == alliance;
        }
    }

    public sealed class HubSupplyOption
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

    public sealed class DivisionSupplyAssignment
    {
        public readonly Division Division;
        public readonly SupplyHub Hub;
        public readonly Alliance Alliance;
        public readonly float HubSupply;
        public readonly float Falloff;
        public readonly float AvailableSupply;
        public readonly int Distance;

        public DivisionSupplyAssignment(
            Division division,
            SupplyHub hub,
            Alliance alliance,
            float hubSupply,
            float falloff,
            float availableSupply,
            int distance)
        {
            Division = division;
            Hub = hub;
            Alliance = alliance;
            HubSupply = hubSupply;
            Falloff = falloff;
            AvailableSupply = availableSupply;
            Distance = distance;
        }
    }
}
