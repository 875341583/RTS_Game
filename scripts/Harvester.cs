using Godot;

namespace RTSGame;

/// <summary>
/// 矿车：继承 Unit，重写 ProcessAI 实现状态机自动采矿循环。
/// 状态：Idle → ToMine → Mining → ToBase → Unloading → Idle …
/// </summary>
public partial class Harvester : Unit
{
    public enum HState { Idle, ToMine, Mining, ToBase, Unloading, Fleeing }

    [Export] public float CargoCapacity { get; set; } = 50f;
    [Export] public float MineTime { get; set; } = 2f;
    [Export] public float UnloadTime { get; set; } = 1f;
    [Export] public int MineYieldPerCycle { get; set; } = 30;
    [Export] public float FleeRange { get; set; } = 150f;

    private HState _state = HState.Idle;
    private ResourceNode? _targetMine;
    private float _timer;
    private float _cargo;
    private HState _preFleeState;
    /// <summary>所属基地引用。Main 初始化时设置。</summary>
    public Building? HomeBase { get; set; }

    public override void _Ready()
    {
        UnitName = "矿车";
        MaxHealth = 120;
        MoveSpeed = 160f;
        AttackRange = 0f;
        AttackDamage = 0f;
        AutoDefend = false;
        base._Ready();
        // 矿车使用基类设置的阵营色染色（与战斗单位一致）
    }

    protected override void ProcessAI(float dt)
    {
        // 优先检测：附近有敌方战斗单位时逃跑
        if (_state != HState.Fleeing && _state != HState.Unloading)
        {
            var threat = FindNearestEnemyUnitInRange(FleeRange);
            if (threat != null && threat.AttackDamage > 0f)
            {
                _preFleeState = _state;
                _state = HState.Fleeing;
            }
        }

        switch (_state)
        {
            case HState.Fleeing:
            {
                // 逃离敌人，迫回基地
                var threat = FindNearestEnemyUnitInRange(FleeRange * 1.5f);
                if (threat == null || threat.AttackDamage <= 0f)
                {
                    // 威胁解除，恢复之前的状态
                    _state = _preFleeState;
                    if (_state == HState.Mining || _state == HState.ToMine)
                    {
                        if (_targetMine != null && IsInstanceValid(_targetMine) && !_targetMine.IsDepleted)
                            MoveTo(_targetMine.GlobalPosition);
                        else
                            _state = HState.Idle;
                    }
                    break;
                }
                // 朝远离敌人的方向逃，同时向基地靠近
                if (HomeBase != null && IsInstanceValid(HomeBase))
                {
                    var fleeDir = (GlobalPosition - threat.GlobalPosition).Normalized();
                    var toBase = (HomeBase.GlobalPosition - GlobalPosition).Normalized();
                    var fleePos = GlobalPosition + (fleeDir * 0.6f + toBase * 0.4f) * 200f;
                    MoveTo(fleePos);
                }
                break;
            }
            case HState.Idle:
            {
                var mine = FindNearestMine();
                _targetMine = mine;
                if (mine != null)
                {
                    MoveTo(mine.GlobalPosition);
                    _state = HState.ToMine;
                }
                else StopMove();
                break;
            }
            case HState.ToMine:
            {
                if (_targetMine == null || !IsInstanceValid(_targetMine) || _targetMine.IsDepleted)
                {
                    _state = HState.Idle;
                    break;
                }
                if (GlobalPosition.DistanceTo(_targetMine.GlobalPosition) < 45f)
                {
                    StopMove();
                    _state = HState.Mining;
                    _timer = MineTime;
                }
                break;
            }
            case HState.Mining:
            {
                if (_targetMine == null || !IsInstanceValid(_targetMine) || _targetMine.IsDepleted)
                {
                    _state = HState.Idle;
                    break;
                }
                _timer -= dt;
                if (_timer <= 0f)
                {
                    int harvested = _targetMine.Harvest(Mathf.Min(MineYieldPerCycle, _targetMine.Amount));
                    _cargo += harvested;
                    if (_cargo >= CargoCapacity || _targetMine.IsDepleted)
                    {
                        if (HomeBase != null && IsInstanceValid(HomeBase))
                        {
                            MoveTo(HomeBase.GlobalPosition);
                            _state = HState.ToBase;
                        }
                        else
                        {
                            _cargo = 0f;
                            _state = HState.Idle;
                        }
                    }
                    else
                    {
                        _timer = MineTime;
                    }
                }
                break;
            }
            case HState.ToBase:
            {
                if (HomeBase == null || !IsInstanceValid(HomeBase))
                {
                    _state = HState.Idle;
                    break;
                }
                if (GlobalPosition.DistanceTo(HomeBase.GlobalPosition) < 70f)
                {
                    StopMove();
                    _state = HState.Unloading;
                    _timer = UnloadTime;
                }
                break;
            }
            case HState.Unloading:
            {
                _timer -= dt;
                if (_timer <= 0f)
                {
                    if (_cargo > 0f && HomeBase != null && IsInstanceValid(HomeBase))
                    {
                        float deposit = _cargo;
                        // 工程车采矿辅助：附近140范围内有友方工程车，收益×1.5
                        var unitsNode = GetParent();
                        if (unitsNode != null)
                        {
                            foreach (var c in unitsNode.GetChildren())
                            {
                                if (c is Unit u && u != this && IsInstanceValid(u)
                                    && u.TeamId == TeamId && u.Type == UnitType.Engineer
                                    && u.GlobalPosition.DistanceTo(GlobalPosition) <= 140f)
                                {
                                    deposit *= 1.5f;
                                    break;
                                }
                            }
                        }
                        HomeBase.Deposit(deposit);
                    }
                    _cargo = 0f;
                    _state = HState.Idle;
                }
                break;
            }
        }

        // 红方矿车：不需要玩家命令，也不主动攻击，由状态机驱动
        // 注意：不要调用 base.ProcessAI，避免 AutoAI 覆盖状态机行为
    }

    private ResourceNode? FindNearestMine()
    {
        var ores = GetParent()?.GetParent()?.GetNodeOrNull<Node>("Resources");
        if (ores == null) return null;
        ResourceNode? best = null;
        float bestDist = float.MaxValue;
        foreach (var child in ores.GetChildren())
        {
            if (child is ResourceNode r && !r.IsDepleted && IsInstanceValid(r))
            {
                var d = GlobalPosition.DistanceSquaredTo(r.GlobalPosition);
                if (d < bestDist) { bestDist = d; best = r; }
            }
        }
        return best;
    }

    /// <summary>当玩家手动右键指挥矿车时，临时打断 AI，到达后自动恢复状态机。</summary>
    public override void CommandMove(Vector2 target)
    {
        base.CommandMove(target);
        _state = HState.Idle;
        _targetMine = null;
    }
}
