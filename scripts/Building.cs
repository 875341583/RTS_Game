using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RTSGame;

/// <summary>
/// 建筑类型枚举。
/// </summary>
public enum BuildingType { Base, PowerPlant, Barracks, WarFactory, TechCenter, Turret, AntiAirTurret, RepairPad, Airfield, Shipyard, NukeSilo, LightningTower, MissileSilo }

/// <summary>
/// 生产项类型：可由建筑排产的战斗单位或矿车。
/// P1-5: 末尾追加 None/Sapper/ChiefEngineer（3D特有成员），原25个成员序号0-24不变，旧存档兼容。
/// </summary>
public enum ProductionType { LightTank, HeavyTank, Artillery, RocketLauncher, MissileTank, Harvester, Infantry, AntiAir, Engineer, Grenadier, Sniper, FlameInfantry, Transport, Hero, Spy, Thief, Fighter, Helicopter, RocketInfantry, Bomber, Scout, TransportHeli, Destroyer, Submarine, AircraftCarrier, LandingCraft, ApocalypseTank, PrismTank, KirovAirship, TeslaTrooper, None, Sapper, ChiefEngineer }

/// <summary>
/// 建筑/基地：可被选中、可被攻击。不同类型解锁不同单位生产。
/// P1-5: 实现 IBuildingEntity 接口，2D/3D 行为契约统一。
/// </summary>
public partial class Building : Area2D, IBuildingEntity
{
    /// <summary>P0-1: 建筑被摧毁/出售时触发，供Main移除PathFinder障碍。</summary>
    public event Action<Building>? Destroyed;

    [Export] public float MaxHealth { get; set; } = 1000f;
    [Export] public string BuildingName { get; set; } = TrManager.Tr("building.base.name");

    public float Health { get; private set; }

    /// <summary>G1: 设置当前血量（科技效果用）。</summary>
    public void SetHealth(float value) { Health = Mathf.Clamp(value, 0f, MaxHealth); }
    public bool IsSelected { get; private set; }
    public int TeamId { get; set; } = 0;
    /// <summary>G5: 最后攻击方阵营（尤里卡用）。</summary>
    public int _lastAttackerTeam = -1;
    public BuildingType Type { get; set; } = BuildingType.Base;
    public int PowerProvided { get; set; } = 0;
    public int PowerConsumed { get; set; } = 0;

    // ---- 阶段12-A1 防御建筑攻击系统 ----
    /// <summary>是否为防御建筑（会自动攻击敌方单位）。</summary>
    public bool IsDefensive { get; private set; } = false;
    /// <summary>攻击伤害（防御建筑）。</summary>
    public float AttackDamage { get; private set; } = 0f;
    /// <summary>攻击射程（防御建筑）。</summary>
    public float AttackRange { get; private set; } = 0f;
    /// <summary>攻击冷却时间（秒，防御建筑）。</summary>
    public float AttackCooldown { get; private set; } = 1f;
    private float _turretAttackTimer = 0f;
    /// <summary>炮塔当前朝向角度（弧度）。防御建筑会在 _Draw 中渲染旋转炮塔。</summary>
    private float _turretAngle = 0f;

    // ---- 阶段12-A2 维修厂系统 ----
    /// <summary>是否为维修厂（自动修复附近友方单位）。</summary>
    public bool IsRepairStation { get; private set; } = false;
    /// <summary>维修半径（维修厂）。</summary>
    public float RepairRadius { get; private set; } = 220f;
    /// <summary>每次修复血量（维修厂，每秒一次）。</summary>
    public float RepairPerTick { get; private set; } = 25f;
    private float _repairTimer = 0f;

    // ---- G2 生产系统 ----
    /// <summary>集结点：新生产单位自动移动到此位置。null 表示无集结点。</summary>
    public Vector2? RallyPoint { get; private set; }
    private readonly Queue<ProductionType> _productionQueue = new();
    private ProductionType? _currentProduction;
    private float _productionTimer;
    private float _productionDuration;
    /// <summary>生产队列最大容量（含正在生产的1个）。</summary>
    public const int MaxQueueSize = 5;
    /// <summary>当前队列中的生产订单数（含正在生产的1个）。</summary>
    public int QueueCount => _productionQueue.Count + (_currentProduction.HasValue ? 1 : 0);
    /// <summary>当前生产进度 0~1。</summary>
    public float ProductionProgress => (_currentProduction.HasValue && _productionDuration > 0f)
        ? Mathf.Clamp(1f - _productionTimer / _productionDuration, 0f, 1f) : 0f;
    public bool IsProducing => _currentProduction.HasValue;
    /// <summary>当前正在生产的单位类型（无则null）。</summary>
    public ProductionType? CurrentProductionType => _currentProduction;
    /// <summary>当前生产剩余时间（秒）。</summary>
    public float ProductionTimeRemaining => _productionTimer;
    /// <summary>当前生产总时间（秒）。</summary>
    public float ProductionTimeTotal => _productionDuration;

    private Sprite2D _body = null!;
    private Sprite2D _selectionRing = null!;
    private ProgressBar _healthBar = null!;
    private static StyleBoxFlat? _bldgHpBgStyle;
    private static StyleBoxFlat? _bldgHpFgGreen;
    private static StyleBoxFlat? _bldgHpFgYellow;
    private static StyleBoxFlat? _bldgHpFgRed;
    private static void InitBldgHealthBarStyles()
    {
        if (_bldgHpBgStyle != null) return;
        _bldgHpBgStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.08f, 0.85f),
            BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
            BorderColor = new Color(0.3f, 0.3f, 0.3f, 0.8f)
        };
        _bldgHpFgGreen = new StyleBoxFlat { BgColor = new Color(0.2f, 0.7f, 0.2f, 0.95f) };
        _bldgHpFgYellow = new StyleBoxFlat { BgColor = new Color(0.9f, 0.8f, 0.15f, 0.95f) };
        _bldgHpFgRed = new StyleBoxFlat { BgColor = new Color(0.85f, 0.15f, 0.1f, 0.95f) };
    }
    private void UpdateBldgHealthBarStyle()
    {
        if (_healthBar == null) return;
        float pct = MaxHealth > 0 ? Health / MaxHealth : 0f;
        var fg = pct > 0.6f ? _bldgHpFgGreen : pct > 0.3f ? _bldgHpFgYellow : _bldgHpFgRed;
        _healthBar.AddThemeStyleboxOverride("fill", fg);
    }
    private float _hitFlashTimer;

    // Phase1: 受损冒烟粒子系统
    private struct SmokeParticle
    {
        public Vector2 Offset;
        public Vector2 Velocity;
        public float Age;
        public float Lifetime;
        public float StartRadius;
    }
    private readonly List<SmokeParticle> _smokeParticles = new();
    private float _smokeSpawnTimer = 0f;
    private static readonly Random _smokeRng = new();

    private static Texture2D? _baseTex;
    private static Texture2D? _powerTex;
    private static Texture2D? _barracksTex;
    private static Texture2D? _warTex;
    private static Texture2D? _techTex;
    // 阶段12-A1 新增建筑纹理
    private static Texture2D? _turretTex;
    private static Texture2D? _antiAirTurretTex;
    private static Texture2D? _repairPadTex;
    private static Texture2D? _airfieldTex;  // E7
    private static Texture2D? _shipyardTex;  // E9
    // E10：超武建筑纹理
    private static Texture2D? _nukeSiloTex, _lightningTowerTex, _missileSiloTex;
    private static Texture2D? _buildingRingTex;
    private Color _teamTint = Colors.White;

    // R4: 等距建筑精灵图缓存
    private static readonly Dictionary<BuildingType, Texture2D?> _isoBuildingSprites = new();
    private static bool _isoBuildingsLoaded = false;

    // ---- 工程车占领系统 ----
    /// <summary>占领进度 0~1（1=占领完成）。</summary>
    public float CaptureProgress { get; private set; } = 0f;
    private int _capturingTeamId = -1;
    private bool _captureTickThisFrame = false;

    // ---- G8: 占领强化系统 ----
    /// <summary>原阵营ID（占领前），-1=未被占领过或已被同化。</summary>
    public int _originalTeamId = -1;
    /// <summary>缴获生产加速剩余时间（秒）。</summary>
    public float _capturedProduceTimer = 0f;
    /// <summary>叛变风险倒计时（秒），0=无风险。</summary>
    public float _defectionTimer = 0f;
    /// <summary>是否处于缴获加速期。</summary>
    public bool IsCapturedProduceBoost => _capturedProduceTimer > 0f;
    /// <summary>是否处于叛变风险期。</summary>
    public bool IsDefectionRisk => _defectionTimer > 0f;

    /// <summary>按建筑类型初始化属性。必须在 _Ready 之前调用。
    /// P1-2: 从data/buildings.json加载基础数值，替代原switch-case。</summary>
    public void InitAsType(BuildingType type)
    {
        Type = type;
        var data = GameData.GetBuilding(type);
        var s = data.Stats2D;

        BuildingName = data.Name;
        MaxHealth = s.MaxHealth;
        PowerProvided = s.PowerProvided;
        PowerConsumed = s.PowerConsumed;
        IsDefensive = s.IsDefensive;
        AttackDamage = s.AttackDamage;
        AttackRange = s.AttackRange;
        AttackCooldown = s.AttackCooldown;
        IsRepairStation = s.IsRepairStation;
        RepairRadius = s.RepairRadius > 0 ? s.RepairRadius : RepairRadius; // 保留默认220
    }

    /// <summary>P1-2: 应用阵营数值乘数。在InitAsType之后、TeamId设置之后调用。</summary>
    public void ApplyFactionMultipliers(int teamId)
    {
        var faction = FactionManager.GetFactionForTeam(teamId);
        MaxHealth = faction.ApplyHealth(MaxHealth);
        AttackDamage = faction.ApplyDamage(AttackDamage);
    }

    public override void _Ready()
    {
        Health = MaxHealth;
        _body = GetNode<Sprite2D>("Body");
        _selectionRing = GetNode<Sprite2D>("SelectionRing");
        _healthBar = GetNode<ProgressBar>("HealthBar");

        EnsureTextures();
        EnsureIsoBuildingTextures();

        // R4: 优先使用等距建筑精灵图，无则回退到旧PNG
        if (_isoBuildingSprites.TryGetValue(Type, out var isoTex) && isoTex != null)
        {
            _body.Texture = isoTex;
            _body.Scale = new Vector2(0.5f, 0.5f); // 等距精灵图256x256需缩放到128显示尺寸
            _body.Modulate = _teamTint; // 仍用队伍色染色
        }
        else
        {
            _body.Texture = Type switch
            {
                BuildingType.PowerPlant => _powerTex!,
                BuildingType.Barracks => _barracksTex!,
                BuildingType.WarFactory => _warTex!,
                BuildingType.TechCenter => _techTex!,
                BuildingType.Turret => _turretTex!,
                BuildingType.AntiAirTurret => _antiAirTurretTex!,
                BuildingType.RepairPad => _repairPadTex!,
                BuildingType.Airfield => _airfieldTex!,  // E7
                BuildingType.Shipyard => _shipyardTex!,  // E9
                BuildingType.NukeSilo => _nukeSiloTex!,       // E10
                BuildingType.LightningTower => _lightningTowerTex!, // E10
                BuildingType.MissileSilo => _missileSiloTex!,     // E10
                _ => _baseTex!
            };
            _body.Scale = new Vector2(1.4f, 1.4f);
            _body.Modulate = _teamTint;
        }
        _selectionRing.Texture = _buildingRingTex;
        _selectionRing.Visible = false;
        _healthBar.MaxValue = MaxHealth;
        _healthBar.Value = Health;
        _healthBar.Visible = false;
        InitBldgHealthBarStyles();
        _healthBar.AddThemeStyleboxOverride("background", _bldgHpBgStyle);
        UpdateBldgHealthBarStyle();

        // Phase1: 建造入场动画 — 从0缩放到目标大小，淡入
        var targetScale = _body.Scale;
        _body.Scale = Vector2.Zero;
        _body.Modulate = new Color(_teamTint.R, _teamTint.G, _teamTint.B, 0f);
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(_body, "scale", targetScale, 0.4f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_body, "modulate:a", 1f, 0.3f);
        tween.Chain();
        // 动画结束后恢复modulate为teamTint（避免alpha值残留）
        tween.TweenCallback(Callable.From(() => _body.Modulate = _teamTint));

        // 8阵营色染色：向白色混合30%，让阵营色占主体（75%），8色强烈区分同时保留建筑手绘明暗细节
        _teamTint = Unit.GetTeamColor(TeamId).Lerp(Colors.White, 0.30f);

        // 像素艺术必须用 Nearest 过滤，Linear 会让 50+ 色的 PNG 被插值平滑成单色块
        _body.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        // 选取圈同步放大
        _selectionRing.Scale = new Vector2(1.4f, 1.4f);
    }

    /// <summary>加载建筑 PNG 纹理（Kenney Sci-fi RTS, CC0）。</summary>
    private static void EnsureTextures()
    {
        if (_baseTex != null) return;

        // 加载外部 PNG 纹理替换代码生成纹理
        _baseTex      = LoadTexture("res://assets/sprites/buildings/base.png");
        _powerTex     = LoadTexture("res://assets/sprites/buildings/powerplant.png");
        _barracksTex  = LoadTexture("res://assets/sprites/buildings/barracks.png");
        _warTex       = LoadTexture("res://assets/sprites/buildings/warfactory.png");
        _techTex      = LoadTexture("res://assets/sprites/buildings/techcenter.png");
        _turretTex         = LoadTexture("res://assets/sprites/buildings/turret.png");
        _antiAirTurretTex  = LoadTexture("res://assets/sprites/buildings/antiair.png");
        _repairPadTex      = LoadTexture("res://assets/sprites/buildings/repairpad.png");
        _airfieldTex       = LoadTexture("res://assets/sprites/buildings/airfield.png");  // E7
        _shipyardTex       = LoadTexture("res://assets/sprites/buildings/shipyard.png");  // E9
        _nukeSiloTex       = LoadTexture("res://assets/sprites/buildings/nuke_silo.png");       // E10
        _lightningTowerTex = LoadTexture("res://assets/sprites/buildings/lightning_tower.png"); // E10
        _missileSiloTex    = LoadTexture("res://assets/sprites/buildings/missile_silo.png");     // E10

        // RA2风格建筑选择环：虚线圆 + 四角L标记
        var ring = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8);
        ring.Fill(Colors.Transparent);
        // 外圈：虚线圆
        for (float a = 0; a < Mathf.Tau; a += 0.105f)
        {
            float endA = a + 0.052f;
            for (float t = a; t < endA; t += 0.004f)
            {
                int cx = (int)(64 + 60 * Mathf.Cos(t));
                int cy = (int)(64 + 60 * Mathf.Sin(t));
                if (cx >= 0 && cx < 128 && cy >= 0 && cy < 128)
                    ring.SetPixel(cx, cy, new Color(0.3f, 0.9f, 1.0f, 1.0f));
            }
        }
        // 内圈：半透明实线
        for (float a = 0; a < Mathf.Tau; a += 0.015f)
        {
            int cx = (int)(64 + 54 * Mathf.Cos(a));
            int cy = (int)(64 + 54 * Mathf.Sin(a));
            if (cx >= 0 && cx < 128 && cy >= 0 && cy < 128)
                ring.SetPixel(cx, cy, new Color(0.2f, 0.7f, 0.9f, 0.5f));
        }
        // 四角L形标记
        int[][] corners = { new[] { 4, 4 }, new[] { 116, 4 }, new[] { 4, 116 }, new[] { 116, 116 } };
        foreach (var c in corners)
        {
            int dx = c[0] < 64 ? 1 : -1;
            int dy = c[1] < 64 ? 1 : -1;
            for (int i = 0; i < 12; i++)
            {
                int px = c[0] + dx * i, py = c[1];
                if (px >= 0 && px < 128 && py >= 0 && py < 128)
                    ring.SetPixel(px, py, new Color(0.5f, 0.95f, 1.0f, 1.0f));
                px = c[0]; py = c[1] + dy * i;
                if (px >= 0 && px < 128 && py >= 0 && py < 128)
                    ring.SetPixel(px, py, new Color(0.5f, 0.95f, 1.0f, 1.0f));
            }
        }
        _buildingRingTex = ImageTexture.CreateFromImage(ring);
    }

    /// <summary>R4: 获取BuildingType对应的等距精灵图文件名。</summary>
    private static string GetIsoBuildingSpriteName(BuildingType type) => type switch
    {
        BuildingType.Base => "base",
        BuildingType.PowerPlant => "powerplant",
        BuildingType.Barracks => "barracks",
        BuildingType.WarFactory => "warfactory",
        BuildingType.TechCenter => "techcenter",
        BuildingType.Turret => "turret",
        BuildingType.AntiAirTurret => "antiair",
        BuildingType.RepairPad => "repairpad",
        BuildingType.Airfield => "airfield",
        BuildingType.Shipyard => "shipyard",
        BuildingType.NukeSilo => "nuke_silo",
        BuildingType.LightningTower => "lightning_tower",
        BuildingType.MissileSilo => "missile_silo",
        _ => "base"
    };

    /// <summary>R4: 预加载13种等距建筑精灵图。</summary>
    private static void EnsureIsoBuildingTextures()
    {
        if (_isoBuildingsLoaded) return;
        _isoBuildingsLoaded = true;
        foreach (BuildingType t in System.Enum.GetValues(typeof(BuildingType)))
        {
            string name = GetIsoBuildingSpriteName(t);
            string path = $"res://assets/sprites/buildings_iso/building_{name}.png";
            var tex = GD.Load<Texture2D>(path);
            if (tex != null)
                _isoBuildingSprites[t] = tex;
            else
                GameLog.Error($"[R4] Failed to load building sprite: {path}");
        }
        GameLog.Debug($"[R4] 等距建筑精灵图加载完成: {_isoBuildingSprites.Count}/{13} 种建筑");
    }

    private static Texture2D LoadTexture(string path)
    {
        var tex = GD.Load<Texture2D>(path);
        if (tex == null)
        {
            GameLog.Error($"[Building] Failed to load texture: {path}");
            // 降级：返回1x1品红色纹理
            var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            img.SetPixel(0, 0, Colors.Magenta);
            return ImageTexture.CreateFromImage(img);
        }
        return tex; // Godot 导入 PNG 返回 CompressedTexture2D，不是 ImageTexture
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        if (_selectionRing != null)
            _selectionRing.Visible = selected;
        if (_healthBar != null)
            _healthBar.Visible = selected || Health < MaxHealth;
    }

    // ---- P1-5: IRenderable / IBuildingEntity 薄适配方法 ----
    // 不改变现有逻辑，仅提供接口要求的访问入口，供统一渲染/逻辑层调用。
    /// <summary>IRenderable: 是否已被摧毁（血量归零）。</summary>
    public bool IsDead => Health <= 0f;
    /// <summary>IRenderable: 返回当前世界坐标。</summary>
    public Vector2 GetPosition() => GlobalPosition;
    /// <summary>IRenderable: Y-Sort 排序键（与 _Process 中 ZIndex 计算一致）。</summary>
    public float GetSortY() => RenderLayer.UnitBase + (int)(GlobalPosition.Y / 2f);
    /// <summary>IBuildingEntity: 是否正常运营（存活）。低电降速由 Main 的 PowerGrid 管理，不在实体层判定。</summary>
    public bool IsOperational() => Health > 0f;

    public void TakeDamage(float damage)
    {
        // G5: 接收Unit攻击代码传入的攻击者阵营
        Health -= damage;
        _hitFlashTimer = 0.1f; // Q5：受击闪白
        if (_healthBar != null)
        {
            _healthBar.Value = Mathf.Max(0, Health);
            _healthBar.Visible = true;
            UpdateBldgHealthBarStyle();
        }
        // 补强：建筑被攻击时播放金属撞击声
        if (GetParent()?.GetParent() is Main hitMain)
            hitMain.PlayBuildingDamagedSfx();
        // G4+：通知己方单位回防
        if (GetParent()?.GetParent() is Main main)
            main.OnBuildingAttacked(this);
        if (Health <= 0)
        {
            GameLog.Debug($"{BuildingName} (阵营 {TeamId}) 被摧毁");
            // Q5：建筑被摧毁爆炸
            if (GetParent()?.GetParent() is Node2D parentNode)
                parentNode.AddChild(BattleEffect.BigExplosion(GlobalPosition));
            // Phase1: 建筑被摧毁时屏幕震动
            if (GetParent()?.GetParent() is Main shakeMain)
                shakeMain.ScreenShake(6f, 0.3f);
            // 阶段12-C：建筑被毁音效
            if (GetParent()?.GetParent() is Main mainNode)
                mainNode.PlayBuildingDestroyedSfx();
            // G5: 尤里卡 — 击毁者获得尤里卡进度
            if (GetParent()?.GetParent() is Main eurekaMain && _lastAttackerTeam >= 0)
                eurekaMain.OnEurekaDestroy(_lastAttackerTeam);
            // P0-1: 通知Main移除PathFinder障碍（在QueueFree前触发，此时位置仍有效）
            Destroyed?.Invoke(this);
            QueueFree();
        }
    }

    /// <summary>向所属阵营资金账户入账（由 Main 转发）。矿车卸货时调用。</summary>
    public void Deposit(float amount)
    {
        if (GetParent().GetParent() is Main main)
        {
            main.AddResourceForTeam(TeamId, (int)amount);
        }
    }

    // ---- G2 生产系统方法 ----

    /// <summary>排入生产订单。若生产空闲则立即开始，否则加入等待队列。
    /// U2: 支持Shift批量加入（count>1时连续排入）。</summary>
    public void EnqueueProduction(ProductionType type, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            if (!_currentProduction.HasValue)
            {
                _currentProduction = type;
                _productionDuration = GetProductionTime(type);
                _productionTimer = _productionDuration;
                QueueRedraw();
            }
            else if (_productionQueue.Count < MaxQueueSize - 1)
            {
                _productionQueue.Enqueue(type);
                QueueRedraw();
            }
            else
            {
                break; // 队列满了
            }
        }
    }

    /// <summary>U1: 取消最后一个排队中的生产订单。返回取消的类型，无则返回null。</summary>
    public ProductionType? CancelLastProduction()
    {
        if (_productionQueue.Count > 0)
        {
            // 取出队列中最后一个
            var list = _productionQueue.ToList();
            var last = list[^1];
            list.RemoveAt(list.Count - 1);
            _productionQueue.Clear();
            foreach (var item in list)
                _productionQueue.Enqueue(item);
            QueueRedraw();
            return last;
        }
        else if (_currentProduction.HasValue)
        {
            // 取消当前正在生产的
            var cancelled = _currentProduction.Value;
            _currentProduction = null;
            _productionTimer = 0f;
            QueueRedraw();
            return cancelled;
        }
        return null;
    }

    /// <summary>设置集结点。</summary>
    public void SetRallyPoint(Vector2 point)
    {
        RallyPoint = point;
        QueueRedraw();
    }

    /// <summary>获取等待队列中的生产类型快照（不含当前正在生产的）。</summary>
    public List<ProductionType> GetQueueSnapshot()
    {
        return _productionQueue.ToList();
    }

    // ==================== P0-2: 存档/读档 访问器 ====================

    /// <summary>获取生产状态的完整快照：(等待队列, 当前生产类型, 剩余时间, 总时间)。</summary>
    public (List<ProductionType> Queue, ProductionType? Current, float Timer, float Duration) GetProductionState()
    {
        return (_productionQueue.ToList(), _currentProduction, _productionTimer, _productionDuration);
    }

    /// <summary>获取集结点（无则返回null）。</summary>
    public Vector2? GetRallyPoint() => RallyPoint;

    /// <summary>获取建筑原始阵营（被占领前的归属，-1=从未被占）。</summary>
    public int GetOriginalTeamId() => _originalTeamId;

    /// <summary>获取当前正在占领本建筑的阵营ID（-1=无占领进行中）。</summary>
    public int GetCapturingTeamId() => _capturingTeamId;

    /// <summary>P0-2 读档：直接恢复生产队列与计时状态（绕过EnqueueProduction的扣费/校验逻辑）。</summary>
    public void RestoreProductionState(List<int> queue, int current, float timer, float duration)
    {
        _productionQueue.Clear();
        foreach (var id in queue) _productionQueue.Enqueue((ProductionType)id);
        _currentProduction = current >= 0 ? (ProductionType?)current : null;
        _productionTimer = timer;
        _productionDuration = duration;
    }

    /// <summary>P0-2 读档：恢复占领状态（用于叛变/缴获逻辑保持）。</summary>
    public void RestoreCaptureState(int originalTeamId, int capturingTeamId, float captureProgress)
    {
        _originalTeamId = originalTeamId;
        _capturingTeamId = capturingTeamId;
        CaptureProgress = Mathf.Clamp(captureProgress, 0f, SaveLoadSystem.CaptureProgressMax);
    }

    // ---- G4 建筑维修与出售 ----

    /// <summary>是否需要维修（血量不满）。</summary>
    public bool NeedsRepair => Health < MaxHealth;

    /// <summary>阶段12-A1 防御建筑：找到攻击范围内的最近敌方单位。无则返回 null。</summary>
    private Unit? FindNearestEnemyUnitInRange(float range)
    {
        Unit? nearest = null;
        float nearestDistSq = range * range;
        var unitsNode = GetParent()?.GetParent()?.GetNode<Node2D>("Units");
        if (unitsNode == null) return null;
        foreach (var c in unitsNode.GetChildren())
        {
            if (c is not Unit u || !IsInstanceValid(u)) continue;
            if (u.TeamId == TeamId) continue; // 同阵营跳过
            float dsq = GlobalPosition.DistanceSquaredTo(u.GlobalPosition);
            if (dsq < nearestDistSq)
            {
                nearestDistSq = dsq;
                nearest = u;
            }
        }
        return nearest;
    }

    /// <summary>执行维修：恢复满血。</summary>
    public void Repair()
    {
        Health = MaxHealth;
        if (_healthBar != null)
        {
            _healthBar.Value = Health;
            _healthBar.Visible = IsSelected;
            UpdateBldgHealthBarStyle();
        }
        QueueRedraw();
    }

    /// <summary>工程车持续修复：增加一定血量，但不超过 MaxHealth。不触发 SetRallyPoint 类逻辑。</summary>
    public void RepairByEngineer(float amount)
    {
        if (amount <= 0f || Health >= MaxHealth) return;
        Health = Mathf.Min(MaxHealth, Health + amount);
        if (_healthBar != null)
        {
            _healthBar.Value = Health;
            _healthBar.Visible = true;
            UpdateBldgHealthBarStyle();
        }
    }

    // G1: 科技效果方法
    private float _techHealthMul = 1f;
    private float _techPowerMul = 1f;

    /// <summary>G1: 应用科技生命值乘数。</summary>
    public void ApplyTechHealthMultiplier(float mul)
    {
        float baseMax = MaxHealth / _techHealthMul;
        _techHealthMul *= mul;
        MaxHealth = baseMax * _techHealthMul;
    }

    /// <summary>G1: 应用科技发电量乘数。</summary>
    public void ApplyTechPowerMultiplier(float mul)
    {
        _techPowerMul *= mul;
        PowerProvided = (int)(PowerProvided * mul);
    }

    /// <summary>工程车推进占领进度（5秒完成一次占领）。占领完成后建筑阵营转换。
    /// G8: 连锁占领速度加成 + 占领即获资源 + 缴获加速 + 叛变风险。</summary>
    public void CaptureTick(float dt, int capturingTeamId)
    {
        if (Health <= 0f) return;
        _captureTickThisFrame = true;
        _capturingTeamId = capturingTeamId;

        // G8: 连锁占领速度加成 — 80px内有己方已占领建筑时+50%
        float captureSpeed = 1f;
        if (GetParent()?.GetParent() is Main mainNode)
        {
            var buildings = mainNode.GetTeamBuildings(capturingTeamId);
            foreach (var b in buildings)
            {
                if (b != this && b._originalTeamId >= 0
                    && GlobalPosition.DistanceTo(b.GlobalPosition) <= CaptureBonus.ChainRange)
                {
                    captureSpeed = CaptureBonus.ChainCaptureSpeedMul;
                    break;
                }
            }
        }

        CaptureProgress += dt * captureSpeed / 5f;
        if (CaptureProgress >= 1f)
        {
            // G8: 保存原阵营
            _originalTeamId = TeamId;

            // 占领完成：转换阵营
            TeamId = capturingTeamId;
            CaptureProgress = 0f;
            _capturingTeamId = -1;
            _teamTint = Unit.GetTeamColor(TeamId).Lerp(Colors.White, 0.30f);
            _body.Modulate = _teamTint;
            GameLog.Debug($"{BuildingName} 被 Team {capturingTeamId} 占领!");

            // G8: 占领即获资源
            if (GetParent()?.GetParent() is Main capturedMain)
            {
                capturedMain.AddResourceForTeam(capturingTeamId, CaptureBonus.CaptureMoneyReward);
                GameLog.Debug($"[G8] 占领奖励: Team {capturingTeamId} +${CaptureBonus.CaptureMoneyReward}");
                capturedMain.ShowToast(capturingTeamId == 0
                    ? TrManager.Tr("building.captured", BuildingName, CaptureBonus.CaptureMoneyReward)
                    : "");
            }

            // G8: 缴获生产加速（60秒）
            _capturedProduceTimer = CaptureBonus.CapturedProduceDuration;

            // G8: 叛变风险（30秒）
            _defectionTimer = CaptureBonus.DefectionRiskDuration;
        }
        QueueRedraw();
    }

    /// <summary>获取生产所需时间（秒）。
    /// P1-2: 从data/buildings.json的productionTimes表加载，替代原switch-case。</summary>
    public static float GetProductionTime(ProductionType type) => GameData.GetProductionTime(type);

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // R5: 等距Y-Sort深度排序 — Y越大越靠前
        ZIndex = RenderLayer.UnitBase + (int)(GlobalPosition.Y / 2f);

        // Q5：受击闪白效果
        if (_hitFlashTimer > 0)
        {
            _hitFlashTimer -= dt;
            _body.Modulate = new Color(3f, 3f, 3f); // 过亮闪白
        }
        else
        {
            _body.Modulate = _teamTint; // 恢复队伍色调
        }

        // Phase1: 受损冒烟 — 血量低于60%开始冒烟，低于30%浓烟
        if (Health > 0f && Health < MaxHealth * 0.6f)
        {
            float damageRatio = Health / MaxHealth;
            // 受损越严重冒烟越密（0.4s间隔 → 0.15s间隔）
            float spawnInterval = damageRatio < 0.3f ? 0.15f : 0.4f;
            _smokeSpawnTimer -= dt;
            if (_smokeSpawnTimer <= 0f)
            {
                _smokeSpawnTimer = spawnInterval;
                // 随机选一个建筑顶部冒烟点
                float offX = (float)(_smokeRng.NextDouble() - 0.5) * 30;
                float offY = -20f - (float)_smokeRng.NextDouble() * 15;
                _smokeParticles.Add(new SmokeParticle
                {
                    Offset = new Vector2(offX, offY),
                    Velocity = new Vector2(
                        (float)(_smokeRng.NextDouble() - 0.5) * 8,
                        -15f - (float)_smokeRng.NextDouble() * 10),
                    Age = 0f,
                    Lifetime = 1.2f + (float)_smokeRng.NextDouble() * 0.6f,
                    StartRadius = damageRatio < 0.3f ? 8f : 5f,
                });
            }
        }
        // 更新烟雾粒子
        for (int i = _smokeParticles.Count - 1; i >= 0; i--)
        {
            var p = _smokeParticles[i];
            p.Age += dt;
            p.Offset += p.Velocity * dt;
            p.Velocity *= 0.96f; // 减速
            if (p.Age >= p.Lifetime)
                _smokeParticles.RemoveAt(i);
            else
                _smokeParticles[i] = p;
        }
        if (_smokeParticles.Count > 0) QueueRedraw();

        // 工程车占领衰减：无工程车附近时自动回退进度
        if (!_captureTickThisFrame && CaptureProgress > 0f)
        {
            CaptureProgress -= dt * 0.3f;
            if (CaptureProgress <= 0f)
            {
                CaptureProgress = 0f;
                _capturingTeamId = -1;
            }
        }
        _captureTickThisFrame = false; // 重置标志

        // G8: 占领强化计时
        if (_capturedProduceTimer > 0f)
        {
            _capturedProduceTimer -= dt;
            if (_capturedProduceTimer < 0f) _capturedProduceTimer = 0f;
        }
        if (_defectionTimer > 0f)
        {
            _defectionTimer -= dt;
            if (_defectionTimer <= 0f)
            {
                _defectionTimer = 0f;
                // G8: 叛变风险结束，安全
            }
            else
            {
                // G8: 叛变检查 — 每秒15%概率
                if (GD.Randf() < CaptureBonus.DefectionChance * dt)
                {
                    if (_originalTeamId >= 0 && _originalTeamId != TeamId)
                    {
                        GameLog.Debug($"[G8] 叛变! {BuildingName} 从 Team {TeamId} 叛变回 Team {_originalTeamId}!");
                        if (GetParent()?.GetParent() is Main capMain)
                            capMain.ShowToast(TeamId == 0
                                ? TrManager.Tr("building.defected", BuildingName, _originalTeamId)
                                : "");
                        TeamId = _originalTeamId;
                        _originalTeamId = -1;
                        _teamTint = Unit.GetTeamColor(TeamId).Lerp(Colors.White, 0.30f);
                        _body.Modulate = _teamTint;
                        _capturedProduceTimer = 0f;
                        _defectionTimer = 0f;
                    }
                }
            }
        }

        // ---- 阶段12-A1 防御建筑攻击逻辑 ----
        if (IsDefensive && AttackDamage > 0f && Health > 0f)
        {
            _turretAttackTimer -= dt;
            if (_turretAttackTimer <= 0f)
            {
                // G6: 邻接加成 — 防御塔射程乘数
                float effectiveRange = AttackRange;
                if (GetParent()?.GetParent() is Main turMain)
                {
                    float rangeMul = turMain.GetAdjacencyRangeMul(this);
                    effectiveRange = AttackRange * rangeMul;
                }
                var target = FindNearestEnemyUnitInRange(effectiveRange);
                if (target != null)
                {
                    _turretAttackTimer = AttackCooldown;
                    // G5: 记录攻击者阵营（尤里卡用）
                    target._lastAttackerTeam = TeamId;
                    target.TakeDamage(AttackDamage);
                    // 视觉效果：炮口闪光 + 拖尾弹道（挂在 effects/Units 父节点上）
                    if (GetParent() is Node2D parentNode)
                    {
                        parentNode.AddChild(BattleEffect.MuzzleFlash(GlobalPosition));
                        parentNode.AddChild(BattleEffect.Shell(GlobalPosition, target.GlobalPosition));
                    }
                    // 补强：防御建筑开火音效
                    if (GetParent()?.GetParent() is Main turretMain)
                        turretMain.PlayTurretFireSfx(Type);
                    // 炮塔转向敌人
                    var dir = target.GlobalPosition - GlobalPosition;
                    if (dir.LengthSquared() > 1f) _turretAngle = dir.Angle();
                }
                else
                {
                    _turretAttackTimer = 0.3f; // 无目标时短间隔再检查
                }
            }
            QueueRedraw();
        }

        // ---- 阶段12-A2 维修厂自动维修 ----
        if (IsRepairStation && Health > 0f)
        {
            // G6: 邻接加成 — 维修厂+车厂相邻时维修速度提升
            float repairMul = 1f;
            if (GetParent()?.GetParent() is Main repMain)
                repairMul = repMain.GetAdjacencyRepairMul(this);
            _repairTimer -= dt;
            if (_repairTimer <= 0f)
            {
                _repairTimer = 1f; // 每秒修复一次
                int repaired = 0;
                var unitsNode = GetParent()?.GetParent()?.GetNode<Node2D>("Units");
                if (unitsNode != null)
                {
                    foreach (var c in unitsNode.GetChildren())
                    {
                        if (c is Unit u && u.TeamId == TeamId && IsInstanceValid(u)
                            && GlobalPosition.DistanceTo(u.GlobalPosition) <= RepairRadius
                            && u.Health < u.MaxHealth)
                        {
                            u.RepairByRepairPad(RepairPerTick * repairMul);
                            repaired++;
                        }
                    }
                }
                // 仅在有维修行为时刷新重绘（显示维修光晕）
                if (repaired > 0) QueueRedraw();
            }
        }

        if (!_currentProduction.HasValue) { QueueRedraw(); return; }
        // G3+G4+G6+G8: 战术卡生产加速 + 电网分区离线减速 + 邻接加成生产速度 + 缴获加速
        float effectiveDt = dt;
        if (GetParent()?.GetParent() is Main mainNode)
        {
            // S1修复: G3战术卡生产速度加成（闪击战术+17.6%速度, 快速部署+25%速度）
            effectiveDt *= mainNode.GetCardProduceSpeedMul(TeamId);
            // G4: 电网分区离线减速
            if (!mainNode.IsBuildingPowered(this))
                effectiveDt *= PowerGrid.OfflineProduceMul;
            // G6: 邻接加成 — 兵营/车厂相邻时生产速度提升
            effectiveDt *= mainNode.GetAdjacencyProduceMul(this);
            // P0修复: Fac_NavalSupport（同盟军）: 船厂海军生产速度+20%
            if (Type == BuildingType.Shipyard)
                effectiveDt *= mainNode.GetTechNavalProduceMul(TeamId);
        }
        // G8: 缴获生产加速（占领后60秒+30%生产速度）
        if (IsCapturedProduceBoost)
            effectiveDt *= CaptureBonus.CapturedProduceSpeedMul;
        _productionTimer -= effectiveDt;
        if (_productionTimer <= 0f)
        {
            var type = _currentProduction.Value;
            _currentProduction = null;
            if (_productionQueue.Count > 0)
            {
                var next = _productionQueue.Dequeue();
                _currentProduction = next;
                _productionDuration = GetProductionTime(next);
                _productionTimer = _productionDuration;
            }
            if (GetParent()?.GetParent() is Main main)
            {
                main.OnUnitProduced(type, this);
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        // 脚下椭圆阴影（Area2D 节点不旋转，本地坐标系始终水平）
        // 向右下偏移模拟光源位于左上，椭圆中心在建筑脚下
        DrawSetTransform(new Vector2(8f, 38f), 0f, Vector2.One);
        DrawPolygon(Unit.GetBuildingShadowPoints(), new Color[] { Unit.GetShadowColor() });
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        // 生产进度条（建筑下方）
        if (_currentProduction.HasValue)
        {
            float progress = ProductionProgress;
            float barY = 42f;
            DrawRect(new Rect2(-30, barY, 60, 6), new Color(0.15f, 0.15f, 0.15f, 0.9f), true);
            if (progress > 0f)
                DrawRect(new Rect2(-30, barY, 60 * progress, 6), new Color(0.3f, 0.85f, 1f), true);
            // 队列计数底框
            if (_productionQueue.Count > 0)
            {
                DrawRect(new Rect2(30, barY - 1, 18, 8), new Color(0.1f, 0.1f, 0.1f, 0.85f), true);
            }
        }

        // 集结点标记（选中时显示）
        if (RallyPoint.HasValue && IsSelected)
        {
            var local = ToLocal(RallyPoint.Value);
            DrawLine(Vector2.Zero, local, new Color(1f, 0.85f, 0.2f, 0.5f), 1.5f);
            DrawArc(local, 9f, 0f, Mathf.Tau, 24, new Color(1f, 0.85f, 0.2f, 0.9f), 2f);
            DrawLine(local - new Vector2(5, 0), local + new Vector2(5, 0), new Color(1f, 0.85f, 0.2f, 0.9f), 1.5f);
            DrawLine(local - new Vector2(0, 5), local + new Vector2(0, 5), new Color(1f, 0.85f, 0.2f, 0.9f), 1.5f);
        }

        // 工程车占领进度条（建筑下方，生产条下面）
        if (CaptureProgress > 0f)
        {
            float capBarY = 42f + 8f;
            DrawRect(new Rect2(-30, capBarY, 60, 5), new Color(0.15f, 0.15f, 0.15f, 0.9f), true);
            var capColor = new Color(1f, 0.3f, 0.3f).Lerp(new Color(0.3f, 1f, 0.3f), CaptureProgress);
            DrawRect(new Rect2(-30, capBarY, 60 * CaptureProgress, 5), capColor, true);
        }

        // ---- 阶段12-A1 防御建筑：旋转炮塔 ----
        if (IsDefensive)
        {
            // 在建筑中心绘制指示性炮管（与 PNG 主体叠加），朝向 _turretAngle
            DrawSetTransform(Vector2.Zero, _turretAngle, Vector2.One);
            // 炮管：从中心向射程方向延伸的深色细矩形 + 末端黄铜色炮口
            DrawRect(new Rect2(4f, -3f, 28f, 6f), new Color(0.08f, 0.08f, 0.10f, 0.95f), true);
            DrawRect(new Rect2(30f, -4f, 5f, 8f), new Color(0.7f, 0.55f, 0.2f, 1f), true);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

            // 射程圈（仅选中时显示）
            if (IsSelected)
            {
                DrawArc(Vector2.Zero, AttackRange, 0f, Mathf.Tau, 48,
                    new Color(1f, 0.4f, 0.2f, 0.35f), 1.5f);
            }
        }

        // Phase1: 受损冒烟粒子（在建筑之上绘制）
        if (_smokeParticles.Count > 0)
        {
            foreach (var p in _smokeParticles)
            {
                float t = p.Age / p.Lifetime;
                float radius = p.StartRadius * (1f + t * 1.5f);
                float alpha = (1f - t) * 0.6f;
                // 暗灰色烟雾，低血量时偏黑
                float damageRatio = Health / MaxHealth;
                float gray = damageRatio < 0.3f ? 0.15f : 0.3f;
                DrawCircle(p.Offset, radius, new Color(gray, gray, gray, alpha));
            }
        }

        // ---- 阶段12-A2 维修厂：维修范围 + 修复光晕 ----
        if (IsRepairStation)
        {
            // 维修范围圈（仅选中时显示）
            if (IsSelected)
            {
                DrawArc(Vector2.Zero, RepairRadius, 0f, Mathf.Tau, 48,
                    new Color(0.3f, 1f, 0.6f, 0.3f), 1.5f);
            }
            // 维修工作中的绿色脉冲圈（每秒一次扩散）
            if (_repairTimer > 0.7f && _repairTimer <= 1f)
            {
                float pulseProgress = (1f - _repairTimer) / 0.3f; // 0→1
                float pulseRadius = pulseProgress * RepairRadius;
                DrawArc(Vector2.Zero, pulseRadius, 0f, Mathf.Tau, 48,
                    new Color(0.3f, 1f, 0.6f, (1f - pulseProgress) * 0.7f), 2f);
            }
        }
    }
}
