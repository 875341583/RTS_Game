using Godot;

namespace RTSGame;

/// <summary>
/// RTS 策略相机：支持 WASD/方向键移动、屏幕边缘滚屏、鼠标滚轮缩放。
/// 等距视角适配：WASD移动方向映射为等距对角线方向。
/// 相机边界动态钳制，不会滚出地图范围。
/// F11 切换全屏。
/// </summary>
public partial class RTSCamera : Camera2D
{
    [Export] public float PanSpeed { get; set; } = 600f;
    [Export] public float EdgePanSpeed { get; set; } = 800f;
    [Export] public int EdgePanMargin { get; set; } = 20;
    [Export] public float MinZoom { get; set; } = 0.5f;
    [Export] public float MaxZoom { get; set; } = 2.0f;
    [Export] public float ZoomSpeed { get; set; } = 0.1f;

    private Vector2 _targetZoom = new(1, 1);

    // Phase1: 屏幕震动
    private float _shakeIntensity = 0f;
    private float _shakeDuration = 0f;
    private float _shakeTotalDuration = 0f;

    /// <summary>触发屏幕震动。
    /// intensity: 像素偏移最大值（4=轻微，8=中等，16=核弹级）
    /// duration: 持续时间（秒）</summary>
    public void Shake(float intensity, float duration)
    {
        // 新震动如果更强则覆盖旧的
        if (intensity > _shakeIntensity || _shakeDuration <= 0f)
        {
            _shakeIntensity = intensity;
            _shakeDuration = duration;
            _shakeTotalDuration = duration;
        }
    }

    /// <summary>地图边界（由 Main 在 _Ready 中设置）</summary>
    public static Rect2 MapBounds { get; set; } = new(-2200f, -500f, 4400f, 3000f);

    /// <summary>是否已设置过地图边界</summary>
    private static bool _boundsSet = false;

    /// <summary>设置地图边界（供 Main 调用）</summary>
    public static void SetMapBounds(float mapSize)
    {
        // 等距地图的屏幕范围：X方向为 ±mapSize*HalfW，Y方向为 0 到 mapSize*HalfH*2
        float halfW = (float)IsoCoords.HalfW;
        float halfH = (float)IsoCoords.HalfH;
        float xRange = mapSize * halfW;
        float yRange = mapSize * halfH;
        MapBounds = new Rect2(-xRange * 0.5f, -yRange * 0.3f, xRange, yRange * 1.3f);
        _boundsSet = true;
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var moveVec = Vector2.Zero;

        // 键盘移动
        int up = Input.IsActionPressed("move_up") ? 1 : 0;
        int down = Input.IsActionPressed("move_down") ? 1 : 0;
        int left = Input.IsActionPressed("move_left") ? 1 : 0;
        int right = Input.IsActionPressed("move_right") ? 1 : 0;

        // 等距视角下 WASD 映射到对角线方向
        moveVec = Vector2.Zero;
        if (up > 0) moveVec += new Vector2(-IsoCoords.HalfW, -IsoCoords.HalfH);   // W → 北
        if (down > 0) moveVec += new Vector2(IsoCoords.HalfW, IsoCoords.HalfH);    // S → 南
        if (left > 0) moveVec += new Vector2(-IsoCoords.HalfW, IsoCoords.HalfH);   // A → 西
        if (right > 0) moveVec += new Vector2(IsoCoords.HalfW, -IsoCoords.HalfH);  // D → 东

        // 屏幕边缘滚屏
        var viewportSize = GetViewportRect().Size;
        var mousePos = GetViewport().GetMousePosition();
        var edgeVec = Vector2.Zero;
        if (mousePos.X < EdgePanMargin) edgeVec.X -= 1;
        else if (mousePos.X > viewportSize.X - EdgePanMargin) edgeVec.X += 1;
        if (mousePos.Y < EdgePanMargin) edgeVec.Y -= 1;
        else if (mousePos.Y > viewportSize.Y - EdgePanMargin) edgeVec.Y += 1;

        if (edgeVec != Vector2.Zero)
        {
            edgeVec = edgeVec.Normalized();
            moveVec += edgeVec * (float)IsoCoords.HalfW;
        }

        // 归一化并移动
        if (moveVec != Vector2.Zero)
        {
            moveVec = moveVec.Normalized();
            var speed = (edgeVec != Vector2.Zero) ? EdgePanSpeed : PanSpeed;
            Position += moveVec * speed * dt;
        }

        // 动态钳制相机到地图边界
        var bounds = _boundsSet ? MapBounds : new Rect2(-2200f, -500f, 4400f, 3000f);
        Position = new Vector2(
            Mathf.Clamp(Position.X, bounds.Position.X, bounds.Position.X + bounds.Size.X),
            Mathf.Clamp(Position.Y, bounds.Position.Y, bounds.Position.Y + bounds.Size.Y)
        );

        // 平滑缩放
        Zoom = Zoom.Lerp(_targetZoom, dt * 10f);

        // Phase1: 屏幕震动
        if (_shakeDuration > 0f)
        {
            _shakeDuration -= dt;
            float falloff = Mathf.Max(0f, _shakeDuration / _shakeTotalDuration);
            float currentIntensity = _shakeIntensity * falloff;
            Offset = new Vector2(
                (float)(GD.RandRange(-1.0, 1.0) * currentIntensity),
                (float)(GD.RandRange(-1.0, 1.0) * currentIntensity)
            );
            if (_shakeDuration <= 0f)
            {
                Offset = Vector2.Zero;
                _shakeIntensity = 0f;
            }
        }
    }

    public override void _Input(InputEvent @event)
    {
        // F11 切换全屏
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.F11)
        {
            var mode = DisplayServer.WindowGetMode();
            if (mode == DisplayServer.WindowMode.Fullscreen)
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            else
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }

        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            if (mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                _targetZoom = new Vector2(
                    Mathf.Clamp(_targetZoom.X - ZoomSpeed, MinZoom, MaxZoom),
                    Mathf.Clamp(_targetZoom.Y - ZoomSpeed, MinZoom, MaxZoom)
                );
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                _targetZoom = new Vector2(
                    Mathf.Clamp(_targetZoom.X + ZoomSpeed, MinZoom, MaxZoom),
                    Mathf.Clamp(_targetZoom.Y + ZoomSpeed, MinZoom, MaxZoom)
                );
            }
        }
    }
}
