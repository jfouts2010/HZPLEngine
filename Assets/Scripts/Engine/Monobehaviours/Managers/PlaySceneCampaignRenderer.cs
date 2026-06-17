using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        private Label titleLabel;
        private Label timeLabel;
        private Label selectedTileLabel;
        private Foldout neighborsFoldout;
        private VisualElement neighborsList;
        private Foldout unitsFoldout;
        private VisualElement unitsList;
        private Button pauseButton;
        private VisualElement hudRoot;
        private VisualElement hudPanel;
        private Font runtimeFont;
        private Vector3Int? selectedCell;

        private IEnumerator Start()
        {
            gameManager = gameManager != null ? gameManager : GetComponent<GameManager>();
            sceneCamera = sceneCamera != null ? sceneCamera : Camera.main;

            EnsureTilemap();
            EnsureEventSystem();
            if (!EnsureUi())
                yield break;

            yield return null;

            RenderCampaign();
        }

        private void Update()
        {
            if (gameManager == null || !gameManager.IsCampaignStarted)
                return;

            UpdateTimeUi();
            HandleTileSelection();
        }

        private void RenderCampaign()
        {
            if (gameManager == null || !gameManager.IsCampaignStarted)
                return;

            tilemap.ClearAllTiles();
            tilesByCell.Clear();
            hexCentersByCell.Clear();
            tilesById.Clear();
            tileDataById.Clear();

            foreach (var tileData in gameManager.Tiles.Where(tileData => tileData != null))
                tileDataById[tileData.TileId] = tileData;

            foreach (var campaignTile in gameManager.CampaignTiles.Where(tile => tile != null))
                tilesById[campaignTile.Coordinates] = campaignTile;

            ClearLabels();
            ClearUnitCounters();

            foreach (var campaignTile in gameManager.CampaignTiles.Where(tile => tile != null))
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

            tilemap.RefreshAllTiles();
            FrameCamera();
            SelectFirstTile();
            UpdateSummaryUi();
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
            hudRoot = root.Q<VisualElement>("campaign-hud-root");
            hudPanel = root.Q<VisualElement>("campaign-hud-panel");

            ApplyRuntimeFont(titleLabel);
            ApplyRuntimeFont(timeLabel);
            ApplyRuntimeFont(selectedTileLabel);
            ApplyRuntimeFont(pauseButton);

            if (hudRoot != null)
                hudRoot.pickingMode = PickingMode.Ignore;

            if (hudPanel != null)
                hudPanel.pickingMode = PickingMode.Position;

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

            if (titleLabel == null || timeLabel == null || selectedTileLabel == null || pauseButton == null)
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

            textElement.style.color = Color.white;
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

        private void UpdateTimeUi()
        {
            if (timeLabel == null || gameManager == null)
                return;

            timeLabel.text = $"{gameManager.GameTime:yyyy-MM-dd HH:mm} | Tiles: {gameManager.CampaignTiles.Count}";
            if (pauseButton != null)
                pauseButton.text = gameManager.IsGamePaused ? "Resume" : "Pause";
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

            selectedTileLabel.text =
                $"Hex {selectedTile.Coordinates.x}, {selectedTile.Coordinates.y}, {selectedTile.Coordinates.z}\n" +
                $"{selectedTile.Surface} | {selectedTile.Terrain}\n" +
                $"Settlement: {selectedTile.Urbanization} | Forest: {selectedTile.ForestCover}\n" +
                $"Control: {controller}\n" +
                $"Infrastructure: {infrastructure}\n" +
                buildingText;

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

            var stats = new Label(
                $"Org {Mathf.RoundToInt(100)}% | Strength {Mathf.RoundToInt(100)}% | Speed {division.Speed:0.#}");
            stats.AddToClassList("campaign-hud-unit-stat");
            ApplyRuntimeFont(stats);

            card.Add(name);
            card.Add(stats);
            return card;
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
                         .Where(division => division != null)
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
            if (sceneCamera == null || tilemap == null || mouse == null || !mouse.leftButton.wasPressedThisFrame)
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
            return $"Land:{campaignTile.Terrain}:{campaignTile.Urbanization}:{campaignTile.ForestCover}:{controller}:{borderMask}";
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

            return $"{controller} {terrain}\n{detailLine}\n{buildings}\n{hexLine}";
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
