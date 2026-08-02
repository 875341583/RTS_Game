using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 地形类型枚举。
/// </summary>
public enum TerrainType
{
    Grass,          // 草地
    Sand,           // 沙地
    Snow,           // 雪地
    City,           // 城市路面
    Field,          // 田地
    ShallowWater,   // 浅水
    DeepWater,      // 深水
    Mountain,       // 山脉
    Road,           // 道路（铺装路面）
    Cliff,          // 悬崖（高差≥2的边界，不可通行）
    Bridge,         // 桥梁（可覆盖浅水/深水）
    Tunnel,         // 隧道（可穿过山脉）
}

/// <summary>
/// 深水宽度分类（决定可用的跨越方式）。
/// </summary>
public enum WaterWidthClass
{
    None,       // 非深水
    River,      // 河流（1-3格宽）
    Strait,     // 海峡（4-8格宽）
    Sea,        // 大海（9-15格宽）
    Ocean,      // 远洋（>15格宽）
}

/// <summary>
/// 单个地形格子数据。
/// </summary>
public struct TerrainCell
{
    public TerrainType Type;
    /// <summary>海拔等级：0=海面/深水, 1=平地/浅水, 2=高地/丘陵, 3=山顶/山脉</summary>
    public int Elevation;
    /// <summary>是否有桥梁（浅水/深水上架桥后陆战可通行）</summary>
    public bool HasBridge;
    /// <summary>是否有隧道（山脉上开隧道后可通行）</summary>
    public bool HasTunnel;
    /// <summary>深水宽度分类（仅 DeepWater 类型有效）</summary>
    public WaterWidthClass WaterWidth;
    /// <summary>所属深水连通区域ID（用于分类，-1=非深水）</summary>
    public int WaterRegionId;

    public static TerrainCell Default => new()
    {
        Type = TerrainType.Grass,
        Elevation = 1,
        HasBridge = false,
        HasTunnel = false,
        WaterWidth = WaterWidthClass.None,
        WaterRegionId = -1,
    };
}

/// <summary>
/// 地形单位类别（决定速度修正和通行性）。
/// </summary>
public enum TerrainUnitCategory
{
    Infantry,       // 步兵
    LightVehicle,   // 轻载具
    HeavyVehicle,   // 重型载具
    Harvester,      // 矿车
    Engineer,       // 工兵（步兵类工程单位）
    EngineerVehicle, // 工程车（载具类工程单位）
    Naval,          // 海军
    Air,            // 空军（不受地形影响）
}

/// <summary>
/// 地形网格——存储地图每个格子的高度、类型、桥梁/隧道标记。
/// 提供速度查询和通行性判定。
/// 种子驱动生成，保证可复现。
/// </summary>
public class TerrainGrid
{
    /// <summary>网格边长（格数）— P2-2: 委托给 MapConfig</summary>
    public static int GridSize => MapConfig.GridSize;
    /// <summary>每格像素大小</summary>
    public const int TileSize = 64;
    /// <summary>地图像素大小 = GridSize * TileSize</summary>
    public static float MapPixelSize => MapConfig.MapPixelSize;

    private TerrainCell[,] _cells = new TerrainCell[32, 32]; // 初始默认，Resize时重建

    /// <summary>确保_cells数组与当前GridSize匹配（P2-2: 动态尺寸）。</summary>
    private void EnsureCellArray()
    {
        int gs = GridSize;
        if (_cells.GetLength(0) != gs || _cells.GetLength(1) != gs)
            _cells = new TerrainCell[gs, gs];
    }

    /// <summary>获取指定格子的地形数据。</summary>
    public TerrainCell GetCell(int gx, int gy)
    {
        if (gx < 0 || gx >= GridSize || gy < 0 || gy >= GridSize)
            return DefaultBorder();
        return _cells[gx, gy];
    }

    /// <summary>设置指定格子的地形数据。</summary>
    public void SetCell(int gx, int gy, TerrainCell cell)
    {
        if (gx >= 0 && gx < GridSize && gy >= 0 && gy < GridSize)
            _cells[gx, gy] = cell;
    }

    /// <summary>通过等距屏幕坐标获取格子索引。</summary>
    public void IsoScreenToGrid(float screenX, float screenY, out int gx, out int gy)
    {
        var grid = IsoCoords.ScreenToGridF(screenX, screenY);
        gx = Math.Clamp((int)Math.Floor(grid.X), 0, GridSize - 1);
        gy = Math.Clamp((int)Math.Floor(grid.Y), 0, GridSize - 1);
    }

    /// <summary>通过世界坐标（等距屏幕坐标）获取格子索引。
    /// R6: 已改为等距坐标转换，兼容所有旧调用方。</summary>
    public void WorldToGrid(float worldX, float worldY, out int gx, out int gy)
    {
        var grid = IsoCoords.ScreenToGridF(worldX, worldY);
        gx = Math.Clamp((int)Math.Floor(grid.X), 0, GridSize - 1);
        gy = Math.Clamp((int)Math.Floor(grid.Y), 0, GridSize - 1);
    }

    /// <summary>网格坐标 → 等距屏幕坐标。</summary>
    public Vector2 GridToIsoScreen(int gx, int gy) => IsoCoords.GridToScreen(gx, gy);

    /// <summary>网格坐标(浮点) → 等距屏幕坐标（用于单位平滑移动）。</summary>
    public Vector2 GridToIsoScreenF(float gx, float gy) => IsoCoords.GridToScreenF(gx, gy);

    /// <summary>通过世界坐标获取地形格子。</summary>
    public TerrainCell GetCellAtWorld(float worldX, float worldY)
    {
        WorldToGrid(worldX, worldY, out int gx, out int gy);
        return GetCell(gx, gy);
    }

    /// <summary>修改格子（运行时地形改造：削平/隧道/架桥）。</summary>
    public void ModifyCell(int gx, int gy, TerrainCell cell)
    {
        SetCell(gx, gy, cell);
        // 如果修改了深水类型，需要重新分类
        // 但为性能考虑，只在批量修改后手动调用 ReclassifyWater()
    }

    /// <summary>获取有效通行地形（考虑桥梁/隧道覆盖）。</summary>
    public TerrainType GetEffectiveType(int gx, int gy)
    {
        var cell = GetCell(gx, gy);
        if (cell.HasBridge && (cell.Type == TerrainType.ShallowWater || cell.Type == TerrainType.DeepWater))
            return TerrainType.Bridge;
        if (cell.HasTunnel && cell.Type == TerrainType.Mountain)
            return TerrainType.Tunnel;
        return cell.Type;
    }

    // ======== 速度修正查询 ========

    /// <summary>
    /// 获取指定单位类别在指定地形上的速度修正系数（0=不可通行，1=正常速度）。
    /// P2-4: 委托给 TerrainModifiers 数据驱动查表，消除2D/3D重复硬编码。
    /// </summary>
    public static float GetSpeedModifier(TerrainUnitCategory unitCat, TerrainType terrainType, int elevation, int targetElevation)
    {
        // 空军不受地形影响
        if (unitCat == TerrainUnitCategory.Air) return 1.0f;

        // 高度差判定
        int elevDiff = targetElevation - elevation;
        if (elevDiff >= 2) return 0f; // 悬崖，不可攀爬（需削平）

        // P2-4: 从 TerrainModifiers 查表
        float baseMod = TerrainModifiers.GetSpeedMod(terrainType, unitCat);

        // 缓坡额外速度惩罚
        if (elevDiff == 1 && baseMod > 0f)
        {
            baseMod *= TerrainModifiers.GetSlopeMod(unitCat);
        }

        return baseMod;
    }

    /// <summary>
    /// 获取单位从当前格子移动到目标格子的综合速度修正。
    /// 自动处理桥梁/隧道覆盖和高度差。
    /// </summary>
    public float GetMovementSpeed(TerrainUnitCategory unitCat, int fromGx, int fromGy, int toGx, int toGy)
    {
        var fromCell = GetCell(fromGx, fromGy);
        var toCell = GetCell(toGx, toGy);
        var effectiveType = GetEffectiveType(toGx, toGy);
        return GetSpeedModifier(unitCat, effectiveType, fromCell.Elevation, toCell.Elevation);
    }

    /// <summary>
    /// 获取单位在指定世界坐标处的速度修正。
    /// </summary>
    public float GetMovementSpeedAtWorld(TerrainUnitCategory unitCat, float worldX, float worldY)
    {
        WorldToGrid(worldX, worldY, out int gx, out int gy);
        var effectiveType = GetEffectiveType(gx, gy);
        var cell = GetCell(gx, gy);
        // 简化：假设同高度
        return GetSpeedModifier(unitCat, effectiveType, cell.Elevation, cell.Elevation);
    }

    // ======== 种子驱动地图生成 ========

    /// <summary>
    /// 从种子生成完整地形布局。
    /// </summary>
    public void GenerateFromSeed(ulong seed)
    {
        EnsureCellArray();
        int gs = GridSize;
        var rng = new Random((int)(seed & 0x7FFFFFFF));
        var theme = MapConfig.Theme;

        // P2-2: 主题影响初始地形
        // Snow主题：初始为雪地; Desert主题：初始为沙地; City主题：初始为草地（后铺城）; Island主题：初始为深水
        for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
            {
                _cells[x, y] = theme switch
                {
                    MapConfig.MapTheme.Snow => new TerrainCell { Type = TerrainType.Snow, Elevation = 1 },
                    MapConfig.MapTheme.Desert => new TerrainCell { Type = TerrainType.Sand, Elevation = 1 },
                    MapConfig.MapTheme.Island => new TerrainCell { Type = TerrainType.DeepWater, Elevation = 0 },
                    _ => TerrainCell.Default,
                };
            }

        // 2. 先生成水域（在山脉之前，避免水覆盖山）
        int lakeCount = theme switch
        {
            MapConfig.MapTheme.Snow => 0,                    // 雪地无湖（冰冻）
            MapConfig.MapTheme.Desert => 1,                   // 沙漠1个绿洲
            MapConfig.MapTheme.Island => 3 + rng.Next(2),    // 海岛多湖（珊瑚礁）
            MapConfig.MapTheme.City => 1,                     // 城市1个湖
            _ => 2 + rng.Next(2),                             // 默认2-3个湖泊
        };
        bool hasRiver = theme != MapConfig.MapTheme.Island && theme != MapConfig.MapTheme.Desert;
        GenerateWater(rng, lakeCount, hasRiver);

        // 3. 生成山脉（在水之后，山脉不会被水覆盖）
        int mountainCount = theme switch
        {
            MapConfig.MapTheme.Snow => 10 + rng.Next(5),     // 雪地多山
            MapConfig.MapTheme.Desert => 5 + rng.Next(4),     // 沙漠少山
            MapConfig.MapTheme.Island => 3 + rng.Next(3),     // 海岛少量山
            MapConfig.MapTheme.City => 3 + rng.Next(3),       // 城市少量山
            _ => 10 + rng.Next(5),                             // 默认10-14个
        };
        GenerateMountains(rng, mountainCount);

        // 4. 生成丘陵/高地
        int hillCount = theme switch
        {
            MapConfig.MapTheme.Snow => 10 + rng.Next(5),
            MapConfig.MapTheme.Desert => 6 + rng.Next(4),
            MapConfig.MapTheme.Island => 4 + rng.Next(3),
            MapConfig.MapTheme.City => 4 + rng.Next(3),
            _ => 10 + rng.Next(5),                             // 默认10-14个
        };
        GenerateHills(rng, hillCount);

        // 5. 主题特化地形调整
        ApplyThemeTerrain(rng, theme);

        // 6. 生成田地（非沙漠/海岛主题）
        if (theme != MapConfig.MapTheme.Desert && theme != MapConfig.MapTheme.Island)
            GenerateFields(rng);

        // 7. 生成城市区（地图中部）
        if (theme != MapConfig.MapTheme.Island)
            GenerateCity(rng);

        // 8. 生成道路
        GenerateRoads(rng);

        // 8.5 海岸线柔化：水体边缘的陆地变为沙地（沙滩过渡）
        SoftenCoastlines();

        // 8.6 生成沙地斑块（增加地形多样性）
        if (theme != MapConfig.MapTheme.Island)
            GenerateSand(rng);

        // 9. Island主题：生成陆地岛屿
        // (在ApplyThemeTerrain中已处理)

        // 10. 确保基地起始位置为平地+草地
        EnsureBaseAreas();

        // 11. 分类深水区域
        ClassifyDeepWater();
    }

    private void GenerateMountains(Random rng, int numMountains)
    {
        // 分区放置山脉：将地图分成4×4区域，每区尝试放置1座山，确保全图覆盖
        int regionsPerSide = 4;
        int regionSize = GridSize / regionsPerSide;
        int placed = 0;
        var peakPositions = new System.Collections.Generic.List<(int x, int y, int size)>();

        // 创建打乱过的区域列表
        var regions = new System.Collections.Generic.List<(int rx, int ry)>();
        for (int ry = 0; ry < regionsPerSide; ry++)
            for (int rx = 0; rx < regionsPerSide; rx++)
                regions.Add((rx, ry));
        // Fisher-Yates shuffle
        for (int i = regions.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (regions[i], regions[j]) = (regions[j], regions[i]);
        }

        foreach (var (rx, ry) in regions)
        {
            if (placed >= numMountains) break;

            // 在区域内随机选位置
            int cx = rx * regionSize + rng.Next(2, regionSize - 2);
            int cy = ry * regionSize + rng.Next(2, regionSize - 2);
            cx = Math.Clamp(cx, 4, GridSize - 5);
            cy = Math.Clamp(cy, 4, GridSize - 5);

            // 避开基地区域
            if (IsBaseArea(cx, cy))
            {
                for (int r = 1; r <= 4; r++)
                {
                    bool found = false;
                    for (int dy = -r; dy <= r && !found; dy++)
                        for (int dx = -r; dx <= r && !found; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx >= 4 && nx < GridSize - 4 && ny >= 4 && ny < GridSize - 4 && !IsBaseArea(nx, ny))
                            {
                                cx = nx; cy = ny; found = true;
                            }
                        }
                    if (found) break;
                }
                if (IsBaseArea(cx, cy)) continue;
            }

            int peakSize = 3 + rng.Next(3); // 3-5格半径
            for (int dy = -peakSize; dy <= peakSize; dy++)
            {
                for (int dx = -peakSize; dx <= peakSize; dx++)
                {
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dist > peakSize) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) continue;
                    if (IsBaseArea(x, y)) continue;

                    if (dist <= peakSize * 0.55f)
                    {
                        // 山峰核心：山脉，海拔3（覆盖水域，山在水之后生成）
                        _cells[x, y].Type = TerrainType.Mountain;
                        _cells[x, y].Elevation = 3;
                    }
                    else if (dist <= peakSize * 0.85f)
                    {
                        // 山腰：雪地+海拔2（不覆盖已有道路）
                        if (_cells[x, y].Type != TerrainType.Road)
                        {
                            _cells[x, y].Type = TerrainType.Snow;
                            _cells[x, y].Elevation = 2;
                        }
                    }
                    else
                    {
                        // 山麓：海拔2，保留原地形（形成坡度感）
                        if (_cells[x, y].Elevation < 2 && !IsBaseArea(x, y))
                            _cells[x, y].Elevation = 2;
                    }
                }
            }
            peakPositions.Add((cx, cy, peakSize));
            placed++;
        }

        // 山脊连接：在距离适中的相邻山峰之间画山脊，形成连绵山脉
        for (int i = 0; i < peakPositions.Count; i++)
        {
            // 找最近的1-2个其他山峰
            var (x1, y1, s1) = peakPositions[i];
            var nearby = new System.Collections.Generic.List<(int x, int y, double dist)>();
            for (int j = 0; j < peakPositions.Count; j++)
            {
                if (i == j) continue;
                var (x2, y2, _) = peakPositions[j];
                double d = Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
                if (d < regionSize * 2.5) // 只连接较近的山峰
                    nearby.Add((x2, y2, d));
            }
            nearby.Sort((a, b) => a.dist.CompareTo(b.dist));
            // 连最近1-2座
            int connections = Math.Min(2, nearby.Count);
            for (int k = 0; k < connections; k++)
            {
                DrawMountainRidge(x1, y1, nearby[k].x, nearby[k].y, rng);
            }
        }
    }

    /// <summary>在两座山峰之间画一条山脊（海拔2的雪地+少量山脉核心），形成连绵效果。</summary>
    private void DrawMountainRidge(int x1, int y1, int x2, int y2, Random rng)
    {
        int dx = x2 - x1, dy = y2 - y1;
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps == 0) return;

        // 沿连线插值，加少量随机偏移使山脊不平直
        for (int s = 0; s <= steps; s++)
        {
            float t = (float)s / steps;
            int cx = (int)(x1 + dx * t + (rng.NextDouble() - 0.5) * 2);
            int cy = (int)(y1 + dy * t + (rng.NextDouble() - 0.5) * 2);
            cx = Math.Clamp(cx, 1, GridSize - 2);
            cy = Math.Clamp(cy, 1, GridSize - 2);
            if (IsBaseArea(cx, cy)) continue;

            // 山脊中心：海拔2雪地，偶尔海拔3山脉
            if (_cells[cx, cy].Elevation < 2)
            {
                if (rng.NextDouble() < 0.3 && _cells[cx, cy].Type != TerrainType.Road)
                {
                    _cells[cx, cy].Type = TerrainType.Mountain;
                    _cells[cx, cy].Elevation = 3;
                }
                else if (_cells[cx, cy].Type != TerrainType.Road &&
                         _cells[cx, cy].Type != TerrainType.DeepWater)
                {
                    _cells[cx, cy].Type = TerrainType.Snow;
                    _cells[cx, cy].Elevation = 2;
                }
            }

            // 山脊两侧：海拔2
            foreach (var (nx, ny) in GetNeighbors4(cx, cy))
            {
                if (nx < 0 || nx >= GridSize || ny < 0 || ny >= GridSize) continue;
                if (IsBaseArea(nx, ny)) continue;
                if (_cells[nx, ny].Elevation < 2 && _cells[nx, ny].Type != TerrainType.DeepWater)
                    _cells[nx, ny].Elevation = 2;
            }
        }
    }

    private void GenerateHills(Random rng, int numHills)
    {
        for (int h = 0; h < numHills; h++)
        {
            int cx = rng.Next(3, GridSize - 3);
            int cy = rng.Next(3, GridSize - 3);
            if (IsBaseArea(cx, cy)) continue;
            // 避免和山脉重叠
            if (_cells[cx, cy].Type == TerrainType.Mountain) continue;

            int size = 1 + rng.Next(2);
            for (int dy = -size; dy <= size; dy++)
            {
                for (int dx = -size; dx <= size; dx++)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) > size) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) continue;
                    if (IsBaseArea(x, y)) continue;
                    // 只升级非山脉格子
                    if (_cells[x, y].Type != TerrainType.Mountain)
                    {
                        _cells[x, y].Elevation = 2;
                        // 丘陵视觉上仍为草地/沙地，海拔2
                    }
                }
            }
        }
    }

    private void GenerateWater(Random rng, int numLakes, bool hasRiver)
    {
        // 生成河流
        if (hasRiver)
            GenerateRiver(rng);

        // 生成湖泊
        for (int l = 0; l < numLakes; l++)
        {
            int cx = rng.Next(6, GridSize - 6);
            int cy = rng.Next(6, GridSize - 6);
            if (IsBaseArea(cx, cy)) continue;

            int radius = 2 + rng.Next(2); // 2-3格半径
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) continue;
                    if (IsBaseArea(x, y)) continue;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= radius - 1)
                    {
                        _cells[x, y].Type = TerrainType.DeepWater;
                        _cells[x, y].Elevation = 0;
                    }
                    else if (dist <= radius)
                    {
                        _cells[x, y].Type = TerrainType.ShallowWater;
                        _cells[x, y].Elevation = 1;
                    }
                }
            }
        }
    }

    private void GenerateRiver(Random rng)
    {
        // 自然蜿蜒河流：从一边出发，用平滑偏移蜿蜒到另一边
        int startEdge = rng.Next(4);
        int x, y, dx, dy;
        // 蜿蜒参数
        double driftAngle = 0; // 当前偏移角度
        switch (startEdge)
        {
            case 0: x = rng.Next(4, GridSize - 4); y = 0; dx = 0; dy = 1; break;   // 从上方
            case 1: x = rng.Next(4, GridSize - 4); y = GridSize - 1; dx = 0; dy = -1; break; // 从下方
            case 2: x = 0; y = rng.Next(4, GridSize - 4); dx = 1; dy = 0; break;   // 从左方
            default: x = GridSize - 1; y = rng.Next(4, GridSize - 4); dx = -1; dy = 0; break; // 从右方
        }

        int steps = GridSize * 2;
        int widenCountdown = 5 + rng.Next(10); // 湖泊扩展的间隔
        for (int s = 0; s < steps; s++)
        {
            if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) break;

            if (!IsBaseArea(x, y))
            {
                // 河流中心=深水
                _cells[x, y].Type = TerrainType.DeepWater;
                _cells[x, y].Elevation = 0;
                // 两岸=浅水（1格宽岸滩）
                foreach (var (nx, ny) in GetNeighbors4(x, y))
                {
                    if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize && !IsBaseArea(nx, ny))
                    {
                        if (_cells[nx, ny].Type != TerrainType.DeepWater)
                        {
                            _cells[nx, ny].Type = TerrainType.ShallowWater;
                            _cells[nx, ny].Elevation = 1;
                        }
                    }
                }

                // 湖泊扩展：每隔一段距离在河道上扩展一个小湖
                widenCountdown--;
                if (widenCountdown <= 0)
                {
                    int lakeR = 2 + rng.Next(2);
                    for (int ldy = -lakeR; ldy <= lakeR; ldy++)
                    {
                        for (int ldx = -lakeR; ldx <= lakeR; ldx++)
                        {
                            float ld = (float)Math.Sqrt(ldx * ldx + ldy * ldy);
                            if (ld > lakeR) continue;
                            int lx = x + ldx, ly = y + ldy;
                            if (lx < 0 || lx >= GridSize || ly < 0 || ly >= GridSize) continue;
                            if (IsBaseArea(lx, ly)) continue;
                            if (ld <= lakeR * 0.6f)
                            {
                                _cells[lx, ly].Type = TerrainType.DeepWater;
                                _cells[lx, ly].Elevation = 0;
                            }
                            else if (_cells[lx, ly].Type != TerrainType.DeepWater)
                            {
                                _cells[lx, ly].Type = TerrainType.ShallowWater;
                                _cells[lx, ly].Elevation = 1;
                            }
                        }
                    }
                    widenCountdown = 8 + rng.Next(12);
                }
            }

            // 蜿蜒：渐进式角度偏移（大幅增加弯曲度）
            driftAngle += (rng.NextDouble() - 0.5) * 1.0; // 角度更快变化
            driftAngle = Math.Max(-2.5, Math.Min(2.5, driftAngle)); // 放宽限幅

            // 计算实际移动方向（主方向 + 蜿蜒偏移）
            if (dx != 0)
            {
                // 水平河流：y方向蜿蜒（大幅增加偏移）
                int sideStep = (int)Math.Round(Math.Sin(driftAngle) * 3.0);
                y += sideStep;
            }
            else
            {
                // 垂直河流：x方向蜿蜒
                int sideStep = (int)Math.Round(Math.Sin(driftAngle) * 3.0);
                x += sideStep;
            }
            x += dx;
            y += dy;
        }

        // 生成1-2条支流
        int tributaries = 1 + rng.Next(2);
        for (int t = 0; t < tributaries; t++)
        {
            GenerateTributary(rng);
        }
    }

    /// <summary>生成支流：从地图边缘出发，短距离蜿蜒后汇入主河道区域。</summary>
    private void GenerateTributary(Random rng)
    {
        // 找一个已有的深水格作为汇入点
        int targetX = -1, targetY = -1;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            int tx = rng.Next(4, GridSize - 4);
            int ty = rng.Next(4, GridSize - 4);
            if (_cells[tx, ty].Type == TerrainType.DeepWater)
            {
                targetX = tx;
                targetY = ty;
                break;
            }
        }
        if (targetX < 0) return;

        // 从最近的边缘出发
        int sx, sy;
        if (targetX < targetY) { sx = 0; sy = targetY; }
        else { sx = targetX; sy = 0; }

        int dx = targetX > sx ? 1 : (targetX < sx ? -1 : 0);
        int dy = targetY > sy ? 1 : (targetY < sy ? -1 : 0);

        int maxSteps = Math.Max(Math.Abs(targetX - sx), Math.Abs(targetY - sy)) + 5;
        double drift = 0;
        for (int s = 0; s < maxSteps; s++)
        {
            if (sx < 0 || sx >= GridSize || sy < 0 || sy >= GridSize) break;
            if (sx == targetX && sy == targetY) break; // 汇入主干
            if (!IsBaseArea(sx, sy))
            {
                if (_cells[sx, sy].Type != TerrainType.DeepWater)
                {
                    _cells[sx, sy].Type = TerrainType.ShallowWater; // 支流较浅
                    _cells[sx, sy].Elevation = 1;
                }
            }
            // 蜿蜒
            drift += (rng.NextDouble() - 0.5) * 0.8;
            drift = Math.Max(-1.0, Math.Min(1.0, drift));
            int wob = (int)Math.Round(Math.Sin(drift));
            if (dx != 0) sy += wob; else sx += wob;
            sx += dx;
            sy += dy;
        }
    }

    private void GenerateSnow(Random rng)
    {
        // 默认主题：山脉周围2格范围内的高地变为雪地
        if (MapConfig.Theme != MapConfig.MapTheme.Default) return;
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (_cells[x, y].Elevation >= 2 && _cells[x, y].Type == TerrainType.Grass)
                {
                    bool nearMountain = false;
                    for (int dy = -2; dy <= 2 && !nearMountain; dy++)
                        for (int dx = -2; dx <= 2 && !nearMountain; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize)
                                if (_cells[nx, ny].Type == TerrainType.Mountain)
                                    nearMountain = true;
                        }
                    if (nearMountain)
                        _cells[x, y].Type = TerrainType.Snow;
                }
            }
        }
    }

    /// <summary>海岸线柔化：水体边缘的草地/田地变为沙地（沙滩过渡效果）。</summary>
    private void SoftenCoastlines()
    {
        var changes = new System.Collections.Generic.List<(int x, int y)>();
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                var t = _cells[x, y].Type;
                if (t != TerrainType.Grass && t != TerrainType.Field) continue;
                if (_cells[x, y].Elevation > 1) continue; // 只柔化低地海岸

                // 检查4邻居是否有水
                bool nearWater = false;
                foreach (var (nx, ny) in GetNeighbors4(x, y))
                {
                    if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize)
                    {
                        var nt = _cells[nx, ny].Type;
                        if (nt == TerrainType.ShallowWater || nt == TerrainType.DeepWater)
                        {
                            nearWater = true;
                            break;
                        }
                    }
                }
                if (nearWater)
                    changes.Add((x, y));
            }
        }
        foreach (var (x, y) in changes)
            _cells[x, y].Type = TerrainType.Sand;
    }

    /// <summary>P2-2: 主题特化地形调整。</summary>
    private void ApplyThemeTerrain(Random rng, MapConfig.MapTheme theme)
    {
        switch (theme)
        {
            case MapConfig.MapTheme.Snow:
                // 雪地主题：山脉自带雪覆盖，低地雪→冻原（Field替代）
                for (int y = 0; y < GridSize; y++)
                    for (int x = 0; x < GridSize; x++)
                        if (_cells[x, y].Type == TerrainType.Snow && _cells[x, y].Elevation <= 1 && rng.NextDouble() < 0.3)
                            _cells[x, y].Type = TerrainType.Field; // 冻原
                break;

            case MapConfig.MapTheme.Desert:
                // 沙漠主题：绿洲区域（少量草地+浅水）
                int oases = 2 + rng.Next(2);
                for (int o = 0; o < oases; o++)
                {
                    int cx = rng.Next(4, GridSize - 4);
                    int cy = rng.Next(4, GridSize - 4);
                    if (IsBaseArea(cx, cy)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize && !IsBaseArea(nx, ny))
                            {
                                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                                if (dist <= 1.5f)
                                {
                                    _cells[nx, ny].Type = TerrainType.ShallowWater; // 绿洲中心
                                    _cells[nx, ny].Elevation = 1;
                                }
                                else if (dist <= 2.5f && _cells[nx, ny].Type == TerrainType.Sand)
                                {
                                    _cells[nx, ny].Type = TerrainType.Grass; // 绿洲边缘草地
                                }
                            }
                        }
                }
                break;

            case MapConfig.MapTheme.City:
                // 城市主题：大面积铺装+少量建筑废墟（用Field表示）
                int cityCenter = MapConfig.Center;
                int cityRadius = GridSize / 4;
                for (int y = 0; y < GridSize; y++)
                    for (int x = 0; x < GridSize; x++)
                    {
                        int dist = Math.Abs(x - cityCenter) + Math.Abs(y - cityCenter);
                        if (dist < cityRadius && _cells[x, y].Type == TerrainType.Grass && !IsBaseArea(x, y))
                        {
                            if (rng.NextDouble() < 0.6)
                                _cells[x, y].Type = TerrainType.City;
                            else if (rng.NextDouble() < 0.15)
                                _cells[x, y].Type = TerrainType.Field; // 废墟/公园
                        }
                    }
                break;

            case MapConfig.MapTheme.Island:
                // 海岛主题：在深水基底上生成多个岛屿
                int numIslands = 3 + rng.Next(3); // 3-5个岛
                for (int i = 0; i < numIslands; i++)
                {
                    int cx, cy;
                    // 确保岛屿不在角落（基地位置）
                    do { cx = rng.Next(GridSize / 4, GridSize * 3 / 4); cy = rng.Next(GridSize / 4, GridSize * 3 / 4); }
                    while (IsBaseArea(cx, cy));

                    int radius = 2 + rng.Next(3); // 2-4格半径
                    for (int dy = -radius; dy <= radius; dy++)
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize && !IsBaseArea(nx, ny))
                            {
                                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                                if (dist <= radius - 1)
                                {
                                    _cells[nx, ny].Type = TerrainType.Grass;
                                    _cells[nx, ny].Elevation = 1;
                                }
                                else if (dist <= radius)
                                {
                                    _cells[nx, ny].Type = TerrainType.Sand; // 岛屿边缘沙滩
                                    _cells[nx, ny].Elevation = 1;
                                }
                            }
                        }
                }
                break;
        }
    }

    private void GenerateSand(Random rng)
    {
        // 默认主题：仅在山脉附近（山脚碎石/沙砾）和河流附近（河岸沙地）生成小片沙地
        if (MapConfig.Theme != MapConfig.MapTheme.Default) return;

        // 山脚沙地：山脉1-2格范围内的草地变沙地（模拟碎石坡）
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (_cells[x, y].Type != TerrainType.Grass) continue;
                if (_cells[x, y].Elevation != 1) continue; // 只在低地

                // 检查2格内是否有山脉
                bool nearMountain = false;
                for (int dy = -2; dy <= 2 && !nearMountain; dy++)
                    for (int dx = -2; dx <= 2 && !nearMountain; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize)
                            if (_cells[nx, ny].Type == TerrainType.Mountain)
                                nearMountain = true;
                    }
                // 30%概率变沙地（不是所有山脚都变）
                if (nearMountain && rng.NextDouble() < 0.3 && !IsBaseArea(x, y))
                    _cells[x, y].Type = TerrainType.Sand;
            }
        }
    }

    private void GenerateFields(Random rng)
    {
        // 平地上随机生成2-3个田地区域
        int numFields = 2 + rng.Next(2);
        for (int f = 0; f < numFields; f++)
        {
            int cx = rng.Next(3, GridSize - 3);
            int cy = rng.Next(3, GridSize - 3);
            if (IsBaseArea(cx, cy)) continue;
            if (_cells[cx, cy].Type != TerrainType.Grass) continue;

            int w = 2 + rng.Next(2);
            int h = 2 + rng.Next(2);
            for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize)
                        if (_cells[nx, ny].Type == TerrainType.Grass && _cells[nx, ny].Elevation == 1 && !IsBaseArea(nx, ny))
                            _cells[nx, ny].Type = TerrainType.Field;
                }
        }
    }

    private void GenerateCity(Random rng)
    {
        // P2-2: 地图中央附近生成一个城市区域
        int center = MapConfig.Center;
        int cx = center - 2 + rng.Next(4);
        int cy = center - 2 + rng.Next(4);
        int size = 2 + rng.Next(2); // 2-3格
        for (int dy = -size; dy <= size; dy++)
            for (int dx = -size; dx <= size; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x >= 0 && x < GridSize && y >= 0 && y < GridSize)
                    if (_cells[x, y].Type == TerrainType.Grass && _cells[x, y].Elevation <= 1)
                        _cells[x, y].Type = TerrainType.City;
            }
    }

    private void GenerateRoads(Random rng)
    {
        // P2: 连贯路网 — BFS寻路连接所有基地到地图中心
        var bases = MapConfig.BasePositions;
        int center = MapConfig.Center;

        foreach (var (bx, by) in bases)
        {
            // 从基地边缘出发，到中心附近
            int startX = Math.Clamp(bx + (bx < center ? 2 : (bx > center ? -2 : 0)), 0, GridSize - 1);
            int startY = Math.Clamp(by + (by < center ? 2 : (by > center ? -2 : 0)), 0, GridSize - 1);
            int targetX = center, targetY = center;

            // BFS寻路：只通过可建路地形
            var path = FindRoadPath(startX, startY, targetX, targetY);
            if (path == null) continue;

            // 沿路径铺设道路
            foreach (var (px, py) in path)
            {
                if (IsBaseArea(px, py)) continue;
                var t = _cells[px, py].Type;
                if (t == TerrainType.ShallowWater)
                {
                    _cells[px, py].Type = TerrainType.Road;
                    _cells[px, py].HasBridge = true;
                }
                else if ((t == TerrainType.Grass || t == TerrainType.Sand ||
                          t == TerrainType.Field || t == TerrainType.Snow) &&
                         _cells[px, py].Elevation <= 2)
                {
                    _cells[px, py].Type = TerrainType.Road;
                }
            }
        }

        // 补充横向联络道（连接相邻基地对）
        for (int i = 0; i < bases.Length - 1; i += 2)
        {
            var (x1, y1) = bases[i];
            var (x2, y2) = bases[i + 1];
            int dist = Math.Abs(x1 - x2) + Math.Abs(y1 - y2);
            if (dist > GridSize) continue;
            DrawRoadBetween(Math.Clamp(x1 + 1, 0, GridSize - 1), Math.Clamp(y1 + 1, 0, GridSize - 1),
                            Math.Clamp(x2 - 1, 0, GridSize - 1), Math.Clamp(y2 - 1, 0, GridSize - 1));
        }
    }

    /// <summary>BFS寻路：从起点到终点，只通过可建路地形（草地/沙地/雪/田地/浅水），绕过山/深水/悬崖。</summary>
    private System.Collections.Generic.List<(int x, int y)>? FindRoadPath(int sx, int sy, int tx, int ty)
    {
        if (sx == tx && sy == ty) return new System.Collections.Generic.List<(int, int)> { (sx, sy) };

        var visited = new bool[GridSize, GridSize];
        var parent = new (int px, int py)[GridSize, GridSize];
        var queue = new Queue<(int x, int y)>();
        queue.Enqueue((sx, sy));
        visited[sx, sy] = true;
        parent[sx, sy] = (-1, -1);

        int[] dxs = { 0, 0, 1, -1 };
        int[] dys = { 1, -1, 0, 0 };

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            // 到达终点附近（3格内）
            if (Math.Abs(cx - tx) <= 2 && Math.Abs(cy - ty) <= 2)
            {
                // 回溯路径
                var path = new System.Collections.Generic.List<(int, int)>();
                int bx = cx, by = cy;
                while (bx != -1)
                {
                    path.Add((bx, by));
                    var (ppx, ppy) = parent[bx, by];
                    bx = ppx; by = ppy;
                }
                path.Reverse();
                return path;
            }

            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dxs[d], ny = cy + dys[d];
                if (nx < 0 || nx >= GridSize || ny < 0 || ny >= GridSize) continue;
                if (visited[nx, ny]) continue;

                var nt = _cells[nx, ny].Type;
                // 不可通过：山脉、深水、悬崖
                if (nt == TerrainType.Mountain || nt == TerrainType.DeepWater || nt == TerrainType.Cliff)
                    continue;

                visited[nx, ny] = true;
                parent[nx, ny] = (cx, cy);
                queue.Enqueue((nx, ny));
            }
        }
        return null; // 无路径
    }

    /// <summary>在两点之间画一条简单的道路（直线+L形拐弯），遇水架桥。</summary>
    private void DrawRoadBetween(int x1, int y1, int x2, int y2)
    {
        int cx = x1, cy = y1;
        // 先水平后垂直
        while (cx != x2)
        {
            if (!IsBaseArea(cx, cy))
            {
                var t = _cells[cx, cy].Type;
                if ((t == TerrainType.Grass || t == TerrainType.Sand ||
                     t == TerrainType.Field || t == TerrainType.Snow) && _cells[cx, cy].Elevation <= 2)
                    _cells[cx, cy].Type = TerrainType.Road;
                if (t == TerrainType.ShallowWater)
                { _cells[cx, cy].Type = TerrainType.Road; _cells[cx, cy].HasBridge = true; }
            }
            cx += cx < x2 ? 1 : -1;
        }
        while (cy != y2)
        {
            if (!IsBaseArea(cx, cy))
            {
                var t = _cells[cx, cy].Type;
                if ((t == TerrainType.Grass || t == TerrainType.Sand ||
                     t == TerrainType.Field || t == TerrainType.Snow) && _cells[cx, cy].Elevation <= 2)
                    _cells[cx, cy].Type = TerrainType.Road;
                if (t == TerrainType.ShallowWater)
                { _cells[cx, cy].Type = TerrainType.Road; _cells[cx, cy].HasBridge = true; }
            }
            cy += cy < y2 ? 1 : -1;
        }
    }

    private void EnsureBaseAreas()
    {
        // P2-2: 使用MapConfig动态计算基地位置
        var basePositions = MapConfig.BasePositions;
        foreach (var (bx, by) in basePositions)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = bx + dx, y = by + dy;
                    if (x >= 0 && x < GridSize && y >= 0 && y < GridSize)
                    {
                        _cells[x, y].Type = TerrainType.Grass;
                        _cells[x, y].Elevation = 1;
                        _cells[x, y].HasBridge = false;
                        _cells[x, y].HasTunnel = false;
                    }
                }
        }
    }

    // ======== 深水宽度分类 ========

    /// <summary>
    /// BFS 扫描所有深水连通区域，按最窄跨度分类。
    /// </summary>
    public void ClassifyDeepWater()
    {
        // 重置
        for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
            {
                _cells[x, y].WaterRegionId = -1;
                _cells[x, y].WaterWidth = WaterWidthClass.None;
            }

        bool[,] visited = new bool[GridSize, GridSize];
        int regionId = 0;

        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (_cells[x, y].Type != TerrainType.DeepWater || visited[x, y]) continue;

                // BFS 收集连通区域
                var region = new List<(int x, int y)>();
                var queue = new Queue<(int x, int y)>();
                queue.Enqueue((x, y));
                visited[x, y] = true;

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    region.Add((cx, cy));
                    foreach (var (nx, ny) in GetNeighbors4(cx, cy))
                    {
                        if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize
                            && !visited[nx, ny]
                            && _cells[nx, ny].Type == TerrainType.DeepWater)
                        {
                            visited[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }

                // 计算最窄跨度（水平或垂直方向上的最小宽度）
                int minWidth = ComputeMinSpan(region);

                // 分类
                WaterWidthClass wc = minWidth switch
                {
                    <= 3 => WaterWidthClass.River,
                    <= 8 => WaterWidthClass.Strait,
                    <= 15 => WaterWidthClass.Sea,
                    _ => WaterWidthClass.Ocean,
                };

                // 标记
                foreach (var (rx, ry) in region)
                {
                    _cells[rx, ry].WaterRegionId = regionId;
                    _cells[rx, ry].WaterWidth = wc;
                }
                regionId++;
            }
        }
    }

    /// <summary>
    /// 计算连通区域的最窄跨度。
    /// 对每行/列，计算区域内该行/列连续深水格子的最大跨度，
    /// 然后取所有方向上的最小值作为"最窄跨度"。
    /// </summary>
    private static int ComputeMinSpan(List<(int x, int y)> region)
    {
        if (region.Count == 0) return 0;

        var set = new HashSet<(int, int)>(region);
        int minSpan = int.MaxValue;

        // 检查水平方向最窄宽度
        var rows = new Dictionary<int, List<int>>();
        foreach (var (x, y) in region)
        {
            if (!rows.ContainsKey(y)) rows[y] = new List<int>();
            rows[y].Add(x);
        }
        foreach (var kvp in rows)
        {
            var xs = kvp.Value;
            xs.Sort();
            // 最大连续段
            int maxConsec = 1, curConsec = 1;
            for (int i = 1; i < xs.Count; i++)
            {
                if (xs[i] == xs[i - 1] + 1) curConsec++;
                else curConsec = 1;
                maxConsec = Math.Max(maxConsec, curConsec);
            }
            minSpan = Math.Min(minSpan, maxConsec);
        }

        // 检查垂直方向最窄宽度
        var cols = new Dictionary<int, List<int>>();
        foreach (var (x, y) in region)
        {
            if (!cols.ContainsKey(x)) cols[x] = new List<int>();
            cols[x].Add(y);
        }
        foreach (var kvp in cols)
        {
            var ys = kvp.Value;
            ys.Sort();
            int maxConsec = 1, curConsec = 1;
            for (int i = 1; i < ys.Count; i++)
            {
                if (ys[i] == ys[i - 1] + 1) curConsec++;
                else curConsec = 1;
                maxConsec = Math.Max(maxConsec, curConsec);
            }
            minSpan = Math.Min(minSpan, maxConsec);
        }

        return minSpan == int.MaxValue ? region.Count : minSpan;
    }

    // ======== 辅助方法 ========

    private static readonly (int, int)[] Neighbor4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    private static IEnumerable<(int, int)> GetNeighbors4(int x, int y)
    {
        foreach (var (dx, dy) in Neighbor4)
            yield return (x + dx, y + dy);
    }

    /// <summary>判断是否在基地起始区域附近（3格范围）— P2-2: 委托给MapConfig。</summary>
    private static bool IsBaseArea(int x, int y) => MapConfig.IsBaseArea(x, y);

    /// <summary>地图边界外的默认格子。</summary>
    private static TerrainCell DefaultBorder() => new()
    {
        Type = TerrainType.Cliff,
        Elevation = 3,
        HasBridge = false,
        HasTunnel = false,
        WaterWidth = WaterWidthClass.None,
        WaterRegionId = -1,
    };

    // ======== E5 资源点位置查询 ========

    /// <summary>
    /// 获取适合放置资源点的格子列表。
    /// 条件：陆地可通行地形（草地/沙地/雪地/城市/田地/道路），非基地区域，指定海拔范围内。
    /// </summary>
    public List<(int gx, int gy)> GetSuitableResourcePositions(
        int minElevation = 1, int maxElevation = 2,
        bool allowCity = true, bool allowField = true)
    {
        var positions = new List<(int, int)>();
        for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
            {
                if (IsBaseArea(x, y)) continue;
                var cell = _cells[x, y];
                if (cell.Elevation < minElevation || cell.Elevation > maxElevation) continue;
                if (cell.HasBridge || cell.HasTunnel) continue;

                bool suitable = cell.Type switch
                {
                    TerrainType.Grass => true,
                    TerrainType.Sand => true,
                    TerrainType.Snow => true,
                    TerrainType.Road => true,
                    TerrainType.City => allowCity,
                    TerrainType.Field => allowField,
                    _ => false,
                };
                if (suitable)
                    positions.Add((x, y));
            }
        return positions;
    }

    /// <summary>
    /// 获取适合放置油田的格子列表。
    /// 油田偏好在沙地/平地，远离山脉，靠近道路。
    /// </summary>
    public List<(int gx, int gy)> GetOilFieldPositions()
    {
        var positions = new List<(int, int)>();
        for (int y = 2; y < GridSize - 2; y++)
            for (int x = 2; x < GridSize - 2; x++)
            {
                if (IsBaseArea(x, y)) continue;
                var cell = _cells[x, y];
                if (cell.Elevation != 1) continue;
                if (cell.Type != TerrainType.Sand && cell.Type != TerrainType.Grass && cell.Type != TerrainType.Field)
                    continue;
                // 远离山脉（3格内无山脉）
                bool nearMountain = false;
                for (int dy = -3; dy <= 3 && !nearMountain; dy++)
                    for (int dx = -3; dx <= 3 && !nearMountain; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize)
                            if (_cells[nx, ny].Type == TerrainType.Mountain || _cells[nx, ny].Type == TerrainType.Cliff)
                                nearMountain = true;
                    }
                if (nearMountain) continue;
                // 附近有道路加分（偏好道路附近）
                positions.Add((x, y));
            }
        return positions;
    }

    /// <summary>
    /// 获取适合放置稀有矿的格子列表。
    /// 稀有矿偏好山脉附近、高海拔区域。
    /// </summary>
    public List<(int gx, int gy)> GetRareMineralPositions()
    {
        var positions = new List<(int, int)>();
        for (int y = 2; y < GridSize - 2; y++)
            for (int x = 2; x < GridSize - 2; x++)
            {
                if (IsBaseArea(x, y)) continue;
                var cell = _cells[x, y];
                if (cell.Elevation < 2) continue; // 高地/山脉附近
                if (cell.Type != TerrainType.Grass && cell.Type != TerrainType.Snow && cell.Type != TerrainType.Sand)
                    continue;
                // 必须在山脉2格范围内
                bool nearMountain = false;
                for (int dy = -2; dy <= 2 && !nearMountain; dy++)
                    for (int dx = -2; dx <= 2 && !nearMountain; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize)
                            if (_cells[nx, ny].Type == TerrainType.Mountain)
                                nearMountain = true;
                    }
                if (!nearMountain) continue;
                positions.Add((x, y));
            }
        return positions;
    }

    /// <summary>统计各类型格子数量（用于调试日志）。</summary>
    public Dictionary<TerrainType, int> GetStats()
    {
        var stats = new Dictionary<TerrainType, int>();
        for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
            {
                var t = _cells[x, y].Type;
                stats.TryGetValue(t, out int c);
                stats[t] = c + 1;
            }
        return stats;
    }
}
