using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// P1-2: 游戏数据管理器 — 从 res://data/*.json 加载单位和建筑属性数据。
/// 替代4处硬编码switch-case（Unit.InitAsType / Unit3D.InitAsType / Building.InitAsType / Building3D.InitAsType）
/// 以及Main.cs/Main3D.cs中的成本const常量。
///
/// 使用Godot FileAccess + Json.ParseString，确保导出后(res://虚拟文件系统)可正常读取。
/// 首次访问时懒加载并缓存，后续访问直接返回内存数据。
/// </summary>
public static class GameData
{
    // ======== 数据结构 ========

    /// <summary>单位属性（单套，不区分2D/3D，由调用方按需取stats2d或stats3d）。</summary>
    public class UnitEntry
    {
        public string Name = "";
        public int Cost;
        public UnitStats Stats2D = new();
        public UnitStats Stats3D = new();
    }

    /// <summary>单位战斗属性。可选字段用Nullable或默认值，调用方按需读取。</summary>
    public class UnitStats
    {
        public float MaxHealth;
        public float MoveSpeed;
        public float AttackDamage;
        public float AttackRange;
        public float AttackCooldown;
        // 可选字段
        public float AggroRange;            // 2D用，3D无
        public float MinAttackRange;        // 火箭炮/炮兵等远程单位
        public float SplashRadius;          // AOE单位
        public bool CanAttackAir;           // 防空车/火箭兵/战斗机
        public bool AutoDefend;             // 自动防御（2D用）
        public bool IsAirUnit;              // 空军
        public bool IsHero;                 // 英雄
        public int MaxPassengers;           // 运输车/航母
        public string? TerrainModType;      // 3D工兵地形平整（"Flatten"或null）
    }

    /// <summary>建筑属性。</summary>
    public class BuildingEntry
    {
        public string Name = "";
        public int Cost;
        public BuildingStats Stats2D = new();
        public BuildingStats Stats3D = new();
    }

    /// <summary>建筑属性。可选字段由调用方按需读取。</summary>
    public class BuildingStats
    {
        public float MaxHealth;
        public int PowerProvided;
        public int PowerConsumed;
        public bool IsDefensive;
        public float AttackDamage;
        public float AttackRange;
        public float AttackCooldown;
        public bool IsRepairStation;
        public float RepairRadius;
    }

    // ======== 缓存 ========

    private static readonly Dictionary<UnitType, UnitEntry> _units = new();
    private static readonly Dictionary<BuildingType, BuildingEntry> _buildings = new();
    private static readonly Dictionary<ProductionType, float> _productionTimes = new();
    private static bool _loaded = false;

    /// <summary>是否已加载数据。</summary>
    public static bool IsLoaded => _loaded;

    // ======== 加载 ========

    /// <summary>从res://data/加载全部JSON数据。仅加载一次，后续调用为空操作。</summary>
    public static void Load()
    {
        if (_loaded) return;
        LoadUnits();
        LoadBuildings();
        _loaded = true;
        GD.Print($"[GameData] 数据加载完成: {_units.Count}单位, {_buildings.Count}建筑, {_productionTimes.Count}生产时间");
    }

    /// <summary>确保数据已加载（首次访问时自动调用）。</summary>
    private static void EnsureLoaded()
    {
        if (!_loaded) Load();
    }

    private static void LoadUnits()
    {
        const string path = "res://data/units.json";
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"[GameData] 无法加载单位数据: {path}");
            return;
        }
        var json = file.GetAsText();
        var parsed = Json.ParseString(json);
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            GD.PrintErr($"[GameData] 单位数据解析失败: {path}");
            return;
        }
        var root = parsed.AsGodotDictionary();
        var units = root["units"].AsGodotDictionary();
        foreach (var key in units.Keys)
        {
            string typeName = key.AsString();
            if (!Enum.TryParse<UnitType>(typeName, out var unitType))
            {
                GD.PrintErr($"[GameData] 未知单位类型: {typeName}");
                continue;
            }
            var entry = ParseUnitEntry(units[key].AsGodotDictionary());
            _units[unitType] = entry;
        }
    }

    private static UnitEntry ParseUnitEntry(Godot.Collections.Dictionary d)
    {
        var entry = new UnitEntry
        {
            Name = d["name"].AsString(),
            Cost = (int)d["cost"].AsInt32()
        };
        if (d.ContainsKey("stats2d"))
            entry.Stats2D = ParseUnitStats(d["stats2d"].AsGodotDictionary());
        if (d.ContainsKey("stats3d"))
            entry.Stats3D = ParseUnitStats(d["stats3d"].AsGodotDictionary());
        return entry;
    }

    private static UnitStats ParseUnitStats(Godot.Collections.Dictionary d)
    {
        var s = new UnitStats();
        if (d.ContainsKey("maxHealth")) s.MaxHealth = (float)d["maxHealth"].AsDouble();
        if (d.ContainsKey("moveSpeed")) s.MoveSpeed = (float)d["moveSpeed"].AsDouble();
        if (d.ContainsKey("attackDamage")) s.AttackDamage = (float)d["attackDamage"].AsDouble();
        if (d.ContainsKey("attackRange")) s.AttackRange = (float)d["attackRange"].AsDouble();
        if (d.ContainsKey("attackCooldown")) s.AttackCooldown = (float)d["attackCooldown"].AsDouble();
        if (d.ContainsKey("aggroRange")) s.AggroRange = (float)d["aggroRange"].AsDouble();
        if (d.ContainsKey("minAttackRange")) s.MinAttackRange = (float)d["minAttackRange"].AsDouble();
        if (d.ContainsKey("splashRadius")) s.SplashRadius = (float)d["splashRadius"].AsDouble();
        if (d.ContainsKey("canAttackAir")) s.CanAttackAir = d["canAttackAir"].AsBool();
        if (d.ContainsKey("autoDefend")) s.AutoDefend = d["autoDefend"].AsBool();
        if (d.ContainsKey("isAirUnit")) s.IsAirUnit = d["isAirUnit"].AsBool();
        if (d.ContainsKey("isHero")) s.IsHero = d["isHero"].AsBool();
        if (d.ContainsKey("maxPassengers")) s.MaxPassengers = (int)d["maxPassengers"].AsInt32();
        if (d.ContainsKey("terrainModType")) s.TerrainModType = d["terrainModType"].AsString();
        return s;
    }

    private static void LoadBuildings()
    {
        const string path = "res://data/buildings.json";
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"[GameData] 无法加载建筑数据: {path}");
            return;
        }
        var json = file.GetAsText();
        var parsed = Json.ParseString(json);
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            GD.PrintErr($"[GameData] 建筑数据解析失败: {path}");
            return;
        }
        var root = parsed.AsGodotDictionary();
        var buildings = root["buildings"].AsGodotDictionary();
        foreach (var key in buildings.Keys)
        {
            string typeName = key.AsString();
            if (!Enum.TryParse<BuildingType>(typeName, out var buildingType))
            {
                GD.PrintErr($"[GameData] 未知建筑类型: {typeName}");
                continue;
            }
            var entry = ParseBuildingEntry(buildings[key].AsGodotDictionary());
            _buildings[buildingType] = entry;
        }

        // 生产时间表
        if (root.ContainsKey("productionTimes"))
        {
            var times = root["productionTimes"].AsGodotDictionary();
            foreach (var key in times.Keys)
            {
                string typeName = key.AsString();
                // 跳过注释字段
                if (typeName.StartsWith("_")) continue;
                if (!Enum.TryParse<ProductionType>(typeName, out var prodType))
                {
                    GD.PrintErr($"[GameData] 未知生产类型: {typeName}");
                    continue;
                }
                _productionTimes[prodType] = (float)times[key].AsDouble();
            }
        }
    }

    private static BuildingEntry ParseBuildingEntry(Godot.Collections.Dictionary d)
    {
        var entry = new BuildingEntry
        {
            Name = d["name"].AsString(),
            Cost = (int)d["cost"].AsInt32()
        };
        if (d.ContainsKey("stats2d"))
            entry.Stats2D = ParseBuildingStats(d["stats2d"].AsGodotDictionary());
        if (d.ContainsKey("stats3d"))
            entry.Stats3D = ParseBuildingStats(d["stats3d"].AsGodotDictionary());
        return entry;
    }

    private static BuildingStats ParseBuildingStats(Godot.Collections.Dictionary d)
    {
        var s = new BuildingStats();
        if (d.ContainsKey("maxHealth")) s.MaxHealth = (float)d["maxHealth"].AsDouble();
        if (d.ContainsKey("powerProvided")) s.PowerProvided = (int)d["powerProvided"].AsInt32();
        if (d.ContainsKey("powerConsumed")) s.PowerConsumed = (int)d["powerConsumed"].AsInt32();
        if (d.ContainsKey("isDefensive")) s.IsDefensive = d["isDefensive"].AsBool();
        if (d.ContainsKey("attackDamage")) s.AttackDamage = (float)d["attackDamage"].AsDouble();
        if (d.ContainsKey("attackRange")) s.AttackRange = (float)d["attackRange"].AsDouble();
        if (d.ContainsKey("attackCooldown")) s.AttackCooldown = (float)d["attackCooldown"].AsDouble();
        if (d.ContainsKey("isRepairStation")) s.IsRepairStation = d["isRepairStation"].AsBool();
        if (d.ContainsKey("repairRadius")) s.RepairRadius = (float)d["repairRadius"].AsDouble();
        return s;
    }

    // ======== 访问器 ========

    /// <summary>获取单位数据条目。</summary>
    public static UnitEntry GetUnit(UnitType type)
    {
        EnsureLoaded();
        return _units.GetValueOrDefault(type) ?? throw new InvalidOperationException($"单位数据未加载: {type}");
    }

    /// <summary>获取建筑数据条目。</summary>
    public static BuildingEntry GetBuilding(BuildingType type)
    {
        EnsureLoaded();
        return _buildings.GetValueOrDefault(type) ?? throw new InvalidOperationException($"建筑数据未加载: {type}");
    }

    /// <summary>获取单位造价。</summary>
    public static int GetUnitCost(UnitType type)
    {
        EnsureLoaded();
        if (_units.TryGetValue(type, out var entry)) return entry.Cost;
        GD.PrintErr($"[GameData] 单位造价缺失: {type}，返回0");
        return 0;
    }

    /// <summary>获取建筑造价。</summary>
    public static int GetBuildingCost(BuildingType type)
    {
        EnsureLoaded();
        if (_buildings.TryGetValue(type, out var entry)) return entry.Cost;
        GD.PrintErr($"[GameData] 建筑造价缺失: {type}，返回0");
        return 0;
    }

    /// <summary>获取生产时间（秒）。</summary>
    public static float GetProductionTime(ProductionType type)
    {
        EnsureLoaded();
        if (_productionTimes.TryGetValue(type, out var time)) return time;
        GD.PrintErr($"[GameData] 生产时间缺失: {type}，返回3.0秒");
        return 3f;
    }

    // ======== 向后兼容：旧代码通过单位类型名获取造价（过渡期用）=====

    /// <summary>通过ProductionType获取单位造价（ProductionType与UnitType枚举值名一致）。</summary>
    public static int GetProductionCost(ProductionType prodType)
    {
        EnsureLoaded();
        // ProductionType和UnitType名称一一对应，通过名称桥接
        string name = prodType.ToString();
        if (Enum.TryParse<UnitType>(name, out var unitType))
            return GetUnitCost(unitType);
        GD.PrintErr($"[GameData] 无法将ProductionType映射为UnitType: {prodType}");
        return 0;
    }
}
