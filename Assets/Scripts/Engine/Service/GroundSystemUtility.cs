using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models.Ground
{
    internal static class GroundSystemUtility
    {
        public static bool TryGetDivisionAlliance(GameManager gameManager, Division division, out Alliance alliance)
        {
            alliance = Alliance.Neutral;
            if (gameManager?.CampaignTemplate?.CountryAllianceAssignments == null || division == null)
                return false;

            var assignment = gameManager.CampaignTemplate.CountryAllianceAssignments
                .FirstOrDefault(candidate => candidate != null && candidate.CountryId == division.CountryId);
            if (assignment == null)
                return false;

            alliance = assignment.Alliance;
            return true;
        }

        public static bool AreHostile(Alliance first, Alliance second)
        {
            return (first == Alliance.Bluefor && second == Alliance.Redfor)
                   || (first == Alliance.Redfor && second == Alliance.Bluefor);
        }

        public static Alliance GetHostileAlliance(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => Alliance.Redfor,
                Alliance.Redfor => Alliance.Bluefor,
                _ => Alliance.Neutral
            };
        }

        public static bool IsRetreating(Division division)
        {
            return division?.CurrentOrder is MoveGroundOrder { Purpose: MoveGroundOrderPurpose.Retreat };
        }

        public static bool IsLandTile(GameManager gameManager, Vector3Int tileId)
        {
            return gameManager?.Tiles != null
                   && gameManager.Tiles.Any(tileData => tileData is LandTileData && tileData.TileId == tileId);
        }

        public static bool TryGetLandTileData(GameManager gameManager, Vector3Int tileId, out LandTileData landTileData)
        {
            landTileData = null;
            if (gameManager?.Tiles == null)
                return false;

            landTileData = gameManager.Tiles
                .OfType<LandTileData>()
                .FirstOrDefault(tileData => tileData.TileId == tileId);
            return landTileData != null;
        }

        public static bool AreNeighbors(GameManager gameManager, Vector3Int firstTileId, Vector3Int secondTileId)
        {
            var tile = gameManager?.CampaignTiles?
                .FirstOrDefault(candidate => candidate != null && candidate.Coordinates == firstTileId);
            return tile?.NeighborTileIds != null && tile.NeighborTileIds.Contains(secondTileId);
        }

        public static IEnumerable<Vector3Int> GetNeighborTileIds(GameManager gameManager, Vector3Int tileId)
        {
            var tile = gameManager?.CampaignTiles?
                .FirstOrDefault(candidate => candidate != null && candidate.Coordinates == tileId);
            return tile?.NeighborTileIds;
        }
    }
}
