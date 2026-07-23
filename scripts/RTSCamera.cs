using Godot;

namespace RTSGame;

/// <summary>
/// RTS 策略相机：支持 WASD/方向键移动、屏幕边缘滚屏、鼠标滚轮缩放。
/// 等距视角适配：WASD移动方向映射为等距对角线方向。
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

        // 键盘移动（按下为1，按下为-1，不按为0）
        int up = Input.IsActionPressed("move_up") ? 1 : 0;
        int down = Input.IsActionPressed("move_down") ? 1 : 0;
        int left = Input.IsActionPressed("move_left") ? 1 : 0;
        int right = Input.IsActionPressed("move_right") ? 1 : 0;

        // 等距视角下，WASD映射到对角线方向：
        // W(上) → 等距北西方向 (screenUp-Left)
        // S(下) → 等距南东方向 (screenDown-Right)
        // A(左) → 等距南西方向 (screenDown-Left)
        // D(右) → 等距北东方向 (screenUp-Right)
        //
        // 等距网格方向 → 屏幕方向：
        // N(0,-1) → screen(-HalfW, -HalfH) = (-32, -16) → normalized
        // S(0,+1) → screen(+HalfW, +HalfH) = (32, 16)
        // W(-1,0) → screen(-HalfW, +HalfH) = (-32, 16)
        // E(+1,0) → screen(+HalfW, -HalfH) = (32, -16)
        //
        // W键(上) = 向北移动 → screen dir = (-32, -16)
        // S键(下) = 向南移动 → screen dir = (32, 16)
        // A键(左) = 向西移动 → screen dir = (-32, 16)
        // D键(右) = 向东移动 → screen dir = (32, -16)

        // WASD组合 = 纯方向叠加，组合后可覆盖8个方向
        float dirX = (right - left) * IsoCoords.HalfW;
        float dirY = 0;
        // W+S 控制南北（等距Y轴）
        dirX += (down - up) * IsoCoords.HalfW;
        dirY += (down + up) * IsoCoords.HalfH;
        // A+D 控制东西（等距X轴）
        dirX += (right - left) * IsoCoords.HalfW; // 已加过，这里需要重新构思

        // 重新计算：清晰的4方向→等距映射
        moveVec = Vector2.Zero;
        if (up > 0) moveVec += new Vector2(-IsoCoords.HalfW, -IsoCoords.HalfH);   // W → 北
        if (down > 0) moveVec += new Vector2(IsoCoords.HalfW, IsoCoords.HalfH);    // S → 南
        if (left > 0) moveVec += new Vector2(-IsoCoords.HalfW, IsoCoords.HalfH);   // A → 西
        if (right > 0) moveVec += new Vector2(IsoCoords.HalfW, -IsoCoords.HalfH);  // D → 东

        // 屏幕边缘滚屏（屏幕坐标方向，直接用）
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

            // 限制相机范围（等距地图边界）
            Position = new Vector2(
                Mathf.Clamp(Position.X, -2200f, 2200f),
                Mathf.Clamp(Position.Y, -500f, 2500f)
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
