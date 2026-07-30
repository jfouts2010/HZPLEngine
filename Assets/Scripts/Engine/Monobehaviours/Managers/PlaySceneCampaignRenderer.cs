using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Engine.Models;
using Engine.Models.Ground;
using Engine.Service;
using Models.Gameplay.Campaign;
using Models.Module;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using Monobehaviours.Singletons;

namespace Engine.Monobehaviours.Managers
{
    [RequireComponent(typeof(GameManager))]
    public class PlaySceneCampaignRenderer : MonoBehaviour
    {
        public static bool IsPointerOverCampaignUi { get; private set; }

        [SerializeField] private GameManager gameManager;
        [SerializeField] private Grid grid;
        [SerializeField] private Tilemap tilemap;
        [SerializeField] private Tilemap airControlTilemap;
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PanelSettings panelSettings;
        [SerializeField] private VisualTreeAsset campaignHudTemplate;

        private const string CampaignHudResourcePath = "UI/PlaySceneCampaignHud";
        private const string PanelSettingsResourcePath = "UI/PlayScenePanelSettings";

        private readonly Dictionary<Vector3Int, RuntimeTile> tilesByCell = new Dictionary<Vector3Int, RuntimeTile>();
        private readonly Dictionary<Vector3Int, Vector3> hexCentersByCell = new Dictionary<Vector3Int, Vector3>();
        private readonly Dictionary<Vector3Int, RuntimeTile> tilesById = new Dictionary<Vector3Int, RuntimeTile>();
        private readonly Dictionary<string, UnityEngine.Tilemaps.Tile> renderTilesByKey = new Dictionary<string, UnityEngine.Tilemaps.Tile>();
        private readonly Dictionary<string, Sprite> spritesByKey = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, Sprite> unitCounterSpritesByKey = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, Sprite> combatBubbleSpritesByScore = new Dictionary<string, Sprite>();
        private UnityEngine.Tilemaps.Tile airControlRenderTile;
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
        private const float BarcapBarrierLineWidth = 0.060f;
        private const float AirIntentLineWidth = 0.052f;
        private const float AirIntentAreaLineWidth = 0.038f;
        private const float AirEngagementLineWidth = 0.060f;
        private const float AirMarkerRadius = 0.12f;
        private const float SamCoverageLineWidth = 0.022f;
        private const float SamIconRadius = 0.16f;
        private static readonly Color RailwayLineColor = new Color(0.38f, 0.32f, 0.24f);
        private static readonly Color SupplyHubMarkerColor = new Color(0.88f, 0.58f, 0.10f);
        private static readonly Color SupplyHubMarkerBorderColor = new Color(0.98f, 0.92f, 0.78f);
        private const float MaximumAirControlOverlayAlpha = 0.75f;
        private static readonly Color BlueAirControlColor = new Color(0.12f, 0.38f, 0.95f, 1f);
        private static readonly Color ContestedAirControlColor = new Color(0.58f, 0.20f, 0.72f, 1f);
        private static readonly Color RedAirControlColor = new Color(0.92f, 0.16f, 0.18f, 1f);
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
        private Transform samOverlayRoot;
        private Transform ordnanceOverlayRoot;
        private Label titleLabel;
        private Label timeLabel;
        private Label selectedTileLabel;
        private Foldout neighborsFoldout;
        private VisualElement neighborsList;
        private Foldout unitsFoldout;
        private VisualElement unitsList;
        private Button pauseButton;
        private Button speedFiveSecondsButton;
        private Button speedFiveMinutesButton;
        private Button nextIncrementButton;
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
        private Button airPassesButton;
        private Label airListTitle;
        private VisualElement airAllianceFilter;
        private Button airBlueFlightsButton;
        private Button airRedFlightsButton;
        private Label airInspectionStatus;
        private VisualElement airRequestsList;
        private VisualElement airPackagesList;
        private VisualElement airFlightsList;
        private VisualElement airPassesList;
        private VisualElement flightDetailBackdrop;
        private Label flightDetailTitle;
        private Label flightDetailSubtitle;
        private Button flightDetailClose;
        private ScrollView flightDetailScroll;
        private VisualElement flightDetailContent;
        private Button groundTabButton;
        private Button turnTabButton;
        private Button diagnosticsTabButton;
        private Button pinPrimaryButton;
        private Label workbenchPageTitle;
        private ScrollView groundOpsContent;
        private Label groundOpsSummary;
        private VisualElement groundCombatsList;
        private ScrollView turnReportContent;
        private Label turnReportSummary;
        private VisualElement turnReportList;
        private VisualElement diagnosticsContent;
        private VisualElement diagnosticsList;
        private TextField diagnosticsSearch;
        private DropdownField diagnosticsSeverity;
        private DropdownField diagnosticsSystem;
        private Button diagnosticsErrorsButton;
        private Button diagnosticsWarningsButton;
        private Button resetLayoutButton;
        private Foldout buildingsFoldout;
        private VisualElement buildingsList;
        private VisualElement airOverviewGrid;
        private Toggle overlayUnitsToggle;
        private Toggle overlayCombatsToggle;
        private Toggle overlayMovementToggle;
        private Toggle overlayRoutesToggle;
        private Toggle overlayBarriersToggle;
        private Toggle overlayTerritoryBoundariesToggle;
        private Toggle overlaySamToggle;
        private Toggle overlayOrdnanceToggle;
        private Toggle overlayRailToggle;
        private Toggle overlayAirControlToggle;
        private VisualElement windowLayer;
        private VisualElement flightDetailPopup;
        private VisualElement flightDetailDragHandle;
        private VisualElement flightDetailResizeHandle;
        private Button flightDetailPin;
        private Button flightDetailFocus;
        private VisualElement panelResizeHandle;
        private Button togglePanelButton;
        private Font runtimeFont;
        private Vector3Int? selectedCell;
        private Guid selectedFlightId;
        private Guid inspectedFlightId;
        private Guid inspectedPackageId;
        private Guid selectedOrdnancePassId;
        private Guid highlightedOrdnanceSourceFlightId;
        private Guid highlightedOrdnanceTargetFlightId;
        private bool showingAirOps;
        private AirOperationsView airOperationsView = AirOperationsView.Flights;
        private Alliance airFlightAlliance = Alliance.Bluefor;
        private WorkbenchPage workbenchPage = WorkbenchPage.Tile;
        private readonly List<FlightPickTarget> flightPickTargets = new List<FlightPickTarget>();
        private readonly List<CombatPickTarget> combatPickTargets = new List<CombatPickTarget>();
        private Vector2 lastPickScreenPosition = new Vector2(float.MinValue, float.MinValue);
        private int currentPickIndex;
        private readonly List<MapPickTarget> currentPickTargets = new List<MapPickTarget>();
        private readonly List<PinnedInspector> pinnedInspectors = new List<PinnedInspector>();
        private int pinnedWindowSequence;
        private bool draggingFlightWindow;
        private bool resizingFlightWindow;
        private Vector2 flightWindowPointerStart;
        private Vector2 flightWindowPositionStart;
        private Vector2 flightWindowSizeStart;
        private bool resizingWorkbenchPanel;
        private float workbenchPanelPointerStart;
        private float workbenchPanelWidthStart;

        private enum AirOperationsView
        {
            Requests,
            Packages,
            Flights,
            OrdnancePasses
        }

        private enum WorkbenchPage
        {
            Tile,
            Ground,
            Air,
            LastTurn,
            Diagnostics
        }

        private IEnumerator Start()
        {
            gameManager = gameManager != null ? gameManager : GetComponent<GameManager>();
            sceneCamera = sceneCamera != null ? sceneCamera : Camera.main;
            if (gameManager != null)
            {
                gameManager.GameTurnCompleted += RefreshCampaignAfterGameTurn;
                gameManager.AirTacticalStepCompleted += RefreshAirAfterTacticalStep;
            }

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
            {
                gameManager.GameTurnCompleted -= RefreshCampaignAfterGameTurn;
                gameManager.AirTacticalStepCompleted -= RefreshAirAfterTacticalStep;
            }
        }

        private void Update()
        {
            if (gameManager == null || !gameManager.IsCampaignStarted)
                return;

            UpdateTimeUi();
            UpdateZoomAwareMarkers();
            HandleTileSelection();
        }

        private void UpdateZoomAwareMarkers()
        {
            if (sceneCamera == null || !sceneCamera.orthographic)
                return;

            var size = sceneCamera.orthographicSize;
            if (unitCounterRoot != null)
            {
                foreach (Transform counter in unitCounterRoot)
                {
                    var count = counter.Find("Counter Label");
                    var detail = counter.Find("Counter Detail");
                    if (count != null)
                        count.gameObject.SetActive(size > 9f);
                    if (detail != null)
                        detail.gameObject.SetActive(size <= 9f);
                }
            }
            if (airOverlayRoot != null)
            {
                foreach (Transform child in airOverlayRoot)
                {
                    if (!child.name.StartsWith("Air Flight ", StringComparison.Ordinal))
                        continue;
                    var label = child.Find("Flight Label");
                    if (label != null)
                        label.gameObject.SetActive(size <= 14f);
                }
            }
            if (samOverlayRoot != null)
            {
                foreach (Transform child in samOverlayRoot)
                {
                    if (!child.name.StartsWith("SAM Icon ", StringComparison.Ordinal))
                        continue;
                    var label = child.Find("SAM Label");
                    if (label != null)
                        label.gameObject.SetActive(size <= 14f);
                }
            }
        }

        private void RefreshCampaignAfterGameTurn()
        {
            RenderCampaign(false, true);
            UpdateGroundOperationsUi();
            UpdateTurnReportUi();
            UpdateDiagnosticsUi();
            RefreshPinnedInspectors();
        }

        private void RefreshAirAfterTacticalStep()
        {
            RefreshAirControlOverlay();
            RefreshAirOverlaysForSelection();
            RefreshSamCoverageOverlay();
            RefreshOrdnanceOverlay();
            RefreshFlightDetails();
            RefreshPinnedInspectors();
        }

        private void RenderCampaign(bool frameCamera = true, bool preserveSelection = false)
        {
            if (gameManager == null || !gameManager.IsCampaignStarted)
                return;

            var previousSelectedCell = preserveSelection ? selectedCell : null;

            tilemap.ClearAllTiles();
            airControlTilemap?.ClearAllTiles();
            tilesByCell.Clear();
            hexCentersByCell.Clear();
            tilesById.Clear();

            foreach (var campaignTile in gameManager.tileSystem.Tiles)
                tilesById[campaignTile.TileId] = campaignTile;

            ClearLabels();
            ClearUnitCounters();
            ClearCombatBubbles();
            ClearMovementArrows();
            ClearRailwayLines();
            ClearAirOverlays();
            ClearAirInspection();
            ClearSamCoverageOverlay();
            ClearOrdnanceOverlay();
            flightPickTargets.Clear();
            combatPickTargets.Clear();

            foreach (var campaignTile in gameManager.tileSystem.Tiles)
            {
                var cell = GetCell(campaignTile.TileId);
                var hexCenter = GetHexCenter(campaignTile.TileId);
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
                ConfigureAirControlTile(cell, hexCenter);
                CreateTileLabel(campaignTile, hexCenter);
            }

            CreateUnitCounters();
            CreateRailwayLines();
            CreateMovementArrows();
            CreateCombatBubbles();
            CreateAirOverlays();
            ApplySelectedFlightMarker();
            CreateAirInspection();
            RefreshSamCoverageOverlay();
            RefreshOrdnanceOverlay();
            RefreshAirControlOverlay();
            SetAirRouteVisibility(overlayRoutesToggle == null || overlayRoutesToggle.value);

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

            if (airControlTilemap == null)
            {
                var overlayObject = new GameObject("Campaign Air Interference");
                overlayObject.transform.SetParent(grid.transform, false);
                airControlTilemap = overlayObject.AddComponent<Tilemap>();
                var overlayRenderer = overlayObject.AddComponent<TilemapRenderer>();
                overlayRenderer.sortingOrder = 5;
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

            if (samOverlayRoot == null)
            {
                var samObject = new GameObject("Campaign SAM Coverage");
                samObject.transform.SetParent(grid.transform, false);
                samOverlayRoot = samObject.transform;
            }

            if (ordnanceOverlayRoot == null)
            {
                var ordnanceObject = new GameObject("Campaign Ordnance Diagnostics");
                ordnanceObject.transform.SetParent(grid.transform, false);
                ordnanceOverlayRoot = ordnanceObject.transform;
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
            speedFiveSecondsButton = root.Q<Button>("speed-5s-button");
            speedFiveMinutesButton = root.Q<Button>("speed-5m-button");
            nextIncrementButton = root.Q<Button>("next-increment-button");
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
            airPassesButton = root.Q<Button>("air-passes-button");
            airListTitle = root.Q<Label>("air-list-title");
            airAllianceFilter = root.Q<VisualElement>("air-alliance-filter");
            airBlueFlightsButton = root.Q<Button>("air-blue-flights-button");
            airRedFlightsButton = root.Q<Button>("air-red-flights-button");
            airInspectionStatus = root.Q<Label>("air-inspection-status");
            airRequestsList = root.Q<VisualElement>("air-requests-list");
            airPackagesList = root.Q<VisualElement>("air-packages-list");
            airFlightsList = root.Q<VisualElement>("air-flights-list");
            airPassesList = root.Q<VisualElement>("air-passes-list");
            flightDetailBackdrop = root.Q<VisualElement>("flight-detail-backdrop");
            flightDetailTitle = root.Q<Label>("flight-detail-title");
            flightDetailSubtitle = root.Q<Label>("flight-detail-subtitle");
            flightDetailClose = root.Q<Button>("flight-detail-close");
            flightDetailScroll = root.Q<ScrollView>("flight-detail-scroll");
            flightDetailContent = root.Q<VisualElement>("flight-detail-content");
            groundTabButton = root.Q<Button>("ground-tab-button");
            turnTabButton = root.Q<Button>("turn-tab-button");
            diagnosticsTabButton = root.Q<Button>("diagnostics-tab-button");
            pinPrimaryButton = root.Q<Button>("pin-primary-button");
            workbenchPageTitle = root.Q<Label>("workbench-page-title");
            groundOpsContent = root.Q<ScrollView>("ground-ops-content");
            groundOpsSummary = root.Q<Label>("ground-ops-summary");
            groundCombatsList = root.Q<VisualElement>("ground-combats-list");
            turnReportContent = root.Q<ScrollView>("turn-report-content");
            turnReportSummary = root.Q<Label>("turn-report-summary");
            turnReportList = root.Q<VisualElement>("turn-report-list");
            diagnosticsContent = root.Q<VisualElement>("diagnostics-content");
            diagnosticsList = root.Q<VisualElement>("diagnostics-list");
            diagnosticsSearch = root.Q<TextField>("diagnostics-search");
            diagnosticsSeverity = root.Q<DropdownField>("diagnostics-severity");
            diagnosticsSystem = root.Q<DropdownField>("diagnostics-system");
            diagnosticsErrorsButton = root.Q<Button>("diagnostics-errors-button");
            diagnosticsWarningsButton = root.Q<Button>("diagnostics-warnings-button");
            resetLayoutButton = root.Q<Button>("reset-layout-button");
            buildingsFoldout = root.Q<Foldout>("buildings-foldout");
            buildingsList = root.Q<VisualElement>("buildings-list");
            airOverviewGrid = root.Q<VisualElement>("air-overview-grid");
            overlayUnitsToggle = root.Q<Toggle>("overlay-units-toggle");
            overlayCombatsToggle = root.Q<Toggle>("overlay-combats-toggle");
            overlayMovementToggle = root.Q<Toggle>("overlay-movement-toggle");
            overlayRoutesToggle = root.Q<Toggle>("overlay-routes-toggle");
            overlayBarriersToggle = root.Q<Toggle>("overlay-barriers-toggle");
            overlayTerritoryBoundariesToggle = root.Q<Toggle>("overlay-territory-boundaries-toggle");
            overlaySamToggle = root.Q<Toggle>("overlay-sam-toggle");
            overlayOrdnanceToggle = root.Q<Toggle>("overlay-ordnance-toggle");
            overlayRailToggle = root.Q<Toggle>("overlay-rail-toggle");
            overlayAirControlToggle = root.Q<Toggle>("overlay-air-control-toggle");
            windowLayer = root.Q<VisualElement>("window-layer");
            flightDetailPopup = root.Q<VisualElement>("flight-detail-popup");
            flightDetailDragHandle = root.Q<VisualElement>("flight-detail-drag-handle");
            flightDetailResizeHandle = root.Q<VisualElement>("flight-detail-resize-handle");
            flightDetailPin = root.Q<Button>("flight-detail-pin");
            flightDetailFocus = root.Q<Button>("flight-detail-focus");
            panelResizeHandle = root.Q<VisualElement>("panel-resize-handle");
            togglePanelButton = root.Q<Button>("toggle-panel-button");

            ApplyRuntimeFont(titleLabel);
            ApplyRuntimeFont(timeLabel);
            ApplyRuntimeFont(selectedTileLabel);
            ApplyRuntimeFont(pauseButton);
            ApplyRuntimeFont(speedFiveSecondsButton);
            ApplyRuntimeFont(speedFiveMinutesButton);
            ApplyRuntimeFont(nextIncrementButton);
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
            ApplyRuntimeFont(airPassesButton);
            ApplyRuntimeFont(airListTitle);
            ApplyRuntimeFont(airBlueFlightsButton);
            ApplyRuntimeFont(airRedFlightsButton);
            ApplyRuntimeFont(airInspectionStatus);
            ApplyRuntimeFont(flightDetailTitle);
            ApplyRuntimeFont(flightDetailSubtitle);
            ApplyRuntimeFont(flightDetailClose);
            ApplyRuntimeFont(groundTabButton);
            ApplyRuntimeFont(turnTabButton);
            ApplyRuntimeFont(diagnosticsTabButton);
            ApplyRuntimeFont(pinPrimaryButton);
            ApplyRuntimeFont(workbenchPageTitle);
            ApplyRuntimeFont(groundOpsSummary);
            ApplyRuntimeFont(turnReportSummary);
            ApplyRuntimeFont(diagnosticsErrorsButton);
            ApplyRuntimeFont(diagnosticsWarningsButton);
            ApplyRuntimeFont(resetLayoutButton);
            ApplyRuntimeFont(flightDetailPin);
            ApplyRuntimeFont(flightDetailFocus);
            ApplyRuntimeFont(togglePanelButton);

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

            RegisterUiPointerBoundary(root.Q<VisualElement>("workbench-topbar"));
            RegisterUiPointerBoundary(root.Q<VisualElement>("workbench-nav"));
            RegisterUiPointerBoundary(root.Q<VisualElement>("overlay-palette"));

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

            if (buildingsFoldout != null)
            {
                buildingsFoldout.pickingMode = PickingMode.Position;
                buildingsFoldout.RegisterValueChangedCallback(_ => UpdateBuildingsList());
            }

            if (pauseButton != null)
            {
                pauseButton.pickingMode = PickingMode.Position;
                pauseButton.clicked -= TogglePause;
                pauseButton.clicked += TogglePause;
            }

            if (speedFiveSecondsButton != null)
                speedFiveSecondsButton.clicked += () => SelectPlaybackIncrement(
                    CampaignPlaybackIncrement.FiveSeconds);

            if (speedFiveMinutesButton != null)
                speedFiveMinutesButton.clicked += () => SelectPlaybackIncrement(
                    CampaignPlaybackIncrement.FiveMinutes);

            if (mapTabButton != null)
            {
                mapTabButton.pickingMode = PickingMode.Position;
                mapTabButton.clicked += () => ShowWorkbenchPage(WorkbenchPage.Tile);
            }

            if (nextIncrementButton != null)
            {
                nextIncrementButton.pickingMode = PickingMode.Position;
                nextIncrementButton.clicked += AdvanceOnePlaybackIncrement;
            }

            if (airOpsTabButton != null)
            {
                airOpsTabButton.pickingMode = PickingMode.Position;
                airOpsTabButton.clicked += () => ShowWorkbenchPage(WorkbenchPage.Air);
            }
            if (groundTabButton != null)
                groundTabButton.clicked += () => ShowWorkbenchPage(WorkbenchPage.Ground);
            if (turnTabButton != null)
                turnTabButton.clicked += () => ShowWorkbenchPage(WorkbenchPage.LastTurn);
            if (diagnosticsTabButton != null)
                diagnosticsTabButton.clicked += () => ShowWorkbenchPage(WorkbenchPage.Diagnostics);
            if (diagnosticsErrorsButton != null)
                diagnosticsErrorsButton.clicked += () => OpenDiagnostics("Errors");
            if (diagnosticsWarningsButton != null)
                diagnosticsWarningsButton.clicked += () => OpenDiagnostics("Warnings");
            if (pinPrimaryButton != null)
                pinPrimaryButton.clicked += PinPrimaryInspector;
            if (resetLayoutButton != null)
                resetLayoutButton.clicked += ResetWorkbenchLayout;
            if (togglePanelButton != null)
                togglePanelButton.clicked += ToggleWorkbenchPanel;

            if (airRequestsButton != null)
                airRequestsButton.clicked += () => ShowAirOperationsView(AirOperationsView.Requests);
            if (airPackagesButton != null)
                airPackagesButton.clicked += () => ShowAirOperationsView(AirOperationsView.Packages);
            if (airFlightsButton != null)
                airFlightsButton.clicked += () => ShowAirOperationsView(AirOperationsView.Flights);
            if (airPassesButton != null)
                airPassesButton.clicked += () => ShowAirOperationsView(AirOperationsView.OrdnancePasses);
            if (airBlueFlightsButton != null)
                airBlueFlightsButton.clicked += () => ShowAirFlightAlliance(Alliance.Bluefor);
            if (airRedFlightsButton != null)
                airRedFlightsButton.clicked += () => ShowAirFlightAlliance(Alliance.Redfor);

            if (flightDetailClose != null)
            {
                flightDetailClose.pickingMode = PickingMode.Position;
                flightDetailClose.clicked += CloseFlightDetails;
            }
            if (flightDetailPin != null)
                flightDetailPin.clicked += PinCurrentFlightInspector;
            if (flightDetailFocus != null)
                flightDetailFocus.clicked += FocusSelectedFlight;

            RegisterUiPointerBoundary(flightDetailPopup);
            ConfigureFloatingFlightWindow();
            ConfigureWorkbenchPanelResize();
            ConfigureOverlayToggles();
            ConfigureDiagnosticsFilters();

            var savedPage = (WorkbenchPage)Mathf.Clamp(
                PlayerPrefs.GetInt("HZPL.Workbench.Page", (int)WorkbenchPage.Tile),
                (int)WorkbenchPage.Tile,
                (int)WorkbenchPage.Diagnostics);
            ShowWorkbenchPage(savedPage);
            ShowAirOperationsView(AirOperationsView.Flights);
            CloseFlightDetails();

            if (titleLabel == null
                || timeLabel == null
                || selectedTileLabel == null
                || pauseButton == null
                || speedFiveSecondsButton == null
                || speedFiveMinutesButton == null
                || nextIncrementButton == null)
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

        private void RegisterUiPointerBoundary(VisualElement element)
        {
            if (element == null)
                return;

            element.pickingMode = PickingMode.Position;
            element.RegisterCallback<PointerEnterEvent>(_ => IsPointerOverCampaignUi = true);
            element.RegisterCallback<PointerLeaveEvent>(_ => IsPointerOverCampaignUi = false);
        }

        private void ShowWorkbenchPage(WorkbenchPage page)
        {
            workbenchPage = page;
            if (mapInfoContent != null)
                mapInfoContent.style.display = page == WorkbenchPage.Tile
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (groundOpsContent != null)
                groundOpsContent.style.display = page == WorkbenchPage.Ground
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (airOpsContent != null)
                airOpsContent.style.display = page == WorkbenchPage.Air
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (turnReportContent != null)
                turnReportContent.style.display = page == WorkbenchPage.LastTurn
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            if (diagnosticsContent != null)
                diagnosticsContent.style.display = page == WorkbenchPage.Diagnostics
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;

            mapTabButton?.EnableInClassList("workbench-nav-button--selected", page == WorkbenchPage.Tile);
            groundTabButton?.EnableInClassList("workbench-nav-button--selected", page == WorkbenchPage.Ground);
            airOpsTabButton?.EnableInClassList("workbench-nav-button--selected", page == WorkbenchPage.Air);
            turnTabButton?.EnableInClassList("workbench-nav-button--selected", page == WorkbenchPage.LastTurn);
            diagnosticsTabButton?.EnableInClassList("workbench-nav-button--selected", page == WorkbenchPage.Diagnostics);

            if (workbenchPageTitle != null)
            {
                workbenchPageTitle.text = page switch
                {
                    WorkbenchPage.Tile => "Tile Inspector",
                    WorkbenchPage.Ground => "Ground War",
                    WorkbenchPage.Air => "Air War",
                    WorkbenchPage.LastTurn => "Last Turn Report",
                    _ => "Diagnostics"
                };
            }

            PlayerPrefs.SetInt("HZPL.Workbench.Page", (int)page);
            switch (page)
            {
                case WorkbenchPage.Ground:
                    UpdateGroundOperationsUi();
                    break;
                case WorkbenchPage.Air:
                    UpdateAirOperationsUi();
                    break;
                case WorkbenchPage.LastTurn:
                    UpdateTurnReportUi();
                    break;
                case WorkbenchPage.Diagnostics:
                    UpdateDiagnosticsUi();
                    break;
            }
        }

        private void OpenDiagnostics(string severity)
        {
            ShowWorkbenchPage(WorkbenchPage.Diagnostics);
            if (diagnosticsSeverity != null)
            {
                diagnosticsSeverity.value = severity;
                UpdateDiagnosticsUi();
            }
        }

        private void ConfigureDiagnosticsFilters()
        {
            if (diagnosticsSeverity != null)
            {
                diagnosticsSeverity.choices = new List<string> { "All", "Errors", "Warnings", "Info" };
                diagnosticsSeverity.value = "All";
                diagnosticsSeverity.RegisterValueChangedCallback(_ => UpdateDiagnosticsUi());
            }
            if (diagnosticsSystem != null)
            {
                diagnosticsSystem.choices = new List<string> { "All", "Air Planning", "Air Execution", "Ordnance" };
                diagnosticsSystem.value = "All";
                diagnosticsSystem.RegisterValueChangedCallback(_ => UpdateDiagnosticsUi());
            }
            diagnosticsSearch?.RegisterValueChangedCallback(_ => UpdateDiagnosticsUi());
        }

        private void ConfigureOverlayToggles()
        {
            ConfigureOverlayToggle(overlayUnitsToggle, "Units", true, value =>
            {
                if (unitCounterRoot != null)
                    unitCounterRoot.gameObject.SetActive(value);
            });
            ConfigureOverlayToggle(overlayCombatsToggle, "Combats", true, value =>
            {
                if (combatBubbleRoot != null)
                    combatBubbleRoot.gameObject.SetActive(value);
            });
            ConfigureOverlayToggle(overlayMovementToggle, "Movement", true, value =>
            {
                if (movementArrowRoot != null)
                    movementArrowRoot.gameObject.SetActive(value);
            });
            ConfigureOverlayToggle(overlayRoutesToggle, "Routes", true, SetAirRouteVisibility);
            ConfigureOverlayToggle(
                overlayBarriersToggle,
                "Barriers",
                true,
                _ => RefreshAirOverlaysForSelection());
            ConfigureOverlayToggle(
                overlayTerritoryBoundariesToggle,
                "TerritoryBoundaries",
                true,
                _ => RefreshTerritoryBoundaries());
            ConfigureOverlayToggle(overlaySamToggle, "SamCoverage", true, _ => RefreshSamCoverageOverlay());
            ConfigureOverlayToggle(overlayRailToggle, "Railways", true, value =>
            {
                if (railwayRoot != null)
                    railwayRoot.gameObject.SetActive(value);
            });
            ConfigureOverlayToggle(overlayOrdnanceToggle, "Ordnance", true, _ => RefreshOrdnanceOverlay());
            ConfigureOverlayToggle(overlayAirControlToggle, "AirControl", false, value =>
            {
                if (airControlTilemap != null)
                    airControlTilemap.gameObject.SetActive(value);
                if (value)
                    RefreshAirControlOverlay();
            });
        }

        private static void ConfigureOverlayToggle(
            Toggle toggle,
            string preferenceName,
            bool defaultValue,
            Action<bool> apply)
        {
            if (toggle == null)
                return;

            var key = $"HZPL.Workbench.Overlay.{preferenceName}";
            var value = PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) != 0;
            toggle.SetValueWithoutNotify(value);
            apply(value);
            toggle.RegisterValueChangedCallback(evt =>
            {
                PlayerPrefs.SetInt(key, evt.newValue ? 1 : 0);
                apply(evt.newValue);
            });
        }

        private void SetAirRouteVisibility(bool visible)
        {
            if (airOverlayRoot == null)
                return;

            foreach (Transform child in airOverlayRoot)
            {
                if (child.name.StartsWith("Air Route ", StringComparison.Ordinal))
                    child.gameObject.SetActive(visible);
            }
        }

        private void RefreshTerritoryBoundaries()
        {
            if (tilemap == null)
                return;

            foreach (var campaignTile in tilesById.Values)
                tilemap.SetTile(GetCell(campaignTile.TileId), GetRenderTile(campaignTile));

            tilemap.RefreshAllTiles();
        }

        private void ConfigureAirControlTile(Vector3Int cell, Vector3 hexCenter)
        {
            if (airControlTilemap == null)
                return;

            airControlTilemap.SetTile(cell, GetAirControlRenderTile());
            airControlTilemap.SetTileFlags(cell, TileFlags.None);
            airControlTilemap.SetTransformMatrix(
                cell,
                Matrix4x4.TRS(
                    hexCenter - airControlTilemap.GetCellCenterLocal(cell),
                    Quaternion.identity,
                    new Vector3(1f, HexHeight, 1f)));
        }

        private void RefreshAirControlOverlay()
        {
            if (airControlTilemap == null || !airControlTilemap.gameObject.activeSelf)
                return;

            var blueCommander = gameManager?.GetAllianceAirTaskingCommander(Alliance.Bluefor);
            var redCommander = gameManager?.GetAllianceAirTaskingCommander(Alliance.Redfor);
            foreach (var campaignTile in tilesById.Values)
            {
                var hasBlueEstimate = TryGetAirInterferencePicture(
                    blueCommander,
                    campaignTile.TileId,
                    out var blueBalance,
                    out var blueInterference);
                var hasRedEstimate = TryGetAirInterferencePicture(
                    redCommander,
                    campaignTile.TileId,
                    out var redBalance,
                    out var redInterference);
                var estimateCount = (hasBlueEstimate ? 1 : 0) + (hasRedEstimate ? 1 : 0);
                var combinedBlueBalance = estimateCount > 0
                    ? ((hasBlueEstimate ? blueBalance : 0f)
                       + (hasRedEstimate ? -redBalance : 0f)) / estimateCount
                    : 0f;
                var combinedInterference = estimateCount > 0
                    ? ((hasBlueEstimate ? blueInterference : 0f)
                       + (hasRedEstimate ? redInterference : 0f)) / estimateCount
                    : 0f;
                airControlTilemap.SetColor(
                    GetCell(campaignTile.TileId),
                    GetAirInterferenceOverlayColor(
                        combinedBlueBalance,
                        combinedInterference));
            }

            // SetColor is immediate. RefreshAllTiles would reapply the shared tile's
            // opaque white default and erase these per-cell translucent tints.
        }

        private static bool TryGetAirInterferencePicture(
            AllianceAirTaskingCommander commander,
            Vector3Int tileId,
            out float interferenceBalance,
            out float interferenceStrength)
        {
            interferenceBalance = 0f;
            interferenceStrength = 0f;
            if (commander == null
                || !commander.TryGetAirControlAssessment(tileId, out var assessment))
                return false;

            var friendlyInterference = assessment.FriendlyAirInterference;
            var hostileInterference = assessment.HostileAirInterference;
            var totalInterference = friendlyInterference + hostileInterference;
            interferenceBalance = totalInterference > 0f
                ? (friendlyInterference - hostileInterference) / totalInterference
                : 0f;
            interferenceStrength = Mathf.Max(
                friendlyInterference,
                hostileInterference);
            return true;
        }

        private static Color GetAirInterferenceOverlayColor(
            float blueBalance,
            float interferenceStrength)
        {
            var balance = Mathf.Clamp(blueBalance, -1f, 1f);
            var color = balance >= 0f
                ? Color.Lerp(ContestedAirControlColor, BlueAirControlColor, balance)
                : Color.Lerp(ContestedAirControlColor, RedAirControlColor, -balance);
            color.a = Mathf.Clamp01(interferenceStrength) * MaximumAirControlOverlayAlpha;
            return color;
        }

        private UnityEngine.Tilemaps.Tile GetAirControlRenderTile()
        {
            if (airControlRenderTile != null)
                return airControlRenderTile;

            var pixels = Enumerable.Repeat(Color.white, TilePixelSize * TilePixelSize).ToArray();
            ApplyHexMask(pixels);
            var texture = new Texture2D(TilePixelSize, TilePixelSize)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(pixels);
            texture.Apply();
            var sprite = Sprite.Create(
                texture,
                new Rect(0, 0, TilePixelSize, TilePixelSize),
                new Vector2(0.5f, 0.5f),
                TilePixelSize);
            airControlRenderTile = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
            airControlRenderTile.sprite = sprite;
            airControlRenderTile.color = Color.white;
            return airControlRenderTile;
        }

        private void ResetWorkbenchLayout()
        {
            PlayerPrefs.DeleteKey("HZPL.Workbench.Window.Left");
            PlayerPrefs.DeleteKey("HZPL.Workbench.Window.Top");
            PlayerPrefs.DeleteKey("HZPL.Workbench.Window.Width");
            PlayerPrefs.DeleteKey("HZPL.Workbench.Window.Height");
            PlayerPrefs.DeleteKey("HZPL.Workbench.Panel.Width");
            PlayerPrefs.DeleteKey("HZPL.Workbench.Panel.Hidden");
            foreach (var name in new[]
                     {
                         "Units",
                         "Combats",
                         "Movement",
                         "Routes",
                         "Barriers",
                         "TerritoryBoundaries",
                         "SamCoverage",
                         "Ordnance",
                         "Railways",
                         "AirControl"
                     })
                PlayerPrefs.DeleteKey($"HZPL.Workbench.Overlay.{name}");
            if (hudPanel != null)
            {
                hudPanel.style.display = DisplayStyle.Flex;
                hudPanel.style.width = 438f;
            }
            if (togglePanelButton != null)
                togglePanelButton.text = "HIDE PANEL";
            SetOverlayToggleValue(overlayUnitsToggle, true);
            SetOverlayToggleValue(overlayCombatsToggle, true);
            SetOverlayToggleValue(overlayMovementToggle, true);
            SetOverlayToggleValue(overlayRoutesToggle, true);
            SetOverlayToggleValue(overlayBarriersToggle, true);
            SetOverlayToggleValue(overlayTerritoryBoundariesToggle, true);
            SetOverlayToggleValue(overlaySamToggle, true);
            SetOverlayToggleValue(overlayOrdnanceToggle, true);
            SetOverlayToggleValue(overlayRailToggle, true);
            SetOverlayToggleValue(overlayAirControlToggle, false);
            if (flightDetailPopup != null)
            {
                flightDetailPopup.style.left = 670f;
                flightDetailPopup.style.top = 120f;
                flightDetailPopup.style.width = 650f;
                flightDetailPopup.style.height = 780f;
            }
        }

        private static void SetOverlayToggleValue(Toggle toggle, bool value)
        {
            if (toggle != null)
                toggle.value = value;
        }

        private void ConfigureWorkbenchPanelResize()
        {
            if (hudPanel == null || panelResizeHandle == null)
                return;

            hudPanel.style.width = PlayerPrefs.GetFloat("HZPL.Workbench.Panel.Width", 438f);
            var hidden = PlayerPrefs.GetInt("HZPL.Workbench.Panel.Hidden", 0) != 0;
            hudPanel.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex;
            if (togglePanelButton != null)
                togglePanelButton.text = hidden ? "SHOW PANEL" : "HIDE PANEL";
            panelResizeHandle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                resizingWorkbenchPanel = true;
                workbenchPanelPointerStart = evt.position.x;
                workbenchPanelWidthStart = hudPanel.resolvedStyle.width;
                panelResizeHandle.CapturePointer(evt.pointerId);
            });
            panelResizeHandle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizingWorkbenchPanel)
                    return;
                hudPanel.style.width = Mathf.Clamp(
                    workbenchPanelWidthStart + evt.position.x - workbenchPanelPointerStart,
                    340f,
                    720f);
            });
            panelResizeHandle.RegisterCallback<PointerUpEvent>(evt =>
            {
                resizingWorkbenchPanel = false;
                panelResizeHandle.ReleasePointer(evt.pointerId);
                PlayerPrefs.SetFloat("HZPL.Workbench.Panel.Width", hudPanel.resolvedStyle.width);
            });
        }

        private void ToggleWorkbenchPanel()
        {
            if (hudPanel == null)
                return;
            var hidden = hudPanel.resolvedStyle.display != DisplayStyle.None;
            hudPanel.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex;
            PlayerPrefs.SetInt("HZPL.Workbench.Panel.Hidden", hidden ? 1 : 0);
            if (togglePanelButton != null)
                togglePanelButton.text = hidden ? "SHOW PANEL" : "HIDE PANEL";
        }

        private void ConfigureFloatingFlightWindow()
        {
            if (flightDetailPopup == null)
                return;

            flightDetailPopup.style.left = PlayerPrefs.GetFloat("HZPL.Workbench.Window.Left", 670f);
            flightDetailPopup.style.top = PlayerPrefs.GetFloat("HZPL.Workbench.Window.Top", 120f);
            flightDetailPopup.style.width = PlayerPrefs.GetFloat("HZPL.Workbench.Window.Width", 650f);
            flightDetailPopup.style.height = PlayerPrefs.GetFloat("HZPL.Workbench.Window.Height", 780f);

            flightDetailDragHandle?.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.target is Button)
                    return;
                draggingFlightWindow = true;
                flightWindowPointerStart = evt.position;
                flightWindowPositionStart = new Vector2(
                    flightDetailPopup.resolvedStyle.left,
                    flightDetailPopup.resolvedStyle.top);
                flightDetailDragHandle.CapturePointer(evt.pointerId);
            });
            flightDetailDragHandle?.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!draggingFlightWindow)
                    return;
                var next = flightWindowPositionStart + (Vector2)evt.position - flightWindowPointerStart;
                SetFloatingWindowPosition(flightDetailPopup, next);
            });
            flightDetailDragHandle?.RegisterCallback<PointerUpEvent>(evt =>
            {
                draggingFlightWindow = false;
                flightDetailDragHandle.ReleasePointer(evt.pointerId);
                SaveFlightWindowLayout();
            });

            flightDetailResizeHandle?.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                resizingFlightWindow = true;
                flightWindowPointerStart = evt.position;
                flightWindowSizeStart = new Vector2(
                    flightDetailPopup.resolvedStyle.width,
                    flightDetailPopup.resolvedStyle.height);
                flightDetailResizeHandle.CapturePointer(evt.pointerId);
            });
            flightDetailResizeHandle?.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizingFlightWindow)
                    return;
                var next = flightWindowSizeStart + (Vector2)evt.position - flightWindowPointerStart;
                flightDetailPopup.style.width = Mathf.Clamp(next.x, 420f, 1000f);
                flightDetailPopup.style.height = Mathf.Clamp(next.y, 360f, 940f);
            });
            flightDetailResizeHandle?.RegisterCallback<PointerUpEvent>(evt =>
            {
                resizingFlightWindow = false;
                flightDetailResizeHandle.ReleasePointer(evt.pointerId);
                SaveFlightWindowLayout();
            });
        }

        private static void SetFloatingWindowPosition(VisualElement window, Vector2 position)
        {
            var width = Mathf.Max(320f, window.resolvedStyle.width);
            var height = Mathf.Max(240f, window.resolvedStyle.height);
            window.style.left = Mathf.Clamp(position.x, 0f, 1920f - width);
            window.style.top = Mathf.Clamp(position.y, 58f, 1080f - height);
        }

        private void SaveFlightWindowLayout()
        {
            if (flightDetailPopup == null)
                return;
            PlayerPrefs.SetFloat("HZPL.Workbench.Window.Left", flightDetailPopup.resolvedStyle.left);
            PlayerPrefs.SetFloat("HZPL.Workbench.Window.Top", flightDetailPopup.resolvedStyle.top);
            PlayerPrefs.SetFloat("HZPL.Workbench.Window.Width", flightDetailPopup.resolvedStyle.width);
            PlayerPrefs.SetFloat("HZPL.Workbench.Window.Height", flightDetailPopup.resolvedStyle.height);
        }

        private void PinPrimaryInspector()
        {
            switch (workbenchPage)
            {
                case WorkbenchPage.Tile:
                    if (!selectedCell.HasValue || !tilesByCell.TryGetValue(selectedCell.Value, out var tile))
                        return;
                    CreatePinnedInspector(
                        $"Tile {FormatTile(tile.TileId)}",
                        () => BuildTileInspectorLines(tile.TileId),
                        () => FocusTile(tile.TileId));
                    break;
                case WorkbenchPage.Ground:
                    CreatePinnedInspector("Ground War", BuildGroundOverviewLines);
                    break;
                case WorkbenchPage.Air:
                    if (selectedFlightId != Guid.Empty)
                        PinCurrentFlightInspector();
                    else
                        CreatePinnedInspector("Air War Overview", BuildAirOverviewLines);
                    break;
                case WorkbenchPage.LastTurn:
                    var turnLines = BuildLastTurnLines().ToList();
                    CreatePinnedInspector("Last Turn Report", () => turnLines);
                    break;
                case WorkbenchPage.Diagnostics:
                    CreatePinnedInspector("Diagnostics", BuildDiagnosticLines);
                    break;
            }
        }

        private void PinDivisionInspector(Division division)
        {
            if (division == null)
                return;
            CreatePinnedInspector(
                string.IsNullOrWhiteSpace(division.Name) ? "Division" : division.Name,
                () => BuildDivisionInspectorLines(division.DivisionId),
                () =>
                {
                    if (gameManager.divisionSystem.TryGetDivision(division.DivisionId, out var current))
                        FocusTile(current.TileId);
                });
        }

        private void PinBuildingInspector(Building building)
        {
            if (building == null)
                return;
            CreatePinnedInspector(
                $"{building.Type} {ShortId(building.BuildingId)}",
                () =>
                {
                    var current = gameManager.buildingSystem.Buildings
                        .FirstOrDefault(candidate => candidate.BuildingId == building.BuildingId);
                    if (current == null)
                        return new[] { "STALE", "Building no longer exists." };
                    var lines = new List<string>
                    {
                        $"Building ID  {current.BuildingId:N}",
                        $"Tile  {FormatTile(current.TileId)}",
                        $"Type  {current.Type}",
                        $"Build level  {current.Level.BuildLevel}",
                        $"Damage  {current.Level.Damage}",
                        $"Functional level  {current.FunctionalLevel}",
                        $"Target toughness  {current.TargetToughness}"
                    };
                    if (current is Airport airport)
                    {
                        var operations =
                            gameManager.GetAirportOperationsSnapshot(
                                airport.BuildingId);
                        lines.Add(
                            $"Runway integrity  {operations.RunwayIntegrity}/{operations.MaximumRunwayIntegrity}");
                        lines.Add(
                            $"Runway capacity  {operations.EffectiveCapacityChannels}/{operations.NominalCapacityChannels} channels");
                        lines.Add(
                            $"Movement capacity  {operations.AircraftMovementCapacity} aircraft/{AirportOperationsRules.MovementWindow.TotalMinutes:0} min");
                        lines.Add(
                            $"Reserved now  {operations.ReservedChannelSlots}/{operations.EffectiveCapacityChannels} channels");
                        lines.Add(
                            $"Air operations  {FormatAirportOperationsStatus(operations)}");
                    }

                    lines.AddRange(BuildSamSiteInspectorLinesForHost(
                        current.BuildingId));
                    return lines;
                },
                () => FocusTile(building.TileId));
        }

        private void PinCurrentFlightInspector()
        {
            if (selectedFlightId == Guid.Empty)
                return;
            var flightId = selectedFlightId;
            CreatePinnedInspector(
                $"Flight {ShortId(flightId)}",
                () => BuildFlightInspectorLines(flightId));
        }

        private void OpenOrdnancePassInspector(Guid employmentPassId)
        {
            if (!TryFindOrdnanceReleaseRecord(employmentPassId, out var record))
                return;

            selectedOrdnancePassId = employmentPassId;
            highlightedOrdnanceSourceFlightId = record.SourceFlightId;
            highlightedOrdnanceTargetFlightId = record.TargetFlightId;
            SetOverlayToggleValue(overlayOrdnanceToggle, true);
            RefreshOrdnanceOverlay();
            ApplySelectedFlightMarker();

            var window = CreateFloatingReportWindow(
                $"Ordnance Pass {ShortId(employmentPassId)}",
                () =>
                {
                    if (selectedOrdnancePassId == employmentPassId)
                    {
                        selectedOrdnancePassId = Guid.Empty;
                        highlightedOrdnanceSourceFlightId = Guid.Empty;
                        highlightedOrdnanceTargetFlightId = Guid.Empty;
                        RefreshOrdnanceOverlay();
                        ApplySelectedFlightMarker();
                    }
                });
            if (window.content == null)
                return;

            var actions = new VisualElement();
            actions.AddToClassList("filter-row");
            AddReportButton(actions, "Fit Pass", () => FrameOrdnancePass(record));
            AddReportButton(actions, "Source", () => SelectOrdnanceSource(record));
            AddReportButton(actions, "Target", () => SelectOrdnanceTarget(record));
            window.content.Add(actions);

            foreach (var line in BuildOrdnancePassInspectorLines(record))
                AddCompactLine(window.content, line);
        }

        private (VisualElement window, VisualElement content) CreateFloatingReportWindow(
            string title,
            Action onClose = null)
        {
            if (windowLayer == null)
                return (null, null);

            pinnedWindowSequence++;
            var window = new VisualElement();
            window.AddToClassList("floating-window");
            window.style.left = 640f + (pinnedWindowSequence % 6) * 30f;
            window.style.top = 118f + (pinnedWindowSequence % 6) * 26f;
            window.style.width = 610f;
            window.style.height = 690f;
            window.pickingMode = PickingMode.Position;

            var header = new VisualElement();
            header.AddToClassList("floating-window-header");
            var heading = new Label(title);
            heading.AddToClassList("floating-window-title");
            heading.style.flexGrow = 1f;
            ApplyRuntimeFont(heading);
            var close = new Button(() =>
            {
                onClose?.Invoke();
                window.RemoveFromHierarchy();
            })
            {
                text = "×"
            };
            close.AddToClassList("window-close");
            ApplyRuntimeFont(close);
            header.Add(heading);
            header.Add(close);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("floating-window-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            var content = new VisualElement();
            scroll.Add(content);
            var resizeHandle = new VisualElement();
            resizeHandle.AddToClassList("window-resize-handle");

            window.Add(header);
            window.Add(scroll);
            window.Add(resizeHandle);
            windowLayer.Add(window);
            RegisterUiPointerBoundary(window);
            ConfigureFloatingWindowInteraction(window, header, resizeHandle);
            window.BringToFront();
            return (window, content);
        }

        private void AddReportButton(VisualElement parent, string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList("workbench-button");
            button.AddToClassList("workbench-button--small");
            ApplyRuntimeFont(button);
            parent.Add(button);
        }

        private void FocusSelectedFlight()
        {
            if (selectedFlightId != Guid.Empty)
                InspectFlightRoute(selectedFlightId);
        }

        private void FocusTile(Vector3Int tileId)
        {
            var cell = GetCell(tileId);
            if (!hexCentersByCell.TryGetValue(cell, out var center) || sceneCamera == null)
                return;
            var world = grid.transform.TransformPoint(center);
            sceneCamera.transform.position = new Vector3(
                world.x,
                world.y,
                sceneCamera.transform.position.z);
            sceneCamera.orthographicSize = Mathf.Clamp(sceneCamera.orthographicSize, 2f, 8f);
        }

        private void CreatePinnedInspector(
            string title,
            Func<IEnumerable<string>> lineFactory,
            Action focusOnMap = null)
        {
            if (windowLayer == null || lineFactory == null)
                return;

            pinnedWindowSequence++;
            var window = new VisualElement();
            window.AddToClassList("floating-window");
            window.style.left = 590f + (pinnedWindowSequence % 7) * 28f;
            window.style.top = 100f + (pinnedWindowSequence % 7) * 24f;
            window.style.width = 520f;
            window.style.height = 600f;
            window.pickingMode = PickingMode.Position;

            var header = new VisualElement();
            header.AddToClassList("floating-window-header");
            var heading = new Label(title);
            heading.AddToClassList("floating-window-title");
            heading.style.flexGrow = 1f;
            ApplyRuntimeFont(heading);
            var close = new Button();
            close.text = "×";
            close.AddToClassList("window-close");
            ApplyRuntimeFont(close);
            header.Add(heading);
            if (focusOnMap != null)
            {
                var focus = new Button(focusOnMap) { text = "Focus" };
                focus.AddToClassList("workbench-button");
                focus.AddToClassList("workbench-button--small");
                ApplyRuntimeFont(focus);
                header.Add(focus);
            }
            header.Add(close);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("floating-window-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
            var content = new VisualElement();
            scroll.Add(content);

            var resizeHandle = new VisualElement();
            resizeHandle.AddToClassList("window-resize-handle");
            window.Add(header);
            window.Add(scroll);
            window.Add(resizeHandle);
            windowLayer.Add(window);
            RegisterUiPointerBoundary(window);

            var inspector = new PinnedInspector(window, content, lineFactory);
            pinnedInspectors.Add(inspector);
            close.clicked += () =>
            {
                pinnedInspectors.Remove(inspector);
                window.RemoveFromHierarchy();
            };
            ConfigureFloatingWindowInteraction(window, header, resizeHandle);
            RefreshPinnedInspector(inspector);
            window.BringToFront();
        }

        private void ConfigureFloatingWindowInteraction(
            VisualElement window,
            VisualElement header,
            VisualElement resizeHandle)
        {
            var dragging = false;
            var resizing = false;
            var pointerStart = Vector2.zero;
            var positionStart = Vector2.zero;
            var sizeStart = Vector2.zero;
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || evt.target is Button)
                    return;
                dragging = true;
                pointerStart = evt.position;
                positionStart = new Vector2(window.resolvedStyle.left, window.resolvedStyle.top);
                header.CapturePointer(evt.pointerId);
                window.BringToFront();
            });
            header.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (dragging)
                    SetFloatingWindowPosition(window, positionStart + (Vector2)evt.position - pointerStart);
            });
            header.RegisterCallback<PointerUpEvent>(evt =>
            {
                dragging = false;
                header.ReleasePointer(evt.pointerId);
            });
            resizeHandle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                    return;
                resizing = true;
                pointerStart = evt.position;
                sizeStart = new Vector2(window.resolvedStyle.width, window.resolvedStyle.height);
                resizeHandle.CapturePointer(evt.pointerId);
                window.BringToFront();
            });
            resizeHandle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!resizing)
                    return;
                var next = sizeStart + (Vector2)evt.position - pointerStart;
                window.style.width = Mathf.Clamp(next.x, 360f, 1100f);
                window.style.height = Mathf.Clamp(next.y, 280f, 960f);
            });
            resizeHandle.RegisterCallback<PointerUpEvent>(evt =>
            {
                resizing = false;
                resizeHandle.ReleasePointer(evt.pointerId);
            });
        }

        private void RefreshPinnedInspectors()
        {
            foreach (var inspector in pinnedInspectors.ToList())
                RefreshPinnedInspector(inspector);
        }

        private void RefreshPinnedInspector(PinnedInspector inspector)
        {
            inspector.Content.Clear();
            foreach (var text in inspector.Lines() ?? Array.Empty<string>())
            {
                var label = new Label(text ?? string.Empty);
                label.AddToClassList("flight-detail-line");
                ApplyRuntimeFont(label);
                inspector.Content.Add(label);
            }
        }

        private IEnumerable<string> BuildTileInspectorLines(Vector3Int tileId)
        {
            if (!tilesById.TryGetValue(tileId, out var tile))
                return new[] { "STALE", "Tile no longer exists." };
            var land = tile as RuntimeLandTile;
            var lines = new List<string>
            {
                $"Coordinates  {FormatTile(tileId)}",
                $"Surface / terrain  {tile.Surface} / {tile.Terrain}",
                $"Urbanization / forest  {tile.Urbanization} / {tile.ForestCover}",
                $"Controller  {(land == null ? "None" : land.Controller.ToString())}",
                $"Infrastructure  {(land == null ? "N/A" : $"{land.InfrastructureFunctionalLevel}/{land.InfrastructureBuildLevel} (damage {land.InfrastructureDamage})")}",
            };
            lines.AddRange(gameManager.divisionSystem.GetDivisionsOnTile(tileId)
                .OrderBy(division => division.Name)
                .Select(division =>
                    $"UNIT  {division.Name}  •  Strength {GetDivisionStatPercent(division.Strength, division.MaxStrength)}%  •  Org {GetDivisionStatPercent(division.Organization, division.MaxOrganization)}%"));
            lines.AddRange(gameManager.buildingSystem.GetBuildingsOnTile(tileId)
                .OrderBy(building => building.Type)
                .Select(building =>
                    $"BUILDING  {building.Type}  •  Functional {building.FunctionalLevel}/{building.Level.BuildLevel}  •  Damage {building.Level.Damage}"));
            foreach (var site in GetSamSitesOnTile(tileId))
                lines.AddRange(BuildSamSiteInspectorLines(site));
            return lines;
        }

        private IEnumerable<string> BuildDivisionInspectorLines(Guid divisionId)
        {
            if (!gameManager.divisionSystem.TryGetDivision(divisionId, out var division) || division == null)
                return new[] { "STALE", "Division no longer exists." };
            var lines = new List<string>
            {
                $"Division ID  {division.DivisionId:N}",
                $"Tile  {FormatTile(division.TileId)}",
                $"Alliance  {GetDivisionAlliance(division)}",
                $"Strength  {division.Strength:0.#}/{division.MaxStrength}",
                $"Organization  {division.Organization:0.#}/{division.MaxOrganization}",
                $"Soft / hard attack  {division.SoftAttack:0.#} / {division.HardAttack:0.#}",
                $"Defense / toughness  {division.Defense} / {division.Toughness}",
                $"Combat width  {division.CombatWidth}",
                $"Supply  {division.SupplyStore:0.##}/{division.MaxSupplyStore:0.##}",
                $"Order  {division.CurrentOrder?.GetType().Name ?? "None"}",
                $"Intent  {division.CurrentOrder?.AIIntent}",
                $"Rationale  {division.CurrentOrder?.Rationale}"
            };
            if (division.CurrentOrder is MoveGroundOrder move)
            {
                var tileDistance = Mathf.Max(
                    SimulationSettings.MinTileDistanceKM,
                    gameManager.SimulationSettings.TileDistanceKM);
                var hours = division.Speed <= 0f
                    ? float.PositiveInfinity
                    : Mathf.Max(0f, 1f - move.MovementProgress) * tileDistance / division.Speed;
                lines.Add($"Moving to  {FormatTile(move.CurrentDestinationTileId)}");
                lines.Add($"Final destination  {FormatTile(move.DestinationTileId)}");
                lines.Add($"Movement progress  {move.MovementProgress:P1}");
                lines.Add($"Estimated next-tile arrival  {(float.IsInfinity(hours) ? "Never" : gameManager.CurrentTime.AddHours(hours).ToString("yyyy-MM-dd HH:mm"))}");
            }
            lines.AddRange(BuildSamSiteInspectorLinesForHost(division.DivisionId));
            return lines;
        }

        private IEnumerable<string> BuildFlightInspectorLines(Guid flightId)
        {
            if (!TryFindFlight(flightId, out var flight, out var package, out _))
                return new[] { "STALE", "Flight no longer exists." };
            var squadron = gameManager.squadronSystem.Squadrons
                .First(candidate => candidate.SquadronId == flight.SquadronId);
            var lines = new List<string>
            {
                $"Flight ID  {flight.FlightId:N}",
                $"Flight  {GetFlightName(flight, squadron)}",
                $"Alliance  {package.Alliance}",
                $"Mission  {GetMissionLabel(flight.MissionType)}",
                $"Package  {package.PackageId:N}",
                $"Aircraft  {flight.AircraftIds.Count}",
                $"Lifecycle / execution  {flight.LifecycleState} / {flight.ExecutionPhase}",
                $"Position  {(flight.HasPosition ? $"X {flight.PositionFeet.x:0}, Z {flight.PositionFeet.z:0}, Alt {flight.PositionFeet.y:0} ft" : "Not airborne")}",
                $"Heading / speed  {flight.HeadingDegrees:0}° / {flight.SpeedKnots:0} kt",
                $"Route progress  {Mathf.Clamp(flight.CurrentWaypointIndex + 1, 0, flight.Route.Count)}/{flight.Route.Count}"
            };
            var aircraftById = squadron.Aircraft
                .ToDictionary(aircraft => aircraft.AircraftId);
            foreach (var aircraftId in flight.AircraftIds)
            {
                if (!aircraftById.TryGetValue(aircraftId, out var aircraft))
                    continue;

                lines.Add(string.Empty);
                lines.Add(
                    $"AIRCRAFT {ShortId(aircraft.AircraftId)}  {aircraft.Status}");
                lines.AddRange(BuildAircraftOrdnanceLines(aircraft, 10)
                    .Select(line => $"  {line}"));
            }
            foreach (var record in gameManager.GetOrdnanceEmploymentRecords()
                         .Where(record => record.SourceFlightId == flightId || record.TargetFlightId == flightId)
                         .OrderByDescending(record => record.OccurredAt)
                         .Take(25))
            {
                lines.Add($"{record.OccurredAt:HH:mm:ss}  {record.Stage}  {record.Detail}");
                if (record.Stage == OrdnanceEmploymentRecordStage.OrdnanceReleased)
                {
                    foreach (var launch in record.Launches.OrderBy(item => item.Sequence))
                    {
                        var shot = FindResolvedShot(record, launch.Sequence);
                        lines.Add(
                            $"  Launch {launch.Sequence}: aircraft {ShortId(launch.SourceAircraftId)} " +
                            $"→ aircraft {ShortId(launch.TargetAircraftId)}  " +
                            $"{GetOrdnanceName(launch.OrdnanceTypeDefinitionId)}" +
                            (shot == null ? string.Empty : $"  result {shot.Result}"));
                    }
                }
                foreach (var shot in record.Shots)
                    lines.Add($"  Shot {shot.Sequence}: {shot.Result}  target {ShortId(shot.TargetAircraftId)}  P {shot.Probability:P1}  roll {(shot.Roll < 0f ? "—" : shot.Roll.ToString("0.000"))}");
            }
            return lines;
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
            SetAirListVisible(airRequestsList, view == AirOperationsView.Requests);
            SetAirListVisible(airPackagesList, view == AirOperationsView.Packages);
            SetAirListVisible(airFlightsList, view == AirOperationsView.Flights);
            SetAirListVisible(airPassesList, view == AirOperationsView.OrdnancePasses);
            airAllianceFilter?.EnableInClassList(
                "campaign-air-alliance-filter--hidden",
                view != AirOperationsView.Flights);

            SetAirTabSelected(airRequestsButton, view == AirOperationsView.Requests);
            SetAirTabSelected(airPackagesButton, view == AirOperationsView.Packages);
            SetAirTabSelected(airFlightsButton, view == AirOperationsView.Flights);
            SetAirTabSelected(airPassesButton, view == AirOperationsView.OrdnancePasses);

            if (airListTitle != null)
            {
                airListTitle.text = view switch
                {
                    AirOperationsView.Requests => "CURRENT MISSION REQUESTS",
                    AirOperationsView.Packages => "CURRENT AIR PACKAGES",
                    AirOperationsView.OrdnancePasses => "LAST TURN ORDNANCE PASSES - SELECT A ROW FOR DETAILS",
                    _ => "CURRENT FLIGHTS — SELECT A ROW FOR DETAILS"
                };
            }

            if (airOpsContent != null)
                airOpsContent.scrollOffset = Vector2.zero;

            UpdateAirOperationsUi();
        }

        private static void SetAirListVisible(VisualElement list, bool visible)
        {
            if (list == null)
                return;

            list.EnableInClassList("workbench-page--hidden", !visible);
            list.EnableInClassList("campaign-air-list--hidden", !visible);
            list.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetAirTabSelected(Button button, bool selected)
        {
            if (button == null)
                return;

            button.EnableInClassList("segment--selected", selected);
            button.EnableInClassList("campaign-air-view-tab--selected", selected);
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
                .ToList();
            var requests = commanders
                .SelectMany(commander => commander.MissionRequests)
                .Where(request => !request.IsTerminal)
                .OrderBy(request => request.Alliance)
                .ThenByDescending(request => request.Priority)
                .ToList();
            var packages = commanders
                .SelectMany(commander => commander.Packages)
                .Where(package => !package.HasPhysicallyEnded)
                .OrderBy(package => package.Alliance)
                .ThenBy(package => package.EarliestTakeoffTime)
                .ToList();
            var flights = packages
                .SelectMany(package => package.Flights)
                .Where(flight => !flight.HasPhysicallyEnded)
                .OrderBy(flight => flight.PlannedTakeoffTime)
                .ToList();
            var ordnancePasses = GetLastTurnOrdnanceReleaseRecords()
                .OrderByDescending(record => record.OccurredAt)
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
            if (airPassesButton != null)
                airPassesButton.text = $"Ordnance  {ordnancePasses.Count}";
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
            RebuildAirPassesList(ordnancePasses);
            UpdateAirOverviewUi();
            if (selectedFlightId != Guid.Empty)
                RefreshFlightDetails();
        }

        private void UpdateAirOverviewUi()
        {
            if (airOverviewGrid == null || gameManager?.squadronSystem == null)
                return;

            airOverviewGrid.Clear();
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var squadrons = gameManager.squadronSystem.Squadrons
                    .Where(squadron => gameManager.GetCountryAlliance(squadron.CountryId) == alliance)
                    .ToList();
                var allAircraft = squadrons.SelectMany(squadron => squadron.Aircraft).ToList();
                var coverage = CalculateAirInterferenceCoverage(alliance);
                var commander = gameManager.GetAllianceAirTaskingCommander(alliance);
                var openRequests = commander?.MissionRequests.Count(request => !request.IsTerminal) ?? 0;
                var unmetRequests = commander?.MissionRequests.Count(request =>
                    !request.IsTerminal
                    && request.State != AirMissionRequestState.Fulfilled
                    && request.State != AirMissionRequestState.InProgress) ?? 0;
                var panel = new VisualElement();
                panel.AddToClassList("campaign-air-card");
                panel.AddToClassList(alliance == Alliance.Bluefor
                    ? "campaign-air-card--blue"
                    : "campaign-air-card--red");
                AddCompactLine(panel, $"{GetAllianceLabel(alliance).ToUpperInvariant()} AIR PICTURE", true);
                AddCompactLine(
                    panel,
                    $"Aircraft {allAircraft.Count}  •  Ready {squadrons.Sum(item => item.ReadyAircraft)}  •  Assigned {squadrons.Sum(item => item.AssignedAircraft)}  •  Damaged {squadrons.Sum(item => item.DamagedAircraft)}  •  Lost {squadrons.Sum(item => item.LostAircraft)}");
                AddCompactLine(
                    panel,
                    $"Own airspace interference: friendly only {coverage.FriendlyOnly}/{coverage.TotalLand}  •  hostile only {coverage.HostileOnly}/{coverage.TotalLand}  •  both {coverage.Both}  •  clear {coverage.Clear}");
                AddCompactLine(panel, $"Requests {openRequests}  •  unmet/deferred {unmetRequests}");
                airOverviewGrid.Add(panel);
            }
        }

        private (int TotalLand, int FriendlyOnly, int HostileOnly, int Both, int Clear)
            CalculateAirInterferenceCoverage(Alliance territoryOwner)
        {
            var landTiles = gameManager.tileSystem.LandTiles
                .Where(tile => tile.Controller == territoryOwner)
                .ToList();
            var commander = gameManager.GetAllianceAirTaskingCommander(territoryOwner);
            var friendly = 0;
            var hostile = 0;
            var contested = 0;
            var quiet = 0;
            foreach (var tile in landTiles)
            {
                if (commander == null
                    || !commander.TryGetAirControlAssessment(
                        tile.TileId,
                        out var assessment))
                {
                    quiet++;
                    continue;
                }

                const float meaningfulInterference = 0.25f;
                var friendlyInterference = assessment.FriendlyAirInterference;
                var hostileInterference = assessment.HostileAirInterference;
                if (friendlyInterference >= meaningfulInterference
                    && hostileInterference >= meaningfulInterference)
                    contested++;
                else if (friendlyInterference >= meaningfulInterference)
                    friendly++;
                else if (hostileInterference >= meaningfulInterference)
                    hostile++;
                else
                    quiet++;
            }
            return (
                landTiles.Count,
                friendly,
                hostile,
                contested,
                quiet);
        }

        private void UpdateGroundOperationsUi()
        {
            if (groundCombatsList == null || gameManager == null)
                return;

            var combats = gameManager.GetActiveGroundCombats().ToList();
            if (groundOpsSummary != null)
                groundOpsSummary.text = combats.Count == 0
                    ? "No active ground combats."
                    : $"{combats.Count} active combats. Bubble values show attacker current-power share, not win probability.";
            groundCombatsList.Clear();
            foreach (var combat in combats.OrderBy(item => item.DefendingTileId.x)
                         .ThenBy(item => item.DefendingTileId.y))
            {
                var score = CalculateAttackerCombatScore(combat);
                var button = new Button(() => OpenCombatInspector(combat))
                {
                    text =
                        $"{GetAllianceLabel(combat.AttackingAlliance)} → {GetAllianceLabel(combat.DefendingAlliance)}  •  Tile {FormatTile(combat.DefendingTileId)}\n" +
                        $"Power balance {score}% attacker  •  {combat.AttackerDivisionIds.Count} attacking / {combat.DefenderDivisionIds.Count} defending"
                };
                button.AddToClassList("campaign-air-card");
                button.AddToClassList("campaign-air-card--clickable");
                ApplyRuntimeFont(button);
                groundCombatsList.Add(button);
            }
        }

        private void OpenCombatInspector(GroundCombat combat)
        {
            if (combat == null)
                return;
            var tileId = combat.DefendingTileId;
            CreatePinnedInspector(
                $"Ground Combat {FormatTile(tileId)}",
                () => BuildCombatInspectorLines(tileId));
        }

        private IEnumerable<string> BuildCombatInspectorLines(Vector3Int tileId)
        {
            var combat = gameManager.GetActiveGroundCombats()
                .FirstOrDefault(candidate => candidate.DefendingTileId == tileId);
            if (combat == null)
                return new[] { "COMPLETED", "This ground combat is no longer active." };
            var lines = new List<string>
            {
                $"Defending tile  {FormatTile(tileId)}",
                $"Attacker / defender  {combat.AttackingAlliance} / {combat.DefendingAlliance}",
                $"Current attacker power share  {CalculateAttackerCombatScore(combat)}%",
                "ATTACKERS"
            };
            lines.AddRange(BuildCombatSideLines(combat.AttackerDivisionIds));
            lines.Add("DEFENDERS");
            lines.AddRange(BuildCombatSideLines(combat.DefenderDivisionIds));
            return lines;
        }

        private IEnumerable<string> BuildCombatSideLines(IEnumerable<Guid> divisionIds)
        {
            foreach (var id in divisionIds)
            {
                if (!gameManager.divisionSystem.TryGetDivision(id, out var division) || division == null)
                    continue;
                yield return
                    $"{division.Name}  •  STR {division.Strength:0.#}/{division.MaxStrength}  •  ORG {division.Organization:0.#}/{division.MaxOrganization}  •  SA/HA {division.SoftAttack:0.#}/{division.HardAttack:0.#}  •  DEF {division.Defense}  •  Width {division.CombatWidth}";
            }
        }

        private IEnumerable<string> BuildGroundOverviewLines()
        {
            var combats = gameManager.GetActiveGroundCombats().ToList();
            var lines = new List<string> { $"Active combats  {combats.Count}" };
            lines.AddRange(combats.Select(combat =>
                $"{combat.AttackingAlliance} → {combat.DefendingAlliance} at {FormatTile(combat.DefendingTileId)}  •  attacker power {CalculateAttackerCombatScore(combat)}%"));
            return lines;
        }

        private IEnumerable<string> BuildAirOverviewLines()
        {
            var lines = new List<string>();
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var squadrons = gameManager.squadronSystem.Squadrons
                    .Where(squadron => gameManager.GetCountryAlliance(squadron.CountryId) == alliance)
                    .ToList();
                var coverage = CalculateAirInterferenceCoverage(alliance);
                lines.Add($"{alliance}");
                lines.Add(
                    $"Aircraft {squadrons.Sum(item => item.Aircraft.Count)}  •  Ready {squadrons.Sum(item => item.ReadyAircraft)}  •  Assigned {squadrons.Sum(item => item.AssignedAircraft)}  •  Damaged {squadrons.Sum(item => item.DamagedAircraft)}  •  Lost {squadrons.Sum(item => item.LostAircraft)}");
                lines.Add($"Air interference on own land: friendly only {coverage.FriendlyOnly}/{coverage.TotalLand}, hostile only {coverage.HostileOnly}/{coverage.TotalLand}, both {coverage.Both}, clear {coverage.Clear}");
            }
            return lines;
        }

        private void UpdateTurnReportUi()
        {
            if (turnReportList == null)
                return;
            var hasTurn = gameManager.LastTurnCompletedAt > gameManager.LastTurnStartedAt;
            if (turnReportSummary != null)
                turnReportSummary.text = hasTurn
                    ? $"{gameManager.LastTurnStartedAt:yyyy-MM-dd HH:mm} → {gameManager.LastTurnCompletedAt:yyyy-MM-dd HH:mm}"
                    : "Advance one turn to generate a report.";
            turnReportList.Clear();
            foreach (var line in BuildLastTurnLines())
                AddCompactLine(turnReportList, line);
        }

        private IEnumerable<string> BuildLastTurnLines()
        {
            if (gameManager.LastTurnCompletedAt <= gameManager.LastTurnStartedAt)
                return new[] { "No completed turn yet." };
            var from = gameManager.LastTurnStartedAt;
            var to = gameManager.LastTurnCompletedAt;
            var lines = new List<string>
            {
                $"END STATE  •  {gameManager.GetActiveGroundCombats().Count} ground combats  •  {gameManager.GetAirborneFlights().Count} airborne flights"
            };
            lines.AddRange(gameManager.LastTurnChanges
                .OrderBy(change => change.System)
                .ThenBy(change => change.Summary)
                .Select(change => $"{change.System.ToUpperInvariant()}  {change.Summary}"));
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var commander = gameManager.GetAllianceAirTaskingCommander(alliance);
                foreach (var diagnostic in commander.Diagnostics
                             .Where(item => item.RecordedAt > from && item.RecordedAt <= to)
                             .OrderBy(item => item.RecordedAt))
                    lines.Add($"{diagnostic.RecordedAt:HH:mm:ss}  AIR PLANNING  {alliance}  {diagnostic.Code}  •  {diagnostic.Message}");
                foreach (var flightEvent in commander.Packages
                             .SelectMany(package => package.Flights)
                             .SelectMany(flight => flight.ExecutionEvents.Select(item => (flight, item)))
                             .Where(pair => pair.item.OccurredAt > from && pair.item.OccurredAt <= to)
                             .OrderBy(pair => pair.item.OccurredAt))
                    lines.Add($"{flightEvent.item.OccurredAt:HH:mm:ss}  AIR EXECUTION  {ShortId(flightEvent.flight.FlightId)}  •  {flightEvent.item.Detail}");
            }
            var launchRecords = gameManager.GetOrdnanceEmploymentRecords()
                .Where(item => item.Stage == OrdnanceEmploymentRecordStage.OrdnanceReleased
                               && item.OccurredAt > from
                               && item.OccurredAt <= to)
                .OrderBy(item => item.OccurredAt)
                .ToList();
            lines.Add($"ORDNANCE LAUNCHES  {launchRecords.Sum(item => Math.Max(1, item.Launches.Count))} launches across {launchRecords.Count} passes");
            foreach (var record in launchRecords)
            {
                lines.Add(BuildOrdnancePassLine(record));
                foreach (var launch in GetRecordLaunches(record))
                {
                    var shot = FindResolvedShot(record, launch.Sequence);
                    lines.Add(BuildOrdnanceLaunchLine(record, launch, shot));
                }
            }
            foreach (var record in gameManager.GetOrdnanceEmploymentRecords()
                         .Where(item => item.OccurredAt > from && item.OccurredAt <= to)
                         .OrderBy(item => item.OccurredAt))
            {
                lines.Add($"{record.OccurredAt:HH:mm:ss}  ORDNANCE  PASS {ShortId(record.EmploymentPassId)}  •  {record.Detail}");
                if (record.Stage == OrdnanceEmploymentRecordStage.OrdnanceReleased)
                    lines.Add($"  Range {record.ReleaseRangeKm:0.0} km  •  P(hit) {record.HitProbability:P1}");
                foreach (var shot in record.Shots)
                    lines.Add($"  Shot {shot.Sequence}: {shot.Result}  •  target {ShortId(shot.TargetAircraftId)}  •  P {shot.Probability:P1}  •  roll {(shot.Roll < 0f ? "—" : shot.Roll.ToString("0.000"))}");
            }
            foreach (var division in gameManager.divisionSystem.Divisions
                         .Where(item => item.CurrentOrder is MoveGroundOrder)
                         .OrderBy(item => item.Name))
            {
                var move = (MoveGroundOrder)division.CurrentOrder;
                lines.Add($"GROUND MOVEMENT  {division.Name}  •  {FormatTile(division.TileId)} → {FormatTile(move.CurrentDestinationTileId)}  •  {move.MovementProgress:P1}");
            }
            return lines;
        }

        private void UpdateDiagnosticsUi()
        {
            if (diagnosticsList == null || gameManager == null)
                return;
            var entries = GetDiagnostics().ToList();
            if (diagnosticsErrorsButton != null)
                diagnosticsErrorsButton.text = $"Errors {entries.Count(item => item.Severity == "Errors")}";
            if (diagnosticsWarningsButton != null)
                diagnosticsWarningsButton.text = $"Warnings {entries.Count(item => item.Severity == "Warnings")}";

            var severity = diagnosticsSeverity?.value ?? "All";
            var system = diagnosticsSystem?.value ?? "All";
            var search = diagnosticsSearch?.value?.Trim() ?? string.Empty;
            var filtered = entries.Where(entry =>
                (severity == "All" || entry.Severity == severity)
                && (system == "All" || entry.System == system)
                && (search.Length == 0
                    || entry.Text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0));
            diagnosticsList.Clear();
            foreach (var entry in filtered.OrderByDescending(item => item.RecordedAt))
                AddCompactLine(
                    diagnosticsList,
                    $"{entry.RecordedAt:MM-dd HH:mm:ss}  {entry.Severity.ToUpperInvariant()}  {entry.System}\n{entry.Text}");
        }

        private IEnumerable<string> BuildDiagnosticLines()
        {
            return GetDiagnostics()
                .OrderByDescending(entry => entry.RecordedAt)
                .Select(entry => $"{entry.RecordedAt:MM-dd HH:mm:ss}  {entry.Severity}  {entry.System}  •  {entry.Text}");
        }

        private IEnumerable<DiagnosticRow> GetDiagnostics()
        {
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var commander = gameManager.GetAllianceAirTaskingCommander(alliance);
                foreach (var diagnostic in commander.Diagnostics)
                {
                    var severity = diagnostic.Code.Contains("failed")
                                   || diagnostic.Code.Contains("invalid")
                                   || diagnostic.Code.Contains("aborted")
                        ? "Errors"
                        : diagnostic.Code.Contains("deferred")
                          || diagnostic.Code.Contains("cancel")
                          || diagnostic.Code.Contains("purged")
                            ? "Warnings"
                            : "Info";
                    yield return new DiagnosticRow(
                        diagnostic.RecordedAt,
                        severity,
                        "Air Planning",
                        $"{alliance}  {diagnostic.Code}  •  {diagnostic.Message}");
                }
            }
            foreach (var record in gameManager.GetOrdnanceEmploymentRecords()
                         .Where(item => item.Stage == OrdnanceEmploymentRecordStage.PreparationAborted))
                yield return new DiagnosticRow(record.OccurredAt, "Warnings", "Ordnance", record.Detail);
        }

        private void AddCompactLine(VisualElement parent, string text, bool heading = false)
        {
            var label = new Label(text);
            label.AddToClassList(heading ? "campaign-air-card-title" : "flight-detail-line");
            ApplyRuntimeFont(label);
            parent.Add(label);
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
                    new AirCardField(
                        "Mission area",
                        $"Hex {FormatTile(request.MissionArea.CenterTileId)} / "
                        + $"{request.MissionArea.RadiusKm:0.#} km radius"),
                    new AirCardField("Effect window", $"{request.EffectStart:MM-dd HH:mm} – {request.EffectEnd:MM-dd HH:mm}"),
                    new AirCardField(
                        "Demand",
                        $"{request.DesiredAircraftStrength} aircraft" +
                        (request.DesiredSupportSlots > 0
                            ? $" / {request.DesiredSupportSlots} support slots"
                            : string.Empty))
                };
                if (request.DeadPlan != null)
                {
                    fields.Add(new AirCardField(
                        "SAM target",
                        $"{ShortId(request.DeadPlan.TargetSiteId)} / "
                        + $"{request.DeadPlan.TargetComponentIds.Count} known components"));
                }
                if (!string.IsNullOrWhiteSpace(request.Rationale))
                    fields.Add(new AirCardField("Intent", request.Rationale));
                airRequestsList.Add(CreateAirCard(
                    request.Alliance,
                    title,
                    fields,
                    () => OpenRequestInspector(request.MissionRequestId, request.Alliance)));
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
                    .First(commander => commander.Alliance == package.Alliance)
                    .MissionRequests
                    .First(candidate => candidate.MissionRequestId == package.MissionRequestId);
                var aircraftCount = package.Flights
                    .Sum(flight => flight.AircraftIds.Count);
                var title =
                    $"{GetAllianceLabel(package.Alliance)} PKG {ShortId(package.PackageId)}  •  {package.LifecycleState}";
                var fields = new List<AirCardField>
                {
                    new AirCardField(
                        "Mission",
                        GetMissionLabel(request.RequestType)),
                    new AirCardField("Composition", $"{package.Flights.Count} flights / {aircraftCount} aircraft"),
                    new AirCardField("Earliest launch", package.EarliestTakeoffTime.ToString("MM-dd HH:mm")),
                    (request.RequestType == AirMissionRequestType.OffensiveCounterAirSweep
                     || request.RequestType
                     == AirMissionRequestType.DestructionOfEnemyAirDefenses)
                        ? new AirCardField("Sweep pass", $"{package.EffectStart:MM-dd HH:mm} – {package.EffectEnd:MM-dd HH:mm}")
                        : request.FulfillmentPattern == AirMissionRequestFulfillmentPattern.Discrete
                            ? new AirCardField("Effect time", package.EffectStart.ToString("MM-dd HH:mm"))
                            : new AirCardField("Effect window", $"{package.EffectStart:MM-dd HH:mm} – {package.EffectEnd:MM-dd HH:mm}"),
                    new AirCardField("Source request", ShortId(package.MissionRequestId))
                };
                var rendezvous = package.RendezvousWaypoint;
                if (rendezvous != null)
                {
                    fields.Add(new AirCardField(
                        "Rendezvous",
                        $"X {rendezvous.PositionFeet.x:0} / Z {rendezvous.PositionFeet.z:0} / "
                        + $"ALT {rendezvous.PositionFeet.y:0} ft"));
                }
                if (!string.IsNullOrWhiteSpace(package.Rationale))
                    fields.Add(new AirCardField("Intent", package.Rationale));
                airPackagesList.Add(CreateAirCard(
                    package.Alliance,
                    title,
                    fields,
                    () => OpenPackageInspector(package.PackageId, package.Alliance)));
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
                var package = GetOwningPackage(flight, packages);
                var alliance = package.Alliance;
                var squadron = gameManager.squadronSystem.Squadrons
                    .First(candidate => candidate.SquadronId == flight.SquadronId);
                var nextWaypoint = flight.CurrentWaypointIndex >= 0
                                   && flight.CurrentWaypointIndex < flight.Route.Count
                    ? flight.Route[flight.CurrentWaypointIndex]
                    : null;
                var altitude = flight.HasPosition ? $"{flight.PositionFeet.y:0} ft" : "not airborne";
                var title =
                    $"{GetAllianceLabel(alliance)} {GetFlightName(flight, squadron)}  •  {flight.ExecutionPhase}";
                var fields = new List<AirCardField>
                {
                    new AirCardField("Mission", GetMissionLabel(flight.MissionType)),
                    new AirCardField("Aircraft", flight.AircraftIds.Count.ToString()),
                    new AirCardField("Execution", $"{flight.LifecycleState} / {flight.ExecutionPhase}"),
                    new AirCardField(
                        "Position",
                        altitude + (flight.HasPosition ? $" / heading {flight.HeadingDegrees:0}°" : string.Empty)),
                    new AirCardField(
                        "Next action",
                        nextWaypoint == null ? "—" : GetWaypointLabel(nextWaypoint.Action)),
                    new AirCardField("Package", ShortId(package.PackageId))
                };
                airFlightsList.Add(CreateAirCard(
                    alliance,
                    title,
                    fields,
                    () => OpenFlightDetails(flight.FlightId),
                    () => InspectFlightRoute(flight.FlightId)));
            }
        }

        private void RebuildAirPassesList(IReadOnlyList<OrdnanceEmploymentRecord> records)
        {
            if (airPassesList == null)
                return;

            airPassesList.Clear();
            if (records.Count == 0)
            {
                airPassesList.Add(CreateAirEmptyLabel("No ordnance releases in the last completed turn."));
                return;
            }

            foreach (var record in records)
            {
                var shots = GetResolvedShots(record).ToList();
                var hits = shots.Count(shot => shot.Result == OrdnanceShotResult.Hit);
                var damaged = shots.Count(shot => shot.Result == OrdnanceShotResult.Damaged);
                var misses = shots.Count(shot => shot.Result == OrdnanceShotResult.Miss);
                var defeated = shots.Count(shot => shot.Result == OrdnanceShotResult.Defeated);
                var ineffective = shots.Count(shot => shot.Result == OrdnanceShotResult.Ineffective);
                var launches = GetRecordLaunches(record).ToList();
                var title =
                    $"{record.OccurredAt:HH:mm:ss} PASS {ShortId(record.EmploymentPassId)}  •  " +
                    $"{GetSourceLabel(record)} → {GetOrdnanceTargetLabel(record)}";
                var fields = new List<AirCardField>
                {
                    new AirCardField("Ordnance", $"{record.Quantity}× {GetOrdnanceName(record.OrdnanceTypeDefinitionId)}"),
                    new AirCardField("Launches", $"{launches.Count} launches / {shots.Count} resolved shots"),
                    new AirCardField("Outcome", $"Destroyed {hits} / Damaged {damaged} / Miss {misses} / Defeated {defeated} / Ineffective {ineffective}"),
                    new AirCardField("Snapshot", $"Range {record.ReleaseRangeKm:0.0} km / P(hit) {record.HitProbability:P1}"),
                    new AirCardField("Source", GetSourceLabel(record)),
                    new AirCardField("Target", GetOrdnanceTargetLabel(record))
                };
                airPassesList.Add(CreateAirCard(
                    GetRecordAlliance(record),
                    title,
                    fields,
                    () => OpenOrdnancePassInspector(record.EmploymentPassId)));
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

            foreach (var field in fields)
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

            card.style.minHeight = 48f + Math.Max(1, fields.Count) * 24f;
            return card;
        }

        private void OpenRequestInspector(Guid requestId, Alliance alliance)
        {
            var commander = gameManager.GetAllianceAirTaskingCommander(alliance);
            var request = commander?.MissionRequests
                .FirstOrDefault(candidate => candidate.MissionRequestId == requestId);
            if (request == null)
                return;
            CreatePinnedInspector(
                $"Request {ShortId(requestId)}",
                () =>
                {
                    var current = gameManager.GetAllianceAirTaskingCommander(alliance)?.MissionRequests
                        .FirstOrDefault(candidate => candidate.MissionRequestId == requestId);
                    if (current == null)
                        return new[] { "STALE", "Mission request is no longer active." };
                    var lines = new List<string>
                    {
                        $"Request ID  {current.MissionRequestId:N}",
                        $"Alliance  {current.Alliance}",
                        $"Type  {GetMissionLabel(current.RequestType)}",
                        $"State  {current.State}",
                        $"Priority  {current.Priority:0.0}",
                        $"Area  {FormatTile(current.MissionArea.CenterTileId)} radius {current.MissionArea.RadiusKm:0.#} km",
                        $"Effect  {current.EffectStart:yyyy-MM-dd HH:mm} → {current.EffectEnd:yyyy-MM-dd HH:mm}",
                        $"Demand  {current.DesiredAircraftStrength} aircraft / {current.DesiredSupportSlots} support slots",
                        $"Rationale  {current.Rationale}"
                    };
                    lines.AddRange(commander.Diagnostics
                        .Where(item => item.MissionRequestId == requestId)
                        .OrderByDescending(item => item.RecordedAt)
                        .Take(20)
                        .Select(item => $"{item.RecordedAt:MM-dd HH:mm}  {item.Code}  •  {item.Message}"));
                    return lines;
                },
                () => FocusTile(request.MissionArea.CenterTileId));
        }

        private void OpenPackageInspector(Guid packageId, Alliance alliance)
        {
            if (!TryFindPackage(packageId, alliance, out _))
                return;
            CreatePinnedInspector(
                $"Package {ShortId(packageId)}",
                () => BuildPackageInspectorLines(packageId, alliance),
                () => FocusPackageOnMap(packageId, alliance));
        }

        private IEnumerable<string> BuildPackageInspectorLines(Guid packageId, Alliance alliance)
        {
            if (!TryFindPackage(packageId, alliance, out var package))
                return new[] { "STALE", "Package is no longer active." };
            var lines = new List<string>
            {
                $"Package ID  {package.PackageId:N}",
                $"Alliance  {package.Alliance}",
                $"Lifecycle  {package.LifecycleState}",
                $"Request  {package.MissionRequestId:N}",
                $"Created  {package.CreatedAt:yyyy-MM-dd HH:mm}",
                $"Effect  {package.EffectStart:yyyy-MM-dd HH:mm} → {package.EffectEnd:yyyy-MM-dd HH:mm}",
                $"Flights  {package.Flights.Count}",
                $"Aircraft  {package.Flights.Sum(flight => flight.AircraftIds.Count)}",
                $"Rationale  {package.Rationale}"
            };
            foreach (var flight in package.Flights)
                lines.Add(
                    $"FLIGHT  {ShortId(flight.FlightId)}  •  {GetMissionLabel(flight.MissionType)}  •  {flight.AircraftIds.Count}-ship  •  {flight.ExecutionPhase}");
            return lines;
        }

        private bool TryFindPackage(Guid packageId, Alliance alliance, out AirPackage package)
        {
            package = gameManager.GetAllianceAirTaskingCommander(alliance)?.Packages
                .FirstOrDefault(candidate => candidate.PackageId == packageId);
            return package != null;
        }

        private void FocusPackageOnMap(Guid packageId, Alliance alliance)
        {
            if (!TryFindPackage(packageId, alliance, out var package))
                return;
            inspectedFlightId = Guid.Empty;
            inspectedPackageId = packageId;
            ClearAirInspection();
            CreateAirInspection();
            var points = package.Flights
                .SelectMany(flight => flight.Route.Select(waypoint =>
                    AirPositionToMapPosition(waypoint.PositionFeet)))
                .ToList();
            if (points.Count == 0)
                return;
            FrameMapPoints(points);
        }

        private void OpenFlightDetails(Guid flightId)
        {
            selectedFlightId = flightId;
            RefreshAirOverlaysForSelection();
            if (flightDetailBackdrop != null)
            {
                flightDetailBackdrop.style.display = DisplayStyle.Flex;
                flightDetailBackdrop.BringToFront();
            }
            RefreshFlightDetails();
        }

        private void RefreshAirOverlaysForSelection()
        {
            ClearAirOverlays();
            flightPickTargets.Clear();
            CreateAirOverlays();
            SetAirRouteVisibility(overlayRoutesToggle == null || overlayRoutesToggle.value);
            ApplySelectedFlightMarker();
        }

        private void ApplySelectedFlightMarker()
        {
            if (airOverlayRoot == null)
                return;
            foreach (Transform child in airOverlayRoot)
            {
                if (!child.name.StartsWith("Air Flight ", StringComparison.Ordinal))
                    continue;
                var line = child.GetComponent<LineRenderer>();
                if (line == null)
                    continue;
                var selected = selectedFlightId != Guid.Empty
                               && child.name.EndsWith(ShortId(selectedFlightId), StringComparison.Ordinal);
                var sourceHighlighted = highlightedOrdnanceSourceFlightId != Guid.Empty
                                        && child.name.EndsWith(ShortId(highlightedOrdnanceSourceFlightId), StringComparison.Ordinal);
                var targetHighlighted = highlightedOrdnanceTargetFlightId != Guid.Empty
                                        && child.name.EndsWith(ShortId(highlightedOrdnanceTargetFlightId), StringComparison.Ordinal);
                line.startWidth = selected || sourceHighlighted || targetHighlighted ? 0.085f : 0.055f;
                line.endWidth = selected || sourceHighlighted || targetHighlighted ? 0.085f : 0.055f;
                if (selected)
                {
                    line.startColor = new Color(1f, 0.88f, 0.22f);
                    line.endColor = new Color(1f, 0.88f, 0.22f);
                }
                else if (sourceHighlighted)
                {
                    line.startColor = new Color(0.25f, 1f, 0.72f);
                    line.endColor = new Color(0.25f, 1f, 0.72f);
                }
                else if (targetHighlighted)
                {
                    line.startColor = new Color(1f, 0.33f, 0.25f);
                    line.endColor = new Color(1f, 0.33f, 0.25f);
                }
                else
                {
                    var pick = flightPickTargets.FirstOrDefault(target =>
                        child.name.EndsWith(ShortId(target.FlightId), StringComparison.Ordinal));
                    if (pick.FlightId != Guid.Empty
                        && TryFindFlight(pick.FlightId, out _, out var package, out _))
                    {
                        line.startColor = GetAirAllianceColor(package.Alliance);
                        line.endColor = GetAirAllianceColor(package.Alliance);
                    }
                }
            }
        }

        private void CloseFlightDetails()
        {
            selectedFlightId = Guid.Empty;
            RefreshAirOverlaysForSelection();
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

            if (!flight.HasPosition && flight.Route.Count == 0)
            {
                SetAirInspectionStatus("This flight has no position or planned route to display.");
                return;
            }

            inspectedFlightId = flightId;
            inspectedPackageId = Guid.Empty;
            ClearAirInspection();
            CreateAirInspection();
            FrameAirInspection(flight);

            var squadron = gameManager.squadronSystem.Squadrons
                .First(candidate => candidate.SquadronId == flight.SquadronId);
            SetAirInspectionStatus(
                $"Inspecting {GetFlightName(flight, squadron)}. Route highlight clears on the next game turn.");
        }

        private void SetAirInspectionStatus(string message)
        {
            if (airInspectionStatus == null)
                return;

            airInspectionStatus.text = message;
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

            var alliance = package.Alliance;
            var squadron = gameManager.squadronSystem.Squadrons
                .First(candidate => candidate.SquadronId == flight.SquadronId);
            var nextWaypoint = flight.CurrentWaypointIndex >= 0
                               && flight.CurrentWaypointIndex < flight.Route.Count
                ? flight.Route[flight.CurrentWaypointIndex]
                : null;

            flightDetailTitle.text = GetFlightName(flight, squadron);
            flightDetailSubtitle.text =
                $"{GetAllianceLabel(alliance)}  •  {GetMissionLabel(flight.MissionType)}  •  " +
                $"{flight.ExecutionPhase}";
            var previousScrollOffset = flightDetailScroll.scrollOffset;
            flightDetailContent.Clear();

            AddFlightDetailSection(
                "IDENTITY & TASKING",
                $"Flight ID: {flight.FlightId:N}",
                $"Squadron: {squadron.Name}",
                $"Package: {package.PackageId:N}",
                $"Mission: {GetMissionLabel(flight.MissionType)}",
                $"Role in package: {(flight.IsRequired ? "Required" : "Supporting")}",
                $"Assigned aircraft: {flight.AircraftIds.Count}");

            AddFlightDetailSection(
                "EXECUTION STATE",
                $"Lifecycle: {flight.LifecycleState}",
                $"Phase: {flight.ExecutionPhase}",
                $"Mission achieved: {(flight.MissionAchieved ? "Yes" : "No")}",
                $"Rendezvous hold: {(flight.IsWaitingAtRendezvous ? "Waiting" : "No")}",
                $"Route progress: {Mathf.Clamp(flight.CurrentWaypointIndex + 1, 0, flight.Route.Count)} of {flight.Route.Count}",
                $"Next action: {(nextWaypoint == null ? "None" : GetWaypointLabel(nextWaypoint.Action))}");

            var tactical = flight.TacticalState;
            AddFlightDetailSection(
                "AIR COMBAT DECISION",
                $"Intent: {tactical.Intent}",
                $"Maneuver: {tactical.Maneuver}",
                tactical.TargetFlightId == Guid.Empty
                    ? "Target: None"
                    : $"Target: FLT {ShortId(tactical.TargetFlightId)}",
                $"Decision: {tactical.DecisionReason}",
                $"Intent since: {tactical.IntentStartedAt:HH:mm:ss}",
                $"Committed through: {tactical.MinimumManeuverEndAt:HH:mm:ss}",
                $"Fuel remaining: {tactical.FuelFraction:P0}",
                $"Recommits: {tactical.RecommitCount}");

            AddWvrCombatSection(flight, squadron);

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
                || flight.MissionType == AirMissionRequestType.OffensiveCounterAirSweep
                || flight.MissionType
                == AirMissionRequestType.DestructionOfEnemyAirDefenses
                    ? $"Effect window: {flight.EffectStart:yyyy-MM-dd HH:mm} – {flight.EffectEnd:yyyy-MM-dd HH:mm}"
                    : $"Effect time: {flight.EffectStart:yyyy-MM-dd HH:mm}",
                $"Mission area: Hex {FormatTile(flight.MissionArea.CenterTileId)}");

            AddAircraftDetailSection(flight, squadron);
            AddRouteDetailSection(flight);
            AddSupportDetailSection(flight, package);
            AddAerialRefuelingDetailSection(flight);
            AddExecutionEventSection(flight);
            AddOrdnanceEmploymentSection(flight);
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

                foreach (var candidatePackage in candidateCommander.Packages)
                {
                    var candidateFlight = candidatePackage.Flights
                        .FirstOrDefault(item => item.FlightId == flightId);
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
            if (squadron == null)
            {
                throw new InvalidOperationException(
                    $"Flight {flight.FlightId} references a missing squadron.");
            }

            var section = CreateFlightDetailSection("ASSIGNED AIRCRAFT");
            var aircraftById = squadron.Aircraft
                .ToDictionary(aircraft => aircraft.AircraftId);
            var aircraftIds = flight.AircraftIds;
            if (aircraftIds.Count == 0)
            {
                AddFlightDetailMessage(section, "No aircraft assigned.");
            }
            else
            {
                var detailLineCount = 0;
                for (var index = 0; index < aircraftIds.Count; index++)
                {
                    var aircraft = aircraftById[aircraftIds[index]];
                    AddFlightDetailMessage(
                        section,
                        $"{index + 1}. Aircraft {ShortId(aircraftIds[index])}  •  " +
                        aircraft.Status);
                    var ordnanceLines =
                        BuildAircraftOrdnanceLines(aircraft, 12).ToList();
                    foreach (var line in ordnanceLines)
                        AddFlightDetailMessage(section, $"    {line}");
                    detailLineCount += 1 + ordnanceLines.Count;
                }

                section.style.minHeight =
                    43f + Math.Max(1, detailLineCount) * 23f;
            }

            if (aircraftIds.Count == 0)
                section.style.minHeight = 66f;
            flightDetailContent.Add(section);
        }

        private IEnumerable<string> BuildAircraftOrdnanceLines(
            CampaignAircraft aircraft,
            int maximumSpentLaunches)
        {
            var lines = new List<string> { "CURRENT ORDNANCE" };
            var currentLoadout = aircraft.Loadout
                .Where(item => item.Count > 0)
                .OrderBy(item => GetOrdnanceName(
                    item.OrdnanceTypeDefinitionId))
                .ToList();
            if (currentLoadout.Count == 0)
            {
                lines.Add("  None");
            }
            else
            {
                lines.AddRange(currentLoadout.Select(item =>
                    $"  {GetOrdnanceName(item.OrdnanceTypeDefinitionId)} ×{item.Count}"));
            }

            var spentLaunches = gameManager.GetOrdnanceEmploymentRecords()
                .Where(record =>
                    record.Stage
                    == OrdnanceEmploymentRecordStage.OrdnanceReleased
                    && record.SourceKind
                    == OrdnanceEmploymentSourceKind.AircraftFlight)
                .SelectMany(record => GetRecordLaunches(record)
                    .Where(launch =>
                        launch.SourceAircraftId == aircraft.AircraftId)
                    .Select(launch => new
                    {
                        Record = record,
                        Launch = launch
                    }))
                .OrderByDescending(entry => entry.Record.OccurredAt)
                .ThenByDescending(entry => entry.Launch.Sequence)
                .Take(Math.Max(0, maximumSpentLaunches))
                .ToList();

            lines.Add("SPENT ORDNANCE");
            if (spentLaunches.Count == 0)
            {
                lines.Add("  None recorded");
                return lines;
            }

            foreach (var entry in spentLaunches)
            {
                var shot = FindResolvedShot(
                    entry.Record,
                    entry.Launch.Sequence);
                var result = shot == null ? "Pending" : shot.Result.ToString();
                lines.Add(
                    $"  {entry.Record.OccurredAt:MM-dd HH:mm:ss}  " +
                    $"{GetOrdnanceName(entry.Launch.OrdnanceTypeDefinitionId)}  " +
                    $"→ {GetAircraftOrdnanceTargetLabel(entry.Record, entry.Launch)}  " +
                    $"• {result}");
            }

            return lines;
        }

        private string GetAircraftOrdnanceTargetLabel(
            OrdnanceEmploymentRecord record,
            OrdnanceLaunchDiagnostic launch)
        {
            if (record.TargetKind
                == OrdnanceEmploymentTargetKind.AirDefenseComponent)
            {
                var componentName = GetSamComponentDisplayName(
                    record.TargetSiteId,
                    record.TargetComponentId);
                return $"SAM {ShortId(record.TargetSiteId)} / {componentName}";
            }

            return launch.TargetAircraftId == Guid.Empty
                ? GetFlightLabel(record.TargetFlightId)
                : $"aircraft {ShortId(launch.TargetAircraftId)}";
        }

        private void AddRouteDetailSection(AirFlight flight)
        {
            var section = CreateFlightDetailSection($"ROUTE ({flight.Route.Count} WAYPOINTS)");
            var route = flight.Route;
            if (route.Count == 0)
            {
                AddFlightDetailMessage(section, "No route was planned.");
            }
            else
            {
                for (var index = 0; index < route.Count; index++)
                {
                    var waypoint = route[index];
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

        private void AddSupportDetailSection(
            AirFlight flight,
            AirPackage package)
        {
            var reservations = flight.SupportReservations;
            var supportingFlightIds = package?.SupportingFlightIds
                                      ?? new List<Guid>();
            if (reservations.Count == 0
                && flight.ProvidedSupportSlots <= 0
                && supportingFlightIds.Count == 0)
                return;

            var section = CreateFlightDetailSection("SUPPORT COMMITMENTS");
            if (flight.ProvidedSupportSlots > 0)
            {
                AddFlightDetailMessage(
                    section,
                    $"Support capacity: {flight.ProvidedSupportSlots} slots");
            }
            foreach (var supportingFlightId in supportingFlightIds)
            {
                AddFlightDetailMessage(
                    section,
                    $"Reserved tanker: FLT {ShortId(supportingFlightId)}");
            }
            foreach (var reservation in reservations)
            {
                AddFlightDetailMessage(
                    section,
                    $"{reservation.SlotCount} slots for PKG {ShortId(reservation.ConsumingPackageId)}  •  " +
                    $"{reservation.StartTime:MM-dd HH:mm} – {reservation.EndTime:HH:mm}");
            }
            section.style.minHeight =
                66f + (reservations.Count + supportingFlightIds.Count) * 23f;
            flightDetailContent.Add(section);
        }

        private void AddAerialRefuelingDetailSection(AirFlight flight)
        {
            var received = flight.AerialRefuelingRecords
                .OrderByDescending(record => record.OccurredAt)
                .ToList();
            var provided = new List<AerialRefuelingRecord>();
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var commander = gameManager.GetAllianceAirTaskingCommander(alliance);
                if (commander == null)
                    continue;

                provided.AddRange(commander.Packages
                    .SelectMany(package => package.Flights)
                    .SelectMany(receiver => receiver.AerialRefuelingRecords)
                    .Where(record => record.TankerFlightId == flight.FlightId));
            }

            provided = provided
                .OrderByDescending(record => record.OccurredAt)
                .ToList();
            if (received.Count == 0 && provided.Count == 0)
                return;

            var section = CreateFlightDetailSection("AERIAL REFUELING");
            if (received.Count > 0)
            {
                AddFlightDetailMessage(
                    section,
                    $"Transfers received: {received.Count}");
                foreach (var record in received.Take(12))
                {
                    AddFlightDetailMessage(
                        section,
                        $"{record.OccurredAt:MM-dd HH:mm} from FLT "
                        + $"{ShortId(record.TankerFlightId)} - "
                        + $"{record.FuelBefore:P0} to {record.FuelAfter:P0}");
                }
            }

            if (provided.Count > 0)
            {
                AddFlightDetailMessage(
                    section,
                    $"Transfers provided: {provided.Count}");
                foreach (var record in provided.Take(12))
                {
                    AddFlightDetailMessage(
                        section,
                        $"{record.OccurredAt:MM-dd HH:mm} to FLT "
                        + $"{ShortId(record.ReceiverFlightId)} - "
                        + $"{record.FuelBefore:P0} to {record.FuelAfter:P0}");
                }
            }

            section.style.minHeight =
                66f + Math.Min(24, received.Count + provided.Count) * 23f;
            flightDetailContent.Add(section);
        }

        private void AddExecutionEventSection(AirFlight flight)
        {
            var section = CreateFlightDetailSection($"EXECUTION LOG ({flight.ExecutionEvents.Count})");
            var events = flight.ExecutionEvents
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

        private void AddOrdnanceEmploymentSection(AirFlight flight)
        {
            var records = gameManager.GetOrdnanceEmploymentRecords()
                .Where(record => record.SourceFlightId == flight.FlightId
                                 || record.TargetFlightId == flight.FlightId)
                .OrderByDescending(record => record.OccurredAt)
                .Take(50)
                .ToList();
            var activePasses = gameManager.GetActiveOrdnanceEmploymentPasses()
                .Where(pass => pass.SourceFlightId == flight.FlightId)
                .OrderBy(pass => pass.ReleaseAt)
                .ToList();
            var pendingEffects = gameManager.GetPendingOrdnanceEffects()
                .Where(effect => effect.SourceFlightId == flight.FlightId
                                 || effect.TargetFlightId == flight.FlightId)
                .OrderBy(effect => effect.ResolveAt)
                .ToList();
            if (records.Count == 0
                && activePasses.Count == 0
                && pendingEffects.Count == 0)
                return;

            var section = CreateFlightDetailSection(
                $"ORDNANCE EMPLOYMENT ({records.Count})");
            foreach (var pass in activePasses)
            {
                AddFlightDetailMessage(
                    section,
                    $"PREPARING  •  {pass.PlannedQuantity} stores  •  " +
                    $"release {pass.ReleaseAt:MM-dd HH:mm:ss}");
            }
            foreach (var effect in pendingEffects)
            {
                var direction = effect.TargetFlightId == flight.FlightId
                    ? "INCOMING"
                    : "OUTBOUND";
                AddFlightDetailMessage(
                    section,
                    $"{direction}  •  {effect.Quantity} stores  •  " +
                    $"resolve {effect.ResolveAt:MM-dd HH:mm:ss}");
            }
            foreach (var record in records)
            {
                AddFlightDetailMessage(
                    section,
                    $"{record.OccurredAt:MM-dd HH:mm:ss}  •  " +
                    $"{GetEmploymentStageLabel(record.Stage)}\n{record.Detail}");
                if (record.Stage == OrdnanceEmploymentRecordStage.OrdnanceReleased)
                {
                    AddFlightDetailButton(
                        section,
                        $"Pass {ShortId(record.EmploymentPassId)}  •  " +
                        $"Range {record.ReleaseRangeKm:0.0} km  •  " +
                        $"P(hit) {record.HitProbability:P1}\nClick to inspect pass and highlight ordnance visuals.",
                        () => OpenOrdnancePassInspector(record.EmploymentPassId));
                    foreach (var launch in GetRecordLaunches(record))
                    {
                        var shot = FindResolvedShot(record, launch.Sequence);
                        AddFlightDetailMessage(
                            section,
                            BuildOrdnanceLaunchLine(record, launch, shot));
                    }
                }
                foreach (var shot in record.Shots)
                {
                    var shotTarget = record.TargetKind
                                     == OrdnanceEmploymentTargetKind
                                         .AirDefenseComponent
                        ? $"SAM component {ShortId(record.TargetComponentId)}"
                        : $"aircraft {ShortId(shot.TargetAircraftId)}";
                    AddFlightDetailMessage(
                        section,
                        $"Shot {shot.Sequence}  •  {shot.Result}  •  " +
                        $"target {shotTarget}  •  " +
                        $"P {shot.Probability:P1}  •  " +
                        $"roll {(shot.Roll < 0f ? "—" : shot.Roll.ToString("0.000"))}");
                }
            }

            section.style.minHeight = 43f
                                      + Math.Max(
                                          1,
                                          records.Count
                                          + activePasses.Count
                                          + pendingEffects.Count
                                          + records.Sum(record => record.Launches?.Count ?? 0)
                                          + records.Sum(record => record.Shots.Count)) * 45f;
            flightDetailContent.Add(section);
        }

        private void AddWvrCombatSection(
            AirFlight flight,
            Squadron squadron)
        {
            var active = gameManager.IsFlightInWvrEngagement(
                flight.FlightId);
            if (!gameManager.TryGetLatestWvrRound(
                    flight.FlightId,
                    out var round))
            {
                if (active)
                {
                    AddFlightDetailSection(
                        "WVR COMBAT - LAST ROUND",
                        "Status: ACTIVE - first round is pending.");
                }
                return;
            }

            var section = CreateFlightDetailSection(
                "WVR COMBAT - LAST ROUND");
            AddFlightDetailMessage(
                section,
                $"Status: {(active ? "ACTIVE" : "ENDED")}  |  "
                + $"Round {round.RoundNumber} at "
                + $"{round.ResolvedAt:MM-dd HH:mm:ss}  |  "
                + $"Engagement {ShortId(round.EngagementId)}");
            AddFlightDetailMessage(
                section,
                "Starting advantage: "
                + FormatWvrAdvantage(
                    round.StartingAdvantageAlliance,
                    round.StartingAdvantageLevel)
                + "  |  Ending advantage: "
                + FormatWvrAdvantage(
                    round.EndingAdvantageAlliance,
                    round.EndingAdvantageLevel));
            AddFlightDetailMessage(
                section,
                "BLUE flights: "
                + FormatFlightIds(round.BlueFlightIds)
                + "\nRED flights: "
                + FormatFlightIds(round.RedFlightIds));
            AddFlightDetailMessage(
                section,
                "Round input snapshot after disengagement and before attacks"
                + $"\nBLUE {round.BlueAircraftCount} aircraft "
                + $"({round.BlueDamagedAircraftCount} damaged) / "
                + $"weight {round.BlueEffectiveCombatWeight:0.00} / "
                + $"rating {round.BlueEffectiveWvrRating:0.000}  |  "
                + $"RED {round.RedAircraftCount} aircraft "
                + $"({round.RedDamagedAircraftCount} damaged) / "
                + $"weight {round.RedEffectiveCombatWeight:0.00} / "
                + $"rating {round.RedEffectiveWvrRating:0.000}");
            AddCurrentWvrState(
                section,
                flight,
                squadron,
                active);
            if (round.UsedControlContest)
            {
                var controlMargin = Math.Abs(
                    round.BlueControlScore - round.RedControlScore);
                AddFlightDetailMessage(
                    section,
                    $"Control scores: BLUE {round.BlueControlScore:0.000} / "
                    + $"RED {round.RedControlScore:0.000} / "
                    + $"margin {controlMargin:0.000}");
            }
            else
            {
                AddFlightDetailMessage(
                    section,
                    "Control scores: Not rolled; an opening or retained "
                    + "advantage forced the opportunity.");
            }
            AddFlightDetailMessage(
                section,
                $"Opportunity: {round.OpportunityReason}");

            if (round.Disengagements.Count == 0)
            {
                AddFlightDetailMessage(
                    section,
                    "Disengagement attempts: None required this round.");
            }
            foreach (var attempt in round.Disengagements)
            {
                var ratingContribution = 0.2f
                                         * (attempt.EffectiveWvrRating
                                            - attempt.EnemyAverageWvrRating);
                var speedContribution = 0.2f
                                        * (attempt.SpeedRatio - 1f);
                AddFlightDetailMessage(
                    section,
                    $"Disengage {GetFlightLabel(attempt.FlightId)}"
                    + (attempt.Damaged ? " [DAMAGED]" : string.Empty)
                    + $"  |  P {attempt.Probability:P1}  |  "
                    + $"roll {attempt.Roll:0.000}  |  "
                    + (attempt.Succeeded ? "SUCCESS" : "FAILED")
                    + "\n"
                    + $"Base 30.0% / rating {ratingContribution:+0.0%;-0.0%;0.0%} "
                    + $"({attempt.EffectiveWvrRating:0.000} vs "
                    + $"{attempt.EnemyAverageWvrRating:0.000}) / "
                    + $"speed {speedContribution:+0.0%;-0.0%;0.0%} "
                    + $"({attempt.SpeedRatio:P0}) / "
                    + $"cover {attempt.CoverBonus:+0.0%;-0.0%;0.0%} "
                    + $"({attempt.CoveringFlightCount}) / "
                    + $"outside pressure "
                    + $"{attempt.ExternalPressureBonus:+0.0%;-0.0%;0.0%} / "
                    + $"advantage "
                    + $"{attempt.AdvantageModifier:+0.0%;-0.0%;0.0%}");
            }

            if (round.Attacks.Count == 0)
                AddFlightDetailMessage(section, "Attacks: None.");
            var damageAppliedAfterRolls = false;
            foreach (var attack in round.Attacks)
            {
                var resolution = FindWvrShot(round, attack);
                damageAppliedAfterRolls |= resolution?.Result
                                           == OrdnanceShotResult.Damaged;
                var resolutionText = !attack.Released
                    ? string.Empty
                    : resolution == null
                        ? "\nResolution: Not available in retained ordnance records."
                        : $"\nResolution: {resolution.Result}  |  "
                          + $"roll {resolution.Roll:0.000}  |  "
                          + $"target aircraft "
                          + $"{ShortId(resolution.TargetAircraftId)}"
                          + (resolution.Result == OrdnanceShotResult.Hit
                             && resolution.TargetWasAlreadyDamaged
                              ? "  |  prior damage made this hit fatal"
                              : string.Empty);
                AddFlightDetailMessage(
                    section,
                    $"Attack {GetFlightLabel(attack.SourceFlightId)} -> "
                    + $"{GetFlightLabel(attack.TargetFlightId)}  |  "
                    + $"{GetOrdnanceName(attack.OrdnanceTypeDefinitionId)}  |  "
                    + $"{attack.Advantage}  |  target "
                    + (attack.TargetAware ? "aware" : "unaware")
                    + $"  |  P(hit) {attack.HitProbability:P1}  |  "
                    + (attack.Released ? "RELEASED" : "NO RELEASE")
                    + resolutionText);
            }
            if (damageAppliedAfterRolls)
            {
                AddFlightDetailMessage(
                    section,
                    "Timing: Damage caused by this round was applied after "
                    + "the displayed round inputs and rolls. Its penalties "
                    + "appear in the current state above and are used from "
                    + "the next WVR round onward.");
            }

            AddFlightDetailMessage(section, round.Outcome);
            var lineUnits = 8
                            + round.Disengagements.Count * 3
                            + round.Attacks.Count * 2;
            section.style.minHeight = 43f + lineUnits * 23f;
            flightDetailContent.Add(section);
        }

        private void AddCurrentWvrState(
            VisualElement section,
            AirFlight flight,
            Squadron squadron,
            bool active)
        {
            var aircraft = squadron.Aircraft
                .Where(candidate =>
                    candidate.AssignedFlightId == flight.FlightId
                    && candidate.Status != CampaignAircraftStatus.Lost)
                .ToList();
            var damagedCount = aircraft.Count(candidate =>
                candidate.Status == CampaignAircraftStatus.Damaged);
            var undamagedCount = aircraft.Count - damagedCount;
            var combatWeight = undamagedCount
                               + damagedCount
                               * WvrEngagementSystem.DamagedCombatWeight;
            var aircraftType = ModuleSingleton.Instance?.ActiveModule
                ?.AircraftTypeDefinitions.FirstOrDefault(definition =>
                    definition.AircraftTypeDefinitionId
                    == squadron.AircraftTypeDefinitionId);
            var effectiveRating = aircraft.Count == 0
                                  || aircraftType == null
                ? 0f
                : aircraftType.WvrCombatRating
                  * (undamagedCount
                     + damagedCount
                     * WvrEngagementSystem.DamagedWvrRatingMultiplier)
                  / aircraft.Count;
            var speedMultiplier = damagedCount > 0
                ? WvrEngagementSystem.DamagedAircraftSpeedMultiplier
                : 1f;
            AddFlightDetailMessage(
                section,
                $"{(active ? "Current next-round" : "Current post-round")} "
                + $"state for FLT {ShortId(flight.FlightId)}: "
                + $"{undamagedCount} undamaged / {damagedCount} damaged / "
                + $"weight {combatWeight:0.00} / "
                + $"rating {effectiveRating:0.000} / "
                + $"speed {speedMultiplier:P0}");
        }

        private OrdnanceShotDiagnostic FindWvrShot(
            WvrRoundDiagnostic round,
            WvrAttackDiagnostic attack)
        {
            return gameManager.GetOrdnanceEmploymentRecords()
                .Where(record =>
                    record.EmploymentPassId == round.EngagementId
                    && record.Stage
                    == OrdnanceEmploymentRecordStage.EffectResolved
                    && record.OccurredAt == round.ResolvedAt
                    && record.SourceFlightId == attack.SourceFlightId
                    && record.TargetFlightId == attack.TargetFlightId
                    && record.OrdnanceTypeDefinitionId
                    == attack.OrdnanceTypeDefinitionId)
                .SelectMany(record => record.Shots)
                .OrderBy(shot => shot.Sequence)
                .FirstOrDefault();
        }

        private string FormatFlightIds(IEnumerable<Guid> flightIds)
        {
            var labels = flightIds?
                .Distinct()
                .Select(GetFlightLabel)
                .ToList();
            return labels == null || labels.Count == 0
                ? "None"
                : string.Join(", ", labels);
        }

        private static string FormatWvrAdvantage(
            Alliance alliance,
            WvrAdvantageLevel level)
        {
            return alliance == Alliance.Neutral
                   || level == WvrAdvantageLevel.Neutral
                ? "Neutral"
                : $"{GetAllianceLabel(alliance)} {level}";
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

        private void AddFlightDetailButton(
            VisualElement section,
            string message,
            Action action)
        {
            var button = new Button(action) { text = message };
            button.AddToClassList("flight-detail-line");
            button.AddToClassList("campaign-air-card--clickable");
            ApplyRuntimeFont(button);
            section.Add(button);
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
            return GetOwningPackage(flight, packages).Alliance;
        }

        private static AirPackage GetOwningPackage(
            AirFlight flight,
            IReadOnlyList<AirPackage> packages)
        {
            return packages.FirstOrDefault(package =>
                       package.Flights.Contains(flight))
                   ?? throw new InvalidOperationException(
                       $"Flight {flight.FlightId} has no owning package.");
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
                AirMissionRequestType.BarrierCombatAirPatrol => "BARCAP",
                AirMissionRequestType.OffensiveCounterAirSweep => "OCA Sweep",
                AirMissionRequestType.DestructionOfEnemyAirDefenses => "DEAD",
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

        private static string GetEmploymentStageLabel(
            OrdnanceEmploymentRecordStage stage)
        {
            return stage switch
            {
                OrdnanceEmploymentRecordStage.PreparationStarted => "Preparation",
                OrdnanceEmploymentRecordStage.PreparationAborted => "Aborted",
                OrdnanceEmploymentRecordStage.OrdnanceReleased => "Released",
                OrdnanceEmploymentRecordStage.EffectResolved => "Resolved",
                _ => stage.ToString()
            };
        }

        private string BuildOrdnancePassLine(OrdnanceEmploymentRecord record)
        {
            return $"{record.OccurredAt:HH:mm:ss}  PASS {ShortId(record.EmploymentPassId)}  " +
                   $"{GetSourceLabel(record)} → {GetOrdnanceTargetLabel(record)}  " +
                   $"{record.Quantity}× {GetOrdnanceName(record.OrdnanceTypeDefinitionId)}  " +
                   $"range {record.ReleaseRangeKm:0.0} km  P(hit) {record.HitProbability:P1}";
        }

        private IEnumerable<string> BuildOrdnancePassInspectorLines(
            OrdnanceEmploymentRecord record)
        {
            var resolvedShots = GetResolvedShots(record).ToList();
            var probabilityExplanation = record.TargetKind
                                         == OrdnanceEmploymentTargetKind
                                             .AirDefenseComponent
                ? "A ground-attack hit chance combines the store's base probability, suitability for the component category, and release-range penalty."
                : record.SourceKind ==
                  OrdnanceEmploymentSourceKind.SamLauncher
                ? "A SAM hit chance starts at the fused IADS track quality snapshotted at release; later guidance support and actual target defense modify terminal probability."
                : "An aircraft-weapon hit chance is snapshotted at release from ordnance probability, range, guidance mode, shooter capability, target ECM, and launch geometry.";
            var lines = new List<string>
            {
                "PASS SUMMARY",
                $"Pass ID  {record.EmploymentPassId:N}",
                $"Released  {record.OccurredAt:yyyy-MM-dd HH:mm:ss}",
                $"Source  {GetSourceLabel(record)}",
                $"Target  {GetOrdnanceTargetLabel(record)}",
                $"Ordnance  {record.Quantity}× {GetOrdnanceName(record.OrdnanceTypeDefinitionId)}",
                $"Release range  {record.ReleaseRangeKm:0.0} km",
                $"Snapshotted P(hit)  {record.HitProbability:P1}",
                $"Release source position  X {record.SourcePositionFeet.x:0} / Z {record.SourcePositionFeet.z:0} / ALT {record.SourcePositionFeet.y:0} ft",
                $"Release target position  X {record.TargetPositionFeet.x:0} / Z {record.TargetPositionFeet.z:0} / ALT {record.TargetPositionFeet.y:0} ft",
                string.Empty,
                "WHY THIS PROBABILITY",
                probabilityExplanation,
                "The current debug record stores the final probability and release context; it does not yet persist each intermediate modifier as a separate field.",
                string.Empty,
                "LAUNCHES AND RESOLUTION"
            };

            foreach (var launch in GetRecordLaunches(record))
            {
                var shot = resolvedShots.FirstOrDefault(item => item.Sequence == launch.Sequence);
                lines.Add(BuildOrdnanceLaunchLine(record, launch, shot));
                lines.Add("    " + BuildShotReasonLine(shot, record));
            }

            if (resolvedShots.Count == 0)
                lines.Add("No resolved effects yet; this pass has released ordnance but impact has not occurred.");

            return lines;
        }

        private string BuildShotReasonLine(
            OrdnanceShotDiagnostic shot,
            OrdnanceEmploymentRecord record)
        {
            if (shot == null)
                return "Pending: no effect-resolution record exists for this launch yet.";
            if (record.TargetKind
                == OrdnanceEmploymentTargetKind.AirDefenseComponent)
            {
                var groundRoll = shot.Roll < 0f ? 0f : shot.Roll;
                var groundThreshold = shot.Probability > 0f
                    ? shot.Probability
                    : record.HitProbability;
                return shot.Result switch
                {
                    OrdnanceShotResult.Hit =>
                        $"Destroyed SAM component {ShortId(record.TargetComponentId)} "
                        + $"because roll {groundRoll:0.000} was below "
                        + $"P(hit) {groundThreshold:P1}.",
                    OrdnanceShotResult.Miss =>
                        $"Missed SAM component {ShortId(record.TargetComponentId)} "
                        + $"because roll {groundRoll:0.000} was at or above "
                        + $"P(hit) {groundThreshold:P1}.",
                    _ => $"Ground effect result: {shot.Result}."
                };
            }
            if (shot.Result == OrdnanceShotResult.Ineffective)
            {
                return shot.TargetAircraftId == Guid.Empty
                    ? "Ineffective: no target aircraft was available for this store."
                    : $"Ineffective: selected target aircraft {ShortId(shot.TargetAircraftId)} was not a valid survivor when the effect resolved.";
            }
            if (shot.Result == OrdnanceShotResult.Defeated)
            {
                return shot.DefeatReason switch
                {
                    OrdnanceDefeatReason.KinematicRangeExceeded =>
                        "Defeated: the target exceeded the missile's maximum "
                        + "range from its launch position.",
                    OrdnanceDefeatReason.RadarLockBroken =>
                        "Defeated: the target's achieved beam geometry and "
                        + "radar defenses broke guidance lock.",
                    _ => "Defeated before reaching a valid terminal engagement."
                };
            }

            var threshold = shot.Probability > 0f ? shot.Probability : record.HitProbability;
            var roll = shot.Roll < 0f ? 0f : shot.Roll;
            return shot.Result switch
            {
                OrdnanceShotResult.Hit when shot.TargetWasAlreadyDamaged =>
                    $"Destroyed because hit roll {roll:0.000} was below "
                    + $"terminal P(hit) {threshold:P1} and the aircraft was "
                    + "already damaged.",
                OrdnanceShotResult.Hit =>
                    $"Destroyed after hit roll {roll:0.000} was below "
                    + $"terminal P(hit) {threshold:P1} and lethality roll "
                    + $"{shot.DestructionRoll:0.000} was below P(destroy | hit) "
                    + $"{shot.DestructionProbability:P1}.",
                OrdnanceShotResult.Damaged =>
                    $"Damaged after hit roll {roll:0.000} was below terminal "
                    + $"P(hit) {threshold:P1}, but lethality roll "
                    + $"{shot.DestructionRoll:0.000} was at or above "
                    + $"P(destroy | hit) {shot.DestructionProbability:P1}.",
                OrdnanceShotResult.Miss =>
                    $"Miss because roll {roll:0.000} was at or above terminal P(hit) {threshold:P1}.",
                _ => $"Result: {shot.Result}."
            };
        }

        private string BuildOrdnanceLaunchLine(
            OrdnanceEmploymentRecord record,
            OrdnanceLaunchDiagnostic launch,
            OrdnanceShotDiagnostic shot)
        {
            var targetLabel = record.TargetKind
                              == OrdnanceEmploymentTargetKind.AirDefenseComponent
                ? $"SAM component {ShortId(record.TargetComponentId)}"
                : $"aircraft {ShortId(launch.TargetAircraftId)}";
            return $"  Launch {launch.Sequence}: {GetLaunchSourceLabel(record, launch)} " +
                   $"→ {targetLabel}  " +
                   $"{GetOrdnanceName(launch.OrdnanceTypeDefinitionId)}" +
                   (shot == null
                       ? string.Empty
                       : $"  result {shot.Result}  roll {(shot.Roll < 0f ? "—" : shot.Roll.ToString("0.000"))}");
        }

        private IEnumerable<OrdnanceLaunchDiagnostic> GetRecordLaunches(
            OrdnanceEmploymentRecord record)
        {
            if (record.Launches != null && record.Launches.Count > 0)
                return record.Launches.OrderBy(item => item.Sequence);

            return Enumerable.Range(1, Math.Max(1, record.Quantity))
                .Select(index => new OrdnanceLaunchDiagnostic
                {
                    Sequence = index,
                    SourceAircraftId = record.SourceAircraftId,
                    OrdnanceTypeDefinitionId = record.OrdnanceTypeDefinitionId,
                    ReleasedAt = record.OccurredAt
                });
        }

        private OrdnanceShotDiagnostic FindResolvedShot(
            OrdnanceEmploymentRecord releaseRecord,
            int sequence)
        {
            return gameManager.GetOrdnanceEmploymentRecords()
                .Where(record => record.Stage == OrdnanceEmploymentRecordStage.EffectResolved
                                 && record.PendingEffectId == releaseRecord.PendingEffectId)
                .SelectMany(record => record.Shots)
                .FirstOrDefault(shot => shot.Sequence == sequence);
        }

        private IEnumerable<OrdnanceEmploymentRecord> GetLastTurnOrdnanceReleaseRecords()
        {
            if (gameManager == null)
                return Enumerable.Empty<OrdnanceEmploymentRecord>();

            var from = gameManager.LastTurnStartedAt;
            var to = gameManager.LastTurnCompletedAt;
            if (to <= from)
                return Enumerable.Empty<OrdnanceEmploymentRecord>();
            return gameManager.GetOrdnanceEmploymentRecords()
                .Where(record => record.Stage == OrdnanceEmploymentRecordStage.OrdnanceReleased
                                 && record.OccurredAt > from
                                 && record.OccurredAt <= to);
        }

        private IEnumerable<OrdnanceShotDiagnostic> GetResolvedShots(
            OrdnanceEmploymentRecord releaseRecord)
        {
            return gameManager.GetOrdnanceEmploymentRecords()
                .Where(record => record.Stage == OrdnanceEmploymentRecordStage.EffectResolved
                                 && record.PendingEffectId == releaseRecord.PendingEffectId)
                .SelectMany(record => record.Shots)
                .OrderBy(shot => shot.Sequence);
        }

        private bool TryFindOrdnanceReleaseRecord(
            Guid employmentPassId,
            out OrdnanceEmploymentRecord record)
        {
            record = gameManager.GetOrdnanceEmploymentRecords()
                .Where(candidate => candidate.Stage == OrdnanceEmploymentRecordStage.OrdnanceReleased)
                .OrderByDescending(candidate => candidate.OccurredAt)
                .FirstOrDefault(candidate => candidate.EmploymentPassId == employmentPassId);
            return record != null;
        }

        private Alliance GetRecordAlliance(OrdnanceEmploymentRecord record)
        {
            if (record.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight
                && TryFindFlight(record.SourceFlightId, out _, out var package, out _))
                return package.Alliance;

            if (record.SourceKind == OrdnanceEmploymentSourceKind.SamLauncher
                && gameManager.airDefenseSiteSystem.TryGetSite(record.SourceSiteId, out var site))
                return gameManager.airDefenseSiteSystem.GetEffectiveAlliance(site);

            return Alliance.Neutral;
        }

        private void FrameOrdnancePass(OrdnanceEmploymentRecord record)
        {
            var endpoints = GetOrdnanceVisualEndpoints(record);
            FrameMapPoints(new[]
            {
                endpoints.source,
                endpoints.target
            });
        }

        private (Vector3 source, Vector3 target) GetOrdnanceVisualEndpoints(
            OrdnanceEmploymentRecord record)
        {
            return (GetOrdnanceSourceVisualPosition(record), GetOrdnanceTargetVisualPosition(record));
        }

        private Vector3 GetOrdnanceSourceVisualPosition(OrdnanceEmploymentRecord record)
        {
            if (record.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight
                && TryFindFlight(record.SourceFlightId, out var sourceFlight, out _, out _)
                && sourceFlight.HasPosition)
                return AirPositionToMapPosition(sourceFlight.PositionFeet);

            if (record.SourceKind == OrdnanceEmploymentSourceKind.SamLauncher
                && gameManager.airDefenseSiteSystem.TryGetSite(record.SourceSiteId, out var site)
                && gameManager.airDefenseSiteSystem.TryGetTileId(site, out var tileId)
                && hexCentersByCell.TryGetValue(GetCell(tileId), out var siteCenter))
                return siteCenter;

            return AirPositionToMapPosition(record.SourcePositionFeet);
        }

        private Vector3 GetOrdnanceTargetVisualPosition(OrdnanceEmploymentRecord record)
        {
            if (TryFindFlight(record.TargetFlightId, out var targetFlight, out _, out _)
                && targetFlight.HasPosition)
                return AirPositionToMapPosition(targetFlight.PositionFeet);

            return AirPositionToMapPosition(record.TargetPositionFeet);
        }

        private void SelectOrdnanceSource(OrdnanceEmploymentRecord record)
        {
            highlightedOrdnanceSourceFlightId = record.SourceFlightId;
            highlightedOrdnanceTargetFlightId = Guid.Empty;
            if (record.SourceKind == OrdnanceEmploymentSourceKind.AircraftFlight
                && record.SourceFlightId != Guid.Empty)
            {
                OpenFlightDetails(record.SourceFlightId);
                InspectFlightRoute(record.SourceFlightId);
            }
            else if (gameManager.airDefenseSiteSystem.TryGetSite(record.SourceSiteId, out var site)
                     && gameManager.airDefenseSiteSystem.TryGetTileId(site, out var tileId))
            {
                FocusTile(tileId);
            }
            ApplySelectedFlightMarker();
        }

        private void SelectOrdnanceTarget(OrdnanceEmploymentRecord record)
        {
            highlightedOrdnanceSourceFlightId = Guid.Empty;
            highlightedOrdnanceTargetFlightId = record.TargetFlightId;
            if (record.TargetFlightId != Guid.Empty)
            {
                OpenFlightDetails(record.TargetFlightId);
                InspectFlightRoute(record.TargetFlightId);
            }
            else if (record.TargetKind
                         == OrdnanceEmploymentTargetKind.AirDefenseComponent
                     && gameManager.airDefenseSiteSystem.TryGetSite(
                         record.TargetSiteId,
                         out var site)
                     && gameManager.airDefenseSiteSystem.TryGetTileId(
                         site,
                         out var tileId))
            {
                FocusTile(tileId);
            }
            ApplySelectedFlightMarker();
        }

        private string GetSourceLabel(OrdnanceEmploymentRecord record)
        {
            if (record.SourceKind != OrdnanceEmploymentSourceKind.SamLauncher)
                return GetFlightLabel(record.SourceFlightId);

            return gameManager.airDefenseSiteSystem.TryGetSite(record.SourceSiteId, out var site)
                ? $"{GetSamSiteDisplayName(site)} {ShortId(site.SiteId)}"
                : $"SAM {ShortId(record.SourceSiteId)}";
        }

        private string GetOrdnanceTargetLabel(OrdnanceEmploymentRecord record)
        {
            if (record.TargetKind
                == OrdnanceEmploymentTargetKind.AirDefenseComponent)
            {
                return $"SAM {ShortId(record.TargetSiteId)} component "
                       + ShortId(record.TargetComponentId);
            }

            return GetFlightLabel(record.TargetFlightId);
        }

        private string GetLaunchSourceLabel(
            OrdnanceEmploymentRecord record,
            OrdnanceLaunchDiagnostic launch)
        {
            return record.SourceKind == OrdnanceEmploymentSourceKind.SamLauncher
                ? $"SAM launcher {ShortId(record.SourceComponentId)}"
                : $"aircraft {ShortId(launch.SourceAircraftId)}";
        }

        private string GetFlightLabel(Guid flightId)
        {
            if (flightId == Guid.Empty)
                return "—";
            return TryFindFlight(flightId, out var flight, out _, out _)
                ? $"FLT {ShortId(flight.FlightId)}"
                : $"FLT {ShortId(flightId)}";
        }

        private static string GetOrdnanceName(Guid ordnanceTypeDefinitionId)
        {
            if (ordnanceTypeDefinitionId == Guid.Empty)
                return "unknown ordnance";

            var module = ModuleSingleton.Instance?.ActiveModule;
            var definition = module?.OrdnanceTypeDefinitions
                .FirstOrDefault(item => item.OrdnanceTypeDefinitionId == ordnanceTypeDefinitionId);
            return definition?.Name ?? ShortId(ordnanceTypeDefinitionId);
        }

        private static string GetFlightName(AirFlight flight, Squadron squadron)
        {
            return string.IsNullOrWhiteSpace(squadron.Name)
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
                Label = label;
                Value = value;
            }
        }

        private void UpdateTimeUi()
        {
            if (timeLabel == null || gameManager == null)
                return;

            timeLabel.text = $"{gameManager.GameTime:yyyy-MM-dd HH:mm:ss} | Tiles: {gameManager.tileSystem.Count}";
            if (pauseButton != null)
                pauseButton.text = gameManager.IsGamePaused ? "Play" : "Pause";
            if (nextIncrementButton != null)
            {
                nextIncrementButton.text = gameManager.PlaybackIncrement == CampaignPlaybackIncrement.FiveMinutes
                    ? "Next +5m"
                    : "Next +5s";
                nextIncrementButton.SetEnabled(gameManager.IsGamePaused);
            }
            speedFiveSecondsButton?.EnableInClassList(
                "simulation-speed-button--selected",
                gameManager.PlaybackIncrement == CampaignPlaybackIncrement.FiveSeconds);
            speedFiveMinutesButton?.EnableInClassList(
                "simulation-speed-button--selected",
                gameManager.PlaybackIncrement == CampaignPlaybackIncrement.FiveMinutes);
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
                UpdateBuildingsUi();
                return;
            }

            var landData = selectedTile as RuntimeLandTile;
            var controller = landData == null ? "None" : landData.Controller.ToString();
            var infrastructure = landData == null ? "N/A" : landData.InfrastructureFunctionalLevel.ToString();
            var buildings = gameManager.buildingSystem.GetBuildingsOnTile(selectedTile.TileId);
            var buildingText = buildings.Count == 0
                ? "No buildings"
                : string.Join(", ", buildings.Select(building => $"{building.Type} {building.FunctionalLevel}"));
            var supplyFeatures = GetSupplyFeatureLabel(selectedTile.TileId);
            var samSites = GetSamSitesOnTile(selectedTile.TileId).ToList();
            var samText = samSites.Count == 0
                ? string.Empty
                : "\n" + string.Join(
                    "\n",
                    samSites.Select(site =>
                    {
                        var components = site.Components?
                            .Where(component => component != null)
                            .ToList() ?? new List<AirDefenseComponent>();
                        var intact = components.Count(component =>
                            !component.IsDamaged);
                        return $"SAM: {GetSamSiteDisplayName(site)}  •  " +
                               $"{GetSamSiteStateLabel(site)}  •  " +
                               $"{intact}/{components.Count} components intact";
                    }));

            selectedTileLabel.text =
                $"Hex {selectedTile.TileId.x}, {selectedTile.TileId.y}, {selectedTile.TileId.z}\n" +
                $"{selectedTile.Surface} | {selectedTile.Terrain}\n" +
                $"Settlement: {selectedTile.Urbanization} | Forest: {selectedTile.ForestCover}\n" +
                $"Control: {controller}\n" +
                $"Infrastructure: {infrastructure}\n" +
                buildingText +
                (string.IsNullOrWhiteSpace(supplyFeatures) ? string.Empty : $"\n{supplyFeatures}") +
                samText;

            UpdateNeighborsUi();
            UpdateUnitsUi();
            UpdateBuildingsUi();
        }

        private void UpdateNeighborsUi()
        {
            if (neighborsFoldout == null)
                return;

            var neighborCount = 0;
            if (selectedCell.HasValue && tilesByCell.TryGetValue(selectedCell.Value, out var selectedTile))
                neighborCount = selectedTile.NeighborTileIds.Count;

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

        private void UpdateBuildingsUi()
        {
            if (buildingsFoldout == null)
                return;

            var count = 0;
            if (selectedCell.HasValue && tilesByCell.TryGetValue(selectedCell.Value, out var selectedTile))
                count = gameManager.buildingSystem.GetBuildingsOnTile(selectedTile.TileId).Count;
            buildingsFoldout.text = count == 0 ? "Buildings" : $"Buildings ({count})";
            if (buildingsFoldout.value)
                UpdateBuildingsList();
        }

        private void UpdateBuildingsList()
        {
            if (buildingsList == null || buildingsFoldout == null || !buildingsFoldout.value)
                return;

            buildingsList.Clear();
            if (!selectedCell.HasValue || !tilesByCell.TryGetValue(selectedCell.Value, out var selectedTile))
            {
                buildingsList.Add(CreateNeighborMessage("Select a tile to inspect buildings."));
                return;
            }

            var buildings = gameManager.buildingSystem
                .GetBuildingsOnTile(selectedTile.TileId)
                .OrderBy(building => building.Type)
                .ThenBy(building => building.BuildingId)
                .ToList();
            if (buildings.Count == 0)
            {
                buildingsList.Add(CreateNeighborMessage("No buildings on this tile."));
                return;
            }

            foreach (var building in buildings)
            {
                var samLines = gameManager.airDefenseSiteSystem
                    .GetSitesForHost(building.BuildingId)
                    .Where(site => site != null)
                    .Select(site =>
                    {
                        var components = site.Components?
                            .Where(component => component != null)
                            .ToList() ?? new List<AirDefenseComponent>();
                        return $"\nSAM {GetSamSiteDisplayName(site)}  •  " +
                               $"{GetSamSiteStateLabel(site)}  •  " +
                               $"{components.Count(component => !component.IsDamaged)}/{components.Count} intact";
                    });
                var button = new Button(() => PinBuildingInspector(building))
                {
                    text =
                        $"{building.Type}  •  Functional {building.FunctionalLevel}/{building.Level.BuildLevel}\n" +
                        $"Damage {building.Level.Damage}  •  Toughness {building.TargetToughness}" +
                        string.Concat(samLines)
                };
                button.AddToClassList("campaign-hud-neighbor-item");
                ApplyRuntimeFont(button);
                buildingsList.Add(button);
            }
        }

        private static string FormatAirportOperationsStatus(
            AirportOperationsSnapshot operations)
        {
            if (!operations.IsOperational)
                return "Closed";
            if (operations.IsSaturated)
                return "Saturated";
            return operations.IsReduced ? "Reduced" : "Full";
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

            var neighborIds = selectedTile.NeighborTileIds;
            if (neighborIds.Count == 0)
            {
                neighborsList.Add(CreateNeighborMessage("No neighbors."));
                return;
            }

            var riverNeighbors = new HashSet<Vector3Int>(selectedTile.RiverNeighborTileIds);

            foreach (var neighborId in neighborIds)
            {
                if (!tilesById.TryGetValue(neighborId, out var neighbor))
                {
                    neighborsList.Add(CreateNeighborMessage($"Unknown tile {neighborId}"));
                    continue;
                }

                var riverSuffix = riverNeighbors.Contains(neighborId) ? " | River" : string.Empty;
                var coords = neighbor.TileId;
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

            return gameManager.divisionSystem.GetDivisionsOnTile(selectedTile.TileId);
        }

        private VisualElement CreateUnitCard(Division division)
        {
            var card = new Button(() => PinDivisionInspector(division)) { text = string.Empty };
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
            var mobileSamSites = gameManager.airDefenseSiteSystem
                .GetSitesForHost(division.DivisionId)
                .Where(site => site != null)
                .ToList();
            if (mobileSamSites.Count > 0)
            {
                stats.text += "\n" + string.Join(
                    "\n",
                    mobileSamSites.Select(site =>
                    {
                        var components = site.Components?
                            .Where(component => component != null)
                            .ToList() ?? new List<AirDefenseComponent>();
                        return $"SAM {GetSamSiteDisplayName(site)} | " +
                               $"{GetSamSiteStateLabel(site)} | " +
                               $"{components.Count(component => !component.IsDamaged)}/{components.Count} intact";
                    }));
            }
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

        private Button CreateNeighborSelectButton(RuntimeTile neighbor, string summary)
        {
            var button = new Button(() => SelectTile(neighbor)) { text = summary };
            button.AddToClassList("campaign-hud-neighbor-item");
            button.pickingMode = PickingMode.Position;
            ApplyRuntimeFont(button);
            return button;
        }

        private void SelectTile(RuntimeTile tile)
        {
            var cell = GetCell(tile.TileId);
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
                         .GroupBy(division => new
                         {
                             division.TileId,
                             Alliance = GetDivisionAlliance(division)
                         }))
            {
                var cell = GetCell(group.Key.TileId);
                if (!hexCentersByCell.TryGetValue(cell, out var hexCenter))
                    continue;

                var divisions = group.ToList();
                var offset = group.Key.Alliance == Alliance.Redfor ? 0.12f : -0.12f;
                hexCenter += new Vector3(offset, -0.1f, 0f);
                CreateUnitCounter(group.Key.TileId, hexCenter, divisions, group.Key.Alliance);
            }
        }

        private void CreateUnitCounter(
            Vector3Int tileId,
            Vector3 hexCenter,
            IReadOnlyList<Division> divisions,
            Alliance alliance)
        {
            var counterObject = new GameObject($"Unit Counter {tileId.x},{tileId.y},{tileId.z}");
            counterObject.transform.SetParent(unitCounterRoot, false);
            counterObject.transform.position = grid.transform.TransformPoint(hexCenter) + new Vector3(0f, -0.1f, -0.2f);

            var renderer = counterObject.AddComponent<SpriteRenderer>();
            var symbol = GetDivisionNatoSymbol(divisions[0]);
            renderer.sprite = GetUnitCounterSprite(alliance, symbol);
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
            textMesh.text = divisions.Count.ToString();

            var textRenderer = textObject.GetComponent<MeshRenderer>();
            textRenderer.sortingOrder = 21;

            var detailObject = new GameObject("Counter Detail");
            detailObject.transform.SetParent(counterObject.transform, false);
            detailObject.transform.localPosition = new Vector3(0f, -0.22f, -0.05f);
            var detail = detailObject.AddComponent<TextMesh>();
            detail.anchor = TextAnchor.UpperCenter;
            detail.alignment = TextAlignment.Center;
            detail.characterSize = 0.014f;
            detail.fontSize = 22;
            detail.color = Color.white;
            var averageStrength = divisions.Average(division =>
                GetDivisionStatPercent(division.Strength, division.MaxStrength));
            var averageOrganization = divisions.Average(division =>
                GetDivisionStatPercent(division.Organization, division.MaxOrganization));
            detail.text =
                $"{symbol}  {divisions[0].Name}\n" +
                $"STR {averageStrength:0}%  ORG {averageOrganization:0}%";
            detailObject.GetComponent<MeshRenderer>().sortingOrder = 22;
        }

        private NatoUnitSymbol GetDivisionNatoSymbol(Division division)
        {
            return ModuleSingleton.Instance.ActiveModule.DivisionTemplates
                .FirstOrDefault(template => template.DivisionTemplateId == division.DivisionTemplateId)
                ?.NatoSymbol ?? NatoUnitSymbol.Unspecified;
        }

        private Sprite GetUnitCounterSprite(Alliance alliance, NatoUnitSymbol symbol)
        {
            var key = $"{alliance}:{symbol}";
            if (unitCounterSpritesByKey.TryGetValue(key, out var sprite))
                return sprite;

            var texture = CreateUnitCounterTexture(alliance, symbol);
            sprite = Sprite.Create(
                texture,
                new Rect(0, 0, UnitCounterPixelWidth, UnitCounterPixelHeight),
                new Vector2(0.5f, 0.5f),
                UnitCounterPixelWidth / UnitCounterWorldWidth);
            unitCounterSpritesByKey[key] = sprite;
            return sprite;
        }

        private Texture2D CreateUnitCounterTexture(Alliance alliance, NatoUnitSymbol symbol)
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

            DrawNatoSymbol(pixels, symbol, Color.white);

            var texture = new Texture2D(UnitCounterPixelWidth, UnitCounterPixelHeight);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static void DrawNatoSymbol(Color[] pixels, NatoUnitSymbol symbol, Color color)
        {
            void Set(int x, int y)
            {
                if (x >= 2 && x < UnitCounterPixelWidth - 2
                    && y >= 2 && y < UnitCounterPixelHeight - 4)
                    pixels[y * UnitCounterPixelWidth + x] = color;
            }

            void Line(int x0, int y0, int x1, int y1)
            {
                var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                for (var index = 0; index <= steps; index++)
                {
                    var t = steps == 0 ? 0f : index / (float)steps;
                    Set(Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, t)));
                }
            }

            if (symbol == NatoUnitSymbol.Infantry
                || symbol == NatoUnitSymbol.MechanizedInfantry
                || symbol == NatoUnitSymbol.MotorizedInfantry
                || symbol == NatoUnitSymbol.Airborne)
            {
                Line(7, 4, 22, 14);
                Line(22, 4, 7, 14);
            }
            if (symbol == NatoUnitSymbol.Armor || symbol == NatoUnitSymbol.MechanizedInfantry)
            {
                for (var angle = 0; angle < 360; angle += 12)
                {
                    var radians = angle * Mathf.Deg2Rad;
                    Set(
                        Mathf.RoundToInt(15 + Mathf.Cos(radians) * 8),
                        Mathf.RoundToInt(9 + Mathf.Sin(radians) * 5));
                }
            }
            if (symbol == NatoUnitSymbol.Artillery)
            {
                Set(15, 9);
                for (var radius = 1; radius <= 5; radius++)
                {
                    Set(15 + radius, 9);
                    Set(15 - radius, 9);
                    Set(15, 9 + radius);
                    Set(15, 9 - radius);
                }
            }
            if (symbol == NatoUnitSymbol.AirDefense)
            {
                for (var x = 7; x <= 22; x++)
                {
                    var t = (x - 7) / 15f;
                    Set(x, Mathf.RoundToInt(5 + Mathf.Sin(t * Mathf.PI) * 9));
                }
            }
            if (symbol == NatoUnitSymbol.Unspecified || symbol == NatoUnitSymbol.Headquarters)
            {
                Line(8, 9, 22, 9);
                Line(15, 4, 15, 14);
            }
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

            if (overlayBarriersToggle == null || overlayBarriersToggle.value)
                CreateBarcapBarrierOverlays();

            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var commander = gameManager.GetAllianceAirTaskingCommander(alliance);
                foreach (var package in commander.Packages)
                {
                    foreach (var flight in package.Flights)
                    {
                        if (!flight.IsAirborne || !flight.HasPosition)
                            continue;

                        if (flight.FlightId == selectedFlightId)
                            CreateAirIntentVisuals(flight, alliance);
                        CreateAirRoute(flight, alliance);
                        CreateAirMarker(flight, alliance);
                    }
                }
            }

            CreateSelectedFlightTargetOverlay();
        }

        private void CreateBarcapBarrierOverlays()
        {
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var commander = gameManager.GetAllianceAirTaskingCommander(alliance);
                var liveRequestIds = commander.Packages
                    .Where(package => !package.IsTerminal)
                    .Select(package => package.MissionRequestId)
                    .ToHashSet();
                var barriers = commander.MissionRequests
                    .Where(request =>
                        (request.PlanningCycle == commander.PlanningCycle
                         || liveRequestIds.Contains(request.MissionRequestId))
                        && request.RequestType ==
                            AirMissionRequestType.BarrierCombatAirPatrol
                        && request.BarcapBarrier?.BarrierTileIds?.Count > 0)
                    .Select(request => request.BarcapBarrier)
                    .GroupBy(barrier => barrier.BarrierId)
                    .Select(group => group.First())
                    .ToList();
                var coverages = commander.Packages
                    .Where(package => !package.IsTerminal)
                    .SelectMany(package => package.Flights)
                    .Where(flight =>
                        flight.MissionType ==
                            AirMissionRequestType.BarrierCombatAirPatrol
                        && !flight.IsTerminal
                        && flight.ExecutionPhase != FlightExecutionPhase.Returning
                        && flight.ExecutionPhase != FlightExecutionPhase.Landing
                        && flight.ExecutionPhase != FlightExecutionPhase.Ended
                        && gameManager.RetainsProjectedBarcapCoverage(flight))
                    .Select(flight => flight.PlannedBarcapCoverage)
                    .ToList();

                foreach (var barrier in barriers)
                    CreateBarcapBarrierOverlay(alliance, barrier, coverages);
            }
        }

        private void CreateBarcapBarrierOverlay(
            Alliance alliance,
            BarcapBarrierPlan barrier,
            IReadOnlyList<BarcapStationCoverage> coverages)
        {
            var tileDistanceKm = gameManager.SimulationSettings.TileDistanceKM;
            var points = BarcapInterceptGeometry
                .GetOperationalBarrierPointsFeet(
                    barrier.BarrierTileIds,
                    barrier.ThreatReferenceTileId,
                    tileDistanceKm,
                    barrier.WeaponReleaseStandoffKm)
                .Select(AirPositionToMapPosition)
                .ToList();
            if (points.Count == 0)
                return;

            var color = GetAirMissionIntentColor(
                AirMissionRequestType.BarrierCombatAirPatrol,
                alliance);
            if (points.Count == 1)
            {
                CreateAirIntentCircle(
                    $"BARCAP Barrier {ShortId(barrier.BarrierId)}",
                    airOverlayRoot,
                    points[0],
                    0.22f,
                    WithAlpha(color, 0.55f),
                    BarcapBarrierLineWidth,
                    -0.36f,
                    37);
            }
            else
            {
                CreateAirIntentPolyline(
                    $"BARCAP Barrier {ShortId(barrier.BarrierId)}",
                    airOverlayRoot,
                    points,
                    WithAlpha(color, 0.55f),
                    BarcapBarrierLineWidth,
                    -0.36f,
                    37);
            }

            var midpoint = points[points.Count / 2];
            var threat = GetTileMapCenter(barrier.ThreatReferenceTileId);
            var defensiveDirection = midpoint - threat;
            CreateAirIntentChevron(
                airOverlayRoot,
                midpoint,
                defensiveDirection,
                0.25f,
                WithAlpha(color, 0.88f),
                39);
            CreateAirIntentLabel(
                airOverlayRoot,
                midpoint + new Vector3(0f, 0.16f, 0f),
                barrier.IsSupplemental
                    ? "BARCAP RELEASE SCREEN"
                    : "BARCAP RELEASE LINE",
                WithAlpha(color, 0.92f),
                39);

            var barrierTiles = barrier.BarrierTileIds.ToHashSet();
            foreach (var coverage in coverages.Where(coverage =>
                         coverage.BarrierId == barrier.BarrierId
                         || coverage.CoveredBarrierTileIds.Any(
                                barrierTiles.Contains)
                            && BarcapInterceptGeometry.IsApproachCompatible(
                                coverage,
                                barrier)))
            {
                var coveredTiles = barrier.BarrierTileIds
                    .Where(coverage.CoveredBarrierTileIds.Contains)
                    .ToList();
                var coveredPoints = BarcapInterceptGeometry
                    .GetOperationalBarrierPointsFeet(
                        coveredTiles,
                        coverage.ThreatReferenceTileId,
                        tileDistanceKm,
                        coverage.WeaponReleaseStandoffKm)
                    .Select(AirPositionToMapPosition)
                    .ToList();
                if (coveredPoints.Count == 0)
                    continue;

                if (coveredPoints.Count == 1)
                {
                    CreateAirIntentCircle(
                        $"BARCAP Station Coverage {ShortId(barrier.BarrierId)}",
                        airOverlayRoot,
                        coveredPoints[0],
                        0.16f,
                        WithAlpha(color, 0.98f),
                        BarcapBarrierLineWidth * 1.35f,
                        -0.38f,
                        40);
                    continue;
                }

                CreateAirIntentPolyline(
                    $"BARCAP Station Coverage {ShortId(barrier.BarrierId)}",
                    airOverlayRoot,
                    coveredPoints,
                    WithAlpha(color, 0.98f),
                    BarcapBarrierLineWidth * 1.35f,
                    -0.38f,
                    40);
            }
        }

        private Vector3 GetTileMapCenter(Vector3Int tileId)
        {
            return GetMissionAreaMapCenter(new AirMissionArea(tileId, 0));
        }

        private void CreateAirIntentVisuals(AirFlight flight, Alliance alliance)
        {
            if (!TryGetFlightMissionArea(flight, out var area))
                return;

            var color = GetAirMissionIntentColor(flight.MissionType, alliance);
            color.a = 0.78f;
            var center = GetMissionAreaMapCenter(area);
            var radius = GetMissionAreaMapRadius(area);
            var label = GetAirIntentLabel(flight.MissionType);

            CreateAirIntentCircle(
                $"Air Intent Area {ShortId(flight.FlightId)}",
                airOverlayRoot,
                center,
                radius,
                WithAlpha(color, 0.42f),
                AirIntentAreaLineWidth,
                -0.30f,
                22);
            CreateAirIntentLabel(
                airOverlayRoot,
                center + new Vector3(-radius * 0.58f, radius * 0.48f, 0f),
                label,
                WithAlpha(color, 0.92f),
                24);

            switch (flight.MissionType)
            {
                case AirMissionRequestType.OffensiveCounterAirSweep:
                    CreateAirSweepPattern(flight, center, radius, color);
                    break;

                case AirMissionRequestType.BarrierCombatAirPatrol:
                    CreateAirPatrolPattern(flight, center, radius, color);
                    break;

                case AirMissionRequestType.DestructionOfEnemyAirDefenses:
                    CreateAirSweepPattern(flight, center, radius, color);
                    break;

                case AirMissionRequestType.ProvideAirborneC2:
                    CreateAirSupportOrbitPattern(flight, center, radius, color, "C2");
                    break;

                case AirMissionRequestType.ProvideAerialRefueling:
                    CreateAirSupportOrbitPattern(flight, center, radius, color, "TANKER");
                    break;
            }
        }

        private void CreateAirSweepPattern(
            AirFlight flight,
            Vector3 center,
            float radius,
            Color color)
        {
            CreateAirIntentCircle(
                $"Air Sweep Focus Area {ShortId(flight.FlightId)}",
                airOverlayRoot,
                center,
                Mathf.Max(0.20f, radius * 0.72f),
                WithAlpha(color, 0.55f),
                AirIntentLineWidth,
                -0.34f,
                25);
        }

        private void CreateAirPatrolPattern(
            AirFlight flight,
            Vector3 center,
            float radius,
            Color color)
        {
            CreateAirIntentCircle(
                $"Air Patrol Inner Orbit {ShortId(flight.FlightId)}",
                airOverlayRoot,
                center,
                Mathf.Max(0.16f, radius * 0.58f),
                WithAlpha(color, 0.62f),
                AirIntentLineWidth,
                -0.34f,
                25);
            CreateStationLaneVisuals(flight, WithAlpha(color, 0.76f), "PATROL LEG", 25);
        }

        private void CreateAirSupportOrbitPattern(
            AirFlight flight,
            Vector3 center,
            float radius,
            Color color,
            string label)
        {
            if (!TryCreateStationRacetrack(flight, color, label))
            {
                CreateAirIntentCircle(
                    $"Air {label} Orbit {ShortId(flight.FlightId)}",
                    airOverlayRoot,
                    center,
                    Mathf.Max(0.16f, radius * 0.48f),
                    WithAlpha(color, 0.66f),
                    AirIntentLineWidth,
                    -0.34f,
                    25);
            }

            if (label == "C2")
            {
                CreateAirIntentCircle(
                    $"Air C2 Outer Ring {ShortId(flight.FlightId)}",
                    airOverlayRoot,
                    center,
                    Mathf.Max(0.24f, radius * 0.78f),
                    WithAlpha(color, 0.35f),
                    AirIntentAreaLineWidth,
                    -0.35f,
                    24);
            }
        }

        private void CreateStationLaneVisuals(
            AirFlight flight,
            Color color,
            string label,
            int sortingOrder)
        {
            foreach (var pair in GetStationPairs(flight))
            {
                var start = AirPositionToMapPosition(pair.entry.PositionFeet);
                var end = AirPositionToMapPosition(pair.endpoint.PositionFeet);
                if (Vector3.Distance(start, end) <= 0.02f)
                    continue;

                CreateAirIntentPolyline(
                    $"Air Station Lane {ShortId(flight.FlightId)}",
                    airOverlayRoot,
                    new[] { start, end },
                    color,
                    AirIntentLineWidth,
                    -0.36f,
                    sortingOrder);
                CreateAirIntentLabel(
                    airOverlayRoot,
                    Vector3.Lerp(start, end, 0.5f) + new Vector3(0.06f, 0.04f, 0f),
                    label,
                    WithAlpha(color, 0.95f),
                    sortingOrder + 1);
            }
        }

        private bool TryCreateStationRacetrack(
            AirFlight flight,
            Color color,
            string label)
        {
            var pair = GetStationPairs(flight).FirstOrDefault();
            if (pair.entry == null || pair.endpoint == null)
                return false;

            var start = AirPositionToMapPosition(pair.entry.PositionFeet);
            var end = AirPositionToMapPosition(pair.endpoint.PositionFeet);
            var delta = end - start;
            if (delta.sqrMagnitude <= 0.0025f)
                return false;

            var direction = delta.normalized;
            var normal = new Vector3(-direction.y, direction.x, 0f);
            var width = Mathf.Clamp(delta.magnitude * 0.22f, 0.16f, 0.34f);
            var points = new List<Vector3>
            {
                start + normal * width,
                end + normal * width,
                end - normal * width,
                start - normal * width,
                start + normal * width
            };
            CreateAirIntentPolyline(
                $"Air {label} Racetrack {ShortId(flight.FlightId)}",
                airOverlayRoot,
                points,
                WithAlpha(color, 0.78f),
                AirIntentLineWidth,
                -0.35f,
                25);
            CreateAirIntentLabel(
                airOverlayRoot,
                Vector3.Lerp(start, end, 0.5f) + normal * (width + 0.06f),
                label,
                WithAlpha(color, 0.95f),
                26);
            return true;
        }

        private IEnumerable<(AirWaypoint entry, AirWaypoint endpoint)> GetStationPairs(AirFlight flight)
        {
            var route = flight.Route;
            foreach (var entry in route.Where(waypoint => waypoint.Action == AirWaypointAction.StationEntry))
            {
                var endpoint = route.FirstOrDefault(waypoint =>
                    waypoint.Action == AirWaypointAction.StationEndpoint
                    && waypoint.HasRepeat
                    && waypoint.RepeatFromWaypointId == entry.WaypointId);
                if (endpoint != null)
                    yield return (entry, endpoint);
            }
        }

        private void CreateSelectedFlightTargetOverlay()
        {
            if (gameManager == null
                || selectedFlightId == Guid.Empty
                || !TryFindFlight(selectedFlightId, out var source, out var sourcePackage, out _)
                || !source.HasPosition)
                return;

            if (!TryFindCounterAirGuidance(sourcePackage, source, out var target, out var guidancePositionFeet)
                || !target.HasPosition)
                return;

            CreateSelectedTargetOverlay(
                source,
                target,
                guidancePositionFeet,
                source.TacticalState.Maneuver.ToString().ToUpperInvariant(),
                source.TacticalState.Intent == AirCombatIntent.Defend
                    ? new Color(1f, 0.32f, 0.22f)
                    : new Color(1f, 0.68f, 0.18f));
        }

        private bool TryFindCounterAirGuidance(
            AirPackage sourcePackage,
            AirFlight sourceFlight,
            out AirFlight targetFlight,
            out Vector3 guidancePositionFeet)
        {
            targetFlight = null;
            guidancePositionFeet = default;
            var tactical = sourceFlight.TacticalState;
            if (sourcePackage == null
                || tactical.TargetFlightId == Guid.Empty
                || !tactical.HasTacticalAimPoint
                || !TryFindFlight(
                    tactical.TargetFlightId,
                    out targetFlight,
                    out _,
                    out _))
                return false;
            guidancePositionFeet = tactical.TacticalAimPointFeet;
            return targetFlight != null;
        }

        private bool HasLiveAircraft(AirFlight flight)
        {
            return gameManager.squadronSystem.TryGetSquadron(flight.SquadronId, out var squadron)
                   && squadron.Aircraft.Any(aircraft =>
                       aircraft.AssignedFlightId == flight.FlightId
                       && aircraft.Status != CampaignAircraftStatus.Lost);
        }

        private IEnumerable<AirPackage> GetAllAirPackages()
        {
            foreach (var alliance in new[] { Alliance.Bluefor, Alliance.Redfor })
            {
                var commander = gameManager.GetAllianceAirTaskingCommander(alliance);
                if (commander == null)
                    continue;

                foreach (var package in commander.Packages)
                    yield return package;
            }
        }

        private void CreateSelectedTargetOverlay(
            AirFlight source,
            AirFlight target,
            Vector3 guidancePositionFeet,
            string label,
            Color color)
        {
            var sourcePosition = AirPositionToMapPosition(source.PositionFeet);
            var guidancePosition = AirPositionToMapPosition(guidancePositionFeet);
            var targetPosition = AirPositionToMapPosition(target.PositionFeet);
            if (Vector3.Distance(sourcePosition, guidancePosition) <= 0.01f)
                return;

            var lineObject = new GameObject(
                $"Air Tactical Vector {ShortId(source.FlightId)} to {ShortId(target.FlightId)}");
            lineObject.transform.SetParent(airOverlayRoot, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPositions(new[]
            {
                sourcePosition + new Vector3(0f, 0f, -0.40f),
                guidancePosition + new Vector3(0f, 0f, -0.40f)
            });
            line.startWidth = AirEngagementLineWidth;
            line.endWidth = AirEngagementLineWidth * 0.45f;
            line.numCapVertices = 3;
            line.material = GetMovementArrowMaterial();
            line.startColor = WithAlpha(color, 0.95f);
            line.endColor = WithAlpha(color, 0.70f);
            line.sortingOrder = 43;

            var direction = (guidancePosition - sourcePosition).normalized;
            CreateAirIntentChevron(
                airOverlayRoot,
                guidancePosition - direction * 0.16f,
                direction,
                0.13f,
                WithAlpha(color, 1f),
                45);
            CreateAirIntentCircle(
                $"Air Guidance Point {ShortId(source.FlightId)}",
                airOverlayRoot,
                guidancePosition,
                0.24f,
                WithAlpha(color, 0.95f),
                0.045f,
                -0.41f,
                46);
            CreateAirIntentPolyline(
                $"Air Guidance Standoff {ShortId(source.FlightId)} to {ShortId(target.FlightId)}",
                airOverlayRoot,
                new[] { guidancePosition, targetPosition },
                new Color(1f, 0.24f, 0.16f, 0.34f),
                AirRouteLineWidth,
                -0.39f,
                42);
            CreateAirIntentCircle(
                $"Air Tactical Target {ShortId(target.FlightId)}",
                airOverlayRoot,
                targetPosition,
                0.28f,
                new Color(1f, 0.22f, 0.16f, 0.92f),
                0.045f,
                -0.41f,
                46);
            CreateAirIntentLabel(
                airOverlayRoot,
                guidancePosition + new Vector3(0.08f, 0.08f, 0f),
                label,
                WithAlpha(color, 1f),
                47);
            CreateAirIntentLabel(
                airOverlayRoot,
                targetPosition + new Vector3(0.08f, -0.08f, 0f),
                "TARGET",
                new Color(1f, 0.30f, 0.22f),
                47);
        }

        private bool TryGetFlightMissionArea(AirFlight flight, out AirMissionArea area)
        {
            area = flight.ActiveEffectArea;
            if (area != null)
                return true;

            try
            {
                area = flight.MissionArea;
                return area != null;
            }
            catch (InvalidOperationException)
            {
                area = null;
                return false;
            }
        }

        private Vector3 GetMissionAreaMapCenter(AirMissionArea area)
        {
            if (hexCentersByCell.TryGetValue(GetCell(area.CenterTileId), out var center))
                return center;

            return AirPositionToMapPosition(AirspaceGeometry.TileCenterFeet(
                area.CenterTileId,
                gameManager.SimulationSettings.TileDistanceKM));
        }

        private float GetMissionAreaMapRadius(AirMissionArea area)
        {
            var tileDistanceKm = Math.Max(
                0.001f,
                gameManager.SimulationSettings.TileDistanceKM);
            return Mathf.Max(
                0.34f,
                (area.RadiusKm / tileDistanceKm + 0.62f) * HexHeight);
        }

        private void CreateAirIntentPolyline(
            string objectName,
            Transform parent,
            IReadOnlyList<Vector3> points,
            Color color,
            float width,
            float zOffset,
            int sortingOrder)
        {
            if (parent == null || points == null || points.Count < 2)
                return;

            var lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(parent, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = points.Count;
            line.SetPositions(points
                .Select(point => point + new Vector3(0f, 0f, zOffset))
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

        private void CreateAirIntentCircle(
            string objectName,
            Transform parent,
            Vector3 center,
            float radius,
            Color color,
            float width,
            float zOffset,
            int sortingOrder)
        {
            const int segmentCount = 36;
            var points = new Vector3[segmentCount + 1];
            for (var index = 0; index <= segmentCount; index++)
            {
                var angle = index / (float)segmentCount * Mathf.PI * 2f;
                points[index] = center + new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f);
            }

            CreateAirIntentPolyline(
                objectName,
                parent,
                points,
                color,
                width,
                zOffset,
                sortingOrder);
        }

        private void CreateAirIntentChevron(
            Transform parent,
            Vector3 tip,
            Vector3 direction,
            float size,
            Color color,
            int sortingOrder)
        {
            if (parent == null || direction.sqrMagnitude <= 0.0001f)
                return;

            direction = direction.normalized;
            var normal = new Vector3(-direction.y, direction.x, 0f);
            var left = tip - direction * size + normal * size * 0.55f;
            var right = tip - direction * size - normal * size * 0.55f;
            CreateAirIntentPolyline(
                "Air Intent Chevron",
                parent,
                new[] { left, tip, right },
                color,
                Mathf.Max(0.018f, size * 0.22f),
                -0.39f,
                sortingOrder);
        }

        private void CreateAirIntentLabel(
            Transform parent,
            Vector3 position,
            string text,
            Color color,
            int sortingOrder)
        {
            if (parent == null || string.IsNullOrWhiteSpace(text))
                return;

            var labelObject = new GameObject($"Air Intent Label {text}");
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.localPosition = position + new Vector3(0f, 0f, -0.43f);
            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = 0.014f;
            textMesh.fontSize = 22;
            textMesh.color = color;
            textMesh.text = text;
            labelObject.GetComponent<MeshRenderer>().sortingOrder = sortingOrder;
        }

        private static Vector3 GetFlightHeadingVector(AirFlight flight)
        {
            var heading = flight.HeadingDegrees * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Sin(heading), Mathf.Cos(heading), 0f);
            return direction.sqrMagnitude <= 0.0001f ? Vector3.up : direction.normalized;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Color GetAirMissionIntentColor(AirMissionRequestType mission, Alliance alliance)
        {
            return mission switch
            {
                AirMissionRequestType.OffensiveCounterAirSweep => new Color(1f, 0.48f, 0.18f),
                AirMissionRequestType.DestructionOfEnemyAirDefenses => new Color(1f, 0.22f, 0.12f),
                AirMissionRequestType.BarrierCombatAirPatrol => Color.Lerp(
                    GetAirAllianceColor(alliance),
                    new Color(0.20f, 1f, 0.72f),
                    0.35f),
                AirMissionRequestType.ProvideAirborneC2 => new Color(0.42f, 0.86f, 1f),
                AirMissionRequestType.ProvideAerialRefueling => new Color(0.38f, 1f, 0.46f),
                _ => GetAirAllianceColor(alliance)
            };
        }

        private static string GetAirIntentLabel(AirMissionRequestType mission)
        {
            return mission switch
            {
                AirMissionRequestType.OffensiveCounterAirSweep => "SWEEP",
                AirMissionRequestType.DestructionOfEnemyAirDefenses => "DEAD",
                AirMissionRequestType.BarrierCombatAirPatrol => "BARCAP",
                AirMissionRequestType.ProvideAirborneC2 => "C2",
                AirMissionRequestType.ProvideAerialRefueling => "TANKER",
                _ => "AIR"
            };
        }

        private void RefreshSamCoverageOverlay()
        {
            ClearSamCoverageOverlay();
            if (samOverlayRoot == null
                || overlaySamToggle == null
                || !overlaySamToggle.value
                || gameManager?.airDefenseSiteSystem == null)
                return;

            var activeModule = ModuleSingleton.Instance.ActiveModule;
            if (activeModule == null)
                return;

            var componentDefinitions = activeModule.SamComponentDefinitions
                .Where(definition => definition != null)
                .GroupBy(definition => definition.SamComponentDefinitionId)
                .ToDictionary(group => group.Key, group => group.First());
            var ordnanceDefinitions = activeModule.OrdnanceTypeDefinitions
                .Where(definition => definition != null)
                .GroupBy(definition => definition.OrdnanceTypeDefinitionId)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (var site in gameManager.airDefenseSiteSystem.Sites
                         .Where(site => site != null)
                         .OrderBy(site => site.SiteId))
            {
                if (!gameManager.airDefenseSiteSystem.TryGetTileId(site, out var tileId)
                    || !hexCentersByCell.TryGetValue(GetCell(tileId), out var center))
                    continue;

                var alliance = gameManager.airDefenseSiteSystem.GetEffectiveAlliance(site);
                var color = GetAirAllianceColor(alliance);
                var operational = !site.IsDisabled && !site.IsDestroyed && !site.IsSuppressed;
                var components = operational
                    ? gameManager.airDefenseSiteSystem.GetAvailableComponents(site).ToList()
                    : new List<AirDefenseComponent>();
                var radarRangeKm = GetSamRadarRangeKm(components, componentDefinitions);
                var engagementRangeKm = GetSamEngagementRangeKm(
                    components,
                    componentDefinitions,
                    ordnanceDefinitions);

                if (radarRangeKm > 0f)
                {
                    CreateAirIntentCircle(
                        $"SAM Radar Range {ShortId(site.SiteId)}",
                        samOverlayRoot,
                        center,
                        SamRangeKmToMapRadius(radarRangeKm),
                        WithAlpha(color, 0.20f),
                        SamCoverageLineWidth,
                        -0.23f,
                        17);
                }
                if (engagementRangeKm > 0f)
                {
                    CreateAirIntentCircle(
                        $"SAM Engagement Range {ShortId(site.SiteId)}",
                        samOverlayRoot,
                        center,
                        SamRangeKmToMapRadius(engagementRangeKm),
                        WithAlpha(color, 0.72f),
                        SamCoverageLineWidth * 1.6f,
                        -0.24f,
                        18);
                }

                CreateSamSiteIcon(site, center, color, operational, engagementRangeKm);
            }
        }

        private static float GetSamRadarRangeKm(
            IEnumerable<AirDefenseComponent> components,
            IReadOnlyDictionary<Guid, AirDefenseComponentDefinition> componentDefinitions)
        {
            return components
                .OfType<RadarAirDefenseComponent>()
                .Where(component => !component.IsDamaged
                                    && componentDefinitions.TryGetValue(
                                        component.SamComponentDefinitionId,
                                        out var definition)
                                    && definition is RadarAirDefenseComponentDefinition)
                .Select(component =>
                    ((RadarAirDefenseComponentDefinition)componentDefinitions[
                        component.SamComponentDefinitionId]).DetectionRangeKm)
                .DefaultIfEmpty(0f)
                .Max();
        }

        private static float GetSamEngagementRangeKm(
            IEnumerable<AirDefenseComponent> components,
            IReadOnlyDictionary<Guid, AirDefenseComponentDefinition> componentDefinitions,
            IReadOnlyDictionary<Guid, OrdnanceTypeDefinition> ordnanceDefinitions)
        {
            var maximumRangeKm = 0f;
            foreach (var launcher in components.OfType<LauncherAirDefenseComponent>()
                         .Where(component => !component.IsDamaged))
            {
                if (!componentDefinitions.TryGetValue(
                        launcher.SamComponentDefinitionId,
                        out var componentDefinition)
                    || componentDefinition is not LauncherAirDefenseComponentDefinition launcherDefinition
                    || !ordnanceDefinitions.TryGetValue(
                        launcherDefinition.SurfaceToAirOrdnanceTypeDefinitionId,
                        out var ordnance)
                    || ordnance.EmploymentCategory != OrdnanceEmploymentCategory.SurfaceToAir)
                    continue;

                maximumRangeKm = Mathf.Max(
                    maximumRangeKm,
                    Mathf.Min(launcherDefinition.MaxEngagementRangeKm, ordnance.MaximumRangeKm));
            }
            return maximumRangeKm;
        }

        private float SamRangeKmToMapRadius(float rangeKm)
        {
            var tileDistanceKm = Mathf.Max(
                SimulationSettings.MinTileDistanceKM,
                gameManager.SimulationSettings.TileDistanceKM);
            return Mathf.Max(0.05f, rangeKm / tileDistanceKm * HexHeight);
        }

        private void CreateSamSiteIcon(
            SamSite site,
            Vector3 center,
            Color allianceColor,
            bool operational,
            float engagementRangeKm)
        {
            var iconObject = new GameObject($"SAM Icon {ShortId(site.SiteId)}");
            iconObject.transform.SetParent(samOverlayRoot, false);
            iconObject.transform.localPosition = center + new Vector3(0f, 0.18f, -0.37f);
            var iconColor = operational
                ? allianceColor
                : new Color(0.50f, 0.50f, 0.50f, 0.82f);

            var iconLine = iconObject.AddComponent<LineRenderer>();
            iconLine.useWorldSpace = false;
            iconLine.loop = true;
            iconLine.positionCount = 4;
            iconLine.SetPositions(new[]
            {
                new Vector3(0f, SamIconRadius, 0f),
                new Vector3(SamIconRadius, 0f, 0f),
                new Vector3(0f, -SamIconRadius, 0f),
                new Vector3(-SamIconRadius, 0f, 0f)
            });
            iconLine.startWidth = 0.045f;
            iconLine.endWidth = 0.045f;
            iconLine.material = GetMovementArrowMaterial();
            iconLine.startColor = iconColor;
            iconLine.endColor = iconColor;
            iconLine.sortingOrder = 36;

            var symbolObject = new GameObject("SAM Symbol");
            symbolObject.transform.SetParent(iconObject.transform, false);
            symbolObject.transform.localPosition = new Vector3(0f, -0.002f, -0.01f);
            var symbol = symbolObject.AddComponent<TextMesh>();
            symbol.anchor = TextAnchor.MiddleCenter;
            symbol.alignment = TextAlignment.Center;
            symbol.characterSize = 0.017f;
            symbol.fontSize = 24;
            symbol.color = iconColor;
            symbol.text = "SAM";
            symbolObject.GetComponent<MeshRenderer>().sortingOrder = 37;

            var labelObject = new GameObject("SAM Label");
            labelObject.transform.SetParent(iconObject.transform, false);
            labelObject.transform.localPosition = new Vector3(0.20f, 0.04f, -0.02f);
            var label = labelObject.AddComponent<TextMesh>();
            label.anchor = TextAnchor.LowerLeft;
            label.alignment = TextAlignment.Left;
            label.characterSize = 0.013f;
            label.fontSize = 21;
            label.color = operational ? Color.white : new Color(0.72f, 0.72f, 0.72f);
            var hostLabel = site.HostType == SamSiteHostType.MobileDivision ? "MOBILE" : "STATIC";
            var siteState = GetSamSiteStateLabel(site);
            var statusLabel = siteState switch
            {
                "OPERATIONAL" => $"{engagementRangeKm:0} km",
                "DEGRADED" => $"DEGRADED | {engagementRangeKm:0} km",
                _ => siteState
            };
            var components = site.Components?
                .Where(component => component != null)
                .ToList() ?? new List<AirDefenseComponent>();
            var intactCount = components.Count(component =>
                !component.IsDamaged);
            label.text =
                $"{GetSamSiteDisplayName(site)}\n" +
                $"{hostLabel} | {statusLabel}\n" +
                $"{intactCount}/{components.Count} components intact";
            labelObject.GetComponent<MeshRenderer>().sortingOrder = 38;
        }

        private IEnumerable<SamSite> GetSamSitesOnTile(Vector3Int tileId)
        {
            if (gameManager?.airDefenseSiteSystem?.Sites == null)
                return Enumerable.Empty<SamSite>();

            return gameManager.airDefenseSiteSystem.Sites
                .Where(site =>
                    site != null
                    && gameManager.airDefenseSiteSystem.TryGetTileId(
                        site,
                        out var siteTileId)
                    && siteTileId == tileId)
                .OrderBy(site => GetSamSiteDisplayName(site))
                .ThenBy(site => site.SiteId)
                .ToList();
        }

        private IEnumerable<string> BuildSamSiteInspectorLinesForHost(
            Guid hostId)
        {
            if (gameManager?.airDefenseSiteSystem == null)
                return Enumerable.Empty<string>();

            return gameManager.airDefenseSiteSystem.GetSitesForHost(hostId)
                .Where(site => site != null)
                .OrderBy(site => GetSamSiteDisplayName(site))
                .ThenBy(site => site.SiteId)
                .SelectMany(BuildSamSiteInspectorLines)
                .ToList();
        }

        private IEnumerable<string> BuildSamSiteInspectorLines(SamSite site)
        {
            if (site == null)
                return Enumerable.Empty<string>();

            var components = site.Components?
                .Where(component => component != null)
                .OrderBy(component => GetSamComponentDisplayName(
                    site.SiteId,
                    component.ComponentId))
                .ThenBy(component => component.ComponentId)
                .ToList() ?? new List<AirDefenseComponent>();
            var intactCount = components.Count(component => !component.IsDamaged);
            var damagedCount = components.Count - intactCount;
            var lines = new List<string>
            {
                string.Empty,
                $"SAM SITE  {GetSamSiteDisplayName(site)}  {ShortId(site.SiteId)}",
                $"Site status  {GetSamSiteStateLabel(site)}",
                $"Host / alliance  {site.HostType} / {gameManager.airDefenseSiteSystem.GetEffectiveAlliance(site)}",
                $"Components  {intactCount} intact / {damagedCount} damaged / {components.Count} total"
            };

            if (components.Count == 0)
            {
                lines.Add("  No components.");
                return lines;
            }

            foreach (var component in components)
            {
                var componentName = GetSamComponentDisplayName(
                    site.SiteId,
                    component.ComponentId);
                var componentState = component.IsDamaged
                    ? "DAMAGED"
                    : site.IsSuppressed
                        ? "INTACT (SITE SUPPRESSED)"
                        : site.IsDisabled
                            ? "INTACT (SITE DISABLED)"
                            : "OPERATIONAL";
                var detail = component switch
                {
                    RadarAirDefenseComponent radar =>
                        $"  •  {(radar.IsEmitting ? "EMITTING" : "SILENT")}"
                        + (radar.IsEmitting || radar.LastEmittedAt == default
                            ? string.Empty
                            : $" / last emission {radar.LastEmittedAt:MM-dd HH:mm:ss}")
                        + (radar.EmissionHoldUntil <= gameManager.CurrentTime
                            ? string.Empty
                            : $" / held until {radar.EmissionHoldUntil:MM-dd HH:mm:ss}"),
                    LauncherAirDefenseComponent launcher =>
                        $"  •  ready {launcher.ReadyRounds} / reserve {launcher.ReserveRounds}"
                        + (launcher.NextReloadAt == default
                            ? string.Empty
                            : $" / reload {launcher.NextReloadAt:MM-dd HH:mm:ss}"),
                    _ => string.Empty
                };
                lines.Add(
                    $"  [{componentState}] {componentName}  •  " +
                    $"{GetSamComponentTypeLabel(component)}  •  " +
                    $"{ShortId(component.ComponentId)}{detail}");
            }

            return lines;
        }

        private static string GetSamSiteStateLabel(SamSite site)
        {
            if (site.IsDestroyed)
                return "DESTROYED";
            if (site.IsDisabled)
                return "DISABLED";
            if (site.IsSuppressed)
                return "SUPPRESSED";

            var components = site.Components?
                .Where(component => component != null)
                .ToList() ?? new List<AirDefenseComponent>();
            if (components.Count == 0
                || components.All(component => component.IsDamaged))
                return "COMBAT INEFFECTIVE";
            return components.Any(component => component.IsDamaged)
                ? "DEGRADED"
                : "OPERATIONAL";
        }

        private static string GetSamComponentTypeLabel(
            AirDefenseComponent component)
        {
            return component switch
            {
                RadarAirDefenseComponent => "Radar",
                LauncherAirDefenseComponent => "Launcher",
                CommandAirDefenseComponent => "Command",
                SupportAirDefenseComponent => "Support",
                _ => "Component"
            };
        }

        private string GetSamComponentDisplayName(
            Guid siteId,
            Guid componentId)
        {
            if (gameManager?.airDefenseSiteSystem == null
                || !gameManager.airDefenseSiteSystem.TryGetSite(
                    siteId,
                    out var site))
                return $"component {ShortId(componentId)}";

            var component = site.Components?
                .FirstOrDefault(candidate =>
                    candidate != null
                    && candidate.ComponentId == componentId);
            if (component == null)
                return $"component {ShortId(componentId)}";

            var definition = ModuleSingleton.Instance?.ActiveModule
                ?.SamComponentDefinitions
                ?.FirstOrDefault(candidate =>
                    candidate != null
                    && candidate.SamComponentDefinitionId
                    == component.SamComponentDefinitionId);
            return definition?.Name ?? $"component {ShortId(componentId)}";
        }

        private static string GetSamSiteDisplayName(SamSite site)
        {
            var template = ModuleSingleton.Instance.ActiveModule.SamSiteTemplates
                .FirstOrDefault(candidate => candidate.SamSiteTemplateId == site.SamSiteTemplateId);
            return template?.Name ?? $"SAM {ShortId(site.SiteId)}";
        }

        private void RefreshOrdnanceOverlay()
        {
            ClearOrdnanceOverlay();
            if (ordnanceOverlayRoot == null
                || overlayOrdnanceToggle == null
                || !overlayOrdnanceToggle.value
                || gameManager == null)
                return;

            var pendingEffectIds = gameManager.GetPendingOrdnanceEffects()
                .Select(effect => effect.PendingEffectId)
                .ToHashSet();
            var records = gameManager.GetOrdnanceEmploymentRecords()
                .Where(record => record.Stage == OrdnanceEmploymentRecordStage.OrdnanceReleased
                                 && pendingEffectIds.Contains(record.PendingEffectId))
                .ToList();
            foreach (var record in records)
            {
                var endpoints = GetOrdnanceVisualEndpoints(record);
                var source = endpoints.source;
                var target = endpoints.target;
                if (Vector3.Distance(source, target) <= 0.01f)
                    continue;
                var color = record.SourceKind == OrdnanceEmploymentSourceKind.SamLauncher
                    ? new Color(1f, 0.62f, 0.16f)
                    : new Color(1f, 0.86f, 0.24f);
                var launches = GetRecordLaunches(record).ToList();
                for (var index = 0; index < launches.Count; index++)
                    CreateOrdnanceLaunchOverlay(
                        record,
                        launches[index],
                        index,
                        launches.Count,
                        source,
                        target,
                        color);
            }
        }

        private void CreateOrdnanceLaunchOverlay(
            OrdnanceEmploymentRecord record,
            OrdnanceLaunchDiagnostic launch,
            int index,
            int count,
            Vector3 source,
            Vector3 target,
            Color color)
        {
            var selected = selectedOrdnancePassId != Guid.Empty
                           && record.EmploymentPassId == selectedOrdnancePassId;
            var direction = target - source;
            var normal = direction.sqrMagnitude <= 0.0001f
                ? Vector3.right
                : Vector3.Cross(direction.normalized, Vector3.forward).normalized;
            var spread = count <= 1 ? 0f : (index - (count - 1) * 0.5f) * 0.055f;
            var offset = normal * spread;
            var lineObject = new GameObject(
                $"Ordnance Launch {ShortId(record.EmploymentPassId)}-{launch.Sequence}");
            lineObject.transform.SetParent(ordnanceOverlayRoot, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPositions(new[]
            {
                source + offset + new Vector3(0f, 0f, -0.50f),
                target + offset + new Vector3(0f, 0f, -0.50f)
            });
            line.startWidth = selected ? 0.070f : 0.036f;
            line.endWidth = selected ? 0.030f : 0.014f;
            line.material = GetMovementArrowMaterial();
            var selectedColor = selected ? new Color(0.28f, 1f, 0.70f) : color;
            line.startColor = selectedColor;
            line.endColor = selected
                ? new Color(1f, 0.28f, 0.22f, 0.9f)
                : new Color(color.r, color.g, color.b, 0.35f);
            line.sortingOrder = selected ? 52 : 45;
            CreateOrdnanceEndpoint(source + offset, selectedColor, $"Shooter {launch.Sequence}");
            CreateOrdnanceEndpoint(
                target + offset,
                selected ? new Color(1f, 0.18f, 0.16f) : new Color(1f, 0.30f, 0.22f),
                $"Target {launch.Sequence}");
            CreateOrdnanceMapLabel(
                Vector3.Lerp(source, target, 0.52f) + offset,
                $"L{launch.Sequence} {GetSourceLabel(record)}→"
                + GetOrdnanceTargetLabel(record),
                selected);
        }

        private void CreateOrdnanceMapLabel(Vector3 position, string text, bool selected)
        {
            var labelObject = new GameObject("Ordnance Launch Label");
            labelObject.transform.SetParent(ordnanceOverlayRoot, false);
            labelObject.transform.localPosition = position + new Vector3(0.05f, 0.05f, -0.52f);
            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.anchor = TextAnchor.LowerLeft;
            textMesh.alignment = TextAlignment.Left;
            textMesh.characterSize = selected ? 0.016f : 0.012f;
            textMesh.fontSize = selected ? 24 : 20;
            textMesh.color = selected ? new Color(1f, 1f, 0.70f) : new Color(1f, 0.92f, 0.42f);
            textMesh.text = text;
            labelObject.GetComponent<MeshRenderer>().sortingOrder = selected ? 54 : 47;
        }

        private void CreateOrdnanceEndpoint(Vector3 position, Color color, string name)
        {
            var endpoint = new GameObject($"Ordnance {name}");
            endpoint.transform.SetParent(ordnanceOverlayRoot, false);
            endpoint.transform.localPosition = position;
            var line = endpoint.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 12;
            var points = new Vector3[12];
            for (var index = 0; index < points.Length; index++)
            {
                var angle = index / (float)points.Length * Mathf.PI * 2f;
                points[index] = new Vector3(Mathf.Cos(angle) * 0.14f, Mathf.Sin(angle) * 0.14f, -0.51f);
            }
            line.SetPositions(points);
            line.startWidth = 0.035f;
            line.endWidth = 0.035f;
            line.material = GetMovementArrowMaterial();
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = 46;
        }

        private void CreateAirInspection()
        {
            if (airInspectionRoot == null)
                return;

            if (inspectedPackageId != Guid.Empty)
            {
                var packageInspection = new[] { Alliance.Bluefor, Alliance.Redfor }
                    .Select(alliance => gameManager.GetAllianceAirTaskingCommander(alliance))
                    .Where(commander => commander != null)
                    .SelectMany(commander => commander.Packages)
                    .FirstOrDefault(candidate => candidate.PackageId == inspectedPackageId);
                if (packageInspection == null)
                    return;
                foreach (var packageFlight in packageInspection.Flights)
                {
                    var points = new List<Vector3>();
                    if (packageFlight.HasPosition)
                        points.Add(AirPositionToMapPosition(packageFlight.PositionFeet));
                    points.AddRange(packageFlight.Route
                        .Skip(Mathf.Clamp(packageFlight.CurrentWaypointIndex, 0, packageFlight.Route.Count))
                        .Select(waypoint => AirPositionToMapPosition(waypoint.PositionFeet)));
                    if (points.Count >= 2)
                        CreateInspectionPolyline(
                            $"Package Flight {ShortId(packageFlight.FlightId)}",
                            points,
                            Color.Lerp(GetAirAllianceColor(packageInspection.Alliance), Color.white, 0.18f),
                            0.065f,
                            36);
                }
                return;
            }

            if (inspectedFlightId == Guid.Empty
                || !TryFindFlight(inspectedFlightId, out var flight, out var package, out var commander))
                return;

            var alliance = package.Alliance;
            var allianceColor = GetAirAllianceColor(alliance);
            var route = flight.Route
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
            var points = flight.Route
                .Select(waypoint => AirPositionToMapPosition(waypoint.PositionFeet))
                .ToList();
            if (flight.HasPosition)
                points.Add(AirPositionToMapPosition(flight.PositionFeet));
            if (points.Count == 0)
                return;

            FrameMapPoints(points);
        }

        private void FrameMapPoints(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count == 0 || sceneCamera == null)
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
            routePoints.AddRange(flight.Route
                .Skip(Mathf.Clamp(flight.CurrentWaypointIndex, 0, flight.Route.Count))
                .Select(waypoint => AirPositionToMapPosition(waypoint.PositionFeet)));

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
            var hasTacticalGuidance = IsSelectedFlightUsingCounterAirGuidance(flight);
            color.a = hasTacticalGuidance ? 0.22f : 0.72f;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            if (hasTacticalGuidance)
            {
                lineRenderer.startWidth = AirRouteLineWidth * 0.65f;
                lineRenderer.endWidth = AirRouteLineWidth * 0.65f;
            }
            lineRenderer.sortingOrder = 24;
        }

        private bool IsSelectedFlightUsingCounterAirGuidance(AirFlight flight)
        {
            return selectedFlightId != Guid.Empty
                   && flight.FlightId == selectedFlightId
                   && TryFindFlight(flight.FlightId, out _, out var package, out _)
                   && TryFindCounterAirGuidance(package, flight, out _, out _);
        }

        private void CreateAirMarker(AirFlight flight, Alliance alliance)
        {
            var markerObject = new GameObject($"Air Flight {ShortId(flight.FlightId)}");
            markerObject.transform.SetParent(airOverlayRoot, false);
            var logicalPosition = AirPositionToMapPosition(flight.PositionFeet);
            var overlapIndex = flightPickTargets.Count(target =>
                Vector3.Distance(target.MapPosition, logicalPosition) < 0.08f);
            var offsetAngle = overlapIndex * 137.5f * Mathf.Deg2Rad;
            var offset = overlapIndex == 0
                ? Vector3.zero
                : new Vector3(Mathf.Cos(offsetAngle), Mathf.Sin(offsetAngle), 0f) * 0.10f;
            markerObject.transform.localPosition = logicalPosition + offset;
            flightPickTargets.Add(new FlightPickTarget(
                flight.FlightId,
                markerObject.transform.localPosition));

            var markerLine = markerObject.AddComponent<LineRenderer>();
            markerLine.useWorldSpace = false;
            markerLine.loop = true;
            markerLine.positionCount = 3;
            var heading = flight.HeadingDegrees * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(heading), Mathf.Cos(heading), 0f);
            var right = new Vector3(forward.y, -forward.x, 0f);
            markerLine.SetPositions(new[]
            {
                forward * (AirMarkerRadius * 1.35f) + new Vector3(0f, 0f, -0.34f),
                -forward * AirMarkerRadius + right * AirMarkerRadius + new Vector3(0f, 0f, -0.34f),
                -forward * AirMarkerRadius - right * AirMarkerRadius + new Vector3(0f, 0f, -0.34f)
            });
            markerLine.startWidth = 0.055f;
            markerLine.endWidth = 0.055f;
            markerLine.numCornerVertices = 2;
            markerLine.material = GetMovementArrowMaterial();
            markerLine.startColor = GetAirAllianceColor(alliance);
            markerLine.endColor = GetAirAllianceColor(alliance);
            markerLine.sortingOrder = 27;

            var squadron = gameManager.squadronSystem.Squadrons
                .First(candidate => candidate.SquadronId == flight.SquadronId);
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
                $"{GetFlightName(flight, squadron)} ×{flight.AircraftIds.Count}\n" +
                $"{GetMissionLabel(flight.MissionType)} • {flight.PositionFeet.y / 1000f:0.#}k ft";
            labelObject.GetComponent<MeshRenderer>().sortingOrder = 28;
        }

        private Vector3 AirPositionToMapPosition(Vector3 positionFeet)
        {
            var spacingFeet = Math.Max(
                0.001f,
                gameManager.SimulationSettings.TileDistanceKM
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

            if (gameManager.tileSystem.TryGetLand(destinationTileId, out var landTile)
                && GroundSystemUtility.AreHostile(divisionAlliance, landTile.Controller))
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

            foreach (var campaignTile in gameManager.tileSystem.LandTiles)
            {
                if (!TileHasRailroad(campaignTile.TileId))
                    continue;

                var fromCell = GetCell(campaignTile.TileId);
                if (!hexCentersByCell.TryGetValue(fromCell, out var fromCenter))
                    continue;

                for (var sideIndex = 0; sideIndex < TerritoryBorderNeighborOffsets.Length; sideIndex++)
                {
                    var neighborId = campaignTile.TileId + TerritoryBorderNeighborOffsets[sideIndex];
                    if (!ShouldDrawRailLink(campaignTile.TileId, neighborId) || !TileHasRailroad(neighborId))
                        continue;

                    var toCell = GetCell(neighborId);
                    if (!hexCentersByCell.TryGetValue(toCell, out var toCenter))
                        continue;

                    CreateRailwayLine(fromCenter, toCenter, campaignTile.TileId, neighborId);
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
                CreateCombatBubble(
                    combat.DefendingTileId,
                    bubbleCenter,
                    score,
                    combat.AttackingAlliance,
                    combat.DefendingAlliance);
            }
        }

        private Vector3 GetAverageAttackerCenter(GroundCombat combat)
        {
            var centers = combat.AttackerDivisionIds
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
            foreach (var divisionId in divisionIds)
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

        private void CreateCombatBubble(
            Vector3Int defendingTileId,
            Vector3 hexCenter,
            int score,
            Alliance attackingAlliance,
            Alliance defendingAlliance)
        {
            combatPickTargets.Add(new CombatPickTarget(defendingTileId, hexCenter));
            var bubbleObject = new GameObject($"Combat Bubble {defendingTileId.x},{defendingTileId.y},{defendingTileId.z}");
            bubbleObject.transform.SetParent(combatBubbleRoot, false);
            bubbleObject.transform.position = grid.transform.TransformPoint(hexCenter) + new Vector3(0f, 0f, -0.35f);

            var renderer = bubbleObject.AddComponent<SpriteRenderer>();
            renderer.sprite = GetCombatBubbleSprite(score, attackingAlliance, defendingAlliance);
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

        private Sprite GetCombatBubbleSprite(
            int score,
            Alliance attackingAlliance,
            Alliance defendingAlliance)
        {
            score = Mathf.Clamp(score, 0, 100);
            var key = $"{score}:{attackingAlliance}:{defendingAlliance}";
            if (combatBubbleSpritesByScore.TryGetValue(key, out var sprite))
                return sprite;

            var texture = CreateCombatBubbleTexture(score, attackingAlliance, defendingAlliance);
            sprite = Sprite.Create(
                texture,
                new Rect(0, 0, CombatBubblePixelSize, CombatBubblePixelSize),
                new Vector2(0.5f, 0.5f),
                CombatBubblePixelSize / CombatBubbleWorldWidth);
            combatBubbleSpritesByScore[key] = sprite;
            return sprite;
        }

        private Texture2D CreateCombatBubbleTexture(
            int score,
            Alliance attackingAlliance,
            Alliance defendingAlliance)
        {
            var pixels = new Color[CombatBubblePixelSize * CombatBubblePixelSize];
            var center = (CombatBubblePixelSize - 1) * 0.5f;
            var radius = center - 1f;
            var attackerColor = GetControlColor(attackingAlliance);
            var defenderColor = GetControlColor(defendingAlliance);
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

                    var attackerShareBoundary = CombatBubblePixelSize * Mathf.Clamp01(score / 100f);
                    var fill = x < attackerShareBoundary ? attackerColor : defenderColor;
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
            return assignment.Alliance;
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

            var screenPosition = mouse.position.ReadValue();
            if (Vector2.Distance(screenPosition, lastPickScreenPosition) > 8f)
            {
                currentPickIndex = 0;
                currentPickTargets.Clear();
            }
            lastPickScreenPosition = screenPosition;
            var picks = BuildMapPickTargets(worldPosition, cell.Value);
            if (!AreSamePickTargets(currentPickTargets, picks))
            {
                currentPickTargets.Clear();
                currentPickTargets.AddRange(picks);
                currentPickIndex = 0;
            }
            if (currentPickTargets.Count == 0)
                return;

            var pick = currentPickTargets[currentPickIndex % currentPickTargets.Count];
            currentPickIndex = (currentPickIndex + 1) % currentPickTargets.Count;
            switch (pick.Kind)
            {
                case MapPickKind.Flight:
                    OpenFlightDetails(pick.FlightId);
                    break;
                case MapPickKind.Combat:
                    var combat = gameManager.GetActiveGroundCombats()
                        .FirstOrDefault(candidate => candidate.DefendingTileId == pick.TileId);
                    if (combat != null)
                        OpenCombatInspector(combat);
                    break;
                default:
                    selectedCell = cell.Value;
                    ShowWorkbenchPage(WorkbenchPage.Tile);
                    UpdateSelectedTileUi();
                    break;
            }
        }

        private List<MapPickTarget> BuildMapPickTargets(Vector3 worldPosition, Vector3Int cell)
        {
            var result = flightPickTargets
                .Where(target => Vector3.Distance(
                    worldPosition,
                    grid.transform.TransformPoint(target.MapPosition)) <= 0.24f)
                .OrderBy(target => target.FlightId)
                .Select(target => MapPickTarget.ForFlight(target.FlightId))
                .ToList();
            result.AddRange(combatPickTargets
                .Where(target => Vector3.Distance(
                    worldPosition,
                    grid.transform.TransformPoint(target.MapPosition)) <= 0.28f)
                .OrderBy(target => target.TileId.x)
                .ThenBy(target => target.TileId.y)
                .Select(target => MapPickTarget.ForCombat(target.TileId)));
            if (tilesByCell.ContainsKey(cell))
                result.Add(MapPickTarget.ForTile(tilesByCell[cell].TileId));
            return result;
        }

        private static bool AreSamePickTargets(
            IReadOnlyList<MapPickTarget> left,
            IReadOnlyList<MapPickTarget> right)
        {
            if (left.Count != right.Count)
                return false;
            for (var index = 0; index < left.Count; index++)
            {
                if (!left[index].Equals(right[index]))
                    return false;
            }
            return true;
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

        private void SelectPlaybackIncrement(CampaignPlaybackIncrement increment)
        {
            if (gameManager == null)
                return;

            gameManager.SetPlaybackIncrement(increment);
            UpdateTimeUi();
        }

        private UnityEngine.Tilemaps.Tile GetRenderTile(RuntimeTile campaignTile)
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

        private string GetTileVisualKey(RuntimeTile campaignTile)
        {
            var controller = campaignTile is RuntimeLandTile landTile
                ? landTile.Controller
                : Alliance.Neutral;

            if (campaignTile.Surface == TileSurface.Ocean)
                return $"Ocean:{campaignTile.Terrain}";

            var borderMask = AreTerritoryBoundariesVisible()
                ? GetTerritoryBorderMask(campaignTile, controller)
                : 0;
            var supplyOverlay = GetSupplyOverlayKey(campaignTile.TileId);
            return $"Land:{campaignTile.Terrain}:{campaignTile.Urbanization}:{campaignTile.ForestCover}:{controller}:{borderMask}:{supplyOverlay}";
        }

        private Sprite GetTileSprite(RuntimeTile campaignTile, string key)
        {
            if (spritesByKey.TryGetValue(key, out var sprite))
                return sprite;

            var controller = campaignTile is RuntimeLandTile landTile
                ? landTile.Controller
                : Alliance.Neutral;
            var borderMask = AreTerritoryBoundariesVisible()
                ? GetTerritoryBorderMask(campaignTile, controller)
                : 0;

            var texture = CreateTileTexture(campaignTile, controller, borderMask);
            sprite = Sprite.Create(
                texture,
                new Rect(0, 0, TilePixelSize, TilePixelSize),
                new Vector2(0.5f, 0.5f),
                TilePixelSize);
            spritesByKey[key] = sprite;
            return sprite;
        }

        private Texture2D CreateTileTexture(RuntimeTile campaignTile, Alliance controller, int borderMask)
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

        private bool AreTerritoryBoundariesVisible()
        {
            return overlayTerritoryBoundariesToggle == null
                   || overlayTerritoryBoundariesToggle.value;
        }

        private void FillTerrainPixels(Color[] pixels, RuntimeTile campaignTile)
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

        private void ApplySupplyInfrastructure(Color[] pixels, RuntimeTile campaignTile)
        {
            if (gameManager?.buildingSystem == null)
                return;

            var tileId = campaignTile.TileId;
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

        private int GetTerritoryBorderMask(RuntimeTile campaignTile, Alliance controller)
        {
            var borderMask = 0;
            for (var sideIndex = 0; sideIndex < TerritoryBorderSideCount; sideIndex++)
            {
                var neighborId = campaignTile.TileId + TerritoryBorderNeighborOffsets[sideIndex];
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
            if (gameManager.tileSystem.TryGetLand(tileId, out var landTile))
            {
                controller = landTile.Controller;
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

        private void CreateTileLabel(RuntimeTile campaignTile, Vector3 hexCenter)
        {
            var labelObject = new GameObject($"Hex Label {campaignTile.TileId.x},{campaignTile.TileId.y},{campaignTile.TileId.z}");
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

        private string GetTileLabel(RuntimeTile campaignTile)
        {
            var buildingCount = gameManager.buildingSystem.GetBuildingsOnTile(campaignTile.TileId).Count;
            var coords = campaignTile.TileId;
            var hexLine = $"Hex {coords.x},{coords.y},{coords.z}";
            var supplyFeatures = GetSupplyFeatureLabel(campaignTile.TileId);

            if (campaignTile.Surface == TileSurface.Ocean)
                return $"{GetTerrainLabel(campaignTile.Terrain)}\n{hexLine}";

            var controller = campaignTile is RuntimeLandTile landTile
                ? GetControllerLabel(landTile.Controller)
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

        private void ClearSamCoverageOverlay()
        {
            if (samOverlayRoot == null)
                return;

            for (var i = samOverlayRoot.childCount - 1; i >= 0; i--)
                Destroy(samOverlayRoot.GetChild(i).gameObject);
        }

        private void AdvanceOnePlaybackIncrement()
        {
            if (gameManager == null || !gameManager.IsGamePaused)
                return;

            gameManager.AdvanceOnePlaybackIncrement();
            UpdateTimeUi();
        }

        private void ClearOrdnanceOverlay()
        {
            if (ordnanceOverlayRoot == null)
                return;

            for (var i = ordnanceOverlayRoot.childCount - 1; i >= 0; i--)
                Destroy(ordnanceOverlayRoot.GetChild(i).gameObject);
        }

        private sealed class PinnedInspector
        {
            public readonly VisualElement Window;
            public readonly VisualElement Content;
            public readonly Func<IEnumerable<string>> Lines;

            public PinnedInspector(
                VisualElement window,
                VisualElement content,
                Func<IEnumerable<string>> lines)
            {
                Window = window;
                Content = content;
                Lines = lines;
            }
        }

        private readonly struct FlightPickTarget
        {
            public readonly Guid FlightId;
            public readonly Vector3 MapPosition;

            public FlightPickTarget(Guid flightId, Vector3 mapPosition)
            {
                FlightId = flightId;
                MapPosition = mapPosition;
            }
        }

        private readonly struct CombatPickTarget
        {
            public readonly Vector3Int TileId;
            public readonly Vector3 MapPosition;

            public CombatPickTarget(Vector3Int tileId, Vector3 mapPosition)
            {
                TileId = tileId;
                MapPosition = mapPosition;
            }
        }

        private enum MapPickKind
        {
            Flight,
            Combat,
            Tile
        }

        private readonly struct MapPickTarget : IEquatable<MapPickTarget>
        {
            public readonly MapPickKind Kind;
            public readonly Guid FlightId;
            public readonly Vector3Int TileId;

            private MapPickTarget(MapPickKind kind, Guid flightId, Vector3Int tileId)
            {
                Kind = kind;
                FlightId = flightId;
                TileId = tileId;
            }

            public static MapPickTarget ForFlight(Guid flightId) =>
                new MapPickTarget(MapPickKind.Flight, flightId, default);

            public static MapPickTarget ForCombat(Vector3Int tileId) =>
                new MapPickTarget(MapPickKind.Combat, Guid.Empty, tileId);

            public static MapPickTarget ForTile(Vector3Int tileId) =>
                new MapPickTarget(MapPickKind.Tile, Guid.Empty, tileId);

            public bool Equals(MapPickTarget other) =>
                Kind == other.Kind && FlightId == other.FlightId && TileId == other.TileId;
        }

        private readonly struct DiagnosticRow
        {
            public readonly DateTime RecordedAt;
            public readonly string Severity;
            public readonly string System;
            public readonly string Text;

            public DiagnosticRow(
                DateTime recordedAt,
                string severity,
                string system,
                string text)
            {
                RecordedAt = recordedAt;
                Severity = severity;
                System = system;
                Text = text;
            }
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
            if (sceneCamera == null || gameManager.tileSystem.Count == 0)
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
