using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models
{
    public class SupplySystem
    {
        private const int MaxHubDistributionDistance = 4;

        private static readonly int[] HubSupplyByEffectiveLevel =
        {
            0, 3, 6, 9, 12, 15, 18, 21, 24, 27, 30
        };

        private readonly GameManager gameManager;
        private Dictionary<Guid, float> supplyAvailabilityRatioByDivisionId = new Dictionary<Guid, float>();
        private Dictionary<Guid, float> supplyStoreRatioByDivisionId = new Dictionary<Guid, float>();

        public SupplySystem(GameManager gameManager)
        {
            this.gameManager = gameManager;
        }

        public IReadOnlyDictionary<Guid, float> SupplyRatioByDivisionId => supplyStoreRatioByDivisionId;

        public void GameTurn(float elapsedHours = 0f)
        {
            supplyAvailabilityRatioByDivisionId = CalculateSupplyRatios();
            ApplyDivisionSupplyStores(elapsedHours);
            supplyStoreRatioByDivisionId = CalculateSupplyStoreRatios();
        }

        public float GetSupplyRatio(Guid divisionId)
        {
            return supplyStoreRatioByDivisionId.TryGetValue(divisionId, out var supplyRatio)
                ? supplyRatio
                : 0f;
        }

        public float GetSupplyAvailabilityRatio(Guid divisionId)
        {
            return supplyAvailabilityRatioByDivisionId.TryGetValue(divisionId, out var supplyRatio)
                ? supplyRatio
                : 0f;
        }

        private void ApplyDivisionSupplyStores(float elapsedHours)
        {
            if (gameManager?.divisionSystem?.Divisions == null)
                return;

            var elapsedDays = Mathf.Max(0f, elapsedHours) / 24f;
            foreach (var division in gameManager.divisionSystem.Divisions)
            {
                if (division == null)
                    continue;

                division.EnsureSupplyStore();
                if (division.SupplyConsumption <= 0f || division.MaxSupplyStore <= 0f)
                    continue;

                if (elapsedDays <= 0f)
                {
                    if (division.SupplyStore <= 0f)
                        division.SupplyStore = division.MaxSupplyStore;
                    continue;
                }

                var dailySupplyUse = Mathf.Max(0f, division.SupplyConsumption);
                var supplyDelta = dailySupplyUse * elapsedDays;
                var availabilityRatio = GetSupplyAvailabilityRatio(division.DivisionId);

                if (availabilityRatio > 1f)
                    division.SupplyStore = Mathf.Min(division.MaxSupplyStore, division.SupplyStore + supplyDelta);
                else if (availabilityRatio <= 0f)
                    division.SupplyStore = Mathf.Max(0f, division.SupplyStore - supplyDelta);
            }
        }

        private Dictionary<Guid, float> CalculateSupplyStoreRatios()
        {
            var ratios = new Dictionary<Guid, float>();
            if (gameManager?.divisionSystem?.Divisions == null)
                return ratios;

            foreach (var division in gameManager.divisionSystem.Divisions)
            {
                if (division == null)
                    continue;

                division.EnsureSupplyStore();
                ratios[division.DivisionId] = division.MaxSupplyStore <= 0f
                    ? 1f
                    : Mathf.Clamp01(division.SupplyStore / division.MaxSupplyStore);
            }

            return ratios;
        }

        private Dictionary<Guid, float> CalculateSupplyRatios()
        {
            var ratios = new Dictionary<Guid, float>();
            if (gameManager == null || gameManager.divisionSystem?.Divisions == null)
                return ratios;

            var landTilesById = BuildLandTileLookup();
            var neighborsByTileId = BuildNeighborLookup();
            var supplyCapitalByAlliance = BuildSupplyCapitalLookup(landTilesById);
            var hubOptions = BuildHubOptions(landTilesById, neighborsByTileId, supplyCapitalByAlliance);
            var assignmentsByHubId = new Dictionary<Guid, List<DivisionSupplyAssignment>>();

            foreach (var division in gameManager.divisionSystem.Divisions)
            {
                if (division == null)
                    continue;

                ratios[division.DivisionId] = 0f;

                if (division.SupplyConsumption <= 0f)
                {
                    ratios[division.DivisionId] = 1f;
                    continue;
                }

                if (!TryGetDivisionAlliance(division, out var alliance))
                    continue;

                var bestAssignment = ChooseBestHubForDivision(
                    division,
                    alliance,
                    hubOptions,
                    neighborsByTileId,
                    landTilesById);

                if (bestAssignment == null)
                    continue;

                if (!assignmentsByHubId.TryGetValue(bestAssignment.Hub.BuildingId, out var assignments))
                {
                    assignments = new List<DivisionSupplyAssignment>();
                    assignmentsByHubId[bestAssignment.Hub.BuildingId] = assignments;
                }

                assignments.Add(bestAssignment);
            }

            foreach (var hubAssignments in assignmentsByHubId.Values)
            {
                if (hubAssignments.Count == 0)
                    continue;

                var hubSupply = hubAssignments[0].HubSupply;
                var totalDemand = hubAssignments.Sum(assignment => Mathf.Max(0f, assignment.Division.SupplyConsumption));
                var allocationRatio = totalDemand <= 0f
                    ? 1f
                    : hubSupply / totalDemand;

                foreach (var assignment in hubAssignments)
                    ratios[assignment.Division.DivisionId] = Mathf.Max(0f, allocationRatio * assignment.Falloff);
            }

            return ratios;
        }

        private Dictionary<Vector3Int, LandTileData> BuildLandTileLookup()
        {
            return (gameManager.Tiles ?? new List<TileData>())
                .OfType<LandTileData>()
                .GroupBy(tileData => tileData.TileId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        private Dictionary<Vector3Int, List<Vector3Int>> BuildNeighborLookup()
        {
            return (gameManager.CampaignTiles ?? new List<Tile>())
                .Where(tile => tile != null)
                .GroupBy(tile => tile.Coordinates)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().NeighborTileIds ?? new List<Vector3Int>());
        }

        private Dictionary<Alliance, Vector3Int> BuildSupplyCapitalLookup(
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

        private List<HubSupplyOption> BuildHubOptions(
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
                    capitalTileId,
                    hub.TileId,
                    alliance,
                    landTilesById,
                    neighborsByTileId);

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

        private DivisionSupplyAssignment ChooseBestHubForDivision(
            Division division,
            Alliance alliance,
            IEnumerable<HubSupplyOption> hubOptions,
            IReadOnlyDictionary<Vector3Int, List<Vector3Int>> neighborsByTileId,
            IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById)
        {
            DivisionSupplyAssignment bestAssignment = null;
            foreach (var hubOption in hubOptions.Where(option => option.Alliance == alliance))
            {
                var distance = FindControlledLandDistance(
                    hubOption.Hub.TileId,
                    division.TileId,
                    alliance,
                    MaxHubDistributionDistance,
                    landTilesById,
                    neighborsByTileId);

                if (distance < 0)
                    continue;

                var falloff = GetDistributionFalloff(distance);
                var supplyAfterFalloff = hubOption.HubSupply * falloff;
                if (supplyAfterFalloff <= 0f)
                    continue;

                var assignment = new DivisionSupplyAssignment(
                    division,
                    hubOption.Hub,
                    hubOption.HubSupply,
                    falloff,
                    supplyAfterFalloff,
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

        private int FindBestRailBottleneck(
            Vector3Int startTileId,
            Vector3Int targetTileId,
            Alliance alliance,
            IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById,
            IReadOnlyDictionary<Vector3Int, List<Vector3Int>> neighborsByTileId)
        {
            if (!TryGetRailLevel(startTileId, alliance, landTilesById, out var startRailLevel))
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
                    if (!TryGetRailLevel(neighborTileId, alliance, landTilesById, out var neighborRailLevel))
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

        private bool TryGetRailLevel(
            Vector3Int tileId,
            Alliance alliance,
            IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById,
            out int railLevel)
        {
            railLevel = 0;
            if (!landTilesById.TryGetValue(tileId, out var landTileData) || landTileData.Controller != alliance)
                return false;

            railLevel = (gameManager.buildingSystem?.GetBuildingsOnTile(tileId, BuildingType.Railroad)
                    ?? new List<Building>())
                .Select(building => building.FunctionalLevel)
                .DefaultIfEmpty(0)
                .Max();

            return railLevel > 0;
        }

        private static int FindControlledLandDistance(
            Vector3Int startTileId,
            Vector3Int targetTileId,
            Alliance alliance,
            int maxDistance,
            IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById,
            IReadOnlyDictionary<Vector3Int, List<Vector3Int>> neighborsByTileId)
        {
            if (!IsControlledLandTile(startTileId, alliance, landTilesById)
                || !IsControlledLandTile(targetTileId, alliance, landTilesById))
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
                        || !IsControlledLandTile(neighborTileId, alliance, landTilesById))
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
            Vector3Int tileId,
            Alliance alliance,
            IReadOnlyDictionary<Vector3Int, LandTileData> landTilesById)
        {
            return landTilesById.TryGetValue(tileId, out var landTileData)
                   && landTileData.Controller == alliance;
        }

        private bool TryGetDivisionAlliance(Division division, out Alliance alliance)
        {
            alliance = Alliance.Neutral;
            if (gameManager?.CampaignTemplate?.CountryAllianceAssignments == null || division == null)
                return false;

            var assignment = gameManager.CampaignTemplate.CountryAllianceAssignments
                .FirstOrDefault(candidate => candidate != null && candidate.CountryId == division.CountryId);
            if (assignment == null)
                return false;

            alliance = assignment.Alliance;
            return alliance != Alliance.Neutral;
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

        private class HubSupplyOption
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

        private class DivisionSupplyAssignment
        {
            public readonly Division Division;
            public readonly SupplyHub Hub;
            public readonly float HubSupply;
            public readonly float Falloff;
            public readonly float AvailableSupply;
            public readonly int Distance;

            public DivisionSupplyAssignment(
                Division division,
                SupplyHub hub,
                float hubSupply,
                float falloff,
                float availableSupply,
                int distance)
            {
                Division = division;
                Hub = hub;
                HubSupply = hubSupply;
                Falloff = falloff;
                AvailableSupply = availableSupply;
                Distance = distance;
            }
        }
    }
}
