using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Models.Gameplay.Campaign;
using Models.Module;
using NUnit.Framework;

public sealed class DcsAiObservationMissionExporterTests
{
    [Test]
    public void Export_UsesEnrouteTaskSchemaForAwacsAndTankers()
    {
        var flights = new[]
        {
            CreateFlight(
                TestModule.BlueCountryId,
                Alliance.Bluefor,
                AirFlightTaskType.AerialRefueling,
                "KC-135"),
            CreateFlight(
                TestModule.RedCountryId,
                Alliance.Redfor,
                AirFlightTaskType.AirborneC2,
                "A-50")
        };
        var snapshot = new ScenarioExportSnapshot(
            DcsPrototypeModule.Id,
            "Support task regression",
            new DateTime(1990, 1, 1, 8, 0, 0),
            Array.Empty<ScenarioAirportSnapshot>(),
            flights,
            Array.Empty<ScenarioSamSiteSnapshot>(),
            Array.Empty<string>());

        var artifact = DcsAiObservationMissionExporter.Export(snapshot);
        var mission = ReadArchiveEntry(artifact.Content, "mission");

        StringAssert.Contains("[\"type\"] = \"KC-135\"", mission);
        StringAssert.Contains("[\"type\"] = \"A-50\"", mission);
        StringAssert.Contains("[\"id\"] = \"Tanker\"", mission);
        StringAssert.Contains("[\"id\"] = \"AWACS\"", mission);
        StringAssert.DoesNotContain(
            "[\"action\"] = { [\"id\"] = \"Tanker\"",
            mission,
            "Tanker must be a DCS enroute task, not a wrapped command.");
        StringAssert.DoesNotContain(
            "[\"action\"] = { [\"id\"] = \"AWACS\"",
            mission,
            "AWACS must be a DCS enroute task, not a wrapped command.");
    }

    [Test]
    public void Export_IncludesRealtimeSamRadarStateMonitor()
    {
        var siteId = Guid.Parse("12345678-1111-2222-3333-444444444444");
        var samSite = CreateOsaSite(siteId);
        var snapshot = new ScenarioExportSnapshot(
            DcsPrototypeModule.Id,
            "Radar debug regression",
            new DateTime(1990, 1, 1, 8, 0, 0),
            Array.Empty<ScenarioAirportSnapshot>(),
            new[]
            {
                CreateFlight(
                    TestModule.BlueCountryId,
                    Alliance.Bluefor,
                    AirFlightTaskType.DeadAttack,
                    "F-16C_50")
            },
            new[] { samSite },
            Array.Empty<string>());

        var artifact = DcsAiObservationMissionExporter.Export(snapshot);
        var mission = ReadArchiveEntry(artifact.Content, "mission");
        var mapResource = ReadArchiveEntry(
            artifact.Content,
            "l10n/DEFAULT/mapResource");
        var script = ReadArchiveEntry(
            artifact.Content,
            "l10n/DEFAULT/HZPLRadarDebug.lua");

        StringAssert.Contains(
            "a_do_script_file(getValueResourceByKey(\\\"ResKey_HZPLRadarDebug\\\"))",
            mission);
        StringAssert.Contains(
            "Start HZPL SAM radar debug monitor",
            mission);
        StringAssert.Contains(
            "[\"ResKey_HZPLRadarDebug\"] = \"HZPLRadarDebug.lua\"",
            mapResource);
        StringAssert.Contains("HZPL-SAM-12345678-1", script);
        StringAssert.Contains(
            "unit:hasSensors(Unit.SensorType.RADAR)",
            script);
        StringAssert.Contains("local radarOn = unit:getRadar()", script);
        StringAssert.Contains("previous ~= state", script);
        Assert.That(
            CountOccurrences(script, "HZPL-SAM-12345678-1"),
            Is.EqualTo(1),
            "Logical Osa components must still map to one physical DCS radar unit.");
    }

    [Test]
    public void Export_UsesExcellentSkillForAirAndGroundUnits()
    {
        var snapshot = new ScenarioExportSnapshot(
            DcsPrototypeModule.Id,
            "AI skill regression",
            new DateTime(1990, 1, 1, 8, 0, 0),
            Array.Empty<ScenarioAirportSnapshot>(),
            new[]
            {
                CreateFlight(
                    TestModule.BlueCountryId,
                    Alliance.Bluefor,
                    AirFlightTaskType.DeadAttack,
                    "F-16C_50")
            },
            new[]
            {
                CreateOsaSite(
                    Guid.Parse("87654321-1111-2222-3333-444444444444"))
            },
            Array.Empty<string>());

        var artifact = DcsAiObservationMissionExporter.Export(snapshot);
        var mission = ReadArchiveEntry(artifact.Content, "mission");

        StringAssert.DoesNotContain("[\"skill\"] = \"High\"", mission);
        Assert.That(
            CountOccurrences(mission, "[\"skill\"] = \"Excellent\""),
            Is.EqualTo(2),
            "The one exported aircraft and one physical Osa must both be Excellent.");
    }

    private static ScenarioAirFlightSnapshot CreateFlight(
        Guid countryId,
        Alliance alliance,
        AirFlightTaskType missionType,
        string aircraftType)
    {
        return new ScenarioAirFlightSnapshot(
            Guid.NewGuid(),
            countryId,
            alliance,
            missionType,
            FlightExecutionPhase.Executing,
            aircraftType,
            new ScenarioPosition(100000f, 25000f, 200000f),
            90f,
            350f,
            new List<ScenarioAircraftSnapshot>
            {
                new ScenarioAircraftSnapshot(
                    Guid.NewGuid(),
                    Array.Empty<ScenarioStationLoadSnapshot>())
            },
            Array.Empty<ScenarioWaypointSnapshot>());
    }

    private static ScenarioSamSiteSnapshot CreateOsaSite(Guid siteId)
    {
        const string osaType = "Osa 9A33 ln";
        return new ScenarioSamSiteSnapshot(
            siteId,
            TestModule.RedCountryId,
            Alliance.Redfor,
            new ScenarioPosition(110000f, 0f, 210000f),
            new[]
            {
                new ScenarioSamComponentSnapshot(
                    Guid.NewGuid(),
                    TestModule.OsaRadarComponentId,
                    osaType),
                new ScenarioSamComponentSnapshot(
                    Guid.NewGuid(),
                    TestModule.OsaLauncherComponentId,
                    osaType),
                new ScenarioSamComponentSnapshot(
                    Guid.NewGuid(),
                    TestModule.OsaCommandComponentId,
                    osaType)
            });
    }

    private static string ReadArchiveEntry(byte[] content, string entryName)
    {
        using var stream = new MemoryStream(content, false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        Assert.That(entry, Is.Not.Null);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(
                   search,
                   index,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }
        return count;
    }
}
