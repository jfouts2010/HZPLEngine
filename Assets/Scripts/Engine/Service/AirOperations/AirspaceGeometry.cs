using System;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    public static class AirspaceGeometry
    {
        public const float FeetPerKilometer = 3280.8399f;
        public const float FeetPerNauticalMile = 6076.1155f;
        private const float SqrtThreeOverTwo = 0.8660254f;
        private static readonly Vector3Int[] NeighborDirections =
        {
            new Vector3Int(1, -1, 0),
            new Vector3Int(1, 0, -1),
            new Vector3Int(0, 1, -1),
            new Vector3Int(-1, 1, 0),
            new Vector3Int(-1, 0, 1),
            new Vector3Int(0, -1, 1)
        };

        public static IEnumerable<Vector3Int> NeighborTiles(Vector3Int tileId)
        {
            return NeighborDirections.Select(direction => tileId + direction);
        }

        public static IReadOnlyList<Vector3Int> TilesAlongLine(
            Vector3Int start,
            Vector3Int end)
        {
            var distance = AirMissionArea.HexDistance(start, end);
            if (distance == 0)
                return new[] { start };

            var tiles = new List<Vector3Int>(distance + 1);
            for (var index = 0; index <= distance; index++)
            {
                var progress = index / (float)distance;
                var tile = CubeRound(
                    Mathf.Lerp(start.x, end.x, progress),
                    Mathf.Lerp(start.y, end.y, progress),
                    Mathf.Lerp(start.z, end.z, progress));
                if (tiles.Count == 0 || tiles[tiles.Count - 1] != tile)
                    tiles.Add(tile);
            }

            return tiles;
        }

        public static Vector3 TileCenterFeet(
            Vector3Int cubeCoordinate,
            float tileDistanceKm,
            float altitudeFeet = 0f)
        {
            var spacingFeet = Math.Max(0f, tileDistanceKm) * FeetPerKilometer;
            return new Vector3(
                cubeCoordinate.x * SqrtThreeOverTwo * spacingFeet,
                altitudeFeet,
                (cubeCoordinate.z + cubeCoordinate.x * 0.5f) * spacingFeet);
        }

        public static Vector3Int TileCoordinateFromPositionFeet(
            Vector3 positionFeet,
            float tileDistanceKm)
        {
            var spacingFeet = Math.Max(0f, tileDistanceKm) * FeetPerKilometer;
            if (spacingFeet <= 0f)
                return Vector3Int.zero;

            var x = positionFeet.x / (SqrtThreeOverTwo * spacingFeet);
            var z = positionFeet.z / spacingFeet - x * 0.5f;
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

        public static double TravelSeconds(
            Vector3 from,
            Vector3 to,
            float speedKnots,
            float climbRateFeetPerMinute,
            float descentRateFeetPerMinute)
        {
            var horizontalFeet = Vector2.Distance(
                new Vector2(from.x, from.z),
                new Vector2(to.x, to.z));
            var horizontalSeconds = HorizontalTravelSeconds(horizontalFeet, speedKnots);
            var altitudeDelta = to.y - from.y;
            var verticalRate = altitudeDelta >= 0f
                ? Math.Max(1f, climbRateFeetPerMinute)
                : Math.Max(1f, descentRateFeetPerMinute);
            var verticalSeconds = Math.Abs(altitudeDelta) / verticalRate * 60d;
            return Math.Max(horizontalSeconds, verticalSeconds);
        }

        public static double HorizontalTravelSeconds(float distanceFeet, float speedKnots)
        {
            var feetPerSecond = Math.Max(1f, speedKnots)
                                * FeetPerNauticalMile / 3600f;
            return Math.Max(0f, distanceFeet) / feetPerSecond;
        }
    }

    internal static class AirRecoveryRouteBuilder
    {
        public static IReadOnlyList<AirWaypoint> Build(
            Vector3 currentPosition,
            AircraftTypeDefinition aircraftType,
            Guid recoveryAirportId,
            Vector3 recoveryPosition,
            DateTime recoveryStart)
        {
            if (recoveryAirportId == Guid.Empty)
                throw new ArgumentException(
                    "A recovery airport is required.",
                    nameof(recoveryAirportId));

            var horizontal = new Vector3(
                currentPosition.x - recoveryPosition.x,
                0f,
                currentPosition.z - recoveryPosition.z);
            var distance = horizontal.magnitude;
            var descentMinutes = currentPosition.y
                                 / Math.Max(1f, aircraftType.DescentRateFeetPerMinute);
            var descentDistance = aircraftType.CruiseSpeedKnots
                                  * AirspaceGeometry.FeetPerNauticalMile
                                  * descentMinutes / 60f;
            var approach = recoveryPosition;
            if (distance > 0.01f)
                approach += horizontal.normalized * Math.Min(distance, descentDistance);
            approach.y = currentPosition.y;

            var approachTime = recoveryStart + TimeSpan.FromSeconds(
                AirspaceGeometry.TravelSeconds(
                    currentPosition,
                    approach,
                    aircraftType.CruiseSpeedKnots,
                    aircraftType.ClimbRateFeetPerMinute,
                    aircraftType.DescentRateFeetPerMinute));
            var landingTime = approachTime + TimeSpan.FromSeconds(
                AirspaceGeometry.TravelSeconds(
                    approach,
                    recoveryPosition,
                    aircraftType.CruiseSpeedKnots,
                    aircraftType.ClimbRateFeetPerMinute,
                    aircraftType.DescentRateFeetPerMinute));

            return new[]
            {
                new AirWaypoint(
                    approach,
                    AirWaypointAction.Approach,
                    approachTime),
                new AirWaypoint(
                    recoveryPosition,
                    AirWaypointAction.Land,
                    landingTime,
                    airportBuildingId: recoveryAirportId)
            };
        }
    }
}
