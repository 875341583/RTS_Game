using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RTSGame;

/// <summary>
/// Main 的经济/建造/生产/AI决策控制器（partial class）。
/// 包含：金钱管理 + 单位生产 + 建筑建造 + 电力计算 + AI经济决策 + Spawn系列 + 资源点。
/// </summary>
public partial class Main
{

    /// <summary>扣减指定阵营的金钱。成功返回true，资金不足返回false。</summary>
    public bool SpendMoney(int teamId, int amount)
    {
        if (teamId < 0 || teamId >= TotalTeamCount) return false;
        if (_money[teamId] < amount) return false;
        _money[teamId] -= amount;
        return true;
    }

    // ---------- 制造 ----------
    /// <summary>P1-2: 尝试生产单位（造价从GameData获取，含阵营乘数）。</summary>
    private void TrySpawnUnit(UnitType type)
    {
        int t = PlayerTeamId;
        int cost = GetUnitCost(type);

        // 建筑前置检查
        if (!CanProduceUnit(t, type))
        {
            GameLog.Warning($"[Warning] missing building required to produce {type}!");
            return;
        }

        // U2: Shift批量加入队列（最多5个）
        int batchCount = Input.IsKeyPressed(Key.Shift) ? 5 : 1;

        for (int i = 0; i < batchCount; i++)
        {
            // 电力检查
            if (GetTeamPower(t) < 0)
            {
                GameLog.Error($"[Warning] power insufficient, cannot produce units! current power: {GetTeamPower(t)}");
                break;
            }

            // G2：单位上限检查（活跃单位 + 队列中）+ G1科技上限加成 + G3战术卡加成
            int effectiveCap = _unitCap + GetTechUnitCapBonus(t) + GetCardUnitCapBonus(t);
            int total = CountUnitsOfTeam(t) + CountQueuedUnitsOfTeam(t);
            if (total >= effectiveCap)
            {
                GameLog.Warning($"[Warning] unit cap reached {effectiveCap}!");
                break;
            }

            // G2：找生产建筑（队列最短的同类建筑，实现多建筑并行）
            var producer = FindProducerForUnit(type, t);
            if (producer == null)
            {
                GameLog.Warning($"[Warning] no available {GetProducerForUnit(type)}!");
                break;
            }

            if (_money[t] < cost)
            {
                GameLog.Warning($"[Warning] money insufficient! need ${cost}, current ${_money[t]}");
                _audio?.PlaySfx(AudioManager.Sfx.UiError);
                break;
            }

            _money[t] -= cost;
            producer.EnqueueProduction(UnitTypeToProductionType(type));
            ReplayRecorder.Record(ReplayRecorder.ActionType.SpawnUnit, new { Type = type.ToString() });
            GameLog.Debug($"Team {t} queued {type} (batch {i+1}/{batchCount}), cost ${cost}, balance ${_money[t]}, {producer.BuildingName} queue {producer.QueueCount}/{Building.MaxQueueSize}");
        }
        _audio?.PlaySfx(AudioManager.Sfx.UiBuildStart);
    }

    public void TrySpawnHarvester()
    {
        int t = PlayerTeamId;
        int cost = GetUnitCost(UnitType.Harvester);
        if (_money[t] < cost) { GameLog.Warning("[Warning] money insufficient!"); _audio?.PlaySfx(AudioManager.Sfx.UiError); return; }
        if (GetTeamPower(t) < 0) { GameLog.Warning("[Warning] power insufficient!"); return; }

        int total = CountUnitsOfTeam(t) + CountQueuedUnitsOfTeam(t);
        if (total >= _unitCap) { GameLog.Warning($"[Warning] unit cap reached {_unitCap}!"); return; }

        var producer = FindProducerBuilding(BuildingType.Base, t);
        if (producer == null) { GameLog.Warning("[Warning] no base!"); return; }

        _money[t] -= cost;
        producer.EnqueueProduction(ProductionType.Harvester);
        ReplayRecorder.Record(ReplayRecorder.ActionType.SpawnHarvester);
        GameLog.Debug($"Team {t} queued harvester, cost ${cost}, balance ${_money[t]}, queue {producer.QueueCount}/{Building.MaxQueueSize}");
    }

    // ---------- 建造系统 ----------
    private bool CanProduceUnit(int teamId, UnitType unitType)
    {
        // P1-2: 阵营白名单检查
        var faction = FactionManager.GetFactionForTeam(teamId);
        if (!faction.CanProduceUnit(unitType))
        {
            if (teamId == PlayerTeamId) GameLog.Warning($"[P1-2] {faction.Name} cannot produce {unitType}!");
            return false;
        }

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
            UnitType.ApocalypseTank => HasBuilding(teamId, BuildingType.WarFactory) && HasBuilding(teamId, BuildingType.TechCenter),
            UnitType.PrismTank => HasBuilding(teamId, BuildingType.WarFactory) && HasBuilding(teamId, BuildingType.TechCenter),
            UnitType.KirovAirship => HasBuilding(teamId, BuildingType.Airfield) && HasBuilding(teamId, BuildingType.TechCenter),
            UnitType.TeslaTrooper => HasBuilding(teamId, BuildingType.Barracks) && HasBuilding(teamId, BuildingType.TechCenter),
            UnitType.RocketLauncher => HasBuilding(teamId, BuildingType.TechCenter),
            UnitType.MissileTank => HasBuilding(teamId, BuildingType.TechCenter),
            UnitType.ChiefEngineer => HasBuilding(teamId, BuildingType.TechCenter),
            _ => HasBuilding(teamId, BuildingType.Base)
        };
    }

    private int GetTeamPower(int teamId)
    {
        int produced = 0, consumed = 0;
        // P2-8修复：离线建筑（超出供电半径）仍计耗电但标记为低效运营，
        // 全局电力 = 所有发电 - 所有耗电（与红警2一致：全局电力池模型）。
        // 电网分区系统(PowerGrid)独立控制离线建筑的生产速度降为50%。
        // 这里保持全局池模型不变，但在电网面板中展示分区详情。
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

    /// <summary>P1-2: 获取单位造价（含阵营乘数和科技折扣）。</summary>
    private int GetUnitCost(UnitType type)
    {
        return GetUnitCost(type, PlayerTeamId);
    }

    /// <summary>P1-2: 获取单位造价（含阵营乘数和科技折扣）。</summary>
    private int GetUnitCost(UnitType type, int teamId)
    {
        int baseCost = GameData.GetUnitCost(type);
        // 阵营乘数
        var faction = FactionManager.GetFactionForTeam(teamId);
        int cost = faction.ApplyCost(baseCost);
        // G1: 科技批量生产折扣
        cost = Mathf.Max(1, (int)(cost * GetTechCostMultiplier(teamId)));
        return cost;
    }

    /// <summary>P1-2: 获取建筑造价（含阵营乘数）。</summary>
    private int GetBuildingCost(BuildingType type, int teamId)
    {
        int baseCost = GameData.GetBuildingCost(type);
        var faction = FactionManager.GetFactionForTeam(teamId);
        return faction.ApplyCost(baseCost);
    }

    /// <summary>P1-2: 获取建筑造价（玩家阵营）。</summary>
    private int GetBuildingCost(BuildingType type)
    {
        return GetBuildingCost(type, PlayerTeamId);
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
        // P1-2: 阵营白名单检查
        var playerFaction = FactionManager.GetFactionForTeam(PlayerTeamId);
        if (!playerFaction.CanBuild(type))
        {
            GameLog.Warning($"[P1-2] {playerFaction.Name} cannot build {type}!");
            return;
        }

        int pId = PlayerTeamId;
        // 前置建筑检查
        if (type == BuildingType.PowerPlant && !HasBuilding(pId, BuildingType.Base)) { GameLog.Warning("[Warning] need construction yard first!"); return; }
        if (type == BuildingType.Barracks && !HasBuilding(pId, BuildingType.PowerPlant)) { GameLog.Warning("[Warning] need power plant first!"); return; }
        if (type == BuildingType.WarFactory && !HasBuilding(pId, BuildingType.Barracks)) { GameLog.Warning("[Warning] need barracks first!"); return; }
        if (type == BuildingType.TechCenter && !HasBuilding(pId, BuildingType.WarFactory)) { GameLog.Warning("[Warning] need war factory first!"); return; }
        // 阶段12-A1+A2 新增前置
        if (type == BuildingType.Turret && !HasBuilding(pId, BuildingType.Barracks)) { GameLog.Warning("[Warning] need barracks first!"); return; }
        if (type == BuildingType.AntiAirTurret && !HasBuilding(pId, BuildingType.WarFactory)) { GameLog.Warning("[Warning] need war factory first!"); return; }
        if (type == BuildingType.RepairPad && !HasBuilding(pId, BuildingType.WarFactory)) { GameLog.Warning("[Warning] need war factory first!"); return; }

        // P5：难度科技等级限制（系统复杂度分级）
        if (type == BuildingType.WarFactory && _playerTechLevel < 2) { GameLog.Debug("[Difficulty] war factory not unlocked at current difficulty!"); return; }
        if (type == BuildingType.TechCenter && _playerTechLevel < 3) { GameLog.Debug("[Difficulty] tech center not unlocked at current difficulty!"); return; }
        if (type == BuildingType.AntiAirTurret && _playerTechLevel < 2) { GameLog.Debug("[Difficulty] anti-air turret not unlocked at current difficulty!"); return; }
        if (type == BuildingType.RepairPad && _playerTechLevel < 2) { GameLog.Debug("[Difficulty] repair pad not unlocked at current difficulty!"); return; }

        // G2: 时代限制检查
        if (!IsBuildingUnlockedByEra(pId, type))
        {
            var ep = _eraProgress[pId];
            GameLog.Debug($"[G2] {type} requires {(EraSystem.GetNextEra(ep.CurrentEra)?.Name ?? "higher era")} to build! current: {EraSystem.Eras[(int)ep.CurrentEra].Name}");
            return;
        }

        // 电力检查（电站本身不受电力限制）
        if (type != BuildingType.PowerPlant && GetTeamPower(pId) < 0)
        {
            GameLog.Warning($"[Warning] power insufficient! current power: {GetTeamPower(pId)}");
            return;
        }

        // 资金检查
        int cost = GetBuildingCost(type);
        if (_money[pId] < cost) { GameLog.Warning($"[Warning] money insufficient! need ${cost}, current ${_money[pId]}"); _audio?.PlaySfx(AudioManager.Sfx.UiError); return; }

        // Q1：进入放置模式（玩家手动选择位置）
        _placementMode = type;
        ReplayRecorder.Record(ReplayRecorder.ActionType.PlaceBuilding, new { Type = type.ToString() });
        if (_buildPanel != null) _buildPanel.ActivePlacement = type;
        QueueRedraw();
        _audio?.PlaySfx(AudioManager.Sfx.UiBuildStart);
        GameLog.Debug($"[Placement] select {type} placement position, left-click to place / right-click to cancel");
    }

    // ---------- Q1 建筑放置 ----------
    public void CancelPlacement()
    {
        _placementMode = null;
        ReplayRecorder.Record(ReplayRecorder.ActionType.CancelPlacement);
        if (_buildPanel != null) _buildPanel.ActivePlacement = null;
        QueueRedraw();
    }

    // P1-2: GetBuildingCost 已迁移到上方（支持阵营乘数的重载版本）

    /// <summary>补强：取消建造/生产时播放取消音效。</summary>
    public void PlayBuildCancelSfx()
    {
        _audio?.PlaySfx(AudioManager.Sfx.BuildCancel);
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
        int t = PlayerTeamId;
        if (_money[t] < cost) { GameLog.Debug("[Placement] money insufficient"); CancelPlacement(); return; }
        if (!CanPlaceBuilding(pos)) { GameLog.Debug("[Placement] position occupied"); return; }
        _money[t] -= cost;
        SpawnBuilding(type, pos, teamId: t);
        GameLog.Debug($"Team {t} built {type}, cost ${cost}, balance ${_money[t]}, position {pos}");
        _audio?.PlaySfx(AudioManager.Sfx.UiPlace);
        // 记录实际放置坐标（联机+回放都需要）
        ReplayRecorder.Record(ReplayRecorder.ActionType.PlaceBuilding, new { Type = type.ToString(), X = pos.X, Y = pos.Y });
        // 放一个就退出放置模式（红警2风格：点一次放一个）
        CancelPlacement();
    }

    private void AIBuildLogic(int teamId, AIStrategy strategy = AIStrategy.Expand)
    {
        if (!_bases.TryGetValue(teamId, out var baseB) || baseB == null || !IsInstanceValid(baseB)) return;

        int power = GetTeamPower(teamId);
        bool hasPower = HasBuilding(teamId, BuildingType.PowerPlant);
        bool hasBarracks = HasBuilding(teamId, BuildingType.Barracks);
        bool hasWarFactory = HasBuilding(teamId, BuildingType.WarFactory);
        bool hasTechCenter = HasBuilding(teamId, BuildingType.TechCenter);

        // === 策略影响：Defend时防御塔优先 ===
        // Defend策略：优先补防御塔（提升上限到4座）和防空炮
        if (strategy == AIStrategy.Defend)
        {
            // 防御塔上限提升到4座（正常2座）
            int turretCountDef = CountBuildingOfType(teamId, BuildingType.Turret);
            if (hasBarracks && turretCountDef < 4
                && _money[teamId] >= GetBuildingCost(BuildingType.Turret, teamId) + 100 && power >= 0
                && IsBuildingUnlockedByEra(teamId, BuildingType.Turret))
            {
                _money[teamId] -= GetBuildingCost(BuildingType.Turret, teamId);
                SpawnBuilding(BuildingType.Turret, GetAIBuildPosition(teamId, BuildingType.Turret), teamId);
                GameLog.Debug($"[AI] Team {teamId} built Turret (Defend) #{turretCountDef + 1}, ${_money[teamId]} left");
                return;
            }
            // 防空炮上限提升到3座
            int aaCountDef = CountBuildingOfType(teamId, BuildingType.AntiAirTurret);
            if (hasWarFactory && aaCountDef < 3
                && _money[teamId] >= GetBuildingCost(BuildingType.AntiAirTurret, teamId) + 100 && power >= 0
                && IsBuildingUnlockedByEra(teamId, BuildingType.AntiAirTurret))
            {
                _money[teamId] -= GetBuildingCost(BuildingType.AntiAirTurret, teamId);
                SpawnBuilding(BuildingType.AntiAirTurret, GetAIBuildPosition(teamId, BuildingType.AntiAirTurret), teamId);
                GameLog.Debug($"[AI] Team {teamId} built AntiAirTurret (Defend) #{aaCountDef + 1}, ${_money[teamId]} left");
                return;
            }
        }

        // === 策略影响：Attack时减少建筑投入（只补关键防御） ===
        // Attack策略：跳过非关键建筑建造，把资金留给造兵
        bool skipNonEssentialBuildings = (strategy == AIStrategy.Attack);

        // 优先级1：没电站就建电站（基地消耗50电，必须建电站）
        if (!hasPower && _money[teamId] >= GetBuildingCost(BuildingType.PowerPlant, teamId))
        {
            _money[teamId] -= GetBuildingCost(BuildingType.PowerPlant, teamId);
            SpawnBuilding(BuildingType.PowerPlant, GetAIBuildPosition(teamId, BuildingType.PowerPlant), teamId);
            GameLog.Debug($"[AI] Team {teamId} built PowerPlant, ${_money[teamId]} left");
            return;
        }

        // 优先级2：电力不足（<30）时补电站
        if (hasPower && power < 30 && _money[teamId] >= GetBuildingCost(BuildingType.PowerPlant, teamId))
        {
            _money[teamId] -= GetBuildingCost(BuildingType.PowerPlant, teamId);
            SpawnBuilding(BuildingType.PowerPlant, GetAIBuildPosition(teamId, BuildingType.PowerPlant), teamId);
            GameLog.Debug($"[AI] Team {teamId} built PowerPlant (low power), ${_money[teamId]} left");
            return;
        }

        // 优先级3：建兵营
        if (hasPower && !hasBarracks && _money[teamId] >= GetBuildingCost(BuildingType.Barracks, teamId))
        {
            _money[teamId] -= GetBuildingCost(BuildingType.Barracks, teamId);
            SpawnBuilding(BuildingType.Barracks, GetAIBuildPosition(teamId, BuildingType.Barracks), teamId);
            GameLog.Debug($"[AI] Team {teamId} built Barracks, ${_money[teamId]} left");
            return;
        }

        // 优先级4：建战车工厂
        if (hasBarracks && !hasWarFactory && _money[teamId] >= GetBuildingCost(BuildingType.WarFactory, teamId)
            && IsBuildingUnlockedByEra(teamId, BuildingType.WarFactory))
        {
            _money[teamId] -= GetBuildingCost(BuildingType.WarFactory, teamId);
            SpawnBuilding(BuildingType.WarFactory, GetAIBuildPosition(teamId, BuildingType.WarFactory), teamId);
            GameLog.Debug($"[AI] Team {teamId} built WarFactory, ${_money[teamId]} left");
            return;
        }

        // 优先级5：建科技中心（解锁高级兵种）
        if (_aiUsesTech && hasWarFactory && !hasTechCenter && _money[teamId] >= GetBuildingCost(BuildingType.TechCenter, teamId) && power >= 0
            && IsBuildingUnlockedByEra(teamId, BuildingType.TechCenter))
        {
            _money[teamId] -= GetBuildingCost(BuildingType.TechCenter, teamId);
            SpawnBuilding(BuildingType.TechCenter, GetAIBuildPosition(teamId, BuildingType.TechCenter), teamId);
            GameLog.Debug($"[AI] Team {teamId} built TechCenter, ${_money[teamId]} left");
            return;
        }

        // 优先级6：后期电力不够就再建电站
        if (hasTechCenter && power < 50 && _money[teamId] >= GetBuildingCost(BuildingType.PowerPlant, teamId))
        {
            _money[teamId] -= GetBuildingCost(BuildingType.PowerPlant, teamId);
            SpawnBuilding(BuildingType.PowerPlant, GetAIBuildPosition(teamId, BuildingType.PowerPlant), teamId);
            GameLog.Debug($"[AI] Team {teamId} built PowerPlant (for tech center), ${_money[teamId]} left");
            return;
        }

        // ---- 阶段12-A1+A2：防御建筑与维修厂 ----
        // 优先级7：建造维修厂（已建车厂且无维修厂且资金充裕）
        // Attack策略跳过非关键建筑
        if (!skipNonEssentialBuildings && hasWarFactory && !HasBuilding(teamId, BuildingType.RepairPad)
            && _money[teamId] >= GetBuildingCost(BuildingType.RepairPad, teamId) + 200 && power >= 0
            && IsBuildingUnlockedByEra(teamId, BuildingType.RepairPad))
        {
            _money[teamId] -= GetBuildingCost(BuildingType.RepairPad, teamId);
            SpawnBuilding(BuildingType.RepairPad, GetAIBuildPosition(teamId, BuildingType.RepairPad), teamId);
            GameLog.Debug($"[AI] Team {teamId} built RepairPad, ${_money[teamId]} left");
            return;
        }

        // 优先级8：建造机枪塔（已建兵营，每阵营最多2座，资金充裕）
        // Attack策略跳过（资金留给造兵），Defend策略已在上方提前处理
        int turretCount = CountBuildingOfType(teamId, BuildingType.Turret);
        if (!skipNonEssentialBuildings && hasBarracks && turretCount < 2
            && _money[teamId] >= GetBuildingCost(BuildingType.Turret, teamId) + 300 && power >= 0
            && IsBuildingUnlockedByEra(teamId, BuildingType.Turret))
        {
            _money[teamId] -= GetBuildingCost(BuildingType.Turret, teamId);
            SpawnBuilding(BuildingType.Turret, GetAIBuildPosition(teamId, BuildingType.Turret), teamId);
            GameLog.Debug($"[AI] Team {teamId} built Turret #{turretCount + 1}, ${_money[teamId]} left");
            return;
        }

        // 优先级9：建造防空炮（已建车厂，每阵营最多2座）
        // Attack策略跳过
        int aaCount = CountBuildingOfType(teamId, BuildingType.AntiAirTurret);
        if (!skipNonEssentialBuildings && hasWarFactory && aaCount < 2
            && _money[teamId] >= GetBuildingCost(BuildingType.AntiAirTurret, teamId) + 300 && power >= 0
            && IsBuildingUnlockedByEra(teamId, BuildingType.AntiAirTurret))
        {
            _money[teamId] -= GetBuildingCost(BuildingType.AntiAirTurret, teamId);
            SpawnBuilding(BuildingType.AntiAirTurret, GetAIBuildPosition(teamId, BuildingType.AntiAirTurret), teamId);
            GameLog.Debug($"[AI] Team {teamId} built AntiAirTurret #{aaCount + 1}, ${_money[teamId]} left");
            return;
        }

        // E7：优先级10：建造机场（已建科技中心，每阵营最多1座）
        // Attack策略跳过
        if (!skipNonEssentialBuildings && hasTechCenter && !HasBuilding(teamId, BuildingType.Airfield)
            && _money[teamId] >= GetBuildingCost(BuildingType.Airfield, teamId) + 300 && power >= 0
            && IsBuildingUnlockedByEra(teamId, BuildingType.Airfield))
        {
            _money[teamId] -= GetBuildingCost(BuildingType.Airfield, teamId);
            SpawnBuilding(BuildingType.Airfield, GetAIBuildPosition(teamId, BuildingType.Airfield), teamId);
            GameLog.Debug($"[AI] Team {teamId} built Airfield, ${_money[teamId]} left");
            return;
        }
        // E9：优先级11：建造船厂（已建科技中心，每阵营最多1座）
        // Attack策略跳过
        if (!skipNonEssentialBuildings && hasTechCenter && !HasBuilding(teamId, BuildingType.Shipyard)
            && _money[teamId] >= GetBuildingCost(BuildingType.Shipyard, teamId) + 300 && power >= 0
            && IsBuildingUnlockedByEra(teamId, BuildingType.Shipyard))
        {
            _money[teamId] -= GetBuildingCost(BuildingType.Shipyard, teamId);
            SpawnBuilding(BuildingType.Shipyard, GetAIBuildPosition(teamId, BuildingType.Shipyard), teamId);
            GameLog.Debug($"[AI] Team {teamId} built Shipyard, ${_money[teamId]} left");
            return;
        }
        // E10：优先级12-14：超武建筑（已建科技中心）
        // Attack策略跳过（资金留给造兵加速进攻）
        if (!skipNonEssentialBuildings && hasTechCenter && !HasBuilding(teamId, BuildingType.NukeSilo)
            && _money[teamId] >= GetBuildingCost(BuildingType.NukeSilo, teamId) + 300 && power >= 0)
        {
            _money[teamId] -= GetBuildingCost(BuildingType.NukeSilo, teamId);
            SpawnBuilding(BuildingType.NukeSilo, GetAIBuildPosition(teamId, BuildingType.NukeSilo), teamId);
            GameLog.Debug($"[AI] Team {teamId} built NukeSilo, ${_money[teamId]} left");
            return;
        }
        if (!skipNonEssentialBuildings && hasTechCenter && !HasBuilding(teamId, BuildingType.LightningTower)
            && _money[teamId] >= GetBuildingCost(BuildingType.LightningTower, teamId) + 300 && power >= 0)
        {
            _money[teamId] -= GetBuildingCost(BuildingType.LightningTower, teamId);
            SpawnBuilding(BuildingType.LightningTower, GetAIBuildPosition(teamId, BuildingType.LightningTower), teamId);
            GameLog.Debug($"[AI] Team {teamId} built LightningTower, ${_money[teamId]} left");
            return;
        }
        if (!skipNonEssentialBuildings && hasTechCenter && !HasBuilding(teamId, BuildingType.MissileSilo)
            && _money[teamId] >= GetBuildingCost(BuildingType.MissileSilo, teamId) + 300 && power >= 0)
        {
            _money[teamId] -= GetBuildingCost(BuildingType.MissileSilo, teamId);
            SpawnBuilding(BuildingType.MissileSilo, GetAIBuildPosition(teamId, BuildingType.MissileSilo), teamId);
            GameLog.Debug($"[AI] Team {teamId} built MissileSilo, ${_money[teamId]} left");
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

        // 0a. 策略状态机更新（按策略检查间隔限流，不是每tick都检查）
        ref var stratState = ref _aiStrategyStates[teamId];
        stratState.StrategyCooldown -= 1; // 每个 tick 递减
        if (stratState.StrategyCooldown <= 0)
        {
            stratState.StrategyCooldown = GetStrategyCheckInterval() / _aiThinkInterval;
            UpdateAIStrategy(teamId);
        }

        // 0b. Attack策略时执行集结逻辑
        if (stratState.Strategy == AIStrategy.Attack)
            AIAssaultRally(teamId);

        // 0. 建筑建造优先（根据策略动态调整优先级）
        AIBuildLogic(teamId, stratState.Strategy);

        bool savingForTech = HasBuilding(teamId, BuildingType.WarFactory) && !HasBuilding(teamId, BuildingType.TechCenter);

        // 1. 自动造兵（P2-10修复：复用提取的公共方法），根据策略调整造兵倾向
        if (!savingForTech)
            AITrainUnits(teamId, stratState.Strategy);

        // 5. G7: AI间谍任务 — 每20秒尝试一次（移到生产逻辑外部，确保不受savingForTech/无单位影响）
        if (!_aiSpyCooldowns.TryGetValue(teamId, out int spyCd))
            _aiSpyCooldowns[teamId] = 0;
        _aiSpyCooldowns[teamId]--;
        if (_aiSpyCooldowns[teamId] <= 0)
        {
            _aiSpyCooldowns[teamId] = 20;
            AISpyMission(teamId);
        }

        // 2. 矿车耗损自动补充（P2-10修复：复用提取的公共方法）
        AIReplenishHarvesters(teamId);

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
                _aiNukeCooldowns[teamId] = GameConst.NukeCooldown;

            if (_aiNukeCooldowns[teamId] <= 0f)
            {
                var target = FindNukeTargetForAi(teamId);
                if (target.HasValue)
                {
                    LaunchNukeWithAnimation(target.Value, teamId);
                    _aiNukeCooldowns[teamId] = GameConst.NukeCooldown;
                }
            }
        }

        if (HasBuilding(teamId, BuildingType.LightningTower))
        {
            if (!_aiLightningCooldowns.ContainsKey(teamId))
                _aiLightningCooldowns[teamId] = GameConst.LightningCooldown;

            if (_aiLightningCooldowns[teamId] <= 0f)
            {
                var target = FindNukeTargetForAi(teamId);
                if (target.HasValue)
                {
                    LaunchLightningWithAnimation(target.Value, teamId);
                    _aiLightningCooldowns[teamId] = GameConst.LightningCooldown;
                }
            }
        }

        // E10：AI巡航导弹
        if (HasBuilding(teamId, BuildingType.MissileSilo))
        {
            if (!_aiMissileCooldowns.ContainsKey(teamId))
                _aiMissileCooldowns[teamId] = GameConst.MissileCooldown;

            if (_aiMissileCooldowns[teamId] <= 0f)
            {
                var target = FindNukeTargetForAi(teamId);
                if (target.HasValue)
                {
                    ApplyCruiseMissile(target.Value, teamId);
                    _aiMissileCooldowns[teamId] = GameConst.MissileCooldown;
                }
            }
        }
        }

    /// <summary>
    /// P2-10修复：提取AI造兵逻辑为公共方法，消除AITickForTeam和BlueTestAITick的重复代码。
    /// 构建可生产的单位列表 → 按造价降序 → 概率优先填线兵 → 遍历排队。
    /// 策略影响：Defend时步兵优先50%，Attack时重装甲优先，Expand时矿车优先。
    /// </summary>
    private void AITrainUnits(int teamId, AIStrategy strategy = AIStrategy.Expand)
    {
        // 造兵（检查建筑前置 + 电力）
        int teamUnits = CountUnitsOfTeam(teamId);
        int teamQueued = CountQueuedUnitsOfTeam(teamId);
        // === 策略影响：Expand策略降低造兵优先级，攒钱发展经济 ===
        int effectiveCap = (strategy == AIStrategy.Expand) ? Mathf.Max(4, _unitCap - 4) : _unitCap;
        if (teamUnits + teamQueued >= effectiveCap || GetTeamPower(teamId) < 0) return;

        // 有科技中心时攒钱优先造高级兵种
        bool hasTech = HasBuilding(teamId, BuildingType.TechCenter);
        // Attack策略不攒钱，有多少钱造多少兵
        if (strategy != AIStrategy.Attack && hasTech && _money[teamId] < GetUnitCost(UnitType.RocketLauncher, teamId) && teamUnits >= 3) return;

        var types = new List<UnitType>();
        if (HasBuilding(teamId, BuildingType.Barracks))
        {
            types.Add(UnitType.LightTank);
            types.Add(UnitType.Infantry);
            types.Add(UnitType.Grenadier);
            types.Add(UnitType.FlameInfantry);
            types.Add(UnitType.Sniper);
            types.Add(UnitType.Thief);
            types.Add(UnitType.RocketInfantry);
            types.Add(UnitType.Fighter);
            types.Add(UnitType.Helicopter);
            types.Add(UnitType.Bomber);
            types.Add(UnitType.Scout);
            types.Add(UnitType.TransportHeli);
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
            types.Add(UnitType.Transport);
            types.Add(UnitType.Hero);
            types.Add(UnitType.Spy);
            types.Add(UnitType.Thief);
        }
        if (hasTech)
        {
            types.Add(UnitType.RocketLauncher);
            types.Add(UnitType.MissileTank);
        }
        if (types.Count == 0) return;

        types.Sort((a, b) => GetUnitCost(b, teamId).CompareTo(GetUnitCost(a, teamId)));
        // === 策略影响：不同策略调整造兵概率 ===
        float infantryChance = 0.35f;
        float engineerChance = 0.15f;
        if (strategy == AIStrategy.Defend)
        {
            // 防御时步兵填线优先
            infantryChance = 0.50f;
        }
        else if (strategy == AIStrategy.Attack)
        {
            // 进攻时重装甲优先，少造步兵
            infantryChance = 0.15f;
            // 重坦克优先排到前面
            if (types.Contains(UnitType.HeavyTank))
            {
                types.Remove(UnitType.HeavyTank);
                types.Insert(0, UnitType.HeavyTank);
            }
        }
        // 步兵作为廉价填线兵
        if (types.Contains(UnitType.Infantry) && DeterministicRng.Randf() < infantryChance)
        {
            types.Remove(UnitType.Infantry);
            types.Insert(0, UnitType.Infantry);
        }
        // 工程车
        if (types.Contains(UnitType.Engineer) && DeterministicRng.Randf() < engineerChance)
        {
            types.Remove(UnitType.Engineer);
            types.Insert(0, UnitType.Engineer);
        }
        foreach (var t in types)
        {
            int c = GetUnitCost(t, teamId);
            if (_money[teamId] >= c)
            {
                var producer = FindProducerForUnit(t, teamId);
                if (producer != null)
                {
                    _money[teamId] -= c;
                    producer.EnqueueProduction(UnitTypeToProductionType(t));
                    GameLog.Debug($"[AI] Team {teamId} queued {t}, ${_money[teamId]} left, {producer.BuildingName} queue {producer.QueueCount}");
                    break;
                }
            }
        }
    }

    /// <summary>
    /// P2-10修复：提取AI矿车补充逻辑为公共方法。
    /// </summary>
    private void AIReplenishHarvesters(int teamId)
    {
        int teamHarvesters = CountHarvestersOfTeam(teamId);
        int harvesterCost = GetUnitCost(UnitType.Harvester, teamId);
        if (_money[teamId] >= harvesterCost && teamHarvesters < 3)
        {
            var harvProducer = FindProducerBuilding(BuildingType.Base, teamId);
            if (harvProducer != null)
            {
                _money[teamId] -= harvesterCost;
                harvProducer.EnqueueProduction(ProductionType.Harvester);
            }
        }
    }

    /// <summary>蓝方测试 AI：模拟玩家自动造兵（仅 headless 模式）。
    /// P2-10修复：使用提取的AITrainUnits/AIReplenishHarvesters方法，消除重复代码。</summary>
    private void BlueTestAITick()
    {
        // G4：自动维修血量低于50%的蓝方建筑
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b.TeamId == PlayerTeamId && IsInstanceValid(b)
                && b.NeedsRepair && b.Health < b.MaxHealth * 0.5f)
            {
                int cost = GetRepairCost(b);
                if (_money[PlayerTeamId] >= cost)
                {
                    _money[PlayerTeamId] -= cost;
                    b.Repair();
                    GameLog.Debug($"[BlueAI] repaired {b.BuildingName}, cost ${cost}, balance ${_money[PlayerTeamId]}");
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
                AITryCaptureStrategicPoint(PlayerTeamId);
            }
        }

        // 先建建筑
        AIBuildLogic(PlayerTeamId);

        bool savingForTech = HasBuilding(PlayerTeamId, BuildingType.WarFactory) && !HasBuilding(PlayerTeamId, BuildingType.TechCenter);
        if (savingForTech) return;

        // 补矿车 + 造兵（复用提取的公共方法）
        AIReplenishHarvesters(PlayerTeamId);
        AITrainUnits(PlayerTeamId);
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
                GameLog.Debug($"[AI] Team {teamId} sending unit to capture point at {sp.GlobalPosition} (dist {(int)nearestDist})");
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
                GameLog.Debug($"[AI] Team {teamId} sending unit to capture oil field at {rn.GlobalPosition} (dist {(int)nearestDist})");
                return;
            }
        }
    }

    // ---------- 场景生成辅助 ----------
    private Unit SpawnUnit(UnitType type, Vector2 pos, int teamId, bool autoAI)
    {
        var u = _unitScene.Instantiate<Unit>();
        u.InitAsType(type);
        u.GlobalPosition = pos;
        u.TeamId = teamId;
        u.AutoAI = autoAI;
        // P1-2: 应用阵营数值乘数
        u.ApplyFactionMultipliers(teamId);
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
        // P1-2: 应用阵营数值乘数
        b.ApplyFactionMultipliers(teamId);
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
            GameLog.Debug($"[Production] {producer.BuildingName} (Team {teamId}) produced harvester");
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
            GameLog.Debug($"[Production] {producer.BuildingName} (Team {teamId}) produced {unitType}");
        }

        // 阶段12-C：玩家方生产完成音效
        if (teamId == PlayerTeamId)
            _audio?.PlaySfx(AudioManager.Sfx.UiUnitReady);
        // 补强：建筑建造完成音效
        if (teamId == PlayerTeamId)
            _audio?.PlaySfx(AudioManager.Sfx.UiBuildDone);
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
            UnitType.ApocalypseTank => BuildingType.WarFactory,
            UnitType.PrismTank => BuildingType.WarFactory,
            UnitType.KirovAirship => BuildingType.Airfield,
            UnitType.TeslaTrooper => BuildingType.Barracks,
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
        UnitType.ApocalypseTank => ProductionType.ApocalypseTank,
        UnitType.PrismTank => ProductionType.PrismTank,
        UnitType.KirovAirship => ProductionType.KirovAirship,
        UnitType.TeslaTrooper => ProductionType.TeslaTrooper,
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
        ProductionType.ApocalypseTank => UnitType.ApocalypseTank,
        ProductionType.PrismTank => UnitType.PrismTank,
        ProductionType.KirovAirship => UnitType.KirovAirship,
        ProductionType.TeslaTrooper => UnitType.TeslaTrooper,
        _ => UnitType.Default
    };

    /// <summary>获取选中的蓝方生产建筑（兵营/车厂/科技中心/基地），用于设置集结点。</summary>
    private Building? GetSelectedFriendlyProducerBuilding()
    {
        foreach (var o in _selected)
        {
            if (o is Building b && b.TeamId == PlayerTeamId && IsInstanceValid(b)
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

    /// <summary>将坐标限制在地图范围内（距边缘至少 margin 像素）。</summary>
    private static Vector2 ClampToMap(Vector2 pos, float margin)
    {
        return new Vector2(
            Mathf.Clamp(pos.X, margin, MapSize - margin),
            Mathf.Clamp(pos.Y, margin, MapSize - margin));
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

    // ---------- 外部 API ----------
    public void AddResourceForTeam(int teamId, int amount)
    {
        if (teamId >= 0 && teamId < _money.Length)
            _money[teamId] += amount;
    }

    /// <summary>获取指定阵营当前资金。</summary>
    public int GetMoney(int teamId)
    {
        if (teamId >= 0 && teamId < _money.Length)
            return _money[teamId];
        return 0;
    }

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
}
