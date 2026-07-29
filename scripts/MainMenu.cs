using Godot;
using System.Collections.Generic;

namespace RTSGame;

/// <summary>
/// 主菜单 — 红警2 (RA2) 风格 UI 全面重构。
/// 设计参考 RA2 原版：
/// - 深黑背景 + 钢铁银色面板边框 + 红色按钮（竖排）
/// - 遭遇战：左侧地图/设置 + 右侧玩家表 + 下方预览
/// - 设置页：金属质感面板 + 标签式分类
/// 全程序化构建 UI。
/// </summary>
public partial class MainMenu : Control
{
    // 页面容器 — 全屏布局
    private Control _pageRoot = null!;
    private LineEdit _seedInput = null!;

    // 设置项状态
    private QualitySettings.QualityLevel _settingsQuality = QualitySettings.QualityLevel.High;
    private float _settingsVolume = 1.0f;
    private bool _settingsFullscreen = true;

    // RA2 配色方案
    private static readonly Color ColBg = new(0.02f, 0.02f, 0.03f, 1f);           // 近黑背景
    private static readonly Color ColPanelBg = new(0.08f, 0.08f, 0.10f, 0.95f);   // 面板暗灰
    private static readonly Color ColPanelBorder = new(0.35f, 0.36f, 0.38f, 1f);  // 钢铁银边框
    private static readonly Color ColPanelBorderHi = new(0.55f, 0.56f, 0.58f, 1f);// 高亮银边框
    private static readonly Color ColRed = new(0.75f, 0.10f, 0.10f, 1f);          // RA2 红
    private static readonly Color ColRedBright = new(0.90f, 0.20f, 0.15f, 1f);    // 亮红
    private static readonly Color ColGold = new(0.85f, 0.70f, 0.30f, 1f);         // 金色高亮
    private static readonly Color ColTextMain = new(0.88f, 0.88f, 0.90f, 1f);     // 主文字白
    private static readonly Color ColTextDim = new(0.50f, 0.50f, 0.55f, 1f);      // 暗灰文字
    private static readonly Color ColTextAccent = new(0.40f, 0.70f, 1.0f, 1f);    // 蓝色强调

    // 选中态追踪
    private MapConfig.SizePreset _selMapSize = MapConfig.SizePreset.Small;
    private MapConfig.MapTheme _selMapTheme = MapConfig.MapTheme.Default;
    private string _selFactionId = "Allies";
    private Main.Difficulty _selDifficulty = Main.Difficulty.Normal;
    // RA2 规则复选框
    private bool _ruleSuperweapons = true;
    private bool _ruleShortGame = false;
    private int _startCredits = 5000;

    public override void _Ready()
    {
        // 有 --difficulty 参数时直接进入游戏（headless 自动化测试）
        {
            var args = OS.GetCmdlineArgs();
            bool hasDifficulty = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--difficulty", System.StringComparison.OrdinalIgnoreCase))
                {
                    string val = a.Contains('=') ? a.Split('=')[1] : "";
                    GameSession.SelectedDifficulty = val.ToLowerInvariant() switch
                    {
                        "easy" or "0" => Main.Difficulty.Easy,
                        "normal" or "1" => Main.Difficulty.Normal,
                        "hard" or "2" => Main.Difficulty.Hard,
                        "brutal" or "3" => Main.Difficulty.Brutal,
                        _ => Main.Difficulty.Normal
                    };
                    hasDifficulty = true;
                }
                if (a.StartsWith("--seed", System.StringComparison.OrdinalIgnoreCase))
                {
                    string val = a.Contains('=') ? a.Split('=')[1] : "";
                    if (ulong.TryParse(val, out var s))
                        GameSession.MapSeed = s;
                }
            }
            if (hasDifficulty)
            {
                GameLog.Info($"[MainMenu] 自动进入游戏 (难度 {GameSession.SelectedDifficulty}, 种子 {GameSession.MapSeed}, mode={DisplayServer.GetName()})");
                CallDeferred(nameof(ChangeToGameScene));
                return;
            }
        }

        // 初始化国际化翻译系统（菜单也需要翻译）
        TrManager.SetLanguage("zh-CN");

        // 加载阵营数据（用于遭遇战页面）
        FactionManager.Load();

        // 初始化设置状态
        _settingsQuality = QualitySettings.Current;
        _settingsFullscreen = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;

        // 同步会话状态到本地
        _selMapSize = GameSession.SelectedMapSize;
        _selMapTheme = GameSession.SelectedMapTheme;
        _selFactionId = GameSession.PlayerFactionId;
        _selDifficulty = GameSession.SelectedDifficulty;

        BuildBackground();
        _pageRoot = new Control();
        _pageRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_pageRoot);

        ShowMainMenu();

        // 菜单 BGM
        var bgmPlayer = new AudioStreamPlayer { Name = "BgmPlayer", Bus = "Master" };
        AddChild(bgmPlayer);
        BgmManager.Initialize(bgmPlayer);
        BgmManager.SwitchScene(BgmManager.BgmScene.Menu);
        GameLog.Info("[MainMenu] 主菜单已加载 (RA2风格)");
    }

    // ==================== 背景与容器 ====================

    private void BuildBackground()
    {
        // RA2 风格：极深黑色背景
        var bg = new ColorRect();
        bg.Color = ColBg;
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        // 微弱暗红色径向渐变效果（模拟 RA2 标题屏的氛围光）
        var vignette = new ColorRect();
        vignette.Color = new Color(0.15f, 0.02f, 0.02f, 0.3f);
        vignette.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(vignette);

        // 细网格线（军事地图风格）
        var grid = new Line2D();
        grid.Width = 1f;
        grid.DefaultColor = new Color(0.10f, 0.10f, 0.12f, 0.5f);
        var pts = new List<Vector2>();
        for (int x = 0; x <= 1920; x += 64) { pts.Add(new Vector2(x, 0)); pts.Add(new Vector2(x, 1080)); }
        for (int y = 0; y <= 1080; y += 64) { pts.Add(new Vector2(0, y)); pts.Add(new Vector2(1920, y)); }
        grid.Points = pts.ToArray();
        AddChild(grid);

        // 扫描线效果（模拟 CRT 雷达屏）
        for (int y = 0; y < 1080; y += 4)
        {
            var line = new ColorRect();
            line.Color = new Color(0, 0, 0, 0.03f);
            line.Position = new Vector2(0, y);
            line.Size = new Vector2(1920, 2);
            AddChild(line);
        }
    }

    /// <summary>清除当前页面内容。</summary>
    private void ClearPage()
    {
        foreach (var child in _pageRoot.GetChildren())
            child.QueueFree();
    }

    // ==================== RA2 风格面板与样式 ====================

    /// <summary>创建 RA2 风格的金属面板（带银色边框）。</summary>
    private Panel MakeMetalPanel(float w, float h, bool highlight = false)
    {
        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(w, h);
        var style = new StyleBoxFlat();
        style.BgColor = ColPanelBg;
        style.BorderColor = highlight ? ColPanelBorderHi : ColPanelBorder;
        style.BorderWidthLeft = 2;
        style.BorderWidthRight = 2;
        style.BorderWidthTop = 2;
        style.BorderWidthBottom = 2;
        style.ContentMarginLeft = 12;
        style.ContentMarginRight = 12;
        style.ContentMarginTop = 8;
        style.ContentMarginBottom = 8;
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    /// <summary>创建 RA2 红色大按钮（主菜单导航用）。</summary>
    private Button MakeRA2Button(string text, int fontSize = 20)
    {
        var btn = new Button();
        btn.Text = text;
        btn.AddThemeFontSizeOverride("font_size", fontSize);
        btn.CustomMinimumSize = new Vector2(320, 48);

        var normal = new StyleBoxFlat();
        normal.BgColor = ColRed;
        normal.BorderColor = ColPanelBorder;
        normal.BorderWidthLeft = 2;
        normal.BorderWidthRight = 2;
        normal.BorderWidthTop = 2;
        normal.BorderWidthBottom = 2;
        normal.ContentMarginLeft = 16;
        normal.ContentMarginRight = 16;
        normal.ContentMarginTop = 6;
        normal.ContentMarginBottom = 6;
        btn.AddThemeStyleboxOverride("normal", normal);

        var hover = new StyleBoxFlat();
        hover.BgColor = ColRedBright;
        hover.BorderColor = ColPanelBorderHi;
        hover.BorderWidthLeft = 2;
        hover.BorderWidthRight = 2;
        hover.BorderWidthTop = 2;
        hover.BorderWidthBottom = 2;
        hover.ContentMarginLeft = 16;
        hover.ContentMarginRight = 16;
        hover.ContentMarginTop = 6;
        hover.ContentMarginBottom = 6;
        btn.AddThemeStyleboxOverride("hover", hover);

        var pressed = new StyleBoxFlat();
        pressed.BgColor = new Color(0.5f, 0.05f, 0.05f, 1f);
        pressed.BorderColor = ColGold;
        pressed.BorderWidthLeft = 2;
        pressed.BorderWidthRight = 2;
        pressed.BorderWidthTop = 2;
        pressed.BorderWidthBottom = 2;
        pressed.ContentMarginLeft = 16;
        pressed.ContentMarginRight = 16;
        pressed.ContentMarginTop = 6;
        pressed.ContentMarginBottom = 6;
        btn.AddThemeStyleboxOverride("pressed", pressed);

        btn.AddThemeColorOverride("font_color", Colors.White);
        btn.AddThemeColorOverride("font_hover_color", ColGold);
        btn.AddThemeColorOverride("font_pressed_color", ColGold);

        return btn;
    }

    /// <summary>RA2 风格灰色小按钮（选项/选择用）。</summary>
    private Button MakeGrayButton(string text, int fontSize = 14, bool selected = false)
    {
        var btn = new Button();
        btn.Text = text;
        btn.AddThemeFontSizeOverride("font_size", fontSize);
        btn.CustomMinimumSize = new Vector2(0, 32);

        var normal = new StyleBoxFlat();
        normal.BgColor = selected ? new Color(0.18f, 0.18f, 0.22f, 0.95f) : new Color(0.10f, 0.10f, 0.12f, 0.9f);
        normal.BorderColor = selected ? ColGold : ColPanelBorder;
        normal.BorderWidthLeft = 1;
        normal.BorderWidthRight = 1;
        normal.BorderWidthTop = 1;
        normal.BorderWidthBottom = 1;
        normal.ContentMarginLeft = 10;
        normal.ContentMarginRight = 10;
        normal.ContentMarginTop = 4;
        normal.ContentMarginBottom = 4;
        btn.AddThemeStyleboxOverride("normal", normal);

        var hover = new StyleBoxFlat();
        hover.BgColor = new Color(0.20f, 0.20f, 0.24f, 0.95f);
        hover.BorderColor = ColPanelBorderHi;
        hover.BorderWidthLeft = 1;
        hover.BorderWidthRight = 1;
        hover.BorderWidthTop = 1;
        hover.BorderWidthBottom = 1;
        hover.ContentMarginLeft = 10;
        hover.ContentMarginRight = 10;
        hover.ContentMarginTop = 4;
        hover.ContentMarginBottom = 4;
        btn.AddThemeStyleboxOverride("hover", hover);

        btn.AddThemeColorOverride("font_color", selected ? ColGold : ColTextMain);
        btn.AddThemeColorOverride("font_hover_color", ColGold);

        return btn;
    }

    /// <summary>RA2 风格复选框行。</summary>
    private HBoxContainer MakeCheckboxRow(string label, bool checkState, System.Action<bool> onToggle)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var btn = new CheckBox();
        btn.Text = label;
        btn.ButtonPressed = checkState;
        btn.AddThemeFontSizeOverride("font_size", 13);
        btn.AddThemeColorOverride("font_color", ColTextMain);
        btn.AddThemeColorOverride("font_hover_color", ColGold);
        btn.CustomMinimumSize = new Vector2(0, 28);
        btn.Toggled += (on) => onToggle(on);
        row.AddChild(btn);

        return row;
    }

    // ==================== 主菜单页 ====================

    private void ShowMainMenu()
    {
        ClearPage();

        // 整体布局：左 60% 展示区 + 右 40% 按钮区
        var layout = new HBoxContainer();
        layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layout.AddThemeConstantOverride("separation", 0);
        _pageRoot.AddChild(layout);

        // ===== 左侧展示区 =====
        var leftPanel = new Panel();
        leftPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftPanel.CustomMinimumSize = new Vector2(0, 0);
        var leftStyle = new StyleBoxFlat();
        leftStyle.BgColor = new Color(0.04f, 0.04f, 0.05f, 0.8f);
        leftStyle.BorderWidthRight = 3;
        leftStyle.BorderColor = ColPanelBorder;
        leftPanel.AddThemeStyleboxOverride("panel", leftStyle);
        layout.AddChild(leftPanel);

        // 左侧标题区
        var leftVb = new VBoxContainer();
        leftVb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        leftVb.OffsetLeft = 40; leftVb.OffsetTop = 60; leftVb.OffsetRight = -20; leftVb.OffsetBottom = -40;
        leftVb.AddThemeConstantOverride("separation", 4);
        leftPanel.AddChild(leftVb);

        // 大标题
        var title = new Label();
        title.Text = TrManager.Tr("menu.title");
        title.AddThemeFontSizeOverride("font_size", 56);
        title.AddThemeColorOverride("font_color", ColRedBright);
        leftVb.AddChild(title);

        var subtitle = new Label();
        subtitle.Text = "IRON CURTAIN RTS";
        subtitle.AddThemeFontSizeOverride("font_size", 18);
        subtitle.AddThemeColorOverride("font_color", ColTextDim);
        leftVb.AddChild(subtitle);

        leftVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        // 版本标签
        var version = new Label();
        version.Text = TrManager.Tr("menu.version");
        version.AddThemeFontSizeOverride("font_size", 14);
        version.AddThemeColorOverride("font_color", ColGold);
        leftVb.AddChild(version);

        var tagline = new Label();
        tagline.Text = "1:1 复刻红警2核心体验 · 15分钟一局";
        tagline.AddThemeFontSizeOverride("font_size", 14);
        tagline.AddThemeColorOverride("font_color", ColTextDim);
        leftVb.AddChild(tagline);

        // 弹性的填充
        leftVb.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        // 底部版权信息
        var copyright = new Label();
        copyright.Text = "© 2026 RTS_Game · Powered by Godot 4.7";
        copyright.AddThemeFontSizeOverride("font_size", 11);
        copyright.AddThemeColorOverride("font_color", ColTextDim);
        leftVb.AddChild(copyright);

        // ===== 右侧按钮区 =====
        var rightPanel = new Panel();
        rightPanel.CustomMinimumSize = new Vector2(420, 0);
        var rightStyle = new StyleBoxFlat();
        rightStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.9f);
        rightPanel.AddThemeStyleboxOverride("panel", rightStyle);
        layout.AddChild(rightPanel);

        var rightVb = new VBoxContainer();
        rightVb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        rightVb.OffsetLeft = 30; rightVb.OffsetTop = 60; rightVb.OffsetRight = -30; rightVb.OffsetBottom = -40;
        rightVb.AddThemeConstantOverride("separation", 8);
        rightPanel.AddChild(rightVb);

        // 遭遇战
        var skBtn = MakeRA2Button(TrManager.Tr("menu.skirmish"), 22);
        skBtn.Pressed += () => ShowSkirmishPage();
        rightVb.AddChild(skBtn);

        // 描述
        rightVb.AddChild(MakeDescLabel(TrManager.Tr("menu.skirmish_desc")));

        rightVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        // 设置
        var setBtn = MakeRA2Button(TrManager.Tr("ui.settings"), 22);
        setBtn.Pressed += () => ShowSettingsPage();
        rightVb.AddChild(setBtn);

        rightVb.AddChild(MakeDescLabel(TrManager.Tr("menu.settings_desc")));

        rightVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        // 地图编辑器
        var meBtn = MakeRA2Button(TrManager.Tr("menu.map_editor"), 22);
        meBtn.Pressed += () =>
        {
            GameLog.Info("[MainMenu] 进入地图编辑器");
            GetTree().ChangeSceneToFile("res://scenes/MapEditor.tscn");
        };
        rightVb.AddChild(meBtn);

        rightVb.AddChild(MakeDescLabel(TrManager.Tr("menu.map_editor_desc")));

        rightVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        // 3D原型预览
        var p3dBtn = MakeRA2Button(TrManager.Tr("menu.prototype_3d"), 22);
        p3dBtn.Pressed += () =>
        {
            GameLog.Info("[MainMenu] 进入3D原型预览");
            GetTree().ChangeSceneToFile("res://scenes/Prototype3D.tscn");
        };
        rightVb.AddChild(p3dBtn);

        rightVb.AddChild(MakeDescLabel(TrManager.Tr("menu.prototype_3d_desc")));

        // 弹性填充
        rightVb.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        // 退出
        var exitBtn = MakeRA2Button(TrManager.Tr("menu.quit_game"), 18);
        exitBtn.Pressed += () => GetTree().Quit();
        rightVb.AddChild(exitBtn);
    }

    // ==================== 遭遇战页面 ====================

    private void ShowSkirmishPage()
    {
        ClearPage();

        // 顶部标题栏
        var titleBar = MakeMetalPanel(0, 44, true);
        titleBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var titleVb = new VBoxContainer();
        titleVb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        titleVb.OffsetLeft = 16; titleVb.OffsetTop = 4; titleVb.OffsetRight = -16; titleVb.OffsetBottom = -4;
        titleBar.AddChild(titleVb);
        var title = MakeLabel(TrManager.Tr("menu.skirmish"), 24, ColGold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        titleVb.AddChild(title);
        _pageRoot.AddChild(titleBar);

        // 主体两列布局
        var body = new HBoxContainer();
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 8);
        _pageRoot.AddChild(body);

        // ===== 左列：地图设置 + 难度 =====
        var leftCol = new VBoxContainer();
        leftCol.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        leftCol.AddThemeConstantOverride("separation", 6);
        body.AddChild(leftCol);

        // 地图设置面板
        var mapPanel = MakeMetalPanel(0, 0);
        mapPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var mapVb = new VBoxContainer();
        mapVb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        mapVb.OffsetLeft = 12; mapVb.OffsetTop = 8; mapVb.OffsetRight = -12; mapVb.OffsetBottom = -8;
        mapVb.AddThemeConstantOverride("separation", 6);
        mapPanel.AddChild(mapVb);
        leftCol.AddChild(mapPanel);

        mapVb.AddChild(MakeLabel(TrManager.Tr("menu.section_map"), 14, ColGold));

        // 地图尺寸
        var sizeRow = new HBoxContainer();
        sizeRow.AddThemeConstantOverride("separation", 6);
        sizeRow.AddChild(MakeLabel(TrManager.Tr("menu.map_size"), 12, ColTextDim));
        var sizeOptions = new (string Label, MapConfig.SizePreset Preset)[]
        {
            (TrManager.Tr("menu.map_small"), MapConfig.SizePreset.Small),
            (TrManager.Tr("menu.map_medium"), MapConfig.SizePreset.Medium),
            (TrManager.Tr("menu.map_large"), MapConfig.SizePreset.Large),
        };
        foreach (var opt in sizeOptions)
        {
            var btn = MakeGrayButton(opt.Label, 12, _selMapSize == opt.Preset);
            btn.Pressed += () =>
            {
                _selMapSize = opt.Preset;
                GameSession.SelectedMapSize = opt.Preset;
                ShowSkirmishPage();
            };
            sizeRow.AddChild(btn);
        }
        mapVb.AddChild(sizeRow);

        // 地图主题
        var themeRow = new HBoxContainer();
        themeRow.AddThemeConstantOverride("separation", 4);
        themeRow.AddChild(MakeLabel(TrManager.Tr("menu.map_theme"), 12, ColTextDim));
        var themeOptions = new (string Label, MapConfig.MapTheme Theme)[]
        {
            (TrManager.Tr("theme.default"), MapConfig.MapTheme.Default),
            (TrManager.Tr("theme.snow"), MapConfig.MapTheme.Snow),
            (TrManager.Tr("theme.desert"), MapConfig.MapTheme.Desert),
            (TrManager.Tr("theme.city"), MapConfig.MapTheme.City),
            (TrManager.Tr("theme.island"), MapConfig.MapTheme.Island),
        };
        foreach (var opt in themeOptions)
        {
            var btn = MakeGrayButton(opt.Label, 11, _selMapTheme == opt.Theme);
            btn.Pressed += () =>
            {
                _selMapTheme = opt.Theme;
                GameSession.SelectedMapTheme = opt.Theme;
                ShowSkirmishPage();
            };
            themeRow.AddChild(btn);
        }
        mapVb.AddChild(themeRow);

        // 种子
        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 6);
        seedRow.AddChild(MakeLabel(TrManager.Tr("menu.map_seed"), 12, ColTextDim));
        _seedInput = new LineEdit();
        _seedInput.CustomMinimumSize = new Vector2(180, 28);
        _seedInput.PlaceholderText = TrManager.Tr("menu.seed_placeholder");
        _seedInput.AddThemeFontSizeOverride("font_size", 13);
        var inputStyle = new StyleBoxFlat();
        inputStyle.BgColor = new Color(0.04f, 0.04f, 0.06f, 1f);
        inputStyle.BorderColor = ColPanelBorder;
        inputStyle.BorderWidthLeft = 1;
        inputStyle.BorderWidthRight = 1;
        inputStyle.BorderWidthTop = 1;
        inputStyle.BorderWidthBottom = 1;
        inputStyle.ContentMarginLeft = 6;
        inputStyle.ContentMarginRight = 6;
        _seedInput.AddThemeStyleboxOverride("normal", inputStyle);
        seedRow.AddChild(_seedInput);
        mapVb.AddChild(seedRow);

        mapVb.AddChild(MakeLabel(TrManager.Tr("menu.seed_hint"), 10, ColTextDim));

        // 难度面板
        leftCol.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

        var diffPanel = MakeMetalPanel(0, 0);
        diffPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var diffVb = new VBoxContainer();
        diffVb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        diffVb.OffsetLeft = 12; diffVb.OffsetTop = 8; diffVb.OffsetRight = -12; diffVb.OffsetBottom = -8;
        diffVb.AddThemeConstantOverride("separation", 4);
        diffPanel.AddChild(diffVb);
        leftCol.AddChild(diffPanel);

        diffVb.AddChild(MakeLabel(TrManager.Tr("menu.section_difficulty"), 14, ColGold));

        var diffCards = new (string Title, string Desc, Main.Difficulty Diff, Color Color)[]
        {
            (TrManager.Tr("diff.easy_title"), TrManager.Tr("diff.easy_desc"), Main.Difficulty.Easy, new Color(0.3f, 0.8f, 0.4f)),
            (TrManager.Tr("diff.normal_title"), TrManager.Tr("diff.normal_desc"), Main.Difficulty.Normal, new Color(0.4f, 0.7f, 1f)),
            (TrManager.Tr("diff.hard_title"), TrManager.Tr("diff.hard_desc"), Main.Difficulty.Hard, new Color(1f, 0.7f, 0.2f)),
            (TrManager.Tr("diff.brutal_title"), TrManager.Tr("diff.brutal_desc"), Main.Difficulty.Brutal, new Color(1f, 0.3f, 0.3f)),
        };
        foreach (var dc in diffCards)
        {
            var isSel = _selDifficulty == dc.Diff;
            var btn = MakeGrayButton("", 14, isSel);
            btn.Text = $"{dc.Title} — {dc.Desc}";
            btn.CustomMinimumSize = new Vector2(0, 36);
            if (isSel)
            {
                btn.AddThemeColorOverride("font_color", dc.Color);
            }
            btn.Pressed += () =>
            {
                _selDifficulty = dc.Diff;
                GameSession.SelectedDifficulty = dc.Diff;
                ShowSkirmishPage();
            };
            diffVb.AddChild(btn);
        }

        // ===== 右列：阵营选择 + 游戏规则 =====
        var rightCol = new VBoxContainer();
        rightCol.CustomMinimumSize = new Vector2(460, 0);
        rightCol.AddThemeConstantOverride("separation", 6);
        body.AddChild(rightCol);

        // 阵营面板
        var facPanel = MakeMetalPanel(0, 0);
        facPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var facVb = new VBoxContainer();
        facVb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        facVb.OffsetLeft = 12; facVb.OffsetTop = 8; facVb.OffsetRight = -12; facVb.OffsetBottom = -8;
        facVb.AddThemeConstantOverride("separation", 4);
        facPanel.AddChild(facVb);
        rightCol.AddChild(facPanel);

        facVb.AddChild(MakeLabel(TrManager.Tr("menu.section_faction"), 14, ColGold));

        var factions = FactionManager.GetAllFactions();
        var facGrid = new GridContainer();
        facGrid.Columns = 3;
        facGrid.AddThemeConstantOverride("h_separation", 6);
        facGrid.AddThemeConstantOverride("v_separation", 6);
        foreach (var fac in factions)
        {
            var isSel = _selFactionId == fac.Id;
            var btn = MakeGrayButton(fac.Name, 15, isSel);
            btn.CustomMinimumSize = new Vector2(130, 40);
            if (isSel)
            {
                btn.AddThemeColorOverride("font_color", fac.Color);
            }
            btn.Pressed += () =>
            {
                _selFactionId = fac.Id;
                GameSession.PlayerFactionId = fac.Id;
                ShowSkirmishPage();
            };
            facGrid.AddChild(btn);
        }
        facVb.AddChild(facGrid);

        // 游戏规则面板
        rightCol.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

        var rulePanel = MakeMetalPanel(0, 0);
        rulePanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var ruleVb = new VBoxContainer();
        ruleVb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ruleVb.OffsetLeft = 12; ruleVb.OffsetTop = 8; ruleVb.OffsetRight = -12; ruleVb.OffsetBottom = -8;
        ruleVb.AddThemeConstantOverride("separation", 4);
        rulePanel.AddChild(ruleVb);
        rightCol.AddChild(rulePanel);

        ruleVb.AddChild(MakeLabel(TrManager.Tr("menu.rules"), 14, ColGold));

        // RA2 风格规则复选框
        ruleVb.AddChild(MakeCheckboxRow(TrManager.Tr("menu.superweapons"), _ruleSuperweapons, (on) => _ruleSuperweapons = on));
        ruleVb.AddChild(MakeCheckboxRow(TrManager.Tr("menu.short_game"), _ruleShortGame, (on) => _ruleShortGame = on));

        // 启动资金选择
        var credRow = new HBoxContainer();
        credRow.AddThemeConstantOverride("separation", 6);
        credRow.AddChild(MakeLabel(TrManager.Tr("menu.credits_start"), 12, ColTextDim));
        var credOptions = new int[] { 3000, 5000, 7500, 10000 };
        foreach (var c in credOptions)
        {
            var btn = MakeGrayButton($"${c}", 12, _startCredits == c);
            btn.Pressed += () => _startCredits = c;
            credRow.AddChild(btn);
        }
        ruleVb.AddChild(credRow);

        // ===== 底部操作栏 =====
        var bottomBar = new HBoxContainer();
        bottomBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bottomBar.Alignment = BoxContainer.AlignmentMode.Center;
        bottomBar.AddThemeConstantOverride("separation", 16);
        _pageRoot.AddChild(bottomBar);

        var backBtn = MakeGrayButton(TrManager.Tr("menu.back"), 16);
        backBtn.CustomMinimumSize = new Vector2(160, 44);
        backBtn.Pressed += () => ShowMainMenu();
        bottomBar.AddChild(backBtn);

        var fightBtn = MakeRA2Button(TrManager.Tr("menu.battle_fight"), 22);
        fightBtn.CustomMinimumSize = new Vector2(260, 48);
        fightBtn.Pressed += () =>
        {
            // 读取种子
            string seedText = _seedInput != null ? _seedInput.Text.Trim() : "";
            if (!string.IsNullOrEmpty(seedText) && ulong.TryParse(seedText, out var parsedSeed))
                GameSession.MapSeed = parsedSeed;
            else
                GameSession.MapSeed = 0;
            GameLog.Info($"[MainMenu] 开始遭遇战 — 难度: {GameSession.SelectedDifficulty}, 种子: {GameSession.MapSeed}, 尺寸: {GameSession.SelectedMapSize}, 主题: {GameSession.SelectedMapTheme}, 阵营: {GameSession.PlayerFactionId}, 超武: {_ruleSuperweapons}, 起始资金: {_startCredits}");
            CallDeferred(nameof(ChangeToGameScene));
        };
        bottomBar.AddChild(fightBtn);
    }

    // ==================== 设置页面 ====================

    private void ShowSettingsPage()
    {
        ClearPage();

        // 顶部标题栏
        var titleBar = MakeMetalPanel(0, 44, true);
        titleBar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var titleVb = new VBoxContainer();
        titleVb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        titleVb.OffsetLeft = 16; titleVb.OffsetTop = 4; titleVb.OffsetRight = -16; titleVb.OffsetBottom = -4;
        titleBar.AddChild(titleVb);
        var title = MakeLabel(TrManager.Tr("ui.settings"), 24, ColGold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        titleVb.AddChild(title);
        _pageRoot.AddChild(titleBar);

        // 居中内容区
        var centerWrap = new CenterContainer();
        centerWrap.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        centerWrap.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _pageRoot.AddChild(centerWrap);

        var contentCol = new VBoxContainer();
        contentCol.CustomMinimumSize = new Vector2(680, 0);
        contentCol.AddThemeConstantOverride("separation", 8);
        centerWrap.AddChild(contentCol);

        // --- 画质面板 ---
        var qPanel = MakeMetalPanel(0, 0);
        var qVb = new VBoxContainer();
        qVb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        qVb.OffsetLeft = 12; qVb.OffsetTop = 8; qVb.OffsetRight = -12; qVb.OffsetBottom = -8;
        qVb.AddThemeConstantOverride("separation", 6);
        qPanel.AddChild(qVb);
        contentCol.AddChild(qPanel);

        qVb.AddChild(MakeLabel(TrManager.Tr("menu.section_quality"), 14, ColGold));

        var qualityOptions = new (string Label, QualitySettings.QualityLevel Level)[]
        {
            (TrManager.Tr("menu.quality_low"), QualitySettings.QualityLevel.Low),
            (TrManager.Tr("menu.quality_medium"), QualitySettings.QualityLevel.Medium),
            (TrManager.Tr("menu.quality_high"), QualitySettings.QualityLevel.High),
        };
        var qRow = new HBoxContainer();
        qRow.AddThemeConstantOverride("separation", 6);
        qRow.AddChild(MakeLabel(TrManager.Tr("menu.quality_level"), 12, ColTextDim));
        foreach (var opt in qualityOptions)
        {
            var btn = MakeGrayButton(opt.Label, 13, _settingsQuality == opt.Level);
            btn.Pressed += () =>
            {
                _settingsQuality = opt.Level;
                QualitySettings.SetQuality(opt.Level);
                ShowSettingsPage();
            };
            qRow.AddChild(btn);
        }
        qVb.AddChild(qRow);

        qVb.AddChild(MakeLabel(TrManager.Tr("ui.quality_current", QualitySettings.LevelName, TrManager.Tr(QualitySettings.HasGPU ? "ui.quality_gpu_detected" : "ui.quality_gpu_not_detected")), 11, ColTextDim));

        // --- 显示面板 ---
        var dPanel = MakeMetalPanel(0, 0);
        var dVb = new VBoxContainer();
        dVb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dVb.OffsetLeft = 12; dVb.OffsetTop = 8; dVb.OffsetRight = -12; dVb.OffsetBottom = -8;
        dVb.AddThemeConstantOverride("separation", 6);
        dPanel.AddChild(dVb);
        contentCol.AddChild(dPanel);

        dVb.AddChild(MakeLabel(TrManager.Tr("menu.section_display"), 14, ColGold));

        var fsRow = new HBoxContainer();
        fsRow.AddThemeConstantOverride("separation", 6);
        fsRow.AddChild(MakeLabel(TrManager.Tr("menu.fullscreen"), 12, ColTextDim));
        var fsBtn = MakeGrayButton(_settingsFullscreen ? TrManager.Tr("menu.fullscreen_on") : TrManager.Tr("menu.fullscreen_off"), 13, _settingsFullscreen);
        fsBtn.Pressed += () =>
        {
            _settingsFullscreen = !_settingsFullscreen;
            if (_settingsFullscreen)
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            else
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            ShowSettingsPage();
        };
        fsRow.AddChild(fsBtn);
        dVb.AddChild(fsRow);

        dVb.AddChild(MakeLabel(TrManager.Tr("menu.f11_hint"), 11, ColTextDim));

        // --- 音频面板 ---
        var aPanel = MakeMetalPanel(0, 0);
        var aVb = new VBoxContainer();
        aVb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        aVb.OffsetLeft = 12; aVb.OffsetTop = 8; aVb.OffsetRight = -12; aVb.OffsetBottom = -8;
        aVb.AddThemeConstantOverride("separation", 6);
        aPanel.AddChild(aVb);
        contentCol.AddChild(aPanel);

        aVb.AddChild(MakeLabel(TrManager.Tr("menu.section_audio"), 14, ColGold));

        var volRow = new HBoxContainer();
        volRow.AddThemeConstantOverride("separation", 8);
        volRow.AddChild(MakeLabel(TrManager.Tr("menu.master_volume"), 12, ColTextDim));
        var slider = new HSlider();
        slider.CustomMinimumSize = new Vector2(300, 24);
        slider.MinValue = 0f;
        slider.MaxValue = 1f;
        slider.Step = 0.05f;
        slider.Value = _settingsVolume;
        slider.ValueChanged += (v) =>
        {
            _settingsVolume = (float)v;
            AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(_settingsVolume));
        };
        volRow.AddChild(slider);
        volRow.AddChild(MakeLabel($"{(int)(_settingsVolume * 100)}%", 13, ColTextDim));
        aVb.AddChild(volRow);

        // --- 语言面板 ---
        var lPanel = MakeMetalPanel(0, 0);
        var lVb = new VBoxContainer();
        lVb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        lVb.OffsetLeft = 12; lVb.OffsetTop = 8; lVb.OffsetRight = -12; lVb.OffsetBottom = -8;
        lVb.AddThemeConstantOverride("separation", 6);
        lPanel.AddChild(lVb);
        contentCol.AddChild(lPanel);

        lVb.AddChild(MakeLabel(TrManager.Tr("menu.section_language"), 14, ColGold));

        var langOptions = new (string Label, string Code)[]
        {
            ("中文", "zh-CN"),
            ("English", "en"),
        };
        var langRow = new HBoxContainer();
        langRow.AddThemeConstantOverride("separation", 8);
        langRow.AddChild(MakeLabel(TrManager.Tr("menu.ui_language"), 12, ColTextDim));
        foreach (var opt in langOptions)
        {
            var btn = MakeGrayButton(opt.Label, 14, TrManager.CurrentLang == opt.Code);
            btn.Pressed += () =>
            {
                TrManager.SetLanguage(opt.Code);
                ShowSettingsPage();
            };
            langRow.AddChild(btn);
        }
        lVb.AddChild(langRow);
        lVb.AddChild(MakeLabel(TrManager.Tr("menu.lang_partial_hint"), 10, ColTextDim));

        // --- 返回按钮 ---
        contentCol.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

        var backRow = new HBoxContainer();
        backRow.Alignment = BoxContainer.AlignmentMode.Center;
        backRow.AddThemeConstantOverride("separation", 16);
        contentCol.AddChild(backRow);

        var backBtn = MakeGrayButton(TrManager.Tr("menu.back_to_main"), 16);
        backBtn.CustomMinimumSize = new Vector2(220, 44);
        backBtn.Pressed += () => ShowMainMenu();
        backRow.AddChild(backBtn);
    }

    // ==================== 辅助方法 ====================

    private Label MakeDescLabel(string text)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeFontSizeOverride("font_size", 11);
        lbl.AddThemeColorOverride("font_color", ColTextDim);
        return lbl;
    }

    private static Label MakeLabel(string text, int fontSize, Color color)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        lbl.AddThemeColorOverride("font_color", color);
        return lbl;
    }

    private void ChangeToGameScene()
    {
        GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
    }
}