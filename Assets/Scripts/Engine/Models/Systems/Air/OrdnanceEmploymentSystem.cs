using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Models
{
    public sealed class OrdnanceEmploymentSystem
    {
        private const float HotAspectDegrees = 30f;
        private const float CloseRangePreferenceKm = 18f;
        private const float MinimumWeaponQualityTrack = 0.5f;
        private const int MaximumEmploymentRecords = 5000;

        private readonly GameManager gameManager;
        private readonly AirTaskingSystem airTaskingSystem;
        private readonly IADSSystem iadsSystem;
        public List<ActiveOrdnanceEmploymentPass> ActivePasses =
            new List<ActiveOrdnanceEmploymentPass>();
        public List<PendingOrdnanceEffect> PendingEffects =
            new List<PendingOrdnanceEffect>();
        public List<OrdnanceEmploymentRecord> Records =
            new List<OrdnanceEmploymentRecord>();
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes;
        private readonly IReadOnlyDictionary<Guid, AirDefenseComponentDefinition>
            airDefenseComponentDefinitions;

        public OrdnanceEmploymentSystem(
            GameManager gameManager,
            AirTaskingSystem airTaskingSystem,
            IADSSystem iadsSystem,
            ModuleDefinition module)
        {
            this.gameManager = gameManager;
            this.airTaskingSystem = airTaskingSystem;
            this.iadsSystem = iadsSystem;
            aircraftTypes = module.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
            airDefenseComponentDefinitions = module.SamComponentDefinitions
                .ToDictionary(definition => definition.SamComponentDefinitionId);
        }

        public void AdvanceScheduledEvents(DateTime currentTime)
        {
            ProcessDueEvents(currentTime);
        }

        public void RefreshTacticalState(DateTime currentTime)
        {
            var contexts = BuildFlightContexts();
            StartFlightEmploymentPasses(contexts, currentTime);
            ProcessDueEvents(currentTime);

            contexts = BuildFlightContexts();
            RefreshSamEngagementAssignments(contexts, currentTime);
            LaunchAssignedSamShots(contexts, currentTime);
            ProcessDueEvents(currentTime);

            TrimEmploymentRecords();
        }

        private void ProcessDueEvents(DateTime currentTime)
        {
            while (true)
            {
                var nextRelease = ActivePasses
                    .Where(pass => pass.ReleaseAt <= currentTime)
                    .Select(pass => (DateTime?)pass.ReleaseAt)
                    .DefaultIfEmpty()
                    .Min();
                var nextEffect = PendingEffects
                    .Where(effect => effect.ResolveAt <= currentTime)
                    .Select(effect => (DateTime?)effect.ResolveAt)
                    .DefaultIfEmpty()
                    .Min();
                var nextEventAt = Earlier(nextRelease, nextEffect);
                if (!nextEventAt.HasValue)
                    return;

                var timestamp = nextEventAt.Value;
                var releases = ActivePasses
                    .Where(pass => pass.ReleaseAt == timestamp)
                    .OrderBy(pass => pass.SourceFlightId)
                    .ThenBy(pass => pass.EmploymentPassId)
                    .ToList();
                foreach (var pass in releases)
                    ReleaseFlightPass(pass, timestamp);

                var effects = PendingEffects
                    .Where(effect => effect.ResolveAt == timestamp)
                    .OrderBy(effect => effect.TargetFlightId)
                    .ThenBy(effect => effect.PendingEffectId)
                    .ToList();
                if (effects.Count > 0)
                    ResolveEffectBatch(effects, timestamp);
            }
        }

        private static DateTime? Earlier(DateTime? first, DateTime? second)
        {
            if (!first.HasValue)
                return second;
            if (!second.HasValue)
                return first;
            return first.Value <= second.Value ? first : second;
        }

        private void StartFlightEmploymentPasses(
            IReadOnlyDictionary<Guid, FlightContext> contexts,
            DateTime currentTime)
        {
            var activeSourceIds = ActivePasses
                .Select(pass => pass.SourceFlightId)
                .ToHashSet();

            foreach (var source in contexts.Values
                         .Where(context => context.Flight.IsAirborne
                                           && context.LiveAircraft.Count > 0
                                           && !activeSourceIds.Contains(context.Flight.FlightId))
                         .OrderBy(context => context.Flight.FlightId))
            {
                var candidate = SelectFlightEmployment(source, contexts);
                if (candidate == null)
                    continue;

                var preferredAircraft = source.LiveAircraft
                    .Where(aircraft => CountRounds(
                        aircraft,
                        candidate.Ordnance.OrdnanceTypeDefinitionId) > 0)
                    .OrderByDescending(aircraft => CountRounds(
                        aircraft,
                        candidate.Ordnance.OrdnanceTypeDefinitionId))
                    .ThenBy(aircraft => aircraft.AircraftId)
                    .First();
                var quantity = Math.Min(
                    candidate.Target.LiveAircraft.Count,
                    source.LiveAircraft.Sum(aircraft => CountRounds(
                        aircraft,
                        candidate.Ordnance.OrdnanceTypeDefinitionId)));
                if (quantity <= 0)
                    continue;

                var preparationSeconds = candidate.Ordnance.PreparationSeconds
                                         / source.AircraftType.OrdnanceEmploymentEfficiency;
                var pass = new ActiveOrdnanceEmploymentPass
                {
                    SourceFlightId = source.Flight.FlightId,
                    PreferredSourceAircraftId = preferredAircraft.AircraftId,
                    TargetFlightId = candidate.Target.Flight.FlightId,
                    OrdnanceTypeDefinitionId =
                        candidate.Ordnance.OrdnanceTypeDefinitionId,
                    PlannedQuantity = quantity,
                    PreparationStartedAt = currentTime,
                    ReleaseAt = currentTime.AddSeconds(preparationSeconds)
                };
                ActivePasses.Add(pass);
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationStarted,
                    currentTime,
                    quantity,
                    $"{source.DisplayName} began preparing {quantity} " +
                    $"{candidate.Ordnance.Name} against {candidate.Target.DisplayName}.");
            }
        }

        private FlightEmploymentCandidate SelectFlightEmployment(
            FlightContext source,
            IReadOnlyDictionary<Guid, FlightContext> contexts)
        {
            var candidates = new List<FlightEmploymentCandidate>();
            foreach (var target in contexts.Values)
            {
                if (target.Flight.FlightId == source.Flight.FlightId
                    || target.Alliance == source.Alliance
                    || target.Alliance == Alliance.Neutral
                    || target.LiveAircraft.Count == 0
                    || !target.Flight.IsAirborne)
                    continue;

                var distanceKm = Vector3.Distance(
                                     source.Flight.PositionFeet,
                                     target.Flight.PositionFeet)
                                 / AirspaceGeometry.FeetPerKilometer;
                var ordnance = SelectAirToAirOrdnance(source, target, distanceKm);
                if (ordnance == null)
                    continue;

                var aspect = HotAspect(source.Flight, target.Flight);
                var isHot = aspect <= HotAspectDegrees;
                var hasPendingAttack = PendingEffects.Any(effect =>
                    effect.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight
                    && effect.SourceFlightId == target.Flight.FlightId
                    && effect.TargetFlightId == source.Flight.FlightId);
                if (!PostureAllows(source, target, isHot, hasPendingAttack))
                    continue;

                candidates.Add(new FlightEmploymentCandidate(
                    target,
                    ordnance,
                    distanceKm,
                    aspect,
                    isHot,
                    hasPendingAttack));
            }

            return candidates
                .OrderByDescending(candidate => candidate.HasPendingAttack)
                .ThenByDescending(candidate => candidate.IsHot)
                .ThenBy(candidate => candidate.ThreatScore)
                .ThenBy(candidate => candidate.DistanceKm)
                .ThenByDescending(candidate => candidate.Target.LiveAircraft.Count)
                .ThenBy(candidate => candidate.Target.Flight.FlightId)
                .FirstOrDefault();
        }

        private bool PostureAllows(
            FlightContext source,
            FlightContext target,
            bool isHot,
            bool hasPendingAttack)
        {
            switch (source.Flight.MissionType)
            {
                case AirMissionRequestType.DefensiveCounterAirPatrol:
                    return IsInsideMissionArea(source.Flight.MissionArea, target.Flight.PositionFeet);

                case AirMissionRequestType.OffensiveCounterAirSweep:
                    if (source.Flight.ExecutionPhase == FlightExecutionPhase.Outbound
                        || source.Flight.ExecutionPhase == FlightExecutionPhase.Executing)
                        return true;
                    return isHot || hasPendingAttack;

                default:
                    return isHot || hasPendingAttack;
            }
        }

        private bool IsInsideMissionArea(AirMissionArea area, Vector3 positionFeet)
        {
            var center = AirspaceGeometry.TileCenterFeet(
                area.CenterTileId,
                gameManager.SimulationSettings.TileDistanceKM);
            var horizontalDistance = Vector2.Distance(
                new Vector2(center.x, center.z),
                new Vector2(positionFeet.x, positionFeet.z));
            var radiusFeet = (area.RadiusTiles + 0.55f)
                             * gameManager.SimulationSettings.TileDistanceKM
                             * AirspaceGeometry.FeetPerKilometer;
            return horizontalDistance <= radiusFeet;
        }

        private OrdnanceTypeDefinition SelectAirToAirOrdnance(
            FlightContext source,
            FlightContext target,
            float distanceKm)
        {
            var available = source.LiveAircraft
                .SelectMany(aircraft => aircraft.Loadout)
                .Where(item => item.Count > 0
                               && ordnanceTypes.TryGetValue(
                                   item.OrdnanceTypeDefinitionId,
                                   out var definition)
                               && IsAirToAir(definition)
                               && IsInAircraftEnvelope(
                                   source,
                                   target,
                                   definition,
                                   distanceKm))
                .Select(item => ordnanceTypes[item.OrdnanceTypeDefinitionId])
                .Distinct()
                .ToList();
            if (available.Count == 0)
                return null;

            var closeRange = distanceKm <= CloseRangePreferenceKm;
            return available
                .OrderBy(definition => closeRange
                    ? definition.EmploymentCategory ==
                      OrdnanceEmploymentCategory.AirToAirInfrared ? 0 : 1
                    : definition.EmploymentCategory ==
                      OrdnanceEmploymentCategory.AirToAirRadar ? 0 : 1)
                .ThenByDescending(definition => definition.HitProbability)
                .ThenByDescending(definition => definition.GetEffectiveness(
                    OrdnanceTargetCategory.Aircraft))
                .ThenBy(definition => definition.OrdnanceTypeDefinitionId)
                .First();
        }

        private bool IsInAircraftEnvelope(
            FlightContext source,
            FlightContext target,
            OrdnanceTypeDefinition ordnance,
            float distanceKm)
        {
            var maximumRange = EffectiveMaximumRangeKm(ordnance, source.Flight);
            var targetAltitude = target.Flight.PositionFeet.y;
            return maximumRange > 0f
                   && distanceKm >= ordnance.MinimumRangeKm
                   && distanceKm <= maximumRange
                   && targetAltitude >= ordnance.MinimumTargetAltitudeFeet
                   && targetAltitude <= ordnance.MaximumTargetAltitudeFeet;
        }

        private static float EffectiveMaximumRangeKm(
            OrdnanceTypeDefinition ordnance,
            AirFlight source)
        {
            if (ordnance.EmploymentCategory != OrdnanceEmploymentCategory.AirToAirRadar)
                return ordnance.MaximumRangeKm;

            var altitudeMultiplier = 1f + Mathf.Clamp(
                (source.PositionFeet.y - 10000f) / 100000f,
                0f,
                0.3f);
            var speedMultiplier = 1f + Mathf.Clamp(
                (source.SpeedKnots - 400f) / 2000f,
                -0.05f,
                0.2f);
            return ordnance.MaximumRangeKm * altitudeMultiplier * speedMultiplier;
        }

        private void ReleaseFlightPass(
            ActiveOrdnanceEmploymentPass pass,
            DateTime releaseAt)
        {
            ActivePasses.Remove(pass);
            var contexts = BuildFlightContexts();
            if (!contexts.TryGetValue(pass.SourceFlightId, out var source)
                || !contexts.TryGetValue(pass.TargetFlightId, out var target)
                || source.LiveAircraft.Count == 0
                || target.LiveAircraft.Count == 0
                || !ordnanceTypes.TryGetValue(
                    pass.OrdnanceTypeDefinitionId,
                    out var ordnance))
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Employment preparation aborted because its source or target was no longer valid.");
                return;
            }

            var launches = SpendFlightRounds(
                source,
                target,
                pass.OrdnanceTypeDefinitionId,
                pass.PreferredSourceAircraftId,
                pass.PlannedQuantity,
                releaseAt);
            var released = launches.Count;
            if (released <= 0)
            {
                AddRecord(
                    pass,
                    OrdnanceEmploymentRecordStage.PreparationAborted,
                    releaseAt,
                    0,
                    "Employment preparation aborted because no selected ordnance remained.");
                return;
            }

            var distanceFeet = Vector3.Distance(
                source.Flight.PositionFeet,
                target.Flight.PositionFeet);
            var travelSeconds = AirspaceGeometry.HorizontalTravelSeconds(
                distanceFeet,
                ordnance.EffectSpeedKnots);
            var hitProbability = CalculateAircraftHitProbability(
                source,
                target,
                ordnance,
                distanceFeet / AirspaceGeometry.FeetPerKilometer);
            var pending = new PendingOrdnanceEffect
            {
                EmploymentPassId = pass.EmploymentPassId,
                SourceKind = OrdnanceEmploymentSourceKind.AircraftFlight,
                SourceFlightId = source.Flight.FlightId,
                SourceAircraftId = pass.PreferredSourceAircraftId,
                TargetFlightId = target.Flight.FlightId,
                OrdnanceTypeDefinitionId = ordnance.OrdnanceTypeDefinitionId,
                Quantity = released,
                HitProbability = hitProbability,
                ReleasedAt = releaseAt,
                ResolveAt = releaseAt.AddSeconds(travelSeconds),
                ReleaseRangeKm = distanceFeet / AirspaceGeometry.FeetPerKilometer,
                SourcePositionFeet = source.Flight.PositionFeet,
                TargetPositionFeet = target.Flight.PositionFeet,
                Launches = launches
            };
            PendingEffects.Add(pending);
            AddRecord(
                pending,
                OrdnanceEmploymentRecordStage.OrdnanceReleased,
                releaseAt,
                released,
                $"{source.DisplayName} released {released} {ordnance.Name} " +
                $"against {target.DisplayName}.");
        }

        private static List<OrdnanceLaunchDiagnostic> SpendFlightRounds(
            FlightContext source,
            FlightContext target,
            Guid ordnanceTypeDefinitionId,
            Guid preferredAircraftId,
            int requested,
            DateTime releaseAt)
        {
            var remaining = Math.Max(0, requested);
            var launches = new List<OrdnanceLaunchDiagnostic>();
            var targetAircraft = target.LiveAircraft
                .OrderBy(aircraft => aircraft.AircraftId)
                .ToList();
            foreach (var aircraft in source.LiveAircraft
                         .OrderBy(aircraft =>
                             aircraft.AircraftId == preferredAircraftId ? 0 : 1)
                         .ThenBy(aircraft => aircraft.AircraftId))
            {
                var item = aircraft.Loadout.FirstOrDefault(candidate =>
                    candidate.OrdnanceTypeDefinitionId == ordnanceTypeDefinitionId
                    && candidate.Count > 0);
                if (item == null)
                    continue;

                var spent = Math.Min(item.Count, remaining);
                item.Count -= spent;
                for (var index = 0; index < spent; index++)
                {
                    var sequence = launches.Count + 1;
                    var targetIndex = targetAircraft.Count == 0
                        ? -1
                        : StableIndex(aircraft.AircraftId, sequence, targetAircraft.Count);
                    var targetAircraftId = targetIndex < 0
                        ? Guid.Empty
                        : targetAircraft[targetIndex].AircraftId;
                    launches.Add(new OrdnanceLaunchDiagnostic
                    {
                        Sequence = sequence,
                        SourceAircraftId = aircraft.AircraftId,
                        TargetAircraftId = targetAircraftId,
                        OrdnanceTypeDefinitionId = ordnanceTypeDefinitionId,
                        ReleasedAt = releaseAt
                    });
                    if (targetIndex >= 0)
                        targetAircraft.RemoveAt(targetIndex);
                }
                remaining -= spent;
                if (remaining == 0)
                    break;
            }
            foreach (var aircraft in source.LiveAircraft)
                aircraft.Loadout.RemoveAll(item => item.Count <= 0);
            return launches;
        }

        private static float CalculateAircraftHitProbability(
            FlightContext source,
            FlightContext target,
            OrdnanceTypeDefinition ordnance,
            float distanceKm)
        {
            var maximumRange = Math.Max(
                ordnance.MinimumRangeKm + 0.01f,
                EffectiveMaximumRangeKm(ordnance, source.Flight));
            var rangeRatio = Mathf.Clamp01(distanceKm / maximumRange);
            var probability = ordnance.HitProbability * (1f - 0.25f * rangeRatio);
            if (ordnance.GuidanceMode == OrdnanceGuidanceMode.Radar)
            {
                probability *= 0.75f + 0.25f * Mathf.Clamp01(source.AircraftType.RadarQuality);
                probability *= 1f - 0.35f * Mathf.Clamp01(target.AircraftType.EcmQuality);
            }
            else if (ordnance.GuidanceMode == OrdnanceGuidanceMode.Infrared)
            {
                probability *= 1f - 0.2f * Mathf.Clamp01(target.AircraftType.EcmQuality);
            }
            return Mathf.Clamp01(probability);
        }

        private void ResolveEffectBatch(
            IReadOnlyCollection<PendingOrdnanceEffect> effects,
            DateTime resolveAt)
        {
            var losses = new HashSet<Guid>();
            var contexts = BuildFlightContexts();
            foreach (var targetGroup in effects
                         .GroupBy(effect => effect.TargetFlightId)
                         .OrderBy(group => group.Key))
            {
                var availableTargets = contexts.TryGetValue(targetGroup.Key, out var target)
                    ? target.LiveAircraft
                        .OrderBy(aircraft => aircraft.AircraftId)
                        .ToList()
                    : new List<CampaignAircraft>();

                foreach (var effect in targetGroup.OrderBy(item => item.PendingEffectId))
                {
                    var hits = 0;
                    var misses = 0;
                    var ineffective = 0;
                    var shotDiagnostics = new List<OrdnanceShotDiagnostic>();
                    for (var missileIndex = 0; missileIndex < effect.Quantity; missileIndex++)
                    {
                        if (availableTargets.Count == 0)
                        {
                            ineffective++;
                            shotDiagnostics.Add(new OrdnanceShotDiagnostic
                            {
                                Sequence = missileIndex + 1,
                                SourceAircraftId = GetLaunchSourceAircraftId(
                                    effect,
                                    missileIndex),
                                TargetAircraftId = GetLaunchTargetAircraftId(
                                    effect,
                                    missileIndex),
                                Probability = effect.HitProbability,
                                Roll = -1f,
                                Result = OrdnanceShotResult.Ineffective
                            });
                            continue;
                        }

                        var launch = GetLaunch(effect, missileIndex);
                        var targetIndex = -1;
                        CampaignAircraft selectedAircraft;
                        if (launch != null && launch.TargetAircraftId != Guid.Empty)
                        {
                            selectedAircraft = availableTargets.FirstOrDefault(
                                aircraft => aircraft.AircraftId == launch.TargetAircraftId);
                            if (selectedAircraft == null)
                            {
                                ineffective++;
                                shotDiagnostics.Add(new OrdnanceShotDiagnostic
                                {
                                    Sequence = missileIndex + 1,
                                    SourceAircraftId = launch.SourceAircraftId,
                                    TargetAircraftId = launch.TargetAircraftId,
                                    Probability = effect.HitProbability,
                                    Roll = -1f,
                                    Result = OrdnanceShotResult.Ineffective
                                });
                                continue;
                            }
                            targetIndex = availableTargets.IndexOf(selectedAircraft);
                        }
                        else
                        {
                            targetIndex = StableIndex(
                                effect.PendingEffectId,
                                missileIndex,
                                availableTargets.Count);
                            selectedAircraft = availableTargets[targetIndex];
                        }
                        availableTargets.RemoveAt(targetIndex);
                        var roll = (float)StableRoll(effect.PendingEffectId, missileIndex);
                        var result = roll < effect.HitProbability
                            ? OrdnanceShotResult.Hit
                            : OrdnanceShotResult.Miss;
                        shotDiagnostics.Add(new OrdnanceShotDiagnostic
                        {
                            Sequence = missileIndex + 1,
                            SourceAircraftId = launch?.SourceAircraftId ?? Guid.Empty,
                            TargetAircraftId = selectedAircraft.AircraftId,
                            Probability = effect.HitProbability,
                            Roll = roll,
                            Result = result
                        });
                        if (result == OrdnanceShotResult.Hit)
                        {
                            hits++;
                            losses.Add(selectedAircraft.AircraftId);
                        }
                        else
                        {
                            misses++;
                        }
                    }

                    AddRecord(
                        effect,
                        OrdnanceEmploymentRecordStage.EffectResolved,
                        resolveAt,
                        effect.Quantity,
                        $"Effect resolved: {hits} hit, {misses} missed, " +
                        $"{ineffective} became ineffective.",
                        shotDiagnostics);
                    PendingEffects.Remove(effect);
                }
            }

            foreach (var aircraft in gameManager.squadronSystem.Squadrons
                         .SelectMany(squadron => squadron.Aircraft)
                         .Where(aircraft => losses.Contains(aircraft.AircraftId)))
            {
                aircraft.Status = CampaignAircraftStatus.Lost;
                aircraft.ClearLoadout();
            }

            foreach (var targetFlightId in effects
                         .Select(effect => effect.TargetFlightId)
                         .Distinct())
            {
                if (!contexts.TryGetValue(targetFlightId, out var target)
                    || target.LiveAircraft.Any(aircraft =>
                        !losses.Contains(aircraft.AircraftId)))
                    continue;

                target.Flight.Fail(resolveAt, "All aircraft were lost to ordnance effects.");
            }
        }

        private static OrdnanceLaunchDiagnostic GetLaunch(
            PendingOrdnanceEffect effect,
            int missileIndex)
        {
            return effect.Launches == null || missileIndex < 0 || missileIndex >= effect.Launches.Count
                ? null
                : effect.Launches[missileIndex];
        }

        private static Guid GetLaunchSourceAircraftId(
            PendingOrdnanceEffect effect,
            int missileIndex)
        {
            return GetLaunch(effect, missileIndex)?.SourceAircraftId ?? Guid.Empty;
        }

        private static Guid GetLaunchTargetAircraftId(
            PendingOrdnanceEffect effect,
            int missileIndex)
        {
            return GetLaunch(effect, missileIndex)?.TargetAircraftId ?? Guid.Empty;
        }

        private void RefreshSamEngagementAssignments(
            IReadOnlyDictionary<Guid, FlightContext> contexts,
            DateTime currentTime)
        {
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var iads = iadsSystem.GetAllianceIADS(alliance);
                if (iads == null)
                    continue;

                var tracks = iads.CurrentTracks
                    .Where(track => track != null
                                    && !track.IsStale
                                    && track.Quality >= MinimumWeaponQualityTrack
                                    && contexts.ContainsKey(track.FlightId))
                    .ToList();
                var assignments = new List<IADSEngagementAssignment>();
                foreach (var site in gameManager.airDefenseSiteSystem.Sites
                             .Where(site => site != null
                                            && gameManager.airDefenseSiteSystem
                                                .GetEffectiveAlliance(site) == alliance)
                             .OrderBy(site => site.SiteId))
                {
                    if (!TryGetSamSitePosition(site, out var sitePosition)
                        || !HasWeaponQualityRadar(site))
                        continue;

                    var bestTrack = tracks
                        .Where(track => CanAnyLauncherEngage(
                            site,
                            sitePosition,
                            contexts[track.FlightId]))
                        .OrderBy(track => Vector3.Distance(
                            sitePosition,
                            contexts[track.FlightId].Flight.PositionFeet))
                        .ThenBy(track => track.FlightId)
                        .FirstOrDefault();
                    if (bestTrack == null)
                        continue;

                    assignments.Add(new IADSEngagementAssignment
                    {
                        SiteId = site.SiteId,
                        TrackId = bestTrack.TrackId,
                        TargetFlightId = bestTrack.FlightId,
                        AssignedAt = currentTime
                    });
                }
                iads.ReplaceEngagementAssignments(assignments);
            }
        }

        private void LaunchAssignedSamShots(
            IReadOnlyDictionary<Guid, FlightContext> contexts,
            DateTime currentTime)
        {
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var iads = iadsSystem.GetAllianceIADS(alliance);
                if (iads == null)
                    continue;

                foreach (var assignment in iads.CurrentEngagementAssignments
                             .OrderBy(item => item.SiteId))
                {
                    if (!gameManager.airDefenseSiteSystem.TryGetSite(
                            assignment.SiteId,
                            out var site)
                        || !contexts.TryGetValue(
                            assignment.TargetFlightId,
                            out var target)
                        || !TryGetSamSitePosition(site, out var sitePosition))
                        continue;

                    var track = iads.CurrentTracks.FirstOrDefault(candidate =>
                        candidate.TrackId == assignment.TrackId);
                    if (track == null)
                        continue;

                    foreach (var launcher in gameManager.airDefenseSiteSystem
                                 .GetAvailableComponents(site)
                                 .OfType<LauncherAirDefenseComponent>()
                                 .OrderBy(component => component.ComponentId))
                    {
                        if (!TryGetLauncherEmployment(
                                launcher,
                                sitePosition,
                                target,
                                out var launcherDefinition,
                                out var ordnance,
                                out var distanceFeet))
                            continue;
                        if (!launcher.TrySpendRound(launcherDefinition, currentTime))
                            continue;

                        var travelSeconds = AirspaceGeometry.HorizontalTravelSeconds(
                            distanceFeet,
                            ordnance.EffectSpeedKnots);
                        var targetAircraftId = SelectTargetAircraftId(
                            target,
                            launcher.ComponentId,
                            1);
                        var pending = new PendingOrdnanceEffect
                        {
                            EmploymentPassId = Guid.NewGuid(),
                            SourceKind = OrdnanceEmploymentSourceKind.SamLauncher,
                            SourceSiteId = site.SiteId,
                            SourceComponentId = launcher.ComponentId,
                            TargetFlightId = target.Flight.FlightId,
                            OrdnanceTypeDefinitionId =
                                ordnance.OrdnanceTypeDefinitionId,
                            Quantity = 1,
                            HitProbability = Mathf.Clamp01(
                                ordnance.HitProbability * track.Quality),
                            ReleasedAt = currentTime,
                            ResolveAt = currentTime.AddSeconds(travelSeconds),
                            ReleaseRangeKm = distanceFeet / AirspaceGeometry.FeetPerKilometer,
                            SourcePositionFeet = sitePosition,
                            TargetPositionFeet = target.Flight.PositionFeet,
                            Launches = new List<OrdnanceLaunchDiagnostic>
                            {
                                new OrdnanceLaunchDiagnostic
                                {
                                    Sequence = 1,
                                    TargetAircraftId = targetAircraftId,
                                    OrdnanceTypeDefinitionId =
                                        ordnance.OrdnanceTypeDefinitionId,
                                    ReleasedAt = currentTime
                                }
                            }
                        };
                        PendingEffects.Add(pending);
                        AddRecord(
                            pending,
                            OrdnanceEmploymentRecordStage.OrdnanceReleased,
                            currentTime,
                            1,
                            $"SAM site {ShortId(site.SiteId)} launched " +
                            $"{ordnance.Name} at {target.DisplayName}.");
                    }
                }
            }
        }

        private static Guid SelectTargetAircraftId(
            FlightContext target,
            Guid seed,
            int sequence)
        {
            var candidates = target.LiveAircraft
                .OrderBy(aircraft => aircraft.AircraftId)
                .ToList();
            if (candidates.Count == 0)
                return Guid.Empty;
            return candidates[StableIndex(seed, sequence, candidates.Count)].AircraftId;
        }

        private bool CanAnyLauncherEngage(
            SamSite site,
            Vector3 sitePosition,
            FlightContext target)
        {
            return gameManager.airDefenseSiteSystem
                .GetAvailableComponents(site)
                .OfType<LauncherAirDefenseComponent>()
                .Any(launcher => TryGetLauncherEmployment(
                    launcher,
                    sitePosition,
                    target,
                    out _,
                    out _,
                    out _));
        }

        private bool TryGetLauncherEmployment(
            LauncherAirDefenseComponent launcher,
            Vector3 sitePosition,
            FlightContext target,
            out LauncherAirDefenseComponentDefinition launcherDefinition,
            out OrdnanceTypeDefinition ordnance,
            out float distanceFeet)
        {
            launcherDefinition = null;
            ordnance = null;
            distanceFeet = 0f;
            if (launcher == null
                || launcher.IsDamaged
                || !airDefenseComponentDefinitions.TryGetValue(
                    launcher.SamComponentDefinitionId,
                    out var componentDefinition)
                || componentDefinition is not LauncherAirDefenseComponentDefinition definition
                || definition.SurfaceToAirOrdnanceTypeDefinitionId == Guid.Empty
                || !ordnanceTypes.TryGetValue(
                    definition.SurfaceToAirOrdnanceTypeDefinitionId,
                    out var interceptor)
                || interceptor.EmploymentCategory !=
                OrdnanceEmploymentCategory.SurfaceToAir)
                return false;

            launcher.ReloadIfReady(definition, gameManager.CurrentTime);
            if (launcher.ReadyRounds <= 0)
                return false;

            distanceFeet = Vector3.Distance(sitePosition, target.Flight.PositionFeet);
            var distanceKm = distanceFeet / AirspaceGeometry.FeetPerKilometer;
            var altitudeFeet = target.Flight.PositionFeet.y;
            var minimumRange = Math.Max(
                definition.MinEngagementRangeKm,
                interceptor.MinimumRangeKm);
            var maximumRange = Math.Min(
                definition.MaxEngagementRangeKm,
                interceptor.MaximumRangeKm);
            var minimumAltitude = Math.Max(
                definition.MinEngagementAltitudeMeters
                * AirspaceGeometry.FeetPerKilometer / 1000f,
                interceptor.MinimumTargetAltitudeFeet);
            var maximumAltitude = Math.Min(
                definition.MaxEngagementAltitudeMeters
                * AirspaceGeometry.FeetPerKilometer / 1000f,
                interceptor.MaximumTargetAltitudeFeet);
            if (distanceKm < minimumRange
                || distanceKm > maximumRange
                || altitudeFeet < minimumAltitude
                || altitudeFeet > maximumAltitude)
                return false;

            launcherDefinition = definition;
            ordnance = interceptor;
            return true;
        }

        private bool HasWeaponQualityRadar(SamSite site)
        {
            return gameManager.airDefenseSiteSystem
                .GetAvailableComponents(site)
                .OfType<RadarAirDefenseComponent>()
                .Any(radar => !radar.IsDamaged
                              && airDefenseComponentDefinitions.TryGetValue(
                                  radar.SamComponentDefinitionId,
                                  out var definition)
                              && definition is RadarAirDefenseComponentDefinition
                              {
                                  ProvidesWeaponQualityTrack: true
                              });
        }

        private bool TryGetSamSitePosition(SamSite site, out Vector3 position)
        {
            if (gameManager.airDefenseSiteSystem.TryGetTileId(site, out var tileId))
            {
                position = AirspaceGeometry.TileCenterFeet(
                    tileId,
                    gameManager.SimulationSettings.TileDistanceKM);
                return true;
            }
            position = default;
            return false;
        }

        private IReadOnlyDictionary<Guid, FlightContext> BuildFlightContexts()
        {
            var contexts = new Dictionary<Guid, FlightContext>();
            foreach (var package in airTaskingSystem.GetPackages())
            {
                foreach (var flight in package.Flights)
                {
                    if (!gameManager.squadronSystem.TryGetSquadron(
                            flight.SquadronId,
                            out var squadron)
                        || !aircraftTypes.TryGetValue(
                            squadron.AircraftTypeDefinitionId,
                            out var aircraftType))
                        continue;

                    var liveAircraft = squadron.Aircraft
                        .Where(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                           && aircraft.Status != CampaignAircraftStatus.Lost)
                        .OrderBy(aircraft => aircraft.AircraftId)
                        .ToList();
                    contexts[flight.FlightId] = new FlightContext(
                        package.Alliance,
                        flight,
                        squadron,
                        aircraftType,
                        liveAircraft);
                }
            }
            return contexts;
        }

        private void AddRecord(
            ActiveOrdnanceEmploymentPass pass,
            OrdnanceEmploymentRecordStage stage,
            DateTime occurredAt,
            int quantity,
            string detail)
        {
            Records.Add(new OrdnanceEmploymentRecord
            {
                EmploymentPassId = pass.EmploymentPassId,
                Stage = stage,
                SourceKind = OrdnanceEmploymentSourceKind.AircraftFlight,
                SourceFlightId = pass.SourceFlightId,
                TargetFlightId = pass.TargetFlightId,
                OrdnanceTypeDefinitionId = pass.OrdnanceTypeDefinitionId,
                Quantity = quantity,
                OccurredAt = occurredAt,
                Detail = detail
            });
        }

        private void AddRecord(
            PendingOrdnanceEffect effect,
            OrdnanceEmploymentRecordStage stage,
            DateTime occurredAt,
            int quantity,
            string detail,
            List<OrdnanceShotDiagnostic> shots = null)
        {
            Records.Add(new OrdnanceEmploymentRecord
            {
                EmploymentPassId = effect.EmploymentPassId,
                PendingEffectId = effect.PendingEffectId,
                Stage = stage,
                SourceKind = effect.SourceKind,
                SourceFlightId = effect.SourceFlightId,
                SourceAircraftId = effect.SourceAircraftId,
                SourceSiteId = effect.SourceSiteId,
                SourceComponentId = effect.SourceComponentId,
                TargetFlightId = effect.TargetFlightId,
                OrdnanceTypeDefinitionId = effect.OrdnanceTypeDefinitionId,
                Quantity = quantity,
                OccurredAt = occurredAt,
                HitProbability = effect.HitProbability,
                ReleaseRangeKm = effect.ReleaseRangeKm,
                SourcePositionFeet = effect.SourcePositionFeet,
                TargetPositionFeet = effect.TargetPositionFeet,
                Launches = effect.Launches ?? new List<OrdnanceLaunchDiagnostic>(),
                Shots = shots ?? new List<OrdnanceShotDiagnostic>(),
                Detail = detail
            });
        }

        private void TrimEmploymentRecords()
        {
            var records = Records;
            if (records.Count <= MaximumEmploymentRecords)
                return;
            records.RemoveRange(0, records.Count - MaximumEmploymentRecords);
        }

        private static bool IsAirToAir(OrdnanceTypeDefinition definition)
        {
            return definition.EmploymentCategory ==
                   OrdnanceEmploymentCategory.AirToAirRadar
                   || definition.EmploymentCategory ==
                   OrdnanceEmploymentCategory.AirToAirInfrared;
        }

        private static int CountRounds(
            CampaignAircraft aircraft,
            Guid ordnanceTypeDefinitionId)
        {
            return aircraft.Loadout
                .Where(item => item.OrdnanceTypeDefinitionId == ordnanceTypeDefinitionId)
                .Sum(item => Math.Max(0, item.Count));
        }

        private static float HotAspect(AirFlight source, AirFlight target)
        {
            var bearingToSource = Mathf.Atan2(
                                      source.PositionFeet.x - target.PositionFeet.x,
                                      source.PositionFeet.z - target.PositionFeet.z)
                                  * Mathf.Rad2Deg;
            return Math.Abs(Mathf.DeltaAngle(target.HeadingDegrees, bearingToSource));
        }

        private static int StableIndex(Guid id, int sequence, int count)
        {
            if (count <= 1)
                return 0;
            var seed = StableSeed(id, sequence);
            return (int)((uint)seed % (uint)count);
        }

        private static double StableRoll(Guid id, int sequence)
        {
            return new System.Random(StableSeed(id, sequence)).NextDouble();
        }

        private static int StableSeed(Guid id, int sequence)
        {
            unchecked
            {
                var seed = 17;
                foreach (var value in id.ToByteArray())
                    seed = seed * 31 + value;
                return seed * 31 + sequence;
            }
        }

        private static string ShortId(Guid id)
        {
            return id == Guid.Empty
                ? "------"
                : id.ToString("N").Substring(0, 6).ToUpperInvariant();
        }

        private sealed class FlightContext
        {
            public readonly Alliance Alliance;
            public readonly AirFlight Flight;
            public readonly Squadron Squadron;
            public readonly AircraftTypeDefinition AircraftType;
            public readonly List<CampaignAircraft> LiveAircraft;

            public FlightContext(
                Alliance alliance,
                AirFlight flight,
                Squadron squadron,
                AircraftTypeDefinition aircraftType,
                List<CampaignAircraft> liveAircraft)
            {
                Alliance = alliance;
                Flight = flight;
                Squadron = squadron;
                AircraftType = aircraftType;
                LiveAircraft = liveAircraft;
            }

            public string DisplayName => string.IsNullOrWhiteSpace(Squadron.Name)
                ? $"Flight {ShortId(Flight.FlightId)}"
                : Squadron.Name;
        }

        private sealed class FlightEmploymentCandidate
        {
            public readonly FlightContext Target;
            public readonly OrdnanceTypeDefinition Ordnance;
            public readonly float DistanceKm;
            public readonly float AspectDegrees;
            public readonly bool IsHot;
            public readonly bool HasPendingAttack;

            public FlightEmploymentCandidate(
                FlightContext target,
                OrdnanceTypeDefinition ordnance,
                float distanceKm,
                float aspectDegrees,
                bool isHot,
                bool hasPendingAttack)
            {
                Target = target;
                Ordnance = ordnance;
                DistanceKm = distanceKm;
                AspectDegrees = aspectDegrees;
                IsHot = isHot;
                HasPendingAttack = hasPendingAttack;
            }

            public float ThreatScore =>
                DistanceKm * (1f + Mathf.Clamp01(AspectDegrees / HotAspectDegrees) * 0.5f);
        }
    }
}
