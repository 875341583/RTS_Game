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
    /// <summary>缓坡修正（elevDiff==1时乘以基础速度）。</summary>
    private static readonly Dictionary<TerrainUnitCategory, float> _slopeMods = new();
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
                // 回退到 _default
                if (catDict.TryGetValue(TerrainUnitCategory.Air, out float def))
                    return def; // Air 作为 _default 的占位键（不会到这里，因为Air在调用方已短路）
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
            return 0.4f; // 默认缓坡修正
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

        const string path = "res://data/terrain_modifiers.json";
        var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GameLog.Warning($"[TerrainModifiers] 无法打开 {path}，使用硬编码fallback");
            LoadFallback();
            return;
        }

        var jsonText = file.GetAsText();
        file.Close();

        var jsonResult = Json.ParseString(jsonText);
        if (jsonResult.VariantType != Variant.Type.Dictionary)
        {
            GameLog.Warning("[TerrainModifiers] terrain_modifiers.json 格式错误，使用硬编码fallback");
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
                    GameLog.Warning($"[TerrainModifiers] 未知地形类型: {terrainStr}");
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
                        // 用一个不可能作为查询键的特殊值存储 default
                        // 我们用一个哨兵：Air 作为 default（因为Air总在调用方短路，不会查表）
                        catMap[TerrainUnitCategory.Air] = val;
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
                    _slopeMods[TerrainUnitCategory.Air] = val; // Air 哨兵
                }
                else if (System.Enum.TryParse<TerrainUnitCategory>(catStr, out var unitCat))
                {
                    _slopeMods[unitCat] = val;
                }
            }
        }

        GameLog.Info($"[TerrainModifiers] 从JSON加载 {_speedMods.Count} 地形修正 + {_slopeMods.Count} 缓坡修正");
    }

    /// <summary>硬编码fallback — 与原始 TerrainGrid.GetSpeedModifier 完全一致</summary>
    private static void LoadFallback()
    {
        _speedMods.Clear();
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
            [TerrainUnitCategory.Air] = 1.0f, // _default
        };
        // Grass — 所有单位1.0
        _speedMods[TerrainType.Grass] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Air] = 1.0f, // _default
        };
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
            [TerrainUnitCategory.Air] = 0.6f, // _default
        };
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
            [TerrainUnitCategory.Air] = 0.5f, // _default
        };
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
            [TerrainUnitCategory.Air] = 0.8f, // _default
        };
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
            [TerrainUnitCategory.Air] = 0.7f, // _default
        };
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
            [TerrainUnitCategory.Air] = 0.2f, // _default
        };
        // DeepWater
        _speedMods[TerrainType.DeepWater] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Naval] = 1.0f,
            [TerrainUnitCategory.Air] = 0f, // _default
        };
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
            [TerrainUnitCategory.Air] = 0f, // _default
        };
        // Cliff — 所有0
        _speedMods[TerrainType.Cliff] = new Dictionary<TerrainUnitCategory, float>
        {
            [TerrainUnitCategory.Air] = 0f, // _default
        };
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
            [TerrainUnitCategory.Air] = 1.0f, // _default
        };
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
            [TerrainUnitCategory.Air] = 0.9f, // _default
        };

        // Slope modifiers
        _slopeMods[TerrainUnitCategory.Infantry] = 0.5f;
        _slopeMods[TerrainUnitCategory.LightVehicle] = 0.3f;
        _slopeMods[TerrainUnitCategory.HeavyVehicle] = 0.2f;
        _slopeMods[TerrainUnitCategory.Harvester] = 0.3f;
        _slopeMods[TerrainUnitCategory.Engineer] = 0.5f;
        _slopeMods[TerrainUnitCategory.EngineerVehicle] = 0.3f;
        _slopeMods[TerrainUnitCategory.Air] = 0.4f; // _default
    }
}
