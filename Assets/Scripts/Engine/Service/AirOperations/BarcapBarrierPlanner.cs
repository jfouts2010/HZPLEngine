using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    public sealed class PlannedBarcapBarrier
    {
        public BarcapBarrierPlan Plan { get; }
        public float ProtectionValue { get; }
        public float HostilePressure { get; }
        public float HostilePower { get; }
        public int FighterTransitDistanceTiles { get; }

        public PlannedBarcapBarrier(
            BarcapBarrierPlan plan,
            float protectionValue,
            float hostilePressure,
            float hostilePower,
            int fighterTransitDistanceTiles)
        {
            Plan = plan;
            ProtectionValue = Mathf.Clamp01(protectionValue);
            HostilePressure = Mathf.Clamp01(hostilePressure);
            HostilePower = Mathf.Max(0f, hostilePower);
            FighterTransitDistanceTiles = Math.Max(0, fighterTransitDistanceTiles);
        }
    }

    public sealed class BarcapBarrierPlanner
    {
        private const float MeaningfulHostilePressure = 0.10f;
        private const float FrontlineProtectionValue = 1f;
        private const float ActiveAirportProtectionValue = 0.8f;
        private const float ReserveAirportProtectionValue = 0.35f;
        private const float UnknownThreatSpeedKnots = 600f;
        private const float PreferredLaunchRangeFraction = 0.78f;
        private const int MaximumThreatDirectionsPerAsset = 3;

        private readonly AirMissionPriorityService priorityService;

        public BarcapBarrierPlanner(AirMissionPriorityService priorityService)
        {
            this.priorityService = priorityService;
        }

        public IReadOnlyList<PlannedBarcapBarrier> Plan(
            AllianceAirTaskingCommander commander,
            AirPlanningSnapshot snapshot)
        {
            var friendlyTiles = snapshot.FriendlyControlledTileIds.ToHashSet();
            var primaryThreats = BuildPrimaryThreatSources(commander, snapshot);
            var frontThreats = primaryThreats.Count > 0
                ? primaryThreats
                : BuildFallbackAirportThreatSources(snapshot);
            if (frontThreats.Count == 0)
                return Array.Empty<PlannedBarcapBarrier>();

            var barriers = BuildFrontBarriers(snapshot, frontThreats, friendlyTiles);
            if (primaryThreats.Count > 0)
            {
                AddAirportProtection(
                    barriers,
                    snapshot,
                    primaryThreats,
                    friendlyTiles);
            }

            return barriers
                .Select(candidate => FinalizeCandidate(
                    candidate,
                    snapshot,
                    commander.Doctrine))
                .OrderByDescending(candidate => candidate.ProtectionValue)
                .ThenByDescending(candidate => candidate.HostilePressure)
                .ThenBy(candidate => candidate.Plan.BarrierTileIds[0].x)
                .ThenBy(candidate => candidate.Plan.BarrierTileIds[0].y)
                .ThenBy(candidate => candidate.Plan.BarrierTileIds[0].z)
                .ToList();
        }

        private List<BarrierCandidate> BuildFrontBarriers(
            AirPlanningSnapshot snapshot,
            IReadOnlyList<ThreatSource> threats,
            HashSet<Vector3Int> friendlyTiles)
        {
            var remaining = snapshot.FriendlyFrontlineDivisionTiles
                .Where(friendlyTiles.Contains)
                .Distinct()
                .ToHashSet();
            var barriers = new List<BarrierCandidate>();
            while (remaining.Count > 0)
            {
                var seed = OrderTiles(remaining).First();
                var component = TakeConnectedComponent(seed, remaining);
                var center = SelectMedoid(component);
                foreach (var threat in SelectThreatsForAsset(center, threats))
                {
                    var threatTile = ResolveThreatReferenceTile(
                        component,
                        threat.TileId,
                        threats,
                        snapshot.HostileControlledTileIds);
                    if (component.Contains(threatTile))
                        continue;
                    var ordered = OrderBarrierTiles(
                        component,
                        threatTile,
                        friendlyTiles);
                    foreach (var orderedRun in SplitContiguousRuns(ordered))
                    {
                        var line = orderedRun;
                        if (line.Count < 2)
                        {
                            line = ExpandSingleTileBarrier(
                                line[0],
                                threatTile,
                                friendlyTiles);
                        }
                        if (line.Count < 1)
                            continue;

                        barriers.Add(new BarrierCandidate(
                            new BarcapBarrierPlan
                            {
                                BarrierTileIds = line,
                                ThreatReferenceTileId = threatTile,
                                RepresentativeThreatSpeedKnots = threat.SpeedKnots,
                                ProtectedFrontlineDivisionCount =
                                    snapshot.FriendlyFrontlineDivisionTiles.Count(
                                        line.Contains)
                            },
                            FrontlineProtectionValue,
                            threat.Pressure,
                            threat.Power));
                    }
                }
            }

            return barriers;
        }

        private void AddAirportProtection(
            ICollection<BarrierCandidate> barriers,
            AirPlanningSnapshot snapshot,
            IReadOnlyList<ThreatSource> threats,
            HashSet<Vector3Int> friendlyTiles)
        {
            var activeAirportTiles = snapshot.FriendlyAirportTiles.ToHashSet();
            var airports = snapshot.FriendlyAirfieldTiles
                .Distinct()
                .OrderByDescending(tile => activeAirportTiles.Contains(tile))
                .ThenBy(tile => tile.x)
                .ThenBy(tile => tile.y)
                .ThenBy(tile => tile.z)
                .ToList();

            foreach (var airport in airports)
            {
                var relevantThreats = SelectThreatsForAsset(airport, threats);
                var uncovered = relevantThreats
                    .Where(threat => !barriers.Any(barrier =>
                        BlocksLane(
                            barrier.Plan,
                            threat.TileId,
                            airport)))
                    .ToList();
                if (uncovered.Count == 0)
                {
                    CreditAirport(
                        barriers,
                        airport,
                        relevantThreats,
                        activeAirportTiles.Contains(airport));
                    continue;
                }

                foreach (var threat in uncovered)
                {
                    var threatTile = ResolveThreatReferenceTile(
                        new[] { airport },
                        threat.TileId,
                        threats,
                        snapshot.HostileControlledTileIds);
                    if (threatTile == airport)
                        continue;
                    if (barriers.Any(barrier =>
                            BlocksLane(
                                barrier.Plan,
                                threatTile,
                                airport)))
                        continue;

                    var center = SelectForwardFriendlyBarrierTile(
                        threatTile,
                        airport,
                        friendlyTiles);
                    var line = BuildPerpendicularBarrier(
                        center,
                        threatTile,
                        friendlyTiles);
                    if (line.Count < 1)
                        continue;

                    var active = activeAirportTiles.Contains(airport);
                    var candidate = new BarrierCandidate(
                        new BarcapBarrierPlan
                            {
                                BarrierTileIds = line,
                                ThreatReferenceTileId = threatTile,
                            RepresentativeThreatSpeedKnots = threat.SpeedKnots,
                            IsSupplemental = true,
                            ProtectedActiveAirportCount = active ? 1 : 0,
                            ProtectedReserveAirportCount = active ? 0 : 1
                        },
                        active
                            ? ActiveAirportProtectionValue
                            : ReserveAirportProtectionValue,
                        threat.Pressure,
                        threat.Power);
                    barriers.Add(candidate);
                }
            }
        }

        private static void CreditAirport(
            IEnumerable<BarrierCandidate> barriers,
            Vector3Int airport,
            IReadOnlyList<ThreatSource> threats,
            bool active)
        {
            var credited = barriers
                .Where(barrier => threats.Any(threat =>
                    BlocksLane(
                        barrier.Plan,
                        threat.TileId,
                        airport)))
                .OrderByDescending(barrier => barrier.ProtectionValue)
                .ThenBy(barrier => barrier.Plan.BarrierTileIds[0].x)
                .FirstOrDefault();
            if (credited == null)
                return;

            if (active)
                credited.Plan.ProtectedActiveAirportCount++;
            else
                credited.Plan.ProtectedReserveAirportCount++;
            credited.ProtectionValue = Math.Max(
                credited.ProtectionValue,
                active
                    ? ActiveAirportProtectionValue
                    : ReserveAirportProtectionValue);
        }

        private PlannedBarcapBarrier FinalizeCandidate(
            BarrierCandidate candidate,
            AirPlanningSnapshot snapshot,
            AllianceAirDoctrine doctrine)
        {
            candidate.Plan.EstimatedAircraftDemand = EstimateAircraftDemand(
                candidate.Plan,
                snapshot,
                doctrine);
            var fighterOrigins = snapshot.FriendlySquadrons
                .Where(squadron => squadron.ReadyAircraftCount > 0
                                   && priorityService.CanPerformAirCombat(
                                       priorityService.GetAircraftType(
                                           squadron.AircraftTypeDefinitionId)))
                .Select(squadron => squadron.AirportTileId)
                .ToList();
            var transit = fighterOrigins
                .Select(origin => candidate.Plan.BarrierTileIds
                    .Min(tile => AirMissionArea.HexDistance(origin, tile)))
                .DefaultIfEmpty(0)
                .Min();
            return new PlannedBarcapBarrier(
                candidate.Plan,
                candidate.ProtectionValue,
                candidate.HostilePressure,
                candidate.HostilePower,
                transit);
        }

        private int EstimateAircraftDemand(
            BarcapBarrierPlan plan,
            AirPlanningSnapshot snapshot,
            AllianceAirDoctrine doctrine)
        {
            var midpoint = plan.BarrierTileIds[plan.BarrierTileIds.Count / 2];
            var threatDistanceKm = AirMissionArea.HexDistance(
                                       plan.ThreatReferenceTileId,
                                       midpoint)
                                   * snapshot.TileDistanceKm;
            var availableAlongBarrierRadii = snapshot.FriendlySquadrons
                .Where(squadron => squadron.ReadyAircraftCount > 0)
                .Select(squadron => priorityService.GetAircraftType(
                    squadron.AircraftTypeDefinitionId))
                .Where(priorityService.CanPerformAirCombat)
                // Sizing assumes best-case warning; the package builder recomputes
                // the radius from actual IADS coverage when it places a station.
                .Select(type =>
                {
                    var responseRadius = BarcapInterceptGeometry.CalculateResponseRadiusKm(
                        type,
                        plan.RepresentativeThreatSpeedKnots,
                        threatDistanceKm,
                        plan.WeaponReleaseStandoffKm,
                        priorityService.GetLongestAirToAirWeaponRangeKm(type)
                        * PreferredLaunchRangeFraction,
                        sensorWarningMinutes:
                            BarcapInterceptGeometry.MaximumResponseMinutes,
                        commandDelaySeconds: doctrine.BarcapCommandDelaySeconds);
                    var trackHalfLengthKm = BarcapInterceptGeometry
                        .CalculateStationTrackHalfLengthKm(
                            type,
                            doctrine.BarcapTrackLegMinutes);
                    var turnRadiusKm = AirspaceGeometry.TurnRadiusFeet(
                                           type.CruiseSpeedKnots,
                                           type.TurnRateDegreesPerSecond)
                                       / AirspaceGeometry.FeetPerKilometer;
                    var worstAxialDistanceKm =
                        BarcapBarrierPlan.ResolveWeaponReleaseStandoffKm(
                            plan.WeaponReleaseStandoffKm)
                        + trackHalfLengthKm
                        + turnRadiusKm;
                    var alongBarrierSquared = responseRadius * responseRadius
                                              - worstAxialDistanceKm
                                              * worstAxialDistanceKm;
                    return Math.Max(
                        0f,
                        Mathf.Sqrt(Math.Max(0f, alongBarrierSquared))
                        - turnRadiusKm);
                })
                .ToList();
            var bestAlongBarrierRadius = availableAlongBarrierRadii.Count > 0
                ? availableAlongBarrierRadii.Max()
                : 0f;
            var coveredTiles = bestAlongBarrierRadius <= 0f
                ? 1
                : Mathf.FloorToInt(
                      bestAlongBarrierRadius / snapshot.TileDistanceKm)
                  * 2 + 1;
            return Math.Max(
                1,
                Mathf.CeilToInt(plan.BarrierTileIds.Count / (float)coveredTiles));
        }

        private List<ThreatSource> BuildPrimaryThreatSources(
            AllianceAirTaskingCommander commander,
            AirPlanningSnapshot snapshot)
        {
            var threats = BuildAirborneThreatSources(commander);
            foreach (var airport in snapshot.EnemyAirports
                         .Where(report => report.Condition
                                          != ObservedAirportCondition.NonFunctional))
            {
                var combatGroups = airport.AircraftGroups
                    .Where(group => group.ApparentlyAvailableCount > 0)
                    .Select(group => new
                    {
                        Group = group,
                        Type = priorityService.GetAircraftType(
                            group.AircraftTypeDefinitionId)
                    })
                    .Where(entry => priorityService.CanPerformCombatMission(entry.Type))
                    .ToList();
                if (combatGroups.Count == 0)
                    continue;

                threats.Add(new ThreatSource(
                    airport.AirportTileId,
                    combatGroups.Max(entry => entry.Type.CombatSpeedKnots),
                    Mathf.Clamp01(combatGroups.Sum(entry =>
                        entry.Group.ApparentlyAvailableCount) / 8f),
                    combatGroups.Sum(entry =>
                        entry.Group.ApparentlyAvailableCount
                        * Math.Max(0.1f, entry.Type.AirInterferenceCapability))));
            }

            return threats;
        }

        private static List<ThreatSource> BuildFallbackAirportThreatSources(
            AirPlanningSnapshot snapshot)
        {
            var airports = snapshot.EnemyAirports
                .Select(report => new ThreatSource(
                    report.AirportTileId,
                    UnknownThreatSpeedKnots,
                    MeaningfulHostilePressure,
                    0f))
                .ToList();
            if (airports.Count > 0)
                return airports;

            return snapshot.HostileControlledTileIds
                .Select(tile => new ThreatSource(
                    tile,
                    UnknownThreatSpeedKnots,
                    MeaningfulHostilePressure,
                    0f))
                .ToList();
        }

        private static List<ThreatSource> BuildAirborneThreatSources(
            AllianceAirTaskingCommander commander)
        {
            var assessments = commander.AirControlAssessments
                .Where(assessment =>
                    assessment.HostileCombatPresence >= MeaningfulHostilePressure)
                .ToDictionary(assessment => assessment.TileId);
            var remaining = assessments.Keys.ToHashSet();
            var threats = new List<ThreatSource>();
            while (remaining.Count > 0)
            {
                var seed = OrderTiles(remaining).First();
                var component = TakeConnectedComponent(seed, remaining);
                var representative = component
                    .Select(tile => assessments[tile])
                    .OrderByDescending(assessment =>
                        assessment.HostileCombatPresence)
                    .ThenByDescending(assessment => assessment.HostileCombatPower)
                    .ThenBy(assessment => assessment.TileId.x)
                    .ThenBy(assessment => assessment.TileId.y)
                    .ThenBy(assessment => assessment.TileId.z)
                    .First();
                threats.Add(new ThreatSource(
                    representative.TileId,
                    UnknownThreatSpeedKnots,
                    representative.HostileCombatPresence,
                    representative.HostileCombatPower));
            }

            return threats;
        }

        private static IReadOnlyList<ThreatSource> SelectThreatsForAsset(
            Vector3Int asset,
            IReadOnlyList<ThreatSource> threats)
        {
            return threats
                .GroupBy(threat => DirectionSector(asset, threat.TileId))
                .Select(group => group
                    .OrderByDescending(threat => threat.Pressure)
                    .ThenByDescending(threat => threat.Power)
                    .ThenBy(threat => AirMissionArea.HexDistance(
                        asset,
                        threat.TileId))
                    .First())
                .OrderByDescending(threat => threat.Pressure)
                .ThenByDescending(threat => threat.Power)
                .ThenBy(threat => AirMissionArea.HexDistance(asset, threat.TileId))
                .Take(MaximumThreatDirectionsPerAsset)
                .ToList();
        }

        private static int DirectionSector(Vector3Int origin, Vector3Int target)
        {
            var direction = AirspaceGeometry.TileCenterFeet(target, 1f)
                            - AirspaceGeometry.TileCenterFeet(origin, 1f);
            var angle = Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg;
            if (angle < 0f)
                angle += 360f;
            return Mathf.FloorToInt((angle + 30f) / 60f) % 6;
        }

        private static List<Vector3Int> OrderBarrierTiles(
            IReadOnlyCollection<Vector3Int> tiles,
            Vector3Int threatTile,
            HashSet<Vector3Int> friendlyTiles)
        {
            var center = SelectMedoid(tiles);
            var threatDirection = AirspaceGeometry.TileCenterFeet(threatTile, 1f)
                                  - AirspaceGeometry.TileCenterFeet(center, 1f);
            threatDirection.y = 0f;
            if (threatDirection.sqrMagnitude < 0.001f)
                threatDirection = Vector3.forward;
            var tangent = new Vector3(-threatDirection.z, 0f, threatDirection.x)
                .normalized;
            var ordered = tiles
                .OrderBy(tile => Vector3.Dot(
                    AirspaceGeometry.TileCenterFeet(tile, 1f),
                    tangent))
                .ThenBy(tile => tile.x)
                .ThenBy(tile => tile.y)
                .ThenBy(tile => tile.z)
                .ToList();
            var line = new List<Vector3Int>();
            foreach (var tile in ordered)
            {
                if (line.Count == 0)
                {
                    line.Add(tile);
                    continue;
                }

                foreach (var step in AirspaceGeometry.TilesAlongLine(
                             line[line.Count - 1],
                             tile).Skip(1))
                {
                    if (friendlyTiles.Contains(step) && !line.Contains(step))
                        line.Add(step);
                }
            }

            return line;
        }

        private static IReadOnlyList<List<Vector3Int>> SplitContiguousRuns(
            IReadOnlyList<Vector3Int> ordered)
        {
            var runs = new List<List<Vector3Int>>();
            foreach (var tile in ordered ?? Array.Empty<Vector3Int>())
            {
                if (runs.Count == 0
                    || AirMissionArea.HexDistance(
                        runs[runs.Count - 1][runs[runs.Count - 1].Count - 1],
                        tile) > 1)
                {
                    runs.Add(new List<Vector3Int>());
                }

                if (runs[runs.Count - 1].Count == 0
                    || runs[runs.Count - 1][runs[runs.Count - 1].Count - 1]
                    != tile)
                {
                    runs[runs.Count - 1].Add(tile);
                }
            }

            return runs.Where(run => run.Count > 0).ToList();
        }

        private static Vector3Int ResolveThreatReferenceTile(
            IReadOnlyCollection<Vector3Int> protectedTiles,
            Vector3Int preferredThreatTile,
            IReadOnlyList<ThreatSource> threats,
            IReadOnlyList<Vector3Int> hostileControlledTiles)
        {
            var protectedSet = (protectedTiles ?? Array.Empty<Vector3Int>())
                .ToHashSet();
            if (!protectedSet.Contains(preferredThreatTile))
                return preferredThreatTile;
            var protectedCenter = SelectMedoid(protectedSet);

            var hostileReference = (hostileControlledTiles
                                    ?? Array.Empty<Vector3Int>())
                .Where(tile => !protectedSet.Contains(tile))
                .OrderBy(tile => AirMissionArea.HexDistance(
                    protectedCenter,
                    tile))
                .ThenBy(tile => tile.x)
                .ThenBy(tile => tile.y)
                .ThenBy(tile => tile.z)
                .Select(tile => (Vector3Int?)tile)
                .FirstOrDefault();
            if (hostileReference.HasValue)
                return hostileReference.Value;

            return (threats ?? Array.Empty<ThreatSource>())
                .Where(threat => !protectedSet.Contains(threat.TileId))
                .OrderByDescending(threat => threat.Pressure)
                .ThenByDescending(threat => threat.Power)
                .ThenBy(threat => AirMissionArea.HexDistance(
                    protectedCenter,
                    threat.TileId))
                .Select(threat => (Vector3Int?)threat.TileId)
                .FirstOrDefault()
                ?? preferredThreatTile;
        }

        private static List<Vector3Int> ExpandSingleTileBarrier(
            Vector3Int center,
            Vector3Int threatTile,
            HashSet<Vector3Int> friendlyTiles)
        {
            return BuildPerpendicularBarrier(center, threatTile, friendlyTiles);
        }

        private static List<Vector3Int> BuildPerpendicularBarrier(
            Vector3Int center,
            Vector3Int threatTile,
            HashSet<Vector3Int> friendlyTiles)
        {
            var threatDirection = AirspaceGeometry.TileCenterFeet(threatTile, 1f)
                                  - AirspaceGeometry.TileCenterFeet(center, 1f);
            threatDirection.y = 0f;
            if (threatDirection.sqrMagnitude < 0.001f)
                threatDirection = Vector3.forward;
            var tangent = new Vector3(-threatDirection.z, 0f, threatDirection.x)
                .normalized;
            var neighbors = AirspaceGeometry.NeighborTiles(center)
                .Where(friendlyTiles.Contains)
                .Select(tile => new
                {
                    Tile = tile,
                    Projection = Vector3.Dot(
                        AirspaceGeometry.TileCenterFeet(tile, 1f)
                        - AirspaceGeometry.TileCenterFeet(center, 1f),
                        tangent)
                })
                .ToList();
            var negative = neighbors
                .Where(candidate => candidate.Projection < 0f)
                .OrderBy(candidate => candidate.Projection)
                .FirstOrDefault();
            var positive = neighbors
                .Where(candidate => candidate.Projection > 0f)
                .OrderByDescending(candidate => candidate.Projection)
                .FirstOrDefault();
            var line = new List<Vector3Int>();
            if (negative != null)
                line.Add(negative.Tile);
            line.Add(center);
            if (positive != null)
                line.Add(positive.Tile);
            return line
                .Where(tile => tile != threatTile)
                .Distinct()
                .ToList();
        }

        private static Vector3Int SelectForwardFriendlyBarrierTile(
            Vector3Int threat,
            Vector3Int protectedTile,
            HashSet<Vector3Int> friendlyTiles)
        {
            return AirspaceGeometry.TilesAlongLine(threat, protectedTile)
                .Where(tile => tile != threat && friendlyTiles.Contains(tile))
                .DefaultIfEmpty(protectedTile)
                .First();
        }

        private static bool BlocksLane(
            BarcapBarrierPlan plan,
            Vector3Int threat,
            Vector3Int protectedTile)
        {
            if (plan?.BarrierTileIds == null
                || plan.BarrierTileIds.Count == 0
                || !FacesThreatDirection(plan, threat))
                return false;

            var barrier = plan.BarrierTileIds.ToHashSet();
            return AirspaceGeometry.TilesAlongLine(threat, protectedTile)
                .TakeWhile(tile => tile != protectedTile)
                .Any(tile => barrier.Contains(tile)
                             || AirspaceGeometry.NeighborTiles(tile)
                                 .Any(barrier.Contains));
        }

        private static bool FacesThreatDirection(
            BarcapBarrierPlan plan,
            Vector3Int threat)
        {
            var center = plan.BarrierTileIds[
                plan.BarrierTileIds.Count / 2];
            var centerPosition = AirspaceGeometry.TileCenterFeet(center, 1f);
            var plannedApproach = centerPosition
                                  - AirspaceGeometry.TileCenterFeet(
                                      plan.ThreatReferenceTileId,
                                      1f);
            var candidateApproach = centerPosition
                                    - AirspaceGeometry.TileCenterFeet(
                                        threat,
                                        1f);
            if (plannedApproach.sqrMagnitude < 0.001f
                || candidateApproach.sqrMagnitude < 0.001f)
            {
                return plan.ThreatReferenceTileId == threat;
            }

            return Vector3.Dot(
                       plannedApproach.normalized,
                       candidateApproach.normalized)
                   >= 0.5f;
        }

        private static HashSet<Vector3Int> TakeConnectedComponent(
            Vector3Int seed,
            HashSet<Vector3Int> remaining)
        {
            var component = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            remaining.Remove(seed);
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                component.Add(tile);
                foreach (var neighbor in AirspaceGeometry.NeighborTiles(tile))
                {
                    if (!remaining.Remove(neighbor))
                        continue;
                    queue.Enqueue(neighbor);
                }
            }

            return component;
        }

        private static Vector3Int SelectMedoid(
            IReadOnlyCollection<Vector3Int> tiles)
        {
            return tiles
                .OrderBy(tile => tiles.Sum(other =>
                    AirMissionArea.HexDistance(tile, other)))
                .ThenBy(tile => tile.x)
                .ThenBy(tile => tile.y)
                .ThenBy(tile => tile.z)
                .First();
        }

        private static IOrderedEnumerable<Vector3Int> OrderTiles(
            IEnumerable<Vector3Int> tiles)
        {
            return tiles
                .OrderBy(tile => tile.x)
                .ThenBy(tile => tile.y)
                .ThenBy(tile => tile.z);
        }

        private sealed class BarrierCandidate
        {
            public BarcapBarrierPlan Plan { get; }
            public float ProtectionValue;
            public float HostilePressure { get; }
            public float HostilePower { get; }

            public BarrierCandidate(
                BarcapBarrierPlan plan,
                float protectionValue,
                float hostilePressure,
                float hostilePower)
            {
                Plan = plan;
                ProtectionValue = protectionValue;
                HostilePressure = hostilePressure;
                HostilePower = hostilePower;
            }
        }

        private sealed class ThreatSource
        {
            public Vector3Int TileId { get; }
            public float SpeedKnots { get; }
            public float Pressure { get; }
            public float Power { get; }

            public ThreatSource(
                Vector3Int tileId,
                float speedKnots,
                float pressure,
                float power)
            {
                TileId = tileId;
                SpeedKnots = Math.Max(0f, speedKnots);
                Pressure = Mathf.Clamp01(pressure);
                Power = Mathf.Max(0f, power);
            }
        }
    }
}
