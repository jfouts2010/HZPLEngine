using System;
using System.Collections.Generic;
using Models.Gameplay.Campaign;

namespace Models.Module
{
    public readonly struct ScenarioPosition
    {
        public float XFeet { get; }
        public float AltitudeFeet { get; }
        public float ZFeet { get; }

        public ScenarioPosition(float xFeet, float altitudeFeet, float zFeet)
        {
            XFeet = xFeet;
            AltitudeFeet = altitudeFeet;
            ZFeet = zFeet;
        }
    }

    public sealed class ScenarioLoadoutItemSnapshot
    {
        public Guid OrdnanceTypeDefinitionId { get; }
        public int Count { get; }

        public ScenarioLoadoutItemSnapshot(
            Guid ordnanceTypeDefinitionId,
            int count)
        {
            OrdnanceTypeDefinitionId = ordnanceTypeDefinitionId;
            Count = Math.Max(0, count);
        }
    }

    public sealed class ScenarioStationLoadSnapshot
    {
        public Guid AircraftLoadoutStationDefinitionId { get; }
        public string StationThirdPartyId { get; }
        public Guid AircraftCarriageConfigurationDefinitionId { get; }
        public string CarriageThirdPartyId { get; }
        public IReadOnlyList<ScenarioLoadoutItemSnapshot> Contents { get; }
        public bool IsPartiallyExpended { get; }

        public ScenarioStationLoadSnapshot(
            Guid aircraftLoadoutStationDefinitionId,
            string stationThirdPartyId,
            Guid aircraftCarriageConfigurationDefinitionId,
            string carriageThirdPartyId,
            IReadOnlyList<ScenarioLoadoutItemSnapshot> contents,
            bool isPartiallyExpended)
        {
            AircraftLoadoutStationDefinitionId =
                aircraftLoadoutStationDefinitionId;
            StationThirdPartyId = stationThirdPartyId ?? string.Empty;
            AircraftCarriageConfigurationDefinitionId =
                aircraftCarriageConfigurationDefinitionId;
            CarriageThirdPartyId = carriageThirdPartyId ?? string.Empty;
            Contents = contents ?? Array.Empty<ScenarioLoadoutItemSnapshot>();
            IsPartiallyExpended = isPartiallyExpended;
        }
    }

    public sealed class ScenarioAircraftSnapshot
    {
        public Guid AircraftId { get; }
        public IReadOnlyList<ScenarioStationLoadSnapshot> ExternalStationLoads
        {
            get;
        }
        public IReadOnlyList<ScenarioLoadoutItemSnapshot> InternalOrdnance
        {
            get;
        }

        public ScenarioAircraftSnapshot(
            Guid aircraftId,
            IReadOnlyList<ScenarioStationLoadSnapshot> externalStationLoads,
            IReadOnlyList<ScenarioLoadoutItemSnapshot> internalOrdnance = null)
        {
            AircraftId = aircraftId;
            ExternalStationLoads = externalStationLoads
                                   ?? Array.Empty<ScenarioStationLoadSnapshot>();
            InternalOrdnance = internalOrdnance
                               ?? Array.Empty<ScenarioLoadoutItemSnapshot>();
        }
    }

    public sealed class ScenarioWaypointSnapshot
    {
        public Guid WaypointId { get; }
        public ScenarioPosition Position { get; }
        public AirWaypointAction Action { get; }
        public DateTime PlannedArrivalTime { get; }
        public bool HasRepeat { get; }
        public DateTime RepeatUntil { get; }
        public int AirportThirdPartyId { get; }

        public ScenarioWaypointSnapshot(
            Guid waypointId,
            ScenarioPosition position,
            AirWaypointAction action,
            DateTime plannedArrivalTime,
            bool hasRepeat,
            DateTime repeatUntil,
            int airportThirdPartyId)
        {
            WaypointId = waypointId;
            Position = position;
            Action = action;
            PlannedArrivalTime = plannedArrivalTime;
            HasRepeat = hasRepeat;
            RepeatUntil = repeatUntil;
            AirportThirdPartyId = airportThirdPartyId;
        }
    }

    public sealed class ScenarioAirFlightSnapshot
    {
        public Guid FlightId { get; }
        public Guid CountryId { get; }
        public Alliance Alliance { get; }
        public AirFlightTaskType TaskType { get; }
        public FlightExecutionPhase ExecutionPhase { get; }
        public string AircraftThirdPartyId { get; }
        public ScenarioPosition Position { get; }
        public float HeadingDegrees { get; }
        public float SpeedKnots { get; }
        public IReadOnlyList<ScenarioAircraftSnapshot> Aircraft { get; }
        public IReadOnlyList<ScenarioWaypointSnapshot> RemainingRoute { get; }

        public ScenarioAirFlightSnapshot(
            Guid flightId,
            Guid countryId,
            Alliance alliance,
            AirFlightTaskType missionType,
            FlightExecutionPhase executionPhase,
            string aircraftThirdPartyId,
            ScenarioPosition position,
            float headingDegrees,
            float speedKnots,
            IReadOnlyList<ScenarioAircraftSnapshot> aircraft,
            IReadOnlyList<ScenarioWaypointSnapshot> remainingRoute)
        {
            FlightId = flightId;
            CountryId = countryId;
            Alliance = alliance;
            TaskType = missionType;
            ExecutionPhase = executionPhase;
            AircraftThirdPartyId = aircraftThirdPartyId ?? string.Empty;
            Position = position;
            HeadingDegrees = headingDegrees;
            SpeedKnots = Math.Max(0f, speedKnots);
            Aircraft = aircraft ?? Array.Empty<ScenarioAircraftSnapshot>();
            RemainingRoute = remainingRoute ?? Array.Empty<ScenarioWaypointSnapshot>();
        }
    }

    public sealed class ScenarioAirportSnapshot
    {
        public Guid BuildingId { get; }
        public int ThirdPartyId { get; }
        public Alliance Alliance { get; }
        public ScenarioPosition Position { get; }
        public bool IsOperational { get; }

        public ScenarioAirportSnapshot(
            Guid buildingId,
            int thirdPartyId,
            Alliance alliance,
            ScenarioPosition position,
            bool isOperational)
        {
            BuildingId = buildingId;
            ThirdPartyId = thirdPartyId;
            Alliance = alliance;
            Position = position;
            IsOperational = isOperational;
        }
    }

    public sealed class ScenarioSamComponentSnapshot
    {
        public Guid ComponentId { get; }
        public Guid ComponentDefinitionId { get; }
        public string ThirdPartyId { get; }

        public ScenarioSamComponentSnapshot(
            Guid componentId,
            Guid componentDefinitionId,
            string thirdPartyId)
        {
            ComponentId = componentId;
            ComponentDefinitionId = componentDefinitionId;
            ThirdPartyId = thirdPartyId ?? string.Empty;
        }
    }

    public sealed class ScenarioSamSiteSnapshot
    {
        public Guid SiteId { get; }
        public Guid CountryId { get; }
        public Alliance Alliance { get; }
        public ScenarioPosition Position { get; }
        public IReadOnlyList<ScenarioSamComponentSnapshot> Components { get; }

        public ScenarioSamSiteSnapshot(
            Guid siteId,
            Guid countryId,
            Alliance alliance,
            ScenarioPosition position,
            IReadOnlyList<ScenarioSamComponentSnapshot> components)
        {
            SiteId = siteId;
            CountryId = countryId;
            Alliance = alliance;
            Position = position;
            Components = components ?? Array.Empty<ScenarioSamComponentSnapshot>();
        }
    }

    public sealed class ScenarioExportSnapshot
    {
        public Guid ModuleId { get; }
        public string CampaignName { get; }
        public DateTime CurrentTime { get; }
        public IReadOnlyList<ScenarioAirportSnapshot> Airports { get; }
        public IReadOnlyList<ScenarioAirFlightSnapshot> AirborneFlights { get; }
        public IReadOnlyList<ScenarioSamSiteSnapshot> SamSites { get; }
        public IReadOnlyList<string> Warnings { get; }

        public ScenarioExportSnapshot(
            Guid moduleId,
            string campaignName,
            DateTime currentTime,
            IReadOnlyList<ScenarioAirportSnapshot> airports,
            IReadOnlyList<ScenarioAirFlightSnapshot> airborneFlights,
            IReadOnlyList<ScenarioSamSiteSnapshot> samSites,
            IReadOnlyList<string> warnings)
        {
            ModuleId = moduleId;
            CampaignName = string.IsNullOrWhiteSpace(campaignName)
                ? "HZPL Campaign"
                : campaignName.Trim();
            CurrentTime = currentTime;
            Airports = airports ?? Array.Empty<ScenarioAirportSnapshot>();
            AirborneFlights = airborneFlights
                              ?? Array.Empty<ScenarioAirFlightSnapshot>();
            SamSites = samSites ?? Array.Empty<ScenarioSamSiteSnapshot>();
            Warnings = warnings ?? Array.Empty<string>();
        }
    }

    public sealed class ScenarioExportArtifact
    {
        public string SuggestedFileName { get; }
        public byte[] Content { get; }
        public IReadOnlyList<string> Warnings { get; }

        public ScenarioExportArtifact(
            string suggestedFileName,
            byte[] content,
            IReadOnlyList<string> warnings)
        {
            SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName)
                ? "HZPL-AI-Observation.miz"
                : suggestedFileName;
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Warnings = warnings ?? Array.Empty<string>();
        }
    }
}
