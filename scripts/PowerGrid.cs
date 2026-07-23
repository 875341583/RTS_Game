using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// G4: 电网分区系统 — 文明6风格区域供电
/// 
/// 每个电站有一个供电半径(280px)。建筑必须在某电站供电半径内
/// 才获得满功率供电。超出半径的建筑被标记为"离线"，
/// 生产速度降低50%。
/// 
/// 基地自带50电力局部供电（自给自足但范围小）。
/// 按G键查看电网分布。
/// </summary>
public static class PowerGrid
{
    /// <summary>电站供电半径（像素）。</summary>
    public const float PowerRadius = 280f;

    /// <summary>基地自供电半径（比电站小）。</summary>
    public const float BasePowerRadius = 160f;

    /// <summary>离线建筑的生产速度乘数。</summary>
    public const float OfflineProduceMul = 0.5f;

    /// <summary>
    /// 检查指定建筑是否在某个电站的供电范围内。
    /// </summary>
    public static bool IsInRange(Building target, IEnumerable<Building> allBuildings)
    {
        foreach (var b in allBuildings)
        {
            if (b == target || !GodotObject.IsInstanceValid(b) || !GodotObject.IsInstanceValid(target)) continue;
            if (b.TeamId != target.TeamId) continue;
            // 电站给周围供电
            if (b.Type == BuildingType.PowerPlant && b.PowerProvided > 0)
            {
                float dist = b.GlobalPosition.DistanceTo(target.GlobalPosition);
                if (dist <= PowerRadius) return true;
            }
            // 基地自供电（小范围）
            if (b.Type == BuildingType.Base && b.PowerProvided > 0)
            {
                float dist = b.GlobalPosition.DistanceTo(target.GlobalPosition);
                if (dist <= BasePowerRadius) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 获取供电范围内的电站供电能力 + 范围内总耗电。
    /// </summary>
    public static (int supplied, int consumed) CalculateGridPower(
        Building target, IEnumerable<Building> allBuildings)
    {
        int supplied = 0;
        int consumed = 0;
        bool hasPowerSource = false;

        foreach (var b in allBuildings)
        {
            if (!GodotObject.IsInstanceValid(b) || b.TeamId != target.TeamId) continue;
            float dist = b.GlobalPosition.DistanceTo(target.GlobalPosition);

            // 供电建筑
            if (b.Type == BuildingType.PowerPlant && b.PowerProvided > 0 && dist <= PowerRadius)
            {
                supplied += b.PowerProvided;
                hasPowerSource = true;
            }
            if (b.Type == BuildingType.Base && b.PowerProvided > 0 && dist <= BasePowerRadius)
            {
                supplied += b.PowerProvided;
                hasPowerSource = true;
            }

            // 同区域耗电建筑
            if (b.PowerConsumed > 0 && dist <= PowerRadius)
            {
                consumed += b.PowerConsumed;
            }
        }

        return (supplied, consumed);
    }
}
