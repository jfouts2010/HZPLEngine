using System;
using System.Collections.Generic;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class GroundCombat
    {
        public Vector3Int DefendingTileId;
        public Alliance AttackingAlliance;
        public Alliance DefendingAlliance;
        public List<Guid> AttackerDivisionIds = new List<Guid>();
        public List<Guid> DefenderDivisionIds = new List<Guid>();

        public GroundCombat()
        {
        }

        public GroundCombat(Vector3Int defendingTileId, Alliance attackingAlliance, Alliance defendingAlliance)
        {
            DefendingTileId = defendingTileId;
            AttackingAlliance = attackingAlliance;
            DefendingAlliance = defendingAlliance;
        }
    }
}
