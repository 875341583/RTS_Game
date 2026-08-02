using Godot;

namespace RTSGame;

/// <summary>
/// 程序化暗角覆盖层（纯_Draw绘制，兼容ANGLE/软件渲染）。
/// 通过绘制同心圆环实现径向渐变暗角效果，不依赖纹理alpha通道。
/// 之前的纹理方式在ANGLE下alpha通道失效导致大面积纯黑遮挡，故改用_Draw。
/// </summary>
public partial class VignetteOverlay : Control
{
    private const float ViewportW = 1920f;
    private const float ViewportH = 1080f;
    private const float MaxAlpha = 0.5f;
    private const int BandCount = 48;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = new Vector2(ViewportW, ViewportH);
        ClipContents = false;
    }

    public override void _Draw()
    {
        var center = new Vector2(ViewportW * 0.5f, ViewportH * 0.5f);
        float halfW = ViewportW * 0.5f;
        float halfH = ViewportH * 0.5f;
        float maxDist = Mathf.Sqrt(halfW * halfW + halfH * halfH);
        float innerR = maxDist * 0.42f;

        float bandWidth = (maxDist - innerR) / BandCount;

        // 从内到外绘制同心圆环，alpha逐渐增大
        for (int i = 0; i < BandCount; i++)
        {
            float r = innerR + (i + 1) * bandWidth;
            float t = (float)(i + 1) / BandCount;
            // smoothstep 让过渡更柔和
            t = t * t * (3f - 2f * t);
            float alpha = t * MaxAlpha;

            DrawArc(center, r, 0f, Mathf.Tau, 80,
                    new Color(0, 0, 0, alpha), bandWidth + 1.5f);
        }

        GameLog.Debug("[Visual] Vignette _Draw complete (48 concentric rings)");
    }
}
