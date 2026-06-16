using System;

namespace Models.Module
{
    public sealed class BattalionDefinition
    {
        public Guid BattalionDefinitionId { get; }
        public Guid CountryId { get; }
        public string Name { get; }
        public int Strength { get; private set; }
        public int Organization { get; private set; }
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

        public BattalionDefinition(
            Guid battalionDefinitionId,
            Guid countryId,
            string name,
            int strength,
            int organization,
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
            if (battalionDefinitionId == Guid.Empty)
                throw new ArgumentException("Battalion definition id is required.", nameof(battalionDefinitionId));

            if (countryId == Guid.Empty)
                throw new ArgumentException("Country id is required.", nameof(countryId));

            BattalionDefinitionId = battalionDefinitionId;
            CountryId = countryId;
            Name = string.IsNullOrWhiteSpace(name) ? battalionDefinitionId.ToString() : name.Trim();
            Strength = strength;
            Organization = organization;
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
    }
}
