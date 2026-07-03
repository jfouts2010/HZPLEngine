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
        public event Action GameTurnCompleted;

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
        public Dictionary<Alliance, List<Guid>> OrdnanceAllowances = new Dictionary<Alliance, List<Guid>>();
        public Dictionary<Alliance, List<Guid>> SamSiteTemplateAllowances = new Dictionary<Alliance, List<Guid>>();
        public List<Tile> CampaignTiles = new List<Tile>();
        [SerializeReference] public List<TileData> Tiles = new List<TileData>();
        public List<SupplyCapitalStartingCondition> SupplyCapitals = new List<SupplyCapitalStartingCondition>();
        private GroundTaskingSystem _groundTaskingSystem;
        private AirTaskingSystem _airTaskingSystem;
        private AirExecutionSystem _airExecutionSystem;
        private IADSSystem _IADSSystem;
        private GroundCombatSystem _groundCombatSystem;
        private GroundOperationsSystem _groundOperationsSystem;
        private SupplySystem _supplySystem;
        public BuildingSystem buildingSystem = new BuildingSystem();
        public DivisionSystem divisionSystem = new DivisionSystem();
        public SquadronSystem squadronSystem = new SquadronSystem();
        public AirDefenseSiteSystem airDefenseSiteSystem = new AirDefenseSiteSystem();

        private Coroutine GameTurnCoroutine = null;
        private bool _campaignStarted;
        private DateTime _campaignStartTime;

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
            _campaignStartTime = template.CampaignStartTime;
            CurrentTime = template.CampaignStartTime;
            SimulationSettings = CopySimulationSettings(template.SimulationSettings);
            OrdnanceAllowances = CopyOrdnanceAllowances(template.OrdnanceAllowances);
            SamSiteTemplateAllowances = CopyGuidAllowances(template.SamSiteTemplateAllowances);
            CampaignTiles = CopyTiles(template.Tiles);
            Tiles = CopyTileData(template.StartingTileData);
            SupplyCapitals = CopySupplyCapitals(template.SupplyCapitals);
            buildingSystem = new BuildingSystem
            {
                Buildings = CreateRuntimeBuildings(template.BuildingStartingConditions, activeModule)
            };
            buildingSystem.RebuildIndex();
            divisionSystem = new DivisionSystem
            {
                Divisions = CreateRuntimeDivisions(template.DivisionStartingConditions, activeModule)
            };
            divisionSystem.RebuildIndex();
            squadronSystem = new SquadronSystem
            {
                Squadrons = CreateRuntimeSquadrons(template.SquadronStartingConditions, activeModule)
            };
            squadronSystem.RebuildIndex();
            airDefenseSiteSystem = new AirDefenseSiteSystem
            {
                Sites = CreateRuntimeSamSites(
                    template.BuildingStartingConditions,
                    template.MobileSamSiteStartingConditions,
                    activeModule)
            };
            airDefenseSiteSystem.Configure(buildingSystem, divisionSystem, GetCountryAlliance);
            airDefenseSiteSystem.RebuildIndex();
            _groundTaskingSystem = new GroundTaskingSystem(this);
            _IADSSystem = new IADSSystem(this);
            _airTaskingSystem = new AirTaskingSystem(this, activeModule);
            _airExecutionSystem = new AirExecutionSystem(this, _airTaskingSystem, activeModule);
            _groundOperationsSystem = new GroundOperationsSystem(this);
            _groundCombatSystem = new GroundCombatSystem(this, _groundOperationsSystem);
            _supplySystem = new SupplySystem(this);
            _supplySystem.GameTurn();
            _groundTaskingSystem.OperationalCadenceTurn();
            _airTaskingSystem.Initialize();
            IsGamePaused = false;
            _campaignStarted = true;
        }

        public GroundTaskingCommander GetGroundTaskingCommander(Alliance alliance)
        {
            if (!_campaignStarted || _groundTaskingSystem == null)
                return null;

            return _groundTaskingSystem.GetCommander(alliance);
        }

        public AllianceAirTaskingCommander GetAllianceAirTaskingCommander(Alliance alliance)
        {
            if (!_campaignStarted || _airTaskingSystem == null)
                return null;

            return _airTaskingSystem.GetCommander(alliance);
        }

        public IReadOnlyList<AirFlight> GetAirborneFlights()
        {
            return _airTaskingSystem.GetAirborneFlights().ToList();
        }

        public AllianceIADS GetAllianceIADS(Alliance alliance)
        {
            if (!_campaignStarted || _IADSSystem == null)
                return null;

            return _IADSSystem.GetAllianceIADS(alliance);
        }

        public Alliance GetCountryAlliance(Guid countryId)
        {
            var assignment = CampaignTemplate?.CountryAllianceAssignments?
                .FirstOrDefault(candidate => candidate.CountryId == countryId);
            return assignment.Alliance;
        }

        public bool IsDivisionEngagedInGroundCombat(Guid divisionId)
        {
            return _groundCombatSystem != null
                   && _groundCombatSystem.IsDivisionEngagedInCombat(divisionId);
        }

        public bool IsDivisionAttackingInGroundCombat(Guid divisionId)
        {
            return _groundCombatSystem != null
                   && _groundCombatSystem.IsDivisionAttackingInCombat(divisionId);
        }

        public bool IsDivisionDefendingInGroundCombat(Guid divisionId)
        {
            return _groundCombatSystem != null
                   && _groundCombatSystem.IsDivisionDefendingInCombat(divisionId);
        }

        public IReadOnlyList<GroundCombat> GetActiveGroundCombats()
        {
            return _groundCombatSystem.ActiveCombats;
        }


        public void PauseCampaign()
        {
            IsGamePaused = true;
            if (GameTurnCoroutine == null)
                return;

            StopCoroutine(GameTurnCoroutine);
            GameTurnCoroutine = null;
        }

        public void ResumeCampaign()
        {
            if (!_campaignStarted)
                return;

            IsGamePaused = false;
        }

        public bool AdvanceOneGameTurn()
        {
            if (!_campaignStarted || !IsGamePaused || GameTurnCoroutine != null)
                return false;

            GameTurn();
            return true;
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
            yield return null; // yield return new WaitForSeconds(0.16f);
            GameTurn();
            GameTurnCoroutine = null;
        }

        private void GameTurn()
        {
            var elapsedHours = SimulationSettings.SimulationTickMinutes / 60f;
            var previousTime = CurrentTime;
            CurrentTime = CurrentTime.AddMinutes(SimulationSettings.SimulationTickMinutes);

            var crossedOperationalCadenceBoundary =
                SimulationSettings.CrossedOperationalCadenceBoundary(
                    _campaignStartTime,
                    previousTime,
                    CurrentTime,
                    SimulationSettings.OperationalCadenceHours);
            if (crossedOperationalCadenceBoundary)
            {
                _groundTaskingSystem.OperationalCadenceTurn();
            }

            var resolveCombatRound = SimulationSettings.CrossedOperationalCadenceBoundary(
                _campaignStartTime,
                previousTime,
                CurrentTime,
                1);
            if (resolveCombatRound)
                _groundTaskingSystem.CombatCadenceTurn();

            _groundCombatSystem.GameTurn(resolveCombatRound);
            _groundOperationsSystem.GameTurn(elapsedHours);
            _airExecutionSystem.GameTurn(previousTime, CurrentTime);
            _IADSSystem.TacticalTurn();
            _supplySystem.GameTurn(elapsedHours);
            _airTaskingSystem.GameTurn(crossedOperationalCadenceBoundary);
            divisionSystem.ApplyCombatSupplyPenalties(
                elapsedHours,
                _groundCombatSystem.IsDivisionEngagedInCombat,
                _supplySystem.GetSupplyRatio,
                division => division?.CurrentOrder is not MoveGroundOrder { Purpose: MoveGroundOrderPurpose.Retreat });
            divisionSystem.ApplyOutOfCombatRecovery(
                elapsedHours,
                _groundCombatSystem.IsDivisionEngagedInCombat,
                _supplySystem.GetSupplyRatio,
                division => division?.CurrentOrder is not MoveGroundOrder { Purpose: MoveGroundOrderPurpose.Retreat });
            GameTurnCompleted?.Invoke();
        }

        private static List<TileData> CopyTileData(List<TileData> startingTileData)
        {
            return (startingTileData)
                .Select(CopyTileData)
                .ToList();
        }

        private static List<CountryAllianceAssignment> CopyCountryAllianceAssignments(
            List<CountryAllianceAssignment> assignments)
        {
            return (assignments)
                .Select(assignment => new CountryAllianceAssignment
                {
                    CountryId = assignment.CountryId,
                    Alliance = assignment.Alliance
                })
                .ToList();
        }

        private static Dictionary<Alliance, List<Guid>> CopyOrdnanceAllowances(
            Dictionary<Alliance, List<Guid>> allowances)
        {
            return CopyGuidAllowances(allowances);
        }

        private static Dictionary<Alliance, List<Guid>> CopyGuidAllowances(
            Dictionary<Alliance, List<Guid>> allowances)
        {
            return allowances
                .ToDictionary(
                    allowance => allowance.Key,
                    allowance => new List<Guid>(allowance.Value));
        }

        private static List<SupplyCapitalStartingCondition> CopySupplyCapitals(
            List<SupplyCapitalStartingCondition> supplyCapitals)
        {
            return supplyCapitals
                .Select(supplyCapital => new SupplyCapitalStartingCondition
                {
                    Alliance = supplyCapital.Alliance,
                    TileId = supplyCapital.TileId
                })
                .ToList();
        }

        private static List<Tile> CopyTiles(List<Tile> tiles)
        {
            return (tiles)
                .Select(CopyTile)
                .ToList();
        }

        private static Tile CopyTile(Tile tile)
        {
            return new Tile
            {
                Coordinates = tile.Coordinates,
                NeighborTileIds = new List<Vector3Int>(tile.NeighborTileIds),
                RiverNeighborTileIds = new List<Vector3Int>(tile.RiverNeighborTileIds),
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
                    Infrastructure = CopyBuildingLevel(landData.Infrastructure),
                    InfrastructureTargetToughness = Math.Max(1, landData.InfrastructureTargetToughness)
                };
            }

            if (data is OceanTileData oceanData)
            {
                return new OceanTileData
                {
                    TileId = oceanData.TileId
                };
            }

            throw new ArgumentOutOfRangeException(
                nameof(data),
                data,
                $"Unsupported tile data type {data.GetType().Name}.");
        }

        private static List<Building> CreateRuntimeBuildings(
            List<BuildingStartingCondition> startingConditions,
            ModuleDefinition module)
        {
            return startingConditions
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
                case BuildingType.AirDefense:
                    return new AirDefenseBuilding(startingCondition);
                default:
                    throw new ArgumentOutOfRangeException(nameof(startingCondition.Type), startingCondition.Type,
                        "Unknown building type.");
            }
        }

        private static List<SamSite> CreateRuntimeSamSites(
            List<BuildingStartingCondition> buildingStartingConditions,
            List<MobileSamSiteStartingCondition> mobileStartingConditions,
            ModuleDefinition module)
        {
            var samSiteTemplates = module.SamSiteTemplates
                .ToDictionary(template => template.SamSiteTemplateId);

            var samComponentDefinitions = module.SamComponentDefinitions
                .ToDictionary(definition => definition.SamComponentDefinitionId);

            var staticSites = buildingStartingConditions
                .Where(startingCondition => startingCondition.Type == BuildingType.AirDefense)
                .Select(startingCondition => new SamSite(
                    startingCondition,
                    CreateAirDefenseComponentsFromTemplate(
                        startingCondition.SamSiteTemplateId,
                        samSiteTemplates,
                        samComponentDefinitions,
                        SamSiteHostConstraint.MobileOnly)));

            var mobileSites = mobileStartingConditions
                .Select(startingCondition => new SamSite(
                    startingCondition,
                    CreateAirDefenseComponentsFromTemplate(
                        startingCondition.SamSiteTemplateId,
                        samSiteTemplates,
                        samComponentDefinitions,
                        SamSiteHostConstraint.StaticOnly)));

            return staticSites.Concat(mobileSites).ToList();
        }

        private static List<AirDefenseComponent> CreateAirDefenseComponentsFromTemplate(
            Guid samSiteTemplateId,
            IReadOnlyDictionary<Guid, SamSiteTemplate> samSiteTemplates,
            IReadOnlyDictionary<Guid, AirDefenseComponentDefinition> samComponentDefinitions,
            SamSiteHostConstraint disallowedHostConstraint)
        {
            if (!samSiteTemplates.TryGetValue(samSiteTemplateId, out var samSiteTemplate))
            {
                throw new KeyNotFoundException(
                    $"SAM site template {samSiteTemplateId} was not found in the active module.");
            }

            if (samSiteTemplate.HostConstraint == disallowedHostConstraint)
            {
                throw new InvalidOperationException(
                    $"SAM site template {samSiteTemplate.SamSiteTemplateId} cannot be used by this host type.");
            }

            return CreateDefaultAirDefenseComponents(samSiteTemplate, samComponentDefinitions);
        }

        private static List<AirDefenseComponent> CreateDefaultAirDefenseComponents(
            SamSiteTemplate samSiteTemplate,
            IReadOnlyDictionary<Guid, AirDefenseComponentDefinition> samComponentDefinitions)
        {
            var components = new List<AirDefenseComponent>();
            foreach (var templateComponent in samSiteTemplate.Components)
            {
                if (templateComponent == null || templateComponent.Count <= 0)
                    continue;

                if (!samComponentDefinitions.TryGetValue(
                        templateComponent.SamComponentDefinitionId,
                        out var componentDefinition))
                {
                    throw new KeyNotFoundException(
                        $"SAM component definition {templateComponent.SamComponentDefinitionId} was not found in the active module.");
                }

                for (var i = 0; i < templateComponent.Count; i++)
                {
                    components.Add(CreateAirDefenseComponent(componentDefinition));
                }
            }

            return components;
        }

        private static AirDefenseComponent CreateAirDefenseComponent(AirDefenseComponentDefinition definition)
        {
            return definition switch
            {
                RadarAirDefenseComponentDefinition radarDefinition => new RadarAirDefenseComponent(radarDefinition),
                LauncherAirDefenseComponentDefinition launcherDefinition => new LauncherAirDefenseComponent(
                    launcherDefinition),
                CommandAirDefenseComponentDefinition commandDefinition => new CommandAirDefenseComponent(
                    commandDefinition),
                SupportAirDefenseComponentDefinition supportDefinition => new SupportAirDefenseComponent(
                    supportDefinition),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(definition),
                    definition,
                    $"Unsupported SAM component definition type {definition?.GetType().Name}.")
            };
        }

        private static List<Division> CreateRuntimeDivisions(
            List<DivisionStartingCondition> startingConditions,
            ModuleDefinition module)
        {
            var battalionDefinitions = module.BattalionDefinitions
                .ToDictionary(battalion => battalion.BattalionDefinitionId);

            var divisionTemplates = module.DivisionTemplates
                .ToDictionary(template => template.DivisionTemplateId);

            return startingConditions
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

        private static List<Squadron> CreateRuntimeSquadrons(
            List<SquadronStartingCondition> startingConditions,
            ModuleDefinition module)
        {
            var aircraftTypeDefinitions = module.AircraftTypeDefinitions
                .ToDictionary(aircraftType => aircraftType.AircraftTypeDefinitionId);

            return startingConditions
                .Select(startingCondition => CreateRuntimeSquadron(
                    startingCondition,
                    aircraftTypeDefinitions))
                .ToList();
        }

        private static Squadron CreateRuntimeSquadron(
            SquadronStartingCondition startingCondition,
            IReadOnlyDictionary<Guid, AircraftTypeDefinition> aircraftTypeDefinitions)
        {
            if (!aircraftTypeDefinitions.ContainsKey(startingCondition.AircraftTypeDefinitionId))
            {
                throw new KeyNotFoundException(
                    $"Aircraft type definition {startingCondition.AircraftTypeDefinitionId} was not found in the active module.");
            }

            return new Squadron(startingCondition);
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
                OperationalCadenceHours = settings.OperationalCadenceHours,
                TileDistanceKM = settings.TileDistanceKM
            };

            copy.Normalize();
            return copy;
        }
    }
}