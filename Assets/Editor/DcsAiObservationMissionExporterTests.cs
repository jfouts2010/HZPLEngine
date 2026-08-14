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
                AirMissionRequestType.ProvideAerialRefueling,
                "KC-135"),
            CreateFlight(
                TestModule.RedCountryId,
                Alliance.Redfor,
                AirMissionRequestType.ProvideAirborneC2,
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

    private static ScenarioAirFlightSnapshot CreateFlight(
        Guid countryId,
        Alliance alliance,
        AirMissionRequestType missionType,
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

    private static string ReadArchiveEntry(byte[] content, string entryName)
    {
        using var stream = new MemoryStream(content, false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        Assert.That(entry, Is.Not.Null);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
