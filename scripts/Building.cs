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
public enum ProductionType { LightTank, HeavyTank, Artillery, RocketLauncher, MissileTank, Harvester }

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

    private static ImageTexture? _baseTex;
    private static ImageTexture? _powerTex;
    private static ImageTexture? _barracksTex;
    private static ImageTexture? _warTex;
    private static ImageTexture? _techTex;
    private static ImageTexture? _buildingRingTex;

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

        var teamColor = TeamId == 0
            ? new Color(0.3f, 0.55f, 1.0f)
            : new Color(1.0f, 0.35f, 0.35f);
        _body.Modulate = teamColor;
    }

    private static void EnsureTextures()
    {
        if (_baseTex != null) return;

        // 建造厂：大方块带四角
        var img = Image.CreateEmpty(96, 96, false, Image.Format.Rgba8);
        FillImage(img, 8, 88, 8, 88, Colors.White);
        FillImage(img, 14, 81, 14, 81, new Color(0.6f, 0.6f, 0.65f, 1f));
        FillImage(img, 32, 64, 32, 64, new Color(0.5f, 0.5f, 0.55f, 1f));
        foreach (var (cx, cy) in new[] { (16, 16), (72, 16), (16, 72), (72, 72) })
            FillImage(img, cx, cx + 8, cy, cy + 8, new Color(0.3f, 0.3f, 0.35f, 1f));
        _baseTex = ImageTexture.CreateFromImage(img);

        // 电站：圆形带闪电符号
        var pwr = Image.CreateEmpty(80, 80, false, Image.Format.Rgba8);
        for (int x = 0; x < 80; x++)
            for (int y = 0; y < 80; y++)
            {
                float dx = x - 40, dy = y - 40;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist < 32) pwr.SetPixel(x, y, Colors.White);
                else if (dist < 35) pwr.SetPixel(x, y, new Color(0.3f, 0.3f, 0.35f, 1f));
            }
        // 闪电符号
        for (int y = 20; y < 60; y++)
        {
            int x = 35 + (int)(8 * Mathf.Sin((y - 20) * 0.15f));
            if (x >= 0 && x < 80) pwr.SetPixel(x, y, new Color(1f, 0.9f, 0.2f, 1f));
            if (x + 1 < 80) pwr.SetPixel(x + 1, y, new Color(1f, 0.9f, 0.2f, 1f));
        }
        _powerTex = ImageTexture.CreateFromImage(pwr);

        // 兵营：小方块带十字
        var bar = Image.CreateEmpty(72, 72, false, Image.Format.Rgba8);
        FillImage(bar, 8, 64, 8, 64, Colors.White);
        FillImage(bar, 12, 60, 12, 60, new Color(0.55f, 0.55f, 0.6f, 1f));
        FillImage(bar, 33, 39, 10, 62, new Color(0.4f, 0.4f, 0.45f, 1f));
        FillImage(bar, 10, 62, 33, 39, new Color(0.4f, 0.4f, 0.45f, 1f));
        _barracksTex = ImageTexture.CreateFromImage(bar);

        // 战车工厂：宽矩形带传送带
        var war = Image.CreateEmpty(96, 72, false, Image.Format.Rgba8);
        FillImage(war, 4, 92, 4, 68, Colors.White);
        FillImage(war, 8, 88, 8, 64, new Color(0.5f, 0.5f, 0.55f, 1f));
        // 传送带条纹
        for (int x = 12; x < 84; x += 12)
            FillImage(war, x, x + 8, 20, 52, new Color(0.35f, 0.35f, 0.4f, 1f));
        _warTex = ImageTexture.CreateFromImage(war);

        // 科技中心：六边形带卫星天线
        var tech = Image.CreateEmpty(96, 96, false, Image.Format.Rgba8);
        for (int x = 0; x < 96; x++)
            for (int y = 0; y < 96; y++)
            {
                float dx = Mathf.Abs(x - 48), dy = Mathf.Abs(y - 48);
                if (dx / 40f + dy / 46f < 1.0f)
                    tech.SetPixel(x, y, Colors.White);
            }
        for (int x = 0; x < 96; x++)
            for (int y = 0; y < 96; y++)
            {
                float dx = Mathf.Abs(x - 48), dy = Mathf.Abs(y - 48);
                if (dx / 34f + dy / 40f < 1.0f)
                    tech.SetPixel(x, y, new Color(0.3f, 0.6f, 0.5f, 1f));
                else if (dx / 37f + dy / 43f < 1.0f)
                    tech.SetPixel(x, y, new Color(0.45f, 0.7f, 0.6f, 1f));
            }
        FillImage(tech, 46, 50, 10, 18, new Color(0.2f, 0.2f, 0.25f, 1f));
        FillImage(tech, 43, 53, 18, 22, new Color(0.2f, 0.2f, 0.25f, 1f));
        for (int x = 42; x < 54; x++)
            for (int y = 42; y < 54; y++)
            {
                float dx = x - 48, dy = y - 48;
                if (dx * dx + dy * dy < 36)
                    tech.SetPixel(x, y, new Color(0.2f, 1f, 0.8f, 1f));
            }
        _techTex = ImageTexture.CreateFromImage(tech);

        // 通用选择环
        var ring = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8);
        for (float a = 0; a < Mathf.Tau; a += 0.02f)
        {
            int cx = (int)(64 + 60 * Mathf.Cos(a));
            int cy = (int)(64 + 60 * Mathf.Sin(a));
            if (cx >= 0 && cx < 128 && cy >= 0 && cy < 128)
                ring.SetPixel(cx, cy, Colors.Lime);
            if (cx + 1 < 128 && cy >= 0 && cy < 128) ring.SetPixel(cx + 1, cy, Colors.Lime);
        }
        _buildingRingTex = ImageTexture.CreateFromImage(ring);
    }

    private static void FillImage(Image img, int x0, int x1, int y0, int y1, Color c)
    {
        for (int y = y0; y < y1 && y < img.GetHeight(); y++)
            for (int x = x0; x < x1 && x < img.GetWidth(); x++)
                img.SetPixel(x, y, c);
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

    /// <summary>获取生产所需时间（秒）。</summary>
    public static float GetProductionTime(ProductionType type) => type switch
    {
        ProductionType.LightTank => 3f,
        ProductionType.HeavyTank => 6f,
        ProductionType.Artillery => 5f,
        ProductionType.RocketLauncher => 8f,
        ProductionType.MissileTank => 10f,
        ProductionType.Harvester => 5f,
        _ => 3f
    };

    public override void _Process(double delta)
    {
        if (!_currentProduction.HasValue) return;
        _productionTimer -= (float)delta;
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
    }
}
