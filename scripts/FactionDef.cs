using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// P1-2: 阵营定义。将原8色阵营升级为差异化阵营。
/// 每个阵营有：颜色、数值乘数（生命/伤害/速度/成本）、可用单位/建筑白名单、专属科技。
///
/// teamId 0=玩家，1..N=AI。每个teamId绑定一个FactionDef。
/// 默认3个阵营（同盟军/苏维埃/尤里军团），但teamId可以超过3（8色兼容）。
/// 超出阵营数的teamId会循环复用阵营定义（保持颜色多样性）。
/// </summary>
public class FactionDef
{
    /// <summary>阵营内部ID（如"Allies"）。</summary>
    public string Id = "";
    /// <summary>阵营显示名（如"同盟军"）。</summary>
    public string Name = "";
    /// <summary>阵营描述。</summary>
    public string Description = "";
    /// <summary>阵营主色调。</summary>
    public Color Color;
    /// <summary>数值乘数。</summary>
    public StatMultipliers Multipliers = new();
    /// <summary>可用单位类型白名单（null=全部可用）。</summary>
    public HashSet<UnitType>? AvailableUnits;
    /// <summary>可用建筑类型白名单（null=全部可用）。</summary>
    public HashSet<BuildingType>? AvailableBuildings;
    /// <summary>专属科技ID列表。</summary>
    public List<string> ExclusiveTechs = new();

    /// <summary>数值乘数。</summary>
    public class StatMultipliers
    {
        public float Health = 1f;
        public float Damage = 1f;
        public float Speed = 1f;
        public float Cost = 1f;
    }

    /// <summary>该阵营是否可生产指定单位。</summary>
    public bool CanProduceUnit(UnitType type)
        => AvailableUnits == null || AvailableUnits.Contains(type);

    /// <summary>该阵营是否可建造指定建筑。</summary>
    public bool CanBuild(BuildingType type)
        => AvailableBuildings == null || AvailableBuildings.Contains(type);

    /// <summary>应用生命值乘数。</summary>
    public float ApplyHealth(float baseValue) => baseValue * Multipliers.Health;

    /// <summary>应用伤害乘数。</summary>
    public float ApplyDamage(float baseValue) => baseValue * Multipliers.Damage;

    /// <summary>应用速度乘数。</summary>
    public float ApplySpeed(float baseValue) => baseValue * Multipliers.Speed;

    /// <summary>应用成本乘数（四舍五入到整数）。</summary>
    public int ApplyCost(int baseValue) => Mathf.RoundToInt(baseValue * Multipliers.Cost);
}

/// <summary>
/// P1-2: 阵营管理器。加载factions.json，为每个teamId分配阵营。
/// </summary>
public static class FactionManager
{
    private static readonly Dictionary<string, FactionDef> _factions = new();
    private static readonly List<FactionDef> _factionList = new();
    private static string _defaultFactionId = "Allies";
    private static bool _loaded = false;

    /// <summary>P1-2: 玩家阵营覆盖。设置后 GetFactionForTeam(PlayerTeamId) 返回此阵营而非默认映射。</summary>
    private static string? _playerFactionId = null;
    /// <summary>本地玩家的teamId（单机=0，联机=NetworkManager.LocalTeamId）。</summary>
    private static int _playerTeamId = 0;

    /// <summary>是否已加载。</summary>
    public static bool IsLoaded => _loaded;

    /// <summary>阵营总数。</summary>
    public static int Count
    {
        get { EnsureLoaded(); return _factionList.Count; }
    }

    /// <summary>加载factions.json。</summary>
    public static void Load()
    {
        if (_loaded) return;
        const string path = "res://data/factions.json";
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GameLog.Error($"[FactionManager] 无法加载阵营配置: {path}");
            // 降级：创建默认阵营
            CreateDefaultFactions();
            _loaded = true;
            return;
        }
        var json = file.GetAsText();
        var parsed = Json.ParseString(json);
        if (parsed.VariantType != Variant.Type.Dictionary)
        {
            GameLog.Error($"[FactionManager] 阵营配置解析失败");
            CreateDefaultFactions();
            _loaded = true;
            return;
        }
        var root = parsed.AsGodotDictionary();
        if (root.ContainsKey("defaultFaction"))
            _defaultFactionId = root["defaultFaction"].AsString();

        var factions = root["factions"].AsGodotDictionary();
        foreach (var key in factions.Keys)
        {
            string id = key.AsString();
            var def = ParseFaction(id, factions[key].AsGodotDictionary());
            _factions[id] = def;
            _factionList.Add(def);
        }
        _loaded = true;
        GameLog.Debug($"[FactionManager] 阵营加载完成: {_factionList.Count}个阵营（默认: {_defaultFactionId}）");
    }

    private static void EnsureLoaded()
    {
        if (!_loaded) Load();
    }

    private static FactionDef ParseFaction(string id, Godot.Collections.Dictionary d)
    {
        var def = new FactionDef { Id = id };
        if (d.ContainsKey("name")) def.Name = d["name"].AsString();
        if (d.ContainsKey("description")) def.Description = d["description"].AsString();
        if (d.ContainsKey("color"))
        {
            var c = d["color"].AsGodotDictionary();
            def.Color = new Color(
                (float)c["r"].AsDouble(),
                (float)c["g"].AsDouble(),
                (float)c["b"].AsDouble()
            );
        }
        if (d.ContainsKey("statMultipliers"))
        {
            var m = d["statMultipliers"].AsGodotDictionary();
            if (m.ContainsKey("health")) def.Multipliers.Health = (float)m["health"].AsDouble();
            if (m.ContainsKey("damage")) def.Multipliers.Damage = (float)m["damage"].AsDouble();
            if (m.ContainsKey("speed")) def.Multipliers.Speed = (float)m["speed"].AsDouble();
            if (m.ContainsKey("cost")) def.Multipliers.Cost = (float)m["cost"].AsDouble();
        }
        if (d.ContainsKey("availableUnits"))
        {
            def.AvailableUnits = new HashSet<UnitType>();
            var arr = d["availableUnits"].AsGodotArray();
            foreach (var v in arr)
            {
                string name = v.AsString();
                if (Enum.TryParse<UnitType>(name, out var ut))
                    def.AvailableUnits.Add(ut);
                else
                    GameLog.Error($"[FactionManager] 阵营{id}未知单位类型: {name}");
            }
        }
        if (d.ContainsKey("availableBuildings"))
        {
            def.AvailableBuildings = new HashSet<BuildingType>();
            var arr = d["availableBuildings"].AsGodotArray();
            foreach (var v in arr)
            {
                string name = v.AsString();
                if (Enum.TryParse<BuildingType>(name, out var bt))
                    def.AvailableBuildings.Add(bt);
                else
                    GameLog.Error($"[FactionManager] 阵营{id}未知建筑类型: {name}");
            }
        }
        if (d.ContainsKey("exclusiveTechs"))
        {
            var arr = d["exclusiveTechs"].AsGodotArray();
            foreach (var v in arr)
                def.ExclusiveTechs.Add(v.AsString());
        }
        return def;
    }

    /// <summary>降级方案：JSON加载失败时创建3个默认阵营。</summary>
    private static void CreateDefaultFactions()
    {
        var allies = new FactionDef
        {
            Id = "Allies", Name = TrManager.Tr("faction.allies.name"),
            Color = new Color(0.16f, 0.32f, 0.82f)
        };
        var soviet = new FactionDef
        {
            Id = "Soviet", Name = TrManager.Tr("faction.soviet.name"),
            Color = new Color(0.82f, 0.16f, 0.16f),
            Multipliers = new FactionDef.StatMultipliers { Health = 1.2f, Damage = 1.1f, Speed = 0.9f }
        };
        var yuri = new FactionDef
        {
            Id = "Yuri", Name = TrManager.Tr("faction.yuri.name"),
            Color = new Color(0.44f, 0.18f, 0.72f),
            Multipliers = new FactionDef.StatMultipliers { Health = 0.9f, Speed = 1.1f, Cost = 0.9f }
        };
        _factions["Allies"] = allies; _factionList.Add(allies);
        _factions["Soviet"] = soviet; _factionList.Add(soviet);
        _factions["Yuri"] = yuri; _factionList.Add(yuri);
    }

    // ======== 访问器 ========

    /// <summary>通过阵营ID获取阵营定义。</summary>
    public static FactionDef? GetFaction(string id)
    {
        EnsureLoaded();
        return _factions.GetValueOrDefault(id);
    }

    /// <summary>通过teamId获取阵营定义（循环复用）。
    /// 本地玩家的teamId（可单机=0，联机=任意）使用_playerFactionId覆盖。
    /// 其余teamId循环复用阵营列表。
    /// 注意：调用 SetPlayerTeamId 设置本地玩家teamId。</summary>
    public static FactionDef GetFactionForTeam(int teamId)
    {
        EnsureLoaded();
        if (_factionList.Count == 0) throw new InvalidOperationException("无阵营定义");
        if (teamId == _playerTeamId && _playerFactionId != null && _factions.TryGetValue(_playerFactionId, out var pf))
            return pf;
        return _factionList[teamId % _factionList.Count];
    }

    /// <summary>设置本地玩家teamId（联机模式由Main调用）。</summary>
    public static void SetPlayerTeamId(int teamId)
    {
        _playerTeamId = teamId;
    }

    /// <summary>P1-2: 设置玩家阵营（由 GameSession.PlayerFactionId 驱动）。</summary>
    public static void SetPlayerFaction(string factionId)
    {
        _playerFactionId = factionId;
        GameLog.Debug($"[FactionManager] 玩家阵营设为: {factionId}");
    }

    /// <summary>获取默认阵营。</summary>
    public static FactionDef GetDefaultFaction()
    {
        EnsureLoaded();
        if (_factions.TryGetValue(_defaultFactionId, out var def)) return def;
        return _factionList[0];
    }

    /// <summary>获取所有阵营定义列表。</summary>
    public static IReadOnlyList<FactionDef> GetAllFactions()
    {
        EnsureLoaded();
        return _factionList;
    }
}
