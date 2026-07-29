using Godot;
using System.Collections.Generic;

namespace RTSGame;

/// <summary>
/// 主菜单 — 红警2 (RA2) 风格 UI 全面重构。
/// 使用绝对定位 + 锚点布局，避免 BoxContainer 子节点锚点失效问题。
/// </summary>
public partial class MainMenu : Control
{
    private Control _pageRoot = null!;
    private LineEdit _seedInput = null!;

    private QualitySettings.QualityLevel _settingsQuality = QualitySettings.QualityLevel.High;
    private float _settingsVolume = 1.0f;
    private bool _settingsFullscreen = true;

    // RA2 配色
    private static readonly Color ColBg = new(0.02f, 0.02f, 0.03f, 1f);
    private static readonly Color ColPanelBg = new(0.08f, 0.08f, 0.10f, 0.95f);
    private static readonly Color ColPanelBorder = new(0.35f, 0.36f, 0.38f, 1f);
    private static readonly Color ColPanelBorderHi = new(0.55f, 0.56f, 0.58f, 1f);
    private static readonly Color ColRed = new(0.75f, 0.10f, 0.10f, 1f);
    private static readonly Color ColRedBright = new(0.90f, 0.20f, 0.15f, 1f);
    private static readonly Color ColGold = new(0.85f, 0.70f, 0.30f, 1f);
    private static readonly Color ColTextMain = new(0.88f, 0.88f, 0.90f, 1f);
    private static readonly Color ColTextDim = new(0.50f, 0.50f, 0.55f, 1f);

    // 选中态
    private MapConfig.SizePreset _selMapSize = MapConfig.SizePreset.Small;
    private MapConfig.MapTheme _selMapTheme = MapConfig.MapTheme.Default;
    private string _selFactionId = "Allies";
    private Main.Difficulty _selDifficulty = Main.Difficulty.Normal;
    private bool _ruleSuperweapons = true;
    private bool _ruleShortGame = false;
    private int _startCredits = 5000;

    public override void _Ready()
    {
        // --difficulty 命令行自动化
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
                GameLog.Info($"[MainMenu] 自动进入游戏 (难度 {GameSession.SelectedDifficulty}, 种子 {GameSession.MapSeed})");
                CallDeferred(nameof(ChangeToGameScene));
                return;
            }
        }

        TrManager.SetLanguage("zh-CN");
        FactionManager.Load();
        _settingsQuality = QualitySettings.Current;
        _settingsFullscreen = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;

        _selMapSize = GameSession.SelectedMapSize;
        _selMapTheme = GameSession.SelectedMapTheme;
        _selFactionId = GameSession.PlayerFactionId;
        _selDifficulty = GameSession.SelectedDifficulty;

        BuildBackground();
        _pageRoot = new Control();
        _pageRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_pageRoot);

        CallDeferred(nameof(ShowMainMenuDeferred));

        var bgmPlayer = new AudioStreamPlayer { Name = "BgmPlayer", Bus = "Master" };
        AddChild(bgmPlayer);
        BgmManager.Initialize(bgmPlayer);
        BgmManager.SwitchScene(BgmManager.BgmScene.Menu);
        GameLog.Info("[MainMenu] 主菜单已加载 (RA2风格 v2)");
    }

    private void ShowMainMenuDeferred()
    {
        if (IsInstanceValid(_pageRoot))
            ShowMainMenu();
    }

    // ==================== 背景 ====================

    private void BuildBackground()
    {
        var bg = new ColorRect();
        bg.Color = ColBg;
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        var grid = new Line2D();
        grid.Width = 1f;
        grid.DefaultColor = new Color(0.10f, 0.10f, 0.12f, 0.5f);
        var pts = new List<Vector2>();
        for (int x = 0; x <= 1920; x += 64) { pts.Add(new Vector2(x, 0)); pts.Add(new Vector2(x, 1080)); }
        for (int y = 0; y <= 1080; y += 64) { pts.Add(new Vector2(0, y)); pts.Add(new Vector2(1920, y)); }
        grid.Points = pts.ToArray();
        AddChild(grid);
    }

    private void ClearPage()
    {
        foreach (var child in _pageRoot.GetChildren())
            child.QueueFree();
    }

    // ==================== 样式辅助 ====================

    /// <summary>创建带银色边框的暗色面板（PanelContainer 会自动布局子节点）。</summary>
    private Control MakePanel(float x, float y, float w, float h, bool highlight = false)
    {
        // 外层 Control 做绝对定位
        var wrapper = new Control();
        wrapper.OffsetLeft = x;
        wrapper.OffsetTop = y;
        wrapper.OffsetRight = x + w;
        wrapper.OffsetBottom = y + h;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var style = new StyleBoxFlat();
        style.BgColor = ColPanelBg;
        style.BorderColor = highlight ? ColPanelBorderHi : ColPanelBorder;
        style.SetBorderWidthAll(2);
        style.SetContentMarginAll(8);
        panel.AddThemeStyleboxOverride("panel", style);
        wrapper.AddChild(panel);

        // 通过 Meta 关联内部 PanelContainer
        wrapper.SetMeta("panel", panel);
        return wrapper;
    }

    /// <summary>在 Control wrapper 内部放置 VBoxContainer。</summary>
    private VBoxContainer MakePanelContent(Control wrapper, float padding = 12f)
    {
        // 获取内部 PanelContainer
        var panel = wrapper.GetMeta("panel").As<PanelContainer>();
        if (panel == null) return new VBoxContainer();

        var mc = new MarginContainer();
        mc.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mc.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        mc.AddThemeConstantOverride("margin_left", (int)padding);
        mc.AddThemeConstantOverride("margin_right", (int)padding);
        mc.AddThemeConstantOverride("margin_top", (int)padding);
        mc.AddThemeConstantOverride("margin_bottom", (int)padding);
        panel.AddChild(mc);

        var vb = new VBoxContainer();
        vb.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vb.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        vb.AddThemeConstantOverride("separation", 6);
        mc.AddChild(vb);
        return vb;
    }

    /// <summary>RA2 红色大按钮。</summary>
    private Button MakeRedButton(string text, int fontSize = 20, float w = 320, float h = 48)
    {
        var btn = new Button();
        btn.Text = text;
        btn.AddThemeFontSizeOverride("font_size", fontSize);
        btn.CustomMinimumSize = new Vector2(w, h);

        var normal = new StyleBoxFlat();
        normal.BgColor = ColRed;
        normal.BorderColor = ColPanelBorder;
        normal.SetBorderWidthAll(2);
        normal.SetContentMarginAll(8);
        btn.AddThemeStyleboxOverride("normal", normal);

        var hover = new StyleBoxFlat();
        hover.BgColor = ColRedBright;
        hover.BorderColor = ColPanelBorderHi;
        hover.SetBorderWidthAll(2);
        hover.SetContentMarginAll(8);
        btn.AddThemeStyleboxOverride("hover", hover);

        var pressed = new StyleBoxFlat();
        pressed.BgColor = new Color(0.5f, 0.05f, 0.05f);
        pressed.BorderColor = ColGold;
        pressed.SetBorderWidthAll(2);
        pressed.SetContentMarginAll(8);
        btn.AddThemeStyleboxOverride("pressed", pressed);

        btn.AddThemeColorOverride("font_color", Colors.White);
        btn.AddThemeColorOverride("font_hover_color", ColGold);
        btn.AddThemeColorOverride("font_pressed_color", ColGold);
        return btn;
    }

    /// <summary>灰色小按钮（选项/选择用）。</summary>
    private Button MakeGrayButton(string text, int fontSize = 14, bool selected = false)
    {
        var btn = new Button();
        btn.Text = text;
        btn.AddThemeFontSizeOverride("font_size", fontSize);
        btn.CustomMinimumSize = new Vector2(0, 32);

        var normal = new StyleBoxFlat();
        normal.BgColor = selected ? new Color(0.18f, 0.18f, 0.22f) : new Color(0.10f, 0.10f, 0.12f);
        normal.BorderColor = selected ? ColGold : ColPanelBorder;
        normal.SetBorderWidthAll(1);
        normal.SetContentMarginAll(6);
        btn.AddThemeStyleboxOverride("normal", normal);

        var hover = new StyleBoxFlat();
        hover.BgColor = new Color(0.20f, 0.20f, 0.24f);
        hover.BorderColor = ColPanelBorderHi;
        hover.SetBorderWidthAll(1);
        hover.SetContentMarginAll(6);
        btn.AddThemeStyleboxOverride("hover", hover);

        btn.AddThemeColorOverride("font_color", selected ? ColGold : ColTextMain);
        btn.AddThemeColorOverride("font_hover_color", ColGold);
        return btn;
    }

    private static Label MakeLabel(string text, int fontSize, Color color)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeFontSizeOverride("font_size", fontSize);
        lbl.AddThemeColorOverride("font_color", color);
        return lbl;
    }

    private HBoxContainer MakeRow(string labelText, int labelSize = 12)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        if (!string.IsNullOrEmpty(labelText))
            row.AddChild(MakeLabel(labelText, labelSize, ColTextDim));
        return row;
    }

    // ==================== 主菜单页 ====================

    private void ShowMainMenu()
    {
        ClearPage();

        // ===== 左侧展示面板 =====
        var leftPanel = MakePanel(0, 0, 1100, 1080);
        _pageRoot.AddChild(leftPanel);
        var leftVb = MakePanelContent(leftPanel, 60);
        leftVb.OffsetLeft = 60; leftVb.OffsetTop = 80; leftVb.OffsetRight = -40; leftVb.OffsetBottom = -60;
        leftVb.AddThemeConstantOverride("separation", 6);

        leftVb.AddChild(MakeLabel(TrManager.Tr("menu.title"), 56, ColRedBright));
        leftVb.AddChild(MakeLabel("IRON CURTAIN RTS", 18, ColTextDim));
        leftVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });
        leftVb.AddChild(MakeLabel(TrManager.Tr("menu.version"), 14, ColGold));
        leftVb.AddChild(MakeLabel("1:1 复刻红警2核心体验 · 15分钟一局", 14, ColTextDim));
        leftVb.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        leftVb.AddChild(MakeLabel("© 2026 RTS_Game · Powered by Godot 4.7", 11, ColTextDim));

        // ===== 右侧按钮面板 =====
        var rightPanel = MakePanel(1100, 0, 820, 1080);
        _pageRoot.AddChild(rightPanel);
        var rightVb = MakePanelContent(rightPanel, 40);
        rightVb.OffsetLeft = 40; rightVb.OffsetTop = 100; rightVb.OffsetRight = -40; rightVb.OffsetBottom = -60;
        rightVb.AddThemeConstantOverride("separation", 8);

        var skBtn = MakeRedButton(TrManager.Tr("menu.skirmish"), 22);
        skBtn.Pressed += () => ShowSkirmishPage();
        rightVb.AddChild(skBtn);
        rightVb.AddChild(MakeLabel(TrManager.Tr("menu.skirmish_desc"), 11, ColTextDim));
        rightVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        var setBtn = MakeRedButton(TrManager.Tr("ui.settings"), 22);
        setBtn.Pressed += () => ShowSettingsPage();
        rightVb.AddChild(setBtn);
        rightVb.AddChild(MakeLabel(TrManager.Tr("menu.settings_desc"), 11, ColTextDim));
        rightVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        var meBtn = MakeRedButton(TrManager.Tr("menu.map_editor"), 22);
        meBtn.Pressed += () =>
        {
            GameLog.Info("[MainMenu] 进入地图编辑器");
            GetTree().ChangeSceneToFile("res://scenes/MapEditor.tscn");
        };
        rightVb.AddChild(meBtn);
        rightVb.AddChild(MakeLabel(TrManager.Tr("menu.map_editor_desc"), 11, ColTextDim));
        rightVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        var p3dBtn = MakeRedButton(TrManager.Tr("menu.prototype_3d"), 22);
        p3dBtn.Pressed += () =>
        {
            GameLog.Info("[MainMenu] 进入3D原型预览");
            GetTree().ChangeSceneToFile("res://scenes/Prototype3D.tscn");
        };
        rightVb.AddChild(p3dBtn);
        rightVb.AddChild(MakeLabel(TrManager.Tr("menu.prototype_3d_desc"), 11, ColTextDim));
        rightVb.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        var exitBtn = MakeRedButton(TrManager.Tr("menu.quit_game"), 18);
        exitBtn.Pressed += () => GetTree().Quit();
        rightVb.AddChild(exitBtn);
    }

    // ==================== 遭遇战页面 ====================

    private void ShowSkirmishPage()
    {
        ClearPage();

        // 顶部标题栏
        var titlePanel = MakePanel(20, 10, 1880, 50, true);
        _pageRoot.AddChild(titlePanel);
        var titleVb = MakePanelContent(titlePanel, 16);
        var title = MakeLabel(TrManager.Tr("menu.skirmish"), 24, ColGold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        titleVb.AddChild(title);

        // 左列面板：地图设置 + 难度
        var leftPanel = MakePanel(20, 80, 900, 940);
        _pageRoot.AddChild(leftPanel);
        var leftVb = MakePanelContent(leftPanel, 16);
        leftVb.AddThemeConstantOverride("separation", 8);

        leftVb.AddChild(MakeLabel(TrManager.Tr("menu.section_map"), 14, ColGold));

        // 地图尺寸
        var sizeRow = MakeRow(TrManager.Tr("menu.map_size"));
        var sizeOpts = new (string L, MapConfig.SizePreset P)[]
        {
            (TrManager.Tr("menu.map_small"), MapConfig.SizePreset.Small),
            (TrManager.Tr("menu.map_medium"), MapConfig.SizePreset.Medium),
            (TrManager.Tr("menu.map_large"), MapConfig.SizePreset.Large),
        };
        foreach (var o in sizeOpts)
        {
            var b = MakeGrayButton(o.L, 12, _selMapSize == o.P);
            b.Pressed += () => { _selMapSize = o.P; GameSession.SelectedMapSize = o.P; ShowSkirmishPage(); };
            sizeRow.AddChild(b);
        }
        leftVb.AddChild(sizeRow);

        // 地图主题
        var themeRow = MakeRow(TrManager.Tr("menu.map_theme"));
        var themeOpts = new (string L, MapConfig.MapTheme T)[]
        {
            (TrManager.Tr("theme.default"), MapConfig.MapTheme.Default),
            (TrManager.Tr("theme.snow"), MapConfig.MapTheme.Snow),
            (TrManager.Tr("theme.desert"), MapConfig.MapTheme.Desert),
            (TrManager.Tr("theme.city"), MapConfig.MapTheme.City),
            (TrManager.Tr("theme.island"), MapConfig.MapTheme.Island),
        };
        foreach (var o in themeOpts)
        {
            var b = MakeGrayButton(o.L, 11, _selMapTheme == o.T);
            b.Pressed += () => { _selMapTheme = o.T; GameSession.SelectedMapTheme = o.T; ShowSkirmishPage(); };
            themeRow.AddChild(b);
        }
        leftVb.AddChild(themeRow);

        // 种子
        var seedRow = MakeRow(TrManager.Tr("menu.map_seed"));
        _seedInput = new LineEdit();
        _seedInput.CustomMinimumSize = new Vector2(180, 28);
        _seedInput.PlaceholderText = TrManager.Tr("menu.seed_placeholder");
        _seedInput.AddThemeFontSizeOverride("font_size", 13);
        seedRow.AddChild(_seedInput);
        leftVb.AddChild(seedRow);
        leftVb.AddChild(MakeLabel(TrManager.Tr("menu.seed_hint"), 10, ColTextDim));

        leftVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        leftVb.AddChild(MakeLabel(TrManager.Tr("menu.section_difficulty"), 14, ColGold));

        var diffs = new (string T, string D, Main.Difficulty Diff, Color C)[]
        {
            (TrManager.Tr("diff.easy_title"), TrManager.Tr("diff.easy_desc"), Main.Difficulty.Easy, new Color(0.3f, 0.8f, 0.4f)),
            (TrManager.Tr("diff.normal_title"), TrManager.Tr("diff.normal_desc"), Main.Difficulty.Normal, new Color(0.4f, 0.7f, 1f)),
            (TrManager.Tr("diff.hard_title"), TrManager.Tr("diff.hard_desc"), Main.Difficulty.Hard, new Color(1f, 0.7f, 0.2f)),
            (TrManager.Tr("diff.brutal_title"), TrManager.Tr("diff.brutal_desc"), Main.Difficulty.Brutal, new Color(1f, 0.3f, 0.3f)),
        };
        foreach (var d in diffs)
        {
            bool sel = _selDifficulty == d.Diff;
            var b = MakeGrayButton($"{d.T} — {d.D}", 14, sel);
            b.CustomMinimumSize = new Vector2(0, 36);
            if (sel) b.AddThemeColorOverride("font_color", d.C);
            b.Pressed += () => { _selDifficulty = d.Diff; GameSession.SelectedDifficulty = d.Diff; ShowSkirmishPage(); };
            leftVb.AddChild(b);
        }

        // 右列面板：阵营 + 游戏规则
        var rightPanel = MakePanel(940, 80, 960, 940);
        _pageRoot.AddChild(rightPanel);
        var rightVb = MakePanelContent(rightPanel, 16);
        rightVb.AddThemeConstantOverride("separation", 8);

        rightVb.AddChild(MakeLabel(TrManager.Tr("menu.section_faction"), 14, ColGold));

        var facGrid = new GridContainer();
        facGrid.Columns = 3;
        facGrid.AddThemeConstantOverride("h_separation", 8);
        facGrid.AddThemeConstantOverride("v_separation", 8);
        foreach (var fac in FactionManager.GetAllFactions())
        {
            bool sel = _selFactionId == fac.Id;
            var b = MakeGrayButton(fac.Name, 15, sel);
            b.CustomMinimumSize = new Vector2(140, 40);
            if (sel) b.AddThemeColorOverride("font_color", fac.Color);
            b.Pressed += () => { _selFactionId = fac.Id; GameSession.PlayerFactionId = fac.Id; ShowSkirmishPage(); };
            facGrid.AddChild(b);
        }
        rightVb.AddChild(facGrid);

        rightVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        rightVb.AddChild(MakeLabel(TrManager.Tr("menu.rules"), 14, ColGold));

        // 超武复选框
        var swCheck = new CheckBox();
        swCheck.Text = TrManager.Tr("menu.superweapons");
        swCheck.ButtonPressed = _ruleSuperweapons;
        swCheck.AddThemeFontSizeOverride("font_size", 13);
        swCheck.AddThemeColorOverride("font_color", ColTextMain);
        swCheck.Toggled += (on) => _ruleSuperweapons = on;
        rightVb.AddChild(swCheck);

        var sgCheck = new CheckBox();
        sgCheck.Text = TrManager.Tr("menu.short_game");
        sgCheck.ButtonPressed = _ruleShortGame;
        sgCheck.AddThemeFontSizeOverride("font_size", 13);
        sgCheck.AddThemeColorOverride("font_color", ColTextMain);
        sgCheck.Toggled += (on) => _ruleShortGame = on;
        rightVb.AddChild(sgCheck);

        // 起始资金
        var credRow = MakeRow(TrManager.Tr("menu.credits_start"));
        foreach (var c in new int[] { 3000, 5000, 7500, 10000 })
        {
            var b = MakeGrayButton($"${c}", 12, _startCredits == c);
            b.Pressed += () => _startCredits = c;
            credRow.AddChild(b);
        }
        rightVb.AddChild(credRow);

        // ===== 底部操作栏 =====
        var bottomPanel = MakePanel(20, 1030, 1880, 50);
        _pageRoot.AddChild(bottomPanel);
        var bottomHb = new HBoxContainer();
        bottomHb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bottomHb.Alignment = BoxContainer.AlignmentMode.Center;
        bottomHb.AddThemeConstantOverride("separation", 24);
        bottomPanel.AddChild(bottomHb);

        var backBtn = MakeGrayButton(TrManager.Tr("menu.back"), 16);
        backBtn.CustomMinimumSize = new Vector2(160, 40);
        backBtn.Pressed += () => ShowMainMenu();
        bottomHb.AddChild(backBtn);

        var fightBtn = MakeRedButton(TrManager.Tr("menu.battle_fight"), 22, 280, 44);
        fightBtn.Pressed += () =>
        {
            string seedText = _seedInput != null ? _seedInput.Text.Trim() : "";
            if (!string.IsNullOrEmpty(seedText) && ulong.TryParse(seedText, out var s))
                GameSession.MapSeed = s;
            else
                GameSession.MapSeed = 0;
            GameLog.Info($"[MainMenu] 开始遭遇战 — 难度:{GameSession.SelectedDifficulty} 种子:{GameSession.MapSeed} 尺寸:{GameSession.SelectedMapSize} 主题:{GameSession.SelectedMapTheme} 阵营:{GameSession.PlayerFactionId} 超武:{_ruleSuperweapons} 资金:{_startCredits}");
            CallDeferred(nameof(ChangeToGameScene));
        };
        bottomHb.AddChild(fightBtn);
    }

    // ==================== 设置页面 ====================

    private void ShowSettingsPage()
    {
        ClearPage();

        // 顶部标题栏
        var titlePanel = MakePanel(20, 10, 1880, 50, true);
        _pageRoot.AddChild(titlePanel);
        var titleVb = MakePanelContent(titlePanel, 16);
        var title = MakeLabel(TrManager.Tr("ui.settings"), 24, ColGold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        titleVb.AddChild(title);

        // 居中面板组
        var centerPanel = MakePanel(460, 80, 1000, 940);
        _pageRoot.AddChild(centerPanel);
        var cv = MakePanelContent(centerPanel, 20);
        cv.AddThemeConstantOverride("separation", 12);

        // 画质
        cv.AddChild(MakeLabel(TrManager.Tr("menu.section_quality"), 14, ColGold));
        var qRow = MakeRow(TrManager.Tr("menu.quality_level"));
        foreach (var o in new (string L, QualitySettings.QualityLevel V)[]
        {
            (TrManager.Tr("menu.quality_low"), QualitySettings.QualityLevel.Low),
            (TrManager.Tr("menu.quality_medium"), QualitySettings.QualityLevel.Medium),
            (TrManager.Tr("menu.quality_high"), QualitySettings.QualityLevel.High),
        })
        {
            var b = MakeGrayButton(o.L, 13, _settingsQuality == o.V);
            b.Pressed += () => { _settingsQuality = o.V; QualitySettings.SetQuality(o.V); ShowSettingsPage(); };
            qRow.AddChild(b);
        }
        cv.AddChild(qRow);
        cv.AddChild(MakeLabel(TrManager.Tr("ui.quality_current", QualitySettings.LevelName, TrManager.Tr(QualitySettings.HasGPU ? "ui.quality_gpu_detected" : "ui.quality_gpu_not_detected")), 11, ColTextDim));

        cv.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        // 显示
        cv.AddChild(MakeLabel(TrManager.Tr("menu.section_display"), 14, ColGold));
        var fsRow = MakeRow(TrManager.Tr("menu.fullscreen"));
        var fsBtn = MakeGrayButton(_settingsFullscreen ? TrManager.Tr("menu.fullscreen_on") : TrManager.Tr("menu.fullscreen_off"), 13, _settingsFullscreen);
        fsBtn.Pressed += () =>
        {
            _settingsFullscreen = !_settingsFullscreen;
            DisplayServer.WindowSetMode(_settingsFullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
            ShowSettingsPage();
        };
        fsRow.AddChild(fsBtn);
        cv.AddChild(fsRow);
        cv.AddChild(MakeLabel(TrManager.Tr("menu.f11_hint"), 11, ColTextDim));

        cv.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        // 音频
        cv.AddChild(MakeLabel(TrManager.Tr("menu.section_audio"), 14, ColGold));
        var volRow = MakeRow(TrManager.Tr("menu.master_volume"));
        var slider = new HSlider();
        slider.CustomMinimumSize = new Vector2(300, 24);
        slider.MinValue = 0; slider.MaxValue = 1; slider.Step = 0.05f; slider.Value = _settingsVolume;
        slider.ValueChanged += (v) => { _settingsVolume = (float)v; AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(_settingsVolume)); };
        volRow.AddChild(slider);
        volRow.AddChild(MakeLabel($"{(int)(_settingsVolume * 100)}%", 13, ColTextDim));
        cv.AddChild(volRow);

        cv.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        // 语言
        cv.AddChild(MakeLabel(TrManager.Tr("menu.section_language"), 14, ColGold));
        var langRow = MakeRow(TrManager.Tr("menu.ui_language"));
        foreach (var o in new (string L, string C)[] { ("中文", "zh-CN"), ("English", "en") })
        {
            var b = MakeGrayButton(o.L, 14, TrManager.CurrentLang == o.C);
            b.Pressed += () => { TrManager.SetLanguage(o.C); ShowSettingsPage(); };
            langRow.AddChild(b);
        }
        cv.AddChild(langRow);
        cv.AddChild(MakeLabel(TrManager.Tr("menu.lang_partial_hint"), 10, ColTextDim));

        cv.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        // 返回
        var backRow = new HBoxContainer();
        backRow.Alignment = BoxContainer.AlignmentMode.Center;
        var backBtn = MakeGrayButton(TrManager.Tr("menu.back_to_main"), 16);
        backBtn.CustomMinimumSize = new Vector2(220, 44);
        backBtn.Pressed += () => ShowMainMenu();
        backRow.AddChild(backBtn);
        cv.AddChild(backRow);
    }

    private void ChangeToGameScene()
    {
        GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
    }
}
