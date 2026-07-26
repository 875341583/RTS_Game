using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RTSGame;

/// <summary>
/// 主游戏控制器
/// · 蓝方（玩家）：手动控制，矿车自动采矿，可花钱造坦克/矿车
/// · 红方（AI）：AutoAI 自动战斗，定时造坦克推进
/// · 胜利条件：摧毁对方所有单位和建筑
/// </summary>
public partial class Main : Node2D
{
    private RTSCamera _camera = null!;
    private Node2D _unitsNode = null!;
    private Node2D _buildingsNode = null!;
    private Node2D _resourcesNode = null!;

    private Node2D _obstaclesNode = null!;
    private Node2D _strategicPointsNode = null!;
    private Sprite2D _groundSprite = null!;

    // Q6：事件通知系统
    private VBoxContainer _toastContainer = null!;
    private readonly List<ToastEntry> _activeToasts = new();
    private class ToastEntry { public Label Label = null!; public float Lifetime; public float Age; }
    private Label _startOverlay = null!;
    private float _startOverlayAge;

    private Line2D _dragBox = null!;
    private Label _uiLabel = null!;
    private Label _hintLabel = null!;

    // 选中集合（统一存放 Unit 和 Building）
    private readonly List<GodotObject> _selected = new();
    private bool _isDragging;
    private Vector2 _dragStart;

    // 资金
    /// <summary>玩家阵营固定为 0；阵营 1..(AiTeamCount) 为 AI 阵营。总阵营数 = AiTeamCount + 1。</summary>
    private const int AiTeamCount = 7;
    /// <summary>总阵营数（8）。与 Unit.TeamPalette 长度对应。</summary>
    private const int TotalTeamCount = 8;
    /// <summary>玩家阵营 ID 固定为 0。</summary>
    private const int PlayerTeamId = 0;

    // 资金：玩家 2500，每个 AI 2000
    private readonly int[] _money = new int[TotalTeamCount] { 2500, 2000, 2000, 2000, 2000, 2000, 2000, 2000 };
    private const int LightTankCost = 200;
    private const int HeavyTankCost = 500;
    private const int ArtilleryCost = 400;
    private const int HarvesterCost = 500;
    private const int InfantryCost = 100;
    private const int AntiAirCost = 300;
    private const int EngineerCost = 300;
    // E4：工程单位造价
    private const int SapperCost = 150;
    private const int ChiefEngineerCost = 400;
    // E6：新步兵造价
    private const int GrenadierCost = 200;
    private const int SniperCost = 250;
    private const int FlameInfantryCost = 180;
    private const int TransportCost = 400;
    // E6b：特殊单位造价
    private const int HeroCost = 600;
    private const int SpyCost = 500;
    private const int ThiefCost = 300;
    // E7：空军造价
    private const int FighterCost = 500;
    private const int HelicopterCost = 600;
    private const int RocketInfantryCost = 350;
    // E8：扩展空军造价
    private const int BomberCost = 800;
    private const int ScoutCost = 300;
    private const int TransportHeliCost = 600;
    // E9：海军造价
    private const int DestroyerCost = 500;
    private const int SubmarineCost = 600;
    private const int AircraftCarrierCost = 1200;
    private const int LandingCraftCost = 400;
    private const int ShipyardCost = 900;
    // E10：超武建筑造价
    private const int NukeSiloCost = 1500;
    private const int LightningTowerCost = 1500;
    private const int MissileSiloCost = 1200;
    private const int MaxUnitsPerTeam = 20;
    private const int PowerPlantCost = 300;
    private const int BarracksCost = 400;
    private const int WarFactoryCost = 600;
    private const int TechCenterCost = 800;
    // 阶段12-A1+A2：新建筑造价
    private const int TurretCost = 400;
    private const int AntiAirTurretCost = 600;
    private const int RepairPadCost = 500;
    // E7：机场造价
    private const int AirfieldCost = 700;
    private const int RocketLauncherCost = 600;
    private const int MissileTankCost = 800;

    // 场景预载
    private PackedScene _unitScene = null!;
    private PackedScene _harvesterScene = null!;
    private PackedScene _buildingScene = null!;
    private PackedScene _oreScene = null!;

    // 基地引用（8 阵营）
    private readonly Dictionary<int, Building> _bases = new();
    /// <summary>获取玩家基地（兼容旧引用）。</summary>
    private Building? PlayerBase => _bases.GetValueOrDefault(PlayerTeamId);
    /// <summary>获取所有 AI 阵营 ID。</summary>
    private static IEnumerable<int> AiTeamIds => Enumerable.Range(1, AiTeamCount);

    // 红方 AI 节奏
    private float _enemyThinkTimer = 8f;
    private float _blueAITimer = 6f;
    private int _blueCaptureCounter;
    // 8 阵营 AI 占领战略点计时器（key=teamId，value=计数）
    private readonly Dictionary<int, int> _aiCaptureCounters = new();
    private float _debugTimer = 10f;
    private bool _gameOver;
    private float _gameOverDelay = -1f; // >0 = 等待显示结束UI

    // ---- P5 难度分级 ----
    public enum Difficulty { Easy, Normal, Hard, Brutal }
    private Difficulty _difficulty = Difficulty.Normal;
    private float _aiThinkInterval = 8f;
    private int _aiStartMoney = 2000;
    private int _blueStartMoney = 2500;
    private int _aiStartHarvesters = 3;
    private bool _aiUsesTech = true;
    private bool _aiCapturesPoints = true;
    private int _unitCap = 20;
    private int _playerTechLevel = 3;
    public bool StrategicPointIncomeEnabled { get; private set; } = true;
    private string _gameResult = "";
    // 每个阵营的建筑索引（生成环形布局用）
    private readonly Dictionary<int, int> _buildIndices = new();
    private BuildPanel _buildPanel = null!;
    private Minimap _minimap = null!;
    private BuildingType? _placementMode;
    private bool _f12ShotDown = false; // F12 截图按键状态（用于验收渲染）
    private float _autoshotTimer = 0f; // 自动截图计时器（验收用）
    /// <summary>全景截图倒数帧：在 autoshot 触发后切换全景相机，等待几帧渲染稳定再截图。</summary>
    private int _panoramaShotPending = 0;
    /// <summary>autoshot 阶段：0=未开始, 1=已拍全景, 2=已拍地表特写。每阶段切换不同相机位置+zoom。</summary>
    private int _autoshotPhase = 0;
    /// <summary>当前待截图的文件名后缀（多阶段截图用）。</summary>
    private string _pendingShotSuffix = "autoshot";
        /// <summary>AI保护期结束通知是否已发出。</summary>
        private bool _aiGraceEndedNotified = false;
        /// <summary>活跃AI数量（剩余AI休眠不发展不进攻）。
        /// 各难度取值：Easy=2 / Normal=4 / Hard=6 / Brutal=7。
        /// teamId 1.._activeAiCount 为活跃AI；teamId (_activeAiCount+1)..AiTeamCount 为休眠AI。</summary>
        private int _activeAiCount = 7;
        /// <summary>休眠AI的初始战斗单位是否禁用 AutoAI（True=完全静止，便于玩家集中应对活跃AI）。</summary>
        private const bool DormantAiAutoAi = false;

        // ---- 阶段12-A4 超武系统（核弹） ----
        /// <summary>核弹冷却总时长（秒）。5分钟。</summary>
        private const float NukeCooldownDuration = 300f;
        /// <summary>核弹爆炸半径（像素）。</summary>
        private const float NukeRadius = 260f;
        /// <summary>核弹爆炸伤害（点）。</summary>
        private const float NukeDamage = 600f;
        /// <summary>玩家核弹冷却剩余（秒）。≤0 表示可发射。</summary>
        private float _playerNukeCooldown = 0f;
        /// <summary>玩家是否处于核弹目标选择模式（按 N 进入，左键释放 / 右键取消）。</summary>
        private bool _nukeTargetMode = false;
        /// <summary>每个 AI 阵营的核弹冷却（key=teamId，value=剩余秒数）。仅在拥有科技中心后生效。</summary>
        private readonly Dictionary<int, float> _aiNukeCooldowns = new();
        /// <summary>核弹特效播放列表（持续若干秒的冲击波+辐射雾）。</summary>
        private readonly List<NukeVisual> _activeNukeVisuals = new();
        /// <summary>核弹视觉特效临时数据。</summary>
        private struct NukeVisual
        {
            public Vector2 Position;
            public float Age;
            public float Lifetime;
        }

        // ---- 阶段12-A4 超武系统（闪电风暴） ----
        /// <summary>闪电风暴冷却总时长（秒）。4分钟（比核弹短）。</summary>
        private const float LightningCooldownDuration = 240f;
        /// <summary>闪电风暴作用半径（像素）。比核弹小。</summary>
        private const float LightningRadius = 160f;
        /// <summary>闪电风暴每秒伤害（点/秒）。</summary>
        private const float LightningDps = 80f;
        /// <summary>闪电风暴持续时间（秒）。在此期间持续对范围内敌方造成伤害。</summary>
        private const float LightningDuration = 5f;
        /// <summary>玩家闪电风暴冷却剩余（秒）。</summary>
        private float _playerLightningCooldown = 0f;
        /// <summary>玩家是否处于闪电风暴目标选择模式（按 C 进入，左键释放 / 右键取消）。</summary>
        private bool _lightningTargetMode = false;
        /// <summary>每个 AI 阵营的闪电风暴冷却。</summary>
        private readonly Dictionary<int, float> _aiLightningCooldowns = new();
    /// <summary>活跃闪电风暴特效列表（持续伤害区域）。每秒对范围内敌方造成 LightningDps 伤害。</summary>
    private readonly List<LightningVisual> _activeLightnings = new();
    /// <summary>闪电风暴视觉与持续伤害数据。DamageTickTimer 累积到1.0即结算一次伤害。</summary>

    // E10：巡航导弹超武
    private const float MissileCooldownDuration = 180f;
    private const float MissileRadius = 180f;
    private const float MissileDamage = 300f;
    private float _playerMissileCooldown = 0f;
    private bool _missileTargetMode = false;
    private readonly Dictionary<int, float> _aiMissileCooldowns = new();

    // G7: AI间谍任务冷却（tick计数）
    private readonly Dictionary<int, int> _aiSpyCooldowns = new();
        private struct LightningVisual
        {
            public Vector2 Position;
            public int FiringTeamId;
            public float Age;            // 已持续时间
            public float Lifetime;       // 总持续时间（5秒）
            public float DamageTickTimer; // 每秒伤害累计器
            public float BoltRefreshTimer; // 闪电形状刷新计时
        }
        // 阶段12-A4 闪电柱形状种子（用于绘制随机折线）
        private float _lightningBoltSeed;

    // ---- 阶段12-B 地图系统（文明6式种子制度） ----
    /// <summary>地图种子。同一种子+难度=完全相同的地图。0=随机生成。</summary>
    private ulong _mapSeed = 0;
    /// <summary>地图 RNG（基于种子初始化，所有地图生成共用此实例保证可复现）。</summary>
    private Random _mapRng = new(42);
    /// <summary>地图大小常量（像素）。阵营基地分布在 200..(MapSize-200) 范围内。</summary>
    private const float MapSize = 2000f;

    // ---- 阶段12-C 音效系统 ----
    private AudioManager _audio = null!;

    // ---- E1 地形系统 ----
    private TerrainGrid _terrain = null!;
    /// <summary>获取地形网格（供Unit等查询速度修正和通行性）。</summary>
    public TerrainGrid GetTerrainGrid() => _terrain;

    // ---- P0-1: A*寻路系统 ----
    private PathFinder? _pathFinder;
    /// <summary>获取全局PathFinder实例（可能为null，调用方需判空）。</summary>
    public PathFinder? GetPathFinder() => _pathFinder;

    // G1 操控增强
    private readonly Dictionary<int, List<Unit>> _squads = new();
    private bool _attackMoveMode;
    // E4：键盘防抖
    private Key _prevKeyState = Key.None;

    // G1: 科技分支树
    private readonly TechProgress[] _techProgress = new TechProgress[8]; // 每阵营一个
    private bool _techTreePanelVisible = false;
    private Label _techTreeLabel = null!;
    private float _aiTechTimer = 0f;
    private float _techAutoRepairTimer = 0f;

    // G2: 时代系统
    private readonly EraProgress[] _eraProgress = new EraProgress[8]; // 每阵营一个
    private bool _eraPanelVisible = false;
    private Label _eraLabel = null!;
    private float _aiEraTimer = 0f;

    // G3: 战术卡系统
    private TacticalCards.CardId? _playerCard = null;
    private readonly TacticalCards.CardId?[] _aiCards = new TacticalCards.CardId?[7];
    private bool _cardSelectionPending = true;
    private float _cardSelectionTimer = 5f; // 游戏开始5秒后弹出
    private TacticalCards.CardId[] _cardChoices = System.Array.Empty<TacticalCards.CardId>();
    private Label _cardLabel = null!;
    private Label _cardStatusLabel = null!;

    // G4: 电网分区
    private bool _powerGridPanelVisible = false;
    private Label _powerGridLabel = null!;
    private float _powerGridRefreshTimer = 0f;

    // G5: 尤里卡时刻
    private readonly EurekaSystem.TeamEureka[] _eureka = new EurekaSystem.TeamEureka[8];
    private Label _eurekaLabel = null!;

    // G6: 邻接加成
    private Label _adjacencyLabel = null!;
    private bool _adjacencyPanelVisible = false;

    // G7: 间谍任务面板
    private Label _spyMissionLabel = null!;
    private bool _spyMissionPanelVisible = false;

    // G8: 占领面板
    private Label _captureLabel = null!;
    private bool _capturePanelVisible = false;

    /// <summary>L1修复: 检查是否有任何信息面板处于打开状态（面板打开时禁用生产热键避免冲突）。</summary>
    private bool AnyPanelOpen()
    {
        return _techTreePanelVisible || _eraPanelVisible || _powerGridPanelVisible
            || _adjacencyPanelVisible || _spyMissionPanelVisible || _capturePanelVisible
            || _eurekaLabel.Visible || _cardLabel.Visible;
    }

    public override void _Ready()
    {
        // R7: 画质分级 — 自动检测GPU并设置渲染参数
        QualitySettings.AutoDetect();

        // P5：解析难度参数（--difficulty=easy/normal/hard/brutal）
        // 优先命令行参数（headless 测试用），否则用菜单选择（GameSession）
        _difficulty = GameSession.SelectedDifficulty;
        var args = OS.GetCmdlineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.StartsWith("--difficulty", StringComparison.OrdinalIgnoreCase))
            {
                string val = a.Contains('=') ? a.Split('=')[1] : "";
                _difficulty = val.ToLowerInvariant() switch
                {
                    "easy" or "0" => Difficulty.Easy,
                    "normal" or "1" => Difficulty.Normal,
                    "hard" or "2" => Difficulty.Hard,
                    "brutal" or "3" => Difficulty.Brutal,
                    _ => _difficulty
                };
            }
            if (a.StartsWith("--seed", StringComparison.OrdinalIgnoreCase))
            {
                string val = a.Contains('=') ? a.Split('=')[1] : "";
                if (ulong.TryParse(val, out var parsedSeed))
                    _mapSeed = parsedSeed;
            }
        }
        // 如果命令行没指定种子，从 GameSession 获取（主菜单输入）
        if (_mapSeed == 0)
            _mapSeed = GameSession.MapSeed;
        // 如果仍然为 0，随机生成一个种子
        if (_mapSeed == 0)
            _mapSeed = (ulong)DateTime.Now.Ticks;
        _mapRng = new Random((int)(_mapSeed & 0x7FFFFFFF));
        GD.Print($"[Map] 种子 {_mapSeed}（可用 --seed={_mapSeed} 复现本张地图）");

        ApplyDifficultyConfig();

        _camera = GetNode<RTSCamera>("Camera2D");
        _unitsNode = GetNode<Node2D>("Units");
        _buildingsNode = GetNode<Node2D>("Buildings");
        _resourcesNode = GetNode<Node2D>("Resources");
        _dragBox = GetNode<Line2D>("DragBox");
        _uiLabel = GetNode<Label>("UI/Label");
        _hintLabel = GetNode<Label>("UI/HintLabel");

        _unitScene = GD.Load<PackedScene>("res://scenes/Unit.tscn");
        _harvesterScene = GD.Load<PackedScene>("res://scenes/Harvester.tscn");
        _buildingScene = GD.Load<PackedScene>("res://scenes/Building.tscn");
        _oreScene = GD.Load<PackedScene>("res://scenes/ResourceNode.tscn");
        _dragBox.Visible = false;

        // 地形容器（程序化创建，不修改场景文件）
        _obstaclesNode = new Node2D { Name = "Obstacles" };
        AddChild(_obstaclesNode);
        _strategicPointsNode = new Node2D { Name = "StrategicPoints" };
        AddChild(_strategicPointsNode);

        // Q4：地面纹理（草地+道路+泥地）→ E1：地形系统驱动
        _terrain = new TerrainGrid();
        _terrain.GenerateFromSeed(_mapSeed);
        var stats = _terrain.GetStats();
        GD.Print("[Terrain] 地形生成统计：");
        foreach (var kv in stats)
            GD.Print($"  {kv.Key}: {kv.Value}格");
        CreateGround();

        // P0-1: 创建A*寻路器（基于地形栅格）
        _pathFinder = new PathFinder(_terrain);
        GD.Print("[PathFinder] A*寻路器已创建");

        // P0-2修复(headless既有bug): 提前实例化尤里卡计数器。
        // 原代码在 _Ready 末尾(line 570)才实例化 _eureka[i]，但 line 398 的 SpawnBuilding 循环
        // 会通过 OnEurekaBuild → _eureka[teamId].OnBuild() 触发 NRE，导致 _Ready 中断、
        // UI Label 未初始化，进而 _Process 每帧报 AnyPanelOpen() NRE。提前实例化消除竞态。
        for (int i = 0; i < _eureka.Length; i++)
            if (_eureka[i] == null) _eureka[i] = new EurekaSystem.TeamEureka();

        // ---- 初始化 8 阵营 ----
        // 阵营起始位置：等距坐标下的网格位置 → 等距屏幕坐标
        // 网格坐标系仍是32×32，转为等距屏幕坐标后视觉上呈菱形分布
        var teamGridPositions = new (int gx, int gy)[TotalTeamCount]
        {
            (1, 1),         // 0 玩家（左上角）
            (30, 30),       // 1 AI（右下角）
            (30, 1),        // 2 AI（右上角）
            (1, 30),        // 3 AI（左下角）
            (16, 1),        // 4 AI（顶部中央）
            (16, 30),       // 5 AI（底部中央）
            (1, 16),        // 6 AI（左侧中央）
            (30, 16),       // 7 AI（右侧中央）
        };
        var teamStartPositions = new Vector2[TotalTeamCount];
        for (int i = 0; i < TotalTeamCount; i++)
            teamStartPositions[i] = IsoCoords.GridToScreen(teamGridPositions[i].gx, teamGridPositions[i].gy);

        for (int teamId = 0; teamId < TotalTeamCount; teamId++)
        {
            var basePos = teamStartPositions[teamId];
            var baseBuilding = SpawnBuilding(BuildingType.Base, basePos, teamId);
            _bases[teamId] = baseBuilding;

            if (teamId == PlayerTeamId)
            {
                // 玩家方：3 矿车起步，2 坦克 1 重坦 1 轻坦（玩家手动操控）
                SpawnHarvester(basePos + new Vector2(-40, 70), teamId, baseBuilding);
                SpawnHarvester(basePos + new Vector2(50, 70), teamId, baseBuilding);
                SpawnHarvester(basePos + new Vector2(0, 110), teamId, baseBuilding);
                SpawnUnit(UnitType.LightTank, basePos + new Vector2(100, -20), teamId, autoAI: false);
                SpawnUnit(UnitType.HeavyTank, basePos + new Vector2(130, 20), teamId, autoAI: false);
                SpawnUnit(UnitType.LightTank, basePos + new Vector2(80, 60), teamId, autoAI: false);
            }
            else
            {
                // AI 方：N 矿车起步 + 1 重坦 1 轻坦
                // 活跃AI（teamId ≤ _activeAiCount）开放 AutoAI 主动进攻
                // 休眠AI（teamId > _activeAiCount）禁用 AutoAI 静止原地不主动进攻
                bool isActiveAi = teamId <= _activeAiCount;
                for (int i = 0; i < _aiStartHarvesters; i++)
                    SpawnHarvester(basePos + new Vector2(-40 + i * 40, 70), teamId, baseBuilding);
                SpawnUnit(UnitType.HeavyTank, basePos + new Vector2(-100, -20), teamId, autoAI: isActiveAi);
                SpawnUnit(UnitType.LightTank, basePos + new Vector2(-130, 20), teamId, autoAI: isActiveAi);
                if (!isActiveAi)
                    GD.Print($"[Difficulty] Team {teamId} 处于休眠状态（不发展不主动进攻）");
            }

            // 每个阵营基地附近自动生成 2 个近矿（位置由种子随机偏移，保证起步经济）
            float oreAngle1 = (float)(_mapRng.NextDouble() * Mathf.Pi * 2);
            float oreAngle2 = oreAngle1 + Mathf.Pi * 0.7f + (float)(_mapRng.NextDouble() * 0.5f);
            float oreDist1 = 180f + (float)(_mapRng.NextDouble() * 60f);
            float oreDist2 = 240f + (float)(_mapRng.NextDouble() * 80f);
            SpawnOre(basePos + new Vector2(Mathf.Cos(oreAngle1) * oreDist1, Mathf.Sin(oreAngle1) * oreDist1), 800);
            SpawnOre(basePos + new Vector2(Mathf.Cos(oreAngle2) * oreDist2, Mathf.Sin(oreAngle2) * oreDist2), 800);
        }

        // 中场争夺矿 + 中央高价值矿（位置由种子随机化，但保持围绕地图中央分布）
        GenerateRandomOreDeposits();

        // E5 资源扩展：油田/稀有矿/陆地矿脉
        GenerateOilFields();
        GenerateRareMinerals();
        GenerateLandVeins();

        // ---- 地形障碍物（种子驱动） ----
        GenerateRandomObstacles();

        // ---- 战略要地（中央固定 + 侧翼种子偏移） ----
        GenerateStrategicPoints();

        // Q1：侧边栏建造面板
        _buildPanel = new BuildPanel();
        _buildPanel.DifficultyName = _difficulty.ToString();
        GetNode<CanvasLayer>("UI").AddChild(_buildPanel);
        _buildPanel.BuildBuildingRequested += (bt) => TryBuildBuilding(bt);
        _buildPanel.BuildUnitRequested += (ut) => TrySpawnUnit(ut, GetUnitCost(ut));
        _buildPanel.BuildHarvesterRequested += () => TrySpawnHarvester();
        GD.Print("[UI] 侧边栏建造面板已加载");

        // 阶段12-C：音效系统初始化 + BGM
        _audio = new AudioManager();
        AddChild(_audio);
        _audio.StartBgm();

        // Q2：小地图
        _minimap = new Minimap();
        _minimap.Setup(this, _camera);
        GetNode<CanvasLayer>("UI").AddChild(_minimap);
        // 调整提示标签位置，避免与小地图重叠
        _hintLabel.OffsetLeft = 200f;
        GD.Print("[UI] 小地图已加载");

        // Q6：开局目标提示（画面内覆盖）
        _startOverlayAge = 0f;
        string graceHint = Unit.AiGraceRemaining > 0f
            ? $"★ AI保护期：前{(int)Unit.AiGraceRemaining}秒AI不会主动进攻，抓紧发展！\n"
            : "";
        int dormantCount = AiTeamCount - _activeAiCount;
        string activeHint = dormantCount > 0
            ? $"★ 对手：{_activeAiCount}个活跃AI阵营（共{AiTeamCount}个，{dormantCount}个休眠不主动进攻）\n"
            : $"★ 对手：{_activeAiCount}个AI阵营全部活跃\n";
        _startOverlay = new Label
        {
            Text = "★ 游戏目标：摧毁敌方所有建筑和单位即获胜！\n" +
                   "★ 建造建议：电站→兵营→车厂→科技中心\n" +
                   "★ 矿车自动采矿，选中基地可生产更多矿车($500)\n" +
                   "★ E5 油田：战斗单位停留4秒占领，占领后每秒产$8\n" +
                   "★ E5 稀有矿(紫色)：矿车采集收益×2 | 陆地矿脉：散布广储值低\n" +
                   activeHint +
                   graceHint +
                   "★ 选中单位右键点敌方建筑/单位攻击\n" +
                   "★ 选中建筑右键设集结点 | R维修 | V出售\n" +
                   "★ ☢ 建造科技中心后按 Z 可发射核弹（5分钟冷却）\n" +
                   "★ ⚡ 按 C 释放闪电风暴（持续5秒范围伤害/4分钟冷却）\n" +
                   $"★ 地图种子: {_mapSeed}（--seed={_mapSeed} 可复现本张地图）",
        };
        _startOverlay.HorizontalAlignment = HorizontalAlignment.Center;
        _startOverlay.SetAnchorsPreset(Control.LayoutPreset.Center);
        _startOverlay.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.4f));
        _startOverlay.AddThemeFontSizeOverride("font_size", 22);
        _startOverlay.AddThemeConstantOverride("shadow_offset_x", 2);
        _startOverlay.AddThemeConstantOverride("shadow_offset_y", 2);
        _startOverlay.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        GetNode<CanvasLayer>("UI").AddChild(_startOverlay);

        // Q6：事件通知容器
        _toastContainer = new VBoxContainer();
        _toastContainer.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _toastContainer.OffsetLeft = -200f;
        _toastContainer.OffsetRight = 200f;
        GetNode<CanvasLayer>("UI").AddChild(_toastContainer);

        // G1: 初始化科技树进度 + 科技树UI面板
        for (int i = 0; i < 8; i++) _techProgress[i] = new TechProgress();
        _techTreeLabel = new Label();
        _techTreeLabel.Name = "TechTreeLabel";
        _techTreeLabel.Position = new Vector2(180, 80);
        _techTreeLabel.Size = new Vector2(580, 380);
        _techTreeLabel.Modulate = new Color(0.9f, 0.95f, 1f, 0.95f);
        _techTreeLabel.AddThemeFontSizeOverride("font_size", 13);
        _techTreeLabel.Visible = false;
        _techTreeLabel.Text = "";
        GetNode<CanvasLayer>("UI").AddChild(_techTreeLabel);
        GD.Print("[G1] 科技树系统初始化完成 — 按Tab打开科技面板");

        // G2: 初始化时代系统进度 + 时代面板
        for (int i = 0; i < 8; i++) _eraProgress[i] = new EraProgress();
        _eraLabel = new Label();
        _eraLabel.Name = "EraLabel";
        _eraLabel.Position = new Vector2(180, 80);
        _eraLabel.Size = new Vector2(580, 300);
        _eraLabel.Modulate = new Color(0.95f, 0.9f, 1f, 0.95f);
        _eraLabel.AddThemeFontSizeOverride("font_size", 14);
        _eraLabel.Visible = false;
        _eraLabel.Text = "";
        GetNode<CanvasLayer>("UI").AddChild(_eraLabel);
        GD.Print("[G2] 时代系统初始化完成 — 按Y打开时代面板");

        // G3: 初始化战术卡面板
        _cardLabel = new Label();
        _cardLabel.Name = "CardLabel";
        _cardLabel.Position = new Vector2(180, 100);
        _cardLabel.Size = new Vector2(580, 350);
        _cardLabel.Modulate = new Color(1f, 0.95f, 0.8f, 0.97f);
        _cardLabel.AddThemeFontSizeOverride("font_size", 14);
        _cardLabel.Visible = false;
        _cardLabel.Text = "";
        GetNode<CanvasLayer>("UI").AddChild(_cardLabel);

        _cardStatusLabel = new Label();
        _cardStatusLabel.Name = "CardStatusLabel";
        _cardStatusLabel.Position = new Vector2(770, 70);
        _cardStatusLabel.Size = new Vector2(200, 60);
        _cardStatusLabel.Modulate = new Color(1f, 0.9f, 0.5f, 0.9f);
        _cardStatusLabel.AddThemeFontSizeOverride("font_size", 12);
        _cardStatusLabel.Visible = false;
        GetNode<CanvasLayer>("UI").AddChild(_cardStatusLabel);
        GD.Print("[G3] 战术卡系统初始化完成 — 游戏开始5秒后选择战术卡");

        // G4: 初始化电网分区面板
        _powerGridLabel = new Label();
        _powerGridLabel.Name = "PowerGridLabel";
        _powerGridLabel.Position = new Vector2(180, 80);
        _powerGridLabel.Size = new Vector2(580, 350);
        _powerGridLabel.Modulate = new Color(0.9f, 1f, 0.9f, 0.95f);
        _powerGridLabel.AddThemeFontSizeOverride("font_size", 13);
        _powerGridLabel.Visible = false;
        _powerGridLabel.Text = "";
        GetNode<CanvasLayer>("UI").AddChild(_powerGridLabel);
        GD.Print("[G4] 电网分区系统初始化完成 — 按G查看电网分布");

        // G5: 初始化尤里卡系统
        for (int i = 0; i < 8; i++) _eureka[i] = new EurekaSystem.TeamEureka();
        _eurekaLabel = new Label();
        _eurekaLabel.Name = "EurekaLabel";
        _eurekaLabel.Position = new Vector2(770, 130);
        _eurekaLabel.Size = new Vector2(200, 100);
        _eurekaLabel.Modulate = new Color(0.7f, 1f, 0.7f, 0.9f);
        _eurekaLabel.AddThemeFontSizeOverride("font_size", 11);
        _eurekaLabel.Visible = false;
        GetNode<CanvasLayer>("UI").AddChild(_eurekaLabel);
        GD.Print("[G5] 尤里卡系统初始化完成 — 按H查看尤里卡进度");

        // G6: 初始化邻接加成面板
        _adjacencyLabel = new Label();
        _adjacencyLabel.Name = "AdjacencyLabel";
        _adjacencyLabel.Position = new Vector2(250, 130);
        _adjacencyLabel.Size = new Vector2(280, 250);
        _adjacencyLabel.Modulate = new Color(1f, 0.85f, 0.5f, 0.9f);
        _adjacencyLabel.AddThemeFontSizeOverride("font_size", 11);
        _adjacencyLabel.Visible = false;
        GetNode<CanvasLayer>("UI").AddChild(_adjacencyLabel);
        GD.Print("[G6] 邻接加成系统初始化完成 — 按J查看邻接加成");

        // G7: 初始化间谍任务面板
        _spyMissionLabel = new Label();
        _spyMissionLabel.Name = "SpyMissionLabel";
        _spyMissionLabel.Position = new Vector2(770, 130);
        _spyMissionLabel.Size = new Vector2(200, 180);
        _spyMissionLabel.Modulate = new Color(0.85f, 0.7f, 1f, 0.9f);
        _spyMissionLabel.AddThemeFontSizeOverride("font_size", 11);
        _spyMissionLabel.Visible = false;
        GetNode<CanvasLayer>("UI").AddChild(_spyMissionLabel);
        GD.Print("[G7] 间谍任务系统初始化完成 — 按N查看间谍任务");

        // G8: 初始化占领强化面板
        _captureLabel = new Label();
        _captureLabel.Name = "CaptureLabel";
        _captureLabel.Position = new Vector2(250, 400);
        _captureLabel.Size = new Vector2(250, 200);
        _captureLabel.Modulate = new Color(0.5f, 1f, 0.7f, 0.9f);
        _captureLabel.AddThemeFontSizeOverride("font_size", 11);
        _captureLabel.Visible = false;
        GetNode<CanvasLayer>("UI").AddChild(_captureLabel);
        GD.Print("[G8] 占领强化系统初始化完成 — 按K查看占领状态");

        // 开局目标提示（控制台）
        GD.Print("========================================");
        GD.Print("★ 游戏目标：摧毁敌方所有建筑和单位即获胜！");
        GD.Print("★ 建造建议：电站→兵营→车厂→科技中心");
        GD.Print("★ 选中单位右键点敌方建筑/单位攻击");
        GD.Print("★ 选中建筑右键设集结点 | R维修 | V出售");
        GD.Print("★ Tab科技树 | Y时代升级 | T战术卡 | G电网分区 | H尤里卡 | J邻接加成 | N间谍 | K占领");
        GD.Print("========================================");
    }

    // ======== E4：地形改造支持方法 ========

    /// <summary>扣减指定阵营的金钱。成功返回true，资金不足返回false。</summary>
    public bool SpendMoney(int teamId, int amount)
    {
        if (teamId < 0 || teamId >= TotalTeamCount) return false;
        if (_money[teamId] < amount) return false;
        _money[teamId] -= amount;
        return true;
    }

    /// <summary>P5：应用难度配置到游戏参数。</summary>
    private void ApplyDifficultyConfig()
    {
        switch (_difficulty)
        {
            case Difficulty.Easy:
                _aiThinkInterval = 14f; _aiStartMoney = 1500; _blueStartMoney = 3000;
                _aiStartHarvesters = 2; _aiUsesTech = false; _aiCapturesPoints = false;
                StrategicPointIncomeEnabled = false; _unitCap = 12; _playerTechLevel = 1;
                Unit.AiGraceRemaining = 120f; // Easy: 2分钟保护期
                _activeAiCount = 2; // Easy: 仅2个活跃AI，其余5个休眠
                break;
            case Difficulty.Normal:
                _aiThinkInterval = 10f; _aiStartMoney = 1800; _blueStartMoney = 2700;
                _aiStartHarvesters = 3; _aiUsesTech = true; _aiCapturesPoints = true;
                StrategicPointIncomeEnabled = true; _unitCap = 16; _playerTechLevel = 3; // v5修复：Lv2→Lv3，解锁科技中心
                Unit.AiGraceRemaining = 60f; // Normal: 60秒保护期
                _activeAiCount = 4; // v5修复：Normal难度活跃AI 7→4，3个AI休眠，缓解1v7压力
                break;
            case Difficulty.Hard:
                _aiThinkInterval = 7f; _aiStartMoney = 2200; _blueStartMoney = 2500;
                _aiStartHarvesters = 3; _aiUsesTech = true; _aiCapturesPoints = true;
                StrategicPointIncomeEnabled = true; _unitCap = 20; _playerTechLevel = 3;
                Unit.AiGraceRemaining = 30f; // Hard: 30秒保护期
                _activeAiCount = 6; // Hard: 6个活跃AI，1个休眠
                break;
            case Difficulty.Brutal:
                _aiThinkInterval = 4f; _aiStartMoney = 3000; _blueStartMoney = 2200;
                _aiStartHarvesters = 4; _aiUsesTech = true; _aiCapturesPoints = true;
                StrategicPointIncomeEnabled = true; _unitCap = 24; _playerTechLevel = 3;
                Unit.AiGraceRemaining = 0f; // Brutal: 无保护期，开局即战
                _activeAiCount = 7; // Brutal: 全部7个AI活跃，极限挑战
                break;
        }
        _enemyThinkTimer = _aiThinkInterval;
        _money[0] = _blueStartMoney;
        for (int t = 1; t <= AiTeamCount; t++)
            _money[t] = _aiStartMoney;
        GD.Print($"[Difficulty] {_difficulty} | AI间隔 {_aiThinkInterval}s | 玩家方${_blueStartMoney} AI${_aiStartMoney}(x7) | 科技等级Lv{_playerTechLevel} | 上限{_unitCap} | 战略点收入{StrategicPointIncomeEnabled} | 活跃AI {_activeAiCount}/7 (休眠 {AiTeamCount - _activeAiCount} 个)");
    }

    public override void _Input(InputEvent @event)
    {
        // P0-2: F5/F9 快捷键存读档 — 即使游戏结束也可用
        if (@event is InputEventKey k && k.Pressed && !k.Echo)
        {
            if (k.Keycode == Key.F5) { QuickSave(); return; }
            if (k.Keycode == Key.F9) { QuickLoad(); return; }
        }

        if (_gameOver) return;

        var vpSize = GetViewportRect().Size;
        bool mouseOverPanel = GetViewport().GetMousePosition().X > vpSize.X - 232f;
        bool mouseOverMinimap = _minimap != null && _minimap.ContainsScreenPos(GetViewport().GetMousePosition());
        if (mouseOverMinimap && @event is InputEventMouse) return;

        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            var worldPos = _camera.GetGlobalMousePosition();
            if (mb.ButtonIndex == MouseButton.Left)
            {
                // 阶段12-A4：闪电风暴目标选择模式（与核弹互斥优先）
                if (_lightningTargetMode && !mouseOverPanel)
                {
                    ApplyLightning(worldPos, PlayerTeamId);
                    _lightningTargetMode = false;
                    _playerLightningCooldown = LightningCooldownDuration;
                    QueueRedraw();
                    return;
                }
                // 阶段12-A4：核弹目标选择模式优先（左键释放核弹）
                if (_nukeTargetMode && !mouseOverPanel)
                {
                    ApplyNuke(worldPos, PlayerTeamId);
                    _nukeTargetMode = false;
                    _playerNukeCooldown = NukeCooldownDuration;
                    QueueRedraw();
                    return;
                }
                // E10：巡航导弹目标选择模式
                if (_missileTargetMode && !mouseOverPanel)
                {
                    ApplyCruiseMissile(worldPos, PlayerTeamId);
                    _missileTargetMode = false;
                    _playerMissileCooldown = MissileCooldownDuration;
                    QueueRedraw();
                    return;
                }
                // Q1 放置建筑模式优先
                if (_placementMode != null && !mouseOverPanel)
                {
                    PlaceBuildingAtMouse();
                    return;
                }
                if (mouseOverPanel) return;
                // G1：攻击移动模式，左键点地发起攻击移动
                if (_attackMoveMode && GetSelectedFriendlyUnits().Count > 0)
                {
                    IssueAttackMove(worldPos);
                    _attackMoveMode = false;
                    return;
                }
                _isDragging = true;
                _dragStart = worldPos;
                _dragBox.Visible = true;
            }
            if (mb.ButtonIndex == MouseButton.Right)
            {
                if (_lightningTargetMode) { _lightningTargetMode = false; QueueRedraw(); return; }
                if (_nukeTargetMode) { _nukeTargetMode = false; QueueRedraw(); return; }
                if (_missileTargetMode) { _missileTargetMode = false; QueueRedraw(); return; }
                if (_placementMode != null) { CancelPlacement(); return; }
                if (_attackMoveMode) { _attackMoveMode = false; return; }
                if (GetSelectedFriendlyUnits().Count > 0) HandleRightClick(worldPos);
            }
        }
        if (@event is InputEventMouseButton mbr && !mbr.Pressed && mbr.ButtonIndex == MouseButton.Left && _isDragging)
        {
            _isDragging = false;
            HandleSelection(_dragStart, _camera.GetGlobalMousePosition());
            _dragBox.Visible = false;
        }
        if (@event is InputEventMouseMotion && _isDragging)
        {
            UpdateDragBox(_dragStart, _camera.GetGlobalMousePosition());
        }

        // G1：键盘命令（编队/攻击移动/停止）
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            HandleCommandKey(key);
        }
    }

    private void HandleCommandKey(InputEventKey key)
    {
        var kc = key.Keycode;
        bool ctrl = Input.IsKeyPressed(Key.Ctrl);

        // G1: Tab键打开/关闭科技树面板
        if (kc == Key.Tab)
        {
            _techTreePanelVisible = !_techTreePanelVisible;
            _techTreeLabel.Visible = _techTreePanelVisible;
            if (_techTreePanelVisible) UpdateTechTreePanel();
            return;
        }

        // G2: Y键打开/关闭时代面板
        if (kc == Key.Y)
        {
            _eraPanelVisible = !_eraPanelVisible;
            _eraLabel.Visible = _eraPanelVisible;
            if (_eraPanelVisible) UpdateEraPanel();
            return;
        }

        // G2: 时代面板可见时，按U键升级时代
        if (_eraPanelVisible && kc == Key.U)
        {
            TryAdvanceEra();
            UpdateEraPanel();
            return;
        }

        // G3: T键查看当前战术卡
        if (kc == Key.T)
        {
            ShowCardStatus();
            return;
        }

        // G4: G键查看电网分布
        if (kc == Key.G)
        {
            _powerGridPanelVisible = !_powerGridPanelVisible;
            _powerGridLabel.Visible = _powerGridPanelVisible;
            if (_powerGridPanelVisible) UpdatePowerGridPanel();
            return;
        }

        // G5: H键查看尤里卡进度
        if (kc == Key.H)
        {
            _eurekaLabel.Visible = !_eurekaLabel.Visible;
            if (_eurekaLabel.Visible) UpdateEurekaPanel();
            return;
        }

        // G6: J键查看邻接加成
        if (kc == Key.J)
        {
            _adjacencyPanelVisible = !_adjacencyPanelVisible;
            _adjacencyLabel.Visible = _adjacencyPanelVisible;
            if (_adjacencyPanelVisible) UpdateAdjacencyPanel();
            return;
        }

        // G7: N键查看间谍任务
        if (kc == Key.N)
        {
            _spyMissionPanelVisible = !_spyMissionPanelVisible;
            _spyMissionLabel.Visible = _spyMissionPanelVisible;
            if (_spyMissionPanelVisible) UpdateSpyMissionPanel();
            return;
        }

        // G8: K键查看占领状态
        if (kc == Key.K)
        {
            _capturePanelVisible = !_capturePanelVisible;
            _captureLabel.Visible = _capturePanelVisible;
            if (_capturePanelVisible) UpdateCapturePanel();
            return;
        }

        // G3: 战术卡选择面板可见时，1/2/3选择对应卡
        if (_cardLabel.Visible && _cardChoices.Length > 0)
        {
            int cardIdx = kc switch
            {
                Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2,
                _ => -1
            };
            if (cardIdx >= 0 && cardIdx < _cardChoices.Length)
            {
                SelectPlayerCard(_cardChoices[cardIdx]);
                return;
            }
        }

        // G1: 科技面板可见时，数字键1-9研究对应行科技
        if (_techTreePanelVisible)
        {
            int techNum = kc switch
            {
                Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2,
                Key.Key4 => 3, Key.Key5 => 4, Key.Key6 => 5,
                Key.Key7 => 6, Key.Key8 => 7, Key.Key9 => 8,
                Key.Key0 => 9, Key.Minus => 10, Key.Equal => 11,
                _ => -1
            };
            if (techNum >= 0)
            {
                TryResearchTech(techNum);
                UpdateTechTreePanel();
                return;
            }
        }

        // 编队：Ctrl+1~9 储存，1~9 取出
        int idx = SquadIndexFromKey(kc);
        if (idx >= 0)
        {
            if (ctrl) SaveSquad(idx);
            else SelectSquad(idx);
            return;
        }

        if (kc == Key.Q)
        {
            if (GetSelectedFriendlyUnits().Count > 0)
            {
                _attackMoveMode = !_attackMoveMode;
                GD.Print($"[操控] 攻击移动模式 {(_attackMoveMode ? "开启 - 左键点地发起" : "关闭")}");
            }
        }
        else if (kc == Key.X)
        {
            var sel = GetSelectedFriendlyUnits();
            if (sel.Count > 0)
            {
                foreach (var u in sel) u.CommandStop();
                GD.Print($"[操控] 停止 ({sel.Count} 单位)");
            }
        }
        else if (kc == Key.Escape)
        {
            _attackMoveMode = false;
        }
        else if (kc == Key.R)
        {
            // G4：维修选中的蓝方受损建筑
            int repaired = 0;
            foreach (var o in _selected)
            {
                if (o is Building b && b.TeamId == 0 && IsInstanceValid(b) && b.NeedsRepair)
                {
                    int cost = GetRepairCost(b);
                    if (_money[0] >= cost)
                    {
                        _money[0] -= cost;
                        b.Repair();
                        repaired++;
                        GD.Print($"[维修] {b.BuildingName} 已修复满血，扣 ${cost}，剩余 ${_money[0]}");
                    }
                    else
                    {
                        GD.Print($"[维修] 资金不足！维修{b.BuildingName}需要 ${cost}，当前 ${_money[0]}");
                    }
                }
            }
            if (repaired == 0)
            {
                GD.Print("[维修] 没有可维修的建筑（需选中受损的蓝方建筑）");
            }
        }
        else if (kc == Key.V)
        {
            // G4：出售选中的蓝方建筑（基地除外），回收50%建造资金
            var toSell = new List<Building>();
            foreach (var o in _selected)
            {
                if (o is Building b && b.TeamId == 0 && IsInstanceValid(b) && b.Type != BuildingType.Base)
                    toSell.Add(b);
            }
            foreach (var b in toSell)
            {
                int refund = Mathf.Max(1, GetBuildingCost(b.Type) / 2);
                _money[0] += refund;
                b.SetSelected(false);
                _selected.Remove(b);
                GD.Print($"[出售] {b.BuildingName} 已出售，回收 ${refund}，资金 ${_money[0]}");
                // P0-1: 移除PathFinder障碍并取消事件订阅（H4修复）
                OnBuildingDestroyed(b);
                b.Destroyed -= OnBuildingDestroyed;
                b.QueueFree();
            }
            if (toSell.Count == 0)
            {
                GD.Print("[出售] 没有可出售的建筑（基地不可出售）");
            }
        }
        else if (kc == Key.Z)
        {
            // 阶段12-A4：核弹超武（需科技中心，5分钟冷却）
            // 注：N键已被InputMap占用为spawn_heavy（重坦），故核弹改用Z键
            // E10：核弹需核弹发射井建筑
            if (!HasBuilding(PlayerTeamId, BuildingType.NukeSilo))
            {
                ShowToast("☢ 核弹不可用：需建造核弹发射井", new Color(1f, 0.5f, 0.3f));
                GD.Print("[核弹] 不可用：需核弹发射井");
            }
            else if (_playerNukeCooldown > 0f)
            {
                int sec = Mathf.CeilToInt(_playerNukeCooldown);
                ShowToast($"☢ 核弹冷却中：{sec / 60}:{sec % 60:D2}", new Color(1f, 0.6f, 0.3f));
                GD.Print($"[核弹] 冷却中：{sec}s");
            }
            else
            {
                _nukeTargetMode = !_nukeTargetMode;
                if (_nukeTargetMode) _lightningTargetMode = false; // 与闪电风暴互斥
                if (_nukeTargetMode) _missileTargetMode = false;   // 与巡航导弹互斥
                if (_nukeTargetMode)
                    ShowToast("☢ 核弹已就绪：左键点击目标 / 右键取消", new Color(1f, 0.3f, 0.2f));
                GD.Print($"[核弹] 目标选择模式 {(_nukeTargetMode ? "开启" : "关闭")}");
                QueueRedraw();
            }
        }
        else if (kc == Key.C)
        {
            // 阶段12-A4：闪电风暴超武（需科技中心，4分钟冷却，持续5秒范围伤害）
            // 注：C 键原本未占用，用作闪电 Storm（雷电英文首字母冲突多，用 C 取"持续伤害"意）
            // E10：闪电风暴需闪电风暴塔建筑
            if (!HasBuilding(PlayerTeamId, BuildingType.LightningTower))
            {
                ShowToast("⚡ 闪电不可用：需建造闪电风暴塔", new Color(0.5f, 0.7f, 1f));
                GD.Print("[闪电] 不可用：需闪电风暴塔");
            }
            else if (_playerLightningCooldown > 0f)
            {
                int sec = Mathf.CeilToInt(_playerLightningCooldown);
                ShowToast($"⚡ 闪电风暴冷却中：{sec / 60}:{sec % 60:D2}", new Color(0.5f, 0.7f, 1f));
                GD.Print($"[闪电] 冷却中：{sec}s");
            }
            else
            {
                _lightningTargetMode = !_lightningTargetMode;
                if (_lightningTargetMode) _nukeTargetMode = false; // 与核弹互斥
                if (_lightningTargetMode) _missileTargetMode = false; // 与导弹互斥
                if (_lightningTargetMode)
                    ShowToast("⚡ 闪电风暴已就绪：左键点击目标 / 右键取消", new Color(0.5f, 0.8f, 1f));
                GD.Print($"[闪电] 目标选择模式 {(_lightningTargetMode ? "开启" : "关闭")}");
                QueueRedraw();
            }
        }
        // E10：巡航导弹超武（Shift+V，需导弹发射井，3分钟冷却）
        else if (kc == Key.V && Input.IsKeyPressed(Key.Shift))
        {
            if (!HasBuilding(PlayerTeamId, BuildingType.MissileSilo))
            {
                ShowToast("🚀 导弹不可用：需建造导弹发射井", new Color(1f, 0.8f, 0.3f));
                GD.Print("[导弹] 不可用：需导弹发射井");
            }
            else if (_playerMissileCooldown > 0f)
            {
                int sec = Mathf.CeilToInt(_playerMissileCooldown);
                ShowToast($"🚀 导弹冷却中：{sec / 60}:{sec % 60:D2}", new Color(1f, 0.8f, 0.5f));
                GD.Print($"[导弹] 冷却中：{sec}s");
            }
            else
            {
                _missileTargetMode = !_missileTargetMode;
                if (_missileTargetMode) _nukeTargetMode = false;
                if (_missileTargetMode) _lightningTargetMode = false;
                if (_missileTargetMode)
                    ShowToast("🚀 巡航导弹已就绪：左键点击目标 / 右键取消", new Color(1f, 0.8f, 0.3f));
                GD.Print($"[导弹] 目标选择模式 {(_missileTargetMode ? "开启" : "关闭")}");
                QueueRedraw();
            }
        }
    }

    private int SquadIndexFromKey(Key kc)
    {
        int v = (int)kc, a = (int)Key.Key1, b = (int)Key.Key9;
        return (v >= a && v <= b) ? v - a : -1;
    }

    // ======== G1: 科技分支树方法 ========

    /// <summary>科技节点顺序列表（用于数字键索引）。</summary>
    private static readonly TechTree.TechId[] TechOrder = new TechTree.TechId[]
    {
        TechTree.TechId.Mil_ArmorUpgrade,
        TechTree.TechId.Mil_AmmoUpgrade,
        TechTree.TechId.Mil_AdvancedTactics,
        TechTree.TechId.Mil_HeroTraining,
        TechTree.TechId.Eco_MiningEfficiency,
        TechTree.TechId.Eco_MassProduction,
        TechTree.TechId.Eco_ResourceNetwork,
        TechTree.TechId.Eco_AdvancedLogistics,
        TechTree.TechId.Def_Fortification,
        TechTree.TechId.Def_PowerGrid,
        TechTree.TechId.Def_AdvancedTurrets,
        TechTree.TechId.Def_RepairSystems,
    };

    // ======== G2: 时代系统方法 ========

    // ======== G3: 战术卡系统方法 ========
    // （_cardStatusHideTimer 字段、GetPlayerCard 方法已移至 Main.Tech.cs）

    // ======== G4: 电网分区系统方法 ========

    private void SaveSquad(int idx)
    {
        _squads[idx] = GetSelectedFriendlyUnits();
        GD.Print($"[编队] 编队{idx + 1} 已保存 ({_squads[idx].Count} 单位)");
    }

    private void SelectSquad(int idx)
    {
        if (!_squads.TryGetValue(idx, out var squad) || squad.Count == 0) return;
        foreach (var o in _selected)
        {
            if (IsInstanceValid(o))
            {
                if (o is Unit u) u.SetSelected(false);
                else if (o is Building b) b.SetSelected(false);
            }
        }
        _selected.Clear();
        foreach (var u in squad)
        {
            if (IsInstanceValid(u) && u.TeamId == 0)
            {
                u.SetSelected(true);
                _selected.Add(u);
            }
        }
        // 镜头跳转到编队中心
        if (_selected.Count > 0)
        {
            var center = Vector2.Zero;
            foreach (var o in _selected) if (o is Node2D n) center += n.GlobalPosition;
            center /= _selected.Count;
            _camera.Position = center;
        }
        GD.Print($"[编队] 选取编队{idx + 1} ({_selected.Count} 单位)");
    }

    private void IssueAttackMove(Vector2 worldPos)
    {
        var list = GetSelectedFriendlyUnits();
        int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(list.Count)));
        for (int i = 0; i < list.Count; i++)
        {
            int col = i % cols, row = i / cols;
            list[i].CommandAttackMove(worldPos + new Vector2(col * 40, row * 40));
        }
        GD.Print($"[操控] 攻击移动 -> {worldPos} ({list.Count} 单位)");
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;

        // AI保护期递减：保护期内AI不主动进攻，给玩家发展空间
        if (Unit.AiGraceRemaining > 0f)
        {
            Unit.AiGraceRemaining -= dt;
            if (Unit.AiGraceRemaining <= 0f)
            {
                Unit.AiGraceRemaining = 0f;
                if (!_aiGraceEndedNotified)
                {
                    _aiGraceEndedNotified = true;
                    ShowToast("⚠ AI保护期结束！敌方开始进攻！", new Color(1f, 0.5f, 0.3f));
                }
            }
        }

        // ===== 截图功能（Godot 内部 API，用于验收渲染效果）=====
        // 在 ANGLE 软件渲染环境下 CopyFromScreen 抓不到 UI，必须用引擎内部截图
        // 1. 自动截图：多时间点截图（22s/45s/75s/110s），观察游戏不同阶段
        if (_autoshotTimer >= 0f)
        {
            _autoshotTimer += dt;
            // 多阶段截图时间点
            float[] shotTimes = { 22f, 45f, 75f, 110f };
            string[] shotSuffixes = { "t1_22s", "t2_45s", "t3_75s", "t4_110s" };
            for (int i = 0; i < shotTimes.Length; i++)
            {
                if (_autoshotTimer >= shotTimes[i] && _autoshotPhase == i)
                {
                    _autoshotPhase = i + 1;
                    // 统一用 zoom=1.0 基地全景，观察游戏进展
                    _camera.Position = new Vector2(320, 340);
                    _camera.Zoom = new Vector2(1.0f, 1.0f);
                    _panoramaShotPending = 3;
                    _pendingShotSuffix = shotSuffixes[i];
                    break;
                }
            }
        }
        // 全景截图倒计时：等待渲染稳定后拍全景图（用于验收矿石/地面等全局视觉）
        if (_panoramaShotPending > 0)
        {
            _panoramaShotPending--;
            if (_panoramaShotPending == 0)
            {
                TakeViewportScreenshot(_pendingShotSuffix);
            }
        }
        // 2. F12 手动截图（玩家可在游戏中按 F12 截图）
        if (Input.IsKeyPressed(Key.F12))
        {
            if (!_f12ShotDown) { _f12ShotDown = true; TakeViewportScreenshot("f12"); }
        }
        else { _f12ShotDown = false; }

        // 制造单位热键
        if (Input.IsActionJustPressed("spawn_unit")) TrySpawnUnit(UnitType.LightTank, LightTankCost);
        if (Input.IsActionJustPressed("spawn_heavy")) TrySpawnUnit(UnitType.HeavyTank, HeavyTankCost);
        if (Input.IsActionJustPressed("spawn_artillery")) TrySpawnUnit(UnitType.Artillery, ArtilleryCost);
        if (Input.IsActionJustPressed("spawn_harvester")) TrySpawnHarvester();
        if (Input.IsActionJustPressed("build_power")) TryBuildBuilding(BuildingType.PowerPlant);
        if (Input.IsActionJustPressed("build_barracks")) TryBuildBuilding(BuildingType.Barracks);
        if (Input.IsActionJustPressed("build_warfactory")) TryBuildBuilding(BuildingType.WarFactory);
        if (Input.IsActionJustPressed("build_tech")) TryBuildBuilding(BuildingType.TechCenter);
        if (Input.IsActionJustPressed("spawn_rocket")) TrySpawnUnit(UnitType.RocketLauncher, RocketLauncherCost);
        if (Input.IsActionJustPressed("spawn_missile")) TrySpawnUnit(UnitType.MissileTank, MissileTankCost);
        // L1修复: 以下生产热键与面板键冲突(N/K/G/H/T/J/Y)，面板打开时禁用生产热键
        if (!AnyPanelOpen())
        {
        // E4：工兵(K) / 高级工程师(Shift+K) 生产热键
        if (Input.IsKeyPressed(Key.K) && _prevKeyState != Key.K)
        {
            if (Input.IsKeyPressed(Key.Shift))
                TrySpawnUnit(UnitType.ChiefEngineer, ChiefEngineerCost);
            else
                TrySpawnUnit(UnitType.Sapper, SapperCost);
        }
        _prevKeyState = Input.IsKeyPressed(Key.K) ? Key.K : Key.None;

        // E6：新步兵热键 G(掷弹兵) / Shift+G(狙击手) / F(喷火兵) / T(运输车)
        if (Input.IsKeyPressed(Key.G) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Grenadier, GrenadierCost);
        if (Input.IsKeyPressed(Key.G) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Sniper, SniperCost);
        if (Input.IsKeyPressed(Key.F) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.FlameInfantry, FlameInfantryCost);
        if (Input.IsKeyPressed(Key.T) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Transport, TransportCost);

        // E6b：特殊单位热键 Y(英雄) / Shift+Y(间谍) / U(窃贼)
        if (Input.IsKeyPressed(Key.Y) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Hero, HeroCost);
        if (Input.IsKeyPressed(Key.Y) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Spy, SpyCost);
        if (Input.IsKeyPressed(Key.U) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Thief, ThiefCost);

        // E7：空军热键 J(战斗机) / Shift+J(直升机) / Shift+W(火箭兵)
        if (Input.IsKeyPressed(Key.J) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Fighter, FighterCost);
        if (Input.IsKeyPressed(Key.J) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Helicopter, HelicopterCost);
        if (Input.IsKeyPressed(Key.W) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.RocketInfantry, RocketInfantryCost);

        // E8：扩展空军热键 Shift+B(轰炸机) / H(侦察机) / Shift+H(运输直升机)
        if (Input.IsKeyPressed(Key.B) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Bomber, BomberCost);
        if (Input.IsKeyPressed(Key.H) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Scout, ScoutCost);
        if (Input.IsKeyPressed(Key.H) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.TransportHeli, TransportHeliCost);
        } // end if (!AnyPanelOpen())

        // E9：海军热键 Shift+1(驱逐舰) / Shift+2(潜艇) / Shift+3(航母) / Shift+4(登陆艇)
        if (Input.IsKeyPressed(Key.Key1) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Destroyer, DestroyerCost);
        if (Input.IsKeyPressed(Key.Key2) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Submarine, SubmarineCost);
        if (Input.IsKeyPressed(Key.Key3) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.AircraftCarrier, AircraftCarrierCost);
        if (Input.IsKeyPressed(Key.Key4) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.LandingCraft, LandingCraftCost);

        // E6：E键运输车下车
        if (Input.IsKeyPressed(Key.E))
        {
            foreach (var obj in _selected)
            {
                if (obj is Unit u && IsInstanceValid(u) && u.IsTransport && u.Passengers.Count > 0)
                    u.DisembarkAll();
            }
        }

        // AI 阵营节奏：仅活跃 AI 阵营（1.._activeAiCount）独立 Tick
        // 休眠AI（_activeAiCount+1..AiTeamCount）既不发展建筑也不造兵进攻，给玩家喘息空间
        if (!_gameOver)
        {
            _enemyThinkTimer -= dt;
            if (_enemyThinkTimer <= 0f)
            {
                for (int t = 1; t <= _activeAiCount; t++)
                    AITickForTeam(t);
                _enemyThinkTimer = _aiThinkInterval;
            }
        }

        // 蓝方测试 AI（模拟玩家自动造兵，仅在 headless 模式生效）
        if (!_gameOver && DisplayServer.GetName() == "headless")
        {
            _blueAITimer -= dt;
            if (_blueAITimer <= 0f)
            {
                BlueTestAITick();
                _blueAITimer = 7f;
            }
        }

        // 清理失效选中
        _selected.RemoveAll(o => !IsInstanceValid(o));

        // 递减建筑警报冷却
        if (_buildingAlertCooldown.Count > 0)
        {
            var keys = new List<ulong>(_buildingAlertCooldown.Keys);
            foreach (var k in keys)
            {
                _buildingAlertCooldown[k] -= dt;
                if (_buildingAlertCooldown[k] <= 0f) _buildingAlertCooldown.Remove(k);
            }
        }

        // ---- 阶段12-A4：核弹冷却递减 + 视觉特效更新 ----
        if (_playerNukeCooldown > 0f)
        {
            _playerNukeCooldown -= dt;
            if (_playerNukeCooldown < 0f) _playerNukeCooldown = 0f;
        }
        if (_aiNukeCooldowns.Count > 0)
        {
            var aiKeys = new List<int>(_aiNukeCooldowns.Keys);
            foreach (var k in aiKeys)
            {
                if (_aiNukeCooldowns[k] > 0f)
                {
                    _aiNukeCooldowns[k] -= dt;
                    if (_aiNukeCooldowns[k] < 0f) _aiNukeCooldowns[k] = 0f;
                }
            }
        }
        // 核弹特效推进
        if (_activeNukeVisuals.Count > 0)
        {
            for (int i = _activeNukeVisuals.Count - 1; i >= 0; i--)
            {
                var nv = _activeNukeVisuals[i];
                nv.Age += dt;
                if (nv.Age >= nv.Lifetime) _activeNukeVisuals.RemoveAt(i);
                else _activeNukeVisuals[i] = nv;
            }
            QueueRedraw();
        }
        // 目标选择模式下持续重绘（保持准星跟随鼠标）
        if (_nukeTargetMode) QueueRedraw();

        // ---- 阶段12-A4：闪电风暴冷却递减 + 持续伤害 Tick + 视觉刷新 ----
        if (_playerLightningCooldown > 0f)
        {
            _playerLightningCooldown -= dt;
            if (_playerLightningCooldown < 0f) _playerLightningCooldown = 0f;
        }
        if (_aiLightningCooldowns.Count > 0)
        {
            var aiKeys2 = new List<int>(_aiLightningCooldowns.Keys);
            foreach (var k in aiKeys2)
            {
                if (_aiLightningCooldowns[k] > 0f)
                {
                    _aiLightningCooldowns[k] -= dt;
                    if (_aiLightningCooldowns[k] < 0f) _aiLightningCooldowns[k] = 0f;
                }
            }
        }
        // E10：巡航导弹冷却
        if (_playerMissileCooldown > 0f)
        {
            _playerMissileCooldown -= dt;
            if (_playerMissileCooldown < 0f) _playerMissileCooldown = 0f;
        }
        if (_aiMissileCooldowns.Count > 0)
        {
            var aiKeys3 = new List<int>(_aiMissileCooldowns.Keys);
            foreach (var k in aiKeys3)
            {
                if (_aiMissileCooldowns[k] > 0f)
                {
                    _aiMissileCooldowns[k] -= dt;
                    if (_aiMissileCooldowns[k] < 0f) _aiMissileCooldowns[k] = 0f;
                }
            }
        }
        // 闪电风暴特效推进 + 每秒持续伤害
        if (_activeLightnings.Count > 0)
        {
            for (int i = _activeLightnings.Count - 1; i >= 0; i--)
            {
                var lv = _activeLightnings[i];
                lv.Age += dt;
                lv.DamageTickTimer += dt;
                lv.BoltRefreshTimer += dt;
                // 每秒结算一次持续伤害
                if (lv.DamageTickTimer >= 1f)
                {
                    lv.DamageTickTimer -= 1f;
                    int hits = DamageLightningAreaOnce(lv.Position, lv.FiringTeamId);
                    GD.Print($"[闪电] 持续伤害 Tick @ {lv.Position}，命中 {hits}（剩余 {(lv.Lifetime - lv.Age):F1}s）");
                }
                // 每 0.08 秒刷新闪电形状种子（让折线抖动闪烁）
                if (lv.BoltRefreshTimer >= 0.08f)
                {
                    lv.BoltRefreshTimer -= 0.08f;
                    _lightningBoltSeed = (float)GD.RandRange(0, 1000);
                }
                if (lv.Age >= lv.Lifetime)
                {
                    GD.Print($"[闪电] 特效结束 @ {lv.Position}");
                    _activeLightnings.RemoveAt(i);
                }
                else
                {
                    _activeLightnings[i] = lv;
                }
            }
            QueueRedraw();
        }
        if (_lightningTargetMode) QueueRedraw();

        // Q6：开局提示淡出
        if (_startOverlay != null && IsInstanceValid(_startOverlay))
        {
            _startOverlayAge += dt;
            if (_startOverlayAge > 8f) // v5修复：4f→8f，文字增多需更多阅读时间
            {
                float fade = 1f - (_startOverlayAge - 8f) / 1.5f;
                _startOverlay.Modulate = new Color(1, 1, 1, Mathf.Max(0, fade));
                if (fade <= 0f) { _startOverlay.QueueFree(); _startOverlay = null!; }
            }
        }

        // Q6：Toast 通知淡出
        for (int i = _activeToasts.Count - 1; i >= 0; i--)
        {
            var t = _activeToasts[i];
            t.Age += dt;
            if (t.Age < 0.2f)
                t.Label.Modulate = new Color(1, 1, 1, t.Age / 0.2f); // 淡入
            else if (t.Age > t.Lifetime - 0.5f)
                t.Label.Modulate = new Color(1, 1, 1, (t.Lifetime - t.Age) / 0.5f); // 淡出
            if (t.Age >= t.Lifetime)
            {
                t.Label.QueueFree();
                _activeToasts.RemoveAt(i);
            }
        }

        // E5：油田占领+产钱处理
        foreach (var child in _resourcesNode.GetChildren())
        {
            if (child is ResourceNode rn && IsInstanceValid(rn) && rn.ResourceType == ResourceType.OilField)
                rn.ProcessOilField(dt);
        }

        // 调试：每5秒输出游戏状态
        _debugTimer -= dt;
        if (_debugTimer <= 0f)
        {
            _debugTimer = 5f;
            // 8阵营状态汇总输出（玩家方 + AI 合计）
            int aiUnits = 0, aiBld = 0;
            for (int t = 1; t <= AiTeamCount; t++)
            {
                aiUnits += CountUnitsOfTeam(t);
                aiBld += CountBuildingsOfTeam(t);
            }
            GD.Print($"[Status] Player: ${_money[0]} | {CountUnitsOfTeam(0)} units / {CountBuildingsOfTeam(0)} buildings | AI(1-7) total: units={aiUnits} / buildings={aiBld}");
        }

        CheckWinCondition();

        // G5：游戏结束延迟后显示重开 UI
        if (_gameOver && _gameOverDelay > 0f)
        {
            _gameOverDelay -= dt;
            if (_gameOverDelay <= 0f)
            {
                _gameOverDelay = -1f;
                ShowGameOverUI();
            }
        }

        UpdateUI();

        // G1: 更新科技研究进度
        UpdateTechResearch(dt);

        // G2: 更新时代升级进度
        UpdateEraProgress(dt);

        // G3: 战术卡选择计时
        if (_cardSelectionPending)
        {
            _cardSelectionTimer -= dt;
            if (_cardSelectionTimer <= 0f)
            {
                ShowCardSelection();
            }
        }

        // G3: 战术卡状态自动隐藏
        if (_cardStatusHideTimer > 0f)
        {
            _cardStatusHideTimer -= dt;
            if (_cardStatusHideTimer <= 0f && _cardStatusLabel.Visible)
                _cardStatusLabel.Visible = false;
        }

        // G1: 建筑自动维修科技效果
        if (_techAutoRepairTimer <= 0f)
        {
            _techAutoRepairTimer = 1f; // 每秒检查一次
            for (int team = 0; team < TotalTeamCount; team++)
            {
                if (!HasTechAutoRepair(team)) continue;
                foreach (var c in _buildingsNode.GetChildren())
                {
                    if (c is Building b && b.TeamId == team && IsInstanceValid(b) && b.Health < b.MaxHealth && b.Health > 0f)
                        b.RepairByEngineer(b.MaxHealth * 0.02f);
                }
            }
        }
        _techAutoRepairTimer -= dt;

        // G4: 刷新电网面板（如果可见，每0.5秒刷新一次）
        if (_powerGridPanelVisible)
        {
            _powerGridRefreshTimer -= dt;
            if (_powerGridRefreshTimer <= 0f)
            {
                _powerGridRefreshTimer = 0.5f;
                UpdatePowerGridPanel();
            }
        }

        // Q1 刷新侧边栏建造面板
        if (_buildPanel != null)
        {
             _buildPanel.UpdateState(_money[0], GetTeamPower(0), _playerTechLevel,
                 CountUnitsOfTeam(0), _unitCap,
                 HasBuilding(0, BuildingType.Base), HasBuilding(0, BuildingType.PowerPlant),
                 HasBuilding(0, BuildingType.Barracks), HasBuilding(0, BuildingType.WarFactory),
                 HasBuilding(0, BuildingType.TechCenter), HasBuilding(0, BuildingType.Airfield),
                 HasBuilding(0, BuildingType.Shipyard));

             // 生产队列信息
             var queueData = CollectPlayerProductionInfo();
             _buildPanel.UpdateProductionQueue(queueData);
        }
        // 放置模式预览重绘
        if (_placementMode != null) QueueRedraw();
        // Esc 取消放置
        if (Input.IsKeyPressed(Key.Escape) && _placementMode != null) CancelPlacement();
    }

    // ---------- 截图 ----------
    /// <summary>用 Godot 内部 API 截取视口并保存为 PNG。在 ANGLE 软渲染环境下 CopyFromScreen 抓不到 UI，必须用此方法。</summary>
    private void TakeViewportScreenshot(string tag)
    {
        try
        {
            var img = GetViewport().GetTexture().GetImage();
            var ts = DateTime.Now.ToString("HHmmss");
            var path = $"user://shot_{tag}_{ts}.png";
            img.SavePng(path);
            GD.Print($"[截图] 已保存: {ProjectSettings.GlobalizePath(path)} 尺寸={img.GetSize()}");
        }
        catch (Exception ex) { GD.PrintErr($"[截图] 失败: {ex.Message}"); }
    }

    // ---------- 制造 ----------
    private void TrySpawnUnit(UnitType type, int cost)
    {
        // G1: 科技批量生产折扣
        cost = Mathf.Max(1, (int)(cost * GetTechCostMultiplier(0)));

        // 建筑前置检查
        if (!CanProduceUnit(0, type))
        {
            GD.Print($"[警告] 缺少生产{type}所需建筑！");
            return;
        }

        // U2: Shift批量加入队列（最多5个）
        int batchCount = Input.IsKeyPressed(Key.Shift) ? 5 : 1;

        for (int i = 0; i < batchCount; i++)
        {
            // 电力检查
            if (GetTeamPower(0) < 0)
            {
                GD.Print($"[警告] 电力不足，无法生产单位！当前电力: {GetTeamPower(0)}");
                break;
            }

            // G2：单位上限检查（活跃单位 + 队列中）+ G1科技上限加成 + G3战术卡加成
            int effectiveCap = _unitCap + GetTechUnitCapBonus(0) + GetCardUnitCapBonus(0);
            int total = CountUnitsOfTeam(0) + CountQueuedUnitsOfTeam(0);
            if (total >= effectiveCap)
            {
                GD.Print($"[警告] 达到单位上限 {effectiveCap}！");
                break;
            }

            // G2：找生产建筑（队列最短的同类建筑，实现多建筑并行）
            var producer = FindProducerForUnit(type, 0);
            if (producer == null)
            {
                GD.Print($"[警告] 没有可用的{GetProducerForUnit(type)}！");
                break;
            }

            if (_money[0] < cost)
            {
                GD.Print($"[警告] 资金不足！需要 ${cost}，当前 ${_money[0]}");
                _audio?.PlaySfx(AudioManager.Sfx.UiError);
                break;
            }

            _money[0] -= cost;
            producer.EnqueueProduction(UnitTypeToProductionType(type));
            GD.Print($"蓝方排产{type}(批量{i+1}/{batchCount})，扣 ${cost}，剩余 ${_money[0]}，{producer.BuildingName}队列 {producer.QueueCount}/{Building.MaxQueueSize}");
        }
        _audio?.PlaySfx(AudioManager.Sfx.UiBuildStart);
    }

    public void TrySpawnHarvester()
    {
        if (_money[0] < HarvesterCost) { GD.Print("[警告] 资金不足！"); _audio?.PlaySfx(AudioManager.Sfx.UiError); return; }
        if (GetTeamPower(0) < 0) { GD.Print("[警告] 电力不足！"); return; }

        int total = CountUnitsOfTeam(0) + CountQueuedUnitsOfTeam(0);
        if (total >= _unitCap) { GD.Print($"[警告] 达到单位上限 {_unitCap}！"); return; }

        var producer = FindProducerBuilding(BuildingType.Base, 0);
        if (producer == null) { GD.Print("[警告] 没有基地！"); return; }

        _money[0] -= HarvesterCost;
        producer.EnqueueProduction(ProductionType.Harvester);
        GD.Print($"蓝方排产矿车，扣 ${HarvesterCost}，剩余 ${_money[0]}，队列 {producer.QueueCount}/{Building.MaxQueueSize}");
    }

    // ---------- 建造系统 ----------
    private bool CanProduceUnit(int teamId, UnitType unitType)
    {
        // G2: 时代限制检查
        if (!IsUnitUnlockedByEra(teamId, unitType)) return false;
        return unitType switch
        {
            UnitType.LightTank => HasBuilding(teamId, BuildingType.Barracks),
            UnitType.Infantry => HasBuilding(teamId, BuildingType.Barracks),
            UnitType.Sapper => HasBuilding(teamId, BuildingType.Barracks),
            UnitType.Grenadier => HasBuilding(teamId, BuildingType.Barracks),       // E6：掷弹兵
            UnitType.Sniper => HasBuilding(teamId, BuildingType.Barracks),          // E6：狙击手
            UnitType.FlameInfantry => HasBuilding(teamId, BuildingType.Barracks),     // E6：喷火兵
            UnitType.HeavyTank => HasBuilding(teamId, BuildingType.WarFactory),
            UnitType.Artillery => HasBuilding(teamId, BuildingType.WarFactory),
            UnitType.AntiAir => HasBuilding(teamId, BuildingType.WarFactory),
            UnitType.Engineer => HasBuilding(teamId, BuildingType.WarFactory),
            UnitType.Transport => HasBuilding(teamId, BuildingType.WarFactory),      // E6：运输车
            UnitType.Hero => HasBuilding(teamId, BuildingType.TechCenter),         // E6b：英雄需科技
            UnitType.Spy => HasBuilding(teamId, BuildingType.TechCenter),          // E6b：间谍需科技
            UnitType.Thief => HasBuilding(teamId, BuildingType.Barracks),          // E6b：窃贼需兵营
            UnitType.Fighter => HasBuilding(teamId, BuildingType.Airfield),      // E7
            UnitType.Helicopter => HasBuilding(teamId, BuildingType.Airfield),   // E7
            UnitType.RocketInfantry => HasBuilding(teamId, BuildingType.Barracks), // E7
            UnitType.Bomber => HasBuilding(teamId, BuildingType.Airfield),       // E8
            UnitType.Scout => HasBuilding(teamId, BuildingType.Airfield),        // E8
            UnitType.TransportHeli => HasBuilding(teamId, BuildingType.Airfield), // E8
            // E9：海军单位需船厂
            UnitType.Destroyer => HasBuilding(teamId, BuildingType.Shipyard),
            UnitType.Submarine => HasBuilding(teamId, BuildingType.Shipyard),
            UnitType.AircraftCarrier => HasBuilding(teamId, BuildingType.Shipyard),
            UnitType.LandingCraft => HasBuilding(teamId, BuildingType.Shipyard),
            UnitType.RocketLauncher => HasBuilding(teamId, BuildingType.TechCenter),
            UnitType.MissileTank => HasBuilding(teamId, BuildingType.TechCenter),
            UnitType.ChiefEngineer => HasBuilding(teamId, BuildingType.TechCenter),
            _ => HasBuilding(teamId, BuildingType.Base)
        };
    }

    private int GetTeamPower(int teamId)
    {
        int produced = 0, consumed = 0;
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b.TeamId == teamId && IsInstanceValid(b))
            {
                // G6: 邻接加成 — 电站叠放/靠基地时发电量提升
                float powMul = GetAdjacencyPowerMul(b);
                produced += (int)(b.PowerProvided * powMul);
                consumed += b.PowerConsumed;
            }
        }
        return produced - consumed;
    }

    private bool HasBuilding(int teamId, BuildingType type)
    {
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b.TeamId == teamId && b.Type == type && IsInstanceValid(b))
                return true;
        }
        return false;
    }

    private int GetUnitCost(UnitType type)
    {
        return type switch
        {
            UnitType.LightTank => LightTankCost,
            UnitType.Infantry => InfantryCost,
            UnitType.HeavyTank => HeavyTankCost,
            UnitType.Artillery => ArtilleryCost,
            UnitType.RocketLauncher => RocketLauncherCost,
            UnitType.MissileTank => MissileTankCost,
            UnitType.AntiAir => AntiAirCost,
            UnitType.Engineer => EngineerCost,
            UnitType.Sapper => SapperCost,
            UnitType.ChiefEngineer => ChiefEngineerCost,
            UnitType.Grenadier => GrenadierCost,
            UnitType.Sniper => SniperCost,
            UnitType.FlameInfantry => FlameInfantryCost,
            UnitType.Transport => TransportCost,
            UnitType.Hero => HeroCost,         // E6b
            UnitType.Spy => SpyCost,            // E6b
            UnitType.Thief => ThiefCost,        // E6b
            UnitType.Fighter => FighterCost,         // E7
            UnitType.Helicopter => HelicopterCost,   // E7
            UnitType.RocketInfantry => RocketInfantryCost, // E7
            UnitType.Bomber => BomberCost,                 // E8
            UnitType.Scout => ScoutCost,                   // E8
            UnitType.TransportHeli => TransportHeliCost,    // E8
            // E9：海军造价
            UnitType.Destroyer => DestroyerCost,
            UnitType.Submarine => SubmarineCost,
            UnitType.AircraftCarrier => AircraftCarrierCost,
            UnitType.LandingCraft => LandingCraftCost,
            _ => 0
        };
    }

    private Vector2 GetBuildPosition(int teamId)
    {
        if (!_bases.TryGetValue(teamId, out var baseBuilding) || baseBuilding == null || !IsInstanceValid(baseBuilding))
            return new Vector2(500, 500);

        // 每个 teamId 独立计数环形位置
        if (!_buildIndices.TryGetValue(teamId, out int idx)) idx = 0;
        _buildIndices[teamId] = idx + 1;

        int ring = idx / 4;
        int side = idx % 4;
        float radius = 120 + ring * 90;
        Vector2 offset = side switch
        {
            0 => new Vector2(radius, 0),
            1 => new Vector2(0, radius),
            2 => new Vector2(-radius, 0),
            _ => new Vector2(0, -radius)
        };
        // AI 阵营反向环形布局（朝地图内侧生长，避免偏出地图）
        if (teamId != PlayerTeamId)
            offset = new Vector2(-offset.X, -offset.Y);
        return baseBuilding.GlobalPosition + offset;
    }

    /// <summary>
    /// M2+M4修复: AI智能建造布局 — 同类建筑簇状排列获G6邻接加成，电站紧邻已有建筑确保G4供电覆盖。
    /// 策略：电站/兵营/车厂优先放在同类型旁边（160px内），其他建筑放在已有建筑附近确保供电覆盖（280px内）。
    /// </summary>
    private Vector2 GetAIBuildPosition(int teamId, BuildingType type)
    {
        if (!_bases.TryGetValue(teamId, out var baseB) || baseB == null || !IsInstanceValid(baseB))
            return new Vector2(500, 500);

        var teamBuildings = GetTeamBuildings(teamId);
        if (teamBuildings.Count == 0)
            return baseB.GlobalPosition + new Vector2(100, 0);

        // 同类建筑簇状布局：寻找同类型建筑，紧邻建造获G6邻接加成
        bool sameTypeCluster = type == BuildingType.PowerPlant || type == BuildingType.Barracks
            || type == BuildingType.WarFactory || type == BuildingType.TechCenter;
        if (sameTypeCluster)
        {
            foreach (var b in teamBuildings)
            {
                if (!IsInstanceValid(b) || b.Type != type) continue;
                // 在同类型建筑旁边找个位置（160px内=邻接加成范围）
                float[] angles = { 0, Mathf.Pi / 2, Mathf.Pi, Mathf.Pi * 1.5f };
                foreach (float a in angles)
                {
                    Vector2 pos = b.GlobalPosition + new Vector2(Mathf.Cos(a) * 110, Mathf.Sin(a) * 110);
                    if (!IsPositionOccupied(pos, teamId))
                        return pos;
                }
            }
        }

        // 非同类建筑：找供电覆盖范围内（280px）的位置
        foreach (var b in teamBuildings)
        {
            if (!IsInstanceValid(b)) continue;
            // 基地/电站周围找位置
            if (b.Type == BuildingType.PowerPlant || b.Type == BuildingType.Base)
            {
                float[] angles = { 0.4f, 1.2f, 2.0f, 2.8f, 3.6f, 5.0f, 5.8f };
                foreach (float a in angles)
                {
                    Vector2 pos = b.GlobalPosition + new Vector2(Mathf.Cos(a) * 130, Mathf.Sin(a) * 130);
                    if (!IsPositionOccupied(pos, teamId))
                        return pos;
                }
            }
        }

        // 兜底：环形布局
        if (!_buildIndices.TryGetValue(teamId, out int idx)) idx = 0;
        _buildIndices[teamId] = idx + 1;
        int ring = idx / 4;
        int side = idx % 4;
        float radius = 120 + ring * 90;
        Vector2 offset = side switch
        {
            0 => new Vector2(-radius, 0),
            1 => new Vector2(0, -radius),
            2 => new Vector2(radius, 0),
            _ => new Vector2(0, radius)
        };
        return baseB.GlobalPosition + offset;
    }

    /// <summary>M2修复辅助: 检查位置是否已被其他建筑占据。</summary>
    private bool IsPositionOccupied(Vector2 pos, int teamId)
    {
        foreach (var b in GetTeamBuildings(teamId))
        {
            if (IsInstanceValid(b) && b.GlobalPosition.DistanceTo(pos) < 80)
                return true;
        }
        return false;
    }

    private void TryBuildBuilding(BuildingType type)
    {
        // 前置建筑检查
        if (type == BuildingType.PowerPlant && !HasBuilding(0, BuildingType.Base)) { GD.Print("[警告] 需要先有建造厂！"); return; }
        if (type == BuildingType.Barracks && !HasBuilding(0, BuildingType.PowerPlant)) { GD.Print("[警告] 需要先有电站！"); return; }
        if (type == BuildingType.WarFactory && !HasBuilding(0, BuildingType.Barracks)) { GD.Print("[警告] 需要先有兵营！"); return; }
        if (type == BuildingType.TechCenter && !HasBuilding(0, BuildingType.WarFactory)) { GD.Print("[警告] 需要先有战车工厂！"); return; }
        // 阶段12-A1+A2 新增前置
        if (type == BuildingType.Turret && !HasBuilding(0, BuildingType.Barracks)) { GD.Print("[警告] 需要先有兵营！"); return; }
        if (type == BuildingType.AntiAirTurret && !HasBuilding(0, BuildingType.WarFactory)) { GD.Print("[警告] 需要先有车厂！"); return; }
        if (type == BuildingType.RepairPad && !HasBuilding(0, BuildingType.WarFactory)) { GD.Print("[警告] 需要先有车厂！"); return; }

        // P5：难度科技等级限制（系统复杂度分级）
        if (type == BuildingType.WarFactory && _playerTechLevel < 2) { GD.Print("[难度限制] 当前难度未解锁战车工厂！"); return; }
        if (type == BuildingType.TechCenter && _playerTechLevel < 3) { GD.Print("[难度限制] 当前难度未解锁科技中心！"); return; }
        if (type == BuildingType.AntiAirTurret && _playerTechLevel < 2) { GD.Print("[难度限制] 当前难度未解锁防空炮！"); return; }
        if (type == BuildingType.RepairPad && _playerTechLevel < 2) { GD.Print("[难度限制] 当前难度未解锁维修厂！"); return; }

        // G2: 时代限制检查
        if (!IsBuildingUnlockedByEra(0, type))
        {
            var ep = _eraProgress[0];
            GD.Print($"[G2] {type} 需要{EraSystem.GetNextEra(ep.CurrentEra)?.Name ?? "更高时代"}才能建造！当前：{EraSystem.Eras[(int)ep.CurrentEra].Name}");
            return;
        }

        // 电力检查（电站本身不受电力限制）
        if (type != BuildingType.PowerPlant && GetTeamPower(0) < 0)
        {
            GD.Print($"[警告] 电力不足！当前电力: {GetTeamPower(0)}");
            return;
        }

        // 资金检查
        int cost = GetBuildingCost(type);
        if (_money[0] < cost) { GD.Print($"[警告] 资金不足！需要 ${cost}，当前 ${_money[0]}"); _audio?.PlaySfx(AudioManager.Sfx.UiError); return; }

        // Q1：进入放置模式（玩家手动选择位置）
        _placementMode = type;
        if (_buildPanel != null) _buildPanel.ActivePlacement = type;
        QueueRedraw();
        _audio?.PlaySfx(AudioManager.Sfx.UiBuildStart);
        GD.Print($"[放置] 选择 {type} 放置位置，左键放置 / 右键取消");
    }

    // ---------- Q1 建筑放置 ----------
    public void CancelPlacement()
    {
        _placementMode = null;
        if (_buildPanel != null) _buildPanel.ActivePlacement = null;
        QueueRedraw();
    }

    private int GetBuildingCost(BuildingType type)
    {
        return type switch
        {
            BuildingType.PowerPlant => PowerPlantCost,
            BuildingType.Barracks => BarracksCost,
            BuildingType.WarFactory => WarFactoryCost,
            BuildingType.TechCenter => TechCenterCost,
            BuildingType.Turret => TurretCost,
            BuildingType.AntiAirTurret => AntiAirTurretCost,
            BuildingType.RepairPad => RepairPadCost,
            _ => 0
        };
    }

    /// <summary>G4：计算维修费用 = 造价 × 缺失血量比例 × 0.5。</summary>
    private int GetRepairCost(Building b)
    {
        float missing = b.MaxHealth - b.Health;
        if (missing <= 0) return 0;
        int buildCost = GetBuildingCost(b.Type);
        if (buildCost <= 0) return Mathf.Max(1, (int)missing);
        return Mathf.Max(1, Mathf.CeilToInt(buildCost * (missing / b.MaxHealth) * 0.5f));
    }

    private bool CanPlaceBuilding(Vector2 pos)
    {
        // 等距坐标边界检查
        var grid = IsoCoords.ScreenToGridF(pos.X, pos.Y);
        if (grid.X < 0 || grid.X >= TerrainGrid.GridSize || grid.Y < 0 || grid.Y >= TerrainGrid.GridSize)
            return false;
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && IsInstanceValid(b) && b.GlobalPosition.DistanceTo(pos) < 90f)
                return false;
        }
        return true;
    }

    private void PlaceBuildingAtMouse()
    {
        var type = _placementMode!.Value;
        int cost = GetBuildingCost(type);
        var pos = _camera.GetGlobalMousePosition();
        // 等距坐标边界检查+钳制
        var grid = IsoCoords.ScreenToGridF(pos.X, pos.Y);
        grid = new Vector2(
            Mathf.Clamp(grid.X, 1f, TerrainGrid.GridSize - 2f),
            Mathf.Clamp(grid.Y, 1f, TerrainGrid.GridSize - 2f)
        );
        pos = IsoCoords.GridToScreenF(grid.X, grid.Y);
        if (_money[0] < cost) { GD.Print("[放置] 资金不足"); CancelPlacement(); return; }
        if (!CanPlaceBuilding(pos)) { GD.Print("[放置] 位置被占用"); return; }
        _money[0] -= cost;
        SpawnBuilding(type, pos, teamId: 0);
        GD.Print($"蓝方建造{type}，扣 ${cost}，剩余 ${_money[0]}，位置 {pos}");
        _audio?.PlaySfx(AudioManager.Sfx.UiPlace);
        // 放一个就退出放置模式（红警2风格：点一次放一个）
        CancelPlacement();
    }

    public override void _Draw()
    {
        // ---- 阶段12-A4：核弹冲击波持久特效（始终绘制） ----
        foreach (var nuke in _activeNukeVisuals)
        {
            float progress = nuke.Age / nuke.Lifetime;
            float radius = NukeRadius * (0.3f + 0.7f * progress);
            // 外层冲击波（亮黄白→淡出）
            DrawArc(nuke.Position, radius, 0f, Mathf.Tau, 48,
                new Color(1f, 0.95f, 0.6f, (1f - progress) * 0.85f), 4f);
            // 内层辐射圈（橙红→暗）
            DrawArc(nuke.Position, radius * 0.6f, 0f, Mathf.Tau, 36,
                new Color(1f, 0.45f, 0.2f, (1f - progress) * 0.6f), 3f);
            // 中心辐射填充（绿色毒雾感）
            if (progress < 0.7f)
            {
                float fillR = NukeRadius * 0.5f * (1f - progress / 0.7f);
                DrawCircle(nuke.Position, fillR,
                    new Color(0.7f, 1f, 0.3f, (1f - progress) * 0.18f));
            }
        }

        // ---- 阶段12-A4：核弹目标选择准星 ----
        if (_nukeTargetMode)
        {
            var mousePos = _camera.GetGlobalMousePosition();
            // 爆炸范围预览圈
            DrawArc(mousePos, NukeRadius, 0f, Mathf.Tau, 64,
                new Color(1f, 0.25f, 0.15f, 0.55f), 2f);
            // 内圈危险标识
            DrawArc(mousePos, NukeRadius * 0.5f, 0f, Mathf.Tau, 48,
                new Color(1f, 0.4f, 0.2f, 0.35f), 1.5f);
            // 中心十字准星
            var cross = new Color(1f, 0.3f, 0.2f, 0.9f);
            DrawLine(mousePos - new Vector2(20, 0), mousePos + new Vector2(20, 0), cross, 2f);
            DrawLine(mousePos - new Vector2(0, 20), mousePos + new Vector2(0, 20), cross, 2f);
            // 四角小三角（瞄准框）
            float corn = 14f;
            var cornCol = new Color(1f, 0.3f, 0.2f, 0.95f);
            DrawLine(mousePos + new Vector2(-corn, -corn + 6), mousePos + new Vector2(-corn, -corn), cornCol, 2f);
            DrawLine(mousePos + new Vector2(-corn, -corn), mousePos + new Vector2(-corn + 6, -corn), cornCol, 2f);
            DrawLine(mousePos + new Vector2(corn - 6, -corn), mousePos + new Vector2(corn, -corn), cornCol, 2f);
            DrawLine(mousePos + new Vector2(corn, -corn), mousePos + new Vector2(corn, -corn + 6), cornCol, 2f);
            DrawLine(mousePos + new Vector2(-corn, corn - 6), mousePos + new Vector2(-corn, corn), cornCol, 2f);
            DrawLine(mousePos + new Vector2(-corn, corn), mousePos + new Vector2(-corn + 6, corn), cornCol, 2f);
            DrawLine(mousePos + new Vector2(corn - 6, corn), mousePos + new Vector2(corn, corn), cornCol, 2f);
            DrawLine(mousePos + new Vector2(corn, corn), mousePos + new Vector2(corn, corn - 6), cornCol, 2f);
            // 中心 ☢ 字样（用 Label 的复杂，这里画一个简化标识）
            DrawCircle(mousePos, 3f, new Color(1f, 0.3f, 0.2f, 0.95f));
        }

        // ---- 阶段12-A4：闪电风暴视觉（持续伤害区域 + 闪电柱 + 乌云 + 电光环） ----
        foreach (var lv in _activeLightnings)
        {
            float progress = lv.Age / lv.Lifetime;
            // 1. 地面电光填充圈（淡蓝色发光）
            DrawCircle(lv.Position, LightningRadius,
                new Color(0.3f, 0.6f, 1f, 0.15f * (1f - progress * 0.5f)));
            // 2. 多重电光环（白蓝色同心圆，向外扩散）
            for (int ring = 0; ring < 3; ring++)
            {
                float ringR = LightningRadius * (0.4f + 0.3f * ring) * (1f + 0.05f * Mathf.Sin(lv.Age * 8f + ring));
                DrawArc(lv.Position, ringR, 0f, Mathf.Tau, 48,
                    new Color(0.7f, 0.9f, 1f, (1f - progress) * 0.7f), 2f);
            }
            // 3. 中心闪电柱（程序化折线，每 0.08s 抖动一次，从地面向上延伸）
            DrawLightningBolt(lv.Position, _lightningBoltSeed, lv.Age);
            // 4. 上方暗乌云盘（深灰圆盘，模拟闪电来源）
            float cloudY = lv.Position.Y - 60f;
            DrawCircle(new Vector2(lv.Position.X, cloudY), 50f,
                new Color(0.2f, 0.2f, 0.3f, 0.6f));
            DrawCircle(new Vector2(lv.Position.X - 25f, cloudY + 5f), 35f,
                new Color(0.25f, 0.25f, 0.35f, 0.55f));
            DrawCircle(new Vector2(lv.Position.X + 30f, cloudY + 8f), 30f,
                new Color(0.2f, 0.2f, 0.3f, 0.55f));
        }

        // ---- 阶段12-A4：闪电风暴目标选择准星 ----
        if (_lightningTargetMode)
        {
            var mousePos = _camera.GetGlobalMousePosition();
            // 爆炸范围预览圈（蓝色）
            DrawArc(mousePos, LightningRadius, 0f, Mathf.Tau, 64,
                new Color(0.4f, 0.8f, 1f, 0.55f), 2f);
            // 内圈
            DrawArc(mousePos, LightningRadius * 0.5f, 0f, Mathf.Tau, 48,
                new Color(0.5f, 0.85f, 1f, 0.35f), 1.5f);
            // 中心十字准星（青蓝色）
            var cross = new Color(0.6f, 0.9f, 1f, 0.95f);
            DrawLine(mousePos - new Vector2(20, 0), mousePos + new Vector2(20, 0), cross, 2f);
            DrawLine(mousePos - new Vector2(0, 20), mousePos + new Vector2(0, 20), cross, 2f);
            // 中心光点
            DrawCircle(mousePos, 4f, new Color(0.7f, 0.95f, 1f, 0.95f));
        }

        // ---- Q1 建筑放置预览（等距菱形预览） ----
        if (_placementMode == null) return;
        var pos = _camera.GetGlobalMousePosition();
        // 钳制到地图范围内
        var posGrid = IsoCoords.ScreenToGridF(pos.X, pos.Y);
        posGrid = new Vector2(
            Mathf.Clamp(posGrid.X, 1f, TerrainGrid.GridSize - 2f),
            Mathf.Clamp(posGrid.Y, 1f, TerrainGrid.GridSize - 2f)
        );
        pos = IsoCoords.GridToScreenF(posGrid.X, posGrid.Y);
        bool ok = CanPlaceBuilding(pos) && _money[0] >= GetBuildingCost(_placementMode.Value);

        // 等距菱形预览：在鼠标位置画菱形
        var buildingColor = ok ? new Color(0.2f, 0.9f, 0.2f, 0.35f) : new Color(0.9f, 0.2f, 0.2f, 0.35f);
        var buildingBorder = ok ? new Color(0.3f, 1f, 0.3f, 0.8f) : new Color(1f, 0.3f, 0.3f, 0.8f);

        // 画菱形（建筑占位）
        var diamond = new Vector2[]
        {
            pos + new Vector2(0, -IsoCoords.HalfH),
            pos + new Vector2(IsoCoords.HalfW, 0),
            pos + new Vector2(0, IsoCoords.HalfH),
            pos + new Vector2(-IsoCoords.HalfW, 0),
        };
        // 填充
        DrawPolygon(diamond, new[] { buildingColor });
        // 边框
        for (int i = 0; i < 4; i++)
            DrawLine(diamond[i], diamond[(i + 1) % 4], buildingBorder, 2f);

        // 中心十字准线
        var crossCol = ok ? new Color(0.3f, 1f, 0.3f, 0.5f) : new Color(1f, 0.3f, 0.3f, 0.5f);
        DrawLine(pos - new Vector2(IsoCoords.HalfW, 0), pos + new Vector2(IsoCoords.HalfW, 0), crossCol, 1.0f);
        DrawLine(pos - new Vector2(0, IsoCoords.HalfH), pos + new Vector2(0, IsoCoords.HalfH), crossCol, 1.0f);
    }

    private void AIBuildLogic(int teamId)
    {
        if (!_bases.TryGetValue(teamId, out var baseB) || baseB == null || !IsInstanceValid(baseB)) return;

        int power = GetTeamPower(teamId);
        bool hasPower = HasBuilding(teamId, BuildingType.PowerPlant);
        bool hasBarracks = HasBuilding(teamId, BuildingType.Barracks);
        bool hasWarFactory = HasBuilding(teamId, BuildingType.WarFactory);
        bool hasTechCenter = HasBuilding(teamId, BuildingType.TechCenter);

        // 优先级1：没电站就建电站（基地消耗50电，必须建电站）
        if (!hasPower && _money[teamId] >= PowerPlantCost)
        {
            _money[teamId] -= PowerPlantCost;
            SpawnBuilding(BuildingType.PowerPlant, GetAIBuildPosition(teamId, BuildingType.PowerPlant), teamId);
            GD.Print($"[AI] Team {teamId} built PowerPlant, ${_money[teamId]} left");
            return;
        }

        // 优先级2：电力不足（<30）时补电站
        if (hasPower && power < 30 && _money[teamId] >= PowerPlantCost)
        {
            _money[teamId] -= PowerPlantCost;
            SpawnBuilding(BuildingType.PowerPlant, GetAIBuildPosition(teamId, BuildingType.PowerPlant), teamId);
            GD.Print($"[AI] Team {teamId} built PowerPlant (low power), ${_money[teamId]} left");
            return;
        }

        // 优先级3：建兵营
        if (hasPower && !hasBarracks && _money[teamId] >= BarracksCost)
        {
            _money[teamId] -= BarracksCost;
            SpawnBuilding(BuildingType.Barracks, GetAIBuildPosition(teamId, BuildingType.Barracks), teamId);
            GD.Print($"[AI] Team {teamId} built Barracks, ${_money[teamId]} left");
            return;
        }

        // 优先级4：建战车工厂
        if (hasBarracks && !hasWarFactory && _money[teamId] >= WarFactoryCost
            && IsBuildingUnlockedByEra(teamId, BuildingType.WarFactory))
        {
            _money[teamId] -= WarFactoryCost;
            SpawnBuilding(BuildingType.WarFactory, GetAIBuildPosition(teamId, BuildingType.WarFactory), teamId);
            GD.Print($"[AI] Team {teamId} built WarFactory, ${_money[teamId]} left");
            return;
        }

        // 优先级5：建科技中心（解锁高级兵种）
        if (_aiUsesTech && hasWarFactory && !hasTechCenter && _money[teamId] >= TechCenterCost && power >= 0
            && IsBuildingUnlockedByEra(teamId, BuildingType.TechCenter))
        {
            _money[teamId] -= TechCenterCost;
            SpawnBuilding(BuildingType.TechCenter, GetAIBuildPosition(teamId, BuildingType.TechCenter), teamId);
            GD.Print($"[AI] Team {teamId} built TechCenter, ${_money[teamId]} left");
            return;
        }

        // 优先级6：后期电力不够就再建电站
        if (hasTechCenter && power < 50 && _money[teamId] >= PowerPlantCost)
        {
            _money[teamId] -= PowerPlantCost;
            SpawnBuilding(BuildingType.PowerPlant, GetAIBuildPosition(teamId, BuildingType.PowerPlant), teamId);
            GD.Print($"[AI] Team {teamId} built PowerPlant (for tech center), ${_money[teamId]} left");
            return;
        }

        // ---- 阶段12-A1+A2：防御建筑与维修厂 ----
        // 优先级7：建造维修厂（已建车厂且无维修厂且资金充裕）
        if (hasWarFactory && !HasBuilding(teamId, BuildingType.RepairPad)
            && _money[teamId] >= RepairPadCost + 200 && power >= 0
            && IsBuildingUnlockedByEra(teamId, BuildingType.RepairPad))
        {
            _money[teamId] -= RepairPadCost;
            SpawnBuilding(BuildingType.RepairPad, GetAIBuildPosition(teamId, BuildingType.RepairPad), teamId);
            GD.Print($"[AI] Team {teamId} built RepairPad, ${_money[teamId]} left");
            return;
        }

        // 优先级8：建造机枪塔（已建兵营，每阵营最多2座，资金充裕）
        int turretCount = CountBuildingOfType(teamId, BuildingType.Turret);
        if (hasBarracks && turretCount < 2
            && _money[teamId] >= TurretCost + 300 && power >= 0
            && IsBuildingUnlockedByEra(teamId, BuildingType.Turret))
        {
            _money[teamId] -= TurretCost;
            SpawnBuilding(BuildingType.Turret, GetAIBuildPosition(teamId, BuildingType.Turret), teamId);
            GD.Print($"[AI] Team {teamId} built Turret #{turretCount + 1}, ${_money[teamId]} left");
            return;
        }

        // 优先级9：建造防空炮（已建车厂，每阵营最多2座）
        int aaCount = CountBuildingOfType(teamId, BuildingType.AntiAirTurret);
        if (hasWarFactory && aaCount < 2
            && _money[teamId] >= AntiAirTurretCost + 300 && power >= 0
            && IsBuildingUnlockedByEra(teamId, BuildingType.AntiAirTurret))
        {
            _money[teamId] -= AntiAirTurretCost;
            SpawnBuilding(BuildingType.AntiAirTurret, GetAIBuildPosition(teamId, BuildingType.AntiAirTurret), teamId);
            GD.Print($"[AI] Team {teamId} built AntiAirTurret #{aaCount + 1}, ${_money[teamId]} left");
            return;
        }

        // E7：优先级10：建造机场（已建科技中心，每阵营最多1座）
        if (hasTechCenter && !HasBuilding(teamId, BuildingType.Airfield)
            && _money[teamId] >= AirfieldCost + 300 && power >= 0
            && IsBuildingUnlockedByEra(teamId, BuildingType.Airfield))
        {
            _money[teamId] -= AirfieldCost;
            SpawnBuilding(BuildingType.Airfield, GetAIBuildPosition(teamId, BuildingType.Airfield), teamId);
            GD.Print($"[AI] Team {teamId} built Airfield, ${_money[teamId]} left");
            return;
        }
        // E9：优先级11：建造船厂（已建科技中心，每阵营最多1座）
        if (hasTechCenter && !HasBuilding(teamId, BuildingType.Shipyard)
            && _money[teamId] >= ShipyardCost + 300 && power >= 0
            && IsBuildingUnlockedByEra(teamId, BuildingType.Shipyard))
        {
            _money[teamId] -= ShipyardCost;
            SpawnBuilding(BuildingType.Shipyard, GetAIBuildPosition(teamId, BuildingType.Shipyard), teamId);
            GD.Print($"[AI] Team {teamId} built Shipyard, ${_money[teamId]} left");
            return;
        }
        // E10：优先级12-14：超武建筑（已建科技中心）
        if (hasTechCenter && !HasBuilding(teamId, BuildingType.NukeSilo)
            && _money[teamId] >= NukeSiloCost + 300 && power >= 0)
        {
            _money[teamId] -= NukeSiloCost;
            SpawnBuilding(BuildingType.NukeSilo, GetAIBuildPosition(teamId, BuildingType.NukeSilo), teamId);
            GD.Print($"[AI] Team {teamId} built NukeSilo, ${_money[teamId]} left");
            return;
        }
        if (hasTechCenter && !HasBuilding(teamId, BuildingType.LightningTower)
            && _money[teamId] >= LightningTowerCost + 300 && power >= 0)
        {
            _money[teamId] -= LightningTowerCost;
            SpawnBuilding(BuildingType.LightningTower, GetAIBuildPosition(teamId, BuildingType.LightningTower), teamId);
            GD.Print($"[AI] Team {teamId} built LightningTower, ${_money[teamId]} left");
            return;
        }
        if (hasTechCenter && !HasBuilding(teamId, BuildingType.MissileSilo)
            && _money[teamId] >= MissileSiloCost + 300 && power >= 0)
        {
            _money[teamId] -= MissileSiloCost;
            SpawnBuilding(BuildingType.MissileSilo, GetAIBuildPosition(teamId, BuildingType.MissileSilo), teamId);
            GD.Print($"[AI] Team {teamId} built MissileSilo, ${_money[teamId]} left");
            return;
        }
    }

    /// <summary>阶段12-A1：统计某阵营指定类型的建筑数量（用于AI建造限制）。</summary>
    private int CountBuildingOfType(int teamId, BuildingType type)
    {
        int count = 0;
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b.TeamId == teamId && b.Type == type && IsInstanceValid(b))
                count++;
        }
        return count;
    }

    // ---------- 阶段12-A4 超武系统（核弹）----------

    // ---------- 阶段12-A4 闪电风暴 ----------

    /// <summary>AI 阵营 Tick：在每个 _aiThinkInterval 周期内为每个 AI 阵营独立调用。</summary>
    private void AITickForTeam(int teamId)
    {
        // 0. 该阵营基地已灭则跳过
        if (!_bases.TryGetValue(teamId, out var teamBase) || !IsInstanceValid(teamBase)) return;

        // 0. 建筑建造优先
        AIBuildLogic(teamId);

        bool savingForTech = HasBuilding(teamId, BuildingType.WarFactory) && !HasBuilding(teamId, BuildingType.TechCenter);

        // 1. 自动造兵（检查建筑前置 + 电力，不超过上限）
        var teamUnits = CountUnitsOfTeam(teamId);
        int teamQueued = CountQueuedUnitsOfTeam(teamId);
        if (!savingForTech && teamUnits + teamQueued < _unitCap && GetTeamPower(teamId) >= 0)
        {
            // 有科技中心时攒钱优先造高级兵种
            bool hasTech = HasBuilding(teamId, BuildingType.TechCenter);
            if (!(hasTech && _money[teamId] < RocketLauncherCost && teamUnits >= 3))
            {
                var types = new List<UnitType>();
                if (HasBuilding(teamId, BuildingType.Barracks))
                {
                    types.Add(UnitType.LightTank);
                    types.Add(UnitType.Infantry);
                    types.Add(UnitType.Grenadier);       // E6
                    types.Add(UnitType.FlameInfantry);   // E6
                    types.Add(UnitType.Sniper);           // E6
                    types.Add(UnitType.Thief);            // E6b
                    types.Add(UnitType.RocketInfantry);   // E7
                    types.Add(UnitType.Fighter);           // E7
                    types.Add(UnitType.Helicopter);        // E7
                    types.Add(UnitType.Bomber);            // E8
                    types.Add(UnitType.Scout);             // E8
                    types.Add(UnitType.TransportHeli);      // E8
                    // E9：海军
                    types.Add(UnitType.Destroyer);
                    types.Add(UnitType.Submarine);
                    types.Add(UnitType.LandingCraft);
                    types.Add(UnitType.AircraftCarrier);
                }
                if (HasBuilding(teamId, BuildingType.WarFactory))
                {
                    types.Add(UnitType.HeavyTank);
                    types.Add(UnitType.Artillery);
                    types.Add(UnitType.AntiAir);
                    types.Add(UnitType.Engineer);
                    types.Add(UnitType.Transport);        // E6
                    types.Add(UnitType.Hero);              // E6b
                    types.Add(UnitType.Spy);               // E6b
                    types.Add(UnitType.Thief);             // E6b
                }
                if (hasTech)
                {
                    types.Add(UnitType.RocketLauncher);
                    types.Add(UnitType.MissileTank);
                }
                if (types.Count > 0)
                {
                    types.Sort((a, b) => GetUnitCost(b).CompareTo(GetUnitCost(a)));
                    // 步兵作为廉价填线兵：35%概率优先生产，保证其稳定出场
                    if (types.Contains(UnitType.Infantry) && GD.Randf() < 0.35f)
                    {
                        types.Remove(UnitType.Infantry);
                        types.Insert(0, UnitType.Infantry);
                    }
                    // 工程车：15%概率优先生产，保证修理/占领功能稳定出场
                    if (types.Contains(UnitType.Engineer) && GD.Randf() < 0.15f)
                    {
                        types.Remove(UnitType.Engineer);
                        types.Insert(0, UnitType.Engineer);
                    }
                    foreach (var t in types)
                    {
                        int c = GetUnitCost(t);
                        if (_money[teamId] >= c)
                        {
                            var producer = FindProducerForUnit(t, teamId);
                            if (producer != null)
                            {
                                _money[teamId] -= c;
                                producer.EnqueueProduction(UnitTypeToProductionType(t));
                                GD.Print($"[AI] Team {teamId} queued {t}, ${_money[teamId]} left, {producer.BuildingName}队列{producer.QueueCount}");
                                break;
        }

        // 5. G7: AI间谍任务 — 每20秒尝试一次
        if (!_aiSpyCooldowns.TryGetValue(teamId, out int spyCd))
            _aiSpyCooldowns[teamId] = 0;
        _aiSpyCooldowns[teamId]--;
        if (_aiSpyCooldowns[teamId] <= 0)
        {
            _aiSpyCooldowns[teamId] = 20;
            AISpyMission(teamId);
        }
        }
                    }
                }
            }
        }

        // 2. 矿车耗损自动补充（最多 3 辆）
        var teamHarvesters = CountHarvestersOfTeam(teamId);
        if (_money[teamId] >= HarvesterCost && teamHarvesters < 3)
        {
            var harvProducer = FindProducerBuilding(BuildingType.Base, teamId);
            if (harvProducer != null)
            {
                _money[teamId] -= HarvesterCost;
                harvProducer.EnqueueProduction(ProductionType.Harvester);
            }
        }

        // 3. 占领战略点
        if (_aiCapturesPoints)
        {
            if (!_aiCaptureCounters.TryGetValue(teamId, out int cap))
                _aiCaptureCounters[teamId] = 0;
            _aiCaptureCounters[teamId]++;
            if (_aiCaptureCounters[teamId] >= 3)
            {
                _aiCaptureCounters[teamId] = 0;
                AITryCaptureStrategicPoint(teamId);
            }
        }

        // 4. E10：超武——核弹需核弹发射井，闪电需闪电风暴塔，巡航导弹需导弹发射井
        if (HasBuilding(teamId, BuildingType.NukeSilo))
        {
            if (!_aiNukeCooldowns.ContainsKey(teamId))
                _aiNukeCooldowns[teamId] = NukeCooldownDuration;

            if (_aiNukeCooldowns[teamId] <= 0f)
            {
                var target = FindNukeTargetForAi(teamId);
                if (target.HasValue)
                {
                    ApplyNuke(target.Value, teamId);
                    _aiNukeCooldowns[teamId] = NukeCooldownDuration;
                }
            }
        }

        if (HasBuilding(teamId, BuildingType.LightningTower))
        {
            if (!_aiLightningCooldowns.ContainsKey(teamId))
                _aiLightningCooldowns[teamId] = LightningCooldownDuration;

            if (_aiLightningCooldowns[teamId] <= 0f)
            {
                var target = FindNukeTargetForAi(teamId);
                if (target.HasValue)
                {
                    ApplyLightning(target.Value, teamId);
                    _aiLightningCooldowns[teamId] = LightningCooldownDuration;
                }
            }
        }

        // E10：AI巡航导弹
        if (HasBuilding(teamId, BuildingType.MissileSilo))
        {
            if (!_aiMissileCooldowns.ContainsKey(teamId))
                _aiMissileCooldowns[teamId] = MissileCooldownDuration;

            if (_aiMissileCooldowns[teamId] <= 0f)
            {
                var target = FindNukeTargetForAi(teamId);
                if (target.HasValue)
                {
                    ApplyCruiseMissile(target.Value, teamId);
                    _aiMissileCooldowns[teamId] = MissileCooldownDuration;
                }
            }
        }
        }

    /// <summary>蓝方测试 AI：模拟玩家自动造兵（仅 headless 模式）。</summary>
    private void BlueTestAITick()
    {
        // G4：自动维修血量低于50%的蓝方建筑
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b.TeamId == 0 && IsInstanceValid(b)
                && b.NeedsRepair && b.Health < b.MaxHealth * 0.5f)
            {
                int cost = GetRepairCost(b);
                if (_money[0] >= cost)
                {
                    _money[0] -= cost;
                    b.Repair();
                    GD.Print($"[BlueAI] 维修{b.BuildingName}，扣 ${cost}，剩余 ${_money[0]}");
                }
            }
        }

        // 占领战略点：每 3 次 tick 派一个单位去未占领战略点
        if (_aiCapturesPoints)
        {
            _blueCaptureCounter++;
            if (_blueCaptureCounter >= 3)
            {
                _blueCaptureCounter = 0;
                AITryCaptureStrategicPoint(0);
            }
        }

        // 先建建筑
        AIBuildLogic(0);

        bool savingForTech = HasBuilding(0, BuildingType.WarFactory) && !HasBuilding(0, BuildingType.TechCenter);
        if (savingForTech) return;

        int blueUnits = CountUnitsOfTeam(0);
        // 优先补矿车
        int blueHarvesters = CountHarvestersOfTeam(0);
        if (_money[0] >= HarvesterCost && blueHarvesters < 3)
        {
            var harvProducer = FindProducerBuilding(BuildingType.Base, 0);
            if (harvProducer != null)
            {
                _money[0] -= HarvesterCost;
                harvProducer.EnqueueProduction(ProductionType.Harvester);
                GD.Print($"[BlueAI] Blue queued harvester, ${_money[0]} left");
                return;
            }
        }

        // 造兵（检查建筑前置 + 电力）
        int blueQueued = CountQueuedUnitsOfTeam(0);
        if (blueUnits + blueQueued >= _unitCap || GetTeamPower(0) < 0) return;

        // 有科技中心时攒钱优先造高级兵种
        bool hasTech = HasBuilding(0, BuildingType.TechCenter);
        if (hasTech && _money[0] < RocketLauncherCost && blueUnits >= 3) return;

        var types = new List<UnitType>();
        if (HasBuilding(0, BuildingType.Barracks))
        {
            types.Add(UnitType.LightTank);
            types.Add(UnitType.Infantry);
        }
        if (HasBuilding(0, BuildingType.WarFactory))
        {
            types.Add(UnitType.HeavyTank);
            types.Add(UnitType.Artillery);
            types.Add(UnitType.AntiAir);
            types.Add(UnitType.Engineer);
        }
        if (hasTech)
        {
            types.Add(UnitType.RocketLauncher);
            types.Add(UnitType.MissileTank);
        }
        // E9：蓝方AI也生产海军
        if (HasBuilding(0, BuildingType.Shipyard))
        {
            types.Add(UnitType.Destroyer);
            types.Add(UnitType.Submarine);
            types.Add(UnitType.LandingCraft);
        }
        if (types.Count == 0) return;

        types.Sort((a, b) => GetUnitCost(b).CompareTo(GetUnitCost(a)));
        // 步兵作为廉价填线兵：35%概率优先生产，保证其稳定出场
        if (types.Contains(UnitType.Infantry) && GD.Randf() < 0.35f)
        {
            types.Remove(UnitType.Infantry);
            types.Insert(0, UnitType.Infantry);
        }
        // 工程车：15%概率优先生产，保证修理/占领功能稳定出场
        if (types.Contains(UnitType.Engineer) && GD.Randf() < 0.15f)
        {
            types.Remove(UnitType.Engineer);
            types.Insert(0, UnitType.Engineer);
        }
        foreach (var t in types)
        {
            int c = GetUnitCost(t);
            if (_money[0] >= c)
            {
                var producer = FindProducerForUnit(t, 0);
                if (producer != null)
                {
                    _money[0] -= c;
                    producer.EnqueueProduction(UnitTypeToProductionType(t));
                    GD.Print($"[BlueAI] Blue queued {t}, ${_money[0]} left, {producer.BuildingName}队列{producer.QueueCount}");
                }
                return;
            }
        }
    }

    /// <summary>AI 占领战略点：派最近的己方战斗单位去最近的非己方战略点。</summary>
    private void AITryCaptureStrategicPoint(int teamId)
    {
        // M3修复: AI优先占领己方已控制战略点附近的目标，触发G8连锁占领+50%加速
        // 收集己方已占领的战略点位置
        var ownedPositions = new System.Collections.Generic.List<Vector2>();
        foreach (var child in _strategicPointsNode.GetChildren())
        {
            if (child is StrategicPoint sp0 && IsInstanceValid(sp0) && sp0.OwningTeam == teamId)
                ownedPositions.Add(sp0.GlobalPosition);
        }

        // 对所有未占领的战略点排序：靠近己方已占领点的优先（连锁加成）
        var targets = new System.Collections.Generic.List<(StrategicPoint sp, float priority)>();
        foreach (var child in _strategicPointsNode.GetChildren())
        {
            if (child is not StrategicPoint sp || !IsInstanceValid(sp)) continue;
            if (sp.OwningTeam == teamId) continue;

            // 优先级计算：有己方占领点在80px内=最高优先级（连锁加成）
            float priority = 0f;
            foreach (var pos in ownedPositions)
            {
                float d = sp.GlobalPosition.DistanceTo(pos);
                if (d < CaptureBonus.ChainRange)
                    priority += 1000f; // 连锁范围内，大幅提升优先级
                else
                    priority += 500f / (d + 1f); // 距离越近优先级越高
            }
            if (priority == 0f) priority = 1f; // 无己方占领点时按默认顺序
            targets.Add((sp, priority));
        }
        // 按优先级降序排序
        targets.Sort((a, b) => b.priority.CompareTo(a.priority));

        foreach (var (sp, _) in targets)
        {
            Unit? nearest = null;
            float nearestDist = float.MaxValue;
            foreach (var uc in _unitsNode.GetChildren())
            {
                if (uc is Unit u && IsInstanceValid(u) && u.TeamId == teamId && u.AttackDamage > 0f)
                {
                    float d = u.GlobalPosition.DistanceTo(sp.GlobalPosition);
                    if (d < nearestDist) { nearestDist = d; nearest = u; }
                }
            }
            if (nearest != null && nearestDist < 1600f)
            {
                nearest.CommandMove(sp.GlobalPosition);
                GD.Print($"[AI] Team {teamId} sending unit to capture point at {sp.GlobalPosition} (dist {(int)nearestDist})");
                return;
            }
        }

        // E5：AI 也尝试占领油田
        foreach (var child in _resourcesNode.GetChildren())
        {
            if (child is not ResourceNode rn || !IsInstanceValid(rn)) continue;
            if (rn.ResourceType != ResourceType.OilField) continue;
            if (rn.OilOwner == teamId) continue;

            Unit? nearest = null;
            float nearestDist = float.MaxValue;
            foreach (var uc in _unitsNode.GetChildren())
            {
                if (uc is Unit u && IsInstanceValid(u) && u.TeamId == teamId && u.AttackDamage > 0f)
                {
                    float d = u.GlobalPosition.DistanceTo(rn.GlobalPosition);
                    if (d < nearestDist) { nearestDist = d; nearest = u; }
                }
            }
            if (nearest != null && nearestDist < 1400f)
            {
                nearest.CommandMove(rn.GlobalPosition);
                GD.Print($"[AI] Team {teamId} sending unit to capture oil field at {rn.GlobalPosition} (dist {(int)nearestDist})");
                return;
            }
        }
    }

    // ---------- 右键命令 ----------
    private void HandleRightClick(Vector2 worldPos)
    {
        var friendlyUnits = GetSelectedFriendlyUnits();

        // G2：如果只选中了生产建筑（没选中单位），右键设集结点
        if (friendlyUnits.Count == 0)
        {
            var producer = GetSelectedFriendlyProducerBuilding();
            if (producer != null)
            {
                // U1: 右键点在建筑自身上 → 取消队列中最后一个生产订单
                var clickedBuilding = PickBuildingAt(worldPos, requireEnemy: false);
                if (clickedBuilding == producer)
                {
                    var cancelled = producer.CancelLastProduction();
                    if (cancelled.HasValue)
                    {
                        GD.Print($"[取消生产] {producer.BuildingName} 取消: {cancelled.Value}");
                        _audio?.PlaySfx(AudioManager.Sfx.UiClick);
                    }
                    return;
                }
                // 否则设集结点
                producer.SetRallyPoint(worldPos);
                GD.Print($"[集结点] {producer.BuildingName} 集结点 -> {worldPos}");
                return;
            }
        }

        // 没有选中单位则不做任何操作
        if (friendlyUnits.Count == 0) return;

        // 优先：点击敌方单位 → 攻击
        var enemyUnit = PickUnitAt(worldPos, requireEnemy: true);
        if (enemyUnit != null)
        {
            foreach (var unit in friendlyUnits)
                unit.CommandAttack(enemyUnit);
            return;
        }
        // 点击敌方建筑 → 攻击建筑（G7: 间谍则执行间谍任务）
        var enemyBuilding = PickBuildingAt(worldPos, requireEnemy: true);
        if (enemyBuilding != null)
        {
            // G7: 间谍右键敌方建筑 → 触发间谍任务
            var spyUnits = friendlyUnits.Where(u => u.Type == UnitType.Spy).ToList();
            var nonSpyUnits = friendlyUnits.Where(u => u.Type != UnitType.Spy).ToList();

            // 非间谍单位正常攻击建筑
            foreach (var unit in nonSpyUnits)
                unit.CommandAttackBuilding(enemyBuilding);

            // 间谍执行任务
            foreach (var spy in spyUnits)
            {
                if (spy.IsSpyOnMission) continue; // 已在执行任务
                var mission = SpyMission.ChooseMission(enemyBuilding.Type);
                spy.CommandSpyMission(enemyBuilding, mission);
            }
            return;
        }

        // E6：步兵点击友方运输车 → 上车
        var friendlyTransport = PickTransportAt(worldPos, requireFriendly: true);
        if (friendlyTransport != null)
        {
            foreach (var unit in friendlyUnits)
            {
                if (Unit.IsInfantryType(unit.Type) && unit != friendlyTransport)
                {
                    // 步兵移动到运输车位置后上车
                    unit.CommandMove(friendlyTransport.GlobalPosition);
                    // 在到达时通过 ProcessInteraction 完成上车
                    unit._embarkTarget = friendlyTransport;
                }
            }
            if (friendlyUnits.Any(u => Unit.IsInfantryType(u.Type)))
                return; // 有步兵上车命令，不执行移动
        }
        // 普通移动：保持简易队形
        int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(friendlyUnits.Count)));
        // E4：工程单位右键不可通行地形 → 触发地形改造
        var terrainCell = _terrain.GetCellAtWorld(worldPos.X, worldPos.Y);
        Unit.TerrainModType modType = DetectTerrainMod(terrainCell);
        if (modType != Unit.TerrainModType.None)
        {
            // 仅工程单位执行地形改造，非工程单位仍正常移动
            for (int i = 0; i < friendlyUnits.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                if (friendlyUnits[i].IsEngineerUnit)
                    friendlyUnits[i].CommandTerrainMod(modType, worldPos + new Vector2(col * 40, row * 40));
                else
                    friendlyUnits[i].CommandMove(worldPos + new Vector2(col * 40, row * 40));
            }
        }
        else
        {
            for (int i = 0; i < friendlyUnits.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                friendlyUnits[i].CommandMove(worldPos + new Vector2(col * 40, row * 40));
            }
        }
        // 阶段12-C：下令移动音效
        _audio?.PlaySfx(AudioManager.Sfx.Move);
    }

    /// <summary>E4：检测右键位置需要的地形改造类型。</summary>
    private Unit.TerrainModType DetectTerrainMod(TerrainCell cell)
    {
        // 山脉→削平
        if (cell.Type == TerrainType.Mountain && !cell.HasTunnel)
            return Unit.TerrainModType.Flatten;
        // 深水→架桥
        if (cell.Type == TerrainType.DeepWater && !cell.HasBridge && !cell.HasTunnel)
            return Unit.TerrainModType.Bridge;
        // 浅水→架桥
        if (cell.Type == TerrainType.ShallowWater && !cell.HasBridge)
            return Unit.TerrainModType.Bridge;
        return Unit.TerrainModType.None;
    }

    // ---------- 选择 ----------
    private void HandleSelection(Vector2 start, Vector2 end)
    {
        if (!Input.IsKeyPressed(Key.Shift))
        {
            foreach (var o in _selected)
            {
                if (IsInstanceValid(o))
                {
                    if (o is Unit u) u.SetSelected(false);
                    else if (o is Building b) b.SetSelected(false);
                }
            }
            _selected.Clear();
        }

        var min = new Vector2(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y));
        var max = new Vector2(Mathf.Max(start.X, end.X), Mathf.Max(start.Y, end.Y));
        var rect = new Rect2(min, max - min);

        if (rect.Size.Length() < 10f)
        {
            // 单击：优先建筑 → 单位
            var building = PickBuildingAt(end, requireEnemy: false);
            if (building != null && building.TeamId == 0)
            {
                building.SetSelected(true);
                _selected.Add(building);
                return;
            }
            var unit = PickUnitAt(end, requireEnemy: false);
            if (unit != null && unit.TeamId == 0)
            {
                unit.SetSelected(true);
                _selected.Add(unit);
            }
            return;
        }

        // 框选蓝方单位和建筑
        foreach (var child in _unitsNode.GetChildren())
        {
            if (child is Unit u && u.TeamId == 0 && rect.HasPoint(u.GlobalPosition))
            {
                u.SetSelected(true);
                if (!_selected.Contains(u)) _selected.Add(u);
            }
        }
        foreach (var child in _buildingsNode.GetChildren())
        {
            if (child is Building b && b.TeamId == 0 && rect.HasPoint(b.GlobalPosition))
            {
                b.SetSelected(true);
                if (!_selected.Contains(b)) _selected.Add(b);
            }
        }

        // 阶段12-C：选中单位音效
        if (_selected.Count > 0)
            _audio?.PlaySfx(AudioManager.Sfx.Select);
    }

    private void UpdateDragBox(Vector2 start, Vector2 end)
    {
        var min = new Vector2(Mathf.Min(start.X, end.X), Mathf.Min(start.Y, end.Y));
        var max = new Vector2(Mathf.Max(start.X, end.X), Mathf.Max(start.Y, end.Y));
        _dragBox.ClearPoints();
        _dragBox.AddPoint(min);
        _dragBox.AddPoint(new Vector2(max.X, min.Y));
        _dragBox.AddPoint(max);
        _dragBox.AddPoint(new Vector2(min.X, max.Y));
        _dragBox.AddPoint(min);
    }

    // ---------- 场景生成辅助 ----------
    private Unit SpawnUnit(UnitType type, Vector2 pos, int teamId, bool autoAI)
    {
        var u = _unitScene.Instantiate<Unit>();
        u.InitAsType(type);
        u.GlobalPosition = pos;
        u.TeamId = teamId;
        u.AutoAI = autoAI;
        _unitsNode.AddChild(u);
        return u;
    }

    private Harvester SpawnHarvester(Vector2 pos, int teamId, Building home)
    {
        var h = _harvesterScene.Instantiate<Harvester>();
        h.GlobalPosition = pos;
        h.TeamId = teamId;
        h.HomeBase = home;
        _unitsNode.AddChild(h);
        return h;
    }

    private Building SpawnBuilding(BuildingType type, Vector2 pos, int teamId)
    {
        var b = _buildingScene.Instantiate<Building>();
        b.InitAsType(type);
        b.GlobalPosition = pos;
        b.TeamId = teamId;
        b.Destroyed += OnBuildingDestroyed;
        _buildingsNode.AddChild(b);
        // G5: 建造触发尤里卡
        OnEurekaBuild(teamId);
        // P0-1: 注册建筑障碍到PathFinder
        RegisterBuildingObstacle(b);
        return b;
    }

    /// <summary>P0-1: 将建筑位置注册为PathFinder障碍（3×3格子）。</summary>
    private void RegisterBuildingObstacle(Building b)
    {
        if (_pathFinder == null) return;
        _terrain.WorldToGrid(b.GlobalPosition.X, b.GlobalPosition.Y, out int gx, out int gy);
        _pathFinder.AddBuilding(gx, gy, 1);
    }

    /// <summary>P0-1: 建筑被摧毁/出售时的回调，移除PathFinder障碍。</summary>
    private void OnBuildingDestroyed(Building b)
    {
        if (_pathFinder == null) return;
        _terrain.WorldToGrid(b.GlobalPosition.X, b.GlobalPosition.Y, out int gx, out int gy);
        _pathFinder.RemoveBuilding(gx, gy, 1);
    }

    // ---------- G2 生产系统辅助 ----------

    /// <summary>生产完成回调：由 Building._Process 在生产计时归零时调用。</summary>
    public void OnUnitProduced(ProductionType type, Building producer)
    {
        if (!IsInstanceValid(producer)) return;
        int teamId = producer.TeamId;
        Vector2 spawnPos = producer.GlobalPosition;
        // 出兵方向：朝地图中心（任意非玩家阵营也按统一规则，避免 AI 反向偏出地图）
        Vector2 mapCenter = new(1000, 1000);
        Vector2 dir = (mapCenter - spawnPos).Normalized();
        if (dir == Vector2.Zero) dir = new Vector2(0, 1);
        Vector2 offset = dir * 90f;

        if (type == ProductionType.Harvester)
        {
            var home = FindHomeBase(teamId);
            if (home == null) return; // 基地被摧毁，无法生成矿车
            SpawnHarvester(spawnPos + new Vector2(60, 0), teamId, home);
            GD.Print($"[生产完成] {producer.BuildingName} (Team {teamId}) 生产矿车");
        }
        else
        {
            var unitType = ProductionTypeToUnitType(type);
            // 玩家(0)保留手动操控；任何 AI 阵营(1..7)都开放 AutoAI
            bool autoAI = teamId != PlayerTeamId;
            var unit = SpawnUnit(unitType, spawnPos + offset, teamId, autoAI);
            // G1+G2+G3: 新单位立即应用科技/时代/战术卡效果
            ApplyAllModifiersToUnit(unit, teamId);
            // G2：集结点 —— 新单位自动移动过去
            if (producer.RallyPoint.HasValue)
            {
                unit.CommandMove(producer.RallyPoint.Value);
            }
            GD.Print($"[生产完成] {producer.BuildingName} (Team {teamId}) 生产 {unitType}");
        }

        // 阶段12-C：玩家方生产完成音效
        if (teamId == PlayerTeamId)
            _audio?.PlaySfx(AudioManager.Sfx.UiUnitReady);
    }

    // ---- 阶段12-C 音效回调（供 Unit/Building 调用） ----

    private Building? FindHomeBase(int teamId)
    {
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b.TeamId == teamId && b.Type == BuildingType.Base && IsInstanceValid(b))
                return b;
        }
        return null;
    }

    private static BuildingType GetProducerForUnit(UnitType unitType) => unitType switch
    {
        UnitType.LightTank => BuildingType.Barracks,
        UnitType.Infantry => BuildingType.Barracks,
        UnitType.Sapper => BuildingType.Barracks,
        UnitType.Grenadier => BuildingType.Barracks,       // E6
        UnitType.Sniper => BuildingType.Barracks,          // E6
        UnitType.FlameInfantry => BuildingType.Barracks,   // E6
        UnitType.ChiefEngineer => BuildingType.TechCenter,
        UnitType.HeavyTank => BuildingType.WarFactory,
        UnitType.Artillery => BuildingType.WarFactory,
        UnitType.AntiAir => BuildingType.WarFactory,
        UnitType.Engineer => BuildingType.WarFactory,
            UnitType.Transport => BuildingType.WarFactory,     // E6
            UnitType.Hero => BuildingType.TechCenter,          // E6b
            UnitType.Spy => BuildingType.TechCenter,           // E6b
            UnitType.Thief => BuildingType.Barracks,           // E6b
            UnitType.Fighter => BuildingType.Airfield,          // E7
            UnitType.Helicopter => BuildingType.Airfield,       // E7
            UnitType.RocketInfantry => BuildingType.Barracks,   // E7
            UnitType.Bomber => BuildingType.Airfield,            // E8
            UnitType.Scout => BuildingType.Airfield,             // E8
            UnitType.TransportHeli => BuildingType.Airfield,     // E8
            // E9：海军单位由船厂生产
            UnitType.Destroyer => BuildingType.Shipyard,
            UnitType.Submarine => BuildingType.Shipyard,
            UnitType.AircraftCarrier => BuildingType.Shipyard,
            UnitType.LandingCraft => BuildingType.Shipyard,
        UnitType.RocketLauncher => BuildingType.TechCenter,
        UnitType.MissileTank => BuildingType.TechCenter,
        _ => BuildingType.Base
    };

    private Building? FindProducerForUnit(UnitType unitType, int teamId)
    {
        return FindProducerBuilding(GetProducerForUnit(unitType), teamId);
    }

    /// <summary>在指定阵营中查找队列最短且未满的同类建筑（实现多建筑并行生产）。</summary>
    private Building? FindProducerBuilding(BuildingType buildingType, int teamId)
    {
        Building? best = null;
        int minQueue = int.MaxValue;
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b.TeamId == teamId && b.Type == buildingType && IsInstanceValid(b))
            {
                int q = b.QueueCount;
                if (q < Building.MaxQueueSize && q < minQueue)
                {
                    minQueue = q;
                    best = b;
                }
            }
        }
        return best;
    }

    /// <summary>统计指定阵营所有建筑的生产队列总订单数。</summary>
    private int CountQueuedUnitsOfTeam(int teamId)
    {
        int n = 0;
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b.TeamId == teamId && IsInstanceValid(b))
                n += b.QueueCount;
        }
        return n;
    }

    /// <summary>收集玩家方所有建筑的生产队列信息，按UnitType汇总（队列数+最高进度+剩余时间）。</summary>
    private Dictionary<UnitType, (int count, float progress, float timeRemaining)> CollectPlayerProductionInfo()
    {
        var result = new Dictionary<UnitType, (int count, float progress, float timeRemaining)>();
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is not Building b || b.TeamId != PlayerTeamId || !IsInstanceValid(b))
                continue;
            if (b.QueueCount == 0) continue;

            // 当前正在生产的类型
            if (b.CurrentProductionType.HasValue)
            {
                var pt = b.CurrentProductionType.Value;
                var ut = ProductionTypeToUnitType(pt);
                float progress = b.ProductionProgress;
                float remaining = b.ProductionTimeRemaining;
                if (result.ContainsKey(ut))
                {
                    var prev = result[ut];
                    if (progress > prev.progress)
                        result[ut] = (prev.count, progress, remaining);
                }
                else
                    result[ut] = (1, progress, remaining);
            }

            // 等待队列中的项
            if (b.QueueCount > 1)
            {
                var snapshot = b.GetQueueSnapshot();
                foreach (var pt in snapshot)
                {
                    var ut = ProductionTypeToUnitType(pt);
                    if (result.ContainsKey(ut))
                    {
                        var prev = result[ut];
                        result[ut] = (prev.count + 1, prev.progress, prev.timeRemaining);
                    }
                    else
                        result[ut] = (1, 0f, 0f);
                }
            }
        }
        return result;
    }

    private static ProductionType UnitTypeToProductionType(UnitType type) => type switch
    {
        UnitType.LightTank => ProductionType.LightTank,
        UnitType.Infantry => ProductionType.Infantry,
        UnitType.HeavyTank => ProductionType.HeavyTank,
        UnitType.Artillery => ProductionType.Artillery,
        UnitType.RocketLauncher => ProductionType.RocketLauncher,
        UnitType.MissileTank => ProductionType.MissileTank,
        UnitType.AntiAir => ProductionType.AntiAir,
        UnitType.Engineer => ProductionType.Engineer,
        UnitType.Grenadier => ProductionType.Grenadier,       // E6
        UnitType.Sniper => ProductionType.Sniper,             // E6
        UnitType.FlameInfantry => ProductionType.FlameInfantry, // E6
        UnitType.Transport => ProductionType.Transport,       // E6
        UnitType.Hero => ProductionType.Hero,                 // E6b
        UnitType.Spy => ProductionType.Spy,                    // E6b
        UnitType.Thief => ProductionType.Thief,               // E6b
        UnitType.Fighter => ProductionType.Fighter,           // E7
        UnitType.Helicopter => ProductionType.Helicopter,     // E7
        UnitType.RocketInfantry => ProductionType.RocketInfantry, // E7
        UnitType.Bomber => ProductionType.Bomber,                 // E8
        UnitType.Scout => ProductionType.Scout,                   // E8
        UnitType.TransportHeli => ProductionType.TransportHeli,  // E8
        // E9：海军生产映射
        UnitType.Destroyer => ProductionType.Destroyer,
        UnitType.Submarine => ProductionType.Submarine,
        UnitType.AircraftCarrier => ProductionType.AircraftCarrier,
        UnitType.LandingCraft => ProductionType.LandingCraft,
        _ => ProductionType.LightTank
    };

    private static UnitType ProductionTypeToUnitType(ProductionType type) => type switch
    {
        ProductionType.LightTank => UnitType.LightTank,
        ProductionType.Infantry => UnitType.Infantry,
        ProductionType.HeavyTank => UnitType.HeavyTank,
        ProductionType.Artillery => UnitType.Artillery,
        ProductionType.RocketLauncher => UnitType.RocketLauncher,
        ProductionType.MissileTank => UnitType.MissileTank,
        ProductionType.AntiAir => UnitType.AntiAir,
        ProductionType.Engineer => UnitType.Engineer,
        ProductionType.Grenadier => UnitType.Grenadier,       // E6
        ProductionType.Sniper => UnitType.Sniper,             // E6
        ProductionType.FlameInfantry => UnitType.FlameInfantry, // E6
        ProductionType.Transport => UnitType.Transport,       // E6
        ProductionType.Hero => UnitType.Hero,                 // E6b
        ProductionType.Spy => UnitType.Spy,                    // E6b
        ProductionType.Thief => UnitType.Thief,               // E6b
        ProductionType.Fighter => UnitType.Fighter,           // E7
        ProductionType.Helicopter => UnitType.Helicopter,     // E7
        ProductionType.RocketInfantry => UnitType.RocketInfantry, // E7
        ProductionType.Bomber => UnitType.Bomber,                 // E8
        ProductionType.Scout => UnitType.Scout,                   // E8
        ProductionType.TransportHeli => UnitType.TransportHeli,    // E8
        // E9：海军生产映射
        ProductionType.Destroyer => UnitType.Destroyer,
        ProductionType.Submarine => UnitType.Submarine,
        ProductionType.AircraftCarrier => UnitType.AircraftCarrier,
        ProductionType.LandingCraft => UnitType.LandingCraft,
        _ => UnitType.Default
    };

    /// <summary>获取选中的蓝方生产建筑（兵营/车厂/科技中心/基地），用于设置集结点。</summary>
    private Building? GetSelectedFriendlyProducerBuilding()
    {
        foreach (var o in _selected)
        {
            if (o is Building b && b.TeamId == 0 && IsInstanceValid(b)
                && (b.Type == BuildingType.Barracks || b.Type == BuildingType.WarFactory
                    || b.Type == BuildingType.TechCenter || b.Type == BuildingType.Base
                    || b.Type == BuildingType.Airfield || b.Type == BuildingType.Shipyard))
                return b;
        }
        return null;
    }

    private void SpawnOre(Vector2 pos, int amount = 1000)
    {
        var o = _oreScene.Instantiate<ResourceNode>();
        o.InitialAmount = amount;
        o.GlobalPosition = pos;
        _resourcesNode.AddChild(o);
    }

    private static Texture2D? _rockTex;
    private static Texture2D? _wallTex;

    // ========== E1 地形纹理系统 ==========

    // 地面瓦片纹理缓存
    private static Texture2D? _grass1Tex, _grass2Tex, _grass3Tex, _grass4Tex;
    private static Texture2D? _sand1Tex, _sand2Tex, _sand3Tex;
    private static Texture2D? _roadETex, _roadNTex, _roadCrossTex;
    // E1 新增地形纹理
    private static Texture2D? _shallow1Tex, _shallow2Tex, _shallow3Tex;
    private static Texture2D? _deep1Tex, _deep2Tex, _deep3Tex;
    private static Texture2D? _mountain1Tex, _mountain2Tex, _mountain3Tex;
    private static Texture2D? _snow1Tex, _snow2Tex, _snow3Tex;
    private static Texture2D? _city1Tex, _city2Tex;
    private static Texture2D? _field1Tex, _field2Tex;
    private static Texture2D? _bridgeTex, _tunnelTex, _cliffTex;

    // ========== 阶段12-B 种子驱动地图生成 ==========

    // ========== E5 资源扩展生成 ==========

    /// <summary>将坐标限制在地图范围内（距边缘至少 margin 像素）。</summary>
    private static Vector2 ClampToMap(Vector2 pos, float margin)
    {
        return new Vector2(
            Mathf.Clamp(pos.X, margin, MapSize - margin),
            Mathf.Clamp(pos.Y, margin, MapSize - margin));
    }

    // ---------- 拾取/查询 ----------
    private Unit? PickUnitAt(Vector2 worldPos, bool requireEnemy)
    {
        foreach (var child in _unitsNode.GetChildren())
        {
            if (child is Unit u && IsInstanceValid(u))
            {
                if (requireEnemy && u.TeamId == 0) continue;
                if (!requireEnemy && u.TeamId != 0) continue;
                if (u.GlobalPosition.DistanceTo(worldPos) < 30f)
                    return u;
            }
        }
        return null;
    }

    // E6：拾取友方运输车
    private Unit? PickTransportAt(Vector2 worldPos, bool requireFriendly)
    {
        foreach (var child in _unitsNode.GetChildren())
        {
            if (child is Unit u && IsInstanceValid(u) && u.IsTransport)
            {
                if (requireFriendly && u.TeamId != 0) continue;
                if (!requireFriendly && u.TeamId == 0) continue;
                if (u.GlobalPosition.DistanceTo(worldPos) < 36f)
                    return u;
            }
        }
        return null;
    }

    private Building? PickBuildingAt(Vector2 worldPos, bool requireEnemy)
    {
        foreach (var child in _buildingsNode.GetChildren())
        {
            if (child is Building b && IsInstanceValid(b))
            {
                if (requireEnemy && b.TeamId == 0) continue;
                if (!requireEnemy && b.TeamId != 0) continue;
                if (b.GlobalPosition.DistanceTo(worldPos) < 72f)
                    return b;
            }
        }
        return null;
    }

    private List<Unit> GetSelectedFriendlyUnits()
    {
        var list = new List<Unit>();
        foreach (var o in _selected)
        {
            if (o is Unit u && IsInstanceValid(u) && u.TeamId == 0)
                list.Add(u);
        }
        return list;
    }

    private int CountUnitsOfTeam(int teamId)
    {
        int n = 0;
        foreach (var c in _unitsNode.GetChildren())
            if (c is Unit u && u.TeamId == teamId && IsInstanceValid(u)) n++;
        return n;
    }

    private int CountHarvestersOfTeam(int teamId)
    {
        int n = 0;
        foreach (var c in _unitsNode.GetChildren())
            if (c is Harvester h && h.TeamId == teamId && IsInstanceValid(h)) n++;
        return n;
    }

    private int CountBuildingsOfTeam(int teamId)
    {
        int n = 0;
        foreach (var c in _buildingsNode.GetChildren())
            if (c is Building b && b.TeamId == teamId && IsInstanceValid(b)) n++;
        return n;
    }

    private void CheckWinCondition()
    {
        if (_gameOver) return;
        int playerUnits = CountUnitsOfTeam(PlayerTeamId);
        int playerBuildings = CountBuildingsOfTeam(PlayerTeamId);

        // 玩家方全灭 = 失败
        if (playerBuildings == 0 && playerUnits == 0)
        {
            _gameOver = true;
            _gameResult = "失败！你的基地被摧毁了。";
            _gameOverDelay = 2f;
            return;
        }

        // 所有 AI 阵营全灭 = 胜利
        bool anyAiAlive = false;
        for (int t = 1; t <= AiTeamCount; t++)
        {
            if (CountUnitsOfTeam(t) > 0 || CountBuildingsOfTeam(t) > 0)
            {
                anyAiAlive = true;
                break;
            }
        }
        if (!anyAiAlive)
        {
            _gameOver = true;
            _gameResult = "胜利！所有敌方阵营已被全部消灭。";
            _gameOverDelay = 2f;
        }
    }

    // ---------- Q6 事件通知系统 ----------

    // ---------- G5 游戏结束 UI ----------
    private void ShowGameOverUI()
    {
        // 阶段12-C：游戏结束音效
        bool win = _gameResult.StartsWith("胜利");
        _audio?.PlaySfxForce(win ? AudioManager.Sfx.NotifyVictory : AudioManager.Sfx.NotifyDefeat);

        var layer = new CanvasLayer { Name = "GameOverUI" };
        AddChild(layer);

        var bg = new ColorRect();
        bg.Color = new Color(0, 0, 0, 0.75f);
        bg.AnchorLeft = 0; bg.AnchorTop = 0; bg.AnchorRight = 1; bg.AnchorBottom = 1;
        layer.AddChild(bg);

        var center = new CenterContainer();
        center.AnchorLeft = 0; center.AnchorTop = 0; center.AnchorRight = 1; center.AnchorBottom = 1;
        layer.AddChild(center);

        var vbox = new VBoxContainer();
        vbox.CustomMinimumSize = new Vector2(400, 0);
        vbox.AddThemeConstantOverride("separation", 20);
        center.AddChild(vbox);

        var title = new Label();
        title.Text = _gameResult;
        title.AddThemeFontSizeOverride("font_size", 32);
        title.AddThemeColorOverride("font_color", win ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.4f, 0.4f));
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        var diffLabel = new Label();
        diffLabel.Text = $"难度：{_difficulty}";
        diffLabel.AddThemeFontSizeOverride("font_size", 18);
        diffLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        diffLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(diffLabel);

        var spacer = new Control { CustomMinimumSize = new Vector2(0, 12) };
        vbox.AddChild(spacer);

        var restartBtn = new Button();
        restartBtn.Text = "重新开始（同难度）";
        restartBtn.CustomMinimumSize = new Vector2(0, 44);
        restartBtn.Pressed += () => CallDeferred(nameof(RestartGame));
        vbox.AddChild(restartBtn);

        var menuBtn = new Button();
        menuBtn.Text = "返回主菜单";
        menuBtn.CustomMinimumSize = new Vector2(0, 44);
        menuBtn.Pressed += () => CallDeferred(nameof(ReturnToMenu));
        vbox.AddChild(menuBtn);

        GD.Print($"[GameOver] {_gameResult} (难度 {_difficulty})");
    }

    private void RestartGame()
    {
        GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
    }

    private void ReturnToMenu()
    {
        GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
    }

    // ---------- 外部 API ----------
    public void AddResourceForTeam(int teamId, int amount)
    {
        if (teamId >= 0 && teamId < _money.Length)
            _money[teamId] += amount;
    }

    // ======== G6: 邻接加成方法 ========

    // ======== G7: 间谍任务系统方法 ========

    // ======== G5: 尤里卡时刻方法 ========

    /// <summary>获取指定阵营当前资金。</summary>
    public int GetMoney(int teamId)
    {
        if (teamId >= 0 && teamId < _money.Length)
            return _money[teamId];
        return 0;
    }

    // ---- G4+: 建筑受击回防 ----
    private readonly Dictionary<ulong, float> _buildingAlertCooldown = new();

    public int GetTeamMoney(int teamId)
    {
        if (teamId >= 0 && teamId < _money.Length)
            return _money[teamId];
        return 0;
    }

    /// <summary>E11：掠夺能力回调——击杀敌人时奖励金钱。</summary>
    public void AwardPlunderGold(int teamId, int amount)
    {
        if (teamId >= 0 && teamId < _money.Length)
            _money[teamId] += amount;
    }

    // ==================== P0-2: 存档/读档 — 公开访问器 ====================
    // SaveLoadSystem 通过以下方法读取游戏状态进行序列化。所有方法只读，不修改状态。

    /// <summary>获取当前地图种子（用于读档重建基础地图）。</summary>
    public ulong GetMapSeed() => _mapSeed;

    /// <summary>获取当前游戏难度。</summary>
    public Difficulty GetDifficulty() => _difficulty;

    /// <summary>获取活跃AI数量（teamId 1..N 为活跃，其余休眠）。</summary>
    public int GetActiveAiCount() => _activeAiCount;

    /// <summary>游戏是否已结束。</summary>
    public bool IsGameOver() => _gameOver;

    /// <summary>获取游戏结果文本（胜利/失败描述）。返回null表示空字符串视为无结果。</summary>
    public string? GetGameResult() => string.IsNullOrEmpty(_gameResult) ? null : _gameResult;

    // （GetPlayerCardId 方法已移至 Main.SaveLoad.cs）

    // ---------- 存档路径处理 ----------

    // ---------- F5/F9 快捷存读档入口 ----------

    // ---------- 读档后重建游戏状态 ----------

}
