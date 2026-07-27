using Godot;
using System.Collections.Generic;

namespace RTSGame;

/// <summary>
/// P2-3: BGM管理器 — 支持多首背景音乐自动切换。
/// 场景：菜单 → 战斗 → 胜利/失败，不同阶段播放不同BGM。
/// 音频文件缺失时静默跳过（优雅降级）。
///
/// BGM曲目规划：
///   - Menu:    菜单/待机 — 缓慢军事进行曲
///   - Battle:  战斗中   — 快节奏军事行军
///   - Alert:   被攻击   — 紧急警报进行曲
///   - Victory: 胜利     — 凯旋曲
///   - Defeat:  失败     — 沉重低音曲
/// </summary>
public static class BgmManager
{
    /// <summary>BGM场景类型。</summary>
    public enum BgmScene
    {
        Menu,       // 菜单/待机
        Battle,     // 战斗进行中
        Alert,      // 被攻击/紧急状态
        Victory,    // 胜利
        Defeat,     // 失败
    }

    private static readonly Dictionary<BgmScene, string> _bgmPaths = new()
    {
        [BgmScene.Menu]    = "res://assets/sounds/bgm_menu.wav",
        [BgmScene.Battle]  = "res://assets/sounds/bgm_march.wav",
        [BgmScene.Alert]   = "res://assets/sounds/bgm_alert.wav",
        [BgmScene.Victory] = "res://assets/sounds/bgm_victory.wav",
        [BgmScene.Defeat]  = "res://assets/sounds/bgm_defeat.wav",
    };

    private static BgmScene _currentScene = BgmScene.Menu;
    private static AudioStreamPlayer? _player;

    /// <summary>当前播放场景。</summary>
    public static BgmScene CurrentScene => _currentScene;

    /// <summary>初始化BGM播放器（由AudioManager._Ready调用）。</summary>
    public static void Initialize(AudioStreamPlayer bgmPlayer)
    {
        _player = bgmPlayer;
    }

    /// <summary>切换BGM场景。</summary>
    public static void SwitchScene(BgmScene scene)
    {
        if (scene == _currentScene && _player != null && _player.Playing) return;

        _currentScene = scene;
        PlayCurrent();
    }

    /// <summary>播放当前场景的BGM。</summary>
    public static void PlayCurrent()
    {
        if (_player == null) return;

        if (!_bgmPaths.TryGetValue(_currentScene, out var path)) return;

        // 尝试加载，文件不存在时静默跳过
        var stream = GD.Load<AudioStream>(path);
        if (stream == null)
        {
            GameLog.Debug($"[BgmManager] BGM文件不存在: {path}，跳过");
            return;
        }

        _player.Stream = stream;
        _player.VolumeDb = Mathf.LinearToDb(0.35f);
        _player.Play();
        GameLog.Debug($"[BgmManager] 切换BGM: {_currentScene}");
    }

    /// <summary>停止BGM。</summary>
    public static void Stop()
    {
        _player?.Stop();
    }
}
