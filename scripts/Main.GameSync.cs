using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace RTSGame;

/// <summary>
/// 联机游戏同步 — 接收远端玩家命令并应用到本地游戏实例。
/// 这是Main的partial class，处理NetworkManager.CommandReceived回调。
/// 
/// 架构说明：
///   - 玩家操作本地立即执行（不影响），同时通过ReplayRecorder.OnRecorded事件发送到网络
///   - Host收到Client命令后验证并广播给所有人
///   - Client收到Host广播的非自己命令后执行（ExecuteNetCommand）
///   - Host的命令也广播给Client，但Host自己不重复执行
/// </summary>
public partial class Main
{
    private bool _netInitialized = false;

    /// <summary>初始化联机同步（在_Ready末尾调用）。</summary>
    private void InitNetSync()
    {
        if (!NetworkManager.IsOnline) return;

        _netInitialized = true;

        // 注册命令接收回调
        NetworkManager.CommandReceived -= OnNetCommandReceived;
        NetworkManager.CommandReceived += OnNetCommandReceived;

        // 注册操作录制事件 → 联机发送命令（不论Host还是Client）
        ReplayRecorder.OnRecorded -= OnPlayerActionRecorded;
        ReplayRecorder.OnRecorded += OnPlayerActionRecorded;

        // 注册状态快照接收回调（仅Client）
        if (NetworkManager.Role == NetworkManager.NetRole.Client)
        {
            NetworkManager.SnapshotReceived -= OnNetSnapshotReceived;
            NetworkManager.SnapshotReceived += OnNetSnapshotReceived;
        }

        // Host：注册状态快照采集回调
        if (NetworkManager.Role == NetworkManager.NetRole.Host)
        {
            NetworkManager.SnapshotData -= CollectAndSendSnapshot;
            NetworkManager.SnapshotData += CollectAndSendSnapshot;
        }

        // 注册游戏结束回调
        NetworkManager.GameOverReceived -= OnNetGameOver;
        NetworkManager.GameOverReceived += OnNetGameOver;

        GameLog.Debug($"[NetSync] 联机同步已初始化 — 角色:{NetworkManager.Role} 本地TeamId:{NetworkManager.LocalTeamId}");
    }

    /// <summary>玩家操作被Record时触发 → 发送到网络。</summary>
    private void OnPlayerActionRecorded(ReplayRecorder.ActionType action, string json)
    {
        if (!NetworkManager.IsOnline) return;
        NetworkManager.SendCommand(action, json);
    }

    /// <summary>收到远端玩家命令时处理。</summary>
    private void OnNetCommandReceived(NetworkManager.NetCommand cmd)
    {
        if (cmd.TeamId == NetworkManager.LocalTeamId) return; // 忽略自己的命令回显

        try
        {
            // 空参数的命令（如CommandStop、Scatter等）Params为空字符串
            JsonElement p = string.IsNullOrEmpty(cmd.Params)
                ? default
                : JsonSerializer.Deserialize<JsonElement>(cmd.Params);
            ExecuteNetCommand(cmd.Action, cmd.TeamId, p);
        }
        catch (Exception e)
        {
            GameLog.Error($"[NetSync] 命令解析失败: {cmd.Action} — {e.Message}");
        }
    }

    // ====== 参数解析辅助 ======

    private static float GetFloat(JsonElement p, string key)
    {
        if (p.ValueKind == JsonValueKind.Undefined) return 0f;
        return p.TryGetProperty(key, out var el) ? el.GetSingle() : 0f;
    }

    private static int GetInt(JsonElement p, string key)
    {
        if (p.ValueKind == JsonValueKind.Undefined) return 0;
        return p.TryGetProperty(key, out var el) ? el.GetInt32() : 0;
    }

    private static string GetString(JsonElement p, string key)
    {
        if (p.ValueKind == JsonValueKind.Undefined) return "";
        return p.TryGetProperty(key, out var el) ? el.GetString() ?? "" : "";
    }

    private static Vector2 GetXY(JsonElement p, string xKey = "X", string yKey = "Y")
    {
        return new Vector2(GetFloat(p, xKey), GetFloat(p, yKey));
    }

    // ====== 执行远端玩家命令 — 覆盖全部31种ActionType ======

    /// <summary>执行远端玩家命令。参数键名与ReplayRecorder.Record完全一致（大写）。</summary>
    private void ExecuteNetCommand(ReplayRecorder.ActionType action, int teamId, JsonElement p)
    {
        switch (action)
        {
            // ---- 单位移动/命令 ----
            case ReplayRecorder.ActionType.CommandMove:
                {
                    var target = GetXY(p);
                    var units = GetUnitsOfTeam(teamId);
                    int cols = Math.Max(1, (int)Mathf.Sqrt(units.Count));
                    for (int i = 0; i < units.Count; i++)
                    {
                        int col = i % cols, row = i / cols;
                        units[i].CommandMove(target + new Vector2(col * 40, row * 40));
                    }
                }
                break;

            case ReplayRecorder.ActionType.CommandAttackMove:
                {
                    var target = GetXY(p);
                    var units = GetUnitsOfTeam(teamId);
                    int cols = Math.Max(1, (int)Mathf.Sqrt(units.Count));
                    for (int i = 0; i < units.Count; i++)
                    {
                        int col = i % cols, row = i / cols;
                        units[i].CommandAttackMove(target + new Vector2(col * 40, row * 40));
                    }
                }
                break;

            case ReplayRecorder.ActionType.FormationMove:
                {
                    var target = GetXY(p);
                    var units = GetUnitsOfTeam(teamId);
                    var center = Vector2.Zero;
                    foreach (var u in units) center += u.GlobalPosition;
                    center /= Math.Max(1, units.Count);
                    for (int i = 0; i < units.Count; i++)
                    {
                        var offset = units[i].GlobalPosition - center;
                        units[i].CommandFormationMove(target + offset);
                    }
                }
                break;

            case ReplayRecorder.ActionType.CommandAttack:
                {
                    var target = GetXY(p);
                    var units = GetUnitsOfTeam(teamId);
                    var enemyUnit = PickUnitAt(target, requireEnemy: true);
                    if (enemyUnit != null)
                        foreach (var u in units) u.CommandAttack(enemyUnit);
                }
                break;

            case ReplayRecorder.ActionType.CommandAttackBuilding:
                {
                    var target = GetXY(p);
                    var units = GetUnitsOfTeam(teamId);
                    var enemyBuilding = PickBuildingAt(target, requireEnemy: true);
                    if (enemyBuilding != null)
                        foreach (var u in units) u.CommandAttackBuilding(enemyBuilding);
                }
                break;

            case ReplayRecorder.ActionType.CommandStop:
                {
                    foreach (var u in GetUnitsOfTeam(teamId)) u.CommandStop();
                }
                break;

            case ReplayRecorder.ActionType.Scatter:
                {
                    foreach (var u in GetUnitsOfTeam(teamId)) u.CommandScatter();
                }
                break;

            case ReplayRecorder.ActionType.HoldPosition:
                {
                    foreach (var u in GetUnitsOfTeam(teamId)) u.CommandHoldPosition();
                }
                break;

            case ReplayRecorder.ActionType.Patrol:
                {
                    var target = GetXY(p);
                    foreach (var u in GetUnitsOfTeam(teamId))
                        u.CommandPatrol(u.GlobalPosition, target);
                }
                break;

            case ReplayRecorder.ActionType.Waypoint:
                {
                    var target = GetXY(p);
                    foreach (var u in GetUnitsOfTeam(teamId)) u.EnqueueWaypoint(target);
                }
                break;

            case ReplayRecorder.ActionType.ForceAttack:
                {
                    var target = GetXY(p);
                    var units = GetUnitsOfTeam(teamId);
                    int cols = Math.Max(1, (int)Mathf.Sqrt(units.Count));
                    for (int i = 0; i < units.Count; i++)
                    {
                        int col = i % cols, row = i / cols;
                        units[i].CommandForceAttack(target + new Vector2(col * 40, row * 40));
                    }
                }
                break;

            case ReplayRecorder.ActionType.CommandSpyMission:
                {
                    // 间谍任务需要目标建筑引用 — 从坐标查找
                    var target = GetXY(p, "TargetX", "TargetY");
                    var enemyBuilding = PickBuildingAt(target, requireEnemy: true);
                    if (enemyBuilding != null)
                    {
                        string missionStr = GetString(p, "Mission");
                        if (Enum.TryParse<SpyMission.MissionType>(missionStr, out var mission))
                        {
                            foreach (var spy in GetUnitsOfTeam(teamId).Where(u => u.Type == UnitType.Spy && !u.IsSpyOnMission))
                                spy.CommandSpyMission(enemyBuilding, mission);
                        }
                    }
                }
                break;

            case ReplayRecorder.ActionType.CommandTerrainMod:
                {
                    var target = GetXY(p);
                    var terrainCell = _terrain.GetCellAtWorld(target.X, target.Y);
                    var modType = DetectTerrainMod(terrainCell);
                    if (modType != Unit.TerrainModType.None)
                    {
                        foreach (var u in GetUnitsOfTeam(teamId).Where(u => u.IsEngineerUnit))
                            u.CommandTerrainMod(modType, target);
                    }
                }
                break;

            // ---- 编队 ----
            case ReplayRecorder.ActionType.SaveSquad:
                {
                    // 编队是本地UI概念，远端不需要同步执行
                    // 但为完整性保留：远端玩家的编队对本地无影响
                }
                break;

            case ReplayRecorder.ActionType.SelectSquad:
                {
                    // 同上，编队选择是本地操作
                }
                break;

            // ---- 建筑 ----
            case ReplayRecorder.ActionType.PlaceBuilding:
                {
                    string typeName = GetString(p, "Type");
                    if (Enum.TryParse<BuildingType>(typeName, out var bt))
                    {
                        // 远端放建筑：直接在基地附近放置（简化版）
                        // 真正需要玩家指定坐标，但当前Record没有记录坐标
                        // Host权威模式下，Host已经执行了SpawnBuilding，通过快照同步建筑
                        // 这里作为备份：在基地附近随机放置
                        var buildings = GetBuildingsOfTeam(teamId);
                        var baseBld = buildings.FirstOrDefault(b => b.Type == BuildingType.Base);
                        if (baseBld != null)
                        {
                            var pos = FindBuildingPlacementNear(baseBld.GlobalPosition, bt, teamId);
                            SpawnBuilding(bt, pos, teamId);
                        }
                    }
                }
                break;

            case ReplayRecorder.ActionType.CancelPlacement:
                {
                    // 放置取消是本地UI状态，远端无需执行
                }
                break;

            // ---- 生产 ----
            case ReplayRecorder.ActionType.SpawnUnit:
                {
                    string typeName = GetString(p, "Type");
                    if (Enum.TryParse<UnitType>(typeName, out var type))
                    {
                        var producer = FindProducerForUnit(type, teamId);
                        if (producer != null && GetTeamPower(teamId) >= 0)
                        {
                            int cost = GetUnitCost(type, teamId);
                            if (_money[teamId] >= cost)
                            {
                                _money[teamId] -= cost;
                                producer.EnqueueProduction(UnitTypeToProductionType(type));
                            }
                        }
                    }
                }
                break;

            case ReplayRecorder.ActionType.SpawnHarvester:
                {
                    var producer = FindProducerBuilding(BuildingType.Base, teamId);
                    if (producer != null && _money[teamId] >= GetUnitCost(UnitType.Harvester, teamId))
                    {
                        _money[teamId] -= GetUnitCost(UnitType.Harvester, teamId);
                        producer.EnqueueProduction(ProductionType.Harvester);
                    }
                }
                break;

            case ReplayRecorder.ActionType.CancelProduction:
                {
                    // 需要知道哪个建筑取消 — 简化：取消队列最长的建筑
                    var buildings = GetBuildingsOfTeam(teamId)
                        .Where(b => b.QueueCount > 0)
                        .OrderByDescending(b => b.QueueCount)
                        .FirstOrDefault();
                    buildings?.CancelLastProduction();
                }
                break;

            case ReplayRecorder.ActionType.SetRallyPoint:
                {
                    var target = GetXY(p);
                    var producers = GetBuildingsOfTeam(teamId)
                        .Where(b => b.Type == BuildingType.Barracks || b.Type == BuildingType.WarFactory || b.Type == BuildingType.Base);
                    foreach (var b in producers) b.SetRallyPoint(target);
                }
                break;

            // ---- 超武 ----
            case ReplayRecorder.ActionType.Nuke:
                {
                    var target = GetXY(p);
                    ApplyNuke(target, teamId);
                }
                break;

            case ReplayRecorder.ActionType.Lightning:
                {
                    var target = GetXY(p);
                    ApplyLightning(target, teamId);
                }
                break;

            case ReplayRecorder.ActionType.CruiseMissile:
                {
                    var target = GetXY(p);
                    ApplyCruiseMissile(target, teamId);
                }
                break;

            // ---- 建筑操作 ----
            case ReplayRecorder.ActionType.RepairBuilding:
                {
                    foreach (var b in GetBuildingsOfTeam(teamId).Where(b => b.NeedsRepair))
                    {
                        int cost = GetRepairCost(b);
                        if (_money[teamId] >= cost)
                        {
                            _money[teamId] -= cost;
                            b.Repair();
                        }
                    }
                }
                break;

            case ReplayRecorder.ActionType.SellBuilding:
                {
                    string typeName = GetString(p, "Type");
                    var toSell = GetBuildingsOfTeam(teamId)
                        .Where(b => b.Type != BuildingType.Base)
                        .ToList();
                    // 如果指定了类型，只卖该类型
                    if (!string.IsNullOrEmpty(typeName) && Enum.TryParse<BuildingType>(typeName, out var sellType))
                        toSell = toSell.Where(b => b.Type == sellType).ToList();
                    foreach (var b in toSell)
                    {
                        int refund = Math.Max(1, GetBuildingCost(b.Type, teamId) / 2);
                        _money[teamId] += refund;
                        OnBuildingDestroyed(b);
                        b.Destroyed -= OnBuildingDestroyed;
                        b.QueueFree();
                    }
                }
                break;

            // ---- 科技 ----
            case ReplayRecorder.ActionType.ResearchTech:
                {
                    string techIdStr = GetString(p, "TechId");
                    // 同步科技研究状态
                    for (int i = 0; i < TechOrder.Length; i++)
                    {
                        if (TechOrder[i].ToString() == techIdStr)
                        {
                            ResearchTechForTeam(teamId, i);
                            break;
                        }
                    }
                }
                break;

            case ReplayRecorder.ActionType.AdvanceEra:
                {
                    AdvanceEraForTeam(teamId);
                }
                break;

            case ReplayRecorder.ActionType.SelectCard:
                {
                    string cardStr = GetString(p, "Card");
                    if (Enum.TryParse<TacticalCards.CardId>(cardStr, out var cardId))
                        SelectCardForTeam(teamId, cardId);
                }
                break;

            default:
                GameLog.Debug($"[NetSync] 远端命令 {action} 未处理（TeamId={teamId}）");
                break;
        }
    }

    // ====== 状态快照 ======

    /// <summary>Host：采集状态快照并广播。</summary>
    private void CollectAndSendSnapshot()
    {
        if (NetworkManager.Role != NetworkManager.NetRole.Host) return;

        var snap = new NetworkManager.StateSnapshotData
        {
            Timestamp = Time.GetTicksMsec(),
            Money = (int[])_money.Clone(),
            Units = new List<NetworkManager.UnitState>(),
            Buildings = new List<NetworkManager.BuildingState>()
        };

        // 只同步房间内活跃阵营的单位/建筑
        var activeTeams = new HashSet<int>();
        foreach (var pl in NetworkManager.Players.Values)
            activeTeams.Add(pl.TeamId);

        foreach (var u in GetAllUnits())
        {
            if (!IsInstanceValid(u)) continue;
            if (!activeTeams.Contains(u.TeamId)) continue;
            snap.Units.Add(new NetworkManager.UnitState
            {
                TeamId = u.TeamId,
                UnitType = (int)u.Type,
                X = u.GlobalPosition.X,
                Y = u.GlobalPosition.Y,
                Health = u.Health,
                UnitId = (int)u.GetInstanceId()
            });
        }

        foreach (var b in GetAllBuildings())
        {
            if (!IsInstanceValid(b)) continue;
            if (!activeTeams.Contains(b.TeamId)) continue;
            snap.Buildings.Add(new NetworkManager.BuildingState
            {
                TeamId = b.TeamId,
                BuildingType = (int)b.Type,
                X = b.GlobalPosition.X,
                Y = b.GlobalPosition.Y,
                Health = b.Health,
                BuildingId = (int)b.GetInstanceId()
            });
        }

        NetworkManager.SendSnapshot(snap);
    }

    // ====== 单位ID映射表（Client端：远端UnitId → 本地Unit节点） ======

    private readonly Dictionary<int, Unit> _netUnitMap = new();
    private readonly Dictionary<int, Building> _netBuildingMap = new();

    /// <summary>Client：接收状态快照并更新。</summary>
    private void OnNetSnapshotReceived(NetworkManager.StateSnapshotData snap)
    {
        if (NetworkManager.Role != NetworkManager.NetRole.Client) return;

        // 更新资金（全阵营）
        if (snap.Money != null)
        {
            for (int i = 0; i < snap.Money.Length && i < _money.Length; i++)
                _money[i] = snap.Money[i];
        }

        // 更新单位位置（通过InstanceId映射）
        if (snap.Units != null)
        {
            // 清理失效映射
            var deadKeys = new List<int>();
            foreach (var kv in _netUnitMap)
                if (!IsInstanceValid(kv.Value)) deadKeys.Add(kv.Key);
            foreach (var k in deadKeys) _netUnitMap.Remove(k);

            // 更新已有单位位置，检测新单位/消失单位
            var seenIds = new HashSet<int>();
            foreach (var us in snap.Units)
            {
                seenIds.Add(us.UnitId);
                if (_netUnitMap.TryGetValue(us.UnitId, out var u) && IsInstanceValid(u))
                {
                    // 插值移动：逐步逼近目标位置
                    var targetPos = new Vector2(us.X, us.Y);
                    var diff = targetPos - u.GlobalPosition;
                    if (diff.Length() > 200f)
                        u.GlobalPosition = targetPos; // 跳跃修正（单位刚生成或严重延迟）
                    else
                        u.GlobalPosition += diff * 0.3f; // 插值平滑
                }
                //else: 新单位 — 由命令同步处理生成，快照不创建新单位
            }

            // 检测消失的单位（在映射中但不在快照中）
            var missingKeys = new List<int>();
            foreach (var kv in _netUnitMap)
                if (!seenIds.Contains(kv.Key)) missingKeys.Add(kv.Key);
            foreach (var k in missingKeys)
            {
                if (IsInstanceValid(_netUnitMap[k]))
                    _netUnitMap[k].QueueFree();
                _netUnitMap.Remove(k);
            }
        }

        // 更新建筑（类似处理）
        if (snap.Buildings != null)
        {
            var deadBldKeys = new List<int>();
            foreach (var kv in _netBuildingMap)
                if (!IsInstanceValid(kv.Value)) deadBldKeys.Add(kv.Key);
            foreach (var k in deadBldKeys) _netBuildingMap.Remove(k);

            var seenBldIds = new HashSet<int>();
            foreach (var bs in snap.Buildings)
            {
                seenBldIds.Add(bs.BuildingId);
                if (_netBuildingMap.TryGetValue(bs.BuildingId, out var b) && IsInstanceValid(b))
                {
                    b.GlobalPosition = new Vector2(bs.X, bs.Y); // 建筑不移动，直接设置
                }
            }

            var missingBldKeys = new List<int>();
            foreach (var kv in _netBuildingMap)
                if (!seenBldIds.Contains(kv.Key)) missingBldKeys.Add(kv.Key);
            foreach (var k in missingBldKeys)
            {
                if (IsInstanceValid(_netBuildingMap[k]))
                    _netBuildingMap[k].QueueFree();
                _netBuildingMap.Remove(k);
            }
        }

        // 建立初始映射：如果映射表为空，用本地单位列表初始化
        if (_netUnitMap.Count == 0 && snap.Units != null)
        {
            var localUnits = GetAllUnits();
            foreach (var us in snap.Units)
            {
                // 尝试按Type+TeamId+最近位置匹配
                var match = localUnits.FirstOrDefault(u => IsInstanceValid(u)
                    && u.TeamId == us.TeamId
                    && (int)u.Type == us.UnitType
                    && u.GlobalPosition.DistanceTo(new Vector2(us.X, us.Y)) < 50f);
                if (match != null)
                    _netUnitMap[us.UnitId] = match;
            }
        }
        if (_netBuildingMap.Count == 0 && snap.Buildings != null)
        {
            var localBuildings = GetAllBuildings();
            foreach (var bs in snap.Buildings)
            {
                var match = localBuildings.FirstOrDefault(b => IsInstanceValid(b)
                    && b.TeamId == bs.TeamId
                    && (int)b.Type == bs.BuildingType
                    && b.GlobalPosition.DistanceTo(new Vector2(bs.X, bs.Y)) < 50f);
                if (match != null)
                    _netBuildingMap[bs.BuildingId] = match;
            }
        }
    }

    // ====== 游戏结束 ======

    /// <summary>收到游戏结束通知。</summary>
    private void OnNetGameOver(string result)
    {
        _gameWon = result.Contains("胜利") || result.Contains("victory");
        _gameResult = result;
        if (!_gameOver)
        {
            _gameOver = true;
            _gameOverDelay = 2f;
            GameLog.Debug($"[NetSync] 收到游戏结束: {result}");
        }
    }

    /// <summary>联机模式下的胜负判定重写。</summary>
    private void CheckWinConditionNet()
    {
        if (!NetworkManager.IsOnline) return;
        if (_gameOver) return;

        int myTeamId = NetworkManager.LocalTeamId;

        // 本地玩家全灭 = 失败
        int myUnits = CountUnitsOfTeam(myTeamId);
        int myBuildings = CountBuildingsOfTeam(myTeamId);
        if (myBuildings == 0 && myUnits == 0)
        {
            _gameOver = true;
            _gameWon = false;
            _gameResult = "战败";
            _gameOverDelay = 2f;
            return;
        }

        // 检查所有其他活跃阵营是否全灭
        bool anyEnemyAlive = false;
        foreach (var p in NetworkManager.Players.Values)
        {
            if (p.TeamId == myTeamId) continue;
            if (CountUnitsOfTeam(p.TeamId) > 0 || CountBuildingsOfTeam(p.TeamId) > 0)
            {
                anyEnemyAlive = true;
                break;
            }
        }
        if (!anyEnemyAlive)
        {
            _gameOver = true;
            _gameWon = true;
            _gameResult = "胜利";
            _gameOverDelay = 2f;
            // Host广播游戏结束
            if (NetworkManager.Role == NetworkManager.NetRole.Host)
                NetworkManager.SendGameOver("胜利");
        }
    }

    // ====== 辅助方法 ======

    private List<Unit> GetUnitsOfTeam(int teamId)
    {
        var result = new List<Unit>();
        foreach (var u in GetAllUnits())
            if (IsInstanceValid(u) && u.TeamId == teamId)
                result.Add(u);
        return result;
    }

    private List<Building> GetBuildingsOfTeam(int teamId)
    {
        var result = new List<Building>();
        foreach (var b in GetAllBuildings())
            if (IsInstanceValid(b) && b.TeamId == teamId)
                result.Add(b);
        return result;
    }

    /// <summary>为指定阵营寻找建筑放置位置（在基地附近）。</summary>
    private Vector2 FindBuildingPlacementNear(Vector2 basePos, BuildingType type, int teamId)
    {
        // 在基地附近螺旋搜索空位
        for (int radius = 80; radius <= 400; radius += 40)
        {
            for (float angle = 0; angle < Mathf.Pi * 2; angle += 0.5f)
            {
                var pos = basePos + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                pos = ClampToMap(pos, 50f);
                // 简单检查：没有重叠建筑
                bool overlap = false;
                foreach (var b in GetAllBuildings())
                {
                    if (IsInstanceValid(b) && b.GlobalPosition.DistanceTo(pos) < 70f)
                    {
                        overlap = true;
                        break;
                    }
                }
                if (!overlap) return pos;
            }
        }
        return ClampToMap(basePos + new Vector2(100, 100), 50f);
    }

    // ====== Team-aware 科技/时代/战术卡方法（联机命令执行，不触发Record） ======

    /// <summary>为指定阵营研究科技（联机版，不调用ReplayRecorder.Record）。</summary>
    private void ResearchTechForTeam(int teamId, int techNum)
    {
        if (techNum >= TechOrder.Length) return;
        var techId = TechOrder[techNum];
        var tp = _techProgress[teamId];
        if (tp == null) return;
        var node = TechTree.Nodes[techId];
        bool hasTech = HasBuilding(teamId, BuildingType.TechCenter) || !node.RequiresTechCenter;

        if (tp.Completed.Contains(techId)) return;
        if (tp.CurrentlyResearching.HasValue) return;
        if (!TechTree.CanResearch(tp.Completed, techId, hasTech, _money[teamId],
            FactionManager.GetFactionForTeam(teamId).Id))
            return;

        _money[teamId] -= node.Cost;
        tp.StartResearch(techId);
        GameLog.Debug($"[NetSync] Team {teamId} 开始研究: {node.Name} (成本${node.Cost})");
    }

    /// <summary>为指定阵营升级时代（联机版，不调用ReplayRecorder.Record）。</summary>
    private void AdvanceEraForTeam(int teamId)
    {
        var ep = _eraProgress[teamId];
        if (ep == null || ep.IsUpgrading) return;
        var next = EraSystem.GetNextEra(ep.CurrentEra);
        if (next == null) return;
        if (!EraSystem.CanAdvance(ep.CurrentEra, t => HasBuilding(teamId, t), _money[teamId]))
            return;

        _money[teamId] -= next.UpgradeCost;
        ep.StartUpgrade();
        GameLog.Debug($"[NetSync] Team {teamId} 开始时代升级 → {next.Name} (成本${next.UpgradeCost})");
    }

    /// <summary>为指定阵营选择战术卡（联机版，不调用ReplayRecorder.Record）。</summary>
    private void SelectCardForTeam(int teamId, TacticalCards.CardId card)
    {
        if (teamId == PlayerTeamId)
            _playerCard = card;
        else
            _aiCards[teamId - 1] = card;

        // 闪电经济即时效果
        if (card == TacticalCards.CardId.BlitzEconomy)
        {
            int startMoney = teamId == PlayerTeamId ? _blueStartMoney : _aiStartMoney;
            int bonus = (int)(startMoney * 0.5f);
            _money[teamId] += bonus;
        }

        ApplyCardEffectsToUnits(teamId);
        GameLog.Debug($"[NetSync] Team {teamId} 选择战术卡: {TacticalCards.Cards[card].Name}");
    }

    /// <summary>联机模式清理（游戏退出时调用）。</summary>
    private void CleanupNetSync()
    {
        if (!_netInitialized) return;
        NetworkManager.CommandReceived -= OnNetCommandReceived;
        ReplayRecorder.OnRecorded -= OnPlayerActionRecorded;
        NetworkManager.SnapshotReceived -= OnNetSnapshotReceived;
        NetworkManager.SnapshotData -= CollectAndSendSnapshot;
        NetworkManager.GameOverReceived -= OnNetGameOver;
        _netInitialized = false;
        _netUnitMap.Clear();
        _netBuildingMap.Clear();
    }
}
