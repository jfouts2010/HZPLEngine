using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models.Ground
{
    public sealed class GroundCombatSystem
    {
        [SerializeReference]
        public List<GroundCombat> Combats = new List<GroundCombat>();

        private Dictionary<Vector3Int, GroundCombat> combatByDefendingTileId;
        private readonly GameManager gameManager;

        public GroundCombatSystem(GameManager gameManager)
        {
            this.gameManager = gameManager;
        }

        public void GameTurn()
        {
            ReconcileCombatsFromOrders();
            ReconcileCombatParticipants();
            RemoveInactiveCombats();
        }

        private void ReconcileCombatsFromOrders()
        {
            foreach (var division in gameManager.Divisions.Divisions.Where(division => division != null))
            {
                if (GroundSystemUtility.IsRetreating(division))
                    continue;

                if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var attackingAlliance))
                    continue;

                if (!TryGetOrderTargetTileId(division, out var defendingTileId))
                    continue;

                if (!GroundSystemUtility.TryGetLandTileData(gameManager, defendingTileId, out var landTileData))
                    continue;

                var defendingAlliance = landTileData.Controller;
                if (!GroundSystemUtility.AreHostile(attackingAlliance, defendingAlliance))
                    continue;

                var combat = GetOrCreateCombat(
                    defendingTileId,
                    attackingAlliance,
                    defendingAlliance);

                if (!combat.AttackerDivisionIds.Contains(division.DivisionId))
                    combat.AttackerDivisionIds.Add(division.DivisionId);
            }
        }

        private void ReconcileCombatParticipants()
        {
            foreach (var combat in Combats.Where(combat => combat != null))
            {
                combat.AttackerDivisionIds = ReconcileAttackers(combat);
                combat.DefenderDivisionIds = ReconcileDefenders(combat);
            }
        }

        private List<System.Guid> ReconcileAttackers(GroundCombat combat)
        {
            return gameManager.Divisions.Divisions
                .Where(division => IsValidAttacker(division, combat))
                .Select(division => division.DivisionId)
                .Distinct()
                .ToList();
        }

        private List<System.Guid> ReconcileDefenders(GroundCombat combat)
        {
            return gameManager.Divisions.GetDivisionsOnTile(combat.DefendingTileId)
                .Where(division => IsValidDefender(division, combat))
                .Select(division => division.DivisionId)
                .Distinct()
                .ToList();
        }

        private bool IsValidAttacker(Division division, GroundCombat combat)
        {
            if (division == null || GroundSystemUtility.IsRetreating(division))
                return false;

            if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var alliance)
                || alliance != combat.AttackingAlliance)
                return false;

            return TryGetOrderTargetTileId(division, out var targetTileId)
                   && targetTileId == combat.DefendingTileId;
        }

        private bool IsValidDefender(Division division, GroundCombat combat)
        {
            if (division == null || GroundSystemUtility.IsRetreating(division))
                return false;

            return GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var alliance)
                   && alliance == combat.DefendingAlliance;
        }

        private void RemoveInactiveCombats()
        {
            var inactiveTileIds = Combats
                .Where(combat => combat == null
                                 || combat.AttackerDivisionIds.Count == 0
                                 || combat.DefenderDivisionIds.Count == 0)
                .Select(combat => combat?.DefendingTileId)
                .Where(tileId => tileId.HasValue)
                .Select(tileId => tileId.Value)
                .ToList();

            foreach (var tileId in inactiveTileIds)
                RemoveCombat(tileId);
        }

        private static bool TryGetOrderTargetTileId(Division division, out UnityEngine.Vector3Int targetTileId)
        {
            switch (division.CurrentOrder)
            {
                case AttackGroundOrder attackOrder:
                    targetTileId = attackOrder.CurrentDestinationTileId;
                    return true;
                case SupportAttackGroundOrder supportAttackOrder:
                    targetTileId = supportAttackOrder.TargetTileId;
                    return true;
                default:
                    targetTileId = default;
                    return false;
            }
        }
        public bool TryGetCombat(Vector3Int defendingTileId, out GroundCombat combat)
        {
            EnsureIndex();
            return combatByDefendingTileId.TryGetValue(defendingTileId, out combat);
        }

        public GroundCombat GetOrCreateCombat(
            Vector3Int defendingTileId,
            Alliance attackingAlliance,
            Alliance defendingAlliance)
        {
            EnsureIndex();
            if (combatByDefendingTileId.TryGetValue(defendingTileId, out var combat))
                return combat;

            combat = new GroundCombat(defendingTileId, attackingAlliance, defendingAlliance);
            Combats.Add(combat);
            combatByDefendingTileId[defendingTileId] = combat;
            return combat;
        }

        public bool RemoveCombat(Vector3Int defendingTileId)
        {
            EnsureIndex();
            if (!combatByDefendingTileId.TryGetValue(defendingTileId, out var combat))
                return false;

            combatByDefendingTileId.Remove(defendingTileId);
            return Combats.Remove(combat);
        }

        public void RebuildIndex()
        {
            combatByDefendingTileId = (Combats ?? new List<GroundCombat>())
                .Where(combat => combat != null)
                .GroupBy(combat => combat.DefendingTileId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        private void EnsureIndex()
        {
            if (combatByDefendingTileId == null)
                RebuildIndex();
        }
    }
}
