using System;
using Models.Module;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class Division
    {
        public Guid DivisionId;
        public Guid DivisionTemplateId;
        public Guid CountryId;
        public Vector3Int TileId;
        public string Name = string.Empty;
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
        public float Organization { get; set; }
        public float Strength { get; set; }

        [SerializeReference] public GroundOrder CurrentOrder = new HoldGroundOrder();

        public Division()
        {
        }

        public Division(DivisionStartingCondition startingCondition, DivisionCombatStats fullStrengthStats)
        {
            if (startingCondition == null)
                throw new ArgumentNullException(nameof(startingCondition));

            if (fullStrengthStats == null)
                throw new ArgumentNullException(nameof(fullStrengthStats));

            DivisionId = startingCondition.DivisionId;
            DivisionTemplateId = startingCondition.DivisionTemplateId;
            CountryId = startingCondition.CountryId;
            TileId = startingCondition.TileId;
            Name = startingCondition.Name ?? string.Empty;
            MaxStrength = fullStrengthStats.MaxStrength;
            MaxOrganization = fullStrengthStats.MaxOrganization;
            Recovery = fullStrengthStats.Recovery;
            SoftAttack = fullStrengthStats.SoftAttack;
            HardAttack = fullStrengthStats.HardAttack;
            Defense = fullStrengthStats.Defense;
            Toughness = fullStrengthStats.Toughness;
            Softness = fullStrengthStats.Softness;
            Speed = fullStrengthStats.Speed;
            CombatWidth = fullStrengthStats.CombatWidth;
            SupplyConsumption = fullStrengthStats.SupplyConsumption;
            FuelConsumption = fullStrengthStats.FuelConsumption;
            Organization = MaxOrganization;
            Strength = MaxStrength;
            CurrentOrder = new HoldGroundOrder(
                GroundOrderAssignmentSource.System,
                "Initial deployment");
        }
    }
}
