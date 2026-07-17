using Godot;

namespace RTSGame;

/// <summary>
/// Q2 小地图：全图态势面板。
/// 左下角固定，显示蓝/红方单位、建筑、矿点、战略点、障碍物。
/// 点击/拖动小地图跳转视角，白色矩形显示当前视口范围。
/// </summary>
public partial class Minimap : Control
{
    private Main _main = null!;
    private RTSCamera _camera = null!;

    // 缓存节点引用
    private Node2D? _obstaclesNode;
    private Node2D? _resourcesNode;
    private Node2D? _strategicPointsNode;
    private Node2D? _buildingsNode;
    private Node2D? _unitsNode;

    // 地图与小地图参数
    private const float MapSize = 2000f;
    private const float MmSize = 180f;
    private const float S = MmSize / MapSize; // ~0.09

    // 颜色
    private static readonly Color CBg = new(0.08f, 0.15f, 0.08f);
    private static readonly Color CBorder = new(0.5f, 0.5f, 0.5f);
    private static readonly Color CObstacle = new(0.22f, 0.22f, 0.28f);
    private static readonly Color COre = new(1f, 0.85f, 0f);
    private static readonly Color COreDim = new(0.5f, 0.42f, 0f);
    private static readonly Color CStratNeutral = new(0.6f, 0.6f, 0.6f);
    private static readonly Color CBlue = new(0.3f, 0.6f, 1f);
    private static readonly Color CRed = new(1f, 0.3f, 0.3f);
    private static readonly Color CBlueSel = new(0.5f, 0.85f, 1f);
    private static readonly Color CBlueHarv = new(0.15f, 0.4f, 0.75f);
    private static readonly Color CRedHarv = new(0.75f, 0.15f, 0.15f);
    private static readonly Color CCamRect = new(1f, 1f, 1f, 0.6f);

    /// <summary>初始化：设置 Main 和 Camera 引用，锚定左下角。</summary>
    public void Setup(Main main, RTSCamera camera)
    {
        _main = main;
        _camera = camera;

        // 缓存节点
        _obstaclesNode = main.GetNodeOrNull<Node2D>("Obstacles");
        _resourcesNode = main.GetNodeOrNull<Node2D>("Resources");
        _strategicPointsNode = main.GetNodeOrNull<Node2D>("StrategicPoints");
        _buildingsNode = main.GetNodeOrNull<Node2D>("Buildings");
        _unitsNode = main.GetNodeOrNull<Node2D>("Units");

        // 锚定左下角
        AnchorLeft = 0; AnchorTop = 1; AnchorRight = 0; AnchorBottom = 1;
        OffsetLeft = 10; OffsetTop = -(MmSize + 10);
        OffsetRight = 10 + MmSize; OffsetBottom = -10;

        CustomMinimumSize = new Vector2(MmSize, MmSize);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>判断屏幕坐标是否在小地图区域内。</summary>
    public bool ContainsScreenPos(Vector2 screenPos) => GetGlobalRect().HasPoint(screenPos);

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_main == null) return;

        // --- 背景 ---
        DrawRect(new Rect2(0, 0, MmSize, MmSize), CBg, true);

        // --- 障碍物 ---
        if (_obstaclesNode != null)
        {
            foreach (var c in _obstaclesNode.GetChildren())
            {
                if (c is StaticBody2D sb && GodotObject.IsInstanceValid(sb))
                {
                    var mp = W2M(sb.GlobalPosition);
                    float sz = 3f;
                    foreach (var ch in sb.GetChildren())
                    {
                        if (ch is CollisionShape2D cs && cs.Shape is RectangleShape2D r)
                        {
                            sz = Mathf.Max(r.Size.X, r.Size.Y) * S;
                            break;
                        }
                    }
                    sz = Mathf.Max(sz, 2f);
                    DrawRect(new Rect2(mp - new Vector2(sz / 2, sz / 2), sz, sz), CObstacle, true);
                }
            }
        }

        // --- 矿点 ---
        if (_resourcesNode != null)
        {
            foreach (var c in _resourcesNode.GetChildren())
            {
                if (c is ResourceNode rn && GodotObject.IsInstanceValid(rn) && !rn.IsDepleted)
                {
                    var mp = W2M(rn.GlobalPosition);
                    DrawCircle(mp, 2f, rn.Amount > 500 ? COre : COreDim);
                }
            }
        }

        // --- 战略要地 ---
        if (_strategicPointsNode != null)
        {
            foreach (var c in _strategicPointsNode.GetChildren())
            {
                if (c is StrategicPoint sp && GodotObject.IsInstanceValid(sp))
                {
                    var mp = W2M(sp.GlobalPosition);
                    var col = sp.OwningTeam == 0 ? CBlue : (sp.OwningTeam == 1 ? CRed : CStratNeutral);
                    DrawCircle(mp, 3f, col);
                    DrawLine(mp - new Vector2(4, 0), mp + new Vector2(4, 0), col, 1f);
                    DrawLine(mp - new Vector2(0, 4), mp + new Vector2(0, 4), col, 1f);
                }
            }
        }

        // --- 建筑 ---
        if (_buildingsNode != null)
        {
            foreach (var c in _buildingsNode.GetChildren())
            {
                if (c is Building b && GodotObject.IsInstanceValid(b))
                {
                    var mp = W2M(b.GlobalPosition);
                    var col = b.TeamId == 0 ? CBlue : CRed;
                    float sz = b.Type == BuildingType.Base ? 6f : 4f;
                    DrawRect(new Rect2(mp - new Vector2(sz / 2, sz / 2), sz, sz), col, true);
                }
            }
        }

        // --- 单位（矿车优先判断，再战斗单位） ---
        if (_unitsNode != null)
        {
            foreach (var c in _unitsNode.GetChildren())
            {
                if (!GodotObject.IsInstanceValid(c)) continue;

                if (c is Harvester h)
                {
                    var mp = W2M(h.GlobalPosition);
                    DrawCircle(mp, 1.5f, h.TeamId == 0 ? CBlueHarv : CRedHarv);
                }
                else if (c is Unit u)
                {
                    var mp = W2M(u.GlobalPosition);
                    var col = u.TeamId == 0 ? (u.IsSelected ? CBlueSel : CBlue) : CRed;
                    float r = u.Type == UnitType.HeavyTank || u.Type == UnitType.MissileTank ? 2f : 1.5f;
                    DrawCircle(mp, r, col);
                }
            }
        }

        // --- 相机视口矩形 ---
        var vpSize = GetViewportRect().Size;
        var zoom = _camera.Zoom;
        var worldVp = vpSize / zoom;
        var camTL = _camera.Position - worldVp / 2;
        var mmTL = W2M(camTL);
        var mmSz = worldVp * S;
        DrawRect(new Rect2(mmTL, mmSz), CCamRect, false, 1.5f);

        // --- 边框 ---
        DrawRect(new Rect2(0, 0, MmSize, MmSize), CBorder, false, 2f);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_camera == null) return;

        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            JumpCamera(mb.Position);
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion mm && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            JumpCamera(mm.Position);
            AcceptEvent();
        }
    }

    private void JumpCamera(Vector2 mmPos)
    {
        var world = M2W(mmPos);
        _camera.Position = new Vector2(Mathf.Clamp(world.X, 0, MapSize), Mathf.Clamp(world.Y, 0, MapSize));
    }

    private Vector2 W2M(Vector2 w) => new(w.X * S, w.Y * S);
    private Vector2 M2W(Vector2 m) => new(m.X / S, m.Y / S);
}
