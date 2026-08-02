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

        private readonly GameManager gameManager;
        private readonly AirTaskingSystem airTaskingSystem;
        private readonly DateTime campaignStartTime;
        private readonly IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypes;
        private readonly IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceTypes;

        private readonly HashSet<Guid> writtenFlightIds = new HashSet<Guid>();
        private readonly HashSet<AirTaskingDiagnostic> writtenDiagnostics =
            new HashSet<AirTaskingDiagnostic>();
        private readonly HashSet<int> startedCycles = new HashSet<int>();
        private readonly Dictionary<Guid, string> flightLabels =
            new Dictionary<Guid, string>();

        private readonly string runDirectory;

        public bool IsEnabled => runDirectory != null;
        public string RunDirectory => runDirectory;

        public SimulationLogWriter(
            GameManager gameManager,
            AirTaskingSystem airTaskingSystem,
            ModuleDefinition module,
            DateTime campaignStartTime)
        {
            this.gameManager = gameManager;
            this.airTaskingSystem = airTaskingSystem;
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
            if (!IsEnabled)
                return;

            try
            {
                RefreshFlightLabels();
                EnsureCycleStarted(GetCycleIndex(gameManager.CurrentTime));
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
            if (!IsEnabled)
                return;

            try
            {
                RefreshFlightLabels();
                EnsureCycleStarted(GetCycleIndex(gameManager.CurrentTime));
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
        }

        /// <summary>
        /// Merges the flight's own execution events with the ordnance records it
        /// took part in, ordered by occurrence so cause reads before effect.
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
