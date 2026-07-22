using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 3D 2.5D 原型 — 正交相机 + 低多边形3D模型
/// 展示红警2风格的等距视角效果
/// </summary>
public partial class Prototype3D : Node3D
{
    private Camera3D _camera;
    private DirectionalLight3D _sunLight;
    private Node3D _terrainRoot;
    private Node3D _unitsRoot;
    private Node3D _buildingsRoot;

    // 网格参数
    private const int GridW = 32;
    private const int GridH = 32;
    private const float CellSize = 2.0f;

    // 单位
    private List<Unit3D> _units = new();
    private float _time;

    public override void _Ready()
    {
        // 正交相机 — 红警2风格的等距视角
        _camera = new Camera3D();
        _camera.Projection = Camera3D.ProjectionType.Orthogonal;
        _camera.Size = 45f; // 视野范围
        _camera.Position = new Vector3(40, 40, 40);
        // 看向地图中心
        _camera.LookAt(new Vector3(GridW * CellSize * 0.5f, 0, GridH * CellSize * 0.5f));
        _camera.Fov = 50f;
        _camera.Near = 0.1f;
        _camera.Far = 200f;
        AddChild(_camera);
        _camera.MakeCurrent();

        // 环境光
        var env = new Godot.Environment();
        env.BackgroundMode = Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.12f, 0.14f, 0.18f);
        env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
        env.AmbientLightColor = new Color(0.35f, 0.38f, 0.42f);
        env.AmbientLightEnergy = 0.6f;
        env.FogEnabled = true;
        env.FogLightColor = new Color(0.15f, 0.17f, 0.2f);
        env.FogLightEnergy = 0.3f;
        env.FogDensity = 0.008f;
        var worldEnv = new WorldEnvironment { Environment = env };
        AddChild(worldEnv);

        // 太阳光 (方向光) — 给场景提供实时光影
        _sunLight = new DirectionalLight3D();
        _sunLight.RotationDegrees = new Vector3(-55, 30, 0);
        _sunLight.LightEnergy = 1.2f;
        _sunLight.ShadowEnabled = true;
        _sunLight.ShadowOpacity = 0.6f;
        AddChild(_sunLight);

        // 天空补光
        var fillLight = new DirectionalLight3D();
        fillLight.RotationDegrees = new Vector3(-40, 210, 0);
        fillLight.LightEnergy = 0.3f;
        fillLight.ShadowEnabled = false;
        AddChild(fillLight);

        _terrainRoot = new Node3D { Name = "Terrain" };
        AddChild(_terrainRoot);
        _unitsRoot = new Node3D { Name = "Units" };
        AddChild(_unitsRoot);
        _buildingsRoot = new Node3D { Name = "Buildings" };
        AddChild(_buildingsRoot);

        BuildTerrain();
        SpawnTestUnits();
        SpawnTestBuildings();

        GD.Print("Prototype3D ready — 2.5D isometric view");
    }

    /// <summary>
    /// 构建3D地形网格
    /// </summary>
    private void BuildTerrain()
    {
        var rng = new Random(42);

        for (int x = 0; x < GridW; x++)
        {
            for (int z = 0; z < GridH; z++)
            {
                float worldX = x * CellSize;
                float worldZ = z * CellSize;

                // 确定地形类型
                float distFromCenter = Mathf.Sqrt(
                    Mathf.Pow(x - GridW * 0.5f, 2) + Mathf.Pow(z - GridH * 0.5f, 2));

                TerrainType type;
                float height = 0f;

                if (distFromCenter < 4f)
                {
                    type = TerrainType.City;
                    height = 0f;
                }
                else if (x < 4 && z < 4)
                {
                    type = TerrainType.Water;
                    height = -0.3f;
                }
                else if (x > GridW - 5 && z > GridH - 5)
                {
                    type = TerrainType.Mountain;
                    height = 1.5f + (float)rng.NextDouble() * 2f;
                }
                else if (x > GridW - 6 && z < 4)
                {
                    type = TerrainType.Water;
                    height = -0.3f;
                }
                else if (distFromCenter > 12f && (float)rng.NextDouble() < 0.15f)
                {
                    type = TerrainType.Mountain;
                    height = 1f + (float)rng.NextDouble() * 1.5f;
                }
                else if (distFromCenter > 10f && (float)rng.NextDouble() < 0.1f)
                {
                    type = TerrainType.Sand;
                    height = 0.05f * (float)rng.NextDouble();
                }
                {
                    type = TerrainType.Grass;
                    height = (float)rng.NextDouble() * 0.15f;
                }

                CreateTerrainTile(worldX, worldZ, type, height, rng);
            }
        }

        // 添加道路
        for (int x = 4; x < GridW - 4; x++)
        {
            ReplaceTerrainTile(x, 16, TerrainType.Road, 0f);
        }
        for (int z = 4; z < GridH - 4; z++)
        {
            ReplaceTerrainTile(16, z, TerrainType.Road, 0f);
        }
    }

    private enum TerrainType { Grass, Sand, Water, Mountain, City, Road }

    private static readonly Color GrassColor1 = new(0.18f, 0.38f, 0.14f);
    private static readonly Color GrassColor2 = new(0.22f, 0.44f, 0.16f);
    private static readonly Color SandColor = new(0.72f, 0.62f, 0.38f);
    private static readonly Color WaterColor = new(0.12f, 0.28f, 0.55f);
    private static readonly Color DeepWaterColor = new(0.06f, 0.16f, 0.38f);
    private static readonly Color MountainColor = new(0.45f, 0.38f, 0.30f);
    private static readonly Color CityColor = new(0.42f, 0.40f, 0.38f);
    private static readonly Color RoadColor = new(0.25f, 0.24f, 0.22f);

    private void CreateTerrainTile(float x, float z, TerrainType type, float height, Random rng)
    {
        var mesh = new BoxMesh();
        mesh.Size = new Vector3(CellSize, Mathf.Max(height, 0.1f), CellSize);

        var mat = new StandardMaterial3D();

        Color baseColor;
        switch (type)
        {
            case TerrainType.Grass:
                baseColor = (float)rng.NextDouble() < 0.5f ? GrassColor1 : GrassColor2;
                baseColor = new Color(
                    baseColor.R + (float)rng.NextDouble() * 0.04f,
                    baseColor.G + (float)rng.NextDouble() * 0.04f,
                    baseColor.B + (float)rng.NextDouble() * 0.03f);
                break;
            case TerrainType.Sand:
                baseColor = new Color(
                    SandColor.R + (float)rng.NextDouble() * 0.05f,
                    SandColor.G + (float)rng.NextDouble() * 0.04f,
                    SandColor.B + (float)rng.NextDouble() * 0.03f);
                break;
            case TerrainType.Water:
                baseColor = height < -0.2f ? DeepWaterColor : WaterColor;
                mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha; // 半透明
                break;
            case TerrainType.Mountain:
                float h = height / 3f;
                baseColor = new Color(
                    MountainColor.R * (1 - h * 0.3f),
                    MountainColor.G * (1 - h * 0.3f),
                    MountainColor.B * (1 - h * 0.3f));
                break;
            case TerrainType.City:
                baseColor = CityColor;
                break;
            case TerrainType.Road:
                baseColor = RoadColor;
                break;
            default:
                baseColor = GrassColor1;
                break;
        }

        mat.AlbedoColor = baseColor;
        mat.Roughness = 0.9f;
        mat.Metallic = 0f;
        if (type == TerrainType.Water)
        {
            mat.Roughness = 0.1f;
            mat.Metallic = 0.3f;
        }
        mesh.Material = mat;

        var mi = new MeshInstance3D();
        mi.Mesh = mesh;

        // 平放
        float yOffset = height > 0 ? height * 0.5f : height * 0.5f;
        mi.Position = new Vector3(x + CellSize * 0.5f, yOffset, z + CellSize * 0.5f);
        mi.CastShadow = height > 0.5f ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
        _terrainRoot.AddChild(mi);

        // 山顶加雪
        if (type == TerrainType.Mountain && height > 2.5f)
        {
            var snowMesh = new BoxMesh();
            snowMesh.Size = new Vector3(CellSize * 0.9f, 0.1f, CellSize * 0.9f);
            var snowMat = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.92f, 0.95f), Roughness = 0.8f };
            snowMesh.Material = snowMat;
            var snowMi = new MeshInstance3D { Mesh = snowMesh };
            snowMi.Position = new Vector3(x + CellSize * 0.5f, height + 0.05f, z + CellSize * 0.5f);
            _terrainRoot.AddChild(snowMi);
        }
    }

    private void ReplaceTerrainTile(int gx, int gz, TerrainType type, float height)
    {
        // 找到对应位置的tile并替换
        float worldX = gx * CellSize;
        float worldZ = gz * CellSize;
        // 简单实现：直接在上面叠一个路面mesh
        var mesh = new BoxMesh();
        mesh.Size = new Vector3(CellSize, 0.08f, CellSize);
        var mat = new StandardMaterial3D { AlbedoColor = RoadColor, Roughness = 0.8f };
        mesh.Material = mat;
        var mi = new MeshInstance3D { Mesh = mesh };
        mi.Position = new Vector3(worldX + CellSize * 0.5f, 0.04f, worldZ + CellSize * 0.5f);
        _terrainRoot.AddChild(mi);

        // 路面中心黄线
        var lineMesh = new BoxMesh();
        lineMesh.Size = new Vector3(CellSize * 0.04f, 0.02f, CellSize * 0.6f);
        var lineMat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.72f, 0.2f), Roughness = 0.6f };
        lineMesh.Material = lineMat;
        var lineMi = new MeshInstance3D { Mesh = lineMesh };
        lineMi.Position = new Vector3(worldX + CellSize * 0.5f, 0.09f, worldZ + CellSize * 0.5f);
        _terrainRoot.AddChild(lineMi);
    }

    /// <summary>
    /// 生成测试单位（3D低多边形坦克）
    /// </summary>
    private void SpawnTestUnits()
    {
        var rng = new Random(123);

        // 玩家方坦克（红色阵营）
        for (int i = 0; i < 5; i++)
        {
            var tank = CreateTank(new Color(0.7f, 0.12f, 0.1f), new Color(0.5f, 0.08f, 0.06f));
            tank.Position = new Vector3(
                8 + i * 3f,
                0,
                14 + (float)rng.NextDouble() * 2f);
            tank.RotationDegrees = new Vector3(0, 45 + i * 10, 0);
            tank.Name = $"PlayerTank_{i}";
            _unitsRoot.AddChild(tank);

            var u = new Unit3D { Body = tank, Speed = 1.5f + (float)rng.NextDouble() * 1f };
            u.TargetPos = tank.Position;
            _units.Add(u);
        }

        // 敌方坦克（蓝色阵营）
        for (int i = 0; i < 3; i++)
        {
            var tank = CreateTank(new Color(0.12f, 0.22f, 0.65f), new Color(0.08f, 0.15f, 0.45f));
            tank.Position = new Vector3(
                24 + i * 3f,
                0,
                8 + (float)rng.NextDouble() * 2f);
            tank.RotationDegrees = new Vector3(0, 200 + i * 15, 0);
            tank.Name = $"EnemyTank_{i}";
            _unitsRoot.AddChild(tank);

            var u = new Unit3D { Body = tank, Speed = 1.2f + (float)rng.NextDouble() * 0.8f };
            u.TargetPos = tank.Position;
            _units.Add(u);
        }

        // 一辆矿车
        var harvester = CreateHarvester(new Color(0.7f, 0.12f, 0.1f));
        harvester.Position = new Vector3(14, 0, 20);
        harvester.RotationDegrees = new Vector3(0, 90, 0);
        harvester.Name = "Harvester";
        _unitsRoot.AddChild(harvester);
    }

    /// <summary>
    /// 创建低多边形坦克：底盘+履带+炮塔+炮管
    /// </summary>
    private Node3D CreateTank(Color bodyColor, Color darkColor)
    {
        var tank = new Node3D();

        var bodyMat = new StandardMaterial3D();
        bodyMat.AlbedoColor = bodyColor;
        bodyMat.Roughness = 0.6f;
        bodyMat.Metallic = 0.3f;

        var darkMat = new StandardMaterial3D();
        darkMat.AlbedoColor = darkColor;
        darkMat.Roughness = 0.7f;
        darkMat.Metallic = 0.2f;

        var trackMat = new StandardMaterial3D();
        trackMat.AlbedoColor = new Color(0.12f, 0.12f, 0.12f);
        trackMat.Roughness = 0.9f;

        var metalMat = new StandardMaterial3D();
        metalMat.AlbedoColor = new Color(0.35f, 0.35f, 0.38f);
        metalMat.Roughness = 0.3f;
        metalMat.Metallic = 0.8f;

        // 底盘（梯形效果用扁方块）
        var hull = new MeshInstance3D();
        var hullMesh = new BoxMesh();
        hullMesh.Size = new Vector3(1.6f, 0.4f, 2.2f);
        hull.Mesh = hullMesh;
        hull.MaterialOverride = bodyMat;
        hull.Position = new Vector3(0, 0.35f, 0);
        hull.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        tank.AddChild(hull);

        // 底盘斜面前装甲
        var frontArmor = new MeshInstance3D();
        var frontMesh = new BoxMesh();
        frontMesh.Size = new Vector3(1.4f, 0.3f, 0.4f);
        frontArmor.Mesh = frontMesh;
        frontArmor.MaterialOverride = bodyMat;
        frontArmor.Position = new Vector3(0, 0.3f, 1.15f);
        frontArmor.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        tank.AddChild(frontArmor);

        // 左履带
        var leftTrack = new MeshInstance3D();
        var trackMesh = new BoxMesh();
        trackMesh.Size = new Vector3(0.4f, 0.5f, 2.4f);
        leftTrack.Mesh = trackMesh;
        leftTrack.MaterialOverride = trackMat;
        leftTrack.Position = new Vector3(-0.75f, 0.25f, 0);
        leftTrack.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        tank.AddChild(leftTrack);

        // 右履带
        var rightTrack = new MeshInstance3D();
        rightTrack.Mesh = trackMesh;
        rightTrack.MaterialOverride = trackMat;
        rightTrack.Position = new Vector3(0.75f, 0.25f, 0);
        rightTrack.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        tank.AddChild(rightTrack);

        // 履带轮子细节
        for (int i = 0; i < 4; i++)
        {
            var wheelL = new MeshInstance3D();
            var wheelMesh = new CylinderMesh();
            wheelMesh.TopRadius = 0.22f;
            wheelMesh.BottomRadius = 0.22f;
            wheelMesh.Height = 0.3f;
            wheelL.Mesh = wheelMesh;
            wheelL.MaterialOverride = metalMat;
            wheelL.Position = new Vector3(-0.75f, 0.2f, -0.9f + i * 0.6f);
            wheelL.RotationDegrees = new Vector3(0, 0, 90);
            wheelL.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            tank.AddChild(wheelL);

            var wheelR = new MeshInstance3D();
            wheelR.Mesh = wheelMesh;
            wheelR.MaterialOverride = metalMat;
            wheelR.Position = new Vector3(0.75f, 0.2f, -0.9f + i * 0.6f);
            wheelR.RotationDegrees = new Vector3(0, 0, 90);
            wheelR.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            tank.AddChild(wheelR);
        }

        // 炮塔
        var turret = new Node3D();
        turret.Position = new Vector3(0, 0.6f, -0.1f);
        tank.AddChild(turret);

        var turretBase = new MeshInstance3D();
        var turretMesh = new BoxMesh();
        turretMesh.Size = new Vector3(1.0f, 0.5f, 1.0f);
        turretBase.Mesh = turretMesh;
        turretBase.MaterialOverride = bodyMat;
        turretBase.Position = new Vector3(0, 0.15f, 0);
        turretBase.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        turret.AddChild(turretBase);

        // 炮塔顶部（圆形）
        var turretTop = new MeshInstance3D();
        var topMesh = new CylinderMesh();
        topMesh.TopRadius = 0.4f;
        topMesh.BottomRadius = 0.5f;
        topMesh.Height = 0.3f;
        turretTop.Mesh = topMesh;
        turretTop.MaterialOverride = bodyMat;
        turretTop.Position = new Vector3(0, 0.5f, 0);
        turretTop.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        turret.AddChild(turretTop);

        // 炮管
        var barrel = new MeshInstance3D();
        var barrelMesh = new CylinderMesh();
        barrelMesh.TopRadius = 0.08f;
        barrelMesh.BottomRadius = 0.1f;
        barrelMesh.Height = 1.8f;
        barrel.Mesh = barrelMesh;
        barrel.MaterialOverride = metalMat;
        barrel.Position = new Vector3(0, 0.2f, 1.0f);
        barrel.RotationDegrees = new Vector3(90, 0, 0);
        barrel.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        turret.AddChild(barrel);

        // 炮口制动器
        var muzzle = new MeshInstance3D();
        var muzzleMesh = new CylinderMesh();
        muzzleMesh.TopRadius = 0.13f;
        muzzleMesh.BottomRadius = 0.14f;
        muzzleMesh.Height = 0.3f;
        muzzle.Mesh = muzzleMesh;
        muzzle.MaterialOverride = darkMat;
        muzzle.Position = new Vector3(0, 0.2f, 1.9f);
        muzzle.RotationDegrees = new Vector3(90, 0, 0);
        muzzle.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        turret.AddChild(muzzle);

        // 顶部舱门
        var hatch = new MeshInstance3D();
        var hatchMesh = new CylinderMesh();
        hatchMesh.TopRadius = 0.2f;
        hatchMesh.BottomRadius = 0.22f;
        hatchMesh.Height = 0.1f;
        hatch.Mesh = hatchMesh;
        hatch.MaterialOverride = darkMat;
        hatch.Position = new Vector3(0, 0.7f, -0.2f);
        hatch.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        turret.AddChild(hatch);

        return tank;
    }

    /// <summary>
    /// 创建矿车
    /// </summary>
    private Node3D CreateHarvester(Color bodyColor)
    {
        var harvester = new Node3D();
        var bodyMat = new StandardMaterial3D { AlbedoColor = bodyColor, Roughness = 0.6f, Metallic = 0.3f };
        var darkMat = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.2f, 0.15f), Roughness = 0.8f };
        var trackMat = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.12f), Roughness = 0.9f };

        // 车体（更大更扁）
        var hull = new MeshInstance3D();
        var hullMesh = new BoxMesh();
        hullMesh.Size = new Vector3(2.0f, 0.5f, 2.8f);
        hull.Mesh = hullMesh;
        hull.MaterialOverride = bodyMat;
        hull.Position = new Vector3(0, 0.4f, 0);
        hull.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        harvester.AddChild(hull);

        // 矿仓
        var bin = new MeshInstance3D();
        var binMesh = new BoxMesh();
        binMesh.Size = new Vector3(1.6f, 0.6f, 1.4f);
        bin.Mesh = binMesh;
        bin.MaterialOverride = darkMat;
        bin.Position = new Vector3(0, 0.8f, -0.5f);
        bin.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        harvester.AddChild(bin);

        // 采集臂
        var arm = new MeshInstance3D();
        var armMesh = new BoxMesh();
        armMesh.Size = new Vector3(0.2f, 0.2f, 1.5f);
        arm.Mesh = armMesh;
        arm.MaterialOverride = bodyMat;
        arm.Position = new Vector3(0, 0.5f, 1.5f);
        arm.RotationDegrees = new Vector3(-20, 0, 0);
        arm.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        harvester.AddChild(arm);

        // 履带
        for (int side = 0; side < 2; side++)
        {
            float xPos = side == 0 ? -0.9f : 0.9f;
            var track = new MeshInstance3D();
            var trackMesh = new BoxMesh();
            trackMesh.Size = new Vector3(0.4f, 0.6f, 3.0f);
            track.Mesh = trackMesh;
            track.MaterialOverride = trackMat;
            track.Position = new Vector3(xPos, 0.3f, 0);
            track.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            harvester.AddChild(track);
        }

        return harvester;
    }

    /// <summary>
    /// 生成测试建筑
    /// </summary>
    private void SpawnTestBuildings()
    {
        // 基地（大型建筑）
        var base1 = CreateBuilding(
            new Color(0.65f, 0.1f, 0.08f),
            new Color(0.4f, 0.06f, 0.04f),
            4f, 2.5f, 3f);
        base1.Position = new Vector3(16, 0, 16);
        base1.Name = "PlayerBase";
        _buildingsRoot.AddChild(base1);

        // 电站
        var power = CreateBuilding(
            new Color(0.6f, 0.55f, 0.5f),
            new Color(0.35f, 0.32f, 0.3f),
            2.5f, 1.8f, 2f);
        power.Position = new Vector3(11, 0, 16);
        power.Name = "PowerPlant";
        _buildingsRoot.AddChild(power);

        // 兵营
        var barracks = CreateBuilding(
            new Color(0.55f, 0.42f, 0.28f),
            new Color(0.35f, 0.28f, 0.18f),
            2.5f, 1.5f, 2.5f);
        barracks.Position = new Vector3(21, 0, 16);
        barracks.Name = "Barracks";
        _buildingsRoot.AddChild(barracks);

        // 车厂
        var warfactory = CreateBuilding(
            new Color(0.5f, 0.48f, 0.45f),
            new Color(0.3f, 0.28f, 0.26f),
            3.5f, 1.5f, 3f);
        warfactory.Position = new Vector3(16, 0, 11);
        warfactory.Name = "WarFactory";
        _buildingsRoot.AddChild(warfactory);

        // 敌方基基地
        var enemyBase = CreateBuilding(
            new Color(0.1f, 0.2f, 0.6f),
            new Color(0.06f, 0.12f, 0.4f),
            4f, 2.5f, 3f);
        enemyBase.Position = new Vector3(48, 0, 48);
        enemyBase.Name = "EnemyBase";
        _buildingsRoot.AddChild(enemyBase);

        // 防御塔
        var turret = CreateDefenseTurret(new Color(0.6f, 0.55f, 0.5f));
        turret.Position = new Vector3(20, 0, 20);
        turret.Name = "Pillbox";
        _buildingsRoot.AddChild(turret);
    }

    /// <summary>
    /// 创建建筑：主体+屋顶+细节
    /// </summary>
    private Node3D CreateBuilding(Color wallColor, Color roofColor, float w, float h, float d)
    {
        var building = new Node3D();

        var wallMat = new StandardMaterial3D { AlbedoColor = wallColor, Roughness = 0.7f, Metallic = 0.1f };
        var roofMat = new StandardMaterial3D { AlbedoColor = roofColor, Roughness = 0.8f };
        var darkMat = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.14f, 0.13f), Roughness = 0.9f };
        var windowMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.6f, 0.7f, 0.8f),
            Roughness = 0.2f,
            Metallic = 0.5f,
            Emission = new Color(0.3f, 0.4f, 0.5f),
            EmissionEnergyMultiplier = 0.3f
        };

        // 地基
        var foundation = new MeshInstance3D();
        var fMesh = new BoxMesh();
        fMesh.Size = new Vector3(w + 0.3f, 0.15f, d + 0.3f);
        foundation.Mesh = fMesh;
        foundation.MaterialOverride = darkMat;
        foundation.Position = new Vector3(0, 0.075f, 0);
        foundation.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        building.AddChild(foundation);

        // 墙体
        var walls = new MeshInstance3D();
        var wMesh = new BoxMesh();
        wMesh.Size = new Vector3(w, h, d);
        walls.Mesh = wMesh;
        walls.MaterialOverride = wallMat;
        walls.Position = new Vector3(0, h * 0.5f + 0.15f, 0);
        walls.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        building.AddChild(walls);

        // 屋顶
        var roof = new MeshInstance3D();
        var rMesh = new BoxMesh();
        rMesh.Size = new Vector3(w + 0.2f, 0.2f, d + 0.2f);
        roof.Mesh = rMesh;
        roof.MaterialOverride = roofMat;
        roof.Position = new Vector3(0, h + 0.25f, 0);
        roof.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        building.AddChild(roof);

        // 屋顶细节 — 烟囱/通风口
        if (w > 3f)
        {
            var chimney = new MeshInstance3D();
            var cMesh = new BoxMesh();
            cMesh.Size = new Vector3(0.4f, 0.6f, 0.4f);
            chimney.Mesh = cMesh;
            chimney.MaterialOverride = darkMat;
            chimney.Position = new Vector3(w * 0.25f, h + 0.6f, d * 0.25f);
            chimney.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            building.AddChild(chimney);
        }

        // 窗户 — 四面各一个
        float winY = h * 0.5f + 0.15f;
        var winMesh = new BoxMesh();
        winMesh.Size = new Vector3(0.4f, 0.5f, 0.05f);
        var winMesh2 = new BoxMesh();
        winMesh2.Size = new Vector3(0.05f, 0.5f, 0.4f);

        // 前后窗
        foreach (float zPos in new float[] { d * 0.5f + 0.01f, -d * 0.5f - 0.01f })
        {
            var win = new MeshInstance3D();
            win.Mesh = winMesh;
            win.MaterialOverride = windowMat;
            win.Position = new Vector3(0, winY, zPos);
            building.AddChild(win);
        }
        // 左右窗
        foreach (float xPos in new float[] { w * 0.5f + 0.01f, -w * 0.5f - 0.01f })
        {
            var win = new MeshInstance3D();
            win.Mesh = winMesh2;
            win.MaterialOverride = windowMat;
            win.Position = new Vector3(xPos, winY, 0);
            building.AddChild(win);
        }

        // 门
        var door = new MeshInstance3D();
        var dMesh = new BoxMesh();
        dMesh.Size = new Vector3(0.6f, 0.9f, 0.05f);
        door.Mesh = dMesh;
        door.MaterialOverride = darkMat;
        door.Position = new Vector3(0, 0.6f, d * 0.5f + 0.02f);
        building.AddChild(door);

        return building;
    }

    /// <summary>
    /// 防御塔
    /// </summary>
    private Node3D CreateDefenseTurret(Color bodyColor)
    {
        var turret = new Node3D();
        var bodyMat = new StandardMaterial3D { AlbedoColor = bodyColor, Roughness = 0.7f };
        var sandbagMat = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.48f, 0.32f), Roughness = 0.9f };
        var metalMat = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.3f, 0.32f), Roughness = 0.3f, Metallic = 0.8f };

        // 沙袋围墙
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.Pi / 4f;
            var sandbag = new MeshInstance3D();
            var sbMesh = new SphereMesh();
            sbMesh.Radius = 0.3f;
            sbMesh.Height = 0.5f;
            sandbag.Mesh = sbMesh;
            sandbag.MaterialOverride = sandbagMat;
            sandbag.Position = new Vector3(Mathf.Cos(angle) * 1.2f, 0.25f, Mathf.Sin(angle) * 1.2f);
            sandbag.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            turret.AddChild(sandbag);
        }

        // 混凝土基座
        var baseMesh = new MeshInstance3D();
        var bMesh = new CylinderMesh();
        bMesh.TopRadius = 0.7f;
        bMesh.BottomRadius = 0.9f;
        bMesh.Height = 0.5f;
        baseMesh.Mesh = bMesh;
        baseMesh.MaterialOverride = bodyMat;
        baseMesh.Position = new Vector3(0, 0.25f, 0);
        baseMesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        turret.AddChild(baseMesh);

        // 机枪塔
        var gunMount = new MeshInstance3D();
        var gmMesh = new BoxMesh();
        gmMesh.Size = new Vector3(0.5f, 0.3f, 0.5f);
        gunMount.Mesh = gmMesh;
        gunMount.MaterialOverride = metalMat;
        gunMount.Position = new Vector3(0, 0.65f, 0);
        gunMount.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        turret.AddChild(gunMount);

        // 双管机枪
        for (int i = 0; i < 2; i++)
        {
            var gun = new MeshInstance3D();
            var gMesh = new CylinderMesh();
            gMesh.TopRadius = 0.05f;
            gMesh.BottomRadius = 0.06f;
            gMesh.Height = 1.2f;
            gun.Mesh = gMesh;
            gun.MaterialOverride = metalMat;
            gun.Position = new Vector3(i == 0 ? -0.12f : 0.12f, 0.65f, 0.6f);
            gun.RotationDegrees = new Vector3(90, 0, 0);
            gun.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            turret.AddChild(gun);
        }

        return turret;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;

        // 单位巡逻移动
        foreach (var u in _units)
        {
            if (u.Body == null) continue;

            float dist = u.Body.Position.DistanceTo(u.TargetPos);
            if (dist < 0.5f)
            {
                // 到达目标，设新目标
                var rng = new Random();
                u.TargetPos = new Vector3(
                    u.Body.Position.X + ((float)rng.NextDouble() - 0.5f) * 10f,
                    0,
                    u.Body.Position.Z + ((float)rng.NextDouble() - 0.5f) * 10f);
                u.TargetPos.X = Mathf.Clamp(u.TargetPos.X, 2, GridW * CellSize - 2);
                u.TargetPos.Z = Mathf.Clamp(u.TargetPos.Z, 2, GridH * CellSize - 2);
            }
            {
                // 移动
                var dir = (u.TargetPos - u.Body.Position).Normalized();
                u.Body.Position += dir * u.Speed * (float)delta;
                u.Body.Position = new Vector3(u.Body.Position.X, 0, u.Body.Position.Z);

                // 转向
                float targetAngle = Mathf.Atan2(dir.X, dir.Z) * 180f / Mathf.Pi;
                float currentAngle = u.Body.RotationDegrees.Y;
                float diff = Mathf.AngleDifference(Mathf.DegToRad(currentAngle), Mathf.DegToRad(targetAngle));
                u.Body.RotationDegrees = new Vector3(0, currentAngle + Mathf.RadToDeg(diff) * 3f * (float)delta, 0);
            }
        }

        // 相机缓慢旋转（展示3D效果）
        float camAngle = _time * 0.05f;
        float camRadius = 55f;
        _camera.Position = new Vector3(
            GridW * CellSize * 0.5f + Mathf.Cos(camAngle) * camRadius,
            45f,
            GridH * CellSize * 0.5f + Mathf.Sin(camAngle) * camRadius);
        _camera.LookAt(new Vector3(GridW * CellSize * 0.5f, 0, GridH * CellSize * 0.5f));
    }

    private class Unit3D
    {
        public Node3D Body;
        public Vector3 TargetPos;
        public float Speed;
    }
}
