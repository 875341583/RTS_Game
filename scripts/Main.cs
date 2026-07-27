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
    // P1-2: 单位/建筑造价已迁移到 data/units.json 和 data/buildings.json
    // 通过 GameData.GetUnitCost() / GameData.GetBuildingCost() 获取
    // 阵营乘数通过 FactionDef.ApplyCost() 应用
    // P0-3残留兼容：以下属性从GameData取基础值（不含阵营乘数，乘数在GetUnitCost/GetBuildingCost方法中应用）
    private const int MaxUnitsPerTeam = 20;

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
        // 超武常量已迁移到 GameConst（P2-4 数据驱动去重）
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
        // 超武常量已迁移到 GameConst（P2-4 数据驱动去重）
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
    // 超武常量已迁移到 GameConst（P2-4 数据驱动去重）
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

    // ---- P1-3 自定义地图系统 ----
    /// <summary>自定义地图文件路径（通过 --map= 参数指定）。</summary>
    private string? _customMapPath = null;
    /// <summary>已加载的自定义地图数据（null表示未使用自定义地图）。</summary>
    private MapData? _customMap = null;

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

    public override void _Ready()
    {
        // P2-4: 加载Mod（在游戏数据之前，以便Mod覆盖生效）
        ModLoader.LoadAllMods();

        // P1-2: 加载游戏数据（单位/建筑/阵营）
        GameData.Load();
        FactionManager.Load();

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
            // P1-3: 自定义地图文件参数 (--map=路径)
            if (a.StartsWith("--map=", StringComparison.OrdinalIgnoreCase))
            {
                _customMapPath = a.Substring(6);
            }
        }
        // 如果命令行没指定种子，从 GameSession 获取（主菜单输入）
        if (_mapSeed == 0)
            _mapSeed = GameSession.MapSeed;
        // 如果仍然为 0，随机生成一个种子
        if (_mapSeed == 0)
            _mapSeed = (ulong)DateTime.Now.Ticks;
        _mapRng = new Random((int)(_mapSeed & 0x7FFFFFFF));
        GameLog.Debug($"[Map] 种子 {_mapSeed}（可用 --seed={_mapSeed} 复现本张地图）");

        // P1-3: 如果指定了自定义地图文件，加载并应用
        if (!string.IsNullOrEmpty(_customMapPath))
        {
            _customMap = MapData.LoadFromFile(_customMapPath);
            if (_customMap != null)
            {
                _mapSeed = _customMap.Seed;
                _mapRng = new Random((int)(_mapSeed & 0x7FFFFFFF));
                GameLog.Debug($"[Map] 自定义地图已加载: {_customMap.Name} (seed={_mapSeed}, {_customMap.TerrainMods.Count}个修改, {_customMap.ResourceNodes.Count}个矿点, {_customMap.StrategicPoints.Count}个战略点)");
            }
            else
            {
                GameLog.Error($"[Map] 自定义地图加载失败: {_customMapPath}，回退到种子生成");
            }
        }

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

        // P1-3: 应用自定义地图的地形修改增量
        if (_customMap != null && _customMap.TerrainMods.Count > 0)
        {
            foreach (var mod in _customMap.TerrainMods)
            {
                if (mod.Gx < 0 || mod.Gx >= TerrainGrid.GridSize ||
                    mod.Gy < 0 || mod.Gy >= TerrainGrid.GridSize) continue;
                var cell = _terrain.GetCell(mod.Gx, mod.Gy);
                cell.Type = (TerrainType)mod.TerrainType;
                cell.Elevation = mod.Elevation;
                cell.HasBridge = mod.HasBridge;
                cell.HasTunnel = mod.HasTunnel;
                _terrain.SetCell(mod.Gx, mod.Gy, cell);
            }
            GameLog.Debug($"[Map] 应用了 {_customMap.TerrainMods.Count} 个地形修改");
        }

        var stats = _terrain.GetStats();
        GameLog.Debug("[Terrain] 地形生成统计：");
        foreach (var kv in stats)
            GameLog.Debug($"  {kv.Key}: {kv.Value}格");
        CreateGround();

        // P0-1: 创建A*寻路器（基于地形栅格）
        _pathFinder = new PathFinder(_terrain);
        GameLog.Debug("[PathFinder] A*寻路器已创建");

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
                    GameLog.Debug($"[Difficulty] Team {teamId} 处于休眠状态（不发展不主动进攻）");
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

        // P1-3: 自定义地图的矿点（与种子随机矿叠加，不替换）
        if (_customMap != null)
        {
            foreach (var r in _customMap.ResourceNodes)
            {
                var pos = IsoCoords.GridToScreen(r.Gx, r.Gy);
                SpawnOre(pos, r.Amount);
            }
            if (_customMap.ResourceNodes.Count > 0)
                GameLog.Debug($"[Map] 放置了 {_customMap.ResourceNodes.Count} 个自定义矿点");
        }

        // E5 资源扩展：油田/稀有矿/陆地矿脉
        GenerateOilFields();
        GenerateRareMinerals();
        GenerateLandVeins();

        // ---- 地形障碍物（种子驱动） ----
        GenerateRandomObstacles();

        // ---- 战略要地（中央固定 + 侧翼种子偏移） ----
        GenerateStrategicPoints();

        // P1-3: 自定义地图的战略点（与种子生成的战略点叠加）
        if (_customMap != null)
        {
            foreach (var p in _customMap.StrategicPoints)
            {
                var pos = IsoCoords.GridToScreen(p.Gx, p.Gy);
                SpawnStrategicPoint(pos);
            }
            if (_customMap.StrategicPoints.Count > 0)
                GameLog.Debug($"[Map] 放置了 {_customMap.StrategicPoints.Count} 个自定义战略点");
        }

        // Q1：侧边栏建造面板
        _buildPanel = new BuildPanel();
        _buildPanel.DifficultyName = _difficulty.ToString();
        GetNode<CanvasLayer>("UI").AddChild(_buildPanel);
        _buildPanel.BuildBuildingRequested += (bt) => TryBuildBuilding(bt);
        _buildPanel.BuildUnitRequested += (ut) => TrySpawnUnit(ut);
        _buildPanel.BuildHarvesterRequested += () => TrySpawnHarvester();
        GameLog.Debug("[UI] 侧边栏建造面板已加载");

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
        GameLog.Debug("[UI] 小地图已加载");

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
        GameLog.Debug("[G1] 科技树系统初始化完成 — 按Tab打开科技面板");

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
        GameLog.Debug("[G2] 时代系统初始化完成 — 按Y打开时代面板");

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
        GameLog.Debug("[G3] 战术卡系统初始化完成 — 游戏开始5秒后选择战术卡");

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
        GameLog.Debug("[G4] 电网分区系统初始化完成 — 按G查看电网分布");

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
        GameLog.Debug("[G5] 尤里卡系统初始化完成 — 按H查看尤里卡进度");

        // G6: 初始化邻接加成面板
        _adjacencyLabel = new Label();
        _adjacencyLabel.Name = "AdjacencyLabel";
        _adjacencyLabel.Position = new Vector2(250, 130);
        _adjacencyLabel.Size = new Vector2(280, 250);
        _adjacencyLabel.Modulate = new Color(1f, 0.85f, 0.5f, 0.9f);
        _adjacencyLabel.AddThemeFontSizeOverride("font_size", 11);
        _adjacencyLabel.Visible = false;
        GetNode<CanvasLayer>("UI").AddChild(_adjacencyLabel);
        GameLog.Debug("[G6] 邻接加成系统初始化完成 — 按J查看邻接加成");

        // G7: 初始化间谍任务面板
        _spyMissionLabel = new Label();
        _spyMissionLabel.Name = "SpyMissionLabel";
        _spyMissionLabel.Position = new Vector2(770, 130);
        _spyMissionLabel.Size = new Vector2(200, 180);
        _spyMissionLabel.Modulate = new Color(0.85f, 0.7f, 1f, 0.9f);
        _spyMissionLabel.AddThemeFontSizeOverride("font_size", 11);
        _spyMissionLabel.Visible = false;
        GetNode<CanvasLayer>("UI").AddChild(_spyMissionLabel);
        GameLog.Debug("[G7] 间谍任务系统初始化完成 — 按N查看间谍任务");

        // G8: 初始化占领强化面板
        _captureLabel = new Label();
        _captureLabel.Name = "CaptureLabel";
        _captureLabel.Position = new Vector2(250, 400);
        _captureLabel.Size = new Vector2(250, 200);
        _captureLabel.Modulate = new Color(0.5f, 1f, 0.7f, 0.9f);
        _captureLabel.AddThemeFontSizeOverride("font_size", 11);
        _captureLabel.Visible = false;
        GetNode<CanvasLayer>("UI").AddChild(_captureLabel);
        GameLog.Debug("[G8] 占领强化系统初始化完成 — 按K查看占领状态");

        // 开局目标提示（控制台）
        GameLog.Debug("========================================");
        GameLog.Debug("★ 游戏目标：摧毁敌方所有建筑和单位即获胜！");
        GameLog.Debug("★ 建造建议：电站→兵营→车厂→科技中心");
        GameLog.Debug("★ 选中单位右键点敌方建筑/单位攻击");
        GameLog.Debug("★ 选中建筑右键设集结点 | R维修 | V出售");
        GameLog.Debug("★ Tab科技树 | Y时代升级 | T战术卡 | G电网分区 | H尤里卡 | J邻接加成 | N间谍 | K占领");
        GameLog.Debug("========================================");
    }

    // ======== E4：地形改造支持方法 ========

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
        if (Input.IsActionJustPressed("spawn_unit")) TrySpawnUnit(UnitType.LightTank);
        if (Input.IsActionJustPressed("spawn_heavy")) TrySpawnUnit(UnitType.HeavyTank);
        if (Input.IsActionJustPressed("spawn_artillery")) TrySpawnUnit(UnitType.Artillery);
        if (Input.IsActionJustPressed("spawn_harvester")) TrySpawnHarvester();
        if (Input.IsActionJustPressed("build_power")) TryBuildBuilding(BuildingType.PowerPlant);
        if (Input.IsActionJustPressed("build_barracks")) TryBuildBuilding(BuildingType.Barracks);
        if (Input.IsActionJustPressed("build_warfactory")) TryBuildBuilding(BuildingType.WarFactory);
        if (Input.IsActionJustPressed("build_tech")) TryBuildBuilding(BuildingType.TechCenter);
        if (Input.IsActionJustPressed("spawn_rocket")) TrySpawnUnit(UnitType.RocketLauncher);
        if (Input.IsActionJustPressed("spawn_missile")) TrySpawnUnit(UnitType.MissileTank);
        // L1修复: 以下生产热键与面板键冲突(N/K/G/H/T/J/Y)，面板打开时禁用生产热键
        if (!AnyPanelOpen())
        {
        // E4：工兵(K) / 高级工程师(Shift+K) 生产热键
        if (Input.IsKeyPressed(Key.K) && _prevKeyState != Key.K)
        {
            if (Input.IsKeyPressed(Key.Shift))
                TrySpawnUnit(UnitType.ChiefEngineer);
            else
                TrySpawnUnit(UnitType.Sapper);
        }
        _prevKeyState = Input.IsKeyPressed(Key.K) ? Key.K : Key.None;

        // E6：新步兵热键 G(掷弹兵) / Shift+G(狙击手) / F(喷火兵) / T(运输车)
        if (Input.IsKeyPressed(Key.G) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Grenadier);
        if (Input.IsKeyPressed(Key.G) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Sniper);
        if (Input.IsKeyPressed(Key.F) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.FlameInfantry);
        if (Input.IsKeyPressed(Key.T) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Transport);

        // E6b：特殊单位热键 Y(英雄) / Shift+Y(间谍) / U(窃贼)
        if (Input.IsKeyPressed(Key.Y) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Hero);
        if (Input.IsKeyPressed(Key.Y) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Spy);
        if (Input.IsKeyPressed(Key.U) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Thief);

        // E7：空军热键 J(战斗机) / Shift+J(直升机) / Shift+W(火箭兵)
        if (Input.IsKeyPressed(Key.J) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Fighter);
        if (Input.IsKeyPressed(Key.J) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Helicopter);
        if (Input.IsKeyPressed(Key.W) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.RocketInfantry);

        // E8：扩展空军热键 Shift+B(轰炸机) / H(侦察机) / Shift+H(运输直升机)
        if (Input.IsKeyPressed(Key.B) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Bomber);
        if (Input.IsKeyPressed(Key.H) && !Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Scout);
        if (Input.IsKeyPressed(Key.H) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.TransportHeli);
        } // end if (!AnyPanelOpen())

        // E9：海军热键 Shift+1(驱逐舰) / Shift+2(潜艇) / Shift+3(航母) / Shift+4(登陆艇)
        if (Input.IsKeyPressed(Key.Key1) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Destroyer);
        if (Input.IsKeyPressed(Key.Key2) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.Submarine);
        if (Input.IsKeyPressed(Key.Key3) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.AircraftCarrier);
        if (Input.IsKeyPressed(Key.Key4) && Input.IsKeyPressed(Key.Shift))
            TrySpawnUnit(UnitType.LandingCraft);

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
                    GameLog.Debug($"[闪电] 持续伤害 Tick @ {lv.Position}，命中 {hits}（剩余 {(lv.Lifetime - lv.Age):F1}s）");
                }
                // 每 0.08 秒刷新闪电形状种子（让折线抖动闪烁）
                if (lv.BoltRefreshTimer >= 0.08f)
                {
                    lv.BoltRefreshTimer -= 0.08f;
                    _lightningBoltSeed = (float)GD.RandRange(0, 1000);
                }
                if (lv.Age >= lv.Lifetime)
                {
                    GameLog.Debug($"[闪电] 特效结束 @ {lv.Position}");
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
            GameLog.Debug($"[Status] Player: ${_money[0]} | {CountUnitsOfTeam(0)} units / {CountBuildingsOfTeam(0)} buildings | AI(1-7) total: units={aiUnits} / buildings={aiBld}");
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

    public override void _Draw()
    {
        // ---- 阶段12-A4：核弹冲击波持久特效（始终绘制） ----
        foreach (var nuke in _activeNukeVisuals)
        {
            float progress = nuke.Age / nuke.Lifetime;
            float radius = GameConst.NukeRadius * (0.3f + 0.7f * progress);
            // 外层冲击波（亮黄白→淡出）
            DrawArc(nuke.Position, radius, 0f, Mathf.Tau, 48,
                new Color(1f, 0.95f, 0.6f, (1f - progress) * 0.85f), 4f);
            // 内层辐射圈（橙红→暗）
            DrawArc(nuke.Position, radius * 0.6f, 0f, Mathf.Tau, 36,
                new Color(1f, 0.45f, 0.2f, (1f - progress) * 0.6f), 3f);
            // 中心辐射填充（绿色毒雾感）
            if (progress < 0.7f)
            {
                float fillR = GameConst.NukeRadius * 0.5f * (1f - progress / 0.7f);
                DrawCircle(nuke.Position, fillR,
                    new Color(0.7f, 1f, 0.3f, (1f - progress) * 0.18f));
            }
        }

        // ---- 阶段12-A4：核弹目标选择准星 ----
        if (_nukeTargetMode)
        {
            var mousePos = _camera.GetGlobalMousePosition();
            // 爆炸范围预览圈
            DrawArc(mousePos, GameConst.NukeRadius, 0f, Mathf.Tau, 64,
                new Color(1f, 0.25f, 0.15f, 0.55f), 2f);
            // 内圈危险标识
            DrawArc(mousePos, GameConst.NukeRadius * 0.5f, 0f, Mathf.Tau, 48,
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
            DrawCircle(lv.Position, GameConst.LightningRadius,
                new Color(0.3f, 0.6f, 1f, 0.15f * (1f - progress * 0.5f)));
            // 2. 多重电光环（白蓝色同心圆，向外扩散）
            for (int ring = 0; ring < 3; ring++)
            {
                float ringR = GameConst.LightningRadius * (0.4f + 0.3f * ring) * (1f + 0.05f * Mathf.Sin(lv.Age * 8f + ring));
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
            DrawArc(mousePos, GameConst.LightningRadius, 0f, Mathf.Tau, 64,
                new Color(0.4f, 0.8f, 1f, 0.55f), 2f);
            // 内圈
            DrawArc(mousePos, GameConst.LightningRadius * 0.5f, 0f, Mathf.Tau, 48,
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

    // ---------- 阶段12-A4 超武系统（核弹）----------

    // ---------- 阶段12-A4 闪电风暴 ----------

    // ---------- G2 生产系统辅助 ----------

    // ---- 阶段12-C 音效回调（供 Unit/Building 调用） ----

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

    // ---------- Q6 事件通知系统 ----------

    // ======== G6: 邻接加成方法 ========

    // ======== G7: 间谍任务系统方法 ========

    // ======== G5: 尤里卡时刻方法 ========

    // ---- G4+: 建筑受击回防 ----
    private readonly Dictionary<ulong, float> _buildingAlertCooldown = new();

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
