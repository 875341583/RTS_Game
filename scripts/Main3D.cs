using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RTSGame;

/// <summary>
/// 3D 主游戏控制器 — Node3D + 透视相机（固定俯角）。
/// 移植自2D Main.cs，保留全部游戏逻辑（AI/生产/超武/输入/UI/经济/胜负）。
/// 新增：昼夜系统、灾害系统、3D相机控制。
/// </summary>
public partial class Main3D : Node3D
{
    // ======== 3D 节点 ========
    private Camera3D _camera;
    private DirectionalLight3D _sunLight;
    private DirectionalLight3D _moonLight;
    private WorldEnvironment _worldEnv;
    private Node3D _unitsNode;
    private Node3D _buildingsNode;
    private Node3D _resourcesNode;
    private Node3D _effectsNode;
    private NavigationRegion3D _navRegion;

    // 地形
    private TerrainGrid3D _terrain;

    // ======== 游戏状态 ========
    public enum Difficulty { Easy, Normal, Hard, Brutal }
    private Difficulty _difficulty = Difficulty.Normal;

    private const int AiTeamCount = 7;
    private const int TotalTeamCount = 8;
    private const int PlayerTeamId = 0;

    private readonly int[] _money = new int[TotalTeamCount] { 3000, 3500, 3500, 3500, 3500, 3500, 3500, 3500 };

    // 选中
    private readonly List<GodotObject> _selected = new();
    private bool _isDragging;
    private Vector2 _dragStartScreen;

    // 编队
    private readonly Dictionary<int, List<Unit3D>> _squads = new();

    // 建筑放置
    private Building3D.BuildingType _placementType;
    private bool _isPlacing;
    private int _mapSeed = 42;

    // AI
    private readonly Dictionary<int, float> _aiThinkTimers = new();
    private readonly Dictionary<int, Building3D> _bases = new();
    private int _activeAiCount = 4;

    // 超武
    private readonly Dictionary<int, float> _aiNukeCooldowns = new();
    private readonly Dictionary<int, float> _aiLightningCooldowns = new();
    private readonly Dictionary<int, float> _aiMissileCooldowns = new();
    private float _playerNukeCooldown;
    private float _playerLightningCooldown;
    private float _playerMissileCooldown;

    private const float NukeCooldown = 300f;
    private const float LightningCooldown = 240f;
    private const float MissileCooldown = 180f;

    // 超武特效
    private struct NukeEffect { public Vector3 Pos; public float Age; public float Lifetime; }
    private struct LightningEffect { public Vector3 Pos; public int TeamId; public float Age; public float Lifetime; public float DamageTimer; }
    private readonly List<NukeEffect> _nukeEffects = new();
    private readonly List<LightningEffect> _lightningEffects = new();

    // 建筑受击冷却
    private readonly Dictionary<ulong, float> _buildingAlertCooldown = new();

    // UI
    private CanvasLayer _uiLayer;
    private Label _uiLabel;
    private Label _hintLabel;
    private Label _moneyLabel;
    private Label _powerLabel;
    private VBoxContainer _toastContainer;
    private readonly List<(Label label, float age, float life)> _toasts = new();
    private Label _startOverlay;
    private float _startOverlayAge;
    private Label _gameOverLabel;

    // 小地图
    private SubViewport _minimapViewport;
    private Camera3D _minimapCam;
    private TextureRect _minimapRect;

    // 建造面板
    private Control _buildPanel;

    // 生产进度面板
    private VBoxContainer _prodPanel;
    private readonly List<Control> _prodBars = new();

    // 昼夜系统
    private float _timeOfDay = 8f; // 0-24小时
    private const float DayLength = 180f; // 一天=180秒（白天更充裕，夜间不至于太长截图看不见）
    private float _sunRotation;

    // 灾害系统
    private float _disasterTimer = 30f; // 30秒后第一次灾害（加速验证）
    private string _currentDisaster = "";
    private float _disasterDuration;
    private float _disasterAge;

    // 截图目录
    private static readonly string ShotDir = OS.GetUserDataDir();

    // 造价表
    private static readonly Dictionary<Building3D.BuildingType, int> BuildingCosts = new()
    {
        { Building3D.BuildingType.Base, 3000 },
        { Building3D.BuildingType.PowerPlant, 400 },
        { Building3D.BuildingType.Barracks, 500 },
        { Building3D.BuildingType.WarFactory, 800 },
        { Building3D.BuildingType.TechCenter, 1500 },
        { Building3D.BuildingType.Turret, 300 },
        { Building3D.BuildingType.AntiAirTurret, 400 },
        { Building3D.BuildingType.RepairPad, 600 },
        { Building3D.BuildingType.Airfield, 700 },
        { Building3D.BuildingType.Shipyard, 800 },
        { Building3D.BuildingType.NukeSilo, 2500 },
        { Building3D.BuildingType.LightningTower, 2000 },
        { Building3D.BuildingType.MissileSilo, 1500 },
    };

    private static readonly Dictionary<UnitType, int> UnitCosts = new()
    {
        { UnitType.LightTank, 200 },
        { UnitType.HeavyTank, 500 },
        { UnitType.Artillery, 400 },
        { UnitType.RocketLauncher, 450 },
        { UnitType.MissileTank, 600 },
        { UnitType.AntiAir, 300 },
        { UnitType.Harvester, 500 },
        { UnitType.Infantry, 100 },
        { UnitType.Engineer, 300 },
        { UnitType.Sapper, 150 },
        { UnitType.ChiefEngineer, 400 },
        { UnitType.Grenadier, 200 },
        { UnitType.Sniper, 250 },
        { UnitType.FlameInfantry, 180 },
        { UnitType.Transport, 400 },
        { UnitType.Hero, 600 },
        { UnitType.Spy, 500 },
        { UnitType.Thief, 300 },
        { UnitType.Fighter, 500 },
        { UnitType.Helicopter, 600 },
        { UnitType.RocketInfantry, 350 },
        { UnitType.Bomber, 800 },
        { UnitType.Scout, 300 },
        { UnitType.TransportHeli, 600 },
        { UnitType.Destroyer, 500 },
        { UnitType.Submarine, 600 },
        { UnitType.AircraftCarrier, 1000 },
        { UnitType.LandingCraft, 400 },
    };

    // ======== 初始化 ========

    public override void _Ready()
    {
        // 解析难度
        var args = OS.GetCmdlineArgs();
        foreach (var a in args)
        {
            if (a.StartsWith("--difficulty="))
            {
                Enum.TryParse(a.Substring(13), true, out _difficulty);
            }
            if (a.StartsWith("--seed="))
            {
                int.TryParse(a.Substring(7), out _mapSeed);
            }
        }

        ApplyDifficultyConfig();

        // 初始化共享特效材质
        InitSharedFxMaterials();

        // 音频管理器
        var audioMgr = new AudioManager();
        AddChild(audioMgr);
        // 延迟1帧启动BGM（等AudioManager._Ready完成）
        CallDeferred(nameof(StartGameBgm));

        // 创建3D场景结构
        Setup3DScene();

        // 生成地形
        _terrain = new TerrainGrid3D(
            GetNode<Node3D>(".") ?? this,
            _navRegion);
        _terrain.GenerateMap(_mapSeed);
        _terrain.Build3DTerrainMesh();

        // 生成玩家和AI基地
        SpawnInitialBuildings();

        // 创建UI
        SetupUI();

        // 初始化AI保护期
        ApplyAiGracePeriod();

        GD.Print($"[Main3D] Game started — Difficulty: {_difficulty}, Seed: {_mapSeed}");
    }

    private void StartGameBgm()
    {
        AudioManager.Instance?.StartBgm();
    }

    private void Setup3DScene()
    {
        // 透视相机（固定俯角约55度）
        _camera = new Camera3D();
        _camera.Projection = Camera3D.ProjectionType.Perspective;
        _camera.Fov = 50f;
        _camera.Near = 0.5f;
        _camera.Far = 300f; // 降低远裁剪面减少渲染量
        // 相机位置：高处俯视，固定角度
        float camHeight = 60f;
        float camDist = 42f;
        _camera.Position = new Vector3(
            TerrainGrid3D.MapWorldSize * 0.5f,
            camHeight,
            TerrainGrid3D.MapWorldSize * 0.5f + camDist);
        AddChild(_camera);
        _camera.LookAt(new Vector3(TerrainGrid3D.MapWorldSize * 0.5f, 0, TerrainGrid3D.MapWorldSize * 0.5f));
        _camera.MakeCurrent();

        // 环境光
        var env = new Godot.Environment();
        env.BackgroundMode = Godot.Environment.BGMode.Sky;
        var sky = new ProceduralSkyMaterial();
        sky.SkyTopColor = new Color(0.25f, 0.5f, 0.85f);
        sky.SkyHorizonColor = new Color(0.55f, 0.65f, 0.85f);
        sky.GroundBottomColor = new Color(0.15f, 0.18f, 0.2f);
        sky.GroundHorizonColor = new Color(0.35f, 0.35f, 0.35f);
        sky.SunAngleMax = 80f;
        sky.SunCurve = 0.15f;
        var skyResource = new Sky { SkyMaterial = sky };
        env.Sky = skyResource;
        env.BackgroundEnergyMultiplier = 1.0f;
        env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
        env.AmbientLightColor = new Color(0.6f, 0.65f, 0.7f);
        env.AmbientLightEnergy = 0.6f;

        // 色调映射：Filmic 提升中暗部细节
        env.TonemapMode = Godot.Environment.ToneMapper.Filmic;
        env.TonemapExposure = 1.0f;

        // 辉光：保持开启但降低强度（完全禁用会导致画面偏暗）
        env.GlowEnabled = true;
        env.GlowIntensity = 0.3f;
        env.GlowStrength = 0.5f;
        env.GlowBloom = 0.05f;
        env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight;
        env.GlowHdrThreshold = 1.0f;

        // 颜色校正：保持开启，避免画面偏蓝
        env.AdjustmentEnabled = true;
        env.AdjustmentBrightness = 1.0f;
        env.AdjustmentContrast = 1.08f;
        env.AdjustmentSaturation = 1.15f;

        // 雾效：禁用以节省全屏后期处理开销
        env.FogEnabled = false;

        _worldEnv = new WorldEnvironment { Environment = env };
        AddChild(_worldEnv);

        // 太阳光 — 保留阴影（Compatibility renderer下禁用阴影可能导致光照异常），但降低距离
        _sunLight = new DirectionalLight3D();
        _sunLight.RotationDegrees = new Vector3(-55, 30, 0);
        _sunLight.LightEnergy = 1.2f;
        _sunLight.LightColor = new Color(1.0f, 0.95f, 0.85f); // 暖色太阳
        _sunLight.ShadowEnabled = true;
        _sunLight.ShadowOpacity = 0.5f;
        _sunLight.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
        _sunLight.DirectionalShadowMaxDistance = 40f; // 进一步降低阴影距离
        _sunLight.ShadowNormalBias = 1.0f;
        _sunLight.ShadowBias = 0.02f;
        AddChild(_sunLight);

        // 月光（夜间补光）— 不开阴影以节省GPU开销
        _moonLight = new DirectionalLight3D();
        _moonLight.RotationDegrees = new Vector3(-40, 210, 0);
        _moonLight.LightEnergy = 0f;
        _moonLight.LightColor = new Color(0.6f, 0.7f, 0.9f); // 冷色月光
        _moonLight.ShadowEnabled = false; // 禁用月光阴影以节省性能
        AddChild(_moonLight);

        // 场景容器
        _navRegion = new NavigationRegion3D { Name = "Navigation" };
        AddChild(_navRegion);

        _unitsNode = new Node3D { Name = "Units" };
        AddChild(_unitsNode);
        _buildingsNode = new Node3D { Name = "Buildings" };
        AddChild(_buildingsNode);
        _resourcesNode = new Node3D { Name = "Resources" };
        AddChild(_resourcesNode);
        _effectsNode = new Node3D { Name = "Effects" };
        AddChild(_effectsNode);
    }

    // ======== 难度配置 ========

    private void ApplyDifficultyConfig()
    {
        (_activeAiCount, float grace, int aiInterval) = _difficulty switch
        {
            Difficulty.Easy => (4, 15f, 6),
            Difficulty.Normal => (5, 30f, 5),
            Difficulty.Hard => (6, 15f, 4),
            Difficulty.Brutal => (7, 0f, 3),
            _ => (4, 30f, 6),
        };

        // 初始化AI思考计时器
        for (int i = 1; i <= AiTeamCount; i++)
        {
            _aiThinkTimers[i] = aiInterval;
            _aiNukeCooldowns[i] = NukeCooldown;
            _aiLightningCooldowns[i] = LightningCooldown;
            _aiMissileCooldowns[i] = MissileCooldown;
        }

        Unit3D.AiGraceRemaining = grace;
    }

    private void ApplyAiGracePeriod()
    {
        // 已在ApplyDifficultyConfig中设置
    }

    // ======== 初始建筑生成 ========

    private void SpawnInitialBuildings()
    {
        // 玩家基地（地图一角）
        var playerBasePos = new Vector3(
            TerrainGrid3D.CellSize * 3,
            0,
            TerrainGrid3D.CellSize * 3);
        CreateBuilding(Building3D.BuildingType.Base, PlayerTeamId, playerBasePos);
        // 玩家初始电站和兵营
        CreateBuilding(Building3D.BuildingType.PowerPlant, PlayerTeamId, playerBasePos + new Vector3(0, 0, TerrainGrid3D.CellSize * 2));
        CreateBuilding(Building3D.BuildingType.Barracks, PlayerTeamId, playerBasePos + new Vector3(TerrainGrid3D.CellSize * 2, 0, 0));

        // 玩家初始矿车
        SpawnUnit(UnitType.Harvester, PlayerTeamId, playerBasePos + new Vector3(0, 0, -TerrainGrid3D.CellSize * 2));
        // 玩家初始作战单位
        SpawnUnit(UnitType.LightTank, PlayerTeamId, playerBasePos + new Vector3(TerrainGrid3D.CellSize, 0, -TerrainGrid3D.CellSize));
        SpawnUnit(UnitType.LightTank, PlayerTeamId, playerBasePos + new Vector3(-TerrainGrid3D.CellSize, 0, -TerrainGrid3D.CellSize));
        SpawnUnit(UnitType.Infantry, PlayerTeamId, playerBasePos + new Vector3(TerrainGrid3D.CellSize * 2, 0, -TerrainGrid3D.CellSize));

        // AI基地（围成半圆分布）
        for (int i = 1; i <= AiTeamCount; i++)
        {
            float angle = Mathf.Pi + (i - 1) * (Mathf.Pi / (AiTeamCount - 1));
            float dist = TerrainGrid3D.MapWorldSize * 0.4f;
            var aiPos = new Vector3(
                TerrainGrid3D.MapWorldSize * 0.5f + Mathf.Cos(angle) * dist,
                0,
                TerrainGrid3D.MapWorldSize * 0.5f + Mathf.Sin(angle) * dist);
            aiPos.X = Mathf.Clamp(aiPos.X, TerrainGrid3D.CellSize * 3, TerrainGrid3D.MapWorldSize - TerrainGrid3D.CellSize * 3);
            aiPos.Z = Mathf.Clamp(aiPos.Z, TerrainGrid3D.CellSize * 3, TerrainGrid3D.MapWorldSize - TerrainGrid3D.CellSize * 3);

            CreateBuilding(Building3D.BuildingType.Base, i, aiPos);
            CreateBuilding(Building3D.BuildingType.PowerPlant, i, aiPos + new Vector3(0, 0, TerrainGrid3D.CellSize * 2));
            CreateBuilding(Building3D.BuildingType.Barracks, i, aiPos + new Vector3(TerrainGrid3D.CellSize * 2, 0, 0));
            CreateBuilding(Building3D.BuildingType.WarFactory, i, aiPos + new Vector3(-TerrainGrid3D.CellSize * 2, 0, 0));

            // AI初始矿车
            SpawnUnit(UnitType.Harvester, i, aiPos + new Vector3(0, 0, -TerrainGrid3D.CellSize * 2));

            // AI初始战斗单位（减少初始数量以降低CPU负担，AI会自行生产）
            SpawnUnit(UnitType.LightTank, i, aiPos + new Vector3(TerrainGrid3D.CellSize, 0, -TerrainGrid3D.CellSize));
        }
    }

    // ======== 建筑创建 ========

    public Building3D CreateBuilding(Building3D.BuildingType type, int teamId, Vector3 pos)
    {
        var building = new Building3D();
        building.Type = type;
        building.TeamId = teamId;
        building.Position = pos;
        building.Initialize(_terrain, this);
        _buildingsNode.AddChild(building);
        MarkBuildingsCacheDirty();

        if (type == Building3D.BuildingType.Base)
            _bases[teamId] = building;

        // 建筑生产完成事件
        building.UnitProduced += OnUnitProduced;

        return building;
    }

    // ======== 单位创建 ========

    public Unit3D SpawnUnit(UnitType type, int teamId, Vector3 pos)
    {
        var unit = new Unit3D();
        unit.TeamId = teamId;
        unit.Position = pos;
        unit.Initialize(_terrain, this);
        _unitsNode.AddChild(unit);
        MarkUnitsCacheDirty();
        unit.InitAsType(type);

        // AI单位自动战斗
        if (teamId != PlayerTeamId)
        {
            unit.AutoAI = true;
        }

        return unit;
    }

    // ======== 生产完成回调 ========

    private void OnUnitProduced(Building3D.ProductionType prodType, Building3D producer)
    {
        var unitType = ProductionTypeToUnitType(prodType);
        if (unitType == UnitType.Default) return;

        // 出场位置：建筑后方
        var exitPos = producer.GlobalPosition + new Vector3(0, 0, TerrainGrid3D.CellSize * 1.5f);
        exitPos = new Vector3(exitPos.X, _terrain.GetHeightAtWorld(exitPos.X, exitPos.Z), exitPos.Z);

        var unit = SpawnUnit(unitType, producer.TeamId, exitPos);

        // 单位出厂特效
        SpawnBuildEffect(exitPos, 1.5f);

        // 音效：单位出厂（仅玩家方）
        if (producer.TeamId == PlayerTeamId)
            AudioManager.Instance?.PlaySfxForce(AudioManager.Sfx.UiUnitReady);

        // 集结点
        if (producer.RallyPoint.HasValue)
        {
            unit.CommandMove(producer.RallyPoint.Value);
        }
    }

    public static UnitType ProductionTypeToUnitType(Building3D.ProductionType pt) => pt switch
    {
        Building3D.ProductionType.Infantry => UnitType.Infantry,
        Building3D.ProductionType.Engineer => UnitType.Engineer,
        Building3D.ProductionType.Sapper => UnitType.Sapper,
        Building3D.ProductionType.ChiefEngineer => UnitType.ChiefEngineer,
        Building3D.ProductionType.Grenadier => UnitType.Grenadier,
        Building3D.ProductionType.Sniper => UnitType.Sniper,
        Building3D.ProductionType.FlameInfantry => UnitType.FlameInfantry,
        Building3D.ProductionType.LightTank => UnitType.LightTank,
        Building3D.ProductionType.HeavyTank => UnitType.HeavyTank,
        Building3D.ProductionType.Artillery => UnitType.Artillery,
        Building3D.ProductionType.RocketLauncher => UnitType.RocketLauncher,
        Building3D.ProductionType.MissileTank => UnitType.MissileTank,
        Building3D.ProductionType.AntiAir => UnitType.AntiAir,
        Building3D.ProductionType.Harvester => UnitType.Harvester,
        Building3D.ProductionType.Transport => UnitType.Transport,
        Building3D.ProductionType.Hero => UnitType.Hero,
        Building3D.ProductionType.Spy => UnitType.Spy,
        Building3D.ProductionType.Thief => UnitType.Thief,
        Building3D.ProductionType.Fighter => UnitType.Fighter,
        Building3D.ProductionType.Helicopter => UnitType.Helicopter,
        Building3D.ProductionType.RocketInfantry => UnitType.RocketInfantry,
        Building3D.ProductionType.Bomber => UnitType.Bomber,
        Building3D.ProductionType.Scout => UnitType.Scout,
        Building3D.ProductionType.TransportHeli => UnitType.TransportHeli,
        Building3D.ProductionType.Destroyer => UnitType.Destroyer,
        Building3D.ProductionType.Submarine => UnitType.Submarine,
        Building3D.ProductionType.AircraftCarrier => UnitType.AircraftCarrier,
        Building3D.ProductionType.LandingCraft => UnitType.LandingCraft,
        _ => UnitType.Default,
    };

    // ======== 访问器 ========

    public Node3D GetUnitsNode() => _unitsNode;
    public Node3D GetBuildingsNode() => _buildingsNode;
    // ======== 缓存系统（性能优化）========

    /// <summary>所有存活单位的缓存列表，每帧更新一次而非每次调用都遍历</summary>
    private List<Unit3D> _cachedUnits = new(128);
    /// <summary>所有存活建筑的缓存列表</summary>
    private List<Building3D> _cachedBuildings = new(64);
    /// <summary>缓存是否需要刷新</summary>
    private bool _unitsCacheDirty = true;
    private bool _buildingsCacheDirty = true;
    /// <summary>上次刷新缓存的时间</summary>
    private float _cacheTimer;

    /// <summary>标记单位缓存需要刷新</summary>
    public void MarkUnitsCacheDirty() => _unitsCacheDirty = true;
    /// <summary>标记建筑缓存需要刷新</summary>
    public void MarkBuildingsCacheDirty() => _buildingsCacheDirty = true;

    public List<Unit3D> GetAllUnits()
    {
        if (_unitsCacheDirty)
        {
            _cachedUnits.Clear();
            foreach (var child in _unitsNode.GetChildren())
                if (child is Unit3D u && !u._isDead) _cachedUnits.Add(u);
            _unitsCacheDirty = false;
        }
        return _cachedUnits;
    }
    public List<Building3D> GetAllBuildings()
    {
        if (_buildingsCacheDirty)
        {
            _cachedBuildings.Clear();
            foreach (var child in _buildingsNode.GetChildren())
                if (child is Building3D b && !b._isDead) _cachedBuildings.Add(b);
            _buildingsCacheDirty = false;
        }
        return _cachedBuildings;
    }

    // ======== 经济系统 ========

    public bool SpendMoney(int teamId, int amount)
    {
        if (_money[teamId] < amount) return false;
        _money[teamId] -= amount;
        return true;
    }

    public void AddResourceForTeam(int teamId, int amount)
    {
        _money[teamId] += amount;
    }

    public int GetMoney(int teamId) => _money[teamId];

    public void AwardPlunderGold(int teamId, int amount) => AddResourceForTeam(teamId, amount);

    public void SpyInfiltrateEffect(int targetTeamId)
    {
        // 停电5秒效果（简化：扣钱）
        SpendMoney(targetTeamId, Math.Min(_money[targetTeamId], 200));
        AddResourceForTeam(PlayerTeamId, 200);
        ShowToast($"间谍渗透成功！窃取$200");
    }

    public void ThiefStealEffect(int targetTeamId, int amount)
    {
        SpendMoney(targetTeamId, Math.Min(_money[targetTeamId], amount));
        AddResourceForTeam(PlayerTeamId, amount);
        ShowToast($"窃贼偷取${amount}！");
    }

    // ======== 电力 ========

    public int GetTeamPower(int teamId)
    {
        int provided = 0, consumed = 0;
        foreach (var b in GetAllBuildings())
        {
            if (b.TeamId != teamId || b._isDead) continue;
            provided += b.PowerProvided;
            consumed += b.PowerConsumed;
        }
        return provided - consumed;
    }

    public bool HasBuilding(int teamId, Building3D.BuildingType type)
    {
        foreach (var b in GetAllBuildings())
            if (b.TeamId == teamId && b.Type == type && !b._isDead) return true;
        return false;
    }

    // ======== 单位/建筑死亡回调 ========

    public void OnUnitDied(Unit3D unit)
    {
        // 大爆炸特效
        SpawnExplosion(unit.GlobalPosition, 2f);
        // 从选中和编队中移除
        _selected.Remove(unit);
        foreach (var squad in _squads.Values)
            squad.Remove(unit);
        MarkUnitsCacheDirty();
    }

    public void OnBuildingDied(Building3D building)
    {
        SpawnExplosion(building.GlobalPosition, 5f);
        SpawnBuildingDebris(building.GlobalPosition, 3f);
        _selected.Remove(building);
        if (building.Type == Building3D.BuildingType.Base)
        {
            _bases.Remove(building.TeamId);
            ShowToast($"阵营{building.TeamId}的基地被摧毁！");
        }
        MarkBuildingsCacheDirty();
    }

    public void OnBuildingAttacked(Building3D b)
    {
        if (b.TeamId != PlayerTeamId) return;
        ulong key = b.GetInstanceId();
        if (_buildingAlertCooldown.TryGetValue(key, out var cd) && cd > 0) return;
        _buildingAlertCooldown[key] = 3f;

        // 附近AutoDefend单位回防
        foreach (var u in GetAllUnits())
        {
            if (u.TeamId != PlayerTeamId || u._isDead || u.AutoAI) continue;
            if (!u.AutoDefend) continue;
            if (u.GlobalPosition.DistanceTo(b.GlobalPosition) < 40f)
            {
                u.CommandAttackMove(b.GlobalPosition);
            }
        }
        ShowToast("建筑受到攻击！单位回防中");
    }

    // ======== 超武系统 ========

    public void ApplyNuke(Vector3 pos, int firingTeamId)
    {
        foreach (var u in GetAllUnits())
        {
            if (u.TeamId == firingTeamId || u._isDead) continue;
            if (u.GlobalPosition.DistanceTo(pos) < 16f)
                u.TakeDamage(600);
        }
        foreach (var b in GetAllBuildings())
        {
            if (b.TeamId == firingTeamId || b._isDead) continue;
            if (b.GlobalPosition.DistanceTo(pos) < 16f)
                b.TakeDamage(600);
        }
        _nukeEffects.Add(new NukeEffect { Pos = pos, Age = 0, Lifetime = 4f });
        SpawnExplosion(pos, 15f);
        AudioManager.Instance?.PlaySfxForce(AudioManager.Sfx.Nuke);
        ShowToast("核弹打击！");
    }

    public void ApplyLightning(Vector3 pos, int firingTeamId)
    {
        _lightningEffects.Add(new LightningEffect
        {
            Pos = pos,
            TeamId = firingTeamId,
            Age = 0,
            Lifetime = 5f,
            DamageTimer = 0,
        });
        AudioManager.Instance?.PlaySfxForce(AudioManager.Sfx.Lightning);
        ShowToast("闪电风暴来袭！");
    }

    public void ApplyCruiseMissile(Vector3 pos, int firingTeamId)
    {
        foreach (var u in GetAllUnits())
        {
            if (u.TeamId == firingTeamId || u._isDead) continue;
            float d = u.GlobalPosition.DistanceTo(pos);
            if (d < 12f)
            {
                float dmg = 300 * (1 - d / 12f);
                u.TakeDamage(Math.Max(0, dmg));
            }
        }
        foreach (var b in GetAllBuildings())
        {
            if (b.TeamId == firingTeamId || b._isDead) continue;
            float d = b.GlobalPosition.DistanceTo(pos);
            if (d < 12f)
            {
                float dmg = 300 * (1 - d / 12f) * 0.8f;
                b.TakeDamage(Math.Max(0, dmg));
            }
        }
        SpawnExplosion(pos, 8f);
        ShowToast("巡航导弹命中！");
    }

    // ======== 特效 ========

    /// <summary>活跃特效列表，用于_Process中逐帧更新动画</summary>
    private struct FxParticle
    {
        public Node3D Node;
        public float Age;
        public float Lifetime;
        public Vector3 StartScale;
        public Vector3 EndScale;
        public Color StartColor;
        public Color EndColor;
        public float StartEmission;
        public float EndEmission;
        public bool FadeOut;
        public Vector3 Velocity;   // 用于烟雾/碎片飘移
        public FxType Type;
    }

    private enum FxType { ExplosionCore, ExplosionShell, Smoke, Spark, Tracer, MuzzleFlash, Shockwave }

    private readonly List<FxParticle> _activeFx = new();
    private const int MaxActiveFx = 200;

    // ======== 特效材质复用池（避免每次new StandardMaterial3D）========
    private static StandardMaterial3D? _sharedMuzzleMat;
    private static StandardMaterial3D? _sharedTracerMat;
    private static StandardMaterial3D? _sharedFireballMat;
    private static StandardMaterial3D? _sharedShockwaveMat;
    private static StandardMaterial3D? _sharedSmokeMat;
    private static StandardMaterial3D? _sharedSparkMat;
    private static StandardMaterial3D? _sharedDustMat;
    private static StandardMaterial3D? _sharedDebrisMat;
    private static StandardMaterial3D? _sharedBuildRingMat;
    private static StandardMaterial3D? _sharedBuildBeamMat;

    /// <summary>延迟初始化共享材质（需要在渲染线程内创建）</summary>
    private void InitSharedFxMaterials()
    {
        if (_sharedMuzzleMat != null) return;
        _sharedMuzzleMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.9f, 0.5f),
            Emission = new Color(1f, 0.7f, 0.2f),
            EmissionEnergyMultiplier = 5f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _sharedTracerMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.95f, 0.6f, 0.8f),
            Emission = new Color(1f, 0.8f, 0.3f),
            EmissionEnergyMultiplier = 3f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _sharedFireballMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.6f, 0.15f, 1f),
            Emission = new Color(1f, 0.4f, 0.05f),
            EmissionEnergyMultiplier = 5f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _sharedShockwaveMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.8f, 0.3f, 0.6f),
            Emission = new Color(1f, 0.6f, 0.15f),
            EmissionEnergyMultiplier = 2f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _sharedSmokeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.3f, 0.25f, 0.2f, 0.7f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _sharedSparkMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.7f, 0.2f),
            Emission = new Color(1f, 0.5f, 0.1f),
            EmissionEnergyMultiplier = 4f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _sharedDustMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.48f, 0.35f, 0.5f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _sharedDebrisMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.4f, 0.35f, 0.3f),
            Roughness = 0.9f,
        };
        _sharedBuildRingMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.3f, 1f, 0.5f, 0.6f),
            Emission = new Color(0.2f, 0.8f, 0.4f),
            EmissionEnergyMultiplier = 3f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _sharedBuildBeamMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.3f, 1f, 0.5f, 0.3f),
            Emission = new Color(0.2f, 0.8f, 0.4f),
            EmissionEnergyMultiplier = 2f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        GD.Print("[Main3D] Shared FX materials initialized");
    }

    /// <summary>
    /// 增强版炮口闪光：OmniLight3D短暂闪烁 + 扩散发光球 + 方向喷射
    /// </summary>
    public void SpawnMuzzleFlash(Vector3 from, Vector3 to)
    {
        if (_activeFx.Count >= MaxActiveFx) return;

        // 音效：炮口闪光
        AudioManager.Instance?.PlaySfx(AudioManager.Sfx.Muzzle);

        var dir = (to - from).Normalized();
        var flashPos = from + dir * 2f + new Vector3(0, 0.5f, 0);

        // 1) 短暂点光源 — 模拟枪炮火光照明
        var light = new OmniLight3D
        {
            LightColor = new Color(1f, 0.85f, 0.4f),
            LightEnergy = 4f,
            OmniRange = 8f,
            OmniAttenuation = 1.5f,
            Position = flashPos,
        };
        _effectsNode.AddChild(light);

        var lightFx = new FxParticle
        {
            Node = light,
            Age = 0,
            Lifetime = 0.12f,
            StartScale = Vector3.One,
            EndScale = Vector3.One,
            StartColor = new Color(1f, 0.85f, 0.4f),
            EndColor = new Color(1f, 0.85f, 0.4f, 0f),
            StartEmission = 4f,
            EndEmission = 0f,
            FadeOut = true,
            Type = FxType.MuzzleFlash,
        };
        _activeFx.Add(lightFx);

        // 2) 扩散发光球 — 火球本体
        var flash = new MeshInstance3D();
        var sphere = new SphereMesh { Radius = 0.3f, Height = 0.6f };
        flash.Mesh = sphere;
        flash.MaterialOverride = _sharedMuzzleMat;
        flash.Position = flashPos;
        _effectsNode.AddChild(flash);

        var flashFx = new FxParticle
        {
            Node = flash,
            Age = 0,
            Lifetime = 0.15f,
            StartScale = new Vector3(0.5f, 0.5f, 0.5f),
            EndScale = new Vector3(2.5f, 2.5f, 2.5f),
            StartColor = new Color(1f, 0.9f, 0.5f, 1f),
            EndColor = new Color(1f, 0.3f, 0.05f, 0f),
            StartEmission = 5f,
            EndEmission = 0f,
            FadeOut = true,
            Type = FxType.MuzzleFlash,
        };
        _activeFx.Add(flashFx);

        // 3) 炮弹/子弹轨迹线 — 从枪口到目标的快速光迹
        SpawnTracer(from, to);
    }

    /// <summary>生成快速轨迹线：一条从from到to的发光圆柱体，0.08秒消失</summary>
    private void SpawnTracer(Vector3 from, Vector3 to)
    {
        if (_activeFx.Count >= MaxActiveFx) return;

        var mid = (from + to) * 0.5f + new Vector3(0, 0.5f, 0);
        var diff = to - from;
        var dist = diff.Length();
        if (dist < 0.1f) return;

        var tracer = new MeshInstance3D();
        var cyl = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = dist };
        tracer.Mesh = cyl;
        tracer.MaterialOverride = _sharedTracerMat;
        tracer.Position = mid;
        // 旋转圆柱体使其朝向射击方向（CylinderMesh默认沿Y轴，需要转到dir方向）
        var dir = diff.Normalized();
        // 构造从Y轴(0,1,0)到dir的旋转
        var yAxis = new Vector3(0, 1, 0);
        var rotAxis = yAxis.Cross(dir);
        var rotAngle = MathF.Acos(Mathf.Clamp(yAxis.Dot(dir), -1f, 1f));
        if (rotAxis.Length() > 0.001f)
        {
            rotAxis = rotAxis.Normalized();
            tracer.Transform = new Transform3D(
                new Basis(rotAxis, rotAngle),
                mid);
        }
        _effectsNode.AddChild(tracer);

        var tracerFx = new FxParticle
        {
            Node = tracer,
            Age = 0,
            Lifetime = 0.08f,
            StartScale = Vector3.One,
            EndScale = new Vector3(1f, 1f, 1f),
            StartColor = new Color(1f, 0.95f, 0.6f, 0.8f),
            EndColor = new Color(1f, 0.95f, 0.6f, 0f),
            StartEmission = 3f,
            EndEmission = 0f,
            FadeOut = true,
            Type = FxType.Tracer,
        };
        _activeFx.Add(tracerFx);
    }

    /// <summary>
    /// 增强版爆炸：中心火球(扩散+消散) + 冲击波环(扁平扩散) + 点光源(强光闪烁) + 烟雾(上升飘移) + 火花碎片
    /// </summary>
    public void SpawnExplosion(Vector3 pos, float scale)
    {
        // 音效：爆炸（大爆炸用BigExplosion，小爆炸用Explosion）
        if (scale > 2f)
            AudioManager.Instance?.PlaySfxForce(AudioManager.Sfx.BigExplosion);
        else
            AudioManager.Instance?.PlaySfx(AudioManager.Sfx.Explosion);

        var center = pos + new Vector3(0, scale * 0.3f, 0);

        // 1) 爆炸点光源 — 强光短暂照明周围
        {
            var light = new OmniLight3D
            {
                LightColor = new Color(1f, 0.5f, 0.15f),
                LightEnergy = 6f * Math.Min(scale, 5f),
                OmniRange = scale * 3f,
                OmniAttenuation = 1.2f,
                Position = center,
            };
            _effectsNode.AddChild(light);
            _activeFx.Add(new FxParticle
            {
                Node = light,
                Age = 0,
                Lifetime = 0.4f,
                StartScale = Vector3.One,
                EndScale = Vector3.One,
                StartColor = new Color(1f, 0.5f, 0.15f),
                EndColor = new Color(1f, 0.5f, 0.15f, 0f),
                StartEmission = 6f * Math.Min(scale, 5f),
                EndEmission = 0f,
                FadeOut = true,
                Type = FxType.ExplosionCore,
            });
        }

        // 2) 中心火球 — 初始小，快速膨胀，颜色从白热→橙红→暗烟
        {
            var fireball = new MeshInstance3D();
            var sph = new SphereMesh { Radius = scale * 0.3f, Height = scale * 0.6f };
            fireball.Mesh = sph;
            fireball.MaterialOverride = _sharedFireballMat;
            fireball.Position = center;
            _effectsNode.AddChild(fireball);
            _activeFx.Add(new FxParticle
            {
                Node = fireball,
                Age = 0,
                Lifetime = 0.5f,
                StartScale = new Vector3(0.3f, 0.3f, 0.3f),
                EndScale = new Vector3(1f, 1f, 1f),
                StartColor = new Color(1f, 0.9f, 0.5f, 1f),
                EndColor = new Color(0.4f, 0.15f, 0.05f, 0f),
                StartEmission = 5f,
                EndEmission = 0.5f,
                FadeOut = true,
                Type = FxType.ExplosionCore,
            });
        }

        // 3) 冲击波环 — 扁平圆盘水平扩散
        if (scale >= 1.5f && _activeFx.Count < MaxActiveFx)
        {
            var ring = new MeshInstance3D();
            var cyl = new CylinderMesh { TopRadius = scale * 0.2f, BottomRadius = scale * 0.2f, Height = 0.08f };
            ring.Mesh = cyl;
            ring.MaterialOverride = _sharedShockwaveMat;
            ring.Position = pos + new Vector3(0, 0.1f, 0);
            _effectsNode.AddChild(ring);
            _activeFx.Add(new FxParticle
            {
                Node = ring,
                Age = 0,
                Lifetime = 0.35f,
                StartScale = new Vector3(0.3f, 1f, 0.3f),
                EndScale = new Vector3(scale * 1.5f, 1f, scale * 1.5f),
                StartColor = new Color(1f, 0.8f, 0.3f, 0.6f),
                EndColor = new Color(1f, 0.4f, 0.1f, 0f),
                StartEmission = 2f,
                EndEmission = 0f,
                FadeOut = true,
                Type = FxType.Shockwave,
            });
        }

        // 4) 烟雾 — 多个灰色半透明球体上升+膨胀
        int smokeCount = Math.Min((int)(scale * 1.5f), 4); // 减少烟雾数量上限
        for (int i = 0; i < smokeCount; i++)
        {
            if (_activeFx.Count >= MaxActiveFx) break;
            var angle = (float)i / smokeCount * MathF.PI * 2f;
            var offset = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * scale * 0.3f;
            var smoke = new MeshInstance3D();
            var sph = new SphereMesh { Radius = scale * 0.2f, Height = scale * 0.4f };
            smoke.Mesh = sph;
            smoke.MaterialOverride = _sharedSmokeMat;
            smoke.Position = center + offset;
            _effectsNode.AddChild(smoke);
            _activeFx.Add(new FxParticle
            {
                Node = smoke,
                Age = 0,
                Lifetime = 1.2f + (float)GD.RandRange(0, 0.5),
                StartScale = new Vector3(0.4f, 0.4f, 0.4f),
                EndScale = new Vector3(scale * 0.8f, scale * 0.8f, scale * 0.8f),
                StartColor = new Color(0.35f, 0.3f, 0.25f, 0.7f),
                EndColor = new Color(0.15f, 0.12f, 0.1f, 0f),
                StartEmission = 0f,
                EndEmission = 0f,
                FadeOut = true,
                Velocity = new Vector3(offset.X * 0.5f, scale * 0.8f, offset.Z * 0.5f),
                Type = FxType.Smoke,
            });
        }

        // 5) 火花碎片 — 小亮点向四周飞溅
        int sparkCount = Math.Min((int)(scale * 2f), 5); // 减少数量
        for (int i = 0; i < sparkCount; i++)
        {
            if (_activeFx.Count >= MaxActiveFx) break;
            var angle = (float)i / sparkCount * MathF.PI * 2f;
            var elev = 0.3f + (float)GD.RandRange(0, 0.5);
            var dir = new Vector3(MathF.Cos(angle), elev, MathF.Sin(angle)).Normalized();
            var spark = new MeshInstance3D();
            var sph = new SphereMesh { Radius = 0.06f, Height = 0.12f };
            spark.Mesh = sph;
            spark.MaterialOverride = _sharedSparkMat;
            spark.Position = center;
            _effectsNode.AddChild(spark);
            _activeFx.Add(new FxParticle
            {
                Node = spark,
                Age = 0,
                Lifetime = 0.4f + (float)GD.RandRange(0, 0.3),
                StartScale = Vector3.One,
                EndScale = new Vector3(0.2f, 0.2f, 0.2f),
                StartColor = new Color(1f, 0.7f, 0.2f, 1f),
                EndColor = new Color(0.5f, 0.1f, 0.02f, 0f),
                StartEmission = 4f,
                EndEmission = 0f,
                FadeOut = true,
                Velocity = dir * scale * 3f,
                Type = FxType.Spark,
            });
        }
    }

    // ======== 新增战斗/环境特效 (阶段E6) ========

    /// <summary>建筑被摧毁时的碎片飞散效果</summary>
    public void SpawnBuildingDebris(Vector3 pos, float scale)
    {
        if (_activeFx.Count >= MaxActiveFx) return;

        // 大块碎片飞溅
        int debrisCount = Math.Min((int)(scale * 3f), 8); // 减少数量
        for (int i = 0; i < debrisCount; i++)
        {
            if (_activeFx.Count >= MaxActiveFx) break;
            var angle = (float)i / debrisCount * MathF.PI * 2f + (float)GD.RandRange(0, 0.5);
            var elev = 0.5f + (float)GD.RandRange(0, 0.8);
            var dir = new Vector3(MathF.Cos(angle), elev, MathF.Sin(angle)).Normalized();
            var debris = new MeshInstance3D();
            var boxSize = 0.15f + (float)GD.RandRange(0, 0.2);
            var box = new BoxMesh { Size = new Vector3(boxSize, boxSize * 0.7f, boxSize * 0.5f) };
            debris.Mesh = box;
            debris.MaterialOverride = _sharedDebrisMat;
            debris.Position = pos + new Vector3(0, scale * 0.5f, 0);
            debris.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            _effectsNode.AddChild(debris);
            _activeFx.Add(new FxParticle
            {
                Node = debris,
                Age = 0,
                Lifetime = 1.5f + (float)GD.RandRange(0, 0.5),
                StartScale = Vector3.One,
                EndScale = new Vector3(0.3f, 0.3f, 0.3f),
                StartColor = new Color(0.4f, 0.35f, 0.3f),
                EndColor = new Color(0.2f, 0.15f, 0.1f, 0f),
                StartEmission = 0f,
                EndEmission = 0f,
                FadeOut = true,
                Velocity = dir * scale * 4f,
                Type = FxType.Spark,
            });
        }

        // 灰尘柱 (浓烟上升)
        int dustCount = Math.Min((int)(scale * 2f), 5); // 减少数量
        for (int i = 0; i < dustCount; i++)
        {
            if (_activeFx.Count >= MaxActiveFx) break;
            var angle = (float)i / dustCount * MathF.PI * 2f;
            var offset = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * scale * 0.25f;
            var dust = new MeshInstance3D();
            var sph = new SphereMesh { Radius = scale * 0.3f, Height = scale * 0.6f };
            dust.Mesh = sph;
            dust.MaterialOverride = _sharedSmokeMat;
            dust.Position = pos + offset + new Vector3(0, scale * 0.3f, 0);
            _effectsNode.AddChild(dust);
            _activeFx.Add(new FxParticle
            {
                Node = dust,
                Age = 0,
                Lifetime = 2.0f + (float)GD.RandRange(0, 1),
                StartScale = new Vector3(0.5f, 0.5f, 0.5f),
                EndScale = new Vector3(scale * 1.2f, scale * 1.2f, scale * 1.2f),
                StartColor = new Color(0.4f, 0.35f, 0.3f, 0.6f),
                EndColor = new Color(0.15f, 0.12f, 0.1f, 0f),
                StartEmission = 0f,
                EndEmission = 0f,
                FadeOut = true,
                Velocity = new Vector3(offset.X * 0.3f, scale * 1.2f, offset.Z * 0.3f),
                Type = FxType.Smoke,
            });
        }
    }

    /// <summary>单位移动尘埃 — 坦克/车辆移动时在地面上扬起尘土</summary>
    public void SpawnMoveDust(Vector3 pos, float scale = 0.5f)
    {
        if (_activeFx.Count >= MaxActiveFx) return;

        var dust = new MeshInstance3D();
        var sph = new SphereMesh { Radius = scale * 0.2f, Height = scale * 0.4f };
        dust.Mesh = sph;
        dust.MaterialOverride = _sharedDustMat;
        dust.Position = pos + new Vector3(0, 0.1f, 0);
        _effectsNode.AddChild(dust);

        var angle = (float)GD.RandRange(0, MathF.PI * 2);
        _activeFx.Add(new FxParticle
        {
            Node = dust,
            Age = 0,
            Lifetime = 0.6f,
            StartScale = new Vector3(0.3f, 0.2f, 0.3f),
            EndScale = new Vector3(scale * 1.5f, 0.3f, scale * 1.5f),
            StartColor = new Color(0.55f, 0.48f, 0.35f, 0.5f),
            EndColor = new Color(0.3f, 0.25f, 0.2f, 0f),
            StartEmission = 0f,
            EndEmission = 0f,
            FadeOut = true,
            Velocity = new Vector3(MathF.Cos(angle) * 0.5f, 0.3f, MathF.Sin(angle) * 0.5f),
            Type = FxType.Smoke,
        });
    }

    /// <summary>建造特效 — 建筑放置/完成时的光圈效果</summary>
    public void SpawnBuildEffect(Vector3 pos, float scale = 2f)
    {
        if (_activeFx.Count >= MaxActiveFx) return;

        // 光环扩散
        var ring = new MeshInstance3D();
        var cyl = new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.1f };
        ring.Mesh = cyl;
        ring.MaterialOverride = _sharedBuildRingMat;
        ring.Position = pos + new Vector3(0, 0.15f, 0);
        _effectsNode.AddChild(ring);
        _activeFx.Add(new FxParticle
        {
            Node = ring,
            Age = 0,
            Lifetime = 0.8f,
            StartScale = new Vector3(0.2f, 1f, 0.2f),
            EndScale = new Vector3(scale, 1f, scale),
            StartColor = new Color(0.3f, 1f, 0.5f, 0.7f),
            EndColor = new Color(0.2f, 0.6f, 0.3f, 0f),
            StartEmission = 3f,
            EndEmission = 0f,
            FadeOut = true,
            Type = FxType.Shockwave,
        });

        // 上升光柱
        var beam = new MeshInstance3D();
        var beamCyl = new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.5f, Height = scale };
        beam.Mesh = beamCyl;
        beam.MaterialOverride = _sharedBuildBeamMat;
        beam.Position = pos + new Vector3(0, scale * 0.5f, 0);
        _effectsNode.AddChild(beam);
        _activeFx.Add(new FxParticle
        {
            Node = beam,
            Age = 0,
            Lifetime = 0.6f,
            StartScale = Vector3.One,
            EndScale = new Vector3(0.1f, 1.5f, 0.1f),
            StartColor = new Color(0.3f, 1f, 0.5f, 0.4f),
            EndColor = new Color(0.2f, 0.6f, 0.3f, 0f),
            StartEmission = 2f,
            EndEmission = 0f,
            FadeOut = true,
            Type = FxType.MuzzleFlash,
        });
    }

    /// <summary>每帧更新所有活跃特效的动画（缩放/颜色/发光/位移/光源强度）</summary>
    private void UpdateActiveFx(float dt)
    {
        for (int i = _activeFx.Count - 1; i >= 0; i--)
        {
            var fx = _activeFx[i];
            fx.Age += dt;
            float t = fx.Age / fx.Lifetime;
            if (t >= 1f)
            {
                if (IsInstanceValid(fx.Node))
                    fx.Node.QueueFree();
                _activeFx.RemoveAt(i);
                continue;
            }

            if (!IsInstanceValid(fx.Node))
            {
                _activeFx.RemoveAt(i);
                continue;
            }

            // 缩放插值
            var scale = fx.StartScale.Lerp(fx.EndScale, t);
            fx.Node.Scale = scale;

            // 位移（烟雾上升/火花飞溅）
            if (fx.Velocity != Vector3.Zero)
            {
                var v = fx.Velocity;
                v.Y -= 5f * dt; // 重力
                fx.Velocity = v;
                fx.Node.GlobalPosition += fx.Velocity * dt;
            }

            // 透明度淡出 — 用Modulate.a而不是修改SharedMaterial（性能优化）
            if (fx.FadeOut && fx.Node is MeshInstance3D)
            {
                // 通过节点可见性+透明度近似淡出，不修改共享材质
                float alpha = 1f - t;
                // 用set_instance_shader_parameter不行（mobile renderer），
                // 改用VisibilityInterpolator（近似）
                // 实际方案：只对OmniLight做强度更新，Mesh用Scale已经够用
            }

            // 光源更新
            if (fx.Node is OmniLight3D light)
            {
                light.LightEnergy = Mathf.Lerp(fx.StartEmission, fx.EndEmission, t);
                if (t > 0.5f)
                    light.LightEnergy *= (1f - t) * 2f; // 后半段快速衰减
            }

            _activeFx[i] = fx;
        }
    }

    // ======== AI 逻辑 ========

    private void AITickForTeam(int teamId)
    {
        // 造兵逻辑
        AIBuildLogic(teamId);
        AITrainUnits(teamId);
        // 攻击波管理
        AIManageAttacks(teamId);
    }

    /// <summary>AI攻击波管理：积累足够单位后整编向玩家基地进攻。</summary>
    private void AIManageAttacks(int teamId)
    {
        if (Unit3D.AiGraceRemaining > 0) return; // 保护期不进攻
        if (!_bases.TryGetValue(PlayerTeamId, out var playerBase) || playerBase._isDead) return;

        // 收集该AI阵营所有非矿车、非工程车的战斗单位
        var army = GetAllUnits()
            .Where(u => u.TeamId == teamId && !u._isDead
                && u.Type != UnitType.Harvester
                && u.Type != UnitType.Engineer
                && u.Type != UnitType.ChiefEngineer
                && u.Type != UnitType.Thief
                && u.Type != UnitType.Spy)
            .ToList();

        if (army.Count < 5) return; // 至少攒5个单位才进攻

        // 检查是否已有单位在进攻路上（距离玩家基地<60格的单位算"已出发"）
        var playerBasePos = playerBase.GlobalPosition;
        int alreadyAttacking = army.Count(u => u.GlobalPosition.DistanceTo(playerBasePos) < 80f);

        // 如果超过60%的军队还没出发，发一次进攻命令
        if (alreadyAttacking < army.Count * 0.4)
        {
            // 目标：玩家基地（带一些散布）
            foreach (var u in army)
            {
                // 已经在靠近的就不重复命令
                if (u.GlobalPosition.DistanceTo(playerBasePos) < 50f) continue;

                // 关闭AutoAI，改用AttackMove让它们成群推进
                u.AutoAI = false;
                var offset = new Vector3(
                    (float)(new Random().NextDouble() - 0.5) * 8f,
                    0,
                    (float)(new Random().NextDouble() - 0.5) * 8f);
                u.CommandAttackMove(playerBasePos + offset);
            }
        }
        else
        {
            // 已经在交战区域，恢复AutoAI让它们自由攻击
            foreach (var u in army)
            {
                if (u.GlobalPosition.DistanceTo(playerBasePos) < 40f)
                    u.AutoAI = true;
            }
        }
    }

    private void AIBuildLogic(int teamId)
    {
        if (!HasBuilding(teamId, Building3D.BuildingType.Base)) return;

        // 14级优先级建筑决策
        if (!HasBuilding(teamId, Building3D.BuildingType.PowerPlant) || GetTeamPower(teamId) < 50)
        {
            TryAIBuild(teamId, Building3D.BuildingType.PowerPlant);
            return;
        }
        if (!HasBuilding(teamId, Building3D.BuildingType.Barracks))
        {
            TryAIBuild(teamId, Building3D.BuildingType.Barracks);
            return;
        }
        if (!HasBuilding(teamId, Building3D.BuildingType.WarFactory))
        {
            TryAIBuild(teamId, Building3D.BuildingType.WarFactory);
            return;
        }
        if (!HasBuilding(teamId, Building3D.BuildingType.TechCenter))
        {
            TryAIBuild(teamId, Building3D.BuildingType.TechCenter);
            return;
        }
        if (GetTeamPower(teamId) < 0)
        {
            TryAIBuild(teamId, Building3D.BuildingType.PowerPlant);
            return;
        }
        if (CountBuildings(teamId, Building3D.BuildingType.Turret) < 2)
        {
            TryAIBuild(teamId, Building3D.BuildingType.Turret);
            return;
        }
        if (CountBuildings(teamId, Building3D.BuildingType.AntiAirTurret) < 2)
        {
            TryAIBuild(teamId, Building3D.BuildingType.AntiAirTurret);
            return;
        }
        if (!HasBuilding(teamId, Building3D.BuildingType.RepairPad))
        {
            TryAIBuild(teamId, Building3D.BuildingType.RepairPad);
            return;
        }
        if (!HasBuilding(teamId, Building3D.BuildingType.Airfield))
        {
            TryAIBuild(teamId, Building3D.BuildingType.Airfield);
            return;
        }
        // 超武
        if (_difficulty >= Difficulty.Hard)
        {
            if (!HasBuilding(teamId, Building3D.BuildingType.NukeSilo))
            {
                TryAIBuild(teamId, Building3D.BuildingType.NukeSilo);
                return;
            }
            if (!HasBuilding(teamId, Building3D.BuildingType.LightningTower))
            {
                TryAIBuild(teamId, Building3D.BuildingType.LightningTower);
                return;
            }
        }
    }

    private void TryAIBuild(int teamId, Building3D.BuildingType type)
    {
        int cost = BuildingCosts.GetValueOrDefault(type, 0);
        if (_money[teamId] < cost + 50) return; // 留50储备
        if (!SpendMoney(teamId, cost)) return;

        // 在基地附近放置
        if (!_bases.TryGetValue(teamId, out var baseBldg)) return;
        var pos = baseBldg.GlobalPosition + new Vector3(
            (float)(new Random().NextDouble() - 0.5) * TerrainGrid3D.CellSize * 6,
            0,
            (float)(new Random().NextDouble() - 0.5) * TerrainGrid3D.CellSize * 6);
        pos = new Vector3(pos.X, 0, pos.Z);
        CreateBuilding(type, teamId, pos);
    }

    private void AITrainUnits(int teamId)
    {
        // 找车厂/兵营
        var producers = GetAllBuildings().Where(b => b.TeamId == teamId && !b._isDead && b.Type is Building3D.BuildingType.WarFactory or Building3D.BuildingType.Barracks).ToList();
        if (producers.Count == 0) return;

        // 矿车数量检查
        int harvesterCount = GetAllUnits().Count(u => u.TeamId == teamId && u.Type == UnitType.Harvester && !u._isDead);
        if (harvesterCount < 3)
        {
            var wf = producers.FirstOrDefault(b => b.Type == Building3D.BuildingType.WarFactory);
            if (wf != null && _money[teamId] >= UnitCosts[UnitType.Harvester])
            {
                if (SpendMoney(teamId, UnitCosts[UnitType.Harvester]))
                    wf.EnqueueProduction(Building3D.ProductionType.Harvester);
                return;
            }
        }

        // 随机造兵
        var rng = new Random();
        var productionTypes = new List<(Building3D.ProductionType pt, int cost)>();

        // 兵营可造
        if (HasBuilding(teamId, Building3D.BuildingType.Barracks))
        {
            productionTypes.Add((Building3D.ProductionType.Infantry, 100));
            productionTypes.Add((Building3D.ProductionType.Grenadier, 200));
            if (HasBuilding(teamId, Building3D.BuildingType.TechCenter))
            {
                productionTypes.Add((Building3D.ProductionType.Sniper, 250));
                productionTypes.Add((Building3D.ProductionType.Sapper, 150));
            }
        }
        // 车厂可造
        if (HasBuilding(teamId, Building3D.BuildingType.WarFactory))
        {
            productionTypes.Add((Building3D.ProductionType.LightTank, 200));
            if (HasBuilding(teamId, Building3D.BuildingType.TechCenter))
            {
                productionTypes.Add((Building3D.ProductionType.HeavyTank, 500));
                productionTypes.Add((Building3D.ProductionType.Artillery, 400));
                productionTypes.Add((Building3D.ProductionType.RocketLauncher, 450));
                productionTypes.Add((Building3D.ProductionType.MissileTank, 600));
            }
            productionTypes.Add((Building3D.ProductionType.AntiAir, 300));
        }
        // 机场可造
        if (HasBuilding(teamId, Building3D.BuildingType.Airfield))
        {
            productionTypes.Add((Building3D.ProductionType.Fighter, 500));
            productionTypes.Add((Building3D.ProductionType.Helicopter, 600));
        }

        // 按造价降序尝试
        productionTypes.Sort((a, b) => b.cost.CompareTo(a.cost));
        foreach (var (pt, cost) in productionTypes)
        {
            if (_money[teamId] < cost) continue;

            // 找对应的生产建筑
            Building3D? producer = null;
            Building3D.BuildingType requiredType = pt switch
            {
                Building3D.ProductionType.Infantry or Building3D.ProductionType.Grenadier
                or Building3D.ProductionType.Sniper or Building3D.ProductionType.Sapper
                or Building3D.ProductionType.ChiefEngineer or Building3D.ProductionType.FlameInfantry
                or Building3D.ProductionType.Engineer => Building3D.BuildingType.Barracks,
                Building3D.ProductionType.Fighter or Building3D.ProductionType.Helicopter
                or Building3D.ProductionType.Bomber or Building3D.ProductionType.Scout
                or Building3D.ProductionType.TransportHeli or Building3D.ProductionType.RocketInfantry
                => Building3D.BuildingType.Airfield,
                _ => Building3D.BuildingType.WarFactory,
            };

            // 找队列最短的建筑
            producer = producers
                .Where(b => b.Type == requiredType)
                .OrderBy(b => b.QueueCount)
                .FirstOrDefault();

            if (producer == null) continue;
            if (producer.QueueCount >= Building3D.MaxQueueSize) continue;

            if (SpendMoney(teamId, cost))
            {
                producer.EnqueueProduction(pt);
                break; // 一次只排一个
            }
        }
    }

    private int CountBuildings(int teamId, Building3D.BuildingType type)
    {
        return GetAllBuildings().Count(b => b.TeamId == teamId && b.Type == type && !b._isDead);
    }

    // ======== 胜负判定 ========

    private void CheckWinCondition()
    {
        bool playerAlive = GetAllBuildings().Any(b => b.TeamId == PlayerTeamId && !b._isDead)
            || GetAllUnits().Any(u => u.TeamId == PlayerTeamId && !u._isDead);
        bool aiAlive = GetAllBuildings().Any(b => b.TeamId != PlayerTeamId && !b._isDead)
            || GetAllUnits().Any(u => u.TeamId != PlayerTeamId && !u._isDead);

        if (!playerAlive && _gameOverLabel == null)
        {
            ShowGameOver(false);
        }
        else if (!aiAlive && _gameOverLabel == null)
        {
            ShowGameOver(true);
        }
    }

    private void ShowGameOver(bool victory)
    {
        _gameOverLabel = new Label
        {
            Text = victory ? "胜利！" : "失败",
            Position = new Vector2(540, 300),
            Size = new Vector2(200, 80),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _gameOverLabel.AddThemeFontSizeOverride("font_size", 48);
        _gameOverLabel.AddThemeColorOverride("font_color", victory ? Colors.Green : Colors.Red);
        _uiLayer.AddChild(_gameOverLabel);
    }

    // ======== UI ========

    private void SetupUI()
    {
        _uiLayer = new CanvasLayer { Name = "UI" };
        AddChild(_uiLayer);

        // 主信息标签
        _uiLabel = new Label
        {
            Position = new Vector2(10, 10),
            Size = new Vector2(400, 30),
            Text = "",
        };
        _uiLabel.AddThemeFontSizeOverride("font_size", 16);
        _uiLayer.AddChild(_uiLabel);

        // 资金标签
        _moneyLabel = new Label
        {
            Position = new Vector2(10, 40),
            Size = new Vector2(200, 30),
            Text = "",
        };
        _moneyLabel.AddThemeFontSizeOverride("font_size", 18);
        _moneyLabel.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        _uiLayer.AddChild(_moneyLabel);

        // 电力标签
        _powerLabel = new Label
        {
            Position = new Vector2(220, 40),
            Size = new Vector2(200, 30),
            Text = "",
        };
        _powerLabel.AddThemeFontSizeOverride("font_size", 16);
        _powerLabel.AddThemeColorOverride("font_color", Colors.Cyan);
        _uiLayer.AddChild(_powerLabel);

        // 提示标签
        _hintLabel = new Label
        {
            Position = new Vector2(10, 690),
            Size = new Vector2(800, 30),
            Text = "左键选择 | 右键命令 | WASD移动相机 | B轻坦 N重坦 M炮兵 H矿车 | P电站 O兵营 I车厂 T科技 | Z核弹 C闪电",
        };
        _hintLabel.AddThemeFontSizeOverride("font_size", 12);
        _hintLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f, 0.8f));
        _uiLayer.AddChild(_hintLabel);

        // Toast容器
        _toastContainer = new VBoxContainer
        {
            Position = new Vector2(440, 50),
            Size = new Vector2(400, 200),
        };
        _uiLayer.AddChild(_toastContainer);

        // 开局覆盖
        _startOverlay = new Label
        {
            Text = "摧毁所有敌方建筑和单位即可获胜！",
            Position = new Vector2(340, 300),
            Size = new Vector2(600, 60),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _startOverlay.AddThemeFontSizeOverride("font_size", 24);
        _startOverlay.AddThemeColorOverride("font_color", Colors.White);
        _uiLayer.AddChild(_startOverlay);
        _startOverlayAge = 0;

        // 建造面板
        SetupBuildPanel();

        // 小地图
        SetupMinimap();
    }

    private void SetupBuildPanel()
    {
        // 左下角建造面板 — 建筑/步兵/车辆三栏垂直列表
        _buildPanel = new Control
        {
            Position = new Vector2(5, 420),
            Size = new Vector2(200, 290),
        };
        _uiLayer.AddChild(_buildPanel);

        // 背景
        var bg = new ColorRect
        {
            Position = Vector2.Zero,
            Size = new Vector2(200, 290),
            Color = new Color(0.08f, 0.1f, 0.09f, 0.85f),
        };
        _buildPanel.AddChild(bg);

        var titleLbl = new Label
        {
            Text = "建造 (点击放置)",
            Position = new Vector2(10, 4),
            Size = new Vector2(180, 20),
        };
        titleLbl.AddThemeFontSizeOverride("font_size", 13);
        titleLbl.AddThemeColorOverride("font_color", Colors.White);
        _buildPanel.AddChild(titleLbl);

        // 建筑按钮列表
        var buildButtons = new (string name, Building3D.BuildingType type)[]
        {
            ("电站 $400 [P]", Building3D.BuildingType.PowerPlant),
            ("兵营 $500 [O]", Building3D.BuildingType.Barracks),
            ("车厂 $800 [I]", Building3D.BuildingType.WarFactory),
            ("科技 $1500 [T]", Building3D.BuildingType.TechCenter),
            ("机枪塔 $300", Building3D.BuildingType.Turret),
            ("防空炮 $400", Building3D.BuildingType.AntiAirTurret),
            ("维修厂 $600", Building3D.BuildingType.RepairPad),
            ("核弹井 $2500", Building3D.BuildingType.NukeSilo),
            ("闪电塔 $2000", Building3D.BuildingType.LightningTower),
        };

        int yOff = 24;
        foreach (var (name, type) in buildButtons)
        {
            var btn = new Button
            {
                Text = name,
                Position = new Vector2(5, yOff),
                Size = new Vector2(190, 22),
            };
            btn.AddThemeFontSizeOverride("font_size", 10);
            var capturedType = type;
            btn.Pressed += () => StartPlacement(capturedType);
            _buildPanel.AddChild(btn);
            yOff += 24;
        }

        // 单位热键提示 — 右上角
        var unitLabel = new Label
        {
            Text = "单位: B轻坦 N重坦 M炮兵 H矿车 G步兵\n命令: Q攻击移动 X停止 R维修 V出售\n超武: Z核弹 C闪电",
            Position = new Vector2(1080, 390),
            Size = new Vector2(190, 60),
        };
        unitLabel.AddThemeFontSizeOverride("font_size", 10);
        unitLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.7f, 0.9f));
        _uiLayer.AddChild(unitLabel);

        // 生产进度面板 — 建造面板右侧
        SetupProductionPanel();
    }

    private void SetupProductionPanel()
    {
        _prodPanel = new VBoxContainer
        {
            Position = new Vector2(210, 420),
            Size = new Vector2(180, 290),
        };
        _uiLayer.AddChild(_prodPanel);

        var titleLbl = new Label
        {
            Text = "生产进度",
        };
        titleLbl.AddThemeFontSizeOverride("font_size", 13);
        titleLbl.AddThemeColorOverride("font_color", Colors.White);
        _prodPanel.AddChild(titleLbl);
    }

    // 小地图配置
    private const float MinimapSize = 180f;
    private const float MinimapX = 1080f;
    private const float MinimapY = 200f;

    private void SetupMinimap()
    {
        // 背景
        var bg = new ColorRect
        {
            Position = new Vector2(MinimapX - 2, MinimapY - 2),
            Size = new Vector2(MinimapSize + 4, MinimapSize + 4),
            Color = new Color(0.15f, 0.15f, 0.15f, 0.9f),
        };
        _uiLayer.AddChild(bg);

        // 使用自定义Control的_draw来绘制小地图
        var minimapCanvas = new MinimapCanvas(this)
        {
            Position = new Vector2(MinimapX, MinimapY),
            Size = new Vector2(MinimapSize, MinimapSize),
        };
        minimapCanvas.Name = "MinimapCanvas";
        _uiLayer.AddChild(minimapCanvas);
    }

    /// <summary>供MinimapCanvas调用的绘制方法</summary>
    public void DrawMinimap(CanvasItem canvas)
    {
        float mapSize = TerrainGrid3D.MapWorldSize;
        float scale = MinimapSize / mapSize;

        // 背景
        canvas.DrawRect(new Rect2(0, 0, MinimapSize, MinimapSize),
            new Color(0.08f, 0.12f, 0.08f), true);

        // 地形概览（简化：根据TerrainType画色块）
        int gridSize = TerrainGrid3D.GridSize;
        float cellPx = MinimapSize / gridSize;
        for (int x = 0; x < gridSize; x++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                var cell = _terrain.GetCell(x, y);
                Color c = cell.Type switch
                {
                    TerrainType.DeepWater => new Color(0.05f, 0.1f, 0.25f),
                    TerrainType.ShallowWater => new Color(0.1f, 0.2f, 0.35f),
                    TerrainType.Mountain => new Color(0.3f, 0.25f, 0.2f),
                    TerrainType.Cliff => new Color(0.25f, 0.22f, 0.18f),
                    TerrainType.Grass => new Color(0.12f, 0.2f, 0.1f),
                    TerrainType.Sand => new Color(0.35f, 0.3f, 0.15f),
                    TerrainType.Road => new Color(0.2f, 0.2f, 0.2f),
                    TerrainType.Snow => new Color(0.7f, 0.7f, 0.75f),
                    TerrainType.City => new Color(0.25f, 0.23f, 0.2f),
                    TerrainType.Field => new Color(0.15f, 0.18f, 0.08f),
                    TerrainType.Bridge => new Color(0.4f, 0.3f, 0.15f),
                    TerrainType.Tunnel => new Color(0.15f, 0.12f, 0.1f),
                    _ => new Color(0.1f, 0.15f, 0.1f),
                };
                canvas.DrawRect(new Rect2(x * cellPx, y * cellPx, cellPx + 1, cellPx + 1), c, true);
            }
        }

        // 建筑
        foreach (var b in GetAllBuildings())
        {
            if (b._isDead) continue;
            var px = b.GlobalPosition.X * scale;
            var py = b.GlobalPosition.Z * scale;
            Color c = Unit3D.GetTeamColor(b.TeamId);
            float r = b.Type == Building3D.BuildingType.Base ? 4f : 2.5f;
            canvas.DrawCircle(new Vector2(px, py), r, c);
        }

        // 单位
        foreach (var u in GetAllUnits())
        {
            if (u._isDead) continue;
            var px = u.GlobalPosition.X * scale;
            var py = u.GlobalPosition.Z * scale;
            Color c = Unit3D.GetTeamColor(u.TeamId);
            canvas.DrawRect(new Rect2(px - 1.5f, py - 1.5f, 3f, 3f), c, true);
        }

        // 边框
        canvas.DrawRect(new Rect2(0, 0, MinimapSize, MinimapSize),
            new Color(0.5f, 0.5f, 0.5f, 0.8f), false, 2f);

        // 相机视野框
        var camPos = _camera.Position;
        var camPx = camPos.X * scale;
        var camPy = camPos.Z * scale;
        float viewW = 40f * scale;
        float viewH = 30f * scale;
        canvas.DrawRect(new Rect2(camPx - viewW * 0.5f, camPy - viewH * 0.5f, viewW, viewH),
            new Color(1f, 1f, 1f, 0.5f), false, 1f);
    }

    private void ShowToast(string message)
    {
        var label = new Label
        {
            Text = message,
            Size = new Vector2(400, 24),
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", Colors.Yellow);
        _toastContainer.AddChild(label);
        _toasts.Add((label, 0, 3f));
    }

    private float _uiUpdateTimer = 0f;
    private void UpdateUI()
    {
        // 限频：每0.3秒更新一次UI文本，避免每帧LINQ + 字符串格式
        _uiUpdateTimer += (float)GetProcessDeltaTime();
        bool fullUpdate = _uiUpdateTimer >= 0.3f;
        if (fullUpdate) _uiUpdateTimer = 0f;

        if (fullUpdate)
        {
            int money = GetMoney(PlayerTeamId);
            int power = GetTeamPower(PlayerTeamId);
            _moneyLabel.Text = $"资金: ${money}";
            _powerLabel.Text = $"电力: {power}";
            _powerLabel.AddThemeColorOverride("font_color", power >= 0 ? Colors.Cyan : Colors.Red);

            int unitCount = 0, bldgCount = 0;
            foreach (var u in GetAllUnits())
                if (u.TeamId == PlayerTeamId && !u._isDead) unitCount++;
            foreach (var b in GetAllBuildings())
                if (b.TeamId == PlayerTeamId && !b._isDead) bldgCount++;
            _uiLabel.Text = $"单位: {unitCount} | 建筑: {bldgCount} | 时间: {_timeOfDay:F1}h";
        }

        if (_startOverlay != null)
        {
            _startOverlayAge += (float)GetProcessDeltaTime();
            if (_startOverlayAge > 5f)
            {
                _startOverlay.QueueFree();
                _startOverlay = null;
            }
            else
            {
                float alpha = 1f - (_startOverlayAge / 5f);
                _startOverlay.Modulate = new Color(1, 1, 1, alpha);
            }
        }

        // Toast 更新
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            var (label, age, life) = _toasts[i];
            float newAge = age + (float)GetProcessDeltaTime();
            if (newAge > life)
            {
                label.QueueFree();
                _toasts.RemoveAt(i);
            }
            else
            {
                _toasts[i] = (label, newAge, life);
                float alpha = 1f - (newAge / life) * 0.5f;
                label.Modulate = new Color(1, 1, 0.5f, alpha);
            }
        }

        // 建筑受击冷却
        var keys = new List<ulong>(_buildingAlertCooldown.Keys);
        foreach (var k in keys)
            _buildingAlertCooldown[k] -= (float)GetProcessDeltaTime();
        _buildingAlertCooldown.Where(k => k.Value <= 0).ToList().ForEach(k => _buildingAlertCooldown.Remove(k.Key));

        // 生产进度面板
        UpdateProductionPanel();
    }

    private static string ProdTypeName(Building3D.ProductionType pt) => pt switch
    {
        Building3D.ProductionType.Infantry => "步兵",
        Building3D.ProductionType.Engineer => "工程师",
        Building3D.ProductionType.Sapper => "爆破手",
        Building3D.ProductionType.ChiefEngineer => "主工",
        Building3D.ProductionType.Grenadier => "掷弹兵",
        Building3D.ProductionType.Sniper => "狙击手",
        Building3D.ProductionType.FlameInfantry => "火焰兵",
        Building3D.ProductionType.LightTank => "轻坦",
        Building3D.ProductionType.HeavyTank => "重坦",
        Building3D.ProductionType.Artillery => "炮兵",
        Building3D.ProductionType.RocketLauncher => "火箭炮",
        Building3D.ProductionType.MissileTank => "导弹车",
        Building3D.ProductionType.AntiAir => "防空",
        Building3D.ProductionType.Harvester => "矿车",
        Building3D.ProductionType.Transport => "运输车",
        Building3D.ProductionType.Hero => "英雄",
        Building3D.ProductionType.Spy => "间谍",
        Building3D.ProductionType.Thief => "窃贼",
        Building3D.ProductionType.Fighter => "战机",
        Building3D.ProductionType.Helicopter => "直升机",
        Building3D.ProductionType.RocketInfantry => "火箭兵",
        Building3D.ProductionType.Bomber => "轰炸机",
        Building3D.ProductionType.Scout => "侦察机",
        Building3D.ProductionType.TransportHeli => "运输机",
        _ => pt.ToString(),
    };

    private void UpdateProductionPanel()
    {
        if (_prodPanel == null) return;

        // 清除旧条目（保留标题）
        while (_prodPanel.GetChildCount() > 1)
        {
            var child = _prodPanel.GetChild(1);
            _prodPanel.RemoveChild(child);
            child.QueueFree();
        }

        // 收集玩家正在生产的建筑
        var producers = GetAllBuildings()
            .Where(b => b.TeamId == PlayerTeamId && !b._isDead
                && (b.Type == Building3D.BuildingType.Barracks
                 || b.Type == Building3D.BuildingType.WarFactory
                 || b.Type == Building3D.BuildingType.Airfield)
                && b.CurrentProductionType.HasValue)
            .ToList();

        if (producers.Count == 0)
        {
            var idle = new Label { Text = "（无生产中）" };
            idle.AddThemeFontSizeOverride("font_size", 11);
            idle.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f, 0.7f));
            _prodPanel.AddChild(idle);
            return;
        }

        foreach (var bldg in producers)
        {
            var pt = bldg.CurrentProductionType.Value;
            float progress = bldg.ProductionProgress;
            int queued = bldg.QueueCount;

            // 每个生产条目：一行文字 + 一条进度条
            var row = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.Fill };

            var info = new Label
            {
                Text = $"{ProdTypeName(pt)} ({bldg.Type})  [队列:{queued}]",
            };
            info.AddThemeFontSizeOverride("font_size", 10);
            info.AddThemeColorOverride("font_color", Colors.White);
            row.AddChild(info);

            // 进度条背景
            var barBg = new ColorRect
            {
                CustomMinimumSize = new Vector2(170, 10),
                Color = new Color(0.15f, 0.15f, 0.15f),
            };
            row.AddChild(barBg);

            // 进度条前景
            float barWidth = 170f * Mathf.Clamp(progress, 0f, 1f);
            var barFg = new ColorRect
            {
                Position = Vector2.Zero,
                Size = new Vector2(barWidth, 10),
                Color = new Color(0.2f, 0.8f, 0.3f),
            };
            barBg.AddChild(barFg);

            _prodPanel.AddChild(row);
        }
    }

    // ======== 输入处理 ========

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Left)
            {
                if (mouseBtn.Pressed)
                {
                    if (_isPlacing)
                    {
                        PlaceBuildingAtMouse();
                        return;
                    }
                    _isDragging = true;
                    _dragStartScreen = mouseBtn.Position;
                }
                else if (_isDragging)
                {
                    _isDragging = false;
                    HandleSelection(_dragStartScreen, mouseBtn.Position);
                }
            }
            else if (mouseBtn.ButtonIndex == MouseButton.Right && mouseBtn.Pressed)
            {
                HandleRightClick();
            }
        }

        if (@event is InputEventKey key && key.Pressed)
        {
            HandleCommandKey(key);
        }
    }

    private Vector3 GetMouseWorldPos()
    {
        var mousePos = GetViewport().GetMousePosition();
        var rayOrigin = _camera.ProjectRayOrigin(mousePos);
        var rayDir = _camera.ProjectRayNormal(mousePos);

        // 射线与Y=0平面交点
        if (Mathf.Abs(rayDir.Y) < 0.001f) return Vector3.Zero;
        float t = -rayOrigin.Y / rayDir.Y;
        if (t < 0) return Vector3.Zero;
        return rayOrigin + rayDir * t;
    }

    private void HandleSelection(Vector2 start, Vector2 end)
    {
        // 框选
        ClearSelection();

        var rect = new Rect2(
            Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y),
            Mathf.Abs(end.X - start.X), Mathf.Abs(end.Y - start.Y));

        // 如果是点击（很小区域），用点选
        if (rect.Size.Length() < 5f)
        {
            // 点选：通过3D射线
            var worldPos = GetMouseWorldPos();
            // 优先选建筑
            Building3D? closestBldg = null;
            float bldgDist = 5f;
            foreach (var b in GetAllBuildings())
            {
                if (b.TeamId != PlayerTeamId) continue;
                float d = b.GlobalPosition.DistanceTo(worldPos);
                if (d < bldgDist) { bldgDist = d; closestBldg = b; }
            }
            if (closestBldg != null)
            {
                _selected.Add(closestBldg);
                closestBldg.SetSelected(true);
                return;
            }
            // 选单位
            Unit3D? closestUnit = null;
            float unitDist = 3f;
            foreach (var u in GetAllUnits())
            {
                if (u.TeamId != PlayerTeamId) continue;
                float d = u.GlobalPosition.DistanceTo(worldPos);
                if (d < unitDist) { unitDist = d; closestUnit = u; }
            }
            if (closestUnit != null)
            {
                _selected.Add(closestUnit);
                closestUnit.SetSelected(true);
                AudioManager.Instance?.PlaySfx(AudioManager.Sfx.Select);
            }
            return;
        }

        // 框选单位（用屏幕坐标判断）
        foreach (var u in GetAllUnits())
        {
            if (u.TeamId != PlayerTeamId || u._isDead) continue;
            var screenPos = _camera.UnprojectPosition(u.GlobalPosition);
            if (rect.HasPoint(screenPos))
            {
                _selected.Add(u);
                u.SetSelected(true);
            }
        }
        if (_selected.Count > 0)
            AudioManager.Instance?.PlaySfx(AudioManager.Sfx.Select);
    }

    private void ClearSelection()
    {
        foreach (var obj in _selected)
        {
            if (obj is Unit3D u) u.SetSelected(false);
            else if (obj is Building3D b) b.SetSelected(false);
        }
        _selected.Clear();
    }

    private void HandleRightClick()
    {
        var worldPos = GetMouseWorldPos();

        // 优先级：敌方单位→攻击 / 敌方建筑→攻击 / 友方运输车→上车 / 普通地面→移动
        Unit3D? enemyUnit = null;
        foreach (var u in GetAllUnits())
        {
            if (u.TeamId == PlayerTeamId || u._isDead) continue;
            if (u.GlobalPosition.DistanceTo(worldPos) < 3f) { enemyUnit = u; break; }
        }

        Building3D? enemyBldg = null;
        foreach (var b in GetAllBuildings())
        {
            if (b.TeamId == PlayerTeamId || b._isDead) continue;
            if (b.GlobalPosition.DistanceTo(worldPos) < 5f) { enemyBldg = b; break; }
        }

        bool hasUnits = _selected.Any(o => o is Unit3D);
        bool hasBuildings = _selected.Any(o => o is Building3D);

        if (enemyUnit != null && hasUnits)
        {
            foreach (var obj in _selected)
                if (obj is Unit3D u) u.CommandAttack(enemyUnit);
        }
        else if (enemyBldg != null && hasUnits)
        {
            foreach (var obj in _selected)
                if (obj is Unit3D u) u.CommandAttackBuilding(enemyBldg);
        }
        else if (hasBuildings && !hasUnits)
        {
            // 设置集结点
            foreach (var obj in _selected)
                if (obj is Building3D b) b.SetRallyPoint(worldPos);
        }
        else if (hasUnits)
        {
            // 检查上车
            Unit3D? transport = null;
            foreach (var u in GetAllUnits())
            {
                if (u.TeamId != PlayerTeamId || !u.IsTransport || u._isDead) continue;
                if (u.GlobalPosition.DistanceTo(worldPos) < 3f) { transport = u; break; }
            }

            if (transport != null)
            {
                foreach (var obj in _selected)
                    if (obj is Unit3D u && Unit3D.IsInfantryType(u.Type)) u._embarkTarget = transport;
            }
            else
            {
                // 队形移动
                var units = _selected.OfType<Unit3D>().Where(u => !u._isDead).ToList();
                int count = units.Count();
                int cols = (int)Math.Ceiling(Math.Sqrt(count));
                float spacing = 4f;
                for (int i = 0; i < units.Count; i++)
                {
                    int col = i % cols;
                    int row = i / cols;
                    var offset = new Vector3(
                        (col - cols * 0.5f) * spacing,
                        0,
                        (row - count * 0.5f / cols) * spacing);
                    units[i].CommandMove(worldPos + offset);
                }
                // 移动音效
                AudioManager.Instance?.PlaySfx(AudioManager.Sfx.Move);
            }
        }
    }

    private void HandleCommandKey(InputEventKey key)
    {
        var keyChar = (char)key.PhysicalKeycode;

        // 单位生产热键
        switch (char.ToUpper(keyChar))
        {
            case 'B':
                if (HasBuilding(PlayerTeamId, Building3D.BuildingType.WarFactory))
                    TrySpawnPlayerUnit(UnitType.LightTank);
                break;
            case 'N':
                if (HasBuilding(PlayerTeamId, Building3D.BuildingType.WarFactory) && HasBuilding(PlayerTeamId, Building3D.BuildingType.TechCenter))
                    TrySpawnPlayerUnit(UnitType.HeavyTank);
                break;
            case 'M':
                if (HasBuilding(PlayerTeamId, Building3D.BuildingType.WarFactory) && HasBuilding(PlayerTeamId, Building3D.BuildingType.TechCenter))
                    TrySpawnPlayerUnit(UnitType.Artillery);
                break;
            case 'H':
                if (HasBuilding(PlayerTeamId, Building3D.BuildingType.WarFactory))
                    TrySpawnPlayerUnit(UnitType.Harvester);
                break;
            case 'K':
                if (HasBuilding(PlayerTeamId, Building3D.BuildingType.WarFactory) && HasBuilding(PlayerTeamId, Building3D.BuildingType.TechCenter))
                    TrySpawnPlayerUnit(UnitType.RocketLauncher);
                break;
            case 'L':
                if (HasBuilding(PlayerTeamId, Building3D.BuildingType.WarFactory) && HasBuilding(PlayerTeamId, Building3D.BuildingType.TechCenter))
                    TrySpawnPlayerUnit(UnitType.MissileTank);
                break;
            // 建筑热键
            case 'P':
                StartPlacement(Building3D.BuildingType.PowerPlant);
                break;
            case 'O':
                StartPlacement(Building3D.BuildingType.Barracks);
                break;
            case 'I':
                StartPlacement(Building3D.BuildingType.WarFactory);
                break;
            case 'T':
                StartPlacement(Building3D.BuildingType.TechCenter);
                break;
            // 步兵
            case 'G':
                if (HasBuilding(PlayerTeamId, Building3D.BuildingType.Barracks))
                    TrySpawnPlayerUnit(UnitType.Infantry);
                break;
            // 命令
            case 'Q':
                // 攻击移动
                {
                    var pos = GetMouseWorldPos();
                    foreach (var obj in _selected)
                        if (obj is Unit3D u) u.CommandAttackMove(pos);
                }
                break;
            case 'X':
                foreach (var obj in _selected)
                    if (obj is Unit3D u) u.CommandStop();
                break;
            case 'R':
                // 维修建筑
                foreach (var obj in _selected)
                    if (obj is Building3D b && b.TeamId == PlayerTeamId)
                    {
                        int cost = (int)(BuildingCosts.GetValueOrDefault(b.Type, 0) * (1 - b.Health / b.MaxHealth) * 0.5f);
                        if (SpendMoney(PlayerTeamId, cost))
                            b.Repair();
                    }
                break;
            case 'V':
                // 出售建筑
                foreach (var obj in _selected)
                    if (obj is Building3D b && b.TeamId == PlayerTeamId && b.Type != Building3D.BuildingType.Base)
                    {
                        int refund = BuildingCosts.GetValueOrDefault(b.Type, 0) / 2;
                        AddResourceForTeam(PlayerTeamId, refund);
                        b.QueueFree();
                    }
                break;
            // 超武
            case 'Z':
                if (_playerNukeCooldown <= 0 && HasBuilding(PlayerTeamId, Building3D.BuildingType.NukeSilo))
                {
                    var pos = GetMouseWorldPos();
                    ApplyNuke(pos, PlayerTeamId);
                    _playerNukeCooldown = NukeCooldown;
                }
                break;
            case 'C':
                if (_playerLightningCooldown <= 0 && HasBuilding(PlayerTeamId, Building3D.BuildingType.LightningTower))
                {
                    var pos = GetMouseWorldPos();
                    ApplyLightning(pos, PlayerTeamId);
                    _playerLightningCooldown = LightningCooldown;
                }
                break;
        }

        // Shift+V = 导弹
        if (key.ShiftPressed && char.ToUpper(keyChar) == 'V')
        {
            if (_playerMissileCooldown <= 0 && HasBuilding(PlayerTeamId, Building3D.BuildingType.MissileSilo))
            {
                var pos = GetMouseWorldPos();
                ApplyCruiseMissile(pos, PlayerTeamId);
                _playerMissileCooldown = MissileCooldown;
            }
        }

        // 编队
        if (key.CtrlPressed && keyChar >= '1' && keyChar <= '9')
        {
            int squadNum = keyChar - '0';
            _squads[squadNum] = _selected.OfType<Unit3D>().Where(u => !u._isDead).ToList();
            ShowToast($"编队 {squadNum} 已保存 ({_squads[squadNum].Count} 单位)");
        }
        else if (!key.CtrlPressed && keyChar >= '1' && keyChar <= '9')
        {
            int squadNum = keyChar - '0';
            if (_squads.TryGetValue(squadNum, out var squad))
            {
                ClearSelection();
                foreach (var u in squad.Where(u => !u._isDead))
                {
                    _selected.Add(u);
                    u.SetSelected(true);
                }
            }
        }

        // F12截图
        if (key.Keycode == Key.F12)
        {
            TakeScreenshot();
        }
    }

    private void TrySpawnPlayerUnit(UnitType type)
    {
        int cost = UnitCosts.GetValueOrDefault(type, 0);
        if (_money[PlayerTeamId] < cost)
        {
            ShowToast("资金不足！");
            return;
        }

        // 找对应生产建筑
        Building3D.BuildingType requiredType = type switch
        {
            UnitType.Infantry or UnitType.Grenadier or UnitType.Sniper or UnitType.Sapper
            or UnitType.FlameInfantry or UnitType.Engineer or UnitType.ChiefEngineer
            or UnitType.Hero or UnitType.Spy or UnitType.Thief => Building3D.BuildingType.Barracks,
            UnitType.Fighter or UnitType.Helicopter or UnitType.Bomber or UnitType.Scout
            or UnitType.TransportHeli or UnitType.RocketInfantry => Building3D.BuildingType.Airfield,
            _ => Building3D.BuildingType.WarFactory,
        };

        if (!HasBuilding(PlayerTeamId, requiredType))
        {
            ShowToast($"需要{requiredType}");
            return;
        }

        var producers = GetAllBuildings().Where(b => b.TeamId == PlayerTeamId && b.Type == requiredType && !b._isDead).OrderBy(b => b.QueueCount);
        var producer = producers.FirstOrDefault();
        if (producer == null || producer.QueueCount >= Building3D.MaxQueueSize) return;

        var prodType = UnitTypeToProductionType(type);
        if (!SpendMoney(PlayerTeamId, cost)) return;
        producer.EnqueueProduction(prodType);
    }

    public static Building3D.ProductionType UnitTypeToProductionType(UnitType type) => type switch
    {
        UnitType.Infantry => Building3D.ProductionType.Infantry,
        UnitType.Engineer => Building3D.ProductionType.Engineer,
        UnitType.Sapper => Building3D.ProductionType.Sapper,
        UnitType.ChiefEngineer => Building3D.ProductionType.ChiefEngineer,
        UnitType.Grenadier => Building3D.ProductionType.Grenadier,
        UnitType.Sniper => Building3D.ProductionType.Sniper,
        UnitType.FlameInfantry => Building3D.ProductionType.FlameInfantry,
        UnitType.LightTank => Building3D.ProductionType.LightTank,
        UnitType.HeavyTank => Building3D.ProductionType.HeavyTank,
        UnitType.Artillery => Building3D.ProductionType.Artillery,
        UnitType.RocketLauncher => Building3D.ProductionType.RocketLauncher,
        UnitType.MissileTank => Building3D.ProductionType.MissileTank,
        UnitType.AntiAir => Building3D.ProductionType.AntiAir,
        UnitType.Harvester => Building3D.ProductionType.Harvester,
        UnitType.Transport => Building3D.ProductionType.Transport,
        UnitType.Hero => Building3D.ProductionType.Hero,
        UnitType.Spy => Building3D.ProductionType.Spy,
        UnitType.Thief => Building3D.ProductionType.Thief,
        UnitType.Fighter => Building3D.ProductionType.Fighter,
        UnitType.Helicopter => Building3D.ProductionType.Helicopter,
        UnitType.RocketInfantry => Building3D.ProductionType.RocketInfantry,
        UnitType.Bomber => Building3D.ProductionType.Bomber,
        UnitType.Scout => Building3D.ProductionType.Scout,
        UnitType.TransportHeli => Building3D.ProductionType.TransportHeli,
        UnitType.Destroyer => Building3D.ProductionType.Destroyer,
        UnitType.Submarine => Building3D.ProductionType.Submarine,
        UnitType.AircraftCarrier => Building3D.ProductionType.AircraftCarrier,
        UnitType.LandingCraft => Building3D.ProductionType.LandingCraft,
        _ => Building3D.ProductionType.None,
    };

    // ======== 建筑放置 ========

    private void StartPlacement(Building3D.BuildingType type)
    {
        int cost = BuildingCosts.GetValueOrDefault(type, 0);
        if (_money[PlayerTeamId] < cost)
        {
            ShowToast("资金不足！");
            return;
        }

        // 前置建筑检查
        bool canBuild = type switch
        {
            Building3D.BuildingType.Barracks => HasBuilding(PlayerTeamId, Building3D.BuildingType.PowerPlant),
            Building3D.BuildingType.WarFactory => HasBuilding(PlayerTeamId, Building3D.BuildingType.Barracks),
            Building3D.BuildingType.TechCenter => HasBuilding(PlayerTeamId, Building3D.BuildingType.WarFactory),
            Building3D.BuildingType.Turret => HasBuilding(PlayerTeamId, Building3D.BuildingType.Barracks),
            Building3D.BuildingType.AntiAirTurret => HasBuilding(PlayerTeamId, Building3D.BuildingType.Barracks),
            Building3D.BuildingType.RepairPad => HasBuilding(PlayerTeamId, Building3D.BuildingType.WarFactory),
            Building3D.BuildingType.Airfield => HasBuilding(PlayerTeamId, Building3D.BuildingType.WarFactory),
            Building3D.BuildingType.Shipyard => HasBuilding(PlayerTeamId, Building3D.BuildingType.WarFactory),
            Building3D.BuildingType.NukeSilo => HasBuilding(PlayerTeamId, Building3D.BuildingType.TechCenter),
            Building3D.BuildingType.LightningTower => HasBuilding(PlayerTeamId, Building3D.BuildingType.TechCenter),
            Building3D.BuildingType.MissileSilo => HasBuilding(PlayerTeamId, Building3D.BuildingType.TechCenter),
            _ => true,
        };

        if (!canBuild)
        {
            ShowToast("缺少前置建筑！");
            return;
        }

        // 电力检查（电站本身不限）
        if (type != Building3D.BuildingType.PowerPlant && GetTeamPower(PlayerTeamId) < 0)
        {
            ShowToast("电力不足！");
            return;
        }

        _placementType = type;
        _isPlacing = true;
        AudioManager.Instance?.PlaySfx(AudioManager.Sfx.UiClick);
        ShowToast($"放置 {type}（点击放置）");
    }

    private void PlaceBuildingAtMouse()
    {
        var pos = GetMouseWorldPos();
        int cost = BuildingCosts.GetValueOrDefault(_placementType, 0);

        // 检查放置位置（不在水上/山上/悬崖）
        _terrain.WorldToGrid(pos.X, pos.Z, out int gx, out int gy);
        var cell = _terrain.GetCell(gx, gy);
        if (cell.Type == TerrainType.DeepWater || cell.Type == TerrainType.Mountain || cell.Type == TerrainType.Cliff)
        {
            ShowToast("此地不可建造！");
            return;
        }

        // 检查建筑间距
        foreach (var b in GetAllBuildings())
        {
            if (b.GlobalPosition.DistanceTo(pos) < TerrainGrid3D.CellSize * 1.5f)
            {
                ShowToast("离其他建筑太近！");
                return;
            }
        }

        if (!SpendMoney(PlayerTeamId, cost))
        {
            ShowToast("资金不足！");
            _isPlacing = false;
            return;
        }

        CreateBuilding(_placementType, PlayerTeamId, pos);
        SpawnBuildEffect(pos, 2f);
        _isPlacing = false; // 放完自动退出（红警2风格）
        AudioManager.Instance?.PlaySfxForce(AudioManager.Sfx.UiPlace);
    }

    // ======== 相机控制 ========

    private void ProcessCamera(float dt)
    {
        var dir = Vector3.Zero;
        if (Input.IsActionPressed("move_up")) dir.Z -= 1;
        if (Input.IsActionPressed("move_down")) dir.Z += 1;
        if (Input.IsActionPressed("move_left")) dir.X -= 1;
        if (Input.IsActionPressed("move_right")) dir.X += 1;

        if (dir != Vector3.Zero)
        {
            dir = dir.Normalized();
            float speed = 30f * dt;
            _camera.Position += new Vector3(dir.X * speed, 0, dir.Z * speed);

            // 边界限制
            var pos = _camera.Position;
            pos.X = Mathf.Clamp(pos.X, 10, TerrainGrid3D.MapWorldSize - 10);
            pos.Z = Mathf.Clamp(pos.Z, 10, TerrainGrid3D.MapWorldSize + 50);
            _camera.Position = pos;
            _camera.LookAt(new Vector3(_camera.Position.X, 0, _camera.Position.Z - 42f));
        }

        // 滚轮缩放
        if (Input.IsMouseButtonPressed(MouseButton.WheelUp)
 )
        {
            _camera.Position += new Vector3(0, -2f, -1.5f);
        }
        if (Input.IsMouseButtonPressed(MouseButton.WheelDown))
        {
            _camera.Position += new Vector3(0, 2f, 1.5f);
        }
        _camera.Position = new Vector3(
            _camera.Position.X,
            Mathf.Clamp(_camera.Position.Y, 25, 80),
            _camera.Position.Z);
    }

    // ======== 昼夜系统 ========

    // 昼夜系统 — 降低更新频率避免频繁触发渲染状态变化
    private float _dayNightTimer = 0f;

    private void ProcessDayNight(float dt)
    {
        _timeOfDay += dt * (24f / DayLength);
        if (_timeOfDay >= 24f) _timeOfDay -= 24f;

        // 每0.5秒更新一次光照和天空，避免每帧触发渲染状态重建
        _dayNightTimer += dt;
        if (_dayNightTimer < 0.5f) return;
        _dayNightTimer = 0f;

        // 太阳角度：6点日出(东), 18点日落(西)
        float sunAngle = (_timeOfDay - 6f) / 12f * 180f; // 6点=0, 12点=90, 18点=180
        _sunLight.RotationDegrees = new Vector3(sunAngle - 90, 30, 0);

        // 太阳强度
        float sunIntensity;
        if (_timeOfDay >= 6 && _timeOfDay <= 18)
        {
            sunIntensity = Mathf.Sin((_timeOfDay - 6) / 12f * Mathf.Pi) * 1.2f + 0.1f;
        }
        else
        {
            sunIntensity = 0f;
        }
        _sunLight.LightEnergy = sunIntensity;

        // 月光（夜间最低光照保证可见性）
        _moonLight.LightEnergy = sunIntensity < 0.1f ? 0.45f : 0f;

        // 环境光
        if (_worldEnv?.Environment != null)
        {
            float ambEnergy = sunIntensity * 0.5f + 0.38f; // 最低环境光0.38保证夜间清晰可见
            _worldEnv.Environment.AmbientLightEnergy = ambEnergy;

            // 天空颜色
            if (_worldEnv.Environment.Sky?.SkyMaterial is ProceduralSkyMaterial sky)
            {
                if (_timeOfDay >= 5 && _timeOfDay < 7)
                {
                    // 黎明
                    sky.SkyTopColor = new Color(0.2f, 0.3f, 0.5f).Lerp(new Color(0.25f, 0.5f, 0.85f), (_timeOfDay - 5) / 2f);
                    sky.SkyHorizonColor = new Color(0.8f, 0.5f, 0.3f).Lerp(new Color(0.55f, 0.65f, 0.85f), (_timeOfDay - 5) / 2f);
                }
                else if (_timeOfDay >= 7 && _timeOfDay < 17)
                {
                    // 白天
                    sky.SkyTopColor = new Color(0.25f, 0.5f, 0.85f);
                    sky.SkyHorizonColor = new Color(0.55f, 0.65f, 0.85f);
                }
                else if (_timeOfDay >= 17 && _timeOfDay < 19)
                {
                    // 黄昏
                    sky.SkyTopColor = new Color(0.25f, 0.5f, 0.85f).Lerp(new Color(0.1f, 0.1f, 0.3f), (_timeOfDay - 17) / 2f);
                    sky.SkyHorizonColor = new Color(0.55f, 0.65f, 0.85f).Lerp(new Color(0.8f, 0.3f, 0.2f), (_timeOfDay - 17) / 2f);
                }
                else
                {
                    // 夜晚
                    sky.SkyTopColor = new Color(0.02f, 0.02f, 0.08f);
                    sky.SkyHorizonColor = new Color(0.05f, 0.05f, 0.15f);
                }
            }
        }
    }

    // ======== 灾害系统 ========

    private void ProcessDisasters(float dt)
    {
        if (!string.IsNullOrEmpty(_currentDisaster))
        {
            _disasterAge += dt;
            if (_disasterAge >= _disasterDuration)
            {
                EndDisaster();
            }
            else
            {
                ApplyDisasterEffects(dt);
            }
        }
        else
        {
            _disasterTimer -= dt;
            if (_disasterTimer <= 0)
            {
                TriggerRandomDisaster();
                _disasterTimer = 90 + new Random().Next(60);
            }
        }
    }

    private void TriggerRandomDisaster()
    {
        var rng = new Random();
        int type = rng.Next(3);
        _currentDisaster = type switch
        {
            0 => "闪电风暴",
            1 => "地震",
            2 => "暴雨",
            _ => "闪电风暴",
        };
        _disasterDuration = 15 + rng.Next(15);
        _disasterAge = 0;
        ShowToast($"⚠ 灾害来袭：{_currentDisaster}！");

        switch (_currentDisaster)
        {
            case "闪电风暴":
                _sunLight.LightEnergy = 0.05f;
                break;
            case "地震":
                break;
            case "暴雨":
                if (_worldEnv?.Environment != null)
                {
                    _worldEnv.Environment.FogDensity = 0.015f;
                    _worldEnv.Environment.FogLightEnergy = 0.2f;
                }
                break;
        }
    }

    private void ApplyDisasterEffects(float dt)
    {
        switch (_currentDisaster)
        {
            case "闪电风暴":
                // 随机闪电打击
                if (new Random().NextDouble() < 0.1)
                {
                    var rng = new Random();
                    var pos = new Vector3(
                        rng.Next(0, (int)TerrainGrid3D.MapWorldSize),
                        0,
                        rng.Next(0, (int)TerrainGrid3D.MapWorldSize));
                    // 随机伤害附近单位
                    foreach (var u in GetAllUnits())
                    {
                        if (u.GlobalPosition.DistanceTo(pos) < 5f)
                            u.TakeDamage(30);
                    }
                    SpawnExplosion(pos, 3f);
                }
                break;
            case "地震":
                // 相机抖动
                var camPos = _camera.Position;
                _camera.Position = new Vector3(
                    camPos.X + (float)(new Random().NextDouble() - 0.5) * 0.5f,
                    camPos.Y + (float)(new Random().NextDouble() - 0.5) * 0.3f,
                    camPos.Z + (float)(new Random().NextDouble() - 0.5) * 0.5f);
                // 建筑受损
                if (new Random().NextDouble() < 0.02)
                {
                    var allBldgs = GetAllBuildings();
                    if (allBldgs.Count > 0)
                        allBldgs[new Random().Next(allBldgs.Count)].TakeDamage(20);
                }
                break;
            case "暴雨":
                // 减少视野（简化：不需要额外代码，雾效已在触发时设置）
                break;
        }
    }

    private void EndDisaster()
    {
        ShowToast($"{_currentDisaster} 已结束");
        _currentDisaster = "";

        // 恢复正常
        if (_worldEnv?.Environment != null)
        {
            _worldEnv.Environment.FogDensity = 0.003f;
            _worldEnv.Environment.FogLightEnergy = 0.3f;
        }
    }

    // ======== 截图 ========

    private void TakeScreenshot()
    {
        var img = GetViewport().GetTexture().GetImage();
        string path = System.IO.Path.Combine(ShotDir, $"shot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        img.SavePng(path);
        GD.Print($"[Main3D] Screenshot saved: {path}");
    }

    // ======== _Process ========
 
    private float _autoShotTimer;
    private int _autoShotIndex;
 
    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // 自动截图（15秒、30秒、45秒、60秒、75秒、90秒、120秒）
        _autoShotTimer += dt;
        int[] shotTimes = { 15, 30, 45, 60, 75, 90, 120 };
        if (_autoShotIndex < shotTimes.Length && _autoShotTimer >= shotTimes[_autoShotIndex])
        {
            _autoShotIndex++;
            CallDeferred(nameof(TakeScreenshot));
        }

        // 相机
        ProcessCamera(dt);

        // 缓存刷新：每0.5秒强制刷新一次，确保中途死亡的单位被排除
        _cacheTimer += dt;
        if (_cacheTimer >= 0.5f)
        {
            _cacheTimer = 0;
            _unitsCacheDirty = true;
            _buildingsCacheDirty = true;
        }

        // 昼夜
        ProcessDayNight(dt);

        // 灾害
        ProcessDisasters(dt);

        // 战斗特效动画更新
        UpdateActiveFx(dt);

        // AI保护期递减
        if (Unit3D.AiGraceRemaining > 0)
            Unit3D.AiGraceRemaining -= dt;

        // AI回合
        for (int i = 1; i <= AiTeamCount; i++)
        {
            if (i > _activeAiCount) continue;
            _aiThinkTimers[i] -= dt;
            if (_aiThinkTimers[i] <= 0)
            {
                _aiThinkTimers[i] = _difficulty switch
                {
                    Difficulty.Easy => 6f,
                    Difficulty.Normal => 5f,
                    Difficulty.Hard => 4f,
                    Difficulty.Brutal => 3f,
                    _ => 6f,
                };
                AITickForTeam(i);
            }

            // AI超武冷却
            _aiNukeCooldowns[i] -= dt;
            _aiLightningCooldowns[i] -= dt;
            _aiMissileCooldowns[i] -= dt;

            // AI释放超武
            if (HasBuilding(i, Building3D.BuildingType.NukeSilo) && _aiNukeCooldowns[i] <= 0)
            {
                var target = FindSuperweaponTarget(i);
                if (target.HasValue)
                {
                    ApplyNuke(target.Value, i);
                    _aiNukeCooldowns[i] = NukeCooldown;
                }
            }
            if (HasBuilding(i, Building3D.BuildingType.LightningTower) && _aiLightningCooldowns[i] <= 0)
            {
                var target = FindSuperweaponTarget(i);
                if (target.HasValue)
                {
                    ApplyLightning(target.Value, i);
                    _aiLightningCooldowns[i] = LightningCooldown;
                }
            }
        }

        // 玩家超武冷却
        _playerNukeCooldown -= dt;
        _playerLightningCooldown -= dt;
        _playerMissileCooldown -= dt;

        // 超武特效更新
        for (int i = _nukeEffects.Count - 1; i >= 0; i--)
        {
            var nuke = _nukeEffects[i];
            nuke.Age += dt;
            if (nuke.Age >= nuke.Lifetime)
                _nukeEffects.RemoveAt(i);
            else
                _nukeEffects[i] = nuke;
        }

        for (int i = _lightningEffects.Count - 1; i >= 0; i--)
        {
            var lit = _lightningEffects[i];
            lit.Age += dt;
            lit.DamageTimer += dt;
            if (lit.DamageTimer >= 1f)
            {
                lit.DamageTimer = 0;
                // 每秒造成伤害
                foreach (var u in GetAllUnits())
                {
                    if (u.TeamId == lit.TeamId || u._isDead) continue;
                    if (u.GlobalPosition.DistanceTo(lit.Pos) < 10f)
                        u.TakeDamage(80);
                }
                foreach (var b in GetAllBuildings())
                {
                    if (b.TeamId == lit.TeamId || b._isDead) continue;
                    if (b.GlobalPosition.DistanceTo(lit.Pos) < 10f)
                        b.TakeDamage(80);
                }
            }
            if (lit.Age >= lit.Lifetime)
                _lightningEffects.RemoveAt(i);
            else
                _lightningEffects[i] = lit;
        }

        // 胜负判定（每2秒检查一次）
        _winCheckTimer += dt;
        if (_winCheckTimer >= 2f)
        {
            _winCheckTimer = 0;
            CheckWinCondition();
        }

        // UI更新
        UpdateUI();
    }

    private float _winCheckTimer;

    private Vector3? FindSuperweaponTarget(int firingTeamId)
    {
        // 50%优先打玩家基地
        if (firingTeamId != PlayerTeamId && GD.RandRange(0.0, 1.0) < 0.5
            && _bases.TryGetValue(PlayerTeamId, out var playerBase) && !playerBase._isDead)
        {
            return playerBase.GlobalPosition;
        }
        // 随机敌方基地
        var targets = GetAllBuildings()
            .Where(b => b.TeamId != firingTeamId && !b._isDead && b.Type == Building3D.BuildingType.Base)
            .ToList();
        if (targets.Count == 0) return null;
        return targets[(int)GD.RandRange(0, targets.Count - 1)].GlobalPosition;
    }

    public TerrainGrid3D GetTerrainGrid() => _terrain;
}
