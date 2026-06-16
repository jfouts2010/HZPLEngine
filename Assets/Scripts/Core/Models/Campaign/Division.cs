using System;
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

        public Division()
        {
        }

        public Division(DivisionStartingCondition startingCondition, float speed)
        {
            if (startingCondition == null)
                throw new ArgumentNullException(nameof(startingCondition));

            DivisionId = startingCondition.DivisionId;
            DivisionTemplateId = startingCondition.DivisionTemplateId;
            CountryId = startingCondition.CountryId;
            TileId = startingCondition.TileId;
            Name = startingCondition.Name ?? string.Empty;
            Speed = speed;
        }
    }
}
