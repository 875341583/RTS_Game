using Godot;
using System;
using System.Collections.Generic;

namespace RTSGame;

/// <summary>
/// 主菜单 — 红警2 (RA2) 风格 UI 全面重构。
/// 使用绝对定位 + 锚点布局，避免 BoxContainer 子节点锚点失效问题。
/// </summary>
public partial class MainMenu : Control
{
    private Control? _pageRoot;
    private LineEdit? _seedInput;

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
                GameLog.Info($"[MainMenu] Auto-start game (difficulty {GameSession.SelectedDifficulty}, seed {GameSession.MapSeed})");
                CallDeferred(nameof(ChangeToGameScene));
                return;
            }
        }

        TrManager.SetLanguage("zh-CN");
        FactionManager.Load();
        NetworkManager.Init(); // 初始化联机系统
        _settingsQuality = QualitySettings.Current;
        _settingsFullscreen = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;

        _selMapSize = GameSession.SelectedMapSize;
        _selMapTheme = GameSession.SelectedMapTheme;
        _selFactionId = GameSession.PlayerFactionId;
        _selDifficulty = GameSession.SelectedDifficulty;

        BuildBackground();
        _pageRoot = new Control();
        _pageRoot!.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_pageRoot!);

        CallDeferred(nameof(ShowMainMenuDeferred));

        var bgmPlayer = new AudioStreamPlayer { Name = "BgmPlayer", Bus = "Master" };
        AddChild(bgmPlayer);
        BgmManager.Initialize(bgmPlayer);
        BgmManager.SwitchScene(BgmManager.BgmScene.Menu);
        GameLog.Info("[MainMenu] Main menu loaded (RA2 style v2)");
    }

    private void ShowMainMenuDeferred()
    {
        if (IsInstanceValid(_pageRoot!))
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
        foreach (var child in _pageRoot!.GetChildren())
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
        _pageRoot!.AddChild(leftPanel);
        var leftVb = MakePanelContent(leftPanel, 60);
        leftVb.OffsetLeft = 60; leftVb.OffsetTop = 80; leftVb.OffsetRight = -40; leftVb.OffsetBottom = -60;
        leftVb.AddThemeConstantOverride("separation", 6);

        leftVb.AddChild(MakeLabel(TrManager.Tr("menu.title"), 56, ColRedBright));
        leftVb.AddChild(MakeLabel("IRON CURTAIN RTS", 18, ColTextDim));
        leftVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });
        leftVb.AddChild(MakeLabel(TrManager.Tr("menu.version"), 14, ColGold));
        leftVb.AddChild(MakeLabel(TrManager.Tr("menu.reproduction_mode"), 14, ColTextDim));
        leftVb.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        leftVb.AddChild(MakeLabel(TrManager.Tr("menu.copyright"), 11, ColTextDim));

        // ===== 右侧按钮面板 =====
        var rightPanel = MakePanel(1100, 0, 820, 1080);
        _pageRoot!.AddChild(rightPanel);
        var rightVb = MakePanelContent(rightPanel, 40);
        rightVb.OffsetLeft = 40; rightVb.OffsetTop = 100; rightVb.OffsetRight = -40; rightVb.OffsetBottom = -60;
        rightVb.AddThemeConstantOverride("separation", 8);

        var skBtn = MakeRedButton(TrManager.Tr("menu.skirmish"), 22);
        skBtn.Pressed += () => ShowSkirmishPage();
        rightVb.AddChild(skBtn);
        rightVb.AddChild(MakeLabel(TrManager.Tr("menu.skirmish_desc"), 11, ColTextDim));
        rightVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        var mpBtn = MakeRedButton(TrManager.Tr("menu.mp_title"), 22);
        mpBtn.Pressed += () => ShowMultiplayerPage();
        rightVb.AddChild(mpBtn);
        rightVb.AddChild(MakeLabel(TrManager.Tr("menu.mp_desc"), 11, ColTextDim));
        rightVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        var setBtn = MakeRedButton(TrManager.Tr("ui.settings"), 22);
        setBtn.Pressed += () => ShowSettingsPage();
        rightVb.AddChild(setBtn);
        rightVb.AddChild(MakeLabel(TrManager.Tr("menu.settings_desc"), 11, ColTextDim));
        rightVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        var meBtn = MakeRedButton(TrManager.Tr("menu.map_editor"), 22);
        meBtn.Pressed += () =>
        {
            GameLog.Info("[MainMenu] Entering map editor");
            GetTree().ChangeSceneToFile("res://scenes/MapEditor.tscn");
        };
        rightVb.AddChild(meBtn);
        rightVb.AddChild(MakeLabel(TrManager.Tr("menu.map_editor_desc"), 11, ColTextDim));
        rightVb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        var p3dBtn = MakeRedButton(TrManager.Tr("menu.prototype_3d"), 22);
        p3dBtn.Pressed += () =>
        {
            GameLog.Info("[MainMenu] Entering 3D prototype preview");
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
        _pageRoot!.AddChild(titlePanel);
        var titleVb = MakePanelContent(titlePanel, 16);
        var title = MakeLabel(TrManager.Tr("menu.skirmish"), 24, ColGold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        titleVb.AddChild(title);

        // 左列面板：地图设置 + 难度
        var leftPanel = MakePanel(20, 80, 900, 940);
        _pageRoot!.AddChild(leftPanel);
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
        _seedInput!.CustomMinimumSize = new Vector2(180, 28);
        _seedInput!.PlaceholderText = TrManager.Tr("menu.seed_placeholder");
        _seedInput!.AddThemeFontSizeOverride("font_size", 13);
        seedRow.AddChild(_seedInput!);
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
        _pageRoot!.AddChild(rightPanel);
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
        _pageRoot!.AddChild(bottomPanel);
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
            string seedText = _seedInput! != null ? _seedInput!.Text.Trim() : "";
            if (!string.IsNullOrEmpty(seedText) && ulong.TryParse(seedText, out var s))
                GameSession.MapSeed = s;
            else
                GameSession.MapSeed = 0;
            GameLog.Info($"[MainMenu] Start skirmish — Difficulty:{GameSession.SelectedDifficulty} Seed:{GameSession.MapSeed} Size:{GameSession.SelectedMapSize} Theme:{GameSession.SelectedMapTheme} Faction:{GameSession.PlayerFactionId} Superweapons:{_ruleSuperweapons} Credits:{_startCredits}");
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
        _pageRoot!.AddChild(titlePanel);
        var titleVb = MakePanelContent(titlePanel, 16);
        var title = MakeLabel(TrManager.Tr("ui.settings"), 24, ColGold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        titleVb.AddChild(title);

        // 居中面板组
        var centerPanel = MakePanel(460, 80, 1000, 940);
        _pageRoot!.AddChild(centerPanel);
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
        foreach (var o in new (string L, string C)[] { (TrManager.Tr("menu.lang_zh"), "zh-CN"), (TrManager.Tr("menu.lang_en"), "en") })
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

    // ==================== 联机页面 ====================

    private LineEdit? _mpNameInput;
    private LineEdit? _mpIpInput;
    private LineEdit? _mpPortInput;
    private LineEdit? _mpChatInput;
    private Label? _mpStatusLabel;
    private Label? _mpPlayerListLabel;
    private VBoxContainer? _mpChatBox;
    private int _mpModeChoice = 3; // 3/5/7/9/11

    private void ShowMultiplayerPage()
    {
        ClearPage();

        // 标题
        var titlePanel = MakePanel(20, 10, 1880, 50, true);
        _pageRoot!.AddChild(titlePanel);
        var titleVb = MakePanelContent(titlePanel, 16);
        var title = MakeLabel(TrManager.Tr("menu.mp_title"), 24, ColGold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        titleVb.AddChild(title);

        // 左列：创建房间
        var leftPanel = MakePanel(20, 80, 900, 940);
        _pageRoot!.AddChild(leftPanel);
        var lvb = MakePanelContent(leftPanel, 16);
        lvb.AddThemeConstantOverride("separation", 10);

        lvb.AddChild(MakeLabel(TrManager.Tr("menu.create_room"), 18, ColGold));

        // 玩家名
        lvb.AddChild(MakeLabel(TrManager.Tr("menu.your_name"), 13, ColTextDim));
        _mpNameInput = new LineEdit { Text = TrManager.Tr("menu.mp_player"), CustomMinimumSize = new Vector2(400, 36) };
        _mpNameInput!.AddThemeFontSizeOverride("font_size", 15);
        lvb.AddChild(_mpNameInput!);

        // 模式选择
        lvb.AddChild(MakeLabel(TrManager.Tr("menu.mp_mode"), 13, ColTextDim));
        var modeRow = new HBoxContainer();
        modeRow.AddThemeConstantOverride("separation", 6);
        foreach (var m in new[] { 3, 5, 7, 9, 11 })
        {
            var b = MakeGrayButton(TrManager.Tr("menu.n_player_mode", m), 13, _mpModeChoice == m);
            int mode = m;
            b.Pressed += () => { _mpModeChoice = mode; ShowMultiplayerPage(); };
            modeRow.AddChild(b);
        }
        lvb.AddChild(modeRow);

        // 阵营选择
        lvb.AddChild(MakeLabel(TrManager.Tr("menu.select_faction"), 13, ColTextDim));
        var facRow = new HBoxContainer();
        facRow.AddThemeConstantOverride("separation", 6);
        string[] factions = { "Allies", "Soviet", "Yuri" };
        string[] facLabels = { TrManager.Tr("faction.allies.name"), TrManager.Tr("faction.soviet.name"), TrManager.Tr("faction.yuri.name") };
        foreach (var (fac, label) in System.Linq.Enumerable.Zip(factions, facLabels))
        {
            var b = MakeGrayButton(label, 12, _selFactionId == fac);
            string f = fac;
            b.Pressed += () => { _selFactionId = f; GameSession.PlayerFactionId = f; ShowMultiplayerPage(); };
            facRow.AddChild(b);
        }
        lvb.AddChild(facRow);

        // 端口
        lvb.AddChild(MakeLabel(TrManager.Tr("menu.server_port"), 13, ColTextDim));
        _mpPortInput = new LineEdit { Text = "25565", CustomMinimumSize = new Vector2(200, 36) };
        _mpPortInput!.AddThemeFontSizeOverride("font_size", 15);
        lvb.AddChild(_mpPortInput!);

        lvb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        var createBtn = MakeRedButton(TrManager.Tr("menu.create_and_wait"), 18);
        createBtn.Pressed += () =>
        {
            int port = 25565;
            if (int.TryParse(_mpPortInput!.Text.Trim(), out var p)) port = p;
            var config = new NetworkManager.RoomConfig
            {
                MaxPlayers = _mpModeChoice,
                Port = port,
                HostName = _mpNameInput!.Text.Trim(),
                HostFaction = _selFactionId,
                ModeName = TrManager.Tr("menu.n_player_mode", _mpModeChoice)
            };
            GameSession.PlayerFactionId = _selFactionId;
            if (NetworkManager.CreateRoom(config))
            {
                ShowLobbyPage(isHost: true);
            }
            else
            {
                ShowMpStatus(TrManager.Tr("menu.mp_create_failed"));
            }
        };
        lvb.AddChild(createBtn);

        // ---- 右列：加入房间 ----
        var rightPanel = MakePanel(960, 80, 920, 940);
        _pageRoot!.AddChild(rightPanel);
        var rvb = MakePanelContent(rightPanel, 16);
        rvb.AddThemeConstantOverride("separation", 10);

        rvb.AddChild(MakeLabel(TrManager.Tr("menu.join_room"), 18, ColGold));

        rvb.AddChild(MakeLabel(TrManager.Tr("menu.server_ip"), 13, ColTextDim));
        _mpIpInput = new LineEdit { Text = "127.0.0.1", CustomMinimumSize = new Vector2(400, 36) };
        _mpIpInput!.AddThemeFontSizeOverride("font_size", 15);
        rvb.AddChild(_mpIpInput!);

        rvb.AddChild(MakeLabel(TrManager.Tr("menu.server_port"), 13, ColTextDim));
        var portInput2 = new LineEdit { Text = "25565", CustomMinimumSize = new Vector2(200, 36) };
        portInput2.AddThemeFontSizeOverride("font_size", 15);
        rvb.AddChild(portInput2);

        rvb.AddChild(MakeLabel(TrManager.Tr("menu.your_name"), 13, ColTextDim));
        // 复用同一个名称输入框
        rvb.AddChild(new Label { Text = TrManager.Tr("menu.same_as_left") });

        rvb.AddChild(MakeLabel(TrManager.Tr("menu.select_faction"), 13, ColTextDim));
        var facRow2 = new HBoxContainer();
        facRow2.AddThemeConstantOverride("separation", 6);
        foreach (var (fac, label) in System.Linq.Enumerable.Zip(factions, facLabels))
        {
            var b = MakeGrayButton(label, 12, _selFactionId == fac);
            string f = fac;
            b.Pressed += () => { _selFactionId = f; GameSession.PlayerFactionId = f; ShowMultiplayerPage(); };
            facRow2.AddChild(b);
        }
        rvb.AddChild(facRow2);

        rvb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        var joinBtn = MakeRedButton(TrManager.Tr("menu.join_room"), 18);
        joinBtn.Pressed += () =>
        {
            int port = 25565;
            if (int.TryParse(portInput2.Text.Trim(), out var p)) port = p;
            string ip = _mpIpInput!.Text.Trim();
            if (string.IsNullOrEmpty(ip)) { ShowMpStatus(TrManager.Tr("menu.mp_enter_ip")); return; }
            if (NetworkManager.JoinRoom(ip, port, _mpNameInput!.Text.Trim(), _selFactionId))
            {
                ShowLobbyPage(isHost: false);
            }
            else
            {
                ShowMpStatus(TrManager.Tr("menu.mp_connect_failed"));
            }
        };
        rvb.AddChild(joinBtn);

        // 状态
        _mpStatusLabel = MakeLabel("", 13, ColRedBright);
        rvb.AddChild(_mpStatusLabel!);

        rvb.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        // 返回
        var backBtn = MakeGrayButton(TrManager.Tr("menu.back"), 16);
        backBtn.CustomMinimumSize = new Vector2(160, 40);
        backBtn.Pressed += () => ShowMainMenu();
        rvb.AddChild(backBtn);

        // 底部提示
        var hintPanel = MakePanel(20, 1030, 1880, 50);
        _pageRoot!.AddChild(hintPanel);
        var hintHb = new HBoxContainer();
        hintHb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hintHb.Alignment = BoxContainer.AlignmentMode.Center;
        hintPanel.AddChild(hintHb);
        hintHb.AddChild(MakeLabel(TrManager.Tr("menu.mp_hint"), 12, ColTextDim));
    }

    private void ShowMpStatus(string msg)
    {
        if (_mpStatusLabel != null)
            _mpStatusLabel!.Text = msg;
    }

    // ==================== 联机大厅页面 ====================

    private void ShowLobbyPage(bool isHost)
    {
        ClearPage();

        NetworkManager.LobbyChanged -= OnLobbyChanged;
        NetworkManager.LobbyChanged += OnLobbyChanged;
        NetworkManager.GameStarted -= OnGameStarted;
        NetworkManager.GameStarted += OnGameStarted;
        NetworkManager.Disconnected -= OnNetDisconnected;
        NetworkManager.Disconnected += OnNetDisconnected;
        NetworkManager.ChatReceived -= OnChatReceived;
        NetworkManager.ChatReceived += OnChatReceived;

        // 标题
        var titlePanel = MakePanel(20, 10, 1880, 50, true);
        _pageRoot!.AddChild(titlePanel);
        var titleVb = MakePanelContent(titlePanel, 16);
        var title = MakeLabel(TrManager.Tr("menu.mp_lobby", NetworkManager.Room.ModeName, NetworkManager.Room.MaxPlayers), 22, ColGold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        titleVb.AddChild(title);

        // 左列：玩家列表
        var leftPanel = MakePanel(20, 80, 900, 820);
        _pageRoot!.AddChild(leftPanel);
        var lvb = MakePanelContent(leftPanel, 16);
        lvb.AddThemeConstantOverride("separation", 8);

        lvb.AddChild(MakeLabel(TrManager.Tr("menu.player_list"), 18, ColGold));
        _mpPlayerListLabel = MakeLabel("", 14, ColTextMain);
        _mpPlayerListLabel!.CustomMinimumSize = new Vector2(0, 400);
        lvb.AddChild(_mpPlayerListLabel!);
        RefreshPlayerList();

        // AI填充按钮（仅Host可见）
        if (isHost)
        {
            var aiBtn = MakeGrayButton(NetworkManager._fillWithAI ? TrManager.Tr("menu.mp_ai_fill_on") : TrManager.Tr("menu.mp_ai_fill_off"), 13, NetworkManager._fillWithAI);
            aiBtn.Pressed += () =>
            {
                NetworkManager.ToggleFillAI();
                ShowLobbyPage(isHost);
            };
            lvb.AddChild(aiBtn);
            lvb.AddChild(MakeLabel(TrManager.Tr("menu.mp_ai_fill_hint"), 11, ColTextDim));
        }

        // 右列：聊天 + 设置
        var rightPanel = MakePanel(960, 80, 920, 820);
        _pageRoot!.AddChild(rightPanel);
        var rvb = MakePanelContent(rightPanel, 16);
        rvb.AddThemeConstantOverride("separation", 8);

        rvb.AddChild(MakeLabel(TrManager.Tr("menu.chat"), 18, ColGold));
        _mpChatBox = new VBoxContainer();
        _mpChatBox!.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _mpChatBox!.AddThemeConstantOverride("separation", 2);
        rvb.AddChild(_mpChatBox!);

        _mpChatInput = new LineEdit { CustomMinimumSize = new Vector2(0, 36) };
        _mpChatInput!.AddThemeFontSizeOverride("font_size", 14);
        _mpChatInput!.PlaceholderText = TrManager.Tr("menu.mp_chat_placeholder");
        _mpChatInput!.TextSubmitted += (text) =>
        {
            if (!string.IsNullOrEmpty(text))
            {
                NetworkManager.SendChat(text);
                _mpChatInput!.Text = "";
            }
        };
        rvb.AddChild(_mpChatInput!);

        // 底部操作栏
        var bottomPanel = MakePanel(20, 920, 1880, 140);
        _pageRoot!.AddChild(bottomPanel);
        var bottomHb = new HBoxContainer();
        bottomHb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bottomHb.Alignment = BoxContainer.AlignmentMode.Center;
        bottomHb.AddThemeConstantOverride("separation", 24);
        bottomPanel.AddChild(bottomHb);

        var leaveBtn = MakeGrayButton(TrManager.Tr("menu.leave_room"), 16);
        leaveBtn.CustomMinimumSize = new Vector2(160, 44);
        leaveBtn.Pressed += () =>
        {
            NetworkManager.Disconnect();
            ShowMultiplayerPage();
        };
        bottomHb.AddChild(leaveBtn);

        if (isHost)
        {
            // 地图设置已在Skirmish中选好，这里使用GameSession的值
            var startBtn = MakeRedButton(TrManager.Tr("menu.mp_start_game"), 22, 280, 44);
            startBtn.Pressed += () =>
            {
                // 检查所有真人玩家是否准备
                if (!NetworkManager._fillWithAI)
                {
                    foreach (var p in NetworkManager.Players.Values)
                        if (!p.IsAI && !p.IsReady)
                        {
                            ShowMpStatus(TrManager.Tr("menu.mp_status_not_ready"));
                            return;
                        }
                }

                ulong seed = GameSession.MapSeed == 0 ? (ulong)DateTime.Now.Ticks : GameSession.MapSeed;
                NetworkManager.HostStartGame(seed, GameSession.SelectedDifficulty,
                    GameSession.SelectedMapSize, GameSession.SelectedMapTheme);
                // Host自己进入游戏
                GameSession.IsMultiplayer = true;
                CallDeferred(nameof(ChangeToGameScene));
            };
            bottomHb.AddChild(startBtn);

            // 地图设置快捷入口
            var mapBtn = MakeGrayButton(TrManager.Tr("menu.map_settings"), 14);
            mapBtn.CustomMinimumSize = new Vector2(160, 44);
            mapBtn.Pressed += () => ShowSkirmishPage();
            bottomHb.AddChild(mapBtn);
        }
        else
        {
            var readyBtn = MakeRedButton(TrManager.Tr("menu.ready_toggle"), 18, 280, 44);
            readyBtn.Pressed += () => { NetworkManager.ToggleReady(); };
            bottomHb.AddChild(readyBtn);
        }
    }

    private void OnLobbyChanged()
    {
        if (_mpPlayerListLabel != null)
            RefreshPlayerList();
    }

    private void RefreshPlayerList()
    {
        if (_mpPlayerListLabel == null) return;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < NetworkManager.Room.MaxPlayers; i++)
        {
            NetworkManager.PlayerSlot? slot = null;
            foreach (var p in NetworkManager.Players.Values)
                if (p.TeamId == i) { slot = p; break; }
            if (slot != null)
            {
                string tag = slot.IsHost ? TrManager.Tr("menu.host_tag") : slot.IsAI ? "[AI] " : "";
                string ready = slot.IsReady ? " ✓" : " ✗";
                string colorName = ((GameData.TeamPalette[i % GameData.TeamPalette.Length]).ToHtml());
                sb.AppendLine($"[color={colorName}]{TrManager.Tr("menu.faction_label")}{i}[/color]  {tag}{slot.Name} ({slot.Faction}){ready}");
            }
            else
            {
                sb.AppendLine($"{TrManager.Tr("menu.faction_label")}{i}  [{TrManager.Tr("menu.empty_slot")}]");
            }
        }
        _mpPlayerListLabel!.Text = sb.ToString();
    }

    private void OnGameStarted()
    {
        GameSession.IsMultiplayer = true;
        CallDeferred(nameof(ChangeToGameScene));
    }

    private void OnNetDisconnected(string reason)
    {
        CallDeferred(nameof(OnNetDisconnectedDeferred), reason);
    }

    private void OnNetDisconnectedDeferred(string reason)
    {
        ShowMpStatus(TrManager.Tr("menu.mp_disconnected") + reason);
        ShowMultiplayerPage();
    }

    private void OnChatReceived(string sender, string message)
    {
        if (_mpChatBox != null && IsInstanceValid(_mpChatBox!))
        {
            var lbl = MakeLabel($"{sender}: {message}", 12, ColTextMain);
            _mpChatBox!.AddChild(lbl);
            // 保持最多20条
            while (_mpChatBox!.GetChildCount() > 20)
                _mpChatBox!.GetChild(0).QueueFree();
        }
    }

    public override void _Process(double delta)
    {
        if (NetworkManager.IsOnline)
            NetworkManager.Poll((float)delta);
    }

    private void ChangeToGameScene()
    {
        GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
    }
}
