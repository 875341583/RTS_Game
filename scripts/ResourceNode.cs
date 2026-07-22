using System;
using Godot;

namespace RTSGame;

/// <summary>
/// 资源点类型枚举。
/// </summary>
public enum ResourceType
{
    Gold,           // 普通金矿（默认）
    RareMineral,    // 稀有矿（采集收益×2，紫蓝色晶体）
    OilField,       // 油田（占领后持续产钱，不可采集）
    LandVein,       // 陆地矿脉（散布广、储值低、数量多）
}

/// <summary>
/// 矿点：地图上散布的可采集资源点，矿车靠近后减速消耗储量。
/// 视觉参考红警2：金色矿块堆积（多个不规则菱形小方块），而非单一彩色圆形。
/// E5 扩展：支持4种资源类型（普通金矿/稀有矿/油田/陆地矿脉）。
/// </summary>
public partial class ResourceNode : Area2D
{
    [Export] public int InitialAmount { get; set; } = 1000;
    /// <summary>矿石外观颜色（保留导出属性兼容 .tscn 配置，实际渲染使用对应调色板）。</summary>
    [Export] public Color OreColor { get; set; } = new Color(1f, 0.78f, 0.28f, 1f);

    /// <summary>资源类型。</summary>
    public ResourceType ResourceType { get; set; } = ResourceType.Gold;
    /// <summary>稀有矿/陆地矿脉的收益倍率（油田不用此字段）。</summary>
    public float YieldMultiplier => ResourceType switch
    {
        ResourceType.RareMineral => 2.0f,
        ResourceType.LandVein => 0.6f,
        _ => 1.0f,
    };

    public int Amount { get; private set; }
    public bool IsDepleted => Amount <= 0;

    private Sprite2D _ore = null!;
    private Label _amountLabel = null!;

    // 金色阶梯调色板（参考红警2矿堆：高光饱和金 + 中金 + 深金 + 棕色描边 + 暗影）
    private static readonly Color COreSpec    = new(1.0f, 0.96f, 0.72f, 1f);
    private static readonly Color COreBright  = new(1.0f, 0.86f, 0.42f, 1f);
    private static readonly Color COreMid     = new(0.88f, 0.62f, 0.18f, 1f);
    private static readonly Color COreDark    = new(0.58f, 0.36f, 0.06f, 1f);
    private static readonly Color COreOutline = new(0.22f, 0.12f, 0.03f, 1f);

    // 稀有矿调色板（紫蓝色晶体）
    private static readonly Color CRareSpec    = new(0.85f, 0.82f, 1.0f, 1f);
    private static readonly Color CRareBright  = new(0.65f, 0.55f, 0.95f, 1f);
    private static readonly Color CRareMid     = new(0.45f, 0.30f, 0.80f, 1f);
    private static readonly Color CRareDark    = new(0.28f, 0.15f, 0.55f, 1f);
    private static readonly Color CRareOutline = new(0.10f, 0.05f, 0.25f, 1f);

    // 陆地矿脉调色板（淡铜色小矿堆）
    private static readonly Color CVeinSpec    = new(1.0f, 0.88f, 0.72f, 1f);
    private static readonly Color CVeinBright  = new(0.90f, 0.70f, 0.45f, 1f);
    private static readonly Color CVeinMid     = new(0.70f, 0.48f, 0.25f, 1f);
    private static readonly Color CVeinDark    = new(0.45f, 0.28f, 0.12f, 1f);
    private static readonly Color CVeinOutline = new(0.18f, 0.10f, 0.04f, 1f);

    // 油田调色板（暗绿+黑色油井）
    private static readonly Color COilDerrick = new(0.35f, 0.30f, 0.25f, 1f);
    private static readonly Color COilPool    = new(0.15f, 0.12f, 0.08f, 1f);
    private static readonly Color COilGlow    = new(0.4f, 0.7f, 0.3f, 0.8f);

    // ======== E5 油田占领系统 ========
    /// <summary>油田拥有者阵营（-1=中立）。</summary>
    public int OilOwner { get; private set; } = -1;
    /// <summary>油田占领进度（0-100）。</summary>
    private float _captureProgress;
    /// <summary>占领速度（每秒增长百分比，4秒占领=25/s）。</summary>
    private const float CaptureSpeed = 25f;
    /// <summary>油田每秒产出金额。</summary>
    private const float OilIncomePerSecond = 8f;
    /// <summary>占领方在场单位计数（0方/1方）。</summary>
    private int _blueCount, _redCount;
    private float _oilIncomeTimer;
    /// <summary>油田是否被占领（持续产钱中）。</summary>
    public bool IsOilCaptured => ResourceType == ResourceType.OilField && OilOwner >= 0;

    public override void _Ready()
    {
        Amount = InitialAmount;
        _ore = GetNode<Sprite2D>("Ore");
        _amountLabel = GetNode<Label>("AmountLabel");

        // 油田需要碰撞检测用于占领
        if (ResourceType == ResourceType.OilField)
        {
            CollisionLayer = 0;
            CollisionMask = 2; // monitor Units layer
            Monitoring = true;
            BodyEntered += OnBodyEntered;
            BodyExited += OnBodyExited;
        }

        RegenerateOreImage();
        UpdateLabel();
    }

    // ======== 油田占领逻辑 ========

    private void OnBodyEntered(Node body)
    {
        if (body is Unit u && u.AttackDamage > 0f)
        {
            if (u.TeamId == 0) _blueCount++;
            else _redCount++;
        }
    }

    private void OnBodyExited(Node body)
    {
        if (body is Unit u && u.AttackDamage > 0f)
        {
            if (u.TeamId == 0) _blueCount = Mathf.Max(0, _blueCount - 1);
            else _redCount = Mathf.Max(0, _redCount - 1);
        }
    }

    /// <summary>油田每帧处理占领和产钱逻辑。由 Main._Process 调用。</summary>
    public void ProcessOilField(float dt)
    {
        if (ResourceType != ResourceType.OilField) return;

        // 占领逻辑
        if (_blueCount > 0 && _redCount == 0 && OilOwner != 0)
        {
            _captureProgress += dt * CaptureSpeed;
            if (_captureProgress >= 100f)
            {
                OilOwner = 0;
                _captureProgress = 0f;
                GD.Print($"[OilField] Blue captured oil at {GlobalPosition}!");
                // 重绘视觉为蓝方占领
                RegenerateOreImage();
            }
        }
        else if (_redCount > 0 && _blueCount == 0 && OilOwner != 1)
        {
            _captureProgress += dt * CaptureSpeed;
            if (_captureProgress >= 100f)
            {
                OilOwner = 1;
                _captureProgress = 0f;
                GD.Print($"[OilField] Red captured oil at {GlobalPosition}!");
                RegenerateOreImage();
            }
        }
        else if (_blueCount == 0 && _redCount == 0)
        {
            _captureProgress = Mathf.Max(0f, _captureProgress - dt * CaptureSpeed * 0.5f);
        }

        // 产钱
        if (OilOwner >= 0 && GetParent()?.GetParent() is Main main2)
        {
            _oilIncomeTimer += dt;
            if (_oilIncomeTimer >= 1f)
            {
                _oilIncomeTimer -= 1f;
                main2.AddResourceForTeam(OilOwner, (int)OilIncomePerSecond);
            }
        }

        // 更新标签
        if (_captureProgress > 0f && OilOwner == -1)
        {
            if (_amountLabel != null)
                _amountLabel.Text = $"占领中 {(_blueCount > 0 ? "蓝" : "红")} {(int)_captureProgress}%";
        }
        else if (OilOwner >= 0)
        {
            if (_amountLabel != null)
                _amountLabel.Text = OilOwner == 0 ? "蓝方油田" : "红方油田";
        }
    }

    /// <summary>根据资源类型绘制图像。</summary>
    private void RegenerateOreImage()
    {
        switch (ResourceType)
        {
            case ResourceType.Gold:
                DrawGoldOre();
                break;
            case ResourceType.RareMineral:
                DrawRareMineral();
                break;
            case ResourceType.OilField:
                DrawOilField();
                break;
            case ResourceType.LandVein:
                DrawLandVein();
                break;
            default:
                DrawGoldOre();
                break;
        }
    }

    /// <summary>普通金矿：RA2风格金色菱形堆积。</summary>
    private void DrawGoldOre()
    {
        const int S = 72;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));

        int chunkCount = Mathf.Clamp(InitialAmount / 250, 5, 9);
        var rng = new Random((int)(GlobalPosition.X * 13.37f + GlobalPosition.Y * 7.31f));

        int center = S / 2;
        for (int i = 0; i < chunkCount; i++)
        {
            float ang, r;
            int halfSize;
            if (i == 0)
            {
                ang = 0f; r = 0f; halfSize = 5;
            }
            else
            {
                ang = (i - 1) * Mathf.Tau / Mathf.Max(1, chunkCount - 1)
                      + ((float)rng.NextDouble() - 0.5f) * 0.6f;
                r = 14f + (float)rng.NextDouble() * 6f;
                halfSize = 3 + (rng.Next(2) == 0 ? 1 : 0);
            }
            int cx = (int)(center + Mathf.Cos(ang) * r);
            int cy = (int)(center + Mathf.Sin(ang) * r);
            DrawChunkWithPalette(img, cx, cy, halfSize, COreSpec, COreBright, COreMid, COreDark, COreOutline);
        }

        _ore.Texture = ImageTexture.CreateFromImage(img);
    }

    /// <summary>稀有矿：紫蓝色晶体，形状更尖锐（六角形轮廓）。</summary>
    private void DrawRareMineral()
    {
        const int S = 72;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));

        int chunkCount = Mathf.Clamp(InitialAmount / 300, 3, 7);
        var rng = new Random((int)(GlobalPosition.X * 17.3f + GlobalPosition.Y * 11.7f));

        int center = S / 2;
        for (int i = 0; i < chunkCount; i++)
        {
            float ang, r;
            int halfSize;
            if (i == 0)
            {
                ang = 0f; r = 0f; halfSize = 6; // 中心大晶体
            }
            else
            {
                ang = (i - 1) * Mathf.Tau / Mathf.Max(1, chunkCount - 1)
                      + ((float)rng.NextDouble() - 0.5f) * 0.5f;
                r = 12f + (float)rng.NextDouble() * 8f;
                halfSize = 3 + (rng.Next(2) == 0 ? 1 : 0);
            }
            int cx = (int)(center + Mathf.Cos(ang) * r);
            int cy = (int)(center + Mathf.Sin(ang) * r);
            // 稀有矿用更尖锐的菱形+发光效果
            DrawChunkWithPalette(img, cx, cy, halfSize, CRareSpec, CRareBright, CRareMid, CRareDark, CRareOutline);
            // 外圈微光
            DrawGlow(img, cx, cy, halfSize + 3, new Color(0.5f, 0.4f, 0.9f, 0.15f));
        }

        _ore.Texture = ImageTexture.CreateFromImage(img);
    }

    /// <summary>陆地矿脉：淡铜色小矿堆，体量更小。</summary>
    private void DrawLandVein()
    {
        const int S = 52; // 比普通矿小
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));

        int chunkCount = Mathf.Clamp(InitialAmount / 200, 2, 4);
        var rng = new Random((int)(GlobalPosition.X * 23.1f + GlobalPosition.Y * 19.3f));

        int center = S / 2;
        for (int i = 0; i < chunkCount; i++)
        {
            float ang, r;
            int halfSize;
            if (i == 0)
            {
                ang = 0f; r = 0f; halfSize = 3;
            }
            else
            {
                ang = (i - 1) * Mathf.Tau / Mathf.Max(1, chunkCount - 1)
                      + ((float)rng.NextDouble() - 0.5f) * 0.8f;
                r = 8f + (float)rng.NextDouble() * 4f;
                halfSize = 2 + (rng.Next(2));
            }
            int cx = (int)(center + Mathf.Cos(ang) * r);
            int cy = (int)(center + Mathf.Sin(ang) * r);
            DrawChunkWithPalette(img, cx, cy, halfSize, CVeinSpec, CVeinBright, CVeinMid, CVeinDark, CVeinOutline);
        }

        _ore.Texture = ImageTexture.CreateFromImage(img);
    }

    /// <summary>油田：油井架+地面油池+阵营色标记。</summary>
    private void DrawOilField()
    {
        const int S = 80;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));

        int center = S / 2;

        // 地面油池（椭圆形暗色区域）
        for (int dy = -12; dy <= 12; dy++)
            for (int dx = -16; dx <= 16; dx++)
            {
                float t = (float)(dx * dx) / (16f * 16f) + (float)(dy * dy) / (12f * 12f);
                if (t > 1f) continue;
                int px = center + dx, py = center + 14 + dy;
                if (px < 0 || px >= S || py < 0 || py >= S) continue;
                float alpha = t < 0.7f ? 0.9f : (1f - t) / 0.3f * 0.9f;
                img.SetPixel(px, py, new Color(COilPool, alpha));
            }

        // 油井架（竖直T字形结构）
        for (int y = -20; y <= 8; y++)
        {
            int px = center, py = center + y;
            if (px >= 0 && px < S && py >= 0 && py < S)
                img.SetPixel(px, py, COilDerrick);
        }
        // 横梁
        for (int dx = -6; dx <= 6; dx++)
        {
            int px = center + dx, py = center - 20;
            if (px >= 0 && px < S && py >= 0 && py < S)
                img.SetPixel(px, py, COilDerrick);
        }
        // 钻头闪烁光点
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > 2) continue;
                int px = center + dx, py = center + 8 + dy;
                if (px >= 0 && px < S && py >= 0 && py < S)
                    img.SetPixel(px, py, COilGlow);
            }

        // 阵营色标记（占领后）
        Color teamColor = Colors.White;
        if (OilOwner == 0) teamColor = new Color(0.3f, 0.6f, 1.0f, 0.9f);
        else if (OilOwner == 1) teamColor = new Color(1.0f, 0.35f, 0.35f, 0.9f);
        if (OilOwner >= 0)
        {
            // 底座颜色环
            for (int a = 0; a < 32; a++)
            {
                float angle = a * Mathf.Tau / 32f;
                int cx = (int)(center + Mathf.Cos(angle) * 10f);
                int cy = (int)(center - 22 + Mathf.Sin(angle) * 5f);
                if (cx >= 0 && cx < S && cy >= 0 && cy < S)
                    img.SetPixel(cx, cy, teamColor);
            }
        }

        _ore.Texture = ImageTexture.CreateFromImage(img);
    }

    /// <summary>通用菱形矿块绘制，按距中心远近分层着色（5层阶梯）。</summary>
    private static void DrawChunkWithPalette(Image img, int cx, int cy, int halfSize,
        Color cSpec, Color cBright, Color cMid, Color cDark, Color cOutline)
    {
        int w = img.GetWidth();
        int h = img.GetHeight();
        for (int dy = -halfSize; dy <= halfSize; dy++)
        for (int dx = -halfSize; dx <= halfSize; dx++)
        {
            int ad = Math.Abs(dx) + Math.Abs(dy);
            if (ad > halfSize) continue;
            int px = cx + dx, py = cy + dy;
            if (px < 0 || px >= w || py < 0 || py >= h) continue;

            float t = halfSize > 0 ? (float)ad / halfSize : 0f;
            Color c;
            if (t >= 0.85f) c = cOutline;
            else if (t >= 0.65f) c = cDark;
            else if (t >= 0.40f) c = cMid;
            else if (t >= 0.15f) c = cBright;
            else c = cSpec;
            img.SetPixel(px, py, c);
        }
    }

    /// <summary>绘制微光圈（稀有矿的发光效果）。</summary>
    private static void DrawGlow(Image img, int cx, int cy, int radius, Color glowColor)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > radius || dist < radius - 2f) continue;
                int px = cx + dx, py = cy + dy;
                if (px < 0 || px >= w || py < 0 || py >= h) continue;
                // 叠加而非覆盖
                var existing = img.GetPixel(px, py);
                if (existing.A < 0.01f)
                    img.SetPixel(px, py, glowColor);
            }
    }

    public int Harvest(int count)
    {
        // 油田不可被矿车采集
        if (ResourceType == ResourceType.OilField) return 0;

        int actual = Mathf.Min(Amount, count);
        Amount -= actual;
        if (IsDepleted)
        {
            GD.Print($"[Resource] {ResourceType} depleted at {GlobalPosition}");
            var tween = CreateTween();
            tween.TweenProperty(_ore, "modulate:a", 0f, 0.6f);
            tween.Parallel().TweenProperty(_amountLabel, "modulate:a", 0f, 0.6f);
            tween.TweenCallback(Callable.From(() => QueueFree()));
        }
        else
        {
            UpdateLabel();
        }
        return actual;
    }

    private void UpdateLabel()
    {
        if (_amountLabel == null) return;

        switch (ResourceType)
        {
            case ResourceType.OilField:
                _amountLabel.Text = OilOwner switch
                {
                    0 => "蓝方油田",
                    1 => "红方油田",
                    _ => "油田",
                };
                break;
            case ResourceType.RareMineral:
                _amountLabel.Text = $"★{Amount}";
                break;
            case ResourceType.LandVein:
                _amountLabel.Text = $"·{Amount}";
                break;
            default:
                _amountLabel.Text = Amount.ToString();
                break;
        }
    }
}
