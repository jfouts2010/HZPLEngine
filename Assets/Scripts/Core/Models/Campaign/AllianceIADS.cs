using System;
using System.Collections.Generic;
using System.Linq;
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

        private Dictionary<Guid, IADSTrack> tracksByAircraftId;

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

        public IADSTrack GetTrackForAircraft(Guid aircraftId)
        {
            EnsureIndex();
            return tracksByAircraftId.TryGetValue(aircraftId, out var track) ? track : null;
        }

        public void RefreshTracks(
            IEnumerable<CampaignAircraft> activeAircraft,
            IReadOnlyDictionary<Guid, Alliance> aircraftAllianceById,
            IEnumerable<RadarContributionSource> radarSources,
            IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypeDefinitions,
            float tileDistanceKm)
        {
            EnsureIndex();

            var allianceByAircraftId = aircraftAllianceById ?? new Dictionary<Guid, Alliance>();
            var activeHostileAircraft = (activeAircraft ?? Enumerable.Empty<CampaignAircraft>())
                .Where(aircraft => aircraft != null
                                   && aircraft.AircraftId != Guid.Empty
                                   && aircraft.IsActiveInSortie
                                   && aircraft.HasCurrentTileId
                                   && aircraft.Status != CampaignAircraftStatus.Lost
                                   && allianceByAircraftId.TryGetValue(aircraft.AircraftId, out var aircraftAlliance)
                                   && AreHostile(Alliance, aircraftAlliance))
                .GroupBy(aircraft => aircraft.AircraftId)
                .Select(group => group.First())
                .ToDictionary(aircraft => aircraft.AircraftId);

            RemoveInactiveTracks(activeHostileAircraft);

            var refreshedAircraftIds = new HashSet<Guid>();
            var availableRadars = (radarSources ?? Enumerable.Empty<RadarContributionSource>())
                .Where(source => source != null && source.Alliance == Alliance && source.CanContribute)
                .ToList();

            foreach (var aircraft in activeHostileAircraft.Values)
            {
                if (!aircraftTypeDefinitions.TryGetValue(
                        aircraft.AircraftTypeDefinitionId,
                        out var aircraftTypeDefinition))
                    continue;

                var contributions = CalculateRadarContributions(
                        aircraft,
                        aircraftTypeDefinition,
                        availableRadars,
                        tileDistanceKm)
                    .OrderByDescending(contribution => contribution.QualityIncrease)
                    .ToList();

                if (contributions.Count == 0)
                    continue;

                var totalQualityIncrease = CalculateDiminishedQualityIncrease(contributions);
                var qualityCap = contributions.Max(contribution => contribution.QualityCap);
                var currentQuality = tracksByAircraftId.TryGetValue(aircraft.AircraftId, out var existingTrack)
                    ? existingTrack.Quality
                    : 0f;
                var newQuality = Mathf.Min(qualityCap, currentQuality + totalQualityIncrease);

                if (existingTrack != null)
                {
                    existingTrack.Refresh(aircraft.CurrentTileId, newQuality);
                    refreshedAircraftIds.Add(aircraft.AircraftId);
                    continue;
                }

                if (newQuality < IADSTrack.MinimumCreationQuality)
                    continue;

                var track = new IADSTrack(
                    aircraft.AircraftId,
                    aircraft.AircraftTypeDefinitionId,
                    aircraft.CurrentTileId,
                    newQuality);

                Tracks.Add(track);
                tracksByAircraftId[track.AircraftId] = track;
                refreshedAircraftIds.Add(aircraft.AircraftId);
            }

            MarkUnrefreshedTracksStale(refreshedAircraftIds);
            RemoveExpiredStaleTracks();
        }

        public void RebuildIndex()
        {
            tracksByAircraftId = (Tracks ?? new List<IADSTrack>())
                .Where(track => track != null && track.AircraftId != Guid.Empty)
                .GroupBy(track => track.AircraftId)
                .ToDictionary(group => group.Key, group => group.First());
        }

        private IEnumerable<RadarContribution> CalculateRadarContributions(
            CampaignAircraft aircraft,
            AircraftTypeDefinition aircraftTypeDefinition,
            IEnumerable<RadarContributionSource> radarSources,
            float tileDistanceKm)
        {
            foreach (var radarSource in radarSources)
            {
                var definition = radarSource.RadarDefinition;
                if (definition == null || definition.DetectionRangeKm <= 0f)
                    continue;

                var distanceKm = CalculateDistanceKm(radarSource.TileId, aircraft.CurrentTileId, tileDistanceKm);
                if (distanceKm > definition.DetectionRangeKm)
                    continue;

                var rangeFactor = Mathf.Clamp01(1f - distanceKm / definition.DetectionRangeKm);
                var detectabilityFactor = Mathf.Clamp01(aircraftTypeDefinition.RadarQuality);
                var qualityCap = Mathf.Clamp01(definition.TrackQuality * detectabilityFactor * (0.5f + 0.5f * rangeFactor));
                var qualityIncrease = Mathf.Clamp01(BaseTrackBuildRatePerTurn * definition.TrackQuality * detectabilityFactor * rangeFactor);

                if (qualityCap <= 0f || qualityIncrease <= 0f)
                    continue;

                yield return new RadarContribution(qualityIncrease, qualityCap);
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

        private static float CalculateDistanceKm(Vector3Int sourceTileId, Vector3Int targetTileId, float tileDistanceKm)
        {
            var distanceInTiles = Vector3Int.Distance(sourceTileId, targetTileId);
            return distanceInTiles * Mathf.Max(0f, tileDistanceKm);
        }

        private void RemoveInactiveTracks(IReadOnlyDictionary<Guid, CampaignAircraft> activeHostileAircraft)
        {
            Tracks.RemoveAll(track => track == null
                                      || track.AircraftId == Guid.Empty
                                      || !activeHostileAircraft.ContainsKey(track.AircraftId));
            RebuildIndex();
        }

        private void MarkUnrefreshedTracksStale(ISet<Guid> refreshedAircraftIds)
        {
            foreach (var track in Tracks)
            {
                if (track == null || refreshedAircraftIds.Contains(track.AircraftId))
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
            if (tracksByAircraftId == null)
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

    public sealed class RadarContributionSource
    {
        public Alliance Alliance { get; }
        public Vector3Int TileId { get; }
        public RadarAirDefenseComponentDefinition RadarDefinition { get; }
        public bool CanContribute { get; }

        public RadarContributionSource(
            Alliance alliance,
            Vector3Int tileId,
            RadarAirDefenseComponentDefinition radarDefinition,
            bool canContribute)
        {
            Alliance = alliance;
            TileId = tileId;
            RadarDefinition = radarDefinition;
            CanContribute = canContribute;
        }
    }
}
