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

        // Q4：升级建筑纹理——屋顶/墙壁/门窗/结构细节
        _baseTex = CreateBaseTexture();
        _powerTex = CreatePowerPlantTexture();
        _barracksTex = CreateBarracksTexture();
        _warTex = CreateWarFactoryTexture();
        _techTex = CreateTechCenterTexture();

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

    /// <summary>建造厂：大型工业建筑+车间门+起重机+窗户+四角支柱。</summary>
    private static ImageTexture CreateBaseTexture()
    {
        var img = Image.CreateEmpty(96, 96, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        // 外框/地基
        FillImage(img, 8, 88, 8, 88, new Color(0.6f, 0.6f, 0.65f));
        // 主体建筑
        FillImage(img, 12, 84, 12, 84, new Color(0.45f, 0.45f, 0.48f));
        // 屋顶区域
        FillImage(img, 12, 84, 12, 24, new Color(0.35f, 0.35f, 0.4f));

        // 车间大门（暗色开口）
        FillImage(img, 36, 64, 58, 84, new Color(0.12f, 0.12f, 0.15f));
        // 门框
        FillImage(img, 34, 66, 56, 86, new Color(0.3f, 0.3f, 0.33f));
        FillImage(img, 36, 64, 58, 84, new Color(0.12f, 0.12f, 0.15f));

        // 窗户排（4扇）
        for (int i = 0; i < 4; i++)
        {
            int wx = 18 + i * 15;
            FillImage(img, wx, wx + 8, 18, 30, new Color(0.5f, 0.7f, 0.9f)); // 窗户高光（带队伍色）
        }

        // 起重机（右侧）
        FillImage(img, 78, 88, 8, 14, new Color(0.28f, 0.28f, 0.32f)); // 底座
        FillImage(img, 84, 88, 8, 40, new Color(0.28f, 0.28f, 0.32f)); // 立柱
        FillImage(img, 56, 88, 36, 40, new Color(0.28f, 0.28f, 0.32f)); // 吊臂
        FillImage(img, 56, 60, 38, 42, new Color(0.22f, 0.22f, 0.25f)); // 吊钩区

        // 四角支柱
        foreach (var (cx, cy) in new[] { (10, 10), (82, 10), (10, 82), (82, 82) })
            FillImage(img, cx, cx + 6, cy, cy + 6, new Color(0.32f, 0.32f, 0.36f));

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>电站：矩形建筑+冷却塔+烟囱+能量发光。</summary>
    private static ImageTexture CreatePowerPlantTexture()
    {
        var img = Image.CreateEmpty(80, 80, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        // 主建筑
        FillImage(img, 8, 72, 8, 72, new Color(0.5f, 0.5f, 0.55f));
        FillImage(img, 12, 68, 12, 68, new Color(0.42f, 0.42f, 0.46f));

        // 冷却塔（右半）
        for (int x = 0; x < 80; x++)
            for (int y = 0; y < 80; y++)
            {
                float dx = x - 56, dy = y - 38;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist < 18) img.SetPixel(x, y, new Color(0.55f, 0.55f, 0.6f));
                else if (dist < 21) img.SetPixel(x, y, new Color(0.3f, 0.3f, 0.35f));
            }
        // 冷却塔发光
        for (int x = 0; x < 80; x++)
            for (int y = 0; y < 80; y++)
            {
                float dx = x - 56, dy = y - 38;
                if (dx * dx + dy * dy < 144)
                    img.SetPixel(x, y, new Color(0.8f, 0.9f, 1f)); // 明亮区（团队色透出）
            }

        // 烟囱
        FillImage(img, 18, 26, 10, 16, new Color(0.3f, 0.3f, 0.35f));
        FillImage(img, 20, 24, 12, 68, new Color(0.25f, 0.25f, 0.3f));
        // 烟囱口
        FillImage(img, 19, 27, 8, 12, new Color(0.2f, 0.2f, 0.25f));

        // 输电管道
        FillImage(img, 28, 40, 28, 34, new Color(0.35f, 0.35f, 0.4f));

        // 窗户
        FillImage(img, 34, 52, 16, 24, new Color(0.5f, 0.7f, 0.9f));
        FillImage(img, 34, 52, 28, 36, new Color(0.5f, 0.7f, 0.9f));

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>兵营：军事建筑+窗户排+入口门+旗帜。</summary>
    private static ImageTexture CreateBarracksTexture()
    {
        var img = Image.CreateEmpty(80, 72, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        // 主建筑
        FillImage(img, 6, 74, 6, 66, new Color(0.55f, 0.55f, 0.6f));
        FillImage(img, 10, 70, 10, 62, new Color(0.45f, 0.45f, 0.5f));

        // 屋顶线条
        FillImage(img, 10, 70, 10, 16, new Color(0.38f, 0.38f, 0.42f));

        // 窗户排（5扇）
        for (int i = 0; i < 5; i++)
        {
            int wx = 14 + i * 11;
            FillImage(img, wx, wx + 7, 20, 32, new Color(0.5f, 0.7f, 0.9f)); // 高光窗
        }

        // 入口门
        FillImage(img, 32, 46, 48, 62, new Color(0.12f, 0.12f, 0.15f));
        // 门框
        FillImage(img, 30, 48, 46, 64, new Color(0.3f, 0.3f, 0.35f));
        FillImage(img, 32, 46, 48, 62, new Color(0.12f, 0.12f, 0.15f));

        // 旗杆
        FillImage(img, 62, 64, 6, 30, new Color(0.25f, 0.25f, 0.3f));
        // 旗帜（团队色高光）
        FillImage(img, 64, 74, 6, 12, new Color(0.8f, 0.85f, 0.9f));

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>战车工厂：大型厂房+双车间门+传送带条纹+吊车轨道。</summary>
    private static ImageTexture CreateWarFactoryTexture()
    {
        var img = Image.CreateEmpty(96, 80, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        // 主建筑
        FillImage(img, 4, 92, 4, 76, new Color(0.5f, 0.5f, 0.55f));
        FillImage(img, 8, 88, 8, 72, new Color(0.42f, 0.42f, 0.47f));

        // 屋顶+吊车轨道
        FillImage(img, 8, 88, 8, 18, new Color(0.35f, 0.35f, 0.4f));
        FillImage(img, 20, 76, 12, 14, new Color(0.28f, 0.28f, 0.32f)); // 轨道

        // 双车间门
        FillImage(img, 12, 36, 48, 72, new Color(0.12f, 0.12f, 0.15f)); // 左门
        FillImage(img, 10, 38, 46, 74, new Color(0.3f, 0.3f, 0.35f)); // 左门框
        FillImage(img, 12, 36, 48, 72, new Color(0.12f, 0.12f, 0.15f));
        FillImage(img, 54, 80, 48, 72, new Color(0.12f, 0.12f, 0.15f)); // 右门
        FillImage(img, 52, 82, 46, 74, new Color(0.3f, 0.3f, 0.35f)); // 右门框
        FillImage(img, 54, 80, 48, 72, new Color(0.12f, 0.12f, 0.15f));

        // 传送带条纹（门内）
        for (int x = 14; x < 34; x += 5)
            FillImage(img, x, x + 3, 56, 68, new Color(0.2f, 0.2f, 0.25f));
        for (int x = 56; x < 78; x += 5)
            FillImage(img, x, x + 3, 56, 68, new Color(0.2f, 0.2f, 0.25f));

        // 烟囱
        FillImage(img, 84, 90, 14, 20, new Color(0.3f, 0.3f, 0.35f));
        FillImage(img, 86, 88, 12, 40, new Color(0.25f, 0.25f, 0.3f));

        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>科技中心：六边形建筑+卫星天线+发光核心+天线阵列。</summary>
    private static ImageTexture CreateTechCenterTexture()
    {
        var img = Image.CreateEmpty(96, 96, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        // 外部轮廓（六边形）
        for (int x = 0; x < 96; x++)
            for (int y = 0; y < 96; y++)
            {
                float dx = Mathf.Abs(x - 48), dy = Mathf.Abs(y - 48);
                if (dx / 38f + dy / 44f < 1.0f)
                    img.SetPixel(x, y, new Color(0.55f, 0.55f, 0.6f));
            }
        // 内部结构
        for (int x = 0; x < 96; x++)
            for (int y = 0; y < 96; y++)
            {
                float dx = Mathf.Abs(x - 48), dy = Mathf.Abs(y - 48);
                if (dx / 32f + dy / 38f < 1.0f)
                    img.SetPixel(x, y, new Color(0.4f, 0.4f, 0.45f));
            }

        // 发光核心
        for (int x = 0; x < 96; x++)
            for (int y = 0; y < 96; y++)
            {
                float dx = x - 48, dy = y - 48;
                if (dx * dx + dy * dy < 196) // 半径14
                    img.SetPixel(x, y, new Color(0.7f, 0.9f, 1f)); // 明亮核心（团队色高光）
                else if (dx * dx + dy * dy < 400) // 半径20
                    img.SetPixel(x, y, new Color(0.35f, 0.65f, 0.55f)); // 科技光环
            }

        // 卫星天线底座
        FillImage(img, 44, 52, 10, 18, new Color(0.25f, 0.25f, 0.3f));
        // 天线杆
        FillImage(img, 47, 49, 4, 14, new Color(0.25f, 0.25f, 0.3f));
        // 天线碟
        for (int x = 38; x < 58; x++)
            for (int y = 6; y < 14; y++)
            {
                float dx = x - 48, dy = y - 10;
                if (dx * dx / 100f + dy * dy / 16f < 1f)
                    img.SetPixel(x, y, new Color(0.3f, 0.5f, 0.45f));
            }

        // 侧翼天线
        FillImage(img, 18, 22, 30, 60, new Color(0.25f, 0.25f, 0.3f));
        FillImage(img, 74, 78, 30, 60, new Color(0.25f, 0.25f, 0.3f));
        // 天线尖端
        FillImage(img, 16, 24, 28, 32, new Color(0.7f, 0.85f, 0.95f));
        FillImage(img, 72, 80, 28, 32, new Color(0.7f, 0.85f, 0.95f));

        return ImageTexture.CreateFromImage(img);
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
