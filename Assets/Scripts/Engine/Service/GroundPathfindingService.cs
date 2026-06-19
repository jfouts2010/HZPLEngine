using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models.Ground
{
    public static  class GroundPathfindingService
    {
        public static bool TryFindFriendlyPath(
            GameManager gameManager,
            Vector3Int startTileId,
            Vector3Int destinationTileId,
            Alliance alliance,
            out GroundPath path)
        {
            path = null;
            if (gameManager == null)
                return false;

            if (startTileId == destinationTileId)
            {
                path = GroundPath.FromSingleTile(startTileId);
                return true;
            }

            if (!IsFriendlyLandTile(gameManager, startTileId, alliance)
                || !IsFriendlyLandTile(gameManager, destinationTileId, alliance))
                return false;

            if (!IsSafeFriendlyWaypoint(gameManager, destinationTileId, alliance))
                return false;

            var visitedTileIds = new HashSet<Vector3Int> { startTileId };
            var previousByTileId = new Dictionary<Vector3Int, Vector3Int>();
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(startTileId);

            while (queue.Count > 0)
            {
                var currentTileId = queue.Dequeue();
                if (currentTileId == destinationTileId)
                {
                    path = GroundPath.FromTileChain(previousByTileId, startTileId, destinationTileId);
                    return true;
                }

                foreach (var neighborTileId in OrderTileIds(
                             GroundSystemUtility.GetNeighborTileIds(gameManager, currentTileId)))
                {
                    if (visitedTileIds.Contains(neighborTileId))
                        continue;

                    if (!IsFriendlyLandTile(gameManager, neighborTileId, alliance))
                        continue;

                    if (!IsSafeFriendlyWaypoint(gameManager, neighborTileId, alliance))
                        continue;

                    visitedTileIds.Add(neighborTileId);
                    previousByTileId[neighborTileId] = currentTileId;
                    queue.Enqueue(neighborTileId);
                }
            }

            return false;
        }

        public static bool TryPrepareMoveGroundOrder(
            GameManager gameManager,
            Vector3Int fromTileId,
            Vector3Int destinationTileId,
            Alliance alliance,
            MoveGroundOrder moveOrder)
        {
            if (gameManager == null || moveOrder == null)
                return false;

            moveOrder.MovementProgress = 0f;

            if (TryFindFriendlyPath(gameManager, fromTileId, destinationTileId, alliance, out var path))
            {
                moveOrder.Path = path;
                if (path.TryGetNextStep(fromTileId, out var nextStep))
                {
                    moveOrder.CurrentDestinationTileId = nextStep;
                    return true;
                }

                return false;
            }

            if (GroundSystemUtility.AreNeighbors(gameManager, fromTileId, destinationTileId))
            {
                moveOrder.Path = GroundPath.FromDirectStep(fromTileId, destinationTileId);
                moveOrder.CurrentDestinationTileId = destinationTileId;
                return true;
            }

            return false;
        }

        public static bool IsSafeFriendlyWaypoint(GameManager gameManager, Vector3Int tileId, Alliance alliance)
        {
            if (!IsFriendlyLandTile(gameManager, tileId, alliance))
                return false;

            if (IsFriendlyTileUnderAttack(gameManager, tileId, alliance))
                return false;

            return !HasNonRetreatingHostileDivision(gameManager, tileId, alliance);
        }

        private static bool IsFriendlyLandTile(GameManager gameManager, Vector3Int tileId, Alliance alliance)
        {
            return GroundSystemUtility.TryGetLandTileData(gameManager, tileId, out var landTileData)
                   && landTileData.Controller == alliance;
        }

        private static bool IsFriendlyTileUnderAttack(GameManager gameManager, Vector3Int tileId, Alliance alliance)
        {
            foreach (var combat in gameManager.GetActiveGroundCombats())
            {
                if (combat == null || combat.DefendingTileId != tileId)
                    continue;

                if (combat.DefendingAlliance == alliance)
                    return true;
            }

            return false;
        }

        private static bool HasNonRetreatingHostileDivision(
            GameManager gameManager,
            Vector3Int tileId,
            Alliance alliance)
        {
            return gameManager.divisionSystem.GetDivisionsOnTile(tileId)
                .Any(division => !GroundSystemUtility.IsRetreating(division)
                                 && GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var divisionAlliance)
                                 && GroundSystemUtility.AreHostile(alliance, divisionAlliance));
        }

        private static IEnumerable<Vector3Int> OrderTileIds(IEnumerable<Vector3Int> tileIds)
        {
            return (tileIds ?? Enumerable.Empty<Vector3Int>())
                .OrderBy(tileId => tileId.x)
                .ThenBy(tileId => tileId.y)
                .ThenBy(tileId => tileId.z);
        }
    }
}
