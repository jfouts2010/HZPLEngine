using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models.Ground
{
    public sealed class GroundCombatSystem
    {
        private const float MultiOriginWidthMultiplier = 1.5f;
        private const float ProtectedShotMissChance = 0.9f;
        private const float ExposedShotMissChance = 0.6f;
        private const float MinimumTargetWeight = 0.01f;

        [SerializeReference]
        public List<GroundCombat> Combats = new List<GroundCombat>();

        private Dictionary<Vector3Int, GroundCombat> combatByDefendingTileId;
        private readonly GameManager gameManager;
        private readonly GroundOperationsSystem groundOperationsSystem;
        private readonly System.Random random;

        public GroundCombatSystem(GameManager gameManager, GroundOperationsSystem groundOperationsSystem)
        {
            this.gameManager = gameManager;
            this.groundOperationsSystem = groundOperationsSystem;
            random = new System.Random(0);
        }

        public void GameTurn()
        {
            ReconcileCombatsFromOrders();
            ReconcileCombatParticipants();
            ResolveCombatRounds();
            ReconcileCombatParticipants();
            RemoveInactiveCombats();
        }

        private void ReconcileCombatsFromOrders()
        {
            foreach (var division in gameManager.divisionSystem.Divisions)
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
            foreach (var combat in Combats)
            {
                combat.AttackerDivisionIds = ReconcileAttackers(combat);
                combat.DefenderDivisionIds = ReconcileDefenders(combat);
            }
        }

        private List<System.Guid> ReconcileAttackers(GroundCombat combat)
        {
            return (combat.AttackerDivisionIds ?? new List<Guid>())
                .Distinct()
                .Where(divisionId => gameManager.divisionSystem.TryGetDivision(divisionId, out var division)
                                     && IsValidAttacker(division, combat))
                .ToList();
        }

        private List<System.Guid> ReconcileDefenders(GroundCombat combat)
        {
            return gameManager.divisionSystem.GetDivisionsOnTile(combat.DefendingTileId)
                .Where(division => IsValidDefender(division, combat))
                .Select(division => division.DivisionId)
                .Distinct()
                .ToList();
        }

        private bool IsValidAttacker(Division division, GroundCombat combat)
        {
            if (!IsCombatReady(division) || GroundSystemUtility.IsRetreating(division))
                return false;

            if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var alliance)
                || alliance != combat.AttackingAlliance)
                return false;

            return TryGetOrderTargetTileId(division, out var targetTileId)
                   && targetTileId == combat.DefendingTileId;
        }

        private bool IsValidDefender(Division division, GroundCombat combat)
        {
            if (!IsCombatReady(division))
                return false;

            return GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var alliance)
                   && alliance == combat.DefendingAlliance;
        }

        private void ResolveCombatRounds()
        {
            foreach (var combat in Combats.ToList())
                ResolveCombatRound(combat);
        }

        private void ResolveCombatRound(GroundCombat combat)
        {
            ResolveBrokenAttackers(combat);
            ResolveBrokenDefenders(combat);

            var attackers = BuildCombatants(ReconcileAttackers(combat));
            var defenders = BuildCombatants(ReconcileDefenders(combat));

            if (attackers.Count == 0 || defenders.Count == 0)
            {
                EndCombatIfDecided(combat, attackers, defenders);
                return;
            }

            if (!TryGetDefendingTerrain(combat, out var terrain))
                return;

            var combatWidth = GetCombatWidth(terrain, attackers);
            var attackerFront = AssignFrontLine(attackers, combatWidth, true);
            var defenderFront = AssignFrontLine(defenders, combatWidth, false);

            FirePhase(attackerFront, defenderFront, terrain, true);
            FirePhase(defenderFront, attackerFront, terrain, false);

            ResolveBrokenAttackers(combat);
            ResolveBrokenDefenders(combat);

            attackers = BuildCombatants(ReconcileAttackers(combat));
            defenders = BuildCombatants(ReconcileDefenders(combat));
            EndCombatIfDecided(combat, attackers, defenders);
        }

        private List<Combatant> BuildCombatants(IEnumerable<Guid> divisionIds)
        {
            return (divisionIds ?? Enumerable.Empty<Guid>())
                .Select(TryBuildCombatant)
                .ToList();
        }

        private Combatant TryBuildCombatant(Guid divisionId)
        {
            if (!gameManager.divisionSystem.TryGetDivision(divisionId, out var division))
                return null;

            return new Combatant(division);
        }

        private bool TryGetDefendingTerrain(GroundCombat combat, out TileTerrain terrain)
        {
            terrain = TileTerrain.Plains;
            var tile = gameManager.CampaignTiles?
                .FirstOrDefault(candidate => candidate.Coordinates == combat.DefendingTileId);
            if (tile == null)
                return false;

            terrain = tile.Terrain;
            return true;
        }

        private static int GetCombatWidth(TileTerrain terrain, IReadOnlyCollection<Combatant> attackers)
        {
            var width = GetBaseCombatWidth(terrain);
            if (attackers.Select(attacker => attacker.Division.TileId).Distinct().Count() > 1)
                width = Mathf.FloorToInt(width * MultiOriginWidthMultiplier);

            return Mathf.Max(1, width);
        }

        private static int GetBaseCombatWidth(TileTerrain terrain)
        {
            return terrain switch
            {
                TileTerrain.Mountain => 50,
                TileTerrain.Hills => 70,
                TileTerrain.Tundra => 70,
                _ => 80
            };
        }

        private static float GetAttackerFireMultiplier(TileTerrain terrain)
        {
            return terrain switch
            {
                TileTerrain.Mountain => 0.6f,
                TileTerrain.Hills => 0.8f,
                _ => 1f
            };
        }

        private static List<Combatant> AssignFrontLine(
            IReadOnlyList<Combatant> combatants,
            int combatWidth,
            bool useToughness)
        {
            var frontLine = new List<Combatant>();
            var usedWidth = 0;

            foreach (var combatant in combatants)
            {
                var width = Mathf.Max(0, combatant.Division.CombatWidth);
                if (usedWidth + width > combatWidth)
                    continue;

                combatant.DefensePoints = useToughness
                    ? combatant.Division.Toughness
                    : combatant.Division.Defense;
                frontLine.Add(combatant);
                usedWidth += width;
            }

            if (frontLine.Count != 0 || combatants.Count == 0)
                return frontLine;

            var overWidthCombatant = combatants[0];
            overWidthCombatant.DefensePoints = useToughness
                ? overWidthCombatant.Division.Toughness
                : overWidthCombatant.Division.Defense;
            frontLine.Add(overWidthCombatant);
            return frontLine;
        }

        private void FirePhase(
            IReadOnlyList<Combatant> shooters,
            IReadOnlyList<Combatant> targets,
            TileTerrain terrain,
            bool shootersAreAttackers)
        {
            if (shooters.Count == 0 || targets.Count == 0)
                return;

            foreach (var shooter in shooters)
            {
                var target = ChooseTarget(shooter, targets);
                if (target == null)
                    continue;

                var shots = CalculateShotCount(shooter, target, terrain, shootersAreAttackers);
                for (var shotIndex = 0; shotIndex < shots; shotIndex++)
                    ResolveShot(target);
            }
        }

        private Combatant ChooseTarget(Combatant shooter, IReadOnlyList<Combatant> targets)
        {
            var totalWeight = 0f;
            var weights = new List<float>(targets.Count);
            var preferSoftTargets = shooter.Division.SoftAttack >= shooter.Division.HardAttack;

            foreach (var target in targets)
            {
                var softness = Mathf.Clamp01(target.Division.Softness);
                var weight = preferSoftTargets ? softness : 1f - softness;
                weight = Mathf.Max(MinimumTargetWeight, weight);
                weights.Add(weight);
                totalWeight += weight;
            }

            var roll = (float)random.NextDouble() * totalWeight;
            for (var index = 0; index < targets.Count; index++)
            {
                roll -= weights[index];
                if (roll <= 0f)
                    return targets[index];
            }

            return targets[targets.Count - 1];
        }

        private static int CalculateShotCount(
            Combatant shooter,
            Combatant target,
            TileTerrain terrain,
            bool shooterIsAttacker)
        {
            var targetSoftness = Mathf.Clamp01(target.Division.Softness);
            var shots = shooter.Division.SoftAttack * targetSoftness
                        + shooter.Division.HardAttack * (1f - targetSoftness);
            shots *= shooter.StrengthPercent;

            if (shooterIsAttacker)
                shots *= GetAttackerFireMultiplier(terrain);

            return Mathf.Max(0, Mathf.FloorToInt(shots));
        }

        private void ResolveShot(Combatant target)
        {
            var missChance = target.DefensePoints > 0
                ? ProtectedShotMissChance
                : ExposedShotMissChance;
            var hit = random.NextDouble() >= missChance;
            target.DefensePoints = Mathf.Max(0, target.DefensePoints - 1);

            if (!hit)
                return;

            var damageScale = GetCombatDamageScale();
            var strengthDamage = RandomRange(0f, 2f) * 0.05f * damageScale;
            var organizationDamage = RandomRange(0f, 4f) * 0.053f * damageScale;

            target.Division.Strength = Mathf.Max(0f, target.Division.Strength - strengthDamage);
            target.Division.Organization = Mathf.Max(0f, target.Division.Organization - organizationDamage);
        }

        private float GetCombatDamageScale()
        {
            var tickMinutes = gameManager.SimulationSettings?.SimulationTickMinutes
                              ?? SimulationSettings.DefaultSimulationTickMinutes;
            return tickMinutes / 60f;
        }

        private float RandomRange(float minInclusive, float maxExclusive)
        {
            return minInclusive + ((float)random.NextDouble() * (maxExclusive - minInclusive));
        }

        private void ResolveBrokenAttackers(GroundCombat combat)
        {
            foreach (var divisionId in combat.AttackerDivisionIds.ToList())
            {
                if (!gameManager.divisionSystem.TryGetDivision(divisionId, out var division))
                    continue;

                if (IsCombatReady(division))
                    continue;

                division.CurrentOrder = new HoldGroundOrder(
                    GroundOrderAssignmentSource.System,
                    "Attack halted after combat losses");
            }
        }

        private void ResolveBrokenDefenders(GroundCombat combat)
        {
            foreach (var division in gameManager.divisionSystem.GetDivisionsOnTile(combat.DefendingTileId).ToList())
            {
                if (division == null || GroundSystemUtility.IsRetreating(division))
                    continue;

                if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var alliance)
                    || alliance != combat.DefendingAlliance)
                    continue;

                if (IsCombatReady(division))
                    continue;

                groundOperationsSystem.TryAssignRetreat(
                    division,
                    combat.DefendingTileId,
                    "Retreating after combat losses");
            }
        }

        private void EndCombatIfDecided(
            GroundCombat combat,
            IReadOnlyList<Combatant> attackers,
            IReadOnlyList<Combatant> defenders)
        {
            if (attackers.Count == 0)
            {
                RemoveCombat(combat.DefendingTileId);
                return;
            }

            if (defenders.Count > 0)
                return;

            HaltSupportAttackers(combat);
            RemoveCombat(combat.DefendingTileId);
        }

        private void HaltSupportAttackers(GroundCombat combat)
        {
            foreach (var divisionId in combat.AttackerDivisionIds.ToList())
            {
                if (!gameManager.divisionSystem.TryGetDivision(divisionId, out var division))
                    continue;

                if (division.CurrentOrder is not SupportAttackGroundOrder supportAttackOrder
                    || supportAttackOrder.TargetTileId != combat.DefendingTileId)
                    continue;

                division.CurrentOrder = new HoldGroundOrder(
                    GroundOrderAssignmentSource.System,
                    "Support attack ended after defenders broke");
            }
        }

        private static bool IsCombatReady(Division division)
        {
            return division.Strength >= 1f
                   && division.Organization >= 1f
                   && !GroundSystemUtility.IsRetreating(division);
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
            {
                if (TryGetCombat(tileId, out var combat))
                    HaltSupportAttackers(combat);

                RemoveCombat(tileId);
            }
        }

        private static bool TryGetOrderTargetTileId(Division division, out UnityEngine.Vector3Int targetTileId)
        {
            switch (division.CurrentOrder)
            {
                case MoveGroundOrder attackOrder:
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

        public bool IsDivisionEngagedInCombat(Guid divisionId)
        {
            return IsDivisionAttackingInCombat(divisionId)
                   || IsDivisionDefendingInCombat(divisionId);
        }

        public bool IsDivisionAttackingInCombat(Guid divisionId)
        {
            EnsureIndex();
            return (Combats ?? Enumerable.Empty<GroundCombat>())
                .Any(combat => combat != null
                               && combat.AttackerDivisionIds != null
                               && combat.AttackerDivisionIds.Contains(divisionId));
        }

        public bool IsDivisionDefendingInCombat(Guid divisionId)
        {
            EnsureIndex();
            return (Combats ?? Enumerable.Empty<GroundCombat>())
                .Any(combat => combat != null
                               && combat.DefenderDivisionIds != null
                               && combat.DefenderDivisionIds.Contains(divisionId));
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
                .GroupBy(combat => combat.DefendingTileId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        private void EnsureIndex()
        {
            if (combatByDefendingTileId == null)
                RebuildIndex();
        }

        private sealed class Combatant
        {
            public readonly Division Division;
            public int DefensePoints;

            public Combatant(Division division)
            {
                Division = division;
            }

            public float StrengthPercent => Division.MaxStrength <= 0
                ? 0f
                : Mathf.Clamp01(Division.Strength / Division.MaxStrength);
        }
    }
}
