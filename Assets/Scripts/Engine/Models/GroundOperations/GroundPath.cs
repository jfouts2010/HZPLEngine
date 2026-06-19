using System;
using System.Collections.Generic;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class GroundPath
    {
        public List<Vector3Int> TileIds = new List<Vector3Int>();

        public bool IsEmpty => TileIds == null || TileIds.Count == 0;

        public int StepCount => IsEmpty ? 0 : TileIds.Count - 1;

        public Vector3Int DestinationTileId => IsEmpty ? default : TileIds[TileIds.Count - 1];

        public static GroundPath FromDirectStep(Vector3Int fromTileId, Vector3Int destinationTileId)
        {
            return new GroundPath
            {
                TileIds = new List<Vector3Int> { fromTileId, destinationTileId }
            };
        }

        public static GroundPath FromSingleTile(Vector3Int tileId)
        {
            return new GroundPath { TileIds = new List<Vector3Int> { tileId } };
        }

        public static GroundPath FromTileChain(
            IReadOnlyDictionary<Vector3Int, Vector3Int> previousByTileId,
            Vector3Int startTileId,
            Vector3Int destinationTileId)
        {
            var chain = new List<Vector3Int>();
            var current = destinationTileId;
            while (true)
            {
                chain.Add(current);
                if (current == startTileId)
                    break;

                current = previousByTileId[current];
            }

            chain.Reverse();
            return new GroundPath { TileIds = chain };
        }

        public bool ContainsTile(Vector3Int tileId)
        {
            return TileIds != null && TileIds.Contains(tileId);
        }

        public bool TryGetNextStep(Vector3Int currentTileId, out Vector3Int nextTileId)
        {
            nextTileId = default;
            if (TileIds == null || TileIds.Count < 2)
                return false;

            for (var index = 0; index < TileIds.Count - 1; index++)
            {
                if (TileIds[index] != currentTileId)
                    continue;

                nextTileId = TileIds[index + 1];
                return true;
            }

            return false;
        }
    }
}
