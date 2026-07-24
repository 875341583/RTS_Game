using Godot;

namespace RTSGame;

/// <summary>
/// 主菜单：难度选择界面。点击难度按钮后进入游戏。
/// 全程序化构建 UI（与 BuildPanel 风格一致）。
/// </summary>
public partial class MainMenu : Control
{
    private LineEdit _seedInput = null!;

    public override void _Ready()
    {
        // 有 --difficulty 参数时直接进入游戏（headless 自动化测试 + 可视化验收通用）
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
                GD.Print($"[MainMenu] 自动进入游戏 (难度 {GameSession.SelectedDifficulty}, 种子 {GameSession.MapSeed}, mode={DisplayServer.GetName()})");
                CallDeferred(nameof(ChangeToGameScene));
                return;
            }
        }

        // 全屏深色背景
        var bg = new ColorRect();
        bg.Color = new Color(0.06f, 0.09f, 0.08f, 1f);
        bg.AnchorLeft = 0; bg.AnchorTop = 0; bg.AnchorRight = 1; bg.AnchorBottom = 1;
        AddChild(bg);

        // 暗绿网格背景（和游戏内一致）
        var grid = new Line2D();
        grid.Width = 1f;
        grid.DefaultColor = new Color(0.12f, 0.2f, 0.14f, 0.4f);
        var pts = new System.Collections.Generic.List<Vector2>();
        for (int x = 0; x <= 1280; x += 80) { pts.Add(new Vector2(x, 0)); pts.Add(new Vector2(x, 720)); }
        for (int y = 0; y <= 720; y += 80) { pts.Add(new Vector2(0, y)); pts.Add(new Vector2(1280, y)); }
        grid.Points = pts.ToArray();
        AddChild(grid);

        // 居中容器
        var center = new CenterContainer();
        center.AnchorLeft = 0; center.AnchorTop = 0; center.AnchorRight = 1; center.AnchorBottom = 1;
        AddChild(center);

        var vbox = new VBoxContainer();
        vbox.CustomMinimumSize = new Vector2(520, 0);
        vbox.AddThemeConstantOverride("separation", 16);
        center.AddChild(vbox);

        // 主标题
        var title = MakeLabel("RTS 红警复刻", 36, Colors.White);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        var subtitle = MakeLabel("选择难度开始游戏", 18, new Color(0.7f, 0.75f, 0.7f));
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(subtitle);

        var spacer1 = new Control { CustomMinimumSize = new Vector2(0, 12) };
        vbox.AddChild(spacer1);

        // 四个难度按钮
        AddDifficultyButton(vbox, "Easy - 新手", "AI 14s · 蓝方 $3000 · 仅兵营 · 上限12", Main.Difficulty.Easy, new Color(0.3f, 0.8f, 0.4f));
        AddDifficultyButton(vbox, "Normal - 标准", "AI 10s · 蓝方 $2700 · 车厂 · 上限16", Main.Difficulty.Normal, new Color(0.4f, 0.7f, 1f));
        AddDifficultyButton(vbox, "Hard - 困难", "AI 7s · 蓝方 $2500 · 科技中心 · 上限20", Main.Difficulty.Hard, new Color(1f, 0.7f, 0.2f));
        AddDifficultyButton(vbox, "Brutal - 残酷", "AI 4s · 蓝方 $2200 · 科技中心 · 上限24", Main.Difficulty.Brutal, new Color(1f, 0.3f, 0.3f));

        var spacer2 = new Control { CustomMinimumSize = new Vector2(0, 20) };
        vbox.AddChild(spacer2);

        // 种子输入框（文明6式：可输入种子复现地图，留空=随机）
        var seedRow = new HBoxContainer();
        seedRow.AddThemeConstantOverride("separation", 8);
        var seedLabel = MakeLabel("地图种子:", 14, new Color(0.6f, 0.65f, 0.6f));
        seedRow.AddChild(seedLabel);
        var seedInput = new LineEdit();
        _seedInput = seedInput;
        seedInput.CustomMinimumSize = new Vector2(200, 30);
        seedInput.PlaceholderText = "留空=随机种子";
        seedInput.AddThemeFontSizeOverride("font_size", 14);
        seedRow.AddChild(seedInput);
        vbox.AddChild(seedRow);

        var seedHint = MakeLabel("输入相同种子可复现同一张地图（类似文明6）", 11, new Color(0.45f, 0.5f, 0.45f));
        seedHint.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(seedHint);

        var spacer2b = new Control { CustomMinimumSize = new Vector2(0, 12) };
        vbox.AddChild(spacer2b);

        // 操作说明
        var hint = MakeLabel(
            "操作：左键拖框选单位 | 右键移动/攻击 | WASD 移动相机\n" +
            "建造：侧边栏点击图标 → 左键放置建筑\n" +
            "快捷：Q攻击移动 | X停止 | R维修 | V出售 | Ctrl+1~9编队",
            13, new Color(0.55f, 0.6f, 0.55f));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(hint);

        var spacer3 = new Control { CustomMinimumSize = new Vector2(0, 8) };
        vbox.AddChild(spacer3);

        // 3D原型预览按钮
        var protoBtn = new Button();
        protoBtn.Text = "3D 2.5D 原型预览";
        protoBtn.CustomMinimumSize = new Vector2(0, 40);
        protoBtn.AddThemeFontSizeOverride("font_size", 18);
        protoBtn.Pressed += () =>
        {
            GD.Print("[MainMenu] 进入3D原型预览");
            GetTree().ChangeSceneToFile("res://scenes/Prototype3D.tscn");
        };
        vbox.AddChild(protoBtn);

        // 3D正式游戏按钮
        var game3DBtn = new Button();
        game3DBtn.Text = "3D 全3D模式 (昼夜+灾害)";
        game3DBtn.CustomMinimumSize = new Vector2(0, 50);
        game3DBtn.AddThemeFontSizeOverride("font_size", 20);
        game3DBtn.Pressed += () =>
        {
            GD.Print("[MainMenu] 进入3D正式游戏");
            GetTree().ChangeSceneToFile("res://scenes/Main3D.tscn");
        };
        vbox.AddChild(game3DBtn);

        var spacer4 = new Control { CustomMinimumSize = new Vector2(0, 8) };
        vbox.AddChild(spacer4);

        // 退出按钮
        var exitBtn = new Button();
        exitBtn.Text = "退出游戏";
        exitBtn.CustomMinimumSize = new Vector2(0, 36);
        exitBtn.Pressed += () => GetTree().Quit();
        vbox.AddChild(exitBtn);

        GD.Print("[MainMenu] 主菜单已加载");
    }

    private void ChangeToGameScene()
    {
        GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
    }

    private void AddDifficultyButton(Container parent, string title, string desc,
        Main.Difficulty diff, Color accent)
    {
        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(0, 64);
        parent.AddChild(panel);

        var vb = new VBoxContainer();
        vb.AnchorLeft = 0; vb.AnchorTop = 0; vb.AnchorRight = 1; vb.AnchorBottom = 1;
        vb.OffsetLeft = 16; vb.OffsetTop = 6; vb.OffsetRight = -16; vb.OffsetBottom = -6;
        vb.AddThemeConstantOverride("separation", 2);
        panel.AddChild(vb);

        var titleLbl = MakeLabel(title, 19, accent);
        vb.AddChild(titleLbl);
        var descLbl = MakeLabel(desc, 13, new Color(0.6f, 0.6f, 0.62f));
        vb.AddChild(descLbl);

        // 整个 panel 可点击
        panel.GuiInput += (@event) =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                GameSession.SelectedDifficulty = diff;
                // 读取种子输入框
                string seedText = _seedInput.Text.Trim();
                if (!string.IsNullOrEmpty(seedText) && ulong.TryParse(seedText, out var parsedSeed))
                    GameSession.MapSeed = parsedSeed;
                else
                    GameSession.MapSeed = 0; // 0=随机
                GD.Print($"[MainMenu] 选择难度: {diff}，种子: {GameSession.MapSeed}，进入游戏");
                CallDeferred(nameof(ChangeToGameScene));
            }
        };
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
