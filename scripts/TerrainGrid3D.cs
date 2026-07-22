using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 3D地形网格 — 存储每个格子的高度、类型、桥梁/隧道标记。
/// 同时负责生成3D地形Mesh和NavigationMesh。
/// 移植自2D TerrainGrid，保留全部游戏逻辑，新增3D渲染。
/// </summary>
public class TerrainGrid3D
{
    /// <summary>网格边长（格数）</summary>
    public const int GridSize = 32;
    /// <summary>每格3D单位大小（米）</summary>
    public const float CellSize = 4.0f;
    /// <summary>地图3D大小 = GridSize * CellSize</summary>
    public static readonly float MapWorldSize = GridSize * CellSize; // 128

    private readonly TerrainCell[,] _cells = new TerrainCell[GridSize, GridSize];

    // ======== 3D 渲染根节点 ========
    private Node3D _terrainRoot;
    private NavigationRegion3D _navRegion;
    private StandardMaterial3D _grassMat, _sandMat, _snowMat, _cityMat, _fieldMat;
    private StandardMaterial3D _shallowWaterMat, _deepWaterMat, _mountainMat, _roadMat;
    private StandardMaterial3D _cliffMat, _bridgeMat, _tunnelMat;

    // 地形颜色常量
    private static readonly Color GrassColor = new(0.18f, 0.38f, 0.14f);
    private static readonly Color GrassColor2 = new(0.22f, 0.44f, 0.16f);
    private static readonly Color SandColor = new(0.72f, 0.62f, 0.38f);
    private static readonly Color SnowColor = new(0.85f, 0.88f, 0.92f);
    private static readonly Color CityColor = new(0.42f, 0.40f, 0.38f);
    private static readonly Color FieldColor = new(0.45f, 0.35f, 0.15f);
    private static readonly Color ShallowWaterColor = new(0.15f, 0.35f, 0.60f);
    private static readonly Color DeepWaterColor = new(0.06f, 0.16f, 0.38f);
    private static readonly Color MountainColor = new(0.45f, 0.38f, 0.30f);
    private static readonly Color RoadColor = new(0.25f, 0.24f, 0.22f);
    private static readonly Color CliffColor = new(0.35f, 0.30f, 0.25f);
    private static readonly Color BridgeColor = new(0.55f, 0.42f, 0.28f);
    private static readonly Color TunnelColor = new(0.15f, 0.14f, 0.13f);

    public TerrainGrid3D(Node3D terrainRoot, NavigationRegion3D navRegion)
    {
        _terrainRoot = terrainRoot;
        _navRegion = navRegion;
        InitMaterials();
    }

    private void InitMaterials()
    {
        _grassMat = CreateTerrainMat(GrassColor, 0.9f, 0f);
        _sandMat = CreateTerrainMat(SandColor, 0.95f, 0f);
        _snowMat = CreateTerrainMat(SnowColor, 0.8f, 0f);
        _cityMat = CreateTerrainMat(CityColor, 0.7f, 0.1f);
        _fieldMat = CreateTerrainMat(FieldColor, 0.9f, 0f);
        _shallowWaterMat = CreateTerrainMat(ShallowWaterColor, 0.1f, 0.4f);
        _shallowWaterMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _deepWaterMat = CreateTerrainMat(DeepWaterColor, 0.1f, 0.5f);
        _deepWaterMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _mountainMat = CreateTerrainMat(MountainColor, 0.9f, 0f);
        _roadMat = CreateTerrainMat(RoadColor, 0.8f, 0f);
        _cliffMat = CreateTerrainMat(CliffColor, 0.95f, 0f);
        _bridgeMat = CreateTerrainMat(BridgeColor, 0.8f, 0f);
        _tunnelMat = CreateTerrainMat(TunnelColor, 0.9f, 0f);
    }

    private static StandardMaterial3D CreateTerrainMat(Color color, float roughness, float metallic)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = roughness,
            Metallic = metallic,
        };
    }

    // ======== 格子数据访问（与2D版逻辑完全一致）========

    public TerrainCell GetCell(int gx, int gy)
    {
        if (gx < 0 || gx >= GridSize || gy < 0 || gy >= GridSize)
            return DefaultBorder();
        return _cells[gx, gy];
    }

    public void SetCell(int gx, int gy, TerrainCell cell)
    {
        if (gx >= 0 && gx < GridSize && gy >= 0 && gy < GridSize)
            _cells[gx, gy] = cell;
    }

    private static TerrainCell DefaultBorder() => new()
    {
        Type = TerrainType.Cliff,
        Elevation = 3,
        HasBridge = false,
        HasTunnel = false,
        WaterWidth = WaterWidthClass.None,
        WaterRegionId = -1,
    };

    /// <summary>世界坐标→网格坐标</summary>
    public void WorldToGrid(float worldX, float worldZ, out int gx, out int gy)
    {
        gx = (int)(worldX / CellSize);
        gy = (int)(worldZ / CellSize);
        gx = Math.Clamp(gx, 0, GridSize - 1);
        gy = Math.Clamp(gy, 0, GridSize - 1);
    }

    /// <summary>网格坐标→世界坐标（格子中心）</summary>
    public static Vector3 GridToWorld(int gx, int gy)
    {
        return new Vector3(
            gx * CellSize + CellSize * 0.5f,
            0,
            gy * CellSize + CellSize * 0.5f);
    }

    /// <summary>世界坐标→地形格子</summary>
    public TerrainCell GetCellAtWorld(float worldX, float worldZ)
    {
        WorldToGrid(worldX, worldZ, out int gx, out int gy);
        return GetCell(gx, gy);
    }

    public void ModifyCell(int gx, int gy, TerrainCell cell)
    {
        SetCell(gx, gy, cell);
    }

    public TerrainType GetEffectiveType(int gx, int gy)
    {
        var cell = GetCell(gx, gy);
        if (cell.HasBridge && (cell.Type == TerrainType.ShallowWater || cell.Type == TerrainType.DeepWater))
            return TerrainType.Bridge;
        if (cell.HasTunnel && cell.Type == TerrainType.Mountain)
            return TerrainType.Tunnel;
        return cell.Type;
    }

    /// <summary>获取格子高度（3D Y坐标）</summary>
    public static float GetElevationY(int elevation)
    {
        return elevation switch
        {
            0 => -1.0f,   // 水面以下
            1 => 0.0f,    // 平地
            2 => 1.5f,    // 丘陵
            3 => 3.0f,    // 山顶
            _ => 0.0f,
        };
    }

    // ======== 速度修正查询（与2D版完全一致）========

    public static float GetSpeedModifier(TerrainUnitCategory unitCat, TerrainType terrainType, int elevation, int targetElevation)
    {
        if (unitCat == TerrainUnitCategory.Air) return 1.0f;

        int elevDiff = targetElevation - elevation;
        if (elevDiff >= 2) return 0f;

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
                _ => 0f,
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
            TerrainType.Cliff => 0f,
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
                _ => 0.4f,
            };
            baseMod *= slopeMod;
        }

        return baseMod;
    }

    // ======== 地图生成（种子驱动）========

    private Random _mapRng = new(42);

    public void GenerateMap(int seed)
    {
        _mapRng = new Random(seed);

        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                _cells[x, y] = TerrainCell.Default;
            }
        }

        // 河流：从一边到另一边的弯曲河流
        int riverY = 8 + _mapRng.Next(0, 4);
        for (int x = 0; x < GridSize; x++)
        {
            int ry = riverY + (int)(Math.Sin(x * 0.3) * 2);
            ry = Math.Clamp(ry, 1, GridSize - 2);
            // 深水主体
            _cells[x, ry] = new TerrainCell { Type = TerrainType.DeepWater, Elevation = 0, WaterWidth = WaterWidthClass.River, WaterRegionId = 0 };
            // 浅水两岸
            if (ry > 0)
                _cells[x, ry - 1] = new TerrainCell { Type = TerrainType.ShallowWater, Elevation = 0, WaterWidth = WaterWidthClass.None, WaterRegionId = -1 };
            if (ry < GridSize - 1)
                _cells[x, ry + 1] = new TerrainCell { Type = TerrainType.ShallowWater, Elevation = 0, WaterWidth = WaterWidthClass.None, WaterRegionId = -1 };
        }

        // 山脉区域（一角）
        for (int x = GridSize - 8; x < GridSize - 2; x++)
        {
            for (int y = GridSize - 8; y < GridSize - 2; y++)
            {
                float d = Mathf.Sqrt(Mathf.Pow(x - (GridSize - 5), 2) + Mathf.Pow(y - (GridSize - 5), 2));
                if (d < 5f && _cells[x, y].Type == TerrainType.Grass)
                {
                    if (d < 2f)
                        _cells[x, y] = new TerrainCell { Type = TerrainType.Mountain, Elevation = 3, HasBridge = false, HasTunnel = false, WaterWidth = WaterWidthClass.None, WaterRegionId = -1 };
                    else if (d < 3.5f && _mapRng.NextDouble() < 0.6)
                        _cells[x, y] = new TerrainCell { Type = TerrainType.Mountain, Elevation = 2, HasBridge = false, HasTunnel = false, WaterWidth = WaterWidthClass.None, WaterRegionId = -1 };
                }
            }
        }

        // 沙地区域（另一角）
        for (int x = 0; x < 6; x++)
        {
            for (int y = GridSize - 6; y < GridSize; y++)
            {
                if (_cells[x, y].Type == TerrainType.Grass && _mapRng.NextDouble() < 0.5)
                    _cells[x, y] = new TerrainCell { Type = TerrainType.Sand, Elevation = 1, HasBridge = false, HasTunnel = false, WaterWidth = WaterWidthClass.None, WaterRegionId = -1 };
            }
        }

        // 城市区域（中心）
        for (int x = GridSize / 2 - 2; x < GridSize / 2 + 2; x++)
        {
            for (int y = GridSize / 2 - 2; y < GridSize / 2 + 2; y++)
            {
                if (_cells[x, y].Type == TerrainType.Grass)
                    _cells[x, y] = new TerrainCell { Type = TerrainType.City, Elevation = 1, HasBridge = false, HasTunnel = false, WaterWidth = WaterWidthClass.None, WaterRegionId = -1 };
            }
        }

        // 田地
        for (int i = 0; i < 5; i++)
        {
            int fx = _mapRng.Next(2, GridSize - 4);
            int fy = _mapRng.Next(2, GridSize - 4);
            for (int dx = 0; dx < 3; dx++)
            {
                for (int dy = 0; dy < 2; dy++)
                {
                    int cx = fx + dx, cy = fy + dy;
                    if (cx < GridSize && cy < GridSize && _cells[cx, cy].Type == TerrainType.Grass)
                        _cells[cx, cy] = new TerrainCell { Type = TerrainType.Field, Elevation = 1, HasBridge = false, HasTunnel = false, WaterWidth = WaterWidthClass.None, WaterRegionId = -1 };
                }
            }
        }

        // 道路：十字交叉
        int roadY = GridSize / 2;
        int roadX = GridSize / 2;
        for (int x = 4; x < GridSize - 4; x++)
        {
            if (_cells[x, roadY].Type != TerrainType.DeepWater && _cells[x, roadY].Type != TerrainType.Mountain)
                _cells[x, roadY] = new TerrainCell { Type = TerrainType.Road, Elevation = 1, HasBridge = false, HasTunnel = false, WaterWidth = WaterWidthClass.None, WaterRegionId = -1 };
        }
        for (int y = 4; y < GridSize - 4; y++)
        {
            if (_cells[roadX, y].Type != TerrainType.DeepWater && _cells[roadX, y].Type != TerrainType.Mountain)
                _cells[roadX, y] = new TerrainCell { Type = TerrainType.Road, Elevation = 1, HasBridge = false, HasTunnel = false, WaterWidth = WaterWidthClass.None, WaterRegionId = -1 };
        }

        // 桥梁：道路过河处自动架桥
        for (int x = 0; x < GridSize; x++)
        {
            if (_cells[x, roadY].Type == TerrainType.DeepWater || _cells[x, roadY].Type == TerrainType.ShallowWater)
            {
                var c = _cells[x, roadY];
                c.HasBridge = true;
                _cells[x, roadY] = c;
            }
        }
        for (int y = 0; y < GridSize; y++)
        {
            if (_cells[roadX, y].Type == TerrainType.DeepWater || _cells[roadX, y].Type == TerrainType.ShallowWater)
            {
                var c = _cells[roadX, y];
                c.HasBridge = true;
                _cells[roadX, y] = c;
            }
        }

        // 随机散布的草地高度变化
        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                if (_cells[x, y].Type == TerrainType.Grass && _mapRng.NextDouble() < 0.05)
                {
                    var c = _cells[x, y];
                    c.Elevation = 2;
                    _cells[x, y] = c;
                }
            }
        }
    }

    // ======== 3D 地形 Mesh 构建 ========

    /// <summary>构建可视3D地形Mesh（合并所有格子到一个Mesh）</summary>
    public void Build3DTerrainMesh()
    {
        // 使用SurfaceTool构建一个大Mesh，性能更好
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        var rng = new Random(99);

        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                var cell = _cells[x, y];
                float worldX = x * CellSize;
                float worldZ = y * CellSize;
                float elevationY = GetElevationY(cell.Elevation);
                TerrainType effType = GetEffectiveType(x, y);

                // 水面单独处理
                if (cell.Type == TerrainType.DeepWater || cell.Type == TerrainType.ShallowWater)
                {
                    // 水面在 y=-0.3，河底在 y=-1.0
                    AddQuadToSurface(surfaceTool, worldX, -0.3f, worldZ, CellSize, CellSize,
                        cell.Type == TerrainType.DeepWater ? _deepWaterMat : _shallowWaterMat, rng);
                    continue;
                }

                // 桥梁在水面上
                if (effType == TerrainType.Bridge)
                {
                    // 先画水面
                    AddQuadToSurface(surfaceTool, worldX, -0.3f, worldZ, CellSize, CellSize, _shallowWaterMat, rng);
                    // 再画桥面在 y=0.1
                    AddQuadToSurface(surfaceTool, worldX, 0.1f, worldZ, CellSize, CellSize, _bridgeMat, rng);
                    continue;
                }

                // 隧道在山脉中
                if (effType == TerrainType.Tunnel)
                {
                    AddQuadToSurface(surfaceTool, worldX, 0.05f, worldZ, CellSize, CellSize, _tunnelMat, rng);
                    continue;
                }

                // 普通= tile
                StandardMaterial3D mat = effType switch
                {
                    TerrainType.Grass => rng.NextDouble() < 0.5 ? _grassMat : _grassMat,
                    TerrainType.Sand => _sandMat,
                    TerrainType.Snow => _snowMat,
                    TerrainType.City => _cityMat,
                    TerrainType.Field => _fieldMat,
                    TerrainType.Road => _roadMat,
                    TerrainType.Mountain => _mountainMat,
                    TerrainType.Cliff => _cliffMat,
                    _ => _grassMat,
                };

                // 山脉有高度，用BoxMesh直接放置
                if (effType == TerrainType.Mountain)
                {
                    // 单独MeshInstance用于山脉（需要体积）
                    var mInst = new MeshInstance3D();
                    var boxMesh = new BoxMesh();
                    float mountainHeight = cell.Elevation == 3 ? 3.0f : 1.5f;
                    boxMesh.Size = new Vector3(CellSize, mountainHeight, CellSize);
                    mInst.Mesh = boxMesh;
                    mInst.MaterialOverride = mat;
                    mInst.Position = new Vector3(worldX + CellSize * 0.5f, mountainHeight * 0.5f, worldZ + CellSize * 0.5f);
                    mInst.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
                    _terrainRoot.AddChild(mInst);

                    // 山顶雪
                    if (cell.Elevation == 3)
                    {
                        var snowInst = new MeshInstance3D();
                        var snowMesh = new BoxMesh();
                        snowMesh.Size = new Vector3(CellSize * 0.7f, 0.2f, CellSize * 0.7f);
                        snowInst.Mesh = snowMesh;
                        snowInst.MaterialOverride = _snowMat;
                        snowInst.Position = new Vector3(worldX + CellSize * 0.5f, mountainHeight + 0.1f, worldZ + CellSize * 0.5f);
                        _terrainRoot.AddChild(snowInst);
                    }
                    continue;
                }

                // 悬崖
                if (effType == TerrainType.Cliff)
                {
                    var mInst = new MeshInstance3D();
                    var boxMesh = new BoxMesh();
                    boxMesh.Size = new Vector3(CellSize, 3.0f, CellSize);
                    mInst.Mesh = boxMesh;
                    mInst.MaterialOverride = _cliffMat;
                    mInst.Position = new Vector3(worldX + CellSize * 0.5f, 1.5f, worldZ + CellSize * 0.5f);
                    mInst.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
                    _terrainRoot.AddChild(mInst);
                    continue;
                }

                // 丘陵小高起
                float tileY = elevationY;
                if (cell.Elevation == 2) tileY = 0.5f;

                // 添加颜色微变化
                Color baseColor = mat.AlbedoColor;
                float r = (float)rng.NextDouble() * 0.05f - 0.025f;
                var tileMat = new StandardMaterial3D
                {
                    AlbedoColor = new Color(
                        Math.Clamp(baseColor.R + r, 0, 1),
                        Math.Clamp(baseColor.G + r, 0, 1),
                        Math.Clamp(baseColor.B + r, 0, 1)),
                    Roughness = mat.Roughness,
                    Metallic = mat.Metallic,
                };
                if (mat.Transparency != BaseMaterial3D.TransparencyEnum.Disabled)
                    tileMat.Transparency = mat.Transparency;

                AddQuadToSurface(surfaceTool, worldX, tileY, worldZ, CellSize, CellSize, tileMat, rng);

                // 道路标线
                if (effType == TerrainType.Road && rng.NextDouble() < 0.3)
                {
                    var lineInst = new MeshInstance3D();
                    var lineMesh = new BoxMesh();
                    lineMesh.Size = new Vector3(0.15f, 0.02f, CellSize * 0.5f);
                    lineInst.Mesh = lineMesh;
                    lineInst.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.72f, 0.2f), Roughness = 0.6f };
                    lineInst.Position = new Vector3(worldX + CellSize * 0.5f, tileY + 0.03f, worldZ + CellSize * 0.5f);
                    _terrainRoot.AddChild(lineInst);
                }
            }
        }

        surfaceTool.Index();
        surfaceTool.GenerateNormals();
        var terrainMesh = surfaceTool.Commit();

        var terrainMi = new MeshInstance3D();
        terrainMi.Mesh = terrainMesh;
        terrainMi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        _terrainRoot.AddChild(terrainMi);

        // 生成静态碰撞体
        var colShape = new ConcavePolygonShape3D();
        var faces = terrainMesh.GetFaces();
        colShape.SetFaces(faces);
        var colBody = new StaticBody3D();
        var colOwner = new CollisionShape3D { Shape = colShape };
        colBody.AddChild(colOwner);
        _terrainRoot.AddChild(colBody);

        // 构建导航网格
        BuildNavigationMesh();
    }

    private void AddQuadToSurface(SurfaceTool st, float x, float y, float z, float w, float d,
        StandardMaterial3D mat, Random rng)
    {
        // 4个顶点（逆时针）
        st.SetMaterial(mat);

        // 微高度变化让地面不那么平
        float h1 = y + (float)rng.NextDouble() * 0.05f;
        float h2 = y + (float)rng.NextDouble() * 0.05f;
        float h3 = y + (float)rng.NextDouble() * 0.05f;
        float h4 = y + (float)rng.NextDouble() * 0.05f;

        // 法线朝上
        st.SetNormal(new Vector3(0, 1, 0));

        // 三角形1
        st.AddVertex(new Vector3(x, h1, z));
        st.AddVertex(new Vector3(x + w, h2, z));
        st.AddVertex(new Vector3(x + w, h3, z + d));

        // 三角形2
        st.AddVertex(new Vector3(x, h1, z));
        st.AddVertex(new Vector3(x + w, h3, z + d));
        st.AddVertex(new Vector3(x, h4, z + d));
    }

    // ======== 导航网格构建 ========

    private void BuildNavigationMesh()
    {
        // 创建NavigationMesh
        var navMesh = new NavigationMesh();
        navMesh.CellSize = 0.5f;
        navMesh.CellHeight = 0.2f;
        navMesh.AgentHeight = 1.5f;
        navMesh.AgentRadius = 0.8f;
        navMesh.AgentMaxSlope = 45f;
        navMesh.AgentMaxClimb = 1.0f;
        navMesh.RegionMinSize = 2;

        // 收集可行走区域作为 baking source
        var bakingSource = new Node3D { Name = "NavBakingSource" };

        for (int x = 0; x < GridSize; x++)
        {
            for (int y = 0; y < GridSize; y++)
            {
                var cell = _cells[x, y];
                TerrainType effType = GetEffectiveType(x, y);

                // 不可通行地形不烘焙
                float speed = GetSpeedModifier(TerrainUnitCategory.LightVehicle, effType, cell.Elevation, cell.Elevation);
                if (speed <= 0f) continue;

                // 水面也不烘焙（海军单位单独处理）
                if (cell.Type == TerrainType.DeepWater || cell.Type == TerrainType.ShallowWater)
                {
                    // 桥梁可以通行
                    if (!cell.HasBridge) continue;
                }

                float worldX = x * CellSize;
                float worldZ = y * CellSize;
                float tileY = GetElevationY(cell.Elevation);
                if (cell.Elevation == 2) tileY = 0.5f;

                // 添加一个薄Box作为烘焙源
                var box = new MeshInstance3D();
                var boxMesh = new BoxMesh();
                boxMesh.Size = new Vector3(CellSize, 0.1f, CellSize);
                box.Mesh = boxMesh;
                box.Position = new Vector3(worldX + CellSize * 0.5f, tileY + 0.05f, worldZ + CellSize * 0.5f);
                bakingSource.AddChild(box);
            }
        }

        _navRegion.AddChild(bakingSource);

        // 设置烘焙源并烘焙
        _navRegion.NavigationMesh = navMesh;

        // Godot 4.x: 使用 NavigationServer3D 烘焙
        // 为简单起见，直接在代码中烘焙
        _navRegion.BakeNavigationMesh(false);

        GD.Print("[TerrainGrid3D] Navigation mesh baked");
    }

    // ======== 便利方法 ========

    /// <summary>检查指定格子是否可通行（对地面单位）</summary>
    public bool IsPassable(int gx, int gy, TerrainUnitCategory cat)
    {
        var cell = GetCell(gx, gy);
        TerrainType effType = GetEffectiveType(gx, gy);
        float speed = GetSpeedModifier(cat, effType, cell.Elevation, cell.Elevation);
        return speed > 0f;
    }

    /// <summary>获取格子中心的3D世界坐标（含高度）</summary>
    public Vector3 GetCellCenterWorld(int gx, int gy)
    {
        var cell = GetCell(gx, gy);
        float y = GetElevationY(cell.Elevation);
        if (cell.Elevation == 2) y = 0.5f;
        return new Vector3(
            gx * CellSize + CellSize * 0.5f,
            y,
            gy * CellSize + CellSize * 0.5f);
    }

    /// <summary>获取指定世界坐标处的高度Y</summary>
    public float GetHeightAtWorld(float worldX, float worldZ)
    {
        WorldToGrid(worldX, worldZ, out int gx, out int gy);
        var cell = GetCell(gx, gy);
        float y = GetElevationY(cell.Elevation);
        if (cell.Elevation == 2) y = 0.5f;
        return y;
    }

    /// <summary>获取可放置建筑的平地位置列表</summary>
    public List<Vector3> GetBuildablePositions(int count, int seed)
    {
        var rng = new Random(seed);
        var positions = new List<Vector3>();
        int attempts = 0;
        while (positions.Count < count && attempts < 500)
        {
            attempts++;
            int gx = rng.Next(2, GridSize - 2);
            int gy = rng.Next(2, GridSize - 2);
            var cell = GetCell(gx, gy);
            if (cell.Elevation == 1 && cell.Type != TerrainType.DeepWater && cell.Type != TerrainType.ShallowWater
                && cell.Type != TerrainType.Mountain && cell.Type != TerrainType.Cliff)
            {
                var pos = GridToWorld(gx, gy);
                // 检查与已有位置的距离
                bool tooClose = false;
                foreach (var p in positions)
                {
                    if (Math.Abs(p.X - pos.X) < CellSize * 3 && Math.Abs(p.Z - pos.Z) < CellSize * 3)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (!tooClose)
                    positions.Add(pos);
            }
        }
        return positions;
    }
}
