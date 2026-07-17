using Godot;

namespace RTSGame;

/// <summary>
/// 战略要地：单位停留 4 秒可占领，占领后每秒提供 $5 被动收入。
/// 只有战斗单位（AttackDamage > 0）能占领。
/// </summary>
public partial class StrategicPoint : Area2D
{
    public int OwningTeam { get; private set; } = -1; // -1 = neutral

    private Sprite2D _visual = null!;
    private Label _label = null!;
    private int _blueCount;
    private int _redCount;
    private float _captureProgress; // 0-100
    private float _incomeTimer;
    private const float CaptureSpeed = 25f;   // 4 seconds to capture
    private const float IncomePerSecond = 5f;

    private static ImageTexture? _neutralTex;
    private static ImageTexture? _blueTex;
    private static ImageTexture? _redTex;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 2; // monitor Units layer
        Monitoring = true;

        // Collision shape
        var shape = new CollisionShape2D();
        var rect = new RectangleShape2D();
        rect.Size = new Vector2(120, 120);
        shape.Shape = rect;
        AddChild(shape);

        // Visual
        EnsureTextures();
        _visual = new Sprite2D();
        _visual.Texture = _neutralTex;
        AddChild(_visual);

        // Label
        _label = new Label();
        _label.OffsetLeft = -50;
        _label.OffsetTop = -55;
        _label.OffsetRight = 50;
        _label.OffsetBottom = -35;
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.Text = "战略点";
        AddChild(_label);

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;

        GD.Print($"[StrategicPoint] Created at {GlobalPosition}");
    }

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

    public override void _Process(double delta)
    {
        var dt = (float)delta;

        // Capture logic
        if (_blueCount > 0 && _redCount == 0 && OwningTeam != 0)
        {
            _captureProgress += dt * CaptureSpeed;
            if (_captureProgress >= 100f)
            {
                OwningTeam = 0;
                _captureProgress = 0f;
                _visual.Texture = _blueTex;
                _label.Text = "蓝方控制";
                GD.Print("[StrategicPoint] Blue captured!");
            }
        }
        else if (_redCount > 0 && _blueCount == 0 && OwningTeam != 1)
        {
            _captureProgress += dt * CaptureSpeed;
            if (_captureProgress >= 100f)
            {
                OwningTeam = 1;
                _captureProgress = 0f;
                _visual.Texture = _redTex;
                _label.Text = "红方控制";
                GD.Print("[StrategicPoint] Red captured!");
            }
        }
        else if (_blueCount == 0 && _redCount == 0)
        {
            _captureProgress = Mathf.Max(0f, _captureProgress - dt * CaptureSpeed * 0.5f);
        }

        // Income（受难度开关控制）
        if (OwningTeam >= 0 && GetParent().GetParent() is Main main2 && main2.StrategicPointIncomeEnabled)
        {
            _incomeTimer += dt;
            if (_incomeTimer >= 1f)
            {
                _incomeTimer -= 1f;
                main2.AddResourceForTeam(OwningTeam, (int)IncomePerSecond);
            }
        }

        // Update label with capture progress
        if (_captureProgress > 0f && OwningTeam == -1)
        {
            _label.Text = $"占领中 {(_blueCount > 0 ? "蓝" : "红")} {(int)_captureProgress}%";
        }
    }

    private static void EnsureTextures()
    {
        if (_neutralTex != null) return;

        _neutralTex = CreatePointTexture(new Color(0.8f, 0.75f, 0.3f, 0.5f), new Color(0.6f, 0.55f, 0.2f, 0.9f));
        _blueTex = CreatePointTexture(new Color(0.3f, 0.6f, 1.0f, 0.5f), new Color(0.2f, 0.5f, 0.9f, 0.9f));
        _redTex = CreatePointTexture(new Color(1.0f, 0.35f, 0.35f, 0.5f), new Color(0.8f, 0.2f, 0.2f, 0.9f));
    }

    private static ImageTexture CreatePointTexture(Color fill, Color border)
    {
        var img = Image.CreateEmpty(100, 100, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int x = 0; x < 100; x++)
            for (int y = 0; y < 100; y++)
            {
                float dx = x - 50, dy = y - 50;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist < 42)
                    img.SetPixel(x, y, fill);
                else if (dist < 48)
                    img.SetPixel(x, y, border);
            }
        // Star marker
        for (float a = 0; a < Mathf.Tau; a += 0.6f)
        {
            float r = (a % 1.2f < 0.6f) ? 28 : 14;
            for (int i = 0; i < (int)r; i++)
            {
                int cx = (int)(50 + i * Mathf.Cos(a));
                int cy = (int)(50 + i * Mathf.Sin(a));
                if (cx >= 0 && cx < 100 && cy >= 0 && cy < 100)
                    img.SetPixel(cx, cy, border.Lightened(0.2f));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }
}
