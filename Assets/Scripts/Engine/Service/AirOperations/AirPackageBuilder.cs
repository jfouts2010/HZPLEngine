using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    public enum AirPackageBuildOutcome
    {
        Built,
        AlreadySatisfied,
        EquivalentCommitment,
        Deferred
    }

    public sealed class AirPackageBuilder
    {
        private const float BarcapAndOcaAltitudeFeet = 40000f;
        private const float AwacsAltitudeFeet = 35000f;
        private const float TankerAltitudeFeet = 25000f;
        private const float DefaultStationTrackHalfLengthTiles = 0.5f;
        private const float MaximumSupportStationHostileInterference = 0.10f;
        private const float MeaningfulOcaPresence = 0.10f;
        private const float MeaningfulBarcapPressure = 0.10f;
        private const float FuelPlanningMarginSeconds = 60f;
        private const float BarcapPreferredLaunchRangeFraction = 0.78f;
        private const int MaximumBarcapRouteChoices = 32;
        private static readonly TimeSpan BarcapHandoffOverlap = TimeSpan.FromMinutes(10);

        private readonly GameManager gameManager;
        private readonly ProjectedAirEffectService projectedEffects;
        private readonly AirMissionPriorityService priorityService;
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes;
        private readonly IReadOnlyDictionary<Guid, RadarAirDefenseComponentDefinition>
            radarDefinitions;
        private readonly AirLoadoutPlanner loadoutPlanner;
        private readonly IAirRouteGeometryPlanner routeGeometryPlanner;
        private readonly KnownSamThreatAssessment knownSamThreatAssessment;
        private readonly AirportOperationsService airportOperations;
        private readonly Dictionary<Alliance, IReadOnlyList<KnownSamThreatEnvelope>>
            knownSamThreatCache =
                new Dictionary<Alliance, IReadOnlyList<KnownSamThreatEnvelope>>();
        private DateTime knownSamThreatCacheTime = DateTime.MinValue;

        public AirPackageBuilder(
            GameManager gameManager,
            ModuleDefinition module,
            ProjectedAirEffectService projectedEffects,
            AirMissionPriorityService priorityService,
            AirportOperationsService airportOperations,
            IAirRouteGeometryPlanner routeGeometryPlanner = null)
        {
            this.gameManager = gameManager;
            this.projectedEffects = projectedEffects;
            this.priorityService = priorityService;
            this.airportOperations = airportOperations
                                     ?? throw new ArgumentNullException(
                                         nameof(airportOperations));
            this.routeGeometryPlanner = routeGeometryPlanner ?? new SeparatedIngressEgressRouteGeometryPlanner();
            knownSamThreatAssessment = new KnownSamThreatAssessment(
                module.SamComponentDefinitions,
                module.OrdnanceTypeDefinitions);
            aircraftTypes = module.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
            radarDefinitions = (module.SamComponentDefinitions
                                ?? new List<AirDefenseComponentDefinition>())
                .OfType<RadarAirDefenseComponentDefinition>()
                .ToDictionary(definition => definition.SamComponentDefinitionId);
            loadoutPlanner = new AirLoadoutPlanner(
                module,
                alliance =>
                    gameManager.OrdnanceAllowances.TryGetValue(alliance, out var allowed)
                        ? allowed
                        : Array.Empty<Guid>());
        }

        public AirPackageBuildOutcome TryBuild(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime currentTime,
            out AirPackage package,
            out string reason)
        {
            package = null;
            reason = string.Empty;
            if (knownSamThreatCacheTime != currentTime)
            {
                knownSamThreatCache.Clear();
                knownSamThreatCacheTime = currentTime;
            }

            return request.IsSupportRequest
                ? TryBuildSupportPackage(commander, request, currentTime, out package, out reason)
                : TryBuildCombatPackage(commander, request, currentTime, out package, out reason);
        }

        private AirPackageBuildOutcome TryBuildSupportPackage(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime currentTime,
            out AirPackage package,
            out string reason)
        {
            package = null;
            reason = string.Empty;
            var planningStart = currentTime + AirPackage.PreparationDelay;
            if (!projectedEffects.TryFindFirstCoverageGap(
                    commander,
                    request,
                    planningStart,
                    out var gapStart,
                    out var projectedSlots))
            {
                reason = "Desired support capacity is already projected.";
                return AirPackageBuildOutcome.AlreadySatisfied;
            }

            var requiredCapability = request.RequestType == AirMissionRequestType.ProvideAirborneC2
                ? AirSupportCapability.AirborneC2
                : AirSupportCapability.AerialRefueling;
            if (!TrySelectSupportStationTile(
                    commander,
                    request,
                    out var stationTileId,
                    out reason))
                return AirPackageBuildOutcome.Deferred;

            var remainingSlots = Math.Max(1, request.DesiredSupportSlots - projectedSlots);
            var candidates = GetFriendlySquadrons(commander.Alliance)
                .Where(squadron =>
                    aircraftTypes.TryGetValue(squadron.AircraftTypeDefinitionId, out var aircraftType)
                    && aircraftType.SupportCapability == requiredCapability
                    && aircraftType.SupportSlotCapacity > 0)
                .Select(squadron => new
                {
                    Squadron = squadron,
                    AircraftType = aircraftTypes[squadron.AircraftTypeDefinitionId],
                    AvailableAircraft = GetAvailableAircraft(squadron)
                })
                .Where(candidate => candidate.AvailableAircraft.Count > 0)
                .OrderBy(candidate => GetAirportDistance(candidate.Squadron, stationTileId))
                .ThenBy(candidate => candidate.Squadron.SquadronId)
                .FirstOrDefault();
            if (candidates == null)
            {
                reason = $"No ready {requiredCapability} aircraft are available.";
                return AirPackageBuildOutcome.Deferred;
            }

            var aircraftCount = Math.Min(
                candidates.AvailableAircraft.Count,
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        remainingSlots / (double)candidates.AircraftType.SupportSlotCapacity)));
            var selectedAircraft = candidates.AvailableAircraft.Take(aircraftCount).ToList();
            var effectStart = gapStart > planningStart ? gapStart : planningStart;
            var effectEnd = request.EffectEnd;
            package = CreatePackage(request, currentTime);
            var flight = CreateFlight(
                request,
                candidates.Squadron,
                selectedAircraft);
            flight.ProvidedSupportSlots =
                selectedAircraft.Count * candidates.AircraftType.SupportSlotCapacity;
            package.Flights.Add(flight);
            if (!TryMaterializeRoutes(
                    commander,
                    package,
                    request,
                    planningStart,
                    effectStart,
                    effectEnd,
                    stationTileId,
                    null,
                    out reason))
            {
                package = null;
                return AirPackageBuildOutcome.Deferred;
            }

            reason = $"Proposed {flight.ProvidedSupportSlots} support slots.";
            return AirPackageBuildOutcome.Built;
        }

        private AirPackageBuildOutcome TryBuildCombatPackage(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime currentTime,
            out AirPackage package,
            out string reason)
        {
            package = null;
            reason = string.Empty;
            if (request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Discrete
                && projectedEffects.HasEquivalentDiscreteCommitment(commander, request))
            {
                reason = "An equivalent discrete effect is already committed.";
                return AirPackageBuildOutcome.EquivalentCommitment;
            }

            var planningStart = currentTime + AirPackage.PreparationDelay;
            DateTime effectStart;
            IReadOnlyList<Vector3Int> uncoveredBarrierTiles = null;
            if (request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained)
            {
                var hasGap = request.RequestType
                             == AirMissionRequestType.BarrierCombatAirPatrol
                             && request.BarcapBarrier?.BarrierTileIds?.Count > 0
                    ? projectedEffects.TryFindFirstBarcapCoverageGap(
                        commander,
                        request,
                        planningStart,
                        out effectStart,
                        out uncoveredBarrierTiles)
                    : projectedEffects.TryFindFirstCoverageGap(
                        commander,
                        request,
                        planningStart,
                        out effectStart,
                        out _);
                if (!hasGap)
                {
                    reason = "Desired combat coverage is already projected.";
                    return AirPackageBuildOutcome.AlreadySatisfied;
                }
                if (request.RequestType == AirMissionRequestType.BarrierCombatAirPatrol
                    && effectStart > request.EffectStart)
                {
                    var earliestHandoff = planningStart > request.EffectStart
                        ? planningStart
                        : request.EffectStart;
                    effectStart = effectStart - BarcapHandoffOverlap > earliestHandoff
                        ? effectStart - BarcapHandoffOverlap
                        : earliestHandoff;
                }
            }
            else
            {
                effectStart = planningStart > request.EffectStart
                    ? planningStart
                    : request.EffectStart;
            }

            var spatialBarcap = request.RequestType
                                == AirMissionRequestType.BarrierCombatAirPatrol
                                && request.BarcapBarrier?.BarrierTileIds?.Count > 0;
            var desiredStrength = spatialBarcap
                ? 1
                : Math.Max(1, request.DesiredAircraftStrength);
            var squadronCandidates = GetFriendlySquadrons(commander.Alliance)
                .Where(squadron =>
                    aircraftTypes.TryGetValue(squadron.AircraftTypeDefinitionId, out var aircraftType)
                    && priorityService.CanPerformAirCombat(aircraftType))
                .Select(squadron =>
                {
                    var aircraftType = aircraftTypes[squadron.AircraftTypeDefinitionId];
                    return loadoutPlanner.TryPlanAirCombatLoadout(
                        aircraftType,
                        commander.Alliance,
                        out var loadout,
                        out _)
                        ? new CombatSquadronCandidate(
                            squadron,
                            aircraftType,
                            GetAvailableAircraft(squadron),
                            loadout,
                            GetAirportDistance(squadron, request.MissionArea.CenterTileId))
                        : null;
                })
                .Where(candidate => candidate != null)
                .Where(candidate => candidate.AvailableAircraft.Count > 0)
                .OrderBy(candidate => candidate.DistanceTiles)
                .ThenBy(candidate => candidate.Squadron.AirportBuildingId)
                .ThenBy(candidate => candidate.Squadron.SquadronId)
                .ToList();

            if (spatialBarcap)
            {
                var choices = GetBarcapAircraftAndCoverageChoices(
                    request,
                    gameManager.SimulationSettings.TileDistanceKM,
                    uncoveredBarrierTiles,
                    squadronCandidates);
                if (choices.Count == 0)
                {
                    reason = "No ready air-combat aircraft can cover the remaining barrier.";
                    return AirPackageBuildOutcome.Deferred;
                }

                var lastRouteFailure = string.Empty;
                foreach (var choice in choices)
                {
                    var selected = new List<SelectedCombatAircraft>
                    {
                        new SelectedCombatAircraft(
                            choice.Candidate.Squadron,
                            choice.Candidate.AircraftType,
                            choice.Candidate.AvailableAircraft.Take(1).ToList(),
                            choice.Candidate.Loadout)
                    };
                    var candidatePackage = CreatePackage(request, currentTime);
                    AddSelectedFlights(candidatePackage, request, selected);
                    var coverage = new BarcapStationCoverage
                    {
                        BarrierId = request.BarcapBarrier.BarrierId,
                        CoveredBarrierTileIds = choice.Covered,
                        ThreatReferenceTileId =
                            request.BarcapBarrier.ThreatReferenceTileId,
                        PlannedResponseRadiusKm = choice.RadiusKm,
                        PlannedPreferredLaunchRangeKm =
                            choice.PreferredLaunchRangeKm,
                        RepresentativeThreatSpeedKnots =
                            request.BarcapBarrier.RepresentativeThreatSpeedKnots,
                        WeaponReleaseStandoffKm =
                            BarcapBarrierPlan.ResolveWeaponReleaseStandoffKm(
                                request.BarcapBarrier.WeaponReleaseStandoffKm)
                    };
                    if (!TryMaterializeRoutes(
                            commander,
                            candidatePackage,
                            request,
                            planningStart,
                            effectStart,
                            request.EffectEnd,
                            choice.Station,
                            coverage,
                            out lastRouteFailure))
                    {
                        continue;
                    }

                    package = candidatePackage;
                    reason = $"Proposed one aircraft covering "
                             + $"{coverage.CoveredBarrierTileIds.Count} barrier tiles"
                             + (package.SupportingFlightIds.Count > 0
                                 ? $" with {package.SupportingFlightIds.Count} tanker rotation(s)."
                                 : ".");
                    return AirPackageBuildOutcome.Built;
                }

                reason = string.IsNullOrWhiteSpace(lastRouteFailure)
                    ? "No route-feasible BARCAP station is available."
                    : lastRouteFailure;
                return AirPackageBuildOutcome.Deferred;
            }

            var selectedCandidates = SelectCombatAircraft(
                squadronCandidates,
                desiredStrength,
                request.RequestType
                == AirMissionRequestType.BarrierCombatAirPatrol);
            if (selectedCandidates.Sum(candidate => candidate.Aircraft.Count) < desiredStrength)
            {
                reason = $"Only {selectedCandidates.Sum(candidate => candidate.Aircraft.Count)}"
                         + $" of {desiredStrength} required combat aircraft are feasible.";
                return AirPackageBuildOutcome.Deferred;
            }

            var effectEnd = request.EffectEnd;
            package = CreatePackage(request, currentTime);
            AddSelectedFlights(package, request, selectedCandidates);

            if (!TryMaterializeRoutes(
                    commander,
                    package,
                    request,
                    planningStart,
                    effectStart,
                    effectEnd,
                    null,
                    null,
                    out reason))
            {
                package = null;
                return AirPackageBuildOutcome.Deferred;
            }

            reason = $"Proposed {desiredStrength} combat aircraft.";
            return AirPackageBuildOutcome.Built;
        }

        private IReadOnlyList<BarcapSelectionChoice>
            GetBarcapAircraftAndCoverageChoices(
            AirMissionRequest request,
            float snapshotTileDistanceKm,
            IReadOnlyList<Vector3Int> uncoveredBarrierTiles,
            IReadOnlyList<CombatSquadronCandidate> candidates)
        {
            if (uncoveredBarrierTiles == null
                || uncoveredBarrierTiles.Count == 0
                || request.BarcapBarrier?.BarrierTileIds == null
                || request.BarcapBarrier.BarrierTileIds.Count < 1)
                return Array.Empty<BarcapSelectionChoice>();

            var barrier = request.BarcapBarrier.BarrierTileIds;
            var gapCenterTile = SelectLargestBarrierGapCenter(
                barrier,
                uncoveredBarrierTiles);
            var defensiveStationTiles = GetDefensiveBarcapStationTiles(
                gapCenterTile,
                request.BarcapBarrier.ThreatReferenceTileId,
                request.Alliance);
            var releaseStandoffKm =
                BarcapBarrierPlan.ResolveWeaponReleaseStandoffKm(
                    request.BarcapBarrier.WeaponReleaseStandoffKm);
            return candidates
                .Where(candidate => candidate.AvailableAircraft.Count > 0)
                .SelectMany(candidate =>
                {
                    GetBarcapWeaponPlanningValues(
                        candidate,
                        out var preferredLaunchRangeKm,
                        out var weaponPreparationSeconds);
                    var responseRadiusByTile = barrier
                        .Distinct()
                        .ToDictionary(
                            tile => tile,
                            tile =>
                            {
                                var tileThreatDistanceKm =
                                    AirMissionArea.HexDistance(
                                        request.BarcapBarrier
                                            .ThreatReferenceTileId,
                                        tile) * snapshotTileDistanceKm;
                                return BarcapInterceptGeometry
                                    .CalculateResponseRadiusKm(
                                        candidate.AircraftType,
                                        request.BarcapBarrier
                                            .RepresentativeThreatSpeedKnots,
                                        tileThreatDistanceKm,
                                        releaseStandoffKm,
                                        preferredLaunchRangeKm,
                                        weaponPreparationSeconds,
                                        CalculateBarcapSensorWarningMinutes(
                                            request.Alliance,
                                            tile,
                                            request.BarcapBarrier
                                                .ThreatReferenceTileId,
                                            snapshotTileDistanceKm,
                                            releaseStandoffKm,
                                            request.BarcapBarrier
                                                .RepresentativeThreatSpeedKnots));
                            });
                    return defensiveStationTiles
                        .Select((candidateStation, depth) =>
                        {
                            var coverable = responseRadiusByTile
                                .Where(entry =>
                                    CanReachOperationalBarrierFromStation(
                                        candidateStation,
                                        entry.Key,
                                        request.BarcapBarrier
                                            .ThreatReferenceTileId,
                                        snapshotTileDistanceKm,
                                        releaseStandoffKm,
                                        entry.Value))
                                .Select(entry => entry.Key)
                                .ToList();
                            var covered = SelectContiguousBarrierRun(
                                barrier,
                                coverable,
                                gapCenterTile);
                            var conservativeResponseRadiusKm = covered
                                .Select(tile => responseRadiusByTile[tile])
                                .DefaultIfEmpty(0f)
                                .Min();
                            return new BarcapSelectionChoice(
                                candidate,
                                conservativeResponseRadiusKm,
                                preferredLaunchRangeKm,
                                candidateStation,
                                depth,
                                covered,
                                covered.Count(uncoveredBarrierTiles.Contains));
                        });
                })
                .Where(candidate => candidate.UncoveredCount > 0)
                .OrderByDescending(candidate => candidate.UncoveredCount)
                .ThenByDescending(candidate => candidate.StationDepth)
                .ThenByDescending(candidate => candidate.RadiusKm)
                .ThenBy(candidate => candidate.Candidate.DistanceTiles)
                .ThenBy(candidate => candidate.Candidate.Squadron.SquadronId)
                .Take(MaximumBarcapRouteChoices)
                .ToList();
        }

        private static bool CanReachOperationalBarrierFromStation(
            Vector3Int stationTile,
            Vector3Int barrierTile,
            Vector3Int threatTile,
            float tileDistanceKm,
            float weaponReleaseStandoffKm,
            float responseRadiusKm)
        {
            var stationCenter = AirspaceGeometry.TileCenterFeet(
                stationTile,
                tileDistanceKm);
            var releasePoint = BarcapInterceptGeometry
                .GetOperationalBarrierPointsFeet(
                    new[] { barrierTile },
                    threatTile,
                    tileDistanceKm,
                    weaponReleaseStandoffKm)
                .FirstOrDefault();
            var threatCenter = AirspaceGeometry.TileCenterFeet(
                threatTile,
                tileDistanceKm);
            var threatDirection = threatCenter - stationCenter;
            threatDirection.y = 0f;
            if (threatDirection.sqrMagnitude < 1f)
                threatDirection = Vector3.forward;
            threatDirection.Normalize();
            var trackDirection = new Vector3(
                -threatDirection.z,
                0f,
                threatDirection.x);
            var trackOffsetFeet = trackDirection
                                   * DefaultStationTrackHalfLengthTiles
                                   * tileDistanceKm
                                   * AirspaceGeometry.FeetPerKilometer;
            var worstStationDistanceKm = Math.Max(
                Vector3.Distance(
                    stationCenter - trackOffsetFeet,
                    releasePoint),
                Vector3.Distance(
                    stationCenter + trackOffsetFeet,
                    releasePoint))
                / AirspaceGeometry.FeetPerKilometer;
            return worstStationDistanceKm <= responseRadiusKm + 0.001f;
        }

        private static List<Vector3Int> SelectContiguousBarrierRun(
            IReadOnlyList<Vector3Int> barrier,
            IReadOnlyCollection<Vector3Int> coverable,
            Vector3Int center)
        {
            var centerIndex = barrier.ToList().IndexOf(center);
            var coverableSet = coverable.ToHashSet();
            if (centerIndex < 0 || !coverableSet.Contains(center))
                return new List<Vector3Int>();

            var start = centerIndex;
            while (start > 0 && coverableSet.Contains(barrier[start - 1]))
                start--;
            var end = centerIndex;
            while (end + 1 < barrier.Count
                   && coverableSet.Contains(barrier[end + 1]))
                end++;

            return barrier
                .Skip(start)
                .Take(end - start + 1)
                .ToList();
        }

        private IReadOnlyList<Vector3Int> GetDefensiveBarcapStationTiles(
            Vector3Int barrierTile,
            Vector3Int threatTile,
            Alliance alliance)
        {
            var friendlyTiles = gameManager.tileSystem.LandTiles
                .Where(tile => tile.Controller == alliance)
                .Select(tile => tile.TileId)
                .ToHashSet();
            if (!friendlyTiles.Contains(barrierTile))
                return Array.Empty<Vector3Int>();

            var stations = new List<Vector3Int> { barrierTile };
            var station = barrierTile;
            var threatCenter = AirspaceGeometry.TileCenterFeet(
                threatTile,
                1f);
            var barrierCenter = AirspaceGeometry.TileCenterFeet(
                barrierTile,
                1f);
            var rearward = barrierCenter - threatCenter;
            rearward.y = 0f;
            if (rearward.sqrMagnitude < 0.001f)
                rearward = Vector3.forward;
            rearward.Normalize();
            while (true)
            {
                var currentThreatDistance = AirMissionArea.HexDistance(
                    station,
                    threatTile);
                var next = AirspaceGeometry.NeighborTiles(station)
                    .Where(friendlyTiles.Contains)
                    .Where(tile => AirMissionArea.HexDistance(tile, threatTile)
                                   > currentThreatDistance)
                    .OrderByDescending(tile => Vector3.Dot(
                        AirspaceGeometry.TileCenterFeet(tile, 1f)
                        - barrierCenter,
                        rearward))
                    .ThenByDescending(tile => AirMissionArea.HexDistance(
                        tile,
                        threatTile))
                    .ThenBy(tile => tile.x)
                    .ThenBy(tile => tile.y)
                    .ThenBy(tile => tile.z)
                    .Select(tile => (Vector3Int?)tile)
                    .FirstOrDefault();
                if (!next.HasValue || next.Value == station)
                    break;
                station = next.Value;
                stations.Add(station);
            }

            return stations;
        }

        private void GetBarcapWeaponPlanningValues(
            CombatSquadronCandidate candidate,
            out float preferredLaunchRangeKm,
            out float weaponPreparationSeconds)
        {
            var weapon = candidate.Loadout
                .Where(item => item.Count > 0
                               && ordnanceTypes.TryGetValue(
                                   item.OrdnanceTypeDefinitionId,
                                   out var definition)
                               && AirLoadoutPlanner.IsAirToAir(definition))
                .Select(item => ordnanceTypes[item.OrdnanceTypeDefinitionId])
                .OrderByDescending(definition => definition.MaximumRangeKm)
                .ThenBy(definition => definition.PreparationSeconds)
                .ThenBy(definition => definition.OrdnanceTypeDefinitionId)
                .FirstOrDefault();
            preferredLaunchRangeKm = weapon == null
                ? 0f
                : weapon.MaximumRangeKm * BarcapPreferredLaunchRangeFraction;
            weaponPreparationSeconds = weapon == null
                ? 0f
                : weapon.PreparationSeconds
                  / Math.Max(
                      0.01f,
                      candidate.AircraftType.OrdnanceEmploymentEfficiency);
        }

        private float CalculateBarcapSensorWarningMinutes(
            Alliance alliance,
            Vector3Int protectedTile,
            Vector3Int threatTile,
            float tileDistanceKm,
            float weaponReleaseStandoffKm,
            float threatSpeedKnots)
        {
            var releasePoint = BarcapInterceptGeometry
                .GetOperationalBarrierPointsFeet(
                    new[] { protectedTile },
                    threatTile,
                    tileDistanceKm,
                    weaponReleaseStandoffKm)
                .FirstOrDefault();
            var threatCenter = AirspaceGeometry.TileCenterFeet(
                threatTile,
                tileDistanceKm);
            var protectedCenter = AirspaceGeometry.TileCenterFeet(
                protectedTile,
                tileDistanceKm);
            var inbound = protectedCenter - threatCenter;
            inbound.y = 0f;
            if (inbound.sqrMagnitude < 1f)
                return 0f;
            inbound.Normalize();

            var threatSpeedFeetPerSecond = Math.Max(
                1f,
                Math.Max(300f, threatSpeedKnots) * 1.68781f);
            var pathLengthFeet = Vector3.Distance(threatCenter, releasePoint);
            var maximumLeadFeet = Math.Min(
                pathLengthFeet,
                threatSpeedFeetPerSecond * 10f * 60f);
            var bestLeadFeet = 0f;
            foreach (var site in gameManager.airDefenseSiteSystem.Sites
                         .Where(site => site != null
                                        && gameManager.airDefenseSiteSystem
                                            .GetEffectiveAlliance(site)
                                        == alliance))
            {
                if (!gameManager.airDefenseSiteSystem.TryGetTileId(
                        site,
                        out var siteTile))
                    continue;
                var sitePosition = AirspaceGeometry.TileCenterFeet(
                    siteTile,
                    tileDistanceKm);
                foreach (var component in gameManager.airDefenseSiteSystem
                             .GetAvailableComponents(site)
                             .OfType<RadarAirDefenseComponent>())
                {
                    if (component.IsDamaged
                        || !radarDefinitions.TryGetValue(
                            component.SamComponentDefinitionId,
                            out var definition)
                        || definition.DetectionRangeKm <= 0f
                        || definition.MaxAltitudeMeters
                        * AirspaceGeometry.FeetPerKilometer / 1000f
                        < BarcapAndOcaAltitudeFeet)
                    {
                        continue;
                    }

                    var radiusFeet = definition.DetectionRangeKm
                                      * AirspaceGeometry.FeetPerKilometer;
                    var releaseToRadar = releasePoint - sitePosition;
                    releaseToRadar.y = 0f;
                    var along = Vector3.Dot(releaseToRadar, inbound);
                    var perpendicularSquared = releaseToRadar.sqrMagnitude
                                               - along * along;
                    var radiusSquared = radiusFeet * radiusFeet;
                    if (perpendicularSquared > radiusSquared)
                        continue;

                    var halfChord = Mathf.Sqrt(
                        Math.Max(0f, radiusSquared - perpendicularSquared));
                    var entryLead = along + halfChord;
                    var exitLead = along - halfChord;
                    if (entryLead < 0f || exitLead > maximumLeadFeet)
                        continue;

                    bestLeadFeet = Math.Max(
                        bestLeadFeet,
                        Math.Min(maximumLeadFeet, entryLead));
                }
            }

            return bestLeadFeet / threatSpeedFeetPerSecond / 60f;
        }

        private static Vector3Int SelectLargestBarrierGapCenter(
            IReadOnlyList<Vector3Int> barrier,
            IReadOnlyCollection<Vector3Int> uncovered)
        {
            var uncoveredSet = uncovered.ToHashSet();
            var bestStart = 0;
            var bestLength = 0;
            var currentStart = 0;
            var currentLength = 0;
            for (var index = 0; index <= barrier.Count; index++)
            {
                if (index < barrier.Count && uncoveredSet.Contains(barrier[index]))
                {
                    if (currentLength == 0)
                        currentStart = index;
                    currentLength++;
                    continue;
                }

                if (currentLength > bestLength)
                {
                    bestStart = currentStart;
                    bestLength = currentLength;
                }
                currentLength = 0;
            }

            return barrier[bestStart + Math.Max(0, bestLength - 1) / 2];
        }

        private static List<SelectedCombatAircraft> SelectCombatAircraft(
            IReadOnlyList<CombatSquadronCandidate> candidates,
            int desiredStrength,
            bool allowMultipleAirports)
        {
            var sameAirportGroup = candidates
                .GroupBy(candidate => candidate.Squadron.AirportBuildingId)
                .Select(group => new
                {
                    Candidates = group.ToList(),
                    Count = group.Sum(candidate => candidate.AvailableAircraft.Count),
                    Distance = group.Min(candidate => candidate.DistanceTiles)
                })
                .Where(group => group.Count >= desiredStrength)
                .OrderBy(group => group.Distance)
                .ThenBy(group => group.Candidates[0].Squadron.AirportBuildingId)
                .FirstOrDefault();
            if (sameAirportGroup == null)
            {
                return allowMultipleAirports
                       && candidates.Sum(candidate => candidate.AvailableAircraft.Count)
                       >= desiredStrength
                    ? TakeAircraft(candidates, desiredStrength)
                    : new List<SelectedCombatAircraft>();
            }

            return TakeAircraft(sameAirportGroup.Candidates, desiredStrength);
        }

        private static List<SelectedCombatAircraft> TakeAircraft(
            IEnumerable<CombatSquadronCandidate> candidates,
            int desiredStrength)
        {
            var remaining = desiredStrength;
            var selected = new List<SelectedCombatAircraft>();
            foreach (var candidate in candidates)
            {
                if (remaining <= 0)
                    break;

                var aircraft = candidate.AvailableAircraft.Take(remaining).ToList();
                if (aircraft.Count == 0)
                    continue;

                selected.Add(new SelectedCombatAircraft(
                    candidate.Squadron,
                    candidate.AircraftType,
                    aircraft,
                    candidate.Loadout));
                remaining -= aircraft.Count;
            }

            return selected;
        }

        private AirPackage CreatePackage(
            AirMissionRequest request,
            DateTime currentTime)
        {
            return new AirPackage
            {
                MissionRequestId = request.MissionRequestId,
                Alliance = request.Alliance,
                CreatedAt = currentTime,
                Rationale = request.Rationale
            };
        }

        private static AirFlight CreateFlight(
            AirMissionRequest request,
            Squadron squadron,
            IReadOnlyCollection<CampaignAircraft> aircraft)
        {
            var flight = new AirFlight
            {
                SquadronId = squadron.SquadronId,
                MissionType = request.RequestType,
                IsRequired = true
            };
            flight.AircraftIds.AddRange(
                aircraft.Select(candidate => candidate.AircraftId));
            return flight;
        }

        private static void AddSelectedFlights(
            AirPackage package,
            AirMissionRequest request,
            IEnumerable<SelectedCombatAircraft> selectedCandidates)
        {
            foreach (var selected in selectedCandidates)
            {
                var flight = CreateFlight(
                    request,
                    selected.Squadron,
                    selected.Aircraft);
                foreach (var aircraft in selected.Aircraft)
                {
                    flight.PlannedAircraftLoadouts.Add(
                        new PlannedAircraftLoadout(
                            aircraft.AircraftId,
                            selected.Loadout));
                }

                package.Flights.Add(flight);
            }
        }

        private List<Squadron> GetFriendlySquadrons(Alliance alliance)
        {
            return gameManager.squadronSystem.Squadrons
                .Where(squadron =>
                    gameManager.GetCountryAlliance(squadron.CountryId) == alliance
                    && airportOperations.CanConductAirOperations(
                        squadron.AirportBuildingId,
                        alliance))
                .OrderBy(squadron => squadron.SquadronId)
                .ToList();
        }

        private static List<CampaignAircraft> GetAvailableAircraft(Squadron squadron)
        {
            return squadron.Aircraft
                .Where(aircraft => aircraft.Status == CampaignAircraftStatus.Ready
                                   && aircraft.AssignedFlightId == Guid.Empty)
                .ToList();
        }

        private int GetAirportDistance(Squadron squadron, Vector3Int targetTile)
        {
            if (!gameManager.buildingSystem.TryGetBuilding(squadron.AirportBuildingId, out var building))
                return int.MaxValue;

            return AirMissionArea.HexDistance(building.TileId, targetTile);
        }

        private IReadOnlyList<KnownSamThreatEnvelope> GetKnownSamThreats(
            Alliance alliance)
        {
            if (knownSamThreatCache.TryGetValue(alliance, out var cached))
                return cached;

            var threats = knownSamThreatAssessment.BuildKnownThreats(
                gameManager.intelligenceSystem?.GetPicture(alliance),
                gameManager.SimulationSettings.TileDistanceKM);
            knownSamThreatCache[alliance] = threats;
            return threats;
        }

        private static bool TrySelectSupportStationTile(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            out Vector3Int stationTileId,
            out string reason)
        {
            stationTileId = request.MissionArea.CenterTileId;
            reason = string.Empty;
            var candidates = commander.AirControlAssessments
                .Where(assessment => request.MissionArea.Contains(assessment.TileId))
                .OrderBy(assessment => assessment.HostileAirInterference)
                .ThenBy(assessment => AirMissionArea.HexDistance(
                    request.MissionArea.CenterTileId,
                    assessment.TileId))
                .ThenBy(assessment => assessment.TileId.x)
                .ThenBy(assessment => assessment.TileId.y)
                .ThenBy(assessment => assessment.TileId.z)
                .ToList();
            if (candidates.Count == 0)
            {
                reason = "No assessed support station exists in the requested area.";
                return false;
            }

            var selected = candidates[0];
            if (selected.HostileAirInterference
                >= MaximumSupportStationHostileInterference)
            {
                reason = "No support station in the requested area is outside meaningful hostile air interference.";
                return false;
            }

            stationTileId = selected.TileId;
            return true;
        }

        private bool TryMaterializeRoutes(
            AllianceAirTaskingCommander commander,
            AirPackage package,
            AirMissionRequest request,
            DateTime earliestTakeoff,
            DateTime proposedEffectStart,
            DateTime proposedEffectEnd,
            Vector3Int? missionCenterOverride,
            BarcapStationCoverage barcapCoverage,
            out string reason)
        {
            reason = string.Empty;
            var plans = new List<RoutePlan>();
            foreach (var flight in package.Flights)
            {
                if (!gameManager.squadronSystem.TryGetSquadron(flight.SquadronId, out var squadron)
                    || !gameManager.buildingSystem.TryGetBuilding(squadron.AirportBuildingId, out var airport)
                    || !aircraftTypes.TryGetValue(squadron.AircraftTypeDefinitionId, out var aircraftType))
                {
                    reason = "A flight route could not resolve its squadron, airport, or aircraft type.";
                    return false;
                }

                plans.Add(new RoutePlan(
                    flight,
                    squadron,
                    aircraftType,
                    AirspaceGeometry.TileCenterFeet(
                        airport.TileId,
                        gameManager.SimulationSettings.TileDistanceKM)));
            }

            var desiredMissionAltitude = GetMissionAltitudeFeet(request.RequestType);
            var missionAltitude = plans.Min(plan =>
                Math.Min(desiredMissionAltitude, plan.AircraftType.ServiceCeilingFeet));
            var missionCenter = AirspaceGeometry.TileCenterFeet(
                missionCenterOverride ?? request.MissionArea.CenterTileId,
                gameManager.SimulationSettings.TileDistanceKM,
                missionAltitude);
            var tileDistanceFeet = gameManager.SimulationSettings.TileDistanceKM
                                   * AirspaceGeometry.FeetPerKilometer;
            var knownSamThreats = GetKnownSamThreats(package.Alliance);
            var combat = request.RequestType == AirMissionRequestType.BarrierCombatAirPatrol
                         || request.RequestType == AirMissionRequestType.OffensiveCounterAirSweep;
            var hasRendezvous = combat && plans.Count > 1;
            var rendezvousPosition = Vector3.zero;
            var coordinatedSpeed = plans.Min(plan => Math.Max(1f, plan.AircraftType.CruiseSpeedKnots));
            if (hasRendezvous)
            {
                var baseCentroid = plans.Aggregate(
                    Vector3.zero,
                    (sum, plan) => sum + plan.BasePositionFeet) / plans.Count;
                rendezvousPosition = (baseCentroid + missionCenter) * 0.5f;
                rendezvousPosition.y = missionAltitude;
            }

            foreach (var plan in plans)
            {
                var maneuverClearanceFeet =
                    AirspaceGeometry.SamManeuverClearanceFeet(
                        plan.AircraftType,
                        plan.AircraftType.CruiseSpeedKnots);
                var missionOrigin = hasRendezvous ? rendezvousPosition : plan.BasePositionFeet;
                SetMissionGeometry(
                    plan,
                    commander,
                    request,
                    missionOrigin,
                    missionCenter,
                    tileDistanceFeet,
                    barcapCoverage);
                if (!TryValidateMissionGeometry(
                        plan,
                        request,
                        knownSamThreats,
                        maneuverClearanceFeet,
                        out reason))
                    return false;

                if (hasRendezvous)
                {
                    if (!KnownSamThreatGeometry.TryBuildAvoidingWaypoints(
                            plan.BasePositionFeet,
                            rendezvousPosition,
                            knownSamThreats,
                            package.PackageId,
                            maneuverClearanceFeet,
                            out var assemblyPath))
                    {
                        reason =
                            "No continuous-airspace route to the package rendezvous avoids known SAM coverage.";
                        return false;
                    }

                    plan.AssemblyWaypoints = assemblyPath
                        .Take(Math.Max(0, assemblyPath.Count - 1))
                        .ToList();
                }

                plan.RouteGeometry = routeGeometryPlanner.Plan(new AirRouteGeometryPlanningContext(
                    missionOrigin,
                    plan.MissionEntryPosition,
                    plan.MissionExitPosition,
                    plan.BasePositionFeet,
                    tileDistanceFeet,
                    package.PackageId,
                    knownSamThreats,
                    maneuverClearanceFeet));
                if (!plan.RouteGeometry.IsThreatSafe)
                {
                    reason =
                        "No continuous-airspace ingress and egress route avoids known SAM coverage.";
                    return false;
                }
                if (!TryValidateRecoveryGeometry(
                        plan,
                        knownSamThreats,
                        maneuverClearanceFeet,
                        out reason))
                    return false;
            }

            var plannedEffectStart = proposedEffectStart;
            var rendezvousTime = plannedEffectStart;
            if (hasRendezvous)
            {
                rendezvousTime -= TimeSpan.FromSeconds(
                    plans.Max(plan => TravelSecondsAlong(
                        rendezvousPosition,
                        plan.RouteGeometry.IngressWaypoints,
                        plan.MissionEntryPosition,
                        coordinatedSpeed,
                        plan.AircraftType.ClimbRateFeetPerMinute,
                        plan.AircraftType.DescentRateFeetPerMinute)));
            }

            var requiredShift = TimeSpan.Zero;
            foreach (var plan in plans)
            {
                var takeoff = hasRendezvous
                    ? rendezvousTime - TimeSpan.FromSeconds(
                        TravelSecondsAlong(
                            plan.BasePositionFeet,
                            plan.AssemblyWaypoints,
                            rendezvousPosition,
                            plan.AircraftType.CruiseSpeedKnots,
                            plan.AircraftType.ClimbRateFeetPerMinute,
                            plan.AircraftType.DescentRateFeetPerMinute))
                    : plannedEffectStart - TimeSpan.FromSeconds(
                        TravelSecondsAlong(
                            plan.BasePositionFeet,
                            plan.RouteGeometry.IngressWaypoints,
                            plan.MissionEntryPosition,
                            plan.AircraftType.CruiseSpeedKnots,
                            plan.AircraftType.ClimbRateFeetPerMinute,
                            plan.AircraftType.DescentRateFeetPerMinute));
                plan.PlannedTakeoff = takeoff;
                if (takeoff < earliestTakeoff && earliestTakeoff - takeoff > requiredShift)
                    requiredShift = earliestTakeoff - takeoff;
            }

            if (requiredShift > TimeSpan.Zero)
            {
                plannedEffectStart += requiredShift;
                rendezvousTime += requiredShift;
                foreach (var plan in plans)
                    plan.PlannedTakeoff += requiredShift;
            }

            var plannedEffectEnd = proposedEffectEnd;
            if (request.FulfillmentPattern
                == AirMissionRequestFulfillmentPattern.Sustained)
            {
                foreach (var plan in plans)
                {
                    var usableFuelSeconds =
                        AirFuelRules.CalculateUsableSecondsUntilJoker(
                            plan.AircraftType,
                            commander.Doctrine)
                        - FuelPlanningMarginSeconds;
                    var fuelLimitedEnd = plan.PlannedTakeoff
                                         + TimeSpan.FromSeconds(Math.Max(0f, usableFuelSeconds));
                    if (fuelLimitedEnd < plannedEffectEnd)
                        plannedEffectEnd = fuelLimitedEnd;
                }
            }

            if (request.RequestType
                == AirMissionRequestType.BarrierCombatAirPatrol
                && barcapCoverage != null
                && plans.Count == 1
                && plannedEffectStart < plannedEffectEnd
                && plans[0].AircraftType.CanReceiveAerialRefueling)
            {
                var receiverArea = CreateSustainedEffectArea(
                    request,
                    plans[0],
                    barcapCoverage);
                var reservations = projectedEffects.PlanAerialRefuelingCoverage(
                    commander,
                    receiverArea,
                    package.PackageId,
                    plans[0].Flight.AircraftIds.Count,
                    plannedEffectStart,
                    proposedEffectEnd,
                    out var supportedUntil);
                if (supportedUntil > plannedEffectEnd)
                {
                    plannedEffectEnd = supportedUntil < proposedEffectEnd
                        ? supportedUntil
                        : proposedEffectEnd;
                    package.SupportingFlightIds.AddRange(
                        reservations
                            .Where(reservation =>
                                reservation.StartTime < plannedEffectEnd)
                            .Select(reservation => reservation.SupportingFlightId)
                            .Distinct()
                            .OrderBy(id => id));
                }
            }

            if (plannedEffectStart >= plannedEffectEnd)
            {
                reason = request.RequestType == AirMissionRequestType.BarrierCombatAirPatrol
                    ? "Preparation and transit leave no usable fuel-bounded patrol time."
                    : "Preparation and transit leave no time for the requested effect.";
                return false;
            }

            if (request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained)
            {
                var firstTrackEnd = plans.Max(plan =>
                    plannedEffectStart + TimeSpan.FromSeconds(
                        AirspaceGeometry.TravelSeconds(
                            plan.MissionEntryPosition,
                            plan.MissionExitPosition,
                            plan.AircraftType.CruiseSpeedKnots,
                            plan.AircraftType.ClimbRateFeetPerMinute,
                            plan.AircraftType.DescentRateFeetPerMinute)));
                if (firstTrackEnd > plannedEffectEnd)
                {
                    reason = request.RequestType == AirMissionRequestType.BarrierCombatAirPatrol
                        ? "The fuel-bounded patrol window is shorter than one station circuit."
                        : "The remaining effect window is shorter than one station circuit.";
                    return false;
                }
            }

            foreach (var plan in plans)
            {
                BuildRoute(
                    plan,
                    request,
                    plannedEffectStart,
                    plannedEffectEnd,
                    hasRendezvous,
                    rendezvousPosition,
                    rendezvousTime,
                    coordinatedSpeed,
                    barcapCoverage);
            }

            IReadOnlyList<AirSupportReservation>
                shiftedSupportReservations = null;
            IReadOnlyList<TimeSpan> additionalShiftCandidates = null;
            var supportSchedulingReason = string.Empty;
            Func<TimeSpan, bool> additionalConstraint = null;
            if (package.SupportingFlightIds.Count > 0)
            {
                var receiverArea = CreateSustainedEffectArea(
                    request,
                    plans[0],
                    barcapCoverage);
                additionalShiftCandidates =
                    projectedEffects.GetAerialRefuelingShiftCandidates(
                        commander,
                        receiverArea,
                        package.EffectStart,
                        package.SupportWindowEnd,
                        request.EffectEnd - package.EffectEnd);
                additionalConstraint = candidate =>
                {
                    var shiftedEffectStart =
                        package.EffectStart + candidate;
                    var shiftedEffectEnd =
                        package.SupportWindowEnd + candidate;
                    var reservations =
                        projectedEffects.PlanAerialRefuelingCoverage(
                            commander,
                            receiverArea,
                            package.PackageId,
                            plans[0].Flight.AircraftIds.Count,
                            shiftedEffectStart,
                            shiftedEffectEnd,
                            out var supportedUntil);
                    if (supportedUntil < shiftedEffectEnd)
                    {
                        supportSchedulingReason =
                            "No runway-capacity window with required tanker "
                            + "coverage is available before the requested effect ends.";
                        return false;
                    }

                    shiftedSupportReservations = reservations;
                    return true;
                };
            }

            if (!airportOperations.TryFindFeasibleShift(
                    package,
                    commander.Packages,
                    request.EffectEnd,
                    additionalShiftCandidates,
                    additionalConstraint,
                    out var airportShift,
                    out reason))
            {
                if (!string.IsNullOrEmpty(supportSchedulingReason))
                    reason = supportSchedulingReason;
                return false;
            }
            if (!package.TryShiftPlannedRoutes(airportShift, out reason))
                return false;

            if (shiftedSupportReservations != null)
            {
                package.SupportingFlightIds.Clear();
                package.SupportingFlightIds.AddRange(
                    shiftedSupportReservations
                        .Select(reservation =>
                            reservation.SupportingFlightId)
                        .Distinct()
                        .OrderBy(id => id));
            }

            return true;
        }

        private static bool TryValidateMissionGeometry(
            RoutePlan plan,
            AirMissionRequest request,
            IReadOnlyList<KnownSamThreatEnvelope> knownSamThreats,
            float maneuverClearanceFeet,
            out string reason)
        {
            reason = string.Empty;
            IReadOnlyList<Vector3> missionPath;
            if (request.RequestType == AirMissionRequestType.OffensiveCounterAirSweep)
            {
                missionPath = new[]
                {
                    plan.MissionEntryPosition,
                    plan.MissionPushPosition,
                    plan.MissionExitPosition
                };
            }
            else
            {
                missionPath = new[]
                {
                    plan.MissionEntryPosition,
                    plan.MissionExitPosition
                };
            }

            if (KnownSamThreatGeometry.IsPathSafe(
                    missionPath,
                    knownSamThreats,
                    maneuverClearanceFeet,
                    out var blockingSiteId))
                return true;

            reason = $"Mission geometry enters known SAM coverage from site "
                     + $"{blockingSiteId.ToString("N").Substring(0, 8)}.";
            return false;
        }

        private static bool TryValidateRecoveryGeometry(
            RoutePlan plan,
            IReadOnlyList<KnownSamThreatEnvelope> knownSamThreats,
            float maneuverClearanceFeet,
            out string reason)
        {
            reason = string.Empty;
            var recoveryStart = plan.RouteGeometry.EgressWaypoints.Count > 0
                ? plan.RouteGeometry.EgressWaypoints[
                    plan.RouteGeometry.EgressWaypoints.Count - 1]
                : plan.MissionExitPosition;
            var recoveryPath = new List<Vector3> { recoveryStart };
            recoveryPath.AddRange(
                AirRecoveryRouteBuilder.Build(
                        recoveryStart,
                        plan.AircraftType,
                        plan.Squadron.AirportBuildingId,
                        plan.BasePositionFeet,
                        new DateTime(2000, 1, 1))
                    .Select(waypoint => waypoint.PositionFeet));

            if (KnownSamThreatGeometry.IsPathSafe(
                    recoveryPath,
                    knownSamThreats,
                    maneuverClearanceFeet,
                    out var blockingSiteId))
                return true;

            reason = $"Recovery approach enters known SAM coverage from site "
                     + $"{blockingSiteId.ToString("N").Substring(0, 8)}.";
            return false;
        }

        private static void SetMissionGeometry(
            RoutePlan plan,
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            Vector3 missionOrigin,
            Vector3 missionCenter,
            float tileDistanceFeet,
            BarcapStationCoverage barcapCoverage)
        {
            if (request.RequestType == AirMissionRequestType.OffensiveCounterAirSweep)
            {
                var tileDistanceKm = tileDistanceFeet / AirspaceGeometry.FeetPerKilometer;
                var originTileId = AirspaceGeometry.TileCoordinateFromPositionFeet(
                    missionOrigin,
                    tileDistanceKm);
                var centerTileId = request.MissionArea.CenterTileId;
                var entryTileId = SelectOcaEntryTile(
                    commander,
                    originTileId,
                    centerTileId);
                var pushTileId = SelectOcaPushTile(
                    commander,
                    originTileId,
                    centerTileId);
                plan.MissionEntryPosition = AirspaceGeometry.TileCenterFeet(
                    entryTileId,
                    tileDistanceKm,
                    missionCenter.y);
                plan.MissionPushPosition = AirspaceGeometry.TileCenterFeet(
                    pushTileId,
                    tileDistanceKm,
                    missionCenter.y);
                plan.MissionExitPosition = plan.MissionEntryPosition;
                return;
            }

            if (request.RequestType == AirMissionRequestType.BarrierCombatAirPatrol)
            {
                var tileDistanceKm = tileDistanceFeet / AirspaceGeometry.FeetPerKilometer;
                var stationCenter = missionCenter;
                var threatTileId = barcapCoverage?.ThreatReferenceTileId
                                   ?? request.BarcapBarrier?.ThreatReferenceTileId
                                   ?? SelectBarcapThreatTile(
                                       commander,
                                       request.MissionArea);
                var threatCenter = AirspaceGeometry.TileCenterFeet(
                    threatTileId,
                    tileDistanceKm,
                    missionCenter.y);
                var threatDirection = threatCenter - stationCenter;
                threatDirection.y = 0f;
                if (threatDirection.sqrMagnitude < 1f)
                {
                    threatDirection = stationCenter - missionOrigin;
                    threatDirection.y = 0f;
                }
                if (threatDirection.sqrMagnitude < 1f)
                    threatDirection = Vector3.forward;
                threatDirection.Normalize();
                var trackDirection = new Vector3(
                    -threatDirection.z,
                    0f,
                    threatDirection.x);
                var barcapTrackOffset = BuildDefensiveBarcapTrackOffset(
                    trackDirection,
                    tileDistanceFeet);
                plan.MissionEntryPosition = stationCenter - barcapTrackOffset;
                plan.MissionPushPosition = stationCenter + barcapTrackOffset;
                plan.MissionExitPosition = stationCenter + barcapTrackOffset;
                return;
            }

            if (request.FulfillmentPattern != AirMissionRequestFulfillmentPattern.Sustained)
            {
                plan.MissionEntryPosition = missionCenter;
                plan.MissionPushPosition = missionCenter;
                plan.MissionExitPosition = missionCenter;
                return;
            }

            var trackOffset = Vector3.right
                              * tileDistanceFeet
                              * DefaultStationTrackHalfLengthTiles;
            plan.MissionEntryPosition = missionCenter - trackOffset;
            plan.MissionPushPosition = missionCenter + trackOffset;
            plan.MissionExitPosition = missionCenter + trackOffset;
        }

        private static Vector3Int SelectBarcapThreatTile(
            AllianceAirTaskingCommander commander,
            AirMissionArea missionArea)
        {
            var threat = commander.AirControlAssessments
                .Where(assessment => missionArea.Contains(assessment.TileId))
                .OrderByDescending(assessment => Math.Max(
                    assessment.HostileAirActivity,
                    assessment.HostileCombatPresence))
                .ThenByDescending(assessment => assessment.HostileAirActivity)
                .ThenBy(assessment => assessment.TileId.x)
                .ThenBy(assessment => assessment.TileId.y)
                .ThenBy(assessment => assessment.TileId.z)
                .FirstOrDefault();
            if (threat == null
                || Math.Max(threat.HostileAirActivity, threat.HostileCombatPresence)
                < MeaningfulBarcapPressure)
                return missionArea.CenterTileId;

            return threat.TileId;
        }

        private static Vector3 BuildDefensiveBarcapTrackOffset(
            Vector3 trackDirection,
            float tileDistanceFeet)
        {
            // Coverage calculation includes this maximum displacement so a
            // patrol is credited only when either end of its track can respond.
            return trackDirection
                   * tileDistanceFeet
                   * DefaultStationTrackHalfLengthTiles;
        }

        private static Vector3Int SelectOcaEntryTile(
            AllianceAirTaskingCommander commander,
            Vector3Int originTileId,
            Vector3Int centerTileId)
        {
            var centerDistance = AirMissionArea.HexDistance(originTileId, centerTileId);
            return AirspaceGeometry.NeighborTiles(centerTileId)
                .Where(tileId => AirMissionArea.HexDistance(originTileId, tileId)
                                 < centerDistance)
                .Select(tileId => commander.TryGetAirControlAssessment(
                        tileId,
                        out var assessment)
                    ? assessment
                    : null)
                .Where(assessment => assessment != null)
                .OrderBy(assessment => assessment.HostileAirInterference)
                .ThenBy(assessment => assessment.TileId.x)
                .ThenBy(assessment => assessment.TileId.y)
                .ThenBy(assessment => assessment.TileId.z)
                .Select(assessment => assessment.TileId)
                .DefaultIfEmpty(centerTileId)
                .First();
        }

        private static Vector3Int SelectOcaPushTile(
            AllianceAirTaskingCommander commander,
            Vector3Int originTileId,
            Vector3Int centerTileId)
        {
            var centerDistance = AirMissionArea.HexDistance(originTileId, centerTileId);
            var maximumHostilePresence = Mathf.Lerp(
                0.35f,
                0.85f,
                commander.Doctrine.RiskTolerance);
            return AirspaceGeometry.NeighborTiles(centerTileId)
                .Append(centerTileId)
                .Where(tileId => AirMissionArea.HexDistance(originTileId, tileId)
                                 >= centerDistance)
                .Select(tileId => commander.TryGetAirControlAssessment(
                        tileId,
                        out var assessment)
                    ? assessment
                    : null)
                .Where(assessment => assessment != null
                                     && assessment.HostileCombatPresence
                                     <= maximumHostilePresence
                                     && (assessment.TileId == centerTileId
                                         || assessment.HostileCombatPresence
                                         >= MeaningfulOcaPresence
                                         || assessment.HostileAirActivity
                                         >= MeaningfulOcaPresence))
                .OrderByDescending(assessment => AirMissionArea.HexDistance(
                    originTileId,
                    assessment.TileId))
                .ThenByDescending(assessment => assessment.HostileAirActivity)
                .ThenByDescending(assessment => assessment.HostileCombatPresence)
                .ThenBy(assessment => assessment.TileId.x)
                .ThenBy(assessment => assessment.TileId.y)
                .ThenBy(assessment => assessment.TileId.z)
                .Select(assessment => assessment.TileId)
                .DefaultIfEmpty(centerTileId)
                .First();
        }

        private void BuildRoute(
            RoutePlan plan,
            AirMissionRequest request,
            DateTime effectStart,
            DateTime effectEnd,
            bool hasRendezvous,
            Vector3 rendezvousPosition,
            DateTime rendezvousTime,
            float coordinatedSpeed,
            BarcapStationCoverage barcapCoverage)
        {
            var flight = plan.Flight;
            var route = new List<AirWaypoint>();
            route.Add(NewWaypoint(
                plan.BasePositionFeet,
                AirWaypointAction.Takeoff,
                plan.PlannedTakeoff,
                airportBuildingId: plan.Squadron.AirportBuildingId));
            if (hasRendezvous)
            {
                AppendTransitRoute(
                    route,
                    plan.BasePositionFeet,
                    plan.PlannedTakeoff,
                    plan.AssemblyWaypoints,
                    plan.AircraftType,
                    plan.AircraftType.CruiseSpeedKnots);
                route.Add(NewWaypoint(rendezvousPosition, AirWaypointAction.Rendezvous, rendezvousTime));
            }

            AppendTransitRoute(
                route,
                hasRendezvous ? rendezvousPosition : plan.BasePositionFeet,
                hasRendezvous ? rendezvousTime : plan.PlannedTakeoff,
                plan.RouteGeometry.IngressWaypoints,
                plan.AircraftType,
                hasRendezvous ? coordinatedSpeed : plan.AircraftType.CruiseSpeedKnots);

            DateTime returnTime;
            Vector3 returnPosition;
            if (request.RequestType == AirMissionRequestType.OffensiveCounterAirSweep)
            {
                var effectArea = new AirMissionArea(
                    request.MissionArea.CenterTileId,
                    request.MissionArea.RadiusTiles);
                var stationEntry = NewWaypoint(
                    plan.MissionEntryPosition,
                    AirWaypointAction.StationEntry,
                    effectStart,
                    effectArea);
                var pushTime = effectStart + TimeSpan.FromSeconds(
                    AirspaceGeometry.TravelSeconds(
                        plan.MissionEntryPosition,
                        plan.MissionPushPosition,
                        plan.AircraftType.CruiseSpeedKnots,
                        plan.AircraftType.ClimbRateFeetPerMinute,
                        plan.AircraftType.DescentRateFeetPerMinute));
                var exitTime = pushTime + TimeSpan.FromSeconds(
                    AirspaceGeometry.TravelSeconds(
                        plan.MissionPushPosition,
                        plan.MissionExitPosition,
                        plan.AircraftType.CruiseSpeedKnots,
                        plan.AircraftType.ClimbRateFeetPerMinute,
                        plan.AircraftType.DescentRateFeetPerMinute));
                route.Add(stationEntry);
                route.Add(NewWaypoint(
                    plan.MissionPushPosition,
                    AirWaypointAction.StationEndpoint,
                    pushTime,
                    repeatFromWaypointId: stationEntry.WaypointId));
                route.Add(NewWaypoint(
                    plan.MissionExitPosition,
                    AirWaypointAction.MissionAction,
                    exitTime,
                    effectArea));
                returnTime = exitTime;
                returnPosition = plan.MissionExitPosition;
            }
            else if (request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained)
            {
                var effectArea = CreateSustainedEffectArea(
                    request,
                    plan,
                    barcapCoverage);
                var stationEntry = NewWaypoint(
                    plan.MissionEntryPosition,
                    AirWaypointAction.StationEntry,
                    effectStart,
                    effectArea,
                    barcapCoverage);
                var stationEnd = NewWaypoint(
                    plan.MissionExitPosition,
                    AirWaypointAction.StationEndpoint,
                    effectStart + TimeSpan.FromSeconds(
                        AirspaceGeometry.TravelSeconds(
                            plan.MissionEntryPosition,
                            plan.MissionExitPosition,
                            plan.AircraftType.CruiseSpeedKnots,
                            plan.AircraftType.ClimbRateFeetPerMinute,
                            plan.AircraftType.DescentRateFeetPerMinute)),
                    hasRepeat: true,
                    repeatFromWaypointId: stationEntry.WaypointId,
                    repeatUntil: effectEnd);
                route.Add(stationEntry);
                route.Add(stationEnd);
                returnTime = effectEnd;
                returnPosition = plan.MissionExitPosition;
            }
            else
            {
                route.Add(NewWaypoint(
                    plan.MissionEntryPosition,
                    AirWaypointAction.MissionAction,
                    effectStart,
                    new AirMissionArea(
                        request.MissionArea.CenterTileId,
                        request.MissionArea.RadiusTiles)));
                returnTime = effectStart;
                returnPosition = plan.MissionExitPosition;
            }

            route.Add(NewWaypoint(returnPosition, AirWaypointAction.ReturnToBase, returnTime));
            returnTime = AppendTransitRoute(
                route,
                returnPosition,
                returnTime,
                plan.RouteGeometry.EgressWaypoints,
                plan.AircraftType,
                plan.AircraftType.CruiseSpeedKnots);
            if (plan.RouteGeometry.EgressWaypoints.Count > 0)
                returnPosition = plan.RouteGeometry.EgressWaypoints[
                    plan.RouteGeometry.EgressWaypoints.Count - 1];
            foreach (var waypoint in AirRecoveryRouteBuilder.Build(
                         returnPosition,
                         plan.AircraftType,
                         plan.Squadron.AirportBuildingId,
                         plan.BasePositionFeet,
                         returnTime))
            {
                route.Add(waypoint);
            }

            flight.MaterializeRoute(route);
        }

        private AirMissionArea CreateSustainedEffectArea(
            AirMissionRequest request,
            RoutePlan plan,
            BarcapStationCoverage barcapCoverage)
        {
            if (barcapCoverage == null)
            {
                return new AirMissionArea(
                    request.MissionArea.CenterTileId,
                    request.MissionArea.RadiusTiles);
            }

            return new AirMissionArea(
                AirspaceGeometry.TileCoordinateFromPositionFeet(
                    plan.MissionEntryPosition
                    + (plan.MissionExitPosition - plan.MissionEntryPosition) * 0.5f,
                    gameManager.SimulationSettings.TileDistanceKM),
                Math.Max(
                    1,
                    Mathf.CeilToInt(
                        barcapCoverage.PlannedResponseRadiusKm
                        / gameManager.SimulationSettings.TileDistanceKM)));
        }

        private static AirWaypoint NewWaypoint(
            Vector3 positionFeet,
            AirWaypointAction action,
            DateTime plannedArrivalTime,
            AirMissionArea effectArea = null,
            BarcapStationCoverage barcapCoverage = null,
            bool hasRepeat = false,
            Guid repeatFromWaypointId = default,
            DateTime repeatUntil = default,
            Guid airportBuildingId = default)
        {
            return new AirWaypoint(
                positionFeet,
                action,
                plannedArrivalTime,
                effectArea,
                barcapCoverage: barcapCoverage,
                hasRepeat: hasRepeat,
                repeatFromWaypointId: repeatFromWaypointId,
                repeatUntil: repeatUntil,
                airportBuildingId: airportBuildingId);
        }

        private static DateTime AppendTransitRoute(
            ICollection<AirWaypoint> route,
            Vector3 start,
            DateTime startTime,
            IReadOnlyList<Vector3> transitPoints,
            AircraftTypeDefinition aircraftType,
            float speedKnots)
        {
            var position = start;
            var time = startTime;
            foreach (var point in transitPoints)
            {
                time += TimeSpan.FromSeconds(AirspaceGeometry.TravelSeconds(
                    position,
                    point,
                    speedKnots,
                    aircraftType.ClimbRateFeetPerMinute,
                    aircraftType.DescentRateFeetPerMinute));
                route.Add(NewWaypoint(point, AirWaypointAction.Transit, time));
                position = point;
            }

            return time;
        }

        private static double TravelSecondsAlong(
            Vector3 start,
            IReadOnlyList<Vector3> transitPoints,
            Vector3 end,
            float speedKnots,
            float climbRateFeetPerMinute,
            float descentRateFeetPerMinute)
        {
            var seconds = 0d;
            var position = start;
            foreach (var point in transitPoints)
            {
                seconds += AirspaceGeometry.TravelSeconds(
                    position,
                    point,
                    speedKnots,
                    climbRateFeetPerMinute,
                    descentRateFeetPerMinute);
                position = point;
            }

            return seconds + AirspaceGeometry.TravelSeconds(
                position,
                end,
                speedKnots,
                climbRateFeetPerMinute,
                descentRateFeetPerMinute);
        }

        private static float GetMissionAltitudeFeet(AirMissionRequestType missionType)
        {
            return missionType switch
            {
                AirMissionRequestType.ProvideAirborneC2 => AwacsAltitudeFeet,
                AirMissionRequestType.ProvideAerialRefueling => TankerAltitudeFeet,
                _ => BarcapAndOcaAltitudeFeet
            };
        }

        private static DateTime Min(DateTime first, DateTime second)
        {
            return first <= second ? first : second;
        }

        private sealed class CombatSquadronCandidate
        {
            public Squadron Squadron { get; }
            public AircraftTypeDefinition AircraftType { get; }
            public List<CampaignAircraft> AvailableAircraft { get; }
            public List<AircraftLoadoutItem> Loadout { get; }
            public int DistanceTiles { get; }

            public CombatSquadronCandidate(
                Squadron squadron,
                AircraftTypeDefinition aircraftType,
                List<CampaignAircraft> availableAircraft,
                List<AircraftLoadoutItem> loadout,
                int distanceTiles)
            {
                Squadron = squadron;
                AircraftType = aircraftType;
                AvailableAircraft = availableAircraft;
                Loadout = loadout;
                DistanceTiles = distanceTiles;
            }
        }

        private sealed class BarcapSelectionChoice
        {
            public CombatSquadronCandidate Candidate { get; }
            public float RadiusKm { get; }
            public float PreferredLaunchRangeKm { get; }
            public Vector3Int Station { get; }
            public int StationDepth { get; }
            public List<Vector3Int> Covered { get; }
            public int UncoveredCount { get; }

            public BarcapSelectionChoice(
                CombatSquadronCandidate candidate,
                float radiusKm,
                float preferredLaunchRangeKm,
                Vector3Int station,
                int stationDepth,
                List<Vector3Int> covered,
                int uncoveredCount)
            {
                Candidate = candidate;
                RadiusKm = Math.Max(0f, radiusKm);
                PreferredLaunchRangeKm = Math.Max(
                    0f,
                    preferredLaunchRangeKm);
                Station = station;
                StationDepth = Math.Max(0, stationDepth);
                Covered = covered ?? new List<Vector3Int>();
                UncoveredCount = Math.Max(0, uncoveredCount);
            }
        }

        private sealed class RoutePlan
        {
            public readonly AirFlight Flight;
            public readonly Squadron Squadron;
            public readonly AircraftTypeDefinition AircraftType;
            public readonly Vector3 BasePositionFeet;
            public DateTime PlannedTakeoff;
            public AirRouteGeometry RouteGeometry;
            public IReadOnlyList<Vector3> AssemblyWaypoints =
                Array.Empty<Vector3>();
            public Vector3 MissionEntryPosition;
            public Vector3 MissionPushPosition;
            public Vector3 MissionExitPosition;

            public RoutePlan(
                AirFlight flight,
                Squadron squadron,
                AircraftTypeDefinition aircraftType,
                Vector3 basePositionFeet)
            {
                Flight = flight;
                Squadron = squadron;
                AircraftType = aircraftType;
                BasePositionFeet = basePositionFeet;
            }
        }

        private sealed class SelectedCombatAircraft
        {
            public Squadron Squadron { get; }
            public AircraftTypeDefinition AircraftType { get; }
            public List<CampaignAircraft> Aircraft { get; }
            public List<AircraftLoadoutItem> Loadout { get; }

            public SelectedCombatAircraft(
                Squadron squadron,
                AircraftTypeDefinition aircraftType,
                List<CampaignAircraft> aircraft,
                List<AircraftLoadoutItem> loadout)
            {
                Squadron = squadron;
                AircraftType = aircraftType;
                Aircraft = aircraft;
                Loadout = loadout;
            }
        }
    }

    public interface IAirRouteGeometryPlanner
    {
        AirRouteGeometry Plan(AirRouteGeometryPlanningContext context);
    }

    public sealed class AirRouteGeometryPlanningContext
    {
        public Vector3 IngressOrigin { get; }
        public Vector3 MissionEntry { get; }
        public Vector3 MissionExit { get; }
        public Vector3 RecoveryDestination { get; }
        public float TileDistanceFeet { get; }
        public Guid RouteKey { get; }
        public IReadOnlyList<KnownSamThreatEnvelope> KnownSamThreats { get; }
        public float ManeuverClearanceFeet { get; }

        public AirRouteGeometryPlanningContext(
            Vector3 ingressOrigin,
            Vector3 missionEntry,
            Vector3 missionExit,
            Vector3 recoveryDestination,
            float tileDistanceFeet,
            Guid routeKey,
            IReadOnlyList<KnownSamThreatEnvelope> knownSamThreats = null,
            float maneuverClearanceFeet = 0f)
        {
            IngressOrigin = ingressOrigin;
            MissionEntry = missionEntry;
            MissionExit = missionExit;
            RecoveryDestination = recoveryDestination;
            TileDistanceFeet = Math.Max(0f, tileDistanceFeet);
            RouteKey = routeKey;
            KnownSamThreats =
                knownSamThreats ?? Array.Empty<KnownSamThreatEnvelope>();
            ManeuverClearanceFeet = Math.Max(0f, maneuverClearanceFeet);
        }
    }

    public sealed class AirRouteGeometry
    {
        public IReadOnlyList<Vector3> IngressWaypoints { get; }
        public IReadOnlyList<Vector3> EgressWaypoints { get; }
        public bool IsThreatSafe { get; }

        public AirRouteGeometry(
            IReadOnlyList<Vector3> ingressWaypoints,
            IReadOnlyList<Vector3> egressWaypoints,
            bool isThreatSafe = true)
        {
            IngressWaypoints = ingressWaypoints ?? Array.Empty<Vector3>();
            EgressWaypoints = egressWaypoints ?? Array.Empty<Vector3>();
            IsThreatSafe = isThreatSafe;
        }
    }

    public sealed class SeparatedIngressEgressRouteGeometryPlanner : IAirRouteGeometryPlanner
    {
        private const float MaximumOffsetLegFraction = 0.25f;

        public AirRouteGeometry Plan(AirRouteGeometryPlanningContext context)
        {
            var side = SelectSide(context.RouteKey);
            var ingress = CreateOffsetMidpoint(
                context.IngressOrigin,
                context.MissionEntry,
                context.TileDistanceFeet,
                side);
            var egress = CreateOffsetMidpoint(
                context.MissionExit,
                context.RecoveryDestination,
                context.TileDistanceFeet,
                side);
            var ingressControlPoints = new List<Vector3>
            {
                context.IngressOrigin
            };
            if (ingress.HasValue)
                ingressControlPoints.Add(ingress.Value);
            ingressControlPoints.Add(context.MissionEntry);

            var egressControlPoints = new List<Vector3>
            {
                context.MissionExit
            };
            if (egress.HasValue)
                egressControlPoints.Add(egress.Value);
            egressControlPoints.Add(context.RecoveryDestination);

            if (!KnownSamThreatGeometry.TryBuildAvoidingPath(
                    ingressControlPoints,
                    context.KnownSamThreats,
                    context.RouteKey,
                    context.ManeuverClearanceFeet,
                    out var safeIngress)
                || !KnownSamThreatGeometry.TryBuildAvoidingPath(
                    egressControlPoints,
                    context.KnownSamThreats,
                    context.RouteKey,
                    context.ManeuverClearanceFeet,
                    out var safeEgress))
            {
                return new AirRouteGeometry(
                    Array.Empty<Vector3>(),
                    Array.Empty<Vector3>(),
                    false);
            }

            return new AirRouteGeometry(
                safeIngress.Skip(1).Take(Math.Max(0, safeIngress.Count - 2)).ToList(),
                safeEgress.Skip(1).Take(Math.Max(0, safeEgress.Count - 2)).ToList());
        }

        private static Vector3? CreateOffsetMidpoint(
            Vector3 start,
            Vector3 end,
            float desiredOffsetFeet,
            float side)
        {
            var horizontal = new Vector2(end.x - start.x, end.z - start.z);
            var distance = horizontal.magnitude;
            if (distance <= 0.01f)
                return null;

            var direction = horizontal / distance;
            var perpendicular = new Vector2(-direction.y, direction.x) * side;
            var offset = Math.Min(
                Math.Max(0f, desiredOffsetFeet),
                distance * MaximumOffsetLegFraction);
            var midpoint = (start + end) * 0.5f;
            midpoint.x += perpendicular.x * offset;
            midpoint.z += perpendicular.y * offset;
            return midpoint;
        }

        private static float SelectSide(Guid routeKey)
        {
            var parity = 0;
            foreach (var value in routeKey.ToByteArray())
                parity ^= value;
            return (parity & 1) == 0 ? 1f : -1f;
        }
    }
}
