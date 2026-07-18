using System;
using System.Collections.Generic;
using System.Linq;
using Engine.Monobehaviours.Managers;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Models
{
    public enum ObservedAirportCondition
    {
        Intact = 0,
        Damaged = 1,
        NonFunctional = 2
    }

    public sealed class AirPlanningSnapshot
    {
        public Alliance Alliance { get; }
        public DateTime CurrentTime { get; }
        public int TileDistanceKm { get; }
        public IReadOnlyList<AirPlanningSquadronSnapshot> FriendlySquadrons { get; }
        public IReadOnlyList<Vector3Int> FriendlyAirportTiles { get; }
        public IReadOnlyList<Vector3Int> FriendlyAirfieldTiles { get; }
        public IReadOnlyList<ObservedEnemyAirportSnapshot> EnemyAirports { get; }
        public IReadOnlyList<Vector3Int> FriendlyFrontlineDivisionTiles { get; }
        public IReadOnlyList<Vector3Int> FriendlyControlledTileIds { get; }
        public IReadOnlyList<Vector3Int> HostileControlledTileIds { get; }

        public AirPlanningSnapshot(
            Alliance alliance,
            DateTime currentTime,
            int tileDistanceKm,
            IReadOnlyList<AirPlanningSquadronSnapshot> friendlySquadrons,
            IReadOnlyList<Vector3Int> friendlyAirportTiles,
            IReadOnlyList<Vector3Int> friendlyAirfieldTiles,
            IReadOnlyList<ObservedEnemyAirportSnapshot> enemyAirports,
            IReadOnlyList<Vector3Int> friendlyFrontlineDivisionTiles,
            IReadOnlyList<Vector3Int> friendlyControlledTileIds,
            IReadOnlyList<Vector3Int> hostileControlledTileIds)
        {
            Alliance = alliance;
            CurrentTime = currentTime;
            TileDistanceKm = Math.Max(1, tileDistanceKm);
            FriendlySquadrons = friendlySquadrons;
            FriendlyAirportTiles = friendlyAirportTiles;
            FriendlyAirfieldTiles = friendlyAirfieldTiles;
            EnemyAirports = enemyAirports
                            ?? Array.Empty<ObservedEnemyAirportSnapshot>();
            FriendlyFrontlineDivisionTiles = friendlyFrontlineDivisionTiles
                                             ?? Array.Empty<Vector3Int>();
            FriendlyControlledTileIds = friendlyControlledTileIds
                                        ?? Array.Empty<Vector3Int>();
            HostileControlledTileIds = hostileControlledTileIds
                                       ?? Array.Empty<Vector3Int>();
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

    public sealed class ObservedEnemyAirportSnapshot
    {
        public Guid AirportBuildingId { get; }
        public Vector3Int AirportTileId { get; }
        public ObservedAirportCondition Condition { get; }
        public IReadOnlyList<ObservedAircraftGroup> AircraftGroups { get; }

        public ObservedEnemyAirportSnapshot(
            Guid airportBuildingId,
            Vector3Int airportTileId,
            ObservedAirportCondition condition,
            IReadOnlyList<ObservedAircraftGroup> aircraftGroups)
        {
            AirportBuildingId = airportBuildingId;
            AirportTileId = airportTileId;
            Condition = condition;
            AircraftGroups = aircraftGroups ?? Array.Empty<ObservedAircraftGroup>();
        }
    }

    public sealed class ObservedAircraftGroup
    {
        public Guid AircraftTypeDefinitionId { get; }
        public int AircraftOnGroundCount { get; }
        public int ApparentlyAvailableCount { get; }

        public ObservedAircraftGroup(
            Guid aircraftTypeDefinitionId,
            int aircraftOnGroundCount,
            int apparentlyAvailableCount)
        {
            AircraftTypeDefinitionId = aircraftTypeDefinitionId;
            AircraftOnGroundCount = Math.Max(0, aircraftOnGroundCount);
            ApparentlyAvailableCount = Math.Max(0, apparentlyAvailableCount);
        }
    }

    public sealed class AirPlanningIntelligence
    {
        private readonly GameManager gameManager;

        public AirPlanningIntelligence(GameManager gameManager)
        {
            this.gameManager = gameManager;
        }

        public AirPlanningSnapshot CreateSnapshot(
            Alliance alliance,
            HashSet<Guid> airborneAircraftIds)
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
                GetFriendlyAirfieldTiles(alliance),
                GetEnemyAirports(alliance, airborneAircraftIds),
                GetFriendlyFrontlineDivisionTiles(alliance),
                GetControlledLandTiles(alliance),
                GetHostileControlledLandTiles(alliance));
        }

        private IReadOnlyList<Vector3Int> GetFriendlyFrontlineDivisionTiles(
            Alliance alliance)
        {
            var front = gameManager.GetGroundTaskingCommander(alliance)?
                .FrontTileIds?
                .ToHashSet() ?? new HashSet<Vector3Int>();
            if (front.Count == 0)
                return Array.Empty<Vector3Int>();

            return gameManager.divisionSystem.Divisions
                .Where(division => division != null
                                   && division.Strength > 0f
                                   && gameManager.GetCountryAlliance(
                                       division.CountryId) == alliance
                                   && front.Contains(division.TileId))
                .Select(division => division.TileId)
                .OrderBy(tile => tile.x)
                .ThenBy(tile => tile.y)
                .ThenBy(tile => tile.z)
                .ToList();
        }

        private IReadOnlyList<Vector3Int> GetControlledLandTiles(
            Alliance alliance)
        {
            return gameManager.Tiles
                .OfType<LandTileData>()
                .Where(tile => tile.Controller == alliance)
                .Select(tile => tile.TileId)
                .Distinct()
                .OrderBy(tile => tile.x)
                .ThenBy(tile => tile.y)
                .ThenBy(tile => tile.z)
                .ToList();
        }

        private IReadOnlyList<Vector3Int> GetHostileControlledLandTiles(
            Alliance alliance)
        {
            return gameManager.Tiles
                .OfType<LandTileData>()
                .Where(tile => IsHostile(alliance, tile.Controller))
                .Select(tile => tile.TileId)
                .Distinct()
                .OrderBy(tile => tile.x)
                .ThenBy(tile => tile.y)
                .ThenBy(tile => tile.z)
                .ToList();
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
                .Where(airport => controllersByTileId.TryGetValue(
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

        private IReadOnlyList<ObservedEnemyAirportSnapshot> GetEnemyAirports(
            Alliance observingAlliance,
            HashSet<Guid> airborneAircraftIds)
        {
            var controllersByTileId = gameManager.Tiles
                .OfType<LandTileData>()
                .GroupBy(tile => tile.TileId)
                .ToDictionary(group => group.Key, group => group.First().Controller);

            return gameManager.buildingSystem
                .GetBuildings<Airport>()
                .Where(airport =>
                    controllersByTileId.TryGetValue(
                        airport.TileId,
                        out var controller)
                    && IsHostile(observingAlliance, controller))
                .Select(airport => new ObservedEnemyAirportSnapshot(
                    airport.BuildingId,
                    airport.TileId,
                    GetObservedCondition(airport),
                    GetObservedAircraft(
                        observingAlliance,
                        airport.BuildingId,
                        airborneAircraftIds)))
                .OrderBy(report => report.AirportTileId.x)
                .ThenBy(report => report.AirportTileId.y)
                .ThenBy(report => report.AirportTileId.z)
                .ToList();
        }

        private IReadOnlyList<ObservedAircraftGroup> GetObservedAircraft(
            Alliance observingAlliance,
            Guid airportBuildingId,
            HashSet<Guid> airborneAircraftIds)
        {
            return gameManager.squadronSystem.Squadrons
                .Where(squadron =>
                    squadron.AirportBuildingId == airportBuildingId
                    && IsHostile(
                        observingAlliance,
                        gameManager.GetCountryAlliance(squadron.CountryId)))
                .SelectMany(squadron => squadron.Aircraft)
                .Where(aircraft =>
                    aircraft.Status != CampaignAircraftStatus.Lost
                    && !airborneAircraftIds.Contains(aircraft.AircraftId))
                .GroupBy(aircraft => aircraft.AircraftTypeDefinitionId)
                .Select(group => new ObservedAircraftGroup(
                    group.Key,
                    group.Count(),
                    group.Count(IsApparentlyAvailable)))
                .OrderBy(group => group.AircraftTypeDefinitionId)
                .ToList();
        }

        private static bool IsApparentlyAvailable(CampaignAircraft aircraft)
        {
            return aircraft.Status == CampaignAircraftStatus.Ready
                   || aircraft.Status == CampaignAircraftStatus.Assigned;
        }

        private static ObservedAirportCondition GetObservedCondition(
            Airport airport)
        {
            if (airport.FunctionalLevel <= 0)
                return ObservedAirportCondition.NonFunctional;

            return airport.Level.Damage > 0
                ? ObservedAirportCondition.Damaged
                : ObservedAirportCondition.Intact;
        }

        private static bool IsHostile(
            Alliance observingAlliance,
            Alliance subjectAlliance)
        {
            return observingAlliance != Alliance.Neutral
                   && subjectAlliance != Alliance.Neutral
                   && observingAlliance != subjectAlliance;
        }

    }
}
