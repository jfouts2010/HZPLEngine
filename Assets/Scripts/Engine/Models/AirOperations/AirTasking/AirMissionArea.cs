using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AirMissionArea
    {
        public Vector3Int CenterTileId;
        public int RadiusTiles = 1;

        public AirMissionArea()
        {
        }

        public AirMissionArea(Vector3Int centerTileId, int radiusTiles)
        {
            CenterTileId = centerTileId;
            RadiusTiles = Math.Max(0, radiusTiles);
        }

        public bool Contains(Vector3Int tileId)
        {
            return HexDistance(CenterTileId, tileId) <= RadiusTiles;
        }

        public static int HexDistance(Vector3Int first, Vector3Int second)
        {
            return Math.Max(
                Math.Abs(first.x - second.x),
                Math.Max(
                    Math.Abs(first.y - second.y),
                    Math.Abs(first.z - second.z)));
        }
    }
}
