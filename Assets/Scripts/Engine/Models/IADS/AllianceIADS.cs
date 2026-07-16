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
        private const float DefaultStaleQualityDecayPerSecond = 0.03f / 60f;
        private const float BaseTrackBuildRatePerSecond = 0.04f;
        private const float AdditionalRadarDiminishingFactor = 0.5f;
        private const float UnknownContactAirControlCapabilityPerAircraft = 1f;

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

        public IReadOnlyList<IADSTrack> CurrentTracks => Tracks;
        public IReadOnlyList<IADSEngagementAssignment> CurrentEngagementAssignments =>
            EngagementAssignments;

        public IADSTrack GetTrackForFlight(Guid flightId)
        {
            EnsureIndex();
            return tracksByFlightId.TryGetValue(flightId, out var track) ? track : null;
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
            float tileDistanceKm,
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
                        tileDistanceKm,
                        elapsedSeconds)
                    .OrderByDescending(contribution => contribution.QualityIncrease)
                    .ToList();

                if (contributions.Count == 0)
                    continue;

                var totalQualityIncrease = CalculateDiminishedQualityIncrease(contributions);
                var qualityCap = contributions.Max(contribution => contribution.QualityCap);
                var currentQuality = tracksByFlightId.TryGetValue(flight.FlightId, out var existingTrack)
                    ? existingTrack.Quality
                    : 0f;
                var newQuality = Mathf.Min(qualityCap, currentQuality + totalQualityIncrease);
                var aircraftCount = aircraftCountByFlightId != null
                                    && aircraftCountByFlightId.TryGetValue(flight.FlightId, out var count)
                    ? count
                    : flight.AircraftIds.Count;
                var aircraftTypeIsIdentified = existingTrack?.HasIdentifiedAircraftType == true
                                               || newQuality
                                               >= IADSTrack
                                                   .AircraftTypeIdentificationQualityThreshold;
                var estimatedCapabilityPerAircraft = aircraftTypeIsIdentified
                    ? aircraftTypeDefinition.AirControlCapability
                    : UnknownContactAirControlCapabilityPerAircraft;
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

                if (newQuality < IADSTrack.MinimumCreationQuality)
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
                                     && assignment.TargetFlightId != Guid.Empty)
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
            float tileDistanceKm,
            float elapsedSeconds)
        {
            if (siteQuery == null)
                yield break;

            foreach (var site in airDefenseSites)
            {
                if (!siteQuery.TryGetTileId(site, out var siteTileId))
                    continue;

                foreach (var radarComponent in siteQuery.GetAvailableComponents(site)
                             .OfType<RadarAirDefenseComponent>())
                {
                    if (radarComponent.IsDamaged
                        || radarDefinitionLookup == null
                        || !radarDefinitionLookup.TryGetValue(
                            radarComponent.SamComponentDefinitionId,
                            out var definition)
                        || definition == null
                        || definition.DetectionRangeKm <= 0f)
                        continue;

                    var sitePositionFeet = AirspaceGeometry.TileCenterFeet(siteTileId, tileDistanceKm);
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

                    if (qualityCap <= 0f || qualityIncrease <= 0f)
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
                                      && track.StaleSeconds >= StaleExpirySeconds);
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
