using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Engine.Models.Ground;
using Models.Gameplay.Campaign;
using Models.Module;
using Monobehaviours.Singletons;
using UnityEngine;

namespace Engine.Monobehaviours.Managers
{
    public enum TestCampaignKind
    {
        Basic,
        Advanced
    }

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
        public TestCampaignKind SelectedTestCampaign = TestCampaignKind.Advanced;
        public CampaignTemplate CampaignTemplate { get; private set; }
        public List<Tile> CampaignTiles = new List<Tile>();
        [SerializeReference] public List<TileData> Tiles = new List<TileData>();
        private AISystem _AISystem;
        private GroundCombatSystem _groundCombatSystem;
        private GroundOperationsSystem _groundOperationsSystem;
        public BuildingSystem buildingSystem = new BuildingSystem();
        public DivisionSystem divisionSystem = new DivisionSystem();

        private Coroutine GameTurnCoroutine = null;
        private bool _campaignStarted;

        private void Start()
        {
            if (AutoStartTestCampaign && !_campaignStarted)
                StartCampaign(CreateSelectedTestCampaign());
        }

        private CampaignTemplate CreateSelectedTestCampaign()
        {
            return SelectedTestCampaign switch
            {
                TestCampaignKind.Basic => TestCampaign.Create(),
                TestCampaignKind.Advanced => AdvancedTestCampaign.Create(),
                _ => AdvancedTestCampaign.Create()
            };
        }

        public void StartCampaign(CampaignTemplate template)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));

            template.RebuildDerivedData();

            TemplateName = template.Name;
            ModuleId = template.ModuleId;
            var activeModule = ModuleSingleton.Instance.ActiveModule;
            if (activeModule.Id != template.ModuleId)
            {
                throw new InvalidOperationException(
                    $"Campaign template module {template.ModuleId} does not match active module {activeModule.Id}.");
            }

            CampaignTemplate = template;
            CurrentTime = template.CampaignStartTime;
            SimulationSettings = CopySimulationSettings(template.SimulationSettings);
            CampaignTiles = CopyTiles(template.Tiles);
            Tiles = CopyTileData(template.StartingTileData);
            buildingSystem = new BuildingSystem
            {
                Buildings = CreateRuntimeBuildings(template.BuildingStartingConditions)
            };
            buildingSystem.RebuildIndex();
            divisionSystem = new DivisionSystem
            {
                Divisions = CreateRuntimeDivisions(template.DivisionStartingConditions, activeModule)
            };
            divisionSystem.RebuildIndex();
            _AISystem = new AISystem(this);
            _groundOperationsSystem = new GroundOperationsSystem(this);
            _groundCombatSystem = new GroundCombatSystem(this, _groundOperationsSystem);
            IsGamePaused = false;
            _campaignStarted = true;
        }

        public AllianceAI GetAllianceAI(Alliance alliance)
        {
            if (!_campaignStarted || _AISystem == null)
                return null;

            return _AISystem.GetAllianceAI(alliance);
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
            var elapsedHours = SimulationSettings.SimulationTickMinutes / 60f;
            CurrentTime = CurrentTime.AddMinutes(SimulationSettings.SimulationTickMinutes);
            _AISystem.GameTurn();
            _groundCombatSystem.GameTurn();
            _groundOperationsSystem.GameTurn(elapsedHours);
        }

        private static List<TileData> CopyTileData(List<TileData> startingTileData)
        {
            return (startingTileData ?? new List<TileData>())
                .Select(CopyTileData)
                .Where(tileData => tileData != null)
                .ToList();
        }

        private static List<CountryAllianceAssignment> CopyCountryAllianceAssignments(
            List<CountryAllianceAssignment> assignments)
        {
            return (assignments ?? new List<CountryAllianceAssignment>())
                .Where(assignment => assignment != null)
                .Select(assignment => new CountryAllianceAssignment
                {
                    CountryId = assignment.CountryId,
                    Alliance = assignment.Alliance
                })
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
                Coordinates = tile.Coordinates,
                NeighborTileIds = new List<Vector3Int>(tile.NeighborTileIds ?? new List<Vector3Int>()),
                RiverNeighborTileIds = new List<Vector3Int>(tile.RiverNeighborTileIds ?? new List<Vector3Int>()),
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
                    throw new ArgumentOutOfRangeException(nameof(startingCondition.Type), startingCondition.Type,
                        "Unknown building type.");
            }
        }

        private static List<Division> CreateRuntimeDivisions(
            List<DivisionStartingCondition> startingConditions,
            ModuleDefinition module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));

            var battalionDefinitions = module.BattalionDefinitions
                .Where(battalion => battalion != null)
                .ToDictionary(battalion => battalion.BattalionDefinitionId);

            var divisionTemplates = module.DivisionTemplates
                .Where(template => template != null)
                .ToDictionary(template => template.DivisionTemplateId);

            return (startingConditions ?? new List<DivisionStartingCondition>())
                .Where(startingCondition => startingCondition != null)
                .Select(startingCondition => CreateRuntimeDivision(
                    startingCondition,
                    divisionTemplates,
                    battalionDefinitions))
                .ToList();
        }

        private static Division CreateRuntimeDivision(
            DivisionStartingCondition startingCondition,
            IReadOnlyDictionary<Guid, DivisionTemplate> divisionTemplates,
            IReadOnlyDictionary<Guid, BattalionDefinition> battalionDefinitions)
        {
            if (!divisionTemplates.TryGetValue(startingCondition.DivisionTemplateId, out var divisionTemplate))
            {
                throw new KeyNotFoundException(
                    $"Division template {startingCondition.DivisionTemplateId} was not found in the active module.");
            }

            if (divisionTemplate.CountryId != startingCondition.CountryId)
            {
                throw new InvalidOperationException(
                    $"Division starting condition {startingCondition.DivisionId} country does not match its division template country.");
            }

            var fullStrengthStats = divisionTemplate.CalculateFullStrengthStats(battalionDefinitions);
            return new Division(startingCondition, fullStrengthStats);
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
