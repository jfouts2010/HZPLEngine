using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models.Ground
{
    public enum GroundCombatAssaultIntent
    {
        Capture,
        SupportOnly
    }

    public readonly struct GroundCombatEstimate
    {
        public float AttackerFrontLinePower { get; }
        public float DefenderFrontLinePower { get; }
        public int AttackerFrontLineCount { get; }
        public int DefenderFrontLineCount { get; }
        public int AttackerReserveCount { get; }
        public int DefenderReserveCount { get; }
        public float VictoryLikelihood { get; }
        public bool LikelyVictory { get; }
        public bool CanCaptureTile { get; }

        public GroundCombatEstimate(
            float attackerFrontLinePower,
            float defenderFrontLinePower,
            int attackerFrontLineCount,
            int defenderFrontLineCount,
            int attackerReserveCount,
            int defenderReserveCount,
            float victoryLikelihood,
            bool likelyVictory,
            bool canCaptureTile)
        {
            AttackerFrontLinePower = attackerFrontLinePower;
            DefenderFrontLinePower = defenderFrontLinePower;
            AttackerFrontLineCount = attackerFrontLineCount;
            DefenderFrontLineCount = defenderFrontLineCount;
            AttackerReserveCount = attackerReserveCount;
            DefenderReserveCount = defenderReserveCount;
            VictoryLikelihood = victoryLikelihood;
            LikelyVictory = likelyVictory;
            CanCaptureTile = canCaptureTile;
        }
    }

    public static class GroundCombatEstimationService
    {
        private const float MultiOriginWidthMultiplier = 1.5f;
        private const float ProtectedShotMissChance = 0.9f;
        private const float ExposedShotMissChance = 0.6f;
        private const float MinimumTargetWeight = 0.01f;
        private const float LikelyVictoryThreshold = 0.55f;
        private const float AverageStrengthDamagePerHit = 0.05f;
        private const float AverageOrganizationDamagePerHit = 0.106f;
        private const float BreakTimeAdvantageScale = 4f;
        private const int EstimateSimulationRounds = 24;

        public static bool TryEstimateTileAssault(
            GameManager gameManager,
            IEnumerable<Division> attackerDivisions,
            Vector3Int defendingTileId,
            GroundCombatAssaultIntent assaultIntent,
            out GroundCombatEstimate estimate)
        {
            estimate = default;
            if (gameManager == null)
                return false;

            if (!TryGetDefendingTerrain(gameManager, defendingTileId, out var terrain))
                return false;

            if (!GroundSystemUtility.TryGetLandTileData(gameManager, defendingTileId, out var landTileData))
                return false;

            var defenders = gameManager.divisionSystem
                .GetDivisionsOnTile(defendingTileId)
                .Where(division => IsCombatReady(division)
                                   && GroundSystemUtility.TryGetDivisionAlliance(
                                       gameManager,
                                       division,
                                       out var alliance)
                                   && alliance == landTileData.Controller)
                .ToList();

            estimate = Estimate(
                (attackerDivisions ?? Enumerable.Empty<Division>()).Where(IsCombatReady).ToList(),
                defenders,
                terrain,
                assaultIntent,
                GetCombatDamageScale(gameManager));
            return true;
        }

        public static bool TryEstimateTileAssault(
            GameManager gameManager,
            IEnumerable<Guid> attackerDivisionIds,
            Vector3Int defendingTileId,
            GroundCombatAssaultIntent assaultIntent,
            out GroundCombatEstimate estimate)
        {
            estimate = default;
            if (gameManager == null)
                return false;

            var attackers = new List<Division>();
            foreach (var divisionId in attackerDivisionIds ?? Enumerable.Empty<Guid>())
            {
                if (gameManager.divisionSystem.TryGetDivision(divisionId, out var division) && division != null)
                    attackers.Add(division);
            }

            return TryEstimateTileAssault(
                gameManager,
                attackers,
                defendingTileId,
                assaultIntent,
                out estimate);
        }

        public static GroundCombatEstimate Estimate(
            IReadOnlyList<Division> attackerDivisions,
            IReadOnlyList<Division> defenderDivisions,
            TileTerrain terrain,
            GroundCombatAssaultIntent assaultIntent = GroundCombatAssaultIntent.Capture)
        {
            return Estimate(
                attackerDivisions,
                defenderDivisions,
                terrain,
                assaultIntent,
                GetDefaultCombatDamageScale());
        }

        private static GroundCombatEstimate Estimate(
            IReadOnlyList<Division> attackerDivisions,
            IReadOnlyList<Division> defenderDivisions,
            TileTerrain terrain,
            GroundCombatAssaultIntent assaultIntent,
            float damageScale)
        {
            var attackers = (attackerDivisions ?? Array.Empty<Division>())
                .Where(IsCombatReady)
                .ToList();
            var defenders = (defenderDivisions ?? Array.Empty<Division>())
                .Where(IsCombatReady)
                .ToList();

            var combatWidth = GetCombatWidth(terrain, attackers);
            var attackerFrontLine = AssignFrontLine(attackers, combatWidth, true);
            var defenderFrontLine = AssignFrontLine(defenders, combatWidth, false);

            var attackerPower = SumExpectedOrganizationDamage(
                attackerFrontLine,
                defenderFrontLine,
                terrain,
                true,
                damageScale);
            var defenderPower = SumExpectedOrganizationDamage(
                defenderFrontLine,
                attackerFrontLine,
                terrain,
                false,
                damageScale);

            var victoryLikelihood = EstimateVictoryLikelihood(attackers, defenders, terrain, damageScale);
            var likelyVictory = victoryLikelihood >= LikelyVictoryThreshold;
            var canCaptureTile = likelyVictory && assaultIntent == GroundCombatAssaultIntent.Capture;

            return new GroundCombatEstimate(
                attackerPower,
                defenderPower,
                attackerFrontLine.Count,
                defenderFrontLine.Count,
                Mathf.Max(0, attackers.Count - attackerFrontLine.Count),
                Mathf.Max(0, defenders.Count - defenderFrontLine.Count),
                victoryLikelihood,
                likelyVictory,
                canCaptureTile);
        }

        public static float CalculateDivisionCombatPower(Division division)
        {
            if (!IsCombatReady(division))
                return 0f;

            var strengthPercent = GetStrengthPercent(division);
            var genericSoftTargetShots = division.SoftAttack * 0.5f + division.HardAttack * 0.5f;
            var protectedHitChance = 1f - ProtectedShotMissChance;
            var expectedDamage = Mathf.Max(0f, genericSoftTargetShots) * strengthPercent * protectedHitChance;
            var widthEfficiency = division.CombatWidth <= 0 ? 1f : 20f / division.CombatWidth;
            return expectedDamage * Mathf.Clamp(widthEfficiency, 0.25f, 2f);
        }

        private static bool TryGetDefendingTerrain(GameManager gameManager, Vector3Int tileId, out TileTerrain terrain)
        {
            terrain = TileTerrain.Plains;
            var tile = gameManager.CampaignTiles?
                .FirstOrDefault(candidate => candidate != null && candidate.Coordinates == tileId);
            if (tile == null)
                return false;

            terrain = tile.Terrain;
            return true;
        }

        private static int GetCombatWidth(TileTerrain terrain, IReadOnlyList<Division> attackers)
        {
            var width = GetBaseCombatWidth(terrain);
            if (attackers.Select(attacker => attacker.TileId).Distinct().Count() > 1)
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

        private static List<EstimatedCombatant> AssignFrontLine(
            IReadOnlyList<Division> combatants,
            int combatWidth,
            bool useToughness)
        {
            var frontLine = new List<EstimatedCombatant>();
            var usedWidth = 0;

            foreach (var division in combatants)
            {
                var width = Mathf.Max(0, division.CombatWidth);
                if (usedWidth + width > combatWidth)
                    continue;

                frontLine.Add(new EstimatedCombatant(division, useToughness));
                usedWidth += width;
            }

            if (frontLine.Count != 0 || combatants.Count == 0)
                return frontLine;

            frontLine.Add(new EstimatedCombatant(combatants[0], useToughness));
            return frontLine;
        }

        private static float EstimateVictoryLikelihood(
            IReadOnlyList<Division> attackers,
            IReadOnlyList<Division> defenders,
            TileTerrain terrain,
            float damageScale)
        {
            if (attackers.Count == 0)
                return 0f;

            if (defenders.Count == 0)
                return 1f;

            var combatWidth = GetCombatWidth(terrain, attackers);
            var attackerSide = attackers.Select(division => new EstimatedCombatant(division, true)).ToList();
            var defenderSide = defenders.Select(division => new EstimatedCombatant(division, false)).ToList();
            var attackerBreakRound = EstimateSimulationRounds + 1f;
            var defenderBreakRound = EstimateSimulationRounds + 1f;

            for (var round = 1; round <= EstimateSimulationRounds; round++)
            {
                var attackerFront = AssignFrontLine(attackerSide, combatWidth, true);
                var defenderFront = AssignFrontLine(defenderSide, combatWidth, false);

                if (attackerFront.Count == 0)
                {
                    attackerBreakRound = round;
                    break;
                }

                if (defenderFront.Count == 0)
                {
                    defenderBreakRound = round;
                    break;
                }

                ApplyExpectedFire(attackerFront, defenderFront, terrain, true, damageScale);
                ApplyExpectedFire(defenderFront, attackerFront, terrain, false, damageScale);

                attackerSide = attackerSide.Where(IsEstimatedCombatReady).ToList();
                defenderSide = defenderSide.Where(IsEstimatedCombatReady).ToList();

                if (attackerSide.Count == 0)
                {
                    attackerBreakRound = round;
                    break;
                }

                if (defenderSide.Count == 0)
                {
                    defenderBreakRound = round;
                    break;
                }
            }

            if (defenderBreakRound <= EstimateSimulationRounds && attackerBreakRound > EstimateSimulationRounds)
                return 1f;

            if (attackerBreakRound <= EstimateSimulationRounds && defenderBreakRound > EstimateSimulationRounds)
                return 0f;

            var attackerRemainingOrganization = attackerSide.Sum(combatant => Mathf.Max(0f, combatant.Organization));
            var defenderRemainingOrganization = defenderSide.Sum(combatant => Mathf.Max(0f, combatant.Organization));
            if (attackerRemainingOrganization <= 0f)
                return 0f;

            if (defenderRemainingOrganization <= 0f)
                return 1f;

            var breakTimeScore = Mathf.Clamp(
                (attackerBreakRound - defenderBreakRound) / BreakTimeAdvantageScale,
                -1f,
                1f);
            var remainingOrgScore = attackerRemainingOrganization
                                    / (attackerRemainingOrganization + defenderRemainingOrganization);

            return Mathf.Clamp01(Mathf.Lerp(remainingOrgScore, 0.5f + (breakTimeScore * 0.5f), 0.65f));
        }

        private static void ApplyExpectedFire(
            IReadOnlyList<EstimatedCombatant> shooters,
            IReadOnlyList<EstimatedCombatant> targets,
            TileTerrain terrain,
            bool shootersAreAttackers,
            float damageScale)
        {
            foreach (var shooter in shooters)
            {
                var totalTargetWeight = GetTotalTargetWeight(shooter, targets);
                if (totalTargetWeight <= 0f)
                    continue;

                foreach (var target in targets)
                {
                    var targetShare = GetTargetWeight(shooter, target) / totalTargetWeight;
                    var shots = CalculateShotCount(shooter, target, terrain, shootersAreAttackers) * targetShare;
                    ApplyExpectedShots(target, shots, damageScale);
                }
            }
        }

        private static float SumExpectedOrganizationDamage(
            IReadOnlyList<EstimatedCombatant> shooters,
            IReadOnlyList<EstimatedCombatant> targets,
            TileTerrain terrain,
            bool shootersAreAttackers,
            float damageScale)
        {
            var total = 0f;
            foreach (var shooter in shooters)
            {
                var totalTargetWeight = GetTotalTargetWeight(shooter, targets);
                if (totalTargetWeight <= 0f)
                    continue;

                foreach (var target in targets)
                {
                    var targetShare = GetTargetWeight(shooter, target) / totalTargetWeight;
                    var shots = CalculateShotCount(shooter, target, terrain, shootersAreAttackers) * targetShare;
                    total += CalculateExpectedHits(target.DefensePoints, shots)
                             * AverageOrganizationDamagePerHit
                             * damageScale;
                }
            }

            return total;
        }

        private static void ApplyExpectedShots(EstimatedCombatant target, float shots, float damageScale)
        {
            var expectedHits = CalculateExpectedHits(target.DefensePoints, shots);
            target.DefensePoints = Mathf.Max(0f, target.DefensePoints - shots);
            target.Strength = Mathf.Max(
                0f,
                target.Strength - (expectedHits * AverageStrengthDamagePerHit * damageScale));
            target.Organization = Mathf.Max(
                0f,
                target.Organization - (expectedHits * AverageOrganizationDamagePerHit * damageScale));
        }

        private static float CalculateExpectedHits(float defensePoints, float shots)
        {
            if (shots <= 0f)
                return 0f;

            var protectedShots = Mathf.Min(Mathf.Max(0f, defensePoints), shots);
            var exposedShots = shots - protectedShots;
            return protectedShots * (1f - ProtectedShotMissChance)
                   + exposedShots * (1f - ExposedShotMissChance);
        }

        private static int CalculateShotCount(
            EstimatedCombatant shooter,
            EstimatedCombatant target,
            TileTerrain terrain,
            bool shooterIsAttacker)
        {
            var targetSoftness = Mathf.Clamp01(target.Softness);
            var shots = shooter.SoftAttack * targetSoftness
                        + shooter.HardAttack * (1f - targetSoftness);
            shots *= shooter.StrengthPercent;

            if (shooterIsAttacker)
                shots *= GetAttackerFireMultiplier(terrain);

            return Mathf.Max(0, Mathf.FloorToInt(shots));
        }

        private static float GetTotalTargetWeight(
            EstimatedCombatant shooter,
            IReadOnlyList<EstimatedCombatant> targets)
        {
            var total = 0f;
            foreach (var target in targets)
                total += GetTargetWeight(shooter, target);

            return total;
        }

        private static float GetTargetWeight(EstimatedCombatant shooter, EstimatedCombatant target)
        {
            var preferSoftTargets = shooter.SoftAttack >= shooter.HardAttack;
            var weight = preferSoftTargets
                ? Mathf.Clamp01(target.Softness)
                : 1f - Mathf.Clamp01(target.Softness);
            return Mathf.Max(MinimumTargetWeight, weight);
        }

        private static List<EstimatedCombatant> AssignFrontLine(
            IReadOnlyList<EstimatedCombatant> combatants,
            int combatWidth,
            bool useToughness)
        {
            var frontLine = new List<EstimatedCombatant>();
            var usedWidth = 0;

            foreach (var combatant in combatants)
            {
                var width = Mathf.Max(0, combatant.CombatWidth);
                if (usedWidth + width > combatWidth)
                    continue;

                combatant.ResetDefensePoints(useToughness);
                frontLine.Add(combatant);
                usedWidth += width;
            }

            if (frontLine.Count != 0 || combatants.Count == 0)
                return frontLine;

            combatants[0].ResetDefensePoints(useToughness);
            frontLine.Add(combatants[0]);
            return frontLine;
        }

        private static bool IsEstimatedCombatReady(EstimatedCombatant combatant)
        {
            return combatant.Strength >= 1f && combatant.Organization >= 1f;
        }

        private static float GetStrengthPercent(Division division)
        {
            return division.MaxStrength <= 0
                ? 0f
                : Mathf.Clamp01(division.Strength / division.MaxStrength);
        }

        private static float GetCombatDamageScale(GameManager gameManager)
        {
            var tickMinutes = gameManager.SimulationSettings?.SimulationTickMinutes
                              ?? SimulationSettings.DefaultSimulationTickMinutes;
            return tickMinutes / 60f;
        }

        private static float GetDefaultCombatDamageScale()
        {
            return SimulationSettings.DefaultSimulationTickMinutes / 60f;
        }

        private static bool IsCombatReady(Division division)
        {
            return division != null
                   && division.Strength >= 1f
                   && division.Organization >= 1f
                   && !GroundSystemUtility.IsRetreating(division);
        }

        private sealed class EstimatedCombatant
        {
            private readonly int defense;
            private readonly int toughness;
            private readonly int maxStrength;

            public float Strength;
            public float Organization;
            public float DefensePoints;
            public float SoftAttack { get; }
            public float HardAttack { get; }
            public float Softness { get; }
            public int CombatWidth { get; }

            public EstimatedCombatant(Division division, bool useToughness)
            {
                maxStrength = division.MaxStrength;
                Strength = division.Strength;
                Organization = division.Organization;
                SoftAttack = division.SoftAttack;
                HardAttack = division.HardAttack;
                defense = division.Defense;
                toughness = division.Toughness;
                Softness = division.Softness;
                CombatWidth = division.CombatWidth;
                ResetDefensePoints(useToughness);
            }

            public float StrengthPercent => maxStrength <= 0
                ? 0f
                : Mathf.Clamp01(Strength / maxStrength);

            public void ResetDefensePoints(bool useToughness)
            {
                DefensePoints = useToughness ? toughness : defense;
            }
        }
    }
}
