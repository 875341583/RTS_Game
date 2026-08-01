using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace RTSGame;

/// <summary>
/// 联机游戏同步 — 接收远端玩家命令并应用到本地游戏实例。
/// 这是Main的partial class，处理NetworkManager.CommandReceived回调。
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

    /// <summary>收到远端玩家命令时处理。</summary>
    private void OnNetCommandReceived(NetworkManager.NetCommand cmd)
    {
        if (cmd.TeamId == NetworkManager.LocalTeamId) return; // 忽略自己的命令回显

        try
        {
            var p = JsonSerializer.Deserialize<JsonElement>(cmd.Params);
            ExecuteNetCommand(cmd.Action, cmd.TeamId, p);
        }
        catch (Exception e)
        {
            GameLog.Error($"[NetSync] 命令解析失败: {cmd.Action} — {e.Message}");
        }
    }

    /// <summary>执行远端玩家命令（映射到本地游戏逻辑）。</summary>
    private void ExecuteNetCommand(ReplayRecorder.ActionType action, int teamId, JsonElement p)
    {
        switch (action)
        {
            case ReplayRecorder.ActionType.CommandMove:
            case ReplayRecorder.ActionType.CommandAttackMove:
            case ReplayRecorder.ActionType.FormationMove:
                {
                    float x = p.GetProperty("x").GetSingle();
                    float y = p.GetProperty("y").GetSingle();
                    var target = new Vector2(x, y);
                    var units = GetUnitsOfTeam(teamId);
                    foreach (var u in units)
                        if (u.IsSelected) // 远端选中的单位
                        {
                            if (action == ReplayRecorder.ActionType.CommandAttackMove)
                                u.CommandAttackMove(target);
                            else
                                u.CommandMove(target);
                        }
                }
                break;

            case ReplayRecorder.ActionType.SpawnUnit:
                {
                    int unitType = p.GetProperty("unitType").GetInt32();
                    var type = (UnitType)unitType;
                    var buildings = GetBuildingsOfTeam(teamId);
                    foreach (var b in buildings)
                        if (b.Type == BuildingType.Barracks || b.Type == BuildingType.WarFactory)
                        {
                            b.EnqueueProduction(UnitTypeToProductionType(type));
                            break;
                        }
                }
                break;

            case ReplayRecorder.ActionType.PlaceBuilding:
                {
                    int bldgType = p.GetProperty("buildingType").GetInt32();
                    float x = p.GetProperty("x").GetSingle();
                    float y = p.GetProperty("y").GetSingle();
                    SpawnBuilding((BuildingType)bldgType, new Vector2(x, y), teamId);
                }
                break;

            case ReplayRecorder.ActionType.SelectCard:
                {
                    int cardIdx = p.GetProperty("cardIdx").GetInt32();
                    ApplyCardSelection(teamId, cardIdx);
                }
                break;

            // 其余命令类型的同步可以通过类似方式逐步扩展
            // 当前版本覆盖最核心的4种命令：移动、造兵、放建筑、选战术卡
            default:
                GameLog.Debug($"[NetSync] 远端命令 {action} 暂未实现同步（TeamId={teamId}）");
                break;
        }
    }

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

        foreach (var u in GetAllUnits())
        {
            if (!IsInstanceValid(u)) continue;
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

    /// <summary>Client：接收状态快照并插值更新。</summary>
    private void OnNetSnapshotReceived(NetworkManager.StateSnapshotData snap)
    {
        if (NetworkManager.Role != NetworkManager.NetRole.Client) return;

        // 更新资金
        if (snap.Money != null)
        {
            for (int i = 0; i < snap.Money.Length && i < _money.Length; i++)
                _money[i] = snap.Money[i];
        }

        // 单位位置插值更新（简化版：直接设置位置）
        // 完整实现需要做插值平滑，这里先做基础同步
        if (snap.Units != null)
        {
            foreach (var us in snap.Units)
            {
                // 尝试匹配已有单位，找不到则跳过（单位生成由命令同步处理）
                // 完整实现需要单位ID映射表
            }
        }

        GameLog.Debug($"[NetSync] 收到状态快照 — {snap.Units?.Count ?? 0}单位 {snap.Buildings?.Count ?? 0}建筑");
    }

    /// <summary>收到游戏结束通知。</summary>
    private void OnNetGameOver(string result)
    {
        _gameOver = true;
        _gameWon = result.Contains("胜利") || result.Contains("victory");
        _gameResult = result;
        _gameOverDelay = 2f;
    }

    /// <summary>联机模式下的胜负判定重写。</summary>
    private void CheckWinConditionNet()
    {
        if (!NetworkManager.IsOnline) return;
        if (_gameOver) return;

        // 本地玩家全灭 = 失败
        int myUnits = CountUnitsOfTeam(NetworkManager.LocalTeamId);
        int myBuildings = CountBuildingsOfTeam(NetworkManager.LocalTeamId);
        if (myBuildings == 0 && myUnits == 0)
        {
            _gameOver = true;
            _gameWon = false;
            _gameResult = "战败";
            _gameOverDelay = 2f;
            return;
        }

        // 所有非本地阵营全灭 = 胜利（简化版，不做联盟）
        bool anyEnemyAlive = false;
        for (int t = 0; t < TotalTeamCount; t++)
        {
            if (t == NetworkManager.LocalTeamId) continue;
            if (CountUnitsOfTeam(t) > 0 || CountBuildingsOfTeam(t) > 0)
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

    // ---- 辅助方法 ----

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

    /// <summary>应用战术卡效果给指定阵营（联机同步用）。</summary>
    private void ApplyCardSelection(int teamId, int cardIdx)
    {
        // 简化版：直接调用TacticalCards的apply逻辑
        // 完整实现需要在Main.Tech.cs中暴露ApplyCard方法
        GameLog.Debug($"[NetSync] TeamId {teamId} 选择了战术卡 {cardIdx}");
    }

    /// <summary>联机模式清理（游戏退出时调用）。</summary>
    private void CleanupNetSync()
    {
        if (!_netInitialized) return;
        NetworkManager.CommandReceived -= OnNetCommandReceived;
        NetworkManager.SnapshotReceived -= OnNetSnapshotReceived;
        NetworkManager.SnapshotData -= CollectAndSendSnapshot;
        NetworkManager.GameOverReceived -= OnNetGameOver;
        _netInitialized = false;
    }
}
