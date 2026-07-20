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
    private GridContainer _unitGrid = null!;
    private Button _tabBuildings = null!;
    private Button _tabUnits = null!;

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

    // 颜色
    private static readonly Color CBg = new(0.08f, 0.1f, 0.14f, 0.94f);
    private static readonly Color CHover = new(0.2f, 0.32f, 0.5f, 0.95f);
    private static readonly Color CLocked = new(0.05f, 0.06f, 0.08f, 0.9f);
    private static readonly Color CSelected = new(0.18f, 0.52f, 0.28f, 0.95f);
    private static readonly Color CReady = new(0.12f, 0.16f, 0.22f, 0.95f);

    private const float W = 232f;

    // 图标（直接使用游戏 PNG 素材）
    private static Texture2D? _iPower, _iBarracks, _iWar, _iTech;
    private static Texture2D? _iLight, _iHeavy, _iArt, _iRocket, _iMissile, _iHarv;
    private static Texture2D? _iInfantry;

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
        _infoLabel.AddThemeFontSizeOverride("normal_font_size", 15);
        _infoLabel.FitContent = true;
        _infoLabel.CustomMinimumSize = new Vector2(W - 16, 56);
        root.AddChild(_infoLabel);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 4);
        _tabBuildings = new Button { Text = "建筑", ToggleMode = true, ButtonPressed = true };
        _tabUnits = new Button { Text = "单位", ToggleMode = true };
        _tabBuildings.Pressed += () => ShowTab(true);
        _tabUnits.Pressed += () => ShowTab(false);
        tabs.AddChild(_tabBuildings);
        tabs.AddChild(_tabUnits);
        root.AddChild(tabs);

        _buildingGrid = new GridContainer { Columns = 2 };
        _buildingGrid.AddThemeConstantOverride("h_separation", 4);
        _buildingGrid.AddThemeConstantOverride("v_separation", 4);
        _buildingGrid.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(_buildingGrid);

        _unitGrid = new GridContainer { Columns = 2 };
        _unitGrid.AddThemeConstantOverride("h_separation", 4);
        _unitGrid.AddThemeConstantOverride("v_separation", 4);
        _unitGrid.SizeFlagsVertical = SizeFlags.ExpandFill;
        _unitGrid.Visible = false;
        root.AddChild(_unitGrid);

        _hintLabel = new RichTextLabel();
        _hintLabel.BbcodeEnabled = true;
        _hintLabel.AddThemeFontSizeOverride("normal_font_size", 12);
        _hintLabel.CustomMinimumSize = new Vector2(W - 16, 70);
        _hintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_hintLabel);

        CreateItems();
    }

    private void ShowTab(bool buildings)
    {
        _tabBuildings.ButtonPressed = buildings;
        _tabUnits.ButtonPressed = !buildings;
        _buildingGrid.Visible = buildings;
        _unitGrid.Visible = !buildings;
    }

    private void CreateItems()
    {
        // 建筑
        AddItem("电站", 300, _iPower, true, BuildingType.PowerPlant, UnitType.Default, false);
        AddItem("兵营", 400, _iBarracks, true, BuildingType.Barracks, UnitType.Default, false);
        AddItem("车厂", 600, _iWar, true, BuildingType.WarFactory, UnitType.Default, false);
        AddItem("科技", 800, _iTech, true, BuildingType.TechCenter, UnitType.Default, false);
        // 单位
        AddItem("步兵", 100, _iInfantry, false, BuildingType.Base, UnitType.Infantry, false);
        AddItem("轻坦", 200, _iLight, false, BuildingType.Base, UnitType.LightTank, false);
        AddItem("重坦", 500, _iHeavy, false, BuildingType.Base, UnitType.HeavyTank, false);
        AddItem("炮兵", 400, _iArt, false, BuildingType.Base, UnitType.Artillery, false);
        AddItem("火箭炮", 600, _iRocket, false, BuildingType.Base, UnitType.RocketLauncher, false);
        AddItem("导弹车", 800, _iMissile, false, BuildingType.Base, UnitType.MissileTank, false);
        AddItem("矿车", 500, _iHarv, false, BuildingType.Base, UnitType.Default, true);
    }

    private void AddItem(string name, int cost, Texture2D? icon, bool isBuilding, BuildingType bt, UnitType ut, bool harv)
    {
        var item = new BuildItem
        {
            Name = name, Cost = cost, Icon = icon,
            IsBuilding = isBuilding, BType = bt, UType = ut, IsHarvester = harv
        };

        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(102, 88);
        var style = new StyleBoxFlat { BgColor = CReady, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderColor = new Color(0.2f, 0.25f, 0.3f, 0.6f), CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4 };
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
        else _unitGrid.AddChild(panel);

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
                if (!_hasBarracks) { it.IsLocked = true; it.LockReason = "需要兵营"; }
                break;
            case UnitType.HeavyTank:
            case UnitType.Artillery:
                if (!_hasWarFactory) { it.IsLocked = true; it.LockReason = "需要车厂"; }
                break;
            case UnitType.RocketLauncher:
            case UnitType.MissileTank:
                if (!_hasTechCenter) { it.IsLocked = true; it.LockReason = "需要科技中心"; }
                break;
        }
    }

    private void RefreshVisuals()
    {
        var powerWarn = _power < 0 ? " [电力不足!]" : "";
        _infoLabel.Text = $"[color=#ffd54f]{DifficultyName}[/color]  科技Lv{_playerTechLevel}\n" +
                          $"[color=#66ff99]资金 ${_money}[/color]  上限 {_unitCount}/{_unitCap}\n" +
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
                BuildingType.WarFactory => "解锁重坦/炮兵",
                BuildingType.TechCenter => "解锁火箭炮/导弹车",
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

        // 灰底单位PNG，AddItem 时会染色为玩家阵营色
        _iInfantry = LoadPng("res://assets/sprites/units/infantry.png");
        _iLight  = LoadPng("res://assets/sprites/units/hull_light.png");
        _iHeavy  = LoadPng("res://assets/sprites/units/hull_heavy.png");
        _iArt    = LoadPng("res://assets/sprites/units/hull_arty.png");
        _iRocket = LoadPng("res://assets/sprites/units/hull_rocket.png");
        _iMissile= LoadPng("res://assets/sprites/units/hull_missile.png");
        _iHarv   = LoadPng("res://assets/sprites/units/harvester.png");
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
