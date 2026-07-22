using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 3D 建筑：Area3D + 低多边形3D模型。
/// 移植自2D Building.cs，保留全部游戏逻辑（生产队列/防御射击/维修/占领/电力），重写渲染层。
/// </summary>
public partial class Building3D : Area3D
{
    public enum BuildingType
    {
        Base, PowerPlant, Barracks, WarFactory, TechCenter,
        Turret, AntiAirTurret, RepairPad, Airfield, Shipyard,
        NukeSilo, LightningTower, MissileSilo,
    }

    public enum ProductionType
    {
        None,
        // 步兵
        Infantry, Engineer, Sapper, ChiefEngineer, Grenadier, Sniper, FlameInfantry,
        // 载具
        LightTank, HeavyTank, Artillery, RocketLauncher, MissileTank, AntiAir, Harvester, Transport,
        // 特殊
        Hero, Spy, Thief,
        // 空军
        Fighter, Helicopter, RocketInfantry, Bomber, Scout, TransportHeli,
        // 海军
        Destroyer, Submarine, AircraftCarrier, LandingCraft,
    }

    public BuildingType Type { get; set; } = BuildingType.Base;
    public int TeamId { get; set; } = 0;
    public float MaxHealth { get; private set; } = 1000f;
    public float Health { get; private set; }
    public bool IsSelected { get; private set; }
    public bool _isDead;

    // 电力
    public int PowerProvided { get; private set; }
    public int PowerConsumed { get; private set; }

    // 防御建筑
    public bool IsDefensive { get; private set; }
    public float AttackDamage { get; private set; }
    public float AttackRange { get; private set; }
    public float AttackCooldown { get; private set; }
    private float _attackTimer;

    // 维修厂
    public bool IsRepairStation { get; private set; }
    public float RepairRadius { get; private set; } = 10f;

    // 生产队列
    private readonly Queue<ProductionType> _productionQueue = new();
    private ProductionType? _currentProduction;
    private float _productionProgress;
    private float _productionTotalTime;
    public const int MaxQueueSize = 5;

    // 集结点
    public Vector3? RallyPoint;

    // 占领
    public float CaptureProgress { get; private set; }
    private int _capturingTeamId = -1;

    // 3D 节点
    private Node3D _modelRoot;
    private MeshInstance3D _selectionRing;
    private Label3D _healthLabel;
    private Node3D _turretNode;
    private float _turretAngle;
    private float _hitFlashTimer;

    private Main3D _game;
    private TerrainGrid3D _terrain;

    // 生产完成回调
    public event Action<ProductionType, Building3D>? UnitProduced;

    public void Initialize(TerrainGrid3D terrain, Main3D game)
    {
        _terrain = terrain;
        _game = game;
    }

    public override void _Ready()
    {
        Health = MaxHealth;
        InitAsType(Type);

        // 模型根节点
        _modelRoot = new Node3D { Name = "Model" };
        AddChild(_modelRoot);
        _turretNode = new Node3D { Name = "Turret" };
        _modelRoot.AddChild(_turretNode);

        CreateSelectionRing();

        _healthLabel = new Label3D
        {
            Text = "",
            FontSize = 24,
            OutlineSize = 4,
            OutlineModulate = Colors.Black,
            PixelSize = 0.01f,
            Position = new Vector3(0, 5f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            Visible = false,
        };
        AddChild(_healthLabel);

        BuildModel();
    }

    private void CreateSelectionRing()
    {
        _selectionRing = new MeshInstance3D();
        var ringMesh = new CylinderMesh();
        ringMesh.TopRadius = 3f;
        ringMesh.BottomRadius = 3f;
        ringMesh.Height = 0.05f;
        _selectionRing.Mesh = ringMesh;
        _selectionRing.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 1.0f, 0.2f, 0.4f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = true,
        };
        _selectionRing.Position = new Vector3(0, 0.03f, 0);
        _selectionRing.Visible = false;
        AddChild(_selectionRing);
    }

    // ======== InitAsType ========

    public void InitAsType(BuildingType type)
    {
        Type = type;
        switch (type)
        {
            case BuildingType.Base:
                MaxHealth = 2000; PowerProvided = 0; PowerConsumed = 0; break;
            case BuildingType.PowerPlant:
                MaxHealth = 600; PowerProvided = 150; PowerConsumed = 0; break;
            case BuildingType.Barracks:
                MaxHealth = 700; PowerProvided = 0; PowerConsumed = 20; break;
            case BuildingType.WarFactory:
                MaxHealth = 900; PowerProvided = 0; PowerConsumed = 50; break;
            case BuildingType.TechCenter:
                MaxHealth = 800; PowerProvided = 0; PowerConsumed = 100; break;
            case BuildingType.Turret:
                MaxHealth = 400; PowerConsumed = 20;
                IsDefensive = true; AttackDamage = 15; AttackRange = 12; AttackCooldown = 0.3f; break;
            case BuildingType.AntiAirTurret:
                MaxHealth = 400; PowerConsumed = 30;
                IsDefensive = true; AttackDamage = 12; AttackRange = 18; AttackCooldown = 0.2f; break;
            case BuildingType.RepairPad:
                MaxHealth = 600; PowerConsumed = 30;
                IsRepairStation = true; RepairRadius = 10f; break;
            case BuildingType.Airfield:
                MaxHealth = 700; PowerConsumed = 40; break;
            case BuildingType.Shipyard:
                MaxHealth = 800; PowerConsumed = 40; break;
            case BuildingType.NukeSilo:
                MaxHealth = 1000; PowerConsumed = 100; break;
            case BuildingType.LightningTower:
                MaxHealth = 800; PowerConsumed = 80; break;
            case BuildingType.MissileSilo:
                MaxHealth = 900; PowerConsumed = 60; break;
        }
        Health = Math.Min(Health, MaxHealth);
    }

    // ======== 3D 模型构建 ========

    private void BuildModel()
    {
        Color teamColor = Unit3D.GetTeamColor(TeamId);
        Color darkColor = new(teamColor.R * 0.6f, teamColor.G * 0.6f, teamColor.B * 0.6f);

        switch (Type)
        {
            case BuildingType.Base: BuildBaseModel(teamColor, darkColor); break;
            case BuildingType.PowerPlant: BuildPowerPlantModel(teamColor, darkColor); break;
            case BuildingType.Barracks: BuildBarracksModel(teamColor, darkColor); break;
            case BuildingType.WarFactory: BuildWarFactoryModel(teamColor, darkColor); break;
            case BuildingType.TechCenter: BuildTechCenterModel(teamColor, darkColor); break;
            case BuildingType.Turret: BuildTurretBuildingModel(teamColor, darkColor); break;
            case BuildingType.AntiAirTurret: BuildAntiAirTurretModel(teamColor, darkColor); break;
            case BuildingType.RepairPad: BuildRepairPadModel(teamColor, darkColor); break;
            case BuildingType.Airfield: BuildAirfieldModel(teamColor, darkColor); break;
            case BuildingType.Shipyard: BuildShipyardModel(teamColor, darkColor); break;
            case BuildingType.NukeSilo: BuildNukeSiloModel(teamColor, darkColor); break;
            case BuildingType.LightningTower: BuildLightningTowerModel(teamColor, darkColor); break;
            case BuildingType.MissileSilo: BuildMissileSiloModel(teamColor, darkColor); break;
        }
    }

    protected StandardMaterial3D MakeMat(Color color, float roughness = 0.7f, float metallic = 0.1f)
    {
        return new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic };
    }

    /// <summary>带纹理的材质</summary>
    protected StandardMaterial3D MakeTexturedMat(string texPath, Color tintColor, float roughness = 0.7f, float metallic = 0.1f)
    {
        var mat = new StandardMaterial3D { AlbedoColor = new Color(1f, 1f, 1f), Roughness = roughness, Metallic = metallic };
        var tex = GD.Load<Texture2D>(texPath);
        if (tex != null)
        {
            mat.AlbedoTexture = tex;
            mat.Uv1Scale = new Vector3(1, 1, 1);
        }
        else
        {
            mat.AlbedoColor = tintColor;
        }
        return mat;
    }

    /// <summary>混凝土材质（用于地基）</summary>
    protected StandardMaterial3D MakeConcreteMat(Color tintColor)
    {
        return MakeTexturedMat("res://textures/concrete.png", tintColor, 0.9f, 0f);
    }

    /// <summary>金属面板材质（用于墙体）</summary>
    protected StandardMaterial3D MakeMetalPanelMat(Color tintColor)
    {
        return MakeTexturedMat("res://textures/metal_panel.png", tintColor, 0.5f, 0.3f);
    }

    /// <summary>砖墙材质（用于兵营）</summary>
    protected StandardMaterial3D MakeBrickMat(Color tintColor)
    {
        return MakeTexturedMat("res://textures/bricks.png", tintColor, 0.8f, 0f);
    }

    /// <summary>屋顶材质</summary>
    protected StandardMaterial3D MakeRoofMat(Color tintColor)
    {
        return MakeTexturedMat("res://textures/roof.png", tintColor, 0.8f, 0f);
    }

    protected MeshInstance3D AddBox(Node3D parent, Vector3 size, Vector3 pos, StandardMaterial3D mat, bool shadow = true)
    {
        var mi = new MeshInstance3D();
        mi.Mesh = new BoxMesh { Size = size };
        mi.MaterialOverride = mat;
        mi.Position = pos;
        if (shadow) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        parent.AddChild(mi);
        return mi;
    }

    protected MeshInstance3D AddCylinder(Node3D parent, float topR, float bottomR, float h, Vector3 pos, Vector3 rotDeg, StandardMaterial3D mat, bool shadow = true)
    {
        var mi = new MeshInstance3D();
        mi.Mesh = new CylinderMesh { TopRadius = topR, BottomRadius = bottomR, Height = h };
        mi.MaterialOverride = mat;
        mi.Position = pos;
        mi.RotationDegrees = rotDeg;
        if (shadow) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        parent.AddChild(mi);
        return mi;
    }

    protected MeshInstance3D AddSphere(Node3D parent, float radius, Vector3 pos, StandardMaterial3D mat, bool shadow = true)
    {
        var mi = new MeshInstance3D();
        mi.Mesh = new SphereMesh { Radius = radius, Height = radius * 2f };
        mi.MaterialOverride = mat;
        mi.Position = pos;
        if (shadow) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        parent.AddChild(mi);
        return mi;
    }

    private void BuildBaseModel(Color teamColor, Color darkColor)
    {
        var wallMat = MakeMetalPanelMat(teamColor);
        var roofMat = MakeRoofMat(darkColor);
        var darkMat = MakeConcreteMat(new Color(0.15f, 0.14f, 0.13f));
        var windowMat = MakeMat(new Color(0.6f, 0.7f, 0.8f), 0.2f, 0.5f);
        windowMat.Emission = new Color(0.3f, 0.4f, 0.5f);
        windowMat.EmissionEnergyMultiplier = 0.3f;

        // 地基
        AddBox(_modelRoot, new Vector3(7, 0.3f, 7), new Vector3(0, 0.15f, 0), darkMat);
        // 主墙体
        AddBox(_modelRoot, new Vector3(6, 3, 5), new Vector3(0, 1.8f, 0), wallMat);
        // 屋顶
        AddBox(_modelRoot, new Vector3(6.5f, 0.3f, 5.5f), new Vector3(0, 3.45f, 0), roofMat);
        // 烟囱
        AddBox(_modelRoot, new Vector3(0.6f, 1f, 0.6f), new Vector3(2, 4f, 1.5f), darkMat);
        // 雷达天线
        AddCylinder(_modelRoot, 0.05f, 0.05f, 3f, new Vector3(-2, 4.5f, 0), new Vector3(0, 0, 0), MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f));
        AddBox(_modelRoot, new Vector3(1.5f, 0.05f, 0.1f), new Vector3(-2, 6f, 0), MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f));

        // 窗户
        for (int i = -1; i <= 1; i++)
        {
            AddBox(_modelRoot, new Vector3(0.6f, 0.8f, 0.05f), new Vector3(i * 2f, 1.8f, 2.55f), windowMat);
            AddBox(_modelRoot, new Vector3(0.6f, 0.8f, 0.05f), new Vector3(i * 2f, 1.8f, -2.55f), windowMat);
        }
        // 门
        AddBox(_modelRoot, new Vector3(1f, 1.5f, 0.1f), new Vector3(0, 0.9f, 2.55f), darkMat);
    }

    private void BuildPowerPlantModel(Color teamColor, Color darkColor)
    {
        var wallMat = MakeMetalPanelMat(new Color(0.6f, 0.55f, 0.5f));
        var darkMat = MakeConcreteMat(new Color(0.15f, 0.14f, 0.13f));
        var metalMat = MakeMat(new Color(0.35f, 0.35f, 0.38f), 0.3f, 0.8f);
        var glowMat = MakeTexturedMat("res://textures/glow_green.png", new Color(0.2f, 0.8f, 0.2f), 0.2f, 0.5f);
        glowMat.Emission = new Color(0.2f, 0.8f, 0.2f);
        glowMat.EmissionEnergyMultiplier = 0.5f;

        AddBox(_modelRoot, new Vector3(5, 0.3f, 4), new Vector3(0, 0.15f, 0), darkMat);
        AddBox(_modelRoot, new Vector3(4, 2.5f, 3), new Vector3(0, 1.55f, 0), wallMat);
        AddBox(_modelRoot, new Vector3(4.5f, 0.2f, 3.5f), new Vector3(0, 2.85f, 0), wallMat);
        // 冷却塔
        AddCylinder(_modelRoot, 0.8f, 1.2f, 2.5f, new Vector3(1.5f, 1.55f, 0), new Vector3(0, 0, 0), wallMat);
        // 发光窗
        AddBox(_modelRoot, new Vector3(0.5f, 0.5f, 0.05f), new Vector3(-1, 1.5f, 1.55f), glowMat);
        // 烟囱
        AddCylinder(_modelRoot, 0.2f, 0.25f, 1.5f, new Vector3(-1.5f, 3.5f, 0), new Vector3(0, 0, 0), metalMat);
    }

    private void BuildBarracksModel(Color teamColor, Color darkColor)
    {
        var wallMat = MakeBrickMat(new Color(0.55f, 0.42f, 0.28f));
        var roofMat = MakeRoofMat(darkColor);
        var darkMat = MakeConcreteMat(new Color(0.15f, 0.14f, 0.13f));

        AddBox(_modelRoot, new Vector3(5, 0.3f, 4), new Vector3(0, 0.15f, 0), darkMat);
        AddBox(_modelRoot, new Vector3(4, 2, 3.5f), new Vector3(0, 1.3f, 0), wallMat);
        AddBox(_modelRoot, new Vector3(4.5f, 0.2f, 4f), new Vector3(0, 2.4f, 0), roofMat);
        // 旗帜
        AddCylinder(_modelRoot, 0.05f, 0.05f, 2f, new Vector3(1.5f, 3.5f, 0), new Vector3(0, 0, 0), MakeMat(new Color(0.2f, 0.2f, 0.2f), 0.5f, 0.5f));
        AddBox(_modelRoot, new Vector3(0.8f, 0.5f, 0.05f), new Vector3(1.9f, 4f, 0), MakeMat(teamColor));
        // 门
        AddBox(_modelRoot, new Vector3(1f, 1.2f, 0.1f), new Vector3(0, 0.75f, 1.8f), darkMat);
    }

    private void BuildWarFactoryModel(Color teamColor, Color darkColor)
    {
        var wallMat = MakeMetalPanelMat(new Color(0.5f, 0.48f, 0.45f));
        var roofMat = MakeRoofMat(darkColor);
        var darkMat = MakeConcreteMat(new Color(0.15f, 0.14f, 0.13f));
        var metalMat = MakeMat(new Color(0.35f, 0.35f, 0.38f), 0.3f, 0.8f);

        AddBox(_modelRoot, new Vector3(7, 0.3f, 5), new Vector3(0, 0.15f, 0), darkMat);
        AddBox(_modelRoot, new Vector3(6, 2.5f, 4), new Vector3(0, 1.55f, 0), wallMat);
        AddBox(_modelRoot, new Vector3(6.5f, 0.2f, 4.5f), new Vector3(0, 2.85f, 0), roofMat);
        // 大门（车间卷帘门）
        AddBox(_modelRoot, new Vector3(3f, 2f, 0.1f), new Vector3(0, 1.3f, 2.05f), metalMat);
        // 吊车
        AddBox(_modelRoot, new Vector3(0.15f, 3f, 0.15f), new Vector3(-2.5f, 3.5f, -1), metalMat);
        AddBox(_modelRoot, new Vector3(2f, 0.15f, 0.15f), new Vector3(-1.5f, 5f, -1), metalMat);
        // 烟囱
        AddCylinder(_modelRoot, 0.3f, 0.35f, 1.5f, new Vector3(2, 3.5f, 1.5f), new Vector3(0, 0, 0), darkMat);
    }

    private void BuildTechCenterModel(Color teamColor, Color darkColor)
    {
        var wallMat = MakeMetalPanelMat(new Color(0.8f, 0.8f, 0.85f));
        var glassMat = MakeMat(new Color(0.3f, 0.5f, 0.7f), 0.1f, 0.5f);
        glassMat.Emission = new Color(0.2f, 0.4f, 0.6f);
        glassMat.EmissionEnergyMultiplier = 0.3f;

        AddBox(_modelRoot, new Vector3(6, 0.3f, 6), new Vector3(0, 0.15f, 0), MakeMat(new Color(0.2f, 0.2f, 0.2f), 0.9f));
        // 主体（高塔状，科技感）
        AddBox(_modelRoot, new Vector3(4, 5f, 4), new Vector3(0, 2.8f, 0), wallMat);
        // 玻璃穹顶
        AddSphere(_modelRoot, 1.5f, new Vector3(0, 5.5f, 0), glassMat);
        // 窗户带
        AddBox(_modelRoot, new Vector3(4.1f, 0.5f, 4.1f), new Vector3(0, 2f, 0), glassMat);
        AddBox(_modelRoot, new Vector3(4.1f, 0.5f, 4.1f), new Vector3(0, 3.5f, 0), glassMat);
        // 雷达
        AddCylinder(_modelRoot, 0.05f, 0.05f, 2f, new Vector3(0, 7.5f, 0), new Vector3(0, 0, 0), MakeMat(new Color(0.2f, 0.2f, 0.2f), 0.5f, 0.5f));
        AddBox(_modelRoot, new Vector3(2f, 0.05f, 0.15f), new Vector3(0, 8.5f, 0), MakeMat(new Color(0.2f, 0.2f, 0.2f), 0.5f, 0.5f));
    }

    private void BuildTurretBuildingModel(Color teamColor, Color darkColor)
    {
        var bodyMat = MakeMat(teamColor, 0.7f);
        var sandbagMat = MakeMat(new Color(0.55f, 0.48f, 0.32f), 0.9f);
        var metalMat = MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f);

        // 沙袋围墙
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.Pi / 4f;
            AddSphere(_modelRoot, 0.3f, new Vector3(Mathf.Cos(angle) * 1.2f, 0.25f, Mathf.Sin(angle) * 1.2f), sandbagMat);
        }
        // 混凝土基座
        AddCylinder(_modelRoot, 0.7f, 0.9f, 0.5f, new Vector3(0, 0.25f, 0), new Vector3(0, 0, 0), bodyMat);
        // 机枪塔
        _turretNode.Position = new Vector3(0, 0.65f, 0);
        AddBox(_turretNode, new Vector3(0.5f, 0.3f, 0.5f), new Vector3(0, 0, 0), metalMat);
        // 双管机枪
        for (int i = 0; i < 2; i++)
        {
            AddCylinder(_turretNode, 0.04f, 0.05f, 1.2f, new Vector3(i == 0 ? -0.1f : 0.1f, 0, 0.6f), new Vector3(90, 0, 0), metalMat);
        }
    }

    private void BuildAntiAirTurretModel(Color teamColor, Color darkColor)
    {
        var bodyMat = MakeMat(teamColor, 0.7f);
        var metalMat = MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f);

        // 基座
        AddCylinder(_modelRoot, 1f, 1.2f, 0.5f, new Vector3(0, 0.25f, 0), new Vector3(0, 0, 0), bodyMat);
        // 炮塔
        _turretNode.Position = new Vector3(0, 0.6f, 0);
        AddBox(_turretNode, new Vector3(0.6f, 0.4f, 0.6f), new Vector3(0, 0, 0), bodyMat);
        // 四管防空炮
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 - 0.5f) * 0.2f;
            float y = (i / 2 - 0.5f) * 0.2f + 0.1f;
            AddCylinder(_turretNode, 0.03f, 0.04f, 1.5f, new Vector3(x, y, 0.75f), new Vector3(90, 0, 0), metalMat);
        }
        // 雷达
        AddBox(_turretNode, new Vector3(0.3f, 0.5f, 0.05f), new Vector3(0, 0.3f, -0.3f), metalMat);
    }

    private void BuildRepairPadModel(Color teamColor, Color darkColor)
    {
        var floorMat = MakeMat(new Color(0.25f, 0.25f, 0.22f), 0.8f);
        var metalMat = MakeMat(new Color(0.35f, 0.35f, 0.38f), 0.3f, 0.8f);

        // 地面
        AddBox(_modelRoot, new Vector3(5, 0.1f, 5), new Vector3(0, 0.05f, 0), floorMat);
        // 维修站的龙门吊
        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -2f : 2f;
            AddBox(_modelRoot, new Vector3(0.2f, 3f, 0.2f), new Vector3(x, 1.5f, 0), metalMat);
        }
        AddBox(_modelRoot, new Vector3(4.5f, 0.2f, 0.2f), new Vector3(0, 3f, 0), metalMat);
        // 维修灯
        AddBox(_modelRoot, new Vector3(0.5f, 0.1f, 0.5f), new Vector3(0, 2f, 0), MakeMat(teamColor, 0.2f, 0.8f));
    }

    private void BuildAirfieldModel(Color teamColor, Color darkColor)
    {
        var runwayMat = MakeMat(new Color(0.2f, 0.2f, 0.2f), 0.9f);
        var wallMat = MakeMat(teamColor, 0.7f);
        AddBox(_modelRoot, new Vector3(8, 0.1f, 4), new Vector3(0, 0.05f, 0), runwayMat);
        // 跑道标线
        AddBox(_modelRoot, new Vector3(0.2f, 0.02f, 3f), new Vector3(-2, 0.12f, 0), MakeMat(new Color(0.9f, 0.9f, 0.5f)));
        AddBox(_modelRoot, new Vector3(0.2f, 0.02f, 3f), new Vector3(0, 0.12f, 0), MakeMat(new Color(0.9f, 0.9f, 0.5f)));
        AddBox(_modelRoot, new Vector3(0.2f, 0.02f, 3f), new Vector3(2, 0.12f, 0), MakeMat(new Color(0.9f, 0.9f, 0.5f)));
        // 塔台
        AddBox(_modelRoot, new Vector3(1.5f, 2.5f, 1.5f), new Vector3(3, 1.3f, -1.5f), wallMat);
        AddBox(_modelRoot, new Vector3(2f, 0.3f, 2f), new Vector3(3, 2.7f, -1.5f), wallMat);
    }

    private void BuildShipyardModel(Color teamColor, Color darkColor)
    {
        var dockMat = MakeMat(new Color(0.4f, 0.38f, 0.35f), 0.8f);
        var metalMat = MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f);
        // 干船坞
        AddBox(_modelRoot, new Vector3(6, 0.3f, 8), new Vector3(0, 0.15f, 0), dockMat);
        // 龙门吊
        AddBox(_modelRoot, new Vector3(0.3f, 4f, 0.3f), new Vector3(-2.5f, 2f, 0), metalMat);
        AddBox(_modelRoot, new Vector3(0.3f, 4f, 0.3f), new Vector3(2.5f, 2f, 0), metalMat);
        AddBox(_modelRoot, new Vector3(5.5f, 0.3f, 0.3f), new Vector3(0, 4f, 0), metalMat);
    }

    private void BuildNukeSiloModel(Color teamColor, Color darkColor)
    {
        var concreteMat = MakeMat(new Color(0.4f, 0.4f, 0.38f), 0.9f);
        var metalMat = MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f);
        AddBox(_modelRoot, new Vector3(6, 0.3f, 6), new Vector3(0, 0.15f, 0), concreteMat);
        // 发射井（圆形凹陷用深色圆柱表示）
        AddCylinder(_modelRoot, 1.5f, 1.5f, 0.3f, new Vector3(0, 0.3f, 0), new Vector3(0, 0, 0), MakeMat(new Color(0.1f, 0.1f, 0.1f), 0.9f));
        // 导弹（露出发射井的部分）
        AddCylinder(_modelRoot, 0.3f, 0.4f, 3f, new Vector3(0, 1.8f, 0), new Vector3(0, 0, 0), metalMat);
        AddCylinder(_modelRoot, 0.0f, 0.3f, 0.5f, new Vector3(0, 3.5f, 0), new Vector3(0, 0, 0), MakeMat(teamColor, 0.3f, 0.5f));
        // 围墙
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Pi / 4f;
            AddBox(_modelRoot, new Vector3(0.4f, 1.5f, 0.4f), new Vector3(Mathf.Cos(a) * 2.5f, 0.75f, Mathf.Sin(a) * 2.5f), concreteMat);
        }
    }

    private void BuildLightningTowerModel(Color teamColor, Color darkColor)
    {
        var metalMat = MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f);
        var glowMat = MakeMat(new Color(0.3f, 0.5f, 1f), 0.2f, 0.8f);
        glowMat.Emission = new Color(0.3f, 0.6f, 1f);
        glowMat.EmissionEnergyMultiplier = 1f;

        // 塔基
        AddCylinder(_modelRoot, 1f, 1.2f, 0.5f, new Vector3(0, 0.25f, 0), new Vector3(0, 0, 0), metalMat);
        // 塔身
        AddCylinder(_modelRoot, 0.3f, 0.5f, 5f, new Vector3(0, 3f, 0), new Vector3(0, 0, 0), metalMat);
        // 顶部球体（发光）
        AddSphere(_modelRoot, 0.8f, new Vector3(0, 6f, 0), glowMat);
        // 顶部尖刺
        AddCylinder(_modelRoot, 0.02f, 0.1f, 1.5f, new Vector3(0, 7f, 0), new Vector3(0, 0, 0), metalMat);
        // 放电弧（4个小球环绕）
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi / 2f;
            AddSphere(_modelRoot, 0.15f, new Vector3(Mathf.Cos(a) * 0.8f, 6f, Mathf.Sin(a) * 0.8f), glowMat);
        }
    }

    private void BuildMissileSiloModel(Color teamColor, Color darkColor)
    {
        var concreteMat = MakeMat(new Color(0.4f, 0.4f, 0.38f), 0.9f);
        var metalMat = MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f);
        AddBox(_modelRoot, new Vector3(5, 0.3f, 5), new Vector3(0, 0.15f, 0), concreteMat);
        // 发射台
        AddCylinder(_modelRoot, 1.2f, 1.4f, 0.5f, new Vector3(0, 0.4f, 0), new Vector3(0, 0, 0), concreteMat);
        // 斜置导弹
        var missileRoot = new Node3D();
        missileRoot.Position = new Vector3(0, 0.7f, 0);
        missileRoot.RotationDegrees = new Vector3(-30, 0, 0);
        AddCylinder(missileRoot, 0.25f, 0.35f, 3f, new Vector3(0, 1.5f, 0), new Vector3(0, 0, 0), metalMat);
        AddCylinder(missileRoot, 0.0f, 0.25f, 0.4f, new Vector3(0, 3.2f, 0), new Vector3(0, 0, 0), MakeMat(teamColor, 0.3f, 0.5f));
        AddBox(missileRoot, new Vector3(1f, 0.05f, 0.3f), new Vector3(0, 1.2f, 0.5f), metalMat); // 弹翼
        _modelRoot.AddChild(missileRoot);
    }

    // ======== 选中 ========

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        if (_selectionRing != null)
            _selectionRing.Visible = selected;
    }

    // ======== 受击 ========

    public void TakeDamage(float damage)
    {
        if (_isDead) return;
        Health -= damage;
        _hitFlashTimer = 0.15f;
        if (Health <= 0)
        {
            Health = 0;
            Die();
        }
    }

    public void Repair()
    {
        Health = MaxHealth;
    }

    public void RepairByEngineer(float amount)
    {
        Health = Math.Min(Health + amount, MaxHealth);
    }

    public void Deposit(float amount)
    {
        _game.AddResourceForTeam(TeamId, (int)amount);
    }

    // ======== 占领 ========

    public void CaptureTick(float dt, int capturingTeamId)
    {
        if (_capturingTeamId != capturingTeamId)
        {
            _capturingTeamId = capturingTeamId;
            CaptureProgress = 0;
        }
        CaptureProgress += dt * 20f; // 20% per second
        if (CaptureProgress >= 100f)
        {
            // 占领成功
            var oldTeam = TeamId;
            TeamId = capturingTeamId;
            CaptureProgress = 0;
            _capturingTeamId = -1;
            // 重建模型换色
            RebuildModel();
        }
    }

    private void RebuildModel()
    {
        // 删除旧模型
        foreach (var child in _modelRoot.GetChildren())
            child.QueueFree();
        BuildModel();
    }

    // ======== 生产队列 ========

    public void EnqueueProduction(ProductionType type)
    {
        if (_productionQueue.Count >= MaxQueueSize) return;
        _productionQueue.Enqueue(type);
        if (_currentProduction == null)
            StartNextProduction();
    }

    private void StartNextProduction()
    {
        if (_productionQueue.Count == 0) return;
        _currentProduction = _productionQueue.Dequeue();
        _productionProgress = 0;
        _productionTotalTime = GetProductionTime(_currentProduction.Value);
    }

    public static float GetProductionTime(ProductionType type) => type switch
    {
        ProductionType.Infantry => 5f,
        ProductionType.Engineer => 8f,
        ProductionType.Sapper => 5f,
        ProductionType.ChiefEngineer => 10f,
        ProductionType.Grenadier => 7f,
        ProductionType.Sniper => 9f,
        ProductionType.FlameInfantry => 7f,
        ProductionType.LightTank => 12f,
        ProductionType.HeavyTank => 20f,
        ProductionType.Artillery => 16f,
        ProductionType.RocketLauncher => 18f,
        ProductionType.MissileTank => 25f,
        ProductionType.AntiAir => 15f,
        ProductionType.Harvester => 18f,
        ProductionType.Transport => 15f,
        ProductionType.Hero => 30f,
        ProductionType.Spy => 20f,
        ProductionType.Thief => 15f,
        ProductionType.Fighter => 20f,
        ProductionType.Helicopter => 24f,
        ProductionType.RocketInfantry => 12f,
        ProductionType.Bomber => 32f,
        ProductionType.Scout => 12f,
        ProductionType.TransportHeli => 24f,
        ProductionType.Destroyer => 25f,
        ProductionType.Submarine => 30f,
        ProductionType.AircraftCarrier => 40f,
        ProductionType.LandingCraft => 20f,
        _ => 10f,
    };

    public bool IsProducing => _currentProduction != null;
    public float ProductionProgress => _currentProduction.HasValue ? (_productionProgress / _productionTotalTime) : 0f;
    public int QueueCount => _productionQueue.Count + (_currentProduction.HasValue ? 1 : 0);
    public ProductionType? CurrentProductionType => _currentProduction;

    public void SetRallyPoint(Vector3 point) => RallyPoint = point;

    // ======== _Process ========

    public override void _Process(double delta)
    {
        if (_isDead) return;
        float dt = (float)delta;

        // 生产进度
        if (_currentProduction.HasValue)
        {
            _productionProgress += dt;
            if (_productionProgress >= _productionTotalTime)
            {
                var producedType = _currentProduction.Value;
                _currentProduction = null;
                UnitProduced?.Invoke(producedType, this);
                StartNextProduction();
            }
        }

        // 防御射击
        if (IsDefensive)
        {
            _attackTimer -= dt;
            if (_attackTimer <= 0)
            {
                _attackTimer = AttackCooldown;
                FireAtNearestEnemy();
            }
            UpdateTurretRotation(dt);
        }

        // 维修站
        if (IsRepairStation)
        {
            foreach (var u in _game.GetAllUnits())
            {
                if (u.TeamId != TeamId || u._isDead) continue;
                if (GlobalPosition.DistanceTo(u.GlobalPosition) < RepairRadius && u.Health < u.MaxHealth)
                    u.RepairByRepairPad(25f * dt);
            }
        }

        // 占领衰减
        if (CaptureProgress > 0 && _capturingTeamId == -1)
        {
            CaptureProgress = Math.Max(0, CaptureProgress - dt * 10f);
        }

        // 血量Label
        UpdateHealthLabel();

        // 通知附近单位回防
        if (_hitFlashTimer > 0)
        {
            _hitFlashTimer -= dt;
            if (_hitFlashTimer > 0.14f)
            {
                _game.OnBuildingAttacked(this);
            }
        }
    }

    private void FireAtNearestEnemy()
    {
        Unit3D? target = null;
        float bestDist = AttackRange * AttackRange;
        foreach (var u in _game.GetAllUnits())
        {
            if (u.TeamId == TeamId || u._isDead) continue;
            if (Type == BuildingType.AntiAirTurret && !u.IsAirUnit) continue; // 防空炮只打空中
            if (Type == BuildingType.Turret && u.IsAirUnit) continue; // 机枪塔不打空中
            float d = GlobalPosition.DistanceSquaredTo(u.GlobalPosition);
            if (d < bestDist) { bestDist = d; target = u; }
        }

        if (target != null)
        {
            target.TakeDamage(AttackDamage);
            _game.SpawnMuzzleFlash(GlobalPosition, target.GlobalPosition);
        }
    }

    private void UpdateTurretRotation(float dt)
    {
        if (_turretNode == null || !_turretNode.Visible) return;
        // 简单跟踪：找最近敌人并指向
        Unit3D? target = null;
        float bestDist = AttackRange * AttackRange;
        foreach (var u in _game.GetAllUnits())
        {
            if (u.TeamId == TeamId || u._isDead) continue;
            float d = GlobalPosition.DistanceSquaredTo(u.GlobalPosition);
            if (d < bestDist) { bestDist = d; target = u; }
        }
        if (target != null)
        {
            var dir = target.GlobalPosition - GlobalPosition;
            dir.Y = 0;
            float targetAngle = Mathf.Atan2(dir.X, dir.Z);
            _turretAngle = Mathf.LerpAngle(_turretAngle, targetAngle, 5f * dt);
            _turretNode.Rotation = new Vector3(0, _turretAngle, 0);
        }
    }

    private void UpdateHealthLabel()
    {
        if (_healthLabel == null) return;
        if (Health < MaxHealth || IsSelected)
        {
            _healthLabel.Visible = true;
            float pct = Health / MaxHealth;
            _healthLabel.Text = $"{(int)Health}/{(int)MaxHealth}";
            _healthLabel.Modulate = pct > 0.5f ? Colors.Green : pct > 0.25f ? Colors.Yellow : Colors.Red;
        }
        else
            _healthLabel.Visible = false;
    }

    protected virtual void Die()
    {
        _isDead = true;
        _game.OnBuildingDied(this);
        QueueFree();
    }
}
