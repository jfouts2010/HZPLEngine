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
        public float Speed;
        public float Organization;
        public float Strength;
        
        [SerializeReference]
        public GroundOrder CurrentOrder = new HoldGroundOrder();

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

            CurrentOrder = new HoldGroundOrder(
                GroundOrderAssignmentSource.System,
                "Initial deployment");
        }
    }
}
