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
        private const int DefaultStaleExpiryTurns = 3;
        private const float DefaultStaleQualityDecayPerTurn = 0.15f;
        private const float BaseTrackBuildRatePerTurn = 0.20f;
        private const float AdditionalRadarDiminishingFactor = 0.5f;

        [SerializeReference] public List<IADSTrack> Tracks = new List<IADSTrack>();
        [SerializeReference] public List<IADSEngagementAssignment> EngagementAssignments =
            new List<IADSEngagementAssignment>();

        private Dictionary<Guid, IADSTrack> tracksByFlightId;

        public Alliance Alliance;
        public int StaleExpiryTurns = DefaultStaleExpiryTurns;
        public float StaleQualityDecayPerTurn = DefaultStaleQualityDecayPerTurn;

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
            float tileDistanceKm)
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
                        tileDistanceKm)
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

                if (existingTrack != null)
                {
                    existingTrack.Refresh(
                        flight.PositionFeet,
                        aircraftCount,
                        newQuality);
                    refreshedFlightIds.Add(flight.FlightId);
                    continue;
                }

                if (newQuality < IADSTrack.MinimumCreationQuality)
                    continue;

                var track = new IADSTrack(
                    flight.FlightId,
                    aircraftTypeId,
                    flight.PositionFeet,
                    aircraftCount,
                    newQuality);

                Tracks.Add(track);
                tracksByFlightId[track.FlightId] = track;
                refreshedFlightIds.Add(flight.FlightId);
            }

            MarkUnrefreshedTracksStale(refreshedFlightIds);
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
            float tileDistanceKm)
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
                    if (distanceKm > definition.DetectionRangeKm)
                        continue;

                    var rangeFactor = definition.CalculateRangeFactor(distanceKm);
                    var detectabilityFactor = aircraftTypeDefinition.RadarDetectability;
                    var qualityCap = definition.CalculateTrackQualityCap(
                        detectabilityFactor,
                        distanceKm);
                    var qualityIncrease = Mathf.Clamp01(BaseTrackBuildRatePerTurn * definition.TrackQuality *
                                                        detectabilityFactor * rangeFactor);

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

        private void MarkUnrefreshedTracksStale(ISet<Guid> refreshedFlightIds)
        {
            foreach (var track in Tracks)
            {
                if (track == null || refreshedFlightIds.Contains(track.FlightId))
                    continue;

                track.MarkStale(StaleQualityDecayPerTurn);
            }
        }

        private void RemoveExpiredStaleTracks()
        {
            Tracks.RemoveAll(track => track == null || track.IsStale && track.StaleTurns >= StaleExpiryTurns);
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
