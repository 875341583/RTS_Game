using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// G1: 科技分支树系统 — 文明6风格科技树
/// 
/// 三个分支：
/// - 军事(Military): 解锁高级兵种、提升攻击力
/// - 经济(Economy): 提升采矿效率、降低成本、增加资金
/// - 防御(Defense): 解锁防御建筑、提升建筑血量、增加电力
/// 
/// 每个分支4层科技，每层需前置科技+科技中心+资金。
/// 玩家按Tab键打开科技树面板查看和研究。
/// 
/// P2-4: 数据驱动 — 从 res://data/techtree.json 加载科技节点定义，
/// 替代硬编码字典。JSON加载失败时回退到硬编码数据。
/// </summary>
public static class TechTree
{
    // ===== 科技ID枚举 =====
    public enum TechId
    {
        // 军事分支
        Mil_ArmorUpgrade,      // 装甲强化 — 所有坦克+15%血量
        Mil_AmmoUpgrade,       // 弹药升级 — 所有单位+15%攻击
        Mil_AdvancedTactics,   // 高级战术 — 解锁火箭炮/导弹车加成
        Mil_HeroTraining,      // 英雄训练 — 英雄生产成本-30%

        // 经济分支
        Eco_MiningEfficiency,  // 采矿效率 — 矿车采集速度+30%
        Eco_MassProduction,    // 批量生产 — 所有单位成本-15%
        Eco_ResourceNetwork,   // 资源网络 — 战略点收入+100%
        Eco_AdvancedLogistics, // 后勤优化 — 单位上限+8

        // 防御分支
        Def_Fortification,     // 筑城术 — 所有建筑+25%血量
        Def_PowerGrid,         // 电网优化 — 电站发电+50%
        Def_AdvancedTurrets,   // 高级炮塔 — 防御建筑射程+20%、伤害+20%
        Def_RepairSystems,     // 维修系统 — 建筑自动缓慢回血

        // P1-2: 阵营专属科技（仅对应阵营可研究）
        Fac_AirSuperiority,    // 同盟军专属：空中优势 — 空军伤害+15%
        Fac_NavalSupport,      // 同盟军专属：海军支援 — 海军生产速度+20%
        Fac_HeavyArmor,        // 苏维埃专属：重装甲 — 坦克生命+15%
        Fac_NuclearPower,      // 苏维埃专属：核能 — 电站发电+50%
        Fac_MindControl,       // 尤里专属：心灵控制 — 间谍/窃贼效率+30%
        Fac_StealthOps,        // 尤里专属：隐蔽行动 — 单位隐蔽时间+50%
    }

    // ===== 科技节点定义 =====
    public class TechNode
    {
        public TechId Id { get; init; }
        public string Name { get; init; } = "";
        public string Branch { get; init; } = "";  // "military"/"economy"/"defense"
        public int Tier { get; init; }              // 1-4
        public int Cost { get; init; }             // 研究资金
        public float ResearchTime { get; init; }   // 研究时间(秒)
        public string Description { get; init; } = "";
        public TechId[] Prerequisites { get; init; } = System.Array.Empty<TechId>();
        public bool RequiresTechCenter { get; init; } = true;
    }

    // ===== P2-4: 从JSON加载的科技节点 =====
    private static readonly Dictionary<TechId, TechNode> _nodes = new();
    private static readonly object _nodesLock = new();
    private static bool _alwaysFallback = false;

    /// <summary>强制使用硬编码数据（供单元测试使用，在无Godot运行时的环境中调用）</summary>
    public static void SetAlwaysFallback(bool value) => _alwaysFallback = value;

    /// <summary>所有科技节点（P2-4: 优先从JSON加载，失败则用硬编码fallback）</summary>
    public static IReadOnlyDictionary<TechId, TechNode> Nodes
    {
        get
        {
            lock (_nodesLock)
            {
                if (_nodes.Count == 0) LoadFromJsonCore(_alwaysFallback);
                return _nodes;
            }
        }
    }

    /// <summary>P2-4: 从 res://data/techtree.json 加载科技节点。
    /// forceFallback=true时跳过Godot IO，直接用硬编码数据（供单元测试使用）。</summary>
    public static void LoadFromJson(bool forceFallback = false)
    {
        lock (_nodesLock)
        {
            if (_nodes.Count > 0) return; // 已加载，无论fallback还是JSON都跳过
            LoadFromJsonCore(forceFallback);
        }
    }

    /// <summary>内部加载实现（调用方需持有 _nodesLock）</summary>
    private static void LoadFromJsonCore(bool forceFallback)
    {
        _nodes.Clear();
        
        if (forceFallback)
        {
            LoadFallback();
            return;
        }
        
        // P2-4: 通过ModLoader读取，支持Mod覆盖
        var jsonText = ModLoader.ReadDataFile("techtree.json");
        if (string.IsNullOrEmpty(jsonText))
        {
            GameLog.Warning("[TechTree] cannot read techtree.json, using hardcoded fallback");
            LoadFallback();
            return;
        }

        var jsonResult = Json.ParseString(jsonText);
        if (jsonResult.VariantType != Variant.Type.Array)
        {
            GameLog.Warning("[TechTree] techtree.json format error, using hardcoded fallback");
            LoadFallback();
            return;
        }

        var array = jsonResult.AsGodotArray();
        foreach (var entry in array)
        {
            var dict = entry.AsGodotDictionary();
            if (dict == null) continue;

            var idStr = dict.ContainsKey("id") ? dict["id"].AsString() : "";
            if (string.IsNullOrEmpty(idStr)) continue;
            if (!System.Enum.TryParse<TechId>(idStr, out var id))
            {
                GameLog.Warning($"[TechTree] unknown tech ID: {idStr}");
                continue;
            }

            var prereqs = new List<TechId>();
            if (dict.ContainsKey("prerequisites") && dict["prerequisites"].VariantType == Variant.Type.Array)
            {
                foreach (var p in dict["prerequisites"].AsGodotArray())
                {
                    if (System.Enum.TryParse<TechId>(p.AsString(), out var pid))
                        prereqs.Add(pid);
                }
            }

            var node = new TechNode
            {
                Id = id,
                Name = dict["name"].AsString(),
                Branch = dict["branch"].AsString(),
                Tier = (int)dict["tier"].AsInt64(),
                Cost = (int)dict["cost"].AsInt64(),
                ResearchTime = (float)dict["researchTime"].AsDouble(),
                Description = dict["description"].AsString(),
                RequiresTechCenter = dict.ContainsKey("requiresTechCenter")
                    && dict["requiresTechCenter"].AsBool(),
                Prerequisites = prereqs.ToArray(),
            };
            _nodes[id] = node;
        }

        GameLog.Info($"[TechTree] loaded {_nodes.Count} tech nodes from JSON");
    }

    /// <summary>P2-4: 硬编码fallback（JSON加载失败时使用）</summary>
    private static void LoadFallback()
    {
        _nodes.Clear();
        // 军事分支
        _nodes[TechId.Mil_ArmorUpgrade] = new TechNode
        {
            Id = TechId.Mil_ArmorUpgrade, Name = TrManager.Tr("tech.name.mil_armor_upgrade"), Branch = "military", Tier = 1,
            Cost = 500, ResearchTime = 30f, RequiresTechCenter = false,
            Description = TrManager.Tr("tech.desc.mil_armor_upgrade")
        };
        _nodes[TechId.Mil_AmmoUpgrade] = new TechNode
        {
            Id = TechId.Mil_AmmoUpgrade, Name = TrManager.Tr("tech.name.mil_ammo_upgrade"), Branch = "military", Tier = 2,
            Cost = 800, ResearchTime = 45f,
            Prerequisites = new[]{ TechId.Mil_ArmorUpgrade },
            Description = TrManager.Tr("tech.desc.mil_ammo_upgrade")
        };
        _nodes[TechId.Mil_AdvancedTactics] = new TechNode
        {
            Id = TechId.Mil_AdvancedTactics, Name = TrManager.Tr("tech.name.mil_advanced_tactics"), Branch = "military", Tier = 3,
            Cost = 1200, ResearchTime = 60f,
            Prerequisites = new[]{ TechId.Mil_AmmoUpgrade },
            Description = TrManager.Tr("tech.desc.mil_advanced_tactics")
        };
        _nodes[TechId.Mil_HeroTraining] = new TechNode
        {
            Id = TechId.Mil_HeroTraining, Name = TrManager.Tr("tech.name.mil_hero_training"), Branch = "military", Tier = 4,
            Cost = 1500, ResearchTime = 75f,
            Prerequisites = new[]{ TechId.Mil_AdvancedTactics },
            Description = TrManager.Tr("tech.desc.mil_hero_training")
        };

        // 经济分支
        _nodes[TechId.Eco_MiningEfficiency] = new TechNode
        {
            Id = TechId.Eco_MiningEfficiency, Name = TrManager.Tr("tech.name.eco_mining_efficiency"), Branch = "economy", Tier = 1,
            Cost = 400, ResearchTime = 25f, RequiresTechCenter = false,
            Description = TrManager.Tr("tech.desc.eco_mining_efficiency")
        };
        _nodes[TechId.Eco_MassProduction] = new TechNode
        {
            Id = TechId.Eco_MassProduction, Name = TrManager.Tr("tech.name.eco_mass_production"), Branch = "economy", Tier = 2,
            Cost = 700, ResearchTime = 40f,
            Prerequisites = new[]{ TechId.Eco_MiningEfficiency },
            Description = TrManager.Tr("tech.desc.eco_mass_production")
        };
        _nodes[TechId.Eco_ResourceNetwork] = new TechNode
        {
            Id = TechId.Eco_ResourceNetwork, Name = TrManager.Tr("tech.name.eco_resource_network"), Branch = "economy", Tier = 3,
            Cost = 1000, ResearchTime = 50f,
            Prerequisites = new[]{ TechId.Eco_MassProduction },
            Description = TrManager.Tr("tech.desc.eco_resource_network")
        };
        _nodes[TechId.Eco_AdvancedLogistics] = new TechNode
        {
            Id = TechId.Eco_AdvancedLogistics, Name = TrManager.Tr("tech.name.eco_advanced_logistics"), Branch = "economy", Tier = 4,
            Cost = 1300, ResearchTime = 65f,
            Prerequisites = new[]{ TechId.Eco_ResourceNetwork },
            Description = TrManager.Tr("tech.desc.eco_advanced_logistics")
        };

        // 防御分支
        _nodes[TechId.Def_Fortification] = new TechNode
        {
            Id = TechId.Def_Fortification, Name = TrManager.Tr("tech.name.def_fortification"), Branch = "defense", Tier = 1,
            Cost = 450, ResearchTime = 28f, RequiresTechCenter = false,
            Description = TrManager.Tr("tech.desc.def_fortification")
        };
        _nodes[TechId.Def_PowerGrid] = new TechNode
        {
            Id = TechId.Def_PowerGrid, Name = TrManager.Tr("tech.name.def_power_grid"), Branch = "defense", Tier = 2,
            Cost = 650, ResearchTime = 35f,
            Prerequisites = new[]{ TechId.Def_Fortification },
            Description = TrManager.Tr("tech.desc.def_power_grid")
        };
        _nodes[TechId.Def_AdvancedTurrets] = new TechNode
        {
            Id = TechId.Def_AdvancedTurrets, Name = TrManager.Tr("tech.name.def_advanced_turrets"), Branch = "defense", Tier = 3,
            Cost = 900, ResearchTime = 50f,
            Prerequisites = new[]{ TechId.Def_PowerGrid },
            Description = TrManager.Tr("tech.desc.def_advanced_turrets")
        };
        _nodes[TechId.Def_RepairSystems] = new TechNode
        {
            Id = TechId.Def_RepairSystems, Name = TrManager.Tr("tech.name.def_repair_systems"), Branch = "defense", Tier = 4,
            Cost = 1200, ResearchTime = 60f,
            Prerequisites = new[]{ TechId.Def_AdvancedTurrets },
            Description = TrManager.Tr("tech.desc.def_repair_systems")
        };

        // P1-2: 阵营专属科技（硬编码后备，JSON加载时覆盖）
        _nodes[TechId.Fac_AirSuperiority] = new TechNode
        {
            Id = TechId.Fac_AirSuperiority, Name = TrManager.Tr("tech.name.fac_air_superiority"), Branch = "faction_special", Tier = 1,
            Cost = 800, ResearchTime = 40f, RequiresTechCenter = true,
            Description = TrManager.Tr("tech.desc.fac_air_superiority")
        };
        _nodes[TechId.Fac_NavalSupport] = new TechNode
        {
            Id = TechId.Fac_NavalSupport, Name = TrManager.Tr("tech.name.fac_naval_support"), Branch = "faction_special", Tier = 2,
            Cost = 900, ResearchTime = 45f, RequiresTechCenter = true,
            Prerequisites = new[]{ TechId.Fac_AirSuperiority },
            Description = TrManager.Tr("tech.desc.fac_naval_support")
        };
        _nodes[TechId.Fac_HeavyArmor] = new TechNode
        {
            Id = TechId.Fac_HeavyArmor, Name = TrManager.Tr("tech.name.fac_heavy_armor"), Branch = "faction_special", Tier = 1,
            Cost = 800, ResearchTime = 40f, RequiresTechCenter = true,
            Description = TrManager.Tr("tech.desc.fac_heavy_armor")
        };
        _nodes[TechId.Fac_NuclearPower] = new TechNode
        {
            Id = TechId.Fac_NuclearPower, Name = TrManager.Tr("tech.name.fac_nuclear_power"), Branch = "faction_special", Tier = 2,
            Cost = 900, ResearchTime = 45f, RequiresTechCenter = true,
            Prerequisites = new[]{ TechId.Fac_HeavyArmor },
            Description = TrManager.Tr("tech.desc.fac_nuclear_power")
        };
        _nodes[TechId.Fac_MindControl] = new TechNode
        {
            Id = TechId.Fac_MindControl, Name = TrManager.Tr("tech.name.fac_mind_control"), Branch = "faction_special", Tier = 1,
            Cost = 800, ResearchTime = 40f, RequiresTechCenter = true,
            Description = TrManager.Tr("tech.desc.fac_mind_control")
        };
        _nodes[TechId.Fac_StealthOps] = new TechNode
        {
            Id = TechId.Fac_StealthOps, Name = TrManager.Tr("tech.name.fac_stealth_ops"), Branch = "faction_special", Tier = 2,
            Cost = 900, ResearchTime = 45f, RequiresTechCenter = true,
            Prerequisites = new[]{ TechId.Fac_MindControl },
            Description = TrManager.Tr("tech.desc.fac_stealth_ops")
        };
    }

    /// <summary>检查科技是否已研究。</summary>
    public static bool IsResearched(HashSet<TechId> completed, TechId id) => completed.Contains(id);

    /// <summary>检查科技是否可以研究（前置条件+科技中心+资金+阵营专属）。</summary>
    public static bool CanResearch(HashSet<TechId> completed, TechId id, bool hasTechCenter, int money, string? factionId = null)
    {
        var node = Nodes[id];
        if (completed.Contains(id)) return false;
        if (node.RequiresTechCenter && !hasTechCenter) return false;
        if (money < node.Cost) return false;
        foreach (var pre in node.Prerequisites)
            if (!completed.Contains(pre)) return false;
        // P1-2: 阵营专属科技检查
        var exclusive = GetFactionExclusiveTech(factionId);
        if (exclusive != null && exclusive.Count > 0)
        {
            // 该科技是某阵营专属，但当前阵营不匹配
            if (IsFactionExclusiveTech(id) && !exclusive.Contains(id))
                return false;
        }
        return true;
    }

    /// <summary>P1-2: 检查科技是否是阵营专属科技。</summary>
    public static bool IsFactionExclusiveTech(TechId id) => id switch
    {
        TechId.Fac_AirSuperiority or TechId.Fac_NavalSupport or
        TechId.Fac_HeavyArmor or TechId.Fac_NuclearPower or
        TechId.Fac_MindControl or TechId.Fac_StealthOps => true,
        _ => false,
    };

    /// <summary>P1-2: 获取指定阵营可研究的专属科技列表。factionId 为null时返回空。</summary>
    public static HashSet<TechId>? GetFactionExclusiveTech(string? factionId) => factionId switch
    {
        "Allies" => _alliesExclusive,
        "Soviet" => _sovietExclusive,
        "Yuri" => _yuriExclusive,
        _ => null,
    };

    private static readonly HashSet<TechId> _alliesExclusive = new()
    {
        TechId.Fac_AirSuperiority, TechId.Fac_NavalSupport,
    };
    private static readonly HashSet<TechId> _sovietExclusive = new()
    {
        TechId.Fac_HeavyArmor, TechId.Fac_NuclearPower,
    };
    private static readonly HashSet<TechId> _yuriExclusive = new()
    {
        TechId.Fac_MindControl, TechId.Fac_StealthOps,
    };

    /// <summary>获取科技在某分支的第tier层节点。</summary>
    public static TechNode? GetByBranchTier(string branch, int tier)
    {
        foreach (var kv in Nodes)
            if (kv.Value.Branch == branch && kv.Value.Tier == tier)
                return kv.Value;
        return null;
    }
}

/// <summary>
/// 每个阵营的科技研究状态。
/// </summary>
public class TechProgress
{
    public HashSet<TechTree.TechId> Completed { get; } = new();
    public TechTree.TechId? CurrentlyResearching { get; private set; }
    public float ResearchTimer { get; private set; }
    public TechTree.TechId? QueuedTech { get; private set; }

    /// <summary>开始研究某科技（不检查条件，调用方需先CanResearch）。</summary>
    public void StartResearch(TechTree.TechId id)
    {
        CurrentlyResearching = id;
        ResearchTimer = TechTree.Nodes[id].ResearchTime;
    }

    /// <summary>每帧更新研究进度。返回研究完成的TechId，无则返回null。</summary>
    public TechTree.TechId? UpdateResearch(float dt)
    {
        if (!CurrentlyResearching.HasValue) return null;
        ResearchTimer -= dt;
        if (ResearchTimer <= 0f)
        {
            var completed = CurrentlyResearching.Value;
            Completed.Add(completed);
            CurrentlyResearching = null;
            ResearchTimer = 0f;
            return completed;
        }
        return null;
    }

    /// <summary>研究进度 0~1。</summary>
    public float Progress => CurrentlyResearching.HasValue && TechTree.Nodes[CurrentlyResearching.Value].ResearchTime > 0f
        ? Mathf.Clamp(1f - ResearchTimer / TechTree.Nodes[CurrentlyResearching.Value].ResearchTime, 0f, 1f)
        : 0f;

    /// <summary>G5: 尤里卡强制完成当前研究（清空状态，不加到Completed）。</summary>
    public void ForceClearResearch()
    {
        CurrentlyResearching = null;
        ResearchTimer = 0f;
    }

    /// <summary>G5: 尤里卡强制完成指定科技（加入Completed，不影响当前研究）。</summary>
    public void ForceComplete(TechTree.TechId id)
    {
        if (CurrentlyResearching == id)
        {
            CurrentlyResearching = null;
            ResearchTimer = 0f;
        }
        Completed.Add(id);
    }

    // ==================== P0-2: 存档/读档 恢复方法 ====================

    /// <summary>P0-2 读档：清空已完成科技和正在研究状态（不重置QueuedTech，由SetQueuedTech单独设置）。</summary>
    public void Clear()
    {
        Completed.Clear();
        CurrentlyResearching = null;
        ResearchTimer = 0f;
        QueuedTech = null;
    }

    /// <summary>P0-2 读档：恢复正在研究的科技与剩余时间（不经过StartResearch的完整初始化）。</summary>
    public void RestoreResearching(TechTree.TechId id, float timer)
    {
        CurrentlyResearching = id;
        ResearchTimer = Mathf.Max(0f, timer);
    }

    /// <summary>P0-2 读档：恢复排队中的下一项科技。</summary>
    public void SetQueuedTech(TechTree.TechId id)
    {
        QueuedTech = id;
    }
}
