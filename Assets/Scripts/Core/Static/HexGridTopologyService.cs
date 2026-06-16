using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    public static class HexGridTopology
    {
        private static readonly Vector3Int[] CubeNeighborOffsets =
        {
            new Vector3Int(1, -1, 0),
            new Vector3Int(1, 0, -1),
            new Vector3Int(0, 1, -1),
            new Vector3Int(-1, 1, 0),
            new Vector3Int(-1, 0, 1),
            new Vector3Int(0, -1, 1)
        };

        public static void AssignNeighbors(IReadOnlyList<Tile> tiles)
        {
            if (tiles == null)
                return;

            var tilesByCoordinate = tiles
                .Where(tile => tile != null)
                .GroupBy(tile => tile.Coordinates)
                .Where(group => group.Count() == 1)
                .Select(group => group.First())
                .ToDictionary(tile => tile.Coordinates);

            foreach (var tile in tiles.Where(tile => tile != null))
            {
                tile.NeighborTileIds = CubeNeighborOffsets
                    .Select(offset => tile.Coordinates + offset)
                    .Where(coordinates => tilesByCoordinate.ContainsKey(coordinates))
                    .Select(coordinates => tilesByCoordinate[coordinates].Coordinates)
                    .ToList();
            }
        }

        public static bool IsValidCubeCoordinate(Vector3Int coordinates)
        {
            return coordinates.x + coordinates.y + coordinates.z == 0;
        }

        public static IReadOnlyList<string> Validate(IReadOnlyList<Tile> tiles)
        {
            var errors = new List<string>();
            if (tiles == null)
                return errors;

            var concreteTiles = tiles.Where(tile => tile != null).ToList();
            foreach (var tile in concreteTiles.Where(tile => !IsValidCubeCoordinate(tile.Coordinates)))
                errors.Add($"Tile {tile.Coordinates} has invalid cube coordinates {tile.Coordinates}.");

            var duplicateCoordinates = concreteTiles
                .GroupBy(tile => tile.Coordinates)
                .Where(group => group.Count() > 1);
            foreach (var group in duplicateCoordinates)
                errors.Add($"Multiple tiles use cube coordinates {group.Key}.");

            var tileIds = new HashSet<Vector3Int>(concreteTiles.Select(tile => tile.Coordinates));
            foreach (var tile in concreteTiles)
            {
                foreach (var riverNeighborId in tile.RiverNeighborTileIds ?? new List<Vector3Int>())
                {
                    if (!tileIds.Contains(riverNeighborId))
                        errors.Add($"Tile {tile.Coordinates} has unknown river neighbor {riverNeighborId}.");
                    else if (tile.NeighborTileIds == null || !tile.NeighborTileIds.Contains(riverNeighborId))
                        errors.Add($"Tile {tile.Coordinates} has river neighbor {riverNeighborId} that is not adjacent.");
                }
            }

            return errors;
        }
    }
}
