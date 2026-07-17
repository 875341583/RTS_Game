using Godot;

namespace RTSGame;

/// <summary>
/// 矿点：地图上散布的可采集资源点，矿车靠近后减速消耗储量。
/// </summary>
public partial class ResourceNode : Area2D
{
    [Export] public int InitialAmount { get; set; } = 1000;
    [Export] public Color OreColor { get; set; } = new Color(0.35f, 0.2f, 0.85f, 1f);

    public int Amount { get; private set; }
    public bool IsDepleted => Amount <= 0;

    private Sprite2D _ore = null!;
    private Label _amountLabel = null!;

    public override void _Ready()
    {
        Amount = InitialAmount;
        _ore = GetNode<Sprite2D>("Ore");
        _amountLabel = GetNode<Label>("AmountLabel");

        var img = Image.CreateEmpty(56, 56, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int x = 0; x < 56; x++)
            for (int y = 0; y < 56; y++)
            {
                float dx = x - 28, dy = y - 28;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist < 24)
                {
                    // 矿石渐变
                    var c = OreColor.Lerp(new Color(0.7f, 0.5f, 1f, 1f), 1f - dist / 24f);
                    img.SetPixel(x, y, c);
                }
                else if (dist < 26)
                {
                    img.SetPixel(x, y, new Color(0.2f, 0.1f, 0.5f, 0.5f));
                }
            }
        _ore.Texture = ImageTexture.CreateFromImage(img);
        UpdateLabel();
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
