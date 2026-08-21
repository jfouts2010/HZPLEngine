using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using Monobehaviours.Singletons;
using UnityEngine;

namespace Engine.Models
{
    public sealed class IADSSystem
    {
        private const float ArmApproachBufferKm = 20f;
        private const float ArmApproachConeDegrees = 30f;
        private const float ArmWarningBaseProbability = 0.2f;
        private const float NeighborArmAlertRadiusKm = 50f;
        private const double DirectArmAlertPostImpactSeconds = 90d;
        private const double NeighborArmAlertPostImpactSeconds = 45d;
        private const double ApproachMinimumHoldSeconds = 45d;
        private const double ApproachMaximumHoldSeconds = 180d;
        private const double ApproachPostClosestPointSeconds = 45d;

        private readonly GameManager gameManager;
        private readonly AllianceIADS blueforIads;
        private readonly AllianceIADS redforIads;
        private readonly List<IADSTrackDiagnostic> pendingTrackDiagnostics =
            new List<IADSTrackDiagnostic>();

        public IADSSystem(GameManager gameManager)
            : this(gameManager, new AllianceIADS(Alliance.Bluefor), new AllianceIADS(Alliance.Redfor))
        {
        }

        public IADSSystem(GameManager gameManager, AllianceIADS blueforIads, AllianceIADS redforIads)
        {
            this.gameManager = gameManager;
            this.blueforIads = blueforIads;
            this.redforIads = redforIads;
            this.blueforIads.Alliance = Alliance.Bluefor;
            this.redforIads.Alliance = Alliance.Redfor;
        }

        public AllianceIADS GetAllianceIADS(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => blueforIads,
                Alliance.Redfor => redforIads,
                _ => null
            };
        }

        /// <summary>
        /// Transfers diagnostics accumulated by five-second tactical updates to
        /// the simulation log without making diagnostic history campaign state.
        /// </summary>
        public IReadOnlyList<IADSTrackDiagnostic> DrainTrackDiagnostics()
        {
            if (pendingTrackDiagnostics.Count == 0)
                return Array.Empty<IADSTrackDiagnostic>();

            var drained = pendingTrackDiagnostics.ToList();
            pendingTrackDiagnostics.Clear();
            return drained;
        }

        public void TacticalTurn(
            float elapsedSeconds,
            DateTime observedAt,
            IEnumerable<PendingOrdnanceEffect> pendingEffects)
        {
            var activeModule = ModuleSingleton.Instance.ActiveModule;
            var aircraftTypeDefinitions = activeModule.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            var radarDefinitionLookup = activeModule.SamComponentDefinitions
                .OfType<RadarAirDefenseComponentDefinition>()
                .ToDictionary(definition => definition.SamComponentDefinitionId);
            var ordnanceDefinitions = activeModule.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
            var antiRadiationOrdnanceIds = ordnanceDefinitions.Values
                .Where(IsAntiRadiation)
                .Select(definition => definition.OrdnanceTypeDefinitionId)
                .ToHashSet();
            var antiRadiationRangeByAircraftTypeId = activeModule
                .AircraftTypeDefinitions
                .Select(definition => new
                {
                    definition.AircraftTypeDefinitionId,
                    MaximumRangeKm = definition.CompatibleOrdnanceTypeDefinitionIds
                        .Where(antiRadiationOrdnanceIds.Contains)
                        .Where(ordnanceDefinitions.ContainsKey)
                        .Select(ordnanceId =>
                            ordnanceDefinitions[ordnanceId].MaximumRangeKm)
                        .DefaultIfEmpty(0f)
                        .Max()
                })
                .Where(candidate => candidate.MaximumRangeKm > 0f)
                .ToDictionary(
                    candidate => candidate.AircraftTypeDefinitionId,
                    candidate => candidate.MaximumRangeKm);
            var airDefenseSites = gameManager.airDefenseSiteSystem.Sites.ToList();
            var activeFlights = gameManager.GetAirborneFlights().ToList();
            var effects = (pendingEffects ?? Array.Empty<PendingOrdnanceEffect>())
                .Where(effect => effect != null && !effect.IsDefeated)
                .ToList();
            var flightContexts = BuildFlightContexts(activeFlights);
            pendingTrackDiagnostics.AddRange(blueforIads.RefreshTracks(
                activeFlights,
                flightContexts.AllianceByFlightId,
                flightContexts.AircraftTypeByFlightId,
                flightContexts.AircraftCountByFlightId,
                airDefenseSites,
                gameManager.airDefenseSiteSystem,
                radarDefinitionLookup,
                aircraftTypeDefinitions,
                elapsedSeconds,
                observedAt));
            pendingTrackDiagnostics.AddRange(redforIads.RefreshTracks(
                activeFlights,
                flightContexts.AllianceByFlightId,
                flightContexts.AircraftTypeByFlightId,
                flightContexts.AircraftCountByFlightId,
                airDefenseSites,
                gameManager.airDefenseSiteSystem,
                radarDefinitionLookup,
                aircraftTypeDefinitions,
                elapsedSeconds,
                observedAt));

            RefreshEmissionControl(
                blueforIads,
                airDefenseSites,
                effects,
                ordnanceDefinitions,
                antiRadiationRangeByAircraftTypeId,
                observedAt);
            RefreshEmissionControl(
                redforIads,
                airDefenseSites,
                effects,
                ordnanceDefinitions,
                antiRadiationRangeByAircraftTypeId,
                observedAt);
        }

        private void RefreshEmissionControl(
            AllianceIADS iads,
            IReadOnlyCollection<SamSite> airDefenseSites,
            IReadOnlyCollection<PendingOrdnanceEffect> pendingEffects,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceDefinitions,
            IReadOnlyDictionary<Guid, float> antiRadiationRangeByAircraftTypeId,
            DateTime observedAt)
        {
            if (iads == null)
                return;

            var alliedSites = airDefenseSites
                .Where(site => IsOperationalAlliedSite(site, iads.Alliance))
                .OrderBy(site => site.SiteId)
                .ToList();
            if (alliedSites.Count == 0)
                return;

            ApplyDetectedArmAlerts(
                iads,
                alliedSites,
                pendingEffects,
                ordnanceDefinitions,
                observedAt);
            ApplyApproachEmissionControl(
                iads,
                alliedSites,
                pendingEffects,
                antiRadiationRangeByAircraftTypeId,
                observedAt);
        }

        private void ApplyDetectedArmAlerts(
            AllianceIADS iads,
            IReadOnlyCollection<SamSite> alliedSites,
            IReadOnlyCollection<PendingOrdnanceEffect> pendingEffects,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceDefinitions,
            DateTime observedAt)
        {
            // Pending effects are ground truth candidates, not automatic
            // defender knowledge. The stable roll models whether this IADS
            // recognizes each launch, with its source-aircraft track improving
            // the chance of a timely warning.
            foreach (var effect in pendingEffects
                         .Where(effect => effect.TargetKind
                                          == OrdnanceEmploymentTargetKind
                                              .AirDefenseComponent
                                          && effect.TargetSiteId != Guid.Empty
                                          && effect.ResolveAt > observedAt
                                          && ordnanceDefinitions.TryGetValue(
                                              effect.OrdnanceTypeDefinitionId,
                                              out var ordnance)
                                          && IsAntiRadiation(ordnance))
                         .OrderBy(effect => effect.PendingEffectId))
            {
                var targetSite = alliedSites.FirstOrDefault(site =>
                    site.SiteId == effect.TargetSiteId);
                if (targetSite == null
                    || !gameManager.airDefenseSiteSystem.TryGetPositionFeet(
                        targetSite,
                        out var targetPosition))
                    continue;

                var sourceTrack = iads.CurrentTracks.FirstOrDefault(track =>
                    track.FlightId == effect.SourceFlightId
                    && !track.IsStale);
                var trackQuality = sourceTrack?.Quality ?? 0f;
                var warningProbability = Mathf.Clamp01(
                    ArmWarningBaseProbability
                    + (1f - ArmWarningBaseProbability) * trackQuality);
                if (StableRoll(
                        effect.PendingEffectId,
                        targetSite.SiteId,
                        0) >= warningProbability)
                    continue;

                HoldSiteRadars(
                    targetSite,
                    effect.ResolveAt.AddSeconds(
                        DirectArmAlertPostImpactSeconds),
                    pendingEffects,
                    observedAt);

                if (!HasOperationalCommandComponent(targetSite))
                    continue;

                foreach (var neighbor in alliedSites
                             .Where(site => site.SiteId != targetSite.SiteId
                                            && HasOperationalCommandComponent(site))
                             .OrderBy(site => site.SiteId))
                {
                    if (!gameManager.airDefenseSiteSystem.TryGetPositionFeet(
                            neighbor,
                            out var neighborPosition))
                        continue;
                    var distanceKm = HorizontalDistanceKm(
                        targetPosition,
                        neighborPosition);
                    if (distanceKm > NeighborArmAlertRadiusKm)
                        continue;

                    var distanceFactor = Mathf.Clamp01(
                        1f - distanceKm / NeighborArmAlertRadiusKm);
                    var neighborProbability = warningProbability
                                              * (0.35f + 0.65f * distanceFactor);
                    if (StableRoll(
                            effect.PendingEffectId,
                            neighbor.SiteId,
                            1) >= neighborProbability)
                        continue;

                    HoldSiteRadars(
                        neighbor,
                        effect.ResolveAt.AddSeconds(
                            NeighborArmAlertPostImpactSeconds),
                        pendingEffects,
                        observedAt);
                }
            }
        }

        private void ApplyApproachEmissionControl(
            AllianceIADS iads,
            IReadOnlyCollection<SamSite> alliedSites,
            IReadOnlyCollection<PendingOrdnanceEffect> pendingEffects,
            IReadOnlyDictionary<Guid, float> antiRadiationRangeByAircraftTypeId,
            DateTime observedAt)
        {
            if (antiRadiationRangeByAircraftTypeId.Count == 0)
                return;

            var tracks = iads.CurrentTracks
                .Where(track => track != null
                                && !track.IsStale
                                && track.HasIdentifiedAircraftType
                                && antiRadiationRangeByAircraftTypeId.ContainsKey(
                                    track.IdentifiedAircraftTypeDefinitionId))
                .OrderBy(track => track.FlightId)
                .ToList();
            foreach (var site in alliedSites)
            {
                if (!gameManager.airDefenseSiteSystem.TryGetPositionFeet(
                        site,
                        out var sitePosition))
                    continue;

                var holdUntil = default(DateTime);
                foreach (var track in tracks)
                {
                    if (!TryGetApproachHoldUntil(
                            track,
                            sitePosition,
                            antiRadiationRangeByAircraftTypeId[
                                track.IdentifiedAircraftTypeDefinitionId],
                            observedAt,
                            out var candidateHoldUntil)
                        || candidateHoldUntil <= holdUntil)
                        continue;
                    holdUntil = candidateHoldUntil;
                }

                if (holdUntil != default)
                {
                    HoldSiteRadars(
                        site,
                        holdUntil,
                        pendingEffects,
                        observedAt);
                }
            }
        }

        private static bool TryGetApproachHoldUntil(
            IADSTrack track,
            Vector3 sitePosition,
            float maximumAntiRadiationRangeKm,
            DateTime observedAt,
            out DateTime holdUntil)
        {
            holdUntil = default;
            if (track == null || track.EstimatedSpeedKnots <= 0f)
                return false;

            var toSite = sitePosition - track.LastKnownPositionFeet;
            toSite.y = 0f;
            var distanceFeet = toSite.magnitude;
            if (distanceFeet <= 1f
                || distanceFeet / AirspaceGeometry.FeetPerKilometer
                > maximumAntiRadiationRangeKm + ArmApproachBufferKm)
                return false;

            var direction = AirCombatRules.Direction(
                track.EstimatedHeadingDegrees);
            direction.y = 0f;
            direction.Normalize();
            var closingAlignment = Vector3.Dot(
                direction,
                toSite / distanceFeet);
            if (closingAlignment < Mathf.Cos(
                    ArmApproachConeDegrees * Mathf.Deg2Rad))
                return false;

            var feetPerSecond = track.EstimatedSpeedKnots
                                * AirspaceGeometry.FeetPerNauticalMile
                                / 3600f;
            if (feetPerSecond <= 0f)
                return false;

            var secondsToClosestPoint = Math.Max(
                0d,
                Vector3.Dot(toSite, direction) / feetPerSecond);
            var holdSeconds = Math.Min(
                ApproachMaximumHoldSeconds,
                Math.Max(
                    ApproachMinimumHoldSeconds,
                    secondsToClosestPoint + ApproachPostClosestPointSeconds));
            holdUntil = observedAt.AddSeconds(holdSeconds);
            return true;
        }

        private static void HoldSiteRadars(
            SamSite site,
            DateTime holdUntil,
            IReadOnlyCollection<PendingOrdnanceEffect> pendingEffects,
            DateTime observedAt)
        {
            foreach (var radar in site.Components
                         .OfType<RadarAirDefenseComponent>()
                         .Where(radar => !radar.IsDamaged))
            {
                var isSupporting = pendingEffects.Any(effect =>
                    effect.SourceKind
                    == OrdnanceEmploymentSourceKind.SamLauncher
                    && effect.SourceSiteId == site.SiteId
                    && effect.SupportSourceComponentId == radar.ComponentId
                    && effect.ResolveAt > observedAt);
                // Preserve an existing SAM engagement. Once its guidance
                // obligation ends, a still-current ARM or approach alert will
                // place the radar under the hold on the next tactical update.
                if (!isSupporting)
                    radar.HoldEmissionUntil(holdUntil);
            }
        }

        private bool IsOperationalAlliedSite(SamSite site, Alliance alliance)
        {
            return site != null
                   && !site.IsDisabled
                   && !site.IsDestroyed
                   && !site.IsSuppressed
                   && gameManager.airDefenseSiteSystem.GetEffectiveAlliance(site)
                   == alliance;
        }

        private static bool HasOperationalCommandComponent(SamSite site)
        {
            return site.Components
                .OfType<CommandAirDefenseComponent>()
                .Any(component => !component.IsDamaged);
        }

        private static bool IsAntiRadiation(OrdnanceTypeDefinition definition)
        {
            return definition != null
                   && (definition.EmploymentCategory
                       == OrdnanceEmploymentCategory.AntiRadiation
                       || definition.GuidanceMode
                       == OrdnanceGuidanceMode.AntiRadiation);
        }

        private static float HorizontalDistanceKm(Vector3 first, Vector3 second)
        {
            var delta = first - second;
            delta.y = 0f;
            return delta.magnitude / AirspaceGeometry.FeetPerKilometer;
        }

        private static double StableRoll(Guid first, Guid second, int salt)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                foreach (var value in first.ToByteArray())
                    hash = (hash ^ value) * 1099511628211UL;
                foreach (var value in second.ToByteArray())
                    hash = (hash ^ value) * 1099511628211UL;
                hash = (hash ^ (uint)salt) * 1099511628211UL;
                return (hash & 0x1FFFFFFFFFFFFFUL)
                       / (double)0x20000000000000UL;
            }
        }

        private FlightContexts BuildFlightContexts(IEnumerable<AirFlight> flights)
        {
            var squadronById = gameManager.squadronSystem.Squadrons
                .GroupBy(squadron => squadron.SquadronId)
                .ToDictionary(group => group.Key, group => group.First());

            var contexts = new FlightContexts();
            foreach (var flight in flights)
            {
                if (flight == null
                    || !squadronById.TryGetValue(flight.SquadronId, out var squadron))
                    continue;

                contexts.AllianceByFlightId[flight.FlightId] =
                    gameManager.GetCountryAlliance(squadron.CountryId);
                contexts.AircraftTypeByFlightId[flight.FlightId] =
                    squadron.AircraftTypeDefinitionId;
                contexts.AircraftCountByFlightId[flight.FlightId] =
                    (squadron.Aircraft)
                    .Count(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                       && aircraft.Status != CampaignAircraftStatus.Lost);
            }

            return contexts;
        }

        private sealed class FlightContexts
        {
            public readonly Dictionary<Guid, Alliance> AllianceByFlightId =
                new Dictionary<Guid, Alliance>();

            public readonly Dictionary<Guid, Guid> AircraftTypeByFlightId =
                new Dictionary<Guid, Guid>();

            public readonly Dictionary<Guid, int> AircraftCountByFlightId =
                new Dictionary<Guid, int>();
        }
    }
}
