using Godot;

namespace RTSGame;

/// <summary>
/// RTS 策略相机：支持 WASD/方向键移动、屏幕边缘滚屏、鼠标滚轮缩放
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

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var moveVec = Vector2.Zero;

        // 键盘移动
        if (Input.IsActionPressed("move_up")) moveVec.Y -= 1;
        if (Input.IsActionPressed("move_down")) moveVec.Y += 1;
        if (Input.IsActionPressed("move_left")) moveVec.X -= 1;
        if (Input.IsActionPressed("move_right")) moveVec.X += 1;

        // 屏幕边缘滚屏
        var viewportSize = GetViewportRect().Size;
        var mousePos = GetViewport().GetMousePosition();

        if (mousePos.X < EdgePanMargin) moveVec.X -= 1;
        else if (mousePos.X > viewportSize.X - EdgePanMargin) moveVec.X += 1;

        if (mousePos.Y < EdgePanMargin) moveVec.Y -= 1;
        else if (mousePos.Y > viewportSize.Y - EdgePanMargin) moveVec.Y += 1;

        // 归一化并移动
        if (moveVec != Vector2.Zero)
        {
            moveVec = moveVec.Normalized();
            var speed = EdgePanSpeed; // 边缘用较快的速度
            if (moveVec.X != 0 && moveVec.Y == 0)
                speed = PanSpeed; // 纯键盘用标准速度

            Position += moveVec * speed * dt;

            // 限制相机范围
            Position = new Vector2(
                Mathf.Clamp(Position.X, 0, 2000),
                Mathf.Clamp(Position.Y, 0, 2000)
            );
        }

        // 平滑缩放
        Zoom = Zoom.Lerp(_targetZoom, dt * 10f);
    }

    public override void _Input(InputEvent @event)
    {
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
