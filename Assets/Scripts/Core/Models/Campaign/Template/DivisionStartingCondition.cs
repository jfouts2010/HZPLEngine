using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class DivisionStartingCondition
    {
        public Guid DivisionId = Guid.NewGuid();
        public Guid DivisionTemplateId;
        public Guid CountryId;
        public Vector3Int TileId;
        public string Name = string.Empty;
    }
}
