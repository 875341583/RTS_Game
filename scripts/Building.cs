using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 建筑类型枚举。
/// </summary>
public enum BuildingType { Base, PowerPlant, Barracks, WarFactory, TechCenter }

/// <summary>
/// 生产项类型：可由建筑排产的战斗单位或矿车。
/// </summary>
public enum ProductionType { LightTank, HeavyTank, Artillery, RocketLauncher, MissileTank, Harvester, Infantry, AntiAir, Engineer }

/// <summary>
/// 建筑/基地：可被选中、可被攻击。不同类型解锁不同单位生产。
/// </summary>
public partial class Building : Area2D
{
    [Export] public float MaxHealth { get; set; } = 1000f;
    [Export] public string BuildingName { get; set; } = "建造厂";

    public float Health { get; private set; }
    public bool IsSelected { get; private set; }
    public int TeamId { get; set; } = 0;
    public BuildingType Type { get; set; } = BuildingType.Base;
    public int PowerProvided { get; set; } = 0;
    public int PowerConsumed { get; set; } = 0;

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

    private Sprite2D _body = null!;
    private Sprite2D _selectionRing = null!;
    private ProgressBar _healthBar = null!;
    private float _hitFlashTimer;

    private static Texture2D? _baseTex;
    private static Texture2D? _powerTex;
    private static Texture2D? _barracksTex;
    private static Texture2D? _warTex;
    private static Texture2D? _techTex;
    private static Texture2D? _buildingRingTex;
    private Color _teamTint = Colors.White;

    // ---- 工程车占领系统 ----
    /// <summary>占领进度 0~1（1=占领完成）。</summary>
    public float CaptureProgress { get; private set; } = 0f;
    private int _capturingTeamId = -1;
    private bool _captureTickThisFrame = false;

    /// <summary>按建筑类型初始化属性。必须在 _Ready 之前调用。</summary>
    public void InitAsType(BuildingType type)
    {
        Type = type;
        switch (type)
        {
            case BuildingType.Base:
                BuildingName = "建造厂";
                MaxHealth = 1000f;
                PowerConsumed = 50;
                break;
            case BuildingType.PowerPlant:
                BuildingName = "电站";
                MaxHealth = 300f;
                PowerProvided = 100;
                break;
            case BuildingType.Barracks:
                BuildingName = "兵营";
                MaxHealth = 500f;
                PowerConsumed = 30;
                break;
            case BuildingType.WarFactory:
                BuildingName = "战车工厂";
                MaxHealth = 700f;
                PowerConsumed = 50;
                break;
            case BuildingType.TechCenter:
                BuildingName = "科技中心";
                MaxHealth = 600f;
                PowerConsumed = 80;
                break;
        }
    }

    public override void _Ready()
    {
        Health = MaxHealth;
        _body = GetNode<Sprite2D>("Body");
        _selectionRing = GetNode<Sprite2D>("SelectionRing");
        _healthBar = GetNode<ProgressBar>("HealthBar");

        EnsureTextures();
        _body.Texture = Type switch
        {
            BuildingType.PowerPlant => _powerTex!,
            BuildingType.Barracks => _barracksTex!,
            BuildingType.WarFactory => _warTex!,
            BuildingType.TechCenter => _techTex!,
            _ => _baseTex!
        };
        _selectionRing.Texture = _buildingRingTex;
        _selectionRing.Visible = false;
        _healthBar.MaxValue = MaxHealth;
        _healthBar.Value = Health;
        _healthBar.Visible = false;

        // 8阵营色染色：向白色混合30%，让阵营色占主体（75%），8色强烈区分同时保留建筑手绘明暗细节
        _teamTint = Unit.GetTeamColor(TeamId).Lerp(Colors.White, 0.30f);
        _body.Modulate = _teamTint;

        // v3 放大显示尺寸：PNG 128x128 在 zoom=1.0 下视觉太小看不清砖缝/铆钉/五星等细节
        // 1.4x 让建筑显示为 ~180px，纹理清晰可辨；cell 90x90 间距足够不严重重叠
        _body.Scale = new Vector2(1.4f, 1.4f);
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

        // 通用选择环
        var ring = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8);
        ring.Fill(Colors.Transparent);
        for (float a = 0; a < Mathf.Tau; a += 0.02f)
        {
            int cx = (int)(64 + 60 * Mathf.Cos(a));
            int cy = (int)(64 + 60 * Mathf.Sin(a));
            if (cx >= 0 && cx < 128 && cy >= 0 && cy < 128)
            {
                ring.SetPixel(cx, cy, Colors.Lime);
                if (cx + 1 < 128 && cy >= 0 && cy < 128) ring.SetPixel(cx + 1, cy, Colors.Lime);
            }
        }
        _buildingRingTex = ImageTexture.CreateFromImage(ring);
    }

    private static Texture2D LoadTexture(string path)
    {
        var tex = GD.Load<Texture2D>(path);
        if (tex == null)
        {
            GD.PrintErr($"[Building] Failed to load texture: {path}");
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

    public void TakeDamage(float damage)
    {
        Health -= damage;
        _hitFlashTimer = 0.1f; // Q5：受击闪白
        if (_healthBar != null)
        {
            _healthBar.Value = Mathf.Max(0, Health);
            _healthBar.Visible = true;
        }
        // G4+：通知己方单位回防
        if (GetParent()?.GetParent() is Main main)
            main.OnBuildingAttacked(this);
        if (Health <= 0)
        {
            GD.Print($"{BuildingName} (Team {TeamId}) destroyed!");
            // Q5：建筑被摧毁爆炸
            if (GetParent()?.GetParent() is Node2D parentNode)
                parentNode.AddChild(BattleEffect.BigExplosion(GlobalPosition));
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

    /// <summary>排入生产订单。若生产空闲则立即开始，否则加入等待队列。</summary>
    public void EnqueueProduction(ProductionType type)
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
    }

    /// <summary>设置集结点。</summary>
    public void SetRallyPoint(Vector2 point)
    {
        RallyPoint = point;
        QueueRedraw();
    }

    // ---- G4 建筑维修与出售 ----

    /// <summary>是否需要维修（血量不满）。</summary>
    public bool NeedsRepair => Health < MaxHealth;

    /// <summary>执行维修：恢复满血。</summary>
    public void Repair()
    {
        Health = MaxHealth;
        if (_healthBar != null)
        {
            _healthBar.Value = Health;
            _healthBar.Visible = IsSelected;
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
        }
    }

    /// <summary>工程车推进占领进度（5秒完成一次占领）。占领完成后建筑阵营转换。</summary>
    public void CaptureTick(float dt, int capturingTeamId)
    {
        if (Health <= 0f) return;
        _captureTickThisFrame = true;
        _capturingTeamId = capturingTeamId;
        CaptureProgress += dt / 5f;
        if (CaptureProgress >= 1f)
        {
            // 占领完成：转换阵营
            TeamId = capturingTeamId;
            CaptureProgress = 0f;
            _capturingTeamId = -1;
            _teamTint = Unit.GetTeamColor(TeamId).Lerp(Colors.White, 0.30f);
            _body.Modulate = _teamTint;
            GD.Print($"{BuildingName} 被 Team {capturingTeamId} 占领!");
        }
        QueueRedraw();
    }

    /// <summary>获取生产所需时间（秒）。</summary>
    public static float GetProductionTime(ProductionType type) => type switch
    {
        ProductionType.LightTank => 3f,
        ProductionType.HeavyTank => 6f,
        ProductionType.Artillery => 5f,
        ProductionType.RocketLauncher => 8f,
        ProductionType.MissileTank => 10f,
        ProductionType.Harvester => 5f,
        ProductionType.Infantry => 2f,
        ProductionType.AntiAir => 3f,
        ProductionType.Engineer => 4f,
        _ => 3f
    };

    public override void _Process(double delta)
    {
        float dt = (float)delta;
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

        if (!_currentProduction.HasValue) { QueueRedraw(); return; }
        _productionTimer -= dt;
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
    }
}
