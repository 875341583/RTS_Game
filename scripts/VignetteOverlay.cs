using System;
using Godot;

namespace RTSGame;

/// <summary>
/// 程序化暗角覆盖层（不依赖着色器，兼容ANGLE/软件渲染）。
/// 在_Ready中生成一张径向渐变暗角纹理，运行时仅显示一个Sprite2D。
/// 效果：屏幕中心透明，边缘逐渐变暗，类似RA2军事氛围暗角。
/// </summary>
public partial class VignetteOverlay : Control
{
    private Sprite2D? _vignetteSprite;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        // 延迟一帧生成纹理（确保已获得正确尺寸）
        CallDeferred(nameof(CreateVignette));
    }

    private void CreateVignette()
    {
        // 使用固定的视口尺寸（project.godot设置为1920x1080）
        int w = 1920;
        int h = 1080;

        // 生成暗角纹理：中心透明，四角深黑
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0)); // 完全透明

        float cx = w * 0.5f;
        float cy = h * 0.5f;
        float maxDist = Mathf.Sqrt(cx * cx + cy * cy);
        // 暗角开始半径（中心50%区域不变暗）
        float innerR = maxDist * 0.5f;

        // 使用字节缓冲区直接操作（快速）
        byte[] data = img.GetData();
        int stride = w * 4;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                if (dist > innerR)
                {
                    // 从innerR到maxDist，alpha从0到0.6
                    float t = (dist - innerR) / (maxDist - innerR);
                    t = Mathf.Clamp(t, 0f, 1f);
                    // 用smoothstep让过渡更柔和
                    t = t * t * (3f - 2f * t);
                    byte alpha = (byte)(t * 160f); // 最大alpha ~0.63

                    int idx = y * stride + x * 4;
                    data[idx] = 0;     // R
                    data[idx + 1] = 0; // G
                    data[idx + 2] = 0; // B
                    data[idx + 3] = alpha; // A
                }
            }
        }

        img.SetData(w, h, false, Image.Format.Rgba8, data);
        var tex = ImageTexture.CreateFromImage(img);

        _vignetteSprite = new Sprite2D();
        _vignetteSprite.Texture = tex;
        _vignetteSprite.Centered = false;
        _vignetteSprite.Position = Vector2.Zero;
        _vignetteSprite.ZIndex = 100;
        AddChild(_vignetteSprite);

        GameLog.Debug($"[Visual] 暗角纹理已生成 ({w}x{h})");
    }
}
