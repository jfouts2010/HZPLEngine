using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Engine.Models.Ground;
using Engine.Service;
using Models.Gameplay.Campaign;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using CampaignTile = Models.Gameplay.Campaign.Tile;
using TileData = Models.Gameplay.Campaign.TileData;

namespace Engine.Monobehaviours.Managers
{
    [RequireComponent(typeof(GameManager))]
    public class PlaySceneCampaignRenderer : MonoBehaviour
    {
        public static bool IsPointerOverCampaignUi { get; private set; }

        [SerializeField] private GameManager gameManager;
        [SerializeField] private Grid grid;
        [SerializeField] private Tilemap tilemap;
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PanelSettings panelSettings;
        [SerializeField] private VisualTreeAsset campaignHudTemplate;

        private const string CampaignHudResourcePath = "UI/PlaySceneCampaignHud";
        private const string PanelSettingsResourcePath = "UI/PlayScenePanelSettings";

        private readonly Dictionary<Vector3Int, CampaignTile> tilesByCell = new Dictionary<Vector3Int, CampaignTile>();
        private readonly Dictionary<Vector3Int, Vector3> hexCentersByCell = new Dictionary<Vector3Int, Vector3>();
        private readonly Dictionary<Vector3Int, CampaignTile> tilesById = new Dictionary<Vector3Int, CampaignTile>();
        private readonly Dictionary<Vector3Int, TileData> tileDataById = new Dictionary<Vector3Int, TileData>();
        private readonly Dictionary<string, UnityEngine.Tilemaps.Tile> renderTilesByKey = new Dictionary<string, UnityEngine.Tilemaps.Tile>();
        private readonly Dictionary<string, Sprite> spritesByKey = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, Sprite> unitCounterSpritesByKey = new Dictionary<string, Sprite>();
        private readonly Dictionary<int, Sprite> combatBubbleSpritesByScore = new Dictionary<int, Sprite>();
        private Sprite movementArrowHeadSprite;
        private Material movementArrowMaterial;

        private const int TilePixelSize = 32;
        private const int HexFlatInsetPixels = 8;
        private const int TerritoryBorderSideCount = 6;
        private const float HexWidth = 1f;
        private const float HexHeight = 0.8660254f;
        private const float HexHorizontalSpacing = HexWidth * 0.75f;
        private const float TileLabelCharacterSize = 0.027f;
        private const float UnitCounterWorldWidth = 0.45f;
        private const float UnitCounterLabelCharacterSize = 0.018f;
        private const int UnitCounterPixelWidth = 30;
        private const int UnitCounterPixelHeight = 20;
        private const float CombatBubbleWorldWidth = 0.42f;
        private const float CombatBubbleLabelCharacterSize = 0.018f;
        private const int CombatBubblePixelSize = 36;
        private const float MovementArrowWidth = 0.045f;
        private const float MovementArrowEndTrim = 0.31f;
        private const float MovementArrowRouteOffset = 0.16f;
        private const float MovementArrowHeadWorldWidth = 0.18f;
        private const int MovementArrowHeadPixelSize = 18;
        private const float RailwayLineWidth = 0.05f;
        private const float AirRouteLineWidth = 0.025f;
        private const float AirMarkerRadius = 0.12f;
        private static readonly Color RailwayLineColor = new Color(0.38f, 0.32f, 0.24f);
        private static readonly Color SupplyHubMarkerColor = new Color(0.88f, 0.58f, 0.10f);
        private static readonly Color SupplyHubMarkerBorderColor = new Color(0.98f, 0.92f, 0.78f);
        private static readonly Vector3Int[] TerritoryBorderNeighborOffsets =
        {
            new Vector3Int(1, -1, 0),
            new Vector3Int(1, 0, -1),
            new Vector3Int(0, 1, -1),
            new Vector3Int(-1, 1, 0),
            new Vector3Int(-1, 0, 1),
            new Vector3Int(0, -1, 1)
        };
        private static readonly float[] TerritoryBorderSideAngles =
        {
            30f,
            -30f,
            -90f,
            -150f,
            150f,
            90f
        };

        private Transform labelRoot;
        private Transform unitCounterRoot;
        private Transform combatBubbleRoot;
        private Transform movementArrowRoot;
        private Transform railwayRoot;
        private Transform airOverlayRoot;
        private Transform airInspectionRoot;
        private Label titleLabel;
        private Label timeLabel;
        private Label selectedTileLabel;
        private Foldout neighborsFoldout;
        private VisualElement neighborsList;
        private Foldout unitsFoldout;
        private VisualElement unitsList;
        private Button pauseButton;
        private Button nextTurnButton;
        private Label simulationStateLabel;
        private VisualElement simulationControls;
        private Button mapTabButton;
        private Button airOpsTabButton;
        private VisualElement hudRoot;
        private VisualElement hudPanel;
        private VisualElement mapInfoContent;
        private ScrollView airOpsContent;
        private Label airOpsSummary;
        private Label airRequestCount;
        private Label airPackageCount;
        private Label airAirborneCount;
        private Button airRequestsButton;
        private Button airPackagesButton;
        private Button airFlightsButton;
        private Label airListTitle;
        private VisualElement airAllianceFilter;
        private Button airBlueFlightsButton;
        private Button airRedFlightsButton;
        private Label airInspectionStatus;
        private VisualElement airRequestsList;
        private VisualElement airPackagesList;
        private VisualElement airFlightsList;
        private VisualElement flightDetailBackdrop;
        private Label flightDetailTitle;
        private Label flightDetailSubtitle;
        private Button flightDetailClose;
        private ScrollView flightDetailScroll;
        private VisualElement flightDetailContent;
        private Font runtimeFont;
        private Vector3Int? selectedCell;
        private Guid selectedFlightId;
        private Guid inspectedFlightId;
        private bool showingAirOps;
        private AirOperationsView airOperationsView = AirOperationsView.Flights;
        private Alliance airFlightAlliance = Alliance.Bluefor;

        private enum AirOperationsView
        {
            Requests,
            Packages,
            Flights
        }

        private IEnumerator Start()
        {
            gameManager = gameManager != null ? gameManager : GetComponent<GameManager>();
            sceneCamera = sceneCamera != null ? sceneCamera : Camera.main;
            if (gameManager != null)
                gameManager.GameTurnCompleted += RefreshCampaignAfterGameTurn;

            EnsureTilemap();
            EnsureEventSystem();
            if (!EnsureUi())
                yield break;

            yield return null;

            RenderCampaign();
        }

        private void OnDestroy()
        {
            IsPointerOverCampaignUi = false;
            if (gameManager != null)
                gameManager.GameTurnCompleted -= RefreshCampaignAfterGameTurn;
        }

        private void Update()
        {
            if (gameManager == null || !gameManager.IsCampaignStarted)
                return;

            UpdateTimeUi();
            HandleTileSelection();
        }

        private void RefreshCampaignAfterGameTurn()
        {
            inspectedFlightId = Guid.Empty;
            RenderCampaign(false, true);
            SetAirInspectionStatus(string.Empty);
        }

        private void RenderCampaign(bool frameCamera = true, bool preserveSelection = false)
        {
            if (gameManager == null || !gameManager.IsCampaignStarted)
                return;

            var previousSelectedCell = preserveSelection ? selectedCell : null;

            tilemap.ClearAllTiles();
            tilesByCell.Clear();
            hexCentersByCell.Clear();
            tilesById.Clear();
            tileDataById.Clear();

            foreach (var tileData in gameManager.Tiles)
                tileDataById[tileData.TileId] = tileData;

            foreach (var campaignTile in gameManager.CampaignTiles)
                tilesById[campaignTile.Coordinates] = campaignTile;

            ClearLabels();
            ClearUnitCounters();
            ClearCombatBubbles();
            ClearMovementArrows();
            ClearRailwayLines();
            ClearAirOverlays();
            ClearAirInspection();

            foreach (var campaignTile in gameManager.CampaignTiles)
            {
                var cell = GetCell(campaignTile.Coordinates);
                var hexCenter = GetHexCenter(campaignTile.Coordinates);
                tilesByCell[cell] = campaignTile;
                hexCentersByCell[cell] = hexCenter;
                tilemap.SetTile(cell, GetRenderTile(campaignTile));
                tilemap.SetTileFlags(cell, TileFlags.None);
                tilemap.SetTransformMatrix(
                    cell,
                    Matrix4x4.TRS(
                        hexCenter - tilemap.GetCellCenterLocal(cell),
                        Quaternion.identity,
                        new Vector3(1f, HexHeight, 1f)));
                tilemap.SetColor(cell, Color.white);
                CreateTileLabel(campaignTile, hexCenter);
            }

            CreateUnitCounters();
            CreateRailwayLines();
            CreateMovementArrows();
            CreateCombatBubbles();
            CreateAirOverlays();
            CreateAirInspection();

            tilemap.RefreshAllTiles();
            if (frameCamera)
                FrameCamera();

            if (previousSelectedCell.HasValue && tilesByCell.ContainsKey(previousSelectedCell.Value))
            {
                selectedCell = previousSelectedCell;
                UpdateSelectedTileUi();
            }
            else
            {
                SelectFirstTile();
            }

            UpdateSummaryUi();
            UpdateAirOperationsUi();
        }

        private void EnsureTilemap()
        {
            if (grid == null)
            {
                var gridObject = new GameObject("Campaign Grid");
                grid = gridObject.AddComponent<Grid>();
            }

            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;
            grid.cellSize = Vector3.one;
            grid.cellGap = Vector3.zero;

            if (tilemap == null)
            {
                var tilemapObject = new GameObject("Campaign Tilemap");
                tilemapObject.transform.SetParent(grid.transform, false);
                tilemap = tilemapObject.AddComponent<Tilemap>();
                tilemapObject.AddComponent<TilemapRenderer>();
            }

            if (labelRoot == null)
            {
                var labelObject = new GameObject("Campaign Hex Labels");
                labelObject.transform.SetParent(grid.transform, false);
                labelRoot = labelObject.transform;
            }

            if (unitCounterRoot == null)
            {
                var counterObject = new GameObject("Campaign Unit Counters");
                counterObject.transform.SetParent(grid.transform, false);
                unitCounterRoot = counterObject.transform;
            }

            if (combatBubbleRoot == null)
            {
                var bubbleObject = new GameObject("Campaign Combat Bubbles");
                bubbleObject.transform.SetParent(grid.transform, false);
                combatBubbleRoot = bubbleObject.transform;
            }

            if (movementArrowRoot == null)
            {
                var arrowObject = new GameObject("Campaign Movement Arrows");
                arrowObject.transform.SetParent(grid.transform, false);
                movementArrowRoot = arrowObject.transform;
            }

            if (railwayRoot == null)
            {
                var railwayObject = new GameObject("Campaign Railways");
                railwayObject.transform.SetParent(grid.transform, false);
                railwayRoot = railwayObject.transform;
            }

            if (airOverlayRoot == null)
            {
                var airObject = new GameObject("Campaign Air Operations");
                airObject.transform.SetParent(grid.transform, false);
                airOverlayRoot = airObject.transform;
            }

            if (airInspectionRoot == null)
            {
                var inspectionObject = new GameObject("Campaign Air Route Inspection");
                inspectionObject.transform.SetParent(grid.transform, false);
                airInspectionRoot = inspectionObject.transform;
            }
        }

        private void EnsureEventSystem()
        {
            var eventSystem = EventSystem.current != null
                ? EventSystem.current
                : FindFirstObjectByType<EventSystem>();

            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        private bool EnsureUi()
        {
            if (uiDocument == null)
                uiDocument = FindFirstObjectByType<UIDocument>();

            if (campaignHudTemplate == null)
                campaignHudTemplate = Resources.Load<VisualTreeAsset>(CampaignHudResourcePath);

            if (panelSettings == null)
                panelSettings = Resources.Load<PanelSettings>(PanelSettingsResourcePath);

            if (campaignHudTemplate == null)
            {
                Debug.LogError($"PlaySceneCampaignRenderer requires a Campaign HUD UXML document at Resources/{CampaignHudResourcePath}.uxml.");
                return false;
            }

            if (panelSettings == null)
            {
                Debug.LogError($"PlaySceneCampaignRenderer requires PanelSettings at Resources/{PanelSettingsResourcePath}.asset.");
                return false;
            }

            if (uiDocument == null)
            {
                var uiObject = new GameObject("Campaign UI");
                uiDocument = uiObject.AddComponent<UIDocument>();
            }

            uiDocument.panelSettings = panelSettings;
            uiDocument.visualTreeAsset = campaignHudTemplate;

            var root = uiDocument.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;

            titleLabel = root.Q<Label>("campaign-title");
            timeLabel = root.Q<Label>("campaign-time");
            selectedTileLabel = root.Q<Label>("selected-tile");
            neighborsFoldout = root.Q<Foldout>("neighbors-foldout");
            neighborsList = root.Q<VisualElement>("neighbors-list");
            unitsFoldout = root.Q<Foldout>("units-foldout");
            unitsList = root.Q<VisualElement>("units-list");
            pauseButton = root.Q<Button>("pause-button");
            nextTurnButton = root.Q<Button>("next-turn-button");
            simulationStateLabel = root.Q<Label>("simulation-state-label");
            simulationControls = root.Q<VisualElement>("simulation-controls");
            mapTabButton = root.Q<Button>("map-tab-button");
            airOpsTabButton = root.Q<Button>("air-ops-tab-button");
            hudRoot = root.Q<VisualElement>("campaign-hud-root");
            hudPanel = root.Q<VisualElement>("campaign-hud-panel");
            mapInfoContent = root.Q<VisualElement>("map-info-content");
            airOpsContent = root.Q<ScrollView>("air-ops-content");
            airOpsSummary = root.Q<Label>("air-ops-summary");
            airRequestCount = root.Q<Label>("air-request-count");
            airPackageCount = root.Q<Label>("air-package-count");
            airAirborneCount = root.Q<Label>("air-airborne-count");
            airRequestsButton = root.Q<Button>("air-requests-button");
            airPackagesButton = root.Q<Button>("air-packages-button");
            airFlightsButton = root.Q<Button>("air-flights-button");
            airListTitle = root.Q<Label>("air-list-title");
            airAllianceFilter = root.Q<VisualElement>("air-alliance-filter");
            airBlueFlightsButton = root.Q<Button>("air-blue-flights-button");
            airRedFlightsButton = root.Q<Button>("air-red-flights-button");
            airInspectionStatus = root.Q<Label>("air-inspection-status");
            airRequestsList = root.Q<VisualElement>("air-requests-list");
            airPackagesList = root.Q<VisualElement>("air-packages-list");
            airFlightsList = root.Q<VisualElement>("air-flights-list");
            flightDetailBackdrop = root.Q<VisualElement>("flight-detail-backdrop");
            flightDetailTitle = root.Q<Label>("flight-detail-title");
            flightDetailSubtitle = root.Q<Label>("flight-detail-subtitle");
            flightDetailClose = root.Q<Button>("flight-detail-close");
            flightDetailScroll = root.Q<ScrollView>("flight-detail-scroll");
            flightDetailContent = root.Q<VisualElement>("flight-detail-content");

            ApplyRuntimeFont(titleLabel);
            ApplyRuntimeFont(timeLabel);
            ApplyRuntimeFont(selectedTileLabel);
            ApplyRuntimeFont(pauseButton);
            ApplyRuntimeFont(nextTurnButton);
            ApplyRuntimeFont(simulationStateLabel);
            ApplyRuntimeFont(mapTabButton);
            ApplyRuntimeFont(airOpsTabButton);
            ApplyRuntimeFont(airOpsSummary);
            ApplyRuntimeFont(airRequestCount);
            ApplyRuntimeFont(airPackageCount);
            ApplyRuntimeFont(airAirborneCount);
            ApplyRuntimeFont(airRequestsButton);
            ApplyRuntimeFont(airPackagesButton);
            ApplyRuntimeFont(airFlightsButton);
            ApplyRuntimeFont(airListTitle);
            ApplyRuntimeFont(airBlueFlightsButton);
            ApplyRuntimeFont(airRedFlightsButton);
            ApplyRuntimeFont(airInspectionStatus);
            ApplyRuntimeFont(flightDetailTitle);
            ApplyRuntimeFont(flightDetailSubtitle);
            ApplyRuntimeFont(flightDetailClose);

            if (hudRoot != null)
                hudRoot.pickingMode = PickingMode.Ignore;

            if (hudPanel != null)
            {
                hudPanel.pickingMode = PickingMode.Position;
                hudPanel.RegisterCallback<PointerEnterEvent>(_ => IsPointerOverCampaignUi = true);
                hudPanel.RegisterCallback<PointerLeaveEvent>(_ => IsPointerOverCampaignUi = false);
            }

            if (simulationControls != null)
            {
                simulationControls.pickingMode = PickingMode.Position;
                simulationControls.RegisterCallback<PointerEnterEvent>(_ => IsPointerOverCampaignUi = true);
                simulationControls.RegisterCallback<PointerLeaveEvent>(_ => IsPointerOverCampaignUi = false);
            }

            if (airOpsContent != null)
                airOpsContent.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            if (flightDetailScroll != null)
                flightDetailScroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;

            if (neighborsFoldout != null)
            {
                neighborsFoldout.pickingMode = PickingMode.Position;
                neighborsFoldout.RegisterValueChangedCallback(_ => UpdateNeighborsList());
            }

            if (unitsFoldout != null)
            {
                unitsFoldout.pickingMode = PickingMode.Position;
                unitsFoldout.RegisterValueChangedCallback(_ => UpdateUnitsList());
            }

            if (pauseButton != null)
            {
                pauseButton.pickingMode = PickingMode.Position;
                pauseButton.clicked -= TogglePause;
                pauseButton.clicked += TogglePause;
            }

            if (nextTurnButton != null)
            {
                nextTurnButton.pickingMode = PickingMode.Position;
                nextTurnButton.clicked += AdvanceOneGameTurn;
            }

            if (mapTabButton != null)
            {
                mapTabButton.pickingMode = PickingMode.Position;
                mapTabButton.clicked += () => ShowAirOperations(false);
            }

            if (airOpsTabButton != null)
            {
                airOpsTabButton.pickingMode = PickingMode.Position;
                airOpsTabButton.clicked += () => ShowAirOperations(true);
            }

            if (airRequestsButton != null)
                airRequestsButton.clicked += () => ShowAirOperationsView(AirOperationsView.Requests);
            if (airPackagesButton != null)
                airPackagesButton.clicked += () => ShowAirOperationsView(AirOperationsView.Packages);
            if (airFlightsButton != null)
                airFlightsButton.clicked += () => ShowAirOperationsView(AirOperationsView.Flights);
            if (airBlueFlightsButton != null)
                airBlueFlightsButton.clicked += () => ShowAirFlightAlliance(Alliance.Bluefor);
            if (airRedFlightsButton != null)
                airRedFlightsButton.clicked += () => ShowAirFlightAlliance(Alliance.Redfor);

            if (flightDetailClose != null)
            {
                flightDetailClose.pickingMode = PickingMode.Position;
                flightDetailClose.clicked += CloseFlightDetails;
            }
            if (flightDetailBackdrop != null)
            {
                flightDetailBackdrop.RegisterCallback<PointerEnterEvent>(_ => IsPointerOverCampaignUi = true);
                flightDetailBackdrop.RegisterCallback<PointerLeaveEvent>(_ => IsPointerOverCampaignUi = false);
                flightDetailBackdrop.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target == flightDetailBackdrop)
                        CloseFlightDetails();
                });
            }

            ShowAirOperations(false);
            ShowAirOperationsView(AirOperationsView.Flights);
            CloseFlightDetails();

            if (titleLabel == null
                || timeLabel == null
                || selectedTileLabel == null
                || pauseButton == null
                || nextTurnButton == null)
            {
                Debug.LogError("PlaySceneCampaignRenderer could not find one or more required elements in the Campaign HUD UXML.");
                return false;
            }

            return true;
        }

        private void ApplyRuntimeFont(TextElement textElement)
        {
            if (textElement == null)
                return;

            runtimeFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (runtimeFont != null)
                textElement.style.unityFontDefinition = FontDefinition.FromFont(runtimeFont);
            else
                Debug.LogWarning("PlaySceneCampaignRenderer could not load Unity's LegacyRuntime.ttf font.");

        }

        private void UpdateSummaryUi()
        {
            if (titleLabel == null)
                return;

            titleLabel.text = string.IsNullOrWhiteSpace(gameManager.TemplateName)
                ? "Campaign"
                : gameManager.TemplateName;
            UpdateTimeUi();
            UpdateSelectedTileUi();
        }

        private void ShowAirOperations(bool showAirOperations)
        {
            showingAirOps = showAirOperations;
            if (mapInfoContent != null)
                mapInfoContent.style.display = showingAirOps ? DisplayStyle.None : DisplayStyle.Flex;
            if (airOpsContent != null)
                airOpsContent.style.display = showingAirOps ? DisplayStyle.Flex : DisplayStyle.None;

            hudPanel?.EnableInClassList("campaign-hud-panel--air-ops", showingAirOps);
            mapTabButton?.EnableInClassList("campaign-hud-tab--selected", !showingAirOps);
            airOpsTabButton?.EnableInClassList("campaign-hud-tab--selected", showingAirOps);
            if (showingAirOps)
                UpdateAirOperationsUi();
        }

        private void ShowAirOperationsView(AirOperationsView view)
        {
            airOperationsView = view;
            airRequestsList?.EnableInClassList(
                "campaign-air-list--hidden",
                view != AirOperationsView.Requests);
            airPackagesList?.EnableInClassList(
                "campaign-air-list--hidden",
                view != AirOperationsView.Packages);
            airFlightsList?.EnableInClassList(
                "campaign-air-list--hidden",
                view != AirOperationsView.Flights);
            airAllianceFilter?.EnableInClassList(
                "campaign-air-alliance-filter--hidden",
                view != AirOperationsView.Flights);

            airRequestsButton?.EnableInClassList(
                "campaign-air-view-tab--selected",
                view == AirOperationsView.Requests);
            airPackagesButton?.EnableInClassList(
                "campaign-air-view-tab--selected",
                view == AirOperationsView.Packages);
            airFlightsButton?.EnableInClassList(
                "campaign-air-view-tab--selected",
                view == AirOperationsView.Flights);

            if (airListTitle != null)
            {
                airListTitle.text = view switch
                {
                    AirOperationsView.Requests => "CURRENT MISSION REQUESTS",
                    AirOperationsView.Packages => "CURRENT AIR PACKAGES",
                    _ => "CURRENT FLIGHTS — SELECT A ROW FOR DETAILS"
                };
            }

            if (airOpsContent != null)
                airOpsContent.scrollOffset = Vector2.zero;
        }

        private void ShowAirFlightAlliance(Alliance alliance)
        {
            if (alliance != Alliance.Bluefor && alliance != Alliance.Redfor)
                return;

            airFlightAlliance = alliance;
            airBlueFlightsButton?.EnableInClassList(
                "campaign-air-alliance-tab--selected",
                alliance == Alliance.Bluefor);
            airRedFlightsButton?.EnableInClassList(
                "campaign-air-alliance-tab--selected",
                alliance == Alliance.Redfor);
            if (airOpsContent != null)
                airOpsContent.scrollOffset = Vector2.zero;
            UpdateAirOperationsUi();
        }

        private void UpdateAirOperationsUi()
        {
            if (gameManager == null || airOpsSummary == null)
                return;

            var commanders = new[]
                {
                    gameManager.GetAllianceAirTaskingCommander(Alliance.Bluefor),
                    gameManager.GetAllianceAirTaskingCommander(Alliance.Redfor)
                }
                .Where(commander => commander != null)
                .ToList();
            var requests = commanders
                .SelectMany(commander => commander.MissionRequests ?? Array.Empty<AirMissionRequest>())
                .Where(request => request != null && !request.IsTerminal)
                .OrderBy(request => request.Alliance)
                .ThenByDescending(request => request.Priority)
                .ToList();
            var packages = commanders
                .SelectMany(commander => commander.Packages ?? Array.Empty<AirPackage>())
                .Where(package => package != null && !package.HasPhysicallyEnded)
                .OrderBy(package => package.Alliance)
                .ThenBy(package => package.EarliestTakeoffTime)
                .ToList();
            var flights = packages
                .SelectMany(package => package.Flights ?? new List<AirFlight>())
                .Where(flight => flight != null && !flight.HasPhysicallyEnded)
                .OrderBy(flight => flight.PlannedTakeoffTime)
                .ToList();
            var airborneCount = flights.Count(flight => flight.IsAirborne);

            airOpsSummary.text =
                $"Planning cycle {commanders.Select(commander => commander.PlanningCycle).DefaultIfEmpty().Max()}  •  " +
                $"{flights.Count} current flights\n" +
                "Airborne flight positions and remaining routes are drawn on the map.";
            ApplyRuntimeFont(airOpsSummary);
            if (airRequestCount != null)
                airRequestCount.text = requests.Count.ToString();
            if (airPackageCount != null)
                airPackageCount.text = packages.Count.ToString();
            if (airAirborneCount != null)
                airAirborneCount.text = airborneCount.ToString();

            if (airRequestsButton != null)
                airRequestsButton.text = $"Requests  {requests.Count}";
            if (airPackagesButton != null)
                airPackagesButton.text = $"Packages  {packages.Count}";
            if (airFlightsButton != null)
                airFlightsButton.text = $"Flights  {flights.Count}";
            if (airBlueFlightsButton != null)
                airBlueFlightsButton.text =
                    $"Blue flights  {flights.Count(flight => GetFlightAlliance(flight, packages) == Alliance.Bluefor)}";
            if (airRedFlightsButton != null)
                airRedFlightsButton.text =
                    $"Red flights  {flights.Count(flight => GetFlightAlliance(flight, packages) == Alliance.Redfor)}";

            RebuildAirRequestsList(requests);
            RebuildAirPackagesList(packages, commanders);
            RebuildAirFlightsList(
                flights.Where(flight => GetFlightAlliance(flight, packages) == airFlightAlliance).ToList(),
                packages);
            if (selectedFlightId != Guid.Empty)
                RefreshFlightDetails();
        }

        private void RebuildAirRequestsList(IReadOnlyList<AirMissionRequest> requests)
        {
            if (airRequestsList == null)
                return;

            airRequestsList.Clear();
            if (requests.Count == 0)
            {
                airRequestsList.Add(CreateAirEmptyLabel("No current mission requests."));
                return;
            }

            foreach (var request in requests)
            {
                var title = $"{GetAllianceLabel(request.Alliance)} {GetMissionLabel(request.RequestType)}  •  {request.State}";
                var fields = new List<AirCardField>
                {
                    new AirCardField("Request ID", ShortId(request.MissionRequestId)),
                    new AirCardField("Priority", request.Priority.ToString("0.0")),
                    new AirCardField("Mission area", $"Hex {FormatTile(request.MissionArea?.CenterTileId ?? default)}"),
                    new AirCardField("Effect window", $"{request.EffectStart:MM-dd HH:mm} – {request.EffectEnd:MM-dd HH:mm}"),
                    new AirCardField(
                        "Demand",
                        $"{request.DesiredAircraftStrength} aircraft" +
                        (request.DesiredSupportSlots > 0
                            ? $" / {request.DesiredSupportSlots} support slots"
                            : string.Empty))
                };
                if (!string.IsNullOrWhiteSpace(request.Rationale))
                    fields.Add(new AirCardField("Intent", request.Rationale));
                airRequestsList.Add(CreateAirCard(request.Alliance, title, fields));
            }
        }

        private void RebuildAirPackagesList(
            IReadOnlyList<AirPackage> packages,
            IReadOnlyList<AllianceAirTaskingCommander> commanders)
        {
            if (airPackagesList == null)
                return;

            airPackagesList.Clear();
            if (packages.Count == 0)
            {
                airPackagesList.Add(CreateAirEmptyLabel("No current packages."));
                return;
            }

            foreach (var package in packages)
            {
                var request = commanders
                    .FirstOrDefault(commander => commander.Alliance == package.Alliance)?
                    .MissionRequests?
                    .FirstOrDefault(candidate => candidate.MissionRequestId == package.MissionRequestId);
                var aircraftCount = (package.Flights ?? new List<AirFlight>())
                    .Where(flight => flight != null)
                    .Sum(flight => flight.AircraftIds?.Count ?? 0);
                var title =
                    $"{GetAllianceLabel(package.Alliance)} PKG {ShortId(package.PackageId)}  •  {package.LifecycleState}";
                var fields = new List<AirCardField>
                {
                    new AirCardField(
                        "Mission",
                        GetMissionLabel(request?.RequestType ?? package.Flights?.FirstOrDefault()?.MissionType ?? default)),
                    new AirCardField("Composition", $"{package.Flights?.Count ?? 0} flights / {aircraftCount} aircraft"),
                    new AirCardField("Earliest launch", package.EarliestTakeoffTime.ToString("MM-dd HH:mm")),
                    request?.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Discrete
                        ? new AirCardField("Effect time", package.EffectStart.ToString("MM-dd HH:mm"))
                        : new AirCardField("Effect window", $"{package.EffectStart:MM-dd HH:mm} – {package.EffectEnd:MM-dd HH:mm}"),
                    new AirCardField("Source request", ShortId(package.MissionRequestId))
                };
                if (package.HasRendezvous)
                    fields.Add(new AirCardField("Rendezvous", $"Hex {FormatTile(package.RendezvousTileId)}"));
                if (!string.IsNullOrWhiteSpace(package.Rationale))
                    fields.Add(new AirCardField("Intent", package.Rationale));
                airPackagesList.Add(CreateAirCard(package.Alliance, title, fields));
            }
        }

        private void RebuildAirFlightsList(
            IReadOnlyList<AirFlight> flights,
            IReadOnlyList<AirPackage> packages)
        {
            if (airFlightsList == null)
                return;

            airFlightsList.Clear();
            if (flights.Count == 0)
            {
                airFlightsList.Add(CreateAirEmptyLabel("No current flights."));
                return;
            }

            foreach (var flight in flights)
            {
                var package = packages.FirstOrDefault(candidate => candidate.PackageId == flight.OwningPackageId);
                var alliance = package?.Alliance ?? Alliance.Neutral;
                var squadron = gameManager.squadronSystem?.Squadrons?
                    .FirstOrDefault(candidate => candidate.SquadronId == flight.SquadronId);
                var nextWaypoint = flight.Route != null
                                   && flight.CurrentWaypointIndex >= 0
                                   && flight.CurrentWaypointIndex < flight.Route.Count
                    ? flight.Route[flight.CurrentWaypointIndex]
                    : null;
                var altitude = flight.HasPosition ? $"{flight.PositionFeet.y:0} ft" : "not airborne";
                var title =
                    $"{GetAllianceLabel(alliance)} {GetFlightName(flight, squadron)}  •  {flight.ExecutionPhase}";
                var fields = new List<AirCardField>
                {
                    new AirCardField("Mission", GetMissionLabel(flight.MissionType)),
                    new AirCardField("Aircraft", (flight.AircraftIds?.Count ?? 0).ToString()),
                    new AirCardField("Execution", $"{flight.LifecycleState} / {flight.ExecutionPhase}"),
                    new AirCardField(
                        "Position",
                        altitude + (flight.HasPosition ? $" / heading {flight.HeadingDegrees:0}°" : string.Empty)),
                    new AirCardField(
                        "Next action",
                        nextWaypoint == null ? "—" : GetWaypointLabel(nextWaypoint.Action)),
                    new AirCardField("Package", ShortId(flight.OwningPackageId))
                };
                airFlightsList.Add(CreateAirCard(
                    alliance,
                    title,
                    fields,
                    () => OpenFlightDetails(flight.FlightId),
                    () => InspectFlightRoute(flight.FlightId)));
            }
        }

        private VisualElement CreateAirCard(
            Alliance alliance,
            string title,
            IReadOnlyList<AirCardField> fields,
            Action onClick = null,
            Action onRightClick = null)
        {
            VisualElement card;
            if (onClick == null)
            {
                card = new VisualElement();
            }
            else
            {
                var button = new Button(onClick) { text = string.Empty };
                button.pickingMode = PickingMode.Position;
                button.AddToClassList("campaign-air-card--clickable");
                if (onRightClick != null)
                {
                    button.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 1)
                            return;

                        onRightClick();
                        evt.StopImmediatePropagation();
                    });
                }
                card = button;
            }
            card.AddToClassList("campaign-air-card");
            if (alliance == Alliance.Bluefor)
                card.AddToClassList("campaign-air-card--blue");
            else if (alliance == Alliance.Redfor)
                card.AddToClassList("campaign-air-card--red");

            var titleLabelElement = new Label(title);
            titleLabelElement.AddToClassList("campaign-air-card-title");
            titleLabelElement.pickingMode = PickingMode.Ignore;
            ApplyRuntimeFont(titleLabelElement);
            card.Add(titleLabelElement);

            foreach (var field in fields ?? Array.Empty<AirCardField>())
            {
                var row = new VisualElement();
                row.AddToClassList("campaign-air-field-row");
                row.pickingMode = PickingMode.Ignore;

                var fieldLabel = new Label(field.Label);
                fieldLabel.AddToClassList("campaign-air-field-label");
                fieldLabel.pickingMode = PickingMode.Ignore;
                ApplyRuntimeFont(fieldLabel);
                row.Add(fieldLabel);

                var fieldValue = new Label(field.Value);
                fieldValue.AddToClassList("campaign-air-field-value");
                fieldValue.pickingMode = PickingMode.Ignore;
                ApplyRuntimeFont(fieldValue);
                row.Add(fieldValue);
                card.Add(row);
            }

            card.style.minHeight = 48f + Math.Max(1, fields?.Count ?? 0) * 24f;
            return card;
        }

        private void OpenFlightDetails(Guid flightId)
        {
            selectedFlightId = flightId;
            if (flightDetailBackdrop != null)
            {
                flightDetailBackdrop.style.display = DisplayStyle.Flex;
                flightDetailBackdrop.BringToFront();
            }
            RefreshFlightDetails();
        }

        private void CloseFlightDetails()
        {
            selectedFlightId = Guid.Empty;
            if (flightDetailBackdrop != null)
                flightDetailBackdrop.style.display = DisplayStyle.None;
            flightDetailContent?.Clear();
        }

        private void InspectFlightRoute(Guid flightId)
        {
            if (gameManager == null || !gameManager.IsGamePaused)
            {
                SetAirInspectionStatus("Pause the campaign before inspecting a flight route.");
                return;
            }

            if (!TryFindFlight(flightId, out var flight, out _, out _))
            {
                SetAirInspectionStatus("That flight is no longer available for inspection.");
                return;
            }

            if (!flight.HasPosition && (flight.Route == null || flight.Route.Count == 0))
            {
                SetAirInspectionStatus("This flight has no position or planned route to display.");
                return;
            }

            inspectedFlightId = flightId;
            ClearAirInspection();
            CreateAirInspection();
            FrameAirInspection(flight);

            var squadron = gameManager.squadronSystem?.Squadrons?
                .FirstOrDefault(candidate => candidate.SquadronId == flight.SquadronId);
            SetAirInspectionStatus(
                $"Inspecting {GetFlightName(flight, squadron)}. Route highlight clears on the next game turn.");
        }

        private void SetAirInspectionStatus(string message)
        {
            if (airInspectionStatus == null)
                return;

            airInspectionStatus.text = message ?? string.Empty;
            airInspectionStatus.EnableInClassList(
                "campaign-air-inspection-status--visible",
                !string.IsNullOrWhiteSpace(message));
        }

        private void RefreshFlightDetails()
        {
            if (selectedFlightId == Guid.Empty || flightDetailContent == null)
                return;

            if (!TryFindFlight(selectedFlightId, out var flight, out var package, out var commander))
            {
                CloseFlightDetails();
                return;
            }

            var alliance = package?.Alliance ?? commander?.Alliance ?? Alliance.Neutral;
            var squadron = gameManager.squadronSystem?.Squadrons?
                .FirstOrDefault(candidate => candidate.SquadronId == flight.SquadronId);
            var nextWaypoint = flight.Route != null
                               && flight.CurrentWaypointIndex >= 0
                               && flight.CurrentWaypointIndex < flight.Route.Count
                ? flight.Route[flight.CurrentWaypointIndex]
                : null;

            flightDetailTitle.text = GetFlightName(flight, squadron);
            flightDetailSubtitle.text =
                $"{GetAllianceLabel(alliance)}  •  {GetMissionLabel(flight.MissionType)}  •  " +
                $"{flight.ExecutionPhase}";
            var previousScrollOffset = flightDetailScroll?.scrollOffset ?? Vector2.zero;
            flightDetailContent.Clear();

            AddFlightDetailSection(
                "IDENTITY & TASKING",
                $"Flight ID: {flight.FlightId:N}",
                $"Squadron: {(string.IsNullOrWhiteSpace(squadron?.Name) ? "Unknown" : squadron.Name)}",
                $"Package: {(package == null ? "Unknown" : package.PackageId.ToString("N"))}",
                $"Mission: {GetMissionLabel(flight.MissionType)}",
                $"Role in package: {(flight.IsRequired ? "Required" : "Supporting")}",
                $"Assigned aircraft: {flight.AircraftIds?.Count ?? 0}");

            AddFlightDetailSection(
                "EXECUTION STATE",
                $"Lifecycle: {flight.LifecycleState}",
                $"Phase: {flight.ExecutionPhase}",
                $"Mission achieved: {(flight.MissionAchieved ? "Yes" : "No")}",
                $"Rendezvous hold: {(flight.IsWaitingAtRendezvous ? "Waiting" : "No")}",
                $"Route progress: {Mathf.Clamp(flight.CurrentWaypointIndex + 1, 0, flight.Route?.Count ?? 0)} of {flight.Route?.Count ?? 0}",
                $"Next action: {(nextWaypoint == null ? "None" : GetWaypointLabel(nextWaypoint.Action))}");

            AddFlightDetailSection(
                "POSITION & SCHEDULE",
                flight.HasPosition
                    ? $"Position: X {flight.PositionFeet.x:0} ft / Y {flight.PositionFeet.z:0} ft"
                    : "Position: Not yet established",
                flight.HasPosition
                    ? $"Altitude: {flight.PositionFeet.y:0} ft"
                    : "Altitude: Ground",
                $"Heading: {flight.HeadingDegrees:0}°",
                $"Planned takeoff: {flight.PlannedTakeoffTime:yyyy-MM-dd HH:mm}",
                flight.HasSustainedEffect
                    ? $"Effect window: {flight.EffectStart:yyyy-MM-dd HH:mm} – {flight.EffectEnd:yyyy-MM-dd HH:mm}"
                    : $"Effect time: {flight.EffectStart:yyyy-MM-dd HH:mm}",
                $"Mission area: Hex {FormatTile(flight.MissionArea?.CenterTileId ?? default)}");

            AddAircraftDetailSection(flight, squadron);
            AddRouteDetailSection(flight);
            AddSupportDetailSection(flight);
            AddExecutionEventSection(flight);
            if (flightDetailScroll != null)
            {
                flightDetailScroll.schedule.Execute(
                    () => flightDetailScroll.scrollOffset = previousScrollOffset);
            }
        }

        private bool TryFindFlight(
            Guid flightId,
            out AirFlight flight,
            out AirPackage package,
            out AllianceAirTaskingCommander commander)
        {
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var candidateCommander = gameManager.GetAllianceAirTaskingCommander(alliance);
                if (candidateCommander == null)
                    continue;

                foreach (var candidatePackage in candidateCommander.Packages ?? Array.Empty<AirPackage>())
                {
                    var candidateFlight = candidatePackage?.Flights?
                        .FirstOrDefault(item => item != null && item.FlightId == flightId);
                    if (candidateFlight == null)
                        continue;

                    flight = candidateFlight;
                    package = candidatePackage;
                    commander = candidateCommander;
                    return true;
                }
            }

            flight = null;
            package = null;
            commander = null;
            return false;
        }

        private void AddFlightDetailSection(string title, params string[] lines)
        {
            var section = CreateFlightDetailSection(title);
            section.style.minHeight = 43f + lines.Count(line => !string.IsNullOrWhiteSpace(line)) * 23f;
            foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                var label = new Label(line);
                label.AddToClassList("flight-detail-line");
                ApplyRuntimeFont(label);
                section.Add(label);
            }

            flightDetailContent.Add(section);
        }

        private void AddAircraftDetailSection(AirFlight flight, Squadron squadron)
        {
            var section = CreateFlightDetailSection("ASSIGNED AIRCRAFT");
            var aircraftById = (squadron?.Aircraft ?? new List<CampaignAircraft>())
                .Where(aircraft => aircraft != null)
                .ToDictionary(aircraft => aircraft.AircraftId);
            var aircraftIds = flight.AircraftIds ?? new List<Guid>();
            if (aircraftIds.Count == 0)
            {
                AddFlightDetailMessage(section, "No aircraft assigned.");
            }
            else
            {
                for (var index = 0; index < aircraftIds.Count; index++)
                {
                    aircraftById.TryGetValue(aircraftIds[index], out var aircraft);
                    var loadoutCount = aircraft?.Loadout?.Sum(item => item?.Count ?? 0) ?? 0;
                    AddFlightDetailMessage(
                        section,
                        $"{index + 1}. Aircraft {ShortId(aircraftIds[index])}  •  " +
                        $"{aircraft?.Status.ToString() ?? "Unknown"}  •  {loadoutCount} stores");
                }
            }

            section.style.minHeight = 43f + Math.Max(1, aircraftIds.Count) * 23f;
            flightDetailContent.Add(section);
        }

        private void AddRouteDetailSection(AirFlight flight)
        {
            var section = CreateFlightDetailSection($"ROUTE ({flight.Route?.Count ?? 0} WAYPOINTS)");
            var route = flight.Route ?? new List<AirWaypoint>();
            if (route.Count == 0)
            {
                AddFlightDetailMessage(section, "No route was planned.");
            }
            else
            {
                for (var index = 0; index < route.Count; index++)
                {
                    var waypoint = route[index];
                    if (waypoint == null)
                        continue;

                    var row = new Label(
                        $"{index + 1}. {GetWaypointLabel(waypoint.Action)}  •  " +
                        $"{waypoint.PlannedArrivalTime:MM-dd HH:mm}\n" +
                        $"X {waypoint.PositionFeet.x:0} / Y {waypoint.PositionFeet.z:0} / " +
                        $"ALT {waypoint.PositionFeet.y:0} ft" +
                        (waypoint.HasRepeat ? $"  •  repeats until {waypoint.RepeatUntil:HH:mm}" : string.Empty));
                    row.AddToClassList("flight-detail-route-row");
                    if (index == flight.CurrentWaypointIndex)
                        row.AddToClassList("flight-detail-route-row--current");
                    ApplyRuntimeFont(row);
                    section.Add(row);
                }
            }

            section.style.minHeight = 43f + Math.Max(1, route.Count) * 54f;
            flightDetailContent.Add(section);
        }

        private void AddSupportDetailSection(AirFlight flight)
        {
            var reservations = flight.SupportReservations ?? new List<AirSupportReservation>();
            if (reservations.Count == 0 && flight.ProvidedSupportSlots <= 0)
                return;

            var section = CreateFlightDetailSection("SUPPORT COMMITMENTS");
            AddFlightDetailMessage(section, $"Support capacity: {flight.ProvidedSupportSlots} slots");
            foreach (var reservation in reservations)
            {
                if (reservation == null)
                    continue;
                AddFlightDetailMessage(
                    section,
                    $"{reservation.SlotCount} slots for PKG {ShortId(reservation.ConsumingPackageId)}  •  " +
                    $"{reservation.StartTime:MM-dd HH:mm} – {reservation.EndTime:HH:mm}");
            }
            section.style.minHeight = 66f + reservations.Count * 23f;
            flightDetailContent.Add(section);
        }

        private void AddExecutionEventSection(AirFlight flight)
        {
            var section = CreateFlightDetailSection($"EXECUTION LOG ({flight.ExecutionEvents?.Count ?? 0})");
            var events = (flight.ExecutionEvents ?? new List<FlightExecutionEvent>())
                .Where(entry => entry != null)
                .OrderByDescending(entry => entry.OccurredAt)
                .ToList();
            if (events.Count == 0)
            {
                AddFlightDetailMessage(section, "No execution events recorded yet.");
            }
            else
            {
                foreach (var executionEvent in events)
                {
                    AddFlightDetailMessage(
                        section,
                        $"{executionEvent.OccurredAt:MM-dd HH:mm}  •  {GetWaypointLabel(executionEvent.Action)}" +
                        (string.IsNullOrWhiteSpace(executionEvent.Detail)
                            ? string.Empty
                            : $"\n{executionEvent.Detail}"));
                }
            }

            section.style.minHeight = 43f + Math.Max(1, events.Count) * 45f;
            flightDetailContent.Add(section);
        }

        private VisualElement CreateFlightDetailSection(string title)
        {
            var section = new VisualElement();
            section.AddToClassList("flight-detail-section");
            var titleLabelElement = new Label(title);
            titleLabelElement.AddToClassList("flight-detail-section-title");
            ApplyRuntimeFont(titleLabelElement);
            section.Add(titleLabelElement);
            return section;
        }

        private void AddFlightDetailMessage(VisualElement section, string message)
        {
            var label = new Label(message);
            label.AddToClassList("flight-detail-line");
            ApplyRuntimeFont(label);
            section.Add(label);
        }

        private Label CreateAirEmptyLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("campaign-air-empty");
            ApplyRuntimeFont(label);
            return label;
        }

        private static Alliance GetFlightAlliance(
            AirFlight flight,
            IReadOnlyList<AirPackage> packages)
        {
            if (flight == null || packages == null)
                return Alliance.Neutral;

            return packages
                       .FirstOrDefault(package =>
                           package != null && package.PackageId == flight.OwningPackageId)?
                       .Alliance
                   ?? Alliance.Neutral;
        }

        private static string ShortId(Guid id)
        {
            return id == Guid.Empty ? "——" : id.ToString("N").Substring(0, 6).ToUpperInvariant();
        }

        private static string GetAllianceLabel(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => "BLUE",
                Alliance.Redfor => "RED",
                _ => "NEUTRAL"
            };
        }

        private static string GetMissionLabel(AirMissionRequestType mission)
        {
            return mission switch
            {
                AirMissionRequestType.DefensiveCounterAirPatrol => "DCA Patrol",
                AirMissionRequestType.OffensiveCounterAirSweep => "OCA Sweep",
                AirMissionRequestType.ProvideAirborneC2 => "Airborne C2",
                AirMissionRequestType.ProvideAerialRefueling => "Aerial Refueling",
                _ => mission.ToString()
            };
        }

        private static string GetWaypointLabel(AirWaypointAction action)
        {
            return action switch
            {
                AirWaypointAction.StationEntry => "On station",
                AirWaypointAction.StationEndpoint => "Station end",
                AirWaypointAction.MissionAction => "Mission action",
                AirWaypointAction.ReturnToBase => "Return to base",
                _ => action.ToString()
            };
        }

        private static string GetFlightName(AirFlight flight, Squadron squadron)
        {
            return string.IsNullOrWhiteSpace(squadron?.Name)
                ? $"FLT {ShortId(flight.FlightId)}"
                : squadron.Name;
        }

        private static string FormatTile(Vector3Int tileId)
        {
            return $"{tileId.x},{tileId.y},{tileId.z}";
        }

        private readonly struct AirCardField
        {
            public readonly string Label;
            public readonly string Value;

            public AirCardField(string label, string value)
            {
                Label = label ?? string.Empty;
                Value = value ?? string.Empty;
            }
        }

        private void UpdateTimeUi()
        {
            if (timeLabel == null || gameManager == null)
                return;

            timeLabel.text = $"{gameManager.GameTime:yyyy-MM-dd HH:mm} | Tiles: {gameManager.CampaignTiles.Count}";
            if (pauseButton != null)
                pauseButton.text = gameManager.IsGamePaused ? "Resume" : "Pause";
            if (nextTurnButton != null)
                nextTurnButton.SetEnabled(gameManager.IsGamePaused);
            if (simulationStateLabel != null)
            {
                simulationStateLabel.text = gameManager.IsGamePaused
                    ? "SIMULATION PAUSED"
                    : "SIMULATION RUNNING";
                simulationStateLabel.EnableInClassList(
                    "simulation-state-label--paused",
                    gameManager.IsGamePaused);
            }
        }

        private void UpdateSelectedTileUi()
        {
            if (selectedTileLabel == null)
                return;

            if (!selectedCell.HasValue || !tilesByCell.TryGetValue(selectedCell.Value, out var selectedTile))
            {
                selectedTileLabel.text = "Select a hex";
                UpdateNeighborsUi();
                UpdateUnitsUi();
                return;
            }

            tileDataById.TryGetValue(selectedTile.Coordinates, out var tileData);
            var landData = tileData as LandTileData;
            var controller = landData == null ? "None" : landData.Controller.ToString();
            var infrastructure = landData == null ? "N/A" : landData.Infrastructure.FunctionalLevel.ToString();
            var buildings = gameManager.buildingSystem.GetBuildingsOnTile(selectedTile.Coordinates);
            var buildingText = buildings.Count == 0
                ? "No buildings"
                : string.Join(", ", buildings.Select(building => $"{building.Type} {building.FunctionalLevel}"));
            var supplyFeatures = GetSupplyFeatureLabel(selectedTile.Coordinates);

            selectedTileLabel.text =
                $"Hex {selectedTile.Coordinates.x}, {selectedTile.Coordinates.y}, {selectedTile.Coordinates.z}\n" +
                $"{selectedTile.Surface} | {selectedTile.Terrain}\n" +
                $"Settlement: {selectedTile.Urbanization} | Forest: {selectedTile.ForestCover}\n" +
                $"Control: {controller}\n" +
                $"Infrastructure: {infrastructure}\n" +
                buildingText +
                (string.IsNullOrWhiteSpace(supplyFeatures) ? string.Empty : $"\n{supplyFeatures}");

            UpdateNeighborsUi();
            UpdateUnitsUi();
        }

        private void UpdateNeighborsUi()
        {
            if (neighborsFoldout == null)
                return;

            var neighborCount = 0;
            if (selectedCell.HasValue && tilesByCell.TryGetValue(selectedCell.Value, out var selectedTile))
                neighborCount = selectedTile.NeighborTileIds?.Count ?? 0;

            neighborsFoldout.text = neighborCount == 0 ? "Neighbors" : $"Neighbors ({neighborCount})";

            if (neighborsFoldout.value)
                UpdateNeighborsList();
        }

        private void UpdateUnitsUi()
        {
            if (unitsFoldout == null)
                return;

            var unitCount = GetSelectedTileDivisions().Count;
            unitsFoldout.text = unitCount == 0 ? "Units" : $"Units ({unitCount})";

            if (unitsFoldout.value)
                UpdateUnitsList();
        }

        private void UpdateNeighborsList()
        {
            if (neighborsList == null || neighborsFoldout == null || !neighborsFoldout.value)
                return;

            neighborsList.Clear();

            if (!selectedCell.HasValue || !tilesByCell.TryGetValue(selectedCell.Value, out var selectedTile))
            {
                neighborsList.Add(CreateNeighborMessage("Select a hex to view neighbors."));
                return;
            }

            var neighborIds = selectedTile.NeighborTileIds ?? new List<Vector3Int>();
            if (neighborIds.Count == 0)
            {
                neighborsList.Add(CreateNeighborMessage("No neighbors."));
                return;
            }

            var riverNeighbors = new HashSet<Vector3Int>(selectedTile.RiverNeighborTileIds ?? new List<Vector3Int>());

            foreach (var neighborId in neighborIds)
            {
                if (!tilesById.TryGetValue(neighborId, out var neighbor))
                {
                    neighborsList.Add(CreateNeighborMessage($"Unknown tile {neighborId}"));
                    continue;
                }

                var riverSuffix = riverNeighbors.Contains(neighborId) ? " | River" : string.Empty;
                var coords = neighbor.Coordinates;
                var summary =
                    $"({coords.x}, {coords.y}, {coords.z}) {neighbor.Surface} | {neighbor.Terrain}{riverSuffix}";
                neighborsList.Add(CreateNeighborSelectButton(neighbor, summary));
            }
        }

        private void UpdateUnitsList()
        {
            if (unitsList == null || unitsFoldout == null || !unitsFoldout.value)
                return;

            unitsList.Clear();

            var divisions = GetSelectedTileDivisions();
            if (divisions.Count == 0)
            {
                unitsList.Add(CreateNeighborMessage("No units on this hex."));
                return;
            }

            foreach (var division in divisions.OrderBy(division => division.Name))
                unitsList.Add(CreateUnitCard(division));
        }

        private List<Division> GetSelectedTileDivisions()
        {
            if (!selectedCell.HasValue || !tilesByCell.TryGetValue(selectedCell.Value, out var selectedTile))
                return new List<Division>();

            return gameManager?.divisionSystem?.GetDivisionsOnTile(selectedTile.Coordinates) ?? new List<Division>();
        }

        private VisualElement CreateUnitCard(Division division)
        {
            var card = new VisualElement();
            card.AddToClassList("campaign-hud-unit-card");
            card.style.borderLeftColor = GetControlColor(GetDivisionAlliance(division));

            var name = new Label(string.IsNullOrWhiteSpace(division.Name) ? "Unnamed Division" : division.Name);
            name.AddToClassList("campaign-hud-unit-name");
            ApplyRuntimeFont(name);

            var organizationPercent = GetDivisionStatPercent(division.Organization, division.MaxOrganization);
            var strengthPercent = GetDivisionStatPercent(division.Strength, division.MaxStrength);
            var supplyPercent = GetDivisionSupplyStorePercent(division);
            var stats = new Label(
                $"Org {organizationPercent}% | Strength {strengthPercent}% | Supply {supplyPercent}% | Speed {division.Speed:0.#}");
            stats.AddToClassList("campaign-hud-unit-stat");
            ApplyRuntimeFont(stats);

            card.Add(name);
            card.Add(stats);
            return card;
        }

        private static int GetDivisionStatPercent(float currentValue, int maxValue)
        {
            return maxValue <= 0
                ? 0
                : Mathf.Clamp(Mathf.RoundToInt((currentValue / maxValue) * 100f), 0, 100);
        }

        private static int GetDivisionSupplyStorePercent(Division division)
        {
            if (division == null || division.MaxSupplyStore <= 0f)
                return 100;

            return Mathf.Clamp(Mathf.RoundToInt((division.SupplyStore / division.MaxSupplyStore) * 100f), 0, 100);
        }

        private Label CreateNeighborMessage(string message)
        {
            var label = new Label(message);
            label.AddToClassList("campaign-hud-neighbor-label");
            ApplyRuntimeFont(label);
            return label;
        }

        private Button CreateNeighborSelectButton(CampaignTile neighbor, string summary)
        {
            var button = new Button(() => SelectTile(neighbor)) { text = summary };
            button.AddToClassList("campaign-hud-neighbor-item");
            button.pickingMode = PickingMode.Position;
            ApplyRuntimeFont(button);
            return button;
        }

        private void SelectTile(CampaignTile tile)
        {
            var cell = GetCell(tile.Coordinates);
            if (!tilesByCell.ContainsKey(cell))
                return;

            selectedCell = cell;
            UpdateSelectedTileUi();
        }

        private void CreateUnitCounters()
        {
            if (unitCounterRoot == null || gameManager?.divisionSystem == null)
                return;

            foreach (var group in gameManager.divisionSystem.Divisions
                         .GroupBy(division => division.TileId))
            {
                var cell = GetCell(group.Key);
                if (!hexCentersByCell.TryGetValue(cell, out var hexCenter))
                    continue;

                var divisions = group.ToList();
                var firstDivision = divisions[0];
                var alliance = GetDivisionAlliance(firstDivision);
                CreateUnitCounter(group.Key, hexCenter, divisions.Count, alliance);
            }
        }

        private void CreateUnitCounter(Vector3Int tileId, Vector3 hexCenter, int divisionCount, Alliance alliance)
        {
            var counterObject = new GameObject($"Unit Counter {tileId.x},{tileId.y},{tileId.z}");
            counterObject.transform.SetParent(unitCounterRoot, false);
            counterObject.transform.position = grid.transform.TransformPoint(hexCenter) + new Vector3(0f, -0.1f, -0.2f);

            var renderer = counterObject.AddComponent<SpriteRenderer>();
            renderer.sprite = GetUnitCounterSprite(alliance);
            renderer.sortingOrder = 20;

            var textObject = new GameObject("Counter Label");
            textObject.transform.SetParent(counterObject.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.05f);

            var textMesh = textObject.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = UnitCounterLabelCharacterSize;
            textMesh.fontSize = 28;
            textMesh.color = Color.white;
            textMesh.text = divisionCount.ToString();

            var textRenderer = textObject.GetComponent<MeshRenderer>();
            textRenderer.sortingOrder = 21;
        }

        private Sprite GetUnitCounterSprite(Alliance alliance)
        {
            var key = alliance.ToString();
            if (unitCounterSpritesByKey.TryGetValue(key, out var sprite))
                return sprite;

            var texture = CreateUnitCounterTexture(alliance);
            sprite = Sprite.Create(
                texture,
                new Rect(0, 0, UnitCounterPixelWidth, UnitCounterPixelHeight),
                new Vector2(0.5f, 0.5f),
                UnitCounterPixelWidth / UnitCounterWorldWidth);
            unitCounterSpritesByKey[key] = sprite;
            return sprite;
        }

        private Texture2D CreateUnitCounterTexture(Alliance alliance)
        {
            var pixels = new Color[UnitCounterPixelWidth * UnitCounterPixelHeight];
            var fill = Blend(GetControlColor(alliance), new Color(0.12f, 0.13f, 0.15f), 0.34f);
            var trim = GetControlColor(alliance);
            var dark = new Color(0.04f, 0.05f, 0.06f);

            for (var y = 0; y < UnitCounterPixelHeight; y++)
            {
                for (var x = 0; x < UnitCounterPixelWidth; x++)
                {
                    var border = x == 0 || y == 0 || x == UnitCounterPixelWidth - 1 || y == UnitCounterPixelHeight - 1;
                    var header = y >= UnitCounterPixelHeight - 4;
                    pixels[y * UnitCounterPixelWidth + x] = border ? dark : header ? trim : fill;
                }
            }

            var texture = new Texture2D(UnitCounterPixelWidth, UnitCounterPixelHeight);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void CreateMovementArrows()
        {
            if (movementArrowRoot == null || gameManager?.divisionSystem == null)
                return;

            foreach (var command in GetMovementArrowCommands())
                CreateMovementArrow(command);
        }

        private void CreateAirOverlays()
        {
            if (airOverlayRoot == null || gameManager == null)
                return;

            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var commander = gameManager.GetAllianceAirTaskingCommander(alliance);
                if (commander == null)
                    continue;

                foreach (var package in commander.Packages ?? Array.Empty<AirPackage>())
                {
                    foreach (var flight in package?.Flights ?? new List<AirFlight>())
                    {
                        if (flight == null || !flight.IsAirborne || !flight.HasPosition)
                            continue;

                        CreateAirRoute(flight, alliance);
                        CreateAirMarker(flight, alliance);
                    }
                }
            }
        }

        private void CreateAirInspection()
        {
            if (inspectedFlightId == Guid.Empty
                || airInspectionRoot == null
                || !TryFindFlight(inspectedFlightId, out var flight, out var package, out var commander))
                return;

            var alliance = package?.Alliance ?? commander?.Alliance ?? Alliance.Neutral;
            var allianceColor = GetAirAllianceColor(alliance);
            var route = (flight.Route ?? new List<AirWaypoint>())
                .Where(waypoint => waypoint != null)
                .ToList();

            if (route.Count >= 2)
            {
                var plannedColor = Color.Lerp(allianceColor, Color.white, 0.25f);
                plannedColor.a = 0.42f;
                CreateInspectionPolyline(
                    "Full Planned Route",
                    route.Select(waypoint => AirPositionToMapPosition(waypoint.PositionFeet)).ToList(),
                    plannedColor,
                    0.045f,
                    34);
            }

            var remainingPoints = new List<Vector3>();
            if (flight.HasPosition)
                remainingPoints.Add(AirPositionToMapPosition(flight.PositionFeet));
            remainingPoints.AddRange(route
                .Skip(Mathf.Clamp(flight.CurrentWaypointIndex, 0, route.Count))
                .Select(waypoint => AirPositionToMapPosition(waypoint.PositionFeet)));
            if (remainingPoints.Count >= 2)
            {
                var activeColor = Color.Lerp(allianceColor, Color.white, 0.16f);
                CreateInspectionPolyline(
                    "Active Route",
                    remainingPoints,
                    activeColor,
                    0.085f,
                    36);
            }

            for (var index = 0; index < route.Count; index++)
                CreateInspectionWaypoint(route[index], index, flight.CurrentWaypointIndex, allianceColor);

            var focusPosition = flight.HasPosition
                ? AirPositionToMapPosition(flight.PositionFeet)
                : route.Count > 0
                    ? AirPositionToMapPosition(route[0].PositionFeet)
                    : Vector3.zero;
            CreateInspectionCircle(
                "Selected Flight",
                focusPosition,
                0.24f,
                new Color(1f, 0.83f, 0.20f),
                0.055f,
                42);
        }

        private void CreateInspectionPolyline(
            string objectName,
            IReadOnlyList<Vector3> points,
            Color color,
            float width,
            int sortingOrder)
        {
            if (points == null || points.Count < 2)
                return;

            var lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(airInspectionRoot, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = points.Count;
            line.SetPositions(points
                .Select(point => point + new Vector3(0f, 0f, -0.42f))
                .ToArray());
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.material = GetMovementArrowMaterial();
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = sortingOrder;
        }

        private void CreateInspectionWaypoint(
            AirWaypoint waypoint,
            int index,
            int currentWaypointIndex,
            Color allianceColor)
        {
            var position = AirPositionToMapPosition(waypoint.PositionFeet);
            var isPast = index < currentWaypointIndex;
            var isCurrent = index == currentWaypointIndex;
            var color = isCurrent
                ? new Color(1f, 0.83f, 0.20f)
                : isPast
                    ? new Color(0.48f, 0.50f, 0.53f)
                    : allianceColor;
            var radius = isCurrent ? 0.15f : 0.105f;
            CreateInspectionCircle(
                $"Waypoint {index + 1}",
                position,
                radius,
                color,
                isCurrent ? 0.045f : 0.035f,
                isCurrent ? 41 : 39);

            var labelObject = new GameObject($"Waypoint {index + 1} Label");
            labelObject.transform.SetParent(airInspectionRoot, false);
            labelObject.transform.localPosition = position + new Vector3(0.15f, 0.10f, -0.48f);
            var text = labelObject.AddComponent<TextMesh>();
            text.anchor = TextAnchor.LowerLeft;
            text.alignment = TextAlignment.Left;
            text.characterSize = 0.014f;
            text.fontSize = 22;
            text.color = isPast ? new Color(0.70f, 0.70f, 0.70f) : Color.white;
            text.text =
                $"WP {index + 1}  {GetWaypointLabel(waypoint.Action)}\n" +
                $"{waypoint.PlannedArrivalTime:MM-dd HH:mm}  •  {waypoint.PositionFeet.y / 1000f:0.#}k ft";
            labelObject.GetComponent<MeshRenderer>().sortingOrder = 43;
        }

        private void CreateInspectionCircle(
            string objectName,
            Vector3 center,
            float radius,
            Color color,
            float width,
            int sortingOrder)
        {
            const int segmentCount = 18;
            var markerObject = new GameObject(objectName);
            markerObject.transform.SetParent(airInspectionRoot, false);
            markerObject.transform.localPosition = center;
            var line = markerObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = segmentCount;
            var points = new Vector3[segmentCount];
            for (var index = 0; index < segmentCount; index++)
            {
                var angle = index / (float)segmentCount * Mathf.PI * 2f;
                points[index] = new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    -0.46f);
            }
            line.SetPositions(points);
            line.startWidth = width;
            line.endWidth = width;
            line.numCornerVertices = 2;
            line.material = GetMovementArrowMaterial();
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = sortingOrder;
        }

        private void FrameAirInspection(AirFlight flight)
        {
            if (sceneCamera == null || flight == null)
                return;

            var points = (flight.Route ?? new List<AirWaypoint>())
                .Where(waypoint => waypoint != null)
                .Select(waypoint => AirPositionToMapPosition(waypoint.PositionFeet))
                .ToList();
            if (flight.HasPosition)
                points.Add(AirPositionToMapPosition(flight.PositionFeet));
            if (points.Count == 0)
                return;

            var minX = points.Min(point => point.x);
            var maxX = points.Max(point => point.x);
            var minY = points.Min(point => point.y);
            var maxY = points.Max(point => point.y);
            var center = new Vector3(
                (minX + maxX) * 0.5f,
                (minY + maxY) * 0.5f,
                sceneCamera.transform.position.z);
            sceneCamera.transform.position = center;

            var verticalSize = (maxY - minY) * 0.5f + 0.9f;
            var horizontalSize = (maxX - minX) * 0.5f / Math.Max(0.1f, sceneCamera.aspect) + 0.9f;
            sceneCamera.orthographicSize = Mathf.Clamp(
                Mathf.Max(verticalSize, horizontalSize),
                2f,
                50f);
        }

        private void CreateAirRoute(AirFlight flight, Alliance alliance)
        {
            var routePoints = new List<Vector3> { AirPositionToMapPosition(flight.PositionFeet) };
            if (flight.Route != null)
            {
                routePoints.AddRange(flight.Route
                    .Skip(Mathf.Clamp(flight.CurrentWaypointIndex, 0, flight.Route.Count))
                    .Where(waypoint => waypoint != null)
                    .Select(waypoint => AirPositionToMapPosition(waypoint.PositionFeet)));
            }

            var distinctPoints = routePoints
                .Where((point, index) => index == 0 || Vector3.Distance(point, routePoints[index - 1]) > 0.01f)
                .ToList();
            if (distinctPoints.Count < 2)
                return;

            var routeObject = new GameObject($"Air Route {ShortId(flight.FlightId)}");
            routeObject.transform.SetParent(airOverlayRoot, false);
            var lineRenderer = routeObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.positionCount = distinctPoints.Count;
            lineRenderer.SetPositions(distinctPoints
                .Select(point => point + new Vector3(0f, 0f, -0.31f))
                .ToArray());
            lineRenderer.startWidth = AirRouteLineWidth;
            lineRenderer.endWidth = AirRouteLineWidth;
            lineRenderer.numCapVertices = 2;
            lineRenderer.numCornerVertices = 2;
            lineRenderer.material = GetMovementArrowMaterial();
            var color = GetAirAllianceColor(alliance);
            color.a = 0.72f;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            lineRenderer.sortingOrder = 24;
        }

        private void CreateAirMarker(AirFlight flight, Alliance alliance)
        {
            var markerObject = new GameObject($"Air Flight {ShortId(flight.FlightId)}");
            markerObject.transform.SetParent(airOverlayRoot, false);
            markerObject.transform.localPosition = AirPositionToMapPosition(flight.PositionFeet);

            var markerLine = markerObject.AddComponent<LineRenderer>();
            markerLine.useWorldSpace = false;
            markerLine.loop = true;
            markerLine.positionCount = 4;
            markerLine.SetPositions(new[]
            {
                new Vector3(0f, AirMarkerRadius, -0.34f),
                new Vector3(AirMarkerRadius, 0f, -0.34f),
                new Vector3(0f, -AirMarkerRadius, -0.34f),
                new Vector3(-AirMarkerRadius, 0f, -0.34f)
            });
            markerLine.startWidth = 0.055f;
            markerLine.endWidth = 0.055f;
            markerLine.numCornerVertices = 2;
            markerLine.material = GetMovementArrowMaterial();
            markerLine.startColor = GetAirAllianceColor(alliance);
            markerLine.endColor = GetAirAllianceColor(alliance);
            markerLine.sortingOrder = 27;

            var squadron = gameManager.squadronSystem?.Squadrons?
                .FirstOrDefault(candidate => candidate.SquadronId == flight.SquadronId);
            var labelObject = new GameObject("Flight Label");
            labelObject.transform.SetParent(markerObject.transform, false);
            labelObject.transform.localPosition = new Vector3(0.17f, 0.11f, -0.36f);
            var text = labelObject.AddComponent<TextMesh>();
            text.anchor = TextAnchor.LowerLeft;
            text.alignment = TextAlignment.Left;
            text.characterSize = 0.018f;
            text.fontSize = 22;
            text.color = Color.white;
            text.text =
                $"{GetFlightName(flight, squadron)} ×{flight.AircraftIds?.Count ?? 0}\n" +
                $"{GetMissionLabel(flight.MissionType)} • {flight.PositionFeet.y / 1000f:0.#}k ft";
            labelObject.GetComponent<MeshRenderer>().sortingOrder = 28;
        }

        private Vector3 AirPositionToMapPosition(Vector3 positionFeet)
        {
            var spacingFeet = Math.Max(
                0.001f,
                (gameManager.SimulationSettings?.TileDistanceKM ?? 1f)
                * AirspaceGeometry.FeetPerKilometer);
            return new Vector3(
                positionFeet.x / spacingFeet * (HexHorizontalSpacing / 0.8660254f),
                positionFeet.z / spacingFeet * HexHeight,
                0f);
        }

        private static Color GetAirAllianceColor(Alliance alliance)
        {
            return alliance switch
            {
                Alliance.Bluefor => new Color(0.25f, 0.66f, 1f),
                Alliance.Redfor => new Color(1f, 0.32f, 0.28f),
                _ => new Color(0.82f, 0.82f, 0.82f)
            };
        }

        private IEnumerable<MovementArrowCommand> GetMovementArrowCommands()
        {
            var commands = new List<MovementArrowCommand>();
            foreach (var division in gameManager.divisionSystem.Divisions)
            {
                if (division.CurrentOrder is not MoveGroundOrder moveOrder)
                    continue;

                var originCell = GetCell(division.TileId);
                var destinationCell = GetCell(moveOrder.CurrentDestinationTileId);
                if (!hexCentersByCell.ContainsKey(originCell) || !hexCentersByCell.ContainsKey(destinationCell))
                    continue;

                if (division.TileId == moveOrder.CurrentDestinationTileId)
                    continue;

                commands.Add(new MovementArrowCommand(
                    division.TileId,
                    moveOrder.CurrentDestinationTileId,
                    GetMovementArrowColor(division, moveOrder)));
            }

            return commands
                .GroupBy(command => new MovementArrowKey(command.FromTileId, command.ToTileId, command.Color))
                .Select(group => group.First());
        }

        private Color GetMovementArrowColor(Division division, MoveGroundOrder moveOrder)
        {
            if (moveOrder.IsRetreat)
                return new Color(0.58f, 0.60f, 0.62f);

            if (DoesMovementTriggerCombat(division, moveOrder.CurrentDestinationTileId))
                return new Color(0.86f, 0.18f, 0.15f);

            return new Color(0.20f, 0.48f, 0.94f);
        }

        private bool DoesMovementTriggerCombat(Division division, Vector3Int destinationTileId)
        {
            if (!GroundSystemUtility.TryGetDivisionAlliance(gameManager, division, out var divisionAlliance))
                return false;

            if (tileDataById.TryGetValue(destinationTileId, out var tileData)
                && tileData is LandTileData landData
                && GroundSystemUtility.AreHostile(divisionAlliance, landData.Controller))
                return true;

            return gameManager.divisionSystem.GetDivisionsOnTile(destinationTileId)
                .Any(candidate => candidate != null
                                  && !GroundSystemUtility.IsRetreating(candidate)
                                  && GroundSystemUtility.TryGetDivisionAlliance(gameManager, candidate, out var candidateAlliance)
                                  && GroundSystemUtility.AreHostile(divisionAlliance, candidateAlliance));
        }

        private void CreateMovementArrow(MovementArrowCommand command)
        {
            var fromCell = GetCell(command.FromTileId);
            var toCell = GetCell(command.ToTileId);
            var fromCenter = hexCentersByCell[fromCell];
            var toCenter = hexCentersByCell[toCell];
            var delta = toCenter - fromCenter;
            if (delta.sqrMagnitude <= Mathf.Epsilon)
                return;

            var direction = delta.normalized;
            var perpendicular = new Vector3(-direction.y, direction.x, 0f);
            var start = fromCenter + direction * MovementArrowEndTrim + perpendicular * MovementArrowRouteOffset;
            var end = toCenter - direction * MovementArrowEndTrim + perpendicular * MovementArrowRouteOffset;
            if ((end - start).sqrMagnitude <= 0.01f)
                return;

            var arrowObject = new GameObject(
                $"Movement Arrow {command.FromTileId.x},{command.FromTileId.y},{command.FromTileId.z} to {command.ToTileId.x},{command.ToTileId.y},{command.ToTileId.z}");
            arrowObject.transform.SetParent(movementArrowRoot, false);

            var lineRenderer = arrowObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, start + new Vector3(0f, 0f, -0.26f));
            lineRenderer.SetPosition(1, end + new Vector3(0f, 0f, -0.26f));
            lineRenderer.startWidth = MovementArrowWidth;
            lineRenderer.endWidth = MovementArrowWidth;
            lineRenderer.numCapVertices = 2;
            lineRenderer.material = GetMovementArrowMaterial();
            lineRenderer.startColor = command.Color;
            lineRenderer.endColor = command.Color;
            lineRenderer.sortingOrder = 18;

            var headObject = new GameObject("Movement Arrow Head");
            headObject.transform.SetParent(arrowObject.transform, false);
            headObject.transform.localPosition = end + new Vector3(0f, 0f, -0.27f);
            headObject.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);

            var headRenderer = headObject.AddComponent<SpriteRenderer>();
            headRenderer.sprite = GetMovementArrowHeadSprite();
            headRenderer.color = command.Color;
            headRenderer.sortingOrder = 19;
        }

        private void CreateRailwayLines()
        {
            if (railwayRoot == null || gameManager?.buildingSystem == null)
                return;

            foreach (var campaignTile in gameManager.CampaignTiles.Where(tile => tile.Surface == TileSurface.Land))
            {
                if (!TileHasRailroad(campaignTile.Coordinates))
                    continue;

                var fromCell = GetCell(campaignTile.Coordinates);
                if (!hexCentersByCell.TryGetValue(fromCell, out var fromCenter))
                    continue;

                for (var sideIndex = 0; sideIndex < TerritoryBorderNeighborOffsets.Length; sideIndex++)
                {
                    var neighborId = campaignTile.Coordinates + TerritoryBorderNeighborOffsets[sideIndex];
                    if (!ShouldDrawRailLink(campaignTile.Coordinates, neighborId) || !TileHasRailroad(neighborId))
                        continue;

                    var toCell = GetCell(neighborId);
                    if (!hexCentersByCell.TryGetValue(toCell, out var toCenter))
                        continue;

                    CreateRailwayLine(fromCenter, toCenter, campaignTile.Coordinates, neighborId);
                }
            }
        }

        private void CreateRailwayLine(Vector3 fromCenter, Vector3 toCenter, Vector3Int fromTileId, Vector3Int toTileId)
        {
            var lineObject = new GameObject(
                $"Railway {fromTileId.x},{fromTileId.y},{fromTileId.z} to {toTileId.x},{toTileId.y},{toTileId.z}");
            lineObject.transform.SetParent(railwayRoot, false);

            var lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, fromCenter + new Vector3(0f, 0f, -0.22f));
            lineRenderer.SetPosition(1, toCenter + new Vector3(0f, 0f, -0.22f));
            lineRenderer.startWidth = RailwayLineWidth;
            lineRenderer.endWidth = RailwayLineWidth;
            lineRenderer.numCapVertices = 2;
            lineRenderer.material = GetMovementArrowMaterial();
            lineRenderer.startColor = RailwayLineColor;
            lineRenderer.endColor = RailwayLineColor;
            lineRenderer.sortingOrder = 12;
        }

        private bool TileHasRailroad(Vector3Int tileId)
        {
            return gameManager?.buildingSystem != null
                   && gameManager.buildingSystem
                       .GetBuildingsOnTile(tileId, BuildingType.Railroad)
                       .Any(building => building.FunctionalLevel > 0);
        }

        private static bool ShouldDrawRailLink(Vector3Int fromTileId, Vector3Int toTileId)
        {
            if (fromTileId.x != toTileId.x)
                return fromTileId.x < toTileId.x;

            if (fromTileId.y != toTileId.y)
                return fromTileId.y < toTileId.y;

            return fromTileId.z < toTileId.z;
        }

        private Material GetMovementArrowMaterial()
        {
            if (movementArrowMaterial != null)
                return movementArrowMaterial;

            movementArrowMaterial = new Material(Shader.Find("Sprites/Default"));
            return movementArrowMaterial;
        }

        private Sprite GetMovementArrowHeadSprite()
        {
            if (movementArrowHeadSprite != null)
                return movementArrowHeadSprite;

            var texture = CreateMovementArrowHeadTexture();
            movementArrowHeadSprite = Sprite.Create(
                texture,
                new Rect(0, 0, MovementArrowHeadPixelSize, MovementArrowHeadPixelSize),
                new Vector2(0.5f, 1f),
                MovementArrowHeadPixelSize / MovementArrowHeadWorldWidth);
            return movementArrowHeadSprite;
        }

        private static Texture2D CreateMovementArrowHeadTexture()
        {
            var pixels = new Color[MovementArrowHeadPixelSize * MovementArrowHeadPixelSize];
            var centerX = MovementArrowHeadPixelSize / 2;

            for (var y = 0; y < MovementArrowHeadPixelSize; y++)
            {
                for (var x = 0; x < MovementArrowHeadPixelSize; x++)
                {
                    var distFromTip = (MovementArrowHeadPixelSize - 1) - y;
                    var halfWidth = (distFromTip / (float)(MovementArrowHeadPixelSize - 1)) * (centerX - 1);
                    var filled = Mathf.Abs(x - centerX) <= halfWidth;
                    pixels[y * MovementArrowHeadPixelSize + x] = filled ? Color.white : Color.clear;
                }
            }

            var texture = new Texture2D(MovementArrowHeadPixelSize, MovementArrowHeadPixelSize);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void CreateCombatBubbles()
        {
            if (combatBubbleRoot == null || gameManager?.divisionSystem == null)
                return;

            foreach (var combat in gameManager.GetActiveGroundCombats())
            {
                var defenderCell = GetCell(combat.DefendingTileId);
                if (!hexCentersByCell.TryGetValue(defenderCell, out var defenderCenter))
                    continue;

                var attackerCenter = GetAverageAttackerCenter(combat);
                var bubbleCenter = Vector3.Lerp(attackerCenter, defenderCenter, 0.5f) + new Vector3(0f, 0.18f, 0f);
                var score = CalculateAttackerCombatScore(combat);
                CreateCombatBubble(combat.DefendingTileId, bubbleCenter, score);
            }
        }

        private Vector3 GetAverageAttackerCenter(GroundCombat combat)
        {
            var centers = (combat.AttackerDivisionIds ?? new List<Guid>())
                .Select(divisionId => gameManager.divisionSystem.TryGetDivision(divisionId, out var division)
                    ? division
                    : null)
                .Select(division => GetCell(division.TileId))
                .Where(cell => hexCentersByCell.ContainsKey(cell))
                .Select(cell => hexCentersByCell[cell])
                .ToList();

            if (centers.Count == 0)
            {
                var defenderCell = GetCell(combat.DefendingTileId);
                return hexCentersByCell.TryGetValue(defenderCell, out var defenderCenter)
                    ? defenderCenter + new Vector3(-HexHorizontalSpacing, 0f, 0f)
                    : Vector3.zero;
            }

            return centers.Aggregate(Vector3.zero, (sum, center) => sum + center) / centers.Count;
        }

        private int CalculateAttackerCombatScore(GroundCombat combat)
        {
            var attackerPower = CalculateSideCombatPower(combat.AttackerDivisionIds);
            var defenderPower = CalculateSideCombatPower(combat.DefenderDivisionIds);
            var totalPower = attackerPower + defenderPower;

            if (attackerPower <= 0f)
                return 0;

            if (defenderPower <= 0f)
                return 100;

            return Mathf.Clamp(Mathf.RoundToInt((attackerPower / totalPower) * 100f), 0, 100);
        }

        private float CalculateSideCombatPower(IEnumerable<Guid> divisionIds)
        {
            var total = 0f;
            foreach (var divisionId in divisionIds ?? Enumerable.Empty<Guid>())
            {
                if (!gameManager.divisionSystem.TryGetDivision(divisionId, out var division) || division == null)
                    continue;

                var strengthPercent = division.MaxStrength <= 0
                    ? 0f
                    : Mathf.Clamp01(division.Strength / division.MaxStrength);
                var organizationPercent = division.MaxOrganization <= 0
                    ? 0f
                    : Mathf.Clamp01(division.Organization / division.MaxOrganization);

                total += Mathf.Max(0f, division.SoftAttack + division.HardAttack)
                         * Mathf.Lerp(strengthPercent, organizationPercent, 0.55f);
            }

            return total;
        }

        private void CreateCombatBubble(Vector3Int defendingTileId, Vector3 hexCenter, int score)
        {
            var bubbleObject = new GameObject($"Combat Bubble {defendingTileId.x},{defendingTileId.y},{defendingTileId.z}");
            bubbleObject.transform.SetParent(combatBubbleRoot, false);
            bubbleObject.transform.position = grid.transform.TransformPoint(hexCenter) + new Vector3(0f, 0f, -0.35f);

            var renderer = bubbleObject.AddComponent<SpriteRenderer>();
            renderer.sprite = GetCombatBubbleSprite(score);
            renderer.sortingOrder = 30;

            var textObject = new GameObject("Combat Bubble Label");
            textObject.transform.SetParent(bubbleObject.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.05f);

            var textMesh = textObject.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = CombatBubbleLabelCharacterSize;
            textMesh.fontSize = 28;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.color = Color.white;
            textMesh.text = score.ToString();

            var textRenderer = textObject.GetComponent<MeshRenderer>();
            textRenderer.sortingOrder = 31;
        }

        private Sprite GetCombatBubbleSprite(int score)
        {
            score = Mathf.Clamp(score, 0, 100);
            if (combatBubbleSpritesByScore.TryGetValue(score, out var sprite))
                return sprite;

            var texture = CreateCombatBubbleTexture(score);
            sprite = Sprite.Create(
                texture,
                new Rect(0, 0, CombatBubblePixelSize, CombatBubblePixelSize),
                new Vector2(0.5f, 0.5f),
                CombatBubblePixelSize / CombatBubbleWorldWidth);
            combatBubbleSpritesByScore[score] = sprite;
            return sprite;
        }

        private static Texture2D CreateCombatBubbleTexture(int score)
        {
            var pixels = new Color[CombatBubblePixelSize * CombatBubblePixelSize];
            var center = (CombatBubblePixelSize - 1) * 0.5f;
            var radius = center - 1f;
            var fill = GetCombatScoreColor(score);
            var dark = new Color(0.04f, 0.04f, 0.05f);
            var highlight = new Color(1f, 1f, 1f, 0.35f);

            for (var y = 0; y < CombatBubblePixelSize; y++)
            {
                for (var x = 0; x < CombatBubblePixelSize; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    var index = y * CombatBubblePixelSize + x;
                    if (distance > radius)
                    {
                        pixels[index] = Color.clear;
                        continue;
                    }

                    if (distance > radius - 2f)
                    {
                        pixels[index] = dark;
                        continue;
                    }

                    var shade = y > center ? -0.08f : 0.05f;
                    pixels[index] = Blend(AdjustColor(fill, shade, 0f), dark, 0.12f);
                    if (distance < radius * 0.45f && y < center)
                        pixels[index] = Blend(pixels[index], highlight, 0.35f);
                }
            }

            var texture = new Texture2D(CombatBubblePixelSize, CombatBubblePixelSize);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Color GetCombatScoreColor(int score)
        {
            var t = Mathf.Clamp01(score / 100f);
            var losing = new Color(0.70f, 0.18f, 0.16f);
            var even = new Color(0.84f, 0.70f, 0.22f);
            var winning = new Color(0.16f, 0.56f, 0.28f);
            return t < 0.5f
                ? Color.Lerp(losing, even, t * 2f)
                : Color.Lerp(even, winning, (t - 0.5f) * 2f);
        }

        private Alliance GetDivisionAlliance(Division division)
        {
            if (division == null || gameManager?.CampaignTemplate.CountryAllianceAssignments == null)
                return Alliance.Neutral;

            var assignment = gameManager.CampaignTemplate.CountryAllianceAssignments
                .FirstOrDefault(candidate => candidate != null && candidate.CountryId == division.CountryId);
            return assignment?.Alliance ?? Alliance.Neutral;
        }

        private void HandleTileSelection()
        {
            var mouse = Mouse.current;
            if (sceneCamera == null
                || tilemap == null
                || mouse == null
                || IsPointerOverCampaignUi
                || !mouse.leftButton.wasPressedThisFrame)
                return;

            var worldPosition = sceneCamera.ScreenToWorldPoint(mouse.position.ReadValue());
            worldPosition.z = 0f;
            var cell = GetCellAtWorldPosition(worldPosition);

            if (!cell.HasValue)
                return;

            selectedCell = cell.Value;
            UpdateSelectedTileUi();
        }

        private void TogglePause()
        {
            if (gameManager == null)
                return;

            if (gameManager.IsGamePaused)
                gameManager.ResumeCampaign();
            else
                gameManager.PauseCampaign();

            UpdateTimeUi();
        }

        private void AdvanceOneGameTurn()
        {
            if (gameManager == null || !gameManager.IsGamePaused)
                return;

            gameManager.AdvanceOneGameTurn();
            UpdateTimeUi();
        }

        private UnityEngine.Tilemaps.Tile GetRenderTile(CampaignTile campaignTile)
        {
            var key = GetTileVisualKey(campaignTile);
            if (renderTilesByKey.TryGetValue(key, out var renderTile))
                return renderTile;

            renderTile = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
            renderTile.sprite = GetTileSprite(campaignTile, key);
            renderTile.color = Color.white;
            renderTilesByKey[key] = renderTile;
            return renderTile;
        }

        private string GetTileVisualKey(CampaignTile campaignTile)
        {
            tileDataById.TryGetValue(campaignTile.Coordinates, out var tileData);
            var controller = tileData is LandTileData landData ? landData.Controller : Alliance.Neutral;

            if (campaignTile.Surface == TileSurface.Ocean)
                return $"Ocean:{campaignTile.Terrain}";

            var borderMask = GetTerritoryBorderMask(campaignTile, controller);
            var supplyOverlay = GetSupplyOverlayKey(campaignTile.Coordinates);
            return $"Land:{campaignTile.Terrain}:{campaignTile.Urbanization}:{campaignTile.ForestCover}:{controller}:{borderMask}:{supplyOverlay}";
        }

        private Sprite GetTileSprite(CampaignTile campaignTile, string key)
        {
            if (spritesByKey.TryGetValue(key, out var sprite))
                return sprite;

            tileDataById.TryGetValue(campaignTile.Coordinates, out var tileData);
            var controller = tileData is LandTileData landData ? landData.Controller : Alliance.Neutral;
            var borderMask = GetTerritoryBorderMask(campaignTile, controller);

            var texture = CreateTileTexture(campaignTile, controller, borderMask);
            sprite = Sprite.Create(
                texture,
                new Rect(0, 0, TilePixelSize, TilePixelSize),
                new Vector2(0.5f, 0.5f),
                TilePixelSize);
            spritesByKey[key] = sprite;
            return sprite;
        }

        private Texture2D CreateTileTexture(CampaignTile campaignTile, Alliance controller, int borderMask)
        {
            var pixels = new Color[TilePixelSize * TilePixelSize];
            FillTerrainPixels(pixels, campaignTile);

            if (campaignTile.Surface == TileSurface.Land)
            {
                ApplyForestCover(pixels, campaignTile.ForestCover);
                ApplyUrbanization(pixels, campaignTile.Urbanization);
                ApplySupplyInfrastructure(pixels, campaignTile);
                DrawTerritoryBorder(pixels, controller, borderMask);
            }

            ApplyHexMask(pixels);

            var texture = new Texture2D(TilePixelSize, TilePixelSize);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private void FillTerrainPixels(Color[] pixels, CampaignTile campaignTile)
        {
            if (campaignTile.Surface == TileSurface.Ocean)
            {
                FillOceanPixels(pixels, campaignTile.Terrain);
                return;
            }

            var baseColor = GetLandTerrainColor(campaignTile.Terrain);
            for (var y = 0; y < TilePixelSize; y++)
            {
                for (var x = 0; x < TilePixelSize; x++)
                {
                    var color = baseColor;
                    color = ApplyLandTerrainPattern(color, campaignTile.Terrain, x, y);
                    pixels[y * TilePixelSize + x] = color;
                }
            }
        }

        private void FillOceanPixels(Color[] pixels, TileTerrain terrain)
        {
            var baseColor = GetOceanColor(terrain);
            for (var y = 0; y < TilePixelSize; y++)
            {
                for (var x = 0; x < TilePixelSize; x++)
                {
                    var wave = ((x + y * 2) / 4) % 2 == 0 ? 0.04f : -0.04f;
                    var depth = terrain == TileTerrain.DeepOcean && (x + y) % 5 == 0 ? -0.06f : 0f;
                    pixels[y * TilePixelSize + x] = AdjustColor(baseColor, wave + depth, 0f);
                }
            }
        }

        private Color ApplyLandTerrainPattern(Color color, TileTerrain terrain, int x, int y)
        {
            return terrain switch
            {
                TileTerrain.Plains => AdjustColor(color, HashNoise(x, y, 11) * 0.05f, 0f),
                TileTerrain.Hills => AdjustColor(color, Mathf.Sin((x + y * 0.5f) * 0.55f) * 0.08f, 0f),
                TileTerrain.Mountain => Blend(
                    color,
                    new Color(0.28f, 0.30f, 0.32f),
                    IsMountainRock(x, y) ? 0.72f : 0.18f),
                TileTerrain.Desert => AdjustColor(color, HashNoise(x, y, 17) * 0.08f, HashNoise(x, y, 23) * 0.04f),
                TileTerrain.Tundra => Blend(
                    color,
                    Color.white,
                    ((x + y * 3) % 7 == 0 || (x * 2 + y) % 11 == 0) ? 0.35f : 0f),
                TileTerrain.Coast => Blend(
                    color,
                    new Color(0.18f, 0.42f, 0.68f),
                    y < 7 ? Mathf.Clamp01((7 - y) / 7f) * 0.85f : 0f),
                _ => color
            };
        }

        private void ApplyForestCover(Color[] pixels, ForestCover forestCover)
        {
            if (forestCover == ForestCover.None)
                return;

            var treeColor = forestCover == ForestCover.Heavy
                ? new Color(0.10f, 0.28f, 0.12f)
                : new Color(0.16f, 0.36f, 0.18f);
            var density = forestCover == ForestCover.Heavy ? 9 : 16;

            for (var y = 1; y < TilePixelSize - 1; y++)
            {
                for (var x = 1; x < TilePixelSize - 1; x++)
                {
                    if ((x * 7 + y * 13) % density != 0)
                        continue;

                    DrawTree(pixels, x, y, treeColor, forestCover == ForestCover.Heavy ? 2 : 1);
                }
            }

            if (forestCover == ForestCover.Heavy)
            {
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = Blend(pixels[i], new Color(0.12f, 0.30f, 0.14f), 0.18f);
            }
        }

        private void ApplyUrbanization(Color[] pixels, Urbanization urbanization)
        {
            switch (urbanization)
            {
                case Urbanization.Rural:
                    DrawBuilding(pixels, 8, 8, 4, 4, new Color(0.58f, 0.56f, 0.50f));
                    DrawBuilding(pixels, 20, 18, 3, 3, new Color(0.52f, 0.50f, 0.46f));
                    break;
                case Urbanization.Suburban:
                    for (var blockY = 0; blockY < 3; blockY++)
                    {
                        for (var blockX = 0; blockX < 3; blockX++)
                        {
                            if ((blockX + blockY) % 2 == 0)
                                continue;

                            DrawBuilding(
                                pixels,
                                4 + blockX * 9,
                                4 + blockY * 9,
                                5,
                                5,
                                new Color(0.62f, 0.60f, 0.56f));
                        }
                    }
                    break;
                case Urbanization.Urban:
                    for (var y = 2; y < TilePixelSize - 2; y += 4)
                    {
                        for (var x = 2; x < TilePixelSize - 2; x += 4)
                        {
                            if (x % 8 == 0 || y % 8 == 0)
                                continue;

                            var height = 3 + ((x + y) % 5);
                            DrawBuilding(
                                pixels,
                                x,
                                y,
                                3,
                                height,
                                new Color(0.42f + ((x + y) % 3) * 0.04f, 0.40f, 0.38f));
                        }
                    }
                    break;
            }
        }

        private void ApplySupplyInfrastructure(Color[] pixels, CampaignTile campaignTile)
        {
            if (gameManager?.buildingSystem == null)
                return;

            var tileId = campaignTile.Coordinates;
            var hasHub = HasActiveSupplyHub(tileId);
            var capitalAlliance = TryGetSupplyCapitalAlliance(tileId, out var alliance) ? alliance : Alliance.Neutral;

            if (hasHub)
                DrawSupplyHubMarker(pixels);

            if (capitalAlliance != Alliance.Neutral)
                DrawSupplyCapitalMarker(pixels, capitalAlliance);
        }

        private string GetSupplyOverlayKey(Vector3Int tileId)
        {
            var parts = new List<string>();
            if (HasActiveSupplyHub(tileId))
                parts.Add("H");

            if (TryGetSupplyCapitalAlliance(tileId, out var alliance))
                parts.Add($"C:{alliance}");

            return parts.Count == 0 ? "none" : string.Join(",", parts);
        }

        private bool HasActiveSupplyHub(Vector3Int tileId)
        {
            return gameManager?.buildingSystem != null
                   && gameManager.buildingSystem
                       .GetBuildingsOnTile(tileId, BuildingType.SupplyHub)
                       .Any(building => building.FunctionalLevel > 0);
        }

        private bool TryGetSupplyCapitalAlliance(Vector3Int tileId, out Alliance alliance)
        {
            alliance = Alliance.Neutral;
            if (gameManager?.SupplyCapitals == null)
                return false;

            var capital = gameManager.SupplyCapitals
                .FirstOrDefault(candidate => candidate != null && candidate.TileId == tileId);
            if (capital == null || capital.Alliance == Alliance.Neutral)
                return false;

            alliance = capital.Alliance;
            return true;
        }

        private static void DrawSupplyHubMarker(Color[] pixels)
        {
            const int width = 10;
            const int height = 8;
            const int x = 11;
            const int y = 7;

            DrawFilledRect(pixels, x, y, width, height, SupplyHubMarkerBorderColor);
            DrawFilledRect(pixels, x + 1, y + 1, width - 2, height - 2, SupplyHubMarkerColor);
            DrawFilledRect(pixels, x + 3, y + 2, 4, 4, SupplyHubMarkerBorderColor);
        }

        private static void DrawSupplyCapitalMarker(Color[] pixels, Alliance alliance)
        {
            var centerX = TilePixelSize / 2;
            var centerY = TilePixelSize - 8;
            var fillColor = GetControlColor(alliance);
            var borderColor = Color.Lerp(fillColor, Color.white, 0.45f);

            DrawFilledDiamond(pixels, centerX, centerY, 6, borderColor);
            DrawFilledDiamond(pixels, centerX, centerY, 4, fillColor);
        }

        private static void DrawFilledRect(Color[] pixels, int x, int y, int width, int height, Color color)
        {
            for (var py = y; py < y + height && py < TilePixelSize; py++)
            {
                for (var px = x; px < x + width && px < TilePixelSize; px++)
                {
                    if (px < 0 || py < 0)
                        continue;

                    pixels[py * TilePixelSize + px] = color;
                }
            }
        }

        private static void DrawFilledDiamond(Color[] pixels, int centerX, int centerY, int radius, Color color)
        {
            for (var y = centerY - radius; y <= centerY + radius; y++)
            {
                for (var x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (x < 0 || y < 0 || x >= TilePixelSize || y >= TilePixelSize)
                        continue;

                    if (Mathf.Abs(x - centerX) + Mathf.Abs(y - centerY) > radius)
                        continue;

                    pixels[y * TilePixelSize + x] = color;
                }
            }
        }

        private int GetTerritoryBorderMask(CampaignTile campaignTile, Alliance controller)
        {
            var borderMask = 0;
            for (var sideIndex = 0; sideIndex < TerritoryBorderSideCount; sideIndex++)
            {
                var neighborId = campaignTile.Coordinates + TerritoryBorderNeighborOffsets[sideIndex];
                if (IsTerritoryBoundary(neighborId, controller))
                    borderMask |= 1 << sideIndex;
            }

            return borderMask;
        }

        private bool IsTerritoryBoundary(Vector3Int neighborId, Alliance controller)
        {
            if (!tilesById.TryGetValue(neighborId, out var neighborTile))
                return true;

            if (neighborTile.Surface != TileSurface.Land)
                return true;

            return !TryGetTileController(neighborId, out var neighborController) || neighborController != controller;
        }

        private bool TryGetTileController(Vector3Int tileId, out Alliance controller)
        {
            if (tileDataById.TryGetValue(tileId, out var tileData) && tileData is LandTileData landData)
            {
                controller = landData.Controller;
                return true;
            }

            controller = Alliance.Neutral;
            return false;
        }

        private void DrawTerritoryBorder(Color[] pixels, Alliance controller, int borderMask)
        {
            if (borderMask == 0)
                return;

            var borderColor = GetControlColor(controller);
            const int borderWidth = 2;

            for (var y = 0; y < TilePixelSize; y++)
            {
                for (var x = 0; x < TilePixelSize; x++)
                {
                    if (IsHexBorderPixel(x, y, borderWidth) && ShouldDrawTerritoryBorderSide(x, y, borderMask))
                        pixels[y * TilePixelSize + x] = borderColor;
                }
            }
        }

        private static bool ShouldDrawTerritoryBorderSide(int x, int y, int borderMask)
        {
            var sideIndex = GetNearestHexSideIndex(x, y);
            return (borderMask & (1 << sideIndex)) != 0;
        }

        private static int GetNearestHexSideIndex(int x, int y)
        {
            var center = (TilePixelSize - 1) * 0.5f;
            var angle = Mathf.Atan2(y - center, x - center) * Mathf.Rad2Deg;
            var nearestSideIndex = 0;
            var nearestDelta = float.MaxValue;

            for (var sideIndex = 0; sideIndex < TerritoryBorderSideAngles.Length; sideIndex++)
            {
                var delta = Mathf.Abs(Mathf.DeltaAngle(angle, TerritoryBorderSideAngles[sideIndex]));
                if (delta >= nearestDelta)
                    continue;

                nearestSideIndex = sideIndex;
                nearestDelta = delta;
            }

            return nearestSideIndex;
        }

        private static void ApplyHexMask(Color[] pixels)
        {
            for (var y = 0; y < TilePixelSize; y++)
            {
                for (var x = 0; x < TilePixelSize; x++)
                {
                    if (!IsHexPixel(x, y))
                        pixels[y * TilePixelSize + x] = Color.clear;
                }
            }
        }

        private static bool IsHexBorderPixel(int x, int y, int borderWidth)
        {
            if (!IsHexPixel(x, y))
                return false;

            for (var offsetY = -borderWidth; offsetY <= borderWidth; offsetY++)
            {
                for (var offsetX = -borderWidth; offsetX <= borderWidth; offsetX++)
                {
                    if (!IsHexPixel(x + offsetX, y + offsetY))
                        return true;
                }
            }

            return false;
        }

        private static bool IsHexPixel(int x, int y)
        {
            if (x < 0 || y < 0 || x >= TilePixelSize || y >= TilePixelSize)
                return false;

            var halfHeight = (TilePixelSize - 1) * 0.5f;
            var distanceFromCenterY = Mathf.Abs(y - halfHeight);
            var inset = Mathf.RoundToInt(HexFlatInsetPixels * (distanceFromCenterY / halfHeight));
            return x >= inset && x < TilePixelSize - inset;
        }

        private void DrawTree(Color[] pixels, int centerX, int centerY, Color treeColor, int radius)
        {
            for (var y = centerY - radius; y <= centerY + radius; y++)
            {
                for (var x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (x < 0 || y < 0 || x >= TilePixelSize || y >= TilePixelSize)
                        continue;

                    if ((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY) > radius * radius + 1)
                        continue;

                    pixels[y * TilePixelSize + x] = Blend(pixels[y * TilePixelSize + x], treeColor, 0.82f);
                }
            }
        }

        private void DrawBuilding(Color[] pixels, int x, int y, int width, int height, Color buildingColor)
        {
            for (var py = y; py < y + height && py < TilePixelSize; py++)
            {
                for (var px = x; px < x + width && px < TilePixelSize; px++)
                {
                    if (px < 0 || py < 0)
                        continue;

                    pixels[py * TilePixelSize + px] = buildingColor;
                }
            }
        }

        private static bool IsMountainRock(int x, int y)
        {
            return ((x * 3 + y * 5) % 11 < 4) || ((x + y * 2) % 13 == 0);
        }

        private static float HashNoise(int x, int y, int seed)
        {
            var hash = x * 374761393 + y * 668265263 + seed * 1274126177;
            hash = (hash ^ (hash >> 13)) * 1274126177;
            return ((hash ^ (hash >> 16)) & 255) / 255f - 0.5f;
        }

        private static Color GetLandTerrainColor(TileTerrain terrain)
        {
            return terrain switch
            {
                TileTerrain.Mountain => new Color(0.40f, 0.43f, 0.39f),
                TileTerrain.Hills => new Color(0.48f, 0.54f, 0.36f),
                TileTerrain.Coast => new Color(0.54f, 0.66f, 0.43f),
                TileTerrain.Desert => new Color(0.72f, 0.65f, 0.42f),
                TileTerrain.Tundra => new Color(0.62f, 0.70f, 0.70f),
                _ => new Color(0.34f, 0.55f, 0.34f)
            };
        }

        private static Color GetOceanColor(TileTerrain terrain)
        {
            return terrain switch
            {
                TileTerrain.DeepOcean => new Color(0.05f, 0.18f, 0.38f),
                TileTerrain.ShallowOcean => new Color(0.18f, 0.48f, 0.70f),
                _ => new Color(0.12f, 0.38f, 0.62f)
            };
        }

        private static Color GetControlColor(Alliance controller)
        {
            return controller switch
            {
                Alliance.Bluefor => new Color(0.30f, 0.48f, 0.86f),
                Alliance.Redfor => new Color(0.78f, 0.28f, 0.25f),
                _ => new Color(0.72f, 0.68f, 0.46f)
            };
        }

        private static Color AdjustColor(Color color, float brightnessDelta, float saturationDelta)
        {
            return new Color(
                Mathf.Clamp01(color.r + brightnessDelta),
                Mathf.Clamp01(color.g + brightnessDelta + saturationDelta),
                Mathf.Clamp01(color.b + brightnessDelta - saturationDelta));
        }

        private static Color Blend(Color baseColor, Color overlay, float amount)
        {
            amount = Mathf.Clamp01(amount);
            return Color.Lerp(baseColor, overlay, amount);
        }

        private void CreateTileLabel(CampaignTile campaignTile, Vector3 hexCenter)
        {
            var labelObject = new GameObject($"Hex Label {campaignTile.Coordinates.x},{campaignTile.Coordinates.y},{campaignTile.Coordinates.z}");
            labelObject.transform.SetParent(labelRoot, false);
            labelObject.transform.position = grid.transform.TransformPoint(hexCenter) + new Vector3(0f, 0f, -0.1f);

            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = TileLabelCharacterSize;
            textMesh.fontSize = 26;
            textMesh.lineSpacing = 0.75f;
            textMesh.color = Color.white;
            textMesh.text = GetTileLabel(campaignTile);

            var renderer = labelObject.GetComponent<MeshRenderer>();
            renderer.sortingOrder = 10;
        }

        private string GetTileLabel(CampaignTile campaignTile)
        {
            tileDataById.TryGetValue(campaignTile.Coordinates, out var tileData);
            var buildingCount = gameManager.buildingSystem.GetBuildingsOnTile(campaignTile.Coordinates).Count;
            var coords = campaignTile.Coordinates;
            var hexLine = $"Hex {coords.x},{coords.y},{coords.z}";
            var supplyFeatures = GetSupplyFeatureLabel(campaignTile.Coordinates);

            if (campaignTile.Surface == TileSurface.Ocean)
                return $"{GetTerrainLabel(campaignTile.Terrain)}\n{hexLine}";

            var controller = tileData is LandTileData landData
                ? GetControllerLabel(landData.Controller)
                : "Neutral";
            var terrain = GetTerrainLabel(campaignTile.Terrain);
            var settlement = GetSettlementLabel(campaignTile.Urbanization);
            var forest = GetForestLabel(campaignTile.ForestCover);
            var buildings = buildingCount == 0
                ? "No buildings"
                : buildingCount == 1
                    ? "1 building"
                    : $"{buildingCount} buildings";

            var detailLine = string.IsNullOrWhiteSpace(settlement)
                ? forest
                : string.IsNullOrWhiteSpace(forest)
                    ? settlement
                    : $"{settlement} woods";
            if (string.IsNullOrWhiteSpace(detailLine))
                detailLine = "Open land";

            var label = $"{controller} {terrain}\n{detailLine}\n{buildings}";
            if (!string.IsNullOrWhiteSpace(supplyFeatures))
                label += $"\n{supplyFeatures}";

            return $"{label}\n{hexLine}";
        }

        private string GetSupplyFeatureLabel(Vector3Int tileId)
        {
            var features = new List<string>();
            if (TryGetSupplyCapitalAlliance(tileId, out var alliance))
                features.Add($"{GetControllerLabel(alliance)} supply capital");

            if (HasActiveSupplyHub(tileId))
                features.Add("Supply hub");

            if (TileHasRailroad(tileId))
                features.Add("Railway");

            return features.Count == 0 ? string.Empty : string.Join(" | ", features);
        }

        private static string GetControllerLabel(Alliance controller)
        {
            return controller switch
            {
                Alliance.Bluefor => "Blue",
                Alliance.Redfor => "Red",
                _ => "Neutral"
            };
        }

        private static string GetTerrainLabel(TileTerrain terrain)
        {
            return terrain switch
            {
                TileTerrain.DeepOcean => "Deep ocean",
                TileTerrain.ShallowOcean => "Shallow sea",
                TileTerrain.Ocean => "Ocean",
                _ => terrain.ToString()
            };
        }

        private static string GetSettlementLabel(Urbanization urbanization)
        {
            return urbanization switch
            {
                Urbanization.Urban => "City",
                Urbanization.Suburban => "Suburb",
                Urbanization.Rural => "Rural",
                _ => string.Empty
            };
        }

        private static string GetForestLabel(ForestCover forestCover)
        {
            return forestCover switch
            {
                ForestCover.Heavy => "Woods",
                ForestCover.Light => "Woods",
                _ => string.Empty
            };
        }

        private void ClearLabels()
        {
            if (labelRoot == null)
                return;

            for (var i = labelRoot.childCount - 1; i >= 0; i--)
                Destroy(labelRoot.GetChild(i).gameObject);
        }

        private void ClearUnitCounters()
        {
            if (unitCounterRoot == null)
                return;

            for (var i = unitCounterRoot.childCount - 1; i >= 0; i--)
                Destroy(unitCounterRoot.GetChild(i).gameObject);
        }

        private void ClearCombatBubbles()
        {
            if (combatBubbleRoot == null)
                return;

            for (var i = combatBubbleRoot.childCount - 1; i >= 0; i--)
                Destroy(combatBubbleRoot.GetChild(i).gameObject);
        }

        private void ClearMovementArrows()
        {
            if (movementArrowRoot == null)
                return;

            for (var i = movementArrowRoot.childCount - 1; i >= 0; i--)
                Destroy(movementArrowRoot.GetChild(i).gameObject);
        }

        private void ClearRailwayLines()
        {
            if (railwayRoot == null)
                return;

            for (var i = railwayRoot.childCount - 1; i >= 0; i--)
                Destroy(railwayRoot.GetChild(i).gameObject);
        }

        private void ClearAirOverlays()
        {
            if (airOverlayRoot == null)
                return;

            for (var i = airOverlayRoot.childCount - 1; i >= 0; i--)
                Destroy(airOverlayRoot.GetChild(i).gameObject);
        }

        private void ClearAirInspection()
        {
            if (airInspectionRoot == null)
                return;

            for (var i = airInspectionRoot.childCount - 1; i >= 0; i--)
                Destroy(airInspectionRoot.GetChild(i).gameObject);
        }

        private readonly struct MovementArrowKey
        {
            private readonly Vector3Int fromTileId;
            private readonly Vector3Int toTileId;
            private readonly Color color;

            public MovementArrowKey(Vector3Int fromTileId, Vector3Int toTileId, Color color)
            {
                this.fromTileId = fromTileId;
                this.toTileId = toTileId;
                this.color = color;
            }
        }

        private sealed class MovementArrowCommand
        {
            public readonly Vector3Int FromTileId;
            public readonly Vector3Int ToTileId;
            public readonly Color Color;

            public MovementArrowCommand(Vector3Int fromTileId, Vector3Int toTileId, Color color)
            {
                FromTileId = fromTileId;
                ToTileId = toTileId;
                Color = color;
            }
        }

        private void FrameCamera()
        {
            if (sceneCamera == null || gameManager.CampaignTiles.Count == 0)
                return;

            var centers = hexCentersByCell.Values
                .Select(center => grid.transform.TransformPoint(center))
                .ToList();
            var minX = centers.Min(center => center.x) - 0.5f;
            var maxX = centers.Max(center => center.x) + 0.5f;
            var minY = centers.Min(center => center.y) - 0.5f;
            var maxY = centers.Max(center => center.y) + 0.5f;
            var center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, sceneCamera.transform.position.z);

            sceneCamera.orthographic = true;
            sceneCamera.orthographicSize = Mathf.Max(3f, (maxY - minY + 2f) * 0.6f);
            sceneCamera.transform.position = center;
        }

        private static Vector3Int GetCell(Vector3Int coordinates)
        {
            return new Vector3Int(coordinates.x, coordinates.z, 0);
        }

        private static Vector3 GetHexCenter(Vector3Int coordinates)
        {
            var x = coordinates.x * HexHorizontalSpacing;
            var y = (coordinates.z + coordinates.x * 0.5f) * HexHeight;
            return new Vector3(x, y, 0f);
        }

        private Vector3Int? GetCellAtWorldPosition(Vector3 worldPosition)
        {
            foreach (var entry in hexCentersByCell)
            {
                var center = grid.transform.TransformPoint(entry.Value);
                if (IsWorldPointInHex(worldPosition, center))
                    return entry.Key;
            }

            return null;
        }

        private static bool IsWorldPointInHex(Vector3 worldPosition, Vector3 center)
        {
            var localX = Mathf.Abs(worldPosition.x - center.x) / (HexWidth * 0.5f);
            var localY = Mathf.Abs(worldPosition.y - center.y) / (HexHeight * 0.5f);
            return localY <= 1f && localX + localY * 0.5f <= 1f;
        }

        private void SelectFirstTile()
        {
            if (tilesByCell.Count == 0)
            {
                selectedCell = null;
                return;
            }

            selectedCell = hexCentersByCell
                .OrderBy(entry => entry.Value.y)
                .ThenBy(entry => entry.Value.x)
                .Select(entry => entry.Key)
                .First();
            UpdateSelectedTileUi();
        }
    }
}
