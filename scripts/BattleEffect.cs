using Godot;

namespace RTSGame;

/// <summary>
/// Q5: 一次性战斗视觉特效（炮口闪光、炮弹飞行轨迹、爆炸）。
/// 爆炸使用 Kenney Top-down Tanks Redux PNG（CC0）多帧动画。
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

    // 炮口闪光和炮弹：程序化小纹理（不需要PNG）
    private static Texture2D? _flashTex;
    private static Texture2D? _shellTex;

    // 爆炸：Kenney 5帧动画
    private static Texture2D?[] _explosionFrames = null!;
    private static Texture2D?[] _bigExplosionFrames = null!;

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
                _lifetime = 0.5f;
                _sprite = new Sprite2D { Texture = _explosionFrames[0]! };
                AddChild(_sprite);
                break;
            case FxType.BigExplosion:
                _lifetime = 0.7f;
                _sprite = new Sprite2D { Texture = _bigExplosionFrames[0]! };
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
            {
                // 5帧爆炸动画顺序播放
                int frame = Mathf.Min((int)(t * 5f), 4);
                _sprite.Texture = _explosionFrames[frame]!;
                // 缩放从小到大再缩小
                float s = t < 0.6f ? 0.8f + t * 1.2f : 1.5f - (t - 0.6f) * 0.8f;
                _sprite.Scale = Vector2.One * s;
                float a = t < 0.2f ? 1f : 1f - (t - 0.2f) / 0.8f;
                _sprite.Modulate = new Color(1f, 1f, 1f, a);
                break;
            }
            case FxType.BigExplosion:
            {
                int frame = Mathf.Min((int)(t * 5f), 4);
                _sprite.Texture = _bigExplosionFrames[frame]!;
                float bs = t < 0.5f ? 0.6f + t * 2.0f : 1.6f - (t - 0.5f) * 0.6f;
                _sprite.Scale = Vector2.One * bs;
                float ba = t < 0.15f ? 1f : 1f - (t - 0.15f) / 0.85f;
                _sprite.Modulate = new Color(1f, 1f, 1f, ba);
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

        // 普通爆炸：Kenney explosion1-5 5帧
        _explosionFrames = new ImageTexture[5];
        for (int i = 0; i < 5; i++)
            _explosionFrames[i] = LoadFxTexture($"res://assets/sprites/effects/explosion{i + 1}.png");

        // 大爆炸：Kenney explosionSmoke1-5 5帧
        _bigExplosionFrames = new ImageTexture[5];
        for (int i = 0; i < 5; i++)
            _bigExplosionFrames[i] = LoadFxTexture($"res://assets/sprites/effects/explosionSmoke{i + 1}.png");
    }

    private static Texture2D LoadFxTexture(string path)
    {
        var tex = GD.Load<Texture2D>(path);
        if (tex == null)
        {
            GD.PrintErr($"[BattleEffect] Failed to load: {path}");
            var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            img.SetPixel(0, 0, Colors.Magenta);
            return ImageTexture.CreateFromImage(img);
        }
        return tex; // Godot 导入 PNG 返回 CompressedTexture2D
    }
}
