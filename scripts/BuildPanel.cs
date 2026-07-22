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
    }

    private readonly List<BuildItem> _items = new();

    // 状态（由 Main 刷新）
    private int _money, _power, _playerTechLevel, _unitCount, _unitCap;
    private bool _hasBase, _hasPower, _hasBarracks, _hasWarFactory, _hasTechCenter;
    public BuildingType? ActivePlacement { get; set; }
    public string DifficultyName { get; set; } = "Normal";

    // 颜色：红警2 风格深灰金属 + 暗金高亮
    private static readonly Color CBg = new(0.13f, 0.14f, 0.16f, 0.96f);
    private static readonly Color CHover = new(0.34f, 0.29f, 0.18f, 0.97f);
    private static readonly Color CLocked = new(0.06f, 0.06f, 0.07f, 0.93f);
    private static readonly Color CSelected = new(0.58f, 0.44f, 0.16f, 0.98f);
    private static readonly Color CReady = new(0.19f, 0.20f, 0.22f, 0.96f);
    /// <summary>金色边框（建筑/单位图标外框）。</summary>
    private static readonly Color CGoldBorder = new(0.72f, 0.58f, 0.22f, 0.9f);
    /// <summary>金色文本（资金主数字、选中项高亮）。</summary>
    private static readonly Color CGoldText = new(1f, 0.82f, 0.32f, 1f);

    private const float W = 232f;

    // 图标（直接使用游戏 PNG 素材）
    private static Texture2D? _iPower, _iBarracks, _iWar, _iTech;
    // 阶段12-A1+A2 新增建筑图标
    private static Texture2D? _iTurret, _iAntiAir, _iRepairPad;
    private static Texture2D? _iLight, _iHeavy, _iArt, _iRocket, _iMissile, _iHarv, _iAntiAirUnit, _iEngineer, _iTransport;
    private static Texture2D? _iInfantry, _iGrenadier, _iSniper, _iFlameInfantry;
    // E6b：特殊单位图标
    private static Texture2D? _iHero, _iSpy, _iThief;

    // 悬停项
    private BuildItem? _hoverItem;

    public override void _Ready()
    {
        EnsureIcons();

        AnchorLeft = 1; AnchorRight = 1; AnchorTop = 0; AnchorBottom = 1;
        OffsetLeft = -W; OffsetRight = 0; OffsetTop = 0; OffsetBottom = 0;
        MouseFilter = MouseFilterEnum.Stop;

        var bg = new ColorRect();
        bg.Color = CBg;
        bg.AnchorRight = 1; bg.AnchorBottom = 1;
        bg.MouseFilter = MouseFilterEnum.Stop;
        AddChild(bg);

        var root = new VBoxContainer();
        root.AnchorRight = 1; root.AnchorBottom = 1;
        root.OffsetLeft = 8; root.OffsetTop = 8; root.OffsetRight = -8; root.OffsetBottom = -8;
        root.AddThemeConstantOverride("separation", 6);
        root.MouseFilter = MouseFilterEnum.Pass;
        AddChild(root);

        _infoLabel = new RichTextLabel();
        _infoLabel.BbcodeEnabled = true;
        _infoLabel.AddThemeFontSizeOverride("normal_font_size", 16);
        _infoLabel.FitContent = true;
        _infoLabel.CustomMinimumSize = new Vector2(W - 16, 64);
        root.AddChild(_infoLabel);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 3);
        _tabBuildings = MakeTabButton("建筑", BuildTab.Buildings);
        _tabInfantry  = MakeTabButton("步兵", BuildTab.Infantry);
        _tabVehicles  = MakeTabButton("车辆", BuildTab.Vehicles);
        tabs.AddChild(_tabBuildings);
        tabs.AddChild(_tabInfantry);
        tabs.AddChild(_tabVehicles);
        root.AddChild(tabs);

        _buildingGrid = MakeGrid();
        root.AddChild(_buildingGrid);

        _infantryGrid = MakeGrid();
        _infantryGrid.Visible = false;
        root.AddChild(_infantryGrid);

        _vehicleGrid = MakeGrid();
        _vehicleGrid.Visible = false;
        root.AddChild(_vehicleGrid);

        _hintLabel = new RichTextLabel();
        _hintLabel.BbcodeEnabled = true;
        _hintLabel.AddThemeFontSizeOverride("normal_font_size", 12);
        _hintLabel.CustomMinimumSize = new Vector2(W - 16, 70);
        _hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_hintLabel);

        CreateItems();
    }

    private Button MakeTabButton(string text, BuildTab tab)
    {
        var b = new Button { Text = text, ToggleMode = true, ButtonPressed = (tab == _currentTab) };
        b.AddThemeFontSizeOverride("font_size", 14);
        b.AddThemeColorOverride("font_color", new Color(0.8f, 0.74f, 0.52f));
        b.AddThemeColorOverride("font_pressed_color", CGoldText);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        b.Pressed += () => ShowTab(tab);
        return b;
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
    }

    private void CreateItems()
    {
        // 建筑（电站/兵营/车厂/科技/防御设施）
        AddItem("电站", 300, _iPower, true, BuildingType.PowerPlant, UnitType.Default, false, BuildTab.Buildings);
        AddItem("兵营", 400, _iBarracks, true, BuildingType.Barracks, UnitType.Default, false, BuildTab.Buildings);
        AddItem("车厂", 600, _iWar, true, BuildingType.WarFactory, UnitType.Default, false, BuildTab.Buildings);
        AddItem("科技", 800, _iTech, true, BuildingType.TechCenter, UnitType.Default, false, BuildTab.Buildings);
        // 阶段12-A1+A2 新增建筑
        AddItem("机枪塔", 400, _iTurret, true, BuildingType.Turret, UnitType.Default, false, BuildTab.Buildings);
        AddItem("防空炮", 600, _iAntiAir, true, BuildingType.AntiAirTurret, UnitType.Default, false, BuildTab.Buildings);
        AddItem("维修厂", 500, _iRepairPad, true, BuildingType.RepairPad, UnitType.Default, false, BuildTab.Buildings);
        // 步兵（按价格升序）
        AddItem("步兵", 100, _iInfantry, false, BuildingType.Base, UnitType.Infantry, false, BuildTab.Infantry);
        AddItem("掷弹兵", 200, _iGrenadier, false, BuildingType.Base, UnitType.Grenadier, false, BuildTab.Infantry);
        AddItem("喷火兵", 180, _iFlameInfantry, false, BuildingType.Base, UnitType.FlameInfantry, false, BuildTab.Infantry);
        AddItem("狙击手", 250, _iSniper, false, BuildingType.Base, UnitType.Sniper, false, BuildTab.Infantry);
        // E6b：特殊步兵
        AddItem("窃贼", 300, _iThief, false, BuildingType.Base, UnitType.Thief, false, BuildTab.Infantry);
        AddItem("英雄", 600, _iHero, false, BuildingType.TechCenter, UnitType.Hero, false, BuildTab.Infantry);
        AddItem("间谍", 500, _iSpy, false, BuildingType.TechCenter, UnitType.Spy, false, BuildTab.Infantry);
        // 车辆（按价格升序排列：基础→中级→高级）
        AddItem("轻坦",   200, _iLight,   false, BuildingType.Base, UnitType.LightTank,      false, BuildTab.Vehicles);
        AddItem("防空车", 300, _iAntiAirUnit, false, BuildingType.Base, UnitType.AntiAir,        false, BuildTab.Vehicles);
        AddItem("工程车", 300, _iEngineer,false, BuildingType.Base, UnitType.Engineer,       false, BuildTab.Vehicles);
        AddItem("运输车", 400, _iTransport, false, BuildingType.WarFactory, UnitType.Transport, false, BuildTab.Vehicles);
        AddItem("炮兵",   400, _iArt,     false, BuildingType.Base, UnitType.Artillery,      false, BuildTab.Vehicles);
        AddItem("重坦",   500, _iHeavy,   false, BuildingType.Base, UnitType.HeavyTank,      false, BuildTab.Vehicles);
        AddItem("矿车",   500, _iHarv,    false, BuildingType.Base, UnitType.Default,       true,  BuildTab.Vehicles);
        AddItem("火箭炮", 600, _iRocket,  false, BuildingType.Base, UnitType.RocketLauncher, false, BuildTab.Vehicles);
        AddItem("导弹车", 800, _iMissile, false, BuildingType.Base, UnitType.MissileTank,    false, BuildTab.Vehicles);
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
        var style = new StyleBoxFlat { BgColor = CReady, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderColor = CGoldBorder, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3 };
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

        // 悬停
        panel.MouseEntered += () => { _hoverItem = item; };
        panel.MouseExited += () => { if (_hoverItem == item) _hoverItem = null; };
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
        bool hasBase, bool hasPower, bool hasBarracks, bool hasWarFactory, bool hasTechCenter)
    {
        _money = money; _power = power; _playerTechLevel = techLevel;
        _unitCount = unitCount; _unitCap = unitCap;
        _hasBase = hasBase; _hasPower = hasPower; _hasBarracks = hasBarracks;
        _hasWarFactory = hasWarFactory; _hasTechCenter = hasTechCenter;

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
                if (!_hasBase) { it.IsLocked = true; it.LockReason = "需要建造厂"; }
                else if (_unitCount >= _unitCap) { it.IsLocked = true; it.LockReason = "单位已满"; }
            }
            else
            {
                EvaluateUnitLock(it);
            }

            // 资金不足也算锁定原因（但不置灰整块，仅成本变红）
            if (!it.CanAfford && string.IsNullOrEmpty(it.LockReason))
                it.LockReason = "资金不足";
        }

        RefreshVisuals();
    }

    private void EvaluateBuildingLock(BuildItem it)
    {
        switch (it.BType)
        {
            case BuildingType.PowerPlant:
                if (!_hasBase) { it.IsLocked = true; it.LockReason = "需要建造厂"; }
                break;
            case BuildingType.Barracks:
                if (!_hasPower) { it.IsLocked = true; it.LockReason = "需要电站"; }
                else if (_playerTechLevel < 1) { it.IsLocked = true; it.LockReason = "难度未解锁"; }
                break;
            case BuildingType.WarFactory:
                if (!_hasBarracks) { it.IsLocked = true; it.LockReason = "需要兵营"; }
                else if (_playerTechLevel < 2) { it.IsLocked = true; it.LockReason = "难度未解锁"; }
                break;
            case BuildingType.TechCenter:
                if (!_hasWarFactory) { it.IsLocked = true; it.LockReason = "需要车厂"; }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = "电力不足"; }
                else if (_playerTechLevel < 3) { it.IsLocked = true; it.LockReason = "难度未解锁"; }
                break;
            // 阶段12-A1+A2 新增建筑
            case BuildingType.Turret:
                if (!_hasBarracks) { it.IsLocked = true; it.LockReason = "需要兵营"; }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = "电力不足"; }
                break;
            case BuildingType.AntiAirTurret:
                if (!_hasWarFactory) { it.IsLocked = true; it.LockReason = "需要车厂"; }
                else if (_playerTechLevel < 2) { it.IsLocked = true; it.LockReason = "难度未解锁"; }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = "电力不足"; }
                break;
            case BuildingType.RepairPad:
                if (!_hasWarFactory) { it.IsLocked = true; it.LockReason = "需要车厂"; }
                else if (_playerTechLevel < 2) { it.IsLocked = true; it.LockReason = "难度未解锁"; }
                else if (_power < 0) { it.IsLocked = true; it.LockReason = "电力不足"; }
                break;
        }
    }

    private void EvaluateUnitLock(BuildItem it)
    {
        if (_power < 0) { it.IsLocked = true; it.LockReason = "电力不足"; return; }
        if (_unitCount >= _unitCap) { it.IsLocked = true; it.LockReason = "单位已满"; return; }

        switch (it.UType)
        {
            case UnitType.LightTank:
            case UnitType.Infantry:
            case UnitType.Grenadier:       // E6
            case UnitType.FlameInfantry:   // E6
            case UnitType.Sniper:          // E6
            case UnitType.Thief:          // E6b
                if (!_hasBarracks) { it.IsLocked = true; it.LockReason = "需要兵营"; }
                break;
            case UnitType.HeavyTank:
            case UnitType.Artillery:
            case UnitType.AntiAir:
            case UnitType.Engineer:
            case UnitType.Transport:       // E6
                if (!_hasWarFactory) { it.IsLocked = true; it.LockReason = "需要车厂"; }
                break;
            case UnitType.RocketLauncher:
            case UnitType.MissileTank:
            case UnitType.Hero:           // E6b
            case UnitType.Spy:            // E6b
                if (!_hasTechCenter) { it.IsLocked = true; it.LockReason = "需要科技中心"; }
                break;
        }
    }

    private void RefreshVisuals()
    {
        var powerWarn = _power < 0 ? " [color=#ff5555]不足![/color]" : "";
        _infoLabel.Text = $"[color=#ffd54f]{DifficultyName}[/color]  科技Lv{_playerTechLevel}\n" +
                          $"[color=#ffd24f][b]${_money}[/b][/color]   {_unitCount}/{_unitCap} 单位\n" +
                          $"[color={(_power < 0 ? "#ff5555" : "#88ccff")}]电力 {_power}{powerWarn}[/color]";

        foreach (var it in _items)
        {
            if (it.PanelNode == null) continue;
            Color bg;
            bool placementActive = (it.IsBuilding && ActivePlacement == it.BType);
            if (it.IsLocked || !it.CanAfford && it.LockReason == "资金不足" && false)
                bg = CLocked;
            else if (placementActive)
                bg = CSelected;
            else
                bg = CReady;

            // 资金不足但前置满足：底色偏暗红
            if (!it.IsLocked && !it.CanAfford)
                bg = new Color(0.3f, 0.15f, 0.12f, 0.95f);

            // 锁定
            if (it.IsLocked)
            {
                bg = CLocked;
                it.PanelNode.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            }
            else if (placementActive)
            {
                it.PanelNode.Modulate = Colors.White;
            }
            else
            {
                it.PanelNode.Modulate = Colors.White;
            }

            if (it.BgRect != null) it.BgRect.Color = bg;

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
        }

        // 悬停提示
        if (_hoverItem != null)
        {
            var h = _hoverItem;
            string status = h.IsLocked ? $"[color=#ff7777]{h.LockReason}[/color]"
                          : !h.CanAfford ? "[color=#ffaa55]资金不足[/color]"
                          : "[color=#77ff77]可建造[/color]";
            _hintLabel.Text = $"{h.Name}  ${h.Cost}\n{GetItemDesc(h)}\n{status}";
        }
        else
        {
            _hintLabel.Text = ActivePlacement != null
                ? $"[color=#66ff99]放置模式[/color]\n左键放置 {ActivePlacement}\n右键/Esc 取消"
                : "点击图标建造\n左键拖框选单位\n右键移动/攻击";
        }
    }

    private string GetItemDesc(BuildItem it)
    {
        if (it.IsBuilding)
        {
            return it.BType switch
            {
                BuildingType.PowerPlant => "提供+100电力",
                BuildingType.Barracks => "解锁步兵/轻坦生产",
                BuildingType.WarFactory => "解锁重坦/炮兵/防空车/工程车",
                BuildingType.TechCenter => "解锁火箭炮/导弹车",
                BuildingType.Turret => "自动对地防御塔，射程犴18，快射速",
                BuildingType.AntiAirTurret => "重型防御塔，高伤害犴25大范围",
                BuildingType.RepairPad => "每秒自动修复220范围内友方单位+25HP",
                _ => ""
            };
        }
        if (it.IsHarvester) return "自动采矿赚钱";
        return it.UType switch
        {
            UnitType.Infantry => "便宜、脆、人多势众",
            UnitType.LightTank => "快、脆、便宜",
            UnitType.HeavyTank => "慢、硬、主力",
            UnitType.Artillery => "远程高伤，不能近战",
            UnitType.AntiAir => "高射速，对地补位",
            UnitType.Engineer => "修复友军建筑/单位，多功能辅助",
            UnitType.Grenadier => "AOE溅射，克制密集步兵",
            UnitType.Sniper => "超远程精确射击，脆皮",
            UnitType.FlameInfantry => "近距高射速喷火，灼烧区域",
            UnitType.Transport => "搭载步兵变战车，IFV合体系统",
            UnitType.Hero => "强力步兵，随机技能(双发/治疗/冲锋/暴击/护盾)",
            UnitType.Spy => "渗透敌方建筑，停电+偷钱",
            UnitType.Thief => "潜入偷取敌方资金",
            UnitType.RocketLauncher => "溅射伤害，需科技",
            UnitType.MissileTank => "超远程爆发，需科技",
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
    }

    /// <summary>加载 PNG 纹理，失败时打印错误但不中断。</summary>
    private static Texture2D? LoadPng(string path)
    {
        var tex = GD.Load<Texture2D>(path);
        if (tex == null)
            GD.PrintErr($"[BuildPanel] Failed to load icon: {path}");
        return tex;
    }
}
