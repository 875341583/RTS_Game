using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 兵种类型枚举。
/// </summary>
public enum UnitType { LightTank, HeavyTank, Artillery, RocketLauncher, MissileTank, Default }

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
    private float _aiThinkTimer;
    private Vector2 _attackMoveTarget;
    private bool _hasAttackMoveTarget;
    private Vector2 _guardPosition;
    private bool _hasGuardPosition;

    // 节点引用
    protected Sprite2D _body = null!;
    private Sprite2D _selectionRing = null!;
    private ProgressBar _healthBar = null!;

    private static ImageTexture? _ringTex;
    // Q3: 按兵种类型的底盘纹理
    private static ImageTexture? _lightHull, _heavyHull, _artyHull, _rocketHull, _missileHull, _harvesterHull;
    // Q3: 按兵种类型的炮塔纹理
    private static ImageTexture? _lightTurret, _heavyTurret, _artyTurret, _rocketTurret, _missileTurret;
    // Q3: 炮塔精灵（战斗单位专用，矿车不需要）
    protected Sprite2D _turret = null!;

    private static void EnsureTextures()
    {
        if (_lightHull != null) return;

        // ---- 底盘纹理（俯视图：履带+车身+发动机） ----
        _lightHull = CreateHull(56, 44, 8);
        _heavyHull = CreateHull(68, 52, 10, hasSkirt: true);
        _artyHull = CreateHull(60, 44, 7);
        _rocketHull = CreateHull(64, 48, 8);
        _missileHull = CreateHull(64, 50, 8);
        _harvesterHull = CreateHarvesterHull();

        // ---- 炮塔纹理（朝右绘制，旋转朝向目标） ----
        _lightTurret = CreateTankTurret(44, 28, 7, 4, 18);
        _heavyTurret = CreateTankTurret(52, 32, 9, 5, 22);
        _artyTurret = CreateArtyTurret();
        _rocketTurret = CreateRocketTurret();
        _missileTurret = CreateMissileTurret();

        // ---- 选中环 ----
        var ring = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        ring.Fill(new Color(0, 0, 0, 0));
        for (float a = 0; a < Mathf.Tau; a += 0.03f)
        {
            int cx = (int)(32 + 28 * Mathf.Cos(a));
            int cy = (int)(32 + 28 * Mathf.Sin(a));
            PlotPixel(ring, cx, cy, Colors.Lime);
            PlotPixel(ring, cx + 1, cy, Colors.Lime);
            PlotPixel(ring, cx, cy + 1, Colors.Lime);
        }
        _ringTex = ImageTexture.CreateFromImage(ring);
    }

    // ---- Q3 纹理生成辅助 ----

    /// <summary>创建底盘纹理：双履带 + 车身 + 发动机舱。</summary>
    private static ImageTexture CreateHull(int w, int h, int trackW, bool hasSkirt = false)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        var track = new Color(0.15f, 0.15f, 0.18f);
        var trackHi = new Color(0.22f, 0.22f, 0.25f);
        var hull = new Color(0.4f, 0.4f, 0.43f);
        var hullHi = new Color(0.5f, 0.5f, 0.53f);
        var engine = new Color(0.28f, 0.28f, 0.32f);
        var skirt = new Color(0.3f, 0.3f, 0.33f);

        // 左履带
        FillRect(img, 0, 2, trackW, h - 2, track);
        for (int y = 2; y < h - 2; y += 4)
            FillRect(img, 1, y, trackW - 1, y + 1, trackHi);

        // 右履带
        FillRect(img, w - trackW, 2, w, h - 2, track);
        for (int y = 2; y < h - 2; y += 4)
            FillRect(img, w - trackW + 1, y, w - 1, y + 1, trackHi);

        // 侧裙板（重型单位）
        if (hasSkirt)
        {
            FillRect(img, trackW, 2, trackW + 3, h - 2, skirt);
            FillRect(img, w - trackW - 3, 2, w - trackW, h - 2, skirt);
        }

        // 车身
        int iL = trackW + (hasSkirt ? 3 : 0);
        int iR = w - trackW - (hasSkirt ? 3 : 0);
        FillRect(img, iL, 4, iR, h - 4, hull);

        // 前部高光
        FillRect(img, iL + 2, 4, iR - 2, 9, hullHi);

        // 发动机舱（后部）
        FillRect(img, iL + 4, h - 8, iR - 4, h - 3, engine);

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>创建矿车底盘：履带+车身+矿石斗+铲臂。</summary>
    private static ImageTexture CreateHarvesterHull()
    {
        var img = Image.CreateEmpty(54, 44, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        var track = new Color(0.15f, 0.15f, 0.18f);
        var trackHi = new Color(0.22f, 0.22f, 0.25f);
        var body = new Color(0.4f, 0.4f, 0.43f);
        var bin = new Color(0.35f, 0.32f, 0.28f);
        var arm = new Color(0.3f, 0.3f, 0.33f);

        // 履带
        FillRect(img, 0, 2, 7, 42, track);
        for (int y = 2; y < 42; y += 4) FillRect(img, 1, y, 6, y + 1, trackHi);
        FillRect(img, 47, 2, 54, 42, track);
        for (int y = 2; y < 42; y += 4) FillRect(img, 48, y, 53, y + 1, trackHi);

        // 驾驶舱（前部）
        FillRect(img, 7, 6, 30, 20, body);

        // 矿石斗（后部加宽）
        FillRect(img, 5, 18, 49, 40, bin);
        FillRect(img, 3, 22, 6, 38, bin);  // 左侧加宽
        FillRect(img, 48, 22, 51, 38, bin); // 右侧加宽

        // 铲臂（前部）
        FillRect(img, 18, 3, 36, 9, arm);
        FillRect(img, 24, 1, 30, 5, arm);  // 铲头

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>创建坦克炮塔：圆形炮塔+炮管。</summary>
    private static ImageTexture CreateTankTurret(int w, int h, int turretR, int barrelW, int barrelLen)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        var turret = new Color(0.7f, 0.7f, 0.75f);
        var barrel = new Color(0.25f, 0.25f, 0.28f);
        var muzzle = new Color(0.2f, 0.2f, 0.23f);

        int cx = w / 2, cy = h / 2;

        // 炮塔圆盘
        FillCircle(img, cx, cy, turretR, turret);

        // 炮管（从圆心向右延伸）
        FillRect(img, cx, cy - barrelW / 2, cx + barrelLen, cy + barrelW / 2 + 1, barrel);

        // 炮口制退器
        int mzL = cx + barrelLen - 2;
        FillRect(img, mzL, cy - barrelW / 2 - 1, mzL + 5, cy + barrelW / 2 + 2, muzzle);

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>创建炮兵炮塔：护盾+超长细炮管。</summary>
    private static ImageTexture CreateArtyTurret()
    {
        var img = Image.CreateEmpty(56, 22, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        var shield = new Color(0.55f, 0.55f, 0.58f);
        var barrel = new Color(0.25f, 0.25f, 0.28f);
        var muzzle = new Color(0.2f, 0.2f, 0.23f);

        int cx = 28, cy = 11;

        // 炮盾（小型护板）
        FillRect(img, cx - 4, cy - 6, cx + 2, cy + 6, shield);

        // 超长细炮管
        FillRect(img, cx - 2, cy - 1, cx + 26, cy + 2, barrel);

        // 炮口
        FillRect(img, cx + 24, cy - 2, cx + 28, cy + 3, muzzle);

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>创建火箭炮炮塔：发射架+多管火箭。</summary>
    private static ImageTexture CreateRocketTurret()
    {
        var img = Image.CreateEmpty(44, 28, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        var rack = new Color(0.5f, 0.5f, 0.53f);
        var tube = new Color(0.25f, 0.25f, 0.28f);
        var tubeOpen = new Color(0.15f, 0.12f, 0.1f);

        int cx = 22, cy = 14;

        // 发射架底座
        FillRect(img, cx - 6, cy - 9, cx + 1, cy + 9, rack);

        // 火箭管（4管，朝右）
        for (int i = 0; i < 4; i++)
        {
            int ty = cy - 7 + i * 4;
            FillRect(img, cx, ty, cx + 14, ty + 3, tube);
            FillRect(img, cx + 12, ty, cx + 14, ty + 3, tubeOpen);
        }

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>创建导弹车炮塔：双联装导弹发射筒。</summary>
    private static ImageTexture CreateMissileTurret()
    {
        var img = Image.CreateEmpty(48, 30, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        var canister = new Color(0.45f, 0.45f, 0.48f);
        var guide = new Color(0.55f, 0.55f, 0.58f);
        var nose = new Color(0.3f, 0.3f, 0.33f);

        int cx = 24, cy = 15;

        // 上发射筒
        FillRect(img, cx - 6, cy - 12, cx + 12, cy - 2, canister);
        // 下发射筒
        FillRect(img, cx - 6, cy + 2, cx + 12, cy + 12, canister);

        // 导弹头部
        FillRect(img, cx + 10, cy - 12, cx + 14, cy - 2, nose);
        FillRect(img, cx + 10, cy + 2, cx + 14, cy + 12, nose);

        // 制导设备（中间）
        FillRect(img, cx - 4, cy - 3, cx + 4, cy + 3, guide);

        return ImageTexture.CreateFromImage(img);
    }

    private static void FillRect(Image img, int x0, int y0, int x1, int y1, Color c)
    {
        for (int y = y0; y < y1 && y < img.GetHeight(); y++)
            for (int x = x0; x < x1 && x < img.GetWidth(); x++)
                img.SetPixel(x, y, c);
    }

    private static void FillCircle(Image img, int cx, int cy, int radius, Color c)
    {
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                if (dx * dx + dy * dy <= radius * radius)
                    PlotPixel(img, cx + dx, cy + dy, c);
    }

    private static void PlotPixel(Image img, int x, int y, Color c)
    {
        if (x >= 0 && x < img.GetWidth() && y >= 0 && y < img.GetHeight())
            img.SetPixel(x, y, c);
    }

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

        // Q3：按兵种设置底盘纹理
        _body.Texture = Type switch
        {
            UnitType.LightTank => _lightHull,
            UnitType.HeavyTank => _heavyHull,
            UnitType.Artillery => _artyHull,
            UnitType.RocketLauncher => _rocketHull,
            UnitType.MissileTank => _missileHull,
            _ => _harvesterHull  // Harvester (Default)
        };
        _body.Modulate = Colors.White; // 底盘保持灰调原色
        _selectionRing.Texture = _ringTex;

        _selectionRing.Visible = false;
        _healthBar.MaxValue = MaxHealth;
        _healthBar.Value = Health;
        UpdateHealthBarVisibility();

        // Q3：炮塔精灵（战斗单位专用，矿车不需要）
        if (this is not Harvester)
        {
            _turret = new Sprite2D { Name = "Turret", ZIndex = 1 };
            AddChild(_turret);
            _turret.Texture = Type switch
            {
                UnitType.LightTank => _lightTurret,
                UnitType.HeavyTank => _heavyTurret,
                UnitType.Artillery => _artyTurret,
                UnitType.RocketLauncher => _rocketTurret,
                UnitType.MissileTank => _missileTurret,
                _ => null
            };
            var teamColor = TeamId == 0
                ? new Color(0.3f, 0.6f, 1.0f)
                : new Color(1.0f, 0.3f, 0.3f);
            _turret.Modulate = teamColor;
        }
        else
        {
            // 矿车用黄/橙色调
            _body.Modulate = TeamId == 0
                ? new Color(0.9f, 0.8f, 0.2f)
                : new Color(0.9f, 0.5f, 0.2f);
        }
    }

    public sealed override void _Process(double delta)
    {
        if (_isDead) return;
        var dt = (float)delta;

        // 调度子类自定义 AI（默认是玩家命令 + 攻击逻辑）
        ProcessAI(dt);

        // 如果子类 AI 没有清理攻击目标，让基类处理追击/开火
        ResolveCombat(dt);

        // 通用移动
        ProcessMovement(dt);

        // Q3：炮塔朝向目标平滑旋转
        UpdateTurretRotation(dt);
    }

    /// <summary>Q3：炮塔朝向攻击目标平滑旋转，无目标时跟随车体方向。</summary>
    private void UpdateTurretRotation(float dt)
    {
        if (_turret == null) return;

        float targetAngle = _body.Rotation; // 默认跟随车体
        bool hasTarget = false;

        if (_attackUnitTarget != null && IsInstanceValid(_attackUnitTarget))
        {
            targetAngle = (_attackUnitTarget.GlobalPosition - GlobalPosition).Angle();
            hasTarget = true;
        }
        else if (_attackBuildingTarget != null && IsInstanceValid(_attackBuildingTarget))
        {
            targetAngle = (_attackBuildingTarget.GlobalPosition - GlobalPosition).Angle();
            hasTarget = true;
        }
        else if (_hasMoveTarget)
        {
            var dir = _moveTarget - GlobalPosition;
            if (dir.Length() > 5f)
            {
                targetAngle = dir.Angle();
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
                    _body.Rotation = direction.Angle();
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
        QueueFree();
    }
}
