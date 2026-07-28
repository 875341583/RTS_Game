using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// G3: 战术卡系统 — 文明6风格开局战略选择
/// 
/// 游戏开始后弹出3张随机战术卡，玩家选1张，影响整局战略走向。
/// AI也随机选1张。
/// 按T键查看当前战术卡。
/// 
/// P2-4: 数据驱动 — 从 res://data/tactical_cards.json 加载卡牌元数据（名称/描述/图标），
/// 替代硬编码字典。效果查询方法（Get*Mul）保留为代码（业务逻辑，非配置数据）。
/// JSON加载失败时回退到硬编码数据。
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

    // ===== P2-4: 从JSON加载的战术卡元数据 =====
    private static readonly Dictionary<CardId, CardInfo> _cards = new();
    private static readonly object _cardsLock = new();
    private static bool _alwaysFallback = false;

    /// <summary>强制使用硬编码数据（供单元测试使用，在无Godot运行时的环境中调用）</summary>
    public static void SetAlwaysFallback(bool value) => _alwaysFallback = value;

    /// <summary>所有战术卡（P2-4: 优先从JSON加载，失败则用硬编码fallback）</summary>
    public static IReadOnlyDictionary<CardId, CardInfo> Cards
    {
        get
        {
            lock (_cardsLock)
            {
                if (_cards.Count == 0)
                {
                    if (_alwaysFallback)
                        LoadFromJsonCore(true);
                    else
                        LoadFromJsonCore(false);
                }
                return _cards;
            }
        }
    }

    /// <summary>P2-4: 从 res://data/tactical_cards.json 加载战术卡元数据。
    /// forceFallback=true时跳过Godot IO，直接用硬编码数据（供单元测试使用）。</summary>
    public static void LoadFromJson(bool forceFallback = false)
    {
        lock (_cardsLock)
        {
            if (_cards.Count > 0) return; // 已加载，无论fallback还是JSON都跳过
            LoadFromJsonCore(forceFallback);
        }
    }

    /// <summary>内部加载实现（调用方需持有 _cardsLock）</summary>
    private static void LoadFromJsonCore(bool forceFallback)
    {
        _cards.Clear();

        if (forceFallback)
        {
            LoadFallback();
            return;
        }

        // P2-4: 通过ModLoader读取，支持Mod覆盖
        var jsonText = ModLoader.ReadDataFile("tactical_cards.json");
        if (string.IsNullOrEmpty(jsonText))
        {
            GameLog.Warning("[TacticalCards] 无法读取 tactical_cards.json，使用硬编码fallback");
            LoadFallback();
            return;
        }

        var jsonResult = Json.ParseString(jsonText);
        if (jsonResult.VariantType != Variant.Type.Array)
        {
            GameLog.Warning("[TacticalCards] tactical_cards.json 格式错误，使用硬编码fallback");
            LoadFallback();
            return;
        }

        var array = jsonResult.AsGodotArray();
        foreach (var entry in array)
        {
            var dict = entry.AsGodotDictionary();
            if (dict == null) continue;

            var idStr = dict["id"].AsString();
            if (!System.Enum.TryParse<CardId>(idStr, out var id))
            {
                GameLog.Warning($"[TacticalCards] 未知卡牌ID: {idStr}");
                continue;
            }

            _cards[id] = new CardInfo
            {
                Id = id,
                Name = dict["name"].AsString(),
                Icon = dict["icon"].AsString(),
                Description = dict["description"].AsString(),
            };
        }

        GameLog.Info($"[TacticalCards] 从JSON加载 {_cards.Count} 张战术卡");
    }

    /// <summary>P2-4: 硬编码fallback（JSON加载失败时使用）</summary>
    private static void LoadFallback()
    {
        _cards.Clear();
        _cards[CardId.BlitzEconomy] = new CardInfo
        {
            Id = CardId.BlitzEconomy, Name = TrManager.Tr("card.blitz_economy.name"), Icon = "$",
            Description = TrManager.Tr("card.blitz_economy.desc")
        };
        _cards[CardId.BlitzTactics] = new CardInfo
        {
            Id = CardId.BlitzTactics, Name = TrManager.Tr("card.blitz_tactics.name"), Icon = ">>",
            Description = TrManager.Tr("card.blitz_tactics.desc")
        };
        _cards[CardId.IronFlood] = new CardInfo
        {
            Id = CardId.IronFlood, Name = TrManager.Tr("card.iron_flood.name"), Icon = "[T]",
            Description = TrManager.Tr("card.iron_flood.desc")
        };
        _cards[CardId.InfantryAssault] = new CardInfo
        {
            Id = CardId.InfantryAssault, Name = TrManager.Tr("card.infantry_assault.name"), Icon = "[I]",
            Description = TrManager.Tr("card.infantry_assault.desc")
        };
        _cards[CardId.Fortress] = new CardInfo
        {
            Id = CardId.Fortress, Name = TrManager.Tr("card.fortress.name"), Icon = "[F]",
            Description = TrManager.Tr("card.fortress.desc")
        };
        _cards[CardId.TechLeap] = new CardInfo
        {
            Id = CardId.TechLeap, Name = TrManager.Tr("card.tech_leap.name"), Icon = "^",
            Description = TrManager.Tr("card.tech_leap.desc")
        };
        _cards[CardId.WarMachine] = new CardInfo
        {
            Id = CardId.WarMachine, Name = TrManager.Tr("card.war_machine.name"), Icon = "+",
            Description = TrManager.Tr("card.war_machine.desc")
        };
        _cards[CardId.RapidDeploy] = new CardInfo
        {
            Id = CardId.RapidDeploy, Name = TrManager.Tr("card.rapid_deploy.name"), Icon = "[]+",
            Description = TrManager.Tr("card.rapid_deploy.desc")
        };
    }

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
