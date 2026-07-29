using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RTSGame;

/// <summary>
/// Main 的存档/读档控制器（partial class）。
/// 包含：存档数据导出Getter + F5/F9快捷存读档 + ApplyLoadData状态重建 + ClearAllEntities + 位置查找辅助。
/// </summary>
public partial class Main
{
    // ==================== 存档数据导出 Getter ====================

    /// <summary>获取全部阵营资金数组的副本（索引0=玩家，1..7=AI）。</summary>
    public int[] GetMoneyArray()
    {
        var arr = new int[_money.Length];
        System.Array.Copy(_money, arr, _money.Length);
        return arr;
    }

    /// <summary>获取指定阵营的科技进度对象（供SaveLoadSystem读取Completed/CurrentlyResearching等字段）。</summary>
    public TechProgress? GetTechProgress(int teamId)
    {
        if (teamId >= 0 && teamId < _techProgress.Length)
            return _techProgress[teamId];
        return null;
    }

    /// <summary>获取指定阵营的时代进度对象（供SaveLoadSystem读取CurrentEra/IsUpgrading等字段）。</summary>
    public EraProgress? GetEraProgress(int teamId)
    {
        if (teamId >= 0 && teamId < _eraProgress.Length)
            return _eraProgress[teamId];
        return null;
    }

    /// <summary>获取玩家战术卡ID（-1=未选卡）。</summary>
    public int GetPlayerCardId() => _playerCard.HasValue ? (int)_playerCard.Value : -1;

    /// <summary>获取全部AI战术卡ID数组副本（索引0对应AI Team1，长度7，-1=未选）。</summary>
    public int[] GetAiCardIds()
    {
        var arr = new int[_aiCards.Length];
        for (int i = 0; i < _aiCards.Length; i++)
            arr[i] = _aiCards[i].HasValue ? (int)_aiCards[i].Value : -1;
        return arr;
    }

    /// <summary>获取场景中全部建筑（缓存版本，每帧最多遍历一次）。</summary>
    public List<Building> GetAllBuildings()
    {
        if (_buildingsCacheDirty)
        {
            _cachedBuildings.Clear();
            if (_buildingsNode != null)
            {
                foreach (var c in _buildingsNode.GetChildren())
                    if (c is Building b && IsInstanceValid(b) && !b.IsDead) _cachedBuildings.Add(b);
            }
            _buildingsCacheDirty = false;
        }
        return _cachedBuildings;
    }

    // ======== 缓存系统（P1-5性能优化）========
    private List<Unit> _cachedUnits = new(128);
    private List<Building> _cachedBuildings = new(64);
    private bool _unitsCacheDirty = true;
    private bool _buildingsCacheDirty = true;

    /// <summary>标记单位缓存需要刷新（单位创建/销毁时调用）。</summary>
    public void MarkUnitsCacheDirty() => _unitsCacheDirty = true;
    /// <summary>标记建筑缓存需要刷新（建筑创建/销毁时调用）。</summary>
    public void MarkBuildingsCacheDirty() => _buildingsCacheDirty = true;

    /// <summary>获取场景中全部单位（缓存版本，每帧最多遍历一次）。</summary>
    public List<Unit> GetAllUnits()
    {
        if (_unitsCacheDirty)
        {
            _cachedUnits.Clear();
            if (_unitsNode != null)
            {
                foreach (var c in _unitsNode.GetChildren())
                    if (c is Unit u && IsInstanceValid(u) && !u.IsDead) _cachedUnits.Add(u);
            }
            _unitsCacheDirty = false;
        }
        return _cachedUnits;
    }

    /// <summary>获取场景中全部资源点的有效引用列表。</summary>
    public List<ResourceNode> GetAllResourceNodes()
    {
        var list = new List<ResourceNode>();
        if (_resourcesNode == null) return list;
        foreach (var c in _resourcesNode.GetChildren())
            if (c is ResourceNode r && IsInstanceValid(r)) list.Add(r);
        return list;
    }

    /// <summary>获取场景中全部战略点的有效引用列表。</summary>
    public List<StrategicPoint> GetAllStrategicPoints()
    {
        var list = new List<StrategicPoint>();
        if (_strategicPointsNode == null) return list;
        foreach (var c in _strategicPointsNode.GetChildren())
            if (c is StrategicPoint sp && IsInstanceValid(sp)) list.Add(sp);
        return list;
    }

    /// <summary>获取地形修改增量列表（读档时按此覆写种子生成的地形）。</summary>
    /// <remarks>当前实现中SetCell由TerrainGrid内部调用，此处遍历地形比较种子默认值与当前值，输出差异。
    /// 为保证性能，仅在存档时遍历一次全网格。</remarks>
    public List<SaveLoadSystem.TerrainModSave> GetTerrainModifications()
    {
        var mods = new List<SaveLoadSystem.TerrainModSave>();
        if (_terrain == null) return mods;
        // 重建一份纯种子地形用于比较
        var seedGrid = new TerrainGrid();
        seedGrid.GenerateFromSeed(_mapSeed);
        for (int gy = 0; gy < TerrainGrid.GridSize; gy++)
        {
            for (int gx = 0; gx < TerrainGrid.GridSize; gx++)
            {
                var cur = _terrain.GetCell(gx, gy);
                var def = seedGrid.GetCell(gx, gy);
                if (cur.Type != def.Type
                    || cur.Elevation != def.Elevation
                    || cur.HasBridge != def.HasBridge
                    || cur.HasTunnel != def.HasTunnel)
                {
                    mods.Add(new SaveLoadSystem.TerrainModSave
                    {
                        Gx = gx, Gy = gy,
                        TerrainType = (int)cur.Type,
                        Elevation = cur.Elevation,
                        HasBridge = cur.HasBridge,
                        HasTunnel = cur.HasTunnel,
                    });
                }
            }
        }
        return mods;
    }

    /// <summary>获取三类超武的玩家与AI冷却时间快照。</summary>
    public SaveLoadSystem.CooldownSave GetSuperweaponCooldowns()
    {
        var cd = new SaveLoadSystem.CooldownSave
        {
            PlayerNuke = _playerNukeCooldown,
            PlayerLightning = _playerLightningCooldown,
            PlayerMissile = _playerMissileCooldown,
        };
        foreach (var kv in _aiNukeCooldowns) cd.AiNukes[kv.Key] = kv.Value;
        foreach (var kv in _aiLightningCooldowns) cd.AiLightnings[kv.Key] = kv.Value;
        foreach (var kv in _aiMissileCooldowns) cd.AiMissiles[kv.Key] = kv.Value;
        return cd;
    }

    // ==================== 存档路径处理 ====================

    /// <summary>获取默认存档目录 (user://saves/)，不存在则创建。</summary>
    public static string GetSaveDir()
    {
        string dir = "user://saves";
        if (!DirAccess.DirExistsAbsolute(dir))
            DirAccess.MakeDirRecursiveAbsolute(dir);
        return dir;
    }

    /// <summary>列出存档目录下所有 .json 存档文件名（不含路径）。</summary>
    public static string[] ListSaveFiles()
    {
        var dir = GetSaveDir();
        var files = new List<string>();
        if (!DirAccess.DirExistsAbsolute(dir)) return files.ToArray();
        using var da = DirAccess.Open(dir);
        if (da == null) return files.ToArray();
        da.ListDirBegin();
        string name = da.GetNext();
        while (!string.IsNullOrEmpty(name))
        {
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                files.Add(name);
            name = da.GetNext();
        }
        files.Sort((a, b) => string.Compare(b, a, StringComparison.Ordinal)); // 新文件在前
        return files.ToArray();
    }

    /// <summary>生成时间戳存档文件名，格式 quick_YYYYMMDD_HHmmSS.json。</summary>
    public static string MakeTimestampSaveName()
    {
        var now = DateTime.Now;
        return $"quick_{now:yyyyMMdd_HHmmss}.json";
    }

    // ==================== F5/F9 快捷存读档入口 ====================

    /// <summary>快速存档（F5）：保存到 user://saves/quick_timestamp.json。</summary>
    public void QuickSave()
    {
        try
        {
            GetSaveDir();
            string path = $"{GetSaveDir()}/{MakeTimestampSaveName()}";
            SaveLoadSystem.SaveGame(this, path);
            ShowToast(TrManager.Tr("saveload.saved", System.IO.Path.GetFileName(path)), new Color(0.4f, 1.0f, 0.4f));
        }
        catch (Exception ex)
        {
            GameLog.Error($"[SaveLoad] 快速存档失败: {ex.Message}");
            ShowToast(TrManager.Tr("saveload.save_failed"), new Color(1.0f, 0.3f, 0.3f));
        }
    }

    /// <summary>快速读档（F9）：从最新存档加载。</summary>
    public void QuickLoad()
    {
        try
        {
            var files = ListSaveFiles();
            if (files.Length == 0)
            {
                ShowToast(TrManager.Tr("saveload.no_saves"), new Color(1.0f, 0.8f, 0.3f));
                GameLog.Debug("[SaveLoad] 无存档可用");
                return;
            }
            string path = $"{GetSaveDir()}/{files[0]}";
            var data = SaveLoadSystem.LoadGame(path);
            if (data == null)
            {
                ShowToast(TrManager.Tr("saveload.load_corrupted"), new Color(1.0f, 0.3f, 0.3f));
                return;
            }
            ApplyLoadData(data);
            ShowToast(TrManager.Tr("saveload.loaded", files[0]), new Color(0.4f, 1.0f, 0.4f));
        }
        catch (Exception ex)
        {
            GameLog.Error($"[SaveLoad] 快速读档失败: {ex.Message}");
            ShowToast(TrManager.Tr("saveload.load_failed"), new Color(1.0f, 0.3f, 0.3f));
        }
    }

    // ==================== 读档后重建游戏状态 ====================

    /// <summary>P0-2: 将SaveData应用到当前场景，重建游戏状态。
    /// 流程：清空场景现有单位/建筑→用种子重建地形→应用地形增量→按位置重建实体→恢复资金/科技/时代/冷却。</summary>
    public void ApplyLoadData(SaveLoadSystem.SaveData data)
    {
        if (data == null) return;

        GameLog.Debug($"[SaveLoad] 开始应用存档：版本{data.Version} 建筑{data.Buildings.Count} 单位{data.Units.Count}");

        // 1. 清空现有单位/建筑/资源/战略点节点
        ClearAllEntities();

        // 2. 用种子重建基础地形 + 应用地形修改增量
        _mapSeed = data.MapSeed;
        _mapRng = new Random((int)(_mapSeed & 0x7FFFFFFF));
        _terrain.GenerateFromSeed(_mapSeed);
        if (data.TerrainMods != null)
        {
            foreach (var tm in data.TerrainMods)
            {
                if (tm.Gx < 0 || tm.Gx >= TerrainGrid.GridSize || tm.Gy < 0 || tm.Gy >= TerrainGrid.GridSize) continue;
                var cell = _terrain.GetCell(tm.Gx, tm.Gy);
                cell.Type = (TerrainType)tm.TerrainType;
                cell.Elevation = tm.Elevation;
                cell.HasBridge = tm.HasBridge;
                cell.HasTunnel = tm.HasTunnel;
                _terrain.SetCell(tm.Gx, tm.Gy, cell);
            }
            GameLog.Debug($"[SaveLoad] 应用了{data.TerrainMods.Count}个地形修改");
        }
        // 地形渲染：由于地图刷新，重置IsoTerrainRenderer的缓存，让下次绘制重新出图
        // IsoTerrainRenderer通过TerrainGrid读取数据，此处无需再额外触发

        // 3. 恢复难度、活跃AI数量、AI保护期
        _difficulty = (Difficulty)data.Difficulty;
        _activeAiCount = data.ActiveAiCount;
        Unit.AiGraceRemaining = data.AiGraceRemaining;
        StrategicPointIncomeEnabled = data.StrategicPointIncomeEnabled;

        // 4. 恢复资金
        for (int i = 0; i < TotalTeamCount && i < data.Money.Length; i++) _money[i] = data.Money[i];

        // 5. 恢复科技进度（清空现有状态后重新填入）
        for (int i = 0; i < TotalTeamCount; i++)
        {
            var tp = _techProgress[i];
            tp.Clear(); // 清空Completed和正在研究
            if (i < data.TechProgress.Length && data.TechProgress[i] != null)
            {
                var sv = data.TechProgress[i];
                foreach (var id in sv.Completed) tp.Completed.Add((TechTree.TechId)id);
                if (sv.CurrentlyResearching >= 0) tp.RestoreResearching((TechTree.TechId)sv.CurrentlyResearching, sv.ResearchTimer);
                if (sv.QueuedTech >= 0) tp.SetQueuedTech((TechTree.TechId)sv.QueuedTech);
            }
        }

        // 6. 恢复时代进度
        for (int i = 0; i < TotalTeamCount; i++)
        {
            var ep = _eraProgress[i];
            ep.Reset();
            if (i < data.EraProgress.Length && data.EraProgress[i] != null)
            {
                var sv = data.EraProgress[i];
                ep.Restore((EraSystem.Era)sv.CurrentEra, sv.IsUpgrading, sv.UpgradeTimer);
            }
        }

        // 7. 恢复战术卡
        _playerCard = data.PlayerCard >= 0 ? (TacticalCards.CardId?)data.PlayerCard : null;
        for (int i = 0; i < _aiCards.Length && i < data.AiCards.Length; i++)
            _aiCards[i] = data.AiCards[i] >= 0 ? (TacticalCards.CardId?)data.AiCards[i] : null;

        // 8. 恢复超武冷却
        _playerNukeCooldown = data.Cooldowns?.PlayerNuke ?? 0f;
        _playerLightningCooldown = data.Cooldowns?.PlayerLightning ?? 0f;
        _playerMissileCooldown = data.Cooldowns?.PlayerMissile ?? 0f;
        _aiNukeCooldowns.Clear();
        _aiLightningCooldowns.Clear();
        _aiMissileCooldowns.Clear();
        if (data.Cooldowns != null)
        {
            foreach (var kv in data.Cooldowns.AiNukes) _aiNukeCooldowns[kv.Key] = kv.Value;
            foreach (var kv in data.Cooldowns.AiLightnings) _aiLightningCooldowns[kv.Key] = kv.Value;
            foreach (var kv in data.Cooldowns.AiMissiles) _aiMissileCooldowns[kv.Key] = kv.Value;
        }

        // 9. 重建建筑
        foreach (var bs in data.Buildings)
        {
            var pos = new Vector2(bs.PosX, bs.PosY);
            var b = SpawnBuilding((BuildingType)bs.Type, pos, bs.TeamId);
            b.SetHealth(bs.Health);
            b.RestoreProductionState(bs.ProductionQueue, bs.CurrentProduction, bs.ProductionTimer, bs.ProductionDuration);
            if (bs.HasRallyPoint) b.SetRallyPoint(new Vector2(bs.RallyX, bs.RallyY));
            b.RestoreCaptureState(bs.OriginalTeamId, bs.CapturingTeamId, bs.CaptureProgress);
        }

        // 10. 重建单位（含矿车HomeBase回链、运输车乘客恢复）
        List<Unit> transportsWithPassengers = new();
        foreach (var us in data.Units)
        {
            var pos = new Vector2(us.PosX, us.PosY);
            Unit u;
            if ((UnitType)us.Type == UnitType.Harvester)
            {
                var home = FindNearestBase(pos, us.TeamId);
                u = SpawnHarvester(pos, us.TeamId, home);
                if (u is Harvester h && home != null) h.HomeBase = home;
            }
            else
            {
                u = SpawnUnit((UnitType)us.Type, pos, us.TeamId, us.AutoAI);
            }
            u.AutoDefend = us.AutoDefend;
            u.SetHealth(us.Health);
            u.RestoreLevel(us.Level, us.Experience);
            u.RestoreAbilities(us.Abilities.Select(a => (Unit.UnitAbility)a).ToList());
            u.RestoreHeroSkill((Unit.HeroSkill)us.HeroSkill);
            u.RestoreSpyState(us.SpyDisguiseTeam, us.LastAttackerTeam);
            if (us.HasMoveTarget) u.RestoreMoveTarget(new Vector2(us.MoveTargetX, us.MoveTargetY));
            if (us.HasGuardPosition) u.RestoreGuardPosition(new Vector2(us.GuardX, us.GuardY));
            if (us.PassengerTypes.Count > 0) transportsWithPassengers.Add(u);
        }
        // 11. 恢复运输车乘客（需要全部单位重新生成后再执行）
        // 当前数据仅保存了乘客类型/血量/等级，按简化策略重新创建乘客节点并EmbarkPassenger。
        foreach (var t in transportsWithPassengers)
        {
            var us = data.Units.FirstOrDefault(x =>
                Mathf.IsEqualApprox(x.PosX, t.GlobalPosition.X) && Mathf.IsEqualApprox(x.PosY, t.GlobalPosition.Y)
                && x.PassengerTypes.Count > 0);
            if (us == null) continue;
            int nPax = us.PassengerTypes.Count;
            // 防御：三列表长度应一致；不一致时取最小值避免越界
            nPax = Math.Min(nPax, Math.Min(us.PassengerHealths.Count, us.PassengerLevels.Count));
            for (int i = 0; i < nPax; i++)
            {
                var pu = SpawnUnit((UnitType)us.PassengerTypes[i], t.GlobalPosition, t.TeamId, false);
                pu.SetHealth(us.PassengerHealths[i]);
                pu.RestoreLevel(us.PassengerLevels[i], 0f);
                t.EmbarkPassenger(pu);
            }
        }

        // 12. 恢复资源点状态（资源点已在地图生成时生成，这里按位置匹配后更新Amount/OilOwner）
        if (_resourcesNode != null)
        {
            foreach (var rs in data.Resources)
            {
                var rn = FindResourceNodeAt(new Vector2(rs.PosX, rs.PosY));
                if (rn != null)
                {
                    rn.SetAmount(rs.Amount);
                    rn.SetOilOwner(rs.OilOwner);
                }
            }
        }

        // 13. 恢复战略点所有者
        if (_strategicPointsNode != null)
        {
            foreach (var sps in data.StrategicPoints)
            {
                var sp = FindStrategicPointAt(new Vector2(sps.PosX, sps.PosY));
                sp?.SetOwningTeam(sps.OwningTeam);
            }
        }

        // 14. 恢复游戏结束状态
        _gameOver = data.GameOver;
        _gameResult = data.GameResult ?? "";

        // 15. 重置PathFinder障碍（建筑已在SpawnBuilding时注册）
        GameLog.Debug($"[SaveLoad] 存档应用完成。单位数={CountUnitsOfTeam(0) + CountBuildingsOfTeam(0)}");
    }

    /// <summary>清空场景中所有单位、建筑节点（保留资源点/战略点/地形不重建）。</summary>
    /// <remarks>P0-2修复: 使用RemoveChild+Free立即释放，而非QueueFree延迟释放。
    /// 原实现用QueueFree导致ApplyLoadData同帧重建时旧实体还在场景树中，实体数量翻倍。</remarks>
    private void ClearAllEntities()
    {
        // 清空选择列表（引用即将失效）
        _selected.Clear();

        // 单位
        if (_unitsNode != null)
        {
            var toRemove = new List<Node>();
            foreach (var c in _unitsNode.GetChildren())
                if (c is Unit) toRemove.Add(c);
            foreach (var n in toRemove)
            {
                _unitsNode.RemoveChild(n);
                n.Free();
            }
        }
        // 建筑
        if (_buildingsNode != null)
        {
            var toRemove = new List<Node>();
            foreach (var c in _buildingsNode.GetChildren())
                if (c is Building) toRemove.Add(c);
            foreach (var n in toRemove)
            {
                if (n is Building b)
                {
                    b.Destroyed -= OnBuildingDestroyed; // 取消事件订阅避免Free时回调
                    if (_pathFinder != null)
                    {
                        _terrain.WorldToGrid(b.GlobalPosition.X, b.GlobalPosition.Y, out int gx, out int gy);
                        _pathFinder.RemoveBuilding(gx, gy, 1);
                    }
                }
                _buildingsNode.RemoveChild(n);
                n.Free();
            }
        }
        // 清空资源点和战略点（它们会由ApplyLoadData按种子重建后匹配）
        if (_resourcesNode != null)
        {
            var toRemove = new List<Node>();
            foreach (var c in _resourcesNode.GetChildren())
                if (c is ResourceNode) toRemove.Add(c);
            foreach (var n in toRemove)
            {
                _resourcesNode.RemoveChild(n);
                n.Free();
            }
        }
        if (_strategicPointsNode != null)
        {
            var toRemove = new List<Node>();
            foreach (var c in _strategicPointsNode.GetChildren())
                if (c is StrategicPoint) toRemove.Add(c);
            foreach (var n in toRemove)
            {
                _strategicPointsNode.RemoveChild(n);
                n.Free();
            }
        }
        // 重新生成资源点与战略点（基于种子）
        GenerateRandomOreDeposits();
        GenerateOilFields();
        GenerateRareMinerals();
        GenerateLandVeins();
        GenerateStrategicPoints();
    }

    /// <summary>找指定位置最近的基地建筑（用于矿车HomeBase回链）。</summary>
    private Building? FindNearestBase(Vector2 pos, int teamId)
    {
        Building? best = null;
        float bestD = float.MaxValue;
        if (_buildingsNode == null) return null;
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && IsInstanceValid(b) && b.TeamId == teamId && b.Type == BuildingType.Base)
            {
                float d = b.GlobalPosition.DistanceSquaredTo(pos);
                if (d < bestD) { bestD = d; best = b; }
            }
        }
        return best;
    }

    /// <summary>按位置（浮点近似）查找资源点（容忍±5像素）。</summary>
    private ResourceNode? FindResourceNodeAt(Vector2 pos)
    {
        if (_resourcesNode == null) return null;
        foreach (var c in _resourcesNode.GetChildren())
        {
            if (c is ResourceNode rn && IsInstanceValid(rn))
            {
                if (Mathf.Abs(rn.GlobalPosition.X - pos.X) < SaveLoadSystem.PositionMatchTolerance && Mathf.Abs(rn.GlobalPosition.Y - pos.Y) < SaveLoadSystem.PositionMatchTolerance)
                    return rn;
            }
        }
        return null;
    }

    /// <summary>按位置（浮点近似）查找战略点（容忍±5像素）。</summary>
    private StrategicPoint? FindStrategicPointAt(Vector2 pos)
    {
        if (_strategicPointsNode == null) return null;
        foreach (var c in _strategicPointsNode.GetChildren())
        {
            if (c is StrategicPoint sp && IsInstanceValid(sp))
            {
                if (Mathf.Abs(sp.GlobalPosition.X - pos.X) < SaveLoadSystem.PositionMatchTolerance && Mathf.Abs(sp.GlobalPosition.Y - pos.Y) < SaveLoadSystem.PositionMatchTolerance)
                    return sp;
            }
        }
        return null;
    }
}
