using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using Models.Module;
using Monobehaviours.Singletons;
using UnityEngine;

namespace Engine.Models
{
    public sealed class IADSSystem
    {
        private readonly GameManager gameManager;
        private readonly AllianceIADS blueforIads;
        private readonly AllianceIADS redforIads;

        public IADSSystem(GameManager gameManager)
            : this(gameManager, new AllianceIADS(Alliance.Bluefor), new AllianceIADS(Alliance.Redfor))
        {
        }

        public IADSSystem(GameManager gameManager, AllianceIADS blueforIads, AllianceIADS redforIads)
        {
            this.gameManager = gameManager;
            this.blueforIads = blueforIads;
            this.redforIads = redforIads;
            this.blueforIads.Alliance = Alliance.Bluefor;
            this.redforIads.Alliance = Alliance.Redfor;
        }

        public AllianceIADS GetAllianceIADS(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => blueforIads,
                Alliance.Redfor => redforIads,
                _ => null
            };
        }

        public void TacticalTurn()
        {
            var activeModule = ModuleSingleton.Instance.ActiveModule;
            var aircraftTypeDefinitions = activeModule.AircraftTypeDefinitions
                .ToDictionary(definition => definition.AircraftTypeDefinitionId);
            var radarDefinitionLookup = activeModule.SamComponentDefinitions
                .OfType<RadarAirDefenseComponentDefinition>()
                .ToDictionary(definition => definition.SamComponentDefinitionId);
            var airDefenseSites = gameManager.airDefenseSiteSystem.Sites.ToList();
            var tileDistanceKm = gameManager.SimulationSettings.TileDistanceKM;
            var activeFlights = gameManager.GetAirborneFlights().ToList();
            var flightContexts = BuildFlightContexts(activeFlights);
            blueforIads.RefreshTracks(
                activeFlights,
                flightContexts.AllianceByFlightId,
                flightContexts.AircraftTypeByFlightId,
                flightContexts.AircraftCountByFlightId,
                airDefenseSites,
                gameManager.airDefenseSiteSystem,
                radarDefinitionLookup,
                aircraftTypeDefinitions,
                tileDistanceKm);
            redforIads.RefreshTracks(
                activeFlights,
                flightContexts.AllianceByFlightId,
                flightContexts.AircraftTypeByFlightId,
                flightContexts.AircraftCountByFlightId,
                airDefenseSites,
                gameManager.airDefenseSiteSystem,
                radarDefinitionLookup,
                aircraftTypeDefinitions,
                tileDistanceKm);
        }

        private FlightContexts BuildFlightContexts(IEnumerable<AirFlight> flights)
        {
            var squadronById = gameManager.squadronSystem.Squadrons
                .GroupBy(squadron => squadron.SquadronId)
                .ToDictionary(group => group.Key, group => group.First());

            var contexts = new FlightContexts();
            foreach (var flight in flights)
            {
                if (flight == null
                    || !squadronById.TryGetValue(flight.SquadronId, out var squadron))
                    continue;

                contexts.AllianceByFlightId[flight.FlightId] =
                    gameManager.GetCountryAlliance(squadron.CountryId);
                contexts.AircraftTypeByFlightId[flight.FlightId] =
                    squadron.AircraftTypeDefinitionId;
                contexts.AircraftCountByFlightId[flight.FlightId] =
                    (squadron.Aircraft)
                    .Count(aircraft => aircraft.AssignedFlightId == flight.FlightId
                                       && aircraft.Status != CampaignAircraftStatus.Lost);
            }

            return contexts;
        }

        private sealed class FlightContexts
        {
            public readonly Dictionary<Guid, Alliance> AllianceByFlightId =
                new Dictionary<Guid, Alliance>();

            public readonly Dictionary<Guid, Guid> AircraftTypeByFlightId =
                new Dictionary<Guid, Guid>();

            public readonly Dictionary<Guid, int> AircraftCountByFlightId =
                new Dictionary<Guid, int>();
        }
    }
}