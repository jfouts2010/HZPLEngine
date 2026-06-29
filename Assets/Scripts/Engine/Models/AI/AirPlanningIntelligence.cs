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
        public IReadOnlyList<AirPlanningSquadronSnapshot> HostileSquadrons { get; }
        public IReadOnlyList<Vector3Int> FriendlyAirportTiles { get; }
        public IReadOnlyList<Vector3Int> HostileAirportTiles { get; }

        public AirPlanningSnapshot(
            Alliance alliance,
            DateTime currentTime,
            int tileDistanceKm,
            IReadOnlyList<AirPlanningSquadronSnapshot> friendlySquadrons,
            IReadOnlyList<AirPlanningSquadronSnapshot> hostileSquadrons,
            IReadOnlyList<Vector3Int> friendlyAirportTiles,
            IReadOnlyList<Vector3Int> hostileAirportTiles)
        {
            Alliance = alliance;
            CurrentTime = currentTime;
            TileDistanceKm = Math.Max(1, tileDistanceKm);
            FriendlySquadrons = friendlySquadrons ?? Array.Empty<AirPlanningSquadronSnapshot>();
            HostileSquadrons = hostileSquadrons ?? Array.Empty<AirPlanningSquadronSnapshot>();
            FriendlyAirportTiles = friendlyAirportTiles ?? Array.Empty<Vector3Int>();
            HostileAirportTiles = hostileAirportTiles ?? Array.Empty<Vector3Int>();
        }
    }

    public sealed class AirPlanningSquadronSnapshot
    {
        public Guid SquadronId { get; }
        public Guid AircraftTypeDefinitionId { get; }
        public Guid AirportBuildingId { get; }
        public Vector3Int AirportTileId { get; }
        public int ReadyAircraftCount { get; }
        public int AssignedAircraftCount { get; }

        public AirPlanningSquadronSnapshot(
            Guid squadronId,
            Guid aircraftTypeDefinitionId,
            Guid airportBuildingId,
            Vector3Int airportTileId,
            int readyAircraftCount,
            int assignedAircraftCount)
        {
            SquadronId = squadronId;
            AircraftTypeDefinitionId = aircraftTypeDefinitionId;
            AirportBuildingId = airportBuildingId;
            AirportTileId = airportTileId;
            ReadyAircraftCount = Math.Max(0, readyAircraftCount);
            AssignedAircraftCount = Math.Max(0, assignedAircraftCount);
        }
    }

    public sealed class PerfectAirPlanningIntelligence : IAirPlanningIntelligence
    {
        private readonly GameManager gameManager;

        public PerfectAirPlanningIntelligence(GameManager gameManager)
        {
            this.gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
        }

        public AirPlanningSnapshot CreateSnapshot(Alliance alliance)
        {
            var friendlySquadrons = new List<AirPlanningSquadronSnapshot>();
            var hostileSquadrons = new List<AirPlanningSquadronSnapshot>();

            foreach (var squadron in (gameManager.squadronSystem?.Squadrons ?? new List<Squadron>())
                         .Where(candidate => candidate != null)
                         .OrderBy(candidate => candidate.SquadronId))
            {
                if (!gameManager.buildingSystem.TryGetBuilding(
                        squadron.AirportBuildingId,
                        out var airportBuilding)
                    || airportBuilding is not Airport
                    || airportBuilding.FunctionalLevel <= 0)
                    continue;

                var snapshot = new AirPlanningSquadronSnapshot(
                    squadron.SquadronId,
                    squadron.AircraftTypeDefinitionId,
                    squadron.AirportBuildingId,
                    airportBuilding.TileId,
                    squadron.ReadyAircraft,
                    squadron.AssignedAircraft);
                var squadronAlliance = gameManager.GetCountryAlliance(squadron.CountryId);

                if (squadronAlliance == alliance)
                    friendlySquadrons.Add(snapshot);
                else if (AreHostile(alliance, squadronAlliance))
                    hostileSquadrons.Add(snapshot);
            }

            return new AirPlanningSnapshot(
                alliance,
                gameManager.CurrentTime,
                gameManager.SimulationSettings?.TileDistanceKM ?? SimulationSettings.DefaultTileDistanceKM,
                friendlySquadrons,
                hostileSquadrons,
                GetAirportTiles(alliance, friendly: true),
                GetAirportTiles(alliance, friendly: false));
        }

        private IReadOnlyList<Vector3Int> GetAirportTiles(Alliance alliance, bool friendly)
        {
            var airportTiles = new HashSet<Vector3Int>();
            foreach (var squadron in gameManager.squadronSystem?.Squadrons ?? new List<Squadron>())
            {
                if (squadron == null)
                    continue;

                var squadronAlliance = gameManager.GetCountryAlliance(squadron.CountryId);
                var include = friendly
                    ? squadronAlliance == alliance
                    : AreHostile(alliance, squadronAlliance);
                if (!include
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

        private static bool AreHostile(Alliance first, Alliance second)
        {
            return (first == Alliance.Bluefor && second == Alliance.Redfor)
                   || (first == Alliance.Redfor && second == Alliance.Bluefor);
        }
    }
}
