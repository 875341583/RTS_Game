using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// G6: 邻接加成 — 文明6风格建筑布局策略
///
/// 同类型或互补类型建筑紧邻建造时获得加成，鼓励玩家规划建筑布局。
/// "相邻"定义：距离 <= AdjacencyRange (160px，约5个等距瓦片)
///
/// 加成规则：
/// - 电站+电站相邻 → 每相邻1座+15%发电
/// - 兵营+兵营相邻 → 每相邻1座+10%生产速度
/// - 车厂+车厂相邻 → 每相邻1座+10%生产速度
/// - 电站+基地相邻 → 电站+10%发电
/// - 防御塔+兵营相邻 → 防御塔+15%射程
/// - 维修厂+车厂相邻 → 维修速度+25%
/// - 科技中心+电站相邻 → 研究速度+15%
///
/// 按J键查看邻接加成面板。
/// </summary>
public static class AdjacencyBonus
{
    /// <summary>邻接判定距离（px）。</summary>
    public const float AdjacencyRange = 160f;

    // ===== 加成乘数常量 =====
    public const float PowerPlantStackMul = 0.15f;     // 电站叠放
    public const float PowerPlantNearBaseMul = 0.10f;  // 电站靠基地
    public const float BarracksStackMul = 0.10f;       // 兵营叠放
    public const float WarFactoryStackMul = 0.10f;     // 车厂叠放
    public const float TurretNearBarracksMul = 0.15f;  // 炮塔靠兵营
    public const float RepairNearWarFactoryMul = 0.25f; // 维修厂靠车厂
    public const float TechNearPowerMul = 0.15f;       // 科技中心靠电站

    /// <summary>获取指定建筑周围的同类相邻建筑数。</summary>
    public static int CountAdjacentOfType(List<Building> allBuildings, Building target, BuildingType type)
    {
        int count = 0;
        foreach (var b in allBuildings)
        {
            if (b == target || !GodotObject.IsInstanceValid(b) || b.TeamId != target.TeamId) continue;
            if (b.Type != type) continue;
            if (target.GlobalPosition.DistanceTo(b.GlobalPosition) <= AdjacencyRange)
                count++;
        }
        return count;
    }

    /// <summary>检查指定建筑周围是否有指定类型的相邻建筑。</summary>
    public static bool HasAdjacentOfType(List<Building> allBuildings, Building target, BuildingType type)
    {
        foreach (var b in allBuildings)
        {
            if (b == target || !GodotObject.IsInstanceValid(b) || b.TeamId != target.TeamId) continue;
            if (b.Type != type) continue;
            if (target.GlobalPosition.DistanceTo(b.GlobalPosition) <= AdjacencyRange)
                return true;
        }
        return false;
    }

    /// <summary>获取建筑的发电量加成乘数（1.0=无加成）。</summary>
    public static float GetPowerMultiplier(List<Building> allBuildings, Building target)
    {
        if (target.Type != BuildingType.PowerPlant) return 1f;
        float mul = 1f;
        // 电站叠放
        int stackCount = CountAdjacentOfType(allBuildings, target, BuildingType.PowerPlant);
        mul += stackCount * PowerPlantStackMul;
        // 电站靠基地
        if (HasAdjacentOfType(allBuildings, target, BuildingType.Base))
            mul += PowerPlantNearBaseMul;
        return mul;
    }

    /// <summary>获取建筑的生产速度加成乘数（1.0=无加成，>1=更快）。</summary>
    public static float GetProduceSpeedMultiplier(List<Building> allBuildings, Building target)
    {
        float mul = 1f;
        if (target.Type == BuildingType.Barracks)
        {
            int stack = CountAdjacentOfType(allBuildings, target, BuildingType.Barracks);
            mul += stack * BarracksStackMul;
        }
        else if (target.Type == BuildingType.WarFactory)
        {
            int stack = CountAdjacentOfType(allBuildings, target, BuildingType.WarFactory);
            mul += stack * WarFactoryStackMul;
        }
        return mul;
    }

    /// <summary>获取防御塔的射程加成乘数。</summary>
    public static float GetAttackRangeMultiplier(List<Building> allBuildings, Building target)
    {
        if (!target.IsDefensive) return 1f;
        if (HasAdjacentOfType(allBuildings, target, BuildingType.Barracks))
            return 1f + TurretNearBarracksMul;
        return 1f;
    }

    /// <summary>获取维修厂的维修速度加成乘数。</summary>
    public static float GetRepairSpeedMultiplier(List<Building> allBuildings, Building target)
    {
        if (!target.IsRepairStation) return 1f;
        if (HasAdjacentOfType(allBuildings, target, BuildingType.WarFactory))
            return 1f + RepairNearWarFactoryMul;
        return 1f;
    }

    /// <summary>获取阵营的研究速度加成乘数（科技中心靠近电站时生效）。</summary>
    public static float GetResearchMultiplier(List<Building> allBuildings, int teamId)
    {
        foreach (var b in allBuildings)
        {
            if (!GodotObject.IsInstanceValid(b) || b.TeamId != teamId) continue;
            if (b.Type != BuildingType.TechCenter) continue;
            if (HasAdjacentOfType(allBuildings, b, BuildingType.PowerPlant))
                return 1f + TechNearPowerMul;
        }
        return 1f;
    }
}
