using System.Collections.Generic;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models
{
    public class AllianceAI
    {
        private readonly GameManager _gameManager;
        private readonly HashSet<Vector3Int> _frontTileIds = new HashSet<Vector3Int>();

        public Alliance Alliance { get; }

        public IReadOnlyCollection<Vector3Int> FrontTileIds => _frontTileIds;

        public AllianceAI(GameManager gameManager, Alliance alliance)
        {
            _gameManager = gameManager;
            Alliance = alliance;
        }

        internal void RefreshFront()
        {
            _frontTileIds.Clear();

            if (!TryGetHostileAlliance(Alliance, out var hostileAlliance))
                return;

            var controllersByTileId = BuildControllerLookup(_gameManager.Tiles);
            var neighborsByTileId = BuildNeighborLookup(_gameManager.CampaignTiles);

            foreach (var entry in controllersByTileId)
            {
                if (entry.Value != Alliance)
                    continue;

                if (!neighborsByTileId.TryGetValue(entry.Key, out var neighborTileIds))
                    continue;

                foreach (var neighborTileId in neighborTileIds)
                {
                    if (controllersByTileId.TryGetValue(neighborTileId, out var neighborController)
                        && neighborController == hostileAlliance)
                    {
                        _frontTileIds.Add(entry.Key);
                        break;
                    }
                }
            }
        }

        private static bool TryGetHostileAlliance(Alliance alliance, out Alliance hostileAlliance)
        {
            switch (alliance)
            {
                case Alliance.Bluefor:
                    hostileAlliance = Alliance.Redfor;
                    return true;
                case Alliance.Redfor:
                    hostileAlliance = Alliance.Bluefor;
                    return true;
                default:
                    hostileAlliance = default;
                    return false;
            }
        }

        private static Dictionary<Vector3Int, Alliance> BuildControllerLookup(IReadOnlyList<TileData> tiles)
        {
            var controllersByTileId = new Dictionary<Vector3Int, Alliance>();
            if (tiles == null)
                return controllersByTileId;

            foreach (var tileData in tiles)
            {
                if (tileData is LandTileData landTileData)
                    controllersByTileId[tileData.TileId] = landTileData.Controller;
            }

            return controllersByTileId;
        }

        private static Dictionary<Vector3Int, List<Vector3Int>> BuildNeighborLookup(IReadOnlyList<Tile> tiles)
        {
            var neighborsByTileId = new Dictionary<Vector3Int, List<Vector3Int>>();
            if (tiles == null)
                return neighborsByTileId;

            foreach (var tile in tiles)
            {
                if (tile == null)
                    continue;

                neighborsByTileId[tile.Coordinates] =
                    tile.NeighborTileIds ?? new List<Vector3Int>();
            }

            return neighborsByTileId;
        }
    }
}
