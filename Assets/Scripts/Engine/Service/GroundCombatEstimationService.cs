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
        private const float LikelyVictoryThreshold = 0.55f;
        private const float CombatReadinessOrganizationWeight = 0.55f;
        private const float DefenderToughnessPowerWeight = 0.03f;

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
            var attackers = (attackerDivisions ?? Array.Empty<Division>())
                .Where(IsCombatReady)
                .ToList();
            var defenders = (defenderDivisions ?? Array.Empty<Division>())
                .Where(IsCombatReady)
                .ToList();

            var combatWidth = GetCombatWidth(terrain, attackers);
            var attackerFrontLine = AssignFrontLine(attackers, combatWidth);
            var defenderFrontLine = AssignFrontLine(defenders, combatWidth);

            var averageDefenderSoftness = GetAverageSoftness(defenderFrontLine);
            var averageAttackerSoftness = GetAverageSoftness(attackerFrontLine);
            var attackerFireMultiplier = GetAttackerFireMultiplier(terrain);

            var attackerPower = SumFrontLineAttackPower(
                attackerFrontLine,
                averageDefenderSoftness,
                attackerFireMultiplier);
            var defenderPower = SumFrontLineAttackPower(
                defenderFrontLine,
                averageAttackerSoftness,
                1f);
            defenderPower *= 1f + (GetAverageToughness(defenderFrontLine) * DefenderToughnessPowerWeight);

            var victoryLikelihood = CalculateVictoryLikelihood(attackerPower, defenderPower);
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

            return CalculateDivisionAttackPower(
                division,
                0.5f,
                1f);
        }

        private static float CalculateVictoryLikelihood(float attackerPower, float defenderPower)
        {
            if (attackerPower <= 0f)
                return 0f;

            if (defenderPower <= 0f)
                return 1f;

            return Mathf.Clamp01(attackerPower / (attackerPower + defenderPower));
        }

        private static float SumFrontLineAttackPower(
            IReadOnlyList<Division> frontLine,
            float targetSoftness,
            float fireMultiplier)
        {
            var total = 0f;
            foreach (var division in frontLine)
                total += CalculateDivisionAttackPower(division, targetSoftness, fireMultiplier);

            return total;
        }

        private static float CalculateDivisionAttackPower(
            Division division,
            float targetSoftness,
            float fireMultiplier)
        {
            var strengthPercent = division.MaxStrength <= 0
                ? 0f
                : Mathf.Clamp01(division.Strength / division.MaxStrength);
            var organizationPercent = division.MaxOrganization <= 0
                ? 0f
                : Mathf.Clamp01(division.Organization / division.MaxOrganization);
            var combatReadiness = Mathf.Lerp(
                strengthPercent,
                organizationPercent,
                CombatReadinessOrganizationWeight);
            var softness = Mathf.Clamp01(targetSoftness);
            var attack = division.SoftAttack * softness + division.HardAttack * (1f - softness);

            return Mathf.Max(0f, attack) * combatReadiness * fireMultiplier;
        }

        private static float GetAverageSoftness(IReadOnlyList<Division> divisions)
        {
            if (divisions == null || divisions.Count == 0)
                return 0.5f;

            var total = 0f;
            foreach (var division in divisions)
                total += Mathf.Clamp01(division.Softness);

            return total / divisions.Count;
        }

        private static float GetAverageToughness(IReadOnlyList<Division> divisions)
        {
            if (divisions == null || divisions.Count == 0)
                return 0f;

            var total = 0f;
            foreach (var division in divisions)
                total += Mathf.Max(0, division.Toughness);

            return total / divisions.Count;
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

        private static List<Division> AssignFrontLine(IReadOnlyList<Division> combatants, int combatWidth)
        {
            var frontLine = new List<Division>();
            var usedWidth = 0;

            foreach (var combatant in combatants)
            {
                var width = Mathf.Max(0, combatant.CombatWidth);
                if (usedWidth + width > combatWidth)
                    continue;

                frontLine.Add(combatant);
                usedWidth += width;
            }

            if (frontLine.Count != 0 || combatants.Count == 0)
                return frontLine;

            frontLine.Add(combatants[0]);
            return frontLine;
        }

        private static bool IsCombatReady(Division division)
        {
            return division != null
                   && division.Strength >= 1f
                   && division.Organization >= 1f
                   && !GroundSystemUtility.IsRetreating(division);
        }
    }
}
