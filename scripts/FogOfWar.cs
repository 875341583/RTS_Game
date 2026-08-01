using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 战争迷雾系统：基于单位视野的瓦片级可见性管理。
/// 三态：Unexplored（黑）→ Explored（暗灰，静态记忆）→ Visible（清晰，实时）。
/// 每帧从友方单位/建筑收集视野圆，刷新 Visible 格子；
/// 已探索格子不退回未探索，只从 Visible 退回 Explored。
/// 渲染：Node2D + DrawPolygon 覆盖等距菱形瓦片。
/// </summary>
public partial class FogOfWar : Node2D
{
    /// <summary>迷雾状态枚举。</summary>
    public enum FogState : byte { Unexplored, Explored, Visible }

    /// <summary>每阵营的迷雾状态表。key=teamId, value=格子状态二维数组。</summary>
    private readonly Dictionary<int, FogState[,]> _fogData = new();

    /// <summary>每阵营的已探索格子集合（快速判断，避免遍历整个数组）。</summary>
    private readonly Dictionary<int, HashSet<Vector2I>> _explored = new();

    /// <summary>当前查看的阵营ID（默认玩家=0）。</summary>
    public int ViewerTeamId { get; set; } = 0;

    /// <summary>地图尺寸（格子数）。</summary>
    private int _mapSize;

    /// <summary>视野圆缓存（避免每帧重算）。</summary>
    private static Vector2I[]? _visionCircle;
    private static int _cachedVisionRadius = -1;

    /// <summary>迷雾是否启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>默认单位视野半径（格）。</summary>
    public const int DefaultVisionRadius = 6;

    /// <summary>建筑视野半径（格）。</summary>
    public const int BuildingVisionRadius = 5;

    /// <summary>迷雾更新间隔（秒），每0.2秒刷新一次以节省性能。</summary>
    private float _updateInterval = 0.2f;
    private float _updateTimer = 0f;

    /// <summary>菱形顶点数组（用于DrawPolygon）。</summary>
    private static readonly Vector2[] DiamondPoints = IsoCoords.DiamondVerts;

    /// <summary>Unexplored 迷雾颜色（RA2风格深蓝灰，非纯黑，保留氛围感）。</summary>
    private static readonly Color ColorUnexplored = new(0.02f, 0.03f, 0.06f, 0.96f);

    /// <summary>Explored 迷雾颜色（暗蓝灰半透明，地形隐约可见）。</summary>
    private static readonly Color ColorExplored = new(0.02f, 0.03f, 0.06f, 0.5f);

    /// <summary>初始化迷雾数据。由 Main._Ready 调用。</summary>
    public void Initialize(int mapSize)
    {
        _mapSize = mapSize;
        _fogData.Clear();
        _explored.Clear();

        ZIndex = RenderLayer.Shroud;
        YSortEnabled = false;

        for (int t = 0; t < 8; t++)
        {
            _fogData[t] = new FogState[mapSize, mapSize];
            _explored[t] = new HashSet<Vector2I>();
        }
    }

    /// <summary>获取指定阵营在指定格子的迷雾状态。</summary>
    public FogState GetState(int teamId, int x, int y)
    {
        if (x < 0 || y < 0 || x >= _mapSize || y >= _mapSize) return FogState.Visible;
        if (!_fogData.TryGetValue(teamId, out var data)) return FogState.Visible;
        return data[x, y];
    }

    /// <summary>指定坐标是否对指定阵营可见（不处于迷雾中）。</summary>
    public bool IsVisible(int teamId, int x, int y) => GetState(teamId, x, y) == FogState.Visible;

    /// <summary>指定坐标是否已被探索（至少探索过一次）。</summary>
    public bool IsExplored(int teamId, int x, int y)
    {
        var state = GetState(teamId, x, y);
        return state == FogState.Explored || state == FogState.Visible;
    }

    /// <summary>每帧刷新可见性。由 Main._Process 调用。</summary>
    public void UpdateVisibility(List<Unit> units, List<Building> buildings, double delta)
    {
        if (!Enabled) return;

        // 节流：每_updateInterval秒刷新一次
        _updateTimer += (float)delta;
        if (_updateTimer < _updateInterval) return;
        _updateTimer = 0f;

        int teamId = ViewerTeamId;
        if (!_fogData.TryGetValue(teamId, out var data)) return;

        // 1. 将所有 Visible 退回 Explored
        var explored = _explored[teamId];
        for (int x = 0; x < _mapSize; x++)
        {
            for (int y = 0; y < _mapSize; y++)
            {
                if (data[x, y] == FogState.Visible)
                    data[x, y] = FogState.Explored;
            }
        }

        // 2. 收集友方视野源
        var sources = new List<(Vector2 worldPos, int radius)>();

        foreach (var unit in units)
        {
            if (unit.TeamId == teamId && !unit.IsDead)
                sources.Add((unit.GlobalPosition, DefaultVisionRadius));
        }

        foreach (var bld in buildings)
        {
            if (bld.TeamId == teamId && !bld.IsDead)
                sources.Add((bld.GlobalPosition, BuildingVisionRadius));
        }

        // 3. 标记视野内格子为 Visible
        foreach (var (worldPos, radius) in sources)
        {
            var circle = GetVisionCircle(radius);
            Vector2I center = WorldToGrid(worldPos);

            foreach (var offset in circle)
            {
                int gx = center.X + offset.X;
                int gy = center.Y + offset.Y;
                if (gx < 0 || gy < 0 || gx >= _mapSize || gy >= _mapSize) continue;
                data[gx, gy] = FogState.Visible;
                explored.Add(new Vector2I(gx, gy));
            }
        }

        // 4. 触发重绘
        QueueRedraw();
    }

    /// <summary>渲染迷雾（Node2D _Draw）。</summary>
    public override void _Draw()
    {
        if (!Enabled) return;

        int teamId = ViewerTeamId;
        if (!_fogData.TryGetValue(teamId, out var data)) return;

        for (int x = 0; x < _mapSize; x++)
        {
            for (int y = 0; y < _mapSize; y++)
            {
                var state = data[x, y];
                if (state == FogState.Visible) continue;

                // 网格坐标 → 等距屏幕坐标
                var center = IsoCoords.GridToScreen(x, y);
                var color = state == FogState.Unexplored ? ColorUnexplored : ColorExplored;

                // 绘制菱形遮罩（偏移顶点到格子中心位置）
                var offsetPoints = new Vector2[DiamondPoints.Length];
                for (int i = 0; i < DiamondPoints.Length; i++)
                    offsetPoints[i] = DiamondPoints[i] + center;
                DrawColoredPolygon(offsetPoints, color);
            }
        }
    }

    /// <summary>世界坐标→网格坐标（等距坐标系）。</summary>
    private static Vector2I WorldToGrid(Vector2 worldPos)
    {
        var grid = IsoCoords.ScreenToGridF(worldPos.X, worldPos.Y);
        return new Vector2I((int)grid.X, (int)grid.Y);
    }

    /// <summary>生成视野圆偏移数组（缓存）。</summary>
    private static Vector2I[] GetVisionCircle(int radius)
    {
        if (radius == _cachedVisionRadius && _visionCircle != null)
            return _visionCircle;

        var list = new List<Vector2I>();
        int r2 = radius * radius;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (dx * dx + dy * dy <= r2)
                    list.Add(new Vector2I(dx, dy));
            }
        }

        _visionCircle = list.ToArray();
        _cachedVisionRadius = radius;
        return _visionCircle;
    }

    /// <summary>强制揭示指定区域（调试/作弊用）。</summary>
    public void RevealArea(int teamId, int cx, int cy, int radius)
    {
        if (!_fogData.TryGetValue(teamId, out var data)) return;
        var circle = GetVisionCircle(radius);
        var explored = _explored[teamId];
        foreach (var off in circle)
        {
            int gx = cx + off.X;
            int gy = cy + off.Y;
            if (gx < 0 || gy < 0 || gx >= _mapSize || gy >= _mapSize) continue;
            data[gx, gy] = FogState.Visible;
            explored.Add(new Vector2I(gx, gy));
        }
        QueueRedraw();
    }

    /// <summary>完全揭示全图（调试/观战用）。</summary>
    public void RevealAll(int teamId)
    {
        if (!_fogData.TryGetValue(teamId, out var data)) return;
        for (int x = 0; x < _mapSize; x++)
            for (int y = 0; y < _mapSize; y++)
                data[x, y] = FogState.Visible;
        QueueRedraw();
    }
}
