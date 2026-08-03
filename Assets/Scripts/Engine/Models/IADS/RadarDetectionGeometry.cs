using Engine.Service;
using Models.Module;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    public enum RadarRangeConstraint
    {
        None = 0,
        EquipmentRange = 1,
        RadarHorizon = 2
    }

    public readonly struct RadarDetectionGeometry
    {
        public readonly float RadarAltitudeMeters;
        public readonly float HorizontalDistanceKm;
        public readonly float SlantDistanceKm;
        public readonly float AuthoredRangeKm;
        public readonly float DetectabilityAdjustedRangeKm;
        public readonly float RadarHorizonKm;
        public readonly float EquipmentRangeFraction;
        public readonly float RadarHorizonFraction;
        public readonly float RangeFactor;
        public readonly RadarRangeConstraint LimitingConstraint;

        public bool IsWithinEquipmentRange => EquipmentRangeFraction <= 1f;
        public bool IsWithinRadarHorizon => RadarHorizonFraction <= 1f;

        public RadarDetectionGeometry(
            float radarAltitudeMeters,
            float horizontalDistanceKm,
            float slantDistanceKm,
            float authoredRangeKm,
            float detectabilityAdjustedRangeKm,
            float radarHorizonKm,
            float equipmentRangeFraction,
            float radarHorizonFraction,
            float rangeFactor,
            RadarRangeConstraint limitingConstraint)
        {
            RadarAltitudeMeters = radarAltitudeMeters;
            HorizontalDistanceKm = horizontalDistanceKm;
            SlantDistanceKm = slantDistanceKm;
            AuthoredRangeKm = authoredRangeKm;
            DetectabilityAdjustedRangeKm = detectabilityAdjustedRangeKm;
            RadarHorizonKm = radarHorizonKm;
            EquipmentRangeFraction = equipmentRangeFraction;
            RadarHorizonFraction = radarHorizonFraction;
            RangeFactor = rangeFactor;
            LimitingConstraint = limitingConstraint;
        }
    }

    public static class RadarDetectionGeometryCalculator
    {
        private const float StandardRadarHorizonKmPerSqrtMeter = 4.12f;
        private const float MetersPerKilometer = 1000f;

        public static RadarDetectionGeometry Calculate(
            RadarAirDefenseComponentDefinition definition,
            Vector3 siteGroundPositionFeet,
            Vector3 targetPositionFeet,
            float targetRadarDetectability)
        {
            if (definition == null)
                return default;

            var metersPerFoot = MetersPerKilometer
                                / AirspaceGeometry.FeetPerKilometer;
            var siteGroundAltitudeMeters = Mathf.Max(
                0f,
                siteGroundPositionFeet.y * metersPerFoot);
            var radarAltitudeMeters = siteGroundAltitudeMeters
                                      + definition.AntennaHeightMeters;
            var targetAltitudeMeters = Mathf.Max(
                0f,
                targetPositionFeet.y * metersPerFoot);

            var deltaXFeet = targetPositionFeet.x - siteGroundPositionFeet.x;
            var deltaZFeet = targetPositionFeet.z - siteGroundPositionFeet.z;
            var horizontalDistanceKm = Mathf.Sqrt(
                                           deltaXFeet * deltaXFeet
                                           + deltaZFeet * deltaZFeet)
                                       / AirspaceGeometry.FeetPerKilometer;

            var radarPositionFeet = siteGroundPositionFeet;
            radarPositionFeet.y += definition.AntennaHeightMeters
                                   / metersPerFoot;
            var slantDistanceKm = Vector3.Distance(
                                      radarPositionFeet,
                                      targetPositionFeet)
                                  / AirspaceGeometry.FeetPerKilometer;
            var adjustedRangeKm = definition
                .CalculateDetectabilityAdjustedRangeKm(
                    targetRadarDetectability);
            var radarHorizonKm = StandardRadarHorizonKmPerSqrtMeter
                                 * (Mathf.Sqrt(radarAltitudeMeters)
                                    + Mathf.Sqrt(targetAltitudeMeters));
            var equipmentRangeFraction = DivideDistanceByLimit(
                slantDistanceKm,
                adjustedRangeKm);
            var radarHorizonFraction = DivideDistanceByLimit(
                horizontalDistanceKm,
                radarHorizonKm);
            var limitingFraction = Mathf.Max(
                equipmentRangeFraction,
                radarHorizonFraction);
            var rangeFactor = float.IsInfinity(limitingFraction)
                ? 0f
                : Mathf.Clamp01(1f - limitingFraction);
            var limitingConstraint = radarHorizonFraction
                                     > equipmentRangeFraction
                ? RadarRangeConstraint.RadarHorizon
                : RadarRangeConstraint.EquipmentRange;

            return new RadarDetectionGeometry(
                radarAltitudeMeters,
                horizontalDistanceKm,
                slantDistanceKm,
                definition.DetectionRangeKm,
                adjustedRangeKm,
                radarHorizonKm,
                equipmentRangeFraction,
                radarHorizonFraction,
                rangeFactor,
                limitingConstraint);
        }

        private static float DivideDistanceByLimit(
            float distanceKm,
            float limitKm)
        {
            if (limitKm > 0f)
                return Mathf.Max(0f, distanceKm) / limitKm;

            return distanceKm <= 0f ? 0f : float.PositiveInfinity;
        }
    }
}
