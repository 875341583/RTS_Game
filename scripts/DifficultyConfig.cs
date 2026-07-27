using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// P2-4: 难度配置数据驱动 — 从 res://data/difficulty.json 加载各难度参数。
/// 替代 Main.ApplyDifficultyConfig() 中的硬编码 switch。
/// JSON加载失败时回退到硬编码数据。
/// </summary>
public static class DifficultyConfig
{
    /// <summary>单难度档位的所有参数。</summary>
    public class Config
    {
        public float AiThinkInterval { get; init; }
        public int AiStartMoney { get; init; }
        public int BlueStartMoney { get; init; }
        public int AiStartHarvesters { get; init; }
        public bool AiUsesTech { get; init; }
        public bool AiCapturesPoints { get; init; }
        public bool StrategicPointIncomeEnabled { get; init; }
        public int UnitCap { get; init; }
        public int PlayerTechLevel { get; init; }
        public float AiGraceRemaining { get; init; }
        public int ActiveAiCount { get; init; }
    }

    private static Dictionary<string, Config> _configs = new();
    private static readonly object _lock = new();
    private static bool _alwaysFallback = false;

    /// <summary>强制使用硬编码数据（供单元测试使用，在无Godot运行时的环境中调用）</summary>
    public static void SetAlwaysFallback(bool value) => _alwaysFallback = value;

    /// <summary>获取指定难度的配置（懒加载）。</summary>
    public static Config Get(string difficulty)
    {
        lock (_lock)
        {
            if (_configs.Count == 0) LoadFromJsonCore(_alwaysFallback);
            return _configs.TryGetValue(difficulty, out var c) ? c : _configs["Normal"];
        }
    }

    /// <summary>从 res://data/difficulty.json 加载。
    /// forceFallback=true时跳过Godot IO，直接用硬编码数据（供单元测试使用）。</summary>
    public static void LoadFromJson(bool forceFallback = false)
    {
        lock (_lock)
        {
            if (_configs.Count > 0) return; // 已加载，无论fallback还是JSON都跳过
            LoadFromJsonCore(forceFallback || _alwaysFallback);
        }
    }

    private static void LoadFromJsonCore(bool forceFallback)
    {
        if (forceFallback)
        {
            LoadFallback();
            return;
        }

        const string path = "res://data/difficulty.json";
        var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GameLog.Warning($"[DifficultyConfig] 无法打开 {path}，使用硬编码fallback");
            LoadFallback();
            return;
        }

        var jsonText = file.GetAsText();
        file.Close();

        var jsonResult = Json.ParseString(jsonText);
        if (jsonResult.VariantType != Variant.Type.Dictionary)
        {
            GameLog.Warning("[DifficultyConfig] difficulty.json 格式错误，使用硬编码fallback");
            LoadFallback();
            return;
        }

        var root = jsonResult.AsGodotDictionary();
        _configs.Clear();
        foreach (var key in root.Keys)
        {
            var diffKey = key.AsString();
            var dict = root[key].AsGodotDictionary();
            if (dict == null) continue;

            _configs[diffKey] = new Config
            {
                AiThinkInterval = (float)dict["aiThinkInterval"].AsDouble(),
                AiStartMoney = (int)dict["aiStartMoney"].AsInt64(),
                BlueStartMoney = (int)dict["blueStartMoney"].AsInt64(),
                AiStartHarvesters = (int)dict["aiStartHarvesters"].AsInt64(),
                AiUsesTech = dict["aiUsesTech"].AsBool(),
                AiCapturesPoints = dict["aiCapturesPoints"].AsBool(),
                StrategicPointIncomeEnabled = dict["strategicPointIncomeEnabled"].AsBool(),
                UnitCap = (int)dict["unitCap"].AsInt64(),
                PlayerTechLevel = (int)dict["playerTechLevel"].AsInt64(),
                AiGraceRemaining = (float)dict["aiGraceRemaining"].AsDouble(),
                ActiveAiCount = (int)dict["activeAiCount"].AsInt64(),
            };
        }

        GameLog.Info($"[DifficultyConfig] 从JSON加载 {_configs.Count} 个难度配置");
    }

    private static void LoadFallback()
    {
        _configs.Clear();
        _configs["Easy"] = new Config
        {
            AiThinkInterval = 14f, AiStartMoney = 1500, BlueStartMoney = 3000,
            AiStartHarvesters = 2, AiUsesTech = false, AiCapturesPoints = false,
            StrategicPointIncomeEnabled = false, UnitCap = 12, PlayerTechLevel = 1,
            AiGraceRemaining = 120f, ActiveAiCount = 2,
        };
        _configs["Normal"] = new Config
        {
            AiThinkInterval = 10f, AiStartMoney = 1800, BlueStartMoney = 2700,
            AiStartHarvesters = 3, AiUsesTech = true, AiCapturesPoints = true,
            StrategicPointIncomeEnabled = true, UnitCap = 16, PlayerTechLevel = 3,
            AiGraceRemaining = 60f, ActiveAiCount = 4,
        };
        _configs["Hard"] = new Config
        {
            AiThinkInterval = 7f, AiStartMoney = 2200, BlueStartMoney = 2500,
            AiStartHarvesters = 3, AiUsesTech = true, AiCapturesPoints = true,
            StrategicPointIncomeEnabled = true, UnitCap = 20, PlayerTechLevel = 3,
            AiGraceRemaining = 30f, ActiveAiCount = 6,
        };
        _configs["Brutal"] = new Config
        {
            AiThinkInterval = 4f, AiStartMoney = 3000, BlueStartMoney = 2200,
            AiStartHarvesters = 4, AiUsesTech = true, AiCapturesPoints = true,
            StrategicPointIncomeEnabled = true, UnitCap = 24, PlayerTechLevel = 3,
            AiGraceRemaining = 0f, ActiveAiCount = 7,
        };
    }
}
