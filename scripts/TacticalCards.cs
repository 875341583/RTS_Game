using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// G3: 战术卡系统 — 文明6风格开局战略选择
/// 
/// 游戏开始后弹出3张随机战术卡，玩家选1张，影响整局战略走向。
/// AI也随机选1张。
/// 按T键查看当前战术卡。
/// </summary>
public static class TacticalCards
{
    // ===== 战术卡ID枚举 =====
    public enum CardId
    {
        BlitzEconomy,       // 闪电经济 — 起始资金+50%，矿车收益+20%
        BlitzTactics,       // 闪击战术 — 单位移速+15%，生产时间-15%
        IronFlood,          // 钢铁洪流 — 坦克血量+20%、攻击+10%
        InfantryAssault,    // 步兵突击 — 步兵血量+25%、成本-20%
        Fortress,           // 要塞防御 — 建筑血量+30%，防御射程+15%
        TechLeap,           // 科技跃进 — 研究速度+50%，时代升级速度+30%
        WarMachine,         // 战争机器 — 全单位攻击+15%，但血量-10%
        RapidDeploy,        // 快速部署 — 单位上限+10，生产时间-20%
    }

    // ===== 战术卡定义 =====
    public class CardInfo
    {
        public CardId Id { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Icon { get; init; } = "";  // 简单图标标识
    }

    // ===== 所有战术卡 =====
    public static readonly Dictionary<CardId, CardInfo> Cards = new()
    {
        [CardId.BlitzEconomy] = new CardInfo
        {
            Id = CardId.BlitzEconomy, Name = "闪电经济", Icon = "$",
            Description = "起始资金+50%，矿车采矿收益+20%"
        },
        [CardId.BlitzTactics] = new CardInfo
        {
            Id = CardId.BlitzTactics, Name = "闪击战术", Icon = ">>",
            Description = "所有单位移动速度+15%，生产时间-15%"
        },
        [CardId.IronFlood] = new CardInfo
        {
            Id = CardId.IronFlood, Name = "钢铁洪流", Icon = "[T]",
            Description = "坦克类单位血量+20%、攻击力+10%"
        },
        [CardId.InfantryAssault] = new CardInfo
        {
            Id = CardId.InfantryAssault, Name = "步兵突击", Icon = "[I]",
            Description = "步兵类单位血量+25%、成本-20%"
        },
        [CardId.Fortress] = new CardInfo
        {
            Id = CardId.Fortress, Name = "要塞防御", Icon = "[F]",
            Description = "建筑血量+30%，防御建筑射程+15%"
        },
        [CardId.TechLeap] = new CardInfo
        {
            Id = CardId.TechLeap, Name = "科技跃进", Icon = "^",
            Description = "研究速度+50%，时代升级速度+30%"
        },
        [CardId.WarMachine] = new CardInfo
        {
            Id = CardId.WarMachine, Name = "战争机器", Icon = "+",
            Description = "全单位攻击+15%，但血量-10%"
        },
        [CardId.RapidDeploy] = new CardInfo
        {
            Id = CardId.RapidDeploy, Name = "快速部署", Icon = "[]+",
            Description = "单位上限+10，生产时间-20%"
        },
    };

    /// <summary>随机抽取N张不重复战术卡。</summary>
    public static CardId[] DrawRandom(int count, RandomNumberGenerator rng)
    {
        var all = new List<CardId>((CardId[])System.Enum.GetValues(typeof(CardId)));
        // Fisher-Yates shuffle
        for (int i = all.Count - 1; i > 0; i--)
        {
            int j = rng.RandiRange(0, i);
            (all[i], all[j]) = (all[j], all[i]);
        }
        return all.GetRange(0, System.Math.Min(count, all.Count)).ToArray();
    }

    // ===== 效果查询方法 =====

    /// <summary>移动速度乘数。</summary>
    public static float GetMoveSpeedMul(CardId? card) => card switch
    {
        CardId.BlitzTactics => 1.15f,
        _ => 1f,
    };

    /// <summary>生产时间乘数（越小越快）。</summary>
    public static float GetProduceTimeMul(CardId? card) => card switch
    {
        CardId.BlitzTactics => 0.85f,
        CardId.RapidDeploy => 0.80f,
        _ => 1f,
    };

    /// <summary>坦克血量乘数。</summary>
    public static float GetTankHealthMul(CardId? card) => card switch
    {
        CardId.IronFlood => 1.20f,
        CardId.WarMachine => 0.90f,
        _ => 1f,
    };

    /// <summary>坦克攻击力乘数。</summary>
    public static float GetTankDamageMul(CardId? card) => card switch
    {
        CardId.IronFlood => 1.10f,
        _ => 1f,
    };

    /// <summary>步兵血量乘数。</summary>
    public static float GetInfantryHealthMul(CardId? card) => card switch
    {
        CardId.InfantryAssault => 1.25f,
        CardId.WarMachine => 0.90f,
        _ => 1f,
    };

    /// <summary>步兵成本乘数。</summary>
    public static float GetInfantryCostMul(CardId? card) => card switch
    {
        CardId.InfantryAssault => 0.80f,
        _ => 1f,
    };

    /// <summary>全单位攻击力乘数（非坦克非步兵也适用）。</summary>
    public static float GetAllDamageMul(CardId? card) => card switch
    {
        CardId.WarMachine => 1.15f,
        _ => 1f,
    };

    /// <summary>全单位血量乘数（非坦克非步兵）。</summary>
    public static float GetAllHealthMul(CardId? card) => card switch
    {
        CardId.WarMachine => 0.90f,
        _ => 1f,
    };

    /// <summary>建筑血量乘数。</summary>
    public static float GetBuildingHealthMul(CardId? card) => card switch
    {
        CardId.Fortress => 1.30f,
        _ => 1f,
    };

    /// <summary>防御建筑射程乘数。</summary>
    public static float GetTurretRangeMul(CardId? card) => card switch
    {
        CardId.Fortress => 1.15f,
        _ => 1f,
    };

    /// <summary>矿车收益乘数。</summary>
    public static float GetMiningMul(CardId? card) => card switch
    {
        CardId.BlitzEconomy => 1.20f,
        _ => 1f,
    };

    /// <summary>研究速度乘数。</summary>
    public static float GetResearchSpeedMul(CardId? card) => card switch
    {
        CardId.TechLeap => 1.50f,
        _ => 1f,
    };

    /// <summary>时代升级速度乘数。</summary>
    public static float GetEraUpgradeSpeedMul(CardId? card) => card switch
    {
        CardId.TechLeap => 1.30f,
        _ => 1f,
    };

    /// <summary>单位上限加成。</summary>
    public static int GetUnitCapBonus(CardId? card) => card switch
    {
        CardId.RapidDeploy => 10,
        _ => 0,
    };

    /// <summary>起始资金乘数。</summary>
    public static float GetStartMoneyMul(CardId? card) => card switch
    {
        CardId.BlitzEconomy => 1.50f,
        _ => 1f,
    };
}
