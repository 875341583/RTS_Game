using System;
using Godot;

namespace RTSGame;

/// <summary>
/// 矿点：地图上散布的可采集资源点，矿车靠近后减速消耗储量。
/// 视觉参考红警2：金色矿块堆积（多个不规则菱形小方块），而非单一彩色圆形。
/// </summary>
public partial class ResourceNode : Area2D
{
    [Export] public int InitialAmount { get; set; } = 1000;
    /// <summary>矿石外观颜色（保留导出属性兼容 .tscn 配置，实际渲染使用金色阶梯）。</summary>
    [Export] public Color OreColor { get; set; } = new Color(1f, 0.78f, 0.28f, 1f);

    public int Amount { get; private set; }
    public bool IsDepleted => Amount <= 0;

    private Sprite2D _ore = null!;
    private Label _amountLabel = null!;

    // 金色阶梯调色板（参考红警2矿堆：高光饱和金 + 中金 + 深金 + 棕色描边 + 暗影）
    private static readonly Color COreSpec    = new(1.0f, 0.96f, 0.72f, 1f); // 顶端白色高光（金字塔尖）
    private static readonly Color COreBright  = new(1.0f, 0.86f, 0.42f, 1f); // 亮金高光层
    private static readonly Color COreMid     = new(0.88f, 0.62f, 0.18f, 1f); // 中色金
    private static readonly Color COreDark    = new(0.58f, 0.36f, 0.06f, 1f); // 暗色金/阴影边
    private static readonly Color COreOutline = new(0.22f, 0.12f, 0.03f, 1f); // 黑棕描边

    public override void _Ready()
    {
        Amount = InitialAmount;
        _ore = GetNode<Sprite2D>("Ore");
        _amountLabel = GetNode<Label>("AmountLabel");
        RegenerateOreImage();
        UpdateLabel();
    }

    /// <summary>根据当前储量绘制矿堆图像：中心1块大矿 + 周围环绕小矿，形成 RA2 风格堆积造型。</summary>
    private void RegenerateOreImage()
    {
        const int S = 72;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));

        // 矿块数量随初始储量缩放：500→5块, 2000→9块
        int chunkCount = Mathf.Clamp(InitialAmount / 250, 5, 9);
        // 用 GlobalPosition 作种子，让每个矿点形状略有差异但稳定
        var rng = new Random((int)(GlobalPosition.X * 13.37f + GlobalPosition.Y * 7.31f));

        int center = S / 2;
        for (int i = 0; i < chunkCount; i++)
        {
            // 第0块放中心且最大；其余块环绕分布
            float ang, r;
            int halfSize;
            if (i == 0)
            {
                ang = 0f;
                r = 0f;
                halfSize = 5; // 中心 11×11 菱形大矿
            }
            else
            {
                // 让环绕块在圆周均匀分布 + 少量随机扰动
                ang = (i - 1) * Mathf.Tau / Mathf.Max(1, chunkCount - 1)
                      + ((float)rng.NextDouble() - 0.5f) * 0.6f;
                r = 14f + (float)rng.NextDouble() * 6f;
                halfSize = 3 + (rng.Next(2) == 0 ? 1 : 0); // 7×7 或 9×9 小矿
            }
            int cx = (int)(center + Mathf.Cos(ang) * r);
            int cy = (int)(center + Mathf.Sin(ang) * r);
            DrawOreChunk(img, cx, cy, halfSize);
        }

        _ore.Texture = ImageTexture.CreateFromImage(img);
    }

    /// <summary>绘制单个菱形金字塔矿块（Manhattan 距离 ≤ halfSize），按距中心远近分层着色。</summary>
    private static void DrawOreChunk(Image img, int cx, int cy, int halfSize)
    {
        int w = img.GetWidth();
        int h = img.GetHeight();
        for (int dy = -halfSize; dy <= halfSize; dy++)
        for (int dx = -halfSize; dx <= halfSize; dx++)
        {
            int ad = Math.Abs(dx) + Math.Abs(dy);
            if (ad > halfSize) continue; // 菱形裁剪
            int px = cx + dx, py = cy + dy;
            if (px < 0 || px >= w || py < 0 || py >= h) continue;

            // 按 ad/halfSize 比例分层：0=顶端高光, 1=描边
            float t = halfSize > 0 ? (float)ad / halfSize : 0f;
            Color c;
            if (t >= 0.85f) c = COreOutline;       // 外环描边（黑棕）
            else if (t >= 0.65f) c = COreDark;     // 暗金阴影
            else if (t >= 0.40f) c = COreMid;      // 中金
            else if (t >= 0.15f) c = COreBright;   // 亮金高光
            else c = COreSpec;                     // 顶端白色高光
            img.SetPixel(px, py, c);
        }
    }

    public int Harvest(int count)
    {
        int actual = Mathf.Min(Amount, count);
        Amount -= actual;
        if (IsDepleted)
        {
            GD.Print($"Ore depleted at {GlobalPosition}");
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
        if (_amountLabel != null)
            _amountLabel.Text = Amount.ToString();
    }
}
