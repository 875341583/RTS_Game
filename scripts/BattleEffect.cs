using Godot;

namespace RTSGame;

/// <summary>
/// Q5: 一次性战斗视觉特效（炮口闪光、炮弹飞行轨迹、爆炸）。
/// 爆炸使用程序化生成的多帧动画（128×128高分辨率）。
/// 自动播完 QueueFree()。
/// </summary>
public partial class BattleEffect : Node2D
{
    public enum FxType { MuzzleFlash, Shell, Explosion, BigExplosion }

    private FxType _type;
    private float _lifetime;
    private float _age;
    private Vector2 _startPos;
    private Vector2 _endPos;
    private Sprite2D? _sprite;

    // 程序化纹理
    private static Texture2D? _flashTex;
    private static Texture2D? _shellTex;

    // 爆炸：程序化生成5帧动画（128×128）
    private static Texture2D?[]? _explosionFrames;
    private static Texture2D?[]? _bigExplosionFrames;

    public override void _Ready()
    {
        EnsureTextures();
        ZIndex = RenderLayer.Effect; // P1-5: 特效层，始终在单位之上（原值10会被单位Y-Sort 1000+遮挡）

        switch (_type)
        {
            case FxType.MuzzleFlash:
                _lifetime = 0.1f;
                _sprite = new Sprite2D { Texture = _flashTex! };
                AddChild(_sprite!);
                break;
            case FxType.Shell:
                _lifetime = 0.22f;
                _sprite = new Sprite2D { Texture = _shellTex! };
                AddChild(_sprite!);
                break;
            case FxType.Explosion:
                _lifetime = 0.5f;
                _sprite = new Sprite2D { Texture = _explosionFrames![0]! };
                AddChild(_sprite!);
                break;
            case FxType.BigExplosion:
                _lifetime = 0.7f;
                _sprite = new Sprite2D { Texture = _bigExplosionFrames![0]! };
                AddChild(_sprite!);
                break;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        if (_age >= _lifetime) { QueueFree(); return; }

        float t = _age / _lifetime;
        switch (_type)
        {
            case FxType.MuzzleFlash:
                _sprite!.Scale = Vector2.One * (0.7f + t * 0.6f);
                _sprite!.Modulate = new Color(1f, 0.9f, 0.4f, 1f - t * t);
                break;
            case FxType.Shell:
                GlobalPosition = _startPos.Lerp(_endPos, t);
                _sprite!.Modulate = new Color(1f, 0.85f, 0.4f, 1f - t * 0.4f);
                break;
            case FxType.Explosion:
            {
                // 5帧爆炸动画顺序播放
                int frame = Mathf.Min((int)(t * 5f), 4);
                _sprite!.Texture = _explosionFrames![frame]!;
                // 缩放从小到大再缩小
                float s = t < 0.6f ? 0.8f + t * 1.2f : 1.5f - (t - 0.6f) * 0.8f;
                _sprite!.Scale = Vector2.One * s;
                float a = t < 0.2f ? 1f : 1f - (t - 0.2f) / 0.8f;
                _sprite!.Modulate = new Color(1f, 1f, 1f, a);
                break;
            }
            case FxType.BigExplosion:
            {
                int frame = Mathf.Min((int)(t * 5f), 4);
                _sprite!.Texture = _bigExplosionFrames![frame]!;
                float bs = t < 0.5f ? 0.6f + t * 2.0f : 1.6f - (t - 0.5f) * 0.6f;
                _sprite!.Scale = Vector2.One * bs;
                float ba = t < 0.15f ? 1f : 1f - (t - 0.15f) / 0.85f;
                _sprite!.Modulate = new Color(1f, 1f, 1f, ba);
                break;
            }
        }
    }

    // ---- 静态工厂方法 ----

    public static BattleEffect MuzzleFlash(Vector2 pos)
        => new() { _type = FxType.MuzzleFlash, GlobalPosition = pos };

    public static BattleEffect Shell(Vector2 from, Vector2 to)
        => new() { _type = FxType.Shell, _startPos = from, _endPos = to };

    public static BattleEffect Explosion(Vector2 pos)
        => new() { _type = FxType.Explosion, GlobalPosition = pos };

    public static BattleEffect BigExplosion(Vector2 pos)
        => new() { _type = FxType.BigExplosion, GlobalPosition = pos };

    // ---- 纹理加载 ----

    private static void EnsureTextures()
    {
        if (_flashTex != null) return;

        // 炮口闪光：亮橙黄圆形发光（程序化小纹理无需PNG）
        var flash = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        flash.Fill(Colors.Transparent);
        for (int x = 0; x < 32; x++)
            for (int y = 0; y < 32; y++)
            {
                float d = Mathf.Sqrt((x - 16) * (x - 16) + (y - 16) * (y - 16));
                if (d < 14)
                {
                    float b = 1f - d / 14f;
                    flash.SetPixel(x, y, new Color(1f, 0.85f * b + 0.15f, 0.25f * b, b));
                }
            }
        _flashTex = ImageTexture.CreateFromImage(flash);

        // 炮弹：小亮点（程序化）
        var shell = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        shell.Fill(Colors.Transparent);
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
            {
                float d = Mathf.Sqrt((x - 4) * (x - 4) + (y - 4) * (y - 4));
                if (d < 3.5f)
                {
                    float b = 1f - d / 3.5f;
                    shell.SetPixel(x, y, new Color(1f, 0.9f, 0.4f, b));
                }
            }
        _shellTex = ImageTexture.CreateFromImage(shell);

        // 普通爆炸：程序化生成5帧（128×128高分辨率）
        _explosionFrames = new Texture2D[5];
        for (int i = 0; i < 5; i++)
            _explosionFrames[i] = GenerateExplosionFrame(i, 5, 128);

        // 大爆炸：程序化生成5帧（128×128，更大更黑烟）
        _bigExplosionFrames = new Texture2D[5];
        for (int i = 0; i < 5; i++)
            _bigExplosionFrames[i] = GenerateBigExplosionFrame(i, 5, 128);
    }

    // ---- 程序化爆炸生成 ----
    // 使用确定性种子保证每次生成的纹理一致

    private static Texture2D GenerateExplosionFrame(int frame, int total, int size)
    {
        // 爆炸进度：0=点火 → 1=消散
        float t = (float)frame / (total - 1);
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        int cx = size / 2, cy = size / 2;

        // 1. 外发光层（橙色光晕）
        float glowR = size * (0.15f + t * 0.25f);
        float glowA = (1f - t) * 0.6f;
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float dx = x - cx, dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < glowR * 1.5f)
                {
                    float f = Mathf.Max(0, 1f - d / (glowR * 1.5f));
                    f = f * f;
                    var existing = img.GetPixel(x, y);
                    if (existing.A < f * glowA)
                        img.SetPixel(x, y, new Color(1f, 0.4f, 0.05f, f * glowA));
                }
            }

        // 2. 火球主体（前期黄白色，后期橙红色）
        float fireR = size * (0.08f + t * 0.18f);
        float fireA = t < 0.3f ? 1f : Mathf.Max(0, 1f - (t - 0.3f) / 0.5f);
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float dx = x - cx, dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < fireR)
                {
                    float f = 1f - d / fireR; // 中心=1，边缘=0
                    // 颜色：中心白热→黄→橙→红
                    float r, g, b;
                    if (f > 0.7f) // 中心白热
                        { r = 1f; g = 1f; b = 0.9f; }
                    else if (f > 0.4f) // 黄
                        { r = 1f; g = 0.85f; b = 0.3f; }
                    else if (f > 0.15f) // 橙
                        { r = 1f; g = 0.5f; b = 0.1f; }
                    else // 红
                        { r = 0.9f; g = 0.2f; b = 0.05f; }

                    // 后期变暗变红
                    r *= (1f - t * 0.2f);
                    g *= (1f - t * 0.5f);
                    b *= (1f - t * 0.6f);

                    var existing = img.GetPixel(x, y);
                    float a = f * fireA;
                    if (existing.A < a)
                        img.SetPixel(x, y, new Color(r, g, b, a));
                }
            }

        // 3. 火星粒子（随机散落的小亮点）
        var rng = new Godot.RandomNumberGenerator();
        rng.Seed = (ulong)(frame * 1000 + 42);
        int sparkCount = (int)(30 * (1f - t * 0.5f));
        for (int s = 0; s < sparkCount; s++)
        {
            float angle = rng.RandfRange(0, Mathf.Tau);
            float dist = rng.RandfRange(fireR * 0.5f, fireR * 1.5f + t * size * 0.15f);
            int sx = cx + (int)(Mathf.Cos(angle) * dist);
            int sy = cy + (int)(Mathf.Sin(angle) * dist);
            if (sx >= 1 && sx < size - 1 && sy >= 1 && sy < size - 1)
            {
                float fade = 1f - t * 0.7f;
                img.SetPixel(sx, sy, new Color(1f, 0.7f * fade, 0.2f * fade, fade));
            }
        }

        // 4. 后期烟雾
        if (t > 0.4f)
        {
            float smokeT = (t - 0.4f) / 0.6f;
            float smokeR = size * (0.12f + smokeT * 0.1f);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < smokeR * 1.2f)
                    {
                        float f = Mathf.Max(0, 1f - d / (smokeR * 1.2f));
                        float gray = 0.3f - smokeT * 0.2f;
                        float a = f * smokeT * 0.7f;
                        var existing = img.GetPixel(x, y);
                        // 烟雾只在火球外、低于烟雾透明度的地方绘制
                        if (existing.A < a && d > fireR * 0.5f)
                            img.SetPixel(x, y, new Color(gray, gray, gray, a));
                    }
                }
        }

        return ImageTexture.CreateFromImage(img);
    }

    private static Texture2D GenerateBigExplosionFrame(int frame, int total, int size)
    {
        float t = (float)frame / (total - 1);
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);

        int cx = size / 2, cy = size / 2;

        // 大爆炸：更大的火球 + 更浓的烟
        // 1. 外发光
        float glowR = size * (0.22f + t * 0.28f);
        float glowA = (1f - t * 0.7f) * 0.7f;
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float dx = x - cx, dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < glowR * 1.6f)
                {
                    float f = Mathf.Max(0, 1f - d / (glowR * 1.6f));
                    f = f * f;
                    var existing = img.GetPixel(x, y);
                    if (existing.A < f * glowA)
                        img.SetPixel(x, y, new Color(1f, 0.35f, 0.05f, f * glowA));
                }
            }

        // 2. 火球（更大）
        float fireR = size * (0.12f + t * 0.22f);
        float fireA = t < 0.25f ? 1f : Mathf.Max(0, 1f - (t - 0.25f) / 0.55f);
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                float dx = x - cx, dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < fireR)
                {
                    float f = 1f - d / fireR;
                    float r, g, b;
                    if (f > 0.75f)
                        { r = 1f; g = 1f; b = 0.95f; }
                    else if (f > 0.45f)
                        { r = 1f; g = 0.8f; b = 0.25f; }
                    else if (f > 0.2f)
                        { r = 1f; g = 0.45f; b = 0.08f; }
                    else
                        { r = 0.85f; g = 0.15f; b = 0.03f; }
                    r *= (1f - t * 0.15f);
                    g *= (1f - t * 0.55f);
                    b *= (1f - t * 0.65f);
                    var existing = img.GetPixel(x, y);
                    float a = f * fireA;
                    if (existing.A < a)
                        img.SetPixel(x, y, new Color(r, g, b, a));
                }
            }

        // 3. 大量火星
        var rng = new Godot.RandomNumberGenerator();
        rng.Seed = (ulong)(frame * 2000 + 99);
        int sparkCount = (int)(60 * (1f - t * 0.4f));
        for (int s = 0; s < sparkCount; s++)
        {
            float angle = rng.RandfRange(0, Mathf.Tau);
            float dist = rng.RandfRange(fireR * 0.6f, fireR * 1.8f + t * size * 0.18f);
            int sx = cx + (int)(Mathf.Cos(angle) * dist);
            int sy = cy + (int)(Mathf.Sin(angle) * dist);
            if (sx >= 1 && sx < size - 1 && sy >= 1 && sy < size - 1)
            {
                float fade = 1f - t * 0.6f;
                float sz = rng.RandfRange(0.7f, 1f);
                img.SetPixel(sx, sy, new Color(1f * sz, (0.6f * fade) * sz, (0.15f * fade) * sz, fade));
                if (rng.Randf() > 0.5f && sx + 1 < size && sy + 1 < size)
                    img.SetPixel(sx + 1, sy + 1, new Color(0.8f * sz, (0.45f * fade) * sz, (0.1f * fade) * sz, fade * 0.6f));
            }
        }

        // 4. 浓烟（更早出现更浓）
        if (t > 0.25f)
        {
            float smokeT = (t - 0.25f) / 0.75f;
            float smokeR = size * (0.16f + smokeT * 0.14f);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < smokeR * 1.4f)
                    {
                        float f = Mathf.Max(0, 1f - d / (smokeR * 1.4f));
                        float gray = 0.25f - smokeT * 0.18f;
                        float a = f * smokeT * 0.85f;
                        var existing = img.GetPixel(x, y);
                        if (existing.A < a && d > fireR * 0.4f)
                            img.SetPixel(x, y, new Color(gray, gray, gray, a));
                    }
                }
        }

        return ImageTexture.CreateFromImage(img);
    }
}
