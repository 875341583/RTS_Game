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
    /// <summary>网格边长（格数）</summary>
    public const int GridSize = 32;
    /// <summary>每格像素大小</summary>
    public const int TileSize = 64;
    /// <summary>地图像素大小 = GridSize * TileSize</summary>
    public const float MapPixelSize = GridSize * TileSize; // 2048

    private readonly TerrainCell[,] _cells = new TerrainCell[GridSize, GridSize];

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

    /// <summary>通过世界坐标获取格子索引。</summary>
    public void WorldToGrid(float worldX, float worldY, out int gx, out int gy)
    {
        gx = (int)(worldX / TileSize);
        gy = (int)(worldY / TileSize);
        gx = Math.Clamp(gx, 0, GridSize - 1);
        gy = Math.Clamp(gy, 0, GridSize - 1);
    }

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
    /// </summary>
    public static float GetSpeedModifier(TerrainUnitCategory unitCat, TerrainType terrainType, int elevation, int targetElevation)
    {
        // 空军不受地形影响
        if (unitCat == TerrainUnitCategory.Air) return 1.0f;

        // 高度差判定
        int elevDiff = targetElevation - elevation;
        if (elevDiff >= 2) return 0f; // 悬崖，不可攀爬（需削平）

        // 按地形类型查表
        float baseMod = terrainType switch
        {
            TerrainType.Road => unitCat switch
            {
                TerrainUnitCategory.Infantry => 1.2f,
                TerrainUnitCategory.LightVehicle => 1.3f,
                TerrainUnitCategory.HeavyVehicle => 1.2f,
                TerrainUnitCategory.Harvester => 1.2f,
                TerrainUnitCategory.Engineer => 1.2f,
                TerrainUnitCategory.EngineerVehicle => 1.2f,
                TerrainUnitCategory.Naval => 0f,
                _ => 1.0f,
            },
            TerrainType.Grass => 1.0f,
            TerrainType.Sand => unitCat switch
            {
                TerrainUnitCategory.Infantry => 0.8f,
                TerrainUnitCategory.LightVehicle => 0.6f,
                TerrainUnitCategory.HeavyVehicle => 0.4f,
                TerrainUnitCategory.Harvester => 0.7f,
                TerrainUnitCategory.Engineer => 0.8f,
                TerrainUnitCategory.EngineerVehicle => 0.7f,
                TerrainUnitCategory.Naval => 0f,
                _ => 0.6f,
            },
            TerrainType.Snow => unitCat switch
            {
                TerrainUnitCategory.Infantry => 0.7f,
                TerrainUnitCategory.LightVehicle => 0.5f,
                TerrainUnitCategory.HeavyVehicle => 0.4f,
                TerrainUnitCategory.Harvester => 0.6f,
                TerrainUnitCategory.Engineer => 0.7f,
                TerrainUnitCategory.EngineerVehicle => 0.6f,
                TerrainUnitCategory.Naval => 0f,
                _ => 0.5f,
            },
            TerrainType.City => unitCat switch
            {
                TerrainUnitCategory.Infantry => 0.9f,
                TerrainUnitCategory.LightVehicle => 0.8f,
                TerrainUnitCategory.HeavyVehicle => 0.7f,
                TerrainUnitCategory.Harvester => 0.8f,
                TerrainUnitCategory.Engineer => 0.9f,
                TerrainUnitCategory.EngineerVehicle => 0.8f,
                TerrainUnitCategory.Naval => 0f,
                _ => 0.8f,
            },
            TerrainType.Field => unitCat switch
            {
                TerrainUnitCategory.Infantry => 0.9f,
                TerrainUnitCategory.LightVehicle => 0.7f,
                TerrainUnitCategory.HeavyVehicle => 0.5f,
                TerrainUnitCategory.Harvester => 0.8f,
                TerrainUnitCategory.Engineer => 0.9f,
                TerrainUnitCategory.EngineerVehicle => 0.8f,
                TerrainUnitCategory.Naval => 0f,
                _ => 0.7f,
            },
            TerrainType.ShallowWater => unitCat switch
            {
                TerrainUnitCategory.Infantry => 0.3f,
                TerrainUnitCategory.LightVehicle => 0.2f,
                TerrainUnitCategory.HeavyVehicle => 0.1f,
                TerrainUnitCategory.Harvester => 0f,
                TerrainUnitCategory.Engineer => 0.3f,
                TerrainUnitCategory.EngineerVehicle => 0.2f,
                TerrainUnitCategory.Naval => 1.0f,
                _ => 0.2f,
            },
            TerrainType.DeepWater => unitCat switch
            {
                TerrainUnitCategory.Naval => 1.0f,
                _ => 0f, // 地面单位不可通行深水（需架桥/隧道）
            },
            TerrainType.Mountain => unitCat switch
            {
                TerrainUnitCategory.Infantry => 0.3f,
                TerrainUnitCategory.LightVehicle => 0.2f,
                TerrainUnitCategory.HeavyVehicle => 0f,
                TerrainUnitCategory.Harvester => 0f,
                TerrainUnitCategory.Engineer => 0.3f,
                TerrainUnitCategory.EngineerVehicle => 0f,
                TerrainUnitCategory.Naval => 0f,
                _ => 0f,
            },
            TerrainType.Cliff => 0f, // 悬崖不可通行
            TerrainType.Bridge => unitCat switch
            {
                TerrainUnitCategory.Infantry => 1.0f,
                TerrainUnitCategory.LightVehicle => 1.0f,
                TerrainUnitCategory.HeavyVehicle => 0.9f,
                TerrainUnitCategory.Harvester => 1.0f,
                TerrainUnitCategory.Engineer => 1.0f,
                TerrainUnitCategory.EngineerVehicle => 1.0f,
                TerrainUnitCategory.Naval => 0f,
                _ => 1.0f,
            },
            TerrainType.Tunnel => unitCat switch
            {
                TerrainUnitCategory.Infantry => 0.9f,
                TerrainUnitCategory.LightVehicle => 0.9f,
                TerrainUnitCategory.HeavyVehicle => 0.8f,
                TerrainUnitCategory.Harvester => 0.9f,
                TerrainUnitCategory.Engineer => 0.9f,
                TerrainUnitCategory.EngineerVehicle => 0.9f,
                TerrainUnitCategory.Naval => 0f,
                _ => 0.9f,
            },
            _ => 1.0f,
        };

        // 缓坡额外速度惩罚
        if (elevDiff == 1 && baseMod > 0f)
        {
            float slopeMod = unitCat switch
            {
                TerrainUnitCategory.Infantry => 0.5f,
                TerrainUnitCategory.LightVehicle => 0.3f,
                TerrainUnitCategory.HeavyVehicle => 0.2f,
                TerrainUnitCategory.Harvester => 0.3f,
                TerrainUnitCategory.Engineer => 0.5f,
                TerrainUnitCategory.EngineerVehicle => 0.3f,
                _ => 0.5f,
            };
            baseMod *= slopeMod;
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
        var rng = new Random((int)(seed & 0x7FFFFFFF));

        // 1. 初始化全部为草地+平地
        for (int y = 0; y < GridSize; y++)
            for (int x = 0; x < GridSize; x++)
                _cells[x, y] = TerrainCell.Default;

        // 2. 生成山脉（2-3个山脉区域，高海拔3）
        GenerateMountains(rng);

        // 3. 生成丘陵/高地（3-4个区域，海拔2）
        GenerateHills(rng);

        // 4. 生成水域（1-2条河流 + 1-2个湖泊）
        GenerateWater(rng);

        // 5. 生成雪地（山脉附近高处）
        GenerateSnow(rng);

        // 6. 生成沙地（远离水域的低地）
        GenerateSand(rng);

        // 7. 生成田地（平地上的农田区域）
        GenerateFields(rng);

        // 8. 生成城市区（地图中部的铺装路面区）
        GenerateCity(rng);

        // 9. 生成道路（连接关键区域的铺装路面）
        GenerateRoads(rng);

        // 10. 确保基地起始位置为平地+草地
        EnsureBaseAreas();

        // 11. 分类深水区域
        ClassifyDeepWater();
    }

    private void GenerateMountains(Random rng)
    {
        int numMountains = 2 + rng.Next(2); // 2-3个
        for (int m = 0; m < numMountains; m++)
        {
            int cx = rng.Next(4, GridSize - 4);
            int cy = rng.Next(4, GridSize - 4);
            // 避开地图中央8x8区域（战略点区域）
            if (Math.Abs(cx - 16) < 4 && Math.Abs(cy - 16) < 4) continue;
            // 避开边缘基地区域
            if (IsBaseArea(cx, cy)) continue;

            int size = 1 + rng.Next(2); // 1-2格半径
            for (int dy = -size; dy <= size; dy++)
            {
                for (int dx = -size; dx <= size; dx++)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) > size) continue; // 菱形
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) continue;
                    if (IsBaseArea(x, y)) continue;
                    _cells[x, y].Type = TerrainType.Mountain;
                    _cells[x, y].Elevation = 3;
                }
            }
        }
    }

    private void GenerateHills(Random rng)
    {
        int numHills = 3 + rng.Next(2); // 3-4个
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

    private void GenerateWater(Random rng)
    {
        // 生成1条河流（从地图一侧蜿蜒到另一侧）
        GenerateRiver(rng);

        // 生成1-2个湖泊
        int numLakes = 1 + rng.Next(2);
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
        // 河流从随机边开始，蜿蜒到另一边
        int startEdge = rng.Next(4);
        int x, y, dx, dy;
        switch (startEdge)
        {
            case 0: x = rng.Next(4, GridSize - 4); y = 0; dx = 0; dy = 1; break;   // 从上方
            case 1: x = rng.Next(4, GridSize - 4); y = GridSize - 1; dx = 0; dy = -1; break; // 从下方
            case 2: x = 0; y = rng.Next(4, GridSize - 4); dx = 1; dy = 0; break;   // 从左方
            default: x = GridSize - 1; y = rng.Next(4, GridSize - 4); dx = -1; dy = 0; break; // 从右方
        }

        int steps = GridSize * 2;
        for (int s = 0; s < steps; s++)
        {
            if (x < 0 || x >= GridSize || y < 0 || y >= GridSize) break;
            if (!IsBaseArea(x, y))
            {
                // 河流中心=深水
                _cells[x, y].Type = TerrainType.DeepWater;
                _cells[x, y].Elevation = 0;
                // 两岸=浅水
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
            }
            // 蜿蜒：80%继续直走，20%横向偏移
            if (rng.NextDouble() < 0.2f)
            {
                // 横向偏移
                if (dx == 0) x += rng.Next(2) == 0 ? 1 : -1;
                else y += rng.Next(2) == 0 ? 1 : -1;
            }
            x += dx;
            y += dy;
        }
    }

    private void GenerateSnow(Random rng)
    {
        // 山脉周围2格范围内的高地变为雪地
        for (int y = 0; y < GridSize; y++)
        {
            for (int x = 0; x < GridSize; x++)
            {
                if (_cells[x, y].Elevation >= 2 && _cells[x, y].Type == TerrainType.Grass)
                {
                    // 检查附近是否有山脉
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

    private void GenerateSand(Random rng)
    {
        // 在远离水域的低地随机生成沙地斑块
        int sandPatches = 4 + rng.Next(3); // 4-6个
        for (int p = 0; p < sandPatches; p++)
        {
            int cx = rng.Next(2, GridSize - 2);
            int cy = rng.Next(2, GridSize - 2);
            if (IsBaseArea(cx, cy)) continue;
            if (_cells[cx, cy].Type != TerrainType.Grass) continue;

            int w = 1 + rng.Next(3);
            int h = 1 + rng.Next(3);
            for (int dy = -h / 2; dy <= h / 2; dy++)
                for (int dx = -w / 2; dx <= w / 2; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize)
                        if (_cells[nx, ny].Type == TerrainType.Grass && !IsBaseArea(nx, ny))
                            _cells[nx, ny].Type = TerrainType.Sand;
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
        // 地图中央附近生成一个城市区域（3-4格方块的城市路面）
        int cx = 14 + rng.Next(4); // 14-17
        int cy = 14 + rng.Next(4);
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
        // 道路：从地图中心向四个方向延伸（沿用十字形骨架）
        int mid = GridSize / 2;
        for (int i = 0; i < GridSize; i++)
        {
            // 水平主干道
            if (_cells[i, mid].Type == TerrainType.Grass || _cells[i, mid].Type == TerrainType.Sand || _cells[i, mid].Type == TerrainType.Field)
                if (_cells[i, mid].Elevation <= 1)
                    _cells[i, mid].Type = TerrainType.Road;
            // 垂直主干道
            if (_cells[mid, i].Type == TerrainType.Grass || _cells[mid, i].Type == TerrainType.Sand || _cells[mid, i].Type == TerrainType.Field)
                if (_cells[mid, i].Elevation <= 1)
                    _cells[mid, i].Type = TerrainType.Road;
        }

        // 对角线道路
        for (int i = 2; i <= mid - 2; i++)
        {
            if (_cells[i, i].Type == TerrainType.Grass && _cells[i, i].Elevation <= 1)
                _cells[i, i].Type = TerrainType.Road;
            int j = GridSize - 1 - i;
            if (j >= 0 && j < GridSize)
                if (_cells[j, i].Type == TerrainType.Grass && _cells[j, i].Elevation <= 1)
                    _cells[j, i].Type = TerrainType.Road;
        }
    }

    private void EnsureBaseAreas()
    {
        // 确保所有8个阵营的起始区域为平地+草地（3x3范围）
        var basePositions = new (int x, int y)[]
        {
            (0, 0), (27, 27), (27, 0), (0, 27),
            (14, 0), (14, 27), (0, 14), (27, 14)
        };
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

    /// <summary>判断是否在基地起始区域附近（3格范围）。</summary>
    private static bool IsBaseArea(int x, int y)
    {
        var basePositions = new (int x, int y)[]
        {
            (0, 0), (27, 27), (27, 0), (0, 27),
            (14, 0), (14, 27), (0, 14), (27, 14)
        };
        foreach (var (bx, by) in basePositions)
            if (Math.Abs(x - bx) <= 2 && Math.Abs(y - by) <= 2)
                return true;
        return false;
    }

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
