using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    public static class AirspaceGeometry
    {
        public const float FeetPerKilometer = CampaignMapCoordinates.FeetPerKilometer;
        public const float FeetPerNauticalMile = 6076.1155f;
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
            float altitudeFeet = 0f)
        {
            return CampaignMapCoordinates.TileCenterFeet(
                cubeCoordinate,
                altitudeFeet);
        }

        public static Vector3Int TileCoordinateFromPositionFeet(Vector3 positionFeet)
        {
            return CampaignMapCoordinates.TileCoordinateFromPositionFeet(
                positionFeet);
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

        public static float TurnRadiusFeet(
            float speedKnots,
            float turnRateDegreesPerSecond)
        {
            var feetPerSecond = Math.Max(1f, speedKnots)
                                * FeetPerNauticalMile / 3600f;
            var turnRateRadians = Math.Max(0.1f, turnRateDegreesPerSecond)
                                  * Mathf.Deg2Rad;
            return feetPerSecond / turnRateRadians;
        }

        public static float SamManeuverClearanceFeet(
            AircraftTypeDefinition aircraftType,
            float speedKnots)
        {
            return TurnRadiusFeet(
                       speedKnots,
                       aircraftType.TurnRateDegreesPerSecond)
                   + 1000f;
        }

        public static float ConservativeSamManeuverClearanceFeet(
            AircraftTypeDefinition aircraftType)
        {
            if (aircraftType == null)
                return 0f;

            return SamManeuverClearanceFeet(
                aircraftType,
                Math.Max(
                    aircraftType.CruiseSpeedKnots,
                    aircraftType.CombatSpeedKnots));
        }
    }

    public sealed class KnownSamThreatEnvelope
    {
        private const float GeometryToleranceFeet = 1f;
        private const float ParameterTolerance = 0.00001f;

        public Guid SiteId { get; }
        public Vector3 CenterFeet { get; }
        public float MinimumSlantRangeFeet { get; }
        public float MaximumSlantRangeFeet { get; }
        public float MinimumAltitudeFeet { get; }
        public float MaximumAltitudeFeet { get; }

        public KnownSamThreatEnvelope(
            Guid siteId,
            Vector3 centerFeet,
            float maximumSlantRangeFeet,
            float minimumAltitudeFeet,
            float maximumAltitudeFeet)
            : this(
                siteId,
                centerFeet,
                0f,
                maximumSlantRangeFeet,
                minimumAltitudeFeet,
                maximumAltitudeFeet)
        {
        }

        public KnownSamThreatEnvelope(
            Guid siteId,
            Vector3 centerFeet,
            float minimumSlantRangeFeet,
            float maximumSlantRangeFeet,
            float minimumAltitudeFeet,
            float maximumAltitudeFeet)
        {
            SiteId = siteId;
            CenterFeet = centerFeet;
            MinimumSlantRangeFeet = Math.Max(0f, minimumSlantRangeFeet);
            MaximumSlantRangeFeet = Math.Max(
                MinimumSlantRangeFeet,
                maximumSlantRangeFeet);
            MinimumAltitudeFeet = Math.Max(0f, minimumAltitudeFeet);
            MaximumAltitudeFeet = Math.Max(
                MinimumAltitudeFeet,
                maximumAltitudeFeet);
        }

        public bool Contains(
            Vector3 positionFeet,
            float maneuverClearanceFeet = 0f)
        {
            var protectedMinimumRangeFeet = Math.Max(
                0f,
                MinimumSlantRangeFeet
                - Math.Max(0f, maneuverClearanceFeet));
            var protectedRangeFeet = MaximumSlantRangeFeet
                                     + Math.Max(0f, maneuverClearanceFeet);
            var distanceFeet = Vector3.Distance(positionFeet, CenterFeet);
            return positionFeet.y >= MinimumAltitudeFeet - GeometryToleranceFeet
                   && positionFeet.y <= MaximumAltitudeFeet + GeometryToleranceFeet
                   && distanceFeet
                   >= protectedMinimumRangeFeet - GeometryToleranceFeet
                   && distanceFeet
                   <= protectedRangeFeet + GeometryToleranceFeet;
        }

        public bool IntersectsSegment(
            Vector3 startFeet,
            Vector3 endFeet,
            float maneuverClearanceFeet = 0f)
        {
            if (!TryGetAltitudeInterval(startFeet, endFeet, out var first, out var last))
                return false;

            var direction = endFeet - startFeet;
            var lengthSquared = direction.sqrMagnitude;
            if (lengthSquared <= 0.01f)
                return Contains(startFeet, maneuverClearanceFeet);

            var closest = Mathf.Clamp(
                Vector3.Dot(CenterFeet - startFeet, direction) / lengthSquared,
                first,
                last);
            var closestPoint = startFeet + direction * closest;
            var firstPoint = startFeet + direction * first;
            var lastPoint = startFeet + direction * last;
            var protectedMinimumRangeFeet = Math.Max(
                0f,
                MinimumSlantRangeFeet
                - Math.Max(0f, maneuverClearanceFeet));
            var protectedRangeFeet = MaximumSlantRangeFeet
                                     + Math.Max(0f, maneuverClearanceFeet);
            var closestDistanceFeet =
                Vector3.Distance(closestPoint, CenterFeet);
            var furthestDistanceFeet = Math.Max(
                Vector3.Distance(firstPoint, CenterFeet),
                Vector3.Distance(lastPoint, CenterFeet));
            return closestDistanceFeet
                   <= protectedRangeFeet + GeometryToleranceFeet
                   && furthestDistanceFeet
                   >= protectedMinimumRangeFeet - GeometryToleranceFeet;
        }

        public bool TryGetSegmentIntersectionInterval(
            Vector3 startFeet,
            Vector3 endFeet,
            out float entryParameter,
            out float exitParameter,
            float maneuverClearanceFeet = 0f)
        {
            entryParameter = 0f;
            exitParameter = 0f;
            if (!TryGetAltitudeInterval(
                    startFeet,
                    endFeet,
                    out var altitudeEntry,
                    out var altitudeExit))
                return false;

            var protectedMaximumRangeFeet = MaximumSlantRangeFeet
                                            + Math.Max(
                                                0f,
                                                maneuverClearanceFeet);
            if (!TryGetSphereIntersectionInterval(
                    startFeet,
                    endFeet,
                    protectedMaximumRangeFeet,
                    out var outerEntry,
                    out var outerExit))
                return false;

            entryParameter = Mathf.Max(altitudeEntry, outerEntry);
            exitParameter = Mathf.Min(altitudeExit, outerExit);
            if (entryParameter > exitParameter)
                return false;

            var protectedMinimumRangeFeet = Math.Max(
                0f,
                MinimumSlantRangeFeet
                - Math.Max(0f, maneuverClearanceFeet));
            if (protectedMinimumRangeFeet <= GeometryToleranceFeet
                || !TryGetSphereIntersectionInterval(
                    startFeet,
                    endFeet,
                    protectedMinimumRangeFeet,
                    out var innerEntry,
                    out var innerExit))
                return true;

            innerEntry = Mathf.Max(entryParameter, innerEntry);
            innerExit = Mathf.Min(exitParameter, innerExit);
            if (innerEntry > innerExit)
                return true;
            if (innerEntry <= entryParameter + ParameterTolerance
                && innerExit >= exitParameter - ParameterTolerance)
                return false;

            if (innerEntry <= entryParameter + ParameterTolerance)
                entryParameter = innerExit;
            else if (innerExit >= exitParameter - ParameterTolerance)
                exitParameter = innerEntry;

            return entryParameter <= exitParameter;
        }

        public float ClosestSegmentParameterWithinEnvelopeAltitude(
            Vector3 startFeet,
            Vector3 endFeet)
        {
            if (!TryGetAltitudeInterval(startFeet, endFeet, out var first, out var last))
                return 0.5f;

            var direction = endFeet - startFeet;
            var lengthSquared = direction.sqrMagnitude;
            if (lengthSquared <= 0.01f)
                return first;

            return Mathf.Clamp(
                Vector3.Dot(CenterFeet - startFeet, direction) / lengthSquared,
                first,
                last);
        }

        public float HorizontalRadiusFeetAtAltitude(
            float altitudeFeet,
            float maneuverClearanceFeet = 0f)
        {
            if (altitudeFeet < MinimumAltitudeFeet - GeometryToleranceFeet
                || altitudeFeet > MaximumAltitudeFeet + GeometryToleranceFeet)
                return 0f;

            var verticalDistance = altitudeFeet - CenterFeet.y;
            var protectedRangeFeet = MaximumSlantRangeFeet
                                     + Math.Max(0f, maneuverClearanceFeet);
            var squared = protectedRangeFeet * protectedRangeFeet
                          - verticalDistance * verticalDistance;
            return squared <= 0f ? 0f : Mathf.Sqrt(squared);
        }

        private bool TryGetAltitudeInterval(
            Vector3 startFeet,
            Vector3 endFeet,
            out float first,
            out float last)
        {
            first = 0f;
            last = 1f;
            var altitudeDelta = endFeet.y - startFeet.y;
            if (Math.Abs(altitudeDelta) <= 0.01f)
            {
                return startFeet.y >= MinimumAltitudeFeet - GeometryToleranceFeet
                       && startFeet.y <= MaximumAltitudeFeet + GeometryToleranceFeet;
            }

            var minimumParameter =
                (MinimumAltitudeFeet - startFeet.y) / altitudeDelta;
            var maximumParameter =
                (MaximumAltitudeFeet - startFeet.y) / altitudeDelta;
            if (minimumParameter > maximumParameter)
            {
                var swap = minimumParameter;
                minimumParameter = maximumParameter;
                maximumParameter = swap;
            }

            first = Mathf.Max(0f, minimumParameter);
            last = Mathf.Min(1f, maximumParameter);
            return first <= last;
        }

        private bool TryGetSphereIntersectionInterval(
            Vector3 startFeet,
            Vector3 endFeet,
            float radiusFeet,
            out float first,
            out float last)
        {
            first = 0f;
            last = 0f;
            if (radiusFeet < 0f)
                return false;

            var direction = endFeet - startFeet;
            var lengthSquared = direction.sqrMagnitude;
            if (lengthSquared <= 0.01f)
            {
                if (Vector3.Distance(startFeet, CenterFeet)
                    > radiusFeet + GeometryToleranceFeet)
                    return false;
                last = 1f;
                return true;
            }

            var offset = startFeet - CenterFeet;
            var b = 2f * Vector3.Dot(offset, direction);
            var c = offset.sqrMagnitude - radiusFeet * radiusFeet;
            var discriminant = b * b - 4f * lengthSquared * c;
            if (discriminant < 0f)
                return false;

            var root = Mathf.Sqrt(discriminant);
            var denominator = 2f * lengthSquared;
            first = Mathf.Max(0f, (-b - root) / denominator);
            last = Mathf.Min(1f, (-b + root) / denominator);
            return first <= last;
        }
    }

    public sealed class KnownSamThreatAssessment
    {
        public const float DefaultSafetyMarginKm = 3f;

        private readonly IReadOnlyDictionary<Guid, AirDefenseComponentDefinition>
            componentDefinitions;
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition>
            ordnanceDefinitions;

        public KnownSamThreatAssessment(
            IEnumerable<AirDefenseComponentDefinition> componentDefinitions,
            IEnumerable<OrdnanceTypeDefinition> ordnanceDefinitions)
        {
            this.componentDefinitions =
                (componentDefinitions ?? Array.Empty<AirDefenseComponentDefinition>())
                .Where(definition => definition != null)
                .GroupBy(definition => definition.SamComponentDefinitionId)
                .ToDictionary(group => group.Key, group => group.First());
            this.ordnanceDefinitions =
                (ordnanceDefinitions ?? Array.Empty<OrdnanceTypeDefinition>())
                .Where(definition => definition != null)
                .GroupBy(definition => definition.OrdnanceTypeDefinitionId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        public IReadOnlyList<KnownSamThreatEnvelope> BuildKnownThreats(
            AllianceIntelligencePicture picture)
        {
            if (picture?.HostileAirDefenseSites == null)
                return Array.Empty<KnownSamThreatEnvelope>();

            var threats = new List<KnownSamThreatEnvelope>();
            foreach (var report in picture.HostileAirDefenseSites
                         .Where(report => report != null
                                          && report.InformationQuality > 0f
                                          && !report.IsDisabled
                                          && !report.IsDestroyed)
                         .OrderBy(report => report.SiteId))
            {
                var components = report.Components
                                 ?? new List<AirDefenseComponentIntelligenceReport>();
                var radars = components
                    .Where(component => component != null
                                        && !component.IsDamaged
                                        && componentDefinitions.TryGetValue(
                                            component.SamComponentDefinitionId,
                                            out var definition)
                                        && definition is RadarAirDefenseComponentDefinition
                                        {
                                            ProvidesWeaponQualityTrack: true
                                        })
                    .Select(component =>
                        (RadarAirDefenseComponentDefinition)
                        componentDefinitions[component.SamComponentDefinitionId])
                    .GroupBy(radar => radar.SamComponentDefinitionId)
                    .Select(group => group.First())
                    .OrderBy(radar => radar.SamComponentDefinitionId)
                    .ToList();
                if (radars.Count == 0)
                    continue;

                var center = report.PositionFeet;

                foreach (var launcherGroup in components
                             .Where(component => component != null
                                                 && !component.IsDamaged
                                                 && component.ReadyRounds
                                                 + component.ReserveRounds > 0)
                             .GroupBy(component =>
                                 component.SamComponentDefinitionId)
                             .OrderBy(group => group.Key))
                {
                    if (!componentDefinitions.TryGetValue(
                            launcherGroup.Key,
                            out var componentDefinition)
                        || componentDefinition
                        is not LauncherAirDefenseComponentDefinition launcher
                        || launcher.SurfaceToAirOrdnanceTypeDefinitionId
                        == Guid.Empty
                        || !ordnanceDefinitions.TryGetValue(
                            launcher.SurfaceToAirOrdnanceTypeDefinitionId,
                            out var ordnance)
                        || ordnance.EmploymentCategory
                        != OrdnanceEmploymentCategory.SurfaceToAir)
                        continue;

                    var minimumAltitudeFeet = Math.Max(
                        launcher.MinEngagementAltitudeMeters
                        * AirspaceGeometry.FeetPerKilometer / 1000f,
                        ordnance.MinimumTargetAltitudeFeet);
                    var minimumRangeKm = Math.Max(
                        launcher.MinEngagementRangeKm,
                        ordnance.MinimumRangeKm);
                    foreach (var radar in radars)
                    {
                        var maximumRangeKm = Math.Min(
                            radar.DetectionRangeKm,
                            Math.Min(
                                launcher.MaxEngagementRangeKm,
                                ordnance.MaximumRangeKm));
                        var maximumRadarAltitudeFeet =
                            radar.MaxAltitudeMeters
                            * AirspaceGeometry.FeetPerKilometer / 1000f;
                        var maximumAltitudeFeet = Math.Min(
                            maximumRadarAltitudeFeet,
                            Math.Min(
                                launcher.MaxEngagementAltitudeMeters
                                * AirspaceGeometry.FeetPerKilometer / 1000f,
                                ordnance.MaximumTargetAltitudeFeet));
                        if (maximumRangeKm <= 0f
                            || maximumRangeKm < minimumRangeKm
                            || maximumAltitudeFeet < minimumAltitudeFeet)
                            continue;

                        threats.Add(new KnownSamThreatEnvelope(
                            report.SiteId,
                            center,
                            Math.Max(
                                0f,
                                minimumRangeKm - DefaultSafetyMarginKm)
                            * AirspaceGeometry.FeetPerKilometer,
                            (maximumRangeKm + DefaultSafetyMarginKm)
                            * AirspaceGeometry.FeetPerKilometer,
                            minimumAltitudeFeet,
                            maximumAltitudeFeet));
                    }
                }
            }

            return threats;
        }
    }

    public static class KnownSamThreatGeometry
    {
        private const int MaximumBypassWaypoints = 16;
        private const int MaximumOffsetAttempts = 8;
        private const float MinimumBypassClearanceFeet = 1000f;

        public static bool IsPathSafe(
            IReadOnlyList<Vector3> points,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            out Guid blockingSiteId)
        {
            return IsPathSafe(
                points,
                threats,
                0f,
                out blockingSiteId);
        }

        public static bool IsPathSafe(
            IReadOnlyList<Vector3> points,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            float maneuverClearanceFeet,
            out Guid blockingSiteId)
        {
            blockingSiteId = Guid.Empty;
            if (points == null || points.Count == 0 || threats == null)
                return true;

            foreach (var point in points)
            {
                var containing = threats
                    .Where(threat => threat != null
                                     && threat.Contains(
                                         point,
                                         maneuverClearanceFeet))
                    .OrderBy(threat => threat.SiteId)
                    .FirstOrDefault();
                if (containing == null)
                    continue;
                blockingSiteId = containing.SiteId;
                return false;
            }

            for (var index = 1; index < points.Count; index++)
            {
                var blocking = threats
                    .Where(threat => threat != null
                                     && threat.IntersectsSegment(
                                         points[index - 1],
                                         points[index],
                                         maneuverClearanceFeet))
                    .OrderBy(threat => threat.SiteId)
                    .FirstOrDefault();
                if (blocking == null)
                    continue;
                blockingSiteId = blocking.SiteId;
                return false;
            }

            return true;
        }

        public static bool TryBuildAvoidingPath(
            IReadOnlyList<Vector3> controlPoints,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            Guid routeKey,
            out IReadOnlyList<Vector3> path)
        {
            return TryBuildAvoidingPath(
                controlPoints,
                threats,
                routeKey,
                0f,
                out path);
        }

        public static bool TryBuildAvoidingPath(
            IReadOnlyList<Vector3> controlPoints,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            Guid routeKey,
            float maneuverClearanceFeet,
            out IReadOnlyList<Vector3> path)
        {
            path = Array.Empty<Vector3>();
            if (controlPoints == null || controlPoints.Count < 2)
                return false;
            if (threats == null || threats.Count == 0)
            {
                path = controlPoints.ToList();
                return true;
            }

            var result = new List<Vector3> { controlPoints[0] };
            for (var index = 1; index < controlPoints.Count; index++)
            {
                if (!TryBuildAvoidingWaypoints(
                        controlPoints[index - 1],
                        controlPoints[index],
                        threats,
                        routeKey,
                        maneuverClearanceFeet,
                        out var segment))
                    return false;

                result.AddRange(segment);
            }

            path = result;
            return true;
        }

        public static bool TryBuildAvoidingWaypoints(
            Vector3 startFeet,
            Vector3 endFeet,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            Guid routeKey,
            out IReadOnlyList<Vector3> waypointsIncludingEnd)
        {
            return TryBuildAvoidingWaypoints(
                startFeet,
                endFeet,
                threats,
                routeKey,
                0f,
                out waypointsIncludingEnd);
        }

        public static bool TryBuildAvoidingWaypoints(
            Vector3 startFeet,
            Vector3 endFeet,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            Guid routeKey,
            float maneuverClearanceFeet,
            out IReadOnlyList<Vector3> waypointsIncludingEnd)
        {
            waypointsIncludingEnd = Array.Empty<Vector3>();
            if (threats == null || threats.Count == 0)
            {
                waypointsIncludingEnd = new[] { endFeet };
                return true;
            }
            if (threats.Any(threat =>
                    threat != null
                    && (threat.Contains(startFeet, maneuverClearanceFeet)
                        || threat.Contains(endFeet, maneuverClearanceFeet))))
                return false;

            var points = new List<Vector3> { startFeet, endFeet };
            while (true)
            {
                if (!TryFindFirstIntersection(
                        points,
                        threats,
                        maneuverClearanceFeet,
                        out var segmentIndex,
                        out var blockingThreat))
                {
                    waypointsIncludingEnd = points.Skip(1).ToList();
                    return true;
                }
                if (points.Count - 2 >= MaximumBypassWaypoints)
                    return false;

                if (!TryCreateDetourPoint(
                        points[segmentIndex],
                        points[segmentIndex + 1],
                        blockingThreat,
                        threats,
                        routeKey,
                        maneuverClearanceFeet,
                        out var detour))
                    return false;

                points.Insert(segmentIndex + 1, detour);
            }
        }

        public static bool TryCreateAvoidanceAimPoint(
            Vector3 startFeet,
            Vector3 desiredAimPointFeet,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            Guid routeKey,
            out Vector3 avoidanceAimPoint,
            out Guid blockingSiteId)
        {
            return TryCreateAvoidanceAimPoint(
                startFeet,
                desiredAimPointFeet,
                threats,
                routeKey,
                0f,
                out avoidanceAimPoint,
                out blockingSiteId);
        }

        public static bool TryCreateAvoidanceAimPoint(
            Vector3 startFeet,
            Vector3 desiredAimPointFeet,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            Guid routeKey,
            float maneuverClearanceFeet,
            out Vector3 avoidanceAimPoint,
            out Guid blockingSiteId)
        {
            avoidanceAimPoint = default;
            blockingSiteId = Guid.Empty;
            if (threats == null || threats.Count == 0)
                return false;

            var blocking = threats
                .Where(threat => threat != null
                                 && !threat.Contains(
                                     startFeet,
                                     maneuverClearanceFeet)
                                 && threat.IntersectsSegment(
                                     startFeet,
                                     desiredAimPointFeet,
                                     maneuverClearanceFeet))
                .OrderBy(threat =>
                    threat.ClosestSegmentParameterWithinEnvelopeAltitude(
                        startFeet,
                        desiredAimPointFeet))
                .ThenBy(threat => threat.SiteId)
                .FirstOrDefault();
            if (blocking == null)
                return false;

            blockingSiteId = blocking.SiteId;
            if (!TryCreateDetourPoint(
                    startFeet,
                    desiredAimPointFeet,
                    blocking,
                    threats,
                    routeKey,
                    maneuverClearanceFeet,
                    out avoidanceAimPoint))
                return false;

            // Tactical movement commits to this first leg immediately. A route
            // planner can resolve later intersections one at a time, but a live
            // command must never solve one SAM conflict by steering through another.
            var firstLegEnd = avoidanceAimPoint;
            return threats.All(threat =>
                threat == null
                || !threat.IntersectsSegment(
                    startFeet,
                    firstLegEnd,
                    maneuverClearanceFeet));
        }

        public static bool TryCreateEgressAimPoint(
            Vector3 positionFeet,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            Guid routeKey,
            out Vector3 egressAimPoint)
        {
            return TryCreateEgressAimPoint(
                positionFeet,
                threats,
                routeKey,
                0f,
                out egressAimPoint);
        }

        public static bool TryCreateEgressAimPoint(
            Vector3 positionFeet,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            Guid routeKey,
            float maneuverClearanceFeet,
            out Vector3 egressAimPoint)
        {
            egressAimPoint = positionFeet;
            if (threats == null || threats.Count == 0)
                return false;

            var containing = threats
                .Where(threat => threat != null
                                 && threat.Contains(
                                     positionFeet,
                                     maneuverClearanceFeet))
                .OrderBy(threat => threat.SiteId)
                .ToList();
            if (containing.Count == 0)
                return false;
            var initialContainingSiteIds = containing
                .Select(threat => threat.SiteId)
                .ToHashSet();

            var candidate = positionFeet;
            for (var attempt = 0; attempt < MaximumOffsetAttempts; attempt++)
            {
                containing = threats
                    .Where(threat => threat != null
                                     && threat.Contains(
                                         candidate,
                                         maneuverClearanceFeet))
                    .OrderBy(threat => threat.SiteId)
                    .ToList();
                if (containing.Count == 0)
                {
                    if (!IsEgressSegmentSafe(
                            positionFeet,
                            candidate,
                            threats,
                            initialContainingSiteIds,
                            maneuverClearanceFeet))
                        return false;
                    egressAimPoint = candidate;
                    return true;
                }

                var correction = Vector2.zero;
                foreach (var threat in containing)
                {
                    var fromCenter = new Vector2(
                        candidate.x - threat.CenterFeet.x,
                        candidate.z - threat.CenterFeet.z);
                    if (fromCenter.sqrMagnitude <= 1f)
                        fromCenter = StableHorizontalDirection(routeKey, threat.SiteId);
                    var requiredDistance =
                        threat.HorizontalRadiusFeetAtAltitude(
                            candidate.y,
                            maneuverClearanceFeet)
                        + MinimumBypassClearanceFeet;
                    correction += fromCenter.normalized
                                  * Math.Max(
                                      MinimumBypassClearanceFeet,
                                      requiredDistance - fromCenter.magnitude);
                }

                if (correction.sqrMagnitude <= 1f)
                    correction = StableHorizontalDirection(
                        routeKey,
                        containing[0].SiteId)
                                 * MinimumBypassClearanceFeet;
                candidate += new Vector3(correction.x, 0f, correction.y);
            }

            if (threats.All(threat =>
                    threat == null
                    || !threat.Contains(candidate, maneuverClearanceFeet))
                && IsEgressSegmentSafe(
                    positionFeet,
                    candidate,
                    threats,
                    initialContainingSiteIds,
                    maneuverClearanceFeet))
            {
                egressAimPoint = candidate;
                return true;
            }

            return false;
        }

        private static bool IsEgressSegmentSafe(
            Vector3 startFeet,
            Vector3 endFeet,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            IReadOnlyCollection<Guid> initialContainingSiteIds,
            float maneuverClearanceFeet)
        {
            return threats.All(threat =>
                threat == null
                || initialContainingSiteIds.Contains(threat.SiteId)
                || !threat.IntersectsSegment(
                    startFeet,
                    endFeet,
                    maneuverClearanceFeet));
        }

        private static bool TryFindFirstIntersection(
            IReadOnlyList<Vector3> points,
            IReadOnlyList<KnownSamThreatEnvelope> threats,
            float maneuverClearanceFeet,
            out int segmentIndex,
            out KnownSamThreatEnvelope blockingThreat)
        {
            segmentIndex = -1;
            blockingThreat = null;
            var earliest = float.MaxValue;
            for (var index = 0; index < points.Count - 1; index++)
            {
                foreach (var threat in threats
                             .Where(threat => threat != null
                                              && threat.IntersectsSegment(
                                                  points[index],
                                                  points[index + 1],
                                                  maneuverClearanceFeet))
                             .OrderBy(threat => threat.SiteId))
                {
                    var parameter =
                        threat.ClosestSegmentParameterWithinEnvelopeAltitude(
                            points[index],
                            points[index + 1]);
                    var routeProgress = index + parameter;
                    if (routeProgress >= earliest)
                        continue;
                    earliest = routeProgress;
                    segmentIndex = index;
                    blockingThreat = threat;
                }
            }

            return blockingThreat != null;
        }

        private static bool TryCreateDetourPoint(
            Vector3 startFeet,
            Vector3 endFeet,
            KnownSamThreatEnvelope blockingThreat,
            IReadOnlyList<KnownSamThreatEnvelope> allThreats,
            Guid routeKey,
            float maneuverClearanceFeet,
            out Vector3 detour)
        {
            detour = default;
            var horizontal = new Vector2(
                endFeet.x - startFeet.x,
                endFeet.z - startFeet.z);
            if (horizontal.sqrMagnitude <= 1f)
                return false;

            var parameter =
                blockingThreat.ClosestSegmentParameterWithinEnvelopeAltitude(
                    startFeet,
                    endFeet);
            var altitude = Mathf.Lerp(startFeet.y, endFeet.y, parameter);
            var radius =
                blockingThreat.HorizontalRadiusFeetAtAltitude(
                    altitude,
                    maneuverClearanceFeet);
            if (radius <= 0f)
                return false;

            var direction = horizontal.normalized;
            var left = new Vector2(-direction.y, direction.x);
            var preferredSide = StableSide(routeKey, blockingThreat.SiteId);
            DetourCandidate best = null;
            foreach (var side in new[] { preferredSide, -preferredSide })
            {
                for (var attempt = 0; attempt < MaximumOffsetAttempts; attempt++)
                {
                    var clearance = MinimumBypassClearanceFeet
                                    + attempt * Math.Max(
                                        MinimumBypassClearanceFeet,
                                        radius * 0.25f);
                    var offset = left * side * (radius + clearance);
                    var candidate = new Vector3(
                        blockingThreat.CenterFeet.x + offset.x,
                        altitude,
                        blockingThreat.CenterFeet.z + offset.y);
                    if (allThreats.Any(threat =>
                            threat != null
                            && threat.Contains(
                                candidate,
                                maneuverClearanceFeet))
                        || blockingThreat.IntersectsSegment(
                            startFeet,
                            candidate,
                            maneuverClearanceFeet)
                        || blockingThreat.IntersectsSegment(
                            candidate,
                            endFeet,
                            maneuverClearanceFeet))
                        continue;

                    var intersections = allThreats.Count(threat =>
                        threat != null
                        && (threat.IntersectsSegment(
                                startFeet,
                                candidate,
                                maneuverClearanceFeet)
                            || threat.IntersectsSegment(
                                candidate,
                                endFeet,
                                maneuverClearanceFeet)));
                    var distance = Vector3.Distance(startFeet, candidate)
                                   + Vector3.Distance(candidate, endFeet);
                    var current = new DetourCandidate(
                        candidate,
                        intersections,
                        distance,
                        side == preferredSide);
                    if (best == null || current.IsBetterThan(best))
                        best = current;
                    break;
                }
            }

            if (best == null)
                return false;
            detour = best.Point;
            return true;
        }

        private static float StableSide(Guid routeKey, Guid siteId)
        {
            var parity = 0;
            foreach (var value in routeKey.ToByteArray())
                parity ^= value;
            foreach (var value in siteId.ToByteArray())
                parity ^= value;
            return (parity & 1) == 0 ? 1f : -1f;
        }

        private static Vector2 StableHorizontalDirection(
            Guid routeKey,
            Guid siteId)
        {
            var bytes = routeKey.ToByteArray();
            var siteBytes = siteId.ToByteArray();
            var seed = 0;
            for (var index = 0; index < bytes.Length; index++)
                seed = seed * 31 + bytes[index] + siteBytes[index];
            var angle = (seed & 0xFFFF) / 65535f * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private sealed class DetourCandidate
        {
            public readonly Vector3 Point;
            private readonly int intersections;
            private readonly float distance;
            private readonly bool preferredSide;

            public DetourCandidate(
                Vector3 point,
                int intersections,
                float distance,
                bool preferredSide)
            {
                Point = point;
                this.intersections = intersections;
                this.distance = distance;
                this.preferredSide = preferredSide;
            }

            public bool IsBetterThan(DetourCandidate other)
            {
                if (intersections != other.intersections)
                    return intersections < other.intersections;
                if (Math.Abs(distance - other.distance) > 1f)
                    return distance < other.distance;
                return preferredSide && !other.preferredSide;
            }
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
