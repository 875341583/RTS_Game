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
        // ---- 补强：22种缺失单位语音（音频文件不存在时静默跳过） ----
        ["ApocalypseTank"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_apocalypse_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_apocalypse_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_apocalypse_attack.wav",
        },
        ["PrismTank"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_prism_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_prism_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_prism_attack.wav",
        },
        ["KirovAirship"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_kirov_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_kirov_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_kirov_attack.wav",
        },
        ["TeslaTrooper"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_tesla_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_tesla_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_tesla_attack.wav",
        },
        ["Fighter"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_fighter_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_fighter_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_fighter_attack.wav",
        },
        ["Bomber"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_bomber_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_bomber_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_bomber_attack.wav",
        },
        ["Scout"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_scout_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_scout_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_scout_attack.wav",
        },
        ["Helicopter"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_heli_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_heli_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_heli_attack.wav",
        },
        ["Destroyer"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_destroyer_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_destroyer_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_destroyer_attack.wav",
        },
        ["Submarine"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_submarine_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_submarine_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_submarine_attack.wav",
        },
        ["Grenadier"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_grenadier_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_grenadier_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_grenadier_attack.wav",
        },
        ["Sniper"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_sniper_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_sniper_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_sniper_attack.wav",
        },
        ["FlameInfantry"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_flame_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_flame_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_flame_attack.wav",
        },
        ["Thief"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_thief_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_thief_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_thief_attack.wav",
        },
        ["RocketInfantry"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_rocketinf_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_rocketinf_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_rocketinf_attack.wav",
        },
        ["Transport"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_transport_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_transport_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_transport_attack.wav",
        },
        ["Sapper"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_sapper_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_sapper_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_sapper_attack.wav",
        },
        ["ChiefEngineer"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_chiefengineer_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_chiefengineer_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_chiefengineer_attack.wav",
        },
        ["AntiAir"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_antiair_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_antiair_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_antiair_attack.wav",
        },
        ["AircraftCarrier"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_carrier_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_carrier_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_carrier_attack.wav",
        },
        ["LandingCraft"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_landing_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_landing_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_landing_attack.wav",
        },
        ["TransportHeli"] = new()
        {
            [VoiceType.Select] = "res://assets/sounds/voice/voice_transheli_select.wav",
            [VoiceType.Move]   = "res://assets/sounds/voice/voice_transheli_move.wav",
            [VoiceType.Attack] = "res://assets/sounds/voice/voice_transheli_attack.wav",
        },
    };

    /// <summary>缓存已加载的AudioStream。</summary>
    private static readonly Dictionary<string, AudioStream?> _cache = new();
    private static readonly object _cacheLock = new();

    /// <summary>
    /// 播放单位语音。音频文件不存在或未导入时静默跳过。
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
                // 先检查资源是否存在且可加载，避免未导入文件产生ERROR日志
                if (!ResourceLoader.Exists(path, "AudioStream"))
                {
                    _cache[path] = null;
                    return;
                }
                stream = GD.Load<AudioStream>(path);
                _cache[path] = stream; // null也缓存，避免重复尝试加载
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
        // 补强：22种缺失单位语音映射
        UnitType.ApocalypseTank => "ApocalypseTank",
        UnitType.PrismTank => "PrismTank",
        UnitType.KirovAirship => "KirovAirship",
        UnitType.TeslaTrooper => "TeslaTrooper",
        UnitType.Fighter => "Fighter",
        UnitType.Bomber => "Bomber",
        UnitType.Scout => "Scout",
        UnitType.Helicopter => "Helicopter",
        UnitType.Destroyer => "Destroyer",
        UnitType.Submarine => "Submarine",
        UnitType.Grenadier => "Grenadier",
        UnitType.Sniper => "Sniper",
        UnitType.FlameInfantry => "FlameInfantry",
        UnitType.Thief => "Thief",
        UnitType.RocketInfantry => "RocketInfantry",
        UnitType.Transport => "Transport",
        UnitType.Sapper => "Sapper",
        UnitType.ChiefEngineer => "ChiefEngineer",
        UnitType.AntiAir => "AntiAir",
        UnitType.AircraftCarrier => "AircraftCarrier",
        UnitType.LandingCraft => "LandingCraft",
        UnitType.TransportHeli => "TransportHeli",
        _ => "",
    };
}
