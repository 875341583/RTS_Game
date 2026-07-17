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

    // 图标
    private static ImageTexture? _iPower, _iBarracks, _iWar, _iTech;
    private static ImageTexture? _iLight, _iHeavy, _iArt, _iRocket, _iMissile, _iHarv;

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
        iconRect.CustomMinimumSize = new Vector2(48, 40);
        iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        iconRect.StretchMode = TextureRect.StretchModeEnum.Scale;
        iconRect.SizeFlagsHorizontal = SizeFlags.Fill;
        iconRect.MouseFilter = MouseFilterEnum.Pass;
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
                BuildingType.Barracks => "解锁轻坦生产",
                BuildingType.WarFactory => "解锁重坦/炮兵",
                BuildingType.TechCenter => "解锁火箭炮/导弹车",
                _ => ""
            };
        }
        if (it.IsHarvester) return "自动采矿赚钱";
        return it.UType switch
        {
            UnitType.LightTank => "快、脆、便宜",
            UnitType.HeavyTank => "慢、硬、主力",
            UnitType.Artillery => "远程高伤，不能近战",
            UnitType.RocketLauncher => "溅射伤害，需科技",
            UnitType.MissileTank => "超远程爆发，需科技",
            _ => ""
        };
    }

    // ---------- 图标生成 ----------
    private static void EnsureIcons()
    {
        if (_iPower != null) return;

        _iPower = MakeIcon(48, 42, (img) =>
        {
            FillRect(img, 8, 6, 32, 30, new Color(0.3f, 0.3f, 0.35f));
            FillRect(img, 12, 10, 24, 22, new Color(0.45f, 0.45f, 0.5f));
            // 闪电符号
            DrawLine(img, 22, 12, 17, 22, new Color(1f, 0.85f, 0.2f));
            DrawLine(img, 17, 22, 24, 22, new Color(1f, 0.85f, 0.2f));
            DrawLine(img, 24, 22, 19, 32, new Color(1f, 0.85f, 0.2f));
        });

        _iBarracks = MakeIcon(48, 42, (img) =>
        {
            FillRect(img, 10, 14, 28, 24, new Color(0.3f, 0.45f, 0.3f));
            // 盾牌
            for (int x = 0; x < 48; x++)
                for (int y = 0; y < 42; y++)
                {
                    float dx = x - 24, dy = y - 20;
                    if (dx * dx + (dy - 2) * (dy - 2) * 1.3f < 64 && dy < 8)
                        img.SetPixel(x, y, new Color(0.5f, 0.8f, 0.5f));
                }
        });

        _iWar = MakeIcon(48, 42, (img) =>
        {
            FillRect(img, 8, 8, 32, 30, new Color(0.4f, 0.3f, 0.2f));
            // 齿轮
            for (int x = 0; x < 48; x++)
                for (int y = 0; y < 42; y++)
                {
                    float dx = x - 24, dy = y - 21;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < 12 && d > 7)
                        img.SetPixel(x, y, new Color(0.8f, 0.6f, 0.3f));
                    else if (d <= 5)
                        img.SetPixel(x, y, new Color(0.6f, 0.45f, 0.25f));
                }
        });

        _iTech = MakeIcon(48, 42, (img) =>
        {
            // 六边形
            for (int x = 0; x < 48; x++)
                for (int y = 0; y < 42; y++)
                {
                    float dx = Mathf.Abs(x - 24), dy = Mathf.Abs(y - 21);
                    if (dx < 14 && dy < 12 && dx * 0.5f + dy < 14)
                        img.SetPixel(x, y, new Color(0.4f, 0.2f, 0.55f));
                }
            // 原子轨道
            for (int x = 0; x < 48; x++)
                for (int y = 0; y < 42; y++)
                {
                    float dx = x - 24, dy = (y - 21) * 1.4f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > 8 && d < 10)
                        img.SetPixel(x, y, new Color(0.7f, 0.5f, 0.9f));
                }
        });

        _iLight = MakeIcon(48, 42, (img) =>
        {
            // 小三角坦克(蓝)
            FillRect(img, 16, 24, 16, 8, new Color(0.2f, 0.2f, 0.25f)); // 履带
            FillRect(img, 18, 16, 12, 10, new Color(0.25f, 0.45f, 0.7f)); // 车身
            FillRect(img, 23, 10, 4, 8, new Color(0.3f, 0.5f, 0.8f)); // 炮塔
            DrawLine(img, 25, 8, 25, 2, new Color(0.35f, 0.55f, 0.85f)); // 炮管
        });

        _iHeavy = MakeIcon(48, 42, (img) =>
        {
            // 大方块坦克(深蓝)
            FillRect(img, 12, 24, 24, 9, new Color(0.15f, 0.15f, 0.2f));
            FillRect(img, 14, 14, 20, 12, new Color(0.18f, 0.28f, 0.5f));
            FillRect(img, 18, 8, 12, 8, new Color(0.22f, 0.34f, 0.58f));
            DrawLine(img, 24, 8, 24, 0, new Color(0.28f, 0.4f, 0.62f)); // 粗炮管
            DrawLine(img, 23, 8, 23, 0, new Color(0.28f, 0.4f, 0.62f));
        });

        _iArt = MakeIcon(48, 42, (img) =>
        {
            // 长管炮兵(青)
            FillRect(img, 14, 26, 20, 8, new Color(0.15f, 0.15f, 0.18f));
            FillRect(img, 16, 18, 16, 10, new Color(0.2f, 0.45f, 0.45f));
            DrawLine(img, 24, 16, 38, 4, new Color(0.3f, 0.6f, 0.6f)); // 长炮管
            DrawLine(img, 25, 17, 39, 5, new Color(0.3f, 0.6f, 0.6f));
        });

        _iRocket = MakeIcon(48, 42, (img) =>
        {
            // 多管火箭炮(绿)
            FillRect(img, 12, 26, 24, 8, new Color(0.15f, 0.18f, 0.12f));
            FillRect(img, 14, 18, 20, 10, new Color(0.25f, 0.4f, 0.2f));
            for (int i = 0; i < 4; i++)
                DrawLine(img, 16 + i * 5, 16, 16 + i * 5, 4, new Color(0.4f, 0.7f, 0.3f));
        });

        _iMissile = MakeIcon(48, 42, (img) =>
        {
            // 导弹车(紫)
            FillRect(img, 12, 26, 24, 8, new Color(0.15f, 0.12f, 0.18f));
            FillRect(img, 14, 16, 20, 12, new Color(0.35f, 0.2f, 0.45f));
            // 尖头导弹
            DrawLine(img, 24, 16, 24, 2, new Color(0.6f, 0.4f, 0.8f));
            DrawLine(img, 23, 16, 21, 4, new Color(0.6f, 0.4f, 0.8f));
            DrawLine(img, 25, 16, 27, 4, new Color(0.6f, 0.4f, 0.8f));
        });

        _iHarv = MakeIcon(48, 42, (img) =>
        {
            // 矿车(黄)
            FillRect(img, 12, 24, 24, 10, new Color(0.2f, 0.18f, 0.1f));
            FillRect(img, 14, 14, 20, 12, new Color(0.55f, 0.45f, 0.15f));
            // 矿铲
            DrawLine(img, 12, 24, 8, 30, new Color(0.7f, 0.6f, 0.2f));
            DrawLine(img, 11, 24, 7, 30, new Color(0.7f, 0.6f, 0.2f));
            DrawLine(img, 12, 30, 6, 34, new Color(0.7f, 0.6f, 0.2f));
        });
    }

    private delegate void DrawAction(Image img);
    private static ImageTexture MakeIcon(int w, int h, DrawAction draw)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        draw(img);
        return ImageTexture.CreateFromImage(img);
    }

    private static void FillRect(Image img, int x, int y, int w, int h, Color c)
    {
        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
            {
                int px = x + i, py = y + j;
                if (px >= 0 && px < img.GetWidth() && py >= 0 && py < img.GetHeight())
                    img.SetPixel(px, py, c);
            }
    }

    private static void DrawLine(Image img, int x0, int y0, int x1, int y1, Color c)
    {
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int x = x0, y = y0;
        while (true)
        {
            if (x >= 0 && x < img.GetWidth() && y >= 0 && y < img.GetHeight())
                img.SetPixel(x, y, c);
            if (x == x1 && y == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
    }
}
