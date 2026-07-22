using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 兵种类型枚举。
/// </summary>
public enum UnitType { LightTank, HeavyTank, Artillery, RocketLauncher, MissileTank, AntiAir, Infantry, Engineer, Sapper, ChiefEngineer, Grenadier, Sniper, FlameInfantry, Transport, Hero, Spy, Thief, Default }

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

    /// <summary>运输车内搭载的乘客（步兵类单位）。</summary>
    public List<Unit> Passengers { get; } = new();
    /// <summary>运输车最大搭载人数。</summary>
    public int MaxPassengers { get; set; } = 3;
    /// <summary>是否是运输车（可搭载步兵）。</summary>
    public bool IsTransport => Type == UnitType.Transport;
    /// <summary>合体后的功能类型（空=未合体，或合体后立即变Type）。</summary>
    private UnitType _preMergeType = UnitType.Default;

    // E6：搭载交互
    /// <summary>步兵上车目标（移动到运输车附近后执行上车）。</summary>
    public Unit? _embarkTarget;

    // ======== E6b：特殊单位系统 ========
    /// <summary>英雄技能类型。</summary>
    public enum HeroSkill { None, DoubleShot, HealAura, Dash, CriticalStrike, Shield }
    public HeroSkill _heroSkill = HeroSkill.None;
    /// <summary>间谍伪装的阵营ID（-1=未伪装）。</summary>
    public int _spyDisguiseTeam = -1;
    /// <summary>间谍渗透计时器。</summary>
    private float _spyInfiltrateTimer;
    /// <summary>窃贼偷钱冷却。</summary>
    private float _thiefStealCooldown;

    // ======== E4：地形改造系统 ========
    public enum TerrainModType { None, Flatten, Tunnel, Bridge, UnderseaTunnel }
    private TerrainModType _terrainModType = TerrainModType.None;
    private Vector2 _terrainModTarget;  // 改造目标世界坐标
    private float _terrainModTimer;     // 施工倒计时
    private float _terrainModDuration;  // 施工总时长
    private int _terrainModCost;        // 施工费用
    private bool _isConstructing;       // 正在施工中
    /// <summary>是否是工程单位（工兵/高级工程师/工程车，或合体后的工兵战车/高级工兵战车）。</summary>
    public bool IsEngineerUnit => Type == UnitType.Sapper || Type == UnitType.ChiefEngineer || Type == UnitType.Engineer
        || (IsTransport && Passengers.Count > 0 && Passengers[0] is { } p && (p.Type == UnitType.Sapper || p.Type == UnitType.ChiefEngineer));

    /// <summary>AI保护期剩余时间（秒）。>0时AI单位不主动搜敌进攻，给玩家发展空间。由Main每帧递减。</summary>
    public static float AiGraceRemaining = 0f;

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
        UnitType.Sapper => _infantryHull!,     // 工兵复用步兵底盘
        UnitType.ChiefEngineer => _infantryHull!, // 高级工程师复用步兵底盘
        UnitType.Grenadier => _infantryHull!,     // E6：掷弹兵复用步兵底盘
        UnitType.Sniper => _infantryHull!,          // E6：狙击手复用步兵底盘
        UnitType.FlameInfantry => _infantryHull!,  // E6：喷火兵复用步兵底盘
        UnitType.Transport => _hullLight!,          // E6：运输车复用轻坦底盘
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
            case UnitType.Sapper:
                UnitName = "工兵";
                MaxHealth = 40f;
                MoveSpeed = 95f;
                AttackDamage = 3f;
                AttackRange = 60f;
                AttackCooldown = 0.8f;
                AggroRange = 100f;
                break;
            case UnitType.ChiefEngineer:
                UnitName = "高级工程师";
                MaxHealth = 60f;
                MoveSpeed = 100f;
                AttackDamage = 5f;
                AttackRange = 80f;
                AttackCooldown = 0.7f;
                AggroRange = 120f;
                break;
            // E6：新步兵系兵种
            case UnitType.Grenadier:
                UnitName = "掷弹兵";
                MaxHealth = 40f;
                MoveSpeed = 85f;
                AttackDamage = 20f;
                AttackRange = 180f;
                AttackCooldown = 2.0f;
                MinAttackRange = 50f;
                SplashRadius = 60f;  // 掷弹兵AOE
                AggroRange = 220f;
                break;
            case UnitType.Sniper:
                UnitName = "狙击手";
                MaxHealth = 30f;
                MoveSpeed = 80f;
                AttackDamage = 45f;
                AttackRange = 350f;
                AttackCooldown = 2.5f;
                MinAttackRange = 80f;
                AggroRange = 380f;
                break;
            case UnitType.FlameInfantry:
                UnitName = "喷火兵";
                MaxHealth = 50f;
                MoveSpeed = 85f;
                AttackDamage = 8f;
                AttackRange = 80f;
                AttackCooldown = 0.3f;  // 高射速近战
                SplashRadius = 40f;    // 短距AOE
                AggroRange = 120f;
                break;
            // E6：运输车
            case UnitType.Transport:
                UnitName = "运输车";
                MaxHealth = 150f;
                MoveSpeed = 200f;
                AttackDamage = 0f;    // 基础无攻击，合体后有
                AttackRange = 0f;
                AttackCooldown = 0f;
                AggroRange = 0f;
                MaxPassengers = 3;
                break;
            // E6b：特殊单位
            case UnitType.Hero:
                UnitName = "英雄";
                MaxHealth = 200f;
                MoveSpeed = 160f;
                AttackDamage = 35f;
                AttackRange = 160f;
                AttackCooldown = 0.6f;
                AggroRange = 300f;
                AutoDefend = true;
                // E6b：随机技能
                _heroSkill = (HeroSkill)(GD.Randi() % 5 + 1);
                switch (_heroSkill)
                {
                    case HeroSkill.DoubleShot: UnitName = "英雄·双发"; AttackCooldown = 0.35f; break;
                    case HeroSkill.HealAura: UnitName = "英雄·治疗光环"; break;
                    case HeroSkill.Dash: UnitName = "英雄·冲锋"; MoveSpeed = 260f; break;
                    case HeroSkill.CriticalStrike: UnitName = "英雄·暴击"; break;
                    case HeroSkill.Shield: UnitName = "英雄·护盾"; MaxHealth = 300f; break;
                }
                Health = MaxHealth;
                GD.Print($"[E6b] 英雄技能：{_heroSkill}");
                break;
            case UnitType.Spy:
                UnitName = "间谍";
                MaxHealth = 45f;
                MoveSpeed = 110f;
                AttackDamage = 0f;
                AttackRange = 0f;
                AttackCooldown = 0f;
                AggroRange = 0f;
                break;
            case UnitType.Thief:
                UnitName = "窃贼";
                MaxHealth = 40f;
                MoveSpeed = 130f;
                AttackDamage = 5f;
                AttackRange = 60f;
                AttackCooldown = 0.8f;
                AggroRange = 100f;
                break;
        }
    }

    /// <summary>判断兵种是否为步兵类（步体、工兵、高级工程师、掷弹兵、狙击手、喷火兵、英雄、间谍、窃贼）。</summary>
    public static bool IsInfantryType(UnitType type) => type switch
    {
        UnitType.Infantry => true,
        UnitType.Sapper => true,
        UnitType.ChiefEngineer => true,
        UnitType.Grenadier => true,
        UnitType.Sniper => true,
        UnitType.FlameInfantry => true,
        UnitType.Hero => true,
        UnitType.Spy => true,
        UnitType.Thief => true,
        _ => false,
    };

    // ======== E6：运输车搭载系统 ========

    /// <summary>步兵进入运输车。执行IFV式合体逻辑。</summary>
    public void EmbarkPassenger(Unit passenger)
    {
        if (!IsTransport) return;
        if (Passengers.Count >= MaxPassengers) return;
        if (passenger == this || !IsInstanceValid(passenger)) return;

        // 将步兵从场景树移除（视觉隐藏），记录在运输车内部
        Passengers.Add(passenger);
        passenger.GetParent().RemoveChild(passenger);
        passenger.Visible = false;
        passenger.SetSelected(false);

        GD.Print($"[IFV] {passenger.UnitName} 进入 {UnitName} (搭载 {Passengers.Count}/{MaxPassengers})");

        // 首个乘客决定合体功能
        if (Passengers.Count == 1)
        {
            ApplyMergeEffect(passenger.Type);
        }
    }

    /// <summary>所有乘客下车。</summary>
    public void DisembarkAll()
    {
        if (!IsTransport || Passengers.Count == 0) return;

        var main = GetParent()?.GetParent() as Node2D;
        if (main == null) return;

        foreach (var p in Passengers)
        {
            if (!IsInstanceValid(p)) continue;
            // 在运输车附近下车
            var exitPos = GlobalPosition + new Vector2(
                (float)(GD.RandRange(-40, 40)),
                (float)(GD.RandRange(-40, 40)));
            p.Visible = true;
            p.GlobalPosition = exitPos;
            main.GetNode<Node2D>("Units").AddChild(p);
        }

        GD.Print($"[IFV] {Passengers.Count} 名乘客从 {UnitName} 下车");
        Passengers.Clear();

        // 恢复运输车基础属性
        RevertToBaseTransport();
    }

    /// <summary>IFV合体效果：首个乘客类型决定运输车的战斗功能。</summary>
    private void ApplyMergeEffect(UnitType passengerType)
    {
        _preMergeType = Type; // 保存原始类型

        // 保存基础运输车属性用于恢复
        string oldName = UnitName;

        switch (passengerType)
        {
            case UnitType.Sapper:
                // 工兵→工程车：维修+改造
                UnitName = "工兵战车";
                AttackDamage = 0f;
                AttackRange = 0f;
                MaxHealth = 200f;
                Health = 200f;
                GD.Print("[IFV] 合体：工兵战车（维修+地形改造）");
                break;
            case UnitType.ChiefEngineer:
                // 高级工程师→高级工程车：高效改造
                UnitName = "高级工兵战车";
                AttackDamage = 0f;
                AttackRange = 0f;
                MaxHealth = 250f;
                Health = 250f;
                GD.Print("[IFV] 合体：高级工兵战车（高级改造）");
                break;
            case UnitType.Infantry:
                // 步兵→武装吉普：轻机枪火力
                UnitName = "武装吉普";
                AttackDamage = 12f;
                AttackRange = 150f;
                AttackCooldown = 0.5f;
                MaxHealth = 160f;
                Health = 160f;
                AggroRange = 250f;
                AutoDefend = true;
                GD.Print("[IFV] 合体：武装吉普（轻机枪）");
                break;
            case UnitType.Grenadier:
                // 掷弹兵→自走炮：AOE火力
                UnitName = "自走炮";
                AttackDamage = 25f;
                AttackRange = 220f;
                AttackCooldown = 1.5f;
                SplashRadius = 70f;
                MaxHealth = 180f;
                Health = 180f;
                AggroRange = 280f;
                AutoDefend = true;
                GD.Print("[IFV] 合体：自走炮（AOE火力）");
                break;
            case UnitType.Sniper:
                // 狙击手→狙击战车：远程精确火力
                UnitName = "狙击战车";
                AttackDamage = 50f;
                AttackRange = 380f;
                AttackCooldown = 2.0f;
                MinAttackRange = 100f;
                MaxHealth = 150f;
                Health = 150f;
                AggroRange = 400f;
                AutoDefend = true;
                GD.Print("[IFV] 合体：狙击战车（远程精确）");
                break;
            case UnitType.FlameInfantry:
                // 喷火兵→喷火战车：近距高DPS
                UnitName = "喷火战车";
                AttackDamage = 15f;
                AttackRange = 100f;
                AttackCooldown = 0.25f;
                SplashRadius = 50f;
                MaxHealth = 200f;
                Health = 200f;
                AggroRange = 150f;
                AutoDefend = true;
                GD.Print("[IFV] 合体：喷火战车（近距高DPS）");
                break;
            // E6b：特殊单位IFV合体
            case UnitType.Hero:
                // 英雄→英雄战车：超强火力
                UnitName = "英雄战车";
                AttackDamage = 40f;
                AttackRange = 200f;
                AttackCooldown = 0.5f;
                MaxHealth = 250f;
                Health = 250f;
                AggroRange = 300f;
                AutoDefend = true;
                GD.Print("[IFV] 合体：英雄战车（超强火力）");
                break;
            case UnitType.Spy:
                // 间谍→间谍车：渗透战车
                UnitName = "间谍车";
                AttackDamage = 0f;
                AttackRange = 0f;
                MaxHealth = 180f;
                Health = 180f;
                MoveSpeed = 280f;
                GD.Print("[IFV] 合体：间谍车（高速渗透）");
                break;
            case UnitType.Thief:
                // 窃贼→劫掠车：偷钱战车
                UnitName = "劫掠车";
                AttackDamage = 8f;
                AttackRange = 120f;
                AttackCooldown = 0.6f;
                MaxHealth = 160f;
                Health = 160f;
                AggroRange = 180f;
                AutoDefend = true;
                GD.Print("[IFV] 合体：劫掠车（偷钱战车）");
                break;
            default:
                // 其他步兵→轻型武装车
                UnitName = "轻型武装车";
                AttackDamage = 10f;
                AttackRange = 140f;
                AttackCooldown = 0.7f;
                MaxHealth = 170f;
                Health = 170f;
                AggroRange = 220f;
                AutoDefend = true;
                GD.Print("[IFV] 合体：轻型武装车");
                break;
        }

        // 合体后更新视觉
        if (GetNodeOrNull<Sprite2D>("Turret") == null && AttackDamage > 0f)
        {
            // 添加炮塔（合体后变为战斗载具）
            _turret = new Sprite2D { Name = "Turret", ZIndex = 1, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
            AddChild(_turret);
            _turret.Texture = _turretLight; // 复用轻坦炮塔
            _turret.Modulate = GetTeamColor(TeamId);
            _turretTint = GetTeamColor(TeamId);
        }
    }

    /// <summary>恢复运输车基础属性（所有乘客下车后）。</summary>
    private void RevertToBaseTransport()
    {
        UnitName = "运输车";
        MaxHealth = 150f;
        AttackDamage = 0f;
        AttackRange = 0f;
        AttackCooldown = 0f;
        MinAttackRange = 0f;
        SplashRadius = 0f;
        AggroRange = 0f;
        AutoDefend = false;
        _preMergeType = UnitType.Default;

        // 移除炮塔
        if (_turret != null)
        {
            RemoveChild(_turret);
            _turret.QueueFree();
            _turret = null;
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
        _body.Scale = IsInfantryType(Type) ? new Vector2(0.9f, 0.9f) : Vector2.One;
        // 像素艺术必须用 Nearest 过滤，Linear 会把 14-17 色的底盘细节插值平滑成单色块
        _body.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        _selectionRing.Texture = _ringTex;
        _selectionRing.Visible = false;
        _healthBar.MaxValue = MaxHealth;
        _healthBar.Value = Health;
        UpdateHealthBarVisibility();

        // 炮塔精灵（战斗单位专用，矿车、步兵和工程车不需要）
        if (this is not Harvester && !IsInfantryType(Type) && Type != UnitType.Engineer)
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

        // E3：地形高度视觉偏移——高海拔单位的body向上偏移，模拟"站在高处"
        if (GetParent()?.GetParent() is Main mainNode)
        {
            var terrain = mainNode.GetTerrainGrid();
            terrain.WorldToGrid(GlobalPosition.X, GlobalPosition.Y, out int gx, out int gy);
            var cell = terrain.GetCell(gx, gy);
            float yOffset = cell.Elevation switch { 2 => -3f, 3 => -6f, _ => 0f };
            _body.Position = new Vector2(_body.Position.X, yOffset + (Type == UnitType.Infantry ? 0f : 0f));
            if (_turret != null) _turret.Position = new Vector2(_turret.Position.X, yOffset);
        }

        // 调度子类自定义 AI（默认是玩家命令 + 攻击逻辑）
        ProcessAI(dt);

        // 如果子类 AI 没有清理攻击目标，让基类处理追击/开火
        ResolveCombat(dt);

        // 通用移动
        ProcessMovement(dt);

        // E6：搭载交互——步兵到达运输车附近后执行上车
        if (_embarkTarget != null && IsInstanceValid(_embarkTarget))
        {
            if (GlobalPosition.DistanceTo(_embarkTarget.GlobalPosition) < 50f)
            {
                _embarkTarget.EmbarkPassenger(this);
                _embarkTarget = null;
            }
        }
        else if (_embarkTarget != null)
        {
            _embarkTarget = null; // 目标失效
        }

        // E4：地形改造施工进度
        ProcessTerrainModification(dt);

        // Q3：炮塔朝向目标平滑旋转
        UpdateTurretRotation(dt);

        // 工程车/合体工程车辅助：每帧治疗附近的友方建筑/单位
        if (IsEngineerUnit) TryRepairNearby(dt);

        // E6b：英雄技能逻辑
        ProcessHeroSkill(dt);

        // E6b：间谍渗透逻辑
        ProcessSpyInfiltrate(dt);

        // E6b：窃贼偷钱逻辑
        ProcessThiefSteal(dt);
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

    // ======== E6b：英雄技能 ========
    private float _heroSkillTimer;
    private void ProcessHeroSkill(float dt)
    {
        if (Type != UnitType.Hero || _heroSkill == HeroSkill.None) return;
        _heroSkillTimer += dt;

        switch (_heroSkill)
        {
            case HeroSkill.HealAura:
                // 治疗光环：每3秒治疗120范围内友方单位20HP
                if (_heroSkillTimer >= 3f)
                {
                    _heroSkillTimer = 0f;
                    var unitsNode = GetParent();
                    if (unitsNode == null) break;
                    foreach (var c in unitsNode.GetChildren())
                    {
                        if (c is Unit u && u != this && IsInstanceValid(u) && u.TeamId == TeamId
                            && u.GlobalPosition.DistanceTo(GlobalPosition) < 120f && u.Health < u.MaxHealth)
                        {
                            u.Heal(20f);
                        }
                    }
                }
                break;
            case HeroSkill.Dash:
                // 冲锋：移速已在InitAsType中提升，这里给攻击加成
                // 冲锋已在属性中体现(高移速)
                break;
            case HeroSkill.CriticalStrike:
                // 暴击：30%概率双倍伤害（在ResolveCombat中处理）
                break;
            case HeroSkill.Shield:
                // 护盾：每10秒获得50临时护盾（用heal模拟）
                if (_heroSkillTimer >= 10f)
                {
                    _heroSkillTimer = 0f;
                    if (Health < MaxHealth)
                        Heal(50f);
                }
                break;
        }
    }

    // ======== E6b：间谍渗透 ========
    private void ProcessSpyInfiltrate(float dt)
    {
        if (Type != UnitType.Spy) return;

        // 间谍伪装：接近敌方建筑时自动伪装成敌方阵营颜色
        var main = GetParent()?.GetParent();
        if (main == null) return;
        var bnode = main.GetNodeOrNull<Node>("Buildings");
        if (bnode == null) return;

        bool nearEnemy = false;
        foreach (var c in bnode.GetChildren())
        {
            if (c is Building b && IsInstanceValid(b) && b.TeamId != TeamId
                && b.GlobalPosition.DistanceTo(GlobalPosition) < 60f)
            {
                nearEnemy = true;

                // 渗透倒计时
                _spyInfiltrateTimer += dt;
                if (_spyInfiltrateTimer >= 4f)
                {
                    _spyInfiltrateTimer = 0f;
                    // 渗透效果：让敌方建筑停电5秒 + 偷取科技
                    GD.Print($"[E6b] 间谍成功渗透 {b.BuildingName} (Team {b.TeamId})！");
                    b.PowerConsumed += 100; // 紧急停电
                    // 5秒后恢复
                    DelayedRestorePower(b, 5f);
                    // 偷取$200
                    if (main is Main mainNode)
                    {
                        int stolen = Mathf.Min(200, mainNode.GetMoney(b.TeamId));
                        mainNode.SpendMoney(b.TeamId, stolen);
                        mainNode.AddResourceForTeam(TeamId, stolen);
                        GD.Print($"[E6b] 间谍偷取 ${stolen} (Team {b.TeamId} → Team {TeamId})");
                    }
                }
                break; // 只渗透最近的一个建筑
            }
        }

        // 伪装逻辑：靠近敌方建筑时外观变色
        if (nearEnemy && _spyDisguiseTeam == -1)
        {
            _spyDisguiseTeam = 1; // 伪装为敌方颜色
            _body.Modulate = GetTeamColor(_spyDisguiseTeam);
        }
        else if (!nearEnemy && _spyDisguiseTeam != -1)
        {
            _spyDisguiseTeam = -1;
            _body.Modulate = _bodyTint; // 恢复原色
        }
    }

    private async void DelayedRestorePower(Building b, float delay)
    {
        await ToSignal(GetTree().CreateTimer(delay), "timeout");
        if (IsInstanceValid(b))
        {
            b.PowerConsumed -= 100;
            if (b.PowerConsumed < 0) b.PowerConsumed = 0;
        }
    }

    // ======== E6b：窃贼偷钱 ========
    private void ProcessThiefSteal(float dt)
    {
        if (Type != UnitType.Thief) return;
        _thiefStealCooldown -= dt;
        if (_thiefStealCooldown > 0f) return;

        var main = GetParent()?.GetParent() as Main;
        if (main == null) return;

        // 偷钱范围：接近敌方基地或资源单位
        var bnode = main.GetNodeOrNull<Node>("Buildings");
        if (bnode != null)
        {
            foreach (var c in bnode.GetChildren())
            {
                if (c is Building b && IsInstanceValid(b) && b.TeamId != TeamId
                    && b.GlobalPosition.DistanceTo(GlobalPosition) < 60f)
                {
                    int stolen = Mathf.Min(100, main.GetMoney(b.TeamId));
                    if (stolen > 0)
                    {
                        main.SpendMoney(b.TeamId, stolen);
                        main.AddResourceForTeam(TeamId, stolen);
                        _thiefStealCooldown = 8f; // 8秒冷却
                        GD.Print($"[E6b] 窃贼偷取 ${stolen} (Team {b.TeamId} → Team {TeamId})");
                    }
                    return;
                }
            }
        }

        // 偷敌方矿车
        var unitsNode = GetParent();
        if (unitsNode != null)
        {
            foreach (var c in unitsNode.GetChildren())
            {
                if (c is Unit u && u != this && IsInstanceValid(u) && u.TeamId != TeamId
                    && u is Harvester && u.GlobalPosition.DistanceTo(GlobalPosition) < 60f)
                {
                    int stolen = Mathf.Min(150, main.GetMoney(u.TeamId));
                    if (stolen > 0)
                    {
                        main.SpendMoney(u.TeamId, stolen);
                        main.AddResourceForTeam(TeamId, stolen);
                        _thiefStealCooldown = 8f;
                        GD.Print($"[E6b] 窃贼偷取矿车 ${stolen} (Team {u.TeamId} → Team {TeamId})");
                    }
                    return;
                }
            }
        }
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

            // AI保护期：给玩家前期发展空间，保护期内只防守不主动进攻
            if (AiGraceRemaining > 0f)
            {
                // 保护期内：只反击身边近距离敌人，不主动全图搜敌
                var nearbyEnemy = FindNearestEnemyUnitInRange(AggroRange);
                if (nearbyEnemy != null)
                {
                    _attackUnitTarget = nearbyEnemy;
                    _attackBuildingTarget = null;
                }
                return;
            }

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
                        float dmg = AttackDamage;
                        // E6b：英雄暴击30%概率双倍伤害
                        if (Type == UnitType.Hero && _heroSkill == HeroSkill.CriticalStrike && GD.Randf() < 0.3f)
                            dmg *= 2f;
                        _attackUnitTarget.TakeDamage(dmg);
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
                        float dmgB = AttackDamage;
                        // E6b：英雄暴击30%概率双倍伤害
                        if (Type == UnitType.Hero && _heroSkill == HeroSkill.CriticalStrike && GD.Randf() < 0.3f)
                            dmgB *= 2f;
                        _attackBuildingTarget.TakeDamage(dmgB);
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

                // E2：地形速度修正——查询当前所在地形获取速度系数
                float speedMult = 1.0f;
                if (GetParent()?.GetParent() is Main mainNode)
                {
                    var terrain = mainNode.GetTerrainGrid();
                    var cat = GetTerrainCategory();
                    speedMult = terrain.GetMovementSpeedAtWorld(cat, GlobalPosition.X, GlobalPosition.Y);
                    // 速度修正为0=不可通行，停下
                    if (speedMult <= 0f)
                    {
                        Velocity = Vector2.Zero;
                        return;
                    }
                }

                Velocity = direction * MoveSpeed * speedMult;
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

    /// <summary>获取当前单位的地形类别（用于速度修正查询）。</summary>
    public virtual TerrainUnitCategory GetTerrainCategory() => Type switch
    {
        UnitType.Infantry => TerrainUnitCategory.Infantry,
        UnitType.Sapper => TerrainUnitCategory.Engineer,
        UnitType.ChiefEngineer => TerrainUnitCategory.Engineer,
        UnitType.Grenadier => TerrainUnitCategory.Infantry,
        UnitType.Sniper => TerrainUnitCategory.Infantry,
        UnitType.FlameInfantry => TerrainUnitCategory.Infantry,
        UnitType.Transport => TerrainUnitCategory.LightVehicle,
        UnitType.Engineer => TerrainUnitCategory.EngineerVehicle,
        UnitType.LightTank => TerrainUnitCategory.LightVehicle,
        UnitType.AntiAir => TerrainUnitCategory.LightVehicle,
        UnitType.HeavyTank => TerrainUnitCategory.HeavyVehicle,
        UnitType.Artillery => TerrainUnitCategory.HeavyVehicle,
        UnitType.RocketLauncher => TerrainUnitCategory.HeavyVehicle,
        UnitType.MissileTank => TerrainUnitCategory.HeavyVehicle,
        _ => TerrainUnitCategory.HeavyVehicle,
    };

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

    /// <summary>阶段12-A2 阶段12-A2 维修厂自动修复：增加一定血量，但不超过 MaxHealth。</summary>
    public void RepairByRepairPad(float amount)
    {
        if (amount <= 0f || Health >= MaxHealth || _isDead) return;
        Health = Mathf.Min(MaxHealth, Health + amount);
        if (_healthBar != null)
        {
            _healthBar.Value = Health;
            UpdateHealthBarVisibility();
        }
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

        // E6：运输车被摧毁时，乘客全部阵亡
        if (IsTransport && Passengers.Count > 0)
        {
            foreach (var p in Passengers)
            {
                if (IsInstanceValid(p))
                    p.QueueFree();
            }
            Passengers.Clear();
        }

        // Q5：死亡爆炸特效，步兵用小爆炸，重坦用大爆炸，其他默认
        var main = GetParent()?.GetParent() as Node2D;
        if (main != null)
        {
            var effect = Type switch
            {
                UnitType.HeavyTank => BattleEffect.BigExplosion(GlobalPosition),
                UnitType.Infantry or UnitType.Sapper or UnitType.ChiefEngineer
                    or UnitType.Grenadier or UnitType.Sniper or UnitType.FlameInfantry
                    or UnitType.Hero or UnitType.Spy or UnitType.Thief  // E6b
                    => BattleEffect.Explosion(GlobalPosition),
                _ => BattleEffect.Explosion(GlobalPosition)
            };
            main.AddChild(effect);
        }

        // 阶段12-C：单位死亡音效
        if (GetParent()?.GetParent() is Main mainNode)
            mainNode.PlayUnitDeathSfx(Type);

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

        // 阶段12-C：开火音效（街机风格短促cannon + muzzle）
        if (main is Main m)
        {
            m.PlayUnitFireSfx(Type);
        }
    }

    public override void _Draw()
    {
        // 脚下椭圆阴影（始终水平：CharacterBody2D 节点本身不旋转，仅 _body sprite 旋转）
        // 通过 DrawSetTransform 把椭圆中心偏移到单位脚下偏右下，模拟光源在左上方
        var pts = IsInfantryType(Type) ? _shadowPtsSmall : _shadowPtsLarge;
        float yOff = IsInfantryType(Type) ? 8f : 18f;
        DrawSetTransform(new Vector2(3f, yOff), 0f, Vector2.One);
        DrawPolygon(pts, new Color[] { _shadowColor });
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    // ======== E4：地形改造系统 ========

    /// <summary>下达地形改造指令。工程单位移动到目标位置后开始施工。</summary>
    public void CommandTerrainMod(TerrainModType modType, Vector2 targetWorldPos)
    {
        if (!IsEngineerUnit) return;
        _terrainModType = modType;
        _terrainModTarget = targetWorldPos;
        _isConstructing = false;
        _terrainModTimer = 0f;
        // 移动到目标
        MoveTo(targetWorldPos);
    }

    /// <summary>每帧检查施工进度。由 _Process 调用。</summary>
    private void ProcessTerrainModification(float dt)
    {
        if (_terrainModType == TerrainModType.None) return;

        // 还在移动中，先到达目标
        if (_hasMoveTarget && !_isConstructing) return;

        // 到达目标后开始施工
        if (!_isConstructing)
        {
            // 检查是否靠近目标
            float dist = GlobalPosition.DistanceTo(_terrainModTarget);
            if (dist > TerrainGrid.TileSize * 1.5f)
            {
                // 太远，取消改造
                _terrainModType = TerrainModType.None;
                return;
            }
            _isConstructing = true;

            // 计算费用和时长（基于单位类型和改造类型）
            if (!CalculateTerrainModCost(out _terrainModCost, out _terrainModDuration))
            {
                // 不支持的改造类型，取消
                _terrainModType = TerrainModType.None;
                _isConstructing = false;
                return;
            }

            // 扣费检查
            if (GetParent()?.GetParent() is Main mainNode)
            {
                if (!mainNode.SpendMoney(TeamId, _terrainModCost))
                {
                    // 资金不足
                    GD.Print($"[TerrainMod] {UnitName} (Team {TeamId}) 资金不足 $_terrainModCost，无法施工");
                    _terrainModType = TerrainModType.None;
                    _isConstructing = false;
                    return;
                }
            }

            GD.Print($"[TerrainMod] {UnitName} (Team {TeamId}) 开始{_terrainModType}施工，费用${_terrainModCost}，耗时{_terrainModDuration:F1}s");
        }

        // 施工倒计时
        _terrainModTimer += dt;
        if (_terrainModTimer >= _terrainModDuration)
        {
            // 施工完成，执行地形修改
            ExecuteTerrainMod();
            _terrainModType = TerrainModType.None;
            _isConstructing = false;
        }
    }

    /// <summary>计算当前改造操作的费用和时长。</summary>
    private bool CalculateTerrainModCost(out int cost, out float duration)
    {
        cost = 0;
        duration = 0f;
        if (GetParent()?.GetParent() is not Main mainNode) return false;
        var terrain = mainNode.GetTerrainGrid();
        terrain.WorldToGrid(_terrainModTarget.X, _terrainModTarget.Y, out int gx, out int gy);
        var cell = terrain.GetCell(gx, gy);

        // 编队协同：计算同一目标同时施工的工程单位数
        int workers = CountWorkersAtTarget();
        float efficiencyMult = GetTeamEfficiency(workers);
        float costReduction = GetTeamCostReduction(workers);

        (int baseCost, float baseDuration) = (_terrainModType, cell.Type) switch
        {
            (TerrainModType.Flatten, TerrainType.Mountain) => Type switch
            {
                UnitType.Sapper => (500, 12f),
                UnitType.ChiefEngineer => (300, 8f),
                UnitType.Engineer => (200, 5f),
                _ => (500, 12f),
            },
            (TerrainModType.Tunnel, TerrainType.Mountain) => Type switch
            {
                UnitType.Sapper => (800, 15f),
                UnitType.ChiefEngineer => (500, 10f),
                UnitType.Engineer => (300, 6f),
                _ => (800, 15f),
            },
            (TerrainModType.Bridge, TerrainType.ShallowWater) => Type switch
            {
                UnitType.Sapper => (300, 8f),
                UnitType.ChiefEngineer => (200, 5f),
                UnitType.Engineer => (150, 3f),
                _ => (300, 8f),
            },
            (TerrainModType.Bridge, TerrainType.DeepWater) => Type switch
            {
                UnitType.Sapper => (500, 10f),   // 河流
                UnitType.ChiefEngineer => (300, 7f),
                UnitType.Engineer => (200, 5f),
                _ => (500, 10f),
            },
            (TerrainModType.UnderseaTunnel, TerrainType.DeepWater) => Type switch
            {
                UnitType.Sapper => (1500, 30f),
                UnitType.ChiefEngineer => (1000, 20f),
                UnitType.Engineer => (800, 15f),
                _ => (1500, 30f),
            },
            _ => (0, 0f),
        };

        if (baseCost == 0) return false;

        cost = (int)(baseCost * (1.0f - costReduction));
        duration = baseDuration / efficiencyMult;
        return true;
    }

    /// <summary>编队协同：计算同一目标同时施工的工程单位数。</summary>
    private int CountWorkersAtTarget()
    {
        int count = 0;
        var unitsNode = GetParent();
        if (unitsNode == null) return 1;
        foreach (var child in unitsNode.GetChildren())
        {
            if (child is Unit u && u != this && u.TeamId == TeamId && u.IsEngineerUnit && u._isConstructing)
            {
                if (u._terrainModTarget.DistanceTo(_terrainModTarget) < TerrainGrid.TileSize)
                    count++;
            }
        }
        return count + 1; // 包括自己
    }

    /// <summary>编队协同效率倍率（有衰减）。</summary>
    private static float GetTeamEfficiency(int workers) => workers switch
    {
        1 => 1.0f, 2 => 1.7f, 3 => 2.3f, 4 => 2.8f, 5 => 3.2f,
        _ => 3.5f, // 上限6人
    };

    /// <summary>编队协同费用节省比例。</summary>
    private static float GetTeamCostReduction(int workers) => workers switch
    {
        1 => 0f, 2 => 0.10f, 3 => 0.15f, 4 => 0.20f, 5 => 0.22f,
        _ => 0.25f,
    };

    /// <summary>执行地形修改（施工完成后调用）。</summary>
    private void ExecuteTerrainMod()
    {
        if (GetParent()?.GetParent() is not Main mainNode) return;
        var terrain = mainNode.GetTerrainGrid();
        terrain.WorldToGrid(_terrainModTarget.X, _terrainModTarget.Y, out int gx, out int gy);
        var cell = terrain.GetCell(gx, gy);

        switch (_terrainModType)
        {
            case TerrainModType.Flatten:
                // 削平山脉：Mountain → Grass，Elevation 3 → 1
                cell.Type = TerrainType.Grass;
                cell.Elevation = 1;
                terrain.SetCell(gx, gy, cell);
                GD.Print($"[TerrainMod] 山脉削平完成 ({gx},{gy})");
                break;

            case TerrainModType.Tunnel:
                // 开凿隧道：山脉格子标记HasTunnel
                cell.HasTunnel = true;
                terrain.SetCell(gx, gy, cell);
                GD.Print($"[TerrainMod] 隧道开通完成 ({gx},{gy})");
                break;

            case TerrainModType.Bridge:
                // 架桥：水面格子标记HasBridge
                cell.HasBridge = true;
                terrain.SetCell(gx, gy, cell);
                GD.Print($"[TerrainMod] 桥梁架设完成 ({gx},{gy})");
                break;

            case TerrainModType.UnderseaTunnel:
                // 海底隧道：深水格子标记HasTunnel
                cell.HasTunnel = true;
                cell.Elevation = 1; // 隧道内部按平地高度
                terrain.SetCell(gx, gy, cell);
                GD.Print($"[TerrainMod] 海底隧道贯通完成 ({gx},{gy})");
                break;
        }

        // 重新生成地面纹理（需要刷新受影响的区域）
        mainNode.RefreshGroundTexture();
    }
}
