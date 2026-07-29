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
/// 
/// P2-4: 数据驱动 — 从 res://data/eras.json 加载时代元数据（名称/描述/升级费用/时间/前置建筑），
/// 替代硬编码数组。效果公式与解锁逻辑保留为代码（业务逻辑，非配置数据）。
/// JSON加载失败时回退到硬编码数据。
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

    // ===== P2-4: 从JSON加载的时代数据 =====
    private static EraInfo[] _eras = System.Array.Empty<EraInfo>();
    private static readonly object _erasLock = new();
    private static bool _alwaysFallback = false;

    /// <summary>强制使用硬编码数据（供单元测试使用，在无Godot运行时的环境中调用）</summary>
    public static void SetAlwaysFallback(bool value) => _alwaysFallback = value;

    /// <summary>所有时代（P2-4: 优先从JSON加载，失败则用硬编码fallback）</summary>
    public static EraInfo[] Eras
    {
        get
        {
            lock (_erasLock)
            {
                if (_eras.Length == 0) LoadFromJsonCore(_alwaysFallback);
                return _eras;
            }
        }
    }

    /// <summary>P2-4: 从 res://data/eras.json 加载时代元数据。
    /// forceFallback=true时跳过Godot IO，直接用硬编码数据（供单元测试使用）。</summary>
    public static void LoadFromJson(bool forceFallback = false)
    {
        lock (_erasLock)
        {
            if (_eras.Length > 0) return; // 已加载，无论fallback还是JSON都跳过
            LoadFromJsonCore(forceFallback);
        }
    }

    /// <summary>内部加载实现（调用方需持有 _erasLock）</summary>
    private static void LoadFromJsonCore(bool forceFallback)
    {
        if (forceFallback)
        {
            LoadFallback();
            return;
        }

        // P2-4: 通过ModLoader读取，支持Mod覆盖
        var jsonText = ModLoader.ReadDataFile("eras.json");
        if (string.IsNullOrEmpty(jsonText))
        {
            GameLog.Warning("[EraSystem] 无法读取 eras.json，使用硬编码fallback");
            LoadFallback();
            return;
        }

        var jsonResult = Json.ParseString(jsonText);
        if (jsonResult.VariantType != Variant.Type.Array)
        {
            GameLog.Warning("[EraSystem] eras.json 格式错误，使用硬编码fallback");
            LoadFallback();
            return;
        }

        var list = new List<EraInfo>();
        var array = jsonResult.AsGodotArray();
        foreach (var entry in array)
        {
            var dict = entry.AsGodotDictionary();
            if (dict == null) continue;

            var idStr = dict["id"].AsString();
            if (!System.Enum.TryParse<Era>(idStr, out var id))
            {
                GameLog.Warning($"[EraSystem] 未知时代ID: {idStr}");
                continue;
            }

            var reqBuildings = new List<BuildingType>();
            if (dict.ContainsKey("requiredBuildings") && dict["requiredBuildings"].VariantType == Variant.Type.Array)
            {
                foreach (var b in dict["requiredBuildings"].AsGodotArray())
                {
                    if (System.Enum.TryParse<BuildingType>(b.AsString(), out var bt))
                        reqBuildings.Add(bt);
                }
            }

            list.Add(new EraInfo
            {
                Id = id,
                Name = dict["name"].AsString(),
                Description = dict["description"].AsString(),
                UpgradeCost = (int)dict["upgradeCost"].AsInt64(),
                UpgradeTime = (float)dict["upgradeTime"].AsDouble(),
                RequiredBuildings = reqBuildings.ToArray(),
            });
        }

        _eras = list.ToArray();
        GameLog.Info($"[EraSystem] 从JSON加载 {_eras.Length} 个时代");
    }

    /// <summary>P2-4: 硬编码fallback（JSON加载失败时使用）</summary>
    private static void LoadFallback()
    {
        _eras = new EraInfo[]
        {
            new EraInfo
            {
                Id = Era.Stone, Name = TrManager.Tr("era.stone.name"), Description = TrManager.Tr("era.stone.desc"),
                UpgradeCost = 0, UpgradeTime = 0f,
                RequiredBuildings = System.Array.Empty<BuildingType>()
            },
            new EraInfo
            {
                Id = Era.Bronze, Name = TrManager.Tr("era.bronze.name"), Description = TrManager.Tr("era.bronze.desc"),
                UpgradeCost = 800, UpgradeTime = 30f,
                RequiredBuildings = new[] { BuildingType.Barracks }
            },
            new EraInfo
            {
                Id = Era.Industrial, Name = TrManager.Tr("era.industrial.name"), Description = TrManager.Tr("era.industrial.desc"),
                UpgradeCost = 1500, UpgradeTime = 45f,
                RequiredBuildings = new[] { BuildingType.WarFactory }
            },
            new EraInfo
            {
                Id = Era.Information, Name = TrManager.Tr("era.information.name"), Description = TrManager.Tr("era.information.desc"),
                UpgradeCost = 2500, UpgradeTime = 60f,
                RequiredBuildings = new[] { BuildingType.TechCenter }
            },
        };
    }

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
                && type != UnitType.LandingCraft
                && type != UnitType.ApocalypseTank && type != UnitType.PrismTank
                && type != UnitType.KirovAirship && type != UnitType.TeslaTrooper;
        }
        // 工业时代：+火箭炮/导弹车/总工程师/空军系列/火箭兵
        if (era == Era.Industrial)
        {
            return type != UnitType.Hero && type != UnitType.Spy
                && type != UnitType.Destroyer && type != UnitType.Submarine && type != UnitType.AircraftCarrier
                && type != UnitType.LandingCraft
                && type != UnitType.ApocalypseTank && type != UnitType.PrismTank
                && type != UnitType.KirovAirship;
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

    // ==================== P0-2: 存档/读档 恢复方法 ====================

    /// <summary>P0-2 读档：重置为石器时代初始状态。</summary>
    public void Reset()
    {
        CurrentEra = EraSystem.Era.Stone;
        IsUpgrading = false;
        UpgradeTimer = 0f;
    }

    /// <summary>P0-2 读档：直接恢复时代与升级进度。</summary>
    public void Restore(EraSystem.Era era, bool isUpgrading, float upgradeTimer)
    {
        CurrentEra = era;
        IsUpgrading = isUpgrading;
        UpgradeTimer = Mathf.Max(0f, upgradeTimer);
    }
}
