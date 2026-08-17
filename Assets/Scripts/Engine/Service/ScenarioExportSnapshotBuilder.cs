using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;

namespace Engine.Service
{
    public static class ScenarioExportSnapshotBuilder
    {
        public static ScenarioExportSnapshot Capture(
            GameManager gameManager,
            ModuleDefinition module)
        {
            if (gameManager == null)
                throw new ArgumentNullException(nameof(gameManager));
            if (module == null)
                throw new ArgumentNullException(nameof(module));
            if (!gameManager.IsCampaignStarted)
                throw new InvalidOperationException(
                    "A campaign must be running before it can be exported.");
            if (!gameManager.IsGamePaused)
                throw new InvalidOperationException(
                    "Pause the campaign before capturing a DCS export snapshot.");
            if (gameManager.ModuleId != module.Id)
                throw new InvalidOperationException(
                    "The active module does not match the running campaign.");

            var warnings = new List<string>();
            var airports = CaptureAirports(gameManager, warnings);
            var flights = CaptureFlights(gameManager, module, airports, warnings);
            var samSites = CaptureSamSites(gameManager, module, warnings);

            return new ScenarioExportSnapshot(
                module.Id,
                gameManager.TemplateName,
                gameManager.CurrentTime,
                airports,
                flights,
                samSites,
                warnings);
        }

        private static List<ScenarioAirportSnapshot> CaptureAirports(
            GameManager gameManager,
            ICollection<string> warnings)
        {
            var airports = new List<ScenarioAirportSnapshot>();
            foreach (var airport in gameManager.buildingSystem
                         .GetBuildings<Airport>()
                         .OrderBy(candidate => candidate.BuildingId))
            {
                if (!int.TryParse(airport.ThirdPartyId, out var thirdPartyId)
                    || thirdPartyId <= 0)
                {
                    warnings.Add(
                        $"Airport {airport.BuildingId} has no numeric DCS airbase ID and was omitted.");
                    continue;
                }

                var alliance = Alliance.Neutral;
                if (gameManager.tileSystem.TryGetLand(
                        airport.TileId,
                        out var landTile))
                {
                    alliance = landTile.Controller;
                }

                airports.Add(new ScenarioAirportSnapshot(
                    airport.BuildingId,
                    thirdPartyId,
                    alliance,
                    Position(airport.PositionFeet),
                    AirportOperationsRules.IsOperational(airport)));
            }

            return airports;
        }

        private static List<ScenarioAirFlightSnapshot> CaptureFlights(
            GameManager gameManager,
            ModuleDefinition module,
            IReadOnlyList<ScenarioAirportSnapshot> airports,
            ICollection<string> warnings)
        {
            var aircraftTypes = (module.AircraftTypeDefinitions
                                 ?? new List<AircraftTypeDefinition>())
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            var airportIds = airports.ToDictionary(
                airport => airport.BuildingId,
                airport => airport.ThirdPartyId);
            var flights = new List<ScenarioAirFlightSnapshot>();

            foreach (var flight in gameManager.GetAirborneFlights()
                         .Where(candidate => candidate != null
                                             && candidate.IsAirborne
                                             && candidate.HasPosition)
                         .OrderBy(candidate => candidate.FlightId))
            {
                if (!gameManager.squadronSystem.TryGetSquadron(
                        flight.SquadronId,
                        out var squadron))
                {
                    warnings.Add(
                        $"Airborne flight {flight.FlightId} has no squadron and was omitted.");
                    continue;
                }

                if (!aircraftTypes.TryGetValue(
                        squadron.AircraftTypeDefinitionId,
                        out var aircraftType)
                    || string.IsNullOrWhiteSpace(aircraftType.ThirdPartyId))
                {
                    warnings.Add(
                        $"Airborne flight {flight.FlightId} has no mapped DCS aircraft type and was omitted.");
                    continue;
                }

                var aircraft = new List<ScenarioAircraftSnapshot>();
                foreach (var aircraftId in flight.AircraftIds.OrderBy(id => id))
                {
                    if (!gameManager.squadronSystem.TryGetAircraft(
                            aircraftId,
                            out _,
                            out var campaignAircraft)
                        || campaignAircraft.Status == CampaignAircraftStatus.Lost)
                    {
                        continue;
                    }

                    var stations = aircraftType.LoadoutStations.ToDictionary(
                        station => station.AircraftLoadoutStationDefinitionId);
                    var configurations = aircraftType.CarriageConfigurations
                        .ToDictionary(configuration => configuration
                            .AircraftCarriageConfigurationDefinitionId);
                    var stationLoads = new List<ScenarioStationLoadSnapshot>();
                    foreach (var stationGroup in campaignAircraft.Loadout
                                 .Where(item => item != null
                                                && item.Count > 0
                                                && item
                                                    .AircraftLoadoutStationDefinitionId
                                                != Guid.Empty)
                                 .GroupBy(item => item
                                     .AircraftLoadoutStationDefinitionId))
                    {
                        if (!stations.TryGetValue(stationGroup.Key, out var station))
                        {
                            throw new InvalidOperationException(
                                $"Aircraft {campaignAircraft.AircraftId} has an unknown loadout station.");
                        }

                        var configurationIds = stationGroup
                            .Select(item => item
                                .AircraftCarriageConfigurationDefinitionId)
                            .Distinct()
                            .ToList();
                        if (configurationIds.Count != 1
                            || !configurations.TryGetValue(
                                configurationIds[0],
                                out var configuration)
                            || !station
                                .CompatibleCarriageConfigurationDefinitionIds
                                .Contains(configurationIds[0]))
                        {
                            throw new InvalidOperationException(
                                $"Aircraft {campaignAircraft.AircraftId} has an invalid station carriage configuration.");
                        }

                        var contents = stationGroup
                            .GroupBy(item => item.OrdnanceTypeDefinitionId)
                            .Select(group => new ScenarioLoadoutItemSnapshot(
                                group.Key,
                                group.Sum(item => item.Count)))
                            .OrderBy(item => item.OrdnanceTypeDefinitionId)
                            .ToList();
                        var currentCounts = contents.ToDictionary(
                            item => item.OrdnanceTypeDefinitionId,
                            item => item.Count);
                        var partiallyExpended = configuration.Contents.Any(
                            content => !currentCounts.TryGetValue(
                                           content.OrdnanceTypeDefinitionId,
                                           out var count)
                                       || count != content.Count)
                                                || currentCounts.Count
                                                != configuration.Contents.Count;
                        stationLoads.Add(new ScenarioStationLoadSnapshot(
                            station.AircraftLoadoutStationDefinitionId,
                            station.ThirdPartyId,
                            configuration
                                .AircraftCarriageConfigurationDefinitionId,
                            configuration.ThirdPartyId,
                            contents,
                            partiallyExpended));
                    }

                    var internalOrdnance = campaignAircraft.Loadout
                        .Where(item => item != null
                                       && item.Count > 0
                                       && item.AircraftLoadoutStationDefinitionId
                                       == Guid.Empty)
                        .Select(item => new ScenarioLoadoutItemSnapshot(
                            item.OrdnanceTypeDefinitionId,
                            item.Count))
                        .ToList();

                    aircraft.Add(new ScenarioAircraftSnapshot(
                        campaignAircraft.AircraftId,
                        stationLoads,
                        internalOrdnance));
                }

                if (aircraft.Count == 0)
                {
                    warnings.Add(
                        $"Airborne flight {flight.FlightId} has no surviving aircraft and was omitted.");
                    continue;
                }

                var route = flight.Route
                    .Skip(Math.Max(0, flight.CurrentWaypointIndex))
                    .Select(waypoint => new ScenarioWaypointSnapshot(
                        waypoint.WaypointId,
                        Position(waypoint.PositionFeet),
                        waypoint.Action,
                        waypoint.PlannedArrivalTime,
                        waypoint.HasRepeat,
                        waypoint.RepeatUntil,
                        waypoint.AirportBuildingId != Guid.Empty
                        && airportIds.TryGetValue(
                            waypoint.AirportBuildingId,
                            out var airportId)
                            ? airportId
                            : 0))
                    .ToList();

                flights.Add(new ScenarioAirFlightSnapshot(
                    flight.FlightId,
                    squadron.CountryId,
                    gameManager.GetCountryAlliance(squadron.CountryId),
                    flight.TaskType,
                    flight.ExecutionPhase,
                    aircraftType.ThirdPartyId,
                    Position(flight.PositionFeet),
                    flight.HeadingDegrees,
                    flight.SpeedKnots > 0f
                        ? flight.SpeedKnots
                        : aircraftType.CruiseSpeedKnots,
                    aircraft,
                    route));
            }

            return flights;
        }

        private static List<ScenarioSamSiteSnapshot> CaptureSamSites(
            GameManager gameManager,
            ModuleDefinition module,
            ICollection<string> warnings)
        {
            var definitions = (module.SamComponentDefinitions
                               ?? new List<AirDefenseComponentDefinition>())
                .ToDictionary(definition => definition.SamComponentDefinitionId);
            var sites = new List<ScenarioSamSiteSnapshot>();

            foreach (var site in gameManager.airDefenseSiteSystem.Sites
                         .Where(candidate => candidate != null)
                         .OrderBy(candidate => candidate.SiteId))
            {
                if (site.IsDestroyed || site.IsDisabled)
                    continue;
                if (site.IsSuppressed)
                {
                    warnings.Add(
                        $"Suppressed SAM site {site.SiteId} was omitted because the prototype export cannot reproduce a timed suppression state.");
                    continue;
                }
                if (!gameManager.airDefenseSiteSystem.TryGetPositionFeet(
                        site,
                        out var positionFeet))
                {
                    warnings.Add(
                        $"SAM site {site.SiteId} has no exportable position and was omitted.");
                    continue;
                }

                var alliance = gameManager.airDefenseSiteSystem
                    .GetEffectiveAlliance(site);
                if (alliance == Alliance.Neutral)
                    continue;

                var components = new List<ScenarioSamComponentSnapshot>();
                foreach (var component in site.Components
                             .Where(component => component != null
                                                 && !component.IsDamaged))
                {
                    if (!definitions.TryGetValue(
                            component.SamComponentDefinitionId,
                            out var definition)
                        || string.IsNullOrWhiteSpace(definition.ThirdPartyId))
                    {
                        warnings.Add(
                            $"SAM component {component.ComponentId} has no DCS unit mapping and was omitted.");
                        continue;
                    }

                    components.Add(new ScenarioSamComponentSnapshot(
                        component.ComponentId,
                        component.SamComponentDefinitionId,
                        definition.ThirdPartyId));
                }

                if (components.Count == 0)
                    continue;

                var countryId = ResolveSamCountryId(
                    gameManager,
                    site,
                    alliance);
                if (countryId == Guid.Empty)
                {
                    warnings.Add(
                        $"SAM site {site.SiteId} has no country assignment and was omitted.");
                    continue;
                }

                sites.Add(new ScenarioSamSiteSnapshot(
                    site.SiteId,
                    countryId,
                    alliance,
                    Position(positionFeet),
                    components));
            }

            return sites;
        }

        private static Guid ResolveSamCountryId(
            GameManager gameManager,
            SamSite site,
            Alliance alliance)
        {
            if (site.HostType == SamSiteHostType.MobileDivision
                && gameManager.divisionSystem.TryGetDivision(
                    site.HostId,
                    out var division))
            {
                return division.CountryId;
            }

            return gameManager.CampaignTemplate.CountryAllianceAssignments
                .Where(assignment => assignment != null
                                     && assignment.Alliance == alliance)
                .Select(assignment => assignment.CountryId)
                .FirstOrDefault();
        }

        private static ScenarioPosition Position(Vector3 positionFeet)
        {
            return new ScenarioPosition(
                positionFeet.x,
                positionFeet.y,
                positionFeet.z);
        }
    }
}
