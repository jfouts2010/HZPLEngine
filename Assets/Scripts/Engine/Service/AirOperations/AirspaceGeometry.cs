using System;
using System.Collections.Generic;
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
