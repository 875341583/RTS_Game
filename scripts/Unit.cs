using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 兵种类型枚举。
/// </summary>
public enum UnitType { LightTank, HeavyTank, Artillery, RocketLauncher, MissileTank, AntiAir, Harvester, Infantry, Engineer, Sapper, ChiefEngineer, Grenadier, Sniper, FlameInfantry, Transport, Hero, Spy, Thief, Fighter, Helicopter, RocketInfantry, Bomber, Scout, TransportHeli, Destroyer, Submarine, AircraftCarrier, LandingCraft, ApocalypseTank, PrismTank, KirovAirship, TeslaTrooper, Default }

/// <summary>
/// RTS 单位基类：支持选中和移动命令，带血量和简单攻击。
/// 子类可重写 ProcessAI 自定义 AI 行为（如矿车自动采矿）。
/// P1-5: 实现 IUnitEntity 接口，2D/3D 行为契约统一。
/// </summary>
public partial class Unit : CharacterBody2D, IUnitEntity
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

    /// <summary>G1: 设置当前血量（科技效果用）。</summary>
    public void SetHealth(float value) => Health = Mathf.Clamp(value, 0f, MaxHealth);
    public bool IsSelected { get; protected set; }
    public int TeamId { get; set; } = 0;
    /// <summary>C3: 联机全局唯一标识（Host分配，非联机模式下为0）。</summary>
    public int NetId { get; set; } = 0;
    /// <summary>红方自动战斗 AI 开关。开启后主动全图搜索敌人攻击。</summary>
    public bool AutoAI { get; set; } = false;
    /// <summary>自动防御开关。无命令时发现附近敌人自动迎击，消灭后返回守卫位置。</summary>
    public bool AutoDefend { get; set; } = true;
    /// <summary>自动防御警戒范围。</summary>
    public float AggroRange { get; set; } = 280f;
    /// <summary>是否是空中单位（飞行高度模拟，不受地形减速）。</summary>
    public bool IsAirUnit { get; set; } = false;
    /// <summary>是否可以对空攻击（防空车/火箭兵默认对空）。</summary>
    public bool CanAttackAir { get; set; } = false;
    /// <summary>是否是运输直升机（空中搭载步兵）。</summary>
    public bool IsTransportHeli => Type == UnitType.TransportHeli;
    /// <summary>是否是海军单位（水面移动，只在浅水/深水通行）。</summary>
    public bool IsNavalUnit => Type == UnitType.Destroyer || Type == UnitType.Submarine || Type == UnitType.AircraftCarrier || Type == UnitType.LandingCraft;

    // 子类可访问的移动状态
    protected Vector2 _moveTarget;
    protected bool _hasMoveTarget;
    // P0-1: A*寻路路径跟踪
    private List<Vector2> _path = new();
    private int _pathIndex;
    private bool _hasPath;
    private float _pathRepathCooldown;
    private Vector2 _lastPathTarget; // 上次计算路径时的目标，用于检测目标变更
    private Unit? _attackUnitTarget;
    private Building? _attackBuildingTarget;
    private float _attackTimer;
    protected bool _isDead;
    /// <summary>单位是否已死亡（公开只读访问）。</summary>
    public bool IsDead => _isDead;
    /// <summary>G5: 最后攻击方阵营（尤里卡用）。</summary>
    public int _lastAttackerTeam = -1;
    private float _hitFlashTimer;
    private Color _bodyTint = Colors.White;
    private Color _turretTint = Colors.White;
    private float _aiThinkTimer;
    private Vector2 _attackMoveTarget;
    private bool _hasAttackMoveTarget;
    private Vector2 _guardPosition;
    private bool _hasGuardPosition;

    // ======== 命令系统字段 ========
    /// <summary>强制攻击目标坐标。</summary>
    private Vector2 _forceAttackTargetPos;
    private bool _hasForceAttackTarget;
    /// <summary>守卫模式：原地不动，只射程内反击，不追击。</summary>
    private bool _holdPosition;
    /// <summary>巡逻模式字段。</summary>
    private bool _isPatrolling;
    private Vector2 _patrolA;
    private Vector2 _patrolB;
    private bool _patrolToB = true; // true=去B, false=去A
    /// <summary>路径点队列（行军路线）。</summary>
    private readonly Queue<Vector2> _waypointQueue = new();

    /// <summary>运输车内搭载的乘客（步兵类单位）。</summary>
    public List<Unit> Passengers { get; } = new();
    /// <summary>运输车最大搭载人数。</summary>
    public int MaxPassengers { get; set; } = 3;
    /// <summary>是否是运输载具（运输车或运输直升机或登陆艇或航母，可搭载单位）。</summary>
    public bool IsTransport => Type == UnitType.Transport || Type == UnitType.TransportHeli
        || Type == UnitType.LandingCraft || Type == UnitType.AircraftCarrier;
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

    // ======== G7: 间谍任务系统 ========
    /// <summary>当前间谍任务类型（null=无任务）。</summary>
    public SpyMission.MissionType? _spyMission = null;
    /// <summary>间谍任务目标建筑。</summary>
    public Building? _spyTargetBuilding = null;
    /// <summary>间谍任务倒计时（秒）。</summary>
    public float _spyMissionTimer = 0f;
    /// <summary>间谍是否正在执行任务（不可移动）。</summary>
    public bool IsSpyOnMission => _spyMission.HasValue && _spyTargetBuilding != null;

    // ======== E11：单位升级制度 ========
    /// <summary>单位随机能力类型（4大类11种）。</summary>
    public enum UnitAbility { None,
        ArmorPiercing, DoubleShot, Scatter,       // 攻击类
        ReactiveArmor, SelfRepair, SmokeScreen,    // 防御类
        TurboEngine,                               // 机动类
        ReconVision, BattleFrenzy, Plunder, Tenacity // 特殊类
    }
    /// <summary>当前经验值。</summary>
    public float _experience = 0f;
    /// <summary>当前等级（1-4）。</summary>
    public int _level = 1;
    /// <summary>已获得的能力列表。</summary>
    public readonly List<UnitAbility> _abilities = new();
    /// <summary>升级所需经验阈值：Lv2=100, Lv3=300, Lv4=600。</summary>
    private static readonly int[] LevelThresholds = { 0, 100, 300, 600 };
    /// <summary>脱离战斗计时（3秒无攻击=脱战）。</summary>
    private float _outOfCombatTimer = 0f;
    /// <summary>上次升级提示（避免重复Toast）。</summary>
    private int _lastToastLevel = 0;

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

    /// <summary>AI集结模式：true=正在前往集结点，不执行全图搜敌。由Main.AI策略系统控制。</summary>
    public bool AiRallyMode = false;
    /// <summary>AI集结点坐标（AiRallyMode=true时生效）。</summary>
    public Vector2 AiRallyPoint = Vector2.Zero;

    // 节点引用
    protected Sprite2D _body = null!;
    private Sprite2D _selectionRing = null!;
    private ProgressBar _healthBar = null!;
    private static StyleBoxFlat? _healthBarBgStyle;
    private static StyleBoxFlat? _healthBarFgStyle;
    private static StyleBoxFlat? _healthBarFgStyleYellow;
    private static StyleBoxFlat? _healthBarFgStyleRed;
    /// <summary>初始化RA2风格血条样式（静态缓存，所有单位共享）。</summary>
    private static void InitHealthBarStyles()
    {
        if (_healthBarBgStyle != null) return;
        _healthBarBgStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.08f, 0.85f),
            BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            BorderColor = new Color(0.3f, 0.3f, 0.3f, 0.8f),
            CornerRadiusTopLeft = 1, CornerRadiusTopRight = 1,
            CornerRadiusBottomLeft = 1, CornerRadiusBottomRight = 1
        };
        _healthBarFgStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.7f, 0.2f, 0.95f),
            CornerRadiusTopLeft = 1, CornerRadiusTopRight = 1,
            CornerRadiusBottomLeft = 1, CornerRadiusBottomRight = 1
        };
        _healthBarFgStyleYellow = new StyleBoxFlat
        {
            BgColor = new Color(0.9f, 0.8f, 0.15f, 0.95f),
            CornerRadiusTopLeft = 1, CornerRadiusTopRight = 1,
            CornerRadiusBottomLeft = 1, CornerRadiusBottomRight = 1
        };
        _healthBarFgStyleRed = new StyleBoxFlat
        {
            BgColor = new Color(0.85f, 0.15f, 0.1f, 0.95f),
            CornerRadiusTopLeft = 1, CornerRadiusTopRight = 1,
            CornerRadiusBottomLeft = 1, CornerRadiusBottomRight = 1
        };
    }
    /// <summary>根据当前血量百分比更新血条前景颜色（绿→黄→红）。</summary>
    private void UpdateHealthBarStyle()
    {
        if (_healthBar == null) return;
        float pct = MaxHealth > 0 ? Health / MaxHealth : 0f;
        var fg = pct > 0.6f ? _healthBarFgStyle : pct > 0.3f ? _healthBarFgStyleYellow : _healthBarFgStyleRed;
        _healthBar.AddThemeStyleboxOverride("fill", fg);
    }
    // 椭圆阴影点（32边形，缓存复用）
    private static readonly Vector2[] _shadowPtsLarge = GenEllipsePoints(26f, 13f);
    private static readonly Vector2[] _shadowPtsSmall = GenEllipsePoints(13f, 7f);
    private static readonly Vector2[] _shadowPtsBldg = GenEllipsePoints(52f, 26f);
    private static readonly Color _shadowColor = new(0, 0, 0, 0.45f);
    private static readonly Color _shadowColorSoft = new(0, 0, 0, 0.2f);

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
    // E7/E8：空军专用纹理
    private static Texture2D? _fighterHull, _helicopterHull, _bomberHull, _scoutHull, _transportHeliHull;
    // E9：海军纹理
    private static Texture2D? _destroyerHull, _submarineHull, _carrierHull, _landingCraftHull;
    // 灰底炮塔纹理（按兵种）
    private static Texture2D? _turretLight, _turretHeavy, _turretArty, _turretRocket, _turretMissile, _turretAntiAir;

    // R3: 等距8方向精灵图缓存 [unitName][direction] 
    private static readonly Dictionary<string, Texture2D?[]> _isoSprites = new();
    private static readonly string[] IsoDirNames = { "E", "SE", "S", "SW", "W", "NW", "N", "NE" };
    private int _lastDirIndex = -1;  // 上次方向，避免每帧换贴图
    // 炮塔精灵
    protected Sprite2D? _turret;
    // 新素材朝右（RIGHT=0°），无需额外旋转偏移
    private const float SpriteRotationOffset = 0f;

    // ---- P1-4: 多帧动画系统 ----
    /// <summary>单位动画播放器（若该兵种有动画素材则激活，否则回退单帧）。</summary>
    private UnitAnimationPlayer? _animPlayer = null;
    /// <summary>是否在攻击中（用于动画状态机）。</summary>
    private bool _isAttackingAnim = false;

    // ---- 8阵营色调色板（灰底素材用 Modulate 染色）----
    // P1-5第1步: TeamPalette 已统一至 GameData.TeamPalette，此处仅保留访问器以保持调用兼容。
    // 直接转发到 GameData.TeamPalette / GameData.GetTeamColor，避免2D/3D重复定义。

    /// <summary>获取 TeamId 对应的阵营色（转发至 GameData）。</summary>
    public static Color GetTeamColor(int teamId) => GameData.GetTeamColor(teamId);

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

        // E7+E8：空军单位纹理
        _fighterHull = LoadUnitTexture("res://assets/sprites/units/fighter.png");
        _helicopterHull = LoadUnitTexture("res://assets/sprites/units/helicopter.png");
        _bomberHull = LoadUnitTexture("res://assets/sprites/units/bomber.png");
        _scoutHull = LoadUnitTexture("res://assets/sprites/units/scout.png");
        _transportHeliHull = LoadUnitTexture("res://assets/sprites/units/transport_heli.png");
        // E9：海军纹理
        _destroyerHull = LoadUnitTexture("res://assets/sprites/units/destroyer.png");
        _submarineHull = LoadUnitTexture("res://assets/sprites/units/submarine.png");
        _carrierHull = LoadUnitTexture("res://assets/sprites/units/carrier.png");
        _landingCraftHull = LoadUnitTexture("res://assets/sprites/units/landing_craft.png");

        // 灰底炮塔（按兵种）
        _turretLight   = LoadUnitTexture("res://assets/sprites/units/turret_light.png");
        _turretHeavy   = LoadUnitTexture("res://assets/sprites/units/turret_heavy.png");
        _turretArty    = LoadUnitTexture("res://assets/sprites/units/turret_arty.png");
        _turretRocket  = LoadUnitTexture("res://assets/sprites/units/turret_rocket.png");
        _turretMissile = LoadUnitTexture("res://assets/sprites/units/turret_missile.png");
        _turretAntiAir  = LoadUnitTexture("res://assets/sprites/units/turret_antiair.png");

        // ---- RA2风格选中环：双层虚线圆 + 四角标记 ----
        var ring = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        ring.Fill(new Color(0, 0, 0, 0));
        // 外圈：虚线圆（每6度画一段，跳3度）
        for (float a = 0; a < Mathf.Tau; a += 0.105f)
        {
            float endA = a + 0.052f;
            for (float t = a; t < endA; t += 0.005f)
            {
                int cx = (int)(32 + 28 * Mathf.Cos(t));
                int cy = (int)(32 + 28 * Mathf.Sin(t));
                if (cx >= 0 && cx < 64 && cy >= 0 && cy < 64)
                    ring.SetPixel(cx, cy, new Color(0.3f, 0.9f, 1.0f, 1.0f));
            }
        }
        // 内圈：实线细圆
        for (float a = 0; a < Mathf.Tau; a += 0.02f)
        {
            int cx = (int)(32 + 22 * Mathf.Cos(a));
            int cy = (int)(32 + 22 * Mathf.Sin(a));
            if (cx >= 0 && cx < 64 && cy >= 0 && cy < 64)
                ring.SetPixel(cx, cy, new Color(0.2f, 0.7f, 0.9f, 0.7f));
        }
        // 四角L形标记（RA2经典风格）
        int[][] corners = { new[] { 4, 4 }, new[] { 56, 4 }, new[] { 4, 56 }, new[] { 56, 56 } };
        foreach (var c in corners)
        {
            int dx = c[0] < 32 ? 1 : -1;
            int dy = c[1] < 32 ? 1 : -1;
            for (int i = 0; i < 8; i++)
            {
                int px = c[0] + dx * i, py = c[1];
                if (px >= 0 && px < 64 && py >= 0 && py < 64)
                    ring.SetPixel(px, py, new Color(0.5f, 0.95f, 1.0f, 1.0f));
                px = c[0]; py = c[1] + dy * i;
                if (px >= 0 && px < 64 && py >= 0 && py < 64)
                    ring.SetPixel(px, py, new Color(0.5f, 0.95f, 1.0f, 1.0f));
            }
        }
        _ringTex = ImageTexture.CreateFromImage(ring);

        // R3: 预加载等距8方向精灵图
        EnsureIsoSprites();
    }

    /// <summary>R3: 获取UnitType对应的等距精灵图名称。</summary>
    public static string GetIsoSpriteName(UnitType type) => type switch
    {
        UnitType.LightTank => "light_tank",
        UnitType.HeavyTank => "heavy_tank",
        UnitType.Artillery => "artillery",
        UnitType.RocketLauncher => "rocket_launcher",
        UnitType.MissileTank => "missile_launcher",
        UnitType.AntiAir => "anti_air",
        UnitType.Harvester => "harvester",
        UnitType.Infantry => "infantry",
        UnitType.Sapper => "sapper",
        UnitType.ChiefEngineer => "sapper",
        UnitType.Grenadier => "grenadier",
        UnitType.Sniper => "sniper",
        UnitType.FlameInfantry => "flame_infantry",
        UnitType.Hero => "hero",
        UnitType.Spy => "spy",
        UnitType.Thief => "thief",
        UnitType.Fighter => "fighter",
        UnitType.Helicopter => "helicopter",
        UnitType.RocketInfantry => "rocket_soldier",
        UnitType.Bomber => "bomber",
        UnitType.Scout => "scout",
        UnitType.TransportHeli => "transport_heli",
        UnitType.Destroyer => "destroyer",
        UnitType.Submarine => "submarine",
        UnitType.AircraftCarrier => "carrier",
        UnitType.LandingCraft => "landing_craft",
        UnitType.ApocalypseTank => "heavy_tank",
        UnitType.PrismTank => "rocket_launcher",
        UnitType.KirovAirship => "bomber",
        UnitType.TeslaTrooper => "infantry",
        UnitType.Transport => "transport_vehicle",
        UnitType.Engineer => "engineer_vehicle",
        _ => "infantry"
    };

    /// <summary>R3: 预加载所有兵种的8方向等距精灵图。</summary>
    private static void EnsureIsoSprites()
    {
        if (_isoSprites.Count > 0) return;
        foreach (UnitType t in Enum.GetValues<UnitType>())
        {
            if (t == UnitType.Hero) continue; // skip if not in sprite set
            string name = GetIsoSpriteName(t);
            if (_isoSprites.ContainsKey(name)) continue;
            var arr = new Texture2D?[8];
            bool anyLoaded = false;
            for (int d = 0; d < 8; d++)
            {
                string path = $"res://assets/sprites/units_iso/unit_{name}_{IsoDirNames[d]}.png";
                if (ResourceLoader.Exists(path, "Texture2D"))
                {
                    arr[d] = GD.Load<Texture2D>(path);
                    if (arr[d] != null) anyLoaded = true;
                }
            }
            if (anyLoaded)
                _isoSprites[name] = arr;
        }
        GameLog.Debug($"[R3] 等距精灵图加载完成: {_isoSprites.Count} 种兵种");
    }

    /// <summary>R3: 根据移动方向更新等距精灵图。</summary>
    private void UpdateIsoSprite(Vector2 moveDir)
    {
        if (moveDir.LengthSquared() < 0.01f) return;
        string name = GetIsoSpriteName(Type);
        if (!_isoSprites.TryGetValue(name, out var arr) || arr == null) return;

        int dirIdx = IsoCoords.GetDirectionIndex(moveDir);
        if (dirIdx < 0 || dirIdx >= 8) return;
        if (dirIdx == _lastDirIndex) return; // 方向没变不换贴图
        _lastDirIndex = dirIdx;

        var tex = arr[dirIdx];
        if (tex != null)
        {
            _body.Texture = tex;
            _body.Rotation = 0f; // 等距精灵不需要旋转
            _body.Modulate = Colors.White; // 等距精灵已含队伍色
            _body.Scale = Vector2.One;
            if (_turret != null) _turret.Visible = false; // 等距精灵已含炮塔
        }
    }

    /// <summary>P1-4: 由动画播放器调用，设置当前帧纹理。</summary>
    public void SetAnimationFrame(Texture2D tex)
    {
        _body.Texture = tex;
        _body.Rotation = 0f;
        _body.Modulate = Colors.White; // 动画帧已含队伍色
        _body.Scale = Vector2.One;
        if (_turret != null) _turret.Visible = false; // 动画帧已含炮塔
    }

    /// <summary>P1-4: 触发攻击动画（在开火时调用）。</summary>
    public void TriggerAttackAnimation()
    {
        if (_animPlayer != null && _animPlayer.HasAnimation && _lastDirIndex >= 0)
        {
            _isAttackingAnim = true;
            _animPlayer.PlayAttack(_lastDirIndex);
        }
    }

    private static Texture2D LoadUnitTexture(string path)
    {
        var tex = GD.Load<Texture2D>(path);
        if (tex == null)
        {
            GameLog.Error($"[Unit] Failed to load texture: {path}");
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
        // E7：空军底盘
        UnitType.Fighter => _fighterHull!,
        UnitType.Helicopter => _helicopterHull!,
        UnitType.RocketInfantry => _infantryHull!,  // E7：火箭兵复用步兵底盘
        // E8：扩展空军底盘
        UnitType.Bomber => _bomberHull!,
        UnitType.Scout => _scoutHull!,
        UnitType.TransportHeli => _transportHeliHull!,
        // E9：海军底盘
        UnitType.Destroyer => _destroyerHull!,
        UnitType.Submarine => _submarineHull!,
        UnitType.AircraftCarrier => _carrierHull!,
        UnitType.LandingCraft => _landingCraftHull!,
        UnitType.ApocalypseTank => _hullHeavy!,
        UnitType.PrismTank => _hullRocket!,
        UnitType.KirovAirship => _bomberHull!,
        UnitType.TeslaTrooper => _infantryHull!,
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
        // E7/E8：空军单位无独立炮塔
        UnitType.Fighter => null!,
        UnitType.Helicopter => null!,
        UnitType.RocketInfantry => null!,
        UnitType.Bomber => null!,
        UnitType.Scout => null!,
        UnitType.TransportHeli => null!,
        // E9：海军单位无独立炮塔
        UnitType.Destroyer => null!,
        UnitType.Submarine => null!,
        UnitType.AircraftCarrier => null!,
        UnitType.LandingCraft => null!,
        UnitType.ApocalypseTank => _turretHeavy!,
        UnitType.PrismTank => _turretRocket!,
        UnitType.KirovAirship => null!,
        UnitType.TeslaTrooper => null!,
        _ => null!
    };

    /// <summary>按兵种类型初始化属性。必须在 _Ready 之前调用。
    /// P1-2: 从data/units.json加载基础数值，替代原300行switch-case。</summary>
    public void InitAsType(UnitType type)
    {
        Type = type;
        var data = GameData.GetUnit(type);
        var s = data.Stats2D;

        UnitName = data.Name;
        MaxHealth = s.MaxHealth;
        MoveSpeed = s.MoveSpeed;
        AttackDamage = s.AttackDamage;
        AttackRange = s.AttackRange;
        AttackCooldown = s.AttackCooldown;
        AggroRange = s.AggroRange;
        MinAttackRange = s.MinAttackRange;
        SplashRadius = s.SplashRadius;
        CanAttackAir = s.CanAttackAir;
        AutoDefend = s.AutoDefend;
        IsAirUnit = s.IsAirUnit;
        MaxPassengers = s.MaxPassengers;

        // Hero技能随机化（保留原逻辑，JSON只提供基础数值）
        if (s.IsHero)
        {
            _heroSkill = (HeroSkill)(GD.Randi() % 5 + 1);
            switch (_heroSkill)
            {
                case HeroSkill.DoubleShot: UnitName = TrManager.Tr("unit.hero_double_shot"); AttackCooldown = 0.35f; break;
                case HeroSkill.HealAura: UnitName = TrManager.Tr("unit.hero_heal_aura"); break;
                case HeroSkill.Dash: UnitName = TrManager.Tr("unit.hero_dash"); MoveSpeed = 260f; break;
                case HeroSkill.CriticalStrike: UnitName = TrManager.Tr("unit.hero_critical_strike"); break;
                case HeroSkill.Shield: UnitName = TrManager.Tr("unit.hero_shield"); MaxHealth = 300f; break;
            }
            Health = MaxHealth;
            GameLog.Debug($"[E6b] 英雄技能：{_heroSkill}");
        }
    }

    /// <summary>P1-2: 应用阵营数值乘数。在InitAsType之后、TeamId设置之后调用。
    /// 影响生命、伤害、速度。成本在Main.GetUnitCost中处理。</summary>
    public void ApplyFactionMultipliers(int teamId)
    {
        var faction = FactionManager.GetFactionForTeam(teamId);
        MaxHealth = faction.ApplyHealth(MaxHealth);
        AttackDamage = faction.ApplyDamage(AttackDamage);
        MoveSpeed = faction.ApplySpeed(MoveSpeed);
        // 同步当前血量到新上限
        if (Health > MaxHealth) Health = MaxHealth;
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
        UnitType.RocketInfantry => true,  // E7
        UnitType.TeslaTrooper => true,
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

        GameLog.Debug($"[IFV] {passenger.UnitName} 进入 {UnitName} (搭载 {Passengers.Count}/{MaxPassengers})");

        // 首个乘客决定合体功能（运输直升机/登陆艇/航母不做IFV合体，只搭载）
        if (Passengers.Count == 1 && Type == UnitType.Transport)
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
                DeterministicRng.RandRangeFloat(-40, 40),
                DeterministicRng.RandRangeFloat(-40, 40));
            p.GlobalPosition = exitPos;
            main.GetNode<Node2D>("Units").AddChild(p);
        }

        GameLog.Debug($"[IFV] {Passengers.Count} 名乘客从 {UnitName} 下车");
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
                UnitName = TrManager.Tr("unit.merge_engineer_vehicle");
                AttackDamage = 0f;
                AttackRange = 0f;
                MaxHealth = 200f;
                Health = 200f;
                GameLog.Debug("[IFV] 合体：工兵战车（维修+地形改造）");
                break;
            case UnitType.ChiefEngineer:
                // 高级工程师→高级工程车：高效改造
                UnitName = TrManager.Tr("unit.merge_advanced_engineer_vehicle");
                AttackDamage = 0f;
                AttackRange = 0f;
                MaxHealth = 250f;
                Health = 250f;
                GameLog.Debug("[IFV] 合体：高级工兵战车（高级改造）");
                break;
            case UnitType.Infantry:
                // 步兵→武装吉普：轻机枪火力
                UnitName = TrManager.Tr("unit.merge_armed_jeep");
                AttackDamage = 12f;
                AttackRange = 150f;
                AttackCooldown = 0.5f;
                MaxHealth = 160f;
                Health = 160f;
                AggroRange = 250f;
                AutoDefend = true;
                GameLog.Debug("[IFV] 合体：武装吉普（轻机枪）");
                break;
            case UnitType.Grenadier:
                // 掷弹兵→自走炮：AOE火力
                UnitName = TrManager.Tr("unit.merge_assault_gun");
                AttackDamage = 25f;
                AttackRange = 220f;
                AttackCooldown = 1.5f;
                SplashRadius = 70f;
                MaxHealth = 180f;
                Health = 180f;
                AggroRange = 280f;
                AutoDefend = true;
                GameLog.Debug("[IFV] 合体：自走炮（AOE火力）");
                break;
            case UnitType.Sniper:
                // 狙击手→狙击战车：远程精确火力
                UnitName = TrManager.Tr("unit.merge_sniper_tank");
                AttackDamage = 50f;
                AttackRange = 380f;
                AttackCooldown = 2.0f;
                MinAttackRange = 100f;
                MaxHealth = 150f;
                Health = 150f;
                AggroRange = 400f;
                AutoDefend = true;
                GameLog.Debug("[IFV] 合体：狙击战车（远程精确）");
                break;
            case UnitType.FlameInfantry:
                // 喷火兵→喷火战车：近距高DPS
                UnitName = TrManager.Tr("unit.merge_flame_tank");
                AttackDamage = 15f;
                AttackRange = 100f;
                AttackCooldown = 0.25f;
                SplashRadius = 50f;
                MaxHealth = 200f;
                Health = 200f;
                AggroRange = 150f;
                AutoDefend = true;
                GameLog.Debug("[IFV] 合体：喷火战车（近距高DPS）");
                break;
            // E6b：特殊单位IFV合体
            case UnitType.Hero:
                // 英雄→英雄战车：超强火力
                UnitName = TrManager.Tr("unit.merge_hero_tank");
                AttackDamage = 40f;
                AttackRange = 200f;
                AttackCooldown = 0.5f;
                MaxHealth = 250f;
                Health = 250f;
                AggroRange = 300f;
                AutoDefend = true;
                GameLog.Debug("[IFV] 合体：英雄战车（超强火力）");
                break;
            case UnitType.Spy:
                // 间谍→间谍车：渗透战车
                UnitName = TrManager.Tr("unit.merge_spy_vehicle");
                AttackDamage = 0f;
                AttackRange = 0f;
                MaxHealth = 180f;
                Health = 180f;
                MoveSpeed = 280f;
                GameLog.Debug("[IFV] 合体：间谍车（高速渗透）");
                break;
            case UnitType.Thief:
                // 窃贼→劫掠车：偷钱战车
                UnitName = TrManager.Tr("unit.merge_raider");
                AttackDamage = 8f;
                AttackRange = 120f;
                AttackCooldown = 0.6f;
                MaxHealth = 160f;
                Health = 160f;
                AggroRange = 180f;
                AutoDefend = true;
                GameLog.Debug("[IFV] 合体：劫掠车（偷钱战车）");
                break;
            default:
                // 其他步兵→轻型武装车
                UnitName = TrManager.Tr("unit.merge_light_armed_vehicle");
                AttackDamage = 10f;
                AttackRange = 140f;
                AttackCooldown = 0.7f;
                MaxHealth = 170f;
                Health = 170f;
                AggroRange = 220f;
                AutoDefend = true;
                GameLog.Debug("[IFV] 合体：轻型武装车");
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

    /// <summary>恢复运输载具基础属性（所有乘客下车后）。</summary>
    private void RevertToBaseTransport()
    {
        if (Type == UnitType.TransportHeli)
        {
            // E8：运输直升机恢复
            UnitName = TrManager.Tr("unit.transport_heli");
            MaxHealth = 180f;
            AttackDamage = 0f;
            AttackRange = 0f;
            AttackCooldown = 0f;
            MinAttackRange = 0f;
            SplashRadius = 0f;
            AggroRange = 0f;
            AutoDefend = false;
            MoveSpeed = 200f;
            MaxPassengers = 4;
        }
        else if (Type == UnitType.LandingCraft)
        {
            UnitName = TrManager.Tr("unit.landing_craft");
            MaxHealth = 120f;
            AttackDamage = 0f;
            AttackRange = 0f;
            AttackCooldown = 0f;
            MinAttackRange = 0f;
            SplashRadius = 0f;
            AggroRange = 0f;
            AutoDefend = false;
            MoveSpeed = 100f;
            MaxPassengers = 3;
        }
        else if (Type == UnitType.AircraftCarrier)
        {
            UnitName = TrManager.Tr("unit.aircraft_carrier");
            MaxHealth = 300f;
            AttackDamage = 0f;
            AttackRange = 0f;
            AttackCooldown = 0f;
            MinAttackRange = 0f;
            SplashRadius = 0f;
            AggroRange = 0f;
            AutoDefend = false;
            MoveSpeed = 80f;
            MaxPassengers = 4;
        }
        else
        {
            UnitName = TrManager.Tr("unit.transport");
            MaxHealth = 150f;
            AttackDamage = 0f;
            AttackRange = 0f;
            AttackCooldown = 0f;
            MinAttackRange = 0f;
            SplashRadius = 0f;
            AggroRange = 0f;
            AutoDefend = false;
        }
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
        InitHealthBarStyles();
        _healthBar.AddThemeStyleboxOverride("background", _healthBarBgStyle);
        UpdateHealthBarStyle();
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

        // P1-4: 初始化动画播放器（自动检测是否有动画素材）
        _animPlayer = new UnitAnimationPlayer();
        _animPlayer.Setup(this, Type);
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

        // R5: 等距Y-Sort深度排序 — Y越大越靠前（画在越上面）
        ZIndex = RenderLayer.UnitBase + (int)(GlobalPosition.Y / 2f);

        // E3：地形高度视觉偏移——高海拔单位的body向上偏移，模拟"站在高处"
        if (GetParent()?.GetParent() is Main mainNode)
        {
            var terrain = mainNode.GetTerrainGrid();
            terrain.WorldToGrid(GlobalPosition.X, GlobalPosition.Y, out int gx, out int gy);
            var cell = terrain.GetCell(gx, gy);
            float yOffset = cell.Elevation switch { 2 => -3f, 3 => -6f, _ => 0f };
            // E7：空中单位额外上浮模拟飞行高度
            if (IsAirUnit) yOffset -= 12f;
            _body.Position = new Vector2(_body.Position.X, yOffset + (Type == UnitType.Infantry ? 0f : 0f));
            if (_turret != null) _turret.Position = new Vector2(_turret.Position.X, yOffset);
        }

        // 调度子类自定义 AI（默认是玩家命令 + 攻击逻辑）
        ProcessAI(dt);

        // 如果子类 AI 没有清理攻击目标，让基类处理追击/开火
        ResolveCombat(dt);

        // 通用移动
        ProcessMovement(dt);

        // P1-4: 多帧动画更新（如果该兵种有动画素材）
        if (_animPlayer != null && _animPlayer.HasAnimation)
        {
            bool isMoving = Velocity != Vector2.Zero;
            _animPlayer.Update(dt, isMoving, _isAttackingAnim, Math.Max(0, _lastDirIndex));
            if (_animPlayer.AttackFinished)
                _isAttackingAnim = false;
        }

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

        // E11：脱战计时 + 自修复
        bool inCombat = _attackUnitTarget != null || _attackBuildingTarget != null;
        if (inCombat) _outOfCombatTimer = 0f;
        else _outOfCombatTimer += dt;
        if (_abilities.Contains(UnitAbility.SelfRepair) && _outOfCombatTimer >= 3f && Health < MaxHealth && !_isDead)
            Health = Mathf.Min(MaxHealth, Health + MaxHealth * 0.01f * dt);
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

    // ======== E6b + G7：间谍渗透 ========
    private void ProcessSpyInfiltrate(float dt)
    {
        if (Type != UnitType.Spy) return;

        // G7: 如果有间谍任务，处理任务倒计时
        if (_spyMission.HasValue && _spyTargetBuilding != null)
        {
            // 检查目标建筑是否还存在
            if (!IsInstanceValid(_spyTargetBuilding) || _spyTargetBuilding.Health <= 0)
            {
                GameLog.Debug("[G7] 间谍任务取消：目标建筑已不存在");
                _spyMission = null;
                _spyTargetBuilding = null;
                _spyMissionTimer = 0f;
                return;
            }

            // 检查距离：间谍必须接近目标建筑
            float dist = GlobalPosition.DistanceTo(_spyTargetBuilding.GlobalPosition);
            if (dist > 80f)
            {
                // 太远了，取消任务
                GameLog.Debug($"[G7] 间谍任务取消：距离目标过远 ({(int)dist}px)");
                _spyMission = null;
                _spyTargetBuilding = null;
                _spyMissionTimer = 0f;
                return;
            }

            _spyMissionTimer -= dt;
            if (_spyMissionTimer <= 0f)
            {
                // 任务完成：判定成功/失败
                // P0修复: Fac_MindControl（尤里）: 成功率额外+15%（间谍效率增强）
                float successRate = SpyMission.SuccessRate;
                if (GetParent()?.GetParent() is Main mainNode0)
                    successRate *= mainNode0.GetTechSpyEfficiencyMul(TeamId);
                bool success = DeterministicRng.Randf() < successRate;
                var missionType = _spyMission.Value;
                var target = _spyTargetBuilding;
                int teamId = TeamId;

                if (success)
                {
                    GameLog.Debug($"[G7] 间谍任务成功: {SpyMission.MissionName(missionType)} → {target.BuildingName} (Team {target.TeamId})");
                    // 通知Main执行任务效果
                    if (GetParent()?.GetParent() is Main mainNode)
                    {
                        mainNode.ExecuteSpyMission(missionType, target, teamId);
                    }
                }
                else
                {
                    GameLog.Error($"[G7] 间谍任务失败: {SpyMission.MissionName(missionType)} — 间谍被击毙！");
                    // 间谍死亡
                    TakeDamage(MaxHealth + 1f); // 确保死亡
                }

                _spyMission = null;
                _spyTargetBuilding = null;
                _spyMissionTimer = 0f;
                return;
            }

            // 伪装：执行任务时自动伪装成敌方颜色
            if (_spyDisguiseTeam == -1)
            {
                _spyDisguiseTeam = _spyTargetBuilding.TeamId;
                _body.Modulate = GetTeamColor(_spyDisguiseTeam);
            }
            return; // 正在执行任务，不执行旧逻辑
        }

        // 旧E6b逻辑：间谍接近敌方建筑时自动伪装
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

                // 旧渗透倒计时（简化版，仅偷$200，G7任务是主系统）
                _spyInfiltrateTimer += dt;
                if (_spyInfiltrateTimer >= 4f)
                {
                    _spyInfiltrateTimer = 0f;
                    // 偷取$200（旧逻辑保留兼容）
                    if (main is Main mainNode)
                    {
                        int stolen = Mathf.Min(200, mainNode.GetMoney(b.TeamId));
                        mainNode.SpendMoney(b.TeamId, stolen);
                        mainNode.AddResourceForTeam(TeamId, stolen);
                        GameLog.Debug($"[E6b] 间谍偷取 ${stolen} (Team {b.TeamId} → Team {TeamId})");
                    }
                }
                break;
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
                        GameLog.Debug($"[E6b] 窃贼偷取 ${stolen} (Team {b.TeamId} → Team {TeamId})");
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
                        GameLog.Debug($"[E6b] 窃贼偷取矿车 ${stolen} (Team {u.TeamId} → Team {TeamId})");
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

            // 集结模式：正在前往集结点等待进攻命令，不执行全图搜敌
            // 只反击进入AggroRange的敌人，保持向集结点移动
            if (AiRallyMode)
            {
                var rallyEnemy = FindNearestEnemyUnitInRange(AggroRange);
                if (rallyEnemy != null)
                {
                    _attackUnitTarget = rallyEnemy;
                    _attackBuildingTarget = null;
                }
                else
                {
                    // 没有近距离敌人时，保持向集结点移动
                    if (GlobalPosition.DistanceTo(AiRallyPoint) > 30f)
                    {
                        _moveTarget = AiRallyPoint;
                        _hasMoveTarget = true;
                    }
                    else
                    {
                        _hasMoveTarget = false;
                    }
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

        // 强制攻击：移动到目标位置后持续对目标点开火（无视友方判断）
        if (_hasForceAttackTarget)
        {
            float distToTarget = GlobalPosition.DistanceTo(_forceAttackTargetPos);
            if (distToTarget <= AttackRange && distToTarget >= MinAttackRange)
            {
                // 在射程内，持续对目标点开火
                _hasMoveTarget = false;
                _attackTimer -= dt;
                if (_attackTimer <= 0f)
                {
                    // 对目标点地面开火：生成特效
                    SpawnFireEffects(_forceAttackTargetPos);
                    TriggerAttackAnimation();
                    // 溅射伤害：对目标点周围的任何单位造成伤害（强制攻击不区分敌友）
                    if (GetParent() is Node2D parent)
                    {
                        foreach (var child in parent.GetChildren())
                        {
                            if (child is Unit u && u != this && !u._isDead
                                && u.GlobalPosition.DistanceTo(_forceAttackTargetPos) <= Mathf.Max(SplashRadius, 30f))
                            {
                                u.TakeDamage(AttackDamage);
                            }
                        }
                    }
                    _attackTimer = AttackCooldown;
                }
            }
            else if (distToTarget < MinAttackRange)
            {
                // 太近了，后退
                var away = (GlobalPosition - _forceAttackTargetPos).Normalized();
                _moveTarget = GlobalPosition + away * (MinAttackRange - distToTarget + 50f);
                _hasMoveTarget = true;
            }
            else
            {
                // 不在射程内，移动到目标
                _moveTarget = _forceAttackTargetPos;
                _hasMoveTarget = true;
            }
            return;
        }

        // 巡逻逻辑：在两点之间来回移动，遇敌接敌后继续巡逻
        if (_isPatrolling)
        {
            _aiThinkTimer -= dt;
            if (_aiThinkTimer <= 0f)
            {
                _aiThinkTimer = 0.25f;
                // 巡逻途中遇敌自动接敌
                var enemy2 = FindNearestEnemyUnitInRange(AggroRange);
                if (enemy2 != null) _attackUnitTarget = enemy2;
                else
                {
                    var bld = FindNearestEnemyBuildingInRange(AggroRange);
                    if (bld != null) _attackBuildingTarget = bld;
                }
            }

            // 如果正在战斗，不更新巡逻移动
            if (_attackUnitTarget == null && _attackBuildingTarget == null)
            {
                var patrolTarget = _patrolToB ? _patrolB : _patrolA;
                if (GlobalPosition.DistanceTo(patrolTarget) < 20f)
                {
                    // 到达巡逻点，切换方向
                    _patrolToB = !_patrolToB;
                    var nextTarget = _patrolToB ? _patrolB : _patrolA;
                    _moveTarget = nextTarget;
                    _hasMoveTarget = true;
                    ClearPath();
                }
                else if (!_hasMoveTarget)
                {
                    // 开始向当前巡逻目标移动
                    _moveTarget = _patrolToB ? _patrolB : _patrolA;
                    _hasMoveTarget = true;
                    ClearPath();
                }
            }
            // 战斗结束后（目标死亡），ProcessAI后续的AutoDefend会触发，
            // 但由于_isPatrolling=true，AutoDefend不会覆盖巡逻路线
            // 只在敌人被消灭后重新移动到巡逻目标
            if (_attackUnitTarget == null && _attackBuildingTarget == null && _isPatrolling)
            {
                var curTarget = _patrolToB ? _patrolB : _patrolA;
                if (!_hasMoveTarget || _moveTarget.DistanceTo(curTarget) > 30f)
                {
                    _moveTarget = curTarget;
                    _hasMoveTarget = true;
                    ClearPath();
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
                var enemy3 = FindNearestEnemyUnitInRange(AggroRange * 1.5f);
                if (enemy3 != null) _attackUnitTarget = enemy3;
                else
                {
                    var bld3 = FindNearestEnemyBuilding();
                    if (bld3 != null && GlobalPosition.DistanceTo(bld3.GlobalPosition) < AggroRange * 1.5f)
                        _attackBuildingTarget = bld3;
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

        // 守卫/驻守模式：原地不动，只在射程内反击，不追击
        if (_holdPosition)
        {
            // 守卫模式下不移动，不追击
            _hasMoveTarget = false;
            // 自动反击射程内的敌人（不更新_attackUnitTarget到远处敌人——让ResolveCombat在射程内处理）
            if (AutoDefend && AttackDamage > 0f && _attackUnitTarget == null && _attackBuildingTarget == null)
            {
                _aiThinkTimer -= dt;
                if (_aiThinkTimer <= 0f)
                {
                    _aiThinkTimer = 0.3f;
                    // 只搜索射程内的敌人，不追击
                    var holdEnemy = FindNearestEnemyUnitInRange(AttackRange);
                    if (holdEnemy != null)
                        _attackUnitTarget = holdEnemy;
                    else
                    {
                        var holdBld = FindNearestEnemyBuildingInRange(AttackRange);
                        if (holdBld != null)
                            _attackBuildingTarget = holdBld;
                    }
                }
            }
            // 守卫模式：如果攻击目标超出射程，放弃追击
            if (_attackUnitTarget != null && IsInstanceValid(_attackUnitTarget)
                && GlobalPosition.DistanceTo(_attackUnitTarget.GlobalPosition) > AttackRange)
            {
                _attackUnitTarget = null;
            }
            if (_attackBuildingTarget != null && IsInstanceValid(_attackBuildingTarget)
                && GlobalPosition.DistanceTo(_attackBuildingTarget.GlobalPosition) > AttackRange)
            {
                _attackBuildingTarget = null;
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
            var enemy4 = FindNearestEnemyUnitInRange(AggroRange);
            if (enemy4 != null)
            {
                _attackUnitTarget = enemy4;
            }
            else
            {
                // 没有敌方单位时，搜索附近敌方建筑并攻击（单位开进敌方家会自动打建筑）
                var enemyBld4 = FindNearestEnemyBuildingInRange(AggroRange);
                if (enemyBld4 != null)
                {
                    _attackBuildingTarget = enemyBld4;
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
        // E11：计算战斗狂热加成（附近有敌方单位则+20%攻速）
        bool frenzyActive = _abilities.Contains(UnitAbility.BattleFrenzy)
            && GetParent() is Node2D frenzyParent;
        if (frenzyActive)
        {
            frenzyActive = false;
            foreach (var c in ((Node2D)GetParent()).GetChildren())
            {
                if (c is Unit eu && eu.TeamId != TeamId && !eu._isDead
                    && GlobalPosition.DistanceTo(eu.GlobalPosition) <= AggroRange)
                { frenzyActive = true; break; }
            }
        }

        // 攻击单位目标
        if (_attackUnitTarget != null)
        {
            if (_attackUnitTarget._isDead || !IsInstanceValid(_attackUnitTarget))
            {
                // E11：击杀敌方单位获得经验
                if (IsInstanceValid(_attackUnitTarget))
                {
                    GainExperience(50);
                    // E11：掠夺能力——击杀+$10
                    if (_abilities.Contains(UnitAbility.Plunder) && GetParent()?.GetParent() is Main plunderMain)
                        plunderMain.AwardPlunderGold(TeamId, 10);
                }
                _attackUnitTarget = null;
            }
            else
            {
                var dist = GlobalPosition.DistanceTo(_attackUnitTarget.GlobalPosition);
                if (dist <= AttackRange && dist >= MinAttackRange)
                {
                    _hasMoveTarget = false;
                    float effectiveCooldown = AttackCooldown;
                    // E11：双发 +40%射速
                    if (_abilities.Contains(UnitAbility.DoubleShot))
                        effectiveCooldown *= 0.6f;
                    // E11：战斗狂热 +20%攻速
                    if (frenzyActive)
                        effectiveCooldown *= 0.8f;
                    _attackTimer -= dt;
                    if (_attackTimer <= 0)
                    {
                        float dmg = AttackDamage;
                        // E6b：英雄暴击30%概率双倍伤害
                        if (Type == UnitType.Hero && _heroSkill == HeroSkill.CriticalStrike && DeterministicRng.Randf() < 0.3f)
                            dmg *= 2f;
                        // E11：穿甲弹 +25%对重甲单位
                        if (_abilities.Contains(UnitAbility.ArmorPiercing) && IsHeavyUnit(_attackUnitTarget.Type))
                            dmg *= 1.25f;
                        // G5: 记录攻击者阵营（尤里卡用）
                        _attackUnitTarget._lastAttackerTeam = TeamId;
                        _attackUnitTarget.TakeDamage(dmg);
                        // E11：散射能力——额外溅射60px范围
                        if (_abilities.Contains(UnitAbility.Scatter) && GetParent() is Node2D sp)
                        {
                            foreach (var child in sp.GetChildren())
                            {
                                if (child is Unit su && su != _attackUnitTarget && su.TeamId != TeamId && !su._isDead
                                    && su.GlobalPosition.DistanceTo(_attackUnitTarget.GlobalPosition) <= 60f)
                                    su.TakeDamage(dmg * 0.5f);
                            }
                        }
                        // Q5：开火视觉特效
                        SpawnFireEffects(_attackUnitTarget.GlobalPosition);
                        // P1-4: 触发攻击动画
                        TriggerAttackAnimation();
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
                        _attackTimer = effectiveCooldown;
                        // E11：造成伤害获得经验
                        GainExperience(dmg * 0.5f);
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
                // E11：击杀建筑获得经验
                GainExperience(100);
                if (_abilities.Contains(UnitAbility.Plunder) && GetParent()?.GetParent() is Main bPlunderMain)
                    bPlunderMain.AwardPlunderGold(TeamId, 10);
                _attackBuildingTarget = null;
            }
            else
            {
                var dist = GlobalPosition.DistanceTo(_attackBuildingTarget.GlobalPosition);
                if (dist <= AttackRange && dist >= MinAttackRange)
                {
                    _hasMoveTarget = false;
                    float effectiveCooldown = AttackCooldown;
                    if (_abilities.Contains(UnitAbility.DoubleShot))
                        effectiveCooldown *= 0.6f;
                    if (frenzyActive)
                        effectiveCooldown *= 0.8f;
                    _attackTimer -= dt;
                    if (_attackTimer <= 0)
                    {
                        float dmgB = AttackDamage;
                        // E6b：英雄暴击30%概率双倍伤害
                        if (Type == UnitType.Hero && _heroSkill == HeroSkill.CriticalStrike && DeterministicRng.Randf() < 0.3f)
                            dmgB *= 2f;
                        if (_abilities.Contains(UnitAbility.ArmorPiercing))
                            dmgB *= 1.25f;
                        // G5: 记录攻击者阵营（尤里卡用）
                        _attackBuildingTarget._lastAttackerTeam = TeamId;
                        _attackBuildingTarget.TakeDamage(dmgB);
                        // Q5：开火视觉特效
                        SpawnFireEffects(_attackBuildingTarget.GlobalPosition);
                        // P1-4: 触发攻击动画
                        TriggerAttackAnimation();
                        _attackTimer = effectiveCooldown;
                        // E11：造成伤害获得经验
                        GainExperience(dmgB * 0.5f);
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
        // G7: 间谍执行任务期间不可移动
        if (IsSpyOnMission)
        {
            Velocity = Vector2.Zero;
            return;
        }

        if (_hasMoveTarget)
        {
            // P0-1: A*寻路路径跟随
            Vector2 currentTarget = _moveTarget; // 默认直线移动目标
            bool usePathfinding = false;
            if (!IsAirUnit)
            {
                usePathfinding = TryGetPathTarget(out currentTarget, dt);
            }

            var direction = (currentTarget - GlobalPosition);
            var distance = direction.Length();

            // H1修复：使用PathFinder的路径点阈值，最终目标用更小阈值确保精确到达
            bool isLastWaypoint = !usePathfinding || !_hasPath || _pathIndex >= _path.Count - 1;
            float threshold = isLastWaypoint ? 5f : PathFinder.GetWaypointThreshold();

            if (distance > threshold)
            {
                direction = direction.Normalized();

                // E2：地形速度修正——查询当前所在地形获取速度系数
                float speedMult = 1.0f;
                // E7：空中单位不受地形减速，始终全速
                if (!IsAirUnit && GetParent()?.GetParent() is Main mainNode)
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

                // P0-4: 单位分离力——防止单位重叠堆叠
                Vector2 separation = ComputeSeparationForce();
                if (separation != Vector2.Zero)
                {
                    // 分离力叠加到移动方向上：移动方向占主导，分离力做偏移
                    Vector2 blended = (direction * 0.7f + separation * 0.3f).Normalized();
                    Velocity = blended * MoveSpeed * speedMult;
                }
                else
                {
                    Velocity = direction * MoveSpeed * speedMult;
                }
                MoveAndSlide();
                if (direction != Vector2.Zero)
                {
                    // R3: 等距精灵方向切换（优先于旋转）
                    UpdateIsoSprite(direction);
                    // 如果没有等距精灵，回退到旋转
                    if (_lastDirIndex < 0)
                        _body.Rotation = direction.Angle() + SpriteRotationOffset;
                }
            }
            else
            {
                // 到达当前路径点
                if (usePathfinding && _hasPath && _pathIndex < _path.Count - 1)
                {
                    // 前进到下一个路径点
                    _pathIndex++;
                }
                else
                {
                    // 到达最终目标
                    Velocity = Vector2.Zero;
                    _hasMoveTarget = false;
                    _hasPath = false;
                    // 检查路径点队列，自动前往下一个路径点
                    if (_waypointQueue.Count > 0)
                    {
                        var nextWp = _waypointQueue.Dequeue();
                        _moveTarget = nextWp;
                        _hasMoveTarget = true;
                        ClearPath();
                    }
                }
            }
        }
        else
        {
            // P0-4: 静止单位也应用分离力——如果其他单位挤过来了，自动让开
            Vector2 separation = ComputeSeparationForce();
            if (separation != Vector2.Zero)
            {
                Velocity = separation * MoveSpeed * 0.5f; // 以半速退开
                MoveAndSlide();
            }
            else
            {
                Velocity = Vector2.Zero;
            }
        }
    }

    /// <summary>
    /// P0-4: 计算单位分离力——查询附近友方单位，产生排斥向量防止单位重叠堆叠。
    /// 空军单位跳过（可重叠飞行）。静止单位也应用分离力以推开叠在上面的其他单位。
    /// 性能：只遍历同父节点的兄弟单位，用DistanceSquaredTo避免开方。
    /// </summary>
    private Vector2 ComputeSeparationForce()
    {
        // 空军单位不需要分离力
        if (IsAirUnit) return Vector2.Zero;

        // 分离半径：单位碰撞框为32×32，理想间距设为36
        const float separationRadius = 36f;
        const float separationRadiusSq = separationRadius * separationRadius;

        Vector2 force = Vector2.Zero;
        int neighborCount = 0;

        // 从父节点获取兄弟单位（性能远优于GetAllUnits全量遍历）
        var parent = GetParent();
        if (parent == null) return Vector2.Zero;

        foreach (var sibling in parent.GetChildren())
        {
            if (sibling is not Unit other) continue;
            if (other == this) continue;
            if (!IsInstanceValid(other)) continue;
            if (other._isDead) continue;
            // 空军单位不参与地面分离
            if (other.IsAirUnit) continue;

            var diff = GlobalPosition - other.GlobalPosition;
            float distSq = diff.LengthSquared();

            if (distSq < separationRadiusSq && distSq > 0.01f)
            {
                // 距离越近排斥力越大（反比归一化）
                float dist = Mathf.Sqrt(distSq);
                float strength = (separationRadius - dist) / separationRadius;
                force += (diff / dist) * strength;
                neighborCount++;
            }
            else if (distSq <= 0.01f)
            {
                // 完全重叠：随机方向推开
                force += new Vector2(1f, 0.5f).Normalized();
                neighborCount++;
            }
        }

        if (neighborCount > 0)
        {
            // 取平均方向并归一化
            force = (force / neighborCount).Normalized();
        }

        return force;
    }

    /// <summary>
    /// P0-1: 尝试获取A*路径上的下一个目标点。
    /// 如果没有有效路径或PathFinder不可用，返回false（调用方退回直线移动）。
    /// C2修复：检测_moveTarget变更时使旧路径失效（节流重算）。
    /// M1修复：cooldown用实际dt递减。
    /// </summary>
    private bool TryGetPathTarget(out Vector2 target, float dt)
    {
        target = _moveTarget;

        // C2修复：目标变更时使路径失效（追击移动目标时路径自动刷新）
        if (_hasPath && _moveTarget.DistanceSquaredTo(_lastPathTarget) > 50f * 50f)
        {
            _hasPath = false;
            _path.Clear();
            _pathIndex = 0;
        }

        if (_hasPath && _path.Count > 0 && _pathIndex < _path.Count)
        {
            target = _path[_pathIndex];
            return true;
        }

        // 路径耗尽或不存在，尝试重新计算
        if (_pathRepathCooldown > 0f)
        {
            _pathRepathCooldown -= dt;
            return false;
        }

        if (GetParent()?.GetParent() is Main mainNode)
        {
            var pathfinder = mainNode.GetPathFinder();
            if (pathfinder != null)
            {
                var cat = GetTerrainCategory();
                var newPath = pathfinder.FindPath(GlobalPosition, _moveTarget, cat);
                if (newPath.Count > 0)
                {
                    _path = newPath;
                    _pathIndex = 0;
                    _hasPath = true;
                    _lastPathTarget = _moveTarget;
                    target = _path[0];
                    return true;
                }
                else
                {
                    // 无可行路径，设冷却避免每帧重算
                    _pathRepathCooldown = 0.5f;
                    _hasPath = false;
                    return false;
                }
            }
        }

        return false;
    }

    /// <summary>P0-1: 清除当前路径（移动命令变更时调用）。</summary>
    private void ClearPath()
    {
        _hasPath = false;
        _path.Clear();
        _pathIndex = 0;
        _pathRepathCooldown = 0f; // 命令变更时允许立即重算
    }

    /// <summary>P0-1: 清除路径但保留重算冷却（停止命令用，避免误触发重算）。</summary>
    private void ClearPathKeepCooldown()
    {
        _hasPath = false;
        _path.Clear();
        _pathIndex = 0;
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
        UnitType.RocketInfantry => TerrainUnitCategory.Infantry,
        // E8：空中单位不会实际调用此处（IsAirUnit跳过地形查询），但给个安全默认值
        UnitType.Fighter => TerrainUnitCategory.LightVehicle,
        UnitType.Helicopter => TerrainUnitCategory.LightVehicle,
        UnitType.Bomber => TerrainUnitCategory.HeavyVehicle,
        UnitType.Scout => TerrainUnitCategory.LightVehicle,
        UnitType.TransportHeli => TerrainUnitCategory.LightVehicle,
        // E9：海军单位
        UnitType.Destroyer => TerrainUnitCategory.Naval,
        UnitType.Submarine => TerrainUnitCategory.Naval,
        UnitType.AircraftCarrier => TerrainUnitCategory.Naval,
        UnitType.LandingCraft => TerrainUnitCategory.Naval,
        UnitType.ApocalypseTank => TerrainUnitCategory.HeavyVehicle,
        UnitType.PrismTank => TerrainUnitCategory.HeavyVehicle,
        UnitType.KirovAirship => TerrainUnitCategory.HeavyVehicle,
        UnitType.TeslaTrooper => TerrainUnitCategory.Infantry,
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
                // E7：对空规则——非对空单位不能锁定空中单位
                if (u.IsAirUnit && !CanAttackAir && !IsAirUnit) continue;
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

    // ---- P1-5: IRenderable / IUnitEntity 薄适配方法 ----
    // 不改变现有逻辑，仅提供接口要求的访问入口，供统一渲染/逻辑层调用。
    /// <summary>IRenderable: 返回当前世界坐标。</summary>
    public new Vector2 GetPosition() => GlobalPosition;
    /// <summary>IRenderable: Y-Sort 排序键（与 _Process 中 ZIndex 计算一致）。</summary>
    public float GetSortY() => RenderLayer.UnitBase + (int)(GlobalPosition.Y / 2f);

    public virtual void CommandMove(Vector2 target)
    {
        _moveTarget = target;
        _hasMoveTarget = true;
        _attackUnitTarget = null;
        _attackBuildingTarget = null;
        _hasAttackMoveTarget = false; // 普通移动取消攻击移动
        _hasForceAttackTarget = false; // 普通移动取消强制攻击
        _holdPosition = false; // 普通移动取消守卫
        _isPatrolling = false; // 普通移动取消巡逻
        _waypointQueue.Clear(); // 普通移动清空路径点队列
        // 玩家下令时更新守卫位置为新的目的地
        _guardPosition = target;
        _hasGuardPosition = true;
        // P0-1: 清除旧路径，让ProcessMovement重新计算A*路径
        ClearPath();
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
        // P0-1: 清除旧路径
        ClearPath();
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
        // 清除新命令状态
        _hasForceAttackTarget = false;
        _holdPosition = false;
        _isPatrolling = false;
        _waypointQueue.Clear();
        // P0-1: 清除路径，保留冷却避免误重算
        ClearPathKeepCooldown();
    }

    /// <summary>强制攻击：对目标坐标持续开火，无视友方判断。</summary>
    public void CommandForceAttack(Vector2 target)
    {
        _forceAttackTargetPos = target;
        _hasForceAttackTarget = true;
        _attackUnitTarget = null;
        _attackBuildingTarget = null;
        _hasAttackMoveTarget = false;
        _holdPosition = false;
        _isPatrolling = false;
        _waypointQueue.Clear();
        _moveTarget = target;
        _hasMoveTarget = true;
        ClearPath();
    }

    /// <summary>散开：向四周随机方向散开100~200px。</summary>
    public void CommandScatter()
    {
        float angle = DeterministicRng.RandRangeFloat(0, 360) * Mathf.Pi / 180.0f;
        float dist = DeterministicRng.RandRangeFloat(100, 200);
        var offset = new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);
        var target = GlobalPosition + offset;
        _moveTarget = target;
        _hasMoveTarget = true;
        _attackUnitTarget = null;
        _attackBuildingTarget = null;
        _hasAttackMoveTarget = false;
        _hasForceAttackTarget = false;
        _holdPosition = false;
        _isPatrolling = false;
        _waypointQueue.Clear();
        ClearPath();
    }

    /// <summary>巡逻：在两点之间来回巡逻，遇敌自动接敌后继续。</summary>
    public void CommandPatrol(Vector2 from, Vector2 to)
    {
        _isPatrolling = true;
        _patrolA = from;
        _patrolB = to;
        _patrolToB = true;
        _moveTarget = to;
        _hasMoveTarget = true;
        _attackUnitTarget = null;
        _attackBuildingTarget = null;
        _hasAttackMoveTarget = false;
        _hasForceAttackTarget = false;
        _holdPosition = false;
        _waypointQueue.Clear();
        ClearPath();
    }

    /// <summary>守卫/驻守：原地不动，只射程内反击，不追击。</summary>
    public void CommandHoldPosition()
    {
        _holdPosition = true;
        _hasMoveTarget = false;
        _hasAttackMoveTarget = false;
        _hasForceAttackTarget = false;
        _isPatrolling = false;
        _waypointQueue.Clear();
        Velocity = Vector2.Zero;
        _guardPosition = GlobalPosition;
        _hasGuardPosition = true;
        ClearPathKeepCooldown();
    }

    /// <summary>阵型移动：移动到目标位置，由外部计算偏移后调用CommandMove。</summary>
    public void CommandFormationMove(Vector2 target)
    {
        CommandMove(target);
    }

    /// <summary>追加路径点到行军路线。</summary>
    public void EnqueueWaypoint(Vector2 waypoint)
    {
        // 如果没有移动目标，直接移动到该路径点
        if (!_hasMoveTarget && _waypointQueue.Count == 0)
        {
            CommandMove(waypoint);
            return;
        }
        _waypointQueue.Enqueue(waypoint);
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

    /// <summary>G7: 间谍执行任务 — 移动到目标建筑附近后开始渗透倒计时。</summary>
    public void CommandSpyMission(Building target, SpyMission.MissionType mission)
    {
        if (Type != UnitType.Spy) return;
        _spyMission = mission;
        _spyTargetBuilding = target;
        _spyMissionTimer = SpyMission.InfiltrateTime;
        // P0修复: Fac_StealthOps（尤里）: 渗透时间-30%（隐身能力增强）
        if (GetParent()?.GetParent() is Main mainNode)
            _spyMissionTimer *= mainNode.GetTechStealthInfiltrateMul(TeamId);
        // 先移动到目标建筑附近
        _moveTarget = target.GlobalPosition;
        _hasMoveTarget = true;
        _attackUnitTarget = null;
        _attackBuildingTarget = null;
        // P0-1: 清除旧路径
        ClearPath();
        GameLog.Debug($"[G7] 间谍开始任务: {SpyMission.MissionName(mission)} → {target.BuildingName}");
    }

    public void TakeDamage(float damage)
    {
        // E11：烟幕闪避 20%概率
        if (_abilities.Contains(UnitAbility.SmokeScreen) && DeterministicRng.Randf() < 0.2f)
            return;
        // E11：反应装甲 -20%伤害
        float actualDmg = damage;
        if (_abilities.Contains(UnitAbility.ReactiveArmor))
            actualDmg *= 0.8f;
        // E11：坚韧——低血+30%防御
        if (_abilities.Contains(UnitAbility.Tenacity) && Health < MaxHealth * 0.3f)
            actualDmg *= 0.7f;
        Health -= actualDmg;
        _hitFlashTimer = 0.08f; // Q5：受击闪白
        // P1-6: 命中音效——受击时播放
        if (GetParent()?.GetParent() is Main mainNode)
            mainNode.PlayHitSfx();
        if (_healthBar != null)
            _healthBar.Value = Mathf.Max(0, Health);
        UpdateHealthBarStyle();
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
            UpdateHealthBarStyle();
            UpdateHealthBarVisibility();
        }
    }

    // G1: 科技效果方法
    private float _techHealthMul = 1f;
    private float _techDamageMul = 1f;
    private float _techMoveSpeedMul = 1f; // G3: 战术卡移速乘数追踪，防止重复叠加

    /// <summary>G1: 应用科技生命值乘数（叠乘方式，已有乘数会叠加）。</summary>
    public void ApplyTechHealthMultiplier(float mul)
    {
        float baseMax = MaxHealth / _techHealthMul; // 恢复到基础值
        _techHealthMul *= mul;
        MaxHealth = baseMax * _techHealthMul;
    }

    /// <summary>G1: 应用科技攻击力乘数。</summary>
    public void ApplyTechDamageMultiplier(float mul)
    {
        _techDamageMul *= mul;
        AttackDamage *= mul;
    }

    /// <summary>G1/G3: 应用科技移动速度乘数（叠乘方式，已有乘数会叠加）。</summary>
    public void ApplyTechMoveSpeedMultiplier(float mul)
    {
        float baseSpeed = MoveSpeed / _techMoveSpeedMul; // 恢复到基础值
        _techMoveSpeedMul *= mul;
        MoveSpeed = baseSpeed * _techMoveSpeedMul;
    }

    /// <summary>G1: 获取科技攻击力乘数。</summary>
    public float TechDamageMultiplier => _techDamageMul;

    protected void MoveTo(Vector2 target) { _moveTarget = target; _hasMoveTarget = true; ClearPath(); }
    protected void StopMove() { _hasMoveTarget = false; Velocity = Vector2.Zero; ClearPath(); }

    // ==================== P0-2: 存档/读档 访问器 ====================
    // 以下方法供SaveLoadSystem读取/写入单位状态。所有读取方法只读，写入方法仅在读档恢复时调用。

    /// <summary>获取当前移动目标坐标（无活动目标时返回GlobalPosition）。</summary>
    public Vector2 GetMoveTarget() => _moveTarget;
    /// <summary>是否存在活跃的移动目标。</summary>
    public bool HasMoveTarget() => _hasMoveTarget;
    /// <summary>获取警戒位置（无则返回GlobalPosition）。</summary>
    public Vector2 GetGuardPosition() => _guardPosition;
    /// <summary>是否存在警戒位置。</summary>
    public bool HasGuardPosition() => _hasGuardPosition;
    /// <summary>获取当前等级。</summary>
    public int GetLevel() => _level;
    /// <summary>获取当前经验值。</summary>
    public float GetExperience() => _experience;
    /// <summary>获取已有能力列表的副本。</summary>
    public List<UnitAbility> GetAbilities() => new(_abilities);
    /// <summary>获取英雄技能枚举值。</summary>
    public HeroSkill GetHeroSkill() => _heroSkill;
    /// <summary>获取间谍伪装阵营ID（-1=未伪装）。</summary>
    public int GetSpyDisguiseTeam() => _spyDisguiseTeam;
    /// <summary>获取上次受到攻击的阵营ID（-1=未受攻击）。</summary>
    public int GetLastAttackerTeam() => _lastAttackerTeam;

    /// <summary>获取运输车乘客的类型列表（仅Type，不含血量等级）。</summary>
    public List<UnitType> GetPassengerTypes()
    {
        var list = new List<UnitType>();
        foreach (var p in Passengers)
            if (IsInstanceValid(p)) list.Add(p.Type);
        return list;
    }
    /// <summary>获取运输车乘客的血量列表。</summary>
    public List<float> GetPassengerHealths()
    {
        var list = new List<float>();
        foreach (var p in Passengers)
            if (IsInstanceValid(p)) list.Add(p.Health);
        return list;
    }
    /// <summary>获取运输车乘客的等级列表。</summary>
    public List<int> GetPassengerLevels()
    {
        var list = new List<int>();
        foreach (var p in Passengers)
            if (IsInstanceValid(p)) list.Add(p.GetLevel());
        return list;
    }

    // ---------- 读档恢复写入器 ----------

    /// <summary>P0-2 读档：恢复等级和经验（绕过AddExperience的连锁升级逻辑，升级后能力由RestoreAbilities单独设置）。</summary>
    public void RestoreLevel(int level, float experience)
    {
        _level = Mathf.Clamp(level, 1, SaveLoadSystem.MaxUnitLevel);
        _experience = Mathf.Max(0f, experience);
    }

    /// <summary>P0-2 读档：直接覆盖能力列表（清空旧能力后重新填入）。</summary>
    public void RestoreAbilities(List<UnitAbility> abilities)
    {
        _abilities.Clear();
        // 去重添加
        var seen = new HashSet<UnitAbility>();
        foreach (var a in abilities)
        {
            if (a == UnitAbility.None || seen.Contains(a)) continue;
            seen.Add(a);
            _abilities.Add(a);
        }
    }

    /// <summary>P0-2 读档：恢复英雄技能。</summary>
    public void RestoreHeroSkill(HeroSkill skill) => _heroSkill = skill;

    /// <summary>P0-2 读档：恢复间谍伪装阵营与最后受击阵营。</summary>
    public void RestoreSpyState(int spyDisguiseTeam, int lastAttackerTeam)
    {
        _spyDisguiseTeam = spyDisguiseTeam;
        _lastAttackerTeam = lastAttackerTeam;
        // 伪装颜色修正
        if (_spyDisguiseTeam >= 0 && _body != null)
            _body.Modulate = GetTeamColor(_spyDisguiseTeam);
    }

    /// <summary>P0-2 读档：恢复移动目标（触发ClearPath以确保下一帧重算路径）。</summary>
    public void RestoreMoveTarget(Vector2 target)
    {
        _moveTarget = target;
        _hasMoveTarget = true;
        ClearPath();
    }

    /// <summary>P0-2 读档：恢复警戒位置。</summary>
    public void RestoreGuardPosition(Vector2 pos)
    {
        _guardPosition = pos;
        _hasGuardPosition = true;
    }

    // ---------- P1-5 第4步：UnitData 快照 ----------

    /// <summary>
    /// 生成当前单位的纯数据快照（无 Godot 节点依赖）。
    /// 2D/3D 共用数据载体，可用于存档、网络同步、3D 数据共享。
    /// </summary>
    public UnitData GetUnitData()
    {
        var d = new UnitData
        {
            Type = Type,
            TeamId = TeamId,
            MaxHealth = MaxHealth,
            MoveSpeed = MoveSpeed,
            AttackDamage = AttackDamage,
            AttackRange = AttackRange,
            AttackCooldown = AttackCooldown,
            MinAttackRange = MinAttackRange,
            SplashRadius = SplashRadius,
            AggroRange = AggroRange,
            CanAttackAir = CanAttackAir,
            IsAirUnit = IsAirUnit,
            AutoDefend = AutoDefend,
            AutoAI = AutoAI,
            MaxPassengers = MaxPassengers,
            Health = Health,
            PosX = GlobalPosition.X,
            PosY = GlobalPosition.Y,
            IsDead = _isDead,
            MoveTargetX = _moveTarget.X,
            MoveTargetY = _moveTarget.Y,
            HasMoveTarget = _hasMoveTarget,
            GuardX = _guardPosition.X,
            GuardY = _guardPosition.Y,
            HasGuardPosition = _hasGuardPosition,
            Level = _level,
            Experience = _experience,
            Abilities = new List<UnitAbility>(_abilities),
            HeroSkill = _heroSkill,
            SpyDisguiseTeam = _spyDisguiseTeam,
            LastAttackerTeam = _lastAttackerTeam,
            Passengers = new List<UnitData>(),
        };
        foreach (var p in Passengers)
            if (IsInstanceValid(p))
                d.Passengers.Add(p.GetUnitData());
        return d;
    }

    /// <summary>
    /// 从 UnitData 快照恢复单位状态（用于读档/3D数据同步）。
    /// 不改变视觉节点引用，仅恢复数据和位置。调用方需确保类型匹配。
    /// </summary>
    public void ApplyUnitData(in UnitData d)
    {
        Health = d.Health;
        MaxHealth = d.MaxHealth;
        MoveSpeed = d.MoveSpeed;
        AttackDamage = d.AttackDamage;
        AttackRange = d.AttackRange;
        AttackCooldown = d.AttackCooldown;
        MinAttackRange = d.MinAttackRange;
        SplashRadius = d.SplashRadius;
        AggroRange = d.AggroRange;
        CanAttackAir = d.CanAttackAir;
        IsAirUnit = d.IsAirUnit;
        AutoDefend = d.AutoDefend;
        AutoAI = d.AutoAI;
        MaxPassengers = d.MaxPassengers;
        GlobalPosition = new Vector2(d.PosX, d.PosY);
        _isDead = d.IsDead;
        _level = d.Level;
        _experience = d.Experience;
        _heroSkill = d.HeroSkill;
        _spyDisguiseTeam = d.SpyDisguiseTeam;
        _lastAttackerTeam = d.LastAttackerTeam;

        if (d.HasMoveTarget)
            RestoreMoveTarget(new Vector2(d.MoveTargetX, d.MoveTargetY));
        if (d.HasGuardPosition)
            RestoreGuardPosition(new Vector2(d.GuardX, d.GuardY));
        if (d.Abilities != null)
            RestoreAbilities(d.Abilities);

        UpdateHealthBarVisibility();
    }

    private void UpdateHealthBarVisibility()
    {
        if (_healthBar != null)
            _healthBar.Visible = IsSelected || Health < MaxHealth;
    }

    protected virtual void Die()
    {
        _isDead = true;
        GameLog.Debug($"{UnitName} (阵营 {TeamId}) 被摧毁");

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

        // Phase1: 单位死亡残骸 — 留下烧焦痕迹，8秒后淡出消失
        if (GetParent()?.GetParent() is Node2D wreckParent)
        {
            var wreck = new Sprite2D();
            // 用程序化生成的暗色椭圆作为残骸
            var wreckImg = Image.CreateEmpty(48, 32, false, Image.Format.Rgba8);
            wreckImg.Fill(new Color(0, 0, 0, 0));
            for (int wx = 0; wx < 48; wx++)
            {
                for (int wy = 0; wy < 32; wy++)
                {
                    float dx = (wx - 24f) / 24f;
                    float dy = (wy - 16f) / 16f;
                    float dist = dx * dx + dy * dy;
                    if (dist < 1f)
                    {
                        // 中心暗黑，边缘暗灰，带随机烧焦纹理
                        float darkness = 0.05f + DeterministicRng.RandRangeFloat(0, 0.1f);
                        float alpha = (1f - dist) * 0.7f;
                        wreckImg.SetPixel(wx, wy, new Color(darkness, darkness, darkness, alpha));
                    }
                }
            }
            var wreckTex = ImageTexture.CreateFromImage(wreckImg);
            wreck.Texture = wreckTex;
            wreck.GlobalPosition = GlobalPosition;
            wreck.ZIndex = RenderLayer.Terrain + 10; // 在地形之上，单位之下
            wreckParent.AddChild(wreck);

            // 8秒后淡出消失
            var fadeTween = wreck.CreateTween();
            fadeTween.TweenInterval(6f); // 6秒不动
            fadeTween.TweenProperty(wreck, "modulate:a", 0f, 2f); // 2秒淡出
            fadeTween.TweenCallback(Callable.From(() => { if (IsInstanceValid(wreck)) wreck.QueueFree(); }));
        }

        // 阶段12-C：单位死亡音效
        if (GetParent()?.GetParent() is Main mainNode)
        {
            mainNode.PlayUnitDeathSfx(Type);
            // Phase1: 重型单位死亡屏幕震动
            if (Type == UnitType.HeavyTank)
                mainNode.ScreenShake(4f, 0.2f);
        }

        // G5: 尤里卡 — 击杀者获得尤里卡进度
        if (GetParent()?.GetParent() is Main eurekaMain && _lastAttackerTeam >= 0)
            eurekaMain.OnEurekaKill(_lastAttackerTeam);

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
        // 外层柔和阴影（更大、更淡，模拟散射光）
        DrawSetTransform(new Vector2(3f, yOff), 0f, new Vector2(1.3f, 1.3f));
        DrawPolygon(pts, new Color[] { _shadowColorSoft });
        // 内层主阴影
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
                    GameLog.Error($"[TerrainMod] {UnitName} (Team {TeamId}) 资金不足 $_terrainModCost，无法施工");
                    _terrainModType = TerrainModType.None;
                    _isConstructing = false;
                    return;
                }
            }

            GameLog.Debug($"[TerrainMod] {UnitName} (Team {TeamId}) 开始{_terrainModType}施工，费用${_terrainModCost}，耗时{_terrainModDuration:F1}s");
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
                GameLog.Debug($"[TerrainMod] 山脉削平完成 ({gx},{gy})");
                break;

            case TerrainModType.Tunnel:
                // 开凿隧道：山脉格子标记HasTunnel
                cell.HasTunnel = true;
                terrain.SetCell(gx, gy, cell);
                GameLog.Debug($"[TerrainMod] 隧道开通完成 ({gx},{gy})");
                break;

            case TerrainModType.Bridge:
                // 架桥：水面格子标记HasBridge
                cell.HasBridge = true;
                terrain.SetCell(gx, gy, cell);
                GameLog.Debug($"[TerrainMod] 桥梁架设完成 ({gx},{gy})");
                break;

            case TerrainModType.UnderseaTunnel:
                // 海底隧道：深水格子标记HasTunnel
                cell.HasTunnel = true;
                cell.Elevation = 1; // 隧道内部按平地高度
                terrain.SetCell(gx, gy, cell);
                GameLog.Debug($"[TerrainMod] 海底隧道贯通完成 ({gx},{gy})");
                break;
        }

        // 重新生成地面纹理（需要刷新受影响的区域）
        mainNode.RefreshGroundTexture();
    }

    // ======== E11：经验/升级/能力系统 ========

    /// <summary>获得经验值，自动检查升级。</summary>
    public void GainExperience(float xp)
    {
        if (_level >= 4) return; // 已满级
        _experience += xp;
        CheckLevelUp();
    }

    /// <summary>检查是否满足升级条件，满足则升级并抽取随机能力。</summary>
    private void CheckLevelUp()
    {
        while (_level < 4 && _experience >= LevelThresholds[_level])
        {
            _level++;
            var ability = RollRandomAbility();
            _abilities.Add(ability);
            // 涡轮引擎立即生效
            if (ability == UnitAbility.TurboEngine)
                MoveSpeed *= 1.2f;
            // 侦察视野立即生效
            if (ability == UnitAbility.ReconVision)
                AggroRange *= 1.5f;
            // 掠夺：需要Main配合（在Die中回调）
            GameLog.Debug($"[E11] {UnitName} 升级到 Lv{_level}！获得能力: {AbilityName(ability)}");
        }
    }

    /// <summary>从未拥有的能力池中随机抽取1个。</summary>
    private UnitAbility RollRandomAbility()
    {
        var pool = new List<UnitAbility>
        {
            UnitAbility.ArmorPiercing, UnitAbility.DoubleShot, UnitAbility.Scatter,
            UnitAbility.ReactiveArmor, UnitAbility.SelfRepair, UnitAbility.SmokeScreen,
            UnitAbility.TurboEngine,
            UnitAbility.ReconVision, UnitAbility.BattleFrenzy, UnitAbility.Plunder, UnitAbility.Tenacity
        };
        // 移除已拥有的
        pool.RemoveAll(a => _abilities.Contains(a));
        if (pool.Count == 0) return UnitAbility.None;
        return DeterministicRng.Choice(pool);
    }

    /// <summary>判断是否为重甲单位（穿甲弹加成目标）。</summary>
    private static bool IsHeavyUnit(UnitType type) => type switch
    {
        UnitType.HeavyTank or UnitType.MissileTank or UnitType.Destroyer
        or UnitType.AircraftCarrier or UnitType.Submarine => true,
        _ => false
    };

    /// <summary>能力中文名（用于HUD显示）。</summary>
    public static string AbilityName(UnitAbility a) => a switch
    {
        UnitAbility.ArmorPiercing => TrManager.Tr("unit.ability_armor_piercing"),
        UnitAbility.DoubleShot => TrManager.Tr("unit.ability_double_shot"),
        UnitAbility.Scatter => TrManager.Tr("unit.ability_scatter"),
        UnitAbility.ReactiveArmor => TrManager.Tr("unit.ability_reactive_armor"),
        UnitAbility.SelfRepair => TrManager.Tr("unit.ability_self_repair"),
        UnitAbility.SmokeScreen => TrManager.Tr("unit.ability_smoke_screen"),
        UnitAbility.TurboEngine => TrManager.Tr("unit.ability_turbo_engine"),
        UnitAbility.ReconVision => TrManager.Tr("unit.ability_recon_vision"),
        UnitAbility.BattleFrenzy => TrManager.Tr("unit.ability_battle_frenzy"),
        UnitAbility.Plunder => TrManager.Tr("unit.ability_plunder"),
        UnitAbility.Tenacity => TrManager.Tr("unit.ability_tenacity"),
        _ => ""
    };

    /// <summary>能力简短描述。</summary>
    public static string AbilityDesc(UnitAbility a) => a switch
    {
        UnitAbility.ArmorPiercing => TrManager.Tr("unit.ability_desc_armor_piercing"),
        UnitAbility.DoubleShot => TrManager.Tr("unit.ability_desc_double_shot"),
        UnitAbility.Scatter => TrManager.Tr("unit.ability_desc_scatter"),
        UnitAbility.ReactiveArmor => TrManager.Tr("unit.ability_desc_reactive_armor"),
        UnitAbility.SelfRepair => TrManager.Tr("unit.ability_desc_self_repair"),
        UnitAbility.SmokeScreen => TrManager.Tr("unit.ability_desc_smoke_screen"),
        UnitAbility.TurboEngine => TrManager.Tr("unit.ability_desc_turbo_engine"),
        UnitAbility.ReconVision => TrManager.Tr("unit.ability_desc_recon_vision"),
        UnitAbility.BattleFrenzy => TrManager.Tr("unit.ability_desc_battle_frenzy"),
        UnitAbility.Plunder => TrManager.Tr("unit.ability_desc_plunder"),
        UnitAbility.Tenacity => TrManager.Tr("unit.ability_desc_tenacity"),
        _ => ""
    };
}
