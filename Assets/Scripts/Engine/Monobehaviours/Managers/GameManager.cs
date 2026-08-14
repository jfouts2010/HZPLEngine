using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Engine.Models.Ground;
using Engine.Models.Systems.Ground;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using Monobehaviours.Singletons;
using UnityEngine;

namespace Engine.Monobehaviours.Managers
{
    public enum TestCampaignKind
    {
        Basic,
        Advanced,
        CaucasusDcsPrototype
    }

    public enum CampaignPlaybackIncrement
    {
        FiveSeconds,
        FiveMinutes
    }

    [Serializable]
    public sealed class CampaignTurnChange
    {
        public DateTime OccurredAt;
        public string System = string.Empty;
        public string Summary = string.Empty;
        public Guid EntityId;
    }

    public class GameManager : MonoBehaviour
    {
        public event Action GameTurnCompleted;
        public event Action AirTacticalStepCompleted;

        public const double AirTacticalStepSeconds = 5d;
        public const double FastPlaybackIncrementSeconds = 5d * 60d;

        public bool IsGamePaused { get; private set; }
        public bool IsCampaignStarted => _campaignStarted;
        public CampaignPlaybackIncrement PlaybackIncrement { get; private set; } =
            CampaignPlaybackIncrement.FiveSeconds;
        public string TemplateName { get; private set; }
        public Guid ModuleId { get; private set; }
        public DateTime CurrentTime { get; private set; }
        public DateTime GameTime => CurrentTime;
        public DateTime LastTurnStartedAt { get; private set; }
        public DateTime LastTurnCompletedAt { get; private set; }
        public IReadOnlyList<CampaignTurnChange> LastTurnChanges => _lastTurnChanges;
        public SimulationSettings SimulationSettings { get; private set; } = new SimulationSettings();
        public bool AutoStartTestCampaign = true;
        public TestCampaignKind SelectedTestCampaign = TestCampaignKind.Advanced;
        public CampaignTemplate CampaignTemplate { get; private set; }
        public Dictionary<Alliance, List<Guid>> OrdnanceAllowances = new Dictionary<Alliance, List<Guid>>();
        public Dictionary<Alliance, List<Guid>> SamSiteTemplateAllowances = new Dictionary<Alliance, List<Guid>>();
        public TileSystem tileSystem { get; private set; }
        public List<SupplyCapitalStartingCondition> SupplyCapitals = new List<SupplyCapitalStartingCondition>();
        private GroundTaskingSystem _groundTaskingSystem;
        private AirTaskingSystem _airTaskingSystem;
        private SimulationLogWriter _simulationLogWriter;
        private AirExecutionSystem _airExecutionSystem;
        private OrdnanceEmploymentSystem _ordnanceEmploymentSystem;
        private IADSSystem _IADSSystem;
        private GroundCombatSystem _groundCombatSystem;
        private GroundOperationsSystem _groundOperationsSystem;
        private SupplySystem _supplySystem;
        public BuildingSystem buildingSystem = new BuildingSystem();
        public DivisionSystem divisionSystem = new DivisionSystem();
        public SquadronSystem squadronSystem = new SquadronSystem();
        public AirDefenseSiteSystem airDefenseSiteSystem = new AirDefenseSiteSystem();
        public AllianceIntelligenceSystem intelligenceSystem =
            new AllianceIntelligenceSystem();

        private Coroutine GameTurnCoroutine = null;
        private bool _campaignStarted;
        private DateTime _campaignStartTime;
        private DateTime _nextGameTurnAt;
        private TurnSnapshot _gameTurnSnapshot;
        private readonly List<CampaignTurnChange> _lastTurnChanges = new List<CampaignTurnChange>();

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
                TestCampaignKind.CaucasusDcsPrototype => CaucasusDcsPrototypeCampaign.Create(),
                _ => AdvancedTestCampaign.Create()
            };
        }

        public void StartCampaign(CampaignTemplate template)
        {
            TemplateName = template.Name;
            ModuleId = template.ModuleId;
            var moduleSingleton = ModuleSingleton.Instance;
            var activeModule = moduleSingleton.ActiveModule;
            if (activeModule.Id != template.ModuleId
                && !moduleSingleton.HasActiveModuleSelection
                && moduleSingleton.TrySetActive(template.ModuleId))
                activeModule = moduleSingleton.ActiveModule;

            if (activeModule.Id != template.ModuleId)
            {
                throw new InvalidOperationException(
                    $"Campaign template module {template.ModuleId} does not match active module {activeModule.Id}.");
            }

            CampaignTemplate = template;
            _campaignStartTime = template.CampaignStartTime;
            CurrentTime = template.CampaignStartTime;
            LastTurnStartedAt = CurrentTime;
            LastTurnCompletedAt = CurrentTime;
            SimulationSettings = CopySimulationSettings(template.SimulationSettings);
            OrdnanceAllowances = CopyOrdnanceAllowances(template.OrdnanceAllowances);
            SamSiteTemplateAllowances = CopyGuidAllowances(template.SamSiteTemplateAllowances);
            tileSystem = TileSystem.Create(template.Tiles, template.StartingTileData);
            SupplyCapitals = CopySupplyCapitals(template.SupplyCapitals);
            buildingSystem = new BuildingSystem
            {
                Buildings = CreateRuntimeBuildings(template.BuildingStartingConditions, activeModule)
            };
            buildingSystem.Configure(tileSystem);
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
            airDefenseSiteSystem.Configure(buildingSystem, divisionSystem, tileSystem);
            airDefenseSiteSystem.RebuildIndex();
            intelligenceSystem = new AllianceIntelligenceSystem();
            intelligenceSystem.RefreshMaximumInformation(
                this,
                CurrentTime,
                new HashSet<Guid>());
            _groundTaskingSystem = new GroundTaskingSystem(this);
            _IADSSystem = new IADSSystem(this);
            _airTaskingSystem = new AirTaskingSystem(this, activeModule);
            _airExecutionSystem = new AirExecutionSystem(this, _airTaskingSystem, activeModule);
            _ordnanceEmploymentSystem = new OrdnanceEmploymentSystem(
                this,
                _airTaskingSystem,
                _IADSSystem,
                activeModule);
            _airExecutionSystem.AttachOrdnanceEmploymentSystem(
                _ordnanceEmploymentSystem);
            _groundOperationsSystem = new GroundOperationsSystem(this);
            _groundCombatSystem = new GroundCombatSystem(this, _groundOperationsSystem);
            _supplySystem = new SupplySystem(this);
            _supplySystem.GameTurn();
            _groundTaskingSystem.OperationalCadenceTurn();
            _airTaskingSystem.Initialize();
            _nextGameTurnAt = CurrentTime.AddMinutes(
                SimulationSettings.SimulationTickMinutes);
            _gameTurnSnapshot = CaptureTurnSnapshot();
            _simulationLogWriter = new SimulationLogWriter(
                this,
                _airTaskingSystem,
                _IADSSystem,
                activeModule,
                _campaignStartTime);
            IsGamePaused = true;
            _campaignStarted = true;
        }

        private void OnDisable()
        {
            // Exiting play mode or unloading the scene should still leave a
            // readable log for sorties that were still airborne.
            _simulationLogWriter?.FlushIncompleteFlights();
        }

        public GroundTaskingCommander GetGroundTaskingCommander(Alliance alliance)
        {
            if (_groundTaskingSystem == null)
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

        public bool RetainsProjectedBarcapCoverage(AirFlight flight)
        {
            return _airTaskingSystem != null
                   && _airTaskingSystem.RetainsProjectedBarcapCoverage(flight);
        }

        public AirportOperationsSnapshot GetAirportOperationsSnapshot(
            Guid airportId)
        {
            return _airTaskingSystem?.GetAirportOperationsSnapshot(airportId)
                   ?? default;
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

        public IReadOnlyList<ActiveOrdnanceEmploymentPass>
            GetActiveOrdnanceEmploymentPasses()
        {
            return _ordnanceEmploymentSystem.ActivePasses;
        }

        public IReadOnlyList<PendingOrdnanceEffect> GetPendingOrdnanceEffects()
        {
            return _ordnanceEmploymentSystem.PendingEffects;
        }

        public IReadOnlyList<OrdnanceEmploymentRecord> GetOrdnanceEmploymentRecords()
        {
            return _ordnanceEmploymentSystem.Records;
        }

        public bool IsFlightInWvrEngagement(Guid flightId)
        {
            return _airExecutionSystem?.IsFlightInWvrEngagement(flightId)
                   == true;
        }

        public bool TryGetLatestWvrRound(
            Guid flightId,
            out WvrRoundDiagnostic diagnostic)
        {
            if (_airExecutionSystem != null)
                return _airExecutionSystem.TryGetLatestWvrRound(
                    flightId,
                    out diagnostic);

            diagnostic = null;
            return false;
        }

        public void ChangeTileControl(Vector3Int tileId, Alliance controller)
        {
            var landTile = tileSystem.GetLand(tileId);
            if (landTile.Controller == controller)
                return;

            tileSystem.ChangeControl(tileId, controller);
            airDefenseSiteSystem?.DisableSitesOnTileCapture(tileId);
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

        public void SetPlaybackIncrement(CampaignPlaybackIncrement increment)
        {
            if (!Enum.IsDefined(typeof(CampaignPlaybackIncrement), increment))
                throw new ArgumentOutOfRangeException(nameof(increment), increment, null);

            PlaybackIncrement = increment;
        }

        public bool AdvanceOnePlaybackIncrement()
        {
            if (!_campaignStarted || !IsGamePaused || GameTurnCoroutine != null)
                return false;

            GameTurnCoroutine = StartCoroutine(AdvancePlaybackIncrement());
            return true;
        }

        public bool AdvanceOneGameTurn()
        {
            if (!_campaignStarted || !IsGamePaused || GameTurnCoroutine != null)
                return false;

            var turnEnd = _nextGameTurnAt;
            GameTurnCoroutine = StartCoroutine(AdvanceGameTurn(turnEnd));
            return true;
        }

        public bool AdvanceOneAirTacticalStep()
        {
            if (!_campaignStarted || !IsGamePaused || GameTurnCoroutine != null)
                return false;

            AdvanceAirTacticalStep(true);
            return true;
        }

        public void Update()
        {
            if (!_campaignStarted)
                return;

            if (IsGamePaused)
                return;

            if (GameTurnCoroutine == null)
                GameTurnCoroutine = StartCoroutine(AdvancePlaybackIncrement());
        }

        private IEnumerator AdvancePlaybackIncrement()
        {
            try
            {
                yield return null;
                var seconds =
                    PlaybackIncrement == CampaignPlaybackIncrement.FiveMinutes
                        ? FastPlaybackIncrementSeconds
                        : AirTacticalStepSeconds;
                var incrementEnd = CurrentTime.AddSeconds(seconds);

                while (CurrentTime < incrementEnd)
                {
                    AdvanceAirTacticalStep(false, incrementEnd);
                    if (CurrentTime < incrementEnd)
                        yield return null;
                }

                AirTacticalStepCompleted?.Invoke();
            }
            finally
            {
                GameTurnCoroutine = null;
            }
        }

        private IEnumerator AdvanceGameTurn(DateTime turnEnd)
        {
            try
            {
                yield return null;
                while (CurrentTime < turnEnd)
                {
                    AdvanceAirTacticalStep(false, turnEnd);
                    if (CurrentTime < turnEnd)
                        yield return null;
                }
            }
            finally
            {
                GameTurnCoroutine = null;
            }
        }

        private void AdvanceAirTacticalStep(bool notifyTacticalStep, DateTime? maximumTime = null)
        {
            var previousTime = CurrentTime;
            var stepEnd = CurrentTime.AddSeconds(AirTacticalStepSeconds);
            if (maximumTime.HasValue && maximumTime.Value < stepEnd)
                stepEnd = maximumTime.Value;
            CurrentTime = stepEnd < _nextGameTurnAt ? stepEnd : _nextGameTurnAt;

            _airExecutionSystem.GameTurn(previousTime, CurrentTime);
            _IADSSystem.TacticalTurn(
                (float)(CurrentTime - previousTime).TotalSeconds,
                CurrentTime);
            _airTaskingSystem.AdvanceAirControl(CurrentTime);
            _ordnanceEmploymentSystem.RefreshTacticalState(CurrentTime);
            if (notifyTacticalStep)
                AirTacticalStepCompleted?.Invoke();

            if (CurrentTime < _nextGameTurnAt)
                return;

            CompleteGameTurn();
        }

        private void CompleteGameTurn()
        {
            var elapsedHours = SimulationSettings.SimulationTickMinutes / 60f;
            var previousTurnTime = LastTurnStartedAt;

            var crossedOperationalCadenceBoundary =
                SimulationSettings.CrossedOperationalCadenceBoundary(
                    _campaignStartTime,
                    previousTurnTime,
                    CurrentTime,
                    SimulationSettings.OperationalCadenceHours);
            var resolveCombatRound = SimulationSettings.CrossedOperationalCadenceBoundary(
                    _campaignStartTime,
                    previousTurnTime,
                    CurrentTime,
                    1);
            if (crossedOperationalCadenceBoundary || resolveCombatRound)
                RefreshAllianceIntelligence();

            if (crossedOperationalCadenceBoundary)
            {
                _groundTaskingSystem.OperationalCadenceTurn();
            }

            if (resolveCombatRound)
                _groundTaskingSystem.CombatCadenceTurn();

            _groundCombatSystem.GameTurn(resolveCombatRound);
            _groundOperationsSystem.GameTurn(elapsedHours);
            _supplySystem.GameTurn(elapsedHours);
            if (crossedOperationalCadenceBoundary)
                RefreshAllianceIntelligence();
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
            RefreshAllianceIntelligence();
            BuildTurnChanges(_gameTurnSnapshot);
            LastTurnCompletedAt = CurrentTime;
            _simulationLogWriter?.OnTurnCompleted();
            GameTurnCompleted?.Invoke();
            LastTurnStartedAt = CurrentTime;
            _nextGameTurnAt = CurrentTime.AddMinutes(
                SimulationSettings.SimulationTickMinutes);
            _gameTurnSnapshot = CaptureTurnSnapshot();
        }

        private void RefreshAllianceIntelligence()
        {
            var airborneAircraftIds = _airTaskingSystem == null
                ? new HashSet<Guid>()
                : _airTaskingSystem
                    .GetAirborneFlights()
                    .SelectMany(flight => flight.AircraftIds)
                    .ToHashSet();
            intelligenceSystem.RefreshMaximumInformation(
                this,
                CurrentTime,
                airborneAircraftIds);
        }

        private TurnSnapshot CaptureTurnSnapshot()
        {
            return new TurnSnapshot
            {
                Divisions = divisionSystem.Divisions.ToDictionary(
                    division => division.DivisionId,
                    division => new DivisionTurnState(
                        division.TileId,
                        division.Strength,
                        division.Organization,
                        division.CurrentOrder?.GetType().Name ?? "None",
                        division.CurrentOrder is MoveGroundOrder move ? move.MovementProgress : 0f)),
                TileControllers = tileSystem.LandTiles
                    .ToDictionary(tile => tile.TileId, tile => tile.Controller),
                Buildings = buildingSystem.Buildings.ToDictionary(
                    building => building.BuildingId,
                    building => new BuildingTurnState(
                        building.Level.BuildLevel,
                        building.Level.Damage,
                        building.FunctionalLevel)),
                SquadronLosses = squadronSystem.Squadrons.ToDictionary(
                    squadron => squadron.SquadronId,
                    squadron => new SquadronTurnState(
                        squadron.ReadyAircraft,
                        squadron.DamagedAircraft,
                        squadron.AssignedAircraft,
                        squadron.LostAircraft)),
                CombatTiles = _groundCombatSystem.ActiveCombats
                    .Select(combat => combat.DefendingTileId)
                    .ToHashSet()
            };
        }

        private void BuildTurnChanges(TurnSnapshot before)
        {
            _lastTurnChanges.Clear();
            foreach (var division in divisionSystem.Divisions)
            {
                if (!before.Divisions.TryGetValue(division.DivisionId, out var previous))
                {
                    AddTurnChange("Ground", $"Division {division.Name} entered play.", division.DivisionId);
                    continue;
                }
                if (previous.TileId != division.TileId)
                    AddTurnChange(
                        "Ground",
                        $"{division.Name} moved {FormatTile(previous.TileId)} → {FormatTile(division.TileId)}.",
                        division.DivisionId);
                var strengthDelta = division.Strength - previous.Strength;
                var organizationDelta = division.Organization - previous.Organization;
                if (!Mathf.Approximately(strengthDelta, 0f)
                    || !Mathf.Approximately(organizationDelta, 0f))
                {
                    AddTurnChange(
                        "Ground",
                        $"{division.Name}: strength {FormatDelta(strengthDelta)}, organization {FormatDelta(organizationDelta)}.",
                        division.DivisionId);
                }
                var orderName = division.CurrentOrder?.GetType().Name ?? "None";
                if (previous.OrderName != orderName)
                    AddTurnChange(
                        "Ground",
                        $"{division.Name} order changed {previous.OrderName} → {orderName}.",
                        division.DivisionId);
                if (division.CurrentOrder is MoveGroundOrder move
                    && !Mathf.Approximately(previous.MovementProgress, move.MovementProgress))
                {
                    AddTurnChange(
                        "Ground",
                        $"{division.Name} movement progress {previous.MovementProgress:P1} → {move.MovementProgress:P1}.",
                        division.DivisionId);
                }
            }

            foreach (var tile in tileSystem.LandTiles)
            {
                if (before.TileControllers.TryGetValue(tile.TileId, out var controller)
                    && controller != tile.Controller)
                    AddTurnChange(
                        "Ground",
                        $"Tile {FormatTile(tile.TileId)} captured: {controller} → {tile.Controller}.");
            }

            var currentCombatTiles = _groundCombatSystem.ActiveCombats
                .Select(combat => combat.DefendingTileId)
                .ToHashSet();
            foreach (var tile in currentCombatTiles.Except(before.CombatTiles))
                AddTurnChange("Ground", $"Combat started at {FormatTile(tile)}.");
            foreach (var tile in before.CombatTiles.Except(currentCombatTiles))
                AddTurnChange("Ground", $"Combat ended at {FormatTile(tile)}.");

            foreach (var building in buildingSystem.Buildings)
            {
                if (!before.Buildings.TryGetValue(building.BuildingId, out var previous))
                    continue;
                if (previous.Damage != building.Level.Damage
                    || previous.FunctionalLevel != building.FunctionalLevel)
                    AddTurnChange(
                        "Infrastructure",
                        $"{building.Type} {ShortId(building.BuildingId)} at {FormatTile(building.TileId)} ({building.PositionFeet.x:0},{building.PositionFeet.z:0} ft): damage {previous.Damage} → {building.Level.Damage}, functional level {previous.FunctionalLevel} → {building.FunctionalLevel}.",
                        building.BuildingId);
            }

            foreach (var squadron in squadronSystem.Squadrons)
            {
                if (!before.SquadronLosses.TryGetValue(squadron.SquadronId, out var previous))
                    continue;
                if (previous.Lost != squadron.LostAircraft
                    || previous.Damaged != squadron.DamagedAircraft)
                    AddTurnChange(
                        "Air",
                        $"{squadron.Name}: lost {previous.Lost} → {squadron.LostAircraft}, damaged {previous.Damaged} → {squadron.DamagedAircraft}.",
                        squadron.SquadronId);
            }
        }

        private void AddTurnChange(string system, string summary, Guid entityId = default)
        {
            _lastTurnChanges.Add(new CampaignTurnChange
            {
                OccurredAt = CurrentTime,
                System = system,
                Summary = summary,
                EntityId = entityId
            });
        }

        private static string FormatDelta(float value) =>
            value >= 0f ? $"+{value:0.##}" : value.ToString("0.##");

        private static string FormatTile(Vector3Int tileId) =>
            $"{tileId.x},{tileId.y},{tileId.z}";

        private static string ShortId(Guid id) =>
            id == Guid.Empty ? "—" : id.ToString("N").Substring(0, 8);

        private sealed class TurnSnapshot
        {
            public Dictionary<Guid, DivisionTurnState> Divisions = new Dictionary<Guid, DivisionTurnState>();
            public Dictionary<Vector3Int, Alliance> TileControllers = new Dictionary<Vector3Int, Alliance>();
            public Dictionary<Guid, BuildingTurnState> Buildings = new Dictionary<Guid, BuildingTurnState>();
            public Dictionary<Guid, SquadronTurnState> SquadronLosses = new Dictionary<Guid, SquadronTurnState>();
            public HashSet<Vector3Int> CombatTiles = new HashSet<Vector3Int>();
        }

        private readonly struct DivisionTurnState
        {
            public readonly Vector3Int TileId;
            public readonly float Strength;
            public readonly float Organization;
            public readonly string OrderName;
            public readonly float MovementProgress;

            public DivisionTurnState(
                Vector3Int tileId,
                float strength,
                float organization,
                string orderName,
                float movementProgress)
            {
                TileId = tileId;
                Strength = strength;
                Organization = organization;
                OrderName = orderName;
                MovementProgress = movementProgress;
            }
        }

        private readonly struct BuildingTurnState
        {
            public readonly int BuildLevel;
            public readonly int Damage;
            public readonly int FunctionalLevel;

            public BuildingTurnState(int buildLevel, int damage, int functionalLevel)
            {
                BuildLevel = buildLevel;
                Damage = damage;
                FunctionalLevel = functionalLevel;
            }
        }

        private readonly struct SquadronTurnState
        {
            public readonly int Ready;
            public readonly int Damaged;
            public readonly int Assigned;
            public readonly int Lost;

            public SquadronTurnState(int ready, int damaged, int assigned, int lost)
            {
                Ready = ready;
                Damaged = damaged;
                Assigned = assigned;
                Lost = lost;
            }
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
