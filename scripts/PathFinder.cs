using Godot;
using System.Collections.Generic;

namespace RTSGame
{
    /// <summary>
    /// 栅格A*寻路器（P0-1）。基于TerrainGrid动态尺寸栅格，支持8方向移动、
    /// 动态建筑障碍、地形可通行性检查和路径平滑（视线优化）。
    /// P0修复：使用PriorityQueue替代List线性搜索，寻路复杂度从O(V²)降至O(E log V)。
    /// 设计目标：消除单位直线移动导致的卡墙/堵路问题。
    /// </summary>
    public class PathFinder : IPathFinder
    {
        // ========== 依赖 ==========
        private readonly TerrainGrid _terrain;

        // ========== 障碍数据（M6修复：引用计数，避免重叠区域错误解锁） ==========
        private readonly int[,] _buildingBlocked;

    // ========== A*常量 ==========
    private const int StraightCost = 10;
    private const int DiagonalCost = 14; // ≈10*√2

    // ========== A*工作数组（P2-2: 动态大小，EnsureWorkArrays时按GridSize分配） ==========
    private int[,] _gCost = null!;
    private int[,] _hCost = null!;
    private int[,] _parentX = null!;
    private int[,] _parentY = null!;
    private bool[,] _closed = null!;
    private bool[,] _opened = null!;
    // P0修复：优先队列替代List线性搜索。F值作为优先级，相同F选H更小（更接近终点）。
    // 用 (F << 16) | H 组合键实现tie-breaking（假设F和H各不超过65535）。
    private PriorityQueue<(int x, int y), int> _openQueue = null!;
    private int _gs; // 当前GridSize快照

    private void EnsureWorkArrays()
    {
        int gs = TerrainGrid.GridSize;
        if (_gs == gs && _gCost != null) return;
        _gs = gs;
        _gCost = new int[gs, gs];
        _hCost = new int[gs, gs];
        _parentX = new int[gs, gs];
        _parentY = new int[gs, gs];
        _closed = new bool[gs, gs];
        _opened = new bool[gs, gs];
        _openQueue = new PriorityQueue<(int x, int y), int>(gs * gs);
    }

        // 8方向偏移：先直线后对角线
        private static readonly (int dx, int dy, bool diagonal)[] _neighbors =
        {
            (1, 0, false), (-1, 0, false), (0, 1, false), (0, -1, false),
            (1, 1, true), (1, -1, true), (-1, 1, true), (-1, -1, true),
        };

        /// <summary>路径点到达阈值（等距屏幕坐标像素）。</summary>
        private const float WaypointThreshold = 12f;

    public PathFinder(TerrainGrid terrain)
    {
        _terrain = terrain;
        EnsureWorkArrays();
        _buildingBlocked = new int[_gs, _gs];
    }

        // ========== 障碍管理 ==========

        /// <summary>标记建筑占据的格子为障碍。radius=1→3×3区域。</summary>
        public void AddBuilding(int centerGx, int centerGy, int radius)
        {
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int x = centerGx + dx, y = centerGy + dy;
                    if (InBounds(x, y)) _buildingBlocked[x, y]++;
                }
        }

        /// <summary>取消建筑障碍标记（引用计数，重叠区域安全）。</summary>
        public void RemoveBuilding(int centerGx, int centerGy, int radius)
        {
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int x = centerGx + dx, y = centerGy + dy;
                    if (InBounds(x, y) && _buildingBlocked[x, y] > 0) _buildingBlocked[x, y]--;
                }
        }

        /// <summary>清空所有建筑障碍（地图重置时调用）。</summary>
        public void ClearBuildings()
        {
            System.Array.Clear(_buildingBlocked, 0, _buildingBlocked.Length);
        }

        /// <summary>检查指定格子是否被建筑占据。</summary>
        public bool IsBuildingBlocked(int gx, int gy) => InBounds(gx, gy) && _buildingBlocked[gx, gy] > 0;

        // ========== P1-5: IPathFinder 显式接口实现 ==========
        // 2D 栅格寻路特有 TerrainUnitCategory 参数；接口通用版用 Ground 默认类别适配。
        // 现有 3 参数 FindPath 公开方法保持不变，Main 调用不受影响。

        /// <summary>IPathFinder: 通用寻路（默认 Infantry 地形类别，通行性最宽松）。</summary>
        List<Vector2> IPathFinder.FindPath(Vector2 startWorld, Vector2 endWorld)
            => FindPath(startWorld, endWorld, TerrainUnitCategory.Infantry);

        /// <summary>IPathFinder: 指定世界坐标是否可通行（按 Infantry 类别检查）。</summary>
        bool IPathFinder.IsWalkable(Vector2 worldPos)
        {
            _terrain.WorldToGrid(worldPos.X, worldPos.Y, out int gx, out int gy);
            return InBounds(gx, gy) && IsPassable(gx, gy, TerrainUnitCategory.Infantry);
        }

        // ========== 寻路接口 ==========

        /// <summary>
        /// 寻路：从起点世界坐标到终点世界坐标，返回路径点列表（等距屏幕坐标）。
        /// 空列表表示无可行路径。路径点为格子中心，已做视线平滑。
        /// </summary>
        public List<Vector2> FindPath(Vector2 startWorld, Vector2 endWorld, TerrainUnitCategory cat)
        {
            EnsureWorkArrays();
            _terrain.WorldToGrid(startWorld.X, startWorld.Y, out int sx, out int sy);
            _terrain.WorldToGrid(endWorld.X, endWorld.Y, out int ex, out int ey);

            if (!InBounds(sx, sy) || !InBounds(ex, ey)) return new List<Vector2>();

            // 起点和终点不可通行时寻找最近可通行格
            if (!IsPassable(sx, sy, cat))
            {
                if (!FindNearestPassable(sx, sy, cat, out sx, out sy))
                    return new List<Vector2>();
            }
            if (!IsPassable(ex, ey, cat))
            {
                if (!FindNearestPassable(ex, ey, cat, out ex, out ey))
                    return new List<Vector2>();
            }

            // 起点终点同格→直接返回终点
            if (sx == ex && sy == ey)
            {
                return new List<Vector2> { IsoCoords.GridToScreenF(ex + 0.5f, ey + 0.5f) };
            }

            var gridPath = AStarSearch(sx, sy, ex, ey, cat);
            if (gridPath == null || gridPath.Count == 0) return new List<Vector2>();

            // 视线平滑
            var smoothed = SmoothPath(gridPath, cat);

            // H3修复：路径长度上限，避免极端碎片化地图产生超长路径
            if (smoothed.Count > 48)
            {
                int step = (int)System.Math.Ceiling(smoothed.Count / 48f);
                var compressed = new List<(int x, int y)>();
                for (int i = 0; i < smoothed.Count; i += step)
                    compressed.Add(smoothed[i]);
                compressed.Add(smoothed[smoothed.Count - 1]);
                smoothed = compressed;
            }

            // M2修复：首点用实际起点世界坐标，避免起点偏移导致的瞬移感
            var result = new List<Vector2>(smoothed.Count);
            result.Add(startWorld);
            for (int i = 1; i < smoothed.Count; i++)
                result.Add(IsoCoords.GridToScreenF(smoothed[i].x + 0.5f, smoothed[i].y + 0.5f));

            return result;
        }

        /// <summary>获取路径点到达阈值，供Unit路径跟随时使用。</summary>
        public static float GetWaypointThreshold() => WaypointThreshold;

        // ========== A*核心 ==========

        private List<(int x, int y)>? AStarSearch(int sx, int sy, int ex, int ey, TerrainUnitCategory cat)
        {
            // 重置工作数组
            System.Array.Clear(_closed, 0, _closed.Length);
            System.Array.Clear(_opened, 0, _opened.Length);
            _openQueue.Clear();

            // 初始化起点
            _gCost[sx, sy] = 0;
            _hCost[sx, sy] = Heuristic(sx, sy, ex, ey);
            _parentX[sx, sy] = sx;
            _parentY[sx, sy] = sy;
            _opened[sx, sy] = true;
            EnqueueOpen(sx, sy);

            int maxIterations = _gs * _gs * 4; // 安全上限

            while (_openQueue.Count > 0 && maxIterations-- > 0)
            {
                // P0修复：O(log n)取最小F值节点（原为O(n)线性扫描）
                _openQueue.TryDequeue(out var cur, out _);
                int cx = cur.x, cy = cur.y;

                // 延迟删除：跳过已被关闭的过时条目
                if (_closed[cx, cy]) continue;
                _opened[cx, cy] = false;

                // 到达终点
                if (cx == ex && cy == ey)
                    return ReconstructPath(sx, sy, ex, ey);

                _closed[cx, cy] = true;

                // 遍历8个邻居
                for (int i = 0; i < 8; i++)
                {
                    int nx = cx + _neighbors[i].dx;
                    int ny = cy + _neighbors[i].dy;
                    bool diagonal = _neighbors[i].diagonal;

                    if (!InBounds(nx, ny)) continue;
                    if (_closed[nx, ny]) continue;
                    if (!IsPassable(nx, ny, cat)) continue;

                    // 对角线穿墙检查：两侧格子必须可通行
                    if (diagonal)
                    {
                        if (!IsPassable(cx + _neighbors[i].dx, cy, cat)) continue;
                        if (!IsPassable(cx, cy + _neighbors[i].dy, cat)) continue;
                    }

                    // 基础移动代价
                    int baseCost = diagonal ? DiagonalCost : StraightCost;
                    int moveCost = _gCost[cx, cy] + baseCost;

                    // 地形速度修正作为额外代价（慢地形代价更高，鼓励走快路）
                    float speedMod = _terrain.GetMovementSpeed(cat, cx, cy, nx, ny);
                    if (speedMod <= 0f) continue;
                    // L3修复：地形惩罚基于实际基础代价计算，对角线和直线一致
                    int terrainPenalty = (int)(baseCost / Mathf.Clamp(speedMod, 0.25f, 4f)) - baseCost;
                    moveCost += terrainPenalty;

                    if (!_opened[nx, ny])
                    {
                        _gCost[nx, ny] = moveCost;
                        _hCost[nx, ny] = Heuristic(nx, ny, ex, ey);
                        _parentX[nx, ny] = cx;
                        _parentY[nx, ny] = cy;
                        _opened[nx, ny] = true;
                        EnqueueOpen(nx, ny);
                    }
                    else if (moveCost < _gCost[nx, ny])
                    {
                        // 找到更短路径→更新gCost并重新入队（旧条目通过_closed标记延迟删除）
                        _gCost[nx, ny] = moveCost;
                        _parentX[nx, ny] = cx;
                        _parentY[nx, ny] = cy;
                        EnqueueOpen(nx, ny);
                    }
                }
            }

            return null; // 无路径
        }

        /// <summary>P0修复：将节点加入优先队列，优先级 = (F << 16) | H，实现F相同时选H更小。</summary>
        private void EnqueueOpen(int x, int y)
        {
            int f = _gCost[x, y] + _hCost[x, y];
            int h = _hCost[x, y];
            _openQueue.Enqueue((x, y), (f << 16) | h);
        }

        private List<(int x, int y)> ReconstructPath(int sx, int sy, int ex, int ey)
        {
            var path = new List<(int x, int y)>();
            int cx = ex, cy = ey;
            int safety = _gs * _gs;
            while ((cx != sx || cy != sy) && safety-- > 0)
            {
                path.Add((cx, cy));
                int px = _parentX[cx, cy];
                int py = _parentY[cx, cy];
                cx = px;
                cy = py;
            }
            path.Add((sx, sy));
            path.Reverse();
            return path;
        }

        // ========== 路径平滑（视线优化） ==========

        /// <summary>
        /// 视线平滑：从起点出发，找能直线看到的最远路径点，跳过中间点。
        /// 减少锯齿走位，让路径更自然。
        /// </summary>
        private List<(int x, int y)> SmoothPath(List<(int x, int y)> path, TerrainUnitCategory cat)
        {
            if (path.Count <= 2) return path;

            var result = new List<(int x, int y)> { path[0] };
            int current = 0;

            while (current < path.Count - 1)
            {
                int farthest = current + 1;
                // 从远到近找最远可见点
                for (int test = path.Count - 1; test > current + 1; test--)
                {
                    if (HasLineOfSight(path[current].x, path[current].y, path[test].x, path[test].y, cat))
                    {
                        farthest = test;
                        break;
                    }
                }
                result.Add(path[farthest]);
                current = farthest;
            }

            return result;
        }

        /// <summary>栅格视线检查（Bresenham线，所有经过格子必须可通行，H2修复：对角线步进检查两侧）。</summary>
        private bool HasLineOfSight(int x0, int y0, int x1, int y1, TerrainUnitCategory cat)
        {
            int dx = System.Math.Abs(x1 - x0);
            int dy = System.Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int x = x0, y = y0;
            int prevX = x, prevY = y;

            int safety = dx + dy + 2;
            while (safety-- > 0)
            {
                if (!IsPassable(x, y, cat)) return false;
                if (x == x1 && y == y1) return true;
                // H2修复：对角线步进时检查两侧格子（与A*对角线穿墙检查一致）
                if (x != prevX && y != prevY)
                {
                    if (!IsPassable(prevX, y, cat)) return false;
                    if (!IsPassable(x, prevY, cat)) return false;
                }
                prevX = x;
                prevY = y;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
            }
            return false;
        }

        // ========== 可通行性 ==========

        /// <summary>
        /// 检查格子是否可通行：在边界内、无建筑障碍、地形速度>0。
        /// 使用同格查询（不检查海拔差），海拔差在A*展开邻居时单独检查。
        /// </summary>
        private bool IsPassable(int gx, int gy, TerrainUnitCategory cat)
        {
            if (!InBounds(gx, gy)) return false;
            if (_buildingBlocked[gx, gy] > 0) return false;
            return _terrain.GetMovementSpeed(cat, gx, gy, gx, gy) > 0f;
        }

        /// <summary>BFS螺旋搜索最近可通行格。</summary>
        private bool FindNearestPassable(int gx, int gy, TerrainUnitCategory cat, out int rx, out int ry)
        {
            for (int r = 1; r < _gs; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        // 只检查外环
                        if (System.Math.Abs(dx) != r && System.Math.Abs(dy) != r) continue;
                        int x = gx + dx, y = gy + dy;
                        if (InBounds(x, y) && IsPassable(x, y, cat))
                        {
                            rx = x;
                            ry = y;
                            return true;
                        }
                    }
                }
            }
            rx = gx;
            ry = gy;
            return false;
        }

        // ========== 工具方法 ==========

        private bool InBounds(int x, int y) => (uint)x < _gs && (uint)y < _gs;

        /// <summary>对角线距离启发函数（与移动代价一致，保证A*最优性）。</summary>
        private static int Heuristic(int x0, int y0, int x1, int y1)
        {
            int dx = System.Math.Abs(x1 - x0);
            int dy = System.Math.Abs(y1 - y0);
            int diag = System.Math.Min(dx, dy);
            int straight = dx + dy - 2 * diag;
            return diag * DiagonalCost + straight * StraightCost;
        }
    }
}
