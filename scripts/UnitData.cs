using System.Collections.Generic;
using Godot;

namespace RTSGame;

// ============================================================================
// P1-5 第4步（最小化）：单位数据快照
// 提取 Unit 核心状态为无 Godot 节点依赖的纯数据结构。
// 用途：2D/3D 共享数据载体、存档序列化、网络同步快照。
// Unit 提供 GetUnitData() 生成快照；ApplyUnitData() 从快照恢复。
// SaveLoadSystem.UnitSave 是本结构的子集（存档专用平铺格式）。
// ============================================================================

/// <summary>
/// 单位纯数据快照（无 Godot 节点依赖）。2D Unit / 3D Unit3D 均可使用。
/// 含属性数据（MaxHealth/AttackDamage 等）+ 状态数据（HP/位置/命令/升级/乘客等）。
/// 不含渲染节点引用、不引用 Sprite2D/ProgressBar 等 Godot 视觉对象。
/// </summary>
public struct UnitData
{
    // ---- 类型与阵营 ----
    public UnitType Type;
    public int TeamId;

    // ---- 属性数据（InitAsType 设定，科技/阵营修改） ----
    public float MaxHealth;
    public float MoveSpeed;
    public float AttackDamage;
    public float AttackRange;
    public float AttackCooldown;
    public float MinAttackRange;
    public float SplashRadius;
    public float AggroRange;
    public bool CanAttackAir;
    public bool IsAirUnit;
    public bool AutoDefend;
    public bool AutoAI;
    public int MaxPassengers;

    // ---- 状态数据（运行时变化） ----
    public float Health;
    public float PosX, PosY;
    public bool IsDead;

    // ---- 命令状态 ----
    public float MoveTargetX, MoveTargetY;
    public bool HasMoveTarget;
    public float GuardX, GuardY;
    public bool HasGuardPosition;

    // ---- 升级系统 ----
    public int Level;
    public float Experience;
    public List<Unit.UnitAbility> Abilities;

    // ---- 英雄/间谍 ----
    public Unit.HeroSkill HeroSkill;
    public int SpyDisguiseTeam;
    public int LastAttackerTeam;

    // ---- 乘客（运输车） ----
    public List<UnitData> Passengers;
}
