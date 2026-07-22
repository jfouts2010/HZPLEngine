using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models.Ground
{
    public sealed class GroundOperationsSystem
    {
        private readonly GameManager gameManager;
        private const float CombatPinnedMovementProgressLimit = 0.5f;

        public GroundOperationsSystem(GameManager gameManager)
        {
            this.gameManager = gameManager;
        }

        public void GameTurn(float elapsedHours)
        {
            AdvanceMovement(elapsedHours);
            ResolveOverruns();
        }

        private void ResolveOverruns()
        {
            foreach (var division in gameManager.divisionSystem.Divisions.ToList())
            {
                if (division?.CurrentOrder is not MoveGroundOrder { Purpose: MoveGroundOrderPurpose.Retreat } retreatOrder)
                    continue;

                if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var divisionAlliance))
                    continue;

                if (!gameManager.tileSystem.TryGetLand(
                        retreatOrder.DestinationTileId,
                        out var destinationTileData))
                    continue;

                if (GroundSystemUtility.AreHostile(divisionAlliance, destinationTileData.Controller))
                    gameManager.divisionSystem.RemoveDivision(division.DivisionId);
            }
        }
         public void AdvanceMovement(float elapsedHours)
        {
            foreach (var division in gameManager.divisionSystem.Divisions.ToList())
            {
                if (division?.CurrentOrder is not MoveGroundOrder moveOrder)
                    continue;

                if (!gameManager.tileSystem.Get(division.TileId)
                        .IsNeighbor(moveOrder.CurrentDestinationTileId))
                    continue;

                var tileDistanceKm = Mathf.Max(
                    SimulationSettings.MinTileDistanceKM,
                    gameManager.SimulationSettings.TileDistanceKM);
                var progressPerHour = Mathf.Max(0f, division.Speed) / tileDistanceKm;
                if (!moveOrder.IsRetreat && gameManager.IsDivisionAttackingInGroundCombat(division.DivisionId))
                    continue;

                if (!moveOrder.IsRetreat && gameManager.IsDivisionDefendingInGroundCombat(division.DivisionId))
                {
                    moveOrder.MovementProgress = Mathf.Min(
                        CombatPinnedMovementProgressLimit,
                        moveOrder.MovementProgress + progressPerHour * elapsedHours);
                    continue;
                }

                moveOrder.MovementProgress += progressPerHour * elapsedHours;

                if (IsArrivalBlockedByDefenders(moveOrder.CurrentDestinationTileId, division))
                {
                    moveOrder.MovementProgress = Mathf.Min(moveOrder.MovementProgress, 0.99f);
                    continue;
                }

                if (moveOrder.MovementProgress >= 1f)
                    CompleteCurrentMoveStep(division, moveOrder);
            }
        }

        private bool IsArrivalBlockedByDefenders(Vector3Int targetTileId, Division attacker)
        {
            if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, attacker, out var attackerAlliance))
                return true;

            return gameManager.divisionSystem.GetDivisionsOnTile(targetTileId)
                .Any(division => !GroundSystemUtility.IsRetreating(division)
                                 && GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var alliance)
                                 && GroundSystemUtility.AreHostile(attackerAlliance, alliance));
        }

        private void CompleteCurrentMoveStep(Division division, MoveGroundOrder moveOrder)
        {
            var arrivedTileId = moveOrder.CurrentDestinationTileId;
            gameManager.divisionSystem.MoveDivision(division.DivisionId, arrivedTileId);
            moveOrder.MovementProgress = 0f;

            ResolveCapture(division, arrivedTileId);

            if (arrivedTileId == moveOrder.DestinationTileId)
            {
                division.CurrentOrder = new HoldGroundOrder(
                    GroundOrderAssignmentSource.System,
                    moveOrder.Purpose == MoveGroundOrderPurpose.Retreat
                        ? "Recovered from retreat"
                        : "Completed movement");
                return;
            }

            if (TryAdvanceMoveOrderToNextStep(division, moveOrder, arrivedTileId))
                return;

            division.CurrentOrder = new HoldGroundOrder(
                GroundOrderAssignmentSource.System,
                "Movement paused; no next path step available");
        }

        private bool TryAdvanceMoveOrderToNextStep(
            Division division,
            MoveGroundOrder moveOrder,
            Vector3Int arrivedTileId)
        {
            if (moveOrder.Path != null
                && moveOrder.Path.TryGetNextStep(arrivedTileId, out var pathNextStep)
                && gameManager.tileSystem.Get(arrivedTileId).IsNeighbor(pathNextStep)
                && IsNextStepAllowed(division, moveOrder, pathNextStep))
            {
                moveOrder.CurrentDestinationTileId = pathNextStep;
                return true;
            }

            if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var alliance))
                return false;

            if (GroundPathfindingService.TryFindFriendlyPath(
                    gameManager,
                    arrivedTileId,
                    moveOrder.DestinationTileId,
                    alliance,
                    out var refreshedPath)
                && refreshedPath.TryGetNextStep(arrivedTileId, out var refreshedNextStep)
                && IsNextStepAllowed(division, moveOrder, refreshedNextStep))
            {
                moveOrder.Path = refreshedPath;
                moveOrder.CurrentDestinationTileId = refreshedNextStep;
                return true;
            }

            if (gameManager.tileSystem.Get(arrivedTileId).IsNeighbor(moveOrder.DestinationTileId)
                && IsNextStepAllowed(division, moveOrder, moveOrder.DestinationTileId))
            {
                moveOrder.Path = GroundPath.FromDirectStep(arrivedTileId, moveOrder.DestinationTileId);
                moveOrder.CurrentDestinationTileId = moveOrder.DestinationTileId;
                return true;
            }

            return false;
        }

        private bool IsNextStepAllowed(
            Division division,
            MoveGroundOrder moveOrder,
            Vector3Int nextTileId)
        {
            if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var alliance))
                return false;

            if (moveOrder.IsRetreat)
                return GroundPathfindingService.IsSafeFriendlyWaypoint(gameManager, nextTileId, alliance);

            if (gameManager.tileSystem.TryGetLand(nextTileId, out var landTileData)
                && GroundSystemUtility.AreHostile(alliance, landTileData.Controller))
                return true;

            return GroundPathfindingService.IsSafeFriendlyWaypoint(gameManager, nextTileId, alliance);
        }

        private void ResolveCapture(Division division, Vector3Int tileId)
        {
            if (!gameManager.tileSystem.TryGetLand(tileId, out var landTileData))
                return;

            if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var divisionAlliance))
                return;

            var hasHostileNonRetreatingDivision = gameManager.divisionSystem.GetDivisionsOnTile(tileId)
                .Any(candidate => candidate != division
                                  && !GroundSystemUtility.IsRetreating(candidate)
                                  && GroundSystemUtility.TryGetDivisionAlliance(gameManager, candidate, out var alliance)
                                  && GroundSystemUtility.AreHostile(divisionAlliance, alliance));
            if (!hasHostileNonRetreatingDivision)
                gameManager.tileSystem.ChangeControl(tileId, divisionAlliance);
        }
         public bool TryAssignRetreat(Division division, Vector3Int fromTileId, string rationale)
        {
            if (division == null)
                return false;

            if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var alliance))
                return DestroyDivision(division);

            foreach (var neighborTileId in gameManager.tileSystem.Get(fromTileId).NeighborTileIds)
            {
                if (!GroundPathfindingService.IsSafeFriendlyWaypoint(gameManager, neighborTileId, alliance))
                    continue;

                var retreatOrder = new MoveGroundOrder
                {
                    AssignmentSource = GroundOrderAssignmentSource.System,
                    CanBeReplaced = false,
                    Rationale = rationale,
                    Purpose = MoveGroundOrderPurpose.Retreat
                };

                if (!GroundPathfindingService.TryPrepareMoveGroundOrder(
                        gameManager,
                        fromTileId,
                        neighborTileId,
                        alliance,
                        retreatOrder))
                    continue;

                division.CurrentOrder = retreatOrder;
                return true;
            }

            return DestroyDivision(division);
        }

        private bool DestroyDivision(Division division)
        {
            return division != null && gameManager.divisionSystem.RemoveDivision(division.DivisionId);
        }
    }
}
