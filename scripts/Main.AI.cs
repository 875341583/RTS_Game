using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RTSGame;

/// <summary>
/// Main 的AI策略状态机控制器（partial class）。
/// 包含：4状态策略状态机(Expand/BuildUp/Attack/Defend) + 进攻集结系统 + 战术评估 + 难度差异化。
/// </summary>
public partial class Main
{
    // ======== AI策略状态机 ========

    /// <summary>AI策略状态枚举。Expand=扩张经济, BuildUp=蓄力建设, Attack=集结进攻, Defend=紧急防御。</summary>
    public enum AIStrategy { Expand, BuildUp, Attack, Defend }

    /// <summary>每个AI阵营的策略状态数据。</summary>
    private struct AIStrategyState
    {
        public AIStrategy Strategy;
        public float StrategyCooldown;       // 策略切换冷却（秒，递减）
        public float LastAttackedTimer;      // 建筑受袭倒计时（>0时强制Defend）
        public float RallyWaitTimer;         // 集结等待超时计时器
        public float RallyCheckTimer;        // 集结条件检查间隔（限流）
        public bool AssaultLaunched;         // 当前一波进攻是否已发出
        public float AssaultCooldown;        // 进攻后冷却（下次集结前等待）
        public Vector2 RallyPoint;           // 集结点坐标
        public bool HasRallyPoint;           // 是否有有效集结点
        public int AssaultTargetTeamId;      // 进攻目标阵营ID（-1=无）
    }

    /// <summary>8阵营的策略状态（索引0=玩家不用，1..7=AI阵营）。</summary>
    private readonly AIStrategyState[] _aiStrategyStates = new AIStrategyState[TotalTeamCount];

    // ======== 难度相关策略参数 ========

    /// <summary>获取当前难度下的策略检查间隔（秒）。
    /// Easy/Normal=20s, Hard=10s, Brutal=7s。</summary>
    private float GetStrategyCheckInterval() => _difficulty switch
    {
        Difficulty.Hard => 10f,
        Difficulty.Brutal => 7f,
        _ => 20f,
    };

    /// <summary>获取进攻集结所需最少单位数。Hard/Brutal=5, 其他=6。</summary>
    private int GetRallyMinUnits() => _difficulty switch
    {
        Difficulty.Hard => 5,
        Difficulty.Brutal => 5,
        _ => 6,
    };

    /// <summary>获取集结等待超时（秒）。Hard=10s, Brutal=7s, 其他=15s。</summary>
    private float GetRallyWaitTimeout() => _difficulty switch
    {
        Difficulty.Hard => 10f,
        Difficulty.Brutal => 7f,
        _ => 15f,
    };

    /// <summary>获取进攻策略的军队上限阈值。Brutal=10, 其他=12。</summary>
    private int GetAttackArmyThreshold() => _difficulty switch
    {
        Difficulty.Brutal => 10,
        _ => 12,
    };

    /// <summary>获取进攻后冷却时间（秒）。下次集结前需等待。</summary>
    private float GetAssaultCooldown() => _difficulty switch
    {
        Difficulty.Brutal => 10f,
        Difficulty.Hard => 12f,
        _ => 15f,
    };

    /// <summary>Easy难度是否禁止主动进攻。</summary>
    private bool EasyNoAttack => _difficulty == Difficulty.Easy;

    // ======== 策略状态机核心 ========

    /// <summary>更新指定AI阵营的策略状态。在AITickForTeam中按策略检查间隔调用。</summary>
    private void UpdateAIStrategy(int teamId)
    {
        if (!_bases.TryGetValue(teamId, out var baseB) || !IsInstanceValid(baseB)) return;
        ref var state = ref _aiStrategyStates[teamId];

        // 1. 建筑受袭 → 强制Defend（最高优先级）
        if (state.LastAttackedTimer > 0f)
        {
            if (state.Strategy != AIStrategy.Defend)
                SetStrategy(teamId, AIStrategy.Defend, "building under attack");
            return;
        }

        // 2. 战术评估：检查基地附近兵力比
        var (friendlyForce, enemyForce) = CountNearbyForceRatio(teamId);

        // 敌方 > 己方2倍 且 敌方有3个以上 → Defend
        if (enemyForce > friendlyForce * 2f && enemyForce >= 3)
        {
            SetStrategy(teamId, AIStrategy.Defend, $"enemy force 2x+ ({enemyForce} vs {friendlyForce})");
            return;
        }

        // Easy难度不主动进攻，只有Expand和Defend
        if (EasyNoAttack)
        {
            if (state.Strategy != AIStrategy.Expand && state.Strategy != AIStrategy.Defend)
                SetStrategy(teamId, AIStrategy.Expand, "Easy mode - no attack");
            return;
        }

        int armyCount = CountCombatUnitsOfTeam(teamId);
        bool hasTech = HasBuilding(teamId, BuildingType.TechCenter);
        int attackThreshold = GetAttackArmyThreshold();

        // 3. 己方 > 敌方1.5倍 且 军队足够 → Attack
        if (friendlyForce > enemyForce * 1.5f && armyCount >= attackThreshold)
        {
            SetStrategy(teamId, AIStrategy.Attack, $"force advantage 1.5x+ ({friendlyForce} vs {enemyForce})");
            return;
        }

        // 4. 军队≥阈值 且无紧急 → Attack
        if (armyCount >= attackThreshold)
        {
            SetStrategy(teamId, AIStrategy.Attack, $"army {armyCount} >= {attackThreshold}");
            return;
        }

        // 5. 有科技中心+军队≥8 → BuildUp
        if (hasTech && armyCount >= 8)
        {
            if (state.Strategy != AIStrategy.BuildUp)
                SetStrategy(teamId, AIStrategy.BuildUp, $"tech center + army {armyCount} >= 8");
            return;
        }

        // 6. 默认Expand
        if (state.Strategy != AIStrategy.Expand)
            SetStrategy(teamId, AIStrategy.Expand, "default expansion");
    }

    /// <summary>切换AI阵营策略状态，处理状态转换副作用。</summary>
    private void SetStrategy(int teamId, AIStrategy newStrategy, string reason)
    {
        ref var state = ref _aiStrategyStates[teamId];
        AIStrategy oldStrategy = state.Strategy;
        if (oldStrategy == newStrategy) return;
        state.Strategy = newStrategy;

        // 切换到Attack时初始化集结系统
        if (newStrategy == AIStrategy.Attack)
        {
            state.AssaultLaunched = false;
            state.HasRallyPoint = false;
            state.RallyWaitTimer = 0f;
            state.AssaultTargetTeamId = -1;
        }

        // 离开Attack时清理集结模式
        if (oldStrategy == AIStrategy.Attack && newStrategy != AIStrategy.Attack)
        {
            state.HasRallyPoint = false;
            state.AssaultLaunched = false;
            // 清除所有单位的集结模式
            foreach (var u in GetTeamCombatUnits(teamId))
                u.AiRallyMode = false;
        }

        GameLog.Info($"[AI-Strategy] Team {teamId} {oldStrategy} -> {newStrategy} ({reason})");
    }

    /// <summary>通知策略系统某AI阵营建筑被攻击，触发Defend策略。</summary>
    private void NotifyBuildingAttackedForStrategy(int teamId)
    {
        if (teamId < 1 || teamId > _activeAiCount) return;
        _aiStrategyStates[teamId].LastAttackedTimer = 10f; // 10秒Defend状态
    }

    // ======== 战术评估 ========

    /// <summary>计算指定AI阵营基地800px范围内的己方/敌方战斗兵力比。
    /// 返回(己方战斗单位数, 敌方战斗单位数)。</summary>
    private (int friendly, int enemy) CountNearbyForceRatio(int teamId)
    {
        if (!_bases.TryGetValue(teamId, out var baseB) || !IsInstanceValid(baseB))
            return (0, 0);

        Vector2 basePos = baseB.GlobalPosition;
        const float evalRadius = 800f;
        int friendly = 0, enemy = 0;

        foreach (var u in GetAllUnits())
        {
            if (!IsInstanceValid(u) || u.IsDead) continue;
            if (u.AttackDamage <= 0f) continue; // 只算战斗单位
            float d = u.GlobalPosition.DistanceTo(basePos);
            if (d > evalRadius) continue;

            if (u.TeamId == teamId)
                friendly++;
            else
                enemy++;
        }
        return (friendly, enemy);
    }

    /// <summary>统计指定阵营的战斗单位数（排除矿车等非战斗单位）。</summary>
    private int CountCombatUnitsOfTeam(int teamId)
    {
        int n = 0;
        foreach (var c in _unitsNode.GetChildren())
        {
            if (c is Unit u && u.TeamId == teamId && IsInstanceValid(u) && u.AttackDamage > 0f)
                n++;
        }
        return n;
    }

    // ======== 进攻集结系统 ========

    /// <summary>Attack策略时的进攻集结逻辑（在AITickForTeam中调用）。
    /// 1. 选定敌方建筑为进攻目标
    /// 2. 设定集结点（己方基地朝目标方向偏移200px）
    /// 3. 命令所有战斗单位MoveTo集结点（设置AiRallyMode）
    /// 实际发起进攻（≥N单位集结 或 等待超时）在UpdateAIStrategyTimers中实时检查。</summary>
    private void AIAssaultRally(int teamId)
    {
        ref var state = ref _aiStrategyStates[teamId];
        if (state.AssaultLaunched) return; // 当前一波进攻未结束

        // 1. 选择进攻目标阵营
        if (state.AssaultTargetTeamId < 0)
        {
            state.AssaultTargetTeamId = SelectAssaultTarget(teamId);
            if (state.AssaultTargetTeamId < 0) return; // 无有效目标
        }

        // 2. 设定集结点并命令军队集结
        if (!state.HasRallyPoint)
        {
            state.RallyPoint = SelectRallyPoint(teamId, state.AssaultTargetTeamId);
            state.HasRallyPoint = true;
            state.RallyWaitTimer = GetRallyWaitTimeout();
            state.RallyCheckTimer = 0.5f;

            // 命令所有战斗单位移动到集结点
            foreach (var u in GetTeamCombatUnits(teamId))
            {
                u.AiRallyMode = true;
                u.AiRallyPoint = state.RallyPoint;
                u.CommandMove(state.RallyPoint);
            }
            GameLog.Debug($"[AI-Rally] Team {teamId} rallying at {state.RallyPoint}, target=Team {state.AssaultTargetTeamId}");
        }
    }

    /// <summary>发起进攻：命令所有战斗单位AttackMove到目标。</summary>
    private void LaunchAssault(int teamId)
    {
        ref var state = ref _aiStrategyStates[teamId];
        if (state.AssaultTargetTeamId < 0) return;

        Vector2 assaultTarget = GetAssaultTargetPosition(state.AssaultTargetTeamId);
        foreach (var u in GetTeamCombatUnits(teamId))
        {
            u.AiRallyMode = false;
            u.CommandAttackMove(assaultTarget);
        }
        state.AssaultLaunched = true;
        state.AssaultCooldown = GetAssaultCooldown();
        state.HasRallyPoint = false;

        int unitCount = CountCombatUnitsOfTeam(teamId);
        GameLog.Info($"[AI-Assault] Team {teamId} launching assault with {unitCount} units -> Team {state.AssaultTargetTeamId} at {assaultTarget}");
    }

    /// <summary>选择进攻目标阵营（最近的非己方有基地的阵营）。</summary>
    private int SelectAssaultTarget(int teamId)
    {
        if (!_bases.TryGetValue(teamId, out var myBase) || !IsInstanceValid(myBase))
            return -1;

        Vector2 myPos = myBase.GlobalPosition;
        int bestTeam = -1;
        float bestDist = float.MaxValue;

        for (int t = 0; t < TotalTeamCount; t++)
        {
            if (t == teamId) continue;
            if (!_bases.TryGetValue(t, out var b) || !IsInstanceValid(b)) continue;
            // 优先攻击玩家（0号），其次是其他AI
            float d = myPos.DistanceTo(b.GlobalPosition);
            if (t == PlayerTeamId) d *= 0.7f; // 玩家优先
            if (d < bestDist)
            {
                bestDist = d;
                bestTeam = t;
            }
        }
        return bestTeam;
    }

    /// <summary>选择集结点：己方基地朝目标方向偏移200px。</summary>
    private Vector2 SelectRallyPoint(int teamId, int targetTeamId)
    {
        if (!_bases.TryGetValue(teamId, out var myBase) || !IsInstanceValid(myBase))
            return Vector2.Zero;

        Vector2 myPos = myBase.GlobalPosition;
        Vector2 targetPos = myPos;

        if (targetTeamId >= 0 && _bases.TryGetValue(targetTeamId, out var targetBase) && IsInstanceValid(targetBase))
            targetPos = targetBase.GlobalPosition;

        // 从基地朝目标方向偏移200px
        Vector2 dir = (targetPos - myPos).Normalized();
        Vector2 rally = myPos + dir * 200f;
        return ClampToMap(rally, 50f);
    }

    /// <summary>获取目标阵营的进攻目标坐标（敌方基地位置）。</summary>
    private Vector2 GetAssaultTargetPosition(int targetTeamId)
    {
        if (targetTeamId >= 0 && _bases.TryGetValue(targetTeamId, out var b) && IsInstanceValid(b))
            return b.GlobalPosition;
        return Vector2.Zero;
    }

    /// <summary>统计在集结点150px范围内的己方战斗单位数。</summary>
    private int CountUnitsNearRally(int teamId, Vector2 rallyPoint)
    {
        int n = 0;
        foreach (var c in _unitsNode.GetChildren())
        {
            if (c is Unit u && u.TeamId == teamId && IsInstanceValid(u) && u.AttackDamage > 0f
                && u.GlobalPosition.DistanceTo(rallyPoint) < 150f)
                n++;
        }
        return n;
    }

    /// <summary>获取指定阵营的所有战斗单位列表。</summary>
    private List<Unit> GetTeamCombatUnits(int teamId)
    {
        var list = new List<Unit>();
        foreach (var c in _unitsNode.GetChildren())
        {
            if (c is Unit u && u.TeamId == teamId && IsInstanceValid(u) && u.AttackDamage > 0f)
                list.Add(u);
        }
        return list;
    }

    // ======== 策略计时器更新（每帧调用） ========

    /// <summary>每帧更新AI策略相关计时器。在_Process中调用。</summary>
    private void UpdateAIStrategyTimers(float dt)
    {
        for (int t = 1; t <= _activeAiCount; t++)
        {
            ref var s = ref _aiStrategyStates[t];

            // 建筑受袭倒计时
            if (s.LastAttackedTimer > 0f)
            {
                s.LastAttackedTimer -= dt;
                if (s.LastAttackedTimer <= 0f && s.Strategy == AIStrategy.Defend)
                {
                    // 10秒无攻击 -> 回BuildUp
                    s.LastAttackedTimer = 0f;
                    SetStrategy(t, AIStrategy.BuildUp, "no attacks for 10s");
                }
            }

            // 集结等待计时器
            if (s.RallyWaitTimer > 0f)
            {
                s.RallyWaitTimer -= dt;
                if (s.RallyWaitTimer < 0f) s.RallyWaitTimer = 0f;
            }

            // 进攻后冷却计时器
            if (s.AssaultCooldown > 0f)
            {
                s.AssaultCooldown -= dt;
                if (s.AssaultCooldown < 0f) s.AssaultCooldown = 0f;
            }

            // 实时检查集结条件 → 发起进攻
            if (s.Strategy == AIStrategy.Attack && !s.AssaultLaunched && s.HasRallyPoint)
            {
                s.RallyCheckTimer -= dt;
                if (s.RallyCheckTimer <= 0f)
                {
                    s.RallyCheckTimer = 0.5f;
                    int rallied = CountUnitsNearRally(t, s.RallyPoint);
                    bool timeout = s.RallyWaitTimer <= 0f;
                    if (rallied >= GetRallyMinUnits() || timeout)
                        LaunchAssault(t);
                }
            }

            // 进攻冷却结束后允许新一轮集结
            if (s.Strategy == AIStrategy.Attack && s.AssaultLaunched && s.AssaultCooldown <= 0f)
            {
                s.AssaultLaunched = false;
            }
        }
    }
}
