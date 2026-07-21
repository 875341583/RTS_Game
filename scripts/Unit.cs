using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 兵种类型枚举。
/// </summary>
public enum UnitType { LightTank, HeavyTank, Artillery, RocketLauncher, MissileTank, AntiAir, Infantry, Engineer, Default }

/// <summary>
/// RTS 单位基类：支持选中和移动命令，带血量和简单攻击。
/// 子类可重写 ProcessAI 自定义 AI 行为（如矿车自动采矿）。
/// </summary>
public partial class Unit : CharacterBody2D
{
    [Export] public float MoveSpeed { get; set; } = 200f;
    [Export] public float MaxHealth { get; set; } = 100f;
    [Export] public float AttackDamage { get; set; } = 15f;
    [Export] public float AttackRange { get; set; } = 150f;
    [Export] public float AttackCooldown { get; set; } = 1.0f;
    [Export] public string UnitName { get; set; } = "Tank";

    /// <summary>当前兵种类型。</summary>
    public UnitType Type { get; set; } = UnitType.Default;
    /// <summary>最小攻击射程（炮兵不能攻击太近的目标）。</summary>
    public float MinAttackRange { get; set; } = 0f;
    /// <summary>溅射伤害范围（0=无溅射）。火箭炮对目标周围单位造成溅射伤害。</summary>
    public float SplashRadius { get; set; } = 0f;

    public float Health { get; protected set; }
    public bool IsSelected { get; protected set; }
    public int TeamId { get; set; } = 0;
    /// <summary>红方自动战斗 AI 开关。开启后主动全图搜索敌人攻击。</summary>
    public bool AutoAI { get; set; } = false;
    /// <summary>自动防御开关。无命令时发现附近敌人自动迎击，消灭后返回守卫位置。</summary>
    public bool AutoDefend { get; set; } = true;
    /// <summary>自动防御警戒范围。</summary>
    public float AggroRange { get; set; } = 280f;

    // 子类可访问的移动状态
    protected Vector2 _moveTarget;
    protected bool _hasMoveTarget;
    private Unit? _attackUnitTarget;
    private Building? _attackBuildingTarget;
    private float _attackTimer;
    protected bool _isDead;
    private float _hitFlashTimer;
    private Color _bodyTint = Colors.White;
    private Color _turretTint = Colors.White;
    private float _aiThinkTimer;
    private Vector2 _attackMoveTarget;
    private bool _hasAttackMoveTarget;
    private Vector2 _guardPosition;
    private bool _hasGuardPosition;

    // 节点引用
    protected Sprite2D _body = null!;
    private Sprite2D _selectionRing = null!;
    private ProgressBar _healthBar = null!;
    // 椭圆阴影点（32边形，缓存复用）
    private static readonly Vector2[] _shadowPtsLarge = GenEllipsePoints(26f, 13f);
    private static readonly Vector2[] _shadowPtsSmall = GenEllipsePoints(13f, 7f);
    private static readonly Vector2[] _shadowPtsBldg = GenEllipsePoints(52f, 26f);
    private static readonly Color _shadowColor = new(0, 0, 0, 0.4f);

    private static Vector2[] GenEllipsePoints(float rx, float ry)
    {
        var pts = new Vector2[32];
        for (int i = 0; i < 32; i++)
        {
            float a = i * Mathf.Pi * 2f / 32f;
            pts[i] = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return pts;
    }

    public static Vector2[] GetBuildingShadowPoints() => _shadowPtsBldg;
    public static Color GetShadowColor() => _shadowColor;

    private static Texture2D? _ringTex;
    // 灰底底盘纹理（按兵种，一套支持任意阵营色染色）
    private static Texture2D? _hullLight, _hullHeavy, _hullArty, _hullRocket, _hullMissile, _hullAntiAir, _hullEngineer;
    private static Texture2D? _harvesterHull;
    // 灰底步兵纹理（32x32俯视）
    private static Texture2D? _infantryHull;
    // 灰底炮塔纹理（按兵种）
    private static Texture2D? _turretLight, _turretHeavy, _turretArty, _turretRocket, _turretMissile, _turretAntiAir;
    // 炮塔精灵
    protected Sprite2D _turret = null!;
    // 新素材朝右（RIGHT=0°），无需额外旋转偏移
    private const float SpriteRotationOffset = 0f;

    // ---- 8阵营色调色板（灰底素材用 Modulate 染色）----
    /// <summary>8阵营色（基于红警2原版8色，明度/色相优化辨识度）。索引=TeamId。超出范围取模。</summary>
    private static readonly Color[] TeamPalette =
    {
        new(0.82f, 0.16f, 0.16f), // 0 Red   纯红
        new(0.16f, 0.32f, 0.82f), // 1 Blue  深蓝
        new(0.18f, 0.78f, 0.22f), // 2 Green 纯绿（亮）
        new(0.95f, 0.82f, 0.18f), // 3 Yellow 明黄
        new(0.95f, 0.42f, 0.78f), // 4 Pink  亮粉（明度高）
        new(0.44f, 0.18f, 0.72f), // 5 Purple 深紫（明度低）
        new(0.95f, 0.51f, 0.12f), // 6 Orange 亮橙
        new(0.14f, 0.62f, 0.88f), // 7 Cyan  偏蓝青（与2纯绿拉大色相差）
    };

    /// <summary>获取 TeamId 对应的阵营色。</summary>
    public static Color GetTeamColor(int teamId) => TeamPalette[teamId % TeamPalette.Length];

    /// <summary>加载灰底单位 PNG 纹理（一套支持任意阵营色染色）。</summary>
    private static void EnsureTextures()
    {
        if (_hullLight != null) return;

        // 灰底底盘（按兵种）
        _hullLight   = LoadUnitTexture("res://assets/sprites/units/hull_light.png");
        _hullHeavy   = LoadUnitTexture("res://assets/sprites/units/hull_heavy.png");
        _hullArty    = LoadUnitTexture("res://assets/sprites/units/hull_arty.png");
        _hullRocket  = LoadUnitTexture("res://assets/sprites/units/hull_rocket.png");
        _hullMissile = LoadUnitTexture("res://assets/sprites/units/hull_missile.png");
        _hullAntiAir  = LoadUnitTexture("res://assets/sprites/units/hull_antiair.png");
        _hullEngineer  = LoadUnitTexture("res://assets/sprites/units/hull_engineer.png");

        // 步兵（32x32灰底俯视）
        _infantryHull = LoadUnitTexture("res://assets/sprites/units/infantry.png");

        // 矿车（灰底，染色）
        _harvesterHull = LoadUnitTexture("res://assets/sprites/units/harvester.png");

        // 灰底炮塔（按兵种）
        _turretLight   = LoadUnitTexture("res://assets/sprites/units/turret_light.png");
        _turretHeavy   = LoadUnitTexture("res://assets/sprites/units/turret_heavy.png");
        _turretArty    = LoadUnitTexture("res://assets/sprites/units/turret_arty.png");
        _turretRocket  = LoadUnitTexture("res://assets/sprites/units/turret_rocket.png");
        _turretMissile = LoadUnitTexture("res://assets/sprites/units/turret_missile.png");
        _turretAntiAir  = LoadUnitTexture("res://assets/sprites/units/turret_antiair.png");

        // ---- 选中环（保留）----
        var ring = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        ring.Fill(new Color(0, 0, 0, 0));
        for (float a = 0; a < Mathf.Tau; a += 0.03f)
        {
            int cx = (int)(32 + 28 * Mathf.Cos(a));
            int cy = (int)(32 + 28 * Mathf.Sin(a));
            if (cx >= 0 && cx < 64 && cy >= 0 && cy < 64)
            {
                ring.SetPixel(cx, cy, Colors.Lime);
                if (cx + 1 < 64) ring.SetPixel(cx + 1, cy, Colors.Lime);
                if (cy + 1 < 64) ring.SetPixel(cx, cy + 1, Colors.Lime);
            }
        }
        _ringTex = ImageTexture.CreateFromImage(ring);
    }

    private static Texture2D LoadUnitTexture(string path)
    {
        var tex = GD.Load<Texture2D>(path);
        if (tex == null)
        {
            GD.PrintErr($"[Unit] Failed to load texture: {path}");
            var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            img.SetPixel(0, 0, Colors.Magenta);
            return ImageTexture.CreateFromImage(img);
        }
        return tex; // Godot 导入 PNG 返回 CompressedTexture2D，不是 ImageTexture
    }

    /// <summary>根据兵种获取灰底底盘纹理（不区分阵营，染色由 Modulate 完成）。</summary>
    private Texture2D GetHullTexture(UnitType type, int teamId) => type switch
    {
        UnitType.LightTank => _hullLight!,
        UnitType.HeavyTank => _hullHeavy!,
        UnitType.Artillery => _hullArty!,
        UnitType.RocketLauncher => _hullRocket!,
        UnitType.MissileTank => _hullMissile!,
        UnitType.AntiAir => _hullAntiAir!,
        UnitType.Engineer => _hullEngineer!,
        UnitType.Infantry => _infantryHull!,
        _ => _harvesterHull!
    };

    /// <summary>根据兵种获取灰底炮塔纹理。</summary>
    private Texture2D GetTurretTexture(UnitType type, int teamId) => type switch
    {
        UnitType.LightTank => _turretLight!,
        UnitType.HeavyTank => _turretHeavy!,
        UnitType.Artillery => _turretArty!,
        UnitType.RocketLauncher => _turretRocket!,
        UnitType.MissileTank => _turretMissile!,
        UnitType.AntiAir => _turretAntiAir!,
        // 工程车无炮塔（底盘已含维修吊臂）
        UnitType.Engineer => null!,
        // 步兵无炮塔（身体朝向代替炮塔朝向）
        UnitType.Infantry => null!,
        _ => null!
    };

    /// <summary>按兵种类型初始化属性。必须在 _Ready 之前调用。</summary>
    public void InitAsType(UnitType type)
    {
        Type = type;
        switch (type)
        {
            case UnitType.LightTank:
                UnitName = "轻坦克";
                MaxHealth = 70f;
                MoveSpeed = 250f;
                AttackDamage = 10f;
                AttackRange = 130f;
                AttackCooldown = 0.8f;
                AggroRange = 250f;
                break;
            case UnitType.HeavyTank:
                UnitName = "重坦克";
                MaxHealth = 180f;
                MoveSpeed = 150f;
                AttackDamage = 30f;
                AttackRange = 160f;
                AttackCooldown = 1.5f;
                AggroRange = 300f;
                break;
            case UnitType.Artillery:
                UnitName = "炮兵";
                MaxHealth = 60f;
                MoveSpeed = 100f;
                AttackDamage = 40f;
                AttackRange = 300f;
                AttackCooldown = 2.5f;
                MinAttackRange = 100f;
                AggroRange = 350f;
                break;
            case UnitType.RocketLauncher:
                UnitName = "火箭炮";
                MaxHealth = 90f;
                MoveSpeed = 110f;
                AttackDamage = 50f;
                AttackRange = 360f;
                AttackCooldown = 3.0f;
                MinAttackRange = 120f;
                SplashRadius = 80f;
                AggroRange = 380f;
                break;
            case UnitType.MissileTank:
                UnitName = "导弹车";
                MaxHealth = 70f;
                MoveSpeed = 130f;
                AttackDamage = 80f;
                AttackRange = 420f;
                AttackCooldown = 4.0f;
                MinAttackRange = 150f;
                AggroRange = 440f;
                break;
            case UnitType.AntiAir:
                UnitName = "防空车";
                MaxHealth = 70f;
                MoveSpeed = 220f;
                AttackDamage = 8f;
                AttackRange = 140f;
                AttackCooldown = 0.45f;
                AggroRange = 260f;
                break;
            case UnitType.Infantry:
                UnitName = "步兵";
                MaxHealth = 35f;
                MoveSpeed = 90f;
                AttackDamage = 6f;
                AttackRange = 100f;
                AttackCooldown = 0.6f;
                AggroRange = 200f;
                break;
            case UnitType.Engineer:
                UnitName = "工程车";
                MaxHealth = 120f;
                MoveSpeed = 240f;
                AttackDamage = 0f;     // 纯辅助不攻击
                AttackRange = 0f;
                AttackCooldown = 0f;
                AggroRange = 0f;       // 不主动锁定目标
                break;
        }
    }

    public override void _Ready()
    {
        Health = MaxHealth;
        _moveTarget = GlobalPosition;
        _attackTimer = 0f;

        _body = GetNode<Sprite2D>("Body");
        _selectionRing = GetNode<Sprite2D>("SelectionRing");
        _healthBar = GetNode<ProgressBar>("HealthBar");

        EnsureTextures();

        // 8阵营色：灰底素材 + Modulate 染色
        var teamColor = GetTeamColor(TeamId);

        // 按兵种加载灰底底盘纹理，运行时按 TeamId 染色
        _body.Texture = GetHullTexture(Type, TeamId);
        _body.Modulate = teamColor;
        _bodyTint = teamColor;
        // 步兵32×32素材按 0.85 缩放，更贴近红警2步兵体里坦克的视觉比例
        _body.Scale = (Type == UnitType.Infantry) ? new Vector2(0.9f, 0.9f) : Vector2.One;
        // 像素艺术必须用 Nearest 过滤，Linear 会把 14-17 色的底盘细节插值平滑成单色块
        _body.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        _selectionRing.Texture = _ringTex;
        _selectionRing.Visible = false;
        _healthBar.MaxValue = MaxHealth;
        _healthBar.Value = Health;
        UpdateHealthBarVisibility();

        // 炮塔精灵（战斗单位专用，矿车、步兵和工程车不需要）
        if (this is not Harvester && Type != UnitType.Infantry && Type != UnitType.Engineer)
        {
            _turret = new Sprite2D { Name = "Turret", ZIndex = 1, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
            AddChild(_turret);
            _turret.Texture = GetTurretTexture(Type, TeamId);
            // 新素材：炮塔圆盘在图片正中心(32,32)，centered=true 自动对齐旋转中心
            if (_turret.Texture != null)
            {
                _turret.Offset = Vector2.Zero;
                _turret.Scale = Vector2.One;
            }
            _turret.Modulate = teamColor; // 灰底炮塔染色
            _turretTint = teamColor;
        }
        // 矿车：底盘染色统一走 teamColor，无需分支
    }

    public sealed override void _Process(double delta)
    {
        if (_isDead) return;
        var dt = (float)delta;

        // Q5：受击闪白效果
        if (_hitFlashTimer > 0)
        {
            _hitFlashTimer -= dt;
            _body.Modulate = new Color(3f, 3f, 3f); // 过亮闪白
            if (_turret != null) _turret.Modulate = Colors.White;
        }
        else
        {
            _body.Modulate = _bodyTint;
            if (_turret != null) _turret.Modulate = _turretTint;
        }

        // 阴影始终水平（由_Draw绘制，需要每帧QueueRedraw）
        QueueRedraw();

        // 调度子类自定义 AI（默认是玩家命令 + 攻击逻辑）
        ProcessAI(dt);

        // 如果子类 AI 没有清理攻击目标，让基类处理追击/开火
        ResolveCombat(dt);

        // 通用移动
        ProcessMovement(dt);

        // Q3：炮塔朝向目标平滑旋转
        UpdateTurretRotation(dt);

        // 工程车辅助：每帧治疗附近的友方建筑/单位
        if (Type == UnitType.Engineer) TryRepairNearby(dt);
    }

    /// <summary>工程车辅助逻辑：修理140范围内友方单位(25 HP/s)和建筑(50 HP/s)。</summary>
    private void TryRepairNearby(float dt)
    {
        const float repairRange = 140f;
        const float unitHealPerSec = 25f;
        const float buildHealPerSec = 50f;
        var pos = GlobalPosition;

        var main = GetParent()?.GetParent();
        if (main == null) return;

        // 1. 修友方单位
        var unitsNode = GetParent();
        if (unitsNode != null)
        {
            foreach (var c in unitsNode.GetChildren())
            {
                if (c is Unit u && u != this && IsInstanceValid(u) && u.TeamId == TeamId && u.Health < u.MaxHealth)
                {
                    if (u.GlobalPosition.DistanceTo(pos) <= repairRange)
                        u.Heal(unitHealPerSec * dt);
                }
            }
        }

        // 2. 修友方建筑 & 3. 占领敌方建筑（共用一次 Buildings 遍历）
        var bnode = main.GetNodeOrNull<Node>("Buildings");
        if (bnode != null)
        {
            foreach (var c in bnode.GetChildren())
            {
                if (c is Building b && IsInstanceValid(b))
                {
                    if (b.GlobalPosition.DistanceTo(pos) > repairRange) continue;
                    if (b.TeamId == TeamId && b.Health < b.MaxHealth)
                        b.RepairByEngineer(buildHealPerSec * dt);
                    else if (b.TeamId != TeamId)
                        b.CaptureTick(dt, TeamId);
                }
            }
        }
    }

    /// <summary>治疗单位：增加 Health，但不超过 MaxHealth。可被工程车外部调用。</summary>
    public void Heal(float amount)
    {
        if (_isDead || amount <= 0f) return;
        Health = Mathf.Min(MaxHealth, Health + amount);
        UpdateHealthBarVisibility();
    }

    /// <summary>Q3：炮塔朝向攻击目标平滑旋转，无目标时跟随车体方向。</summary>
    private void UpdateTurretRotation(float dt)
    {
        if (_turret == null) return;

        float targetAngle = _body.Rotation; // 默认跟随车体（已含 SpriteRotationOffset）
        bool hasTarget = false;

        if (_attackUnitTarget != null && IsInstanceValid(_attackUnitTarget))
        {
            targetAngle = (_attackUnitTarget.GlobalPosition - GlobalPosition).Angle() + SpriteRotationOffset;
            hasTarget = true;
        }
        else if (_attackBuildingTarget != null && IsInstanceValid(_attackBuildingTarget))
        {
            targetAngle = (_attackBuildingTarget.GlobalPosition - GlobalPosition).Angle() + SpriteRotationOffset;
            hasTarget = true;
        }
        else if (_hasMoveTarget)
        {
            var dir = _moveTarget - GlobalPosition;
            if (dir.Length() > 5f)
            {
                targetAngle = dir.Angle() + SpriteRotationOffset;
                hasTarget = true;
            }
        }

        float diff = Mathf.AngleDifference(_turret.Rotation, targetAngle);
        float speed = hasTarget ? 8f : 5f;
        _turret.Rotation += diff * Mathf.Min(1f, dt * speed);
    }

    /// <summary>子类钩子：实现单位 AI（如矿车状态机或自动战斗）。默认实现玩家命令模式。</summary>
    protected virtual void ProcessAI(float dt)
    {
        if (AutoAI)
        {
            _aiThinkTimer -= dt;
            if (_aiThinkTimer > 0f) return;
            _aiThinkTimer = 0.5f;

            // 主动 AI：全图搜索敌人
            var enemy = FindNearestEnemyUnit();
            if (enemy != null)
            {
                _attackUnitTarget = enemy;
                _attackBuildingTarget = null;
            }
            else
            {
                var building = FindNearestEnemyBuilding();
                if (building != null)
                {
                    _attackBuildingTarget = building;
                    _attackUnitTarget = null;
                }
            }
            return;
        }

        // 攻击移动：移动到目标，途中遇敌自动接敌，消灭后继续向目标前进
        if (_hasAttackMoveTarget)
        {
            _aiThinkTimer -= dt;
            if (_aiThinkTimer <= 0f)
            {
                _aiThinkTimer = 0.25f;
                var enemy = FindNearestEnemyUnitInRange(AggroRange * 1.5f);
                if (enemy != null) _attackUnitTarget = enemy;
                else
                {
                    var bld = FindNearestEnemyBuilding();
                    if (bld != null && GlobalPosition.DistanceTo(bld.GlobalPosition) < AggroRange * 1.5f)
                        _attackBuildingTarget = bld;
                }
            }
            if (_attackUnitTarget == null && _attackBuildingTarget == null)
            {
                _moveTarget = _attackMoveTarget;
                _hasMoveTarget = true;
            }
            if (GlobalPosition.DistanceTo(_attackMoveTarget) < 20f)
            {
                _hasAttackMoveTarget = false;
                _hasMoveTarget = false;
            }
            return;
        }

        // 自动防御：无命令时警戒附近敌人
        if (AutoDefend && AttackDamage > 0f && _attackUnitTarget == null && _attackBuildingTarget == null)
        {
            _aiThinkTimer -= dt;
            if (_aiThinkTimer > 0f) return;
            _aiThinkTimer = 0.3f;

            // 记录守卫位置
            if (!_hasGuardPosition)
            {
                _guardPosition = GlobalPosition;
                _hasGuardPosition = true;
            }

            // 如果正在移动（玩家下令），不触发自动防御
            if (_hasMoveTarget) return;

            // 搜索警戒范围内的敌人
            var enemy = FindNearestEnemyUnitInRange(AggroRange);
            if (enemy != null)
            {
                _attackUnitTarget = enemy;
            }
            else
            {
                // 没有敌方单位时，搜索附近敌方建筑并攻击（单位开进敌方家会自动打建筑）
                var enemyBld = FindNearestEnemyBuildingInRange(AggroRange);
                if (enemyBld != null)
                {
                    _attackBuildingTarget = enemyBld;
                }
                else if (_hasGuardPosition && GlobalPosition.DistanceTo(_guardPosition) > 60f)
                {
                    MoveTo(_guardPosition);
                }
            }
        }
        else if (AutoDefend && _attackUnitTarget == null && _attackBuildingTarget == null && _hasMoveTarget)
        {
            // 玩家下达移动命令时更新守卫位置
            _guardPosition = _moveTarget;
            _hasGuardPosition = true;
        }
    }

    private void ResolveCombat(float dt)
    {
        // 攻击单位目标
        if (_attackUnitTarget != null)
        {
            if (_attackUnitTarget._isDead || !IsInstanceValid(_attackUnitTarget))
            {
                _attackUnitTarget = null;
            }
            else
            {
                var dist = GlobalPosition.DistanceTo(_attackUnitTarget.GlobalPosition);
                if (dist <= AttackRange && dist >= MinAttackRange)
                {
                    _hasMoveTarget = false;
                    _attackTimer -= dt;
                    if (_attackTimer <= 0)
                    {
                        _attackUnitTarget.TakeDamage(AttackDamage);
                        // Q5：开火视觉特效
                        SpawnFireEffects(_attackUnitTarget.GlobalPosition);
                        // 溅射伤害：对目标周围敌方单位造成 50% 伤害
                        if (SplashRadius > 0f && GetParent() is Node2D parent)
                        {
                            foreach (var child in parent.GetChildren())
                            {
                                if (child is Unit u && u != _attackUnitTarget && u.TeamId != TeamId && !u._isDead
                                    && u.GlobalPosition.DistanceTo(_attackUnitTarget.GlobalPosition) <= SplashRadius)
                                {
                                    u.TakeDamage(AttackDamage * 0.5f);
                                }
                            }
                        }
                        _attackTimer = AttackCooldown;
                    }
                }
                else if (dist < MinAttackRange)
                {
                    // 目标太近（炮兵），后退拉开距离
                    var away = (GlobalPosition - _attackUnitTarget.GlobalPosition).Normalized();
                    _moveTarget = GlobalPosition + away * (MinAttackRange - dist + 50f);
                    _hasMoveTarget = true;
                }
                else
                {
                    _moveTarget = _attackUnitTarget.GlobalPosition;
                    _hasMoveTarget = true;
                }
                return;
            }
        }

        // 攻击建筑目标
        if (_attackBuildingTarget != null)
        {
            if (!IsInstanceValid(_attackBuildingTarget) || _attackBuildingTarget.Health <= 0)
            {
                _attackBuildingTarget = null;
            }
            else
            {
                var dist = GlobalPosition.DistanceTo(_attackBuildingTarget.GlobalPosition);
                if (dist <= AttackRange && dist >= MinAttackRange)
                {
                    _hasMoveTarget = false;
                    _attackTimer -= dt;
                    if (_attackTimer <= 0)
                    {
                        _attackBuildingTarget.TakeDamage(AttackDamage);
                        // Q5：开火视觉特效
                        SpawnFireEffects(_attackBuildingTarget.GlobalPosition);
                        _attackTimer = AttackCooldown;
                    }
                }
                else
                {
                    _moveTarget = _attackBuildingTarget.GlobalPosition;
                    _hasMoveTarget = true;
                }
            }
        }
    }

    protected virtual void ProcessMovement(float dt)
    {
        if (_hasMoveTarget)
        {
            var direction = (_moveTarget - GlobalPosition);
            var distance = direction.Length();
            if (distance > 5f)
            {
                direction = direction.Normalized();
                Velocity = direction * MoveSpeed;
                MoveAndSlide();
                if (direction != Vector2.Zero)
                    _body.Rotation = direction.Angle() + SpriteRotationOffset;
            }
            else
            {
                Velocity = Vector2.Zero;
                _hasMoveTarget = false;
            }
        }
        else
        {
            Velocity = Vector2.Zero;
        }
    }

    // ---- 查询辅助（供子类使用）----
    protected Unit? FindNearestEnemyUnit()
    {
        if (GetParent() is not Node2D parent) return null;
        Unit? best = null;
        float bestDist = float.MaxValue;
        foreach (var child in parent.GetChildren())
        {
            if (child is Unit u && u.TeamId != TeamId && !u._isDead)
            {
                var d = GlobalPosition.DistanceSquaredTo(u.GlobalPosition);
                if (d < bestDist) { bestDist = d; best = u; }
            }
        }
        return best;
    }

    /// <summary>搜索指定范围内的最近敌方单位（用于自动防御）。</summary>
    protected Unit? FindNearestEnemyUnitInRange(float range)
    {
        if (GetParent() is not Node2D parent) return null;
        Unit? best = null;
        float bestDist = range * range;
        foreach (var child in parent.GetChildren())
        {
            if (child is Unit u && u.TeamId != TeamId && !u._isDead)
            {
                var d = GlobalPosition.DistanceSquaredTo(u.GlobalPosition);
                if (d < bestDist) { bestDist = d; best = u; }
            }
        }
        return best;
    }

    protected Building? FindNearestEnemyBuilding()
    {
        if (GetParent() is not Node2D parent) return null;
        var buildings = parent.GetParent()?.GetNodeOrNull<Node>("Buildings");
        if (buildings == null) return null;
        Building? best = null;
        float bestDist = float.MaxValue;
        foreach (var child in buildings.GetChildren())
        {
            if (child is Building b && b.TeamId != TeamId && b.Health > 0)
            {
                var d = GlobalPosition.DistanceSquaredTo(b.GlobalPosition);
                if (d < bestDist) { bestDist = d; best = b; }
            }
        }
        return best;
    }

    /// <summary>搜索指定范围内最近的敌方建筑（用于自动防御/攻击建筑）。</summary>
    protected Building? FindNearestEnemyBuildingInRange(float range)
    {
        if (GetParent() is not Node2D parent) return null;
        var buildings = parent.GetParent()?.GetNodeOrNull<Node>("Buildings");
        if (buildings == null) return null;
        Building? best = null;
        float bestDist = range * range;
        foreach (var child in buildings.GetChildren())
        {
            if (child is Building b && b.TeamId != TeamId && b.Health > 0)
            {
                var d = GlobalPosition.DistanceSquaredTo(b.GlobalPosition);
                if (d < bestDist) { bestDist = d; best = b; }
            }
        }
        return best;
    }

    // ---- 对外接口 ----
    public virtual void SetSelected(bool selected)
    {
        IsSelected = selected;
        if (_selectionRing != null)
            _selectionRing.Visible = selected;
        UpdateHealthBarVisibility();
    }

    public virtual void CommandMove(Vector2 target)
    {
        _moveTarget = target;
        _hasMoveTarget = true;
        _attackUnitTarget = null;
        _attackBuildingTarget = null;
        _hasAttackMoveTarget = false; // 普通移动取消攻击移动
        // 玩家下令时更新守卫位置为新的目的地
        _guardPosition = target;
        _hasGuardPosition = true;
    }

    /// <summary>攻击移动：移动到目标位置，途中遇敌自动接敌，消灭后继续前进。</summary>
    public void CommandAttackMove(Vector2 target)
    {
        _attackMoveTarget = target;
        _hasAttackMoveTarget = true;
        _moveTarget = target;
        _hasMoveTarget = true;
        _attackUnitTarget = null;
        _attackBuildingTarget = null;
    }

    /// <summary>停止：取消一切命令，原地转为守卫。</summary>
    public void CommandStop()
    {
        _hasMoveTarget = false;
        _hasAttackMoveTarget = false;
        _attackUnitTarget = null;
        _attackBuildingTarget = null;
        Velocity = Vector2.Zero;
        _guardPosition = GlobalPosition;
        _hasGuardPosition = true;
    }

    public virtual void CommandAttack(Unit target)
    {
        _attackUnitTarget = target;
        _attackBuildingTarget = null;
    }

    public virtual void CommandAttackBuilding(Building target)
    {
        _attackBuildingTarget = target;
        _attackUnitTarget = null;
    }

    public void TakeDamage(float damage)
    {
        Health -= damage;
        _hitFlashTimer = 0.08f; // Q5：受击闪白
        if (_healthBar != null)
            _healthBar.Value = Mathf.Max(0, Health);
        UpdateHealthBarVisibility();
        if (Health <= 0 && !_isDead) Die();
    }

    protected void MoveTo(Vector2 target) { _moveTarget = target; _hasMoveTarget = true; }
    protected void StopMove() { _hasMoveTarget = false; Velocity = Vector2.Zero; }

    private void UpdateHealthBarVisibility()
    {
        if (_healthBar != null)
            _healthBar.Visible = IsSelected || Health < MaxHealth;
    }

    protected virtual void Die()
    {
        _isDead = true;
        GD.Print($"{UnitName} (Team {TeamId}) destroyed!");
        // Q5：死亡爆炸特效，步兵用小爆炸，重坦用大爆炸，其他默认
        var main = GetParent()?.GetParent() as Node2D;
        if (main != null)
        {
            var effect = Type switch
            {
                UnitType.HeavyTank => BattleEffect.BigExplosion(GlobalPosition),
                UnitType.Infantry => BattleEffect.Explosion(GlobalPosition),  // 步兵用小爆炸
                _ => BattleEffect.Explosion(GlobalPosition)
            };
            main.AddChild(effect);
        }
        QueueFree();
    }

    /// <summary>Q5：开火时生成炮口闪光 + 炮弹飞行 + 命中爆炸特效。</summary>
    private void SpawnFireEffects(Vector2 targetPos)
    {
        var main = GetParent()?.GetParent() as Node2D;
        if (main == null) return;
        var dir = (targetPos - GlobalPosition).Normalized();
        main.AddChild(BattleEffect.MuzzleFlash(GlobalPosition + dir * 16f));
        main.AddChild(BattleEffect.Shell(GlobalPosition, targetPos));
        main.AddChild(BattleEffect.Explosion(targetPos));
    }

    public override void _Draw()
    {
        // 脚下椭圆阴影（始终水平：CharacterBody2D 节点本身不旋转，仅 _body sprite 旋转）
        // 通过 DrawSetTransform 把椭圆中心偏移到单位脚下偏右下，模拟光源在左上方
        var pts = (Type == UnitType.Infantry) ? _shadowPtsSmall : _shadowPtsLarge;
        float yOff = (Type == UnitType.Infantry) ? 8f : 18f;
        DrawSetTransform(new Vector2(3f, yOff), 0f, Vector2.One);
        DrawPolygon(pts, new Color[] { _shadowColor });
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
