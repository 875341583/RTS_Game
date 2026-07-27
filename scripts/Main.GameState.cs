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

    /// <summary>P5：应用难度配置到游戏参数。</summary>
    private void ApplyDifficultyConfig()
    {
        switch (_difficulty)
        {
            case Difficulty.Easy:
                _aiThinkInterval = 14f; _aiStartMoney = 1500; _blueStartMoney = 3000;
                _aiStartHarvesters = 2; _aiUsesTech = false; _aiCapturesPoints = false;
                StrategicPointIncomeEnabled = false; _unitCap = 12; _playerTechLevel = 1;
                Unit.AiGraceRemaining = 120f; // Easy: 2分钟保护期
                _activeAiCount = 2; // Easy: 仅2个活跃AI，其余5个休眠
                break;
            case Difficulty.Normal:
                _aiThinkInterval = 10f; _aiStartMoney = 1800; _blueStartMoney = 2700;
                _aiStartHarvesters = 3; _aiUsesTech = true; _aiCapturesPoints = true;
                StrategicPointIncomeEnabled = true; _unitCap = 16; _playerTechLevel = 3; // v5修复：Lv2→Lv3，解锁科技中心
                Unit.AiGraceRemaining = 60f; // Normal: 60秒保护期
                _activeAiCount = 4; // v5修复：Normal难度活跃AI 7→4，3个AI休眠，缓解1v7压力
                break;
            case Difficulty.Hard:
                _aiThinkInterval = 7f; _aiStartMoney = 2200; _blueStartMoney = 2500;
                _aiStartHarvesters = 3; _aiUsesTech = true; _aiCapturesPoints = true;
                StrategicPointIncomeEnabled = true; _unitCap = 20; _playerTechLevel = 3;
                Unit.AiGraceRemaining = 30f; // Hard: 30秒保护期
                _activeAiCount = 6; // Hard: 6个活跃AI，1个休眠
                break;
            case Difficulty.Brutal:
                _aiThinkInterval = 4f; _aiStartMoney = 3000; _blueStartMoney = 2200;
                _aiStartHarvesters = 4; _aiUsesTech = true; _aiCapturesPoints = true;
                StrategicPointIncomeEnabled = true; _unitCap = 24; _playerTechLevel = 3;
                Unit.AiGraceRemaining = 0f; // Brutal: 无保护期，开局即战
                _activeAiCount = 7; // Brutal: 全部7个AI活跃，极限挑战
                break;
        }
        _enemyThinkTimer = _aiThinkInterval;
        _money[0] = _blueStartMoney;
        for (int t = 1; t <= AiTeamCount; t++)
            _money[t] = _aiStartMoney;
        GameLog.Debug($"[Difficulty] {_difficulty} | AI间隔 {_aiThinkInterval}s | 玩家方${_blueStartMoney} AI${_aiStartMoney}(x7) | 科技等级Lv{_playerTechLevel} | 上限{_unitCap} | 战略点收入{StrategicPointIncomeEnabled} | 活跃AI {_activeAiCount}/7 (休眠 {AiTeamCount - _activeAiCount} 个)");
    }

    private void CheckWinCondition()
    {
        if (_gameOver) return;
        int playerUnits = CountUnitsOfTeam(PlayerTeamId);
        int playerBuildings = CountBuildingsOfTeam(PlayerTeamId);

        // 玩家方全灭 = 失败
        if (playerBuildings == 0 && playerUnits == 0)
        {
            _gameOver = true;
            _gameResult = "失败！你的基地被摧毁了。";
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
            _gameResult = "胜利！所有敌方阵营已被全部消灭。";
            _gameOverDelay = 2f;
        }
    }

    // ---------- G5 游戏结束 UI ----------
    private void ShowGameOverUI()
    {
        // 阶段12-C：游戏结束音效
        bool win = _gameResult.StartsWith("胜利");
        _audio?.PlaySfxForce(win ? AudioManager.Sfx.NotifyVictory : AudioManager.Sfx.NotifyDefeat);

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
        diffLabel.Text = $"难度：{_difficulty}";
        diffLabel.AddThemeFontSizeOverride("font_size", 18);
        diffLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        diffLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(diffLabel);

        var spacer = new Control { CustomMinimumSize = new Vector2(0, 12) };
        vbox.AddChild(spacer);

        var restartBtn = new Button();
        restartBtn.Text = "重新开始（同难度）";
        restartBtn.CustomMinimumSize = new Vector2(0, 44);
        restartBtn.Pressed += () => CallDeferred(nameof(RestartGame));
        vbox.AddChild(restartBtn);

        var menuBtn = new Button();
        menuBtn.Text = "返回主菜单";
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
