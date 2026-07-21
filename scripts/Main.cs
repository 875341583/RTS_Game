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
    private const int MaxUnitsPerTeam = 20;
    private const int PowerPlantCost = 300;
    private const int BarracksCost = 400;
    private const int WarFactoryCost = 600;
    private const int TechCenterCost = 800;
    // 阶段12-A1+A2：新建筑造价
    private const int TurretCost = 400;
    private const int AntiAirTurretCost = 600;
    private const int RepairPadCost = 500;
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
    // G1 操控增强
    private readonly Dictionary<int, List<Unit>> _squads = new();
    private bool _attackMoveMode;

    public override void _Ready()
    {
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

        // Q4：地面纹理（草地+道路+泥地）
        CreateGround();

        // ---- 初始化 8 阵营 ----
        // 阵营起始位置：玩家=Team0 在地图左上角；7 个 AI 围绕地图边缘均匀分布
        // 地图 2000×2000，各基地放在距边缘 200 的位置
        var teamStartPositions = new Vector2[TotalTeamCount]
        {
            new(200, 200),     // 0 玩家（左上角）
            new(1800, 1800),   // 1 AI（右下角，原红方位）
            new(1800, 200),    // 2 AI（右上角）
            new(200, 1800),    // 3 AI（左下角）
            new(1000, 200),    // 4 AI（顶部中央）
            new(1000, 1800),   // 5 AI（底部中央）
            new(200, 1000),    // 6 AI（左侧中央）
            new(1800, 1000),   // 7 AI（右侧中央）
        };

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

            // 每个阵营基地附近自动生成 2 个近矿（800 资源）保证起步经济
            SpawnOre(basePos + new Vector2(200, 150), 800);
            SpawnOre(basePos + new Vector2(250, 350), 800);
        }

        // 散布中场争夺矿 + 中央高价值矿（保留原地图设计）
        // 中场争夺矿
        SpawnOre(new Vector2(700, 1100), 1200);
        SpawnOre(new Vector2(1300, 900), 1200);
        SpawnOre(new Vector2(900, 400), 1200);
        SpawnOre(new Vector2(1100, 1600), 1200);
        // 中央高价值矿（高风险高回报）
        SpawnOre(new Vector2(1000, 1000), 2000);
        SpawnOre(new Vector2(850, 1150), 1500);
        SpawnOre(new Vector2(1150, 850), 1500);

        // ---- 地形障碍物 ----
        // 中央墙体（形成天然屏障和狭窄通道）
        SpawnObstacle(new Vector2(1000, 700), new Vector2(120, 30));
        SpawnObstacle(new Vector2(1000, 1300), new Vector2(120, 30));
        SpawnObstacle(new Vector2(700, 1000), new Vector2(30, 120));
        SpawnObstacle(new Vector2(1300, 1000), new Vector2(30, 120));
        // 散布岩石
        SpawnObstacle(new Vector2(600, 800), new Vector2(50, 50));
        SpawnObstacle(new Vector2(1400, 1200), new Vector2(50, 50));
        SpawnObstacle(new Vector2(800, 1300), new Vector2(40, 40));
        SpawnObstacle(new Vector2(1200, 700), new Vector2(40, 40));
        SpawnObstacle(new Vector2(500, 1200), new Vector2(45, 45));
        SpawnObstacle(new Vector2(1500, 800), new Vector2(45, 45));

        // ---- 战略要地 ----
        SpawnStrategicPoint(new Vector2(1000, 1000));   // 地图正中央
        SpawnStrategicPoint(new Vector2(700, 700));     // 蓝方侧中场
        SpawnStrategicPoint(new Vector2(1300, 1300));   // 红方侧中场

        // Q1：侧边栏建造面板
        _buildPanel = new BuildPanel();
        _buildPanel.DifficultyName = _difficulty.ToString();
        GetNode<CanvasLayer>("UI").AddChild(_buildPanel);
        _buildPanel.BuildBuildingRequested += (bt) => TryBuildBuilding(bt);
        _buildPanel.BuildUnitRequested += (ut) => TrySpawnUnit(ut, GetUnitCost(ut));
        _buildPanel.BuildHarvesterRequested += () => TrySpawnHarvester();
        GD.Print("[UI] 侧边栏建造面板已加载");

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
                   activeHint +
                   graceHint +
                   "★ 选中单位右键点敌方建筑/单位攻击\n" +
                   "★ 选中建筑右键设集结点 | R维修 | V出售",
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

        // 开局目标提示（控制台）
        GD.Print("========================================");
        GD.Print("★ 游戏目标：摧毁敌方所有建筑和单位即获胜！");
        GD.Print("★ 建造建议：电站→兵营→车厂→科技中心");
        GD.Print("★ 选中单位右键点敌方建筑/单位攻击");
        GD.Print("★ 选中建筑右键设集结点 | R维修 | V出售");
        GD.Print("========================================");
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
                b.QueueFree();
            }
            if (toSell.Count == 0)
            {
                GD.Print("[出售] 没有可出售的建筑（基地不可出售）");
            }
        }
    }

    private static int SquadIndexFromKey(Key kc)
    {
        int v = (int)kc, a = (int)Key.Key1, b = (int)Key.Key9;
        return (v >= a && v <= b) ? v - a : -1;
    }

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

        // Q1 刷新侧边栏建造面板
        if (_buildPanel != null)
        {
            _buildPanel.UpdateState(_money[0], GetTeamPower(0), _playerTechLevel,
                CountUnitsOfTeam(0), _unitCap,
                HasBuilding(0, BuildingType.Base), HasBuilding(0, BuildingType.PowerPlant),
                HasBuilding(0, BuildingType.Barracks), HasBuilding(0, BuildingType.WarFactory),
                HasBuilding(0, BuildingType.TechCenter));
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
        // 建筑前置检查
        if (!CanProduceUnit(0, type))
        {
            GD.Print($"[警告] 缺少生产{type}所需建筑！");
            return;
        }

        // 电力检查
        if (GetTeamPower(0) < 0)
        {
            GD.Print($"[警告] 电力不足，无法生产单位！当前电力: {GetTeamPower(0)}");
            return;
        }

        // G2：单位上限检查（活跃单位 + 队列中）
        int total = CountUnitsOfTeam(0) + CountQueuedUnitsOfTeam(0);
        if (total >= _unitCap)
        {
            GD.Print($"[警告] 达到单位上限 {_unitCap}！");
            return;
        }

        // G2：找生产建筑（队列最短的同类建筑，实现多建筑并行）
        var producer = FindProducerForUnit(type, 0);
        if (producer == null)
        {
            GD.Print($"[警告] 没有可用的{GetProducerForUnit(type)}！");
            return;
        }

        if (_money[0] < cost)
        {
            GD.Print($"[警告] 资金不足！需要 ${cost}，当前 ${_money[0]}");
            return;
        }

        _money[0] -= cost;
        producer.EnqueueProduction(UnitTypeToProductionType(type));
        GD.Print($"蓝方排产{type}，扣 ${cost}，剩余 ${_money[0]}，{producer.BuildingName}队列 {producer.QueueCount}/{Building.MaxQueueSize}");
    }

    public void TrySpawnHarvester()
    {
        if (_money[0] < HarvesterCost) { GD.Print("[警告] 资金不足！"); return; }
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
        return unitType switch
        {
            UnitType.LightTank => HasBuilding(teamId, BuildingType.Barracks),
            UnitType.Infantry => HasBuilding(teamId, BuildingType.Barracks),
            UnitType.HeavyTank => HasBuilding(teamId, BuildingType.WarFactory),
            UnitType.Artillery => HasBuilding(teamId, BuildingType.WarFactory),
            UnitType.AntiAir => HasBuilding(teamId, BuildingType.WarFactory),
            UnitType.Engineer => HasBuilding(teamId, BuildingType.WarFactory),
            UnitType.RocketLauncher => HasBuilding(teamId, BuildingType.TechCenter),
            UnitType.MissileTank => HasBuilding(teamId, BuildingType.TechCenter),
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
                produced += b.PowerProvided;
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

        // 电力检查（电站本身不受电力限制）
        if (type != BuildingType.PowerPlant && GetTeamPower(0) < 0)
        {
            GD.Print($"[警告] 电力不足！当前电力: {GetTeamPower(0)}");
            return;
        }

        // 资金检查
        int cost = GetBuildingCost(type);
        if (_money[0] < cost) { GD.Print($"[警告] 资金不足！需要 ${cost}，当前 ${_money[0]}"); return; }

        // Q1：进入放置模式（玩家手动选择位置）
        _placementMode = type;
        if (_buildPanel != null) _buildPanel.ActivePlacement = type;
        QueueRedraw();
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
        if (pos.X < 60 || pos.X > 1940 || pos.Y < 60 || pos.Y > 1940) return false;
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
        pos = new Vector2(Mathf.Clamp(pos.X, 80f, 1920f), Mathf.Clamp(pos.Y, 80f, 1920f));
        if (_money[0] < cost) { GD.Print("[放置] 资金不足"); CancelPlacement(); return; }
        if (!CanPlaceBuilding(pos)) { GD.Print("[放置] 位置被占用"); return; }
        _money[0] -= cost;
        SpawnBuilding(type, pos, teamId: 0);
        GD.Print($"蓝方建造{type}，扣 ${cost}，剩余 ${_money[0]}，位置 {pos}");
        if (_money[0] < cost) CancelPlacement();
    }

    public override void _Draw()
    {
        if (_placementMode == null) return;
        var pos = _camera.GetGlobalMousePosition();
        pos = new Vector2(Mathf.Clamp(pos.X, 80f, 1920f), Mathf.Clamp(pos.Y, 80f, 1920f));
        bool ok = CanPlaceBuilding(pos) && _money[0] >= GetBuildingCost(_placementMode.Value);

        // ---- 红警2风格网格建造预览 ----
        // 建筑占位：以 pos 为中心的 2x2 方格（每格45px）
        const float CellSize = 45f;
        var buildingColor = ok ? new Color(0.2f, 0.9f, 0.2f, 0.35f) : new Color(0.9f, 0.2f, 0.2f, 0.35f);
        var buildingBorder = ok ? new Color(0.3f, 1f, 0.3f, 0.8f) : new Color(1f, 0.3f, 0.3f, 0.8f);

        // 建筑占位填充 + 边框
        DrawRect(new Rect2(pos - new Vector2(CellSize, CellSize), CellSize * 2, CellSize * 2), buildingColor, true);
        DrawRect(new Rect2(pos - new Vector2(CellSize, CellSize), CellSize * 2, CellSize * 2), buildingBorder, false, 2.0f);

        // 中心十字准线
        var crossCol = ok ? new Color(0.3f, 1f, 0.3f, 0.5f) : new Color(1f, 0.3f, 0.3f, 0.5f);
        DrawLine(pos - new Vector2(CellSize, 0), pos + new Vector2(CellSize, 0), crossCol, 1.0f);
        DrawLine(pos - new Vector2(0, CellSize), pos + new Vector2(0, CellSize), crossCol, 1.0f);

        // 周围可达范围网格线（5x5 区域）
        const int GridRadius = 2; // ±2 格
        var gridLine = ok ? new Color(0.3f, 1f, 0.3f, 0.25f) : new Color(1f, 0.3f, 0.3f, 0.25f);
        for (int i = -GridRadius; i <= GridRadius; i++)
        {
            // 垂直网格线
            float x = pos.X + i * CellSize;
            DrawLine(new Vector2(x, pos.Y - GridRadius * CellSize),
                     new Vector2(x, pos.Y + GridRadius * CellSize), gridLine, 1.0f);
            // 水平网格线
            float y = pos.Y + i * CellSize;
            DrawLine(new Vector2(pos.X - GridRadius * CellSize, y),
                     new Vector2(pos.X + GridRadius * CellSize, y), gridLine, 1.0f);
        }

        // 2x2 建筑格交叉线加重
        var heavyLine = ok ? new Color(0.3f, 1f, 0.3f, 0.6f) : new Color(1f, 0.3f, 0.3f, 0.6f);
        DrawLine(new Vector2(pos.X, pos.Y - CellSize), new Vector2(pos.X, pos.Y + CellSize), heavyLine, 1.5f);
        DrawLine(new Vector2(pos.X - CellSize, pos.Y), new Vector2(pos.X + CellSize, pos.Y), heavyLine, 1.5f);
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
            SpawnBuilding(BuildingType.PowerPlant, GetBuildPosition(teamId), teamId);
            GD.Print($"[AI] Team {teamId} built PowerPlant, ${_money[teamId]} left");
            return;
        }

        // 优先级2：电力不足（<30）时补电站
        if (hasPower && power < 30 && _money[teamId] >= PowerPlantCost)
        {
            _money[teamId] -= PowerPlantCost;
            SpawnBuilding(BuildingType.PowerPlant, GetBuildPosition(teamId), teamId);
            GD.Print($"[AI] Team {teamId} built PowerPlant (low power), ${_money[teamId]} left");
            return;
        }

        // 优先级3：建兵营
        if (hasPower && !hasBarracks && _money[teamId] >= BarracksCost)
        {
            _money[teamId] -= BarracksCost;
            SpawnBuilding(BuildingType.Barracks, GetBuildPosition(teamId), teamId);
            GD.Print($"[AI] Team {teamId} built Barracks, ${_money[teamId]} left");
            return;
        }

        // 优先级4：建战车工厂
        if (hasBarracks && !hasWarFactory && _money[teamId] >= WarFactoryCost)
        {
            _money[teamId] -= WarFactoryCost;
            SpawnBuilding(BuildingType.WarFactory, GetBuildPosition(teamId), teamId);
            GD.Print($"[AI] Team {teamId} built WarFactory, ${_money[teamId]} left");
            return;
        }

        // 优先级5：建科技中心（解锁高级兵种）
        if (_aiUsesTech && hasWarFactory && !hasTechCenter && _money[teamId] >= TechCenterCost && power >= 0)
        {
            _money[teamId] -= TechCenterCost;
            SpawnBuilding(BuildingType.TechCenter, GetBuildPosition(teamId), teamId);
            GD.Print($"[AI] Team {teamId} built TechCenter, ${_money[teamId]} left");
            return;
        }

        // 优先级6：后期电力不够就再建电站
        if (hasTechCenter && power < 50 && _money[teamId] >= PowerPlantCost)
        {
            _money[teamId] -= PowerPlantCost;
            SpawnBuilding(BuildingType.PowerPlant, GetBuildPosition(teamId), teamId);
            GD.Print($"[AI] Team {teamId} built PowerPlant (for tech center), ${_money[teamId]} left");
            return;
        }

        // ---- 阶段12-A1+A2：防御建筑与维修厂 ----
        // 优先级7：建造维修厂（已建车厂且无维修厂且资金充裕）
        if (hasWarFactory && !HasBuilding(teamId, BuildingType.RepairPad)
            && _money[teamId] >= RepairPadCost + 200 && power >= 0)
        {
            _money[teamId] -= RepairPadCost;
            SpawnBuilding(BuildingType.RepairPad, GetBuildPosition(teamId), teamId);
            GD.Print($"[AI] Team {teamId} built RepairPad, ${_money[teamId]} left");
            return;
        }

        // 优先级8：建造机枪塔（已建兵营，每阵营最多2座，资金充裕）
        int turretCount = CountBuildingOfType(teamId, BuildingType.Turret);
        if (hasBarracks && turretCount < 2
            && _money[teamId] >= TurretCost + 300 && power >= 0)
        {
            _money[teamId] -= TurretCost;
            SpawnBuilding(BuildingType.Turret, GetBuildPosition(teamId), teamId);
            GD.Print($"[AI] Team {teamId} built Turret #{turretCount + 1}, ${_money[teamId]} left");
            return;
        }

        // 优先级9：建造防空炮（已建车厂，每阵营最多2座）
        int aaCount = CountBuildingOfType(teamId, BuildingType.AntiAirTurret);
        if (hasWarFactory && aaCount < 2
            && _money[teamId] >= AntiAirTurretCost + 300 && power >= 0)
        {
            _money[teamId] -= AntiAirTurretCost;
            SpawnBuilding(BuildingType.AntiAirTurret, GetBuildPosition(teamId), teamId);
            GD.Print($"[AI] Team {teamId} built AntiAirTurret #{aaCount + 1}, ${_money[teamId]} left");
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
                }
                if (HasBuilding(teamId, BuildingType.WarFactory))
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
        foreach (var child in _strategicPointsNode.GetChildren())
        {
            if (child is not StrategicPoint sp || !IsInstanceValid(sp)) continue;
            if (sp.OwningTeam == teamId) continue;

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
        // 点击敌方建筑 → 攻击建筑
        var enemyBuilding = PickBuildingAt(worldPos, requireEnemy: true);
        if (enemyBuilding != null)
        {
            foreach (var unit in friendlyUnits)
                unit.CommandAttackBuilding(enemyBuilding);
            return;
        }
        // 普通移动：保持简易队形
        int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(friendlyUnits.Count)));
        for (int i = 0; i < friendlyUnits.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            friendlyUnits[i].CommandMove(worldPos + new Vector2(col * 40, row * 40));
        }
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
        _buildingsNode.AddChild(b);
        return b;
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
            // G2：集结点 —— 新单位自动移动过去
            if (producer.RallyPoint.HasValue)
            {
                unit.CommandMove(producer.RallyPoint.Value);
            }
            GD.Print($"[生产完成] {producer.BuildingName} (Team {teamId}) 生产 {unitType}");
        }
    }

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
        UnitType.HeavyTank => BuildingType.WarFactory,
        UnitType.Artillery => BuildingType.WarFactory,
        UnitType.AntiAir => BuildingType.WarFactory,
        UnitType.Engineer => BuildingType.WarFactory,
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
        _ => UnitType.Default
    };

    /// <summary>获取选中的蓝方生产建筑（兵营/车厂/科技中心/基地），用于设置集结点。</summary>
    private Building? GetSelectedFriendlyProducerBuilding()
    {
        foreach (var o in _selected)
        {
            if (o is Building b && b.TeamId == 0 && IsInstanceValid(b)
                && (b.Type == BuildingType.Barracks || b.Type == BuildingType.WarFactory
                    || b.Type == BuildingType.TechCenter || b.Type == BuildingType.Base))
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

    // ========== Q4 地面纹理系统 ==========

    // 地面瓦片纹理缓存
    private static Texture2D? _grass1Tex, _grass2Tex, _grass3Tex, _grass4Tex;
    private static Texture2D? _sand1Tex, _sand2Tex, _sand3Tex;
    private static Texture2D? _roadETex, _roadNTex, _roadCrossTex;

    private void CreateGround()
    {
        EnsureGroundTileTextures();

        const int TileSize = 64;
        const int GridSize = 32; // 32*64=2048 覆盖 2000x2000
        var rng = new Random(42);

        // 瓦片类型网格
        int[,] tileGrid = new int[GridSize, GridSize];
        // 沙地区域
        for (int ty = 2; ty <= 5; ty++) for (int tx = 2; tx <= 5; tx++) tileGrid[tx, ty] = 1;
        for (int ty = 27; ty <= 30; ty++) for (int tx = 27; tx <= 30; tx++) tileGrid[tx, ty] = 1;
        for (int ty = 15; ty <= 16; ty++) for (int tx = 15; tx <= 16; tx++) tileGrid[tx, ty] = 1;
        for (int ty = 10; ty <= 12; ty++) for (int tx = 10; tx <= 12; tx++) tileGrid[tx, ty] = 1;
        for (int ty = 20; ty <= 21; ty++) for (int tx = 20; tx <= 21; tx++) tileGrid[tx, ty] = 1;
        // 道路
        for (int tx = 0; tx < GridSize; tx++) { if (tileGrid[tx, 15] == 0) tileGrid[tx, 15] = 2; if (tileGrid[tx, 16] == 0) tileGrid[tx, 16] = 2; }
        for (int ty = 0; ty < GridSize; ty++) { if (tileGrid[15, ty] == 0) tileGrid[15, ty] = 3; if (tileGrid[16, ty] == 0) tileGrid[16, ty] = 3; }
        tileGrid[15, 15] = 4; tileGrid[15, 16] = 4; tileGrid[16, 15] = 4; tileGrid[16, 16] = 4;
        for (int i = 3; i <= 15; i++) { if (tileGrid[i, i] == 0) tileGrid[i, i] = 2; }
        for (int i = 16; i <= 28; i++) { if (tileGrid[i, i] == 0) tileGrid[i, i] = 2; }

        // 拼接为单张大纹理（避免创建 1024 个 Sprite2D 节点）
        var groundImg = Image.CreateEmpty(GridSize * TileSize, GridSize * TileSize, false, Image.Format.Rgba8);
        var grass1Img = _grass1Tex!.GetImage();
        var grass2Img = _grass2Tex!.GetImage();
        var grass3Img = _grass3Tex!.GetImage();
        var grass4Img = _grass4Tex!.GetImage();
        var sand1Img  = _sand1Tex!.GetImage();
        var sand2Img  = _sand2Tex!.GetImage();
        var sand3Img  = _sand3Tex!.GetImage();
        var roadEImg  = _roadETex!.GetImage();
        var roadNImg  = _roadNTex!.GetImage();
        var roadCrossImg = _roadCrossTex!.GetImage();

        for (int ty = 0; ty < GridSize; ty++)
        {
            for (int tx = 0; tx < GridSize; tx++)
            {
                Image tileImg = tileGrid[tx, ty] switch
                {
                    1 => (rng.Next(3) switch { 0 => sand1Img, 1 => sand2Img, _ => sand3Img }),
                    2 => roadEImg,
                    3 => roadNImg,
                    4 => roadCrossImg,
                    _ => (rng.Next(4) switch { 0 => grass1Img, 1 => grass2Img, 2 => grass3Img, _ => grass4Img })
                };
                groundImg.BlitRect(tileImg, new Rect2I(0, 0, TileSize, TileSize),
                    new Vector2I(tx * TileSize, ty * TileSize));
            }
        }

        var groundTex = ImageTexture.CreateFromImage(groundImg);
        _groundSprite = new Sprite2D { Name = "Ground", Texture = groundTex, Centered = false, ZIndex = -3, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        AddChild(_groundSprite);
        MoveChild(_groundSprite, 0); // 最底层
    }

    private static void EnsureGroundTileTextures()
    {
        if (_grass1Tex != null) return;
        _grass1Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass1.png");
        _grass2Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass2.png");
        _grass3Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass3.png");
        _grass4Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass4.png");
        _sand1Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileSand1.png");
        _sand2Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileSand2.png");
        _sand3Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileSand3.png");
        _roadETex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass_roadEast.png");
        _roadNTex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass_roadNorth.png");
        _roadCrossTex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass_roadCrossing.png");
    }

    private void SpawnObstacle(Vector2 pos, Vector2 size)
    {
        EnsureObstacleTextures();
        var body = new StaticBody2D();
        body.GlobalPosition = pos;
        body.CollisionLayer = 1; // Terrain
        body.CollisionMask = 0;

        var shape = new CollisionShape2D();
        var rect = new RectangleShape2D();
        rect.Size = size;
        shape.Shape = rect;
        body.AddChild(shape);

        // Visual
        var sprite = new Sprite2D();
        bool isWall = size.X > size.Y * 1.5f || size.Y > size.X * 1.5f;
        sprite.Texture = isWall ? _wallTex! : _rockTex!;
        sprite.Scale = new Vector2(size.X / 80f, size.Y / 80f);
        body.AddChild(sprite);

        _obstaclesNode.AddChild(body);
    }

    private static void EnsureObstacleTextures()
    {
        if (_rockTex != null) return;

        // Kenney 环境素材（CC0）
        _rockTex = GD.Load<Texture2D>("res://assets/sprites/environment/crateMetal.png");
        if (_rockTex == null)
        {
            GD.PrintErr("[Obstacle] Failed to load crateMetal.png");
            var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            img.SetPixel(0, 0, Colors.Magenta);
            _rockTex = ImageTexture.CreateFromImage(img);
        }

        _wallTex = GD.Load<Texture2D>("res://assets/sprites/environment/sandbagBrown.png");
        if (_wallTex == null)
        {
            GD.PrintErr("[Obstacle] Failed to load sandbagBrown.png");
            var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            img.SetPixel(0, 0, Colors.Magenta);
            _wallTex = ImageTexture.CreateFromImage(img);
        }
    }

    private void SpawnStrategicPoint(Vector2 pos)
    {
        var sp = new StrategicPoint();
        sp.GlobalPosition = pos;
        _strategicPointsNode.AddChild(sp);
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

    /// <summary>在画面顶部显示一条 Toast 通知，自动淡出。</summary>
    public void ShowToast(string message, Color? color = null)
    {
        if (_toastContainer == null) return;
        var label = new Label
        {
            Text = message,
        };
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeColorOverride("font_color", color ?? new Color(1f, 0.9f, 0.3f));
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        label.Modulate = new Color(1, 1, 1, 0); // 初始透明，淡入
        _toastContainer.AddChild(label);
        _activeToasts.Add(new ToastEntry { Label = label, Lifetime = 3f, Age = 0f });
    }

    // ---------- G5 游戏结束 UI ----------
    private void ShowGameOverUI()
    {
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

        bool win = _gameResult.StartsWith("胜利");
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

    // ---- G4+: 建筑受击回防 ----
    private readonly Dictionary<ulong, float> _buildingAlertCooldown = new();

    /// <summary>建筑被攻击时调用：命令附近己方 AutoDefend 单位回防（有冷却避免频繁触发）。</summary>
    public void OnBuildingAttacked(Building b)
    {
        if (b == null || !IsInstanceValid(b)) return;
        ulong key = b.GetInstanceId();
        if (_buildingAlertCooldown.TryGetValue(key, out float t) && t > 0f) return;
        _buildingAlertCooldown[key] = 3f; // 3秒冷却

        int teamId = b.TeamId;
        Vector2 bPos = b.GlobalPosition;

        // Q6：建筑受袭事件通知
        if (teamId == 0)
            ShowToast($"⚠ {b.BuildingName}正在遭受攻击！", new Color(1f, 0.5f, 0.3f));
        foreach (var child in _unitsNode.GetChildren())
        {
            if (child is Unit u && u.TeamId == teamId && IsInstanceValid(u)
                && u.AutoDefend && !u.AutoAI && u.AttackDamage > 0f)
            {
                float d = u.GlobalPosition.DistanceTo(bPos);
                if (d < 700f) // 回防响应范围
                {
                    u.CommandAttackMove(bPos);
                }
            }
        }
    }

    public int GetTeamMoney(int teamId)
    {
        if (teamId >= 0 && teamId < _money.Length)
            return _money[teamId];
        return 0;
    }

    // ---------- UI ----------
    private void UpdateUI()
    {
        int playerUnits = CountUnitsOfTeam(PlayerTeamId);
        int playerPower = GetTeamPower(PlayerTeamId);
        int oreCount = 0;
        foreach (var c in _resourcesNode.GetChildren())
            if (c is ResourceNode && IsInstanceValid((Node)c)) oreCount++;

        string playerBuildings = GetBuildingList(PlayerTeamId);
        string powerWarn = playerPower < 0 ? "  [电力不足!]" : "";

        // 汇总 7 个 AI 阵营（总单位、总资金、总电力）
        int aiTotalUnits = 0, aiTotalMoney = 0, aiTotalPower = 0;
        for (int t = 1; t <= AiTeamCount; t++)
        {
            aiTotalUnits += CountUnitsOfTeam(t);
            aiTotalMoney += _money[t];
            aiTotalPower += GetTeamPower(t);
        }

        string status = _gameOver ? _gameResult : "目标：消灭所有敌方阵营（8色对战，玩家为红色方）";
        _uiLabel.Text = $"难度: {_difficulty} (科技Lv{_playerTechLevel} | 上限{_unitCap})    资金: ${_money[0]}    |    AI合计资金: ${aiTotalMoney}\n" +
                        $"电力: {playerPower}{powerWarn}    |    AI合计电力: {aiTotalPower}\n" +
                        $"玩家方: {playerUnits} 单位 / {playerBuildings}  · " +
                        $"AI合计: {aiTotalUnits} 单位 (7阵营)\n" +
                        $"地图剩余矿点: {oreCount}\n" +
                        (string.IsNullOrEmpty(status) ? "" : $"\n★ {status}");

        _hintLabel.Text = "WASD 移动相机 | 滚轮 缩放 | 左键拖框 选择 | 右键 移动/攻击/集结点\n" +
                          "Q 攻击移动 | X 停止 | R 维修建筑 | V 出售建筑(回收50%) | Ctrl+1~9 编队 | 1~9 选编队\n" +
                          "选中建筑右键设集结点 | 选中受损建筑按R维修 | 选中建筑(非基地)按V出售\n" +
                          "B 轻坦$" + LightTankCost + " | N 重坦$" + HeavyTankCost +
                          " | M 炮兵$" + ArtilleryCost + " | H 矿车$" + HarvesterCost + "\n" +
                          "K 火箭炮$" + RocketLauncherCost + " | L 导弹车$" + MissileTankCost + " (需科技中心)\n" +
                          "P 电站$" + PowerPlantCost + " | O 兵营$" + BarracksCost +
                          " | I 车厂$" + WarFactoryCost + " | T 科技$" + TechCenterCost + " (需前置建筑)";
        if (_attackMoveMode)
            _hintLabel.Text = "★ 攻击移动模式：左键点地发起 | 右键/Esc 取消";
    }

    private string GetBuildingList(int teamId)
    {
        int baseN = 0, power = 0, barrack = 0, war = 0, tech = 0;
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b.TeamId == teamId && IsInstanceValid(b))
            {
                switch (b.Type)
                {
                    case BuildingType.Base: baseN++; break;
                    case BuildingType.PowerPlant: power++; break;
                    case BuildingType.Barracks: barrack++; break;
                    case BuildingType.WarFactory: war++; break;
                    case BuildingType.TechCenter: tech++; break;
                }
            }
        }
        var parts = new List<string>();
        if (baseN > 0) parts.Add($"基地{baseN}");
        if (power > 0) parts.Add($"电站{power}");
        if (barrack > 0) parts.Add($"兵营{barrack}");
        if (war > 0) parts.Add($"车厂{war}");
        if (tech > 0) parts.Add($"科技{tech}");
        return parts.Count > 0 ? string.Join(" ", parts) : "0建筑";
    }
}
