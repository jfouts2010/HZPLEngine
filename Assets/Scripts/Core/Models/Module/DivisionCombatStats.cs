using System;
using System.Collections.Generic;

namespace Models.Module
{
    public sealed class DivisionCombatStats
    {
        public int MaxStrength { get; private set; }
        public int MaxOrganization { get; private set; }
        public float Recovery { get; private set; }
        public float SoftAttack { get; private set; }
        public float HardAttack { get; private set; }
        public int Defense { get; private set; }
        public int Toughness { get; private set; }
        public float Softness { get; private set; }
        public float Speed { get; private set; }
        public int CombatWidth { get; private set; }
        public float SupplyConsumption { get; private set; }
        public float FuelConsumption { get; private set; }

        public DivisionCombatStats(
            int maxStrength,
            int maxOrganization,
            float recovery,
            float softAttack,
            float hardAttack,
            int defense,
            int toughness,
            float softness,
            float speed,
            int combatWidth,
            float supplyConsumption,
            float fuelConsumption)
        {
            MaxStrength = maxStrength;
            MaxOrganization = maxOrganization;
            Recovery = recovery;
            SoftAttack = softAttack;
            HardAttack = hardAttack;
            Defense = defense;
            Toughness = toughness;
            Softness = softness;
            Speed = speed;
            CombatWidth = combatWidth;
            SupplyConsumption = supplyConsumption;
            FuelConsumption = fuelConsumption;
        }

        public static DivisionCombatStats Calculate(IEnumerable<DivisionCombatStatsBattalion> battalions)
        {
            if (battalions == null)
                throw new ArgumentNullException(nameof(battalions));

            var strength = 0;
            var organization = 0;
            var recovery = 0f;
            var softAttack = 0f;
            var hardAttack = 0f;
            var defense = 0;
            var toughness = 0;
            var softnessStrengthTotal = 0f;
            var speed = 0f;
            var hasSpeed = false;
            var combatWidth = 0;
            var supplyConsumption = 0f;
            var fuelConsumption = 0f;

            foreach (var combatBattalion in battalions)
            {
                if (combatBattalion == null || combatBattalion.Count <= 0)
                    continue;

                var battalion = combatBattalion.BattalionDefinition;
                if (battalion == null)
                    throw new ArgumentException("Battalion definition is required.", nameof(battalions));

                var count = combatBattalion.Count;
                var battalionStrength = battalion.Strength * count;
                strength += battalionStrength;
                organization += battalion.Organization * count;
                recovery += battalion.Recovery * count;
                softAttack += battalion.SoftAttack * count;
                hardAttack += battalion.HardAttack * count;
                defense += battalion.Defense * count;
                toughness += battalion.Toughness * count;
                softnessStrengthTotal += battalion.Softness * battalionStrength;
                combatWidth += battalion.CombatWidth * count;
                supplyConsumption += battalion.SupplyConsumption * count;
                fuelConsumption += battalion.FuelConsumption * count;

                if (!hasSpeed || battalion.Speed < speed)
                {
                    speed = battalion.Speed;
                    hasSpeed = true;
                }
            }

            var softness = strength <= 0 ? 0f : softnessStrengthTotal / strength;

            return new DivisionCombatStats(
                strength,
                organization,
                recovery,
                softAttack,
                hardAttack,
                defense,
                toughness,
                softness,
                speed,
                combatWidth,
                supplyConsumption,
                fuelConsumption);
        }
    }
}
