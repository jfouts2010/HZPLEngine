using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Service;
using Models.Module;
using UnityEngine;

namespace Models.Gameplay.Campaign
{
    [Serializable]
    public sealed class AllianceIADS
    {
        private const float DefaultStaleExpirySeconds = 15f * 60f;
        private const float DefaultStaleQualityDecayPerSecond = 0.02f;
        private const float BaseTrackBuildRatePerSecond = 0.04f;
        private const float AdditionalRadarDiminishingFactor = 0.5f;
        private const float ObservedExcessQualityDecayPerSecond = 0.01f;
        private const float HeadingChangeQualityPenalty = 0.35f;
        private const float SignificantAltitudeChangeFeet = 10000f;
        private const float UnknownContactAirInterferenceCapabilityPerAircraft = 1f;

        [SerializeReference] public List<IADSTrack> Tracks = new List<IADSTrack>();
        [SerializeReference] public List<IADSEngagementAssignment> EngagementAssignments =
            new List<IADSEngagementAssignment>();

        private Dictionary<Guid, IADSTrack> tracksByFlightId;

        public Alliance Alliance;
        public float StaleExpirySeconds = DefaultStaleExpirySeconds;
        public float StaleQualityDecayPerSecond = DefaultStaleQualityDecayPerSecond;

        public AllianceIADS()
        {
        }

        public AllianceIADS(Alliance alliance)
        {
            Alliance = alliance;
        }

        public IReadOnlyList<IADSTrack> CurrentTracks
        {
            get
            {
                EnsureIndex();
                return Tracks
                    .Where(track => track != null && track.IsEstablished)
                    .ToList();
            }
        }
        public IReadOnlyList<IADSEngagementAssignment> CurrentEngagementAssignments =>
            EngagementAssignments;

        public IADSTrack GetTrackForFlight(Guid flightId)
        {
            EnsureIndex();
            return tracksByFlightId.TryGetValue(flightId, out var track)
                   && track.IsEstablished
                ? track
                : null;
        }

        public void RefreshTracks(
            IEnumerable<AirFlight> activeFlights,
            IReadOnlyDictionary<Guid, Alliance> flightAllianceById,
            IReadOnlyDictionary<Guid, Guid> aircraftTypeByFlightId,
            IReadOnlyDictionary<Guid, int> aircraftCountByFlightId,
            IEnumerable<SamSite> airDefenseSites,
            AirDefenseSiteSystem siteQuery,
            IReadOnlyDictionary<Guid, RadarAirDefenseComponentDefinition> radarDefinitionLookup,
            IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypeDefinitions,
            float elapsedSeconds,
            DateTime observedAt)
        {
            EnsureIndex();

            var allianceByFlightId = flightAllianceById;
            var activeHostileFlights = (activeFlights)
                .Where(flight => flight != null
                                 && flight.FlightId != Guid.Empty
                                 && flight.IsAirborne
                                 && flight.HasPosition
                                 && allianceByFlightId.TryGetValue(flight.FlightId, out var flightAlliance)
                                 && AreHostile(Alliance, flightAlliance))
                .GroupBy(flight => flight.FlightId)
                .Select(group => group.First())
                .ToDictionary(flight => flight.FlightId);

            RemoveInactiveTracks(activeHostileFlights);

            var refreshedFlightIds = new HashSet<Guid>();
            var availableSites = (airDefenseSites)
                .Where(site => site != null
                               && siteQuery != null
                               && siteQuery.GetEffectiveAlliance(site) == Alliance)
                .ToList();

            foreach (var flight in activeHostileFlights.Values)
            {
                if (aircraftTypeByFlightId == null
                    || !aircraftTypeByFlightId.TryGetValue(flight.FlightId, out var aircraftTypeId)
                    || !aircraftTypeDefinitions.TryGetValue(
                        aircraftTypeId,
                        out var aircraftTypeDefinition))
                    continue;

                var contributions = CalculateRadarContributions(
                        flight,
                        aircraftTypeDefinition,
                        availableSites,
                        siteQuery,
                        radarDefinitionLookup,
                        elapsedSeconds)
                    .OrderByDescending(contribution => contribution.QualityIncrease)
                    .ToList();

                if (contributions.Count == 0)
                    continue;

                var totalQualityIncrease = CalculateDiminishedQualityIncrease(contributions);
                var qualityCap = CalculateFusedQualityCap(contributions);
                var currentQuality = tracksByFlightId.TryGetValue(flight.FlightId, out var existingTrack)
                    ? existingTrack.Quality
                    : 0f;
                var newQuality = CalculateObservedQuality(
                    currentQuality,
                    qualityCap,
                    totalQualityIncrease,
                    elapsedSeconds);
                if (existingTrack != null)
                {
                    newQuality = Mathf.Clamp01(
                        newQuality - CalculateManeuverQualityPenalty(
                            existingTrack,
                            flight));
                }
                var aircraftCount = aircraftCountByFlightId != null
                                    && aircraftCountByFlightId.TryGetValue(flight.FlightId, out var count)
                    ? count
                    : flight.AircraftIds.Count;
                var aircraftTypeIsIdentified = existingTrack?.HasIdentifiedAircraftType == true
                                               || newQuality
                                               >= IADSTrack
                                                   .AircraftTypeIdentificationQualityThreshold;
                var estimatedCapabilityPerAircraft = aircraftTypeIsIdentified
                    ? aircraftTypeDefinition.AirInterferenceCapability
                    : UnknownContactAirInterferenceCapabilityPerAircraft;
                var estimatedAirCombatPower = Math.Max(0, aircraftCount)
                                              * estimatedCapabilityPerAircraft;

                if (existingTrack != null)
                {
                    existingTrack.Refresh(
                        flight.PositionFeet,
                        aircraftCount,
                        estimatedAirCombatPower,
                        flight.HeadingDegrees,
                        flight.SpeedKnots,
                        newQuality,
                        observedAt);
                    if (newQuality
                        >= IADSTrack.AircraftTypeIdentificationQualityThreshold)
                    {
                        existingTrack.IdentifyAircraftType(aircraftTypeId);
                    }
                    refreshedFlightIds.Add(flight.FlightId);
                    continue;
                }

                if (newQuality <= 0f)
                    continue;

                var track = new IADSTrack(
                    flight.FlightId,
                    flight.PositionFeet,
                    aircraftCount,
                    estimatedAirCombatPower,
                    flight.HeadingDegrees,
                    flight.SpeedKnots,
                    newQuality,
                    observedAt);
                if (newQuality >= IADSTrack.AircraftTypeIdentificationQualityThreshold)
                    track.IdentifyAircraftType(aircraftTypeId);

                Tracks.Add(track);
                tracksByFlightId[track.FlightId] = track;
                refreshedFlightIds.Add(flight.FlightId);
            }

            MarkUnrefreshedTracksStale(refreshedFlightIds, elapsedSeconds);
            RemoveExpiredStaleTracks();
        }

        public void RebuildIndex()
        {
            foreach (var track in Tracks.Where(track => track != null))
            {
                if (track.Quality >= IADSTrack.MinimumCreationQuality)
                    track.IsEstablished = true;
            }

            tracksByFlightId = (Tracks)
                .Where(track => track != null && track.FlightId != Guid.Empty)
                .GroupBy(track => track.FlightId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        public void ReplaceEngagementAssignments(
            IEnumerable<IADSEngagementAssignment> assignments)
        {
            EngagementAssignments = assignments?
                .Where(assignment => assignment != null
                                     && assignment.SiteId != Guid.Empty
                                     && assignment.TrackId != Guid.Empty
                                     && assignment.TargetFlightId != Guid.Empty
                                     && assignment.FireControlRadarComponentId
                                     != Guid.Empty)
                .OrderBy(assignment => assignment.SiteId)
                .ThenBy(assignment => assignment.TargetFlightId)
                .ToList();
        }

        private IEnumerable<RadarContribution> CalculateRadarContributions(
            AirFlight flight,
            AircraftTypeDefinition aircraftTypeDefinition,
            IEnumerable<SamSite> airDefenseSites,
            AirDefenseSiteSystem siteQuery,
            IReadOnlyDictionary<Guid, RadarAirDefenseComponentDefinition> radarDefinitionLookup,
            float elapsedSeconds)
        {
            if (siteQuery == null)
                yield break;

            foreach (var site in airDefenseSites)
            {
                if (!siteQuery.TryGetPositionFeet(
                        site,
                        out var sitePositionFeet))
                    continue;

                foreach (var radarComponent in siteQuery.GetAvailableComponents(site)
                             .OfType<RadarAirDefenseComponent>())
                {
                    if (radarComponent.IsDamaged
                        || !radarComponent.IsEmitting
                        || radarDefinitionLookup == null
                        || !radarDefinitionLookup.TryGetValue(
                            radarComponent.SamComponentDefinitionId,
                            out var definition)
                        || definition == null
                        || definition.DetectionRangeKm <= 0f)
                        continue;

                    var distanceKm = Vector3.Distance(
                                         sitePositionFeet,
                                         flight.PositionFeet)
                                     / AirspaceGeometry.FeetPerKilometer;
                    var maximumAltitudeFeet = definition.MaxAltitudeMeters
                                              * AirspaceGeometry.FeetPerKilometer
                                              / 1000f;
                    if (distanceKm > definition.DetectionRangeKm
                        || flight.PositionFeet.y > maximumAltitudeFeet)
                        continue;

                    var rangeFactor = definition.CalculateRangeFactor(distanceKm);
                    var detectabilityFactor = aircraftTypeDefinition.RadarDetectability;
                    var qualityCap = definition.CalculateTrackQualityCap(
                        detectabilityFactor,
                        distanceKm);
                    var qualityIncrease = Mathf.Clamp01(
                        BaseTrackBuildRatePerSecond
                        * Mathf.Max(0f, elapsedSeconds)
                        * definition.TrackQuality
                        * detectabilityFactor
                        * rangeFactor);

                    if (qualityCap <= 0f)
                        continue;

                    yield return new RadarContribution(qualityIncrease, qualityCap);
                }
            }
        }

        private static float CalculateDiminishedQualityIncrease(IReadOnlyList<RadarContribution> contributions)
        {
            var total = 0f;
            var multiplier = 1f;
            foreach (var contribution in contributions)
            {
                total += contribution.QualityIncrease * multiplier;
                multiplier *= AdditionalRadarDiminishingFactor;
            }

            return Mathf.Clamp01(total);
        }

        private static float CalculateFusedQualityCap(
            IReadOnlyList<RadarContribution> contributions)
        {
            var remainingUncertainty = 1f;
            foreach (var contribution in contributions)
            {
                remainingUncertainty *= 1f - Mathf.Clamp01(contribution.QualityCap);
            }

            return Mathf.Clamp01(1f - remainingUncertainty);
        }

        private static float CalculateObservedQuality(
            float currentQuality,
            float qualityCap,
            float qualityIncrease,
            float elapsedSeconds)
        {
            var current = Mathf.Clamp01(currentQuality);
            var cap = Mathf.Clamp01(qualityCap);
            if (current <= cap)
                return Mathf.Min(cap, current + Mathf.Max(0f, qualityIncrease));

            return Mathf.Max(
                cap,
                current - ObservedExcessQualityDecayPerSecond
                * Mathf.Max(0f, elapsedSeconds));
        }

        private static float CalculateManeuverQualityPenalty(
            IADSTrack track,
            AirFlight flight)
        {
            if (track == null || flight == null)
                return 0f;

            var headingChange = Mathf.Abs(Mathf.DeltaAngle(
                                    track.EstimatedHeadingDegrees,
                                    flight.HeadingDegrees))
                                / 180f;
            var speedScale = Mathf.Max(
                100f,
                Mathf.Max(track.EstimatedSpeedKnots, flight.SpeedKnots));
            var speedChange = Mathf.Abs(
                                  track.EstimatedSpeedKnots - flight.SpeedKnots)
                              / speedScale;
            var altitudeChange = Mathf.Abs(
                                     track.LastKnownPositionFeet.y
                                     - flight.PositionFeet.y)
                                 / SignificantAltitudeChangeFeet;
            return Mathf.Clamp01(
                headingChange * HeadingChangeQualityPenalty);
        }

        private void RemoveInactiveTracks(IReadOnlyDictionary<Guid, AirFlight> activeHostileFlights)
        {
            Tracks.RemoveAll(track => track == null
                                      || track.FlightId == Guid.Empty
                                      || !activeHostileFlights.ContainsKey(track.FlightId));
            RebuildIndex();
        }

        private void MarkUnrefreshedTracksStale(
            ISet<Guid> refreshedFlightIds,
            float elapsedSeconds)
        {
            foreach (var track in Tracks)
            {
                if (track == null || refreshedFlightIds.Contains(track.FlightId))
                    continue;

                track.MarkStale(elapsedSeconds, StaleQualityDecayPerSecond);
            }
        }

        private void RemoveExpiredStaleTracks()
        {
            Tracks.RemoveAll(track => track == null
                                      || track.IsStale
                                      && (!track.IsEstablished
                                          || track.StaleSeconds >= StaleExpirySeconds));
            RebuildIndex();
        }

        private void EnsureIndex()
        {
            if (tracksByFlightId == null)
                RebuildIndex();
        }

        private static bool AreHostile(Alliance first, Alliance second)
        {
            if (first == Alliance.Neutral || second == Alliance.Neutral)
                return false;

            return first != second;
        }

        private readonly struct RadarContribution
        {
            public readonly float QualityIncrease;
            public readonly float QualityCap;

            public RadarContribution(float qualityIncrease, float qualityCap)
            {
                QualityIncrease = qualityIncrease;
                QualityCap = qualityCap;
            }
        }
    }
}
