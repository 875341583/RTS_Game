using Godot;

namespace RTSGame;

/// <summary>
/// Q5: 一次性战斗视觉特效（炮口闪光、炮弹飞行轨迹、爆炸）。
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
    private Sprite2D _sprite = null!;

    private static ImageTexture? _flashTex;
    private static ImageTexture? _shellTex;
    private static ImageTexture? _explosionTex;
    private static ImageTexture? _bigExplosionTex;

    public override void _Ready()
    {
        EnsureTextures();
        ZIndex = 10; // 在单位之上渲染

        switch (_type)
        {
            case FxType.MuzzleFlash:
                _lifetime = 0.1f;
                _sprite = new Sprite2D { Texture = _flashTex! };
                AddChild(_sprite);
                break;
            case FxType.Shell:
                _lifetime = 0.22f;
                _sprite = new Sprite2D { Texture = _shellTex! };
                AddChild(_sprite);
                break;
            case FxType.Explosion:
                _lifetime = 0.45f;
                _sprite = new Sprite2D { Texture = _explosionTex! };
                AddChild(_sprite);
                break;
            case FxType.BigExplosion:
                _lifetime = 0.65f;
                _sprite = new Sprite2D { Texture = _bigExplosionTex! };
                AddChild(_sprite);
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
                _sprite.Scale = Vector2.One * (0.7f + t * 0.6f);
                _sprite.Modulate = new Color(1f, 0.9f, 0.4f, 1f - t * t);
                break;
            case FxType.Shell:
                GlobalPosition = _startPos.Lerp(_endPos, t);
                _sprite.Modulate = new Color(1f, 0.85f, 0.4f, 1f - t * 0.4f);
                break;
            case FxType.Explosion:
                float s = 0.5f + t * 1.5f;
                _sprite.Scale = Vector2.One * s;
                float a = t < 0.25f ? 1f : 1f - (t - 0.25f) / 0.75f;
                _sprite.Modulate = new Color(1f, 0.6f + (1 - t) * 0.3f, 0.2f, a);
                break;
            case FxType.BigExplosion:
                float bs = 0.4f + t * 2.5f;
                _sprite.Scale = Vector2.One * bs;
                float ba = t < 0.15f ? 1f : 1f - (t - 0.15f) / 0.85f;
                _sprite.Modulate = new Color(1f, 0.5f + (1 - t) * 0.4f, 0.15f, ba);
                break;
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

    // ---- 程序化纹理生成 ----

    private static void EnsureTextures()
    {
        if (_flashTex != null) return;

        // 炮口闪光：亮橙黄圆形发光
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

        // 炮弹：小亮点
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

        // 爆炸：橙红扩展圆（含噪声细节）
        var exp = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        exp.Fill(Colors.Transparent);
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 64; y++)
            {
                float d = Mathf.Sqrt((x - 32) * (x - 32) + (y - 32) * (y - 32));
                if (d < 28)
                {
                    float b = 1f - d / 28f;
                    float n = Mathf.Sin(x * 0.5f) * Mathf.Cos(y * 0.4f) * 0.08f;
                    exp.SetPixel(x, y, new Color(1f + n, 0.5f + b * 0.4f, 0.1f + b * 0.15f, b * 0.9f));
                }
                else if (d < 31)
                {
                    float a = (31 - d) / 3f * 0.25f;
                    exp.SetPixel(x, y, new Color(0.8f, 0.3f, 0.08f, a));
                }
            }
        _explosionTex = ImageTexture.CreateFromImage(exp);

        // 大爆炸（建筑/重坦死亡）：更大更亮
        var big = Image.CreateEmpty(96, 96, false, Image.Format.Rgba8);
        big.Fill(Colors.Transparent);
        for (int x = 0; x < 96; x++)
            for (int y = 0; y < 96; y++)
            {
                float d = Mathf.Sqrt((x - 48) * (x - 48) + (y - 48) * (y - 48));
                if (d < 44)
                {
                    float b = 1f - d / 44f;
                    float n = Mathf.Sin(x * 0.3f) * Mathf.Cos(y * 0.25f) * 0.1f;
                    big.SetPixel(x, y, new Color(1f + n, 0.4f + b * 0.5f, 0.05f + b * 0.15f, b * 0.85f));
                }
                else if (d < 48)
                {
                    float a = (48 - d) / 4f * 0.2f;
                    big.SetPixel(x, y, new Color(0.7f, 0.2f, 0.05f, a));
                }
            }
        _bigExplosionTex = ImageTexture.CreateFromImage(big);
    }
}
