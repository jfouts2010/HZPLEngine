using System;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    public sealed class BarcapRacetrackGeometry
    {
        public Vector3 CenterFeet { get; }
        public float HeadingDegrees { get; }
        public float TurnRadiusFeet { get; }
        public float CircuitLengthFeet { get; }
        public IReadOnlyList<Vector3> LoopPointsFeet { get; }

        internal BarcapRacetrackGeometry(
            Vector3 centerFeet,
            float headingDegrees,
            float turnRadiusFeet,
            float circuitLengthFeet,
            IReadOnlyList<Vector3> loopPointsFeet)
        {
            CenterFeet = centerFeet;
            HeadingDegrees = Mathf.Repeat(headingDegrees, 360f);
            TurnRadiusFeet = Math.Max(0f, turnRadiusFeet);
            CircuitLengthFeet = Math.Max(0f, circuitLengthFeet);
            LoopPointsFeet = loopPointsFeet ?? Array.Empty<Vector3>();
        }

        public IReadOnlyList<Vector3> GetClosedLoopPoints()
        {
            if (LoopPointsFeet.Count == 0)
                return Array.Empty<Vector3>();
            var result = LoopPointsFeet.ToList();
            result.Add(LoopPointsFeet[0]);
            return result;
        }
    }

    public static class BarcapInterceptGeometry
    {
        private const float KilometersPerNauticalMile = 1.852f;
        private const int HalfTurnSegmentCount = 12;
        private static readonly float[] StationDepthFractions =
        {
            0.35f,
            0.55f,
            0.75f
        };
        private static readonly float[] StationLateralFractions =
        {
            0f,
            -0.2f,
            0.2f
        };
        public const float MaximumResponseMinutes = 10f;
        private const float MinimumRepresentativeThreatSpeedKnots = 300f;
        private const float PlanningCommitMarginMinutes = 1.5f;

        public static float CalculateStationTrackHalfLengthKm(
            AircraftTypeDefinition aircraftType,
            float trackLegMinutes)
        {
            if (aircraftType == null || aircraftType.CruiseSpeedKnots <= 0f)
                return 0f;

            var fullLegKm = aircraftType.CruiseSpeedKnots
                            * KilometersPerNauticalMile
                            * Math.Max(0f, trackLegMinutes)
                            / 60f;
            return fullLegKm * 0.5f;
        }

        public static float GetStationHeadingDegrees(
            Vector3 stationCenterFeet,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm)
        {
            var threatCenter = AirspaceGeometry.TileCenterFeet(
                threatReferenceTileId,
                stationCenterFeet.y);
            var threatDirection = threatCenter - stationCenterFeet;
            threatDirection.y = 0f;
            if (threatDirection.sqrMagnitude < 1f)
                threatDirection = Vector3.forward;
            threatDirection.Normalize();
            return Mathf.Repeat(
                Mathf.Atan2(threatDirection.x, threatDirection.z)
                * Mathf.Rad2Deg,
                360f);
        }

        public static BarcapRacetrackGeometry BuildRacetrack(
            Vector3 stationCenterFeet,
            float headingDegrees,
            float trackHalfLengthKm,
            AircraftTypeDefinition aircraftType)
        {
            var radians = headingDegrees * Mathf.Deg2Rad;
            var forward = new Vector3(
                Mathf.Sin(radians),
                0f,
                Mathf.Cos(radians));
            var right = new Vector3(forward.z, 0f, -forward.x);
            var halfLengthFeet = Math.Max(0f, trackHalfLengthKm)
                                 * AirspaceGeometry.FeetPerKilometer;
            var turnRadiusFeet = aircraftType == null
                ? 0f
                : AirspaceGeometry.TurnRadiusFeet(
                    aircraftType.CruiseSpeedKnots,
                    aircraftType.TurnRateDegreesPerSecond);
            var rearCenter = stationCenterFeet - forward * halfLengthFeet;
            var frontCenter = stationCenterFeet + forward * halfLengthFeet;
            var points = new List<Vector3>
            {
                rearCenter - right * turnRadiusFeet,
                frontCenter - right * turnRadiusFeet
            };
            for (var segment = 1; segment <= HalfTurnSegmentCount; segment++)
            {
                var angle = Mathf.PI * segment / HalfTurnSegmentCount;
                points.Add(frontCenter
                           + (-right * Mathf.Cos(angle)
                              + forward * Mathf.Sin(angle))
                           * turnRadiusFeet);
            }
            points.Add(rearCenter + right * turnRadiusFeet);
            for (var segment = 1; segment < HalfTurnSegmentCount; segment++)
            {
                var angle = Mathf.PI * segment / HalfTurnSegmentCount;
                points.Add(rearCenter
                           + (right * Mathf.Cos(angle)
                              - forward * Mathf.Sin(angle))
                           * turnRadiusFeet);
            }

            return new BarcapRacetrackGeometry(
                stationCenterFeet,
                headingDegrees,
                turnRadiusFeet,
                halfLengthFeet * 4f + 2f * Mathf.PI * turnRadiusFeet,
                points.AsReadOnly());
        }

        public static IReadOnlyList<Vector3> GetDefensiveStationPositions(
            Vector3Int barrierTileId,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm,
            float altitudeFeet,
            float maximumStationDistanceKm)
        {
            if (tileDistanceKm <= 0f)
                return Array.Empty<Vector3>();

            var threatCenter = AirspaceGeometry.TileCenterFeet(
                threatReferenceTileId,
                altitudeFeet);
            var barrierCenter = AirspaceGeometry.TileCenterFeet(
                barrierTileId,
                altitudeFeet);
            var hostileToFriendly = barrierCenter - threatCenter;
            hostileToFriendly.y = 0f;
            if (hostileToFriendly.sqrMagnitude < 1f)
                return new[] { barrierCenter };
            hostileToFriendly.Normalize();
            var tangent = new Vector3(
                -hostileToFriendly.z,
                0f,
                hostileToFriendly.x);
            var maximum = Math.Max(0f, maximumStationDistanceKm);
            if (maximum <= 0.001f)
                return new[] { barrierCenter };

            var result = new List<Vector3>(
                StationDepthFractions.Length * StationLateralFractions.Length
                + 1);
            foreach (var depthFraction in StationDepthFractions)
            {
                var rearwardFeet = hostileToFriendly
                                   * maximum
                                   * depthFraction
                                   * AirspaceGeometry.FeetPerKilometer;
                foreach (var lateralFraction in StationLateralFractions)
                {
                    var lateralFeet = tangent
                                      * maximum
                                      * lateralFraction
                                      * AirspaceGeometry.FeetPerKilometer;
                    result.Add(barrierCenter + rearwardFeet + lateralFeet);
                }
            }

            // Retain one maximum-depth option for an aircraft that must move
            // farther from a newly detected threat after reaching station.
            result.Add(
                barrierCenter
                + hostileToFriendly
                * maximum
                * AirspaceGeometry.FeetPerKilometer);
            return result;
        }

        public static float GetDefensiveStationDepthKm(
            Vector3 stationCenterFeet,
            Vector3Int barrierTileId,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm)
        {
            if (tileDistanceKm <= 0f)
                return 0f;
            var threatCenter = AirspaceGeometry.TileCenterFeet(
                threatReferenceTileId);
            var barrierCenter = AirspaceGeometry.TileCenterFeet(
                barrierTileId);
            var hostileToFriendly = barrierCenter - threatCenter;
            hostileToFriendly.y = 0f;
            if (hostileToFriendly.sqrMagnitude < 1f)
                return 0f;
            hostileToFriendly.Normalize();
            return Math.Max(
                0f,
                Vector3.Dot(stationCenterFeet - barrierCenter, hostileToFriendly)
                / AirspaceGeometry.FeetPerKilometer);
        }

        public static bool CanReachOperationalBarrierFromStation(
            IReadOnlyList<Vector3> racetrackPointsFeet,
            Vector3Int barrierTileId,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm,
            float weaponReleaseStandoffKm,
            float responseRadiusKm)
        {
            var worstStationDistanceKm = GetWorstStationDistanceToOperationalBarrierKm(
                racetrackPointsFeet,
                barrierTileId,
                threatReferenceTileId,
                tileDistanceKm,
                weaponReleaseStandoffKm);
            return worstStationDistanceKm
                   <= Math.Max(0f, responseRadiusKm) + 0.001f;
        }

        public static float GetWorstStationDistanceToOperationalBarrierKm(
            IReadOnlyList<Vector3> racetrackPointsFeet,
            Vector3Int barrierTileId,
            Vector3Int threatReferenceTileId,
            float tileDistanceKm,
            float weaponReleaseStandoffKm)
        {
            var releasePoint = GetOperationalBarrierPointsFeet(
                    new[] { barrierTileId },
                    threatReferenceTileId,
                    tileDistanceKm,
                    weaponReleaseStandoffKm)
                .FirstOrDefault();
            var releaseHorizontal = new Vector2(releasePoint.x, releasePoint.z);
            return (racetrackPointsFeet ?? Array.Empty<Vector3>())
                       .Select(point => Vector2.Distance(
                           new Vector2(point.x, point.z),
                           releaseHorizontal))
                       .DefaultIfEmpty(float.MaxValue)
                       .Max()
                   / AirspaceGeometry.FeetPerKilometer;
        }

        public static IReadOnlyList<Vector3Int> GetContiguousCoveredBarrierRun(
            IReadOnlyList<Vector3Int> orderedBarrierTiles,
            Vector3Int centerTileId,
            IReadOnlyList<Vector3> racetrackPointsFeet,
            BarcapStationCoverage coverage,
            float tileDistanceKm)
        {
            if (orderedBarrierTiles == null
                || orderedBarrierTiles.Count == 0
                || coverage == null)
                return Array.Empty<Vector3Int>();

            var coverable = orderedBarrierTiles
                .Where(tile => CanReachOperationalBarrierFromStation(
                    racetrackPointsFeet,
                    tile,
                    coverage.ThreatReferenceTileId,
                    tileDistanceKm,
                    coverage.WeaponReleaseStandoffKm,
                    coverage.PlannedResponseRadiusKm))
                .ToHashSet();
            var centerIndex = orderedBarrierTiles.ToList().IndexOf(centerTileId);
            if (centerIndex < 0 || !coverable.Contains(centerTileId))
                return Array.Empty<Vector3Int>();

            var start = centerIndex;
            while (start > 0 && coverable.Contains(orderedBarrierTiles[start - 1]))
                start--;
            var end = centerIndex;
            while (end + 1 < orderedBarrierTiles.Count
                   && coverable.Contains(orderedBarrierTiles[end + 1]))
                end++;

            return orderedBarrierTiles
                .Skip(start)
                .Take(end - start + 1)
                .ToList();
        }

        public static float CalculateResponseRadiusKm(
            AircraftTypeDefinition aircraftType,
            float representativeThreatSpeedKnots,
            float threatDistanceToBarrierKm,
            float weaponReleaseStandoffKm =
                BarcapBarrierPlan.DefaultWeaponReleaseStandoffKm,
            float preferredLaunchRangeKm = 0f,
            float weaponPreparationSeconds = 0f,
            float sensorWarningMinutes = 0f,
            float commandDelaySeconds = 0f)
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
                                   / 60f
                                   + Math.Max(0f, commandDelaySeconds) / 60f;
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
                threatReferenceTileId);
            var offsetFeet =
                BarcapBarrierPlan.ResolveWeaponReleaseStandoffKm(
                    weaponReleaseStandoffKm)
                * AirspaceGeometry.FeetPerKilometer;
            return coveredBarrierTiles
                .Select(tile =>
                {
                    var protectedCenter = AirspaceGeometry.TileCenterFeet(
                        tile);
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
                commonTile.Value);
            var plannedApproach = commonCenter
                                  - AirspaceGeometry.TileCenterFeet(
                                      barrier.ThreatReferenceTileId);
            var assignedApproach = commonCenter
                                   - AirspaceGeometry.TileCenterFeet(
                                       coverage.ThreatReferenceTileId);
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
                threatReferenceTileId);
            var protectedCenters = coveredBarrierTiles
                .Select(tile => AirspaceGeometry.TileCenterFeet(
                    tile))
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
                threatReferenceTileId);
            var protectedMidpoint = coveredBarrierTiles
                .Select(tile => AirspaceGeometry.TileCenterFeet(
                    tile))
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
                threatReferenceTileId);
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
                        tile)
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
