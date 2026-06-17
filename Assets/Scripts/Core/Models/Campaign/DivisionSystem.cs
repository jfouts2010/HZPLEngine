using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public class DivisionSystem
    {
        [SerializeReference]
        public List<Division> Divisions = new List<Division>();

        private Dictionary<Vector3Int, List<Division>> divisionsByTileId;
        private Dictionary<Guid, Division> divisionsById;

        public List<Division> GetDivisionsOnTile(Vector3Int tileId)
        {
            EnsureIndex();
            return divisionsByTileId.TryGetValue(tileId, out var divisions)
                ? divisions
                : new List<Division>();
        }

        public bool TryGetDivision(Guid divisionId, out Division division)
        {
            EnsureIndex();
            return divisionsById.TryGetValue(divisionId, out division);
        }

        public bool MoveDivision(Guid divisionId, Vector3Int tileId)
        {
            EnsureIndex();
            if (!divisionsById.TryGetValue(divisionId, out var division))
                return false;

            if (division.TileId == tileId)
                return true;

            if (divisionsByTileId.TryGetValue(division.TileId, out var oldTileDivisions))
            {
                oldTileDivisions.Remove(division);
                if (oldTileDivisions.Count == 0)
                    divisionsByTileId.Remove(division.TileId);
            }

            division.TileId = tileId;

            if (!divisionsByTileId.TryGetValue(tileId, out var newTileDivisions))
            {
                newTileDivisions = new List<Division>();
                divisionsByTileId[tileId] = newTileDivisions;
            }

            newTileDivisions.Add(division);
            return true;
        }

        public bool RemoveDivision(Guid divisionId)
        {
            EnsureIndex();
            if (!divisionsById.TryGetValue(divisionId, out var division))
                return false;

            divisionsById.Remove(divisionId);
            if (divisionsByTileId.TryGetValue(division.TileId, out var tileDivisions))
            {
                tileDivisions.Remove(division);
                if (tileDivisions.Count == 0)
                    divisionsByTileId.Remove(division.TileId);
            }

            return Divisions.Remove(division);
        }

        public void RebuildIndex()
        {
            var divisions = (Divisions ?? new List<Division>())
                .Where(division => division != null)
                .ToList();

            foreach (var division in divisions)
                division.CurrentOrder ??= new HoldGroundOrder();

            divisionsByTileId = divisions
                .GroupBy(division => division.TileId)
                .ToDictionary(group => group.Key, group => group.ToList());

            divisionsById = divisions
                .GroupBy(division => division.DivisionId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        private void EnsureIndex()
        {
            if (divisionsByTileId == null || divisionsById == null)
                RebuildIndex();
        }
    }
}
