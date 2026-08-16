using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Models.Gameplay.Campaign;

namespace Models.Module
{
    internal static class DcsAiObservationMissionExporter
    {
        private const double FeetPerMeter = 3.280839895d;
        private const double KnotsToMetersPerSecond = 0.514444444d;
        private const int MissionFormatVersion = 23;
        private const int RouteDrawingThickness = 8;
        private const string DcsAiSkill = "Excellent";
        private const string RadarDebugResourceKey = "ResKey_HZPLRadarDebug";
        private const string RadarDebugFileName = "HZPLRadarDebug.lua";
        private static readonly UTF8Encoding Utf8WithoutBom =
            new UTF8Encoding(false);

        private static readonly IReadOnlyDictionary<string, AircraftStores> Stores =
            new Dictionary<string, AircraftStores>(StringComparer.Ordinal)
            {
                { "F-16C_50", new AircraftStores(3249d, 60, 60) },
                { "MiG-29A", new AircraftStores(3376d, 30, 30) },
                { "E-3A", new AircraftStores(65000d, 60, 120) },
                { "KC-135", new AircraftStores(90700d, 0, 0) },
                { "A-50", new AircraftStores(70000d, 192, 192) },
                { "IL-78M", new AircraftStores(112000d, 96, 96) }
            };

        public static ScenarioExportArtifact Export(
            ScenarioExportSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.ModuleId != DcsPrototypeModule.Id)
            {
                throw new InvalidOperationException(
                    "The DCS prototype exporter can only export its matching module.");
            }
            if (snapshot.AirborneFlights.Count == 0)
            {
                throw new InvalidOperationException(
                    "There are no airborne flights to observe. Advance the paused campaign until at least one flight has taken off.");
            }

            var warnings = snapshot.Warnings.ToList();
            var flights = snapshot.AirborneFlights
                .Where(flight => ValidateFlight(flight, warnings))
                .ToList();
            var samSites = snapshot.SamSites
                .Where(site => ValidateSamSite(site, warnings))
                .ToList();
            if (flights.Count == 0)
            {
                throw new InvalidOperationException(
                    "None of the airborne flights have complete DCS mappings.");
            }

            var mission = BuildMission(snapshot, flights, samSites, warnings);
            if (mission.Contains("\"Player\"")
                || mission.Contains("\"Client\""))
            {
                throw new InvalidOperationException(
                    "An AI-observation export cannot contain player or client aircraft.");
            }

            var entries = new Dictionary<string, string>
            {
                { "mission", mission },
                { "options", BuildOptions() },
                { "warehouses", BuildWarehouses(snapshot.Airports) },
                { "theatre", DcsPrototypeModule.TheaterId },
                { "l10n/DEFAULT/dictionary", BuildDictionary(snapshot) },
                { "l10n/DEFAULT/mapResource", BuildMapResource() },
                {
                    $"l10n/DEFAULT/{RadarDebugFileName}",
                    BuildRadarDebugScript(samSites)
                }
            };

            var content = BuildArchive(entries);
            ValidateArchive(content, entries.Keys);
            var fileName = SanitizeFileName(
                $"HZPL AI Observation {snapshot.CurrentTime:yyyy-MM-dd HHmm}.miz");
            return new ScenarioExportArtifact(fileName, content, warnings);
        }

        private static bool ValidateFlight(
            ScenarioAirFlightSnapshot flight,
            ICollection<string> warnings)
        {
            if (flight == null
                || flight.FlightId == Guid.Empty
                || flight.Aircraft.Count == 0
                || string.IsNullOrWhiteSpace(flight.AircraftThirdPartyId))
                return false;
            if (!DcsPrototypeModule.CountryIds.ContainsKey(flight.CountryId))
            {
                warnings.Add(
                    $"Flight {flight.FlightId} has no DCS country mapping and was omitted.");
                return false;
            }
            return flight.Alliance == Alliance.Bluefor
                   || flight.Alliance == Alliance.Redfor;
        }

        private static bool ValidateSamSite(
            ScenarioSamSiteSnapshot site,
            ICollection<string> warnings)
        {
            if (site == null || site.Components.Count == 0)
                return false;
            if (!DcsPrototypeModule.CountryIds.ContainsKey(site.CountryId))
            {
                warnings.Add(
                    $"SAM site {site.SiteId} has no DCS country mapping and was omitted.");
                return false;
            }
            return site.Alliance == Alliance.Bluefor
                   || site.Alliance == Alliance.Redfor;
        }

        private static string BuildMission(
            ScenarioExportSnapshot snapshot,
            IReadOnlyList<ScenarioAirFlightSnapshot> flights,
            IReadOnlyList<ScenarioSamSiteSnapshot> samSites,
            ICollection<string> warnings)
        {
            var builder = new StringBuilder(65536);
            var center = GetMissionCenter(flights);
            var startSeconds = snapshot.CurrentTime.Hour * 3600
                               + snapshot.CurrentTime.Minute * 60
                               + snapshot.CurrentTime.Second;

            builder.AppendLine("mission =");
            builder.AppendLine("{");
            AppendCommonMissionHeader(builder, snapshot, center);
            AppendCoalitionIdLists(builder, flights, samSites);
            builder.AppendLine("    [\"descriptionText\"] = \"DictKey_descriptionText_1\",");
            builder.AppendLine("    [\"descriptionBlueTask\"] = \"DictKey_descriptionBlueTask_3\",");
            builder.AppendLine("    [\"descriptionRedTask\"] = \"DictKey_descriptionRedTask_2\",");
            builder.AppendLine("    [\"descriptionNeutralsTask\"] = \"DictKey_descriptionNeutralsTask_4\",");
            builder.AppendLine("    [\"sortie\"] = \"DictKey_sortie_5\",");
            AppendAircraftRouteDrawings(builder, flights);
            AppendCoalitionContents(builder, flights, samSites, warnings);
            builder.AppendLine($"    [\"version\"] = {MissionFormatVersion},");
            AppendRadarDebugTrigger(builder);
            builder.AppendLine("    [\"currentKey\"] = 6,");
            builder.AppendLine("    [\"failures\"] = {},");
            builder.AppendLine("    [\"forcedOptions\"] =");
            builder.AppendLine("    {");
            builder.AppendLine("        [\"optionsView\"] = \"optview_all\",");
            builder.AppendLine("    },");
            builder.AppendLine($"    [\"start_time\"] = {startSeconds},");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendCommonMissionHeader(
            StringBuilder builder,
            ScenarioExportSnapshot snapshot,
            DcsPoint center)
        {
            builder.AppendLine("    [\"groundControl\"] =");
            builder.AppendLine("    {");
            builder.AppendLine("        [\"passwords\"] = { [\"artillery_commander\"] = {}, [\"instructor\"] = {}, [\"observer\"] = {}, [\"forward_observer\"] = {} },");
            builder.AppendLine("        [\"roles\"] =");
            builder.AppendLine("        {");
            foreach (var role in new[]
                     {
                         "artillery_commander", "instructor", "observer",
                         "forward_observer"
                     })
            {
                builder.AppendLine($"            [\"{role}\"] = {{ [\"neutrals\"] = 0, [\"blue\"] = 0, [\"red\"] = 0 }},");
            }
            builder.AppendLine("        },");
            builder.AppendLine("        [\"isPilotControlVehicles\"] = false,");
            builder.AppendLine("    },");
            builder.AppendLine("    [\"requiredModules\"] = {},");
            builder.AppendLine("    [\"date\"] =");
            builder.AppendLine("    {");
            builder.AppendLine($"        [\"Day\"] = {snapshot.CurrentTime.Day},");
            builder.AppendLine($"        [\"Year\"] = {snapshot.CurrentTime.Year},");
            builder.AppendLine($"        [\"Month\"] = {snapshot.CurrentTime.Month},");
            builder.AppendLine("    },");
            builder.AppendLine("    [\"trig\"] = { [\"actions\"] = {}, [\"events\"] = {}, [\"custom\"] = {}, [\"func\"] = {}, [\"flag\"] = {}, [\"conditions\"] = {}, [\"customStartup\"] = {}, [\"funcStartup\"] = {} },");
            builder.AppendLine("    [\"maxDictId\"] = 5,");
            builder.AppendLine("    [\"result\"] = { [\"offline\"] = { [\"conditions\"] = {}, [\"actions\"] = {}, [\"func\"] = {} }, [\"total\"] = 0, [\"blue\"] = { [\"conditions\"] = {}, [\"actions\"] = {}, [\"func\"] = {} }, [\"red\"] = { [\"conditions\"] = {}, [\"actions\"] = {}, [\"func\"] = {} } },");
            builder.AppendLine("    [\"pictureFileNameN\"] = {},");
            builder.AppendLine("    [\"pictureFileNameServer\"] = {},");
            AppendWeather(builder);
            builder.AppendLine($"    [\"theatre\"] = \"{DcsPrototypeModule.TheaterId}\",");
            builder.AppendLine("    [\"triggers\"] = { [\"zones\"] = {} },");
            builder.AppendLine("    [\"map\"] =");
            builder.AppendLine("    {");
            builder.AppendLine($"        [\"centerY\"] = {Number(center.Y)},");
            builder.AppendLine("        [\"zoom\"] = 350000,");
            builder.AppendLine($"        [\"centerX\"] = {Number(center.X)},");
            builder.AppendLine("    },");
            builder.AppendLine("    [\"pictureFileNameR\"] = {},");
            builder.AppendLine("    [\"goals\"] = {},");
            builder.AppendLine("    [\"pictureFileNameB\"] = {},");
        }

        private static void AppendWeather(StringBuilder builder)
        {
            builder.AppendLine("    [\"weather\"] =");
            builder.AppendLine("    {");
            builder.AppendLine("        [\"atmosphere_type\"] = 0,");
            builder.AppendLine("        [\"groundTurbulence\"] = 0,");
            builder.AppendLine("        [\"wind\"] = { [\"at8000\"] = { [\"speed\"] = 0, [\"dir\"] = 0 }, [\"atGround\"] = { [\"speed\"] = 0, [\"dir\"] = 0 }, [\"at2000\"] = { [\"speed\"] = 0, [\"dir\"] = 0 } },");
            builder.AppendLine("        [\"visibility\"] = { [\"distance\"] = 80000 },");
            builder.AppendLine("        [\"season\"] = { [\"temperature\"] = 20 },");
            builder.AppendLine("        [\"type_weather\"] = 0,");
            builder.AppendLine("        [\"qnh\"] = 760,");
            builder.AppendLine("        [\"cyclones\"] = {},");
            builder.AppendLine("        [\"name\"] = \"HZPL clear weather\",");
            builder.AppendLine("        [\"dust_density\"] = 0,");
            builder.AppendLine("        [\"enable_dust\"] = false,");
            builder.AppendLine("        [\"clouds\"] = { [\"thickness\"] = 200, [\"density\"] = 0, [\"preset\"] = \"Preset2\", [\"base\"] = 2500, [\"iprecptns\"] = 0 },");
            builder.AppendLine("    },");
        }

        private static void AppendAircraftRouteDrawings(
            StringBuilder builder,
            IReadOnlyList<ScenarioAirFlightSnapshot> flights)
        {
            builder.AppendLine("    [\"drawings\"] =");
            builder.AppendLine("    {");
            builder.AppendLine("        [\"options\"] =");
            builder.AppendLine("        {");
            builder.AppendLine("            [\"hiddenOnF10Map\"] =");
            builder.AppendLine("            {");
            foreach (var role in new[]
                     {
                         "Observer", "Instructor", "ForwardObserver",
                         "Spectrator", "ArtilleryCommander", "Pilot"
                     })
            {
                builder.AppendLine($"                [\"{role}\"] = {{ [\"Neutral\"] = false, [\"Blue\"] = false, [\"Red\"] = false }},");
            }
            builder.AppendLine("            },");
            builder.AppendLine("        },");
            builder.AppendLine("        [\"layers\"] =");
            builder.AppendLine("        {");
            AppendRouteDrawingLayer(
                builder,
                1,
                "Red",
                Array.Empty<ScenarioAirFlightSnapshot>());
            AppendRouteDrawingLayer(
                builder,
                2,
                "Blue",
                Array.Empty<ScenarioAirFlightSnapshot>());
            AppendRouteDrawingLayer(
                builder,
                3,
                "Neutral",
                Array.Empty<ScenarioAirFlightSnapshot>());
            AppendRouteDrawingLayer(
                builder,
                4,
                "Common",
                flights);
            AppendRouteDrawingLayer(
                builder,
                5,
                "Author",
                Array.Empty<ScenarioAirFlightSnapshot>());
            builder.AppendLine("        },");
            builder.AppendLine("    },");
        }

        private static void AppendRouteDrawingLayer(
            StringBuilder builder,
            int layerIndex,
            string layerName,
            IEnumerable<ScenarioAirFlightSnapshot> flights)
        {
            builder.AppendLine($"            [{layerIndex}] =");
            builder.AppendLine("            {");
            builder.AppendLine("                [\"visible\"] = true,");
            builder.AppendLine($"                [\"name\"] = \"{layerName}\",");
            builder.AppendLine("                [\"objects\"] =");
            builder.AppendLine("                {");
            var objectIndex = 1;
            foreach (var flight in flights.OrderBy(candidate => candidate.FlightId))
            {
                AppendRouteDrawingObject(
                    builder,
                    objectIndex++,
                    layerName,
                    flight);
            }
            builder.AppendLine("                },");
            builder.AppendLine("            },");
        }

        private static void AppendRouteDrawingObject(
            StringBuilder builder,
            int objectIndex,
            string layerName,
            ScenarioAirFlightSnapshot flight)
        {
            var points = BuildAirRoute(flight)
                .Select(waypoint => ToDcs(waypoint.Position))
                .ToList();
            var origin = points[0];
            var name = $"HZPL Route {flight.TaskType} {ShortId(flight.FlightId)}";
            var color = flight.Alliance == Alliance.Redfor
                ? "0xff4040ff"
                : "0x00aaffff";

            builder.AppendLine($"                    [{objectIndex}] =");
            builder.AppendLine("                    {");
            builder.AppendLine("                        [\"visible\"] = true,");
            builder.AppendLine("                        [\"hiddenOnPlanner\"] = false,");
            builder.AppendLine("                        [\"primitiveType\"] = \"Line\",");
            builder.AppendLine("                        [\"lineMode\"] = \"free\",");
            builder.AppendLine("                        [\"style\"] = \"solid\",");
            builder.AppendLine("                        [\"closed\"] = false,");
            builder.AppendLine($"                        [\"thickness\"] = {RouteDrawingThickness},");
            builder.AppendLine($"                        [\"colorString\"] = \"{color}\",");
            builder.AppendLine($"                        [\"mapY\"] = {Number(origin.Y)},");
            builder.AppendLine($"                        [\"mapX\"] = {Number(origin.X)},");
            builder.AppendLine($"                        [\"layerName\"] = \"{layerName}\",");
            builder.AppendLine($"                        [\"name\"] = \"{Lua(name)}\",");
            builder.AppendLine("                        [\"points\"] =");
            builder.AppendLine("                        {");
            for (var index = 0; index < points.Count; index++)
            {
                builder.AppendLine($"                            [{index + 1}] = {{ [\"y\"] = {Number(points[index].Y - origin.Y)}, [\"x\"] = {Number(points[index].X - origin.X)} }},");
            }
            builder.AppendLine("                        },");
            builder.AppendLine("                    },");
        }

        private static void AppendCoalitionIdLists(
            StringBuilder builder,
            IReadOnlyList<ScenarioAirFlightSnapshot> flights,
            IReadOnlyList<ScenarioSamSiteSnapshot> samSites)
        {
            builder.AppendLine("    [\"coalitions\"] =");
            builder.AppendLine("    {");
            AppendCoalitionIdList(builder, "neutrals", Alliance.Neutral,
                Array.Empty<Guid>());
            AppendCoalitionIdList(builder, "blue", Alliance.Bluefor,
                GetCountryIds(Alliance.Bluefor, flights, samSites));
            AppendCoalitionIdList(builder, "red", Alliance.Redfor,
                GetCountryIds(Alliance.Redfor, flights, samSites));
            builder.AppendLine("    },");
        }

        private static void AppendCoalitionIdList(
            StringBuilder builder,
            string name,
            Alliance alliance,
            IEnumerable<Guid> countryIds)
        {
            builder.AppendLine($"        [\"{name}\"] =");
            builder.AppendLine("        {");
            var index = 1;
            foreach (var countryId in countryIds.Distinct().OrderBy(id => id))
            {
                if (DcsPrototypeModule.CountryIds.TryGetValue(
                        countryId,
                        out var dcsId))
                {
                    builder.AppendLine($"            [{index++}] = {dcsId},");
                }
            }
            builder.AppendLine("        },");
        }

        private static IEnumerable<Guid> GetCountryIds(
            Alliance alliance,
            IEnumerable<ScenarioAirFlightSnapshot> flights,
            IEnumerable<ScenarioSamSiteSnapshot> samSites)
        {
            return flights.Where(flight => flight.Alliance == alliance)
                .Select(flight => flight.CountryId)
                .Concat(samSites.Where(site => site.Alliance == alliance)
                    .Select(site => site.CountryId));
        }

        private static void AppendCoalitionContents(
            StringBuilder builder,
            IReadOnlyList<ScenarioAirFlightSnapshot> flights,
            IReadOnlyList<ScenarioSamSiteSnapshot> samSites,
            ICollection<string> warnings)
        {
            var nextGroupId = 1000;
            var nextUnitId = 1000;
            builder.AppendLine("    [\"coalition\"] =");
            builder.AppendLine("    {");
            AppendCoalition(builder, "neutrals", Alliance.Neutral,
                flights, samSites, warnings, ref nextGroupId, ref nextUnitId);
            AppendCoalition(builder, "blue", Alliance.Bluefor,
                flights, samSites, warnings, ref nextGroupId, ref nextUnitId);
            AppendCoalition(builder, "red", Alliance.Redfor,
                flights, samSites, warnings, ref nextGroupId, ref nextUnitId);
            builder.AppendLine("    },");
        }

        private static void AppendCoalition(
            StringBuilder builder,
            string name,
            Alliance alliance,
            IReadOnlyList<ScenarioAirFlightSnapshot> flights,
            IReadOnlyList<ScenarioSamSiteSnapshot> samSites,
            ICollection<string> warnings,
            ref int nextGroupId,
            ref int nextUnitId)
        {
            var coalitionFlights = flights
                .Where(flight => flight.Alliance == alliance)
                .ToList();
            var coalitionSites = samSites
                .Where(site => site.Alliance == alliance)
                .ToList();
            var countryIds = coalitionFlights.Select(flight => flight.CountryId)
                .Concat(coalitionSites.Select(site => site.CountryId))
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            var bullseye = coalitionFlights.Count > 0
                ? GetMissionCenter(coalitionFlights)
                : new DcsPoint(0d, 0d, 0d);

            builder.AppendLine($"        [\"{name}\"] =");
            builder.AppendLine("        {");
            builder.AppendLine($"            [\"bullseye\"] = {{ [\"y\"] = {Number(bullseye.Y)}, [\"x\"] = {Number(bullseye.X)} }},");
            builder.AppendLine("            [\"nav_points\"] = {},");
            builder.AppendLine($"            [\"name\"] = \"{name}\",");
            builder.AppendLine("            [\"country\"] =");
            builder.AppendLine("            {");
            var countryIndex = 1;
            foreach (var countryId in countryIds)
            {
                if (!DcsPrototypeModule.CountryIds.TryGetValue(
                        countryId,
                        out var dcsCountryId))
                    continue;

                var countryFlights = coalitionFlights
                    .Where(flight => flight.CountryId == countryId)
                    .ToList();
                var countrySites = coalitionSites
                    .Where(site => site.CountryId == countryId)
                    .ToList();
                builder.AppendLine($"                [{countryIndex++}] =");
                builder.AppendLine("                {");
                builder.AppendLine($"                    [\"id\"] = {dcsCountryId},");
                builder.AppendLine($"                    [\"name\"] = \"{CountryName(dcsCountryId)}\",");
                if (countryFlights.Count > 0)
                    AppendAircraftCategory(builder, countryFlights, warnings,
                        ref nextGroupId, ref nextUnitId);
                if (countrySites.Count > 0)
                    AppendVehicleCategory(builder, countrySites,
                        ref nextGroupId, ref nextUnitId);
                builder.AppendLine("                },");
            }
            builder.AppendLine("            },");
            builder.AppendLine("        },");
        }

        private static void AppendAircraftCategory(
            StringBuilder builder,
            IReadOnlyList<ScenarioAirFlightSnapshot> flights,
            ICollection<string> warnings,
            ref int nextGroupId,
            ref int nextUnitId)
        {
            builder.AppendLine("                    [\"plane\"] =");
            builder.AppendLine("                    {");
            builder.AppendLine("                        [\"group\"] =");
            builder.AppendLine("                        {");
            var groupIndex = 1;
            foreach (var flight in flights.OrderBy(candidate => candidate.FlightId))
            {
                AppendAirGroup(builder, groupIndex++, flight, warnings,
                    nextGroupId++, ref nextUnitId);
            }
            builder.AppendLine("                        },");
            builder.AppendLine("                    },");
        }

        private static void AppendAirGroup(
            StringBuilder builder,
            int groupIndex,
            ScenarioAirFlightSnapshot flight,
            ICollection<string> warnings,
            int groupId,
            ref int nextUnitId)
        {
            var origin = ToDcs(flight.Position);
            var speed = Math.Max(80d,
                flight.SpeedKnots * KnotsToMetersPerSecond);
            var name = $"HZPL-{flight.TaskType}-{ShortId(flight.FlightId)}";
            builder.AppendLine($"                            [{groupIndex}] =");
            builder.AppendLine("                            {");
            builder.AppendLine("                                [\"modulation\"] = 0,");
            builder.AppendLine("                                [\"tasks\"] = {},");
            builder.AppendLine("                                [\"radioSet\"] = false,");
            builder.AppendLine($"                                [\"task\"] = \"{DcsTask(flight.TaskType)}\",");
            builder.AppendLine("                                [\"uncontrolled\"] = false,");
            AppendAirRoute(builder, flight, speed);
            builder.AppendLine($"                                [\"groupId\"] = {groupId},");
            builder.AppendLine("                                [\"hidden\"] = false,");
            builder.AppendLine("                                [\"units\"] =");
            builder.AppendLine("                                {");
            for (var index = 0; index < flight.Aircraft.Count; index++)
            {
                AppendAircraftUnit(
                    builder,
                    index + 1,
                    flight,
                    flight.Aircraft[index],
                    origin,
                    speed,
                    nextUnitId++,
                    groupIndex,
                    warnings);
            }
            builder.AppendLine("                                },");
            builder.AppendLine($"                                [\"y\"] = {Number(origin.Y)},");
            builder.AppendLine($"                                [\"x\"] = {Number(origin.X)},");
            builder.AppendLine($"                                [\"name\"] = \"{Lua(name)}\",");
            builder.AppendLine("                                [\"communication\"] = true,");
            builder.AppendLine("                                [\"start_time\"] = 0,");
            builder.AppendLine("                                [\"frequency\"] = 251,");
            builder.AppendLine("                            },");
        }

        private static void AppendAirRoute(
            StringBuilder builder,
            ScenarioAirFlightSnapshot flight,
            double speed)
        {
            var route = BuildAirRoute(flight);

            builder.AppendLine("                                [\"route\"] =");
            builder.AppendLine("                                {");
            builder.AppendLine("                                    [\"points\"] =");
            builder.AppendLine("                                    {");
            var eta = 0d;
            DcsPoint previous = default;
            var orbitAssigned = false;
            for (var index = 0; index < route.Count; index++)
            {
                var waypoint = route[index];
                var point = ToDcs(waypoint.Position);
                if (index > 0)
                {
                    var distance = Math.Sqrt(
                        Math.Pow(point.X - previous.X, 2d)
                        + Math.Pow(point.Y - previous.Y, 2d));
                    eta += distance / speed;
                }

                var isLanding = waypoint.Action == AirWaypointAction.Land
                                && waypoint.AirportThirdPartyId > 0;
                builder.AppendLine($"                                        [{index + 1}] =");
                builder.AppendLine("                                        {");
                builder.AppendLine($"                                            [\"alt\"] = {Number(Math.Max(0d, point.Altitude))},");
                builder.AppendLine($"                                            [\"action\"] = \"{(isLanding ? "Landing" : "Turning Point")}\",");
                builder.AppendLine("                                            [\"alt_type\"] = \"BARO\",");
                builder.AppendLine($"                                            [\"speed\"] = {Number(speed)},");
                var addOrbit = IsSustained(flight.TaskType)
                               && !orbitAssigned
                               && (flight.ExecutionPhase == FlightExecutionPhase.Executing
                                   && index == 0
                                   || waypoint.Action == AirWaypointAction.StationEntry);
                AppendWaypointTasks(builder, flight.TaskType,
                    index == 0, addOrbit, point, speed);
                orbitAssigned |= addOrbit;
                builder.AppendLine($"                                            [\"type\"] = \"{(isLanding ? "Land" : "Turning Point")}\",");
                builder.AppendLine($"                                            [\"ETA\"] = {Number(eta)},");
                builder.AppendLine($"                                            [\"ETA_locked\"] = {(index == 0 ? "true" : "false")},");
                builder.AppendLine($"                                            [\"y\"] = {Number(point.Y)},");
                builder.AppendLine($"                                            [\"x\"] = {Number(point.X)},");
                builder.AppendLine("                                            [\"formation_template\"] = \"\",");
                builder.AppendLine("                                            [\"speed_locked\"] = true,");
                if (isLanding)
                    builder.AppendLine($"                                            [\"airdromeId\"] = {waypoint.AirportThirdPartyId},");
                builder.AppendLine("                                        },");
                previous = point;
            }
            builder.AppendLine("                                    },");
            builder.AppendLine("                                },");
        }

        private static List<ScenarioWaypointSnapshot> BuildAirRoute(
            ScenarioAirFlightSnapshot flight)
        {
            var route = new List<ScenarioWaypointSnapshot>
            {
                new ScenarioWaypointSnapshot(
                    Guid.Empty,
                    flight.Position,
                    AirWaypointAction.Transit,
                    default,
                    false,
                    default,
                    0)
            };
            route.AddRange(flight.RemainingRoute.Where(waypoint =>
                waypoint.Action != AirWaypointAction.Takeoff));
            if (route.Count == 1)
            {
                var distanceFeet = 20f * 3280.8399f;
                var radians = flight.HeadingDegrees * Math.PI / 180d;
                route.Add(new ScenarioWaypointSnapshot(
                    Guid.Empty,
                    new ScenarioPosition(
                        flight.Position.XFeet
                        + (float)(Math.Sin(radians) * distanceFeet),
                        flight.Position.AltitudeFeet,
                        flight.Position.ZFeet
                        + (float)(Math.Cos(radians) * distanceFeet)),
                    AirWaypointAction.Transit,
                    default,
                    false,
                    default,
                    0));
            }
            return route;
        }

        private static void AppendWaypointTasks(
            StringBuilder builder,
            AirFlightTaskType missionType,
            bool firstPoint,
            bool addOrbit,
            DcsPoint point,
            double speed)
        {
            builder.AppendLine("                                            [\"task\"] =");
            builder.AppendLine("                                            {");
            builder.AppendLine("                                                [\"id\"] = \"ComboTask\",");
            builder.AppendLine("                                                [\"params\"] =");
            builder.AppendLine("                                                {");
            builder.AppendLine("                                                    [\"tasks\"] =");
            builder.AppendLine("                                                    {");
            var taskIndex = 1;
            if (firstPoint)
            {
                if (missionType == AirFlightTaskType.Barcap
                    || missionType == AirFlightTaskType.OcaSweep
                    || missionType == AirFlightTaskType.FighterEscort)
                {
                    AppendEngageTask(builder, taskIndex++, "Air", "CAP");
                }
                else if (missionType
                         == AirFlightTaskType.DeadAttack
                         || missionType == AirFlightTaskType.SeadEscort)
                {
                    AppendEngageTask(builder, taskIndex++, "Air Defence", "SEAD");
                }
                else if (missionType == AirFlightTaskType.Strike)
                {
                    AppendEngageTask(
                        builder,
                        taskIndex++,
                        "Ground Units",
                        "Ground Attack");
                }
                else if (missionType == AirFlightTaskType.AirborneC2)
                {
                    AppendEnrouteTask(builder, taskIndex++, "AWACS");
                }
                else if (missionType == AirFlightTaskType.AerialRefueling)
                {
                    AppendEnrouteTask(builder, taskIndex++, "Tanker");
                }
            }
            if (addOrbit)
            {
                builder.AppendLine($"                                                        [{taskIndex}] =");
                builder.AppendLine("                                                        {");
                builder.AppendLine($"                                                            [\"number\"] = {taskIndex},");
                builder.AppendLine("                                                            [\"auto\"] = false,");
                builder.AppendLine("                                                            [\"id\"] = \"Orbit\",");
                builder.AppendLine("                                                            [\"enabled\"] = true,");
                builder.AppendLine($"                                                            [\"params\"] = {{ [\"altitude\"] = {Number(point.Altitude)}, [\"speed\"] = {Number(speed)}, [\"pattern\"] = \"Race-Track\" }},");
                builder.AppendLine("                                                        },");
            }
            builder.AppendLine("                                                    },");
            builder.AppendLine("                                                },");
            builder.AppendLine("                                            },");
        }

        private static void AppendEngageTask(
            StringBuilder builder,
            int index,
            string targetType,
            string key)
        {
            builder.AppendLine($"                                                        [{index}] =");
            builder.AppendLine("                                                        {");
            builder.AppendLine($"                                                            [\"number\"] = {index},");
            builder.AppendLine($"                                                            [\"key\"] = \"{key}\",");
            builder.AppendLine("                                                            [\"id\"] = \"EngageTargets\",");
            builder.AppendLine("                                                            [\"enabled\"] = true,");
            builder.AppendLine("                                                            [\"auto\"] = true,");
            builder.AppendLine($"                                                            [\"params\"] = {{ [\"targetTypes\"] = {{ [1] = \"{targetType}\" }}, [\"priority\"] = 0 }},");
            builder.AppendLine("                                                        },");
        }

        private static void AppendEnrouteTask(
            StringBuilder builder,
            int index,
            string taskId)
        {
            builder.AppendLine($"                                                        [{index}] =");
            builder.AppendLine("                                                        {");
            builder.AppendLine($"                                                            [\"number\"] = {index},");
            builder.AppendLine("                                                            [\"auto\"] = true,");
            builder.AppendLine($"                                                            [\"id\"] = \"{taskId}\",");
            builder.AppendLine("                                                            [\"enabled\"] = true,");
            builder.AppendLine("                                                            [\"params\"] = {},");
            builder.AppendLine("                                                        },");
        }

        private static void AppendAircraftUnit(
            StringBuilder builder,
            int unitIndex,
            ScenarioAirFlightSnapshot flight,
            ScenarioAircraftSnapshot aircraft,
            DcsPoint origin,
            double speed,
            int unitId,
            int callsignGroup,
            ICollection<string> warnings)
        {
            var heading = NormalizeRadians(
                flight.HeadingDegrees * Math.PI / 180d);
            var lateralOffset = (unitIndex - (flight.Aircraft.Count + 1d) / 2d)
                                * 60d;
            var x = origin.X - Math.Sin(heading) * lateralOffset;
            var y = origin.Y + Math.Cos(heading) * lateralOffset;
            var unitName = $"HZPL-{ShortId(flight.FlightId)}-{unitIndex}";
            builder.AppendLine($"                                    [{unitIndex}] =");
            builder.AppendLine("                                    {");
            builder.AppendLine($"                                        [\"alt\"] = {Number(Math.Max(1d, origin.Altitude))},");
            builder.AppendLine("                                        [\"alt_type\"] = \"BARO\",");
            builder.AppendLine($"                                        [\"livery_id\"] = \"{Lua(DcsLivery(flight.AircraftThirdPartyId))}\",");
            builder.AppendLine($"                                        [\"skill\"] = \"{DcsAiSkill}\",");
            builder.AppendLine($"                                        [\"speed\"] = {Number(speed)},");
            builder.AppendLine("                                        [\"AddPropAircraft\"] = {},");
            builder.AppendLine($"                                        [\"type\"] = \"{Lua(flight.AircraftThirdPartyId)}\",");
            builder.AppendLine($"                                        [\"unitId\"] = {unitId},");
            builder.AppendLine($"                                        [\"psi\"] = {Number(-heading)},");
            builder.AppendLine($"                                        [\"y\"] = {Number(y)},");
            builder.AppendLine($"                                        [\"x\"] = {Number(x)},");
            builder.AppendLine($"                                        [\"name\"] = \"{Lua(unitName)}\",");
            AppendPayload(builder, flight, aircraft);
            builder.AppendLine($"                                        [\"heading\"] = {Number(heading)},");
            AppendCallsign(builder, flight, callsignGroup, unitIndex);
            builder.AppendLine($"                                        [\"onboard_num\"] = \"{unitId % 1000:000}\",");
            builder.AppendLine("                                    },");
        }

        private static void AppendCallsign(
            StringBuilder builder,
            ScenarioAirFlightSnapshot flight,
            int groupIndex,
            int unitIndex)
        {
            var flightNumber = 1 + (groupIndex - 1) % 9;
            var unitNumber = Math.Min(4, unitIndex);
            if (DcsPrototypeModule.CountryIds.TryGetValue(
                    flight.CountryId,
                    out var countryId)
                && countryId == 0)
            {
                // DCS Russian aircraft use a numeric callsign rather than the
                // NATO callsign table. The final digit identifies the unit.
                var callsign = 100 + (flightNumber - 1) * 10 + unitNumber;
                builder.AppendLine(
                    $"                                        [\"callsign\"] = {callsign},");
                return;
            }

            var callsignId = 1;
            var callsignName = "Enfield";
            if (flight.TaskType == AirFlightTaskType.AirborneC2)
            {
                callsignName = "Overlord";
            }
            else if (flight.TaskType
                     == AirFlightTaskType.AerialRefueling)
            {
                callsignName = "Texaco";
            }

            builder.AppendLine(
                $"                                        [\"callsign\"] = {{ [1] = {callsignId}, [2] = {flightNumber}, [3] = {unitNumber}, [\"name\"] = \"{callsignName}{flightNumber}{unitNumber}\" }},");
        }

        private static string DcsLivery(string aircraftType)
        {
            return aircraftType switch
            {
                "E-3A" => "nato",
                "KC-135" => "Standard USAF",
                "A-50" => "RF Air Force",
                "IL-78M" => "RF Air Force",
                _ => "default"
            };
        }

        private static void AppendPayload(
            StringBuilder builder,
            ScenarioAirFlightSnapshot flight,
            ScenarioAircraftSnapshot aircraft)
        {
            Stores.TryGetValue(flight.AircraftThirdPartyId, out var stores);
            stores ??= new AircraftStores(0d, 0, 0);
            var pylons = ResolvePylons(aircraft);
            builder.AppendLine("                                        [\"payload\"] =");
            builder.AppendLine("                                        {");
            builder.AppendLine("                                            [\"pylons\"] =");
            builder.AppendLine("                                            {");
            foreach (var pylon in pylons.OrderBy(item => item.Key))
            {
                builder.AppendLine($"                                                [{pylon.Key}] = {{ [\"CLSID\"] = \"{Lua(pylon.Value)}\" }},");
            }
            builder.AppendLine("                                            },");
            builder.AppendLine($"                                            [\"fuel\"] = {Number(stores.Fuel)},");
            builder.AppendLine($"                                            [\"flare\"] = {stores.Flares},");
            if (!IsSupportAircraft(flight.TaskType))
                builder.AppendLine("                                            [\"ammo_type\"] = 1,");
            builder.AppendLine($"                                            [\"chaff\"] = {stores.Chaff},");
            builder.AppendLine("                                            [\"gun\"] = 100,");
            builder.AppendLine("                                        },");
        }

        private static bool IsSupportAircraft(
            AirFlightTaskType missionType)
        {
            return missionType == AirFlightTaskType.AirborneC2
                   || missionType
                   == AirFlightTaskType.AerialRefueling;
        }

        private static Dictionary<int, string> ResolvePylons(
            ScenarioAircraftSnapshot aircraft)
        {
            var result = new Dictionary<int, string>();
            foreach (var stationLoad in aircraft.ExternalStationLoads)
            {
                if (stationLoad.IsPartiallyExpended)
                {
                    throw new InvalidOperationException(
                        $"Aircraft {aircraft.AircraftId} has a partially expended carriage configuration that DCS cannot represent exactly.");
                }
                if (!int.TryParse(
                        stationLoad.StationThirdPartyId,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var station)
                    || station <= 0)
                {
                    throw new InvalidOperationException(
                        $"Aircraft {aircraft.AircraftId} loadout station {stationLoad.AircraftLoadoutStationDefinitionId} has no valid DCS pylon mapping.");
                }
                if (string.IsNullOrWhiteSpace(
                        stationLoad.CarriageThirdPartyId))
                {
                    throw new InvalidOperationException(
                        $"Aircraft {aircraft.AircraftId} carriage configuration {stationLoad.AircraftCarriageConfigurationDefinitionId} has no DCS CLSID mapping.");
                }
                if (result.ContainsKey(station))
                {
                    throw new InvalidOperationException(
                        $"Aircraft {aircraft.AircraftId} maps more than one HZPL loadout station to DCS pylon {station}.");
                }

                result.Add(station, stationLoad.CarriageThirdPartyId);
            }
            return result;
        }

        private static void AppendVehicleCategory(
            StringBuilder builder,
            IReadOnlyList<ScenarioSamSiteSnapshot> sites,
            ref int nextGroupId,
            ref int nextUnitId)
        {
            builder.AppendLine("                    [\"vehicle\"] =");
            builder.AppendLine("                    {");
            builder.AppendLine("                        [\"group\"] =");
            builder.AppendLine("                        {");
            var groupIndex = 1;
            foreach (var site in sites.OrderBy(candidate => candidate.SiteId))
            {
                AppendSamGroup(builder, groupIndex++, site,
                    nextGroupId++, ref nextUnitId);
            }
            builder.AppendLine("                        },");
            builder.AppendLine("                    },");
        }

        private static void AppendSamGroup(
            StringBuilder builder,
            int groupIndex,
            ScenarioSamSiteSnapshot site,
            int groupId,
            ref int nextUnitId)
        {
            var origin = ToDcs(site.Position);
            var physicalUnits = GetSamPhysicalUnitTypes(site);
            var name = $"HZPL-SAM-{ShortId(site.SiteId)}";

            builder.AppendLine($"                            [{groupIndex}] =");
            builder.AppendLine("                            {");
            builder.AppendLine("                                [\"visible\"] = false,");
            builder.AppendLine("                                [\"tasks\"] = {},");
            builder.AppendLine("                                [\"uncontrollable\"] = false,");
            builder.AppendLine("                                [\"task\"] = \"Ground Nothing\",");
            builder.AppendLine("                                [\"route\"] =");
            builder.AppendLine("                                {");
            builder.AppendLine("                                    [\"spans\"] = {},");
            builder.AppendLine("                                    [\"points\"] =");
            builder.AppendLine("                                    {");
            builder.AppendLine("                                        [1] =");
            builder.AppendLine("                                        {");
            builder.AppendLine("                                            [\"alt\"] = 0,");
            builder.AppendLine("                                            [\"type\"] = \"Turning Point\",");
            builder.AppendLine("                                            [\"ETA\"] = 0,");
            builder.AppendLine("                                            [\"alt_type\"] = \"BARO\",");
            builder.AppendLine($"                                            [\"y\"] = {Number(origin.Y)},");
            builder.AppendLine($"                                            [\"x\"] = {Number(origin.X)},");
            builder.AppendLine("                                            [\"ETA_locked\"] = true,");
            builder.AppendLine("                                            [\"speed\"] = 0,");
            builder.AppendLine("                                            [\"action\"] = \"Off Road\",");
            builder.AppendLine("                                            [\"task\"] = { [\"id\"] = \"ComboTask\", [\"params\"] = { [\"tasks\"] = {} } },");
            builder.AppendLine("                                            [\"speed_locked\"] = true,");
            builder.AppendLine("                                        },");
            builder.AppendLine("                                    },");
            builder.AppendLine("                                },");
            builder.AppendLine($"                                [\"groupId\"] = {groupId},");
            builder.AppendLine("                                [\"hidden\"] = false,");
            builder.AppendLine("                                [\"units\"] =");
            builder.AppendLine("                                {");
            for (var index = 0; index < physicalUnits.Count; index++)
            {
                var angle = physicalUnits.Count <= 1
                    ? 0d
                    : index * Math.PI * 2d / physicalUnits.Count;
                var radius = index == 0 ? 0d : 140d;
                var x = origin.X + Math.Cos(angle) * radius;
                var y = origin.Y + Math.Sin(angle) * radius;
                builder.AppendLine($"                                    [{index + 1}] =");
                builder.AppendLine("                                    {");
                builder.AppendLine($"                                        [\"skill\"] = \"{DcsAiSkill}\",");
                builder.AppendLine("                                        [\"coldAtStart\"] = false,");
                builder.AppendLine($"                                        [\"type\"] = \"{Lua(physicalUnits[index])}\",");
                builder.AppendLine($"                                        [\"unitId\"] = {nextUnitId},");
                builder.AppendLine($"                                        [\"y\"] = {Number(y)},");
                builder.AppendLine($"                                        [\"x\"] = {Number(x)},");
                builder.AppendLine($"                                        [\"name\"] = \"{Lua(name)}-{index + 1}\",");
                builder.AppendLine($"                                        [\"heading\"] = {Number(angle)},");
                builder.AppendLine("                                        [\"playerCanDrive\"] = false,");
                builder.AppendLine("                                    },");
                nextUnitId++;
            }
            builder.AppendLine("                                },");
            builder.AppendLine($"                                [\"y\"] = {Number(origin.Y)},");
            builder.AppendLine($"                                [\"x\"] = {Number(origin.X)},");
            builder.AppendLine($"                                [\"name\"] = \"{Lua(name)}\",");
            builder.AppendLine("                                [\"start_time\"] = 0,");
            builder.AppendLine("                            },");
        }

        private static List<string> GetSamPhysicalUnitTypes(
            ScenarioSamSiteSnapshot site)
        {
            return site.Components
                .GroupBy(component => component.ComponentDefinitionId)
                .Select(group => new
                {
                    Type = group.First().ThirdPartyId,
                    Count = group.Count()
                })
                .GroupBy(component => component.Type, StringComparer.Ordinal)
                .Select(group => new
                {
                    Type = group.Key,
                    Count = group.Max(component => component.Count)
                })
                .SelectMany(group => Enumerable.Repeat(group.Type, group.Count))
                .ToList();
        }

        private static void AppendRadarDebugTrigger(StringBuilder builder)
        {
            builder.AppendLine("    [\"trig\"] =");
            builder.AppendLine("    {");
            builder.AppendLine("        [\"actions\"] =");
            builder.AppendLine("        {");
            builder.AppendLine($"            [1] = \"a_do_script_file(getValueResourceByKey(\\\"{RadarDebugResourceKey}\\\"));\",");
            builder.AppendLine("        },");
            builder.AppendLine("        [\"events\"] = {},");
            builder.AppendLine("        [\"custom\"] = {},");
            builder.AppendLine("        [\"func\"] = {},");
            builder.AppendLine("        [\"flag\"] = { [1] = true },");
            builder.AppendLine("        [\"conditions\"] = { [1] = \"return(true)\" },");
            builder.AppendLine("        [\"customStartup\"] = {},");
            builder.AppendLine("        [\"funcStartup\"] =");
            builder.AppendLine("        {");
            builder.AppendLine("            [1] = \"if mission.trig.conditions[1]() then mission.trig.actions[1]() end\",");
            builder.AppendLine("        },");
            builder.AppendLine("    },");
            builder.AppendLine("    [\"trigrules\"] =");
            builder.AppendLine("    {");
            builder.AppendLine("        [1] =");
            builder.AppendLine("        {");
            builder.AppendLine("            [\"rules\"] = {},");
            builder.AppendLine("            [\"eventlist\"] = \"\",");
            builder.AppendLine("            [\"predicate\"] = \"triggerStart\",");
            builder.AppendLine("            [\"actions\"] =");
            builder.AppendLine("            {");
            builder.AppendLine("                [1] =");
            builder.AppendLine("                {");
            builder.AppendLine("                    [\"density\"] = 1,");
            builder.AppendLine("                    [\"zone\"] = \"\",");
            builder.AppendLine("                    [\"preset\"] = 1,");
            builder.AppendLine($"                    [\"file\"] = \"{RadarDebugResourceKey}\",");
            builder.AppendLine("                    [\"predicate\"] = \"a_do_script_file\",");
            builder.AppendLine("                    [\"ai_task\"] = { [1] = \"\", [2] = \"\" },");
            builder.AppendLine("                },");
            builder.AppendLine("            },");
            builder.AppendLine("            [\"comment\"] = \"Start HZPL SAM radar debug monitor\",");
            builder.AppendLine("        },");
            builder.AppendLine("    },");
        }

        private static string BuildRadarDebugScript(
            IReadOnlyList<ScenarioSamSiteSnapshot> samSites)
        {
            var builder = new StringBuilder(8192);
            builder.AppendLine("HZPLRadarDebug = HZPLRadarDebug or {}");
            builder.AppendLine("HZPLRadarDebug.unitNames = {");
            foreach (var site in samSites.OrderBy(site => site.SiteId))
            {
                var groupName = $"HZPL-SAM-{ShortId(site.SiteId)}";
                var physicalUnits = GetSamPhysicalUnitTypes(site);
                for (var index = 0; index < physicalUnits.Count; index++)
                {
                    builder.AppendLine(
                        $"    \"{Lua(groupName)}-{index + 1}\",");
                }
            }
            builder.AppendLine("}");
            builder.AppendLine("HZPLRadarDebug.states = HZPLRadarDebug.states or {}");
            builder.AppendLine();
            builder.AppendLine("local function report(unitName, state, unit)");
            builder.AppendLine("    local typeName = \"radar\"");
            builder.AppendLine("    if unit and unit:isExist() then");
            builder.AppendLine("        typeName = unit:getTypeName() or typeName");
            builder.AppendLine("    end");
            builder.AppendLine("    local message = \"[HZPL RADAR] \" .. unitName .. \" (\" .. typeName .. \") -> \" .. state");
            builder.AppendLine("    trigger.action.outText(message, 10, false)");
            builder.AppendLine("    env.info(message)");
            builder.AppendLine("end");
            builder.AppendLine();
            builder.AppendLine("function HZPLRadarDebug.poll(_, now)");
            builder.AppendLine("    for _, unitName in ipairs(HZPLRadarDebug.unitNames) do");
            builder.AppendLine("        local unit = Unit.getByName(unitName)");
            builder.AppendLine("        local previous = HZPLRadarDebug.states[unitName]");
            builder.AppendLine("        if unit and unit:isExist() and unit:isActive() then");
            builder.AppendLine("            if unit:hasSensors(Unit.SensorType.RADAR) then");
            builder.AppendLine("                local radarOn = unit:getRadar()");
            builder.AppendLine("                local state = radarOn and \"ON\" or \"OFF\"");
            builder.AppendLine("                if previous ~= state then");
            builder.AppendLine("                    report(unitName, state, unit)");
            builder.AppendLine("                    HZPLRadarDebug.states[unitName] = state");
            builder.AppendLine("                end");
            builder.AppendLine("            end");
            builder.AppendLine("        elseif previous and previous ~= \"UNAVAILABLE\" then");
            builder.AppendLine("            report(unitName, \"UNAVAILABLE\", unit)");
            builder.AppendLine("            HZPLRadarDebug.states[unitName] = \"UNAVAILABLE\"");
            builder.AppendLine("        end");
            builder.AppendLine("    end");
            builder.AppendLine("    return now + 1");
            builder.AppendLine("end");
            builder.AppendLine();
            builder.AppendLine($"trigger.action.outText(\"[HZPL RADAR] Real-time monitor started for {samSites.Count} SAM site(s).\", 10, false)");
            builder.AppendLine("timer.scheduleFunction(HZPLRadarDebug.poll, nil, timer.getTime() + 1)");
            return builder.ToString();
        }

        private static string BuildWarehouses(
            IReadOnlyList<ScenarioAirportSnapshot> airports)
        {
            var builder = new StringBuilder(16384);
            builder.AppendLine("warehouses =");
            builder.AppendLine("{");
            builder.AppendLine("    [\"airports\"] =");
            builder.AppendLine("    {");
            foreach (var airport in airports.OrderBy(item => item.ThirdPartyId))
            {
                var operatingLevel = airport.IsOperational ? 10 : 0;
                builder.AppendLine($"        [{airport.ThirdPartyId}] =");
                builder.AppendLine("        {");
                builder.AppendLine("            [\"allowHotStart\"] = false,");
                builder.AppendLine($"            [\"unlimitedMunitions\"] = {(airport.IsOperational ? "true" : "false")},");
                builder.AppendLine($"            [\"OperatingLevel_Air\"] = {operatingLevel},");
                builder.AppendLine("            [\"speed\"] = 16.666666,");
                builder.AppendLine("            [\"dynamicSpawn\"] = false,");
                builder.AppendLine($"            [\"unlimitedAircrafts\"] = {(airport.IsOperational ? "true" : "false")},");
                builder.AppendLine($"            [\"unlimitedFuel\"] = {(airport.IsOperational ? "true" : "false")},");
                builder.AppendLine("            [\"periodicity\"] = 30,");
                builder.AppendLine("            [\"suppliers\"] = {},");
                builder.AppendLine($"            [\"coalition\"] = \"{WarehouseCoalition(airport.Alliance)}\",");
                builder.AppendLine("            [\"dynamicCargo\"] = false,");
                builder.AppendLine($"            [\"OperatingLevel_Eqp\"] = {operatingLevel},");
                builder.AppendLine("            [\"aircrafts\"] = {},");
                builder.AppendLine("            [\"weapons\"] = {},");
                builder.AppendLine($"            [\"OperatingLevel_Fuel\"] = {operatingLevel},");
                builder.AppendLine("            [\"size\"] = 100,");
                builder.AppendLine("        },");
            }
            builder.AppendLine("    },");
            builder.AppendLine("    [\"warehouses\"] = {},");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string BuildOptions()
        {
            return "options =\n"
                   + "{\n"
                   + "    [\"playerName\"] = \"HZPL Observer\",\n"
                   + "    [\"miscellaneous\"] = { [\"f11_free_camera\"] = true, [\"f10_awacs\"] = true, [\"f5_nearest_ac\"] = true },\n"
                   + "    [\"difficulty\"] = { [\"optionsView\"] = \"optview_all\", [\"setGlobal\"] = true, [\"map\"] = true, [\"spectatorExternalViews\"] = true, [\"externalViews\"] = true, [\"labels\"] = 1 },\n"
                   + "}\n";
        }

        private static string BuildDictionary(ScenarioExportSnapshot snapshot)
        {
            var title = $"HZPL AI Observation - {snapshot.CampaignName}";
            var description =
                $"AI observation export captured at {snapshot.CurrentTime:yyyy-MM-dd HH:mm:ss}. "
                + $"Aircraft: {snapshot.AirborneFlights.Sum(flight => flight.Aircraft.Count)}. "
                + $"SAM sites: {snapshot.SamSites.Count}. No player slot or campaign result import is included.";
            return "dictionary =\n"
                   + "{\n"
                   + $"    [\"DictKey_descriptionText_1\"] = \"{Lua(description)}\",\n"
                   + "    [\"DictKey_descriptionRedTask_2\"] = \"Observe HZPL Redfor AI operations.\",\n"
                   + "    [\"DictKey_descriptionBlueTask_3\"] = \"Observe HZPL Bluefor AI operations.\",\n"
                   + "    [\"DictKey_descriptionNeutralsTask_4\"] = \"AI observation only.\",\n"
                   + $"    [\"DictKey_sortie_5\"] = \"{Lua(title)}\",\n"
                   + "}\n";
        }

        private static string BuildMapResource()
        {
            return "mapResource =\n"
                   + "{\n"
                   + $"    [\"{RadarDebugResourceKey}\"] = \"{RadarDebugFileName}\",\n"
                   + "}\n";
        }

        private static byte[] BuildArchive(
            IReadOnlyDictionary<string, string> entries)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(
                       stream,
                       ZipArchiveMode.Create,
                       true))
            {
                foreach (var pair in entries)
                {
                    var entry = archive.CreateEntry(
                        pair.Key,
                        CompressionLevel.Optimal);
                    using var writer = new StreamWriter(
                        entry.Open(),
                        Utf8WithoutBom);
                    writer.Write(pair.Value);
                }
            }
            return stream.ToArray();
        }

        private static void ValidateArchive(
            byte[] content,
            IEnumerable<string> requiredEntries)
        {
            if (content == null || content.Length == 0)
                throw new InvalidOperationException("The DCS mission archive is empty.");

            using var stream = new MemoryStream(content, false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var names = archive.Entries.Select(entry => entry.FullName)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var required in requiredEntries)
            {
                if (!names.Contains(required))
                {
                    throw new InvalidOperationException(
                        $"The DCS mission archive is missing {required}.");
                }
            }
        }

        private static DcsPoint GetMissionCenter(
            IReadOnlyList<ScenarioAirFlightSnapshot> flights)
        {
            if (flights.Count == 0)
                return new DcsPoint(0d, 0d, 0d);
            var points = flights.Select(flight => ToDcs(flight.Position)).ToList();
            return new DcsPoint(
                points.Average(point => point.X),
                points.Average(point => point.Y),
                points.Average(point => point.Altitude));
        }

        private static DcsPoint ToDcs(ScenarioPosition position)
        {
            return new DcsPoint(
                position.ZFeet / FeetPerMeter,
                position.XFeet / FeetPerMeter,
                position.AltitudeFeet / FeetPerMeter);
        }

        private static string DcsTask(AirFlightTaskType missionType)
        {
            return missionType switch
            {
                AirFlightTaskType.Barcap => "CAP",
                AirFlightTaskType.OcaSweep => "CAP",
                AirFlightTaskType.AirborneC2 => "AWACS",
                AirFlightTaskType.AerialRefueling => "Refueling",
                AirFlightTaskType.DeadAttack => "SEAD",
                AirFlightTaskType.FighterEscort => "Escort",
                AirFlightTaskType.SeadEscort => "SEAD",
                AirFlightTaskType.Strike => "Ground Attack",
                _ => "Nothing"
            };
        }

        private static bool IsSustained(AirFlightTaskType missionType)
        {
            return missionType == AirFlightTaskType.Barcap
                   || missionType == AirFlightTaskType.AirborneC2
                   || missionType == AirFlightTaskType.AerialRefueling
                   || missionType == AirFlightTaskType.FighterEscort
                   || missionType == AirFlightTaskType.SeadEscort;
        }

        private static string WarehouseCoalition(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => "BLUE",
                Alliance.Redfor => "RED",
                _ => "NEUTRAL"
            };
        }

        private static string CountryName(int countryId)
        {
            return countryId switch
            {
                0 => "Russia",
                2 => "USA",
                _ => $"Country {countryId}"
            };
        }

        private static string Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "0";
            return value.ToString("0.###############", CultureInfo.InvariantCulture);
        }

        private static string Lua(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", string.Empty)
                .Replace("\n", "\\n");
        }

        private static string ShortId(Guid id)
        {
            return id.ToString("N").Substring(0, 8);
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars().ToHashSet();
            return new string(value.Select(character =>
                    invalid.Contains(character) ? '_' : character)
                .ToArray());
        }

        private static double NormalizeRadians(double radians)
        {
            var normalized = radians % (Math.PI * 2d);
            return normalized < 0d ? normalized + Math.PI * 2d : normalized;
        }

        private sealed class AircraftStores
        {
            public double Fuel { get; }
            public int Flares { get; }
            public int Chaff { get; }

            public AircraftStores(double fuel, int flares, int chaff)
            {
                Fuel = fuel;
                Flares = flares;
                Chaff = chaff;
            }
        }

        private readonly struct DcsPoint
        {
            public double X { get; }
            public double Y { get; }
            public double Altitude { get; }

            public DcsPoint(double x, double y, double altitude)
            {
                X = x;
                Y = y;
                Altitude = altitude;
            }
        }
    }
}
