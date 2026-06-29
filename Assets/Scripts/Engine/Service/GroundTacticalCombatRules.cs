using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models.Ground
{
    public interface IGroundTacticalCombatant
    {
        Vector3Int TileId { get; }
        float Strength { get; }
        int MaxStrength { get; }
        float Organization { get; }
        float SoftAttack { get; }
        float HardAttack { get; }
        int Defense { get; }
        int Toughness { get; }
        float Softness { get; }
        int CombatWidth { get; }
        float DefensePoints { get; set; }
    }

    public static class GroundTacticalCombatRules
    {
        public const float ProtectedShotMissChance = 0.9f;
        public const float ExposedShotMissChance = 0.6f;

        private const float MultiOriginWidthMultiplier = 1.5f;
        private const float MinimumTargetWeight = 0.01f;

        public static bool IsCombatReady(Division division)
        {
            return division != null
                   && division.Strength >= 1f
                   && division.Organization >= 1f
                   && !GroundSystemUtility.IsRetreating(division);
        }

        public static bool IsCombatReady(IGroundTacticalCombatant combatant)
        {
            return combatant != null
                   && combatant.Strength >= 1f
                   && combatant.Organization >= 1f;
        }

        public static float GetStrengthPercent(IGroundTacticalCombatant combatant)
        {
            return combatant.MaxStrength <= 0
                ? 0f
                : Mathf.Clamp01(combatant.Strength / combatant.MaxStrength);
        }

        public static int GetCombatWidth<TCombatant>(
            TileTerrain terrain,
            IReadOnlyCollection<TCombatant> attackers)
            where TCombatant : IGroundTacticalCombatant
        {
            var width = GetBaseCombatWidth(terrain);
            if (attackers.Select(attacker => attacker.TileId).Distinct().Count() > 1)
                width = Mathf.FloorToInt(width * MultiOriginWidthMultiplier);

            return Mathf.Max(1, width);
        }

        public static int GetBaseCombatWidth(TileTerrain terrain)
        {
            return terrain switch
            {
                TileTerrain.Mountain => 50,
                TileTerrain.Hills => 70,
                TileTerrain.Tundra => 70,
                _ => 80
            };
        }

        public static float GetAttackerFireMultiplier(TileTerrain terrain)
        {
            return terrain switch
            {
                TileTerrain.Mountain => 0.6f,
                TileTerrain.Hills => 0.8f,
                _ => 1f
            };
        }

        public static List<TCombatant> AssignFrontLine<TCombatant>(
            IReadOnlyList<TCombatant> combatants,
            int combatWidth,
            bool useToughness)
            where TCombatant : IGroundTacticalCombatant
        {
            var frontLine = new List<TCombatant>();
            var usedWidth = 0;

            foreach (var combatant in combatants)
            {
                var width = Mathf.Max(0, combatant.CombatWidth);
                if (usedWidth + width > combatWidth)
                    continue;

                ResetDefensePoints(combatant, useToughness);
                frontLine.Add(combatant);
                usedWidth += width;
            }

            if (frontLine.Count != 0 || combatants.Count == 0)
                return frontLine;

            ResetDefensePoints(combatants[0], useToughness);
            frontLine.Add(combatants[0]);
            return frontLine;
        }

        public static int CalculateShotCount(
            IGroundTacticalCombatant shooter,
            IGroundTacticalCombatant target,
            TileTerrain terrain,
            bool shooterIsAttacker)
        {
            var targetSoftness = Mathf.Clamp01(target.Softness);
            var shots = shooter.SoftAttack * targetSoftness
                        + shooter.HardAttack * (1f - targetSoftness);
            shots *= GetStrengthPercent(shooter);

            if (shooterIsAttacker)
                shots *= GetAttackerFireMultiplier(terrain);

            return Mathf.Max(0, Mathf.FloorToInt(shots));
        }

        public static float GetTotalTargetWeight<TCombatant>(
            IGroundTacticalCombatant shooter,
            IReadOnlyList<TCombatant> targets)
            where TCombatant : IGroundTacticalCombatant
        {
            var total = 0f;
            foreach (var target in targets)
                total += GetTargetWeight(shooter, target);

            return total;
        }

        public static float GetTargetWeight(
            IGroundTacticalCombatant shooter,
            IGroundTacticalCombatant target)
        {
            var preferSoftTargets = shooter.SoftAttack >= shooter.HardAttack;
            var weight = preferSoftTargets
                ? Mathf.Clamp01(target.Softness)
                : 1f - Mathf.Clamp01(target.Softness);
            return Mathf.Max(MinimumTargetWeight, weight);
        }

        public static float CalculateExpectedHits(float defensePoints, float shots)
        {
            if (shots <= 0f)
                return 0f;

            var protectedShots = Mathf.Min(Mathf.Max(0f, defensePoints), shots);
            var exposedShots = shots - protectedShots;
            return protectedShots * (1f - ProtectedShotMissChance)
                   + exposedShots * (1f - ExposedShotMissChance);
        }

        private static void ResetDefensePoints(
            IGroundTacticalCombatant combatant,
            bool useToughness)
        {
            combatant.DefensePoints = useToughness
                ? combatant.Toughness
                : combatant.Defense;
        }
    }
}
