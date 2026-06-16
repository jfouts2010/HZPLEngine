using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class DivisionCollection
    {
        [SerializeReference]
        public List<Division> Divisions = new List<Division>();

        private Dictionary<Vector3Int, List<Division>> divisionsByTileId;

        public List<Division> GetDivisionsOnTile(Vector3Int tileId)
        {
            EnsureIndex();
            return divisionsByTileId.TryGetValue(tileId, out var divisions)
                ? divisions
                : new List<Division>();
        }

        public void RebuildIndex()
        {
            divisionsByTileId = (Divisions ?? new List<Division>())
                .Where(division => division != null)
                .GroupBy(division => division.TileId)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        private void EnsureIndex()
        {
            if (divisionsByTileId == null)
                RebuildIndex();
        }
    }
}
