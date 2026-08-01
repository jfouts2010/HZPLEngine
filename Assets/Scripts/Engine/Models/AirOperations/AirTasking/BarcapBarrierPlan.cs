using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class BarcapBarrierPlan
    {
        public const float DefaultWeaponReleaseStandoffKm = 18.52f;

        public Guid BarrierId = Guid.NewGuid();
        public List<Vector3Int> BarrierTileIds = new List<Vector3Int>();
        public Vector3Int ThreatReferenceTileId;
        public float RepresentativeThreatSpeedKnots;
        public float WeaponReleaseStandoffKm =
            DefaultWeaponReleaseStandoffKm;
        public bool IsSupplemental;
        public int ProtectedFrontlineDivisionCount;
        public int ProtectedActiveAirportCount;
        public int ProtectedReserveAirportCount;
        public int EstimatedAircraftDemand;

        public BarcapBarrierPlan Clone()
        {
            return new BarcapBarrierPlan
            {
                BarrierId = BarrierId,
                BarrierTileIds = new List<Vector3Int>(
                    BarrierTileIds ?? Enumerable.Empty<Vector3Int>()),
                ThreatReferenceTileId = ThreatReferenceTileId,
                RepresentativeThreatSpeedKnots = Math.Max(
                    0f,
                    RepresentativeThreatSpeedKnots),
                WeaponReleaseStandoffKm = ResolveWeaponReleaseStandoffKm(
                    WeaponReleaseStandoffKm),
                IsSupplemental = IsSupplemental,
                ProtectedFrontlineDivisionCount = Math.Max(
                    0,
                    ProtectedFrontlineDivisionCount),
                ProtectedActiveAirportCount = Math.Max(
                    0,
                    ProtectedActiveAirportCount),
                ProtectedReserveAirportCount = Math.Max(
                    0,
                    ProtectedReserveAirportCount),
                EstimatedAircraftDemand = Math.Max(0, EstimatedAircraftDemand)
            };
        }

        public static float ResolveWeaponReleaseStandoffKm(float value)
        {
            return value > 0f
                ? value
                : DefaultWeaponReleaseStandoffKm;
        }
    }

    [Serializable]
    public sealed class BarcapStationCoverage
    {
        public Guid BarrierId;
        public List<Vector3Int> CoveredBarrierTileIds = new List<Vector3Int>();
        public Vector3Int ThreatReferenceTileId;
        public Vector3 StationCenterFeet;
        public float StationHeadingDegrees;
        public float StationTrackHalfLengthKm;
        public float PlannedResponseRadiusKm;
        public float PlannedMinimumInterceptSlackKm;
        public float PlannedPreferredLaunchRangeKm;
        public float RepresentativeThreatSpeedKnots;
        public int PlannedAircraftCount = 1;
        public int PreferredAircraftCount = 1;
        public List<Guid> PlannedKnownSamSiteIds = new List<Guid>();
        public float WeaponReleaseStandoffKm =
            BarcapBarrierPlan.DefaultWeaponReleaseStandoffKm;

        public BarcapStationCoverage Clone()
        {
            return new BarcapStationCoverage
            {
                BarrierId = BarrierId,
                CoveredBarrierTileIds = (CoveredBarrierTileIds
                                         ?? new List<Vector3Int>())
                    .Distinct()
                    .ToList(),
                ThreatReferenceTileId = ThreatReferenceTileId,
                StationCenterFeet = StationCenterFeet,
                StationHeadingDegrees = Mathf.Repeat(
                    StationHeadingDegrees,
                    360f),
                StationTrackHalfLengthKm = Math.Max(0f, StationTrackHalfLengthKm),
                PlannedResponseRadiusKm = Math.Max(0f, PlannedResponseRadiusKm),
                PlannedMinimumInterceptSlackKm = PlannedMinimumInterceptSlackKm,
                PlannedPreferredLaunchRangeKm = Math.Max(
                    0f,
                    PlannedPreferredLaunchRangeKm),
                RepresentativeThreatSpeedKnots = Math.Max(
                    0f,
                    RepresentativeThreatSpeedKnots),
                PlannedAircraftCount = Math.Max(1, PlannedAircraftCount),
                PreferredAircraftCount = Math.Max(1, PreferredAircraftCount),
                PlannedKnownSamSiteIds = (PlannedKnownSamSiteIds
                                          ?? new List<Guid>())
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList(),
                WeaponReleaseStandoffKm =
                    BarcapBarrierPlan.ResolveWeaponReleaseStandoffKm(
                        WeaponReleaseStandoffKm)
            };
        }
    }
}
