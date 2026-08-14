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
        private const float OcaAltitudeFeet = 40000f;
        private const float AwacsAltitudeFeet = 35000f;
        private const float TankerAltitudeFeet = 25000f;
        private const float MaximumSupportStationHostileInterference = 0.10f;
        private const float MeaningfulOcaPresence = 0.10f;
        private const float MeaningfulBarcapPressure = 0.10f;
        private const float MeaningfulDeadEscortThreatPower = 0.10f;
        private const float DeadSelfDefenseCombatPowerCredit = 0.25f;
        private const float DeadAirActivityPowerScale = 2f;
        private const float DefaultFighterEscortScreenDistanceKm = 40f;
        private const float DefaultSupportStationTrackHalfLengthTiles = 0.5f;
        private const float FuelPlanningMarginSeconds = 60f;
        private const float BarcapPreferredLaunchRangeFraction = 0.78f;
        private const int MaximumBarcapRouteChoices = 32;
        public const float DeadLocalPassRadiusNauticalMiles = 5f;
        private const float DeadStandoffDistanceKm = 40f;
        private static readonly TimeSpan BarcapHandoffOverlap = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DeadAttackWindow = TimeSpan.FromMinutes(15);

        private readonly GameManager gameManager;
        private readonly ProjectedAirEffectService projectedEffects;
        private readonly AirMissionPriorityService priorityService;
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes;
        private readonly IReadOnlyDictionary<Guid, RadarAirDefenseComponentDefinition>
            radarDefinitions;
        private readonly AirLoadoutPlanner loadoutPlanner;
        private readonly DeadLoadoutPlanner deadLoadoutPlanner;
        private readonly IReadOnlyDictionary<Guid, AirDefenseComponentDefinition>
            airDefenseComponentDefinitions;
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
            airDefenseComponentDefinitions = module.SamComponentDefinitions
                .ToDictionary(definition => definition.SamComponentDefinitionId);
            Func<Alliance, IReadOnlyCollection<Guid>> allowedOrdnanceResolver =
                alliance =>
                    gameManager.OrdnanceAllowances.TryGetValue(alliance, out var allowed)
                        ? allowed
                        : Array.Empty<Guid>();
            loadoutPlanner = new AirLoadoutPlanner(
                module,
                allowedOrdnanceResolver);
            deadLoadoutPlanner = new DeadLoadoutPlanner(
                module,
                allowedOrdnanceResolver);
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

            if (request.RequestType
                == AirMissionRequestType.DestructionOfEnemyAirDefenses)
            {
                return TryBuildDeadPackage(
                    commander,
                    request,
                    currentTime,
                    out package,
                    out reason);
            }

            var planningStart = currentTime + AirPackage.PreparationDelay;
            DateTime effectStart;
            IReadOnlyList<Vector3Int> uncoveredBarrierTiles = null;
            var isSpatialBarcapGap = false;
            var barcapAircraftDeficit = 1;
            if (request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained)
            {
                var hasGap = request.RequestType
                             == AirMissionRequestType.BarrierCombatAirPatrol
                             && request.BarcapBarrier?.BarrierTileIds?.Count > 0
                    ? projectedEffects.TryFindFirstBarcapTaskingGap(
                        commander,
                        request,
                        planningStart,
                        out effectStart,
                        out uncoveredBarrierTiles,
                        out isSpatialBarcapGap,
                        out barcapAircraftDeficit)
                    : projectedEffects.TryFindFirstCoverageGap(
                        commander,
                        request,
                        planningStart,
                        out effectStart,
                        out _);
                if (!hasGap)
                {
                    reason = request.RequestType
                             == AirMissionRequestType.BarrierCombatAirPatrol
                             && request.BarcapBarrier?.BarrierTileIds?.Count > 0
                        ? "Desired BARCAP coverage and defensive capacity are already projected."
                        : "Desired combat coverage is already projected.";
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
                    uncoveredBarrierTiles,
                    squadronCandidates,
                    commander.Doctrine);
                if (choices.Count == 0)
                {
                    reason = "No ready air-combat aircraft can cover the remaining barrier.";
                    return AirPackageBuildOutcome.Deferred;
                }

                var lastRouteFailure = string.Empty;
                foreach (var choice in choices)
                {
                    var closesCurrentSpatialGap = isSpatialBarcapGap
                                                  && uncoveredBarrierTiles.All(
                                                      choice.Covered.Contains);
                    var isLastSpatialGap = closesCurrentSpatialGap
                                           && !projectedEffects
                                               .HasOtherSpatialBarcapCoverageGap(
                                                   commander,
                                                   request.MissionRequestId,
                                                   planningStart);
                    var desiredStationAircraft = isLastSpatialGap
                        ? Math.Max(
                            1,
                            commander.Doctrine.PreferredBarcapStationAircraft)
                        : Math.Max(1, barcapAircraftDeficit);
                    var selectedAircraftCount = Math.Min(
                        desiredStationAircraft,
                        choice.Candidate.AvailableAircraft.Count);
                    var selected = new List<SelectedCombatAircraft>
                    {
                        new SelectedCombatAircraft(
                            choice.Candidate.Squadron,
                            choice.Candidate.AircraftType,
                            choice.Candidate.AvailableAircraft
                                .Take(selectedAircraftCount)
                                .ToList(),
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
                        StationCenterFeet = choice.Racetrack.CenterFeet,
                        StationHeadingDegrees = choice.Racetrack.HeadingDegrees,
                        StationTrackHalfLengthKm = choice.TrackHalfLengthKm,
                        PlannedResponseRadiusKm = choice.RadiusKm,
                        PlannedMinimumInterceptSlackKm =
                            choice.MinimumInterceptSlackKm,
                        PlannedPreferredLaunchRangeKm =
                            choice.PreferredLaunchRangeKm,
                        RepresentativeThreatSpeedKnots =
                            request.BarcapBarrier.RepresentativeThreatSpeedKnots,
                        PlannedAircraftCount = selectedAircraftCount,
                        PreferredAircraftCount = Math.Max(
                            1,
                            commander.Doctrine.PreferredBarcapStationAircraft),
                        PlannedKnownSamSiteIds = GetKnownSamThreats(request.Alliance)
                            .Select(threat => threat.SiteId)
                            .Where(id => id != Guid.Empty)
                            .Distinct()
                            .OrderBy(id => id)
                            .ToList(),
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
                            null,
                            coverage,
                            out lastRouteFailure))
                    {
                        continue;
                    }

                    package = candidatePackage;
                    reason = $"Proposed {selectedAircraftCount} aircraft covering "
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

        private AirPackageBuildOutcome TryBuildDeadPackage(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            DateTime currentTime,
            out AirPackage package,
            out string reason)
        {
            package = null;
            reason = string.Empty;
            if (request.DeadPlan == null
                || request.DeadPlan.TargetSiteId == Guid.Empty)
            {
                reason = "The DEAD request has no persistent SAM-site target.";
                return AirPackageBuildOutcome.Deferred;
            }

            var report = gameManager.intelligenceSystem
                ?.GetPicture(commander.Alliance)
                ?.HostileAirDefenseSites
                ?.FirstOrDefault(candidate => candidate != null
                                              && candidate.SiteId
                                              == request.DeadPlan.TargetSiteId);
            if (report == null
                || report.InformationQuality <= 0f
                || report.IsDisabled
                || report.IsDestroyed)
            {
                reason = "The assigned SAM site is no longer a valid known hostile target.";
                return AirPackageBuildOutcome.AlreadySatisfied;
            }

            var liveComponents = (report.Components
                                  ?? new List<AirDefenseComponentIntelligenceReport>())
                .Where(component => component != null && !component.IsDamaged)
                .OrderBy(component => component.ComponentId)
                .ToList();
            var minimumEffectComponentCount = liveComponents.Count(component =>
                airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                && definition is RadarAirDefenseComponentDefinition
                {
                    ProvidesWeaponQualityTrack: true
                });
            var hasFunctionalLauncher = liveComponents.Any(component =>
                airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                && definition is LauncherAirDefenseComponentDefinition);
            if (minimumEffectComponentCount == 0 || !hasFunctionalLauncher)
            {
                reason = "The assigned SAM site no longer has a functional shooter chain.";
                return AirPackageBuildOutcome.AlreadySatisfied;
            }

            request.DeadPlan.TargetComponentIds = liveComponents
                .Select(component => component.ComponentId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            request.MissionArea.CenterTileId = report.TileId;

            var hostileEscortThreatPower = AssessDeadEscortThreatPower(
                commander,
                request,
                report.TileId);
            var seekEscort = hostileEscortThreatPower
                             >= MeaningfulDeadEscortThreatPower;
            if (!TrySelectDeadAttackAircraft(
                    commander.Alliance,
                    liveComponents,
                    report.TileId,
                    request,
                    requireSelfDefense: !seekEscort,
                    out var attackSelection,
                    out reason))
            {
                return AirPackageBuildOutcome.Deferred;
            }

            var selected = attackSelection.Flights;
            var deadSelfDefenseCombatPower =
                CalculateDeadSelfDefenseCombatPower(selected);
            var requiredEscortPower = seekEscort
                ? Math.Max(
                    0f,
                    hostileEscortThreatPower
                    * commander.Doctrine.DesiredAirCombatAdvantage
                    - deadSelfDefenseCombatPower
                    * DeadSelfDefenseCombatPowerCredit)
                : 0f;
            var escortSelection = requiredEscortPower
                                  >= MeaningfulDeadEscortThreatPower
                ? SelectFighterEscortAircraft(
                    commander.Alliance,
                    report.TileId,
                    request.MissionArea.RadiusKm,
                    selected,
                    requiredEscortPower)
                : new FighterEscortSelection();
            var escortShortfall = Math.Max(
                0f,
                requiredEscortPower - escortSelection.CombatPower);
            var hasAdequateEscort = escortSelection.Flights.Count > 0
                                    && escortShortfall
                                    < MeaningfulDeadEscortThreatPower;
            if (seekEscort && !hasAdequateEscort)
            {
                if (!TrySelectDeadAttackAircraft(
                        commander.Alliance,
                        liveComponents,
                        report.TileId,
                        request,
                        requireSelfDefense: true,
                        out var selfDefendingSelection,
                        out reason))
                {
                    return AirPackageBuildOutcome.Deferred;
                }

                var fallbackSelfDefensePower =
                    CalculateDeadSelfDefenseCombatPower(
                        selfDefendingSelection.Flights);
                var unescortedThreatLimit = fallbackSelfDefensePower
                                            * Mathf.Lerp(
                                                0.5f,
                                                1f,
                                                commander.Doctrine.RiskTolerance);
                if (hostileEscortThreatPower
                    > Math.Max(
                        MeaningfulDeadEscortThreatPower,
                        unescortedThreatLimit))
                {
                    reason = $"DEAD requires {requiredEscortPower:0.00} escort power "
                             + $"against {hostileEscortThreatPower:0.00} hostile power, "
                             + $"but only {escortSelection.CombatPower:0.00} is available "
                             + $"and organic protection tolerates "
                             + $"{unescortedThreatLimit:0.00}.";
                    return AirPackageBuildOutcome.Deferred;
                }

                // An inadequate partial escort is not paired with a ground-first
                // attack loadout. Fall back to the independently feasible organic
                // protection plan and release those fighters for other missions.
                attackSelection = selfDefendingSelection;
                selected = attackSelection.Flights;
                escortSelection = new FighterEscortSelection();
            }

            var planningStart = currentTime + AirPackage.PreparationDelay;
            var effectStart = planningStart > request.EffectStart
                ? planningStart
                : request.EffectStart;
            package = CreatePackage(request, currentTime);
            AddSelectedFlights(package, request, selected);
            var protectedFlightIds = package.Flights
                .Select(flight => flight.FlightId)
                .ToList();
            AddFighterEscortFlights(
                package,
                request,
                escortSelection.Flights,
                protectedFlightIds);
            if (!TryMaterializeRoutes(
                    commander,
                    package,
                    request,
                    planningStart,
                    effectStart,
                    effectStart + DeadAttackWindow,
                    report.TileId,
                    null,
                    out reason))
            {
                package = null;
                return AirPackageBuildOutcome.Deferred;
            }

            var aircraftCount = selected.Sum(candidate => candidate.Aircraft.Count);
            var escortAircraftCount = escortSelection.Flights.Sum(candidate =>
                candidate.Aircraft.Count);
            package.Rationale = request.Rationale
                                + $" Selected {package.Flights.Count} flight(s) / "
                                + $"{aircraftCount} DEAD aircraft with "
                                + $"{attackSelection.MinimumEffectStores} minimum-effect stores, "
                                + $"{attackSelection.CleanupStores} cleanup stores, and "
                                + $"{attackSelection.SelfDefenseShots} self-defense shots; "
                                + $"assessed hostile fighter power {hostileEscortThreatPower:0.00} "
                                + $"and assigned {escortAircraftCount} escort aircraft "
                                + $"providing {escortSelection.CombatPower:0.00} power.";
            reason = $"Proposed {aircraftCount} DEAD aircraft "
                     + $"against {minimumEffectComponentCount} required component(s) "
                     + $"with {attackSelection.MinimumEffectStores} minimum-effect and "
                     + $"{attackSelection.CleanupStores} cleanup stores"
                     + (escortAircraftCount > 0
                         ? $", protected by {escortAircraftCount} fighter escort(s)."
                         : ", using organic self-protection.");
            return AirPackageBuildOutcome.Built;
        }

        private IReadOnlyList<BarcapSelectionChoice>
            GetBarcapAircraftAndCoverageChoices(
            AirMissionRequest request,
            IReadOnlyList<Vector3Int> uncoveredBarrierTiles,
            IReadOnlyList<CombatSquadronCandidate> candidates,
            AllianceAirDoctrine doctrine)
        {
            var tileDistanceKm = CampaignMapCoordinates
                .TileCenterSpacingKilometers;
            if (uncoveredBarrierTiles == null
                || uncoveredBarrierTiles.Count == 0
                || request.BarcapBarrier?.BarrierTileIds == null
                || request.BarcapBarrier.BarrierTileIds.Count < 1)
                return Array.Empty<BarcapSelectionChoice>();

            var barrier = request.BarcapBarrier.BarrierTileIds;
            var gapCenterTile = SelectLargestBarrierGapCenter(
                barrier,
                uncoveredBarrierTiles);
            var knownSamThreats = GetKnownSamThreats(request.Alliance);
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
                    var trackHalfLengthKm = BarcapInterceptGeometry
                        .CalculateStationTrackHalfLengthKm(
                            candidate.AircraftType,
                            doctrine.BarcapTrackLegMinutes);
                    var stationAltitudeFeet = Math.Min(
                        doctrine.BarcapStationAltitudeFeet,
                        candidate.AircraftType.ServiceCeilingFeet);
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
                                        tile) * tileDistanceKm;
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
                                            tileDistanceKm,
                                            releaseStandoffKm,
                                            request.BarcapBarrier
                                                .RepresentativeThreatSpeedKnots,
                                            stationAltitudeFeet),
                                        doctrine.BarcapCommandDelaySeconds);
                            });
                    var maximumResponseRadiusKm = responseRadiusByTile
                        .Values
                        .DefaultIfEmpty(0f)
                        .Max();
                    var defensiveStationPositions = BarcapInterceptGeometry
                        .GetDefensiveStationPositions(
                            gapCenterTile,
                            request.BarcapBarrier.ThreatReferenceTileId,
                            tileDistanceKm,
                            stationAltitudeFeet,
                            maximumResponseRadiusKm);
                    var maneuverClearanceFeet = AirspaceGeometry
                        .ConservativeSamManeuverClearanceFeet(
                            candidate.AircraftType);
                    return defensiveStationPositions
                        .Select(candidateStationCenter =>
                        {
                            var stationHeading = BarcapInterceptGeometry
                                .GetStationHeadingDegrees(
                                    candidateStationCenter,
                                    request.BarcapBarrier.ThreatReferenceTileId,
                                    tileDistanceKm);
                            var racetrack = BarcapInterceptGeometry.BuildRacetrack(
                                candidateStationCenter,
                                stationHeading,
                                trackHalfLengthKm,
                                candidate.AircraftType);
                            if (!KnownSamThreatGeometry.IsPathSafe(
                                    racetrack.GetClosedLoopPoints(),
                                    knownSamThreats,
                                    maneuverClearanceFeet,
                                    out _))
                                return null;
                            var coverable = responseRadiusByTile
                                .Where(entry =>
                                    BarcapInterceptGeometry
                                        .CanReachOperationalBarrierFromStation(
                                        racetrack.LoopPointsFeet,
                                        entry.Key,
                                        request.BarcapBarrier
                                            .ThreatReferenceTileId,
                                        tileDistanceKm,
                                        releaseStandoffKm,
                                        entry.Value))
                                .Select(entry => entry.Key)
                                .ToList();
                            var covered = SelectContiguousBarrierRun(
                                barrier,
                                coverable,
                                gapCenterTile);
                            if (covered.Count == 0
                                || racetrack.LoopPointsFeet.Any(point =>
                                    !BarcapInterceptGeometry.IsOnDefendedSide(
                                        point,
                                        covered,
                                        request.BarcapBarrier.ThreatReferenceTileId,
                                        tileDistanceKm,
                                        releaseStandoffKm)))
                                return null;
                            var conservativeResponseRadiusKm = covered
                                .Select(tile => responseRadiusByTile[tile])
                                .DefaultIfEmpty(0f)
                                .Min();
                            var minimumInterceptSlackKm = covered
                                .Select(tile => responseRadiusByTile[tile]
                                                - BarcapInterceptGeometry
                                                    .GetWorstStationDistanceToOperationalBarrierKm(
                                                        racetrack.LoopPointsFeet,
                                                        tile,
                                                        request.BarcapBarrier
                                                            .ThreatReferenceTileId,
                                                        tileDistanceKm,
                                                        releaseStandoffKm))
                                .DefaultIfEmpty(float.NegativeInfinity)
                                .Min();
                            var desiredInterceptSlackKm = candidate.AircraftType
                                .CombatSpeedKnots
                                * 1.852f
                                * doctrine.BarcapDesiredInterceptMarginMinutes
                                / 60f;
                            return new BarcapSelectionChoice(
                                candidate,
                                conservativeResponseRadiusKm,
                                preferredLaunchRangeKm,
                                racetrack,
                                BarcapInterceptGeometry.GetDefensiveStationDepthKm(
                                    candidateStationCenter,
                                    gapCenterTile,
                                    request.BarcapBarrier.ThreatReferenceTileId,
                                    tileDistanceKm),
                                trackHalfLengthKm,
                                minimumInterceptSlackKm,
                                desiredInterceptSlackKm,
                                covered,
                                covered.Count(uncoveredBarrierTiles.Contains));
                        });
                })
                .Where(candidate => candidate != null)
                .Where(candidate => candidate.UncoveredCount > 0)
                .OrderByDescending(candidate => candidate.UncoveredCount)
                .ThenByDescending(candidate => Math.Min(
                    candidate.MinimumInterceptSlackKm,
                    candidate.DesiredInterceptSlackKm))
                .ThenByDescending(candidate => candidate.StationDepthKm)
                .ThenByDescending(candidate => candidate.RadiusKm)
                .ThenBy(candidate => candidate.Candidate.DistanceTiles)
                .ThenBy(candidate => candidate.Candidate.Squadron.SquadronId)
                .Take(MaximumBarcapRouteChoices)
                .ToList();
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
            float threatSpeedKnots,
            float stationAltitudeFeet)
        {
            var releasePoint = BarcapInterceptGeometry
                .GetOperationalBarrierPointsFeet(
                    new[] { protectedTile },
                    threatTile,
                    tileDistanceKm,
                    weaponReleaseStandoffKm)
                .FirstOrDefault();
            var threatCenter = AirspaceGeometry.TileCenterFeet(
                threatTile);
            var protectedCenter = AirspaceGeometry.TileCenterFeet(
                protectedTile);
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
                if (!gameManager.airDefenseSiteSystem.TryGetPositionFeet(
                        site,
                        out var sitePosition))
                    continue;
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
                         < stationAltitudeFeet)
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
            IReadOnlyCollection<CampaignAircraft> aircraft,
            AirFlightRole role = AirFlightRole.PrimaryMission)
        {
            var flight = new AirFlight
            {
                SquadronId = squadron.SquadronId,
                MissionType = request.RequestType,
                Role = role,
                AuthorizedSurfaceThreatSiteId = role
                                                == AirFlightRole.PrimaryMission
                    ? request.DeadPlan?.TargetSiteId ?? Guid.Empty
                    : Guid.Empty,
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

        // Package-owned fighter escort is mission-neutral. Mission builders
        // decide when it is needed and which primary flights it protects.
        private static void AddFighterEscortFlights(
            AirPackage package,
            AirMissionRequest request,
            IEnumerable<SelectedCombatAircraft> selectedEscorts,
            IReadOnlyCollection<Guid> protectedFlightIds)
        {
            foreach (var selected in selectedEscorts)
            {
                var flight = CreateFlight(
                    request,
                    selected.Squadron,
                    selected.Aircraft,
                    AirFlightRole.FighterEscort);
                // An attached escort participates in the package rendezvous.
                // Existing package integrity therefore treats it as required;
                // Low-risk packages that cannot assign any escort fall back to
                // organic self-protection instead of attaching this dependency.
                flight.IsRequired = true;
                flight.ProtectedFlightIds.AddRange(
                    protectedFlightIds.Where(id => id != Guid.Empty));
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

        private bool TrySelectDeadAttackAircraft(
            Alliance alliance,
            IReadOnlyList<AirDefenseComponentIntelligenceReport> liveComponents,
            Vector3Int targetTileId,
            AirMissionRequest request,
            bool requireSelfDefense,
            out DeadAttackSelection selection,
            out string reason)
        {
            selection = null;
            reason = string.Empty;
            var minimumEffectComponentCount = liveComponents.Count(component =>
                airDefenseComponentDefinitions.TryGetValue(
                    component.SamComponentDefinitionId,
                    out var definition)
                && definition is RadarAirDefenseComponentDefinition
                {
                    ProvidesWeaponQualityTrack: true
                });
            var candidates = GetFriendlySquadrons(alliance)
                .Where(squadron => aircraftTypes.ContainsKey(
                    squadron.AircraftTypeDefinitionId))
                .Select(squadron =>
                {
                    var aircraftType = aircraftTypes[
                        squadron.AircraftTypeDefinitionId];
                    return deadLoadoutPlanner.TryPlan(
                        aircraftType,
                        alliance,
                        liveComponents,
                        out var loadout,
                        out _,
                        requireSelfDefense)
                        ? new DeadSquadronCandidate(
                            squadron,
                            aircraftType,
                            GetAvailableAircraft(squadron),
                            loadout,
                            GetAirportPhysicalDistanceKm(
                                squadron,
                                targetTileId))
                        : null;
                })
                .Where(candidate => candidate != null
                                    && candidate.AvailableAircraft.Count > 0
                                    && (candidate.AircraftType.RangeKm <= 0f
                                        || candidate.DistanceKm * 2f
                                        + request.MissionArea.RadiusKm * 2f
                                        <= candidate.AircraftType.RangeKm))
                .OrderBy(candidate => candidate.DistanceKm)
                .ThenBy(candidate => candidate.Squadron.AirportBuildingId)
                .ThenBy(candidate => candidate.Squadron.SquadronId)
                .ToList();
            if (candidates.Count == 0)
            {
                reason = requireSelfDefense
                    ? "No ready aircraft can carry both DEAD effect and self-defense."
                    : "No ready aircraft can carry the required DEAD effect.";
                return false;
            }

            selection = new DeadAttackSelection();
            var remainingPrimaryAttacks = minimumEffectComponentCount;
            foreach (var candidate in candidates)
            {
                if (remainingPrimaryAttacks <= 0)
                    break;

                var attacksPerAircraft = Math.Max(
                    1,
                    candidate.LoadoutPlan.MinimumEffectStoreCount);
                var requiredAircraft = (int)Math.Ceiling(
                    remainingPrimaryAttacks / (double)attacksPerAircraft);
                var aircraft = candidate.AvailableAircraft
                    .Take(Math.Min(requiredAircraft, candidate.AvailableAircraft.Count))
                    .ToList();
                if (aircraft.Count == 0)
                    continue;

                selection.Flights.Add(new SelectedCombatAircraft(
                    candidate.Squadron,
                    candidate.AircraftType,
                    aircraft,
                    CloneLoadout(candidate.LoadoutPlan.Loadout)));
                selection.MinimumEffectStores += aircraft.Count
                                                 * candidate.LoadoutPlan
                                                     .MinimumEffectStoreCount;
                selection.CleanupStores += aircraft.Count
                                           * candidate.LoadoutPlan.CleanupStoreCount;
                selection.SelfDefenseShots += aircraft.Count
                                              * candidate.LoadoutPlan
                                                  .SelfDefenseShotCount;
                remainingPrimaryAttacks -= aircraft.Count * attacksPerAircraft;
            }

            if (remainingPrimaryAttacks <= 0)
                return true;

            reason = "Ready aircraft cannot cover every required DEAD component.";
            selection = null;
            return false;
        }

        // Selects the smallest available fighter allocation for any escorted
        // package. Mission-specific planners provide the operating area,
        // protected aircraft, and required combat power.
        private FighterEscortSelection SelectFighterEscortAircraft(
            Alliance alliance,
            Vector3Int operatingAreaTileId,
            float operatingAreaRadiusKm,
            IReadOnlyCollection<SelectedCombatAircraft> protectedAircraft,
            float requiredCombatPower)
        {
            var selectedAircraftIds = protectedAircraft
                .SelectMany(selected => selected.Aircraft)
                .Select(aircraft => aircraft.AircraftId)
                .ToHashSet();
            var candidates = GetFriendlySquadrons(alliance)
                .Where(squadron => aircraftTypes.TryGetValue(
                    squadron.AircraftTypeDefinitionId,
                    out var aircraftType)
                    && priorityService.CanPerformAirCombat(aircraftType))
                .Select(squadron =>
                {
                    var aircraftType = aircraftTypes[
                        squadron.AircraftTypeDefinitionId];
                    return loadoutPlanner.TryPlanAirCombatLoadout(
                        aircraftType,
                        alliance,
                        out var loadout,
                        out _)
                        ? new CombatSquadronCandidate(
                            squadron,
                            aircraftType,
                            GetAvailableAircraft(squadron)
                                .Where(aircraft => !selectedAircraftIds.Contains(
                                    aircraft.AircraftId))
                                .ToList(),
                            loadout,
                            GetAirportDistance(squadron, operatingAreaTileId))
                        : null;
                })
                .Where(candidate => candidate != null
                                    && candidate.AvailableAircraft.Count > 0
                                    && (candidate.AircraftType.RangeKm <= 0f
                                        || GetAirportPhysicalDistanceKm(
                                               candidate.Squadron,
                                               operatingAreaTileId) * 2f
                                           + Math.Max(0f, operatingAreaRadiusKm) * 2f
                                           <= candidate.AircraftType.RangeKm))
                // Highest combat power first guarantees that the greedy pass
                // reaches the requirement with the fewest available aircraft.
                // Distance remains the preference among equally capable types.
                .OrderByDescending(candidate => Math.Max(
                    0.01f,
                    candidate.AircraftType.AirInterferenceCapability))
                .ThenBy(candidate => candidate.DistanceTiles)
                .ThenBy(candidate => candidate.Squadron.AirportBuildingId)
                .ThenBy(candidate => candidate.Squadron.SquadronId)
                .ToList();

            var result = new FighterEscortSelection();
            foreach (var candidate in candidates)
            {
                if (result.CombatPower >= requiredCombatPower)
                    break;

                var powerPerAircraft = Math.Max(
                    0.01f,
                    candidate.AircraftType.AirInterferenceCapability);
                var requiredAircraft = (int)Math.Ceiling(
                    (requiredCombatPower - result.CombatPower)
                    / powerPerAircraft);
                var aircraft = candidate.AvailableAircraft
                    .Take(Math.Min(requiredAircraft, candidate.AvailableAircraft.Count))
                    .ToList();
                if (aircraft.Count == 0)
                    continue;

                result.Flights.Add(new SelectedCombatAircraft(
                    candidate.Squadron,
                    candidate.AircraftType,
                    aircraft,
                    CloneLoadout(candidate.Loadout)));
                result.CombatPower += aircraft.Count * powerPerAircraft;
            }

            return result;
        }

        private float CalculateDeadSelfDefenseCombatPower(
            IEnumerable<SelectedCombatAircraft> selectedAircraft)
        {
            return selectedAircraft.Sum(candidate =>
                CalculateOrganicDeadSelfDefenseCombatPower(
                    candidate.Aircraft.Count,
                    candidate.AircraftType.AirInterferenceCapability,
                    loadoutPlanner.CountMissionUsefulAirCombatShots(
                        candidate.Loadout)));
        }

        internal static float CalculateOrganicDeadSelfDefenseCombatPower(
            int aircraftCount,
            float combatPowerPerAircraft,
            int missionUsefulShotsPerAircraft)
        {
            var selfDefenseReadiness = Mathf.Clamp01(
                Math.Max(0, missionUsefulShotsPerAircraft)
                / (float)AirLoadoutPlanner.MinimumAirCombatShots);
            return Math.Max(0, aircraftCount)
                   * Math.Max(0f, combatPowerPerAircraft)
                   * selfDefenseReadiness;
        }

        private float AssessDeadEscortThreatPower(
            AllianceAirTaskingCommander commander,
            AirMissionRequest request,
            Vector3Int targetTileId)
        {
            var corridor = request.DeadPlan?.SupportedCorridor;
            var originTileId = corridor == null
                ? targetTileId
                : AirspaceGeometry.TileCoordinateFromPositionFeet(
                    corridor.OriginPositionFeet);
            var recoveryTileId = corridor == null
                ? originTileId
                : AirspaceGeometry.TileCoordinateFromPositionFeet(
                    corridor.RecoveryPositionFeet);
            var routeTiles = AirspaceGeometry.TilesAlongLine(
                    originTileId,
                    targetTileId)
                .Concat(AirspaceGeometry.TilesAlongLine(
                    targetTileId,
                    recoveryTileId))
                .SelectMany(tile => AirspaceGeometry.NeighborTiles(tile)
                    .Append(tile))
                .Distinct();

            return routeTiles
                .Select(tile => commander.TryGetAirControlAssessment(
                        tile,
                        out var assessment)
                    ? assessment
                    : null)
                .Where(assessment => assessment != null)
                .Select(assessment => Math.Max(
                    assessment.HostileCombatPower,
                    assessment.HostileAirActivity
                    * DeadAirActivityPowerScale))
                .DefaultIfEmpty(0f)
                .Max();
        }

        private static List<AircraftLoadoutItem> CloneLoadout(
            IEnumerable<AircraftLoadoutItem> loadout)
        {
            return loadout.Select(item => new AircraftLoadoutItem(
                    item.AircraftLoadoutStationDefinitionId,
                    item.AircraftCarriageConfigurationDefinitionId,
                    item.OrdnanceTypeDefinitionId,
                    item.Count))
                .ToList();
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

        private float GetAirportPhysicalDistanceKm(
            Squadron squadron,
            Vector3Int targetTile)
        {
            if (!gameManager.buildingSystem.TryGetBuilding(
                    squadron.AirportBuildingId,
                    out var building))
                return float.PositiveInfinity;

            var origin = building.PositionFeet;
            var destination = AirspaceGeometry.TileCenterFeet(
                targetTile);
            return Vector2.Distance(
                       new Vector2(origin.x, origin.z),
                       new Vector2(destination.x, destination.z))
                   / AirspaceGeometry.FeetPerKilometer;
        }

        private IReadOnlyList<KnownSamThreatEnvelope> GetKnownSamThreats(
            Alliance alliance)
        {
            if (knownSamThreatCache.TryGetValue(alliance, out var cached))
                return cached;

            var threats = knownSamThreatAssessment.BuildKnownThreats(
                gameManager.intelligenceSystem?.GetPicture(alliance));
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
                    airport.PositionFeet));
            }

            var desiredMissionAltitude = GetMissionAltitudeFeet(
                request.RequestType,
                barcapCoverage);
            var missionAltitude = plans.Min(plan =>
                Math.Min(desiredMissionAltitude, plan.AircraftType.ServiceCeilingFeet));
            var missionCenter = request.RequestType
                                == AirMissionRequestType.BarrierCombatAirPatrol
                                && barcapCoverage != null
                ? barcapCoverage.StationCenterFeet
                : AirspaceGeometry.TileCenterFeet(
                    missionCenterOverride ?? request.MissionArea.CenterTileId,
                    missionAltitude);
            missionCenter.y = missionAltitude;
            var tileDistanceFeet = CampaignMapCoordinates.TileCenterSpacingFeet;
            var allKnownSamThreats = GetKnownSamThreats(package.Alliance);
            IReadOnlyCollection<KnownSamThreatEnvelope> targetSamThreats =
                request.DeadPlan == null
                ? Array.Empty<KnownSamThreatEnvelope>()
                : allKnownSamThreats
                    .Where(threat => threat.SiteId
                                     == request.DeadPlan.TargetSiteId)
                    .ToList();
            var coordinatedCombatPackage =
                request.RequestType == AirMissionRequestType.BarrierCombatAirPatrol
                || request.RequestType
                == AirMissionRequestType.OffensiveCounterAirSweep
                || request.RequestType
                == AirMissionRequestType.DestructionOfEnemyAirDefenses
                || plans.Any(plan => plan.Flight.IsFighterEscort);
            var hasRendezvous = coordinatedCombatPackage && plans.Count > 1;
            var rendezvousPosition = Vector3.zero;
            var coordinatedSpeed = plans.Min(plan => Math.Max(1f, plan.AircraftType.CruiseSpeedKnots));
            if (hasRendezvous)
            {
                var baseCentroid = plans.Aggregate(
                    Vector3.zero,
                    (sum, plan) => sum + plan.BasePositionFeet) / plans.Count;
                rendezvousPosition = (baseCentroid + missionCenter) * 0.5f;
                rendezvousPosition.y = missionAltitude;
                if (plans.Any(plan => plan.Flight.IsFighterEscort))
                {
                    var escortClearance = plans
                        .Where(plan => plan.Flight.IsFighterEscort)
                        .Select(plan => AirspaceGeometry
                            .ConservativeSamManeuverClearanceFeet(
                                plan.AircraftType))
                        .DefaultIfEmpty(0f)
                        .Max();
                    if (targetSamThreats.Any(threat =>
                            threat.Contains(
                                rendezvousPosition,
                                escortClearance)))
                    {
                        var friendlyDirection = baseCentroid - missionCenter;
                        friendlyDirection.y = 0f;
                        if (friendlyDirection.sqrMagnitude < 1f)
                            friendlyDirection = Vector3.back;
                        friendlyDirection.Normalize();
                        var targetThreatRadius =
                            GetMaximumHorizontalThreatRadiusFeet(
                                targetSamThreats,
                                missionAltitude,
                                escortClearance);
                        rendezvousPosition = missionCenter
                                             + friendlyDirection
                                             * (targetThreatRadius
                                                + AirspaceGeometry.FeetPerKilometer);
                        rendezvousPosition.y = missionAltitude;
                    }
                }
            }

            foreach (var plan in plans)
            {
                var maneuverClearanceFeet = AirspaceGeometry
                    .ConservativeSamManeuverClearanceFeet(
                        plan.AircraftType);
                var missionOrigin = hasRendezvous ? rendezvousPosition : plan.BasePositionFeet;
                // Only the DEAD attack element may accept exposure to the
                // assigned site. Its fighter escort remains outside that
                // envelope and treats every known SAM as a routing constraint.
                var knownSamThreats = plan.Flight.IsDeadAttackFlight
                                      && request.DeadPlan != null
                    ? allKnownSamThreats
                        .Where(threat => threat.SiteId
                                         != request.DeadPlan.TargetSiteId)
                        .ToList()
                    : allKnownSamThreats;
                SetMissionGeometry(
                    plan,
                    commander,
                    request,
                    missionOrigin,
                    missionCenter,
                    tileDistanceFeet,
                    barcapCoverage,
                    targetSamThreats,
                    maneuverClearanceFeet);
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

            var plannedEffectEnd = request.RequestType
                                   == AirMissionRequestType
                                       .DestructionOfEnemyAirDefenses
                ? plannedEffectStart + DeadAttackWindow
                : proposedEffectEnd;
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
                        plan.BarcapRacetrack == null
                            ? AirspaceGeometry.TravelSeconds(
                                plan.MissionEntryPosition,
                                plan.MissionExitPosition,
                                plan.AircraftType.CruiseSpeedKnots,
                                plan.AircraftType.ClimbRateFeetPerMinute,
                                plan.AircraftType.DescentRateFeetPerMinute)
                            : AirspaceGeometry.HorizontalTravelSeconds(
                                plan.BarcapRacetrack.CircuitLengthFeet,
                                plan.AircraftType.CruiseSpeedKnots)));
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
            else if (request.RequestType
                     == AirMissionRequestType.BarrierCombatAirPatrol
                     && plan.BarcapRacetrack != null)
            {
                missionPath = plan.BarcapRacetrack.GetClosedLoopPoints();
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
            BarcapStationCoverage barcapCoverage,
            IReadOnlyCollection<KnownSamThreatEnvelope> targetSamThreats,
            float maneuverClearanceFeet)
        {
            if (plan.Flight.IsFighterEscort)
            {
                SetFighterEscortMissionGeometry(
                    plan,
                    missionOrigin,
                    missionCenter,
                    tileDistanceFeet,
                    targetSamThreats,
                    maneuverClearanceFeet);
                return;
            }

            if (request.RequestType == AirMissionRequestType.OffensiveCounterAirSweep)
            {
                var originTileId = AirspaceGeometry.TileCoordinateFromPositionFeet(
                    missionOrigin);
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
                    missionCenter.y);
                plan.MissionPushPosition = AirspaceGeometry.TileCenterFeet(
                    pushTileId,
                    missionCenter.y);
                plan.MissionExitPosition = plan.MissionEntryPosition;
                return;
            }

            if (request.RequestType == AirMissionRequestType.BarrierCombatAirPatrol)
            {
                var stationCenter = missionCenter;
                var heading = barcapCoverage?.StationHeadingDegrees ?? 0f;
                plan.BarcapRacetrack = BarcapInterceptGeometry.BuildRacetrack(
                    stationCenter,
                    heading,
                    Math.Max(
                        0f,
                        barcapCoverage?.StationTrackHalfLengthKm ?? 0f),
                    plan.AircraftType);
                var loop = plan.BarcapRacetrack.LoopPointsFeet;
                plan.MissionEntryPosition = loop[0];
                plan.MissionPushPosition = loop[Math.Min(1, loop.Count - 1)];
                plan.MissionExitPosition = loop[loop.Count - 1];
                return;
            }

            if (request.RequestType
                == AirMissionRequestType.DestructionOfEnemyAirDefenses)
            {
                var attackDirection = missionCenter - missionOrigin;
                attackDirection.y = 0f;
                if (attackDirection.sqrMagnitude < 1f)
                    attackDirection = Vector3.forward;
                attackDirection.Normalize();
                var standoffOffset = attackDirection * Math.Max(
                    AirspaceGeometry.FeetPerKilometer,
                    DeadStandoffDistanceKm
                    * AirspaceGeometry.FeetPerKilometer);
                var localPassOffset = attackDirection
                                      * DeadLocalPassRadiusNauticalMiles
                                      * AirspaceGeometry.FeetPerNauticalMile;
                plan.MissionEntryPosition = missionCenter - standoffOffset;
                plan.MissionPushPosition = missionCenter - localPassOffset;
                plan.MissionExitPosition = missionCenter + localPassOffset;
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
                              * DefaultSupportStationTrackHalfLengthTiles;
            plan.MissionEntryPosition = missionCenter - trackOffset;
            plan.MissionPushPosition = missionCenter + trackOffset;
            plan.MissionExitPosition = missionCenter + trackOffset;
        }

        private static void SetFighterEscortMissionGeometry(
            RoutePlan plan,
            Vector3 missionOrigin,
            Vector3 missionCenter,
            float tileDistanceFeet,
            IReadOnlyCollection<KnownSamThreatEnvelope> protectedObjectiveThreats,
            float maneuverClearanceFeet)
        {
            var missionDirection = missionCenter - missionOrigin;
            missionDirection.y = 0f;
            if (missionDirection.sqrMagnitude < 1f)
                missionDirection = Vector3.forward;
            missionDirection.Normalize();

            // This is the mission-neutral forward-screen default. A mission
            // may supply a protected-objective threat (DEAD does) to keep the
            // initial screen beyond that envelope.
            var protectedThreatRadius = GetMaximumHorizontalThreatRadiusFeet(
                protectedObjectiveThreats,
                missionCenter.y,
                maneuverClearanceFeet);
            var threatSafeStandoffFeet = protectedThreatRadius > 0f
                ? protectedThreatRadius + AirspaceGeometry.FeetPerKilometer
                : DefaultFighterEscortScreenDistanceKm
                  * AirspaceGeometry.FeetPerKilometer;
            var screenCenter = missionCenter
                               - missionDirection
                               * Math.Max(
                                   DefaultFighterEscortScreenDistanceKm
                                   * AirspaceGeometry.FeetPerKilometer,
                                   threatSafeStandoffFeet);
            var lateral = new Vector3(
                              -missionDirection.z,
                              0f,
                              missionDirection.x)
                          * Math.Min(
                              tileDistanceFeet * 0.5f,
                              10f * AirspaceGeometry.FeetPerNauticalMile);
            plan.MissionEntryPosition = screenCenter - lateral;
            plan.MissionPushPosition = screenCenter;
            plan.MissionExitPosition = screenCenter + lateral;
        }

        internal static float GetMaximumHorizontalThreatRadiusFeet(
            IEnumerable<KnownSamThreatEnvelope> threats,
            float altitudeFeet,
            float maneuverClearanceFeet)
        {
            return (threats ?? Array.Empty<KnownSamThreatEnvelope>())
                .Where(threat => threat != null)
                .Select(threat => threat.HorizontalRadiusFeetAtAltitude(
                    altitudeFeet,
                    maneuverClearanceFeet))
                .DefaultIfEmpty(0f)
                .Max();
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
            if (flight.IsFighterEscort)
            {
                var effectArea = new AirMissionArea(
                    AirspaceGeometry.TileCoordinateFromPositionFeet(
                        plan.MissionEntryPosition
                        + (plan.MissionExitPosition
                           - plan.MissionEntryPosition) * 0.5f),
                    request.MissionArea.RadiusKm);
                var stationEntry = NewWaypoint(
                    plan.MissionEntryPosition,
                    AirWaypointAction.StationEntry,
                    effectStart,
                    effectArea);
                var firstScreenEnd = effectStart + TimeSpan.FromSeconds(
                    AirspaceGeometry.TravelSeconds(
                        plan.MissionEntryPosition,
                        plan.MissionExitPosition,
                        plan.AircraftType.CruiseSpeedKnots,
                        plan.AircraftType.ClimbRateFeetPerMinute,
                        plan.AircraftType.DescentRateFeetPerMinute));
                route.Add(stationEntry);
                route.Add(NewWaypoint(
                    plan.MissionExitPosition,
                    AirWaypointAction.StationEndpoint,
                    firstScreenEnd,
                    hasRepeat: true,
                    repeatFromWaypointId: stationEntry.WaypointId,
                    repeatUntil: effectEnd));
                var screenCenter = plan.MissionEntryPosition
                                   + (plan.MissionExitPosition
                                      - plan.MissionEntryPosition) * 0.5f;
                var friendlyDirection = plan.BasePositionFeet - screenCenter;
                friendlyDirection.y = 0f;
                if (friendlyDirection.sqrMagnitude < 1f)
                    friendlyDirection = Vector3.back;
                friendlyDirection.Normalize();
                var intendedReleasePosition = plan.MissionExitPosition
                                              + friendlyDirection
                                              * CampaignMapCoordinates
                                                  .TileCenterSpacingFeet;
                var releaseTravelSeconds = AirspaceGeometry.TravelSeconds(
                    plan.MissionExitPosition,
                    intendedReleasePosition,
                    plan.AircraftType.CruiseSpeedKnots,
                    plan.AircraftType.ClimbRateFeetPerMinute,
                    plan.AircraftType.DescentRateFeetPerMinute);
                var availableReleaseSeconds = Math.Max(
                    0d,
                    (request.EffectEnd - effectEnd).TotalSeconds);
                var releaseFraction = releaseTravelSeconds <= 0d
                    ? 1f
                    : Mathf.Clamp01((float)(availableReleaseSeconds
                                             / releaseTravelSeconds));
                var releasePosition = Vector3.Lerp(
                    plan.MissionExitPosition,
                    intendedReleasePosition,
                    releaseFraction);
                var releaseTime = effectEnd + TimeSpan.FromSeconds(
                    Math.Min(releaseTravelSeconds, availableReleaseSeconds));
                route.Add(NewWaypoint(
                    releasePosition,
                    AirWaypointAction.MissionAction,
                    releaseTime,
                    effectArea));
                returnTime = releaseTime;
                returnPosition = releasePosition;
            }
            else if (request.RequestType == AirMissionRequestType.OffensiveCounterAirSweep)
            {
                var effectArea = new AirMissionArea(
                    request.MissionArea.CenterTileId,
                    request.MissionArea.RadiusKm);
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
            else if (request.RequestType
                     == AirMissionRequestType.DestructionOfEnemyAirDefenses)
            {
                var effectArea = new AirMissionArea(
                    request.MissionArea.CenterTileId,
                    request.MissionArea.RadiusKm);
                route.Add(NewWaypoint(
                    plan.MissionEntryPosition,
                    AirWaypointAction.MissionAction,
                    effectStart,
                    effectArea));
                var localPassStart = effectStart + TimeSpan.FromSeconds(
                    AirspaceGeometry.TravelSeconds(
                        plan.MissionEntryPosition,
                        plan.MissionPushPosition,
                        plan.AircraftType.CruiseSpeedKnots,
                        plan.AircraftType.ClimbRateFeetPerMinute,
                        plan.AircraftType.DescentRateFeetPerMinute));
                var stationEntry = NewWaypoint(
                    plan.MissionPushPosition,
                    AirWaypointAction.StationEntry,
                    localPassStart,
                    effectArea);
                var firstPassEnd = localPassStart + TimeSpan.FromSeconds(
                    AirspaceGeometry.TravelSeconds(
                        plan.MissionPushPosition,
                        plan.MissionExitPosition,
                        plan.AircraftType.CruiseSpeedKnots,
                        plan.AircraftType.ClimbRateFeetPerMinute,
                        plan.AircraftType.DescentRateFeetPerMinute));
                route.Add(stationEntry);
                route.Add(NewWaypoint(
                    plan.MissionExitPosition,
                    AirWaypointAction.StationEndpoint,
                    firstPassEnd,
                    hasRepeat: true,
                    repeatFromWaypointId: stationEntry.WaypointId,
                    repeatUntil: effectEnd));
                returnTime = effectEnd;
                returnPosition = plan.MissionExitPosition;
            }
            else if (request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Sustained)
            {
                var effectArea = CreateSustainedEffectArea(
                    request,
                    plan,
                    barcapCoverage);
                var stationPoints = plan.BarcapRacetrack?.LoopPointsFeet;
                var stationEntryPosition = stationPoints?.FirstOrDefault()
                                           ?? plan.MissionEntryPosition;
                var stationEntry = NewWaypoint(
                    stationEntryPosition,
                    AirWaypointAction.StationEntry,
                    effectStart,
                    effectArea,
                    barcapCoverage);
                route.Add(stationEntry);
                var stationTime = effectStart;
                var previousStationPoint = stationEntryPosition;
                if (stationPoints != null && stationPoints.Count > 1)
                {
                    for (var index = 1; index < stationPoints.Count; index++)
                    {
                        var point = stationPoints[index];
                        stationTime += TimeSpan.FromSeconds(
                            AirspaceGeometry.TravelSeconds(
                                previousStationPoint,
                                point,
                                plan.AircraftType.CruiseSpeedKnots,
                                plan.AircraftType.ClimbRateFeetPerMinute,
                                plan.AircraftType.DescentRateFeetPerMinute));
                        var isEndpoint = index == stationPoints.Count - 1;
                        route.Add(NewWaypoint(
                            point,
                            isEndpoint
                                ? AirWaypointAction.StationEndpoint
                                : AirWaypointAction.Transit,
                            stationTime,
                            hasRepeat: isEndpoint,
                            repeatFromWaypointId: isEndpoint
                                ? stationEntry.WaypointId
                                : default,
                            repeatUntil: isEndpoint ? effectEnd : default));
                        previousStationPoint = point;
                    }
                }
                else
                {
                    route.Add(NewWaypoint(
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
                        repeatUntil: effectEnd));
                }
                returnTime = effectEnd;
                returnPosition = stationPoints?.LastOrDefault()
                                 ?? plan.MissionExitPosition;
            }
            else
            {
                route.Add(NewWaypoint(
                    plan.MissionEntryPosition,
                    AirWaypointAction.MissionAction,
                    effectStart,
                    new AirMissionArea(
                        request.MissionArea.CenterTileId,
                        request.MissionArea.RadiusKm)));
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
                    request.MissionArea.RadiusKm);
            }

            return new AirMissionArea(
                AirspaceGeometry.TileCoordinateFromPositionFeet(
                    barcapCoverage.StationCenterFeet),
                Math.Max(0f, barcapCoverage.PlannedResponseRadiusKm));
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

        private static float GetMissionAltitudeFeet(
            AirMissionRequestType missionType,
            BarcapStationCoverage barcapCoverage)
        {
            return missionType switch
            {
                AirMissionRequestType.ProvideAirborneC2 => AwacsAltitudeFeet,
                AirMissionRequestType.ProvideAerialRefueling => TankerAltitudeFeet,
                AirMissionRequestType.BarrierCombatAirPatrol => Math.Max(
                    0f,
                    barcapCoverage?.StationCenterFeet.y
                    ?? AllianceAirDoctrine.DefaultBarcapStationAltitudeFeet),
                _ => OcaAltitudeFeet
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

        private sealed class DeadSquadronCandidate
        {
            public Squadron Squadron { get; }
            public AircraftTypeDefinition AircraftType { get; }
            public List<CampaignAircraft> AvailableAircraft { get; }
            public DeadAircraftLoadoutPlan LoadoutPlan { get; }
            public float DistanceKm { get; }

            public DeadSquadronCandidate(
                Squadron squadron,
                AircraftTypeDefinition aircraftType,
                List<CampaignAircraft> availableAircraft,
                DeadAircraftLoadoutPlan loadoutPlan,
                float distanceKm)
            {
                Squadron = squadron;
                AircraftType = aircraftType;
                AvailableAircraft = availableAircraft;
                LoadoutPlan = loadoutPlan;
                DistanceKm = Math.Max(0f, distanceKm);
            }
        }

        private sealed class DeadAttackSelection
        {
            public List<SelectedCombatAircraft> Flights { get; } =
                new List<SelectedCombatAircraft>();
            public int MinimumEffectStores { get; set; }
            public int CleanupStores { get; set; }
            public int SelfDefenseShots { get; set; }
        }

        private sealed class FighterEscortSelection
        {
            public List<SelectedCombatAircraft> Flights { get; } =
                new List<SelectedCombatAircraft>();
            public float CombatPower { get; set; }
        }

        private sealed class BarcapSelectionChoice
        {
            public CombatSquadronCandidate Candidate { get; }
            public float RadiusKm { get; }
            public float PreferredLaunchRangeKm { get; }
            public BarcapRacetrackGeometry Racetrack { get; }
            public float StationDepthKm { get; }
            public float TrackHalfLengthKm { get; }
            public float MinimumInterceptSlackKm { get; }
            public float DesiredInterceptSlackKm { get; }
            public List<Vector3Int> Covered { get; }
            public int UncoveredCount { get; }

            public BarcapSelectionChoice(
                CombatSquadronCandidate candidate,
                float radiusKm,
                float preferredLaunchRangeKm,
                BarcapRacetrackGeometry racetrack,
                float stationDepthKm,
                float trackHalfLengthKm,
                float minimumInterceptSlackKm,
                float desiredInterceptSlackKm,
                List<Vector3Int> covered,
                int uncoveredCount)
            {
                Candidate = candidate;
                RadiusKm = Math.Max(0f, radiusKm);
                PreferredLaunchRangeKm = Math.Max(
                    0f,
                    preferredLaunchRangeKm);
                Racetrack = racetrack
                            ?? throw new ArgumentNullException(nameof(racetrack));
                StationDepthKm = Math.Max(0f, stationDepthKm);
                TrackHalfLengthKm = Math.Max(0f, trackHalfLengthKm);
                MinimumInterceptSlackKm = minimumInterceptSlackKm;
                DesiredInterceptSlackKm = Math.Max(0f, desiredInterceptSlackKm);
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
            public BarcapRacetrackGeometry BarcapRacetrack;

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
