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
///
/// 补强：BGM切换时0.5秒淡出旧曲 + 0.5秒淡入新曲（Tween实现，不阻塞主循环）。
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
    private static float _bgmVolume = 0.35f;
    private const float FadeDuration = 0.5f;

    /// <summary>是否正在淡出中（防止重复触发）。</summary>
    private static bool _fadingOut;

    /// <summary>待切换的目标场景（淡出完成后播放）。</summary>
    private static BgmScene? _pendingScene;

    /// <summary>当前播放场景。</summary>
    public static BgmScene CurrentScene => _currentScene;

    /// <summary>初始化BGM播放器（由AudioManager._Ready调用）。</summary>
    public static void Initialize(AudioStreamPlayer bgmPlayer)
    {
        _player = bgmPlayer;
    }

    /// <summary>切换BGM场景（带淡入淡出）。</summary>
    public static void SwitchScene(BgmScene scene)
    {
        if (scene == _currentScene && _player != null && _player.Playing && !_fadingOut) return;

        // 如果正在播放，先淡出再切换；否则直接播放新场景
        if (_player != null && _player.Playing && !_fadingOut)
        {
            _pendingScene = scene;
            FadeOutThenSwitch();
        }
        else
        {
            _currentScene = scene;
            _pendingScene = null;
            PlayCurrent();
        }
    }

    /// <summary>淡出当前BGM，完成后播放待切换场景。</summary>
    private static void FadeOutThenSwitch()
    {
        if (_player == null || _fadingOut) return;
        _fadingOut = true;

        // 使用Tween淡出音量→0→Stop
        var tween = _player.CreateTween();
        tween.TweenProperty(_player, "volume_db", -80f, FadeDuration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() =>
        {
            if (_player != null)
            {
                _player.Stop();
                // 恢复音量，准备下次播放
                _player.VolumeDb = Mathf.LinearToDb(_bgmVolume);
            }
            _fadingOut = false;

            // 播放待切换场景
            if (_pendingScene.HasValue)
            {
                _currentScene = _pendingScene.Value;
                _pendingScene = null;
                PlayCurrent();
            }
        }));
    }

    /// <summary>播放当前场景的BGM（带0.5秒淡入）。</summary>
    public static void PlayCurrent()
    {
        if (_player == null) return;

        if (!_bgmPaths.TryGetValue(_currentScene, out var path)) return;

        // 尝试加载，文件不存在或未导入时静默跳过
        if (!ResourceLoader.Exists(path, "AudioStream"))
        {
            GameLog.Debug($"[BgmManager] BGM文件未导入: {path}，跳过");
            return;
        }
        var stream = GD.Load<AudioStream>(path);
        if (stream == null)
        {
            GameLog.Debug($"[BgmManager] BGM加载失败: {path}，跳过");
            return;
        }

        _player.Stream = stream;
        // 从0音量开始淡入
        _player.VolumeDb = -80f;
        _player.Play();

        // 淡入动画
        var tween = _player.CreateTween();
        tween.TweenProperty(_player, "volume_db", Mathf.LinearToDb(_bgmVolume), FadeDuration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);

        GameLog.Debug($"[BgmManager] 切换BGM: {_currentScene}");
    }

    /// <summary>停止BGM（带淡出）。</summary>
    public static void Stop()
    {
        if (_player == null || !_player.Playing || _fadingOut) return;
        _pendingScene = null; // 停止后不再切换
        FadeOutThenSwitch();
    }
}
