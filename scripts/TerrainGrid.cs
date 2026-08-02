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
        // ===== 山脉链生成系统：生成2-3条连续山脉链 =====
        // 每条山脉链沿一条带噪声的曲线从地图一侧延伸到另一侧
        // 沿途连续放置山峰+山脊，形成真正的连绵山脉

        int numRanges = 2 + rng.Next(2); // 2-3条山脉链
        var peakPositions = new System.Collections.Generic.List<(int x, int y, int size)>();

        for (int range = 0; range < numRanges; range++)
        {
            // 随机选择山脉链走向：水平/垂直/对角
            int dirType = rng.Next(3);
            int startX, startY, endX, endY;
            int margin = 4;

            switch (dirType)
            {
                case 0: // 水平：左到右
                    startX = margin;
                    endX = GridSize - margin;
                    startY = rng.Next(GridSize / 4, GridSize * 3 / 4);
                    endY = rng.Next(GridSize / 4, GridSize * 3 / 4);
                    break;
                case 1: // 垂直：上到下
                    startX = rng.Next(GridSize / 4, GridSize * 3 / 4);
                    endX = rng.Next(GridSize / 4, GridSize * 3 / 4);
                    startY = margin;
                    endY = GridSize - margin;
                    break;
                default: // 对角
                    startX = margin;
                    startY = rng.Next(margin, GridSize / 2);
                    endX = GridSize - margin;
                    endY = rng.Next(GridSize / 2, GridSize - margin);
                    break;
            }

            // 沿曲线放置连续山峰：用多段贝塞尔+噪声偏移
            int segments = 6 + rng.Next(5); // 6-10个山峰（更密的山峰确保连绵感）
            float prevNoise = (float)(rng.NextDouble() - 0.5) * 6;
            for (int s = 0; s < segments; s++)
            {
                float t = (float)s / (segments - 1);
                // 线性插值主路径
                int cx = (int)(startX + (endX - startX) * t);
                int cy = (int)(startY + (endY - startY) * t);

                // 叠加噪声偏移使山脉弯曲，偏移渐变而非跳变
                float noiseT = t * 3.0f; // 先快速变化
                float noiseOffset = (float)(Math.Sin(noiseT * 2.5 + range * 1.7) * 4.0 +
                                           Math.Sin(noiseT * 5.3 + range * 3.1) * 2.0);
                // 垂直于主方向偏移
                if (dirType <= 1)
                {
                    // 水平/垂直山脉：偏移次要轴
                    if (endX != startX) cy += (int)noiseOffset;
                    else cx += (int)noiseOffset;
                }
                else
                {
                    // 对角：两者都偏
                    cx += (int)(noiseOffset * 0.7);
                    cy += (int)(noiseOffset * 0.7);
                }

                cx = Math.Clamp(cx, 4, GridSize - 5);
                cy = Math.Clamp(cy, 4, GridSize - 5);

                // 避开基地
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

                // 1. 山峰核心
                for (int dy = -peakSize; dy <= peakSize; dy++)
                {
                    for (int dx = -peakSize; dx <= peakSize; dx++)
                    {
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (dist > peakSize) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) continue;
                        if (IsBaseArea(x, y)) continue;

                        if (dist <= peakSize * 0.5f)
                        {
                            // 山峰核心：山脉，海拔3
                            _cells[x, y].Type = TerrainType.Mountain;
                            _cells[x, y].Elevation = 3;
                        }
                        else if (dist <= peakSize * 0.8f)
                        {
                            // 山腰：雪地+海拔2
                            if (_cells[x, y].Type != TerrainType.Road)
                            {
                                _cells[x, y].Type = TerrainType.Snow;
                                _cells[x, y].Elevation = 2;
                            }
                        }
                        else
                        {
                            // 山麓坡度
                            if (_cells[x, y].Elevation < 2 && !IsBaseArea(x, y) &&
                                _cells[x, y].Type != TerrainType.DeepWater)
                                _cells[x, y].Elevation = 2;
                        }
                    }
                }
                peakPositions.Add((cx, cy, peakSize));
            }

            // 连接相邻山峰（同一条链上的）
            int rangeStart = peakPositions.Count - segments;
            for (int i = rangeStart; i < peakPositions.Count - 1; i++)
            {
                var (x1, y1, _) = peakPositions[i];
                var (x2, y2, _) = peakPositions[i + 1];
                DrawMountainRidge(x1, y1, x2, y2, rng);
            }
        }

        // 补充几座孤立山峰（增加多样性）
        int extraPeaks = Math.Max(0, numMountains - peakPositions.Count);
        for (int i = 0; i < extraPeaks; i++)
        {
            int cx = rng.Next(5, GridSize - 5);
            int cy = rng.Next(5, GridSize - 5);
            if (IsBaseArea(cx, cy)) continue;
            if (_cells[cx, cy].Type == TerrainType.Mountain) continue;

            int peakSize = 2 + rng.Next(2);
            for (int dy = -peakSize; dy <= peakSize; dy++)
                for (int dx = -peakSize; dx <= peakSize; dx++)
                {
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dist > peakSize) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) continue;
                    if (IsBaseArea(x, y)) continue;
                    if (dist <= peakSize * 0.5f)
                    {
                        _cells[x, y].Type = TerrainType.Mountain;
                        _cells[x, y].Elevation = 3;
                    }
                    else if (_cells[x, y].Elevation < 2 && _cells[x, y].Type != TerrainType.DeepWater)
                    {
                        _cells[x, y].Type = TerrainType.Snow;
                        _cells[x, y].Elevation = 2;
                    }
                }
            peakPositions.Add((cx, cy, peakSize));
        }

        // 山脚碎石坡：山脉周围1-2格形成连续缓坡带
        GenerateMountainFoothills(rng);
    }

    /// <summary>山脚碎石坡：围绕所有山脉核心连续生成一圈海拔2缓坡，取代零散沙地。</summary>
    private void GenerateMountainFoothills(Random rng)
    {
        var changes = new System.Collections.Generic.List<(int x, int y)>();
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (_cells[x, y].Type != TerrainType.Grass) continue;
                if (_cells[x, y].Elevation != 1) continue;
                if (IsBaseArea(x, y)) continue;

                // 检查2格内是否有山脉/雪地
                int mountainCount = 0;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize)
                        {
                            var t = _cells[nx, ny].Type;
                            if (t == TerrainType.Mountain || t == TerrainType.Snow)
                                mountainCount++;
                        }
                    }
                // 越多山脉邻居，越倾向于变坡地
                if (mountainCount >= 3)
                {
                    _cells[x, y].Elevation = 2; // 升为缓坡
                    if (rng.NextDouble() < 0.5)
                        changes.Add((x, y));
                }
                else if (mountainCount >= 1 && rng.NextDouble() < 0.4)
                {
                    _cells[x, y].Elevation = 2; // 偶尔升坡
                }
            }
        }
        // 部分碎石坡变为沙地（干碎石外观）
        foreach (var (x, y) in changes)
            if (_cells[x, y].Elevation == 2 && _cells[x, y].Type == TerrainType.Grass)
                _cells[x, y].Type = TerrainType.Sand;
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

            // 山脊中心：强制设为山脉或雪地（无视已有海拔，覆盖一切非道路/深水格）
            var cellType = _cells[cx, cy].Type;
            if (cellType != TerrainType.Road && cellType != TerrainType.DeepWater)
            {
                // 50%山脉核心 + 50%雪地（更高的山脉比例让山脊看起来连续）
                if (rng.NextDouble() < 0.5)
                {
                    _cells[cx, cy].Type = TerrainType.Mountain;
                    _cells[cx, cy].Elevation = 3;
                }
                else
                {
                    _cells[cx, cy].Type = TerrainType.Snow;
                    _cells[cx, cy].Elevation = 2;
                }
            }

            // 山脊加宽：对周围2格内的格子也设为雪地+海拔2（形成连续山脊宽度）
            for (int rdy = -1; rdy <= 1; rdy++)
            {
                for (int rdx = -1; rdx <= 1; rdx++)
                {
                    if (rdx == 0 && rdy == 0) continue;
                    int nx = cx + rdx, ny = cy + rdy;
                    if (nx < 0 || nx >= GridSize || ny < 0 || ny >= GridSize) continue;
                    if (IsBaseArea(nx, ny)) continue;
                    var nt = _cells[nx, ny].Type;
                    if (nt == TerrainType.Road || nt == TerrainType.DeepWater ||
                        nt == TerrainType.Mountain) continue;
                    // 设为雪地+海拔2
                    _cells[nx, ny].Type = TerrainType.Snow;
                    _cells[nx, ny].Elevation = 2;
                }
            }

            // 再外圈：海拔2缓坡（不改变地形类型，只升海拔）
            for (int rdy = -2; rdy <= 2; rdy++)
            {
                for (int rdx = -2; rdx <= 2; rdx++)
                {
                    if (Math.Abs(rdx) <= 1 && Math.Abs(rdy) <= 1) continue; // 内圈已处理
                    int nx = cx + rdx, ny = cy + rdy;
                    if (nx < 0 || nx >= GridSize || ny < 0 || ny >= GridSize) continue;
                    if (IsBaseArea(nx, ny)) continue;
                    if (_cells[nx, ny].Elevation < 2 && _cells[nx, ny].Type != TerrainType.DeepWater)
                        _cells[nx, ny].Elevation = 2;
                }
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
        // ===== 平滑曲流河流：用三次贝塞尔曲线生成自然蜿蜒河道 =====
        // 从地图一边出发，沿贝塞尔曲线蜿蜒到另一边，避免直角转折

        int startEdge = rng.Next(4);
        int startX, startY, endX, endY;
        int margin = 2;

        switch (startEdge)
        {
            case 0: // 上到下
                startX = rng.Next(GridSize / 4, GridSize * 3 / 4);
                startY = margin;
                endX = rng.Next(GridSize / 4, GridSize * 3 / 4);
                endY = GridSize - margin;
                break;
            case 1: // 下到上
                startX = rng.Next(GridSize / 4, GridSize * 3 / 4);
                startY = GridSize - margin;
                endX = rng.Next(GridSize / 4, GridSize * 3 / 4);
                endY = margin;
                break;
            case 2: // 左到右
                startX = margin;
                startY = rng.Next(GridSize / 4, GridSize * 3 / 4);
                endX = GridSize - margin;
                endY = rng.Next(GridSize / 4, GridSize * 3 / 4);
                break;
            default: // 右到左
                startX = GridSize - margin;
                startY = rng.Next(GridSize / 4, GridSize * 3 / 4);
                endX = margin;
                endY = rng.Next(GridSize / 4, GridSize * 3 / 4);
                break;
        }

        // 两个控制点：在起终点之间，大幅偏移制造蜿蜒
        float cp1Offset = (float)((rng.NextDouble() - 0.5) * GridSize * 0.5);
        float cp2Offset = (float)((rng.NextDouble() - 0.5) * GridSize * 0.5);
        float midT = 0.33f, midT2 = 0.66f;
        float cp1X = startX + (endX - startX) * midT + (endY - startY != 0 ? cp1Offset : 0);
        float cp1Y = startY + (endY - startY) * midT + (endX - startX != 0 ? cp1Offset : 0);
        float cp2X = startX + (endX - startX) * midT2 + (endY - startY != 0 ? cp2Offset : 0);
        float cp2Y = startY + (endY - startY) * midT2 + (endX - startX != 0 ? cp2Offset : 0);

        // 沿贝塞尔曲线生成河道，用足够细的步长确保平滑
        int steps = GridSize * 3;
        int widenCountdown = 8 + rng.Next(12);
        var riverCells = new System.Collections.Generic.List<(int x, int y)>();

        for (int s = 0; s <= steps; s++)
        {
            float t = (float)s / steps;
            // 三次贝塞尔曲线
            float u = 1 - t;
            float x = u * u * u * startX + 3 * u * u * t * cp1X + 3 * u * t * t * cp2X + t * t * t * endX;
            float y = u * u * u * startY + 3 * u * u * t * cp1Y + 3 * u * t * t * cp2Y + t * t * t * endY;

            int gx = (int)Math.Round(x);
            int gy = (int)Math.Round(y);
            gx = Math.Clamp(gx, 0, GridSize - 1);
            gy = Math.Clamp(gy, 0, GridSize - 1);

            if (!IsBaseArea(gx, gy))
            {
                // 河流中心=深水
                _cells[gx, gy].Type = TerrainType.DeepWater;
                _cells[gx, gy].Elevation = 0;
                // 两岸=浅水（1格宽岸滩）
                foreach (var (nx, ny) in GetNeighbors4(gx, gy))
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
                riverCells.Add((gx, gy));
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
                        int lx = gx + ldx, ly = gy + ldy;
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
                widenCountdown = 12 + rng.Next(15);
            }
        }

        // 生成1-2条支流（贝塞尔曲线）
        int tributaries = 1 + rng.Next(2);
        for (int t = 0; t < tributaries; t++)
        {
            GenerateTributaryBezier(rng, riverCells);
        }
    }

    /// <summary>贝塞尔曲线支流：从地图边缘蜿蜒到主河道附近汇入。</summary>
    private void GenerateTributaryBezier(Random rng, System.Collections.Generic.List<(int x, int y)> mainRiver)
    {
        if (mainRiver.Count == 0) return;

        // 从主河道取一个汇入点
        int targetIdx = rng.Next(mainRiver.Count / 4, mainRiver.Count * 3 / 4);
        var (targetX, targetY) = mainRiver[targetIdx];

        // 从最近的边缘出发
        int startEdge = rng.Next(4);
        int startX, startY;
        switch (startEdge)
        {
            case 0: startX = rng.Next(4, GridSize - 4); startY = 0; break;
            case 1: startX = rng.Next(4, GridSize - 4); startY = GridSize - 1; break;
            case 2: startX = 0; startY = rng.Next(4, GridSize - 4); break;
            default: startX = GridSize - 1; startY = rng.Next(4, GridSize - 4); break;
        }

        // 贝塞尔控制点偏移
        float offset1 = (float)(rng.NextDouble() - 0.5) * GridSize * 0.3f;
        float offset2 = (float)(rng.NextDouble() - 0.5) * GridSize * 0.3f;
        float cp1X = startX + (targetX - startX) * 0.33f + offset1;
        float cp1Y = startY + (targetY - startY) * 0.33f + offset2;
        float cp2X = startX + (targetX - startX) * 0.66f - offset1;
        float cp2Y = startY + (targetY - startY) * 0.66f - offset2;

        int steps = GridSize;
        for (int s = 0; s <= steps; s++)
        {
            float t = (float)s / steps;
            float u = 1 - t;
            int x = (int)Math.Round(u * u * u * startX + 3 * u * u * t * cp1X + 3 * u * t * t * cp2X + t * t * t * targetX);
            int y = (int)Math.Round(u * u * u * startY + 3 * u * u * t * cp1Y + 3 * u * t * t * cp2Y + t * t * t * targetY);
            x = Math.Clamp(x, 0, GridSize - 1);
            y = Math.Clamp(y, 0, GridSize - 1);

            if (x == targetX && y == targetY) break; // 汇入主干

            if (!IsBaseArea(x, y) && _cells[x, y].Type != TerrainType.DeepWater)
            {
                _cells[x, y].Type = TerrainType.ShallowWater; // 支流较浅
                _cells[x, y].Elevation = 1;
            }
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

    /// <summary>海岸线柔化：所有水体边缘的陆地变为沙地（沙滩过渡效果）。
    /// 扩展版：覆盖所有陆地类型（草地/田地/雪地/城市），多格宽度过渡。</summary>
    private void SoftenCoastlines()
    {
        var changes = new System.Collections.Generic.List<(int x, int y, int dist)>();
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                var t = _cells[x, y].Type;
                // 跳过水面和道路（道路保持原样，不变成沙滩）
                if (t == TerrainType.ShallowWater || t == TerrainType.DeepWater ||
                    t == TerrainType.Road || t == TerrainType.Bridge || t == TerrainType.Tunnel)
                    continue;
                if (t == TerrainType.Mountain || t == TerrainType.Cliff) continue;
                if (_cells[x, y].Elevation > 1) continue; // 只柔化低地海岸

                // 查找最近的水体距离（1-2格范围内）
                int minWaterDist = int.MaxValue;
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize)
                        {
                            var nt = _cells[nx, ny].Type;
                            if (nt == TerrainType.ShallowWater || nt == TerrainType.DeepWater)
                            {
                                int d = Math.Abs(dx) + Math.Abs(dy);
                                if (d < minWaterDist) minWaterDist = d;
                            }
                        }
                    }
                }

                if (minWaterDist <= 2)
                    changes.Add((x, y, minWaterDist));
            }
        }

        foreach (var (x, y, dist) in changes)
        {
            if (IsBaseArea(x, y)) continue;
            // 1格距离=必变沙地，2格=50%概率（渐变过渡）
            if (dist == 1)
                _cells[x, y].Type = TerrainType.Sand;
            else if (dist == 2 && (x * 31 + y * 17) % 2 == 0) // 确定性的50%
                _cells[x, y].Type = TerrainType.Sand;
        }
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
        // 默认主题：仅在河流附近（河岸沙地）生成小片沙地
        // 山脚碎石坡已由 GenerateMountainFoothills 处理
        if (MapConfig.Theme != MapConfig.MapTheme.Default) return;

        // 河岸沙地：浅水/深水边缘1格的草地→沙地（补充海岸线柔化遗漏的区域）
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (_cells[x, y].Type != TerrainType.Grass) continue;
                if (_cells[x, y].Elevation != 1) continue;
                if (IsBaseArea(x, y)) continue;

                // 检查1格内是否有浅水（河岸）
                bool nearShallow = false;
                foreach (var (nx, ny) in GetNeighbors4(x, y))
                {
                    if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize)
                    {
                        if (_cells[nx, ny].Type == TerrainType.ShallowWater)
                        {
                            nearShallow = true;
                            break;
                        }
                    }
                }
                // 40%概率变沙地（不是所有河岸都变）
                if (nearShallow && rng.NextDouble() < 0.4)
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

        // 道路连通性修补：检查所有道路段是否连通，用最短路径连接断裂段
        RepairRoadConnectivity();
    }

    /// <summary>修补道路连通性：找到所有道路段，用BFS连接断裂的路段。</summary>
    private void RepairRoadConnectivity()
    {
        // 找到所有道路格
        var roadCells = new System.Collections.Generic.List<(int x, int y)>();
        for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
                if (_cells[x, y].Type == TerrainType.Road)
                    roadCells.Add((x, y));

        if (roadCells.Count < 2) return;

        // 用BFS标记连通区域
        var visited = new bool[GridSize, GridSize];
        var regions = new System.Collections.Generic.List<System.Collections.Generic.List<(int x, int y)>>();

        foreach (var (sx, sy) in roadCells)
        {
            if (visited[sx, sy]) continue;
            var region = new System.Collections.Generic.List<(int x, int y)>();
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue((sx, sy));
            visited[sx, sy] = true;

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                region.Add((cx, cy));
                foreach (var (nx, ny) in GetNeighbors4(cx, cy))
                {
                    if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize && !visited[nx, ny])
                    {
                        if (_cells[nx, ny].Type == TerrainType.Road)
                        {
                            visited[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
            }
            regions.Add(region);
        }

        // 如果有多个断裂区域，用最短路径连接它们
        while (regions.Count > 1)
        {
            // 找到两个最近的区域
            double minDist = double.MaxValue;
            int bestI = 0, bestJ = 1;
            (int x, int y) bestFrom = (0, 0), bestTo = (0, 0);

            for (int i = 0; i < regions.Count; i++)
            {
                for (int j = i + 1; j < regions.Count; j++)
                {
                    foreach (var (ix, iy) in regions[i])
                    {
                        foreach (var (jx, jy) in regions[j])
                        {
                            double d = (ix - jx) * (ix - jx) + (iy - jy) * (iy - jy);
                            if (d < minDist)
                            {
                                minDist = d;
                                bestI = i; bestJ = j;
                                bestFrom = (ix, iy);
                                bestTo = (jx, jy);
                            }
                        }
                    }
                }
            }

            if (minDist == double.MaxValue) break;

            // 用BFS在两点间找可建路路径
            var path = FindRoadPath(bestFrom.x, bestFrom.y, bestTo.x, bestTo.y);
            if (path != null)
            {
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

            // 合并区域
            regions[bestI].AddRange(regions[bestJ]);
            regions.RemoveAt(bestJ);
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
