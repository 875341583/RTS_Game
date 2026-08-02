using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// P1-3: 地图编辑器 — 可视化编辑 32×32 等距地形。
///
/// 功能：
/// - 笔刷绘制地形/海拔/桥梁/隧道
/// - 5种笔刷模式（单格/3×3方/5圆/填充/橡皮）
/// - 放置矿点和战略点
/// - 保存/加载 .rmap 地图文件
/// - 实时等距预览（复用 IsoTerrainRenderer）
/// - 从主菜单进入
///
/// 交互：
/// - 左键：应用当前笔刷
/// - 右键拖动：平移视图
/// - 滚轮：缩放
/// - 数字键1-5：切换笔刷模式
/// - Ctrl+S：保存  Ctrl+O：加载  Ctrl+N：新建
/// </summary>
public partial class MapEditor : Control
{
    // ======== 核心数据 ========
    private TerrainGrid _terrain = null!;
    private MapData _mapData = null!;
    private Random _rng = new(42);
    private ulong _currentSeed = 42;

    // ======== 渲染节点 ========
    private Sprite2D _groundSprite = null!;
    private Camera2D _camera = null!;
    private Node2D _overlayLayer = null!;    // 鼠标悬停高亮、笔刷预览
    private Node2D _markerLayer = null!;     // 矿点/战略点标记

    // ======== UI 控件引用 ========
    private Label _statusLabel = null!;
    private Label _coordLabel = null!;
    private OptionButton _toolSelect = null!;
    private OptionButton _terrainSelect = null!;
    private OptionButton _elevationSelect = null!;
    private OptionButton _brushModeSelect = null!;
    private LineEdit _mapNameInput = null!;
    private LineEdit _seedInput = null!;
    private CheckBox _bridgeCheck = null!;
    private CheckBox _tunnelCheck = null!;
    private SpinBox _resourceAmountBox = null!;
    private OptionButton _placeModeSelect = null!;

    // ======== 视图状态 ========
    private float _zoom = 1.0f;
    private bool _isPanning = false;
    private Vector2 _panStart = Vector2.Zero;

    // ======== 拖拽连续绘制状态 ========
    private bool _isPainting = false;

    // ======== 撤销/重做 ========
    private readonly Stack<MapData> _undoStack = new();
    private readonly Stack<MapData> _redoStack = new();
    private const int MaxUndoStack = 50;  // 最多保留50步历史

    // ======== 当前指针所在的网格坐标 ========
    private int _hoverGx = -1, _hoverGy = -1;

    // ======== 放置模式 ========
    private enum PlaceMode
    {
        /// <summary>绘制地形</summary>
        Terrain,
        /// <summary>放置矿点</summary>
        Resource,
        /// <summary>放置战略点</summary>
        Strategic,
        /// <summary>放置基地出生点</summary>
        Base,
    }

    private PlaceMode _placeMode = PlaceMode.Terrain;

    // ======== 地形类型显示名（存储i18n key，显示时翻译） ========
    private static readonly (TerrainType type, string key)[] TerrainOptions =
    {
        (TerrainType.Grass,       "terrain.grass"),
        (TerrainType.Sand,        "terrain.sand"),
        (TerrainType.Snow,        "terrain.snow"),
        (TerrainType.City,        "terrain.city"),
        (TerrainType.Field,       "terrain.field"),
        (TerrainType.ShallowWater,"terrain.shallow"),
        (TerrainType.DeepWater,   "terrain.deep"),
        (TerrainType.Mountain,    "terrain.mountain"),
        (TerrainType.Road,        "terrain.road"),
        (TerrainType.Cliff,       "terrain.cliff"),
        (TerrainType.Bridge,      "terrain.bridge"),
        (TerrainType.Tunnel,      "terrain.tunnel"),
    };

    private static readonly string[] BrushModeKeys =
    {
        "brush.single",
        "brush.square3",
        "brush.circle5",
        "brush.fill",
        "brush.eraser",
    };

    // ====================================================================
    //                          生命周期
    // ====================================================================

    public override void _Ready()
    {
        // 命令行参数：可指定初始地图文件
        var args = OS.GetCmdlineArgs();
        string? loadPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--map=", StringComparison.OrdinalIgnoreCase))
                loadPath = args[i].Substring(6);
        }

        // 初始化数据
        _terrain = new TerrainGrid();
        _mapData = new MapData { Name = TrManager.Tr("editor.new_map_name"), Seed = 42 };

        if (loadPath != null && System.IO.File.Exists(loadPath))
        {
            var loaded = MapData.LoadFromFile(loadPath);
            if (loaded != null)
            {
                _mapData = loaded;
                _currentSeed = loaded.Seed;
                GameLog.Info($"[MapEditor] map loaded from file: {loadPath}");
            }
        }

        _terrain.GenerateFromSeed(_currentSeed);
        ApplyMapDataToTerrain();

        // 构建UI
        BuildUI();

        // 构建渲染层
        BuildRenderLayers();

        // 首次渲染
        RefreshGround();
        RefreshMarkers();

        GameLog.Info($"[MapEditor] editor ready (seed={_currentSeed}, mods={_mapData.TerrainMods.Count})");
    }

    public override void _Input(InputEvent @event)
    {
        // 右键拖动平移
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                if (mb.Pressed)
                {
                    _isPanning = true;
                    _panStart = GetGlobalMousePosition();
                }
                else
                {
                    _isPanning = false;
                }
            }

            // 滚轮缩放
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
            {
                _zoom = Mathf.Clamp(_zoom * 1.1f, 0.3f, 3.0f);
                _camera.Zoom = new Vector2(_zoom, _zoom);
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
            {
                _zoom = Mathf.Clamp(_zoom / 1.1f, 0.3f, 3.0f);
                _camera.Zoom = new Vector2(_zoom, _zoom);
            }

            // 左键绘制
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    // 按下左键 → 开始绘制并执行一次
                    _isPainting = true;
                    // 记录初始绘制位置，避免连续绘制重复处理首格
                    var (px, py) = MouseToGrid();
                    _lastPaintGx = px;
                    _lastPaintGy = py;
                    HandlePaint();
                }
                else
                {
                    // 松开左键 → 停止连续绘制
                    _isPainting = false;
                }
            }
        }

        if (@event is InputEventMouseMotion mm && _isPanning)
        {
            var delta = GetGlobalMousePosition() - _panStart;
            _camera.Position -= delta / _zoom;
        }

        // 键盘快捷键
        if (@event is InputEventKey key && key.Pressed)
        {
            // 数字键切换笔刷
            if (key.Keycode >= Key.Kp1 && key.Keycode <= Key.Kp5)
            {
                int idx = (int)(key.Keycode - Key.Kp1);
                _brushModeSelect.Selected = idx;
                MapEditorBrush.CurrentMode = (MapEditorBrush.BrushMode)idx;
            }

            // Ctrl+S 保存
            if (key.Keycode == Key.S && (key.CtrlPressed || key.MetaPressed))
            {
                AcceptEvent();
                OnSavePressed();
            }
            // Ctrl+O 加载
            if (key.Keycode == Key.O && (key.CtrlPressed || key.MetaPressed))
            {
                AcceptEvent();
                OnLoadPressed();
            }
            // Ctrl+N 新建
            if (key.Keycode == Key.N && (key.CtrlPressed || key.MetaPressed))
            {
                AcceptEvent();
                OnNewPressed();
            }
            // Ctrl+Z 撤销
            if (key.Keycode == Key.Z && (key.CtrlPressed || key.MetaPressed) && !key.ShiftPressed)
            {
                AcceptEvent();
                Undo();
            }
            // Ctrl+Y 或 Ctrl+Shift+Z 重做
            if ((key.Keycode == Key.Y && (key.CtrlPressed || key.MetaPressed)) ||
                (key.Keycode == Key.Z && (key.CtrlPressed || key.MetaPressed) && key.ShiftPressed))
            {
                AcceptEvent();
                Redo();
            }
            // Ctrl+Z 撤销
            if (key.Keycode == Key.Z && (key.CtrlPressed || key.MetaPressed) && !key.ShiftPressed)
            {
                AcceptEvent();
                Undo();
            }
            // Ctrl+Y 或 Ctrl+Shift+Z 重做
            if ((key.Keycode == Key.Y && (key.CtrlPressed || key.MetaPressed)) ||
                (key.Keycode == Key.Z && (key.CtrlPressed || key.MetaPressed) && key.ShiftPressed))
            {
                AcceptEvent();
                Redo();
            }
            // Escape 返回主菜单
            if (key.Keycode == Key.Escape)
            {
                GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
            }
            // F12 截图（保存到 user://screenshot.png 便于验证）
            if (key.Keycode == Key.F12)
            {
                var img = GetViewport().GetTexture().GetImage();
                string path = "user://mapeditor_screenshot.png";
                img.SavePng(path);
                string abs = ProjectSettings.GlobalizePath(path);
                GameLog.Info($"[MapEditor] screenshot saved: {abs}");
                _statusLabel.Text = TrManager.Tr("editor.screenshot", abs);
            }
        }
    }

    public override void _Process(double delta)
    {
        // 更新悬停坐标显示
        UpdateHoverCoord();

        // 拖拽连续绘制：按住左键时逐帧执行绘制
        if (_isPainting && _placeMode == PlaceMode.Terrain)
        {
            HandlePaintContinuous();
        }
    }

    // ====================================================================
    //                          UI 构建
    // ====================================================================

    private void BuildUI()
    {
        // 全屏深色背景
        var bg = new ColorRect();
        bg.Color = new Color(0.06f, 0.09f, 0.08f, 1f);
        bg.AnchorLeft = 0; bg.AnchorTop = 0; bg.AnchorRight = 1; bg.AnchorBottom = 1;
        AddChild(bg);

        // 左侧工具栏面板 (宽 280px)
        var sidebar = new Panel();
        sidebar.OffsetLeft = 0; sidebar.OffsetTop = 0;
        sidebar.OffsetRight = 280; sidebar.OffsetBottom = 720;
        AddChild(sidebar);

        var vbox = new VBoxContainer();
        vbox.OffsetLeft = 12; vbox.OffsetTop = 12;
        vbox.OffsetRight = 268; vbox.OffsetBottom = 708;
        vbox.AddThemeConstantOverride("separation", 8);
        sidebar.AddChild(vbox);

        // ── 标题
        var title = MakeLabel(TrManager.Tr("editor.title"), 22, Colors.White);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);
        vbox.AddChild(MakeLabel("P1-3 - v1", 11, new Color(0.45f, 0.5f, 0.45f)));

        // ── 放置模式
        vbox.AddChild(MakeLabel(TrManager.Tr("editor.section_place_mode"), 13, new Color(0.7f, 0.75f, 0.7f)));
        _placeModeSelect = new OptionButton();
        _placeModeSelect.AddItem(TrManager.Tr("editor.place_terrain"), (int)PlaceMode.Terrain);
        _placeModeSelect.AddItem(TrManager.Tr("editor.place_resource"), (int)PlaceMode.Resource);
        _placeModeSelect.AddItem(TrManager.Tr("editor.place_strategic"), (int)PlaceMode.Strategic);
        _placeModeSelect.AddItem(TrManager.Tr("editor.place_base"), (int)PlaceMode.Base);
        _placeModeSelect.Selected = (int)PlaceMode.Terrain;
        _placeModeSelect.ItemSelected += (idx) =>
        {
            _placeMode = (PlaceMode)(int)idx;
            UpdatePlaceModeUI();
            UpdateStatus();
        };
        vbox.AddChild(_placeModeSelect);

        // ── 笔刷模式（仅地形模式有效）
        vbox.AddChild(MakeLabel(TrManager.Tr("editor.section_brush_size"), 13, new Color(0.7f, 0.75f, 0.7f)));
        _brushModeSelect = new OptionButton();
        for (int i = 0; i < BrushModeKeys.Length; i++)
            _brushModeSelect.AddItem(TrManager.Tr(BrushModeKeys[i]), i);
        _brushModeSelect.Selected = (int)MapEditorBrush.CurrentMode;
        _brushModeSelect.ItemSelected += (idx) =>
        {
            MapEditorBrush.CurrentMode = (MapEditorBrush.BrushMode)(int)idx;
            UpdateStatus();
        };
        vbox.AddChild(_brushModeSelect);

        // ── 地形选择
        vbox.AddChild(MakeLabel(TrManager.Tr("editor.section_terrain"), 13, new Color(0.7f, 0.75f, 0.7f)));
        _terrainSelect = new OptionButton();
        for (int i = 0; i < TerrainOptions.Length; i++)
            _terrainSelect.AddItem(TrManager.Tr(TerrainOptions[i].key), i);
        _terrainSelect.Selected = (int)MapEditorBrush.SelectedTerrain;
        _terrainSelect.ItemSelected += (idx) =>
        {
            MapEditorBrush.SelectedTerrain = TerrainOptions[(int)idx].type;
            UpdateStatus();
        };
        vbox.AddChild(_terrainSelect);

        // ── 海拔选择
        vbox.AddChild(MakeLabel(TrManager.Tr("editor.section_elevation"), 13, new Color(0.7f, 0.75f, 0.7f)));
        _elevationSelect = new OptionButton();
        _elevationSelect.AddItem(TrManager.Tr("elevation.0"), 0);
        _elevationSelect.AddItem(TrManager.Tr("elevation.1"), 1);
        _elevationSelect.AddItem(TrManager.Tr("elevation.2"), 2);
        _elevationSelect.AddItem(TrManager.Tr("elevation.3"), 3);
        _elevationSelect.Selected = MapEditorBrush.SelectedElevation;
        _elevationSelect.ItemSelected += (idx) =>
        {
            MapEditorBrush.SelectedElevation = (int)idx;
            UpdateStatus();
        };
        vbox.AddChild(_elevationSelect);

        // ── 桥梁/隧道
        var bridgeRow = new HBoxContainer();
        bridgeRow.AddThemeConstantOverride("separation", 16);
        _bridgeCheck = new CheckBox { Text = TrManager.Tr("editor.bridge"), ButtonPressed = MapEditorBrush.PaintBridge };
        _bridgeCheck.Toggled += (on) => MapEditorBrush.PaintBridge = on;
        bridgeRow.AddChild(_bridgeCheck);
        _tunnelCheck = new CheckBox { Text = TrManager.Tr("editor.tunnel"), ButtonPressed = MapEditorBrush.PaintTunnel };
        _tunnelCheck.Toggled += (on) => MapEditorBrush.PaintTunnel = on;
        bridgeRow.AddChild(_tunnelCheck);
        vbox.AddChild(bridgeRow);

        // ── 矿石数量（矿点模式有效）
        vbox.AddChild(MakeLabel(TrManager.Tr("editor.section_resource_amount"), 13, new Color(0.7f, 0.75f, 0.7f)));
        _resourceAmountBox = new SpinBox();
        _resourceAmountBox.MinValue = 500;
        _resourceAmountBox.MaxValue = 50000;
        _resourceAmountBox.Step = 500;
        _resourceAmountBox.Value = 5000;
        _resourceAmountBox.Suffix = " $";
        vbox.AddChild(_resourceAmountBox);

        // ── 地图信息
        vbox.AddChild(MakeLabel(TrManager.Tr("editor.section_map_info"), 13, new Color(0.7f, 0.75f, 0.7f)));
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 8);
        nameRow.AddChild(MakeLabel(TrManager.Tr("editor.name_label"), 12, new Color(0.6f, 0.65f, 0.6f)));
        _mapNameInput = new LineEdit { CustomMinimumSize = new Vector2(160, 0) };
        _mapNameInput.Text = _mapData.Name;
        _mapNameInput.TextChanged += (txt) => _mapData.Name = txt;
        nameRow.AddChild(_mapNameInput);
        vbox.AddChild(nameRow);

        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 8);
        seedRow.AddChild(MakeLabel(TrManager.Tr("editor.seed_label"), 12, new Color(0.6f, 0.65f, 0.6f)));
        _seedInput = new LineEdit { CustomMinimumSize = new Vector2(160, 0) };
        _seedInput.Text = _currentSeed.ToString();
        seedRow.AddChild(_seedInput);
        var seedBtn = new Button { Text = TrManager.Tr("editor.apply") };
        seedBtn.Pressed += () =>
        {
            if (ulong.TryParse(_seedInput.Text.Trim(), out var s))
            {
                _currentSeed = s;
                _mapData.Seed = s;
                _terrain.GenerateFromSeed(s);
                _rng = new Random((int)(s & 0x7FFFFFFF));
                ApplyMapDataToTerrain();
                RefreshGround();
                RefreshMarkers();
                UpdateStatus();
                GameLog.Info($"[MapEditor] seed updated: {s}");
            }
        };
        seedRow.AddChild(seedBtn);
        vbox.AddChild(seedRow);

        // ── 地图大小
        vbox.AddChild(MakeLabel(TrManager.Tr("editor.section_map_size"), 13, new Color(0.7f, 0.75f, 0.7f)));
        var sizeRow = new HBoxContainer();
        sizeRow.AddThemeConstantOverride("separation", 8);
        var sizeSelect = new OptionButton();
        sizeSelect.AddItem("32 x 32", 0);
        sizeSelect.AddItem("64 x 64", 1);
        sizeSelect.AddItem("96 x 96", 2);
        sizeSelect.Selected = MapConfig.GridSize switch
        {
            32 => 0,
            64 => 1,
            96 => 2,
            _ => 0,
        };
        sizeSelect.ItemSelected += (idx) =>
        {
            int newSize = (int)idx switch
            {
                0 => 32,
                1 => 64,
                2 => 96,
                _ => 32,
            };
            ChangeMapSize(newSize);
        };
        sizeRow.AddChild(sizeSelect);
        vbox.AddChild(sizeRow);

        // ── 操作按钮
        var spacer = new Control { CustomMinimumSize = new Vector2(0, 8) };
        vbox.AddChild(spacer);

        var btnRow1 = new HBoxContainer();
        btnRow1.AddThemeConstantOverride("separation", 8);
        var newBtn = new Button { Text = TrManager.Tr("editor.new_map"), CustomMinimumSize = new Vector2(80, 32) };
        newBtn.Pressed += OnNewPressed;
        btnRow1.AddChild(newBtn);
        var saveBtn = new Button { Text = TrManager.Tr("editor.save_map"), CustomMinimumSize = new Vector2(80, 32) };
        saveBtn.Pressed += OnSavePressed;
        btnRow1.AddChild(saveBtn);
        var loadBtn = new Button { Text = TrManager.Tr("editor.load_map"), CustomMinimumSize = new Vector2(80, 32) };
        loadBtn.Pressed += OnLoadPressed;
        btnRow1.AddChild(loadBtn);
        vbox.AddChild(btnRow1);

        // ── 文件对话框（隐藏，按需弹）
        // 用文件路径输入而非原生对话框（更可靠）

        // ── 返回按钮
        var backBtn = new Button { Text = TrManager.Tr("editor.back_to_menu"), CustomMinimumSize = new Vector2(0, 36) };
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        vbox.AddChild(backBtn);

        // ── 底部状态栏
        var statusBar = new Panel();
        statusBar.OffsetLeft = 280; statusBar.OffsetTop = 688;
        statusBar.OffsetRight = 1280; statusBar.OffsetBottom = 720;
        AddChild(statusBar);

        var statusHbox = new HBoxContainer();
        statusHbox.OffsetLeft = 8; statusHbox.OffsetTop = 2;
        statusHbox.OffsetRight = 992; statusHbox.OffsetBottom = 30;
        statusHbox.AddThemeConstantOverride("separation", 24);
        statusBar.AddChild(statusHbox);

        _statusLabel = MakeLabel("", 12, new Color(0.8f, 0.85f, 0.8f));
        statusHbox.AddChild(_statusLabel);

        _coordLabel = MakeLabel(TrManager.Tr("editor.coord_label"), 12, new Color(0.7f, 0.8f, 0.7f));
        statusHbox.AddChild(_coordLabel);

        UpdateStatus();
    }

    /// <summary>根据放置模式禁用/启用相关控件。</summary>
    private void UpdatePlaceModeUI()
    {
        bool isTerrain = _placeMode == PlaceMode.Terrain;
        _brushModeSelect.Disabled = !isTerrain;
        _terrainSelect.Disabled = !isTerrain;
        _elevationSelect.Disabled = !isTerrain;
        _bridgeCheck.Disabled = !isTerrain;
        _tunnelCheck.Disabled = !isTerrain;
        _resourceAmountBox.Editable = _placeMode == PlaceMode.Resource;
    }

    // ====================================================================
    //                          渲染层
    // ====================================================================

    private void BuildRenderLayers()
    {
        // Camera2D 用于平移和缩放
        _camera = new Camera2D();
        _camera.Position = new Vector2(0, 0);
        _camera.Zoom = new Vector2(_zoom, _zoom);
        AddChild(_camera);

        // 地面精灵（等距渲染的地形）
        _groundSprite = new Sprite2D
        {
            Name = "EditorGround",
            Centered = false,
            ZIndex = -3,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        AddChild(_groundSprite);
        MoveChild(_groundSprite, 0);

        // 覆盖层（笔刷预览）
        _overlayLayer = new Node2D { Name = "Overlay" };
        AddChild(_overlayLayer);

        // 标记层（矿点/战略点/基地）
        _markerLayer = new Node2D { Name = "Markers" };
        AddChild(_markerLayer);
    }

    /// <summary>重新渲染地面纹理。</summary>
    private void RefreshGround()
    {
        var isoImg = IsoTerrainRenderer.RenderTerrain(_terrain, _rng);
        var (offX, offY) = IsoTerrainRenderer.GetRenderOffset();
        var tex = ImageTexture.CreateFromImage(isoImg);
        _groundSprite.Texture = tex;
        _groundSprite.Position = new Vector2(-offX, offY);
    }

    /// <summary>刷新矿点/战略点/基地标记。</summary>
    private void RefreshMarkers()
    {
        // 清空旧的
        foreach (var child in _markerLayer.GetChildren())
        {
            _markerLayer.RemoveChild((Node)child);
            ((Node)child).QueueFree();
        }

        // 矿点（黄色菱形）
        foreach (var r in _mapData.ResourceNodes)
        {
            var pos = IsoCoords.GridToScreen(r.Gx, r.Gy);
            var marker = MakeDiamondMarker(new Color(1f, 0.85f, 0.2f), 12);
            marker.Position = pos + new Vector2(0, -4);
            _markerLayer.AddChild(marker);

            var lbl = MakeLabel($"${r.Amount}", 9, new Color(1f, 0.9f, 0.5f));
            lbl.Position = pos + new Vector2(-16, -22);
            _markerLayer.AddChild(lbl);
        }

        // 战略点（青色星标）
        foreach (var p in _mapData.StrategicPoints)
        {
            var pos = IsoCoords.GridToScreen(p.Gx, p.Gy);
            var marker = MakeDiamondMarker(new Color(0.3f, 0.9f, 1f), 14);
            marker.Position = pos;
            _markerLayer.AddChild(marker);
        }

        // 基地出生点（红色—默认4角）
        // 编辑器不直接管理基地位置，这里显示默认的baseCount个角点
        DrawBaseSpawns();
    }

    /// <summary>绘制基地出生点：自动对称分布的点位 + 手动放置的点位。</summary>
    private void DrawBaseSpawns()
    {
        int gs = TerrainGrid.GridSize;
        // 对称角点：2个=左上右下, 4个=四角, 8个=八向
        var corners = new List<(int x, int y)>();
        int n = _mapData.BaseCount;
        int margin = 3;
        if (n <= 2)
        {
            corners.Add((margin, margin));
            corners.Add((gs - 1 - margin, gs - 1 - margin));
        }
        else if (n <= 4)
        {
            corners.Add((margin, margin));
            corners.Add((gs - 1 - margin, margin));
            corners.Add((margin, gs - 1 - margin));
            corners.Add((gs - 1 - margin, gs - 1 - margin));
        }
        else
        {
            // 8方向
            int mid = gs / 2;
            corners.Add((margin, margin));
            corners.Add((mid, margin));
            corners.Add((gs - 1 - margin, margin));
            corners.Add((gs - 1 - margin, mid));
            corners.Add((gs - 1 - margin, gs - 1 - margin));
            corners.Add((mid, gs - 1 - margin));
            corners.Add((margin, gs - 1 - margin));
            corners.Add((margin, mid));
        }

        // 自动对称分布的点位：红色菱形
        foreach (var (x, y) in corners)
        {
            var pos = IsoCoords.GridToScreen(x, y);
            var marker = MakeDiamondMarker(new Color(1f, 0.3f, 0.3f), 16);
            marker.Position = pos;
            _markerLayer.AddChild(marker);
        }

        // 手动放置的点位：橙红色菱形（外圈带白色描边以区分）
        foreach (var b in _mapData.CustomBasePositions)
        {
            if (b.X < 0 || b.X >= gs || b.Y < 0 || b.Y >= gs)
                continue;
            var pos = IsoCoords.GridToScreen(b.X, b.Y);
            var marker = MakeDiamondMarker(new Color(1f, 0.6f, 0.1f), 18);
            marker.Position = pos;
            _markerLayer.AddChild(marker);

            // 添加"M"标签标记手动放置
            var lbl = MakeLabel("M", 10, Colors.White);
            lbl.Position = pos + new Vector2(-3, -20);
            _markerLayer.AddChild(lbl);
        }
    }

    /// <summary>创建一个菱形标记节点。</summary>
    private static Node2D MakeDiamondMarker(Color color, float size)
    {
        var node = new Node2D();
        var poly = new Polygon2D();
        var s = size;
        poly.Polygon = new Vector2[]
        {
            new(0, -s),
            new(s * 0.6f, 0),
            new(0, s),
            new(-s * 0.6f, 0),
        };
        poly.Color = color;
        node.AddChild(poly);

        // 边框
        var line = new Line2D();
        line.Width = 1.5f;
        line.DefaultColor = new Color(color.R * 0.5f, color.G * 0.5f, color.B * 0.5f);
        line.Closed = true;
        line.Points = new Vector2[]
        {
            new(0, -s),
            new(s * 0.6f, 0),
            new(0, s),
            new(-s * 0.6f, 0),
            new(0, -s),
        };
        node.AddChild(line);
        return node;
    }

    // ====================================================================
    //                          绘制逻辑
    // ====================================================================

    /// <summary>将鼠标坐标转换为网格坐标。</summary>
    private (int gx, int gy) MouseToGrid()
    {
        var world = GetGlobalMousePosition();
        // 地面sprite的position偏移补偿
        var (offX, offY) = IsoTerrainRenderer.GetRenderOffset();
        // world 已经是相机变换后的坐标；地面sprite position = (-offX, offY)
        // 网格(0,0)的屏幕坐标 = sprite本地原点 = global (-offX, offY)
        // 所以对于世界坐标 world: 相对sprite位置 = world - (-offX, offY) = world + (offX, -offY)
        // 但 IsoCoords.ScreenToGrid 期望的输入是相对于网格(0,0)的偏移
        float relX = world.X + offX;
        float relY = world.Y - offY;
        return IsoCoords.ScreenToGrid(relX, relY);
    }

    /// <summary>每帧更新悬停坐标和高亮预览。</summary>
    private void UpdateHoverCoord()
    {
        var (gx, gy) = MouseToGrid();

        if (gx != _hoverGx || gy != _hoverGy)
        {
            _hoverGx = gx;
            _hoverGy = gy;
            RedrawOverlay();
        }

        if (gx >= 0 && gx < TerrainGrid.GridSize && gy >= 0 && gy < TerrainGrid.GridSize)
            _coordLabel.Text = TrManager.Tr("editor.coord", gx, gy);
        else
            _coordLabel.Text = TrManager.Tr("editor.coord_label");
    }

    /// <summary>重绘笔刷预览覆盖层。</summary>
    private void RedrawOverlay()
    {
        // 清空
        foreach (var child in _overlayLayer.GetChildren())
        {
            _overlayLayer.RemoveChild((Node)child);
            ((Node)child).QueueFree();
        }

        if (_hoverGx < 0 || _hoverGx >= TerrainGrid.GridSize ||
            _hoverGy < 0 || _hoverGy >= TerrainGrid.GridSize)
            return;

        // 根据放置模式绘制不同预览
        if (_placeMode == PlaceMode.Terrain)
        {
            var cells = MapEditorBrush.GetBrushCells(_hoverGx, _hoverGy, MapEditorBrush.CurrentMode);
            Color c = MapEditorBrush.CurrentMode == MapEditorBrush.BrushMode.Eraser
                ? new Color(1f, 0.4f, 0.4f, 0.4f)
                : new Color(1f, 1f, 1f, 0.35f);
            foreach (var (cx, cy) in cells)
                DrawCellHighlight(cx, cy, c);

            // 填充预览
            if (MapEditorBrush.CurrentMode == MapEditorBrush.BrushMode.Fill)
            {
                var fillCells = MapEditorBrush.FloodFill(_hoverGx, _hoverGy, _terrain, _mapData);
                foreach (var (fx, fy) in fillCells)
                    DrawCellHighlight(fx, fy, new Color(0.3f, 1f, 0.5f, 0.25f));
            }
        }
        else if (_placeMode == PlaceMode.Resource)
        {
            DrawCellHighlight(_hoverGx, _hoverGy, new Color(1f, 0.85f, 0.2f, 0.4f));
        }
        else if (_placeMode == PlaceMode.Strategic)
        {
            DrawCellHighlight(_hoverGx, _hoverGy, new Color(0.3f, 0.9f, 1f, 0.4f));
        }
        else if (_placeMode == PlaceMode.Base)
        {
            DrawCellHighlight(_hoverGx, _hoverGy, new Color(1f, 0.3f, 0.3f, 0.4f));
        }
    }

    /// <summary>绘制单个格子的高亮菱形。</summary>
    private void DrawCellHighlight(int gx, int gy, Color color)
    {
        var pos = IsoCoords.GridToScreen(gx, gy);
        var poly = new Polygon2D();
        poly.Polygon = IsoCoords.DiamondVerts;
        poly.Color = color;
        poly.Position = pos;
        _overlayLayer.AddChild(poly);
    }

    /// <summary>处理左键点击的绘制操作（含撤销快照）。</summary>
    private void HandlePaint()
    {
        var (gx, gy) = MouseToGrid();
        if (gx < 0 || gx >= TerrainGrid.GridSize || gy < 0 || gy >= TerrainGrid.GridSize)
            return;

        bool changed = false;

        switch (_placeMode)
        {
            case PlaceMode.Terrain:
                // 保存撤销快照
                PushUndo();
                MapEditorBrush.ApplyBrush(gx, gy, _terrain, _mapData);
                ApplyMapDataToTerrain();
                changed = true;
                break;

            case PlaceMode.Resource:
                // 左键放置，Shift+左键删除
                if (!IsKeyPressed(Key.Shift))
                {
                    PushUndo();
                    _mapData.AddResourceNode(gx, gy, (int)_resourceAmountBox.Value);
                    changed = true;
                }
                else
                {
                    PushUndo();
                    _mapData.RemoveResourceNode(gx, gy);
                    changed = true;
                }
                break;

            case PlaceMode.Strategic:
                if (!IsKeyPressed(Key.Shift))
                {
                    PushUndo();
                    _mapData.AddStrategicPoint(gx, gy);
                    changed = true;
                }
                else
                {
                    PushUndo();
                    _mapData.RemoveStrategicPoint(gx, gy);
                    changed = true;
                }
                break;

            case PlaceMode.Base:
                // 左键放置基地出生点，Shift+左键移除最近的手动出生点
                if (!IsKeyPressed(Key.Shift))
                {
                    PushUndo();
                    // 避免重复放置同一位置
                    if (!_mapData.CustomBasePositions.Exists(b => b.X == gx && b.Y == gy))
                    {
                        _mapData.CustomBasePositions.Add(new Vector2I(gx, gy));
                    }
                    changed = true;
                }
                else
                {
                    PushUndo();
                    // 移除点击位置附近（1格内）的手动出生点
                    _mapData.CustomBasePositions.RemoveAll(b =>
                        System.Math.Abs(b.X - gx) <= 1 && System.Math.Abs(b.Y - gy) <= 1);
                    changed = true;
                }
                break;
        }

        if (changed)
        {
            if (_placeMode == PlaceMode.Terrain)
                RefreshGround();
            RefreshMarkers();
            RedrawOverlay();
            UpdateStatus();
        }
    }

    /// <summary>拖拽连续绘制：仅地形模式，每次绘制到新格子时压栈。</summary>
    private void HandlePaintContinuous()
    {
        var (gx, gy) = MouseToGrid();
        if (gx < 0 || gx >= TerrainGrid.GridSize || gy < 0 || gy >= TerrainGrid.GridSize)
            return;

        // 仅当鼠标移到新格子时才绘制，避免同一格重复操作
        if (gx == _lastPaintGx && gy == _lastPaintGy)
            return;

        _lastPaintGx = gx;
        _lastPaintGy = gy;

        // 连续绘制时直接调用 ApplyBrush，每次都压栈（用户可逐步撤销）
        PushUndo();
        MapEditorBrush.ApplyBrush(gx, gy, _terrain, _mapData);
        ApplyMapDataToTerrain();
        RefreshGround();
        RefreshMarkers();
        RedrawOverlay();
        UpdateStatus();
    }

    // 追踪连续绘制时上一次绘制的格子，避免重复
    private int _lastPaintGx = -1, _lastPaintGy = -1;

    /// <summary>检查修饰键是否按下。</summary>
    private static bool IsKeyPressed(Key key)
    {
        return Input.IsKeyPressed(key);
    }

    // ====================================================================
    //                          数据同步
    // ====================================================================

    /// <summary>将MapData中的增量修改应用到TerrainGrid（用于渲染）。</summary>
    private void ApplyMapDataToTerrain()
    {
        foreach (var mod in _mapData.TerrainMods)
        {
            if (mod.Gx >= 0 && mod.Gx < TerrainGrid.GridSize &&
                mod.Gy >= 0 && mod.Gy < TerrainGrid.GridSize)
            {
                _terrain.SetCell(mod.Gx, mod.Gy, new TerrainCell
                {
                    Type = (TerrainType)mod.TerrainType,
                    Elevation = mod.Elevation,
                    HasBridge = mod.HasBridge,
                    HasTunnel = mod.HasTunnel,
                });
            }
        }
    }

    // ====================================================================
    //                          按钮事件
    // ====================================================================

    private void OnNewPressed()
    {
        _mapData = new MapData { Name = TrManager.Tr("editor.new_map_name"), Seed = _currentSeed };
        _mapNameInput.Text = _mapData.Name;
        _terrain.GenerateFromSeed(_currentSeed);
        ApplyMapDataToTerrain();
        RefreshGround();
        RefreshMarkers();
        UpdateStatus();
        GameLog.Info("[MapEditor] new map (current seed retained)");
    }

    private void OnSavePressed()
    {
        // 默认保存到 user://maps/ 目录
        string dir = "user://maps";
        if (!Godot.DirAccess.DirExistsAbsolute(dir))
            Godot.DirAccess.MakeDirRecursiveAbsolute(dir);
        string fileName = string.IsNullOrEmpty(_mapData.Name) ? "untitled" : SanitizeFileName(_mapData.Name);
        string path = $"{dir}/{fileName}.rmap";

        if (MapData.SaveToFile(_mapData, path))
        {
            GameLog.Info($"[MapEditor] map saved: {path}");
            // 也显示系统绝对路径
            string absPath = ProjectSettings.GlobalizePath(path);
            _statusLabel.Text = TrManager.Tr("editor.saved_to", absPath);
        }
    }

    private void OnLoadPressed()
    {
        // 简化版：从user://maps/加载最新的.rmap文件
        string dir = "user://maps";
        if (!Godot.DirAccess.DirExistsAbsolute(dir))
        {
            _statusLabel.Text = TrManager.Tr("editor.no_saved_maps");
            return;
        }

        var files = Godot.DirAccess.GetFilesAt(dir);
        string? latest = null;
        ulong latestTime = 0;
        foreach (var f in files)
        {
            if (!f.EndsWith(".rmap")) continue;
            var path = $"{dir}/{f}";
            var time = Godot.FileAccess.GetModifiedTime(path);
            if (time > latestTime) { latestTime = time; latest = path; }
        }

        if (latest == null)
        {
            _statusLabel.Text = TrManager.Tr("editor.no_rmap_files");
            return;
        }

        var loaded = MapData.LoadFromFile(latest);
        if (loaded != null)
        {
            _mapData = loaded;
            _currentSeed = loaded.Seed;
            _seedInput.Text = _currentSeed.ToString();
            _mapNameInput.Text = loaded.Name;
            _terrain.GenerateFromSeed(_currentSeed);
            _rng = new Random((int)(_currentSeed & 0x7FFFFFFF));
            ApplyMapDataToTerrain();
            RefreshGround();
            RefreshMarkers();
            UpdateStatus();
            GameLog.Info($"[MapEditor] loaded: {latest}");
        }
    }

    // ====================================================================
    //                          撤销/重做
    // ====================================================================

    /// <summary>将当前 MapData 的快照压入撤销栈（执行修改前调用）。</summary>
    private void PushUndo()
    {
        _undoStack.Push(_mapData.Clone());
        // 限制栈大小
        while (_undoStack.Count > MaxUndoStack)
        {
            // 移除最底部的（转数组后跳过第一个）
            var arr = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = arr.Length - 2; i >= 0; i--)
                _undoStack.Push(arr[i]);
        }
        // 执行新操作时清空重做栈
        _redoStack.Clear();
    }

    /// <summary>撤销：恢复到上一个快照。</summary>
    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            _statusLabel.Text = TrManager.Tr("editor.no_undo");
            return;
        }

        // 当前状态压入重做栈
        _redoStack.Push(_mapData.Clone());
        // 恢复快照
        _mapData = _undoStack.Pop();
        // 同步UI和渲染
        _mapNameInput.Text = _mapData.Name;
        _seedInput.Text = _mapData.Seed.ToString();
        _currentSeed = _mapData.Seed;
        _terrain.GenerateFromSeed(_currentSeed);
        _rng = new Random((int)(_currentSeed & 0x7FFFFFFF));
        ApplyMapDataToTerrain();
        RefreshGround();
        RefreshMarkers();
        RedrawOverlay();
        UpdateStatus();
        _statusLabel.Text = TrManager.Tr("editor.undo_done", _undoStack.Count, _redoStack.Count);
        GameLog.Info($"[MapEditor] Undo → undo stack:{_undoStack.Count}, redo stack:{_redoStack.Count}");
    }

    /// <summary>重做：恢复最近撤销的操作。</summary>
    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            _statusLabel.Text = TrManager.Tr("editor.no_redo");
            return;
        }

        // 当前状态压入撤销栈
        _undoStack.Push(_mapData.Clone());
        // 恢复快照
        _mapData = _redoStack.Pop();
        // 同步UI和渲染
        _mapNameInput.Text = _mapData.Name;
        _seedInput.Text = _mapData.Seed.ToString();
        _currentSeed = _mapData.Seed;
        _terrain.GenerateFromSeed(_currentSeed);
        _rng = new Random((int)(_currentSeed & 0x7FFFFFFF));
        ApplyMapDataToTerrain();
        RefreshGround();
        RefreshMarkers();
        RedrawOverlay();
        UpdateStatus();
        _statusLabel.Text = TrManager.Tr("editor.redo_done", _undoStack.Count, _redoStack.Count);
        GameLog.Info($"[MapEditor] Redo → undo stack:{_undoStack.Count}, redo stack:{_redoStack.Count}");
    }

    // ====================================================================
    //                          地图大小调整
    // ====================================================================

    /// <summary>切换地图大小，尽可能保留当前编辑数据。</summary>
    private void ChangeMapSize(int newSize)
    {
        int oldSize = TerrainGrid.GridSize;
        if (newSize == oldSize)
            return;

        // 保存撤销快照
        PushUndo();

        // 更新全局配置
        MapConfig.SetSize(newSize);

        // 重建地形网格
        _terrain.GenerateFromSeed(_currentSeed);

        // 过滤掉超出新地图范围的修改数据
        var oldMods = _mapData.TerrainMods;
        _mapData.TerrainMods = new List<SaveLoadSystem.TerrainModSave>();
        foreach (var m in oldMods)
            if (m.Gx >= 0 && m.Gx < newSize && m.Gy >= 0 && m.Gy < newSize)
                _mapData.TerrainMods.Add(m);

        // 过滤矿点
        _mapData.ResourceNodes.RemoveAll(r => r.Gx >= newSize || r.Gy >= newSize);

        // 过滤战略点
        _mapData.StrategicPoints.RemoveAll(p => p.Gx >= newSize || p.Gy >= newSize);

        // 过滤手动基地出生点
        _mapData.CustomBasePositions.RemoveAll(b => b.X >= newSize || b.Y >= newSize);

        // 应用到地形并刷新
        ApplyMapDataToTerrain();
        RefreshGround();
        RefreshMarkers();
        RedrawOverlay();
        UpdateStatus();
        _statusLabel.Text = TrManager.Tr("editor.map_size_changed", newSize, newSize);
        GameLog.Info($"[MapEditor] map size {oldSize}->{newSize}, retained mods:{_mapData.TerrainMods.Count}");
    }

    // ====================================================================
    //                          工具方法
    // ====================================================================

    private void UpdateStatus()
    {
        _statusLabel.Text = TrManager.Tr("editor.status_mode", _placeMode, MapEditorBrush.CurrentMode,
                                          MapEditorBrush.SelectedTerrain, MapEditorBrush.SelectedElevation,
                                          _mapData.TerrainMods.Count, _mapData.ResourceNodes.Count,
                                          _mapData.StrategicPoints.Count, _mapData.CustomBasePositions.Count,
                                          _undoStack.Count, _redoStack.Count);
    }

    private static string SanitizeFileName(string s)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            bool bad = false;
            foreach (var ic in invalid) if (c == ic) { bad = true; break; }
            sb.Append(bad ? '_' : c);
        }
        return sb.ToString().Trim();
    }

    private static Label MakeLabel(string text, int fontSize, Color color)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        lbl.AddThemeColorOverride("font_color", color);
        return lbl;
    }
}
