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
        public float Speed { get; private set; }
        public float SupplyConsumption { get; private set; }
        public float FuelConsumption { get; private set; }
        public float Organization { get; set; }
        public float Strength { get; set; }
        public float Recovery { get; private set; }

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
            Speed = fullStrengthStats.Speed;
            Organization = fullStrengthStats.MaxOrganization;
            Strength = fullStrengthStats.MaxStrength;
            SupplyConsumption = fullStrengthStats.SupplyConsumption;
            FuelConsumption = fullStrengthStats.FuelConsumption;
            Recovery = fullStrengthStats.Recovery;
            CurrentOrder = new HoldGroundOrder(
                GroundOrderAssignmentSource.System,
                "Initial deployment");
        }
    }
}