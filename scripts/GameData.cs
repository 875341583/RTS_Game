using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

// ============================================================================
// P1-5 第3步：渲染分层常量
// 基于红警2画家算法 + Godot ZIndex。CanvasLayer 仅用于 UI（脱离世界坐标），
// 游戏世界一切可见物通过 ZIndex 在同一 World2D 中分层，保证物理/碰撞正常。
// ============================================================================

/// <summary>
/// 2D 渲染层 ZIndex 基准常量。红警2 画家算法核心：值越大越靠前绘制（盖在上方）。
/// Y-Sort 对象层使用动态 ZIndex = UnitBase + (int)(Y / 2)。
/// </summary>
public static class RenderLayer
{
    /// <summary>地形层基线：背景/网格线。最底层。</summary>
    public const int Terrain = -100;
    /// <summary>对象层基线：单位/建筑 Y-Sort 动态起始值。</summary>
    public const int UnitBase = 1000;
    /// <summary>特效层基线：炮口闪光/炮弹/爆炸。始终在单位之上。</summary>
    public const int Effect = 2000;
    /// <summary>迷雾层基线：战争迷雾覆盖（预留）。</summary>
    public const int Shroud = 3000;
    // UI 层使用 CanvasLayer（脱离世界坐标），不参与 ZIndex 排序。
}

// ============================================================================
// P2-1: 游戏逻辑常量 — 替代散布在代码中的魔法数字
// ============================================================================

/// <summary>游戏核心常量：经济、单位上限、地图尺寸等</summary>
public static class GameConst
{
    // === 经济 ===
    /// <summary>初始资金</summary>
    public const int StartingMoney = 5000;
    /// <summary>最低资金（低于此值显示警告）</summary>
    public const int LowMoneyThreshold = 500;
    /// <summary>建筑变卖退款比例</summary>
    public const float SellRefundRatio = 0.5f;

    // === 单位 ===
    /// <summary>默认单位上限</summary>
    public const int DefaultUnitCap = 50;
    /// <summary>单位上限软上限倍率（实际=Base*倍率）</summary>
    public const float UnitCapMultiplier = 1.0f;

    // === 地图 ===
    /// <summary>地图尺寸选项（P2-2: 委托给 MapConfig.SizePreset）</summary>
    public static readonly int[] MapSizeOptions = { 32, 64, 96 };
    /// <summary>战略点每16x16格1个</summary>
    public const int StrategicPointInterval = 16;

    // === 战斗 ===
    /// <summary>闪电风暴伤害</summary>
    public const int LightningDamage = 200;
    /// <summary>核弹伤害</summary>
    public const int NukeDamage = 500;
    /// <summary>导弹伤害</summary>
    public const int MissileDamage = 300;
    /// <summary>超武最小距离（防自伤）</summary>
    public const float SuperWeaponMinSafeDistance = 200f;

    // === 超武冷却（秒） ===
    /// <summary>核弹冷却时间</summary>
    public const float NukeCooldown = 300f;
    /// <summary>闪电风暴冷却时间</summary>
    public const float LightningCooldown = 240f;
    /// <summary>导弹冷却时间</summary>
    public const float MissileCooldown = 180f;

    // === 超武范围 ===
    /// <summary>核弹爆炸半径</summary>
    public const float NukeRadius = 260f;
    /// <summary>闪电风暴半径</summary>
    public const float LightningRadius = 160f;
    /// <summary>导弹爆炸半径</summary>
    public const float MissileRadius = 180f;

    // === 超武持续参数 ===
    /// <summary>闪电风暴每秒伤害</summary>
    public const float LightningDps = 80f;
    /// <summary>闪电风暴持续时间</summary>
    public const float LightningDuration = 5f;

    // === 电力 ===
    /// <summary>低电力警告阈值比（电力/需求 &lt; 此值）</summary>
    public const float LowPowerRatio = 0.8f;

    // === 时间 ===
    /// <summary>AI思考间隔（秒）</summary>
    public const float AiThinkInterval = 2.0f;
    /// <summary>战术卡选择倒计时（秒）</summary>
    public const int TacticalCardCountdown = 5;

    // === 渲染/UI ===
    /// <summary>等距图块宽度（像素）</summary>
    public const int IsoTileWidth = 90;
    /// <summary>等距图块高度（像素）</summary>
    public const int IsoTileHeight = 60;
    /// <summary>单位精灵尺寸</summary>
    public const int UnitSpriteSize = 128;
    /// <summary>建筑精灵尺寸</summary>
    public const int BuildingSpriteSize = 256;
    /// <summary>小地图尺寸</summary>
    public const int MinimapSize = 200;
}

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

    // ======== 共享常量（P1-5第1步：消除2D/3D重复定义）========

    /// <summary>
    /// 8阵营色调色板（基于红警2原版8色，明度/色相优化辨识度）。
    /// 灰底素材用 Modulate 染色（2D）/ 材质染色（3D）。索引=TeamId。超出范围由调用方取模。
    /// 原 Unit.TeamPalette 与 Unit3D.TeamPalette 的唯一共用数据源。
    /// </summary>
    public static readonly Color[] TeamPalette =
    {
        new(0.82f, 0.16f, 0.16f), // 0 Red   纯红
        new(0.16f, 0.32f, 0.82f), // 1 Blue  深蓝
        new(0.18f, 0.78f, 0.22f), // 2 Green 纯绿（亮）
        new(0.95f, 0.82f, 0.18f), // 3 Yellow 明黄
        new(0.95f, 0.42f, 0.78f), // 4 Pink  亮粉（明度高）
        new(0.44f, 0.18f, 0.72f), // 5 Purple 深紫（明度低）
        new(0.95f, 0.51f, 0.12f), // 6 Orange 亮橙
        new(0.14f, 0.62f, 0.88f), // 7 Cyan  偏蓝青（与2纯绿拉大色相差）
    };

    /// <summary>获取 TeamId 对应的阵营色。优先从 FactionManager 取色（阵营差异化），超出阵营数后回退到 TeamPalette 取模。</summary>
    public static Color GetTeamColor(int teamId)
    {
        if (FactionManager.IsLoaded && teamId < FactionManager.Count)
            return FactionManager.GetFactionForTeam(teamId).Color;
        return TeamPalette[((teamId % TeamPalette.Length) + TeamPalette.Length) % TeamPalette.Length];
    }

    // ======== 缓存 ========

    private static readonly Dictionary<UnitType, UnitEntry> _units = new();
    private static readonly Dictionary<BuildingType, BuildingEntry> _buildings = new();
    private static readonly Dictionary<ProductionType, float> _productionTimes = new();
    private static readonly Dictionary<ProductionType, float> _productionTimes3d = new();
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
        GameLog.Debug($"[GameData] 数据加载完成: {_units.Count}单位, {_buildings.Count}建筑, {_productionTimes.Count}生产时间(2D), {_productionTimes3d.Count}生产时间(3D)");
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
            GameLog.Error($"[GameData] 无法加载单位数据: {path}");
            return;
        }
        var json = file.GetAsText();
        var parsed = Json.ParseString(json);
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            GameLog.Error($"[GameData] 单位数据解析失败: {path}");
            return;
        }
        var root = parsed.AsGodotDictionary();
        var units = root["units"].AsGodotDictionary();
        foreach (var key in units.Keys)
        {
            string typeName = key.AsString();
            if (!Enum.TryParse<UnitType>(typeName, out var unitType))
            {
                GameLog.Error($"[GameData] 未知单位类型: {typeName}");
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
            GameLog.Error($"[GameData] 无法加载建筑数据: {path}");
            return;
        }
        var json = file.GetAsText();
        var parsed = Json.ParseString(json);
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            GameLog.Error($"[GameData] 建筑数据解析失败: {path}");
            return;
        }
        var root = parsed.AsGodotDictionary();
        var buildings = root["buildings"].AsGodotDictionary();
        foreach (var key in buildings.Keys)
        {
            string typeName = key.AsString();
            if (!Enum.TryParse<BuildingType>(typeName, out var buildingType))
            {
                GameLog.Error($"[GameData] 未知建筑类型: {typeName}");
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
                    GameLog.Error($"[GameData] 未知生产类型: {typeName}");
                    continue;
                }
                _productionTimes[prodType] = (float)times[key].AsDouble();
            }
        }

        // 3D版生产时间表
        if (root.ContainsKey("productionTimes3d"))
        {
            var times3d = root["productionTimes3d"].AsGodotDictionary();
            foreach (var key in times3d.Keys)
            {
                string typeName = key.AsString();
                if (typeName.StartsWith("_")) continue;
                if (!Enum.TryParse<ProductionType>(typeName, out var prodType))
                {
                    GameLog.Error($"[GameData] 未知3D生产类型: {typeName}");
                    continue;
                }
                _productionTimes3d[prodType] = (float)times3d[key].AsDouble();
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
        GameLog.Error($"[GameData] 单位造价缺失: {type}，返回0");
        return 0;
    }

    /// <summary>获取建筑造价。</summary>
    public static int GetBuildingCost(BuildingType type)
    {
        EnsureLoaded();
        if (_buildings.TryGetValue(type, out var entry)) return entry.Cost;
        GameLog.Error($"[GameData] 建筑造价缺失: {type}，返回0");
        return 0;
    }

    /// <summary>获取生产时间（秒）。is3d=true时返回3D版时间。</summary>
    public static float GetProductionTime(ProductionType type, bool is3d = false)
    {
        EnsureLoaded();
        var dict = is3d ? _productionTimes3d : _productionTimes;
        if (dict.TryGetValue(type, out var time)) return time;
        GameLog.Error($"[GameData] 生产时间缺失: {type}(3D={is3d})，返回3.0秒");
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
        GameLog.Error($"[GameData] 无法将ProductionType映射为UnitType: {prodType}");
        return 0;
    }
}
