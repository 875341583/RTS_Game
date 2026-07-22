using Godot;
using System.Collections.Generic;

namespace RTSGame;

/// <summary>
/// 阶段12-C 音效管理器
/// 风格：红警2军事金属质感 + 街机短促冲击力
/// 统一管理所有 SFX 和 BGM 的播放，支持音量控制和节流
/// </summary>
public partial class AudioManager : Node
{
    // ---- 音效枚举 ----
    public enum Sfx
    {
        // UI
        UiClick, UiBuildStart, UiBuildDone, UiUnitReady, UiError, UiPlace,
        // 单位操控
        Select, Move,
        // 战斗
        Cannon, Muzzle, Hit, Explosion, BigExplosion, UnitDie,
        // 超武
        Nuke, Lightning,
        // 通知
        NotifyLowPower, NotifyAttack, NotifyVictory, NotifyDefeat,
    }

    // ---- 音量 ----
    public float SfxVolume { get; set; } = 0.7f;
    public float BgmVolume { get; set; } = 0.35f;
    public bool Muted { get; set; } = false;

    // ---- 内部资源 ----
    private AudioStreamPlayer _bgmPlayer = null!;
    private AudioStreamPlayer _sfxPlayer = null!;
    private AudioStreamPlayer _sfxPlayer2 = null!;  // 第二个播放器用于重叠音效
    private readonly Dictionary<Sfx, AudioStream> _streams = new();

    // ---- 节流：同种音效最小间隔（秒），防止刷屏 ----
    private readonly Dictionary<Sfx, float> _lastPlayTime = new();
    private const float MinInterval = 0.05f;  // 50ms 间隔

    // 静态单例（简便访问）
    private static AudioManager? _instance;
    public static AudioManager Instance => _instance!;

    public override void _Ready()
    {
        _instance = this;
        Name = "AudioManager";

        // BGM 播放器
        _bgmPlayer = new AudioStreamPlayer { Name = "BGMPlayer" };
        AddChild(_bgmPlayer);

        // SFX 播放器（双通道，用于重叠音效）
        _sfxPlayer = new AudioStreamPlayer { Name = "SFXPlayer" };
        AddChild(_sfxPlayer);
        _sfxPlayer2 = new AudioStreamPlayer { Name = "SFXPlayer2" };
        AddChild(_sfxPlayer2);

        LoadAllSounds();
        GD.Print("[Audio] 音效管理器初始化完毕");
    }

    private void LoadAllSounds()
    {
        _streams[Sfx.UiClick]        = GD.Load<AudioStream>("res://assets/sounds/ui_click.wav");
        _streams[Sfx.UiBuildStart]   = GD.Load<AudioStream>("res://assets/sounds/ui_build_start.wav");
        _streams[Sfx.UiBuildDone]    = GD.Load<AudioStream>("res://assets/sounds/ui_build_done.wav");
        _streams[Sfx.UiUnitReady]    = GD.Load<AudioStream>("res://assets/sounds/ui_unit_ready.wav");
        _streams[Sfx.UiError]        = GD.Load<AudioStream>("res://assets/sounds/ui_error.wav");
        _streams[Sfx.UiPlace]        = GD.Load<AudioStream>("res://assets/sounds/ui_place.wav");
        _streams[Sfx.Select]         = GD.Load<AudioStream>("res://assets/sounds/sfx_select.wav");
        _streams[Sfx.Move]           = GD.Load<AudioStream>("res://assets/sounds/sfx_move.wav");
        _streams[Sfx.Cannon]         = GD.Load<AudioStream>("res://assets/sounds/sfx_cannon.wav");
        _streams[Sfx.Muzzle]         = GD.Load<AudioStream>("res://assets/sounds/sfx_muzzle.wav");
        _streams[Sfx.Hit]            = GD.Load<AudioStream>("res://assets/sounds/sfx_hit.wav");
        _streams[Sfx.Explosion]      = GD.Load<AudioStream>("res://assets/sounds/sfx_explosion.wav");
        _streams[Sfx.BigExplosion]   = GD.Load<AudioStream>("res://assets/sounds/sfx_big_explosion.wav");
        _streams[Sfx.UnitDie]        = GD.Load<AudioStream>("res://assets/sounds/sfx_unit_die.wav");
        _streams[Sfx.Nuke]           = GD.Load<AudioStream>("res://assets/sounds/sfx_nuke.wav");
        _streams[Sfx.Lightning]      = GD.Load<AudioStream>("res://assets/sounds/sfx_lightning.wav");
        _streams[Sfx.NotifyLowPower] = GD.Load<AudioStream>("res://assets/sounds/notify_low_power.wav");
        _streams[Sfx.NotifyAttack]   = GD.Load<AudioStream>("res://assets/sounds/notify_attack.wav");
        _streams[Sfx.NotifyVictory]  = GD.Load<AudioStream>("res://assets/sounds/notify_victory.wav");
        _streams[Sfx.NotifyDefeat]   = GD.Load<AudioStream>("res://assets/sounds/notify_defeat.wav");
    }

    /// <summary>播放音效（带节流，同种音效50ms内不重复）。</summary>
    public void PlaySfx(Sfx sfx, float pitch = 1f)
    {
        if (Muted) return;
        if (!_streams.TryGetValue(sfx, out var stream) || stream == null) return;

        // 节流检查
        float now = Time.GetTicksMsec() / 1000f;
        if (_lastPlayTime.TryGetValue(sfx, out float lastTime))
        {
            if (now - lastTime < MinInterval) return;
        }
        _lastPlayTime[sfx] = now;

        // 选择空闲的播放器
        var player = !_sfxPlayer.Playing ? _sfxPlayer : _sfxPlayer2;
        player.Stream = stream;
        player.VolumeDb = Mathf.LinearToDb(SfxVolume);
        player.PitchScale = pitch;
        player.Play();
    }

    /// <summary>播放音效（无节流，用于重要事件如超武/通知）。</summary>
    public void PlaySfxForce(Sfx sfx, float pitch = 1f)
    {
        if (Muted) return;
        if (!_streams.TryGetValue(sfx, out var stream) || stream == null) return;

        var player = !_sfxPlayer.Playing ? _sfxPlayer : _sfxPlayer2;
        player.Stream = stream;
        player.VolumeDb = Mathf.LinearToDb(SfxVolume);
        player.PitchScale = pitch;
        player.Play();
    }

    /// <summary>开始播放 BGM（循环）。</summary>
    public void StartBgm()
    {
        var bgm = GD.Load<AudioStream>("res://assets/sounds/bgm_march.wav");
        if (bgm == null) return;
        _bgmPlayer.Stream = bgm;
        _bgmPlayer.VolumeDb = Mathf.LinearToDb(BgmVolume);
        _bgmPlayer.Play();
        GD.Print("[Audio] BGM 开始播放");
    }

    /// <summary>停止 BGM。</summary>
    public void StopBgm()
    {
        _bgmPlayer.Stop();
    }

    /// <summary>切换静音。</summary>
    public void ToggleMute()
    {
        Muted = !Muted;
        _bgmPlayer.VolumeDb = Muted ? -80f : Mathf.LinearToDb(BgmVolume);
        GD.Print($"[Audio] 静音: {Muted}");
    }
}
