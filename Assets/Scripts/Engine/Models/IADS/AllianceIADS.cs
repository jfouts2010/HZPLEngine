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
        private const float SimilarRadarCapDiminishingFactor = 0.65f;
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

        public IReadOnlyList<IADSTrackDiagnostic> RefreshTracks(
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
            var diagnostics = new List<IADSTrackDiagnostic>();

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

            RemoveInactiveTracks(activeHostileFlights, observedAt, diagnostics);

            var processedFlightIds = new HashSet<Guid>();
            var availableSites = (airDefenseSites)
                .Where(site => site != null
                               && siteQuery != null
                               && siteQuery.GetEffectiveAlliance(site) == Alliance)
                .ToList();

            foreach (var flight in activeHostileFlights.Values.OrderBy(item => item.FlightId))
            {
                var truthAircraftCount = aircraftCountByFlightId != null
                                         && aircraftCountByFlightId.TryGetValue(
                                             flight.FlightId,
                                             out var liveAircraftCount)
                    ? liveAircraftCount
                    : flight.AircraftIds.Count;
                if (aircraftTypeByFlightId == null
                    || !aircraftTypeByFlightId.TryGetValue(flight.FlightId, out var aircraftTypeId)
                    || !aircraftTypeDefinitions.TryGetValue(
                        aircraftTypeId,
                        out var aircraftTypeDefinition))
                {
                    processedFlightIds.Add(flight.FlightId);
                    if (tracksByFlightId.TryGetValue(flight.FlightId, out var missingContextTrack))
                    {
                        MarkTrackStale(
                            missingContextTrack,
                            flight,
                            Guid.Empty,
                            0f,
                            truthAircraftCount,
                            elapsedSeconds,
                            observedAt,
                            "missing_aircraft_type_context",
                            new List<IADSRadarEvaluation>(),
                            diagnostics);
                    }
                    else
                    {
                        diagnostics.Add(CreateUntrackedDiagnostic(
                            flight,
                            Guid.Empty,
                            0f,
                            truthAircraftCount,
                            elapsedSeconds,
                            observedAt,
                            "missing_aircraft_type_context",
                            new List<IADSRadarEvaluation>()));
                    }
                    continue;
                }

                var radarEvaluations = EvaluateRadars(
                        flight,
                        aircraftTypeDefinition,
                        availableSites,
                        siteQuery,
                        radarDefinitionLookup,
                        elapsedSeconds);
                var contributions = radarEvaluations
                    .Where(evaluation => evaluation.Contributed)
                    .OrderByDescending(evaluation => evaluation.RawQualityIncrease)
                    .ThenBy(evaluation => evaluation.SiteId)
                    .ThenBy(evaluation => evaluation.RadarComponentId)
                    .ToList();

                if (contributions.Count == 0)
                {
                    processedFlightIds.Add(flight.FlightId);
                    if (tracksByFlightId.TryGetValue(flight.FlightId, out var unobservedTrack))
                    {
                        MarkTrackStale(
                            unobservedTrack,
                            flight,
                            aircraftTypeId,
                            aircraftTypeDefinition.RadarDetectability,
                            truthAircraftCount,
                            elapsedSeconds,
                            observedAt,
                            "no_contributing_radars",
                            radarEvaluations,
                            diagnostics);
                    }
                    else
                    {
                        diagnostics.Add(CreateUntrackedDiagnostic(
                            flight,
                            aircraftTypeId,
                            aircraftTypeDefinition.RadarDetectability,
                            truthAircraftCount,
                            elapsedSeconds,
                            observedAt,
                            "no_contributing_radars",
                            radarEvaluations));
                    }
                    continue;
                }

                var totalQualityIncrease = CalculateDiminishedQualityIncrease(contributions);
                var qualityCap = CalculateFusedQualityCap(contributions);
                var currentQuality = tracksByFlightId.TryGetValue(flight.FlightId, out var existingTrack)
                    ? existingTrack.Quality
                    : 0f;
                var qualityAfterObservation = CalculateObservedQuality(
                    currentQuality,
                    qualityCap,
                    totalQualityIncrease,
                    elapsedSeconds);
                var maneuver = CalculateManeuverQualityAdjustment(existingTrack, flight);
                var newQuality = Mathf.Clamp01(
                    qualityAfterObservation - maneuver.AppliedPenalty);
                var aircraftTypeIsIdentified = existingTrack?.HasIdentifiedAircraftType == true
                                               || newQuality
                                               >= IADSTrack
                                                   .AircraftTypeIdentificationQualityThreshold;
                var estimatedCapabilityPerAircraft = aircraftTypeIsIdentified
                    ? aircraftTypeDefinition.AirInterferenceCapability
                    : UnknownContactAirInterferenceCapabilityPerAircraft;
                var estimatedAirCombatPower = Math.Max(0, truthAircraftCount)
                                              * estimatedCapabilityPerAircraft;

                var wasEstablished = existingTrack?.IsEstablished == true;
                var wasStale = existingTrack?.IsStale == true;
                var wasIdentified = existingTrack?.HasIdentifiedAircraftType == true;
                IADSTrack track;

                if (existingTrack != null)
                {
                    existingTrack.Refresh(
                        flight.PositionFeet,
                        truthAircraftCount,
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
                    track = existingTrack;
                }
                else
                {
                    if (newQuality <= 0f)
                    {
                        processedFlightIds.Add(flight.FlightId);
                        diagnostics.Add(CreateUntrackedDiagnostic(
                            flight,
                            aircraftTypeId,
                            aircraftTypeDefinition.RadarDetectability,
                            truthAircraftCount,
                            elapsedSeconds,
                            observedAt,
                            "zero_quality_after_observation",
                            radarEvaluations));
                        continue;
                    }

                    track = new IADSTrack(
                        flight.FlightId,
                        flight.PositionFeet,
                        truthAircraftCount,
                        estimatedAirCombatPower,
                        flight.HeadingDegrees,
                        flight.SpeedKnots,
                        newQuality,
                        observedAt);
                    if (newQuality >= IADSTrack.AircraftTypeIdentificationQualityThreshold)
                        track.IdentifyAircraftType(aircraftTypeId);

                    Tracks.Add(track);
                    tracksByFlightId[track.FlightId] = track;
                }

                processedFlightIds.Add(flight.FlightId);
                var becameEstablished = !wasEstablished && track.IsEstablished;
                var becameIdentified = !wasIdentified && track.HasIdentifiedAircraftType;
                var trackEvent = existingTrack == null
                    ? track.IsEstablished
                        ? IADSTrackDiagnosticEvent.Established
                        : IADSTrackDiagnosticEvent.TentativeStarted
                    : wasStale
                        ? IADSTrackDiagnosticEvent.Reacquired
                        : becameEstablished
                            ? IADSTrackDiagnosticEvent.Established
                            : becameIdentified
                                ? IADSTrackDiagnosticEvent.Identified
                                : track.IsEstablished
                                    ? IADSTrackDiagnosticEvent.Updated
                                    : IADSTrackDiagnosticEvent.TentativeUpdated;
                diagnostics.Add(new IADSTrackDiagnostic
                {
                    OccurredAt = observedAt,
                    ObserverAlliance = Alliance,
                    FlightId = flight.FlightId,
                    AircraftTypeDefinitionId = aircraftTypeId,
                    TrackId = track.TrackId,
                    Event = trackEvent,
                    Reason = "radar_observation",
                    ElapsedSeconds = Mathf.Max(0f, elapsedSeconds),
                    TruthPositionFeet = flight.PositionFeet,
                    TruthHeadingDegrees = flight.HeadingDegrees,
                    TruthSpeedKnots = flight.SpeedKnots,
                    HasTrackEstimate = true,
                    TrackPositionFeet = track.LastKnownPositionFeet,
                    TrackHeadingDegrees = track.EstimatedHeadingDegrees,
                    TrackSpeedKnots = track.EstimatedSpeedKnots,
                    TruthAircraftCount = truthAircraftCount,
                    EstimatedAircraftCount = track.EstimatedAircraftCount,
                    EstimatedAirCombatPower = track.EstimatedAirCombatPower,
                    TargetRadarDetectability = aircraftTypeDefinition.RadarDetectability,
                    PreviousQuality = currentQuality,
                    QualityAfterObservation = qualityAfterObservation,
                    NewQuality = track.Quality,
                    FusedQualityCap = qualityCap,
                    DiminishedQualityIncrease = totalQualityIncrease,
                    ObservedExcessQualityDecay = Mathf.Max(
                        0f,
                        currentQuality - qualityAfterObservation),
                    HeadingChangeFraction = maneuver.HeadingChangeFraction,
                    SpeedChangeFraction = maneuver.SpeedChangeFraction,
                    AltitudeChangeFraction = maneuver.AltitudeChangeFraction,
                    HeadingQualityPenalty = maneuver.HeadingPenalty,
                    SpeedQualityPenalty = maneuver.SpeedPenalty,
                    AltitudeQualityPenalty = maneuver.AltitudePenalty,
                    AppliedManeuverQualityPenalty = maneuver.AppliedPenalty,
                    StaleSeconds = track.StaleSeconds,
                    WasEstablished = wasEstablished,
                    IsEstablished = track.IsEstablished,
                    WasStale = wasStale,
                    IsStale = track.IsStale,
                    BecameEstablished = becameEstablished,
                    BecameIdentified = becameIdentified,
                    HasIdentifiedAircraftType = track.HasIdentifiedAircraftType,
                    RadarEvaluations = radarEvaluations
                });
            }

            MarkUnprocessedTracksStale(
                processedFlightIds,
                activeHostileFlights,
                elapsedSeconds,
                observedAt,
                diagnostics);
            return diagnostics;
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

        private List<IADSRadarEvaluation> EvaluateRadars(
            AirFlight flight,
            AircraftTypeDefinition aircraftTypeDefinition,
            IEnumerable<SamSite> airDefenseSites,
            AirDefenseSiteSystem siteQuery,
            IReadOnlyDictionary<Guid, RadarAirDefenseComponentDefinition> radarDefinitionLookup,
            float elapsedSeconds)
        {
            var evaluations = new List<IADSRadarEvaluation>();
            if (siteQuery == null)
                return evaluations;

            foreach (var site in airDefenseSites)
            {
                var hasSitePosition = siteQuery.TryGetPositionFeet(
                    site,
                    out var sitePositionFeet);

                foreach (var radarComponent in (site.Components ?? new List<AirDefenseComponent>())
                             .OfType<RadarAirDefenseComponent>())
                {
                    RadarAirDefenseComponentDefinition definition = null;
                    radarDefinitionLookup?.TryGetValue(
                        radarComponent.SamComponentDefinitionId,
                        out definition);
                    var evaluation = new IADSRadarEvaluation
                    {
                        SiteId = site.SiteId,
                        RadarComponentId = radarComponent.ComponentId,
                        RadarDefinitionId = radarComponent.SamComponentDefinitionId,
                        RadarName = definition?.Name ?? string.Empty,
                        SitePositionFeet = sitePositionFeet,
                        HasSitePosition = hasSitePosition,
                        TargetAltitudeFeet = flight.PositionFeet.y,
                        TargetDetectability = aircraftTypeDefinition.RadarDetectability,
                        MaximumRangeKm = definition?.DetectionRangeKm ?? 0f,
                        RadarAntennaHeightMeters = definition?.AntennaHeightMeters ?? 0f,
                        FusionCorrelationGroup = definition?.FusionCorrelationGroup
                                                 ?? string.Empty,
                        MaximumAltitudeFeet = definition == null
                            ? 0f
                            : definition.MaxAltitudeMeters
                              * AirspaceGeometry.FeetPerKilometer
                              / 1000f,
                        RadarTrackQuality = definition?.TrackQuality ?? 0f
                    };
                    evaluations.Add(evaluation);

                    if (hasSitePosition
                        && definition != null
                        && definition.DetectionRangeKm > 0f)
                    {
                        var geometry = RadarDetectionGeometryCalculator.Calculate(
                            definition,
                            sitePositionFeet,
                            flight.PositionFeet,
                            aircraftTypeDefinition.RadarDetectability);
                        evaluation.RadarAltitudeMeters = geometry.RadarAltitudeMeters;
                        evaluation.HorizontalDistanceKm = geometry.HorizontalDistanceKm;
                        evaluation.DistanceKm = geometry.SlantDistanceKm;
                        evaluation.DetectabilityAdjustedRangeKm =
                            geometry.DetectabilityAdjustedRangeKm;
                        evaluation.RadarHorizonKm = geometry.RadarHorizonKm;
                        evaluation.DistanceFraction = geometry.EquipmentRangeFraction;
                        evaluation.RadarHorizonFraction = geometry.RadarHorizonFraction;
                        evaluation.RangeMarginKm =
                            geometry.DetectabilityAdjustedRangeKm
                            - geometry.SlantDistanceKm;
                        evaluation.RadarHorizonMarginKm = geometry.RadarHorizonKm
                                                         - geometry.HorizontalDistanceKm;
                        evaluation.LimitingConstraint = geometry.LimitingConstraint;
                        evaluation.AltitudeMarginFeet =
                            evaluation.MaximumAltitudeFeet
                            - flight.PositionFeet.y;
                        evaluation.RangeFactor = geometry.RangeFactor;
                        evaluation.QualityCap = definition.CalculateTrackQualityCap(
                            evaluation.RangeFactor);
                        evaluation.RawQualityIncrease = Mathf.Clamp01(
                            BaseTrackBuildRatePerSecond
                            * Mathf.Max(0f, elapsedSeconds)
                            * definition.TrackQuality
                            * evaluation.RangeFactor);
                    }

                    if (site.IsDisabled)
                    {
                        evaluation.Result = IADSRadarEvaluationResult.SiteDisabled;
                        continue;
                    }
                    if (site.IsDestroyed)
                    {
                        evaluation.Result = IADSRadarEvaluationResult.SiteDestroyed;
                        continue;
                    }
                    if (site.IsSuppressed)
                    {
                        evaluation.Result = IADSRadarEvaluationResult.SiteSuppressed;
                        continue;
                    }
                    if (!hasSitePosition)
                    {
                        evaluation.Result =
                            IADSRadarEvaluationResult.SitePositionUnavailable;
                        continue;
                    }
                    if (radarComponent.IsDamaged)
                    {
                        evaluation.Result = IADSRadarEvaluationResult.RadarDamaged;
                        continue;
                    }
                    if (!radarComponent.IsEmitting)
                    {
                        evaluation.Result = IADSRadarEvaluationResult.RadarSilent;
                        continue;
                    }
                    if (definition == null)
                    {
                        evaluation.Result = IADSRadarEvaluationResult.DefinitionMissing;
                        continue;
                    }
                    if (definition.DetectionRangeKm <= 0f)
                    {
                        evaluation.Result =
                            IADSRadarEvaluationResult.DetectionRangeInvalid;
                        continue;
                    }

                    if (aircraftTypeDefinition.RadarDetectability <= 0f)
                    {
                        evaluation.Result =
                            IADSRadarEvaluationResult.TargetUndetectable;
                        continue;
                    }

                    if (evaluation.DistanceFraction > 1f
                        || evaluation.RadarHorizonFraction > 1f)
                    {
                        evaluation.Result = evaluation.LimitingConstraint
                                            == RadarRangeConstraint.RadarHorizon
                            ? IADSRadarEvaluationResult.BelowRadarHorizon
                            : IADSRadarEvaluationResult.OutOfRange;
                        continue;
                    }
                    if (flight.PositionFeet.y > evaluation.MaximumAltitudeFeet)
                    {
                        evaluation.Result =
                            IADSRadarEvaluationResult.AboveAltitudeCeiling;
                        continue;
                    }

                    if (evaluation.QualityCap <= 0f)
                    {
                        evaluation.Result = IADSRadarEvaluationResult.ZeroQualityCap;
                        continue;
                    }

                    evaluation.Result = IADSRadarEvaluationResult.Contributed;
                }
            }

            return evaluations
                .OrderBy(evaluation => evaluation.SiteId)
                .ThenBy(evaluation => evaluation.RadarComponentId)
                .ToList();
        }

        private static float CalculateDiminishedQualityIncrease(
            IReadOnlyList<IADSRadarEvaluation> contributions)
        {
            var total = 0f;
            var multiplier = 1f;
            foreach (var contribution in contributions)
            {
                contribution.AppliedBuildMultiplier = multiplier;
                contribution.AppliedQualityIncrease =
                    contribution.RawQualityIncrease * multiplier;
                total += contribution.AppliedQualityIncrease;
                multiplier *= AdditionalRadarDiminishingFactor;
            }

            return Mathf.Clamp01(total);
        }

        private static float CalculateFusedQualityCap(
            IReadOnlyList<IADSRadarEvaluation> contributions)
        {
            var remainingUncertainty = 1f;
            foreach (var group in contributions
                         .GroupBy(contribution =>
                             string.IsNullOrWhiteSpace(
                                 contribution.FusionCorrelationGroup)
                                 ? contribution.RadarDefinitionId.ToString("N")
                                 : contribution.FusionCorrelationGroup,
                             StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var groupIndex = 0;
                foreach (var contribution in group
                             .OrderByDescending(item => item.QualityCap)
                             .ThenBy(item => item.SiteId)
                             .ThenBy(item => item.RadarComponentId))
                {
                    contribution.AppliedCapMultiplier = Mathf.Pow(
                        SimilarRadarCapDiminishingFactor,
                        groupIndex);
                    contribution.AdjustedQualityCap = Mathf.Clamp01(
                        contribution.QualityCap
                        * contribution.AppliedCapMultiplier);
                    remainingUncertainty *= 1f
                                            - contribution.AdjustedQualityCap;
                    groupIndex++;
                }
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

        private static ManeuverQualityAdjustment CalculateManeuverQualityAdjustment(
            IADSTrack track,
            AirFlight flight)
        {
            if (track == null || flight == null)
                return default;

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
            var headingPenalty = Mathf.Clamp01(
                headingChange * HeadingChangeQualityPenalty);
            return new ManeuverQualityAdjustment(
                headingChange,
                speedChange,
                altitudeChange,
                headingPenalty,
                0f,
                0f,
                headingPenalty);
        }

        private void RemoveInactiveTracks(
            IReadOnlyDictionary<Guid, AirFlight> activeHostileFlights,
            DateTime observedAt,
            ICollection<IADSTrackDiagnostic> diagnostics)
        {
            foreach (var track in Tracks
                         .Where(track => track != null
                                         && track.FlightId != Guid.Empty
                                         && !activeHostileFlights.ContainsKey(track.FlightId))
                         .ToList())
            {
                diagnostics.Add(new IADSTrackDiagnostic
                {
                    OccurredAt = observedAt,
                    ObserverAlliance = Alliance,
                    FlightId = track.FlightId,
                    TrackId = track.TrackId,
                    Event = IADSTrackDiagnosticEvent.Removed,
                    Reason = "flight_no_longer_active_hostile",
                    TruthPositionFeet = track.LastKnownPositionFeet,
                    TruthHeadingDegrees = track.EstimatedHeadingDegrees,
                    TruthSpeedKnots = track.EstimatedSpeedKnots,
                    HasTrackEstimate = true,
                    TrackPositionFeet = track.LastKnownPositionFeet,
                    TrackHeadingDegrees = track.EstimatedHeadingDegrees,
                    TrackSpeedKnots = track.EstimatedSpeedKnots,
                    EstimatedAircraftCount = track.EstimatedAircraftCount,
                    EstimatedAirCombatPower = track.EstimatedAirCombatPower,
                    PreviousQuality = track.Quality,
                    QualityAfterObservation = track.Quality,
                    NewQuality = track.Quality,
                    StaleSeconds = track.StaleSeconds,
                    WasEstablished = track.IsEstablished,
                    IsEstablished = track.IsEstablished,
                    WasStale = track.IsStale,
                    IsStale = track.IsStale,
                    HasIdentifiedAircraftType = track.HasIdentifiedAircraftType
                });
            }

            Tracks.RemoveAll(track => track == null
                                      || track.FlightId == Guid.Empty
                                      || !activeHostileFlights.ContainsKey(track.FlightId));
            RebuildIndex();
        }

        private IADSTrackDiagnostic CreateUntrackedDiagnostic(
            AirFlight flight,
            Guid aircraftTypeDefinitionId,
            float radarDetectability,
            int truthAircraftCount,
            float elapsedSeconds,
            DateTime observedAt,
            string reason,
            List<IADSRadarEvaluation> radarEvaluations)
        {
            return new IADSTrackDiagnostic
            {
                OccurredAt = observedAt,
                ObserverAlliance = Alliance,
                FlightId = flight.FlightId,
                AircraftTypeDefinitionId = aircraftTypeDefinitionId,
                Event = IADSTrackDiagnosticEvent.NotObserved,
                Reason = reason,
                ElapsedSeconds = Mathf.Max(0f, elapsedSeconds),
                TruthPositionFeet = flight.PositionFeet,
                TruthHeadingDegrees = flight.HeadingDegrees,
                TruthSpeedKnots = flight.SpeedKnots,
                TruthAircraftCount = truthAircraftCount,
                TargetRadarDetectability = radarDetectability,
                RadarEvaluations = radarEvaluations
            };
        }

        private void MarkTrackStale(
            IADSTrack track,
            AirFlight flight,
            Guid aircraftTypeDefinitionId,
            float radarDetectability,
            int truthAircraftCount,
            float elapsedSeconds,
            DateTime observedAt,
            string reason,
            List<IADSRadarEvaluation> radarEvaluations,
            ICollection<IADSTrackDiagnostic> diagnostics)
        {
            var previousQuality = track.Quality;
            var wasStale = track.IsStale;
            track.MarkStale(elapsedSeconds, StaleQualityDecayPerSecond);
            var expired = !track.IsEstablished
                          || track.StaleSeconds >= StaleExpirySeconds;
            diagnostics.Add(new IADSTrackDiagnostic
            {
                OccurredAt = observedAt,
                ObserverAlliance = Alliance,
                FlightId = track.FlightId,
                AircraftTypeDefinitionId = aircraftTypeDefinitionId,
                TrackId = track.TrackId,
                Event = expired
                    ? IADSTrackDiagnosticEvent.Expired
                    : wasStale
                        ? IADSTrackDiagnosticEvent.StaleUpdated
                        : IADSTrackDiagnosticEvent.Stale,
                Reason = expired
                    ? track.IsEstablished
                        ? "stale_expiry"
                        : "tentative_contact_lost"
                    : reason,
                ElapsedSeconds = Mathf.Max(0f, elapsedSeconds),
                TruthPositionFeet = flight?.PositionFeet ?? track.LastKnownPositionFeet,
                TruthHeadingDegrees = flight?.HeadingDegrees
                                      ?? track.EstimatedHeadingDegrees,
                TruthSpeedKnots = flight?.SpeedKnots ?? track.EstimatedSpeedKnots,
                HasTrackEstimate = true,
                TrackPositionFeet = track.LastKnownPositionFeet,
                TrackHeadingDegrees = track.EstimatedHeadingDegrees,
                TrackSpeedKnots = track.EstimatedSpeedKnots,
                TruthAircraftCount = truthAircraftCount,
                EstimatedAircraftCount = track.EstimatedAircraftCount,
                EstimatedAirCombatPower = track.EstimatedAirCombatPower,
                TargetRadarDetectability = radarDetectability,
                PreviousQuality = previousQuality,
                QualityAfterObservation = previousQuality,
                NewQuality = track.Quality,
                StaleQualityDecay = Mathf.Max(0f, previousQuality - track.Quality),
                StaleSeconds = track.StaleSeconds,
                WasEstablished = track.IsEstablished,
                IsEstablished = track.IsEstablished,
                WasStale = wasStale,
                IsStale = true,
                HasIdentifiedAircraftType = track.HasIdentifiedAircraftType,
                RadarEvaluations = radarEvaluations
            });

            if (!expired)
                return;

            Tracks.Remove(track);
            tracksByFlightId.Remove(track.FlightId);
        }

        private void MarkUnprocessedTracksStale(
            ISet<Guid> processedFlightIds,
            IReadOnlyDictionary<Guid, AirFlight> activeHostileFlights,
            float elapsedSeconds,
            DateTime observedAt,
            ICollection<IADSTrackDiagnostic> diagnostics)
        {
            foreach (var track in Tracks.ToList())
            {
                if (track == null || processedFlightIds.Contains(track.FlightId))
                    continue;

                activeHostileFlights.TryGetValue(track.FlightId, out var flight);
                MarkTrackStale(
                    track,
                    flight,
                    Guid.Empty,
                    0f,
                    flight?.AircraftIds.Count ?? track.EstimatedAircraftCount,
                    elapsedSeconds,
                    observedAt,
                    "track_not_processed",
                    new List<IADSRadarEvaluation>(),
                    diagnostics);
            }
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

        private readonly struct ManeuverQualityAdjustment
        {
            public readonly float HeadingChangeFraction;
            public readonly float SpeedChangeFraction;
            public readonly float AltitudeChangeFraction;
            public readonly float HeadingPenalty;
            public readonly float SpeedPenalty;
            public readonly float AltitudePenalty;
            public readonly float AppliedPenalty;

            public ManeuverQualityAdjustment(
                float headingChangeFraction,
                float speedChangeFraction,
                float altitudeChangeFraction,
                float headingPenalty,
                float speedPenalty,
                float altitudePenalty,
                float appliedPenalty)
            {
                HeadingChangeFraction = headingChangeFraction;
                SpeedChangeFraction = speedChangeFraction;
                AltitudeChangeFraction = altitudeChangeFraction;
                HeadingPenalty = headingPenalty;
                SpeedPenalty = speedPenalty;
                AltitudePenalty = altitudePenalty;
                AppliedPenalty = appliedPenalty;
            }
        }
    }
}
