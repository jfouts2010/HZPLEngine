using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AirMissionArea
    {
        public Vector3Int CenterTileId;
        public float RadiusKm = 20f;
        public float TileDistanceKm = 20f;

        public AirMissionArea()
        {
        }

        public AirMissionArea(
            Vector3Int centerTileId,
            float radiusKm,
            float tileDistanceKm = 20f)
        {
            CenterTileId = centerTileId;
            RadiusKm = Math.Max(0f, radiusKm);
            TileDistanceKm = Math.Max(0.001f, tileDistanceKm);
        }

        public bool Contains(Vector3Int tileId)
        {
            return ContainsPosition(
                Engine.Service.AirspaceGeometry.TileCenterFeet(
                    tileId,
                    TileDistanceKm));
        }

        public bool ContainsPosition(Vector3 positionFeet)
        {
            var centerFeet = Engine.Service.AirspaceGeometry.TileCenterFeet(
                CenterTileId,
                TileDistanceKm);
            var distanceFeet = Vector2.Distance(
                new Vector2(centerFeet.x, centerFeet.z),
                new Vector2(positionFeet.x, positionFeet.z));
            return distanceFeet
                   <= RadiusKm * Engine.Service.AirspaceGeometry.FeetPerKilometer
                      + 0.001f;
        }

        public bool Intersects(AirMissionArea other)
        {
            if (other == null)
                return false;

            var centerFeet = Engine.Service.AirspaceGeometry.TileCenterFeet(
                CenterTileId,
                TileDistanceKm);
            var otherCenterFeet = Engine.Service.AirspaceGeometry.TileCenterFeet(
                other.CenterTileId,
                other.TileDistanceKm);
            var distanceFeet = Vector2.Distance(
                new Vector2(centerFeet.x, centerFeet.z),
                new Vector2(otherCenterFeet.x, otherCenterFeet.z));
            return distanceFeet <= (RadiusKm + other.RadiusKm)
                                   * Engine.Service.AirspaceGeometry.FeetPerKilometer
                                   + 0.001f;
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
