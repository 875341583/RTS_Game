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

        GameLog.Debug($"[NetSync] net sync initialized — role:{NetworkManager.Role} localTeamId:{NetworkManager.LocalTeamId}");
    }

    /// <summary>玩家操作被Record时触发 → 发送到网络。</summary>
    private void OnPlayerActionRecorded(ReplayRecorder.ActionType action, string json)
    {
        if (!NetworkManager.IsOnline) return;
        NetworkManager.SendCommand(action, json);
    }

    /// <summary>收到远端玩家命令时处理。返回true=执行成功。</summary>
    private bool OnNetCommandReceived(NetworkManager.NetCommand cmd)
    {
        if (cmd.TeamId == NetworkManager.LocalTeamId) return true; // 忽略自己的命令回显

        try
        {
            // 空参数的命令（如CommandStop、Scatter等）Params为空字符串
            JsonElement p = string.IsNullOrEmpty(cmd.Params)
                ? default
                : JsonSerializer.Deserialize<JsonElement>(cmd.Params);
            ExecuteNetCommand(cmd.Action, cmd.TeamId, p);
            return true;
        }
        catch (Exception e)
        {
            GameLog.Error($"[NetSync] command parse failed: {cmd.Action} — {e.Message}");
            return false;
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
                    // M4: 空引用防护
                    if (_terrain == null) break;
                    var target = GetXY(p);
                    var terrainCell = _terrain!.GetCellAtWorld(target.X, target.Y);
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
                        // 如果包含坐标，在精确位置放置建筑
                        if (p.TryGetProperty("X", out var xel) && p.TryGetProperty("Y", out var yel))
                        {
                            var pos = new Vector2(xel.GetSingle(), yel.GetSingle());
                            if (CanPlaceBuilding(pos))
                            {
                                _money[teamId] -= GetBuildingCost(bt, teamId);
                                SpawnBuilding(bt, pos, teamId);
                            }
                        }
                        // 无坐标 = 进入放置模式，远端不需要执行
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
                    // 使用坐标精确定位建筑
                    if (p.TryGetProperty("X", out var xel) && p.TryGetProperty("Y", out var yel))
                    {
                        var target = new Vector2(xel.GetSingle(), yel.GetSingle());
                        var bld = GetBuildingsOfTeam(teamId)
                            .Where(b => b.QueueCount > 0)
                            .OrderBy(b => b.GlobalPosition.DistanceSquaredTo(target))
                            .FirstOrDefault();
                        bld?.CancelLastProduction();
                    }
                    else
                    {
                        // 兼容旧格式：取消队列最长的建筑
                        var buildings = GetBuildingsOfTeam(teamId)
                            .Where(b => b.QueueCount > 0)
                            .OrderByDescending(b => b.QueueCount)
                            .FirstOrDefault();
                        buildings?.CancelLastProduction();
                    }
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
                    // 使用坐标精确匹配需要维修的建筑
                    if (p.TryGetProperty("Buildings", out var bldArr) && bldArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in bldArr.EnumerateArray())
                        {
                            if (item.TryGetProperty("X", out var xel) && item.TryGetProperty("Y", out var yel))
                            {
                                var target = new Vector2(xel.GetSingle(), yel.GetSingle());
                                var bld = GetBuildingsOfTeam(teamId)
                                    .Where(b => b.NeedsRepair)
                                    .OrderBy(b => b.GlobalPosition.DistanceSquaredTo(target))
                                    .FirstOrDefault();
                                if (bld != null)
                                {
                                    int cost = GetRepairCost(bld);
                                    if (_money[teamId] >= cost)
                                    {
                                        _money[teamId] -= cost;
                                        bld.Repair();
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // 兼容旧格式：维修所有需维修的建筑
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
                }
                break;

            case ReplayRecorder.ActionType.SellBuilding:
                {
                    string typeName = GetString(p, "Type");
                    // 使用坐标精确匹配要出售的建筑
                    if (p.TryGetProperty("X", out var xel) && p.TryGetProperty("Y", out var yel))
                    {
                        var target = new Vector2(xel.GetSingle(), yel.GetSingle());
                        var bld = GetBuildingsOfTeam(teamId)
                            .Where(b => b.Type != BuildingType.Base)
                            .OrderBy(b => b.GlobalPosition.DistanceSquaredTo(target))
                            .FirstOrDefault();
                        if (bld != null)
                        {
                            int refund = Math.Max(1, GetBuildingCost(bld.Type, teamId) / 2);
                            _money[teamId] += refund;
                            OnBuildingDestroyed(bld);
                            bld.Destroyed -= OnBuildingDestroyed;
                            bld.QueueFree();
                        }
                    }
                    else
                    {
                        // 兼容旧格式：出售所有同类型建筑
                        var toSell = GetBuildingsOfTeam(teamId)
                            .Where(b => b.Type != BuildingType.Base)
                            .ToList();
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
                GameLog.Debug($"[NetSync] remote command {action} not handled (TeamId={teamId})");
                break;
        }
    }

    // ====== 状态快照 ======

    /// <summary>Host：采集状态快照并广播。</summary>
    private void CollectAndSendSnapshot()
    {
        if (NetworkManager.Role != NetworkManager.NetRole.Host) return;

        // C3: 游戏开始时重置NetId，并为本场景所有单位/建筑分配NetId
        if (_netIdsAssigned == false)
        {
            NetworkManager.ResetNetIds();
            foreach (var u in GetAllUnits())
                if (IsInstanceValid(u) && u.NetId == 0)
                    u.NetId = NetworkManager.AllocateNetId();
            foreach (var b in GetAllBuildings())
                if (IsInstanceValid(b) && b.NetId == 0)
                    b.NetId = NetworkManager.AllocateNetId();
            _netIdsAssigned = true;
        }

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
            // C3: 确保新单位有NetId
            if (u.NetId == 0)
                u.NetId = NetworkManager.AllocateNetId();
            snap.Units.Add(new NetworkManager.UnitState
            {
                TeamId = u.TeamId,
                UnitType = (int)u.Type,
                X = u.GlobalPosition.X,
                Y = u.GlobalPosition.Y,
                Health = u.Health,
                NetId = u.NetId  // C3: 使用NetId替代InstanceId
            });
        }

        foreach (var b in GetAllBuildings())
        {
            if (!IsInstanceValid(b)) continue;
            if (!activeTeams.Contains(b.TeamId)) continue;
            if (b.NetId == 0)
                b.NetId = NetworkManager.AllocateNetId();
            snap.Buildings.Add(new NetworkManager.BuildingState
            {
                TeamId = b.TeamId,
                BuildingType = (int)b.Type,
                X = b.GlobalPosition.X,
                Y = b.GlobalPosition.Y,
                Health = b.Health,
                NetId = b.NetId,  // C3: 使用NetId
                QueueCount = b.QueueCount,
                ProductionType = b.CurrentProductionType.HasValue ? (int)b.CurrentProductionType.Value : -1
            });
        }

        // M6: 采集科技/时代/超武冷却
        int maxTeam = TotalTeamCount;
        snap.TechProgress = new int[maxTeam];
        snap.EraProgress = new int[maxTeam];
        snap.NukeCooldown = new float[maxTeam];
        snap.LightningCooldown = new float[maxTeam];
        snap.MissileCooldown = new float[maxTeam];
        for (int t = 0; t < maxTeam; t++)
        {
            var tp = _techProgress[t];
            snap.TechProgress[t] = tp?.CurrentlyResearching.HasValue == true ? (int)tp.CurrentlyResearching.Value : -1;
            snap.EraProgress[t] = (int)(_eraProgress[t]?.CurrentEra ?? EraSystem.Era.Stone);
            snap.NukeCooldown[t] = t == PlayerTeamId ? _playerNukeCooldown : (_aiNukeCooldowns.GetValueOrDefault(t, 0f));
            snap.LightningCooldown[t] = t == PlayerTeamId ? _playerLightningCooldown : (_aiLightningCooldowns.GetValueOrDefault(t, 0f));
            snap.MissileCooldown[t] = t == PlayerTeamId ? _playerMissileCooldown : (_aiMissileCooldowns.GetValueOrDefault(t, 0f));
        }

        // M6: 战略点占领状态
        snap.StrategicPoints = new List<NetworkManager.StrategicPointState>();
        if (_strategicPointsNode != null)
        {
            foreach (var c in _strategicPointsNode!.GetChildren())
            {
                if (c is not StrategicPoint sp || !IsInstanceValid(sp)) continue;
                snap.StrategicPoints.Add(new NetworkManager.StrategicPointState
                {
                    X = sp.GlobalPosition.X,
                    Y = sp.GlobalPosition.Y,
                    TeamId = sp.OwningTeam
                });
            }
        }

        NetworkManager.SendSnapshot(snap);
    }

    private bool _netIdsAssigned = false;

    // ====== 单位ID映射表（Client端：NetId → 本地Unit/Building节点） ======

    private readonly Dictionary<int, Unit> _netUnitMap = new();
    private readonly Dictionary<int, Building> _netBuildingMap = new();

    /// <summary>Client：接收状态快照并更新。</summary>
    private void OnNetSnapshotReceived(NetworkManager.StateSnapshotData snap)
    {
        if (NetworkManager.Role != NetworkManager.NetRole.Client) return;

        // M1: 更新资金（全阵营）— 只在差值超过阈值时纠正，避免回滚客户端刚执行的操作
        if (snap.Money != null)
        {
            for (int i = 0; i < snap.Money.Length && i < _money.Length; i++)
            {
                int diff = snap.Money[i] - _money[i];
                // 只在差值>50时纠正（避免覆盖刚执行的经济操作）
                if (System.Math.Abs(diff) > 50)
                    _money[i] = snap.Money[i];
            }
        }

        // 更新单位位置（通过NetId映射）— C3
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
            seenIds.Add(us.NetId);
            if (_netUnitMap.TryGetValue(us.NetId, out var u) && IsInstanceValid(u))
            {
                // 插值移动：逐步逼近目标位置
                var targetPos = new Vector2(us.X, us.Y);
                var diffVec = targetPos - u.GlobalPosition;
                if (diffVec.Length() > 200f)
                    u.GlobalPosition = targetPos; // 跳跃修正（单位刚生成或严重延迟）
                else
                    u.GlobalPosition += diffVec * 0.3f; // 插值平滑
                // 同步血量
                if (System.Math.Abs(u.Health - us.Health) > 1f)
                    u.SetHealth(us.Health);
            }
            else if (!_netUnitMap.ContainsKey(us.NetId))
            {
                // P1: 快照中有未知NetId的单位 → 客户端Spawn新单位
                var newUnit = SpawnUnit((UnitType)us.UnitType, new Vector2(us.X, us.Y), us.TeamId, autoAI: false);
                newUnit.NetId = us.NetId;
                newUnit.SetHealth(us.Health);
                _netUnitMap[us.NetId] = newUnit;
                GameLog.Debug($"[NetSync] client spawn new unit: NetId={us.NetId} Type={(UnitType)us.UnitType} Team={us.TeamId}");
            }
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

        // 更新建筑（类似处理）— C3
        if (snap.Buildings != null)
        {
            var deadBldKeys = new List<int>();
            foreach (var kv in _netBuildingMap)
                if (!IsInstanceValid(kv.Value)) deadBldKeys.Add(kv.Key);
            foreach (var k in deadBldKeys) _netBuildingMap.Remove(k);

            var seenBldIds = new HashSet<int>();
            foreach (var bs in snap.Buildings)
            {
            seenBldIds.Add(bs.NetId);
            if (_netBuildingMap.TryGetValue(bs.NetId, out var b) && IsInstanceValid(b))
            {
                b.GlobalPosition = new Vector2(bs.X, bs.Y); // 建筑不移动，直接设置
                // 同步血量
                if (System.Math.Abs(b.Health - bs.Health) > 1f)
                    b.SetHealth(bs.Health);
                // P2: 同步生产队列UI（仅更新非本地阵营建筑）
                if (bs.TeamId != NetworkManager.LocalTeamId)
                {
                    b.SyncProductionState(bs.QueueCount, bs.ProductionType);
                }
            }
            else if (!_netBuildingMap.ContainsKey(bs.NetId))
            {
                // P1: 快照中有未知NetId的建筑 → 客户端Spawn新建筑
                var newBld = SpawnBuilding((BuildingType)bs.BuildingType, new Vector2(bs.X, bs.Y), bs.TeamId);
                newBld.NetId = bs.NetId;
                newBld.SetHealth(bs.Health);
                _netBuildingMap[bs.NetId] = newBld;
                GameLog.Debug($"[NetSync] client spawn new building: NetId={bs.NetId} Type={(BuildingType)bs.BuildingType} Team={bs.TeamId}");
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

        // 建立初始映射：如果映射表为空，用本地单位列表初始化（C3: 用NetId匹配）
        if (_netUnitMap.Count == 0 && snap.Units != null)
        {
            var localUnits = GetAllUnits();
            foreach (var us in snap.Units)
            {
                // 按Type+TeamId+最近位置匹配
                var match = localUnits.FirstOrDefault(u => IsInstanceValid(u)
                    && u.TeamId == us.TeamId
                    && (int)u.Type == us.UnitType
                    && u.GlobalPosition.DistanceTo(new Vector2(us.X, us.Y)) < 50f);
                if (match != null)
                {
                    match.NetId = us.NetId; // C3: 记录分配的NetId
                    _netUnitMap[us.NetId] = match;
                }
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
                {
                    match.NetId = bs.NetId; // C3: 记录分配的NetId
                    _netBuildingMap[bs.NetId] = match;
                }
            }
        }

        // M6: 同步科技/时代/超武冷却
        if (snap.TechProgress != null)
        {
            for (int t = 0; t < snap.TechProgress.Length && t < _techProgress.Length; t++)
            {
                // 只更新非本地阵营的科技进度
                if (t == NetworkManager.LocalTeamId) continue;
                var tp = _techProgress[t];
                if (tp != null && snap.TechProgress[t] >= 0)
                    tp.RestoreResearching((TechTree.TechId)snap.TechProgress[t], 0f);
            }
        }
        if (snap.EraProgress != null)
        {
            for (int t = 0; t < snap.EraProgress.Length && t < _eraProgress.Length; t++)
            {
                if (t == NetworkManager.LocalTeamId) continue;
                var ep = _eraProgress[t];
                if (ep != null)
                    ep.Restore((EraSystem.Era)snap.EraProgress[t], false, 0f);
            }
        }

        // M6: 同步战略点占领状态
        if (snap.StrategicPoints != null && _strategicPointsNode != null)
        {
            foreach (var sps in snap.StrategicPoints)
            {
                StrategicPoint? match = null;
                foreach (var c in _strategicPointsNode!.GetChildren())
                {
                    if (c is StrategicPoint sp && IsInstanceValid(sp)
                        && sp.GlobalPosition.DistanceTo(new Vector2(sps.X, sps.Y)) < 50f)
                    {
                        match = sp;
                        break;
                    }
                }
                // P2: 使用已有的SetOwningTeam方法同步占领状态
                if (match != null && sps.TeamId >= 0)
                {
                    if (match.OwningTeam != sps.TeamId)
                        match.SetOwningTeam(sps.TeamId);
                }
                else if (match != null && sps.TeamId < 0 && match.OwningTeam >= 0)
                {
                    // 战略点变为中立
                    match.SetOwningTeam(-1);
                }
            }
        }
    }

    // ====== 游戏结束 ======

    /// <summary>收到游戏结束通知。</summary>
    private void OnNetGameOver(string result)
    {
        _gameWon = result.Contains("victory");
        _gameResult = result;
        if (!_gameOver)
        {
            _gameOver = true;
            _gameOverDelay = 2f;
            GameLog.Debug($"[NetSync] game over received: {result}");
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
            _gameResult = "defeat";
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
            _gameResult = "victory";
            _gameOverDelay = 2f;
            // Host广播游戏结束
            if (NetworkManager.Role == NetworkManager.NetRole.Host)
                NetworkManager.SendGameOver("victory");
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
        GameLog.Debug($"[NetSync] Team {teamId} start research: {node.Name} (cost ${node.Cost})");
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
        GameLog.Debug($"[NetSync] Team {teamId} start era upgrade → {next.Name} (cost ${next.UpgradeCost})");
    }

    /// <summary>为指定阵营选择战术卡（联机版，不调用ReplayRecorder.Record）。</summary>
    private void SelectCardForTeam(int teamId, TacticalCards.CardId card)
    {
        if (teamId == PlayerTeamId)
            _playerCard = card;
        else if (teamId > 0 && teamId <= _aiCards.Length)
            _aiCards[teamId - 1] = card;

        // 闪电经济即时效果
        if (card == TacticalCards.CardId.BlitzEconomy)
        {
            int startMoney = teamId == PlayerTeamId ? _blueStartMoney : _aiStartMoney;
            int bonus = (int)(startMoney * 0.5f);
            _money[teamId] += bonus;
        }

        ApplyCardEffectsToUnits(teamId);
        GameLog.Debug($"[NetSync] Team {teamId} select tactical card: {TacticalCards.Cards[card].Name}");
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
        _netIdsAssigned = false;
        _netUnitMap.Clear();
        _netBuildingMap.Clear();
    }
}
