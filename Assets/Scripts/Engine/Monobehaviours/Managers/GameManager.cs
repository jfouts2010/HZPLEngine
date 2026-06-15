using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Models.Gameplay.Campaign;
using UnityEngine;

namespace Engine.Monobehaviours.Managers
{
    public class GameManager : MonoBehaviour
    {
        public bool IsGamePaused { get; private set; }
        public bool IsCampaignStarted => _campaignStarted;
        public string TemplateName { get; private set; }
        public Guid ModuleId { get; private set; }
        public DateTime CurrentTime { get; private set; }
        public DateTime GameTime => CurrentTime;
        public SimulationSettings SimulationSettings { get; private set; } = new SimulationSettings();
        public bool AutoStartTestCampaign = true;
        public List<Tile> CampaignTiles = new List<Tile>();
        [SerializeReference] public List<TileData> Tiles = new List<TileData>();
        public BuildingCollection Buildings = new BuildingCollection();

        private Coroutine GameTurnCoroutine = null;
        private bool _campaignStarted;

        private void Start()
        {
            if (AutoStartTestCampaign && !_campaignStarted)
                StartCampaign(TestCampaign.Create());
        }

        public void StartCampaign(CampaignTemplate template)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));

            TemplateName = template.Name;
            ModuleId = template.ModuleId;
            CurrentTime = template.CampaignStartTime;
            SimulationSettings = CopySimulationSettings(template.SimulationSettings);
            CampaignTiles = CopyTiles(template.Tiles);
            Tiles = CopyTileData(template.StartingTileData);
            Buildings = new BuildingCollection
            {
                Buildings = CreateRuntimeBuildings(template.BuildingStartingConditions)
            };
            Buildings.RebuildIndex();

            IsGamePaused = false;
            _campaignStarted = true;
        }

        public void PauseCampaign()
        {
            IsGamePaused = true;
        }

        public void ResumeCampaign()
        {
            if (!_campaignStarted)
                return;

            IsGamePaused = false;
        }

        public void Update()
        {
            if (!_campaignStarted)
                return;

            if (IsGamePaused)
                return;

            if (GameTurnCoroutine == null)
                GameTurnCoroutine = StartCoroutine(SlowGameTurn());
        }

        private IEnumerator SlowGameTurn()
        {
            yield return new WaitForSeconds(0.16f);
            GameTurn();
            GameTurnCoroutine = null;
        }

        private void GameTurn()
        {
            CurrentTime = CurrentTime.AddMinutes(SimulationSettings.SimulationTickMinutes);
        }

        private static List<TileData> CopyTileData(List<TileData> startingTileData)
        {
            return (startingTileData ?? new List<TileData>())
                .Select(CopyTileData)
                .Where(tileData => tileData != null)
                .ToList();
        }

        private static List<Tile> CopyTiles(List<Tile> tiles)
        {
            return (tiles ?? new List<Tile>())
                .Where(tile => tile != null)
                .Select(CopyTile)
                .ToList();
        }

        private static Tile CopyTile(Tile tile)
        {
            return new Tile
            {
                TileId = tile.TileId,
                Coordinates = tile.Coordinates,
                NeighborTileIds = new List<Guid>(tile.NeighborTileIds ?? new List<Guid>()),
                RiverNeighborTileIds = new List<Guid>(tile.RiverNeighborTileIds ?? new List<Guid>()),
                Surface = tile.Surface,
                Terrain = tile.Terrain,
                Urbanization = tile.Urbanization,
                ForestCover = tile.ForestCover
            };
        }

        private static TileData CopyTileData(TileData data)
        {
            if (data is LandTileData landData)
            {
                return new LandTileData
                {
                    TileId = landData.TileId,
                    Controller = landData.Controller,
                    Infrastructure = CopyBuildingLevel(landData.Infrastructure)
                };
            }

            if (data is OceanTileData oceanData)
            {
                return new OceanTileData
                {
                    TileId = oceanData.TileId
                };
            }

            return null;
        }

        private static List<Building> CreateRuntimeBuildings(List<BuildingStartingCondition> startingConditions)
        {
            return (startingConditions ?? new List<BuildingStartingCondition>())
                .Where(startingCondition => startingCondition != null)
                .Select(CreateRuntimeBuilding)
                .ToList();
        }

        private static Building CreateRuntimeBuilding(BuildingStartingCondition startingCondition)
        {
            switch (startingCondition.Type)
            {
                case BuildingType.Airport:
                    return new Airport(startingCondition);
                case BuildingType.Factory:
                    return new Factory(startingCondition);
                case BuildingType.SupplyHub:
                    return new SupplyHub(startingCondition);
                case BuildingType.Fort:
                    return new Fort(startingCondition);
                case BuildingType.Port:
                    return new Port(startingCondition);
                case BuildingType.Railroad:
                    return new Railroad(startingCondition);
                case BuildingType.Refinery:
                    return new Refinery(startingCondition);
                case BuildingType.PowerPlant:
                    return new PowerPlant(startingCondition);
                default:
                    throw new ArgumentOutOfRangeException(nameof(startingCondition.Type), startingCondition.Type, "Unknown building type.");
            }
        }

        private static BuildingLevel CopyBuildingLevel(BuildingLevel level)
        {
            return level == null
                ? new BuildingLevel()
                : new BuildingLevel(level.BuildLevel, level.Damage);
        }

        private static SimulationSettings CopySimulationSettings(SimulationSettings settings)
        {
            if (settings == null)
                return new SimulationSettings();

            var copy = new SimulationSettings
            {
                SimulationTickMinutes = settings.SimulationTickMinutes,
                OperationalCadenceHours = settings.OperationalCadenceHours
            };

            copy.Normalize();
            return copy;
        }
    }
}
