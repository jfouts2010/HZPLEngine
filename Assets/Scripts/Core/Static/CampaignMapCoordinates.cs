using System;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    public static class CampaignMapCoordinates
    {
        public const float FeetPerKilometer = 3280.8399f;
        public const float TileCenterSpacingKilometers = 20f;
        public const float TileCenterSpacingFeet =
            TileCenterSpacingKilometers * FeetPerKilometer;
        private const float SqrtThreeOverTwo = 0.8660254f;

        public static Vector3 TileCenterFeet(
            Vector3Int cubeCoordinate,
            float altitudeFeet = 0f)
        {
            return new Vector3(
                cubeCoordinate.x * SqrtThreeOverTwo * TileCenterSpacingFeet,
                altitudeFeet,
                (cubeCoordinate.z + cubeCoordinate.x * 0.5f)
                * TileCenterSpacingFeet);
        }

        public static Vector3Int TileCoordinateFromPositionFeet(Vector3 positionFeet)
        {
            var x = positionFeet.x
                    / (SqrtThreeOverTwo * TileCenterSpacingFeet);
            var z = positionFeet.z / TileCenterSpacingFeet - x * 0.5f;
            var y = -x - z;
            return CubeRound(x, y, z);
        }

        private static Vector3Int CubeRound(float x, float y, float z)
        {
            var roundedX = Mathf.RoundToInt(x);
            var roundedY = Mathf.RoundToInt(y);
            var roundedZ = Mathf.RoundToInt(z);

            var xDifference = Math.Abs(roundedX - x);
            var yDifference = Math.Abs(roundedY - y);
            var zDifference = Math.Abs(roundedZ - z);

            if (xDifference > yDifference && xDifference > zDifference)
                roundedX = -roundedY - roundedZ;
            else if (yDifference > zDifference)
                roundedY = -roundedX - roundedZ;
            else
                roundedZ = -roundedX - roundedY;

            return new Vector3Int(roundedX, roundedY, roundedZ);
        }
    }
}
