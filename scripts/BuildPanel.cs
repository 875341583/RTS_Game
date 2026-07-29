using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// Q1 经典 RTS 侧边栏建造面板。
/// 右侧固定面板：顶部资金/电力/难度，建筑/单位标签切换，图标网格，
/// 锁定项灰显半透明，点击建筑进入放置模式，点击单位直接生产。
/// 鼠标悬停显示前置/锁定原因。
/// </summary>
public partial class BuildPanel : Control
{
    public event Action<BuildingType>? BuildBuildingRequested;
    public event Action<UnitType>? BuildUnitRequested;
    public event Action? BuildHarvesterRequested;

    private RichTextLabel _infoLabel = null!;
    private RichTextLabel _hintLabel = null!;
    private GridContainer _buildingGrid = null!;
    private GridContainer _infantryGrid = null!;
    private GridContainer _vehicleGrid = null!;
    private Button _tabBuildings = null!;
    private Button _tabInfantry = null!;
    private Button _tabVehicles = null!;

    /// <summary>侧边栏底部三个分类标签：建筑 / 步兵 / 车辆（参考红警2侧边栏）</summary>
    private enum BuildTab { Buildings, Infantry, Vehicles }
    private BuildTab _currentTab = BuildTab.Buildings;

    private sealed class BuildItem
    {
        public string Name = "";
        public int Cost;
        public Texture2D? Icon;
        public bool IsBuilding;
        public BuildingType BType;
        public UnitType UType;
        public bool IsHarvester;
        public Panel? PanelNode;
        public Label? CostLabel;
        public ColorRect? BgRect;
        public string LockReason = "";
        public bool IsLocked;
        public bool CanAfford;
        // E11b：生产队列UI
        public Label? QueueBadge;       // 右上角 "×N" 标签
        public ProgressBar? ProdBar;     // 底部进度条
        public int QueueCount;          // 当前队列数
        public float ProdProgress;      // 当前进度 0~1
        public float _timeRemaining;    // 剩余时间（秒）
    }

    private readonly List<BuildItem> _items = new();

    // 状态（由 Main 刷新）
    private int _money, _power, _playerTechLevel, _unitCount, _unitCap;
    private bool _hasBase, _hasPower, _hasBarracks, _hasWarFactory, _hasTechCenter, HasAirfield, HasShipyard;
    public BuildingType? ActivePlacement { get; set; }
    public string DifficultyName { get; set; } = "Normal";

    // 颜色：RA2风格深灰金属 + 钢银色边框 + 金色高亮
    // 主背景：深灰金属质感 (0.10-0.14)
    private static readonly Color CBg = new(0.11f, 0.12f, 0.14f, 0.98f);
    // 标题栏背景：更深的金属色
    private static readonly Color CTitleBar = new(0.07f, 0.08f, 0.09f, 1f);
    // 信息区底板：暗金属
    private static readonly Color CInfoPlate = new(0.14f, 0.15f, 0.17f, 1f);
    // 提示区底板：暗金属
    private static readonly Color CHintPlate = new(0.09f, 0.10f, 0.11f, 1f);
    // 悬停：亮金色底
    private static readonly Color CHover = new(0.45f, 0.36f, 0.16f, 0.97f);
    // 锁定：深黑底
    private static readonly Color CLocked = new(0.04f, 0.04f, 0.05f, 0.93f);
    // 选中放置：蓝绿色底（RA2放置模式）
    private static readonly Color CSelected = new(0.12f, 0.35f, 0.28f, 0.98f);
    // 可建造：暗灰底
    private static readonly Color CReady = new(0.15f, 0.16f, 0.18f, 0.96f);
    // 资金不足：暗红底
    private static readonly Color CCantAfford = new(0.28f, 0.10f, 0.08f, 0.95f);

    // 边框颜色：外圈钢银色 (0.45-0.55)，内圈暗色
    private static readonly Color CSteelBorder = new(0.50f, 0.52f, 0.55f, 0.9f);  // 外圈钢银色
    private static readonly Color CDarkBorder = new(0.03f, 0.03f, 0.04f, 0.9f);   // 内圈暗色（凹槽感）
    // 悬停边框：金色
    private static readonly Color CHoverBorder = new(0.85f, 0.70f, 0.28f, 0.95f);
    // 选中边框：亮蓝绿色
    private static readonly Color CSelectedBorder = new(0.25f, 0.85f, 0.65f, 0.95f);
    // 锁定边框：暗灰
    private static readonly Color CLockedBorder = new(0.20f, 0.20f, 0.22f, 0.8f);
    // 资金不足边框：红色
    private static readonly Color CCantAffordBorder = new(0.70f, 0.22f, 0.15f, 0.9f);

    /// <summary>金色边框（建筑/单位图标外框）。</summary>
    private static readonly Color CGoldBorder = new(0.72f, 0.58f, 0.22f, 0.9f);
    /// <summary>金色文本（资金主数字、选中项高亮）。</summary>
    private static readonly Color CGoldText = new(1f, 0.82f, 0.32f, 1f);
    /// <summary>标签选中色（深金底色）。</summary>
    private static readonly Color CTabActive = new(0.38f, 0.30f, 0.12f, 1f);
    /// <summary>标签未选中色（暗灰底色）。</summary>
    private static readonly Color CTabInactive = new(0.10f, 0.11f, 0.12f, 1f);
    /// <summary>分隔线暗色。</summary>
    private static readonly Color CDivider = new(0.03f, 0.03f, 0.04f, 0.8f);

    private const float W = 232f;

    // 图标（直接使用游戏 PNG 素材）
    private static Texture2D? _iPower, _iBarracks, _iWar, _iTech;
    // 阶段12-A1+A2 新增建筑图标
    private static Texture2D? _iTurret, _iAntiAir, _iRepairPad;
    private static Texture2D? _iLight, _iHeavy, _iArt, _iRocket, _iMissile, _iHarv, _iAntiAirUnit, _iEngineer, _iTransport;
    private static Texture2D? _iInfantry, _iGrenadier, _iSniper, _iFlameInfantry;
    // E6b：特殊单位图标
    private static Texture2D? _iHero, _iSpy, _iThief;
    // E7：空军图标
    private static Texture2D? _iFighter, _iHelicopter, _iRocketInfantry, _iAirfield;
    // E8：扩展空军图标
    private static Texture2D? _iBomber, _iScout, _iTransportHeli;
    // E9：海军图标
    private static Texture2D? _iDestroyer, _iSubmarine, _iCarrier, _iLandingCraft, _iShipyard;
    // E10：超武建筑图标
    private static Texture2D? _iNukeSilo, _iLightningTower, _iMissileSilo;
    // RA2标志单位图标（复用现有素材）
    private static Texture2D? _iApocalypseTank, _iPrismTank, _iKirovAirship, _iTeslaTrooper;

    // 信息区/提示区底板引用（用于更新时刷新StyleBox）
    private Panel _infoPanel = null!;
    private Panel _hintPanel = null!;
    private Label _titleLabel = null!;
    private Label _moneyLabel = null!;
    private ColorRect _powerBar = null!;

    // 悬停项
    private BuildItem? _hoverItem;

    public override void _Ready()
    {
        EnsureIcons();

        AnchorLeft = 1; AnchorRight = 1; AnchorTop = 0; AnchorBottom = 1;
        OffsetLeft = -W; OffsetRight = 0; OffsetTop = 0; OffsetBottom = 0;
        MouseFilter = MouseFilterEnum.Stop;

        // RA2风格深灰金属背景
        var bg = new ColorRect();
        bg.Color = CBg;
        bg.AnchorRight = 1; bg.AnchorBottom = 1;
        bg.MouseFilter = MouseFilterEnum.Stop;
        AddChild(bg);

        var root = new VBoxContainer();
        root.AnchorRight = 1; root.AnchorBottom = 1;
        root.OffsetLeft = 6; root.OffsetTop = 6; root.OffsetRight = -6; root.OffsetBottom = -6;
        root.AddThemeConstantOverride("separation", 4);
        root.MouseFilter = MouseFilterEnum.Pass;
        AddChild(root);

        // RA2标题栏：深色金属背景 + 金色文字
        var titleBar = new Panel();
        titleBar.CustomMinimumSize = new Vector2(W - 12, 24);
        var titleStyle = new StyleBoxFlat
        {
            BgColor = CTitleBar,
            BorderWidthBottom = 1,
            BorderColor = CSteelBorder,
            ContentMarginLeft = 4, ContentMarginRight = 4,
            ContentMarginTop = 2, ContentMarginBottom = 2
        };
        titleBar.AddThemeStyleboxOverride("panel", titleStyle);
        titleBar.MouseFilter = MouseFilterEnum.Pass;
        root.AddChild(titleBar);

        _titleLabel = new Label();
        _titleLabel.Text = TrManager.Tr("build.panel_title");
        _titleLabel.AddThemeFontSizeOverride("font_size", 16);
        _titleLabel.AddThemeColorOverride("font_color", CGoldText);
        _titleLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        _titleLabel.AddThemeConstantOverride("outline_size", 1);
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _titleLabel.AnchorRight = 1; _titleLabel.AnchorBottom = 1;
        _titleLabel.MouseFilter = MouseFilterEnum.Pass;
        titleBar.AddChild(_titleLabel);

        // 资金/电力信息区：金属底板背景
        _infoPanel = new Panel();
        _infoPanel.CustomMinimumSize = new Vector2(W - 12, 68);
        var infoStyle = new StyleBoxFlat
        {
            BgColor = CInfoPlate,
            BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            BorderColor = CSteelBorder,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 4, ContentMarginBottom = 4
        };
        _infoPanel.AddThemeStyleboxOverride("panel", infoStyle);
        _infoPanel.MouseFilter = MouseFilterEnum.Pass;
        root.AddChild(_infoPanel);

        _infoLabel = new RichTextLabel();
        _infoLabel.BbcodeEnabled = true;
        _infoLabel.AddThemeFontSizeOverride("normal_font_size", 18);
        _infoLabel.FitContent = true;
        _infoLabel.AnchorRight = 1; _infoLabel.AnchorBottom = 1;
        _infoLabel.OffsetLeft = 4; _infoLabel.OffsetTop = 2; _infoLabel.OffsetRight = -4; _infoLabel.OffsetBottom = -2;
        _infoLabel.MouseFilter = MouseFilterEnum.Pass;
        _infoPanel.AddChild(_infoLabel);

        // 电力指示条（正：蓝 / 负：红）
        _powerBar = new ColorRect();
        _powerBar.CustomMinimumSize = new Vector2(0, 2);
        _powerBar.Color = new Color(0.2f, 0.5f, 0.9f, 0.8f);
        _powerBar.AnchorLeft = 0.04f; _powerBar.AnchorRight = 0.96f;
        _powerBar.AnchorTop = 0.90f; _powerBar.AnchorBottom = 0.95f;
        _powerBar.MouseFilter = MouseFilterEnum.Pass;
        _infoPanel.AddChild(_powerBar);

        // 分类标签区：建筑/步兵/车辆
        var tabsPanel = new Panel();
        tabsPanel.CustomMinimumSize = new Vector2(0, 28);
        var tabsStyle = new StyleBoxFlat { BgColor = CTitleBar, ContentMarginLeft = 2, ContentMarginRight = 2 };
        tabsPanel.AddThemeStyleboxOverride("panel", tabsStyle);
        tabsPanel.MouseFilter = MouseFilterEnum.Pass;
        root.AddChild(tabsPanel);

        var tabs = new HBoxContainer();
        tabs.AnchorRight = 1; tabs.AnchorBottom = 1;
        tabs.OffsetLeft = 2; tabs.OffsetTop = 2; tabs.OffsetRight = -2; tabs.OffsetBottom = -2;
        tabs.AddThemeConstantOverride("separation", 1);
        tabs.MouseFilter = MouseFilterEnum.Pass;
        tabsPanel.AddChild(tabs);

        _tabBuildings = MakeTabButton(TrManager.Tr("build.tab_buildings"), BuildTab.Buildings);
        _tabInfantry  = MakeTabButton(TrManager.Tr("build.tab_infantry"), BuildTab.Infantry);
        _tabVehicles  = MakeTabButton(TrManager.Tr("build.tab_vehicles"), BuildTab.Vehicles);
        tabs.AddChild(_tabBuildings);
        tabs.AddChild(_tabInfantry);
        tabs.AddChild(_tabVehicles);

        _buildingGrid = MakeGrid();
        root.AddChild(_buildingGrid);

        _infantryGrid = MakeGrid();
        _infantryGrid.Visible = false;
        root.AddChild(_infantryGrid);

        _vehicleGrid = MakeGrid();
        _vehicleGrid.Visible = false;
        root.AddChild(_vehicleGrid);

        // 底部提示区：暗色金属底板
        _hintPanel = new Panel();
        _hintPanel.CustomMinimumSize = new Vector2(W - 12, 74);
        var hintStyle = new StyleBoxFlat
        {
            BgColor = CHintPlate,
            BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            BorderColor = CSteelBorder,
            ContentMarginLeft = 4, ContentMarginRight = 4,
            ContentMarginTop = 3, ContentMarginBottom = 3
        };
        _hintPanel.AddThemeStyleboxOverride("panel", hintStyle);
        _hintPanel.MouseFilter = MouseFilterEnum.Pass;
        root.AddChild(_hintPanel);

        _hintLabel = new RichTextLabel();
        _hintLabel.BbcodeEnabled = true;
        _hintLabel.AddThemeFontSizeOverride("normal_font_size", 12);
        _hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hintLabel.AnchorRight = 1; _hintLabel.AnchorBottom = 1;
        _hintLabel.OffsetLeft = 2; _hintLabel.OffsetTop = 2; _hintLabel.OffsetRight = -2; _hintLabel.OffsetBottom = -2;
        _hintLabel.MouseFilter = MouseFilterEnum.Pass;
        _hintPanel.AddChild(_hintLabel);

        CreateItems();

        // 初始刷新标签视觉
        UpdateTabVisuals();
    }

    /// <summary>RA2风格面板边框绘制：外钢银线 + 内暗线 + 四角铆钉装饰。</summary>
    public override void _Draw()
    {
        var sz = Size;
        float margin = 1f;

        // 外边框：钢银色2px线
        DrawRect(new Rect2(margin, margin, sz.X - margin * 2, sz.Y - margin * 2), CSteelBorder, false, 2f);
        // 内边框：暗色1px线（产生立体凹槽感）
        float inner = margin + 3f;
        DrawRect(new Rect2(inner, inner, sz.X - inner * 2, sz.Y - inner * 2), CDarkBorder, false, 1f);

        // 四角铆钉装饰：短斜线
        float cornerLen = 8f;
        float cornerOffset = 4f;
        // 左上角
        DrawLine(new Vector2(cornerOffset, cornerOffset), new Vector2(cornerOffset + cornerLen, cornerOffset + cornerLen), CSteelBorder, 1.5f);
        DrawLine(new Vector2(cornerOffset + 2, cornerOffset), new Vector2(cornerOffset + cornerLen, cornerOffset + cornerLen - 2), CDarkBorder, 1f);
        // 右下角
        DrawLine(new Vector2(sz.X - cornerOffset, sz.Y - cornerOffset), new Vector2(sz.X - cornerOffset - cornerLen, sz.Y - cornerOffset - cornerLen), CSteelBorder, 1.5f);
        DrawLine(new Vector2(sz.X - cornerOffset - 2, sz.Y - cornerOffset), new Vector2(sz.X - cornerOffset - cornerLen, sz.Y - cornerOffset - cornerLen + 2), CDarkBorder, 1f);
        // 左下角
        DrawLine(new Vector2(cornerOffset, sz.Y - cornerOffset), new Vector2(cornerOffset + cornerLen, sz.Y - cornerOffset - cornerLen), CSteelBorder, 1.5f);
        // 右上角
        DrawLine(new Vector2(sz.X - cornerOffset, cornerOffset), new Vector2(sz.X - cornerOffset - cornerLen, cornerOffset + cornerLen), CSteelBorder, 1.5f);
    }

    private Button MakeTabButton(string text, BuildTab tab)
    {
        var b = new Button { Text = text, ToggleMode = true, ButtonPressed = (tab == _currentTab) };
        b.AddThemeFontSizeOverride("font_size", 14);
        b.AddThemeColorOverride("font_color", new Color(0.65f, 0.60f, 0.42f));
        b.AddThemeColorOverride("font_pressed_color", CGoldText);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_hover_pressed_color", CGoldText);
        // RA2标签按钮金属底色
        var tabStyle = new StyleBoxFlat
        {
            BgColor = (tab == _currentTab) ? CTabActive : CTabInactive,
            BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            BorderColor = (tab == _currentTab) ? CGoldBorder : CDarkBorder,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 3, ContentMarginBottom = 3
        };
        b.AddThemeStyleboxOverride("normal", tabStyle);
        // 选中时顶部1px亮线（凸起感）
        var pressedStyle = new StyleBoxFlat
        {
            BgColor = CTabActive,
            BorderWidthTop = 2, BorderWidthBottom = 0, BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderColor = CSteelBorder,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 3, ContentMarginBottom = 3
        };
        b.AddThemeStyleboxOverride("pressed", pressedStyle);
        b.AddThemeStyleboxOverride("hover_pressed", pressedStyle);
        var hoverStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.20f, 0.17f, 0.10f, 1f),
            BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            BorderColor = CSteelBorder,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 3, ContentMarginBottom = 3
        };
        b.AddThemeStyleboxOverride("hover", hoverStyle);
        b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        b.Pressed += () => { ShowTab(tab); };
        return b;
    }

    /// <summary>更新三个标签按钮的视觉状态（选中/未选中配色）。</summary>
    private void UpdateTabVisuals()
    {
        UpdateSingleTabStyle(_tabBuildings, _currentTab == BuildTab.Buildings);
        UpdateSingleTabStyle(_tabInfantry, _currentTab == BuildTab.Infantry);
        UpdateSingleTabStyle(_tabVehicles, _currentTab == BuildTab.Vehicles);
    }

    private static void UpdateSingleTabStyle(Button b, bool isActive)
    {
        var normalStyle = new StyleBoxFlat
        {
            BgColor = isActive ? CTabActive : CTabInactive,
            BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            BorderColor = isActive ? CGoldBorder : CDarkBorder,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 3, ContentMarginBottom = 3
        };
        b.AddThemeStyleboxOverride("normal", normalStyle);
        var pressedStyle = new StyleBoxFlat
        {
            BgColor = CTabActive,
            BorderWidthTop = 2, BorderWidthBottom = 0, BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderColor = CSteelBorder,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 3, ContentMarginBottom = 3
        };
        b.AddThemeStyleboxOverride("pressed", pressedStyle);
        b.AddThemeStyleboxOverride("hover_pressed", pressedStyle);
        b.AddThemeColorOverride("font_color", isActive ? CGoldText : new Color(0.65f, 0.60f, 0.42f));
    }

    private static GridContainer MakeGrid()
    {
        var g = new GridContainer { Columns = 2 };
        g.AddThemeConstantOverride("h_separation", 4);
        g.AddThemeConstantOverride("v_separation", 4);
        g.SizeFlagsVertical = SizeFlags.ExpandFill;
        return g;
    }

    private void ShowTab(BuildTab tab)
    {
        _currentTab = tab;
        _tabBuildings.ButtonPressed = tab == BuildTab.Buildings;
        _tabInfantry.ButtonPressed  = tab == BuildTab.Infantry;
        _tabVehicles.ButtonPressed  = tab == BuildTab.Vehicles;
        _buildingGrid.Visible = tab == BuildTab.Buildings;
        _infantryGrid.Visible = tab == BuildTab.Infantry;
        _vehicleGrid.Visible  = tab == BuildTab.Vehicles;
        UpdateTabVisuals();
    }

    private void CreateItems()
    {
        // P2-2修复：造价从GameData获取，不再使用硬编码字面量
        // 建筑（电站/兵营/车厂/科技/防御设施）
        AddItem(TrManager.Tr("build.power_plant"), GameData.GetBuildingCost(BuildingType.PowerPlant), _iPower, true, BuildingType.PowerPlant, UnitType.Default, false, BuildTab.Buildings);
        AddItem(TrManager.Tr("build.barracks"), GameData.GetBuildingCost(BuildingType.Barracks), _iBarracks, true, BuildingType.Barracks, UnitType.Default, false, BuildTab.Buildings);
        AddItem(TrManager.Tr("build.war_factory"), GameData.GetBuildingCost(BuildingType.WarFactory), _iWar, true, BuildingType.WarFactory, UnitType.Default, false, BuildTab.Buildings);
        AddItem(TrManager.Tr("build.tech"), GameData.GetBuildingCost(BuildingType.TechCenter), _iTech, true, BuildingType.TechCenter, UnitType.Default, false, BuildTab.Buildings);
        // 阶段12-A1+A2 新增建筑
        AddItem(TrManager.Tr("build.machine_gun_turret"), GameData.GetBuildingCost(BuildingType.Turret), _iTurret, true, BuildingType.Turret, UnitType.Default, false, BuildTab.Buildings);
        AddItem(TrManager.Tr("build.anti_air_turret"), GameData.GetBuildingCost(BuildingType.AntiAirTurret), _iAntiAir, true, BuildingType.AntiAirTurret, UnitType.Default, false, BuildTab.Buildings);
        AddItem(TrManager.Tr("build.repair_station"), GameData.GetBuildingCost(BuildingType.RepairPad), _iRepairPad, true, BuildingType.RepairPad, UnitType.Default, false, BuildTab.Buildings);
        // E7：机场
        AddItem(TrManager.Tr("build.airfield"), GameData.GetBuildingCost(BuildingType.Airfield), _iAirfield, true, BuildingType.Airfield, UnitType.Default, false, BuildTab.Buildings);
        // E9：船厂
        AddItem(TrManager.Tr("build.shipyard_panel"), GameData.GetBuildingCost(BuildingType.Shipyard), _iShipyard, true, BuildingType.Shipyard, UnitType.Default, false, BuildTab.Buildings);
        // E10：超武建筑
        AddItem(TrManager.Tr("build.nuke_silo_panel"), GameData.GetBuildingCost(BuildingType.NukeSilo), _iNukeSilo, true, BuildingType.NukeSilo, UnitType.Default, false, BuildTab.Buildings);
        AddItem(TrManager.Tr("build.lightning_tower_panel"), GameData.GetBuildingCost(BuildingType.LightningTower), _iLightningTower, true, BuildingType.LightningTower, UnitType.Default, false, BuildTab.Buildings);
        AddItem(TrManager.Tr("build.missile_silo_panel"), GameData.GetBuildingCost(BuildingType.MissileSilo), _iMissileSilo, true, BuildingType.MissileSilo, UnitType.Default, false, BuildTab.Buildings);
        // 步兵（按价格升序）
        AddItem(TrManager.Tr("build.infantry_unit"), GameData.GetUnitCost(UnitType.Infantry), _iInfantry, false, BuildingType.Base, UnitType.Infantry, false, BuildTab.Infantry);
        AddItem(TrManager.Tr("build.grenadier"), GameData.GetUnitCost(UnitType.Grenadier), _iGrenadier, false, BuildingType.Base, UnitType.Grenadier, false, BuildTab.Infantry);
        AddItem(TrManager.Tr("build.flame_infantry"), GameData.GetUnitCost(UnitType.FlameInfantry), _iFlameInfantry, false, BuildingType.Base, UnitType.FlameInfantry, false, BuildTab.Infantry);
        AddItem(TrManager.Tr("build.sniper"), GameData.GetUnitCost(UnitType.Sniper), _iSniper, false, BuildingType.Base, UnitType.Sniper, false, BuildTab.Infantry);
        // E6b：特殊步兵
        AddItem(TrManager.Tr("build.thief"), GameData.GetUnitCost(UnitType.Thief), _iThief, false, BuildingType.Base, UnitType.Thief, false, BuildTab.Infantry);
        AddItem(TrManager.Tr("build.hero"), GameData.GetUnitCost(UnitType.Hero), _iHero, false, BuildingType.TechCenter, UnitType.Hero, false, BuildTab.Infantry);
        AddItem(TrManager.Tr("build.spy"), GameData.GetUnitCost(UnitType.Spy), _iSpy, false, BuildingType.TechCenter, UnitType.Spy, false, BuildTab.Infantry);
        // E7：火箭兵
        AddItem(TrManager.Tr("build.rocket_infantry"), GameData.GetUnitCost(UnitType.RocketInfantry), _iRocketInfantry, false, BuildingType.Barracks, UnitType.RocketInfantry, false, BuildTab.Infantry);
        // 车辆（按价格升序排列：基础→中级→高级）
        AddItem(TrManager.Tr("build.light_tank"),   GameData.GetUnitCost(UnitType.LightTank),   _iLight,   false, BuildingType.Base, UnitType.LightTank,      false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.anti_air_vehicle"), GameData.GetUnitCost(UnitType.AntiAir), _iAntiAirUnit, false, BuildingType.Base, UnitType.AntiAir,        false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.engineer"), GameData.GetUnitCost(UnitType.Engineer), _iEngineer,false, BuildingType.Base, UnitType.Engineer,       false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.transport"), GameData.GetUnitCost(UnitType.Transport), _iTransport, false, BuildingType.WarFactory, UnitType.Transport, false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.artillery"),   GameData.GetUnitCost(UnitType.Artillery),   _iArt,     false, BuildingType.Base, UnitType.Artillery,      false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.heavy_tank"),   GameData.GetUnitCost(UnitType.HeavyTank),   _iHeavy,   false, BuildingType.Base, UnitType.HeavyTank,      false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.harvester"),   GameData.GetUnitCost(UnitType.Harvester),   _iHarv,    false, BuildingType.Base, UnitType.Default,       true,  BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.rocket_launcher"), GameData.GetUnitCost(UnitType.RocketLauncher), _iRocket,  false, BuildingType.Base, UnitType.RocketLauncher, false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.missile_tank"), GameData.GetUnitCost(UnitType.MissileTank), _iMissile, false, BuildingType.Base, UnitType.MissileTank,    false, BuildTab.Vehicles);
        // E7：空军
        AddItem(TrManager.Tr("build.fighter"), GameData.GetUnitCost(UnitType.Fighter), _iFighter, false, BuildingType.Airfield, UnitType.Fighter,       false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.helicopter"), GameData.GetUnitCost(UnitType.Helicopter), _iHelicopter, false, BuildingType.Airfield, UnitType.Helicopter, false, BuildTab.Vehicles);
        // E8：扩展空军
        AddItem(TrManager.Tr("build.bomber"), GameData.GetUnitCost(UnitType.Bomber), _iBomber, false, BuildingType.Airfield, UnitType.Bomber,         false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.scout"), GameData.GetUnitCost(UnitType.Scout), _iScout, false, BuildingType.Airfield, UnitType.Scout,           false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.transport_heli"),   GameData.GetUnitCost(UnitType.TransportHeli), _iTransportHeli, false, BuildingType.Airfield, UnitType.TransportHeli, false, BuildTab.Vehicles);
        // E9：海军
        AddItem(TrManager.Tr("build.destroyer"),  GameData.GetUnitCost(UnitType.Destroyer),  _iDestroyer,  false, BuildingType.Shipyard, UnitType.Destroyer,     false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.submarine"),    GameData.GetUnitCost(UnitType.Submarine),  _iSubmarine,  false, BuildingType.Shipyard, UnitType.Submarine,      false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.carrier"),   GameData.GetUnitCost(UnitType.AircraftCarrier), _iCarrier,    false, BuildingType.Shipyard, UnitType.AircraftCarrier, false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.landing_craft"),  GameData.GetUnitCost(UnitType.LandingCraft), _iLandingCraft, false, BuildingType.Shipyard, UnitType.LandingCraft,  false, BuildTab.Vehicles);
        // RA2标志单位
        AddItem(TrManager.Tr("build.apocalypse_tank"), GameData.GetUnitCost(UnitType.ApocalypseTank), _iApocalypseTank, false, BuildingType.TechCenter, UnitType.ApocalypseTank, false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.prism_tank"), GameData.GetUnitCost(UnitType.PrismTank), _iPrismTank, false, BuildingType.TechCenter, UnitType.PrismTank, false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.kirov_airship"), GameData.GetUnitCost(UnitType.KirovAirship), _iKirovAirship, false, BuildingType.TechCenter, UnitType.KirovAirship, false, BuildTab.Vehicles);
        AddItem(TrManager.Tr("build.tesla_trooper"), GameData.GetUnitCost(UnitType.TeslaTrooper), _iTeslaTrooper, false, BuildingType.TechCenter, UnitType.TeslaTrooper, false, BuildTab.Infantry);
    }

    private void AddItem(string name, int cost, Texture2D? icon, bool isBuilding, BuildingType bt, UnitType ut, bool harv, BuildTab tab)
    {
        var item = new BuildItem
        {
            Name = name, Cost = cost, Icon = icon,
            IsBuilding = isBuilding, BType = bt, UType = ut, IsHarvester = harv
        };

        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(102, 88);
        // RA2双线凹槽：外圈钢银色1px + 内圈暗色1px
        var style = new StyleBoxFlat
        {
            BgColor = CReady,
            BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            BorderColor = CSteelBorder,
            ContentMarginLeft = 0, ContentMarginRight = 0,
            ContentMarginTop = 0, ContentMarginBottom = 0
        };
        panel.AddThemeStyleboxOverride("panel", style);

        var bgRect = new ColorRect();
        bgRect.Color = CReady;
        bgRect.AnchorRight = 1; bgRect.AnchorBottom = 1;
        bgRect.MouseFilter = MouseFilterEnum.Pass;
        panel.AddChild(bgRect);

        var vbox = new VBoxContainer();
        vbox.AnchorRight = 1; vbox.AnchorBottom = 1;
        vbox.OffsetLeft = 2; vbox.OffsetTop = 2; vbox.OffsetRight = -2; vbox.OffsetBottom = -2;
        vbox.AddThemeConstantOverride("separation", 1);
        vbox.MouseFilter = MouseFilterEnum.Pass;
        panel.AddChild(vbox);

        var iconRect = new TextureRect();
        iconRect.Texture = icon;
        iconRect.CustomMinimumSize = new Vector2(56, 46);
        iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        iconRect.SizeFlagsHorizontal = SizeFlags.Fill;
        iconRect.MouseFilter = MouseFilterEnum.Pass;
        // 建筑PNG原图显示（已带金属色），单位灰底PNG染色为玩家阵营色
        if (!isBuilding && !harv && icon != null)
            iconRect.Modulate = Unit.GetTeamColor(0); // 玩家方阵营色
        else if (harv && icon != null)
            iconRect.Modulate = Unit.GetTeamColor(0); // 矿车也染玩家色
        vbox.AddChild(iconRect);

        var nameLabel = new Label();
        nameLabel.Text = name;
        nameLabel.AddThemeFontSizeOverride("font_size", 12);
        nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
        nameLabel.MouseFilter = MouseFilterEnum.Pass;
        vbox.AddChild(nameLabel);

        var costLabel = new Label();
        costLabel.Text = $"${cost}";
        costLabel.AddThemeFontSizeOverride("font_size", 12);
        costLabel.HorizontalAlignment = HorizontalAlignment.Center;
        costLabel.MouseFilter = MouseFilterEnum.Pass;
        vbox.AddChild(costLabel);

        item.PanelNode = panel;
        item.CostLabel = costLabel;
        item.BgRect = bgRect;

        // 生产队列UI（仅非建筑单位）：右上角数量标签 + 底部进度条
        if (!isBuilding)
        {
            var badge = new Label();
            badge.Text = "";
            badge.AddThemeFontSizeOverride("font_size", 14);
            badge.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.3f));
            badge.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
            badge.AddThemeConstantOverride("outline_size", 2);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Top;
            badge.AnchorLeft = 0.35f; badge.AnchorTop = 0f;
            badge.AnchorRight = 0.95f; badge.AnchorBottom = 0.3f;
            badge.MouseFilter = MouseFilterEnum.Pass;
            panel.AddChild(badge);
            item.QueueBadge = badge;

            var bar = new ProgressBar();
            bar.MinValue = 0f; bar.MaxValue = 1f; bar.Value = 0f;
            bar.CustomMinimumSize = new Vector2(0, 5);
            bar.AnchorLeft = 0.05f; bar.AnchorTop = 0.88f;
            bar.AnchorRight = 0.95f; bar.AnchorBottom = 0.96f;
            bar.MouseFilter = MouseFilterEnum.Pass;
            bar.ShowPercentage = false;
            var barStyle = new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.1f, 0.8f) };
            bar.AddThemeStyleboxOverride("background", barStyle);
            var fillStyle = new StyleBoxFlat { BgColor = new Color(0.3f, 0.8f, 0.3f, 0.9f) };
            bar.AddThemeStyleboxOverride("fill", fillStyle);
            bar.Visible = false;
            panel.AddChild(bar);
            item.ProdBar = bar;
        }

        // 悬停（触发局部刷新以更新高亮配色）
        panel.MouseEntered += () => { _hoverItem = item; RefreshVisuals(); };
        panel.MouseExited += () => { if (_hoverItem == item) { _hoverItem = null; RefreshVisuals(); } };
        // 点击
        panel.GuiInput += (@event) => OnItemGuiInput(@event, item);

        if (isBuilding) _buildingGrid.AddChild(panel);
        else if (tab == BuildTab.Infantry) _infantryGrid.AddChild(panel);
        else _vehicleGrid.AddChild(panel);

        _items.Add(item);
    }

    private void OnItemGuiInput(InputEvent @event, BuildItem item)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (item.IsLocked) return;
            if (!item.CanAfford) return;

            if (item.IsBuilding)
            {
                ActivePlacement = item.BType;
                BuildBuildingRequested?.Invoke(item.BType);
            }
            else if (item.IsHarvester)
            {
                BuildHarvesterRequested?.Invoke();
            }
            else
            {
                BuildUnitRequested?.Invoke(item.UType);
            }
        }
    }

    /// <summary>由 Main 每帧/定期调用刷新所有按钮状态。</summary>
    public void UpdateState(int money, int power, int techLevel, int unitCount, int unitCap,
        bool hasBase, bool hasPower, bool hasBarracks, bool hasWarFactory, bool hasTechCenter,
        bool hasAirfield = false, bool hasShipyard = false)
    {
        _money = money; _power = power; _playerTechLevel = techLevel;
        _unitCount = unitCount; _unitCap = unitCap;
        _hasBase = hasBase; _hasPower = hasPower; _hasBarracks = hasBarracks;
        _hasWarFactory = hasWarFactory; _hasTechCenter = hasTechCenter;
        HasAirfield = hasAirfield; HasShipyard = hasShipyard;

        foreach (var it in _items)
        {
            it.CanAfford = _money >= it.Cost;
            it.IsLocked = false;
            it.LockReason = "";

            if (it.IsBuilding)
            {
                EvaluateBuildingLock(it);
            }
            else if (it.IsHarvester)
            {
                if (!_hasBase) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_base"); }
                else if (_unitCount >= _unitCap) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_unit_full"); }
            }
            else
            {
                EvaluateUnitLock(it);
            }

            // 资金不足也算锁定原因（但不置灰整块，仅成本变红）
            if (!it.CanAfford && string.IsNullOrEmpty(it.LockReason))
                it.LockReason = TrManager.Tr("build.lock_money_low");
        }

        RefreshVisuals();
    }

    /// <summary>更新生产队列显示（由Main每帧调用）。传入每个UnitType的队列数和最高进度。</summary>
    public void UpdateProductionQueue(Dictionary<UnitType, (int count, float progress, float timeRemaining)> queueData)
    {
        foreach (var it in _items)
        {
            if (it.IsBuilding) continue;

            var ut = it.IsHarvester ? UnitType.Default : it.UType;
            if (queueData.TryGetValue(ut, out var info))
            {
                it.QueueCount = info.count;
                it.ProdProgress = info.progress;
                it._timeRemaining = info.timeRemaining;
            }
            else
            {
                it.QueueCount = 0;
                it.ProdProgress = 0f;
                it._timeRemaining = 0f;
            }
        }
        // 在 RefreshVisuals 中更新UI
    }
    private void EvaluateBuildingLock(BuildItem it)
    {
        switch (it.BType)
        {
            case BuildingType.PowerPlant:
                if (!_hasBase) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_base"); }
                break;
            case BuildingType.Barracks:
                if (!_hasPower) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_power"); }
                else if (_playerTechLevel < 1) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_difficulty"); }
                break;
            case BuildingType.WarFactory:
                if (!_hasBarracks) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_barracks"); }
                else if (_playerTechLevel < 2) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_difficulty"); }
                break;
            case BuildingType.TechCenter:
                if (!_hasWarFactory) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_warfactory"); }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_power_low"); }
                else if (_playerTechLevel < 3) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_difficulty"); }
                break;
            // 阶段12-A1+A2 新增建筑
            case BuildingType.Turret:
                if (!_hasBarracks) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_barracks"); }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_power_low"); }
                break;
            case BuildingType.AntiAirTurret:
                if (!_hasWarFactory) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_warfactory"); }
                else if (_playerTechLevel < 2) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_difficulty"); }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_power_low"); }
                break;
            case BuildingType.RepairPad:
                if (!_hasWarFactory) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_warfactory"); }
                else if (_playerTechLevel < 2) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_difficulty"); }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_power_low"); }
                break;
            // E7：机场
            case BuildingType.Airfield:
                if (!_hasTechCenter) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_techcenter"); }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_power_low"); }
                break;
            // E9：船厂
            case BuildingType.Shipyard:
                if (!_hasTechCenter) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_techcenter"); }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_power_low"); }
                break;
            // E10：超武建筑
            case BuildingType.NukeSilo:
                if (!_hasTechCenter) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_techcenter"); }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_power_low"); }
                break;
            case BuildingType.LightningTower:
                if (!_hasTechCenter) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_techcenter"); }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_power_low"); }
                break;
            case BuildingType.MissileSilo:
                if (!_hasTechCenter) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_techcenter"); }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_power_low"); }
                break;
        }
    }

    private void EvaluateUnitLock(BuildItem it)
    {
        if (_power < 0) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_power_low"); return; }
        if (_unitCount >= _unitCap) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_unit_full"); return; }

        switch (it.UType)
        {
            case UnitType.LightTank:
            case UnitType.Infantry:
            case UnitType.Grenadier:       // E6
            case UnitType.FlameInfantry:   // E6
            case UnitType.Sniper:          // E6
            case UnitType.Thief:          // E6b
            case UnitType.RocketInfantry:   // E7
                if (!_hasBarracks) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_barracks"); }
                break;
            case UnitType.HeavyTank:
            case UnitType.Artillery:
            case UnitType.AntiAir:
            case UnitType.Engineer:
            case UnitType.Transport:       // E6
                if (!_hasWarFactory) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_warfactory"); }
                break;
            case UnitType.Fighter:          // E7
            case UnitType.Helicopter:       // E7
            case UnitType.Bomber:           // E8
            case UnitType.Scout:            // E8
            case UnitType.TransportHeli:    // E8
                if (!_hasWarFactory) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_warfactory"); }
                else if (!HasAirfield) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_airfield"); }
                break;
            // E9：海军单位需船厂
            case UnitType.Destroyer:
            case UnitType.Submarine:
            case UnitType.AircraftCarrier:
            case UnitType.LandingCraft:
                if (!HasShipyard) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_shipyard"); }
                break;
            case UnitType.ApocalypseTank:
            case UnitType.PrismTank:
            case UnitType.KirovAirship:
            case UnitType.TeslaTrooper:
                if (!_hasTechCenter) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_techcenter"); }
                break;
            case UnitType.RocketLauncher:
            case UnitType.MissileTank:
            case UnitType.Hero:           // E6b
            case UnitType.Spy:            // E6b
                if (!_hasTechCenter) { it.IsLocked = true; it.LockReason = TrManager.Tr("build.lock_need_techcenter"); }
                break;
        }
    }

    private void RefreshVisuals()
    {
        var powerWarn = _power < 0 ? $" [color=#ff5555]{TrManager.Tr("build.lock_power_low")}![/color]" : "";
        // 资金用金色大字体，电力正/负用蓝/红
        string powerColor = _power < 0 ? "#ff5555" : "#88ccff";
        _infoLabel.Text = $"[color=#ffd54f]{DifficultyName}[/color]  {TrManager.Tr("build.tech")}Lv{_playerTechLevel}\n" +
                          $"[color=#ffd24f][b][font_size=22]${_money}[/font_size][/b][/color]   {_unitCount}/{_unitCap} {TrManager.Tr("build.tab_infantry")}\n" +
                          $"[color={powerColor}]{TrManager.Tr("ui.power_label", _power)}{powerWarn}[/color]";

        // 电力指示条颜色：正蓝色 / 负红色
        if (_powerBar != null)
            _powerBar.Color = _power < 0
                ? new Color(0.8f, 0.2f, 0.15f, 0.85f)
                : new Color(0.2f, 0.5f, 0.9f, 0.8f);

        foreach (var it in _items)
        {
            if (it.PanelNode == null) continue;
            Color bg, borderColor;
            bool placementActive = (it.IsBuilding && ActivePlacement == it.BType);

            // RA2配色：锁定/选中/可建造/资金不足/悬停
            if (it.IsLocked)
            {
                bg = CLocked;
                borderColor = CLockedBorder;
                it.PanelNode.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            }
            else if (placementActive)
            {
                bg = CSelected;
                borderColor = CSelectedBorder;
                it.PanelNode.Modulate = Colors.White;
            }
            else if (!it.CanAfford)
            {
                bg = CCantAfford;
                borderColor = CCantAffordBorder;
                it.PanelNode.Modulate = Colors.White;
            }
            else if (_hoverItem == it)
            {
                bg = CHover;
                borderColor = CHoverBorder;
                it.PanelNode.Modulate = Colors.White;
            }
            else
            {
                bg = CReady;
                borderColor = CSteelBorder;
                it.PanelNode.Modulate = Colors.White;
            }

            // 更新底板背景色
            if (it.BgRect != null) it.BgRect.Color = bg;

            // 更新Panel边框（RA2双线凹槽：钢银外线 + 暗内线）
            if (it.PanelNode != null)
            {
                var sb = new StyleBoxFlat
                {
                    BgColor = bg,
                    BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
                    BorderColor = borderColor
                };
                it.PanelNode.AddThemeStyleboxOverride("panel", sb);
            }

            // 成本颜色
            if (it.CostLabel != null)
            {
                if (it.IsLocked)
                    it.CostLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
                else if (!it.CanAfford)
                    it.CostLabel.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.3f));
                else
                    it.CostLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
            }

            // 生产队列UI：数量标签 + 进度条
            if (it.QueueBadge != null)
            {
                if (it.QueueCount > 0)
                {
                    it.QueueBadge.Text = it.QueueCount > 1 ? $"×{it.QueueCount}" : "●";
                    it.QueueBadge.Visible = true;
                }
                else
                    it.QueueBadge.Visible = false;
            }
            if (it.ProdBar != null)
            {
                if (it.QueueCount > 0 && it.ProdProgress > 0f)
                {
                    it.ProdBar.Value = it.ProdProgress;
                    it.ProdBar.Visible = true;
                }
                else
                    it.ProdBar.Visible = false;
            }
        }

        // 悬停提示
        if (_hoverItem != null)
        {
            var h = _hoverItem;
            string status = h.IsLocked ? $"[color=#ff7777]{h.LockReason}[/color]"
                          : !h.CanAfford ? $"[color=#ffaa55]{TrManager.Tr("build.lock_money_low")}[/color]"
                          : $"[color=#77ff77]{TrManager.Tr("build.hint_can_build")}[/color]";
            string queueInfo = h.QueueCount > 0
                ? $"\n[color=#88ff88]{TrManager.Tr("build.hint_queue", h.QueueCount, $"{h.ProdProgress * 100:F0}")}"
                + (h._timeRemaining > 0f ? $"  {TrManager.Tr("build.hint_remaining", $"{h._timeRemaining:F1}")}[/color]"
                : "[/color]")
                : "";
            _hintLabel.Text = $"{h.Name}  ${h.Cost}\n{GetItemDesc(h)}\n{status}{queueInfo}";
        }
        else
        {
            _hintLabel.Text = ActivePlacement != null
                ? $"[color=#66ff99]{TrManager.Tr("build.hint_place_mode")}[/color]\n{TrManager.Tr("build.hint_place_click", ActivePlacement)}\n{TrManager.Tr("build.hint_cancel")}"
                : $"{TrManager.Tr("build.hint_click_build")}\n{TrManager.Tr("build.hint_drag_select")}\n{TrManager.Tr("build.hint_right_move")}";
        }
    }

    private string GetItemDesc(BuildItem it)
    {
        if (it.IsBuilding)
        {
            return it.BType switch
            {
                BuildingType.PowerPlant => TrManager.Tr("build.desc_power_plant"),
                BuildingType.Barracks => TrManager.Tr("build.desc_barracks"),
                BuildingType.WarFactory => TrManager.Tr("build.desc_war_factory"),
                BuildingType.TechCenter => TrManager.Tr("build.desc_tech_center"),
                BuildingType.Turret => TrManager.Tr("build.desc_turret"),
                BuildingType.AntiAirTurret => TrManager.Tr("build.desc_anti_air_turret"),
                BuildingType.RepairPad => TrManager.Tr("build.desc_repair_pad"),
                BuildingType.Airfield => TrManager.Tr("build.desc_airfield"),
                BuildingType.Shipyard => TrManager.Tr("build.desc_shipyard"),
                BuildingType.NukeSilo => TrManager.Tr("build.desc_nuke_silo"),
                BuildingType.LightningTower => TrManager.Tr("build.desc_lightning_tower"),
                BuildingType.MissileSilo => TrManager.Tr("build.desc_missile_silo"),
                _ => ""
            };
        }
        if (it.IsHarvester) return TrManager.Tr("build.desc_harvester");
        return it.UType switch
        {
            UnitType.Infantry => TrManager.Tr("build.desc_infantry"),
            UnitType.LightTank => TrManager.Tr("build.desc_light_tank"),
            UnitType.HeavyTank => TrManager.Tr("build.desc_heavy_tank"),
            UnitType.Artillery => TrManager.Tr("build.desc_artillery"),
            UnitType.AntiAir => TrManager.Tr("build.desc_anti_air_unit"),
            UnitType.Engineer => TrManager.Tr("build.desc_engineer"),
            UnitType.Grenadier => TrManager.Tr("build.desc_grenadier"),
            UnitType.Sniper => TrManager.Tr("build.desc_sniper"),
            UnitType.FlameInfantry => TrManager.Tr("build.desc_flame_infantry"),
            UnitType.Transport => TrManager.Tr("build.desc_transport"),
            UnitType.Hero => TrManager.Tr("build.desc_hero"),
            UnitType.Spy => TrManager.Tr("build.desc_spy"),
            UnitType.Thief => TrManager.Tr("build.desc_thief"),
            UnitType.Fighter => TrManager.Tr("build.desc_fighter"),
            UnitType.Helicopter => TrManager.Tr("build.desc_helicopter"),
            UnitType.RocketInfantry => TrManager.Tr("build.desc_rocket_infantry"),
            UnitType.Bomber => TrManager.Tr("build.desc_bomber"),
            UnitType.Scout => TrManager.Tr("build.desc_scout"),
            UnitType.TransportHeli => TrManager.Tr("build.desc_transport_heli"),
            // E9：海军描述
            UnitType.Destroyer => TrManager.Tr("build.desc_destroyer"),
            UnitType.Submarine => TrManager.Tr("build.desc_submarine"),
            UnitType.AircraftCarrier => TrManager.Tr("build.desc_carrier"),
            UnitType.LandingCraft => TrManager.Tr("build.desc_landing_craft"),
            UnitType.RocketLauncher => TrManager.Tr("build.desc_rocket_launcher"),
            UnitType.MissileTank => TrManager.Tr("build.desc_missile_tank"),
            UnitType.ApocalypseTank => TrManager.Tr("build.desc_apocalypse_tank"),
            UnitType.PrismTank => TrManager.Tr("build.desc_prism_tank"),
            UnitType.KirovAirship => TrManager.Tr("build.desc_kirov_airship"),
            UnitType.TeslaTrooper => TrManager.Tr("build.desc_tesla_trooper"),
            _ => ""
        };
    }

    // ---------- 图标加载（使用真实 PNG 素材） ----------
    private void EnsureIcons()
    {
        if (_iPower != null) return;

        // 建筑PNG原图显示（已带金属/水泥色，玩家所见即所得）
        _iPower    = LoadPng("res://assets/sprites/buildings/powerplant.png");
        _iBarracks = LoadPng("res://assets/sprites/buildings/barracks.png");
        _iWar      = LoadPng("res://assets/sprites/buildings/warfactory.png");
        _iTech     = LoadPng("res://assets/sprites/buildings/techcenter.png");
        // 阶段12-A1+A2 新增建筑
        _iTurret    = LoadPng("res://assets/sprites/buildings/turret.png");
        _iAntiAir   = LoadPng("res://assets/sprites/buildings/antiair.png");
        _iRepairPad = LoadPng("res://assets/sprites/buildings/repairpad.png");

        // 灰底单位PNG，AddItem 时会染色为玩家阵营色
        _iInfantry = LoadPng("res://assets/sprites/units/infantry.png");
        _iLight  = LoadPng("res://assets/sprites/units/hull_light.png");
        _iHeavy  = LoadPng("res://assets/sprites/units/hull_heavy.png");
        _iArt    = LoadPng("res://assets/sprites/units/hull_arty.png");
        _iRocket = LoadPng("res://assets/sprites/units/hull_rocket.png");
        _iMissile= LoadPng("res://assets/sprites/units/hull_missile.png");
        _iHarv   = LoadPng("res://assets/sprites/units/harvester.png");
        _iAntiAirUnit= LoadPng("res://assets/sprites/units/turret_antiair.png");
        _iEngineer= LoadPng("res://assets/sprites/units/hull_engineer.png");
        _iTransport = LoadPng("res://assets/sprites/units/hull_transport.png");
        _iGrenadier = LoadPng("res://assets/sprites/units/grenadier.png");
        _iSniper    = LoadPng("res://assets/sprites/units/sniper.png");
        _iFlameInfantry = LoadPng("res://assets/sprites/units/flame_infantry.png");
        // E6b：特殊单位图标
        _iHero = LoadPng("res://assets/sprites/units/hero.png");
        _iSpy  = LoadPng("res://assets/sprites/units/spy.png");
        _iThief = LoadPng("res://assets/sprites/units/thief.png");
        // E7：空军图标
        _iFighter = LoadPng("res://assets/sprites/units/fighter.png");
        _iHelicopter = LoadPng("res://assets/sprites/units/helicopter.png");
        _iRocketInfantry = LoadPng("res://assets/sprites/units/rocket_infantry.png");
        _iAirfield = LoadPng("res://assets/sprites/buildings/airfield.png");
        // E8：扩展空军图标
        _iBomber = LoadPng("res://assets/sprites/units/bomber.png");
        _iScout = LoadPng("res://assets/sprites/units/scout.png");
        _iTransportHeli = LoadPng("res://assets/sprites/units/transport_heli.png");
        // E9：海军图标
        _iDestroyer = LoadPng("res://assets/sprites/units/destroyer.png");
        _iSubmarine = LoadPng("res://assets/sprites/units/submarine.png");
        _iCarrier = LoadPng("res://assets/sprites/units/carrier.png");
        _iLandingCraft = LoadPng("res://assets/sprites/units/landing_craft.png");
        _iShipyard = LoadPng("res://assets/sprites/buildings/shipyard.png");
        // E10：超武建筑图标
        _iNukeSilo = LoadPng("res://assets/sprites/buildings/nuke_silo.png");
        _iLightningTower = LoadPng("res://assets/sprites/buildings/lightning_tower.png");
        _iMissileSilo = LoadPng("res://assets/sprites/buildings/missile_silo.png");
        // RA2标志单位图标（复用现有素材）
        _iApocalypseTank = LoadPng("res://assets/sprites/units/hull_heavy.png");
        _iPrismTank = LoadPng("res://assets/sprites/units/hull_rocket.png");
        _iKirovAirship = LoadPng("res://assets/sprites/units/bomber.png");
        _iTeslaTrooper = LoadPng("res://assets/sprites/units/infantry.png");
    }

    /// <summary>加载 PNG 纹理，失败时打印错误但不中断。</summary>
    private static Texture2D? LoadPng(string path)
    {
        var tex = GD.Load<Texture2D>(path);
        if (tex == null)
            GameLog.Error($"[BuildPanel] Failed to load icon: {path}");
        return tex;
    }
}
