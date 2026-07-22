using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 3D RTS 单位基类：CharacterBody3D + 低多边形3D模型。
/// 移植自2D Unit.cs，保留全部游戏逻辑（AI/战斗/升级/运输/地形改造），重写渲染层。
/// 坐标系：地面XZ平面，Y=高度
/// </summary>
public partial class Unit3D : CharacterBody3D
{
    [Export] public float MoveSpeed { get; set; } = 12f;     // 3D速度（米/秒）
    [Export] public float MaxHealth { get; set; } = 100f;
    [Export] public float AttackDamage { get; set; } = 15f;
    [Export] public float AttackRange { get; set; } = 10f;   // 3D范围（米）
    [Export] public float AttackCooldown { get; set; } = 1.0f;
    [Export] public string UnitName { get; set; } = "Tank";

    public UnitType Type { get; set; } = UnitType.Default;
    public float MinAttackRange { get; set; } = 0f;
    public float SplashRadius { get; set; } = 0f;

    public float Health { get; protected set; }
    public bool IsSelected { get; protected set; }
    public int TeamId { get; set; } = 0;
    public bool AutoAI { get; set; } = false;
    public bool AutoDefend { get; set; } = true;
    public float AggroRange { get; set; } = 18f;
    public bool IsAirUnit { get; set; } = false;
    public bool CanAttackAir { get; set; } = false;
    public bool IsTransportHeli => Type == UnitType.TransportHeli;
    public bool IsNavalUnit => Type == UnitType.Destroyer || Type == UnitType.Submarine || Type == UnitType.AircraftCarrier || Type == UnitType.LandingCraft;

    // 移动状态
    protected Vector3 _moveTarget;
    protected bool _hasMoveTarget;
    private Unit3D? _attackUnitTarget;
    private Building3D? _attackBuildingTarget;
    private float _attackTimer;
    public bool _isDead;
    public bool IsDead => _isDead;
    private float _hitFlashTimer;
    private float _aiThinkTimer;
    private Vector3 _attackMoveTarget;
    private bool _hasAttackMoveTarget;
    private Vector3 _guardPosition;
    private bool _hasGuardPosition;

    // 运输系统
    public List<Unit3D> Passengers { get; } = new();
    public int MaxPassengers { get; set; } = 3;
    public bool IsTransport => Type == UnitType.Transport || Type == UnitType.TransportHeli
        || Type == UnitType.LandingCraft || Type == UnitType.AircraftCarrier;
    private UnitType _preMergeType = UnitType.Default;
    public Unit3D? _embarkTarget;

    // 特殊单位
    public enum HeroSkill { None, DoubleShot, HealAura, Dash, CriticalStrike, Shield }
    public HeroSkill _heroSkill = HeroSkill.None;
    public int _spyDisguiseTeam = -1;
    private float _spyInfiltrateTimer;
    private float _thiefStealCooldown;

    // 升级系统
    public enum UnitAbility { None, ArmorPiercing, DoubleShot, Scatter, ReactiveArmor, SelfRepair, SmokeScreen, TurboEngine, ReconVision, BattleFrenzy, Plunder, Tenacity }
    public float _experience = 0f;
    public int _level = 1;
    public readonly List<UnitAbility> _abilities = new();
    private static readonly int[] LevelThresholds = { 0, 100, 300, 600 };
    private float _outOfCombatTimer = 0f;

    // 地形改造
    public enum TerrainModType { None, Flatten, Tunnel, Bridge, UnderseaTunnel }
    private TerrainModType _terrainModType = TerrainModType.None;
    private Vector3 _terrainModTarget;
    private float _terrainModTimer;
    private float _terrainModDuration;
    private int _terrainModCost;
    private bool _isConstructing;
    public bool IsEngineerUnit => Type == UnitType.Sapper || Type == UnitType.ChiefEngineer || Type == UnitType.Engineer;

    public static float AiGraceRemaining = 0f;

    // 8阵营色调色板（用于3D材质染色）
    public static readonly Color[] TeamPalette =
    {
        new(0.82f, 0.16f, 0.16f),   // 0 Red
        new(0.16f, 0.32f, 0.82f),   // 1 Blue
        new(0.18f, 0.78f, 0.22f),   // 2 Green
        new(0.95f, 0.82f, 0.18f),   // 3 Yellow
        new(0.95f, 0.42f, 0.78f),   // 4 Pink
        new(0.44f, 0.18f, 0.72f),   // 5 Purple
        new(0.95f, 0.51f, 0.12f),   // 6 Orange
        new(0.14f, 0.62f, 0.88f),   // 7 Cyan
    };

    public static Color GetTeamColor(int teamId) => TeamPalette[teamId % TeamPalette.Length];

    // ======== 3D 节点引用 ========
    protected Node3D _modelRoot;        // 模型根节点（底盘等）
    protected Node3D _turretNode;       // 炮塔节点（可旋转）
    private MeshInstance3D _selectionRing;
    private Label3D _healthLabel;
    private float _turretAngle;
    private float _bodyAngle;

    // 地形引用
    private TerrainGrid3D _terrain;
    private Main3D _game;

    public void Initialize(TerrainGrid3D terrain, Main3D game)
    {
        _terrain = terrain;
        _game = game;
    }

    public override void _Ready()
    {
        Health = MaxHealth;

        // 模型根节点
        _modelRoot = new Node3D { Name = "Model" };
        AddChild(_modelRoot);

        // 炮塔节点
        _turretNode = new Node3D { Name = "Turret" };
        _modelRoot.AddChild(_turretNode);

        // 选中环（3D）
        CreateSelectionRing();

        // 血量Label3D
        _healthLabel = new Label3D
        {
            Text = "",
            FontSize = 24,
            OutlineSize = 4,
            OutlineModulate = Colors.Black,
            PixelSize = 0.01f,
            Position = new Vector3(0, 3f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            Visible = false,
        };
        AddChild(_healthLabel);

        // 构建单位3D模型
        BuildModel();
    }

    private void CreateSelectionRing()
    {
        // 用CylinderMesh做一个扁平圆环
        _selectionRing = new MeshInstance3D();
        var ringMesh = new CylinderMesh();
        ringMesh.TopRadius = 1.5f;
        ringMesh.BottomRadius = 1.5f;
        ringMesh.Height = 0.05f;
        _selectionRing.Mesh = ringMesh;

        var ringMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 1.0f, 0.2f, 0.5f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = true,
        };
        _selectionRing.MaterialOverride = ringMat;
        _selectionRing.Position = new Vector3(0, 0.03f, 0);
        _selectionRing.Visible = false;
        AddChild(_selectionRing);
    }

    // ======== 3D 模型构建 ========

    private void BuildModel()
    {
        Color teamColor = GetTeamColor(TeamId);
        Color darkColor = new(teamColor.R * 0.6f, teamColor.G * 0.6f, teamColor.B * 0.6f);

        switch (Type)
        {
            case UnitType.LightTank:
            case UnitType.Transport:
                BuildTankModel(teamColor, darkColor, 1.6f, 2.2f, 0.4f);
                break;
            case UnitType.HeavyTank:
                BuildTankModel(teamColor, darkColor, 2.0f, 2.6f, 0.5f);
                break;
            case UnitType.Artillery:
                BuildArtilleryModel(teamColor, darkColor);
                break;
            case UnitType.RocketLauncher:
                BuildRocketModel(teamColor, darkColor);
                break;
            case UnitType.MissileTank:
                BuildMissileModel(teamColor, darkColor);
                break;
            case UnitType.AntiAir:
                BuildAntiAirModel(teamColor, darkColor);
                break;
            case UnitType.Infantry:
            case UnitType.Grenadier:
            case UnitType.Sniper:
            case UnitType.FlameInfantry:
            case UnitType.RocketInfantry:
                BuildInfantryModel(teamColor, darkColor);
                break;
            case UnitType.Engineer:
            case UnitType.Sapper:
            case UnitType.ChiefEngineer:
                BuildEngineerModel(teamColor, darkColor);
                break;
            case UnitType.Hero:
                BuildHeroModel(teamColor, darkColor);
                break;
            case UnitType.Spy:
            case UnitType.Thief:
                BuildInfantryModel(teamColor, darkColor);
                break;
            case UnitType.Fighter:
                BuildFighterModel(teamColor, darkColor);
                break;
            case UnitType.Helicopter:
                BuildHelicopterModel(teamColor, darkColor);
                break;
            case UnitType.Bomber:
                BuildBomberModel(teamColor, darkColor);
                break;
            case UnitType.Scout:
                BuildScoutModel(teamColor, darkColor);
                break;
            case UnitType.TransportHeli:
                BuildTransportHeliModel(teamColor, darkColor);
                break;
            case UnitType.Destroyer:
                BuildDestroyerModel(teamColor, darkColor);
                break;
            case UnitType.Submarine:
                BuildSubmarineModel(teamColor, darkColor);
                break;
            case UnitType.AircraftCarrier:
                BuildCarrierModel(teamColor, darkColor);
                break;
            case UnitType.LandingCraft:
                BuildLandingCraftModel(teamColor, darkColor);
                break;
            default:
                BuildTankModel(teamColor, darkColor, 1.6f, 2.2f, 0.4f);
                break;
        }
    }

    protected StandardMaterial3D MakeMat(Color color, float roughness = 0.6f, float metallic = 0.3f)
    {
        var mat = new StandardMaterial3D { AlbedoColor = color, Roughness = roughness, Metallic = metallic };
        return mat;
    }

    /// <summary>带纹理的材质 — 用于坦克底盘等</summary>
    protected StandardMaterial3D MakeTexturedMat(string texPath, Color tintColor, float roughness = 0.6f, float metallic = 0.3f)
    {
        var mat = new StandardMaterial3D { AlbedoColor = tintColor, Roughness = roughness, Metallic = metallic };
        var tex = GD.Load<Texture2D>(texPath);
        if (tex != null)
        {
            mat.AlbedoTexture = tex;
            mat.Uv1Scale = new Vector3(1, 1, 1);
        }
        return mat;
    }

    /// <summary>坦克材质（迷彩纹理+阵营色调）</summary>
    protected StandardMaterial3D MakeTankMat(Color teamColor)
    {
        return MakeTexturedMat("res://textures/camo.png", teamColor, 0.5f, 0.4f);
    }

    /// <summary>金属装甲材质</summary>
    protected StandardMaterial3D MakeArmorMat(Color teamColor)
    {
        return MakeTexturedMat("res://textures/metal_panel.png", teamColor, 0.4f, 0.5f);
    }

    protected MeshInstance3D AddBox(Node3D parent, Vector3 size, Vector3 pos, StandardMaterial3D mat, bool shadow = true)
    {
        var mi = new MeshInstance3D();
        var mesh = new BoxMesh { Size = size };
        mi.Mesh = mesh;
        mi.MaterialOverride = mat;
        mi.Position = pos;
        if (shadow) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        parent.AddChild(mi);
        return mi;
    }

    protected MeshInstance3D AddCylinder(Node3D parent, float topR, float bottomR, float height, Vector3 pos, Vector3 rotDeg, StandardMaterial3D mat, bool shadow = true)
    {
        var mi = new MeshInstance3D();
        var mesh = new CylinderMesh { TopRadius = topR, BottomRadius = bottomR, Height = height };
        mi.Mesh = mesh;
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
        var mesh = new SphereMesh { Radius = radius, Height = radius * 2f };
        mi.Mesh = mesh;
        mi.MaterialOverride = mat;
        mi.Position = pos;
        if (shadow) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>创建坦克模型：底盘+履带+轮子+炮塔+炮管</summary>
    protected void BuildTankModel(Color bodyColor, Color darkColor, float width, float length, float hullHeight)
    {
        var bodyMat = MakeTankMat(bodyColor);
        var darkMat = MakeArmorMat(darkColor);
        var trackMat = MakeMat(new Color(0.12f, 0.12f, 0.12f), 0.9f, 0f);
        var metalMat = MakeMat(new Color(0.35f, 0.35f, 0.38f), 0.3f, 0.8f);

        // 底盘
        AddBox(_modelRoot, new Vector3(width, hullHeight, length), new Vector3(0, hullHeight * 0.5f + 0.2f, 0), bodyMat);
        // 前装甲斜面
        AddBox(_modelRoot, new Vector3(width * 0.88f, hullHeight * 0.75f, length * 0.18f), new Vector3(0, hullHeight * 0.4f + 0.2f, length * 0.52f), bodyMat);

        // 左右履带
        float trackOffset = width * 0.5f + 0.1f;
        AddBox(_modelRoot, new Vector3(0.4f, hullHeight + 0.1f, length + 0.2f), new Vector3(-trackOffset, hullHeight * 0.35f, 0), trackMat);
        AddBox(_modelRoot, new Vector3(0.4f, hullHeight + 0.1f, length + 0.2f), new Vector3(trackOffset, hullHeight * 0.35f, 0), trackMat);

        // 轮子
        int wheelCount = 4;
        float wheelSpacing = length * 0.8f / (wheelCount - 1);
        for (int i = 0; i < wheelCount; i++)
        {
            float z = -length * 0.4f + i * wheelSpacing;
            AddCylinder(_modelRoot, 0.22f, 0.22f, 0.3f, new Vector3(-trackOffset, 0.15f, z), new Vector3(0, 0, 90), metalMat);
            AddCylinder(_modelRoot, 0.22f, 0.22f, 0.3f, new Vector3(trackOffset, 0.15f, z), new Vector3(0, 0, 90), metalMat);
        }

        // 炮塔位置
        _turretNode.Position = new Vector3(0, hullHeight + 0.3f, -0.1f);

        // 炮塔主体
        AddBox(_turretNode, new Vector3(width * 0.65f, 0.45f, width * 0.65f), new Vector3(0, 0.1f, 0), bodyMat);
        // 炮塔顶部圆柱
        AddCylinder(_turretNode, 0.35f, 0.45f, 0.3f, new Vector3(0, 0.45f, 0), new Vector3(0, 0, 0), bodyMat);

        // 炮管
        float barrelLen = Type == UnitType.HeavyTank ? 2.0f : 1.6f;
        AddCylinder(_turretNode, 0.07f, 0.09f, barrelLen, new Vector3(0, 0.1f, barrelLen * 0.5f + 0.3f), new Vector3(90, 0, 0), metalMat);
        // 炮口制动器
        AddCylinder(_turretNode, 0.12f, 0.13f, 0.3f, new Vector3(0, 0.1f, barrelLen + 0.3f), new Vector3(90, 0, 0), darkMat);

        // 舱门
        AddCylinder(_turretNode, 0.18f, 0.2f, 0.1f, new Vector3(0, 0.65f, -0.15f), new Vector3(0, 0, 0), darkMat);
    }

    protected void BuildArtilleryModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.6f, 0.3f);
        var darkMat = MakeMat(darkColor, 0.7f, 0.2f);
        var trackMat = MakeMat(new Color(0.12f, 0.12f, 0.12f), 0.9f, 0f);
        var metalMat = MakeMat(new Color(0.35f, 0.35f, 0.38f), 0.3f, 0.8f);

        // 底盘（更小）
        AddBox(_modelRoot, new Vector3(1.4f, 0.35f, 2.0f), new Vector3(0, 0.375f, 0), bodyMat);
        // 履带
        AddBox(_modelRoot, new Vector3(0.35f, 0.45f, 2.2f), new Vector3(-0.7f, 0.225f, 0), trackMat);
        AddBox(_modelRoot, new Vector3(0.35f, 0.45f, 2.2f), new Vector3(0.7f, 0.225f, 0), trackMat);

        // 炮塔（开放式）
        _turretNode.Position = new Vector3(0, 0.55f, -0.2f);
        AddBox(_turretNode, new Vector3(1.0f, 0.3f, 1.0f), new Vector3(0, 0, 0), bodyMat);

        // 长炮管
        AddCylinder(_turretNode, 0.08f, 0.10f, 2.5f, new Vector3(0, 0.1f, 1.25f + 0.2f), new Vector3(90, 0, 0), metalMat);
        // 炮口
        AddCylinder(_turretNode, 0.14f, 0.15f, 0.3f, new Vector3(0, 0.1f, 2.5f + 0.2f), new Vector3(90, 0, 0), darkMat);

        // 护盾
        AddBox(_turretNode, new Vector3(1.0f, 0.6f, 0.1f), new Vector3(0, 0.25f, 0.2f), bodyMat);
    }

    protected void BuildRocketModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.6f, 0.3f);
        var darkMat = MakeMat(darkColor, 0.7f, 0.2f);
        var trackMat = MakeMat(new Color(0.12f, 0.12f, 0.12f), 0.9f, 0f);
        var metalMat = MakeMat(new Color(0.35f, 0.35f, 0.38f), 0.3f, 0.8f);

        // 底盘
        AddBox(_modelRoot, new Vector3(1.5f, 0.4f, 2.2f), new Vector3(0, 0.4f, 0), bodyMat);
        AddBox(_modelRoot, new Vector3(0.35f, 0.5f, 2.4f), new Vector3(-0.75f, 0.25f, 0), trackMat);
        AddBox(_modelRoot, new Vector3(0.35f, 0.5f, 2.4f), new Vector3(0.75f, 0.25f, 0), trackMat);

        // 发射架（可旋转）
        _turretNode.Position = new Vector3(0, 0.6f, -0.1f);
        AddBox(_turretNode, new Vector3(1.0f, 0.25f, 1.2f), new Vector3(0, 0, 0), bodyMat);

        // 火箭发射管（4管）
        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 2; j++)
            {
                float x = (i - 0.5f) * 0.4f;
                float y = j * 0.3f + 0.15f;
                AddCylinder(_turretNode, 0.12f, 0.12f, 1.2f, new Vector3(x, y, 0.6f), new Vector3(90, 0, 0), metalMat);
            }
        }
    }

    protected void BuildMissileModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.6f, 0.3f);
        var darkMat = MakeMat(darkColor, 0.7f, 0.2f);
        var trackMat = MakeMat(new Color(0.12f, 0.12f, 0.12f), 0.9f, 0f);
        var metalMat = MakeMat(new Color(0.35f, 0.35f, 0.38f), 0.3f, 0.8f);

        // 底盘
        AddBox(_modelRoot, new Vector3(1.5f, 0.4f, 2.4f), new Vector3(0, 0.4f, 0), bodyMat);
        AddBox(_modelRoot, new Vector3(0.35f, 0.5f, 2.6f), new Vector3(-0.75f, 0.25f, 0), trackMat);
        AddBox(_modelRoot, new Vector3(0.35f, 0.5f, 2.6f), new Vector3(0.75f, 0.25f, 0), trackMat);

        // 发射台
        _turretNode.Position = new Vector3(0, 0.6f, 0);
        AddBox(_turretNode, new Vector3(1.2f, 0.2f, 1.4f), new Vector3(0, 0, 0), bodyMat);

        // 垂直发射导弹（2枚）
        for (int i = 0; i < 2; i++)
        {
            float x = (i - 0.5f) * 0.6f;
            AddCylinder(_turretNode, 0.15f, 0.15f, 1.5f, new Vector3(x, 0.85f, 0), new Vector3(0, 0, 0), metalMat);
            AddCylinder(_turretNode, 0.08f, 0.08f, 0.3f, new Vector3(x, 1.75f, 0), new Vector3(0, 0, 0), darkMat);
        }
    }

    protected void BuildAntiAirModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.6f, 0.3f);
        var darkMat = MakeMat(darkColor, 0.7f, 0.2f);
        var trackMat = MakeMat(new Color(0.12f, 0.12f, 0.12f), 0.9f, 0f);
        var metalMat = MakeMat(new Color(0.35f, 0.35f, 0.38f), 0.3f, 0.8f);

        // 底盘（轮式）
        AddBox(_modelRoot, new Vector3(1.5f, 0.5f, 2.0f), new Vector3(0, 0.5f, 0), bodyMat);
        // 轮子
        for (int i = 0; i < 4; i++)
        {
            float z = -0.7f + i * 0.5f;
            AddCylinder(_modelRoot, 0.3f, 0.3f, 0.25f, new Vector3(-0.85f, 0.25f, z), new Vector3(0, 0, 90), metalMat);
            AddCylinder(_modelRoot, 0.3f, 0.3f, 0.25f, new Vector3(0.85f, 0.25f, z), new Vector3(0, 0, 90), metalMat);
        }

        // 防空炮塔
        _turretNode.Position = new Vector3(0, 0.8f, -0.1f);
        AddBox(_turretNode, new Vector3(1.0f, 0.35f, 1.0f), new Vector3(0, 0, 0), bodyMat);
        // 双管防空炮
        for (int i = 0; i < 2; i++)
        {
            float x = (i - 0.5f) * 0.3f;
            AddCylinder(_turretNode, 0.04f, 0.05f, 1.5f, new Vector3(x, 0.2f, 0.75f), new Vector3(90, 0, 0), metalMat);
        }
        // 雷达天线
        AddBox(_turretNode, new Vector3(0.3f, 0.4f, 0.05f), new Vector3(0, 0.3f, -0.4f), metalMat);
    }

    protected void BuildInfantryModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.7f, 0f);
        var darkMat = MakeMat(darkColor, 0.7f, 0.2f);
        var skinMat = MakeMat(new Color(0.8f, 0.65f, 0.5f), 0.8f, 0f);
        var metalMat = MakeMat(new Color(0.2f, 0.2f, 0.2f), 0.5f, 0.5f);

        // 身体
        AddBox(_modelRoot, new Vector3(0.4f, 0.6f, 0.3f), new Vector3(0, 0.6f, 0), bodyMat);
        // 头
        AddSphere(_modelRoot, 0.18f, new Vector3(0, 1.1f, 0), skinMat);
        // 腿
        AddBox(_modelRoot, new Vector3(0.15f, 0.4f, 0.15f), new Vector3(-0.1f, 0.2f, 0), darkMat);
        AddBox(_modelRoot, new Vector3(0.15f, 0.4f, 0.15f), new Vector3(0.1f, 0.2f, 0), darkMat);
        // 武器
        AddBox(_modelRoot, new Vector3(0.06f, 0.06f, 0.6f), new Vector3(0.25f, 0.7f, 0.2f), metalMat);

        // 步兵没有炮塔旋转
        _turretNode.Visible = false;
    }

    protected void BuildEngineerModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.7f, 0f);
        var darkMat = MakeMat(darkColor, 0.7f, 0.2f);
        var toolMat = MakeMat(new Color(0.8f, 0.6f, 0.2f), 0.5f, 0.5f);

        // 身体（工兵穿不同颜色的背心）
        AddBox(_modelRoot, new Vector3(0.4f, 0.6f, 0.3f), new Vector3(0, 0.6f, 0), bodyMat);
        AddSphere(_modelRoot, 0.18f, new Vector3(0, 1.1f, 0), MakeMat(new Color(0.8f, 0.65f, 0.5f), 0.8f, 0f));
        AddBox(_modelRoot, new Vector3(0.15f, 0.4f, 0.15f), new Vector3(-0.1f, 0.2f, 0), darkMat);
        AddBox(_modelRoot, new Vector3(0.15f, 0.4f, 0.15f), new Vector3(0.1f, 0.2f, 0), darkMat);
        // 工具箱
        AddBox(_modelRoot, new Vector3(0.3f, 0.2f, 0.2f), new Vector3(0, 0.4f, 0.25f), toolMat);

        _turretNode.Visible = false;
    }

    protected void BuildHeroModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.5f, 0.4f);
        var darkMat = MakeMat(darkColor, 0.7f, 0.2f);
        var skinMat = MakeMat(new Color(0.8f, 0.65f, 0.5f), 0.8f, 0f);
        var metalMat = MakeMat(new Color(0.9f, 0.8f, 0.2f), 0.2f, 0.9f);

        // 更大的身体
        AddBox(_modelRoot, new Vector3(0.5f, 0.75f, 0.35f), new Vector3(0, 0.7f, 0), bodyMat);
        AddSphere(_modelRoot, 0.22f, new Vector3(0, 1.3f, 0), skinMat);
        AddBox(_modelRoot, new Vector3(0.18f, 0.5f, 0.18f), new Vector3(-0.12f, 0.25f, 0), darkMat);
        AddBox(_modelRoot, new Vector3(0.18f, 0.5f, 0.18f), new Vector3(0.12f, 0.25f, 0), darkMat);
        // 大型武器
        AddBox(_modelRoot, new Vector3(0.08f, 0.08f, 0.9f), new Vector3(0.3f, 0.8f, 0.3f), metalMat);
        // 披风
        AddBox(_modelRoot, new Vector3(0.5f, 0.7f, 0.05f), new Vector3(0, 0.7f, -0.2f), bodyMat);

        _turretNode.Visible = false;
    }

    // 空军模型
    protected void BuildFighterModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.3f, 0.6f);
        var metalMat = MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f);

        // 机身
        AddCylinder(_modelRoot, 0.2f, 0.35f, 2.5f, new Vector3(0, 8f, 0), new Vector3(90, 0, 0), bodyMat);
        // 机头
        AddCylinder(_modelRoot, 0.0f, 0.2f, 0.8f, new Vector3(0, 8f, 1.65f), new Vector3(90, 0, 0), bodyMat);
        // 机翼
        AddBox(_modelRoot, new Vector3(3.0f, 0.08f, 0.8f), new Vector3(0, 8f, 0), metalMat);
        // 尾翼
        AddBox(_modelRoot, new Vector3(0.1f, 0.6f, 0.4f), new Vector3(0, 8.4f, -1.0f), metalMat);
        AddBox(_modelRoot, new Vector3(1.2f, 0.08f, 0.3f), new Vector3(0, 8f, -1.0f), metalMat);

        // 空军飞行高度8
        IsAirUnit = true;
        _turretNode.Visible = false;
    }

    protected void BuildHelicopterModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.5f, 0.3f);
        var metalMat = MakeMat(new Color(0.2f, 0.2f, 0.2f), 0.5f, 0.5f);

        // 机身
        AddSphere(_modelRoot, 0.4f, new Vector3(0, 6f, 0), bodyMat);
        // 尾梁
        AddBox(_modelRoot, new Vector3(0.2f, 0.2f, 1.5f), new Vector3(0, 6f, -1.0f), bodyMat);
        // 尾旋翼
        AddCylinder(_modelRoot, 0.02f, 0.02f, 0.5f, new Vector3(0, 6f, -1.7f), new Vector3(0, 0, 0), metalMat);
        // 主旋翼
        AddBox(_modelRoot, new Vector3(3.0f, 0.02f, 0.1f), new Vector3(0, 6.5f, 0), metalMat);
        // 起落架
        AddBox(_modelRoot, new Vector3(0.6f, 0.05f, 0.05f), new Vector3(0, 5.6f, 0), metalMat);

        IsAirUnit = true;
        _turretNode.Visible = false;
    }

    protected void BuildBomberModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.3f, 0.6f);
        var metalMat = MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f);

        // 大机身
        AddCylinder(_modelRoot, 0.3f, 0.5f, 3.5f, new Vector3(0, 9f, 0), new Vector3(90, 0, 0), bodyMat);
        // 大机翼
        AddBox(_modelRoot, new Vector3(4.5f, 0.1f, 1.0f), new Vector3(0, 9f, 0), metalMat);
        // 尾翼
        AddBox(_modelRoot, new Vector3(0.1f, 0.8f, 0.5f), new Vector3(0, 9.5f, -1.5f), metalMat);
        AddBox(_modelRoot, new Vector3(1.5f, 0.1f, 0.4f), new Vector3(0, 9f, -1.5f), metalMat);

        IsAirUnit = true;
        _turretNode.Visible = false;
    }

    protected void BuildScoutModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.4f, 0.4f);
        var metalMat = MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f);

        // 轻型机身
        AddCylinder(_modelRoot, 0.15f, 0.25f, 1.8f, new Vector3(0, 7f, 0), new Vector3(90, 0, 0), bodyMat);
        // 机翼
        AddBox(_modelRoot, new Vector3(2.5f, 0.06f, 0.6f), new Vector3(0, 7f, 0), metalMat);
        // 尾翼
        AddBox(_modelRoot, new Vector3(0.08f, 0.4f, 0.3f), new Vector3(0, 7.3f, -0.8f), metalMat);

        IsAirUnit = true;
        _turretNode.Visible = false;
    }

    protected void BuildTransportHeliModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.5f, 0.3f);
        var metalMat = MakeMat(new Color(0.2f, 0.2f, 0.2f), 0.5f, 0.5f);

        // 大机身
        AddBox(_modelRoot, new Vector3(1.2f, 0.8f, 2.0f), new Vector3(0, 6f, 0), bodyMat);
        // 尾梁
        AddBox(_modelRoot, new Vector3(0.2f, 0.2f, 1.8f), new Vector3(0, 6f, -1.5f), bodyMat);
        // 主旋翼
        AddBox(_modelRoot, new Vector3(3.5f, 0.02f, 0.1f), new Vector3(0, 6.6f, 0.2f), metalMat);
        // 尾旋翼
        AddCylinder(_modelRoot, 0.02f, 0.02f, 0.6f, new Vector3(0, 6f, -2.5f), new Vector3(0, 0, 90), metalMat);

        IsAirUnit = true;
        _turretNode.Visible = false;
    }

    // 海军模型
    protected void BuildDestroyerModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.5f, 0.3f);
        var metalMat = MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f);

        // 船体
        AddBox(_modelRoot, new Vector3(1.5f, 0.6f, 3.5f), new Vector3(0, 0.3f, 0), bodyMat);
        // 舰桥
        AddBox(_modelRoot, new Vector3(1.0f, 0.8f, 1.0f), new Vector3(0, 1.0f, -0.5f), bodyMat);
        // 炮塔
        _turretNode.Position = new Vector3(0, 1.4f, 0.5f);
        AddCylinder(_turretNode, 0.25f, 0.3f, 0.3f, new Vector3(0, 0, 0), new Vector3(0, 0, 0), bodyMat);
        AddCylinder(_turretNode, 0.06f, 0.07f, 1.2f, new Vector3(0, 0.1f, 0.6f), new Vector3(90, 0, 0), metalMat);
        // 烟囱
        AddCylinder(_modelRoot, 0.15f, 0.18f, 0.4f, new Vector3(0, 1.5f, -0.8f), new Vector3(0, 0, 0), metalMat);

        IsNavalUnit_test = true;
    }

    protected void BuildSubmarineModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.3f, 0.5f);
        var metalMat = MakeMat(new Color(0.2f, 0.2f, 0.2f), 0.4f, 0.6f);

        // 船体（圆柱）
        AddCylinder(_modelRoot, 0.5f, 0.5f, 3.0f, new Vector3(0, 0.2f, 0), new Vector3(90, 0, 0), bodyMat);
        // 指挥塔
        AddBox(_modelRoot, new Vector3(0.3f, 0.5f, 0.6f), new Vector3(0, 0.7f, 0), bodyMat);
        // 潜望镜
        AddCylinder(_modelRoot, 0.03f, 0.03f, 0.8f, new Vector3(0, 1.2f, 0), new Vector3(0, 0, 0), metalMat);

        _turretNode.Visible = false;
    }

    protected void BuildCarrierModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.5f, 0.3f);
        var metalMat = MakeMat(new Color(0.3f, 0.3f, 0.32f), 0.3f, 0.8f);

        // 大型甲板
        AddBox(_modelRoot, new Vector3(2.5f, 0.5f, 5.0f), new Vector3(0, 0.4f, 0), bodyMat);
        // 舰桥
        AddBox(_modelRoot, new Vector3(0.8f, 1.2f, 1.0f), new Vector3(0, 1.25f, -1.5f), bodyMat);
        // 飞机
        AddBox(_modelRoot, new Vector3(0.3f, 0.1f, 0.6f), new Vector3(0, 0.7f, 0.5f), metalMat);

        _turretNode.Visible = false;
    }

    protected void BuildLandingCraftModel(Color bodyColor, Color darkColor)
    {
        var bodyMat = MakeMat(bodyColor, 0.6f, 0.2f);
        // 船体（敞开式）
        AddBox(_modelRoot, new Vector3(1.8f, 0.4f, 3.0f), new Vector3(0, 0.2f, 0), bodyMat);
        AddBox(_modelRoot, new Vector3(1.8f, 0.5f, 0.2f), new Vector3(0, 0.45f, 1.5f), bodyMat); // 前舱门
        AddBox(_modelRoot, new Vector3(0.2f, 0.6f, 2.5f), new Vector3(-0.9f, 0.5f, -0.2f), bodyMat); // 左舷
        AddBox(_modelRoot, new Vector3(0.2f, 0.6f, 2.5f), new Vector3(0.9f, 0.5f, -0.2f), bodyMat); // 右舷

        _turretNode.Visible = false;
    }

    // 兼容字段
    private bool IsNavalUnit_test;

    // ======== InitAsType: 27种兵种数值初始化（从2D移植）========

    public void InitAsType(UnitType type)
    {
        Type = type;

        switch (type)
        {
            case UnitType.LightTank:
                MaxHealth = 120; MoveSpeed = 14f; AttackDamage = 18; AttackRange = 12; AttackCooldown = 0.8f; break;
            case UnitType.HeavyTank:
                MaxHealth = 250; MoveSpeed = 9f; AttackDamage = 30; AttackRange = 13; AttackCooldown = 1.2f; break;
            case UnitType.Artillery:
                MaxHealth = 80; MoveSpeed = 8f; AttackDamage = 35; AttackRange = 20; AttackCooldown = 2.0f;
                MinAttackRange = 5; SplashRadius = 3; break;
            case UnitType.RocketLauncher:
                MaxHealth = 70; MoveSpeed = 9f; AttackDamage = 25; AttackRange = 18; AttackCooldown = 1.5f;
                SplashRadius = 4; break;
            case UnitType.MissileTank:
                MaxHealth = 100; MoveSpeed = 8f; AttackDamage = 40; AttackRange = 22; AttackCooldown = 3.0f; break;
            case UnitType.AntiAir:
                MaxHealth = 80; MoveSpeed = 11f; AttackDamage = 12; AttackRange = 16; AttackCooldown = 0.3f;
                CanAttackAir = true; break;
            case UnitType.Infantry:
                MaxHealth = 50; MoveSpeed = 8f; AttackDamage = 8; AttackRange = 8; AttackCooldown = 0.5f; break;
            case UnitType.Engineer:
                MaxHealth = 60; MoveSpeed = 8f; AttackDamage = 5; AttackRange = 5; AttackCooldown = 1.0f; break;
            case UnitType.Sapper:
                MaxHealth = 50; MoveSpeed = 8f; AttackDamage = 3; AttackRange = 3; AttackCooldown = 2.0f;
                _terrainModType = TerrainModType.Flatten; break;
            case UnitType.ChiefEngineer:
                MaxHealth = 70; MoveSpeed = 9f; AttackDamage = 5; AttackRange = 5; AttackCooldown = 1.5f;
                _terrainModType = TerrainModType.Flatten; break;
            case UnitType.Grenadier:
                MaxHealth = 55; MoveSpeed = 8f; AttackDamage = 15; AttackRange = 10; AttackCooldown = 1.0f;
                SplashRadius = 2; break;
            case UnitType.Sniper:
                MaxHealth = 45; MoveSpeed = 8f; AttackDamage = 30; AttackRange = 18; AttackCooldown = 2.0f; break;
            case UnitType.FlameInfantry:
                MaxHealth = 60; MoveSpeed = 8f; AttackDamage = 12; AttackRange = 6; AttackCooldown = 0.3f;
                SplashRadius = 1.5f; break;
            case UnitType.Transport:
                MaxHealth = 100; MoveSpeed = 10f; AttackDamage = 0; AttackRange = 0; AttackCooldown = 99;
                MaxPassengers = 3; break;
            case UnitType.Hero:
                MaxHealth = 200; MoveSpeed = 10f; AttackDamage = 25; AttackRange = 10; AttackCooldown = 0.5f;
                _heroSkill = (HeroSkill)new Random().Next(1, 6); break;
            case UnitType.Spy:
                MaxHealth = 50; MoveSpeed = 9f; AttackDamage = 5; AttackRange = 5; AttackCooldown = 2.0f; break;
            case UnitType.Thief:
                MaxHealth = 45; MoveSpeed = 9f; AttackDamage = 3; AttackRange = 3; AttackCooldown = 2.0f; break;
            case UnitType.Fighter:
                MaxHealth = 80; MoveSpeed = 25f; AttackDamage = 20; AttackRange = 14; AttackCooldown = 0.5f;
                IsAirUnit = true; CanAttackAir = true; break;
            case UnitType.Helicopter:
                MaxHealth = 100; MoveSpeed = 18f; AttackDamage = 15; AttackRange = 12; AttackCooldown = 0.8f;
                IsAirUnit = true; break;
            case UnitType.RocketInfantry:
                MaxHealth = 50; MoveSpeed = 7f; AttackDamage = 18; AttackRange = 14; AttackCooldown = 1.5f;
                CanAttackAir = true; break;
            case UnitType.Bomber:
                MaxHealth = 120; MoveSpeed = 20f; AttackDamage = 50; AttackRange = 6; AttackCooldown = 3.0f;
                IsAirUnit = true; SplashRadius = 5; break;
            case UnitType.Scout:
                MaxHealth = 40; MoveSpeed = 28f; AttackDamage = 0; AttackRange = 0; AttackCooldown = 99;
                IsAirUnit = true; break;
            case UnitType.TransportHeli:
                MaxHealth = 90; MoveSpeed = 16f; AttackDamage = 0; AttackRange = 0; AttackCooldown = 99;
                IsAirUnit = true; MaxPassengers = 5; break;
            case UnitType.Destroyer:
                MaxHealth = 150; MoveSpeed = 10f; AttackDamage = 20; AttackRange = 15; AttackCooldown = 1.0f; break;
            case UnitType.Submarine:
                MaxHealth = 100; MoveSpeed = 8f; AttackDamage = 35; AttackRange = 14; AttackCooldown = 2.5f; break;
            case UnitType.AircraftCarrier:
                MaxHealth = 300; MoveSpeed = 6f; AttackDamage = 0; AttackRange = 0; AttackCooldown = 99;
                MaxPassengers = 4; break;
            case UnitType.LandingCraft:
                MaxHealth = 120; MoveSpeed = 7f; AttackDamage = 5; AttackRange = 5; AttackCooldown = 2.0f;
                MaxPassengers = 4; break;
        }

        // 等级能力修正
        if (_abilities.Contains(UnitAbility.TurboEngine))
            MoveSpeed *= 1.2f;
    }

    public static bool IsInfantryType(UnitType type) => type == UnitType.Infantry || type == UnitType.Engineer
        || type == UnitType.Sapper || type == UnitType.ChiefEngineer || type == UnitType.Grenadier
        || type == UnitType.Sniper || type == UnitType.FlameInfantry || type == UnitType.RocketInfantry
        || type == UnitType.Hero || type == UnitType.Spy || type == UnitType.Thief;

    public virtual TerrainUnitCategory GetTerrainCategory()
    {
        if (IsAirUnit) return TerrainUnitCategory.Air;
        if (IsNavalUnit) return TerrainUnitCategory.Naval;
        if (Type == UnitType.Harvester) return TerrainUnitCategory.Harvester;
        if (IsEngineerUnit) return TerrainUnitCategory.EngineerVehicle;
        if (IsInfantryType(Type)) return TerrainUnitCategory.Infantry;
        if (Type == UnitType.LightTank || Type == UnitType.Transport || Type == UnitType.AntiAir
            || Type == UnitType.Artillery || Type == UnitType.RocketLauncher)
            return TerrainUnitCategory.LightVehicle;
        return TerrainUnitCategory.HeavyVehicle;
    }

    // ======== 选中/命令 ========

    public virtual void SetSelected(bool selected)
    {
        IsSelected = selected;
        if (_selectionRing != null)
            _selectionRing.Visible = selected;
    }

    public virtual void CommandMove(Vector3 target)
    {
        _moveTarget = target;
        _hasMoveTarget = true;
        _attackUnitTarget = null;
        _attackBuildingTarget = null;
        _hasAttackMoveTarget = false;
    }

    public void CommandAttackMove(Vector3 target)
    {
        _attackMoveTarget = target;
        _hasAttackMoveTarget = true;
        _hasMoveTarget = false;
        _attackUnitTarget = null;
        _attackBuildingTarget = null;
        if (!_hasGuardPosition)
        {
            _guardPosition = GlobalPosition;
            _hasGuardPosition = true;
        }
    }

    public void CommandStop()
    {
        _hasMoveTarget = false;
        _hasAttackMoveTarget = false;
        _attackUnitTarget = null;
        _attackBuildingTarget = null;
    }

    public virtual void CommandAttack(Unit3D target)
    {
        _attackUnitTarget = target;
        _attackBuildingTarget = null;
        _hasMoveTarget = false;
        _hasAttackMoveTarget = false;
    }

    public virtual void CommandAttackBuilding(Building3D target)
    {
        _attackBuildingTarget = target;
        _attackUnitTarget = null;
        _hasMoveTarget = false;
        _hasAttackMoveTarget = false;
    }

    // ======== 运输系统 ========

    public void EmbarkPassenger(Unit3D passenger)
    {
        if (!IsTransport || Passengers.Count >= MaxPassengers) return;
        passenger.GetParent()?.RemoveChild(passenger);
        Passengers.Add(passenger);
        ApplyMergeEffect(passenger);
    }

    private void ApplyMergeEffect(Unit3D passenger)
    {
        if (_preMergeType == UnitType.Default && Passengers.Count == 1)
        {
            _preMergeType = Type;
            Type = passenger.Type switch
            {
                UnitType.Sapper => UnitType.Engineer,
                UnitType.ChiefEngineer => UnitType.Engineer,
                UnitType.Grenadier => UnitType.Artillery,
                UnitType.Sniper => UnitType.AntiAir,
                UnitType.FlameInfantry => UnitType.RocketLauncher,
                UnitType.Hero => UnitType.HeavyTank,
                UnitType.Spy => UnitType.Scout,
                UnitType.Thief => UnitType.LightTank,
                _ => Type,
            };
        }
    }

    public void DisembarkAll()
    {
        foreach (var p in Passengers)
        {
            var pos = GlobalPosition + new Vector3(
                (float)(new Random().NextDouble() - 0.5) * 3f, 0,
                (float)(new Random().NextDouble() - 0.5) * 3f);
            p.GlobalPosition = pos;
            _game.GetUnitsNode().AddChild(p);
        }
        Passengers.Clear();
        if (_preMergeType != UnitType.Default)
        {
            var oldType = Type;
            Type = _preMergeType;
            _preMergeType = UnitType.Default;
            InitAsType(Type);
        }
    }

    // ======== 攻击/伤害 ========

    public void TakeDamage(float damage)
    {
        if (_isDead) return;

        // 能力修饰
        if (_abilities.Contains(UnitAbility.ReactiveArmor))
            damage *= 0.8f;
        if (_abilities.Contains(UnitAbility.Tenacity) && Health < MaxHealth * 0.3f)
            damage *= 0.7f;
        if (_abilities.Contains(UnitAbility.SmokeScreen) && new Random().NextDouble() < 0.2f)
            return; // 闪避

        Health -= damage;
        _outOfCombatTimer = 0f;
        _hitFlashTimer = 0.15f;

        if (Health <= 0)
        {
            Health = 0;
            Die();
        }
    }

    public void Heal(float amount)
    {
        Health = Math.Min(Health + amount, MaxHealth);
    }

    public void RepairByRepairPad(float amount) => Heal(amount);

    public void GainExperience(float xp)
    {
        if (_isDead || _level >= 4) return;
        _experience += xp;
        CheckLevelUp();
    }

    private void CheckLevelUp()
    {
        while (_level < 4 && _experience >= LevelThresholds[_level])
        {
            _level++;
            RollRandomAbility();
        }
    }

    private void RollRandomAbility()
    {
        var pool = new List<UnitAbility>
        {
            UnitAbility.ArmorPiercing, UnitAbility.DoubleShot, UnitAbility.Scatter,
            UnitAbility.ReactiveArmor, UnitAbility.SelfRepair, UnitAbility.SmokeScreen,
            UnitAbility.TurboEngine, UnitAbility.ReconVision, UnitAbility.BattleFrenzy,
            UnitAbility.Plunder, UnitAbility.Tenacity,
        };
        pool.RemoveAll(a => _abilities.Contains(a));
        if (pool.Count == 0) return;
        var picked = pool[new Random().Next(pool.Count)];
        _abilities.Add(picked);

        if (picked == UnitAbility.TurboEngine)
            MoveSpeed *= 1.2f;
    }

    protected virtual void Die()
    {
        _isDead = true;
        _game.OnUnitDied(this);
        QueueFree();
    }

    // ======== _Process ========

    public override void _Process(double delta)
    {
        if (_isDead) return;
        float dt = (float)delta;

        // 受击闪白
        if (_hitFlashTimer > 0)
        {
            _hitFlashTimer -= dt;
            // 3D：用Emission实现闪白
            FlashModel(_hitFlashTimer > 0);
        }

        // 脱战计时
        _outOfCombatTimer += dt;

        // 自修复
        if (_abilities.Contains(UnitAbility.SelfRepair) && _outOfCombatTimer > 3f && Health < MaxHealth)
        {
            Health = Math.Min(Health + MaxHealth * 0.01f * dt, MaxHealth);
        }

        // AI
        _aiThinkTimer -= dt;
        if (_aiThinkTimer <= 0)
        {
            _aiThinkTimer = 0.2f;
            ProcessAI(dt);
        }

        // 战斗
        ResolveCombat(dt);

        // 移动
        ProcessMovement(dt);

        // 更新血量Label
        UpdateHealthLabel();

        // 炮塔旋转追踪
        UpdateTurretRotation(dt);

        // 地形改造
        if (_isConstructing) ProcessTerrainMod(dt);

        // 工程车辅助
        if (Type == UnitType.Engineer) TryRepairNearby(dt);

        // 特殊单位
        if (Type == UnitType.Hero && _heroSkill != HeroSkill.None) ProcessHeroSkill(dt);
        if (Type == UnitType.Spy) ProcessSpyInfiltrate(dt);
        if (Type == UnitType.Thief) ProcessThiefSteal(dt);

        // 运输交互
        if (_embarkTarget != null) ProcessEmbark(dt);
    }

    private void FlashModel(bool flash)
    {
        if (_modelRoot == null) return;
        // 遍历_modelRoot下所有MeshInstance3D，切换emission实现受击闪白
        foreach (var child in _modelRoot.GetChildren())
        {
            if (child is MeshInstance3D mi)
            {
                var mat = mi.MaterialOverride as StandardMaterial3D;
                if (mat == null) continue;
                if (flash)
                {
                    // 受击闪白：增强emission到白色
                    mat.Emission = new Color(1f, 0.3f, 0.3f);
                    mat.EmissionEnergyMultiplier = 3f;
                }
                else
                {
                    // 恢复：低emission
                    mat.EmissionEnergyMultiplier = 0f;
                }
            }
            else if (child is Node3D sub && sub != _turretNode)
            {
                // 也检查子节点的子节点（炮塔上的barrel等在_turretNode下）
                foreach (var sub2 in sub.GetChildren())
                {
                    if (sub2 is MeshInstance3D mi2)
                    {
                        var mat = mi2.MaterialOverride as StandardMaterial3D;
                        if (mat == null) continue;
                        if (flash)
                        {
                            mat.Emission = new Color(1f, 0.3f, 0.3f);
                            mat.EmissionEnergyMultiplier = 3f;
                        }
                        else
                        {
                            mat.EmissionEnergyMultiplier = 0f;
                        }
                    }
                }
            }
        }
        // 炮塔下的网格也要闪
        if (_turretNode != null)
        {
            foreach (var child in _turretNode.GetChildren())
            {
                if (child is MeshInstance3D mi)
                {
                    var mat = mi.MaterialOverride as StandardMaterial3D;
                    if (mat == null) continue;
                    if (flash)
                    {
                        mat.Emission = new Color(1f, 0.3f, 0.3f);
                        mat.EmissionEnergyMultiplier = 3f;
                    }
                    else
                    {
                        mat.EmissionEnergyMultiplier = 0f;
                    }
                }
            }
        }
    }

    // ======== AI 逻辑（从2D移植，Vector2→Vector3）========

    protected virtual void ProcessAI(float dt)
    {
        // AutoAI: 全图搜敌
        if (AutoAI)
        {
            if (AiGraceRemaining > 0)
            {
                // 保护期只打身边敌人
                var enemy = FindNearestEnemyUnitInRange(AggroRange);
                if (enemy != null) { CommandAttack(enemy); return; }
                var bldg = FindNearestEnemyBuildingInRange(AggroRange);
                if (bldg != null) { CommandAttackBuilding(bldg); return; }
                return;
            }

            var target = FindNearestEnemyUnit();
            if (target != null) { CommandAttack(target); return; }
            var bldgTarget = FindNearestEnemyBuilding();
            if (bldgTarget != null) { CommandAttackBuilding(bldgTarget); return; }
            return;
        }

        // 攻击移动
        if (_hasAttackMoveTarget)
        {
            var enemy = FindNearestEnemyUnitInRange(AggroRange * 1.5f);
            if (enemy != null) { _attackUnitTarget = enemy; return; }
            var bldg = FindNearestEnemyBuildingInRange(AggroRange * 1.5f);
            if (bldg != null) { _attackBuildingTarget = bldg; return; }

            float dist = GlobalPosition.DistanceTo(_attackMoveTarget);
            if (dist < 1f)
            {
                _hasAttackMoveTarget = false;
            }
            else
            {
                _moveTarget = _attackMoveTarget;
                _hasMoveTarget = true;
            }
            return;
        }

        // 自动防御
        if (AutoDefend && !_hasMoveTarget && _attackUnitTarget == null && _attackBuildingTarget == null)
        {
            var enemy = FindNearestEnemyUnitInRange(AggroRange);
            if (enemy != null)
            {
                if (!_hasGuardPosition) { _guardPosition = GlobalPosition; _hasGuardPosition = true; }
                CommandAttack(enemy);
                return;
            }
            var bldg = FindNearestEnemyBuildingInRange(AggroRange);
            if (bldg != null) { CommandAttackBuilding(bldg); return; }
        }
    }

    protected virtual void ResolveCombat(float dt)
    {
        _attackTimer -= dt;

        if (_attackUnitTarget != null)
        {
            if (_attackUnitTarget._isDead || !IsInstanceValid(_attackUnitTarget))
            {
                _attackUnitTarget = null;
                return;
            }

            // 对空规则
            if (_attackUnitTarget.IsAirUnit && !CanAttackAir)
            {
                _attackUnitTarget = null;
                return;
            }

            float dist = GlobalPosition.DistanceTo(_attackUnitTarget.GlobalPosition);
            float minRange = MinAttackRange;

            if (dist > AttackRange)
            {
                // 追击
                _moveTarget = _attackUnitTarget.GlobalPosition;
                _hasMoveTarget = true;
            }
            else if (dist < minRange)
            {
                // 太近，后退
                var away = (GlobalPosition - _attackUnitTarget.GlobalPosition).Normalized();
                _moveTarget = GlobalPosition + away * 2f;
                _hasMoveTarget = true;
            }
            else
            {
                // 在射程内，停止移动
                _hasMoveTarget = false;

                if (_attackTimer <= 0)
                {
                    float cd = AttackCooldown;
                    if (_abilities.Contains(UnitAbility.BattleFrenzy))
                    {
                        var nearest = FindNearestEnemyUnitInRange(AttackRange);
                        if (nearest != null) cd *= 0.8f;
                    }
                    if (_abilities.Contains(UnitAbility.DoubleShot))
                        cd *= 0.6f;
                    _attackTimer = cd;

                    FireAtUnit(_attackUnitTarget);
                }
            }

            _outOfCombatTimer = 0f;
        }
        else if (_attackBuildingTarget != null)
        {
            if (_attackBuildingTarget._isDead || !IsInstanceValid(_attackBuildingTarget))
            {
                _attackBuildingTarget = null;
                return;
            }

            float dist = GlobalPosition.DistanceTo(_attackBuildingTarget.GlobalPosition);
            if (dist > AttackRange)
            {
                _moveTarget = _attackBuildingTarget.GlobalPosition;
                _hasMoveTarget = true;
            }
            else
            {
                _hasMoveTarget = false;
                if (_attackTimer <= 0)
                {
                    _attackTimer = AttackCooldown;
                    FireAtBuilding(_attackBuildingTarget);
                }
            }
            _outOfCombatTimer = 0f;
        }
    }

    private void FireAtUnit(Unit3D target)
    {
        float dmg = AttackDamage;
        bool crit = false;

        if (_abilities.Contains(UnitAbility.ArmorPiercing) && (target.Type == UnitType.HeavyTank || target.Type == UnitType.MissileTank))
            dmg *= 1.25f;

        if (Type == UnitType.Hero && _heroSkill == HeroSkill.CriticalStrike && new Random().NextDouble() < 0.3)
        {
            dmg *= 2f; crit = true;
        }

        target.TakeDamage(dmg);
        _game.SpawnMuzzleFlash(GlobalPosition, target.GlobalPosition);

        // 溅射
        if (SplashRadius > 0 || _abilities.Contains(UnitAbility.Scatter))
        {
            float splashR = Math.Max(SplashRadius, _abilities.Contains(UnitAbility.Scatter) ? 3f : 0);
            if (splashR > 0)
            {
                foreach (var u in _game.GetAllUnits())
                {
                    if (u == target || u.TeamId == TeamId || u._isDead) continue;
                    if (u.GlobalPosition.DistanceTo(target.GlobalPosition) < splashR)
                        u.TakeDamage(dmg * 0.5f);
                }
            }
        }

        // 经验
        GainExperience(dmg * 0.5f);
        if (target._isDead)
        {
            GainExperience(50);
            if (_abilities.Contains(UnitAbility.Plunder))
                _game.AddResourceForTeam(TeamId, 10);
        }

        _outOfCombatTimer = 0f;
    }

    private void FireAtBuilding(Building3D target)
    {
        float dmg = AttackDamage;
        if (Type == UnitType.MissileTank) dmg *= 1.5f; // 导弹车克建筑

        target.TakeDamage(dmg);
        _game.SpawnMuzzleFlash(GlobalPosition, target.GlobalPosition);

        GainExperience(dmg * 0.5f);
        if (target._isDead)
        {
            GainExperience(100);
            if (_abilities.Contains(UnitAbility.Plunder))
                _game.AddResourceForTeam(TeamId, 10);
        }
    }

    // ======== 移动 ========

    protected virtual void ProcessMovement(float dt)
    {
        if (!_hasMoveTarget) return;

        var dir = _moveTarget - GlobalPosition;
        dir.Y = 0; // 保持地面
        float dist = dir.Length();
        if (dist < 0.5f) { _hasMoveTarget = false; return; }

        dir = dir.Normalized();

        // 地形速度修正
        float speedMult = 1f;
        if (!IsAirUnit)
        {
            var cat = GetTerrainCategory();
            var cell = _terrain.GetCellAtWorld(GlobalPosition.X, GlobalPosition.Z);
            var effType = _terrain.GetEffectiveType((int)(GlobalPosition.X / TerrainGrid3D.CellSize), (int)(GlobalPosition.Z / TerrainGrid3D.CellSize));
            speedMult = TerrainGrid3D.GetSpeedModifier(cat, effType, cell.Elevation, cell.Elevation);
            if (speedMult <= 0f) { _hasMoveTarget = false; return; }
        }

        var velocity = dir * MoveSpeed * speedMult;
        Velocity = new Vector3(velocity.X, 0, velocity.Z);
        MoveAndSlide();

        // 转向
        float targetAngle = Mathf.Atan2(dir.X, dir.Z);
        _bodyAngle = Mathf.LerpAngle(_bodyAngle, targetAngle, 5f * dt);
        _modelRoot.Rotation = new Vector3(0, _bodyAngle, 0);

        // 空军高度
        if (IsAirUnit && GlobalPosition.Y < 6f)
        {
            GlobalPosition = new Vector3(GlobalPosition.X, 6f, GlobalPosition.Z);
        }

        // 地面单位高度跟随地形
        if (!IsAirUnit && !IsNavalUnit)
        {
            float terrainY = _terrain.GetHeightAtWorld(GlobalPosition.X, GlobalPosition.Z);
            GlobalPosition = new Vector3(GlobalPosition.X, terrainY, GlobalPosition.Z);
        }
    }

    // ======== 炮塔旋转追踪 ========

    private void UpdateTurretRotation(float dt)
    {
        if (_turretNode == null || !_turretNode.Visible) return;

        Vector3? aimTarget = null;
        if (_attackUnitTarget != null && IsInstanceValid(_attackUnitTarget))
            aimTarget = _attackUnitTarget.GlobalPosition;
        else if (_attackBuildingTarget != null && IsInstanceValid(_attackBuildingTarget))
            aimTarget = _attackBuildingTarget.GlobalPosition;

        if (aimTarget.HasValue)
        {
            var dir = aimTarget.Value - GlobalPosition;
            dir.Y = 0;
            float targetAngle = Mathf.Atan2(dir.X, dir.Z);
            _turretAngle = Mathf.LerpAngle(_turretAngle, targetAngle, 8f * dt);
            _turretNode.Rotation = new Vector3(0, _turretAngle - _bodyAngle, 0);
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
        {
            _healthLabel.Visible = false;
        }
    }

    // ======== 敌方搜索 ========

    public Unit3D? FindNearestEnemyUnit() => FindNearestEnemyUnitInRange(float.MaxValue);

    public Unit3D? FindNearestEnemyUnitInRange(float range)
    {
        Unit3D? best = null;
        float bestDist = range * range;
        foreach (var u in _game.GetAllUnits())
        {
            if (u.TeamId == TeamId || u._isDead) continue;
            if (u.IsAirUnit && !CanAttackAir) continue;
            float d = GlobalPosition.DistanceSquaredTo(u.GlobalPosition);
            if (d < bestDist) { bestDist = d; best = u; }
        }
        return best;
    }

    public Building3D? FindNearestEnemyBuilding() => FindNearestEnemyBuildingInRange(float.MaxValue);

    public Building3D? FindNearestEnemyBuildingInRange(float range)
    {
        Building3D? best = null;
        float bestDist = range * range;
        foreach (var b in _game.GetAllBuildings())
        {
            if (b.TeamId == TeamId || b._isDead) continue;
            float d = GlobalPosition.DistanceSquaredTo(b.GlobalPosition);
            if (d < bestDist) { bestDist = d; best = b; }
        }
        return best;
    }

    // ======== 地形改造 ========

    public void CommandTerrainMod(TerrainModType modType, Vector3 target)
    {
        _terrainModType = modType;
        _terrainModTarget = target;
        _moveTarget = target;
        _hasMoveTarget = true;
    }

    private void ProcessTerrainMod(float dt)
    {
        if (!_hasMoveTarget)
        {
            // 到达目标，开始施工
            _terrainModTimer -= dt;
            if (_terrainModTimer <= 0)
            {
                ExecuteTerrainMod();
                _isConstructing = false;
            }
        }
    }

    private void ExecuteTerrainMod()
    {
        _terrain.WorldToGrid(_terrainModTarget.X, _terrainModTarget.Z, out int gx, out int gy);
        var cell = _terrain.GetCell(gx, gy);

        switch (_terrainModType)
        {
            case TerrainModType.Flatten:
                cell.Type = TerrainType.Grass;
                cell.Elevation = 1;
                break;
            case TerrainModType.Tunnel:
                cell.HasTunnel = true;
                break;
            case TerrainModType.Bridge:
                cell.HasBridge = true;
                break;
            case TerrainModType.UnderseaTunnel:
                cell.HasTunnel = true;
                cell.Type = TerrainType.DeepWater;
                break;
        }

        _terrain.ModifyCell(gx, gy, cell);
    }

    // ======== 工程车辅助 ========

    private void TryRepairNearby(float dt)
    {
        foreach (var u in _game.GetAllUnits())
        {
            if (u.TeamId != TeamId || u._isDead || u == this) continue;
            if (u.Health < u.MaxHealth && GlobalPosition.DistanceTo(u.GlobalPosition) < 8f)
            {
                u.Heal(25f * dt);
            }
        }
        foreach (var b in _game.GetAllBuildings())
        {
            if (b.TeamId != TeamId || b._isDead) continue;
            if (b.Health < b.MaxHealth && GlobalPosition.DistanceTo(b.GlobalPosition) < 8f)
            {
                b.RepairByEngineer(50f * dt);
            }
            // 对敌方建筑执行占领
            if (b.TeamId != TeamId && GlobalPosition.DistanceTo(b.GlobalPosition) < 3f)
            {
                b.CaptureTick(dt, TeamId);
            }
        }
    }

    // ======== 英雄技能 ========

    private void ProcessHeroSkill(float dt)
    {
        switch (_heroSkill)
        {
            case HeroSkill.HealAura:
                foreach (var u in _game.GetAllUnits())
                {
                    if (u.TeamId != TeamId || u._isDead) continue;
                    if (u != this && GlobalPosition.DistanceTo(u.GlobalPosition) < 8f)
                        u.Heal(20f * dt);
                }
                break;
            case HeroSkill.Shield:
                // 简化：被动减伤
                break;
        }
    }

    // ======== 间谍渗透 ========

    private void ProcessSpyInfiltrate(float dt)
    {
        if (_spyDisguiseTeam == -1)
        {
            foreach (var b in _game.GetAllBuildings())
            {
                if (b.TeamId != TeamId && GlobalPosition.DistanceTo(b.GlobalPosition) < 3f)
                {
                    _spyDisguiseTeam = b.TeamId;
                    _spyInfiltrateTimer = 4f;
                    break;
                }
            }
        }
        else
        {
            _spyInfiltrateTimer -= dt;
            if (_spyInfiltrateTimer <= 0)
            {
                // 停电5秒+偷$200
                _game.SpyInfiltrateEffect(_spyDisguiseTeam);
                _spyDisguiseTeam = -1;
            }
        }
    }

    // ======== 窃贼偷钱 ========

    private void ProcessThiefSteal(float dt)
    {
        _thiefStealCooldown -= dt;
        if (_thiefStealCooldown > 0) return;

        foreach (var b in _game.GetAllBuildings())
        {
            if (b.TeamId != TeamId && GlobalPosition.DistanceTo(b.GlobalPosition) < 3f)
            {
                int stolen = 100 + new Random().Next(50);
                _game.ThiefStealEffect(b.TeamId, stolen);
                _thiefStealCooldown = 8f;
                break;
            }
        }
    }

    // ======== 运输交互 ========

    private void ProcessEmbark(float dt)
    {
        if (_embarkTarget == null || !IsInstanceValid(_embarkTarget) || _embarkTarget._isDead)
        {
            _embarkTarget = null;
            return;
        }
        float dist = GlobalPosition.DistanceTo(_embarkTarget.GlobalPosition);
        if (dist < 2f)
        {
            _embarkTarget.EmbarkPassenger(this);
            _embarkTarget = null;
        }
        else
        {
            CommandMove(_embarkTarget.GlobalPosition);
        }
    }

    // ======== 能力名称 ========

    public static string AbilityName(UnitAbility a) => a switch
    {
        UnitAbility.ArmorPiercing => "穿甲弹",
        UnitAbility.DoubleShot => "双发",
        UnitAbility.Scatter => "散射",
        UnitAbility.ReactiveArmor => "反应装甲",
        UnitAbility.SelfRepair => "自修复",
        UnitAbility.SmokeScreen => "烟幕",
        UnitAbility.TurboEngine => "涡轮引擎",
        UnitAbility.ReconVision => "侦察视野",
        UnitAbility.BattleFrenzy => "战斗狂热",
        UnitAbility.Plunder => "掠夺",
        UnitAbility.Tenacity => "坚韧",
        _ => "未知",
    };

    public static string AbilityDesc(UnitAbility a) => a switch
    {
        UnitAbility.ArmorPiercing => "对重甲单位伤害+25%",
        UnitAbility.DoubleShot => "射速+40%",
        UnitAbility.Scatter => "攻击溅射3米",
        UnitAbility.ReactiveArmor => "受到伤害-20%",
        UnitAbility.SelfRepair => "脱战3秒后每秒恢复1%HP",
        UnitAbility.SmokeScreen => "20%概率闪避",
        UnitAbility.TurboEngine => "移动速度+20%",
        UnitAbility.ReconVision => "视野+50%",
        UnitAbility.BattleFrenzy => "近敌时攻速+20%",
        UnitAbility.Plunder => "击杀获得$10",
        UnitAbility.Tenacity => "低血量时防御+30%",
        _ => "",
    };
}
