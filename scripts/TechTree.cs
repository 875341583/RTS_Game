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
    }

    // ===== 科技节点定义 =====
    public class TechNode
    {
        public TechId Id { get; init; }
        public string Name { get; init; } = "";
        public string Branch { get; init; } = "";  // "军事"/"经济"/"防御"
        public int Tier { get; init; }              // 1-4
        public int Cost { get; init; }             // 研究资金
        public float ResearchTime { get; init; }   // 研究时间(秒)
        public string Description { get; init; } = "";
        public TechId[] Prerequisites { get; init; } = System.Array.Empty<TechId>();
        public bool RequiresTechCenter { get; init; } = true;
    }

    // ===== 所有科技节点 =====
    public static readonly Dictionary<TechId, TechNode> Nodes = new()
    {
        // 军事分支
        [TechId.Mil_ArmorUpgrade] = new TechNode
        {
            Id = TechId.Mil_ArmorUpgrade, Name = "装甲强化", Branch = "军事", Tier = 1,
            Cost = 500, ResearchTime = 30f, RequiresTechCenter = false,
            Description = "所有坦克类单位血量+15%"
        },
        [TechId.Mil_AmmoUpgrade] = new TechNode
        {
            Id = TechId.Mil_AmmoUpgrade, Name = "弹药升级", Branch = "军事", Tier = 2,
            Cost = 800, ResearchTime = 45f,
            Prerequisites = new[]{ TechId.Mil_ArmorUpgrade },
            Description = "所有单位攻击力+15%"
        },
        [TechId.Mil_AdvancedTactics] = new TechNode
        {
            Id = TechId.Mil_AdvancedTactics, Name = "高级战术", Branch = "军事", Tier = 3,
            Cost = 1200, ResearchTime = 60f,
            Prerequisites = new[]{ TechId.Mil_AmmoUpgrade },
            Description = "火箭炮/导弹车射程+30%"
        },
        [TechId.Mil_HeroTraining] = new TechNode
        {
            Id = TechId.Mil_HeroTraining, Name = "英雄训练", Branch = "军事", Tier = 4,
            Cost = 1500, ResearchTime = 75f,
            Prerequisites = new[]{ TechId.Mil_AdvancedTactics },
            Description = "英雄生产成本-30%，英雄初始即为Lv2"
        },

        // 经济分支
        [TechId.Eco_MiningEfficiency] = new TechNode
        {
            Id = TechId.Eco_MiningEfficiency, Name = "采矿效率", Branch = "经济", Tier = 1,
            Cost = 400, ResearchTime = 25f, RequiresTechCenter = false,
            Description = "矿车采集速度+30%"
        },
        [TechId.Eco_MassProduction] = new TechNode
        {
            Id = TechId.Eco_MassProduction, Name = "批量生产", Branch = "经济", Tier = 2,
            Cost = 700, ResearchTime = 40f,
            Prerequisites = new[]{ TechId.Eco_MiningEfficiency },
            Description = "所有单位生产成本-15%"
        },
        [TechId.Eco_ResourceNetwork] = new TechNode
        {
            Id = TechId.Eco_ResourceNetwork, Name = "资源网络", Branch = "经济", Tier = 3,
            Cost = 1000, ResearchTime = 50f,
            Prerequisites = new[]{ TechId.Eco_MassProduction },
            Description = "战略点占领收入+100%"
        },
        [TechId.Eco_AdvancedLogistics] = new TechNode
        {
            Id = TechId.Eco_AdvancedLogistics, Name = "后勤优化", Branch = "经济", Tier = 4,
            Cost = 1300, ResearchTime = 65f,
            Prerequisites = new[]{ TechId.Eco_ResourceNetwork },
            Description = "单位上限+8"
        },

        // 防御分支
        [TechId.Def_Fortification] = new TechNode
        {
            Id = TechId.Def_Fortification, Name = "筑城术", Branch = "防御", Tier = 1,
            Cost = 450, ResearchTime = 28f, RequiresTechCenter = false,
            Description = "所有建筑血量+25%"
        },
        [TechId.Def_PowerGrid] = new TechNode
        {
            Id = TechId.Def_PowerGrid, Name = "电网优化", Branch = "防御", Tier = 2,
            Cost = 650, ResearchTime = 35f,
            Prerequisites = new[]{ TechId.Def_Fortification },
            Description = "电站发电量+50%"
        },
        [TechId.Def_AdvancedTurrets] = new TechNode
        {
            Id = TechId.Def_AdvancedTurrets, Name = "高级炮塔", Branch = "防御", Tier = 3,
            Cost = 900, ResearchTime = 50f,
            Prerequisites = new[]{ TechId.Def_PowerGrid },
            Description = "防御建筑射程+20%、伤害+20%"
        },
        [TechId.Def_RepairSystems] = new TechNode
        {
            Id = TechId.Def_RepairSystems, Name = "维修系统", Branch = "防御", Tier = 4,
            Cost = 1200, ResearchTime = 60f,
            Prerequisites = new[]{ TechId.Def_AdvancedTurrets },
            Description = "所有建筑每秒自动恢复2%血量"
        },
    };

    /// <summary>检查科技是否已研究。</summary>
    public static bool IsResearched(HashSet<TechId> completed, TechId id) => completed.Contains(id);

    /// <summary>检查科技是否可以研究（前置条件+科技中心+资金）。</summary>
    public static bool CanResearch(HashSet<TechId> completed, TechId id, bool hasTechCenter, int money)
    {
        var node = Nodes[id];
        if (completed.Contains(id)) return false;
        if (node.RequiresTechCenter && !hasTechCenter) return false;
        if (money < node.Cost) return false;
        foreach (var pre in node.Prerequisites)
            if (!completed.Contains(pre)) return false;
        return true;
    }

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
}
