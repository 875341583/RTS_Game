using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RTSGame;

/// <summary>
/// Main 的游戏状态控制器（partial class）。
/// 包含：难度配置应用 + 胜负判定 + 游戏结束UI + 重启/返回菜单。
/// </summary>
public partial class Main
{
    private bool _gameWon;

    /// <summary>P5：应用难度配置到游戏参数。P2-4: 从DifficultyConfig数据驱动加载。</summary>
    private void ApplyDifficultyConfig()
    {
        var dc = DifficultyConfig.Get(_difficulty.ToString());
        _aiThinkInterval = dc.AiThinkInterval;
        _aiStartMoney = dc.AiStartMoney;
        _blueStartMoney = dc.BlueStartMoney;
        _aiStartHarvesters = dc.AiStartHarvesters;
        _aiUsesTech = dc.AiUsesTech;
        _aiCapturesPoints = dc.AiCapturesPoints;
        StrategicPointIncomeEnabled = dc.StrategicPointIncomeEnabled;
        _unitCap = dc.UnitCap;
        _playerTechLevel = dc.PlayerTechLevel;
        Unit.AiGraceRemaining = dc.AiGraceRemaining;
        _activeAiCount = dc.ActiveAiCount;

        _enemyThinkTimer = _aiThinkInterval;
        _money[0] = _blueStartMoney;
        for (int t = 1; t <= AiTeamCount; t++)
            _money[t] = _aiStartMoney;
        GameLog.Debug($"[Difficulty] {_difficulty} | AI间隔 {_aiThinkInterval}s | 玩家方${_blueStartMoney} AI${_aiStartMoney}(x7) | 科技等级Lv{_playerTechLevel} | 上限{_unitCap} | 战略点收入{StrategicPointIncomeEnabled} | 活跃AI {_activeAiCount}/7 (休眠 {AiTeamCount - _activeAiCount} 个)");
    }

    private void CheckWinCondition()
    {
        if (_gameOver) return;

        // 联机模式：使用网络版胜负判定
        if (NetworkManager.IsOnline)
        {
            CheckWinConditionNet();
            return;
        }

        int playerUnits = CountUnitsOfTeam(PlayerTeamId);
        int playerBuildings = CountBuildingsOfTeam(PlayerTeamId);

        // 玩家方全灭 = 失败
        if (playerBuildings == 0 && playerUnits == 0)
        {
            _gameOver = true;
            _gameWon = false;
            _gameResult = TrManager.Tr("game_state.defeat");
            _gameOverDelay = 2f;
            return;
        }

        // 所有 AI 阵营全灭 = 胜利
        bool anyAiAlive = false;
        for (int t = 1; t <= AiTeamCount; t++)
        {
            if (CountUnitsOfTeam(t) > 0 || CountBuildingsOfTeam(t) > 0)
            {
                anyAiAlive = true;
                break;
            }
        }
        if (!anyAiAlive)
        {
            _gameOver = true;
            _gameWon = true;
            _gameResult = TrManager.Tr("game_state.victory");
            _gameOverDelay = 2f;
        }
    }

    // ---------- G5 游戏结束 UI ----------
    private void ShowGameOverUI()
    {
        // 阶段12-C：游戏结束音效 + P1-6: BGM切换
        bool win = _gameWon;
        _audio?.PlaySfxForce(win ? AudioManager.Sfx.NotifyVictory : AudioManager.Sfx.NotifyDefeat);
        BgmManager.SwitchScene(win ? BgmManager.BgmScene.Victory : BgmManager.BgmScene.Defeat);

        var layer = new CanvasLayer { Name = "GameOverUI" };
        AddChild(layer);

        var bg = new ColorRect();
        bg.Color = new Color(0, 0, 0, 0.75f);
        bg.AnchorLeft = 0; bg.AnchorTop = 0; bg.AnchorRight = 1; bg.AnchorBottom = 1;
        layer.AddChild(bg);

        var center = new CenterContainer();
        center.AnchorLeft = 0; center.AnchorTop = 0; center.AnchorRight = 1; center.AnchorBottom = 1;
        layer.AddChild(center);

        var vbox = new VBoxContainer();
        vbox.CustomMinimumSize = new Vector2(400, 0);
        vbox.AddThemeConstantOverride("separation", 20);
        center.AddChild(vbox);

        var title = new Label();
        title.Text = _gameResult;
        title.AddThemeFontSizeOverride("font_size", 32);
        title.AddThemeColorOverride("font_color", win ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        var diffLabel = new Label();
        diffLabel.Text = TrManager.Tr("game_state.difficulty_display", _difficulty);
        diffLabel.AddThemeFontSizeOverride("font_size", 18);
        diffLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        diffLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(diffLabel);

        var spacer = new Control { CustomMinimumSize = new Vector2(0, 12) };
        vbox.AddChild(spacer);

        var restartBtn = new Button();
        restartBtn.Text = TrManager.Tr("game_state.restart_same_difficulty");
        restartBtn.CustomMinimumSize = new Vector2(0, 44);
        restartBtn.Pressed += () => CallDeferred(nameof(RestartGame));
        vbox.AddChild(restartBtn);

        var menuBtn = new Button();
        menuBtn.Text = TrManager.Tr("game_state.return_to_menu");
        menuBtn.CustomMinimumSize = new Vector2(0, 44);
        menuBtn.Pressed += () => CallDeferred(nameof(ReturnToMenu));
        vbox.AddChild(menuBtn);

        GameLog.Debug($"[GameOver] {_gameResult} (难度 {_difficulty})");
    }

    private void RestartGame()
    {
        GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
    }

    private void ReturnToMenu()
    {
        GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
    }
}
