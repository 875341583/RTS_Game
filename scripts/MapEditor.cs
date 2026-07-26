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

    // ======== 地形类型显示名 ========
    private static readonly (TerrainType type, string name)[] TerrainOptions =
    {
        (TerrainType.Grass,       "草地"),
        (TerrainType.Sand,        "沙地"),
        (TerrainType.Snow,        "雪地"),
        (TerrainType.City,        "城市路面"),
        (TerrainType.Field,       "田地"),
        (TerrainType.ShallowWater,"浅水"),
        (TerrainType.DeepWater,   "深水"),
        (TerrainType.Mountain,    "山脉"),
        (TerrainType.Road,        "道路"),
        (TerrainType.Cliff,       "悬崖"),
        (TerrainType.Bridge,      "桥梁"),
        (TerrainType.Tunnel,      "隧道"),
    };

    private static readonly string[] BrushModeNames =
    {
        "单格",
        "3×3方形",
        "5格圆形",
        "填充",
        "橡皮擦",
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
        _mapData = new MapData { Name = "新地图", Seed = 42 };

        if (loadPath != null && System.IO.File.Exists(loadPath))
        {
            var loaded = MapData.LoadFromFile(loadPath);
            if (loaded != null)
            {
                _mapData = loaded;
                _currentSeed = loaded.Seed;
                GD.Print($"[MapEditor] 从文件加载地图: {loadPath}");
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

        GD.Print($"[MapEditor] 编辑器就绪 (seed={_currentSeed}, mods={_mapData.TerrainMods.Count})");
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
            if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                HandlePaint();
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
            // 普通数字键
            int digit = (int)key.Keycode - (int)Key.Kp1;
            if (digit >= 0 && digit <= 4)
            {
                _brushModeSelect.Selected = digit;
                MapEditorBrush.CurrentMode = (MapEditorBrush.BrushMode)digit;
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
                GD.Print($"[MapEditor] 截图已保存: {abs}");
                _statusLabel.Text = $"截图: {abs}";
            }
        }
    }

    public override void _Process(double delta)
    {
        // 更新悬停坐标显示
        UpdateHoverCoord();
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
        var title = MakeLabel("地图编辑器", 22, Colors.White);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);
        vbox.AddChild(MakeLabel("P1-3 · v1", 11, new Color(0.45f, 0.5f, 0.45f)));

        // ── 放置模式
        vbox.AddChild(MakeLabel("── 放置模式 ──", 13, new Color(0.7f, 0.75f, 0.7f)));
        _placeModeSelect = new OptionButton();
        _placeModeSelect.AddItem("地形笔刷", (int)PlaceMode.Terrain);
        _placeModeSelect.AddItem("矿点", (int)PlaceMode.Resource);
        _placeModeSelect.AddItem("战略点", (int)PlaceMode.Strategic);
        _placeModeSelect.AddItem("基地出生点", (int)PlaceMode.Base);
        _placeModeSelect.Selected = (int)PlaceMode.Terrain;
        _placeModeSelect.ItemSelected += (idx) =>
        {
            _placeMode = (PlaceMode)(int)idx;
            UpdatePlaceModeUI();
            UpdateStatus();
        };
        vbox.AddChild(_placeModeSelect);

        // ── 笔刷模式（仅地形模式有效）
        vbox.AddChild(MakeLabel("── 笔刷大小 ──", 13, new Color(0.7f, 0.75f, 0.7f)));
        _brushModeSelect = new OptionButton();
        for (int i = 0; i < BrushModeNames.Length; i++)
            _brushModeSelect.AddItem(BrushModeNames[i], i);
        _brushModeSelect.Selected = (int)MapEditorBrush.CurrentMode;
        _brushModeSelect.ItemSelected += (idx) =>
        {
            MapEditorBrush.CurrentMode = (MapEditorBrush.BrushMode)(int)idx;
            UpdateStatus();
        };
        vbox.AddChild(_brushModeSelect);

        // ── 地形选择
        vbox.AddChild(MakeLabel("── 地形类型 ──", 13, new Color(0.7f, 0.75f, 0.7f)));
        _terrainSelect = new OptionButton();
        for (int i = 0; i < TerrainOptions.Length; i++)
            _terrainSelect.AddItem(TerrainOptions[i].name, i);
        _terrainSelect.Selected = (int)MapEditorBrush.SelectedTerrain;
        _terrainSelect.ItemSelected += (idx) =>
        {
            MapEditorBrush.SelectedTerrain = TerrainOptions[(int)idx].type;
            UpdateStatus();
        };
        vbox.AddChild(_terrainSelect);

        // ── 海拔选择
        vbox.AddChild(MakeLabel("── 海拔等级 ──", 13, new Color(0.7f, 0.75f, 0.7f)));
        _elevationSelect = new OptionButton();
        _elevationSelect.AddItem("0 - 水面/深水", 0);
        _elevationSelect.AddItem("1 - 平地/浅水", 1);
        _elevationSelect.AddItem("2 - 丘陵", 2);
        _elevationSelect.AddItem("3 - 山顶", 3);
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
        _bridgeCheck = new CheckBox { Text = "桥梁", ButtonPressed = MapEditorBrush.PaintBridge };
        _bridgeCheck.Toggled += (on) => MapEditorBrush.PaintBridge = on;
        bridgeRow.AddChild(_bridgeCheck);
        _tunnelCheck = new CheckBox { Text = "隧道", ButtonPressed = MapEditorBrush.PaintTunnel };
        _tunnelCheck.Toggled += (on) => MapEditorBrush.PaintTunnel = on;
        bridgeRow.AddChild(_tunnelCheck);
        vbox.AddChild(bridgeRow);

        // ── 矿石数量（矿点模式有效）
        vbox.AddChild(MakeLabel("── 矿点数量 ──", 13, new Color(0.7f, 0.75f, 0.7f)));
        _resourceAmountBox = new SpinBox();
        _resourceAmountBox.MinValue = 500;
        _resourceAmountBox.MaxValue = 50000;
        _resourceAmountBox.Step = 500;
        _resourceAmountBox.Value = 5000;
        _resourceAmountBox.Suffix = " $";
        vbox.AddChild(_resourceAmountBox);

        // ── 地图信息
        vbox.AddChild(MakeLabel("── 地图信息 ──", 13, new Color(0.7f, 0.75f, 0.7f)));
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 8);
        nameRow.AddChild(MakeLabel("名称:", 12, new Color(0.6f, 0.65f, 0.6f)));
        _mapNameInput = new LineEdit { CustomMinimumSize = new Vector2(160, 0) };
        _mapNameInput.Text = _mapData.Name;
        _mapNameInput.TextChanged += (txt) => _mapData.Name = txt;
        nameRow.AddChild(_mapNameInput);
        vbox.AddChild(nameRow);

        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 8);
        seedRow.AddChild(MakeLabel("种子:", 12, new Color(0.6f, 0.65f, 0.6f)));
        _seedInput = new LineEdit { CustomMinimumSize = new Vector2(160, 0) };
        _seedInput.Text = _currentSeed.ToString();
        seedRow.AddChild(_seedInput);
        var seedBtn = new Button { Text = "应用" };
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
                GD.Print($"[MapEditor] 种子已更新: {s}");
            }
        };
        seedRow.AddChild(seedBtn);
        vbox.AddChild(seedRow);

        // ── 操作按钮
        var spacer = new Control { CustomMinimumSize = new Vector2(0, 8) };
        vbox.AddChild(spacer);

        var btnRow1 = new HBoxContainer();
        btnRow1.AddThemeConstantOverride("separation", 8);
        var newBtn = new Button { Text = "新建", CustomMinimumSize = new Vector2(80, 32) };
        newBtn.Pressed += OnNewPressed;
        btnRow1.AddChild(newBtn);
        var saveBtn = new Button { Text = "保存", CustomMinimumSize = new Vector2(80, 32) };
        saveBtn.Pressed += OnSavePressed;
        btnRow1.AddChild(saveBtn);
        var loadBtn = new Button { Text = "加载", CustomMinimumSize = new Vector2(80, 32) };
        loadBtn.Pressed += OnLoadPressed;
        btnRow1.AddChild(loadBtn);
        vbox.AddChild(btnRow1);

        // ── 文件对话框（隐藏，按需弹）
        // 用文件路径输入而非原生对话框（更可靠）

        // ── 返回按钮
        var backBtn = new Button { Text = "← 返回主菜单 (Esc)", CustomMinimumSize = new Vector2(0, 36) };
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

        _coordLabel = MakeLabel("Grid: (--, --)", 12, new Color(0.7f, 0.8f, 0.7f));
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

    /// <summary>绘制默认基地出生点（基地数决定角点数，对称分布）。</summary>
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

        foreach (var (x, y) in corners)
        {
            var pos = IsoCoords.GridToScreen(x, y);
            var marker = MakeDiamondMarker(new Color(1f, 0.3f, 0.3f), 16);
            marker.Position = pos;
            _markerLayer.AddChild(marker);
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
            _coordLabel.Text = $"Grid: ({gx}, {gy})";
        else
            _coordLabel.Text = "Grid: (--, --)";
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

    /// <summary>处理左键点击的绘制操作。</summary>
    private void HandlePaint()
    {
        var (gx, gy) = MouseToGrid();
        if (gx < 0 || gx >= TerrainGrid.GridSize || gy < 0 || gy >= TerrainGrid.GridSize)
            return;

        bool changed = false;

        switch (_placeMode)
        {
            case PlaceMode.Terrain:
                MapEditorBrush.ApplyBrush(gx, gy, _terrain, _mapData);
                ApplyMapDataToTerrain();
                changed = true;
                break;

            case PlaceMode.Resource:
                // 左键放置，按住不拖动只放一个
                if (!IsKeyPressed(Key.Shift))
                {
                    _mapData.AddResourceNode(gx, gy, (int)_resourceAmountBox.Value);
                    changed = true;
                }
                else
                {
                    _mapData.RemoveResourceNode(gx, gy);
                    changed = true;
                }
                break;

            case PlaceMode.Strategic:
                if (!IsKeyPressed(Key.Shift))
                {
                    _mapData.AddStrategicPoint(gx, gy);
                    changed = true;
                }
                else
                {
                    _mapData.RemoveStrategicPoint(gx, gy);
                    changed = true;
                }
                break;

            case PlaceMode.Base:
                // 基地出生点暂时由BaseCount决定（对称分布），未来可手动指定
                // 当前模式仅显示，不修改
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
        _mapData = new MapData { Name = "新地图", Seed = _currentSeed };
        _mapNameInput.Text = _mapData.Name;
        _terrain.GenerateFromSeed(_currentSeed);
        ApplyMapDataToTerrain();
        RefreshGround();
        RefreshMarkers();
        UpdateStatus();
        GD.Print("[MapEditor] 新建地图（保留当前种子）");
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
            GD.Print($"[MapEditor] 地图已保存: {path}");
            // 也显示系统绝对路径
            string absPath = ProjectSettings.GlobalizePath(path);
            _statusLabel.Text = $"已保存到: {absPath}";
        }
    }

    private void OnLoadPressed()
    {
        // 简化版：从user://maps/加载最新的.rmap文件
        string dir = "user://maps";
        if (!Godot.DirAccess.DirExistsAbsolute(dir))
        {
            _statusLabel.Text = "暂无已保存地图";
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
            _statusLabel.Text = "暂无.rmap文件";
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
            GD.Print($"[MapEditor] 已加载: {latest}");
        }
    }

    // ====================================================================
    //                          工具方法
    // ====================================================================

    private void UpdateStatus()
    {
        _statusLabel.Text = $"模式:{_placeMode} 笔刷:{MapEditorBrush.CurrentMode} " +
                            $"地形:{MapEditorBrush.SelectedTerrain} 海拔:{MapEditorBrush.SelectedElevation} " +
                            $"│ 修改:{_mapData.TerrainMods.Count} 矿点:{_mapData.ResourceNodes.Count} " +
                            $"战略点:{_mapData.StrategicPoints.Count}";
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
