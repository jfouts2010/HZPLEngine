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

            if (!gameManager.tileSystem.TryGetLand(defendingTileId, out var landTileData))
                return false;

            var defenders = gameManager.divisionSystem
                .GetDivisionsOnTile(defendingTileId)
                .Where(division => GroundTacticalCombatRules.IsCombatReady(division)
                                   && GroundSystemUtility.TryGetDivisionAlliance(
                                       gameManager,
                                       division,
                                       out var alliance)
                                   && alliance == landTileData.Controller)
                .ToList();

            estimate = Estimate(
                attackerDivisions
                .Where(GroundTacticalCombatRules.IsCombatReady)
                .ToList(),
                defenders,
                terrain,
                assaultIntent);
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
            foreach (var divisionId in attackerDivisionIds)
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

        public static bool TryEstimateTileAssault(
            GameManager gameManager,
            IEnumerable<Guid> attackerDivisionIds,
            Vector3Int defendingTileId,
            IReadOnlyList<DivisionIntelligenceReport> defenderReports,
            GroundCombatAssaultIntent assaultIntent,
            out GroundCombatEstimate estimate)
        {
            estimate = default;
            if (gameManager == null || defenderReports == null)
                return false;

            if (!TryGetDefendingTerrain(gameManager, defendingTileId, out var terrain))
                return false;

            var attackers = new List<EstimatedCombatant>();
            foreach (var divisionId in attackerDivisionIds)
            {
                if (gameManager.divisionSystem.TryGetDivision(
                        divisionId,
                        out var division)
                    && GroundTacticalCombatRules.IsCombatReady(division))
                {
                    attackers.Add(new EstimatedCombatant(division));
                }
            }

            var defenders = defenderReports
                .Where(report => report != null && report.IsCombatReady)
                .Select(report => new EstimatedCombatant(report))
                .ToList();
            estimate = EstimateCombatants(
                attackers,
                defenders,
                terrain,
                assaultIntent,
                1f);
            return true;
        }

        public static GroundCombatEstimate Estimate(
            IReadOnlyList<Division> attackerDivisions,
            IReadOnlyList<Division> defenderDivisions,
            TileTerrain terrain,
            GroundCombatAssaultIntent assaultIntent = GroundCombatAssaultIntent.Capture)
        {
            return EstimateCombatants(
                attackerDivisions
                    .Where(GroundTacticalCombatRules.IsCombatReady)
                    .Select(division => new EstimatedCombatant(division))
                    .ToList(),
                defenderDivisions
                    .Where(GroundTacticalCombatRules.IsCombatReady)
                    .Select(division => new EstimatedCombatant(division))
                    .ToList(),
                terrain,
                assaultIntent,
                1f);
        }

        private static GroundCombatEstimate EstimateCombatants(
            IReadOnlyList<EstimatedCombatant> attackerDivisions,
            IReadOnlyList<EstimatedCombatant> defenderDivisions,
            TileTerrain terrain,
            GroundCombatAssaultIntent assaultIntent,
            float damageScale)
        {
            var attackers = attackerDivisions
                .Where(GroundTacticalCombatRules.IsCombatReady)
                .Select(combatant => new EstimatedCombatant(combatant))
                .ToList();
            var defenders = defenderDivisions
                .Where(GroundTacticalCombatRules.IsCombatReady)
                .Select(combatant => new EstimatedCombatant(combatant))
                .ToList();

            var attackerSide = attackers
                .Select(combatant => new EstimatedCombatant(combatant))
                .ToList();
            var defenderSide = defenders
                .Select(combatant => new EstimatedCombatant(combatant))
                .ToList();
            var combatWidth = GroundTacticalCombatRules.GetCombatWidth(terrain, attackerSide);
            var attackerFrontLine = GroundTacticalCombatRules.AssignFrontLine(attackerSide, combatWidth, true);
            var defenderFrontLine = GroundTacticalCombatRules.AssignFrontLine(defenderSide, combatWidth, false);

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
            if (!GroundTacticalCombatRules.IsCombatReady(division))
                return 0f;

            return CalculateCombatPower(new EstimatedCombatant(division));
        }

        public static float CalculateDivisionCombatPower(
            DivisionIntelligenceReport report)
        {
            if (report == null || !report.IsCombatReady)
                return 0f;

            return CalculateCombatPower(new EstimatedCombatant(report));
        }

        private static float CalculateCombatPower(
            IGroundTacticalCombatant combatant)
        {
            var strengthPercent =
                GroundTacticalCombatRules.GetStrengthPercent(combatant);
            var genericSoftTargetShots =
                combatant.SoftAttack * 0.5f + combatant.HardAttack * 0.5f;
            var protectedHitChance = 1f - GroundTacticalCombatRules.ProtectedShotMissChance;
            var expectedDamage = Mathf.Max(0f, genericSoftTargetShots) * strengthPercent * protectedHitChance;
            var widthEfficiency = combatant.CombatWidth <= 0
                ? 1f
                : 20f / combatant.CombatWidth;
            return expectedDamage * Mathf.Clamp(widthEfficiency, 0.25f, 2f);
        }

        private static bool TryGetDefendingTerrain(GameManager gameManager, Vector3Int tileId, out TileTerrain terrain)
        {
            terrain = TileTerrain.Plains;
            if (!gameManager.tileSystem.TryGet(tileId, out var tile))
                return false;

            terrain = tile.Terrain;
            return true;
        }

        private static float EstimateVictoryLikelihood(
            IReadOnlyList<EstimatedCombatant> attackers,
            IReadOnlyList<EstimatedCombatant> defenders,
            TileTerrain terrain,
            float damageScale)
        {
            if (attackers.Count == 0)
                return 0f;

            if (defenders.Count == 0)
                return 1f;

            var attackerSide = attackers
                .Select(combatant => new EstimatedCombatant(combatant))
                .ToList();
            var defenderSide = defenders
                .Select(combatant => new EstimatedCombatant(combatant))
                .ToList();
            var combatWidth = GroundTacticalCombatRules.GetCombatWidth(terrain, attackerSide);
            var attackerBreakRound = EstimateSimulationRounds + 1f;
            var defenderBreakRound = EstimateSimulationRounds + 1f;

            for (var round = 1; round <= EstimateSimulationRounds; round++)
            {
                var attackerFront = GroundTacticalCombatRules.AssignFrontLine(attackerSide, combatWidth, true);
                var defenderFront = GroundTacticalCombatRules.AssignFrontLine(defenderSide, combatWidth, false);

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

                attackerSide = attackerSide.Where(GroundTacticalCombatRules.IsCombatReady).ToList();
                defenderSide = defenderSide.Where(GroundTacticalCombatRules.IsCombatReady).ToList();

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
                var totalTargetWeight = GroundTacticalCombatRules.GetTotalTargetWeight(shooter, targets);
                if (totalTargetWeight <= 0f)
                    continue;

                foreach (var target in targets)
                {
                    var targetShare = GroundTacticalCombatRules.GetTargetWeight(shooter, target) / totalTargetWeight;
                    var shots = GroundTacticalCombatRules.CalculateShotCount(shooter, target, terrain, shootersAreAttackers) * targetShare;
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
                var totalTargetWeight = GroundTacticalCombatRules.GetTotalTargetWeight(shooter, targets);
                if (totalTargetWeight <= 0f)
                    continue;

                foreach (var target in targets)
                {
                    var targetShare = GroundTacticalCombatRules.GetTargetWeight(shooter, target) / totalTargetWeight;
                    var shots = GroundTacticalCombatRules.CalculateShotCount(shooter, target, terrain, shootersAreAttackers) * targetShare;
                    total += GroundTacticalCombatRules.CalculateExpectedHits(target.DefensePoints, shots)
                             * AverageOrganizationDamagePerHit
                             * damageScale;
                }
            }

            return total;
        }

        private static void ApplyExpectedShots(EstimatedCombatant target, float shots, float damageScale)
        {
            var expectedHits = GroundTacticalCombatRules.CalculateExpectedHits(target.DefensePoints, shots);
            target.DefensePoints = Mathf.Max(0f, target.DefensePoints - shots);
            target.Strength = Mathf.Max(
                0f,
                target.Strength - (expectedHits * AverageStrengthDamagePerHit * damageScale));
            target.Organization = Mathf.Max(
                0f,
                target.Organization - (expectedHits * AverageOrganizationDamagePerHit * damageScale));
        }

        private sealed class EstimatedCombatant : IGroundTacticalCombatant
        {
            public float Strength { get; set; }
            public float Organization { get; set; }
            public float DefensePoints { get; set; }
            public float SoftAttack { get; }
            public float HardAttack { get; }
            public float Softness { get; }
            public Vector3Int TileId { get; }
            public int MaxStrength { get; }
            public int Defense { get; }
            public int Toughness { get; }
            public int CombatWidth { get; }

            public EstimatedCombatant(Division division)
            {
                TileId = division.TileId;
                MaxStrength = division.MaxStrength;
                Strength = division.Strength;
                Organization = division.Organization;
                SoftAttack = division.SoftAttack;
                HardAttack = division.HardAttack;
                Defense = division.Defense;
                Toughness = division.Toughness;
                Softness = division.Softness;
                CombatWidth = division.CombatWidth;
            }

            public EstimatedCombatant(DivisionIntelligenceReport report)
            {
                TileId = report.TileId;
                MaxStrength = report.MaxStrength;
                Strength = report.Strength;
                Organization = report.Organization;
                SoftAttack = report.SoftAttack;
                HardAttack = report.HardAttack;
                Defense = report.Defense;
                Toughness = report.Toughness;
                Softness = report.Softness;
                CombatWidth = report.CombatWidth;
            }

            public EstimatedCombatant(EstimatedCombatant combatant)
            {
                TileId = combatant.TileId;
                MaxStrength = combatant.MaxStrength;
                Strength = combatant.Strength;
                Organization = combatant.Organization;
                SoftAttack = combatant.SoftAttack;
                HardAttack = combatant.HardAttack;
                Defense = combatant.Defense;
                Toughness = combatant.Toughness;
                Softness = combatant.Softness;
                CombatWidth = combatant.CombatWidth;
                DefensePoints = combatant.DefensePoints;
            }
        }
    }
}
