using System;
using Godot;

namespace RTSGame;

/// <summary>
/// Q2 小地图：全图态势面板（RA2 风格）。
/// 左下角固定，显示蓝/红方单位、建筑、矿点、战略点、障碍物。
/// 点击/拖动小地图跳转视角，金色虚线矩形显示当前视口范围。
/// 金属边框 + 铆钉 + 雷达扫描线 + 四角 L 形角标 + 网格底板。
/// </summary>
public partial class Minimap : Control
{
    private Main? _main;
    private RTSCamera? _camera;

    // 缓存节点引用
    private Node2D? _obstaclesNode;
    private Node2D? _resourcesNode;
    private Node2D? _strategicPointsNode;
    private Node2D? _buildingsNode;
    private Node2D? _unitsNode;

    // 地图与小地图参数
    private static float MapSize => MapConfig.MapPixelSize;
    private const float MmSize = 180f;
    private static float S => MmSize / MapSize;

    // 原始颜色
    private static readonly Color CObstacle = new(0.22f, 0.22f, 0.28f);
    private static readonly Color COre = new(1f, 0.85f, 0f);
    private static readonly Color COreDim = new(0.5f, 0.42f, 0f);
    private static readonly Color CStratNeutral = new(0.6f, 0.6f, 0.6f);
    private static readonly Color CBlue = new(0.3f, 0.6f, 1f);
    private static readonly Color CRed = new(1f, 0.3f, 0.3f);
    private static readonly Color CBlueSel = new(0.5f, 0.85f, 1f);
    private static readonly Color CBlueHarv = new(0.15f, 0.4f, 0.75f);
    private static readonly Color CRedHarv = new(0.75f, 0.15f, 0.15f);

    // RA2 风格配色
    private const float MmMargin = 6f; // 边框总厚度（外3 + 间距2 + 内1）
    private static readonly Color CBg = new(0.05f, 0.08f, 0.05f);
    private static readonly Color COuterBorder = new(0.5f, 0.5f, 0.55f);
    private static readonly Color CInnerBorder = new(0.15f, 0.15f, 0.18f);
    private static readonly Color CRivet = new(0.75f, 0.75f, 0.8f);
    private static readonly Color CLCorner = new(0.7f, 0.7f, 0.75f, 0.8f);
    private static readonly Color CGrid = new(0.08f, 0.14f, 0.08f);
    private static readonly Color CSweepLine = new(0.2f, 0.8f, 0.3f, 0.15f);
    private static readonly Color CCamRectGold = new(1f, 0.85f, 0.3f, 0.7f);
    private static readonly Color CTitleText = new(0.85f, 0.85f, 0.9f, 0.9f);

    // 雷达扫描角度（弧度），在 _Process 中累积
    private float _radarSweepAngle;

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

        // 锚定左下角（内容区为 MmSize×MmSize，外加边框）
        var totalSize = MmSize + MmMargin * 2;
        AnchorLeft = 0; AnchorTop = 1; AnchorRight = 0; AnchorBottom = 1;
        OffsetLeft = 10; OffsetTop = -(totalSize + 10);
        OffsetRight = 10 + totalSize; OffsetBottom = -10;

        CustomMinimumSize = new Vector2(totalSize, totalSize);
        MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>判断屏幕坐标是否在小地图区域内。</summary>
    public bool ContainsScreenPos(Vector2 screenPos) => GetGlobalRect().HasPoint(screenPos);

    public override void _Process(double delta)
    {
        // 6 秒一圈
        _radarSweepAngle += (float)(delta * Mathf.Tau / 6.0);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_main == null) return;

        var totalSize = MmSize + MmMargin * 2;
        var contentOrigin = new Vector2(MmMargin, MmMargin);

        // ============ 1. 金属外边框 ============
        // 外圈：钢银色 3px 矩形边框
        DrawRect(new Rect2(0, 0, totalSize, totalSize), COuterBorder, false, 3f);
        // 内圈：暗色 1px 矩形边框（位于内容区边界）
        DrawRect(new Rect2(contentOrigin, new Vector2(MmSize, MmSize)), CInnerBorder, false, 1f);

        // 四角铆钉圆点
        DrawRivets(totalSize);

        // ============ 标题 "战术地图" ============
        DrawTitle(totalSize);

        // 以下内容均绘制在 contentOrigin 偏移的坐标系内（0..MmSize）
        // 通过 DrawSetTransformMatrix 实现局部坐标
        DrawSetTransformMatrix(Transform2D.Identity.Translated(contentOrigin));

        // ============ 3. 暗色底板 + 网格 ============
        DrawRect(new Rect2(0, 0, MmSize, MmSize), CBg, true);
        DrawGrid();

        // ============ 2. 雷达扫描线 ============
        DrawRadarSweep();

        // ============ 障碍物 ============
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

        // ============ 矿点 ============
        if (_resourcesNode != null)
        {
            foreach (var c in _resourcesNode.GetChildren())
            {
                if (c is ResourceNode rn && GodotObject.IsInstanceValid(rn) && !rn.IsDepleted)
                {
                    var mp = W2M(rn.GlobalPosition);
                    Color oreColor;
                    switch (rn.ResourceType)
                    {
                        case ResourceType.RareMineral:
                            oreColor = new Color(0.6f, 0.4f, 0.9f); // 紫蓝
                            break;
                        case ResourceType.OilField:
                            oreColor = rn.OilOwner == 0 ? new Color(0.3f, 0.6f, 1.0f) :
                                       rn.OilOwner == 1 ? new Color(1.0f, 0.35f, 0.35f) :
                                       new Color(0.4f, 0.7f, 0.3f); // 绿色=中立油田
                            break;
                        case ResourceType.LandVein:
                            oreColor = new Color(0.7f, 0.55f, 0.35f); // 淡铜色
                            break;
                        default:
                            oreColor = rn.Amount > 500 ? COre : COreDim; // 金矿
                            break;
                    }
                    DrawCircle(mp, rn.ResourceType == ResourceType.OilField ? 3f : 2f, oreColor);
                }
            }
        }

        // ============ 战略要地 ============
        if (_strategicPointsNode != null)
        {
            foreach (var c in _strategicPointsNode.GetChildren())
            {
                if (c is StrategicPoint sp && GodotObject.IsInstanceValid(sp))
                {
                    var mp = W2M(sp.GlobalPosition);
                    var col = sp.OwningTeam == Main.PlayerTeamId ? CBlue : (sp.OwningTeam == 1 ? CRed : CStratNeutral);
                    DrawCircle(mp, 3f, col);
                    DrawLine(mp - new Vector2(4, 0), mp + new Vector2(4, 0), col, 1f);
                    DrawLine(mp - new Vector2(0, 4), mp + new Vector2(0, 4), col, 1f);
                }
            }
        }

        // ============ 建筑 ============
        if (_buildingsNode != null)
        {
            foreach (var c in _buildingsNode.GetChildren())
            {
                if (c is Building b && GodotObject.IsInstanceValid(b))
                {
                    var mp = W2M(b.GlobalPosition);
                    var col = b.TeamId == Main.PlayerTeamId ? CBlue : CRed;
                    float sz = b.Type == BuildingType.Base ? 6f : 4f;
                    DrawRect(new Rect2(mp - new Vector2(sz / 2, sz / 2), sz, sz), col, true);
                }
            }
        }

        // ============ 单位（矿车优先判断，再战斗单位） ============
        if (_unitsNode != null)
        {
            foreach (var c in _unitsNode.GetChildren())
            {
                if (!GodotObject.IsInstanceValid(c)) continue;

                if (c is Harvester h)
                {
                    var mp = W2M(h.GlobalPosition);
                    DrawCircle(mp, 1.5f, h.TeamId == Main.PlayerTeamId ? CBlueHarv : CRedHarv);
                }
                else if (c is Unit u)
                {
                    var mp = W2M(u.GlobalPosition);
                    var col = u.TeamId == Main.PlayerTeamId ? (u.IsSelected ? CBlueSel : CBlue) : CRed;
                    float r = u.Type == UnitType.HeavyTank || u.Type == UnitType.MissileTank ? 2f : 1.5f;
                    DrawCircle(mp, r, col);
                }
            }
        }

        // ============ 5. 相机视口矩形（金色虚线） ============
        DrawCameraViewportDashed();

        // ============ 4. 四角 L 形角标 ============
        DrawLCorners();

        // 复位变换
        DrawSetTransformMatrix(Transform2D.Identity);
    }

    /// <summary>绘制四角铆钉圆点（亮银色，半径 2px）。</summary>
    private void DrawRivets(float totalSize)
    {
        float offset = 4f;
        var positions = new Vector2[]
        {
            new(offset, offset),
            new(totalSize - offset, offset),
            new(offset, totalSize - offset),
            new(totalSize - offset, totalSize - offset),
        };
        foreach (var p in positions)
        {
            DrawCircle(p, 2f, CRivet);
        }
    }

    /// <summary>绘制顶部 "战术地图" 标题（军事风格）。</summary>
    private void DrawTitle(float totalSize)
    {
        var font = GetWindow().GetThemeDefaultFont();
        if (font == null) return;
        const string title = "Tactical Map";
        const float fontSize = 11f;
        const float offsetY = -2f;
        var textSize = font.GetStringSize(title, HorizontalAlignment.Left, fontSize);
        var titlePos = new Vector2(totalSize / 2 - textSize.X / 2, offsetY);
        // 背景条
        DrawRect(new Rect2(0, 0, totalSize, 14f), new Color(0.05f, 0.05f, 0.07f, 0.85f), true);
        DrawString(font, new Vector2(titlePos.X, titlePos.Y + fontSize), title,
            HorizontalAlignment.Left, fontSize);
    }

    /// <summary>绘制暗色底板上的细微网格线（每 20px 一条）。</summary>
    private void DrawGrid()
    {
        for (float x = 20f; x < MmSize; x += 20f)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x, MmSize), CGrid, 1f);
        }
        for (float y = 20f; y < MmSize; y += 20f)
        {
            DrawLine(new Vector2(0, y), new Vector2(MmSize, y), CGrid, 1f);
        }
    }

    /// <summary>
    /// 绘制雷达扫描线：从中心向外的旋转线 + 渐变扇形。
    /// 角度跨度约 30 度，越远越淡。
    /// </summary>
    private void DrawRadarSweep()
    {
        var center = new Vector2(MmSize / 2f, MmSize / 2f);
        float radius = MmSize / 2f;

        // 主扫描线
        var edge = center + new Vector2(Mathf.Cos(_radarSweepAngle), Mathf.Sin(_radarSweepAngle)) * radius;
        DrawLine(center, edge, CSweepLine, 1.5f);

        // 渐变扇形：用多条线模拟，跨度约 30 度（落后于主线）
        const int segments = 12; // 扇形细分
        float spread = Mathf.DegToRad(30f);
        for (int i = 1; i <= segments; i++)
        {
            float t = (float)i / segments; // 0..1，越远离主线越淡
            float a = _radarSweepAngle - spread * t;
            float alpha = CSweepLine.A * (1f - t) * 0.7f; // 逐渐变淡
            var end = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            DrawLine(center, end, new Color(CSweepLine.R, CSweepLine.G, CSweepLine.B, alpha), 1f);
        }
    }

    /// <summary>绘制内容区域四角的 L 形角标（军事 HUD 风格）。</summary>
    private void DrawLCorners()
    {
        const float len = 10f;
        const float inset = 2f;
        float w = 1.5f;

        // 左上
        DrawLine(new Vector2(inset, inset), new Vector2(inset + len, inset), CLCorner, w);
        DrawLine(new Vector2(inset, inset), new Vector2(inset, inset + len), CLCorner, w);
        // 右上
        DrawLine(new Vector2(MmSize - inset - len, inset), new Vector2(MmSize - inset, inset), CLCorner, w);
        DrawLine(new Vector2(MmSize - inset, inset), new Vector2(MmSize - inset, inset + len), CLCorner, w);
        // 左下
        DrawLine(new Vector2(inset, MmSize - inset), new Vector2(inset + len, MmSize - inset), CLCorner, w);
        DrawLine(new Vector2(inset, MmSize - inset - len), new Vector2(inset, MmSize - inset), CLCorner, w);
        // 右下
        DrawLine(new Vector2(MmSize - inset - len, MmSize - inset), new Vector2(MmSize - inset, MmSize - inset), CLCorner, w);
        DrawLine(new Vector2(MmSize - inset, MmSize - inset - len), new Vector2(MmSize - inset, MmSize - inset), CLCorner, w);
    }

    /// <summary>绘制金色虚线相机视口矩形。</summary>
    private void DrawCameraViewportDashed()
    {
        var vpSize = GetViewportRect().Size;
        var zoom = _camera!.Zoom;
        var worldVp = vpSize / zoom;
        var camTL = _camera!.Position - worldVp / 2;
        var mmTL = W2M(camTL);
        var mmSz = worldVp * S;

        // 裁剪到小地图范围
        var rect = new Rect2(mmTL, mmSz);
        // 用四条虚线边绘制
        DrawDashedLine(rect.Position, new Vector2(rect.Position.X + rect.Size.X, rect.Position.Y), CCamRectGold, 1.5f); // top
        DrawDashedLine(new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y), rect.Position + rect.Size, CCamRectGold, 1.5f); // bottom
        DrawDashedLine(rect.Position, new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y), CCamRectGold, 1.5f); // left
        DrawDashedLine(new Vector2(rect.Position.X + rect.Size.X, rect.Position.Y), rect.Position + rect.Size, CCamRectGold, 1.5f); // right
    }

    /// <summary>绘制虚线段。</summary>
    private void DrawDashedLine(Vector2 from, Vector2 to, Color color, float width, float dashLen = 4f, float gapLen = 3f)
    {
        var dir = (to - from);
        float totalLen = dir.Length();
        if (totalLen < 0.001f) return;
        dir /= totalLen; // 单位向量
        float pos = 0f;
        while (pos < totalLen)
        {
            float segEnd = Mathf.Min(pos + dashLen, totalLen);
            DrawLine(from + dir * pos, from + dir * segEnd, color, width);
            pos += dashLen + gapLen;
        }
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
        // mmPos 来自 _GuiInput，是相对控件左上角的本地坐标
        // 由于使用了 DrawSetTransformMatrix 偏移，点击坐标需减去 MmMargin 转换到内容区坐标
        var contentPos = mmPos - new Vector2(MmMargin, MmMargin);
        var world = M2W(contentPos);
        _camera!.Position = new Vector2(Mathf.Clamp(world.X, 0, MapSize), Mathf.Clamp(world.Y, 0, MapSize));
    }

    private Vector2 W2M(Vector2 w) => new(w.X * S, w.Y * S);
    private Vector2 M2W(Vector2 m) => new(m.X / S, m.Y / S);
}
