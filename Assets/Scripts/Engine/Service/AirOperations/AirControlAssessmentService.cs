using System;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Service
{
    public sealed class AirControlAssessmentService
    {
        public static readonly TimeSpan AssessmentInterval = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan PresenceRiseHalfLife = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan PresenceDecayHalfLife = TimeSpan.FromHours(3);
        public static readonly TimeSpan ActivityHalfLife = TimeSpan.FromHours(2);

        private const float HeavyActivityReference = 8f;
        private const float MinimumRememberedCombatPower = 0.05f;

        private readonly HashSet<Vector3Int> tileIds;
        private readonly float tileDistanceKm;
        private readonly IReadOnlyDictionary<Vector3Int, IReadOnlyList<Vector3Int>>
            neighborTileIdsByTileId;
        private readonly Dictionary<Alliance, Dictionary<Vector3Int, IntervalObservation>>
            intervalObservationsByAlliance =
                new Dictionary<Alliance, Dictionary<Vector3Int, IntervalObservation>>();
        private readonly Dictionary<(Alliance ObservingAlliance, Guid ContactId), ContactPresenceState>
            contactPresenceByKey =
                new Dictionary<(Alliance ObservingAlliance, Guid ContactId), ContactPresenceState>();

        private DateTime lastAssessmentAt;
        private DateTime nextAssessmentAt;
        private bool initialized;

        public AirControlAssessmentService(
            IReadOnlyList<Tile> campaignTiles,
            float tileDistanceKm,
            IReadOnlyCollection<Vector3Int> neutralTerritoryTileIds)
        {
            var tiles = campaignTiles ?? Array.Empty<Tile>();
            var neutralTerritory = neutralTerritoryTileIds == null
                ? new HashSet<Vector3Int>()
                : new HashSet<Vector3Int>(neutralTerritoryTileIds);
            this.tileDistanceKm = Mathf.Max(0.001f, tileDistanceKm);
            tileIds = new HashSet<Vector3Int>(tiles
                .Where(tile => tile != null
                               && !neutralTerritory.Contains(tile.Coordinates))
                .Select(tile => tile.Coordinates));
            neighborTileIdsByTileId = tiles
                .Where(tile => tile != null
                               && tileIds.Contains(tile.Coordinates))
                .GroupBy(tile => tile.Coordinates)
                .Where(group => group.Count() == 1)
                .Select(group => group.First())
                .ToDictionary(
                    tile => tile.Coordinates,
                    tile => (IReadOnlyList<Vector3Int>)(tile.NeighborTileIds
                            ?? new List<Vector3Int>())
                        .Where(tileIds.Contains)
                        .Distinct()
                        .ToList());
        }

        public void Initialize(
            DateTime currentTime,
            params AllianceAirTaskingCommander[] commanders)
        {
            lastAssessmentAt = currentTime;
            nextAssessmentAt = currentTime + AssessmentInterval;
            initialized = true;

            foreach (var commander in commanders.Where(commander => commander != null))
                commander.InitializeAirControlAssessments(tileIds);
        }

        public bool ContainsTile(Vector3Int tileId)
        {
            return tileIds.Contains(tileId);
        }

        public void RecordContact(
            Alliance observingAlliance,
            Alliance contactAlliance,
            Guid contactId,
            Vector3Int tileId,
            int estimatedAircraftCount,
            float airCombatPower,
            IReadOnlyList<AirCombatProjection> combatProjections,
            float observationQuality,
            DateTime observedAt)
        {
            if (!initialized
                || contactId == Guid.Empty
                || !tileIds.Contains(tileId)
                || !IsActiveAlliance(observingAlliance)
                || !IsActiveAlliance(contactAlliance))
                return;

            var isFriendly = observingAlliance == contactAlliance;
            if (!isFriendly && !AreHostile(observingAlliance, contactAlliance))
                return;

            var quality = Mathf.Clamp01(observationQuality);
            var aircraftCount = Math.Max(0, estimatedAircraftCount) * quality;
            var combatPower = Mathf.Max(0f, airCombatPower) * quality;
            var combatInfluenceByTileId = BuildCombatInfluence(
                tileId,
                combatPower,
                combatProjections,
                quality);
            var key = (observingAlliance, contactId);

            if (contactPresenceByKey.TryGetValue(key, out var existing))
            {
                var changed = existing.TileId != tileId
                              || existing.IsFriendly != isFriendly
                              || Math.Abs(existing.AircraftCount - aircraftCount) > 0.001f
                              || !HaveSameCombatInfluence(
                                  existing.CombatInfluenceByTileId,
                                  combatInfluenceByTileId);
                if (!changed)
                    return;

                ClosePresence(existing, observedAt);
            }

            contactPresenceByKey[key] = new ContactPresenceState
            {
                ObservingAlliance = observingAlliance,
                TileId = tileId,
                IsFriendly = isFriendly,
                AircraftCount = aircraftCount,
                CombatInfluenceByTileId = combatInfluenceByTileId,
                EnteredAt = observedAt
            };
        }

        public void EndContactsNotObserved(
            Alliance observingAlliance,
            IReadOnlyCollection<Guid> observedContactIds,
            DateTime observedAt)
        {
            var observed = observedContactIds == null
                ? new HashSet<Guid>()
                : new HashSet<Guid>(observedContactIds);
            var ended = contactPresenceByKey
                .Where(entry => entry.Key.ObservingAlliance == observingAlliance
                                && !observed.Contains(entry.Key.ContactId))
                .Select(entry => entry.Key)
                .ToList();

            foreach (var key in ended)
            {
                ClosePresence(contactPresenceByKey[key], observedAt);
                contactPresenceByKey.Remove(key);
            }
        }

        public void RefreshIfDue(
            DateTime currentTime,
            params AllianceAirTaskingCommander[] commanders)
        {
            if (!initialized || currentTime < nextAssessmentAt)
                return;

            while (nextAssessmentAt <= currentTime)
            {
                CloseOpenPresenceAt(nextAssessmentAt);
                RefreshAt(nextAssessmentAt, commanders);
                lastAssessmentAt = nextAssessmentAt;
                nextAssessmentAt += AssessmentInterval;
            }
        }

        private void CloseOpenPresenceAt(DateTime assessmentTime)
        {
            foreach (var presence in contactPresenceByKey.Values)
            {
                ClosePresence(presence, assessmentTime);
                presence.EnteredAt = assessmentTime;
            }
        }

        private void ClosePresence(ContactPresenceState presence, DateTime endedAt)
        {
            var durationSeconds = Math.Max(0d, (endedAt - presence.EnteredAt).TotalSeconds);
            if (durationSeconds <= 0d)
                return;

            var sourceObservation = GetIntervalObservation(
                presence.ObservingAlliance,
                presence.TileId);
            sourceObservation.AddActivity(
                presence.IsFriendly,
                presence.AircraftCount * (float)durationSeconds);

            foreach (var influence in presence.CombatInfluenceByTileId)
            {
                GetIntervalObservation(
                        presence.ObservingAlliance,
                        influence.Key)
                    .AddCombatPower(
                        presence.IsFriendly,
                        influence.Value * (float)durationSeconds);
            }
        }

        private Dictionary<Vector3Int, float> BuildCombatInfluence(
            Vector3Int sourceTileId,
            float fallbackCombatPower,
            IReadOnlyList<AirCombatProjection> combatProjections,
            float observationQuality)
        {
            var influenceByTileId = new Dictionary<Vector3Int, float>();
            var projections = combatProjections?
                .Where(projection => projection.Power > 0f)
                .ToList() ?? new List<AirCombatProjection>();
            if (projections.Count == 0)
            {
                if (fallbackCombatPower > 0f)
                    influenceByTileId[sourceTileId] = fallbackCombatPower;
                return influenceByTileId;
            }

            var maximumRangeKm = projections.Max(
                projection => projection.MaximumInterceptRangeKm);
            var maximumRing = Mathf.CeilToInt(maximumRangeKm / tileDistanceKm);
            var visited = new HashSet<Vector3Int> { sourceTileId };
            var frontier = new Queue<(Vector3Int TileId, int Ring)>();
            frontier.Enqueue((sourceTileId, 0));

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                var distanceKm = current.Ring * tileDistanceKm;
                var influence = projections.Sum(
                    projection => projection.CalculateInfluence(distanceKm));
                influence *= observationQuality;
                if (influence > 0.001f)
                    influenceByTileId[current.TileId] = influence;

                if (current.Ring >= maximumRing
                    || !neighborTileIdsByTileId.TryGetValue(
                        current.TileId,
                        out var neighbors))
                    continue;

                foreach (var neighbor in neighbors)
                {
                    if (visited.Add(neighbor))
                        frontier.Enqueue((neighbor, current.Ring + 1));
                }
            }

            return influenceByTileId;
        }

        private static bool HaveSameCombatInfluence(
            IReadOnlyDictionary<Vector3Int, float> first,
            IReadOnlyDictionary<Vector3Int, float> second)
        {
            if (ReferenceEquals(first, second))
                return true;
            if (first == null || second == null || first.Count != second.Count)
                return false;

            foreach (var influence in first)
            {
                if (!second.TryGetValue(influence.Key, out var other)
                    || Math.Abs(influence.Value - other) > 0.001f)
                    return false;
            }

            return true;
        }

        private IntervalObservation GetIntervalObservation(
            Alliance observingAlliance,
            Vector3Int tileId)
        {
            if (!intervalObservationsByAlliance.TryGetValue(
                    observingAlliance,
                    out var byTileId))
            {
                byTileId = new Dictionary<Vector3Int, IntervalObservation>();
                intervalObservationsByAlliance[observingAlliance] = byTileId;
            }

            if (!byTileId.TryGetValue(tileId, out var observation))
            {
                observation = new IntervalObservation();
                byTileId[tileId] = observation;
            }

            return observation;
        }

        private void RefreshAt(
            DateTime assessmentTime,
            IEnumerable<AllianceAirTaskingCommander> commanders)
        {
            var elapsedSeconds = Math.Max(
                1d,
                (assessmentTime - lastAssessmentAt).TotalSeconds);
            var elapsedHours = elapsedSeconds / 3600d;
            var presenceRiseDecay = Mathf.Pow(
                0.5f,
                (float)(elapsedHours / PresenceRiseHalfLife.TotalHours));
            var presenceFallDecay = Mathf.Pow(
                0.5f,
                (float)(elapsedHours / PresenceDecayHalfLife.TotalHours));
            var activityDecay = Mathf.Pow(
                0.5f,
                (float)(elapsedHours / ActivityHalfLife.TotalHours));

            foreach (var commander in commanders.Where(commander => commander != null))
            {
                intervalObservationsByAlliance.TryGetValue(
                    commander.Alliance,
                    out var intervalByTileId);
                foreach (var tileId in tileIds)
                {
                    var assessment = commander.GetOrCreateAirControlAssessment(tileId);
                    IntervalObservation interval = null;
                    intervalByTileId?.TryGetValue(tileId, out interval);

                    assessment.FriendlyCombatPower = UpdateCombatPowerMemory(
                        assessment.FriendlyCombatPower,
                        interval?.FriendlyCombatPowerSeconds ?? 0f,
                        elapsedSeconds,
                        presenceRiseDecay,
                        presenceFallDecay);
                    assessment.HostileCombatPower = UpdateCombatPowerMemory(
                        assessment.HostileCombatPower,
                        interval?.HostileCombatPowerSeconds ?? 0f,
                        elapsedSeconds,
                        presenceRiseDecay,
                        presenceFallDecay);
                    assessment.FriendlyAirActivity = Mathf.Clamp01(UpdateMemory(
                        assessment.FriendlyAirActivity,
                        (interval?.FriendlyAircraftSeconds ?? 0f)
                        / HeavyActivityReference,
                        elapsedSeconds,
                        activityDecay,
                        activityDecay));
                    assessment.HostileAirActivity = Mathf.Clamp01(UpdateMemory(
                        assessment.HostileAirActivity,
                        (interval?.HostileAircraftSeconds ?? 0f)
                        / HeavyActivityReference,
                        elapsedSeconds,
                        activityDecay,
                        activityDecay));

                }
            }

            intervalObservationsByAlliance.Clear();
        }

        private static float UpdateCombatPowerMemory(
            float previousPower,
            float observedPowerSeconds,
            double elapsedSeconds,
            float riseDecay,
            float fallDecay)
        {
            var previousPresence = AirControlTileAssessment.CalculateCombatPresence(
                previousPower);
            var observedPower = (float)(Math.Max(0f, observedPowerSeconds)
                                        / elapsedSeconds);
            var observedPresence = AirControlTileAssessment.CalculateCombatPresence(
                observedPower);
            var decay = observedPresence >= previousPresence
                ? Mathf.Clamp01(riseDecay)
                : Mathf.Clamp01(fallDecay);
            var updatedPresence = Mathf.Max(
                0f,
                previousPresence * decay
                + observedPresence * (1f - decay));
            var updatedPower = AirControlTileAssessment.CalculateCombatPower(
                updatedPresence);
            return updatedPower < MinimumRememberedCombatPower
                ? 0f
                : updatedPower;
        }

        private static float UpdateMemory(
            float previous,
            float observedValueSeconds,
            double elapsedSeconds,
            float riseDecay,
            float fallDecay)
        {
            var previousValue = Mathf.Max(0f, previous);
            var observedAverage = (float)(Math.Max(0f, observedValueSeconds)
                                          / elapsedSeconds);
            var decay = observedAverage >= previousValue
                ? Mathf.Clamp01(riseDecay)
                : Mathf.Clamp01(fallDecay);
            return Mathf.Max(
                0f,
                previousValue * decay
                + observedAverage * (1f - decay));
        }

        private static bool IsActiveAlliance(Alliance alliance)
        {
            return alliance == Alliance.Bluefor || alliance == Alliance.Redfor;
        }

        private static bool AreHostile(Alliance first, Alliance second)
        {
            return first == Alliance.Bluefor && second == Alliance.Redfor
                   || first == Alliance.Redfor && second == Alliance.Bluefor;
        }

        private sealed class ContactPresenceState
        {
            public Alliance ObservingAlliance;
            public Vector3Int TileId;
            public bool IsFriendly;
            public float AircraftCount;
            public Dictionary<Vector3Int, float> CombatInfluenceByTileId =
                new Dictionary<Vector3Int, float>();
            public DateTime EnteredAt;
        }

        private sealed class IntervalObservation
        {
            public float FriendlyAircraftSeconds;
            public float HostileAircraftSeconds;
            public float FriendlyCombatPowerSeconds;
            public float HostileCombatPowerSeconds;
            public void AddActivity(
                bool friendly,
                float aircraftSeconds)
            {
                if (friendly)
                {
                    FriendlyAircraftSeconds += Mathf.Max(0f, aircraftSeconds);
                }
                else
                {
                    HostileAircraftSeconds += Mathf.Max(0f, aircraftSeconds);
                }
            }

            public void AddCombatPower(
                bool friendly,
                float combatPowerSeconds)
            {
                if (friendly)
                {
                    FriendlyCombatPowerSeconds += Mathf.Max(0f, combatPowerSeconds);
                }
                else
                {
                    HostileCombatPowerSeconds += Mathf.Max(0f, combatPowerSeconds);
                }
            }
        }
    }
}
