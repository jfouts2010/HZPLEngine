using System.Collections.Generic;
using System.Linq;
using Engine.Models.Ground;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models
{
    public class AllianceAI
    {
        private readonly GameManager _gameManager;
        private readonly HashSet<Vector3Int> _frontTileIds = new HashSet<Vector3Int>();

        public Alliance Alliance { get; }

        public IReadOnlyCollection<Vector3Int> FrontTileIds => _frontTileIds;

        public AllianceAI(GameManager gameManager, Alliance alliance)
        {
            _gameManager = gameManager;
            Alliance = alliance;
        }

        internal void RefreshFront()
        {
            _frontTileIds.Clear();

            if (!TryGetHostileAlliance(Alliance, out var hostileAlliance))
                return;

            var controllersByTileId = BuildControllerLookup(_gameManager.Tiles);
            var neighborsByTileId = BuildNeighborLookup(_gameManager.CampaignTiles);

            foreach (var entry in controllersByTileId)
            {
                if (entry.Value != Alliance)
                    continue;

                if (!neighborsByTileId.TryGetValue(entry.Key, out var neighborTileIds))
                    continue;

                foreach (var neighborTileId in neighborTileIds)
                {
                    if (controllersByTileId.TryGetValue(neighborTileId, out var neighborController)
                        && neighborController == hostileAlliance)
                    {
                        _frontTileIds.Add(entry.Key);
                        break;
                    }
                }
            }
        }

        internal void AssignMovementOrders()
        {
            if (!TryGetHostileAlliance(Alliance, out var hostileAlliance))
                return;

            var controllersByTileId = BuildControllerLookup(_gameManager.Tiles);
            var neighborsByTileId = BuildNeighborLookup(_gameManager.CampaignTiles);
            var distanceToFrontByTileId = BuildDistanceToFrontLookup(controllersByTileId, neighborsByTileId);

            foreach (var division in _gameManager.divisionSystem.Divisions)
            {
                if (!CanReceiveAIOrder(division))
                    continue;

                if (!GroundSystemUtility.TryGetDivisionAlliance(_gameManager, division, out var divisionAlliance)
                    || divisionAlliance != Alliance)
                    continue;

                if (!TryChooseMovementTarget(
                        division.TileId,
                        hostileAlliance,
                        controllersByTileId,
                        neighborsByTileId,
                        distanceToFrontByTileId,
                        out var targetTileId))
                    continue;

                division.CurrentOrder = new MoveGroundOrder
                {
                    AssignmentSource = GroundOrderAssignmentSource.AI,
                    CanBeReplaced = true,
                    Rationale = "Advancing toward enemy land",
                    Purpose = MoveGroundOrderPurpose.Normal,
                    FinalDestinationTileId = targetTileId,
                    CurrentDestinationTileId = targetTileId,
                    MovementProgress = 0f
                };
            }
        }

        private bool CanReceiveAIOrder(Division division)
        {
            if (division == null)
                return false;

            if (GroundSystemUtility.IsRetreating(division))
                return false;

            if (_gameManager.IsDivisionEngagedInGroundCombat(division.DivisionId))
                return false;

            if (division.CurrentOrder is MoveGroundOrder)
                return false;

            return division.CurrentOrder == null || division.CurrentOrder.CanBeReplaced;
        }

        private bool TryChooseMovementTarget(
            Vector3Int tileId,
            Alliance hostileAlliance,
            IReadOnlyDictionary<Vector3Int, Alliance> controllersByTileId,
            IReadOnlyDictionary<Vector3Int, List<Vector3Int>> neighborsByTileId,
            IReadOnlyDictionary<Vector3Int, int> distanceToFrontByTileId,
            out Vector3Int targetTileId)
        {
            targetTileId = default;

            if (!neighborsByTileId.TryGetValue(tileId, out var neighborTileIds))
                return false;

            foreach (var neighborTileId in OrderTileIds(neighborTileIds))
            {
                if (controllersByTileId.TryGetValue(neighborTileId, out var controller)
                    && controller == hostileAlliance)
                {
                    targetTileId = neighborTileId;
                    return true;
                }
            }

            if (!distanceToFrontByTileId.TryGetValue(tileId, out var currentDistance))
                return false;

            foreach (var neighborTileId in OrderTileIds(neighborTileIds))
            {
                if (!controllersByTileId.TryGetValue(neighborTileId, out var controller)
                    || controller != Alliance)
                    continue;

                if (!distanceToFrontByTileId.TryGetValue(neighborTileId, out var neighborDistance))
                    continue;

                if (neighborDistance >= currentDistance)
                    continue;

                targetTileId = neighborTileId;
                return true;
            }

            return false;
        }

        private Dictionary<Vector3Int, int> BuildDistanceToFrontLookup(
            IReadOnlyDictionary<Vector3Int, Alliance> controllersByTileId,
            IReadOnlyDictionary<Vector3Int, List<Vector3Int>> neighborsByTileId)
        {
            var distanceByTileId = new Dictionary<Vector3Int, int>();
            var queue = new Queue<Vector3Int>();

            foreach (var frontTileId in OrderTileIds(_frontTileIds))
            {
                if (!controllersByTileId.TryGetValue(frontTileId, out var controller)
                    || controller != Alliance)
                    continue;

                distanceByTileId[frontTileId] = 0;
                queue.Enqueue(frontTileId);
            }

            while (queue.Count > 0)
            {
                var tileId = queue.Dequeue();
                var nextDistance = distanceByTileId[tileId] + 1;

                if (!neighborsByTileId.TryGetValue(tileId, out var neighborTileIds))
                    continue;

                foreach (var neighborTileId in OrderTileIds(neighborTileIds))
                {
                    if (distanceByTileId.ContainsKey(neighborTileId))
                        continue;

                    if (!controllersByTileId.TryGetValue(neighborTileId, out var controller)
                        || controller != Alliance)
                        continue;

                    distanceByTileId[neighborTileId] = nextDistance;
                    queue.Enqueue(neighborTileId);
                }
            }

            return distanceByTileId;
        }

        private static bool TryGetHostileAlliance(Alliance alliance, out Alliance hostileAlliance)
        {
            switch (alliance)
            {
                case Alliance.Bluefor:
                    hostileAlliance = Alliance.Redfor;
                    return true;
                case Alliance.Redfor:
                    hostileAlliance = Alliance.Bluefor;
                    return true;
                default:
                    hostileAlliance = default;
                    return false;
            }
        }

        private static Dictionary<Vector3Int, Alliance> BuildControllerLookup(IReadOnlyList<TileData> tiles)
        {
            var controllersByTileId = new Dictionary<Vector3Int, Alliance>();
            if (tiles == null)
                return controllersByTileId;

            foreach (var tileData in tiles)
            {
                if (tileData is LandTileData landTileData)
                    controllersByTileId[tileData.TileId] = landTileData.Controller;
            }

            return controllersByTileId;
        }

        private static Dictionary<Vector3Int, List<Vector3Int>> BuildNeighborLookup(IReadOnlyList<Tile> tiles)
        {
            var neighborsByTileId = new Dictionary<Vector3Int, List<Vector3Int>>();
            if (tiles == null)
                return neighborsByTileId;

            foreach (var tile in tiles)
            {
                if (tile == null)
                    continue;

                neighborsByTileId[tile.Coordinates] =
                    tile.NeighborTileIds ?? new List<Vector3Int>();
            }

            return neighborsByTileId;
        }

        private static IOrderedEnumerable<Vector3Int> OrderTileIds(IEnumerable<Vector3Int> tileIds)
        {
            return (tileIds ?? Enumerable.Empty<Vector3Int>())
                .OrderBy(tileId => tileId.x)
                .ThenBy(tileId => tileId.y)
                .ThenBy(tileId => tileId.z);
        }
    }
}
