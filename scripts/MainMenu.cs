using Godot;
using System.Collections.Generic;

namespace RTSGame;

/// <summary>
/// 主菜单 — 红警2风格分页流程：
/// 主菜单页 → 遭遇战 → 选图/阵营/难度 → 开始
///          → 设置 → 画质/音量/分辨率 → 返回
/// 全程序化构建 UI（军工风深色主题）。
/// </summary>
public partial class MainMenu : Control
{
    // 页面容器
    private VBoxContainer _pageContainer = null!;
    private LineEdit _seedInput = null!;

    // 设置项状态
    private QualitySettings.QualityLevel _settingsQuality = QualitySettings.QualityLevel.High;
    private float _settingsVolume = 1.0f;
    private bool _settingsFullscreen = true;

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

        // 加载阵营数据（用于遭遇战页面）
        FactionManager.Load();

        // 初始化设置状态
        _settingsQuality = QualitySettings.Current;
        _settingsFullscreen = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen;

        BuildBackground();
        BuildPageContainer();
        ShowMainMenu();
        GameLog.Info("[MainMenu] 主菜单已加载");
    }

    // ==================== 背景与容器 ====================

    private void BuildBackground()
    {
        // 全屏深色背景
        var bg = new ColorRect();
        bg.Color = new Color(0.06f, 0.09f, 0.08f, 1f);
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        // 暗绿网格背景
        var grid = new Line2D();
        grid.Width = 1f;
        grid.DefaultColor = new Color(0.12f, 0.2f, 0.14f, 0.4f);
        var pts = new List<Vector2>();
        for (int x = 0; x <= 1920; x += 80) { pts.Add(new Vector2(x, 0)); pts.Add(new Vector2(x, 1080)); }
        for (int y = 0; y <= 1080; y += 80) { pts.Add(new Vector2(0, y)); pts.Add(new Vector2(1920, y)); }
        grid.Points = pts.ToArray();
        AddChild(grid);
    }

    private void BuildPageContainer()
    {
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        _pageContainer = new VBoxContainer();
        _pageContainer.CustomMinimumSize = new Vector2(640, 0);
        _pageContainer.AddThemeConstantOverride("separation", 12);
        center.AddChild(_pageContainer);
    }

    /// <summary>清除当前页面内容，准备渲染新页面。</summary>
    private void ClearPage()
    {
        foreach (var child in _pageContainer.GetChildren())
            child.QueueFree();
    }

    // ==================== 主菜单页 ====================

    private void ShowMainMenu()
    {
        ClearPage();

        // 主标题
        var title = MakeLabel("铁幕突袭", 42, Colors.White);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(title);

        var subtitle = MakeLabel("Iron Curtain RTS", 16, new Color(0.5f, 0.6f, 0.5f));
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(subtitle);

        _pageContainer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 24) });

        // 遭遇战
        AddMenuButton("遭遇战", "选择地图、阵营和难度，与AI对战", new Color(0.4f, 0.7f, 1f), () => ShowSkirmishPage());

        // 设置
        AddMenuButton("设置", "画质、音量、分辨率、全屏", new Color(0.7f, 0.7f, 0.7f), () => ShowSettingsPage());

        // 地图编辑器
        AddMenuButton("地图编辑器", "创建和编辑自定义地图", new Color(0.6f, 0.8f, 0.5f), () =>
        {
            GameLog.Info("[MainMenu] 进入地图编辑器");
            GetTree().ChangeSceneToFile("res://scenes/MapEditor.tscn");
        });

        // 3D原型预览
        AddMenuButton("3D 原型预览", "2.5D/全3D模式实验", new Color(0.8f, 0.6f, 1f), () =>
        {
            GameLog.Info("[MainMenu] 进入3D原型预览");
            GetTree().ChangeSceneToFile("res://scenes/Prototype3D.tscn");
        });

        _pageContainer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        // 退出
        var exitBtn = new Button();
        exitBtn.Text = "退出游戏";
        exitBtn.CustomMinimumSize = new Vector2(0, 40);
        exitBtn.AddThemeFontSizeOverride("font_size", 18);
        exitBtn.Pressed += () => GetTree().Quit();
        _pageContainer.AddChild(exitBtn);
    }

    // ==================== 遭遇战页面 ====================

    private void ShowSkirmishPage()
    {
        ClearPage();

        var title = MakeLabel("遭遇战", 30, new Color(0.4f, 0.7f, 1f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(title);

        _pageContainer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        // --- 地图设置 ---
        var section1 = MakeLabel("── 地图设置 ──", 16, new Color(0.6f, 0.65f, 0.6f));
        section1.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(section1);

        // 地图尺寸
        var mapSizeRow = MakeRowWithLabel("地图尺寸:");
        var mapSizeOptions = new (string Label, MapConfig.SizePreset Preset)[]
        {
            ("小 (32×32)", MapConfig.SizePreset.Small),
            ("中 (64×64)", MapConfig.SizePreset.Medium),
            ("大 (96×96)", MapConfig.SizePreset.Large),
        };
        foreach (var opt in mapSizeOptions)
        {
            var btn = MakeChoiceButton(opt.Label);
            btn.Pressed += () =>
            {
                GameSession.SelectedMapSize = opt.Preset;
                GameLog.Debug($"[MainMenu] 地图尺寸: {opt.Label}");
            };
            mapSizeRow.AddChild(btn);
        }
        _pageContainer.AddChild(mapSizeRow);

        // 地图主题
        var themeRow = MakeRowWithLabel("地图主题:");
        var themeOptions = new (string Label, MapConfig.MapTheme Theme)[]
        {
            ("默认", MapConfig.MapTheme.Default),
            ("雪地", MapConfig.MapTheme.Snow),
            ("沙漠", MapConfig.MapTheme.Desert),
            ("城市", MapConfig.MapTheme.City),
            ("海岛", MapConfig.MapTheme.Island),
        };
        foreach (var opt in themeOptions)
        {
            var btn = MakeChoiceButton(opt.Label);
            btn.Pressed += () =>
            {
                GameSession.SelectedMapTheme = opt.Theme;
                GameLog.Debug($"[MainMenu] 地图主题: {opt.Label}");
            };
            themeRow.AddChild(btn);
        }
        _pageContainer.AddChild(themeRow);

        // 种子
        var seedRow = MakeRowWithLabel("地图种子:");
        _seedInput = new LineEdit();
        _seedInput.CustomMinimumSize = new Vector2(240, 32);
        _seedInput.PlaceholderText = "留空 = 随机种子";
        _seedInput.AddThemeFontSizeOverride("font_size", 14);
        seedRow.AddChild(_seedInput);
        _pageContainer.AddChild(seedRow);

        var seedHint = MakeLabel("相同种子可复现同一张地图", 11, new Color(0.45f, 0.5f, 0.45f));
        seedHint.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(seedHint);

        _pageContainer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        // --- 阵营选择 ---
        var section2 = MakeLabel("── 阵营选择 ──", 16, new Color(0.6f, 0.65f, 0.6f));
        section2.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(section2);

        var factions = FactionManager.GetAllFactions();
        var factionRow = MakeRowWithLabel("玩家阵营:");
        foreach (var fac in factions)
        {
            var btn = MakeChoiceButton(fac.Name);
            btn.AddThemeColorOverride("font_color", fac.Color);
            btn.AddThemeColorOverride("font_hover_color", fac.Color.Lightened(0.3f));
            btn.Pressed += () =>
            {
                GameSession.PlayerFactionId = fac.Id;
                GameLog.Debug($"[MainMenu] 阵营: {fac.Name} ({fac.Id})");
            };
            factionRow.AddChild(btn);
        }
        _pageContainer.AddChild(factionRow);

        _pageContainer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        // --- 难度选择 ---
        var section3 = MakeLabel("── 难度选择 ──", 16, new Color(0.6f, 0.65f, 0.6f));
        section3.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(section3);

        AddDifficultyCard("Easy - 新手", "AI 14s · 蓝方 $3000 · 仅兵营 · 上限12", Main.Difficulty.Easy, new Color(0.3f, 0.8f, 0.4f));
        AddDifficultyCard("Normal - 标准", "AI 10s · 蓝方 $2700 · 车厂 · 上限16", Main.Difficulty.Normal, new Color(0.4f, 0.7f, 1f));
        AddDifficultyCard("Hard - 困难", "AI 7s · 蓝方 $2500 · 科技中心 · 上限20", Main.Difficulty.Hard, new Color(1f, 0.7f, 0.2f));
        AddDifficultyCard("Brutal - 残酷", "AI 4s · 蓝方 $2200 · 科技中心 · 上限24", Main.Difficulty.Brutal, new Color(1f, 0.3f, 0.3f));

        _pageContainer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        // 开始游戏 + 返回
        var actionRow = new HBoxContainer();
        actionRow.AddThemeConstantOverride("separation", 12);
        actionRow.Alignment = BoxContainer.AlignmentMode.Center;

        var backBtn = new Button();
        backBtn.Text = "← 返回";
        backBtn.CustomMinimumSize = new Vector2(140, 40);
        backBtn.AddThemeFontSizeOverride("font_size", 16);
        backBtn.Pressed += () => ShowMainMenu();
        actionRow.AddChild(backBtn);

        var startBtn = new Button();
        startBtn.Text = "开始游戏 ▶";
        startBtn.CustomMinimumSize = new Vector2(200, 40);
        startBtn.AddThemeFontSizeOverride("font_size", 18);
        startBtn.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
        startBtn.Pressed += () =>
        {
            // 读取种子
            string seedText = _seedInput != null ? _seedInput.Text.Trim() : "";
            if (!string.IsNullOrEmpty(seedText) && ulong.TryParse(seedText, out var parsedSeed))
                GameSession.MapSeed = parsedSeed;
            else
                GameSession.MapSeed = 0;
            GameLog.Info($"[MainMenu] 开始遭遇战 — 难度: {GameSession.SelectedDifficulty}, 种子: {GameSession.MapSeed}, 尺寸: {GameSession.SelectedMapSize}, 主题: {GameSession.SelectedMapTheme}, 阵营: {GameSession.PlayerFactionId}");
            CallDeferred(nameof(ChangeToGameScene));
        };
        actionRow.AddChild(startBtn);

        _pageContainer.AddChild(actionRow);
    }

    // ==================== 设置页面 ====================

    private void ShowSettingsPage()
    {
        ClearPage();

        var title = MakeLabel("设置", 30, new Color(0.7f, 0.7f, 0.7f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(title);

        _pageContainer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        // --- 画质 ---
        var section1 = MakeLabel("── 画质 ──", 16, new Color(0.6f, 0.65f, 0.6f));
        section1.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(section1);

        var qualityRow = MakeRowWithLabel("画质等级:");
        var qualityOptions = new (string Label, QualitySettings.QualityLevel Level)[]
        {
            ("低 (省电)", QualitySettings.QualityLevel.Low),
            ("中", QualitySettings.QualityLevel.Medium),
            ("高", QualitySettings.QualityLevel.High),
        };
        foreach (var opt in qualityOptions)
        {
            var btn = MakeChoiceButton(opt.Label);
            if (_settingsQuality == opt.Level)
            {
                btn.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.4f));
            }
            btn.Pressed += () =>
            {
                _settingsQuality = opt.Level;
                QualitySettings.SetQuality(opt.Level);
                ShowSettingsPage(); // 刷新高亮
            };
            qualityRow.AddChild(btn);
        }
        _pageContainer.AddChild(qualityRow);

        var qualityHint = MakeLabel($"当前: {QualitySettings.LevelName} — GPU: {(QualitySettings.HasGPU ? "检测到" : "未检测到(软件渲染)")}", 11, new Color(0.45f, 0.5f, 0.45f));
        qualityHint.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(qualityHint);

        _pageContainer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        // --- 显示 ---
        var section2 = MakeLabel("── 显示 ──", 16, new Color(0.6f, 0.65f, 0.6f));
        section2.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(section2);

        // 全屏切换
        var fullscreenRow = MakeRowWithLabel("全屏模式:");
        var fsBtn = MakeChoiceButton(_settingsFullscreen ? "开启 ✓" : "关闭");
        fsBtn.Pressed += () =>
        {
            _settingsFullscreen = !_settingsFullscreen;
            if (_settingsFullscreen)
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
            }
            else
            {
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            }
            ShowSettingsPage();
        };
        fullscreenRow.AddChild(fsBtn);
        _pageContainer.AddChild(fullscreenRow);

        // F11 提示
        var fsHint = MakeLabel("游戏中按 F11 切换全屏/窗口", 11, new Color(0.45f, 0.5f, 0.45f));
        fsHint.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(fsHint);

        _pageContainer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        // --- 音量 ---
        var section3 = MakeLabel("── 音频 ──", 16, new Color(0.6f, 0.65f, 0.6f));
        section3.HorizontalAlignment = HorizontalAlignment.Center;
        _pageContainer.AddChild(section3);

        var volumeRow = MakeRowWithLabel("主音量:");
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
        volumeRow.AddChild(slider);

        var volLabel = MakeLabel($"{(int)(_settingsVolume * 100)}%", 13, new Color(0.6f, 0.65f, 0.6f));
        volumeRow.AddChild(volLabel);
        _pageContainer.AddChild(volumeRow);

        _pageContainer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        // --- 语言 ---
        var sectionLang = MakeLabel("── 语言 / Language ──", 16, new Color(0.6f, 0.65f, 0.6f));
        _pageContainer.AddChild(sectionLang);

        var langRow = MakeRowWithLabel("界面语言:");
        var langOptions = new (string Label, string Code)[]
        {
            ("中文", "zh-CN"),
            ("English", "en"),
        };
        foreach (var opt in langOptions)
        {
            var btn = new Button();
            btn.Text = opt.Label + (TrManager.CurrentLang == opt.Code ? " ✓" : "");
            btn.CustomMinimumSize = new Vector2(80, 32);
            btn.AddThemeFontSizeOverride("font_size", 14);
            btn.Pressed += () =>
            {
                TrManager.SetLanguage(opt.Code);
                ShowSettingsPage();
            };
            langRow.AddChild(btn);
        }
        _pageContainer.AddChild(langRow);

        var langHint = MakeLabel("P0修复: 语言切换（部分文本尚未完全国际化）", 11, new Color(0.45f, 0.5f, 0.45f));
        _pageContainer.AddChild(langHint);

        _pageContainer.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        // 返回按钮
        var backRow = new HBoxContainer();
        backRow.Alignment = BoxContainer.AlignmentMode.Center;
        var backBtn = new Button();
        backBtn.Text = "← 返回主菜单";
        backBtn.CustomMinimumSize = new Vector2(200, 40);
        backBtn.AddThemeFontSizeOverride("font_size", 16);
        backBtn.Pressed += () => ShowMainMenu();
        backRow.AddChild(backBtn);
        _pageContainer.AddChild(backRow);
    }

    // ==================== 辅助方法 ====================

    private void AddMenuButton(string title, string desc, Color accent, System.Action action)
    {
        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(0, 60);
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.1f, 0.14f, 0.11f, 0.9f);
        style.BorderWidthBottom = 2;
        style.BorderColor = new Color(0.2f, 0.3f, 0.22f);
        style.ContentMarginLeft = 16;
        style.ContentMarginRight = 16;
        style.ContentMarginTop = 8;
        style.ContentMarginBottom = 8;
        panel.AddThemeStyleboxOverride("panel", style);
        _pageContainer.AddChild(panel);

        var vb = new VBoxContainer();
        vb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vb.OffsetLeft = 16; vb.OffsetTop = 8; vb.OffsetRight = -16; vb.OffsetBottom = -8;
        vb.AddThemeConstantOverride("separation", 2);
        panel.AddChild(vb);

        vb.AddChild(MakeLabel(title, 19, accent));
        vb.AddChild(MakeLabel(desc, 12, new Color(0.55f, 0.58f, 0.55f)));

        panel.GuiInput += (@event) =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                action();
        };
    }

    private void AddDifficultyCard(string title, string desc, Main.Difficulty diff, Color accent)
    {
        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(0, 56);
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.1f, 0.14f, 0.11f, 0.9f);
        style.BorderWidthBottom = 2;
        style.BorderColor = accent.Darkened(0.5f);
        style.ContentMarginLeft = 16;
        style.ContentMarginRight = 16;
        style.ContentMarginTop = 6;
        style.ContentMarginBottom = 6;
        panel.AddThemeStyleboxOverride("panel", style);
        _pageContainer.AddChild(panel);

        var vb = new VBoxContainer();
        vb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vb.OffsetLeft = 16; vb.OffsetTop = 6; vb.OffsetRight = -16; vb.OffsetBottom = -6;
        vb.AddThemeConstantOverride("separation", 2);
        panel.AddChild(vb);

        vb.AddChild(MakeLabel(title, 18, accent));
        vb.AddChild(MakeLabel(desc, 12, new Color(0.55f, 0.58f, 0.55f)));

        // 高亮已选难度
        if (GameSession.SelectedDifficulty == diff)
        {
            style.BorderColor = accent;
            style.BorderWidthTop = 2;
            style.BorderWidthLeft = 2;
            style.BorderWidthRight = 2;
        }

        panel.GuiInput += (@event) =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                GameSession.SelectedDifficulty = diff;
                ShowSkirmishPage(); // 刷新高亮
            }
        };
    }

    private HBoxContainer MakeRowWithLabel(string labelText)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(MakeLabel(labelText, 14, new Color(0.6f, 0.65f, 0.6f)));
        return row;
    }

    private Button MakeChoiceButton(string text)
    {
        var btn = new Button();
        btn.Text = text;
        btn.AddThemeFontSizeOverride("font_size", 13);
        btn.CustomMinimumSize = new Vector2(0, 30);
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

    private void ChangeToGameScene()
    {
        GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
    }
}
