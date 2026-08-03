using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Engine.Models;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    /// <summary>
    /// Writes a human and LLM readable record of what each flight did and why.
    /// One file per operational cadence period; each flight is written as one
    /// contiguous section when it physically ends, so a flight's whole story
    /// can be read in a single pass or reconstructed with a single grep on its
    /// label.
    ///
    /// The writer only ever creates and appends. It never deletes.
    /// </summary>
    public sealed class SimulationLogWriter
    {
        private const string RootFolderName = "Logs";
        private const string SimFolderName = "sim";
        private const float ReadableTrackQualityDelta = 0.05f;
        private static readonly TimeSpan ReadableTrackHeartbeat =
            TimeSpan.FromMinutes(1d);

        private readonly GameManager gameManager;
        private readonly AirTaskingSystem airTaskingSystem;
        private readonly IADSSystem iadsSystem;
        private readonly DateTime campaignStartTime;
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes;

        private readonly HashSet<Guid> writtenFlightIds = new HashSet<Guid>();
        private readonly HashSet<AirTaskingDiagnostic> writtenDiagnostics =
            new HashSet<AirTaskingDiagnostic>();
        private readonly HashSet<int> startedCycles = new HashSet<int>();
        private readonly Dictionary<Guid, string> flightLabels =
            new Dictionary<Guid, string>();
        private readonly Dictionary<Guid, List<IADSTrackDiagnostic>>
            trackDiagnosticsByFlightId =
                new Dictionary<Guid, List<IADSTrackDiagnostic>>();
        private readonly Dictionary<(Alliance Observer, Guid FlightId), IADSTrackDiagnostic>
            lastReadableTrackDiagnosticByContact =
                new Dictionary<(Alliance Observer, Guid FlightId), IADSTrackDiagnostic>();

        private readonly string runDirectory;

        public bool IsEnabled => runDirectory != null;
        public string RunDirectory => runDirectory;

        public SimulationLogWriter(
            GameManager gameManager,
            AirTaskingSystem airTaskingSystem,
            IADSSystem iadsSystem,
            ModuleDefinition module,
            DateTime campaignStartTime)
        {
            this.gameManager = gameManager;
            this.airTaskingSystem = airTaskingSystem;
            this.iadsSystem = iadsSystem;
            this.campaignStartTime = campaignStartTime;
            aircraftTypes = module.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            ordnanceTypes = module.OrdnanceTypeDefinitions
                .ToDictionary(definition => definition.OrdnanceTypeDefinitionId);
            runDirectory = TryCreateRunDirectory();
            if (runDirectory != null)
                WriteRunHeader(module);
        }

        /// <summary>
        /// Appends everything that became final since the previous turn. Safe to
        /// call every turn; nothing is written twice.
        /// </summary>
        public void OnTurnCompleted()
        {
            var trackDiagnostics = iadsSystem?.DrainTrackDiagnostics()
                                   ?? Array.Empty<IADSTrackDiagnostic>();
            if (!IsEnabled)
                return;

            try
            {
                RefreshFlightLabels();
                EnsureCycleStarted(GetCycleIndex(gameManager.CurrentTime));
                AppendTrackDiagnostics(trackDiagnostics);
                AppendTaskingDiagnostics();
                AppendEndedFlights();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Simulation log write failed: {exception.Message}");
            }
        }

        /// <summary>
        /// Writes any flight that has not ended yet, so stopping mid-sortie
        /// still produces a readable log.
        /// </summary>
        public void FlushIncompleteFlights()
        {
            var trackDiagnostics = iadsSystem?.DrainTrackDiagnostics()
                                   ?? Array.Empty<IADSTrackDiagnostic>();
            if (!IsEnabled)
                return;

            try
            {
                RefreshFlightLabels();
                EnsureCycleStarted(GetCycleIndex(gameManager.CurrentTime));
                AppendTrackDiagnostics(trackDiagnostics);
                var ordnanceByFlight = IndexOrdnanceRecordsByFlight();
                foreach (var package in airTaskingSystem.GetPackages().ToList())
                foreach (var flight in package.Flights)
                {
                    if (writtenFlightIds.Contains(flight.FlightId))
                        continue;
                    AppendFlightSection(
                        package,
                        flight,
                        ordnanceByFlight,
                        complete: false);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Simulation log flush failed: {exception.Message}");
            }
        }

        // ---------------------------------------------------------------
        // Run directory and header
        // ---------------------------------------------------------------

        private static string TryCreateRunDirectory()
        {
            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                    return null;

                var path = Path.Combine(
                    projectRoot,
                    RootFolderName,
                    SimFolderName,
                    $"run_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.CreateDirectory(path);
                return path;
            }
            catch (Exception exception)
            {
                // A built player may have no writable project root. Logging is a
                // development aid, so degrade to disabled rather than fail play.
                Debug.LogWarning(
                    $"Simulation logging disabled; could not create log directory: {exception.Message}");
                return null;
            }
        }

        private void WriteRunHeader(ModuleDefinition module)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"# Simulation run {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine();
            builder.AppendLine($"- Campaign: {gameManager.TemplateName}");
            builder.AppendLine($"- Module: {module.Name} ({module.Id})");
            builder.AppendLine(
                $"- Campaign start: {campaignStartTime:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine(
                $"- Tick: {gameManager.SimulationSettings.SimulationTickMinutes} min"
                + $" | Operational cadence: {gameManager.SimulationSettings.OperationalCadenceHours} h"
                + $" | Tile: {CampaignMapCoordinates.TileCenterSpacingKilometers} km");
            builder.AppendLine();
            builder.AppendLine("## Reading these logs");
            builder.AppendLine();
            builder.AppendLine(
                "One file per operational cadence period. Every event line is");
            builder.AppendLine(
                "`D<day> HH:MM:SS  <FLIGHT>  <CODE>  <detail>` and carries its own");
            builder.AppendLine(
                "flight label, so `grep BLU-3F2B8C cycle-*.md` reconstructs one");
            builder.AppendLine("flight's full story across every cycle, in order.");
            builder.AppendLine(
                "Each cycle also has a `_tracks.ndjson` file containing one complete");
            builder.AppendLine(
                "IADS observer/flight evaluation per tactical update, including every");
            builder.AppendLine(
                "contributing or rejected radar. Fields prefixed `truth` are diagnostic-only.");
            builder.AppendLine(
                "Maneuver change fractions describe observed changes; penalty fields state what");
            builder.AppendLine(
                "the current model actually applied, including explicit zero values.");
            builder.AppendLine();
            builder.AppendLine("### Event codes");
            builder.AppendLine();
            builder.AppendLine("| Code | Meaning |");
            builder.AppendLine("|------|---------|");
            builder.AppendLine("| `TASKED` | Flight created for a mission request |");
            builder.AppendLine("| `RATIONALE` | Why the mission was requested |");
            builder.AppendLine("| `PRIORITY` | Request priority and its components |");
            builder.AppendLine("| `DECIDE` | Tactical intent or maneuver changed |");
            builder.AppendLine("| `WAYPOINT` | Route progress or amendment |");
            builder.AppendLine("| `PREP` | Began preparing an ordnance pass |");
            builder.AppendLine("| `PREPABORT` | Ordnance pass cancelled before release |");
            builder.AppendLine("| `LAUNCH` | Ordnance released |");
            builder.AppendLine("| `EFFECT` | Ordnance effect resolved |");
            builder.AppendLine("| `NO_TRACK` | Observer has no contributing radar for the flight |");
            builder.AppendLine("| `ACQUIRE` | Tentative track is accumulating below establishment quality |");
            builder.AppendLine("| `TRACK+` | Track crossed the establishment threshold |");
            builder.AppendLine("| `TRACK` | Material or periodic fused-quality update |");
            builder.AppendLine("| `IDENT` | Track identified the aircraft type |");
            builder.AppendLine("| `STALE` | Observation was lost and quality is decaying |");
            builder.AppendLine("| `REACQUIRE` | A stale track regained radar observation |");
            builder.AppendLine("| `TRACK-` | Track expired or its flight ceased to be active |");
            builder.AppendLine("| `OUTCOME` | Final lifecycle state of the flight |");
            builder.AppendLine("| `TASKING` | Alliance-level planning event |");
            builder.AppendLine("| `TRUNCATED` | Early events dropped by the event cap |");
            builder.AppendLine();
            builder.AppendLine("### Enumerations");
            builder.AppendLine();
            AppendEnumLegend(builder, "Intent", typeof(AirCombatIntent));
            AppendEnumLegend(builder, "Maneuver", typeof(AirCombatManeuver));
            AppendEnumLegend(builder, "Phase", typeof(FlightExecutionPhase));
            AppendEnumLegend(builder, "Lifecycle", typeof(AirTaskingLifecycleState));
            AppendEnumLegend(builder, "Waypoint", typeof(AirWaypointAction));
            builder.AppendLine();
            builder.AppendLine("## Flights");
            builder.AppendLine();
            builder.AppendLine("| Flight | Mission | Outcome | Cycle |");
            builder.AppendLine("|--------|---------|---------|-------|");
            File.AppendAllText(RunFilePath, builder.ToString());
        }

        private static void AppendEnumLegend(
            StringBuilder builder,
            string label,
            Type enumType)
        {
            builder.AppendLine(
                $"- **{label}**: {string.Join(", ", Enum.GetNames(enumType))}");
        }

        private string RunFilePath => Path.Combine(runDirectory, "run.md");

        // ---------------------------------------------------------------
        // Cycle files
        // ---------------------------------------------------------------

        private int GetCycleIndex(DateTime time)
        {
            var hours = Math.Max(
                1,
                gameManager.SimulationSettings.OperationalCadenceHours);
            var cadenceTicks = TimeSpan.FromHours(hours).Ticks;
            var elapsed = (time - campaignStartTime).Ticks;
            return elapsed <= 0 ? 0 : (int)(elapsed / cadenceTicks);
        }

        private string GetCycleFilePath(int cycleIndex)
        {
            var hours = Math.Max(
                1,
                gameManager.SimulationSettings.OperationalCadenceHours);
            var cycleStart = campaignStartTime.AddHours((double)cycleIndex * hours);
            return Path.Combine(
                runDirectory,
                $"cycle-{cycleIndex:D3}_{cycleStart:yyyyMMdd-HHmm}.md");
        }

        private string GetTrackCycleFilePath(int cycleIndex)
        {
            var hours = Math.Max(
                1,
                gameManager.SimulationSettings.OperationalCadenceHours);
            var cycleStart = campaignStartTime.AddHours((double)cycleIndex * hours);
            return Path.Combine(
                runDirectory,
                $"cycle-{cycleIndex:D3}_{cycleStart:yyyyMMdd-HHmm}_tracks.ndjson");
        }

        private void EnsureCycleStarted(int cycleIndex)
        {
            if (!startedCycles.Add(cycleIndex))
                return;

            var hours = Math.Max(
                1,
                gameManager.SimulationSettings.OperationalCadenceHours);
            var cycleStart = campaignStartTime.AddHours((double)cycleIndex * hours);
            var builder = new StringBuilder();
            builder.AppendLine(
                $"# Cycle {cycleIndex:D3} — {cycleStart:yyyy-MM-dd HH:mm} "
                + $"(+{hours}h)");
            builder.AppendLine();
            File.AppendAllText(GetCycleFilePath(cycleIndex), builder.ToString());
        }

        // ---------------------------------------------------------------
        // IADS track diagnostics
        // ---------------------------------------------------------------

        private void AppendTrackDiagnostics(
            IReadOnlyList<IADSTrackDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
                return;

            foreach (var cycleGroup in diagnostics
                         .Where(item => item != null)
                         .OrderBy(item => item.OccurredAt)
                         .ThenBy(item => item.ObserverAlliance)
                         .ThenBy(item => item.FlightId)
                         .GroupBy(item => GetCycleIndex(item.OccurredAt)))
            {
                EnsureCycleStarted(cycleGroup.Key);
                var raw = new StringBuilder();
                foreach (var diagnostic in cycleGroup)
                {
                    raw.AppendLine(FormatTrackDiagnosticJson(diagnostic));
                    if (writtenFlightIds.Contains(diagnostic.FlightId)
                        || !ShouldRetainReadableTrackDiagnostic(diagnostic))
                        continue;

                    if (!trackDiagnosticsByFlightId.TryGetValue(
                            diagnostic.FlightId,
                            out var flightDiagnostics))
                    {
                        flightDiagnostics = new List<IADSTrackDiagnostic>();
                        trackDiagnosticsByFlightId[diagnostic.FlightId] =
                            flightDiagnostics;
                    }

                    flightDiagnostics.Add(diagnostic);
                }

                File.AppendAllText(GetTrackCycleFilePath(cycleGroup.Key), raw.ToString());
            }
        }

        private bool ShouldRetainReadableTrackDiagnostic(
            IADSTrackDiagnostic diagnostic)
        {
            var key = (diagnostic.ObserverAlliance, diagnostic.FlightId);
            lastReadableTrackDiagnosticByContact.TryGetValue(key, out var previous);
            var isContinuousUpdate = diagnostic.Event == IADSTrackDiagnosticEvent.Updated
                                     || diagnostic.Event
                                     == IADSTrackDiagnosticEvent.TentativeUpdated
                                     || diagnostic.Event
                                     == IADSTrackDiagnosticEvent.StaleUpdated
                                     || diagnostic.Event
                                     == IADSTrackDiagnosticEvent.NotObserved;
            var retain = !isContinuousUpdate
                         || previous == null
                         || previous.Event != diagnostic.Event
                         || Math.Abs(diagnostic.NewQuality - previous.NewQuality)
                         >= ReadableTrackQualityDelta
                         || diagnostic.OccurredAt - previous.OccurredAt
                         >= ReadableTrackHeartbeat
                         || ContributingRadarSetChanged(previous, diagnostic);
            if (retain)
                lastReadableTrackDiagnosticByContact[key] = diagnostic;
            return retain;
        }

        private static bool ContributingRadarSetChanged(
            IADSTrackDiagnostic previous,
            IADSTrackDiagnostic current)
        {
            var previousIds = (previous.RadarEvaluations
                               ?? new List<IADSRadarEvaluation>())
                .Where(item => item.Contributed)
                .Select(item => item.RadarComponentId)
                .OrderBy(id => id);
            var currentIds = (current.RadarEvaluations
                              ?? new List<IADSRadarEvaluation>())
                .Where(item => item.Contributed)
                .Select(item => item.RadarComponentId)
                .OrderBy(id => id);
            return !previousIds.SequenceEqual(currentIds);
        }

        private string FormatTrackDiagnosticJson(IADSTrackDiagnostic diagnostic)
        {
            var builder = new StringBuilder();
            builder.Append('{');
            AppendJsonStringProperty(builder, "schema", "hzpl.iads-track.v1");
            AppendJsonStringProperty(
                builder,
                "at",
                diagnostic.OccurredAt.ToString(
                    "yyyy-MM-ddTHH:mm:ss.fff",
                    CultureInfo.InvariantCulture));
            AppendJsonStringProperty(
                builder,
                "event",
                ToSnakeCase(diagnostic.Event.ToString()));
            AppendJsonStringProperty(
                builder,
                "reason",
                diagnostic.Reason ?? string.Empty);
            AppendJsonStringProperty(
                builder,
                "observer",
                diagnostic.ObserverAlliance.ToString());
            AppendJsonStringProperty(
                builder,
                "flight",
                ResolveFlightLabel(diagnostic.FlightId));
            AppendJsonGuidProperty(builder, "flight_id", diagnostic.FlightId);
            AppendJsonGuidProperty(builder, "track_id", diagnostic.TrackId);
            AppendJsonGuidProperty(
                builder,
                "aircraft_type_definition_id",
                diagnostic.AircraftTypeDefinitionId);
            AppendJsonFloatProperty(builder, "elapsed_seconds", diagnostic.ElapsedSeconds);

            builder.Append("\"truth\":{");
            builder.Append("\"position_feet\":");
            AppendJsonVector(builder, diagnostic.TruthPositionFeet);
            builder.Append(',');
            AppendJsonFloatProperty(
                builder,
                "altitude_feet",
                diagnostic.TruthPositionFeet.y);
            AppendJsonFloatProperty(
                builder,
                "heading_degrees",
                diagnostic.TruthHeadingDegrees);
            AppendJsonFloatProperty(builder, "speed_knots", diagnostic.TruthSpeedKnots, false);
            builder.Append("},");

            builder.Append("\"track_estimate\":");
            if (diagnostic.HasTrackEstimate)
            {
                builder.Append('{');
                builder.Append("\"position_feet\":");
                AppendJsonVector(builder, diagnostic.TrackPositionFeet);
                builder.Append(',');
                AppendJsonFloatProperty(
                    builder,
                    "altitude_feet",
                    diagnostic.TrackPositionFeet.y);
                AppendJsonFloatProperty(
                    builder,
                    "heading_degrees",
                    diagnostic.TrackHeadingDegrees);
                AppendJsonFloatProperty(
                    builder,
                    "speed_knots",
                    diagnostic.TrackSpeedKnots);
                AppendJsonFloatProperty(
                    builder,
                    "position_error_km",
                    Vector3.Distance(
                        diagnostic.TruthPositionFeet,
                        diagnostic.TrackPositionFeet)
                    / AirspaceGeometry.FeetPerKilometer,
                    false);
                builder.Append('}');
            }
            else
            {
                builder.Append("null");
            }
            builder.Append(',');

            builder.Append("\"strength\":{");
            AppendJsonNullableIntProperty(
                builder,
                "truth_aircraft_count",
                diagnostic.TruthAircraftCount);
            AppendJsonNullableIntProperty(
                builder,
                "estimated_aircraft_count",
                diagnostic.EstimatedAircraftCount);
            AppendJsonFloatProperty(
                builder,
                "estimated_air_combat_power",
                diagnostic.EstimatedAirCombatPower,
                false);
            builder.Append("},");

            builder.Append("\"quality\":{");
            AppendJsonFloatProperty(builder, "before", diagnostic.PreviousQuality);
            AppendJsonFloatProperty(
                builder,
                "after_observation",
                diagnostic.QualityAfterObservation);
            AppendJsonFloatProperty(builder, "after", diagnostic.NewQuality);
            AppendJsonFloatProperty(builder, "fused_cap", diagnostic.FusedQualityCap);
            AppendJsonFloatProperty(
                builder,
                "diminished_build",
                diagnostic.DiminishedQualityIncrease);
            AppendJsonFloatProperty(
                builder,
                "observed_excess_decay",
                diagnostic.ObservedExcessQualityDecay);
            AppendJsonFloatProperty(
                builder,
                "stale_decay",
                diagnostic.StaleQualityDecay);
            AppendJsonFloatProperty(
                builder,
                "applied_maneuver_penalty",
                diagnostic.AppliedManeuverQualityPenalty,
                false);
            builder.Append("},");

            builder.Append("\"maneuver\":{");
            AppendJsonFloatProperty(
                builder,
                "heading_change_fraction",
                diagnostic.HeadingChangeFraction);
            AppendJsonFloatProperty(
                builder,
                "speed_change_fraction",
                diagnostic.SpeedChangeFraction);
            AppendJsonFloatProperty(
                builder,
                "altitude_change_fraction",
                diagnostic.AltitudeChangeFraction);
            AppendJsonFloatProperty(
                builder,
                "heading_penalty",
                diagnostic.HeadingQualityPenalty);
            AppendJsonFloatProperty(
                builder,
                "speed_penalty",
                diagnostic.SpeedQualityPenalty);
            AppendJsonFloatProperty(
                builder,
                "altitude_penalty",
                diagnostic.AltitudeQualityPenalty,
                false);
            builder.Append("},");

            builder.Append("\"state\":{");
            AppendJsonBooleanProperty(
                builder,
                "was_established",
                diagnostic.WasEstablished);
            AppendJsonBooleanProperty(
                builder,
                "is_established",
                diagnostic.IsEstablished);
            AppendJsonBooleanProperty(builder, "was_stale", diagnostic.WasStale);
            AppendJsonBooleanProperty(builder, "is_stale", diagnostic.IsStale);
            AppendJsonBooleanProperty(
                builder,
                "became_established",
                diagnostic.BecameEstablished);
            AppendJsonBooleanProperty(
                builder,
                "became_identified",
                diagnostic.BecameIdentified);
            AppendJsonBooleanProperty(
                builder,
                "has_identified_aircraft_type",
                diagnostic.HasIdentifiedAircraftType);
            AppendJsonFloatProperty(builder, "stale_seconds", diagnostic.StaleSeconds, false);
            builder.Append("},");

            builder.Append("\"thresholds\":{");
            AppendJsonFloatProperty(
                builder,
                "creation_quality",
                diagnostic.CreationQualityThreshold);
            AppendJsonFloatProperty(
                builder,
                "identification_quality",
                diagnostic.IdentificationQualityThreshold,
                false);
            builder.Append("},");

            AppendJsonFloatProperty(
                builder,
                "target_radar_detectability",
                diagnostic.TargetRadarDetectability);
            builder.Append("\"radars\":[");
            var radars = diagnostic.RadarEvaluations
                         ?? new List<IADSRadarEvaluation>();
            for (var index = 0; index < radars.Count; index++)
            {
                if (index > 0)
                    builder.Append(',');
                AppendRadarEvaluationJson(builder, radars[index]);
            }

            builder.Append("]}");
            return builder.ToString();
        }

        private static void AppendRadarEvaluationJson(
            StringBuilder builder,
            IADSRadarEvaluation radar)
        {
            builder.Append('{');
            AppendJsonGuidProperty(builder, "site_id", radar.SiteId);
            AppendJsonStringProperty(
                builder,
                "site",
                SimLogNames.SiteLabel(radar.SiteId));
            AppendJsonGuidProperty(
                builder,
                "radar_component_id",
                radar.RadarComponentId);
            AppendJsonStringProperty(
                builder,
                "radar",
                $"RDR-{SimLogNames.ShortId(radar.RadarComponentId)}");
            AppendJsonGuidProperty(
                builder,
                "radar_definition_id",
                radar.RadarDefinitionId);
            AppendJsonStringProperty(builder, "radar_name", radar.RadarName ?? string.Empty);
            AppendJsonStringProperty(
                builder,
                "result",
                ToSnakeCase(radar.Result.ToString()));
            AppendJsonBooleanProperty(builder, "contributed", radar.Contributed);
            builder.Append("\"site_position_feet\":");
            if (radar.HasSitePosition)
                AppendJsonVector(builder, radar.SitePositionFeet);
            else
                builder.Append("null");
            builder.Append(',');
            AppendJsonFloatProperty(
                builder,
                "radar_antenna_height_meters",
                radar.RadarAntennaHeightMeters);
            AppendJsonFloatProperty(
                builder,
                "radar_altitude_meters",
                radar.RadarAltitudeMeters);
            AppendJsonFloatProperty(
                builder,
                "horizontal_distance_km",
                radar.HorizontalDistanceKm);
            AppendJsonFloatProperty(builder, "distance_km", radar.DistanceKm);
            AppendJsonFloatProperty(builder, "maximum_range_km", radar.MaximumRangeKm);
            AppendJsonFloatProperty(builder, "authored_range_km", radar.MaximumRangeKm);
            AppendJsonFloatProperty(
                builder,
                "detectability_adjusted_range_km",
                radar.DetectabilityAdjustedRangeKm);
            AppendJsonFloatProperty(
                builder,
                "radar_horizon_km",
                radar.RadarHorizonKm);
            AppendJsonFloatProperty(builder, "distance_fraction", radar.DistanceFraction);
            AppendJsonFloatProperty(
                builder,
                "radar_horizon_fraction",
                radar.RadarHorizonFraction);
            AppendJsonFloatProperty(builder, "range_margin_km", radar.RangeMarginKm);
            AppendJsonFloatProperty(
                builder,
                "radar_horizon_margin_km",
                radar.RadarHorizonMarginKm);
            AppendJsonStringProperty(
                builder,
                "limiting_constraint",
                ToSnakeCase(radar.LimitingConstraint.ToString()));
            AppendJsonFloatProperty(
                builder,
                "target_altitude_feet",
                radar.TargetAltitudeFeet);
            AppendJsonFloatProperty(
                builder,
                "maximum_altitude_feet",
                radar.MaximumAltitudeFeet);
            AppendJsonFloatProperty(
                builder,
                "altitude_margin_feet",
                radar.AltitudeMarginFeet);
            AppendJsonFloatProperty(
                builder,
                "radar_track_quality",
                radar.RadarTrackQuality);
            AppendJsonFloatProperty(
                builder,
                "target_detectability",
                radar.TargetDetectability);
            AppendJsonStringProperty(
                builder,
                "fusion_correlation_group",
                radar.FusionCorrelationGroup ?? string.Empty);
            AppendJsonFloatProperty(builder, "range_factor", radar.RangeFactor);
            AppendJsonFloatProperty(builder, "quality_cap", radar.QualityCap);
            AppendJsonFloatProperty(
                builder,
                "applied_cap_multiplier",
                radar.AppliedCapMultiplier);
            AppendJsonFloatProperty(
                builder,
                "adjusted_quality_cap",
                radar.AdjustedQualityCap);
            AppendJsonFloatProperty(
                builder,
                "raw_quality_increase",
                radar.RawQualityIncrease);
            AppendJsonFloatProperty(
                builder,
                "applied_build_multiplier",
                radar.AppliedBuildMultiplier);
            AppendJsonFloatProperty(
                builder,
                "applied_quality_increase",
                radar.AppliedQualityIncrease,
                false);
            builder.Append('}');
        }

        private static void AppendJsonStringProperty(
            StringBuilder builder,
            string name,
            string value,
            bool trailingComma = true)
        {
            AppendJsonString(builder, name);
            builder.Append(':');
            AppendJsonString(builder, value);
            if (trailingComma)
                builder.Append(',');
        }

        private static void AppendJsonGuidProperty(
            StringBuilder builder,
            string name,
            Guid value,
            bool trailingComma = true)
        {
            AppendJsonStringProperty(
                builder,
                name,
                value == Guid.Empty ? string.Empty : value.ToString("N"),
                trailingComma);
        }

        private static void AppendJsonFloatProperty(
            StringBuilder builder,
            string name,
            float value,
            bool trailingComma = true)
        {
            AppendJsonString(builder, name);
            builder.Append(':');
            AppendJsonNumber(builder, value);
            if (trailingComma)
                builder.Append(',');
        }

        private static void AppendJsonBooleanProperty(
            StringBuilder builder,
            string name,
            bool value,
            bool trailingComma = true)
        {
            AppendJsonString(builder, name);
            builder.Append(value ? ":true" : ":false");
            if (trailingComma)
                builder.Append(',');
        }

        private static void AppendJsonNullableIntProperty(
            StringBuilder builder,
            string name,
            int value,
            bool trailingComma = true)
        {
            AppendJsonString(builder, name);
            builder.Append(':');
            if (value < 0)
                builder.Append("null");
            else
                builder.Append(value.ToString(CultureInfo.InvariantCulture));
            if (trailingComma)
                builder.Append(',');
        }

        private static void AppendJsonVector(StringBuilder builder, Vector3 value)
        {
            builder.Append('{');
            AppendJsonFloatProperty(builder, "x", value.x);
            AppendJsonFloatProperty(builder, "y", value.y);
            AppendJsonFloatProperty(builder, "z", value.z, false);
            builder.Append('}');
        }

        private static void AppendJsonNumber(StringBuilder builder, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                builder.Append("null");
                return;
            }

            builder.Append(value.ToString("0.#####", CultureInfo.InvariantCulture));
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
            builder.Append('"');
        }

        private static string ToSnakeCase(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length + 4);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (index > 0 && char.IsUpper(character))
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(character));
            }

            return builder.ToString();
        }

        // ---------------------------------------------------------------
        // Tasking diagnostics
        // ---------------------------------------------------------------

        private void AppendTaskingDiagnostics()
        {
            var builder = new StringBuilder();
            var live = new HashSet<AirTaskingDiagnostic>();
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var commander = airTaskingSystem.GetCommander(alliance);
                if (commander == null)
                    continue;

                foreach (var diagnostic in commander.Diagnostics)
                {
                    live.Add(diagnostic);
                    if (!writtenDiagnostics.Add(diagnostic))
                        continue;

                    builder.Append(FormatTime(diagnostic.RecordedAt));
                    builder.Append("  ");
                    builder.Append(SimLogNames.AllianceCode(alliance).PadRight(10));
                    builder.Append("  TASKING    ");
                    builder.Append(diagnostic.Code);
                    builder.Append("  ");
                    builder.Append(SimLogNames.RequestLabel(diagnostic.MissionRequestId));
                    if (diagnostic.PackageId != Guid.Empty)
                    {
                        builder.Append(' ');
                        builder.Append(SimLogNames.PackageLabel(diagnostic.PackageId));
                    }

                    var message = SimLogNames.SingleLine(diagnostic.Message);
                    if (message.Length > 0)
                    {
                        builder.Append("  ");
                        builder.Append(message);
                    }

                    AppendValues(builder, diagnostic.Values);
                    builder.AppendLine();
                }
            }

            // Diagnostics are trimmed by the commander, so drop anything that has
            // aged out to keep the seen-set bounded by the commanders' own caps.
            writtenDiagnostics.IntersectWith(live);

            if (builder.Length > 0)
            {
                File.AppendAllText(
                    GetCycleFilePath(GetCycleIndex(gameManager.CurrentTime)),
                    builder.ToString());
            }
        }

        private static void AppendValues(
            StringBuilder builder,
            IReadOnlyDictionary<string, float> values)
        {
            if (values == null || values.Count == 0)
                return;

            foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                builder.Append("  ");
                builder.Append(pair.Key);
                builder.Append('=');
                builder.Append(pair.Value.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        // ---------------------------------------------------------------
        // Flight sections
        // ---------------------------------------------------------------

        private void RefreshFlightLabels()
        {
            foreach (var package in airTaskingSystem.GetPackages())
            foreach (var flight in package.Flights)
            {
                flightLabels[flight.FlightId] =
                    SimLogNames.FlightLabel(package.Alliance, flight.FlightId);
            }
        }

        private void AppendEndedFlights()
        {
            var pending = airTaskingSystem.GetPackages()
                .ToList()
                .SelectMany(
                    package => package.Flights.Select(flight => (package, flight)))
                .Where(entry => entry.flight.HasPhysicallyEnded
                                && !writtenFlightIds.Contains(entry.flight.FlightId))
                .ToList();
            if (pending.Count == 0)
                return;

            var ordnanceByFlight = IndexOrdnanceRecordsByFlight();
            foreach (var (package, flight) in pending)
                AppendFlightSection(package, flight, ordnanceByFlight, complete: true);
        }

        /// <summary>
        /// Groups the employment record list by participating flight once per
        /// write pass. Scanning it per flight would be quadratic against the
        /// system's 5000 record retention.
        /// </summary>
        private Dictionary<Guid, List<OrdnanceEmploymentRecord>>
            IndexOrdnanceRecordsByFlight()
        {
            var index = new Dictionary<Guid, List<OrdnanceEmploymentRecord>>();
            foreach (var record in gameManager.GetOrdnanceEmploymentRecords())
            {
                AddOrdnanceRecord(index, record.SourceFlightId, record);
                if (record.TargetFlightId != record.SourceFlightId)
                    AddOrdnanceRecord(index, record.TargetFlightId, record);
            }

            return index;
        }

        private static void AddOrdnanceRecord(
            IDictionary<Guid, List<OrdnanceEmploymentRecord>> index,
            Guid flightId,
            OrdnanceEmploymentRecord record)
        {
            if (flightId == Guid.Empty)
                return;
            if (!index.TryGetValue(flightId, out var records))
            {
                records = new List<OrdnanceEmploymentRecord>();
                index[flightId] = records;
            }

            records.Add(record);
        }

        private void AppendFlightSection(
            AirPackage package,
            AirFlight flight,
            IReadOnlyDictionary<Guid, List<OrdnanceEmploymentRecord>> ordnanceByFlight,
            bool complete)
        {
            writtenFlightIds.Add(flight.FlightId);

            var label = SimLogNames.FlightLabel(package.Alliance, flight.FlightId);
            var squadronName = ResolveSquadronName(flight.SquadronId);
            var aircraftName = ResolveAircraftName(flight.SquadronId);
            var request = FindRequest(package);

            var builder = new StringBuilder();
            builder.AppendLine();
            builder.AppendLine(
                $"## {label} · {aircraftName} ×{flight.AircraftIds.Count} · "
                + $"{squadronName} · {package.Alliance}");
            builder.AppendLine();

            AppendLine(
                builder,
                flight.PlannedTakeoffTime,
                label,
                "TASKED",
                $"{flight.MissionType}  role={flight.Role}  "
                + $"{SimLogNames.PackageLabel(package.PackageId)} "
                + $"{SimLogNames.RequestLabel(package.MissionRequestId)}");

            var rationale = SimLogNames.SingleLine(
                !string.IsNullOrWhiteSpace(request?.Rationale)
                    ? request.Rationale
                    : package.Rationale);
            if (rationale.Length > 0)
            {
                AppendLine(
                    builder,
                    flight.PlannedTakeoffTime,
                    label,
                    "RATIONALE",
                    rationale);
            }

            if (request != null)
            {
                var priority = new StringBuilder(
                    request.Priority.ToString("0.###", CultureInfo.InvariantCulture));
                AppendValues(priority, request.PriorityComponents);
                AppendLine(
                    builder,
                    flight.PlannedTakeoffTime,
                    label,
                    "PRIORITY",
                    priority.ToString());
            }

            if (flight.DroppedExecutionEventCount > 0)
            {
                AppendLine(
                    builder,
                    flight.PlannedTakeoffTime,
                    label,
                    "TRUNCATED",
                    $"{flight.DroppedExecutionEventCount} earlier events dropped "
                    + "by the execution event cap");
            }

            foreach (var line in BuildTimeline(flight, label, ordnanceByFlight))
                builder.AppendLine(line);

            AppendLine(
                builder,
                gameManager.CurrentTime,
                label,
                "OUTCOME",
                complete
                    ? $"{flight.LifecycleState}  phase={flight.ExecutionPhase}"
                    : $"IN-PROGRESS  lifecycle={flight.LifecycleState} "
                      + $"phase={flight.ExecutionPhase}");

            File.AppendAllText(
                GetCycleFilePath(GetCycleIndex(gameManager.CurrentTime)),
                builder.ToString());

            AppendIndexRow(
                label,
                flight.MissionType.ToString(),
                complete ? flight.LifecycleState.ToString() : "IN-PROGRESS");
            trackDiagnosticsByFlightId.Remove(flight.FlightId);
            foreach (var key in lastReadableTrackDiagnosticByContact.Keys
                         .Where(key => key.FlightId == flight.FlightId)
                         .ToList())
            {
                lastReadableTrackDiagnosticByContact.Remove(key);
            }
        }

        /// <summary>
        /// Merges flight execution, observer track, and ordnance records ordered
        /// by occurrence so movement reads before sensing and sensing before fire.
        /// </summary>
        private List<string> BuildTimeline(
            AirFlight flight,
            string label,
            IReadOnlyDictionary<Guid, List<OrdnanceEmploymentRecord>> ordnanceByFlight)
        {
            var entries = new List<(DateTime At, string Line)>();

            foreach (var executionEvent in flight.ExecutionEvents)
            {
                var code = string.IsNullOrWhiteSpace(executionEvent.Code)
                    ? ResolveWaypointCode(executionEvent.Action)
                    : executionEvent.Code;
                var detail = SimLogNames.SingleLine(executionEvent.Detail);
                if (code == "WAYPOINT")
                {
                    detail = detail.Length > 0
                        ? $"{executionEvent.Action}  {detail}"
                        : executionEvent.Action.ToString();
                }

                entries.Add((
                    executionEvent.OccurredAt,
                    FormatLine(executionEvent.OccurredAt, label, code, detail)));
            }

            if (trackDiagnosticsByFlightId.TryGetValue(
                    flight.FlightId,
                    out var trackDiagnostics))
            {
                foreach (var diagnostic in trackDiagnostics)
                {
                    entries.Add((
                        diagnostic.OccurredAt,
                        FormatLine(
                            diagnostic.OccurredAt,
                            label,
                            ResolveTrackCode(diagnostic.Event),
                            FormatTrackDetail(diagnostic))));
                }
            }

            if (!ordnanceByFlight.TryGetValue(flight.FlightId, out var records))
                records = new List<OrdnanceEmploymentRecord>();

            foreach (var record in records)
            {
                entries.Add((
                    record.OccurredAt,
                    FormatLine(
                        record.OccurredAt,
                        label,
                        ResolveOrdnanceCode(record.Stage),
                        FormatOrdnanceDetail(record, flight.FlightId))));
            }

            return entries
                .OrderBy(entry => entry.At)
                .Select(entry => entry.Line)
                .ToList();
        }

        private static string FormatTrackDetail(IADSTrackDiagnostic diagnostic)
        {
            var radars = diagnostic.RadarEvaluations
                         ?? new List<IADSRadarEvaluation>();
            var contributors = radars
                .Where(item => item.Contributed)
                .OrderByDescending(item => item.AppliedQualityIncrease)
                .ThenBy(item => item.SiteId)
                .ThenBy(item => item.RadarComponentId)
                .ToList();
            var builder = new StringBuilder();
            builder.Append("observer=");
            builder.Append(SimLogNames.AllianceCode(diagnostic.ObserverAlliance));
            if (diagnostic.TrackId != Guid.Empty)
            {
                builder.Append("  track=TRK-");
                builder.Append(SimLogNames.ShortId(diagnostic.TrackId));
            }
            builder.Append("  event=");
            builder.Append(ToSnakeCase(diagnostic.Event.ToString()));
            builder.Append("  q=");
            builder.Append(FormatCompactFloat(diagnostic.PreviousQuality));
            builder.Append("->");
            builder.Append(FormatCompactFloat(diagnostic.NewQuality));
            if (contributors.Count > 0)
            {
                builder.Append("  cap=");
                builder.Append(FormatCompactFloat(diagnostic.FusedQualityCap));
                builder.Append("  build=+");
                builder.Append(FormatCompactFloat(diagnostic.DiminishedQualityIncrease));
            }
            if (diagnostic.AppliedManeuverQualityPenalty > 0f)
            {
                builder.Append("  maneuver=-");
                builder.Append(FormatCompactFloat(
                    diagnostic.AppliedManeuverQualityPenalty));
            }
            if (diagnostic.StaleQualityDecay > 0f)
            {
                builder.Append("  stale_decay=-");
                builder.Append(FormatCompactFloat(diagnostic.StaleQualityDecay));
            }
            builder.Append("  sensors=");
            builder.Append(contributors.Count);
            builder.Append('/');
            builder.Append(radars.Count);
            builder.Append("  pos_km=(");
            builder.Append(FormatCompactFloat(
                diagnostic.TruthPositionFeet.x / AirspaceGeometry.FeetPerKilometer));
            builder.Append(',');
            builder.Append(FormatCompactFloat(
                diagnostic.TruthPositionFeet.z / AirspaceGeometry.FeetPerKilometer));
            builder.Append(")");
            builder.Append("  alt_ft=");
            builder.Append(diagnostic.TruthPositionFeet.y.ToString(
                "0",
                CultureInfo.InvariantCulture));
            if (diagnostic.TruthAircraftCount >= 0)
            {
                builder.Append("  aircraft=");
                builder.Append(diagnostic.TruthAircraftCount);
            }
            if (diagnostic.EstimatedAircraftCount >= 0)
            {
                builder.Append("  tracked_aircraft=");
                builder.Append(diagnostic.EstimatedAircraftCount);
            }
            if (diagnostic.HasTrackEstimate)
            {
                builder.Append("  err_km=");
                builder.Append(FormatCompactFloat(
                    Vector3.Distance(
                        diagnostic.TruthPositionFeet,
                        diagnostic.TrackPositionFeet)
                    / AirspaceGeometry.FeetPerKilometer));
            }

            if (contributors.Count > 0)
            {
                var top = contributors[0];
                builder.Append("  top=");
                builder.Append(SimLogNames.SiteLabel(top.SiteId));
                builder.Append("/RDR-");
                builder.Append(SimLogNames.ShortId(top.RadarComponentId));
                if (!string.IsNullOrWhiteSpace(top.RadarName))
                {
                    builder.Append('(');
                    builder.Append(SimLogNames.SingleLine(top.RadarName));
                    builder.Append(')');
                }
                builder.Append("  range_km=");
                builder.Append(FormatCompactFloat(top.DistanceKm));
                builder.Append("  top_cap=");
                builder.Append(FormatCompactFloat(top.AdjustedQualityCap));
                if (top.AppliedCapMultiplier < 1f)
                {
                    builder.Append("(raw=");
                    builder.Append(FormatCompactFloat(top.QualityCap));
                    builder.Append(",mult=");
                    builder.Append(FormatCompactFloat(top.AppliedCapMultiplier));
                    builder.Append(')');
                }
                builder.Append("  top_build=+");
                builder.Append(FormatCompactFloat(top.AppliedQualityIncrease));
            }
            else
            {
                var nearest = radars
                    .Where(item => item.DistanceKm >= 0f)
                    .OrderBy(item => item.DistanceKm)
                    .ThenBy(item => item.SiteId)
                    .ThenBy(item => item.RadarComponentId)
                    .FirstOrDefault();
                if (nearest != null)
                {
                    builder.Append("  nearest=");
                    builder.Append(SimLogNames.SiteLabel(nearest.SiteId));
                    builder.Append("/RDR-");
                    builder.Append(SimLogNames.ShortId(nearest.RadarComponentId));
                    builder.Append("  nearest_result=");
                    builder.Append(ToSnakeCase(nearest.Result.ToString()));
                    builder.Append("  range_km=");
                    builder.Append(FormatCompactFloat(nearest.DistanceKm));
                    builder.Append('/');
                    builder.Append(FormatCompactFloat(
                        nearest.DetectabilityAdjustedRangeKm));
                    builder.Append("  range_margin_km=");
                    builder.Append(FormatCompactFloat(nearest.RangeMarginKm));
                    builder.Append("  horizon_km=");
                    builder.Append(FormatCompactFloat(nearest.RadarHorizonKm));
                    builder.Append("  horizon_margin_km=");
                    builder.Append(FormatCompactFloat(
                        nearest.RadarHorizonMarginKm));
                }
            }

            var rejectionSummary = radars
                .Where(item => !item.Contributed)
                .GroupBy(item => item.Result)
                .OrderBy(group => group.Key)
                .Select(group => $"{ToSnakeCase(group.Key.ToString())}:{group.Count()}")
                .ToList();
            if (rejectionSummary.Count > 0)
            {
                builder.Append("  rejected=");
                builder.Append(string.Join(",", rejectionSummary));
            }
            if (!string.IsNullOrWhiteSpace(diagnostic.Reason))
            {
                builder.Append("  reason=");
                builder.Append(SimLogNames.SingleLine(diagnostic.Reason));
            }
            if (diagnostic.BecameIdentified)
                builder.Append("  identified=true");

            return builder.ToString();
        }

        private static string FormatCompactFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private string FormatOrdnanceDetail(
            OrdnanceEmploymentRecord record,
            Guid flightId)
        {
            var builder = new StringBuilder();
            var ordnanceName = ordnanceTypes.TryGetValue(
                record.OrdnanceTypeDefinitionId,
                out var ordnance)
                ? ordnance.Name
                : SimLogNames.ShortId(record.OrdnanceTypeDefinitionId);

            var incoming = record.TargetFlightId == flightId
                           && record.SourceFlightId != flightId;
            builder.Append(incoming ? "INBOUND " : string.Empty);
            if (record.Quantity > 0)
            {
                builder.Append(record.Quantity);
                builder.Append("x ");
            }

            builder.Append(ordnanceName);
            builder.Append(incoming ? "  from " : "  -> ");
            builder.Append(incoming
                ? DescribeSource(record)
                : DescribeTarget(record));

            if (record.HitProbability > 0f)
            {
                builder.Append("  pk=");
                builder.Append(record.HitProbability.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture));
            }

            if (record.ReleaseRangeKm > 0f)
            {
                builder.Append("  rng=");
                builder.Append(record.ReleaseRangeKm.ToString(
                    "0.#",
                    CultureInfo.InvariantCulture));
                builder.Append("km");
            }

            if (record.Shots is { Count: > 0 })
            {
                builder.Append("  shots=");
                builder.Append(string.Join(
                    ",",
                    record.Shots.Select(shot => shot.Result.ToString())));
            }

            var detail = SimLogNames.SingleLine(record.Detail);
            if (detail.Length > 0)
            {
                builder.Append("  ");
                builder.Append(detail);
            }

            return builder.ToString();
        }

        private string DescribeTarget(OrdnanceEmploymentRecord record)
        {
            if (record.TargetFlightId != Guid.Empty)
                return ResolveFlightLabel(record.TargetFlightId);
            if (record.TargetSiteId != Guid.Empty)
                return SimLogNames.SiteLabel(record.TargetSiteId);
            var ground = SimLogNames.SingleLine(record.GroundOpportunityDescription);
            return ground.Length > 0 ? ground : "unknown target";
        }

        private string DescribeSource(OrdnanceEmploymentRecord record)
        {
            if (record.SourceFlightId != Guid.Empty)
                return ResolveFlightLabel(record.SourceFlightId);
            if (record.SourceSiteId != Guid.Empty)
                return SimLogNames.SiteLabel(record.SourceSiteId);
            return "unknown source";
        }

        private string ResolveFlightLabel(Guid flightId)
        {
            return flightLabels.TryGetValue(flightId, out var label)
                ? label
                : $"???-{SimLogNames.ShortId(flightId)}";
        }

        private static string ResolveWaypointCode(AirWaypointAction action)
        {
            return action switch
            {
                AirWaypointAction.Takeoff => "TAKEOFF",
                AirWaypointAction.Land => "LAND",
                AirWaypointAction.ReturnToBase => "RTB",
                _ => "WAYPOINT"
            };
        }

        private static string ResolveOrdnanceCode(OrdnanceEmploymentRecordStage stage)
        {
            return stage switch
            {
                OrdnanceEmploymentRecordStage.PreparationStarted => "PREP",
                OrdnanceEmploymentRecordStage.PreparationAborted => "PREPABORT",
                OrdnanceEmploymentRecordStage.OrdnanceReleased => "LAUNCH",
                OrdnanceEmploymentRecordStage.EffectResolved => "EFFECT",
                _ => "ORDNANCE"
            };
        }

        private static string ResolveTrackCode(IADSTrackDiagnosticEvent trackEvent)
        {
            return trackEvent switch
            {
                IADSTrackDiagnosticEvent.NotObserved => "NO_TRACK",
                IADSTrackDiagnosticEvent.TentativeStarted => "ACQUIRE",
                IADSTrackDiagnosticEvent.TentativeUpdated => "ACQUIRE",
                IADSTrackDiagnosticEvent.Established => "TRACK+",
                IADSTrackDiagnosticEvent.Updated => "TRACK",
                IADSTrackDiagnosticEvent.Identified => "IDENT",
                IADSTrackDiagnosticEvent.Stale => "STALE",
                IADSTrackDiagnosticEvent.StaleUpdated => "STALE",
                IADSTrackDiagnosticEvent.Reacquired => "REACQUIRE",
                IADSTrackDiagnosticEvent.Expired => "TRACK-",
                IADSTrackDiagnosticEvent.Removed => "TRACK-",
                _ => "TRACK"
            };
        }

        private void AppendIndexRow(string label, string mission, string outcome)
        {
            var cycleIndex = GetCycleIndex(gameManager.CurrentTime);
            File.AppendAllText(
                RunFilePath,
                $"| `{label}` | {mission} | {outcome} | {cycleIndex:D3} |"
                + Environment.NewLine);
        }

        private AirMissionRequest FindRequest(AirPackage package)
        {
            var commander = airTaskingSystem.GetCommander(package.Alliance);
            return commander?.MissionRequests.FirstOrDefault(
                request => request.MissionRequestId == package.MissionRequestId);
        }

        private string ResolveSquadronName(Guid squadronId)
        {
            if (gameManager.squadronSystem.TryGetSquadron(squadronId, out var squadron)
                && !string.IsNullOrWhiteSpace(squadron.Name))
                return squadron.Name;
            return $"SQN-{SimLogNames.ShortId(squadronId)}";
        }

        private string ResolveAircraftName(Guid squadronId)
        {
            if (gameManager.squadronSystem.TryGetSquadron(squadronId, out var squadron)
                && aircraftTypes.TryGetValue(
                    squadron.AircraftTypeDefinitionId,
                    out var aircraftType))
                return aircraftType.Name;
            return "unknown type";
        }

        // ---------------------------------------------------------------
        // Line formatting
        // ---------------------------------------------------------------

        private void AppendLine(
            StringBuilder builder,
            DateTime occurredAt,
            string label,
            string code,
            string detail)
        {
            builder.AppendLine(FormatLine(occurredAt, label, code, detail));
        }

        private string FormatLine(
            DateTime occurredAt,
            string label,
            string code,
            string detail)
        {
            return $"{FormatTime(occurredAt)}  {label.PadRight(10)}  "
                   + $"{code.PadRight(9)}  {detail}";
        }

        /// <summary>
        /// Campaign day plus sim clock time. Fixed width so event codes and
        /// labels stay column aligned for grep.
        /// </summary>
        private string FormatTime(DateTime occurredAt)
        {
            var day = (int)(occurredAt.Date - campaignStartTime.Date).TotalDays + 1;
            return $"D{Math.Max(0, day):D2} {occurredAt:HH:mm:ss}";
        }
    }
}
