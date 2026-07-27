using Godot;
using System.Collections.Generic;

namespace RTSGame;

/// <summary>
/// P2-3: 单位语音系统 — 为不同单位类型播放选择/移动/攻击语音。
/// 音频文件缺失时静默跳过（优雅降级）。
///
/// 语音规划（TTS合成，后期可替换为专业配音）：
///   - 轻坦：轻型装甲确认/移动/攻击
///   - 重坦：重型装甲确认/移动/攻击
///   - 炮兵：炮兵部队确认/移动/攻击
///   - 火箭炮：火箭炮确认/移动/攻击
///   - 导弹车：导弹车确认/移动/攻击
///   - 矿车：矿车确认/移动
///   - 步兵：步兵确认/移动/攻击
///   - 工兵：工兵确认/移动
///   - 英雄：英雄确认/移动/攻击
///   - 间谍：间谍确认/移动
/// </summary>
public static class UnitVoice
{
    /// <summary>语音类型。</summary>
    public enum VoiceType
    {
        Select,   // 选择/确认
        Move,     // 移动命令
        Attack,   // 攻击命令
    }

    /// <summary>按单位类型×语音类型的音频路径。</summary>
    private static readonly Dictionary<string, Dictionary<VoiceType, string>> _voicePaths = new()
    {
        ["LightTank"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/light_tank_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/light_tank_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/light_tank_attack.wav",
        },
        ["HeavyTank"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/heavy_tank_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/heavy_tank_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/heavy_tank_attack.wav",
        },
        ["Artillery"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/artillery_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/artillery_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/artillery_attack.wav",
        },
        ["RocketLauncher"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/rocket_launcher_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/rocket_launcher_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/rocket_launcher_attack.wav",
        },
        ["MissileTank"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/missile_tank_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/missile_tank_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/missile_tank_attack.wav",
        },
        ["Harvester"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/harvester_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/harvester_move.wav",
        },
        ["Infantry"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/infantry_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/infantry_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/infantry_attack.wav",
        },
        ["Engineer"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/engineer_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/engineer_move.wav",
        },
        ["Hero"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/hero_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/hero_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/hero_attack.wav",
        },
        ["Spy"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/spy_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/spy_move.wav",
        },
    };

    /// <summary>缓存已加载的AudioStream。</summary>
    private static readonly Dictionary<string, AudioStream> _cache = new();
    private static readonly object _cacheLock = new();

    /// <summary>
    /// 播放单位语音。音频文件不存在时静默跳过。
    /// </summary>
    public static void Play(AudioStreamPlayer player, string unitType, VoiceType voiceType)
    {
        if (player == null) return;
        if (!_voicePaths.TryGetValue(unitType, out var voiceDict)) return;
        if (!voiceDict.TryGetValue(voiceType, out var path)) return;

        lock (_cacheLock)
        {
            if (!_cache.TryGetValue(path, out var stream))
            {
                stream = GD.Load<AudioStream>(path);
                _cache[path] = stream!; // null也缓存，避免重复尝试加载
            }

            if (stream == null) return; // 文件不存在，静默跳过

            player.Stream = stream;
            player.VolumeDb = Mathf.LinearToDb(0.7f);
            player.Play();
        }
    }

    /// <summary>获取单位类型对应的语音键名。</summary>
    public static string GetUnitTypeKey(UnitType type) => type switch
    {
        UnitType.LightTank => "LightTank",
        UnitType.HeavyTank => "HeavyTank",
        UnitType.Artillery => "Artillery",
        UnitType.RocketLauncher => "RocketLauncher",
        UnitType.MissileTank => "MissileTank",
        UnitType.Harvester => "Harvester",
        UnitType.Infantry => "Infantry",
        UnitType.Engineer => "Engineer",
        UnitType.Hero => "Hero",
        UnitType.Spy => "Spy",
        _ => "",
    };
}
