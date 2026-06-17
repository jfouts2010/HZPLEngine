using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models.Ground
{
    public sealed class GroundOperationsSystem
    {
        private readonly GameManager gameManager;
        private const float MinimumProgressPerHour = 0.05f;

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

                if (!GroundSystemUtility.TryGetLandTileData(
                        gameManager,
                        retreatOrder.FinalDestinationTileId,
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

                if (!GroundSystemUtility.AreNeighbors(
                        gameManager,
                        division.TileId,
                        moveOrder.CurrentDestinationTileId))
                    continue;

                var progressPerHour = Mathf.Max(MinimumProgressPerHour, division.Speed);
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

            if (arrivedTileId == moveOrder.FinalDestinationTileId)
            {
                division.CurrentOrder = new HoldGroundOrder(
                    GroundOrderAssignmentSource.System,
                    moveOrder.Purpose == MoveGroundOrderPurpose.Retreat
                        ? "Recovered from retreat"
                        : "Completed movement");
                return;
            }

            if (GroundSystemUtility.AreNeighbors(gameManager, arrivedTileId, moveOrder.FinalDestinationTileId))
            {
                moveOrder.CurrentDestinationTileId = moveOrder.FinalDestinationTileId;
                return;
            }

            division.CurrentOrder = new HoldGroundOrder(
                GroundOrderAssignmentSource.System,
                "Movement paused; no next path step available");
        }

        private void ResolveCapture(Division division, Vector3Int tileId)
        {
            if (!GroundSystemUtility.TryGetLandTileData(gameManager, tileId, out var landTileData))
                return;

            if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var divisionAlliance))
                return;

            var hasHostileNonRetreatingDivision = gameManager.divisionSystem.GetDivisionsOnTile(tileId)
                .Any(candidate => candidate != division
                                  && !GroundSystemUtility.IsRetreating(candidate)
                                  && GroundSystemUtility.TryGetDivisionAlliance(gameManager, candidate, out var alliance)
                                  && GroundSystemUtility.AreHostile(divisionAlliance, alliance));
            if (!hasHostileNonRetreatingDivision)
                landTileData.Controller = divisionAlliance;
        }
         public bool TryAssignRetreat(Division division, Vector3Int fromTileId, string rationale)
        {
            if (division == null)
                return false;

            if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var alliance))
                return DestroyDivision(division);

            foreach (var neighborTileId in GroundSystemUtility.GetNeighborTileIds(gameManager, fromTileId))
            {
                if (!IsValidRetreatDestination(neighborTileId, alliance))
                    continue;

                division.CurrentOrder = new MoveGroundOrder
                {
                    AssignmentSource = GroundOrderAssignmentSource.System,
                    CanBeReplaced = false,
                    Rationale = rationale ?? "Retreating after combat defeat",
                    Purpose = MoveGroundOrderPurpose.Retreat,
                    FinalDestinationTileId = neighborTileId,
                    CurrentDestinationTileId = neighborTileId,
                    MovementProgress = 0f
                };
                return true;
            }

            return DestroyDivision(division);
        }

        private bool IsValidRetreatDestination(Vector3Int tileId, Alliance retreatingAlliance)
        {
            if (!GroundSystemUtility.TryGetLandTileData(gameManager, tileId, out var landTileData))
                return false;

            if (landTileData.Controller != retreatingAlliance)
                return false;

            return !gameManager.divisionSystem.GetDivisionsOnTile(tileId)
                .Any(division => IsNonRetreatingHostileDivision(division, retreatingAlliance));
        }

        private bool IsNonRetreatingHostileDivision(Division division, Alliance retreatingAlliance)
        {
            if (GroundSystemUtility.IsRetreating(division))
                return false;

            return GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var alliance)
                   && GroundSystemUtility.AreHostile(alliance, retreatingAlliance);
        }

        private bool DestroyDivision(Division division)
        {
            return division != null && gameManager.divisionSystem.RemoveDivision(division.DivisionId);
        }
    }
}
