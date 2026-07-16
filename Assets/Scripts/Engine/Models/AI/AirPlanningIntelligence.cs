using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models
{
    public interface IAirPlanningIntelligence
    {
        AirPlanningSnapshot CreateSnapshot(Alliance alliance);
    }

    public sealed class AirPlanningSnapshot
    {
        public Alliance Alliance { get; }
        public DateTime CurrentTime { get; }
        public int TileDistanceKm { get; }
        public IReadOnlyList<AirPlanningSquadronSnapshot> FriendlySquadrons { get; }
        public IReadOnlyList<Vector3Int> FriendlyAirportTiles { get; }
        public IReadOnlyList<Vector3Int> FriendlyAirfieldTiles { get; }

        public AirPlanningSnapshot(
            Alliance alliance,
            DateTime currentTime,
            int tileDistanceKm,
            IReadOnlyList<AirPlanningSquadronSnapshot> friendlySquadrons,
            IReadOnlyList<Vector3Int> friendlyAirportTiles,
            IReadOnlyList<Vector3Int> friendlyAirfieldTiles)
        {
            Alliance = alliance;
            CurrentTime = currentTime;
            TileDistanceKm = Math.Max(1, tileDistanceKm);
            FriendlySquadrons = friendlySquadrons;
            FriendlyAirportTiles = friendlyAirportTiles;
            FriendlyAirfieldTiles = friendlyAirfieldTiles;
        }
    }

    public sealed class AirPlanningSquadronSnapshot
    {
        public Guid SquadronId { get; }
        public Alliance Alliance { get; }
        public Guid AircraftTypeDefinitionId { get; }
        public Guid AirportBuildingId { get; }
        public Vector3Int AirportTileId { get; }
        public int ReadyAircraftCount { get; }
        public int AssignedAircraftCount { get; }

        public AirPlanningSquadronSnapshot(
            Guid squadronId,
            Alliance alliance,
            Guid aircraftTypeDefinitionId,
            Guid airportBuildingId,
            Vector3Int airportTileId,
            int readyAircraftCount,
            int assignedAircraftCount)
        {
            SquadronId = squadronId;
            Alliance = alliance;
            AircraftTypeDefinitionId = aircraftTypeDefinitionId;
            AirportBuildingId = airportBuildingId;
            AirportTileId = airportTileId;
            ReadyAircraftCount = Math.Max(0, readyAircraftCount);
            AssignedAircraftCount = Math.Max(0, assignedAircraftCount);
        }
    }

    public sealed class FriendlyAirPlanningIntelligence : IAirPlanningIntelligence
    {
        private readonly GameManager gameManager;

        public FriendlyAirPlanningIntelligence(GameManager gameManager)
        {
            this.gameManager = gameManager;
        }

        public AirPlanningSnapshot CreateSnapshot(Alliance alliance)
        {
            var friendlySquadrons = new List<AirPlanningSquadronSnapshot>();

            foreach (var squadron in gameManager.squadronSystem.Squadrons
                         .OrderBy(candidate => candidate.SquadronId))
            {
                var squadronAlliance = gameManager.GetCountryAlliance(squadron.CountryId);
                if (squadronAlliance != alliance)
                    continue;

                if (!gameManager.buildingSystem.TryGetBuilding(
                        squadron.AirportBuildingId,
                        out var airportBuilding)
                    || airportBuilding is not Airport
                    || airportBuilding.FunctionalLevel <= 0)
                    continue;

                var snapshot = new AirPlanningSquadronSnapshot(
                    squadron.SquadronId,
                    squadronAlliance,
                    squadron.AircraftTypeDefinitionId,
                    squadron.AirportBuildingId,
                    airportBuilding.TileId,
                    squadron.ReadyAircraft,
                    squadron.AssignedAircraft);

                friendlySquadrons.Add(snapshot);
            }

            return new AirPlanningSnapshot(
                alliance,
                gameManager.CurrentTime,
                gameManager.SimulationSettings.TileDistanceKM,
                friendlySquadrons,
                GetAirportTiles(alliance),
                GetFriendlyAirfieldTiles(alliance));
        }

        private IReadOnlyList<Vector3Int> GetFriendlyAirfieldTiles(
            Alliance alliance)
        {
            var controllersByTileId = gameManager.Tiles
                .OfType<LandTileData>()
                .GroupBy(tile => tile.TileId)
                .ToDictionary(group => group.Key, group => group.First().Controller);
            return gameManager.buildingSystem
                .GetBuildings<Airport>()
                .Where(airport => airport.FunctionalLevel > 0
                                  && controllersByTileId.TryGetValue(
                                      airport.TileId,
                                      out var controller)
                                  && controller == alliance)
                .Select(airport => airport.TileId)
                .Distinct()
                .OrderBy(tile => tile.x)
                .ThenBy(tile => tile.y)
                .ThenBy(tile => tile.z)
                .ToList();
        }

        private IReadOnlyList<Vector3Int> GetAirportTiles(Alliance alliance)
        {
            var airportTiles = new HashSet<Vector3Int>();
            foreach (var squadron in gameManager.squadronSystem.Squadrons)
            {
                var squadronAlliance = gameManager.GetCountryAlliance(squadron.CountryId);
                if (squadronAlliance != alliance
                    || !gameManager.buildingSystem.TryGetBuilding(squadron.AirportBuildingId, out var building)
                    || building is not Airport
                    || building.FunctionalLevel <= 0)
                    continue;

                airportTiles.Add(building.TileId);
            }

            return airportTiles
                .OrderBy(tile => tile.x)
                .ThenBy(tile => tile.y)
                .ThenBy(tile => tile.z)
                .ToList();
        }

    }
}
