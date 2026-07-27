using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// G7: 间谍深化 — 文明6风格间谍任务系统
///
/// 间谍（UnitType.Spy）可以执行5种任务，选中间谍右键敌方建筑触发：
/// - 窃取科技：渗透科技中心，窃取敌方已研究科技
/// - 破坏电网：渗透电站，使其断电8秒
/// - 窃取资金：渗透基地，偷取$500
/// - 瘫痪生产：渗透兵营/车厂，暂停生产10秒
/// - 侦察：渗透任意建筑，揭示敌方信息
///
/// 机制：
/// - 渗透需4秒倒计时，期间间谍不可移动
/// - 成功率80%，失败间谍死亡(20%)
/// - N键查看间谍任务面板
///
/// P2-4: 数据驱动 — 从 res://data/spy_missions.json 加载常量与任务元数据，
/// 替代硬编码常量和字符串映射。ChooseMission业务逻辑保留为代码。
/// JSON加载失败时回退到硬编码数据。
/// </summary>
public static class SpyMission
{
    /// <summary>间谍任务类型。</summary>
    public enum MissionType
    {
        StealTech,      // 窃取科技 — 科技中心
        SabotagePower,  // 破坏电网 — 电站
        StealMoney,     // 窃取资金 — 基地
        SabotageProd,   // 瘫痪生产 — 兵营/车厂
        Recon,          // 侦察 — 任意建筑
    }

    // ===== 任务元数据 =====
    public class MissionInfo
    {
        public MissionType Id { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
    }

    // ===== P2-4: 从JSON加载的常量 =====
    private static float _successRate = 0f;
    private static float _infiltrateTime = 0f;
    private static float _sabotagePowerDuration = 0f;
    private static float _sabotageProdDuration = 0f;
    private static int _stealMoneyAmount = 0;
    private static Dictionary<MissionType, MissionInfo> _missions = new();
    private static readonly object _dataLock = new();
    private static bool _alwaysFallback = false;

    /// <summary>强制使用硬编码数据（供单元测试使用，在无Godot运行时的环境中调用）</summary>
    public static void SetAlwaysFallback(bool value) => _alwaysFallback = value;

    /// <summary>任务成功率（0.8 = 80%）。</summary>
    public static float SuccessRate => _successRate;
    /// <summary>渗透倒计时（秒）。</summary>
    public static float InfiltrateTime => _infiltrateTime;
    /// <summary>破坏电网持续秒数。</summary>
    public static float SabotagePowerDuration => _sabotagePowerDuration;
    /// <summary>瘫痪生产持续秒数。</summary>
    public static float SabotageProdDuration => _sabotageProdDuration;
    /// <summary>窃取资金量。</summary>
    public static int StealMoneyAmount => _stealMoneyAmount;

    /// <summary>P2-4: 从 res://data/spy_missions.json 加载间谍任务数据。
    /// forceFallback=true时跳过Godot IO，直接用硬编码数据（供单元测试使用）。</summary>
    public static void LoadFromJson(bool forceFallback = false)
    {
        lock (_dataLock)
        {
            if (_missions.Count > 0) return; // 已加载，无论fallback还是JSON都跳过
            LoadFromJsonCore(forceFallback || _alwaysFallback);
        }
    }

    /// <summary>内部加载实现（调用方需持有 _dataLock）</summary>
    private static void LoadFromJsonCore(bool forceFallback)
    {
        if (forceFallback)
        {
            LoadFallback();
            return;
        }

        // P2-4: 通过ModLoader读取，支持Mod覆盖
        var jsonText = ModLoader.ReadDataFile("spy_missions.json");
        if (string.IsNullOrEmpty(jsonText))
        {
            GameLog.Warning("[SpyMission] 无法读取 spy_missions.json，使用硬编码fallback");
            LoadFallback();
            return;
        }

        var jsonResult = Json.ParseString(jsonText);
        if (jsonResult.VariantType != Variant.Type.Dictionary)
        {
            GameLog.Warning("[SpyMission] spy_missions.json 格式错误，使用硬编码fallback");
            LoadFallback();
            return;
        }

        var root = jsonResult.AsGodotDictionary();
        var constants = root["constants"].AsGodotDictionary();
        _successRate = (float)constants["successRate"].AsDouble();
        _infiltrateTime = (float)constants["infiltrateTime"].AsDouble();
        _sabotagePowerDuration = (float)constants["sabotagePowerDuration"].AsDouble();
        _sabotageProdDuration = (float)constants["sabotageProdDuration"].AsDouble();
        _stealMoneyAmount = (int)constants["stealMoneyAmount"].AsInt64();

        _missions.Clear();
        if (root.ContainsKey("missions") && root["missions"].VariantType == Variant.Type.Array)
        {
            foreach (var entry in root["missions"].AsGodotArray())
            {
                var dict = entry.AsGodotDictionary();
                if (dict == null) continue;

                var idStr = dict["id"].AsString();
                if (!System.Enum.TryParse<MissionType>(idStr, out var id))
                {
                    GameLog.Warning($"[SpyMission] 未知任务ID: {idStr}");
                    continue;
                }

                _missions[id] = new MissionInfo
                {
                    Id = id,
                    Name = dict["name"].AsString(),
                    Description = dict["description"].AsString(),
                };
            }
        }

        GameLog.Info($"[SpyMission] 从JSON加载 {_missions.Count} 个任务 + 常量");
    }

    /// <summary>P2-4: 硬编码fallback（JSON加载失败时使用）</summary>
    private static void LoadFallback()
    {
        _successRate = 0.80f;
        _infiltrateTime = 4f;
        _sabotagePowerDuration = 8f;
        _sabotageProdDuration = 10f;
        _stealMoneyAmount = 500;

        _missions = new Dictionary<MissionType, MissionInfo>
        {
            [MissionType.StealTech] = new MissionInfo { Id = MissionType.StealTech, Name = "窃取科技", Description = "窃取1个敌方已研究科技(免费完成)" },
            [MissionType.SabotagePower] = new MissionInfo { Id = MissionType.SabotagePower, Name = "破坏电网", Description = "使电站断电8秒" },
            [MissionType.StealMoney] = new MissionInfo { Id = MissionType.StealMoney, Name = "窃取资金", Description = "偷取$500" },
            [MissionType.SabotageProd] = new MissionInfo { Id = MissionType.SabotageProd, Name = "瘫痪生产", Description = "暂停生产10秒" },
            [MissionType.Recon] = new MissionInfo { Id = MissionType.Recon, Name = "侦察", Description = "揭示敌方建筑/单位信息5秒" },
        };
    }

    /// <summary>根据目标建筑类型自动选择最优任务。</summary>
    public static MissionType ChooseMission(BuildingType buildingType)
    {
        return buildingType switch
        {
            BuildingType.TechCenter => MissionType.StealTech,
            BuildingType.PowerPlant => MissionType.SabotagePower,
            BuildingType.Base => MissionType.StealMoney,
            BuildingType.Barracks => MissionType.SabotageProd,
            BuildingType.WarFactory => MissionType.SabotageProd,
            _ => MissionType.Recon,
        };
    }

    /// <summary>获取任务的中文名称（P2-4: 从数据字典查找）。</summary>
    public static string MissionName(MissionType type)
    {
        lock (_dataLock)
        {
            if (_missions.Count == 0) LoadFromJsonCore(_alwaysFallback);
            return _missions.TryGetValue(type, out var info) ? info.Name : "未知";
        }
    }

    /// <summary>获取任务描述（P2-4: 从数据字典查找，含动态常量替换）。</summary>
    public static string MissionDesc(MissionType type)
    {
        lock (_dataLock)
        {
            if (_missions.Count == 0) LoadFromJsonCore(_alwaysFallback);
            return _missions.TryGetValue(type, out var info) ? info.Description : "";
        }
    }
}
