using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// P2-4: 地形速度修正数据驱动 — 从 res://data/terrain_modifiers.json 加载。
/// 替代 TerrainGrid 和 TerrainGrid3D 中的重复硬编码 switch 表。
/// 两份完全相同的 80 个修正值现在由单一数据源提供。
/// JSON加载失败时回退到硬编码数据。
/// </summary>
public static class TerrainModifiers
{
    // ===== 数据结构 =====

    /// <summary>地形×单位类别 的速度修正表。</summary>
    private static readonly Dictionary<TerrainType, Dictionary<TerrainUnitCategory, float>> _speedMods = new();
    /// <summary>每种地形的 _default 速度修正值（与单位类别无关的回退值）。</summary>
    private static readonly Dictionary<TerrainType, float> _speedDefaults = new();
    /// <summary>缓坡修正（elevDiff==1时乘以基础速度）。</summary>
    private static readonly Dictionary<TerrainUnitCategory, float> _slopeMods = new();
    /// <summary>缓坡修正的 _default 值（替代之前用 Air 作为哨兵键的方案）。</summary>
    private static float _slopeDefault = 0.4f;
    private static readonly object _lock = new();
    private static bool _alwaysFallback = false;

    /// <summary>强制使用硬编码数据（供单元测试使用）</summary>
    public static void SetAlwaysFallback(bool value) => _alwaysFallback = value;

    // ===== 公开查询接口 =====

    /// <summary>
    /// 获取指定地形×单位类别的速度修正值。
    /// 如果类别不存在，返回该地形的 _default 值。
    /// </summary>
    public static float GetSpeedMod(TerrainType terrainType, TerrainUnitCategory unitCat)
    {
        lock (_lock)
        {
            if (_speedMods.Count == 0) LoadFromJsonCore(_alwaysFallback);

            if (_speedMods.TryGetValue(terrainType, out var catDict))
            {
                if (catDict.TryGetValue(unitCat, out float val))
                    return val;
                // 回退到 _default（独立存储，不再借用 Air 作为哨兵键）
                if (_speedDefaults.TryGetValue(terrainType, out float defVal))
                    return defVal;
            }
            return 1.0f; // 未知地形默认满速
        }
    }

    /// <summary>
    /// 获取缓坡修正系数（elevDiff==1时使用）。
    /// </summary>
    public static float GetSlopeMod(TerrainUnitCategory unitCat)
    {
        lock (_lock)
        {
            if (_slopeMods.Count == 0) LoadFromJsonCore(_alwaysFallback);

            if (_slopeMods.TryGetValue(unitCat, out float val))
                return val;
            return _slopeDefault; // 默认缓坡修正（独立存储，不再借用 Air 作为哨兵键）
        }
    }

    // ===== JSON 加载 =====

    /// <summary>从 res://data/terrain_modifiers.json 加载。
    /// forceFallback=true时跳过Godot IO，直接用硬编码数据。</summary>
    public static void LoadFromJson(bool forceFallback = false)
    {
        lock (_lock)
        {
            if (_speedMods.Count > 0) return; // 已加载，跳过
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

        // P2-4: 通过ModLoader读取，支持Mod覆盖
        var jsonText = ModLoader.ReadDataFile("terrain_modifiers.json");
        if (string.IsNullOrEmpty(jsonText))
        {
            GameLog.Warning("[TerrainModifiers] Cannot read terrain_modifiers.json, using hardcoded fallback");
            LoadFallback();
            return;
        }

        var jsonResult = Json.ParseString(jsonText);
        if (jsonResult.VariantType != Variant.Type.Dictionary)
        {
            GameLog.Warning("[TerrainModifiers] terrain_modifiers.json parse error, using hardcoded fallback");
            LoadFallback();
            return;
        }

        var root = jsonResult.AsGodotDictionary();
        _speedMods.Clear();
        _slopeMods.Clear();

        // 解析 speedModifiers
        if (root.ContainsKey("speedModifiers") && root["speedModifiers"].VariantType == Variant.Type.Dictionary)
        {
            var smDict = root["speedModifiers"].AsGodotDictionary();
            foreach (var terrainKey in smDict.Keys)
            {
                var terrainStr = terrainKey.AsString();
                if (!System.Enum.TryParse<TerrainType>(terrainStr, out var terrainType))
                {
                    GameLog.Warning($"[TerrainModifiers] Unknown terrain type: {terrainStr}");
                    continue;
                }

                var catDict = smDict[terrainKey].AsGodotDictionary();
                var catMap = new Dictionary<TerrainUnitCategory, float>();
                foreach (var catKey in catDict.Keys)
                {
                    var catStr = catKey.AsString();
                    float val = (float)catDict[catKey].AsDouble();

                    if (catStr == "_default")
                    {
                        // P1-8修复：使用独立字典存储 _default，不再借用 Air 作为哨兵键
                        _speedDefaults[terrainType] = val;
                    }
                    else if (System.Enum.TryParse<TerrainUnitCategory>(catStr, out var unitCat))
                    {
                        catMap[unitCat] = val;
                    }
                }
                _speedMods[terrainType] = catMap;
            }
        }

        // 解析 slopeModifiers
        if (root.ContainsKey("slopeModifiers") && root["slopeModifiers"].VariantType == Variant.Type.Dictionary)
        {
            var slDict = root["slopeModifiers"].AsGodotDictionary();
            foreach (var catKey in slDict.Keys)
            {
                var catStr = catKey.AsString();
                float val = (float)slDict[catKey].AsDouble();

                if (catStr == "_default")
                {
                    _slopeDefault = val;
                }
                else if (System.Enum.TryParse<TerrainUnitCategory>(catStr, out var unitCat))
                {
                    _slopeMods[unitCat] = val;
                }
            }
        }

        GameLog.Info($"[TerrainModifiers] Loaded {_speedMods.Count} terrain mods + {_slopeMods.Count} slope mods from JSON");
    }

    /// <summary>硬编码fallback — 与原始 TerrainGrid.GetSpeedModifier 完全一致</summary>
    private static void LoadFallback()
    {
        _speedMods.Clear();
        _speedDefaults.Clear();
        _slopeMods.Clear();

        // Road
        _speedMods[TerrainType.Road] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Infantry] = 1.2f,
            [TerrainUnitCategory.LightVehicle] = 1.3f,
            [TerrainUnitCategory.HeavyVehicle] = 1.2f,
            [TerrainUnitCategory.Harvester] = 1.2f,
            [TerrainUnitCategory.Engineer] = 1.2f,
            [TerrainUnitCategory.EngineerVehicle] = 1.2f,
            [TerrainUnitCategory.Naval] = 0f,
        };
        _speedDefaults[TerrainType.Road] = 1.0f;
        // Grass — 所有单位1.0
        _speedMods[TerrainType.Grass] = new Dictionary<TerrainUnitCategory, float>();
        _speedDefaults[TerrainType.Grass] = 1.0f;
        // Sand
        _speedMods[TerrainType.Sand] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Infantry] = 0.8f,
            [TerrainUnitCategory.LightVehicle] = 0.6f,
            [TerrainUnitCategory.HeavyVehicle] = 0.4f,
            [TerrainUnitCategory.Harvester] = 0.7f,
            [TerrainUnitCategory.Engineer] = 0.8f,
            [TerrainUnitCategory.EngineerVehicle] = 0.7f,
            [TerrainUnitCategory.Naval] = 0f,
        };
        _speedDefaults[TerrainType.Sand] = 0.6f;
        // Snow
        _speedMods[TerrainType.Snow] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Infantry] = 0.7f,
            [TerrainUnitCategory.LightVehicle] = 0.5f,
            [TerrainUnitCategory.HeavyVehicle] = 0.4f,
            [TerrainUnitCategory.Harvester] = 0.6f,
            [TerrainUnitCategory.Engineer] = 0.7f,
            [TerrainUnitCategory.EngineerVehicle] = 0.6f,
            [TerrainUnitCategory.Naval] = 0f,
        };
        _speedDefaults[TerrainType.Snow] = 0.5f;
        // City
        _speedMods[TerrainType.City] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Infantry] = 0.9f,
            [TerrainUnitCategory.LightVehicle] = 0.8f,
            [TerrainUnitCategory.HeavyVehicle] = 0.7f,
            [TerrainUnitCategory.Harvester] = 0.8f,
            [TerrainUnitCategory.Engineer] = 0.9f,
            [TerrainUnitCategory.EngineerVehicle] = 0.8f,
            [TerrainUnitCategory.Naval] = 0f,
        };
        _speedDefaults[TerrainType.City] = 0.8f;
        // Field
        _speedMods[TerrainType.Field] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Infantry] = 0.9f,
            [TerrainUnitCategory.LightVehicle] = 0.7f,
            [TerrainUnitCategory.HeavyVehicle] = 0.5f,
            [TerrainUnitCategory.Harvester] = 0.8f,
            [TerrainUnitCategory.Engineer] = 0.9f,
            [TerrainUnitCategory.EngineerVehicle] = 0.8f,
            [TerrainUnitCategory.Naval] = 0f,
        };
        _speedDefaults[TerrainType.Field] = 0.7f;
        // ShallowWater
        _speedMods[TerrainType.ShallowWater] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Infantry] = 0.3f,
            [TerrainUnitCategory.LightVehicle] = 0.2f,
            [TerrainUnitCategory.HeavyVehicle] = 0.1f,
            [TerrainUnitCategory.Harvester] = 0f,
            [TerrainUnitCategory.Engineer] = 0.3f,
            [TerrainUnitCategory.EngineerVehicle] = 0.2f,
            [TerrainUnitCategory.Naval] = 1.0f,
        };
        _speedDefaults[TerrainType.ShallowWater] = 0.2f;
        // DeepWater
        _speedMods[TerrainType.DeepWater] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Naval] = 1.0f,
        };
        _speedDefaults[TerrainType.DeepWater] = 0f;
        // Mountain
        _speedMods[TerrainType.Mountain] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Infantry] = 0.3f,
            [TerrainUnitCategory.LightVehicle] = 0.2f,
            [TerrainUnitCategory.HeavyVehicle] = 0f,
            [TerrainUnitCategory.Harvester] = 0f,
            [TerrainUnitCategory.Engineer] = 0.3f,
            [TerrainUnitCategory.EngineerVehicle] = 0f,
            [TerrainUnitCategory.Naval] = 0f,
        };
        _speedDefaults[TerrainType.Mountain] = 0f;
        // Cliff — 所有0
        _speedMods[TerrainType.Cliff] = new Dictionary<TerrainUnitCategory, float>();
        _speedDefaults[TerrainType.Cliff] = 0f;
        // Bridge
        _speedMods[TerrainType.Bridge] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Infantry] = 1.0f,
            [TerrainUnitCategory.LightVehicle] = 1.0f,
            [TerrainUnitCategory.HeavyVehicle] = 0.9f,
            [TerrainUnitCategory.Harvester] = 1.0f,
            [TerrainUnitCategory.Engineer] = 1.0f,
            [TerrainUnitCategory.EngineerVehicle] = 1.0f,
            [TerrainUnitCategory.Naval] = 0f,
        };
        _speedDefaults[TerrainType.Bridge] = 1.0f;
        // Tunnel
        _speedMods[TerrainType.Tunnel] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Infantry] = 0.9f,
            [TerrainUnitCategory.LightVehicle] = 0.9f,
            [TerrainUnitCategory.HeavyVehicle] = 0.8f,
            [TerrainUnitCategory.Harvester] = 0.9f,
            [TerrainUnitCategory.Engineer] = 0.9f,
            [TerrainUnitCategory.EngineerVehicle] = 0.9f,
            [TerrainUnitCategory.Naval] = 0f,
        };
        _speedDefaults[TerrainType.Tunnel] = 0.9f;

        // Slope modifiers
        _slopeMods[TerrainUnitCategory.Infantry] = 0.5f;
        _slopeMods[TerrainUnitCategory.LightVehicle] = 0.3f;
        _slopeMods[TerrainUnitCategory.HeavyVehicle] = 0.2f;
        _slopeMods[TerrainUnitCategory.Harvester] = 0.3f;
        _slopeMods[TerrainUnitCategory.Engineer] = 0.5f;
        _slopeMods[TerrainUnitCategory.EngineerVehicle] = 0.3f;
        _slopeDefault = 0.4f;
    }
}
