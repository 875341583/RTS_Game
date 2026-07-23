using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// G2: 时代系统 — 文明6风格时代演进
/// 
/// 四个时代阶梯：
///   石器时代 (Stone)     — 起始时代，仅基础步兵/轻坦/矿车/基地/电站/兵营
///   青铜时代 (Bronze)    — 解锁车厂/重坦/炮兵/防空/机枪塔/维修厂/工程师
///   工业时代 (Industrial)— 解锁科技中心/火箭炮/导弹车/机场/战斗机/直升机/防空炮
///   信息时代 (Information)— 解锁船厂/海军/轰炸机/超武/英雄/间谍
/// 
/// 时代升级条件：
///   - 拥有当前时代所有核心建筑
///   - 花费升级资金
///   - 等待升级时间
/// 
/// 时代效果：
///   - 每个时代所有单位+5%攻击/+5%血量（累计）
///   - 每个时代矿车+10%采集速度（累计）
///   - 高时代解锁更高级单位/建筑
/// </summary>
public static class EraSystem
{
    // ===== 时代枚举 =====
    public enum Era
    {
        Stone,          // 石器时代 (0)
        Bronze,         // 青铜时代 (1)
        Industrial,     // 工业时代 (2)
        Information,    // 信息时代 (3)
    }

    // ===== 时代定义 =====
    public class EraInfo
    {
        public Era Id { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public int UpgradeCost { get; init; }       // 升级到本时代所需资金
        public float UpgradeTime { get; init; }      // 升级所需时间(秒)
        /// <summary>升级到本时代需要拥有的建筑类型（仅玩家方检查）。</summary>
        public BuildingType[] RequiredBuildings { get; init; } = System.Array.Empty<BuildingType>();
    }

    // ===== 时代数据 =====
    public static readonly EraInfo[] Eras = new EraInfo[]
    {
        new EraInfo
        {
            Id = Era.Stone, Name = "石器时代", Description = "起步时代：仅能建造基础建筑和生产步兵",
            UpgradeCost = 0, UpgradeTime = 0f,
            RequiredBuildings = System.Array.Empty<BuildingType>()
        },
        new EraInfo
        {
            Id = Era.Bronze, Name = "青铜时代", Description = "解锁车厂/重坦/炮兵/防御塔/维修厂",
            UpgradeCost = 800, UpgradeTime = 30f,
            RequiredBuildings = new[] { BuildingType.Barracks }
        },
        new EraInfo
        {
            Id = Era.Industrial, Name = "工业时代", Description = "解锁科技中心/火箭炮/导弹车/机场/空军",
            UpgradeCost = 1500, UpgradeTime = 45f,
            RequiredBuildings = new[] { BuildingType.WarFactory }
        },
        new EraInfo
        {
            Id = Era.Information, Name = "信息时代", Description = "解锁船厂/海军/轰炸机/超武/英雄/间谍",
            UpgradeCost = 2500, UpgradeTime = 60f,
            RequiredBuildings = new[] { BuildingType.TechCenter }
        },
    };

    /// <summary>获取时代的攻击力加成乘数（每个时代+5%，累计）。</summary>
    public static float GetDamageMultiplier(Era era) => 1f + (int)era * 0.05f;

    /// <summary>获取时代的血量加成乘数（每个时代+5%，累计）。</summary>
    public static float GetHealthMultiplier(Era era) => 1f + (int)era * 0.05f;

    /// <summary>获取时代的矿车采集速度乘数（每个时代+10%，累计）。</summary>
    public static float GetMiningMultiplier(Era era) => 1f + (int)era * 0.10f;

    /// <summary>获取时代建造速度乘数（每个时代+10%，累计）。</summary>
    public static float GetBuildSpeedMultiplier(Era era) => 1f + (int)era * 0.10f;

    /// <summary>判断指定建筑类型在当前时代是否可建造。</summary>
    public static bool CanBuildBuilding(Era era, BuildingType type)
    {
        // 石器时代：仅基地/电站/兵营
        if (era == Era.Stone)
        {
            return type == BuildingType.Base || type == BuildingType.PowerPlant || type == BuildingType.Barracks;
        }
        // 青铜时代：+车厂/机枪塔/防空炮/维修厂
        if (era == Era.Bronze)
        {
            return type == BuildingType.Base || type == BuildingType.PowerPlant || type == BuildingType.Barracks
                || type == BuildingType.WarFactory || type == BuildingType.Turret || type == BuildingType.AntiAirTurret
                || type == BuildingType.RepairPad;
        }
        // 工业时代：+科技中心/机场
        if (era == Era.Industrial)
        {
            return type != BuildingType.Shipyard && type != BuildingType.NukeSilo
                && type != BuildingType.LightningTower && type != BuildingType.MissileSilo;
        }
        // 信息时代：全部解锁
        return true;
    }

    /// <summary>判断指定单位类型在当前时代是否可生产。</summary>
    public static bool CanProduceUnit(Era era, UnitType type)
    {
        // 石器时代：仅步兵/轻坦/矿车
        if (era == Era.Stone)
        {
            return type == UnitType.Infantry || type == UnitType.LightTank || type == UnitType.Harvester
                || type == UnitType.Sapper;
        }
        // 青铜时代：+重坦/炮兵/防空/工程师/运输车/掷弹兵/狙击手/喷火兵/窃贼
        if (era == Era.Bronze)
        {
            return type != UnitType.RocketLauncher && type != UnitType.MissileTank
                && type != UnitType.ChiefEngineer && type != UnitType.Hero && type != UnitType.Spy
                && type != UnitType.Fighter && type != UnitType.Helicopter && type != UnitType.RocketInfantry
                && type != UnitType.Bomber && type != UnitType.Scout && type != UnitType.TransportHeli
                && type != UnitType.Destroyer && type != UnitType.Submarine && type != UnitType.AircraftCarrier
                && type != UnitType.LandingCraft;
        }
        // 工业时代：+火箭炮/导弹车/总工程师/空军系列/火箭兵
        if (era == Era.Industrial)
        {
            return type != UnitType.Hero && type != UnitType.Spy
                && type != UnitType.Destroyer && type != UnitType.Submarine && type != UnitType.AircraftCarrier
                && type != UnitType.LandingCraft;
        }
        // 信息时代：全部解锁
        return true;
    }

    /// <summary>检查是否满足升级到下一时代的条件。</summary>
    public static bool CanAdvance(Era current, System.Func<BuildingType, bool> hasBuilding, int money)
    {
        int nextIdx = (int)current + 1;
        if (nextIdx >= Eras.Length) return false; // 已是最高时代
        var next = Eras[nextIdx];
        if (money < next.UpgradeCost) return false;
        foreach (var req in next.RequiredBuildings)
            if (!hasBuilding(req)) return false;
        return true;
    }

    /// <summary>获取下一个时代信息，无则null。</summary>
    public static EraInfo? GetNextEra(Era current)
    {
        int nextIdx = (int)current + 1;
        return nextIdx < Eras.Length ? Eras[nextIdx] : null;
    }
}

/// <summary>
/// 每个阵营的时代进度状态。
/// </summary>
public class EraProgress
{
    public EraSystem.Era CurrentEra { get; private set; } = EraSystem.Era.Stone;
    public bool IsUpgrading { get; private set; }
    public float UpgradeTimer { get; private set; }

    /// <summary>开始时代升级（不检查条件，调用方需先CanAdvance）。</summary>
    public void StartUpgrade()
    {
        var next = EraSystem.GetNextEra(CurrentEra);
        if (next == null) return;
        IsUpgrading = true;
        UpgradeTimer = next.UpgradeTime;
    }

    /// <summary>每帧更新升级进度。返回true表示升级完成。</summary>
    public bool UpdateUpgrade(float dt)
    {
        if (!IsUpgrading) return false;
        UpgradeTimer -= dt;
        if (UpgradeTimer <= 0f)
        {
            int nextIdx = (int)CurrentEra + 1;
            if (nextIdx < EraSystem.Eras.Length)
            {
                CurrentEra = EraSystem.Eras[nextIdx].Id;
                IsUpgrading = false;
                UpgradeTimer = 0f;
                return true;
            }
            IsUpgrading = false;
        }
        return false;
    }

    /// <summary>升级进度 0~1。</summary>
    public float Progress => IsUpgrading && EraSystem.GetNextEra(CurrentEra) != null
        ? Mathf.Clamp(1f - UpgradeTimer / EraSystem.GetNextEra(CurrentEra)!.UpgradeTime, 0f, 1f)
        : 0f;
}
