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
        public float PlannedResponseRadiusKm;
        public float PlannedPreferredLaunchRangeKm;
        public float RepresentativeThreatSpeedKnots;
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
                PlannedResponseRadiusKm = Math.Max(0f, PlannedResponseRadiusKm),
                PlannedPreferredLaunchRangeKm = Math.Max(
                    0f,
                    PlannedPreferredLaunchRangeKm),
                RepresentativeThreatSpeedKnots = Math.Max(
                    0f,
                    RepresentativeThreatSpeedKnots),
                WeaponReleaseStandoffKm =
                    BarcapBarrierPlan.ResolveWeaponReleaseStandoffKm(
                        WeaponReleaseStandoffKm)
            };
        }
    }
}
