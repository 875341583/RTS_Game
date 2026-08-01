using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace RTSGame;

/// <summary>
/// Main 的回放操作回放控制器（partial class）。
/// 包含 ReplayPlayer 调用的所有 ReplayXxx 方法，
/// 将回放文件中的操作重新应用到游戏实例上。
/// P3-1: 与 ReplayRecorder 对应的逆操作。
/// </summary>
public partial class Main
{
    // 注意：回放期间大多数操作会重新触发对应的 GameLog/音效，
    // 但不会再次调用 ReplayRecorder.Record()（录制只在实时游戏中进行）。

    /// <summary>回放：单位移动。</summary>
    public void ReplayCommandMove(string? parms)
    {
        var pos = ParseReplayXY(parms);
        var friendlyUnits = GetSelectedFriendlyUnits();
        int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(friendlyUnits.Count)));
        for (int i = 0; i < friendlyUnits.Count; i++)
        {
            int col = i % cols, row = i / cols;
            friendlyUnits[i].CommandMove(pos + new Vector2(col * 40, row * 40));
        }
        GameLog.Debug($"[Replay] 移动 -> {pos}");
    }

    /// <summary>回放：攻击移动。</summary>
    public void ReplayCommandAttackMove(string? parms)
    {
        var pos = ParseReplayXY(parms);
        var friendlyUnits = GetSelectedFriendlyUnits();
        int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(friendlyUnits.Count)));
        for (int i = 0; i < friendlyUnits.Count; i++)
        {
            int col = i % cols, row = i / cols;
            friendlyUnits[i].CommandAttackMove(pos + new Vector2(col * 40, row * 40));
        }
        GameLog.Debug($"[Replay] 攻击移动 -> {pos}");
    }

    /// <summary>回放：攻击单位。</summary>
    public void ReplayCommandAttack(string? parms)
    {
        var pos = ParseReplayXY(parms);
        var friendlyUnits = GetSelectedFriendlyUnits();
        var enemyUnit = PickUnitAt(pos, requireEnemy: true);
        if (enemyUnit != null)
        {
            foreach (var unit in friendlyUnits)
                unit.CommandAttack(enemyUnit);
        }
        GameLog.Debug($"[Replay] 攻击 @ {pos}");
    }

    /// <summary>回放：攻击建筑。</summary>
    public void ReplayCommandAttackBuilding(string? parms)
    {
        var pos = ParseReplayXY(parms);
        var friendlyUnits = GetSelectedFriendlyUnits();
        var enemyBuilding = PickBuildingAt(pos, requireEnemy: true);
        if (enemyBuilding != null)
        {
            foreach (var unit in friendlyUnits)
                unit.CommandAttackBuilding(enemyBuilding);
        }
        GameLog.Debug($"[Replay] 攻击建筑 @ {pos}");
    }

    /// <summary>回放：停止。</summary>
    public void ReplayCommandStop()
    {
        var sel = GetSelectedFriendlyUnits();
        foreach (var u in sel) u.CommandStop();
        GameLog.Debug("[Replay] 停止");
    }

    /// <summary>回放：间谍任务。</summary>
    public void ReplayCommandSpyMission(string? parms)
    {
        // 间谍任务需要选中间谍+右键敌方建筑，回放中简化为日志
        GameLog.Debug($"[Replay] 间谍任务 ({parms})");
    }

    /// <summary>回放：地形改造。</summary>
    public void ReplayCommandTerrainMod(string? parms)
    {
        var pos = ParseReplayXY(parms, "TargetX", "TargetY");
        var friendlyUnits = GetSelectedFriendlyUnits();
        int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(friendlyUnits.Count)));
        var terrainCell = _terrain.GetCellAtWorld(pos.X, pos.Y);
        Unit.TerrainModType modType = DetectTerrainMod(terrainCell);
        if (modType != Unit.TerrainModType.None)
        {
            for (int i = 0; i < friendlyUnits.Count; i++)
            {
                if (friendlyUnits[i].IsEngineerUnit)
                {
                    int col = i % cols, row = i / cols;
                    friendlyUnits[i].CommandTerrainMod(modType, pos + new Vector2(col * 40, row * 40));
                }
            }
        }
        GameLog.Debug($"[Replay] 地形改造 @ {pos}");
    }

    /// <summary>回放：强制攻击。</summary>
    public void ReplayForceAttack(string? parms)
    {
        var pos = ParseReplayXY(parms);
        var friendlyUnits = GetSelectedFriendlyUnits();
        int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(friendlyUnits.Count)));
        for (int i = 0; i < friendlyUnits.Count; i++)
        {
            int col = i % cols, row = i / cols;
            friendlyUnits[i].CommandForceAttack(pos + new Vector2(col * 40, row * 40));
        }
        GameLog.Debug($"[Replay] 强制攻击 @ {pos}");
    }

    /// <summary>回放：散开。</summary>
    public void ReplayScatter()
    {
        var friendlyUnits = GetSelectedFriendlyUnits();
        foreach (var u in friendlyUnits) u.CommandScatter();
        GameLog.Debug($"[Replay] 散开 ({friendlyUnits.Count} 单位)");
    }

    /// <summary>回放：巡逻。</summary>
    public void ReplayPatrol(string? parms)
    {
        var pos = ParseReplayXY(parms);
        var friendlyUnits = GetSelectedFriendlyUnits();
        foreach (var u in friendlyUnits)
            u.CommandPatrol(u.GlobalPosition, pos);
        GameLog.Debug($"[Replay] 巡逻 -> {pos}");
    }

    /// <summary>回放：守卫/驻守。</summary>
    public void ReplayHoldPosition()
    {
        var friendlyUnits = GetSelectedFriendlyUnits();
        foreach (var u in friendlyUnits) u.CommandHoldPosition();
        GameLog.Debug("[Replay] 守卫/驻守");
    }

    /// <summary>回放：路径点追加。</summary>
    public void ReplayWaypoint(string? parms)
    {
        var pos = ParseReplayXY(parms);
        var friendlyUnits = GetSelectedFriendlyUnits();
        foreach (var u in friendlyUnits) u.EnqueueWaypoint(pos);
        GameLog.Debug($"[Replay] 路径点追加 @ {pos}");
    }

    /// <summary>回放：阵型移动。</summary>
    public void ReplayFormationMove(string? parms)
    {
        var pos = ParseReplayXY(parms);
        var friendlyUnits = GetSelectedFriendlyUnits();
        // 计算阵型偏移（与Main.Input.cs中FormationMove逻辑一致）
        var center = Vector2.Zero;
        foreach (var u in friendlyUnits) center += u.GlobalPosition;
        center /= Mathf.Max(1, friendlyUnits.Count);
        for (int i = 0; i < friendlyUnits.Count; i++)
        {
            var offset = friendlyUnits[i].GlobalPosition - center;
            friendlyUnits[i].CommandFormationMove(pos + offset);
        }
        GameLog.Debug($"[Replay] 阵型移动 -> {pos}");
    }

    /// <summary>回放：保存编队。</summary>
    public void ReplaySaveSquad(string? parms)
    {
        int idx = ParseReplayInt(parms, "Index");
        if (idx >= 0) SaveSquad(idx);
        GameLog.Debug($"[Replay] 保存编队 {idx}");
    }

    /// <summary>回放：选择编队。</summary>
    public void ReplaySelectSquad(string? parms)
    {
        int idx = ParseReplayInt(parms, "Index");
        if (idx >= 0) SelectSquad(idx);
        GameLog.Debug($"[Replay] 选择编队 {idx}");
    }

    /// <summary>回放：放置建筑。</summary>
    public void ReplayPlaceBuilding(string? parms)
    {
        string? typeName = ParseReplayString(parms, "Type");
        if (Enum.TryParse<BuildingType>(typeName, out var bt))
        {
            TryBuildBuilding(bt);
        }
        GameLog.Debug($"[Replay] 放置建筑 {typeName}");
    }

    /// <summary>回放：取消放置。</summary>
    public void ReplayCancelPlacement()
    {
        CancelPlacement();
        GameLog.Debug("[Replay] 取消放置");
    }

    /// <summary>回放：生产单位。</summary>
    public void ReplaySpawnUnit(string? parms)
    {
        string? typeName = ParseReplayString(parms, "Type");
        if (Enum.TryParse<UnitType>(typeName, out var ut))
        {
            TrySpawnUnit(ut);
        }
        GameLog.Debug($"[Replay] 生产单位 {typeName}");
    }

    /// <summary>回放：生产矿车。</summary>
    public void ReplaySpawnHarvester()
    {
        TrySpawnHarvester();
        GameLog.Debug("[Replay] 生产矿车");
    }

    /// <summary>回放：取消生产。</summary>
    public void ReplayCancelProduction(string? parms)
    {
        // 简化：回放中无法精确还原取消的是哪个建筑的生产
        GameLog.Debug($"[Replay] 取消生产 ({parms})");
    }

    /// <summary>回放：设置集结点。</summary>
    public void ReplaySetRallyPoint(string? parms)
    {
        var pos = ParseReplayXY(parms);
        GameLog.Debug($"[Replay] 集结点 -> {pos}");
    }

    /// <summary>回放：核弹。</summary>
    public void ReplayNuke(string? parms)
    {
        var pos = ParseReplayXY(parms);
        ApplyNuke(pos, PlayerTeamId);
        _playerNukeCooldown = GameConst.NukeCooldown;
        GameLog.Debug($"[Replay] 核弹 @ {pos}");
    }

    /// <summary>回放：闪电风暴。</summary>
    public void ReplayLightning(string? parms)
    {
        var pos = ParseReplayXY(parms);
        ApplyLightning(pos, PlayerTeamId);
        _playerLightningCooldown = GameConst.LightningCooldown;
        GameLog.Debug($"[Replay] 闪电风暴 @ {pos}");
    }

    /// <summary>回放：巡航导弹。</summary>
    public void ReplayCruiseMissile(string? parms)
    {
        var pos = ParseReplayXY(parms);
        ApplyCruiseMissile(pos, PlayerTeamId);
        _playerMissileCooldown = GameConst.MissileCooldown;
        GameLog.Debug($"[Replay] 巡航导弹 @ {pos}");
    }

    /// <summary>回放：维修建筑。</summary>
    public void ReplayRepairBuilding(string? parms)
    {
        foreach (var o in _selected)
        {
            if (o is Building b && b.TeamId == PlayerTeamId && IsInstanceValid(b) && b.NeedsRepair)
            {
                int cost = GetRepairCost(b);
                if (_money[PlayerTeamId] >= cost)
                {
                    _money[PlayerTeamId] -= cost;
                    b.Repair();
                }
            }
        }
        GameLog.Debug("[Replay] 维修建筑");
    }

    /// <summary>回放：出售建筑。</summary>
    public void ReplaySellBuilding(string? parms)
    {
        string? typeName = ParseReplayString(parms, "Type");
        var toSell = new List<Building>();
        foreach (var o in _selected)
        {
            if (o is Building b && b.TeamId == PlayerTeamId && IsInstanceValid(b) && b.Type != BuildingType.Base)
                toSell.Add(b);
        }
        foreach (var b in toSell)
        {
            int refund = Mathf.Max(1, GetBuildingCost(b.Type) / 2);
            _money[PlayerTeamId] += refund;
            b.SetSelected(false);
            _selected.Remove(b);
            OnBuildingDestroyed(b);
            b.Destroyed -= OnBuildingDestroyed;
            b.QueueFree();
        }
        GameLog.Debug($"[Replay] 出售建筑 {typeName}");
    }

    /// <summary>回放：研究科技。</summary>
    public void ReplayResearchTech(string? parms)
    {
        string? techIdStr = ParseReplayString(parms, "TechId");
        // 尝试解析 TechId 索引
        for (int i = 0; i < TechOrder.Length; i++)
        {
            if (TechOrder[i].ToString() == techIdStr)
            {
                TryResearchTech(i);
                return;
            }
        }
        GameLog.Debug($"[Replay] 研究科技 {techIdStr}");
    }

    /// <summary>回放：时代升级。</summary>
    public void ReplayAdvanceEra(string? parms)
    {
        TryAdvanceEra();
        GameLog.Debug($"[Replay] 时代升级 ({parms})");
    }

    /// <summary>回放：选择战术卡。</summary>
    public void ReplaySelectCard(string? parms)
    {
        string? cardStr = ParseReplayString(parms, "Card");
        if (Enum.TryParse<TacticalCards.CardId>(cardStr, out var cardId))
        {
            SelectPlayerCard(cardId);
        }
        GameLog.Debug($"[Replay] 选择战术卡 {cardStr}");
    }

    // ---- 回放参数解析辅助 ----

    /// <summary>从 JSON 参数中解析 X/Y 坐标。</summary>
    private static Vector2 ParseReplayXY(string? json, string xKey = "X", string yKey = "Y")
    {
        if (string.IsNullOrEmpty(json)) return Vector2.Zero;
        try
        {
            var doc = JsonDocument.Parse(json);
            float x = doc.RootElement.TryGetProperty(xKey, out var xe) ? xe.GetSingle() : 0f;
            float y = doc.RootElement.TryGetProperty(yKey, out var ye) ? ye.GetSingle() : 0f;
            return new Vector2(x, y);
        }
        catch { return Vector2.Zero; }
    }

    /// <summary>从 JSON 参数中解析整数值。</summary>
    private static int ParseReplayInt(string? json, string key)
    {
        if (string.IsNullOrEmpty(json)) return -1;
        try
        {
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var el) ? el.GetInt32() : -1;
        }
        catch { return -1; }
    }

    /// <summary>从 JSON 参数中解析字符串值。</summary>
    private static string? ParseReplayString(string? json, string key)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var el) ? el.GetString() : null;
        }
        catch { return null; }
    }
}
