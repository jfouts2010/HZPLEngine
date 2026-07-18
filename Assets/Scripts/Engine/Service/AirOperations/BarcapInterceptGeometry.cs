using System;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    public static class BarcapInterceptGeometry
    {
        private const float KilometersPerNauticalMile = 1.852f;
        private const float MaximumResponseMinutes = 10f;
        private const float MinimumRepresentativeThreatSpeedKnots = 300f;
        private const float PlanningCommitMarginMinutes = 1.5f;

        public static float CalculateResponseRadiusKm(
            AircraftTypeDefinition aircraftType,
            float representativeThreatSpeedKnots,
            float threatDistanceToBarrierKm,
            float weaponReleaseStandoffKm =
                BarcapBarrierPlan.DefaultWeaponReleaseStandoffKm,
            float preferredLaunchRangeKm = 0f,
            float weaponPreparationSeconds = 0f,
            float sensorWarningMinutes = 0f)
        {
            if (aircraftType == null || aircraftType.CombatSpeedKnots <= 0f)
                return 0f;

            var responseMinutes = Mathf.Clamp(
                sensorWarningMinutes,
                0f,
                MaximumResponseMinutes);
            var threatSpeedKmPerHour = Math.Max(
                MinimumRepresentativeThreatSpeedKnots,
                representativeThreatSpeedKnots)
                * KilometersPerNauticalMile;
            var threatDistanceToReleaseKm = Math.Max(
                0f,
                threatDistanceToBarrierKm
                - BarcapBarrierPlan.ResolveWeaponReleaseStandoffKm(
                    weaponReleaseStandoffKm));
            var threatMinutesToRelease = threatDistanceToReleaseKm
                                         / Math.Max(1f, threatSpeedKmPerHour)
                                         * 60f;
            var availableMinutes = Math.Min(
                responseMinutes,
                threatMinutesToRelease);
            var readinessMinutes = PlanningCommitMarginMinutes
                                   + Math.Max(0f, weaponPreparationSeconds)
                                   / 60f;
            if (availableMinutes < readinessMinutes)
                return 0f;

            var flightMinutes = availableMinutes - readinessMinutes;

            return Math.Max(0f, preferredLaunchRangeKm)
                   + aircraftType.CombatSpeedKnots
                   * KilometersPerNauticalMile
                   * flightMinutes / 60f;
        }

        public static IReadOnlyList<Vector3> GetOperationalBarrierPointsFeet(
            IReadOnlyList<Vector3Int> coveredBarrierTiles,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm,
            float weaponReleaseStandoffKm)
        {
            if (coveredBarrierTiles == null
                || coveredBarrierTiles.Count < 1
                || tileDistanceKm <= 0f)
                return Array.Empty<Vector3>();

            var threatCenter = AirspaceGeometry.TileCenterFeet(
                threatReferenceTileId,
                tileDistanceKm);
            var offsetFeet =
                BarcapBarrierPlan.ResolveWeaponReleaseStandoffKm(
                    weaponReleaseStandoffKm)
                * AirspaceGeometry.FeetPerKilometer;
            return coveredBarrierTiles
                .Select(tile =>
                {
                    var protectedCenter = AirspaceGeometry.TileCenterFeet(
                        tile,
                        tileDistanceKm);
                    var towardThreat = threatCenter - protectedCenter;
                    towardThreat.y = 0f;
                    if (towardThreat.sqrMagnitude < 1f)
                        return protectedCenter;
                    var maximumOffsetFeet = Math.Max(
                        0f,
                        towardThreat.magnitude - 1f);
                    return protectedCenter
                           + towardThreat.normalized
                           * Math.Min(offsetFeet, maximumOffsetFeet);
                })
                .ToList();
        }

        public static float DistanceToOperationalBarrierKm(
            Vector3 positionFeet,
            IReadOnlyList<Vector3Int> coveredBarrierTiles,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm,
            float weaponReleaseStandoffKm)
        {
            var segmentPoints = GetOperationalBarrierSegmentPointsFeet(
                coveredBarrierTiles,
                threatReferenceTileId,
                tileDistanceKm,
                weaponReleaseStandoffKm);
            if (segmentPoints.Count < 2)
                return float.MaxValue;

            var position = new Vector2(positionFeet.x, positionFeet.z);
            var minimumSquaredDistance = float.MaxValue;
            for (var index = 1; index < segmentPoints.Count; index++)
            {
                var start = new Vector2(
                    segmentPoints[index - 1].x,
                    segmentPoints[index - 1].z);
                var end = new Vector2(
                    segmentPoints[index].x,
                    segmentPoints[index].z);
                var segment = end - start;
                var progress = segment.sqrMagnitude < 0.001f
                    ? 0f
                    : Mathf.Clamp01(
                        Vector2.Dot(position - start, segment)
                        / segment.sqrMagnitude);
                var closest = start + segment * progress;
                minimumSquaredDistance = Math.Min(
                    minimumSquaredDistance,
                    (position - closest).sqrMagnitude);
            }

            return Mathf.Sqrt(minimumSquaredDistance)
                   / AirspaceGeometry.FeetPerKilometer;
        }

        public static bool IsApproachCompatible(
            BarcapStationCoverage coverage,
            BarcapBarrierPlan barrier)
        {
            if (coverage?.CoveredBarrierTileIds == null
                || barrier?.BarrierTileIds == null)
                return false;

            var commonTile = barrier.BarrierTileIds
                .Where(coverage.CoveredBarrierTileIds.Contains)
                .Select(tile => (Vector3Int?)tile)
                .FirstOrDefault();
            if (!commonTile.HasValue)
                return false;

            var commonCenter = AirspaceGeometry.TileCenterFeet(
                commonTile.Value,
                1f);
            var plannedApproach = commonCenter
                                  - AirspaceGeometry.TileCenterFeet(
                                      barrier.ThreatReferenceTileId,
                                      1f);
            var assignedApproach = commonCenter
                                   - AirspaceGeometry.TileCenterFeet(
                                       coverage.ThreatReferenceTileId,
                                       1f);
            if (plannedApproach.sqrMagnitude < 0.001f
                || assignedApproach.sqrMagnitude < 0.001f)
                return false;

            return Vector3.Dot(
                       plannedApproach.normalized,
                       assignedApproach.normalized)
                   >= 0.5f;
        }

        public static bool TryPredictBarrierCrossing(
            Vector3 trackPositionFeet,
            float trackHeadingDegrees,
            float trackSpeedKnots,
            IReadOnlyList<Vector3Int> coveredBarrierTiles,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm,
            float weaponReleaseStandoffKm,
            float maximumLookaheadMinutes,
            out Vector3 crossingFeet,
            out float minutesToCrossing)
        {
            crossingFeet = default;
            minutesToCrossing = float.MaxValue;
            if (coveredBarrierTiles == null
                || coveredBarrierTiles.Count < 1
                || trackSpeedKnots <= 0f
                || tileDistanceKm <= 0f)
                return false;

            var velocity3 = HeadingDirection(trackHeadingDegrees)
                            * trackSpeedKnots
                            * 1.68781f;
            var velocity = new Vector2(velocity3.x, velocity3.z);
            if (velocity.sqrMagnitude < 1f)
                return false;

            var position = new Vector2(trackPositionFeet.x, trackPositionFeet.z);
            var threatCenter3 = AirspaceGeometry.TileCenterFeet(
                threatReferenceTileId,
                tileDistanceKm);
            var protectedCenters = coveredBarrierTiles
                .Select(tile => AirspaceGeometry.TileCenterFeet(
                    tile,
                    tileDistanceKm))
                .ToList();
            var protectedMidpoint = protectedCenters.Aggregate(
                Vector3.zero,
                (sum, point) => sum + point) / protectedCenters.Count;
            var threatToProtected = protectedMidpoint - threatCenter3;
            threatToProtected.y = 0f;
            if (threatToProtected.sqrMagnitude < 1f)
                return false;
            var barrierCenters = GetOperationalBarrierSegmentPointsFeet(
                coveredBarrierTiles,
                threatReferenceTileId,
                tileDistanceKm,
                weaponReleaseStandoffKm);
            if (barrierCenters.Count < 2)
                return false;

            var hostileToFriendly = new Vector2(
                threatToProtected.x,
                threatToProtected.z);
            if (Vector2.Dot(velocity, hostileToFriendly) <= 0f)
                return false;

            var bestSeconds = float.MaxValue;
            var bestPoint = Vector2.zero;
            for (var index = 1; index < barrierCenters.Count; index++)
            {
                var start = new Vector2(
                    barrierCenters[index - 1].x,
                    barrierCenters[index - 1].z);
                var end = new Vector2(
                    barrierCenters[index].x,
                    barrierCenters[index].z);
                if (!TryRaySegmentIntersection(
                        position,
                        velocity,
                        start,
                        end,
                        out var seconds,
                        out var point)
                    || seconds >= bestSeconds)
                    continue;

                bestSeconds = seconds;
                bestPoint = point;
            }

            if (bestSeconds == float.MaxValue
                || bestSeconds > maximumLookaheadMinutes * 60f)
                return false;

            minutesToCrossing = bestSeconds / 60f;
            crossingFeet = new Vector3(bestPoint.x, trackPositionFeet.y, bestPoint.y);
            return true;
        }

        private static List<Vector3> GetOperationalBarrierSegmentPointsFeet(
            IReadOnlyList<Vector3Int> coveredBarrierTiles,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm,
            float weaponReleaseStandoffKm)
        {
            var barrierCenters = GetOperationalBarrierPointsFeet(
                    coveredBarrierTiles,
                    threatReferenceTileId,
                    tileDistanceKm,
                    weaponReleaseStandoffKm)
                .ToList();
            if (barrierCenters.Count == 0)
                return barrierCenters;

            var threatCenter = AirspaceGeometry.TileCenterFeet(
                threatReferenceTileId,
                tileDistanceKm);
            var protectedMidpoint = coveredBarrierTiles
                .Select(tile => AirspaceGeometry.TileCenterFeet(
                    tile,
                    tileDistanceKm))
                .Aggregate(
                    Vector3.zero,
                    (sum, point) => sum + point) / coveredBarrierTiles.Count;
            var threatToProtected = protectedMidpoint - threatCenter;
            threatToProtected.y = 0f;
            if (threatToProtected.sqrMagnitude < 1f)
                return new List<Vector3>();

            var halfTileWidthFeet = tileDistanceKm
                                    * AirspaceGeometry.FeetPerKilometer
                                    * 0.5f;
            if (barrierCenters.Count == 1)
            {
                var tangent = new Vector3(
                    -threatToProtected.z,
                    0f,
                    threatToProtected.x).normalized;
                return new List<Vector3>
                {
                    barrierCenters[0] - tangent * halfTileWidthFeet,
                    barrierCenters[0] + tangent * halfTileWidthFeet
                };
            }

            var firstDirection = barrierCenters[1] - barrierCenters[0];
            firstDirection.y = 0f;
            if (firstDirection.sqrMagnitude >= 1f)
            {
                barrierCenters[0] -= firstDirection.normalized
                                     * halfTileWidthFeet;
            }

            var lastIndex = barrierCenters.Count - 1;
            var lastDirection = barrierCenters[lastIndex]
                                - barrierCenters[lastIndex - 1];
            lastDirection.y = 0f;
            if (lastDirection.sqrMagnitude >= 1f)
            {
                barrierCenters[lastIndex] += lastDirection.normalized
                                             * halfTileWidthFeet;
            }

            return barrierCenters;
        }

        public static bool IsOnDefendedSide(
            Vector3 positionFeet,
            IReadOnlyList<Vector3Int> coveredBarrierTiles,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm,
            float weaponReleaseStandoffKm,
            float toleranceKm = 0f)
        {
            if (!TryGetLocalBarrierFrame(
                    positionFeet,
                    coveredBarrierTiles,
                    threatReferenceTileId,
                    tileDistanceKm,
                    weaponReleaseStandoffKm,
                    out var barrierCenter,
                    out var hostileToFriendly))
                return false;

            var position = new Vector2(positionFeet.x, positionFeet.z);
            var signedDistanceFeet = Vector2.Dot(
                position - barrierCenter,
                hostileToFriendly);
            return signedDistanceFeet
                   >= -Math.Max(0f, toleranceKm)
                   * AirspaceGeometry.FeetPerKilometer;
        }

        public static Vector3 ClampToDefendedSide(
            Vector3 positionFeet,
            IReadOnlyList<Vector3Int> coveredBarrierTiles,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm,
            float weaponReleaseStandoffKm,
            float defensiveBufferKm)
        {
            if (!TryGetLocalBarrierFrame(
                    positionFeet,
                    coveredBarrierTiles,
                    threatReferenceTileId,
                    tileDistanceKm,
                    weaponReleaseStandoffKm,
                    out var barrierCenter,
                    out var hostileToFriendly))
                return positionFeet;

            var position = new Vector2(positionFeet.x, positionFeet.z);
            var signedDistanceFeet = Vector2.Dot(
                position - barrierCenter,
                hostileToFriendly);
            var minimumDistanceFeet = Math.Max(0f, defensiveBufferKm)
                                       * AirspaceGeometry.FeetPerKilometer;
            if (signedDistanceFeet >= minimumDistanceFeet)
                return positionFeet;

            var correction = hostileToFriendly
                             * (minimumDistanceFeet - signedDistanceFeet);
            return new Vector3(
                positionFeet.x + correction.x,
                positionFeet.y,
                positionFeet.z + correction.y);
        }

        private static bool TryGetLocalBarrierFrame(
            Vector3 positionFeet,
            IReadOnlyList<Vector3Int> coveredBarrierTiles,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm,
            float weaponReleaseStandoffKm,
            out Vector2 barrierCenter,
            out Vector2 hostileToFriendly)
        {
            barrierCenter = default;
            hostileToFriendly = default;
            if (coveredBarrierTiles == null
                || coveredBarrierTiles.Count < 1
                || tileDistanceKm <= 0f)
                return false;

            var threatCenter3 = AirspaceGeometry.TileCenterFeet(
                threatReferenceTileId,
                tileDistanceKm);
            var threatCenter = new Vector2(threatCenter3.x, threatCenter3.z);
            var position = new Vector2(positionFeet.x, positionFeet.z);
            var operationalCenters = GetOperationalBarrierPointsFeet(
                coveredBarrierTiles,
                threatReferenceTileId,
                tileDistanceKm,
                weaponReleaseStandoffKm);
            var nearest = coveredBarrierTiles
                .Select((tile, index) => new
                {
                    Operational = new Vector2(
                        operationalCenters[index].x,
                        operationalCenters[index].z),
                    Protected = AirspaceGeometry.TileCenterFeet(
                        tile,
                        tileDistanceKm)
                })
                .OrderBy(candidate =>
                    (candidate.Operational - position).sqrMagnitude)
                .First();
            var protectedCenter = new Vector2(
                nearest.Protected.x,
                nearest.Protected.z);
            var direction = protectedCenter - threatCenter;
            if (direction.sqrMagnitude < 1f)
                return false;

            barrierCenter = nearest.Operational;
            hostileToFriendly = direction.normalized;
            return true;
        }

        private static bool TryRaySegmentIntersection(
            Vector2 rayOrigin,
            Vector2 rayVelocity,
            Vector2 segmentStart,
            Vector2 segmentEnd,
            out float seconds,
            out Vector2 point)
        {
            seconds = 0f;
            point = default;
            var segment = segmentEnd - segmentStart;
            var denominator = Cross(rayVelocity, segment);
            if (Math.Abs(denominator) < 0.001f)
                return false;

            var offset = segmentStart - rayOrigin;
            var rayTime = Cross(offset, segment) / denominator;
            var segmentFraction = Cross(offset, rayVelocity) / denominator;
            if (rayTime < 0f || segmentFraction < 0f || segmentFraction > 1f)
                return false;

            seconds = rayTime;
            point = rayOrigin + rayVelocity * rayTime;
            return true;
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        private static Vector3 HeadingDirection(float headingDegrees)
        {
            var radians = headingDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }
    }
}
