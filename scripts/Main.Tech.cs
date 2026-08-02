using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RTSGame;

/// <summary>
/// Main 的科技/时代/战术卡/电网/尤里卡/邻接/间谍/占领控制器（partial class）。
/// 包含 G1~G8 八个子系统的逻辑方法，已从 Main.cs 拆分。
/// </summary>
public partial class Main
{
    /// <summary>玩家尝试研究指定索引的科技。</summary>
    private void TryResearchTech(int techNum)
    {
        if (techNum >= TechOrder.Length) return;
        var techId = TechOrder[techNum];
        int pId = PlayerTeamId;
        var tp = _techProgress[pId];
        var node = TechTree.Nodes[techId];
        bool hasTech = HasBuilding(pId, BuildingType.TechCenter) || !node.RequiresTechCenter;

        if (tp.Completed.Contains(techId))
        {
            GameLog.Debug($"[G1] {node.Name} 已研究完成");
            return;
        }
        if (tp.CurrentlyResearching.HasValue)
        {
            GameLog.Debug($"[G1] 正在研究中: {TechTree.Nodes[tp.CurrentlyResearching.Value].Name} ({tp.Progress*100:F0}%)");
            return;
        }
        if (!TechTree.CanResearch(tp.Completed, techId, hasTech, _money[pId], FactionManager.GetFactionForTeam(pId).Id))
        {
            if (!hasTech) GameLog.Debug($"[G1] {node.Name} 需要科技中心");
            else if (_money[pId] < node.Cost) GameLog.Debug($"[G1] 资金不足: {node.Name} 需要${node.Cost}，当前${_money[pId]}");
            else GameLog.Debug($"[G1] {node.Name} 需要前置科技");
            return;
        }
        _money[pId] -= node.Cost;
        tp.StartResearch(techId);
        ReplayRecorder.Record(ReplayRecorder.ActionType.ResearchTech, new { TechId = techId.ToString() });
        GameLog.Debug($"[G1] 开始研究: {node.Name} (成本${node.Cost}，{node.ResearchTime:F0}秒) — 资金剩余${_money[pId]}");
        ShowToast(TrManager.Tr("tech.toast_research_started", node.Name));
    }

    /// <summary>更新科技树面板显示文本。</summary>
    private void UpdateTechTreePanel()
    {
        int pId = PlayerTeamId;
        var tp = _techProgress[pId];
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(TrManager.Tr("tech.tree_title"));
        sb.AppendLine(TrManager.Tr("tech.tree_header",
            _money[pId],
            HasBuilding(pId, BuildingType.TechCenter) ? TrManager.Tr("tech.has_yes") : TrManager.Tr("tech.has_no"),
            tp.CurrentlyResearching.HasValue
                ? TrManager.Tr("tech.researching", TechTree.Nodes[tp.CurrentlyResearching.Value].Name, $"{tp.Progress*100:F0}")
                : TrManager.Tr("tech.has_no")));
        sb.AppendLine();

        string[] branches = { TrManager.Tr("tech.branch_military"), TrManager.Tr("tech.branch_economy"), TrManager.Tr("tech.branch_defense") };
        int techIdx = 0;
        foreach (var branch in branches)
        {
            sb.AppendLine(TrManager.Tr("tech.branch_header", branch));
            for (int tier = 1; tier <= 4; tier++)
            {
                var node = TechTree.GetByBranchTier(branch, tier);
                if (node == null) continue;
                bool done = tp.Completed.Contains(node.Id);
                bool researching = tp.CurrentlyResearching == node.Id;
                bool available = TechTree.CanResearch(tp.Completed, node.Id, HasBuilding(pId, BuildingType.TechCenter) || !node.RequiresTechCenter, _money[pId], FactionManager.GetFactionForTeam(pId).Id);
                string status = done ? TrManager.Tr("tech.status_done") : researching ? TrManager.Tr("tech.status_researching", $"{tp.Progress*100:F0}") : available ? TrManager.Tr("tech.status_available") : TrManager.Tr("tech.status_locked");
                string keyHint = done ? "  " : $"({techIdx})";
                sb.AppendLine(TrManager.Tr("tech.node_line", keyHint, tier, node.Name, status, node.Cost, $"{node.ResearchTime:F0}"));
                sb.AppendLine(TrManager.Tr("tech.node_desc", node.Description));
                techIdx++;
            }
            sb.AppendLine();
        }
        sb.AppendLine(TrManager.Tr("tech.hint_hotkey"));
        _techTreeLabel.Text = sb.ToString();
    }

    /// <summary>G1: 更新所有阵营的科技研究进度（每帧调用）。</summary>
    private void UpdateTechResearch(float dt)
    {
        // G3: 战术卡研究速度加成 + G6: 邻接加成研究速度
        float playerResearchMul = GetCardResearchSpeedMul(PlayerTeamId) * GetAdjacencyResearchMul(PlayerTeamId);
        // 玩家阵营
        var completed = _techProgress[PlayerTeamId].UpdateResearch(dt * playerResearchMul);
        if (completed.HasValue)
        {
            var node = TechTree.Nodes[completed.Value];
            GameLog.Debug($"[G1] 科技研究完成: {node.Name} — {node.Description}");
            ShowToast(TrManager.Tr("tech.toast_research_done", node.Name));
            ApplyTechEffects(PlayerTeamId);
            // 补强：科技解锁音效
            PlayTechUnlockSfx();
            if (_techTreePanelVisible) UpdateTechTreePanel();
        }

        // AI阵营：自动研究逻辑
        _aiTechTimer -= dt;
        if (_aiTechTimer <= 0f)
        {
            _aiTechTimer = 5f; // 每5秒AI检查一次
            for (int team = 1; team < TotalTeamCount; team++)
            {
                if (team > _activeAiCount) break; // 休眠AI不研究
                var aiTp = _techProgress[team];
                if (aiTp.CurrentlyResearching.HasValue) continue;
                bool aiHasTech = HasBuilding(team, BuildingType.TechCenter);
                // AI优先经济分支第一层（不需要科技中心）
                var target = TechTree.TechId.Eco_MiningEfficiency;
                if (aiTp.Completed.Contains(TechTree.TechId.Eco_MiningEfficiency))
                    target = TechTree.TechId.Def_Fortification;
                if (aiTp.Completed.Contains(TechTree.TechId.Def_Fortification))
                    target = TechTree.TechId.Mil_ArmorUpgrade;
                if (aiTp.Completed.Contains(TechTree.TechId.Mil_ArmorUpgrade) && aiHasTech)
                    target = TechTree.TechId.Eco_MassProduction;
                if (aiTp.Completed.Contains(TechTree.TechId.Eco_MassProduction))
                    target = TechTree.TechId.Mil_AmmoUpgrade;

                var node = TechTree.Nodes[target];
                if (aiTp.Completed.Contains(target)) continue;
                bool hasReq = aiHasTech || !node.RequiresTechCenter;
                if (!hasReq) continue;
                // 检查前置
                bool preOk = true;
                foreach (var pre in node.Prerequisites)
                    if (!aiTp.Completed.Contains(pre)) { preOk = false; break; }
                if (!preOk) continue;
                if (_money[team] >= node.Cost * 2) // AI保留一倍资金用于造兵
                {
                    _money[team] -= node.Cost;
                    aiTp.StartResearch(target);
                    GameLog.Debug($"[G1] AI Team {team} 开始研究: {node.Name}");
                }
            }
        }

        // AI研究完成处理
        for (int team = 1; team < TotalTeamCount; team++)
        {
            float aiResearchMul = GetCardResearchSpeedMul(team) * GetAdjacencyResearchMul(team);
            var aiCompleted = _techProgress[team].UpdateResearch(dt * aiResearchMul);
            if (aiCompleted.HasValue)
            {
                var node = TechTree.Nodes[aiCompleted.Value];
                GameLog.Debug($"[G1] AI Team {team} 科技完成: {node.Name}");
                ApplyTechEffects(team);
            }
        }
    }

    /// <summary>应用科技效果到指定阵营（研究完成时调用）。</summary>
    private void ApplyTechEffects(int teamId)
    {
        var tp = _techProgress[teamId];
        // 应用效果到现有单位
        foreach (var c in _unitsNode.GetChildren())
        {
            if (c is not Unit u || u.TeamId != teamId || !IsInstanceValid(u)) continue;
            ApplyTechToUnit(u, tp.Completed);
        }
        // 应用效果到现有建筑
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is not Building b || b.TeamId != teamId || !IsInstanceValid(b)) continue;
            ApplyTechToBuilding(b, tp.Completed);
        }
    }

    /// <summary>合并应用G1科技+G2时代+G3战术卡的所有效果到单个新单位。</summary>
    private void ApplyAllModifiersToUnit(Unit u, int teamId)
    {
        // G1: 科技效果
        var tp = _techProgress[teamId];
        if (tp != null) ApplyTechToUnit(u, tp.Completed);

        // G2: 时代效果
        var ep = _eraProgress[teamId];
        if (ep != null)
        {
            float healthMul = EraSystem.GetHealthMultiplier(ep.CurrentEra);
            float damageMul = EraSystem.GetDamageMultiplier(ep.CurrentEra);
            if (healthMul != 1f) u.ApplyTechHealthMultiplier(healthMul);
            if (damageMul != 1f) u.ApplyTechDamageMultiplier(damageMul);
        }

        // G3: 战术卡效果
        TacticalCards.CardId? card = GetCardForTeam(teamId);
        if (card.HasValue)
        {
            float allHealth = TacticalCards.GetAllHealthMul(card);
            float allDamage = TacticalCards.GetAllDamageMul(card);
            if (IsTankType(u.Type))
            {
                allHealth *= TacticalCards.GetTankHealthMul(card);
                allDamage *= TacticalCards.GetTankDamageMul(card);
            }
            if (IsInfantryType(u.Type))
            {
                allHealth *= TacticalCards.GetInfantryHealthMul(card);
            }
            if (allHealth != 1f) u.ApplyTechHealthMultiplier(allHealth);
            if (allDamage != 1f) u.ApplyTechDamageMultiplier(allDamage);
            // S2修复: G3战术卡移动速度加成（闪击战术+15%移速）
            float moveSpeedMul = TacticalCards.GetMoveSpeedMul(card);
            if (moveSpeedMul != 1f) u.ApplyTechMoveSpeedMultiplier(moveSpeedMul);
        }
    }

    /// <summary>将科技效果应用到单个单位。</summary>
    private void ApplyTechToUnit(Unit u, HashSet<TechTree.TechId> tech)
    {
        // Mil_ArmorUpgrade: 坦克血量+15%
        if (tech.Contains(TechTree.TechId.Mil_ArmorUpgrade) && IsTankType(u.Type))
        {
            // 通过MaxHealth倍率临时增加，使用比例方式
            float ratio = u.Health / u.MaxHealth;
            u.ApplyTechHealthMultiplier(1.15f);
            u.SetHealth(u.MaxHealth * ratio);
        }
        // Mil_AmmoUpgrade: 攻击力+15%
        if (tech.Contains(TechTree.TechId.Mil_AmmoUpgrade))
        {
            u.ApplyTechDamageMultiplier(1.15f);
        }
        // Mil_HeroTraining: 英雄成本-30%（通过成本折扣处理，这里不做运行时修改）

        // === P0修复: 阵营专属科技效果实现 ===
        // Fac_AirSuperiority（同盟军）: 空军伤害+15%
        if (tech.Contains(TechTree.TechId.Fac_AirSuperiority) && u.IsAirUnit)
        {
            u.ApplyTechDamageMultiplier(1.15f);
        }
        // Fac_HeavyArmor（苏维埃）: 坦克生命+15%
        if (tech.Contains(TechTree.TechId.Fac_HeavyArmor) && IsTankType(u.Type))
        {
            float ratio = u.Health / u.MaxHealth;
            u.ApplyTechHealthMultiplier(1.15f);
            u.SetHealth(u.MaxHealth * ratio);
        }
    }

    /// <summary>将科技效果应用到单个建筑。</summary>
    private void ApplyTechToBuilding(Building b, HashSet<TechTree.TechId> tech)
    {
        // Def_Fortification: 建筑血量+25%
        if (tech.Contains(TechTree.TechId.Def_Fortification))
        {
            float ratio = b.Health / b.MaxHealth;
            b.ApplyTechHealthMultiplier(1.25f);
            b.SetHealth(b.MaxHealth * ratio);
        }
        // Def_PowerGrid: 电站+50%发电
        if (tech.Contains(TechTree.TechId.Def_PowerGrid) && b.Type == BuildingType.PowerPlant)
        {
            b.ApplyTechPowerMultiplier(1.5f);
        }
        // === P0修复: 阵营专属科技效果实现 ===
        // Fac_NuclearPower（苏维埃）: 电站发电+50%
        if (tech.Contains(TechTree.TechId.Fac_NuclearPower) && b.Type == BuildingType.PowerPlant)
        {
            b.ApplyTechPowerMultiplier(1.5f);
        }
    }

    /// <summary>判断单位是否为坦克类（M1修复: 排除非战斗车辆）。</summary>
    private static bool IsTankType(UnitType type) => type switch
    {
        UnitType.LightTank or UnitType.HeavyTank or UnitType.Artillery
        or UnitType.RocketLauncher or UnitType.MissileTank or UnitType.AntiAir => true,
        _ => false,
    };

    /// <summary>获取科技带来的单位成本折扣（0~1，1=无折扣）。</summary>
    public float GetTechCostMultiplier(int teamId)
    {
        var tp = _techProgress[teamId];
        if (tp == null) return 1f;
        return tp.Completed.Contains(TechTree.TechId.Eco_MassProduction) ? 0.85f : 1f;
    }

    /// <summary>获取科技带来的矿车采集速度乘数。</summary>
    public float GetTechMiningMultiplier(int teamId)
    {
        var tp = _techProgress[teamId];
        if (tp == null) return 1f;
        return tp.Completed.Contains(TechTree.TechId.Eco_MiningEfficiency) ? 1.3f : 1f;
    }

    /// <summary>获取科技带来的单位上限加成。</summary>
    public int GetTechUnitCapBonus(int teamId)
    {
        var tp = _techProgress[teamId];
        if (tp == null) return 0;
        return tp.Completed.Contains(TechTree.TechId.Eco_AdvancedLogistics) ? 8 : 0;
    }

    /// <summary>获取科技带来的战略点收入乘数。</summary>
    public float GetTechStratPointMultiplier(int teamId)
    {
        var tp = _techProgress[teamId];
        if (tp == null) return 1f;
        return tp.Completed.Contains(TechTree.TechId.Eco_ResourceNetwork) ? 2f : 1f;
    }

    /// <summary>建筑是否有自动维修科技。</summary>
    public bool HasTechAutoRepair(int teamId)
    {
        var tp = _techProgress[teamId];
        return tp != null && tp.Completed.Contains(TechTree.TechId.Def_RepairSystems);
    }

    /// <summary>防御建筑是否有高级炮塔科技加成。</summary>
    public bool HasTechAdvancedTurrets(int teamId)
    {
        var tp = _techProgress[teamId];
        return tp != null && tp.Completed.Contains(TechTree.TechId.Def_AdvancedTurrets);
    }

    /// <summary>P0修复: 阵营专属科技 — Fac_NavalSupport（同盟军）: 海军建筑生产速度+20%。</summary>
    public float GetTechNavalProduceMul(int teamId)
    {
        var tp = _techProgress[teamId];
        if (tp == null) return 1f;
        return tp.Completed.Contains(TechTree.TechId.Fac_NavalSupport) ? 1.2f : 1f;
    }

    /// <summary>P0修复: 阵营专属科技 — Fac_MindControl（尤里）: 间谍/窃贼效率+30%。
    /// 体现为窃取资金量+30%和间谍任务成功率+15%。</summary>
    public float GetTechSpyEfficiencyMul(int teamId)
    {
        var tp = _techProgress[teamId];
        if (tp == null) return 1f;
        return tp.Completed.Contains(TechTree.TechId.Fac_MindControl) ? 1.3f : 1f;
    }

    /// <summary>P0修复: 阵营专属科技 — Fac_StealthOps（尤里）: 间谍渗透时间-30%（隐身能力增强）。</summary>
    public float GetTechStealthInfiltrateMul(int teamId)
    {
        var tp = _techProgress[teamId];
        if (tp == null) return 1f;
        return tp.Completed.Contains(TechTree.TechId.Fac_StealthOps) ? 0.7f : 1f;
    }

    // ======== G2: 时代系统方法 ========

    /// <summary>玩家尝试升级时代。</summary>
    private void TryAdvanceEra()
    {
        int pId = PlayerTeamId;
        var ep = _eraProgress[pId];
        if (ep.IsUpgrading)
        {
            GameLog.Debug($"[G2] 时代升级进行中... {ep.Progress*100:F0}%");
            return;
        }
        var next = EraSystem.GetNextEra(ep.CurrentEra);
        if (next == null)
        {
            GameLog.Debug("[G2] 已达到最高时代（信息时代）");
            return;
        }
        if (!EraSystem.CanAdvance(ep.CurrentEra, t => HasBuilding(pId, t), _money[pId]))
        {
            if (_money[pId] < next.UpgradeCost)
                GameLog.Debug($"[G2] 资金不足：升级到{next.Name}需要${next.UpgradeCost}，当前${_money[pId]}");
            else
            {
                string missing = "";
                foreach (var req in next.RequiredBuildings)
                    if (!HasBuilding(pId, req)) missing += $" {req}";
                GameLog.Debug($"[G2] 缺少前置建筑：{missing}");
            }
            return;
        }
        _money[pId] -= next.UpgradeCost;
        ep.StartUpgrade();
        ReplayRecorder.Record(ReplayRecorder.ActionType.AdvanceEra, new { FromEra = ep.CurrentEra.ToString() });
        GameLog.Debug($"[G2] 开始时代升级：{EraSystem.Eras[(int)ep.CurrentEra].Name} → {next.Name} (成本${next.UpgradeCost}，{next.UpgradeTime:F0}秒)");
        ShowToast(TrManager.Tr("tech.toast_era_upgrading", next.Name));
    }

    /// <summary>更新时代面板显示。</summary>
    private void UpdateEraPanel()
    {
        int pId = PlayerTeamId;
        var ep = _eraProgress[pId];
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(TrManager.Tr("era.panel_title"));
        sb.AppendLine(TrManager.Tr("era.panel_header",
            EraSystem.Eras[(int)ep.CurrentEra].Name, _money[pId]));
        if (ep.IsUpgrading)
        {
            var next = EraSystem.GetNextEra(ep.CurrentEra);
            sb.AppendLine(TrManager.Tr("era.panel_upgrading", next?.Name ?? "", $"{ep.Progress*100:F0}"));
        }
        sb.AppendLine();

        for (int i = 0; i < EraSystem.Eras.Length; i++)
        {
            var era = EraSystem.Eras[i];
            string marker = era.Id == ep.CurrentEra ? "▶" : (int)era.Id < (int)ep.CurrentEra ? "✓" : " ";
            string status = era.Id == ep.CurrentEra ? TrManager.Tr("era.status_current") : (int)era.Id < (int)ep.CurrentEra ? TrManager.Tr("era.status_done") : "";
            sb.AppendLine(TrManager.Tr("era.era_line", marker, era.Name, status));
            sb.AppendLine(TrManager.Tr("era.era_desc", era.Description));
            if ((int)era.Id == (int)ep.CurrentEra + 1 && !ep.IsUpgrading)
            {
                bool canAdv = EraSystem.CanAdvance(ep.CurrentEra, t => HasBuilding(pId, t), _money[pId]);
                string reqStr = era.RequiredBuildings.Length > 0
                    ? string.Join("/", System.Array.ConvertAll(era.RequiredBuildings, b => b.ToString()))
                    : TrManager.Tr("era.no_req");
                sb.AppendLine(TrManager.Tr("era.upgrade_cost", era.UpgradeCost, reqStr, $"{era.UpgradeTime:F0}"));
                sb.AppendLine(TrManager.Tr("era.upgrade_status", canAdv ? TrManager.Tr("era.can_advance") : TrManager.Tr("era.cannot_advance")));
            }
            sb.AppendLine();
        }

        sb.AppendLine(TrManager.Tr("era.bonus_summary"));
        sb.AppendLine(TrManager.Tr("era.hint_hotkey"));
        _eraLabel.Text = sb.ToString();
    }

    /// <summary>G2: 更新所有阵营的时代升级进度（每帧调用）。</summary>
    private void UpdateEraProgress(float dt)
    {
        // 玩家阵营
        var ep = _eraProgress[PlayerTeamId];
        float eraUpgradeMul = TacticalCards.GetEraUpgradeSpeedMul(GetCardForTeam(PlayerTeamId));
        if (ep.UpdateUpgrade(dt * eraUpgradeMul))
        {
            var eraInfo = EraSystem.Eras[(int)ep.CurrentEra];
            GameLog.Debug($"[G2] 时代升级完成: {eraInfo.Name} — {eraInfo.Description}");
            ShowToast(TrManager.Tr("era.toast_entered", eraInfo.Name));
            ApplyEraEffects(PlayerTeamId);
            if (_eraPanelVisible) UpdateEraPanel();
        }

        // AI阵营：自动升级
        _aiEraTimer -= dt;
        if (_aiEraTimer <= 0f)
        {
            _aiEraTimer = 8f; // 每8秒AI检查一次
            for (int team = 1; team < TotalTeamCount; team++)
            {
                if (team > _activeAiCount) break;
                var aiEp = _eraProgress[team];
                if (aiEp.IsUpgrading) continue;
                var next = EraSystem.GetNextEra(aiEp.CurrentEra);
                if (next == null) continue;
                // AI保留2倍资金用于造兵
                if (_money[team] >= next.UpgradeCost * 2)
                {
                    bool reqOk = true;
                    foreach (var req in next.RequiredBuildings)
                        if (!HasBuilding(team, req)) { reqOk = false; break; }
                    if (reqOk)
                    {
                        _money[team] -= next.UpgradeCost;
                        aiEp.StartUpgrade();
                        GameLog.Debug($"[G2] AI Team {team} 开始时代升级: → {next.Name}");
                    }
                }
            }
        }

        // AI时代升级完成处理
        for (int team = 1; team < TotalTeamCount; team++)
        {
            var aiEp = _eraProgress[team];
            float aiEraMul = TacticalCards.GetEraUpgradeSpeedMul(_aiCards[team - 1]);
            if (aiEp.UpdateUpgrade(dt * aiEraMul))
            {
                var eraInfo = EraSystem.Eras[(int)aiEp.CurrentEra];
                GameLog.Debug($"[G2] AI Team {team} 进入{eraInfo.Name}");
                ApplyEraEffects(team);
            }
        }
    }

    /// <summary>应用时代效果到指定阵营所有单位/建筑（时代升级完成时调用）。</summary>
    private void ApplyEraEffects(int teamId)
    {
        var ep = _eraProgress[teamId];
        float healthMul = EraSystem.GetHealthMultiplier(ep.CurrentEra);
        float damageMul = EraSystem.GetDamageMultiplier(ep.CurrentEra);
        foreach (var c in _unitsNode.GetChildren())
        {
            if (c is not Unit u || u.TeamId != teamId || !IsInstanceValid(u)) continue;
            u.ApplyTechHealthMultiplier(healthMul);
            u.ApplyTechDamageMultiplier(damageMul);
        }
    }

    /// <summary>获取阵营当前时代。</summary>
    public EraSystem.Era GetTeamEra(int teamId)
    {
        if (teamId < 0 || teamId >= _eraProgress.Length) return EraSystem.Era.Stone;
        return _eraProgress[teamId].CurrentEra;
    }

    /// <summary>获取时代的矿车采集速度乘数（G1科技+G2时代叠加）。</summary>
    public float GetEraMiningMultiplier(int teamId)
    {
        var ep = _eraProgress[teamId];
        return ep != null ? EraSystem.GetMiningMultiplier(ep.CurrentEra) : 1f;
    }

    /// <summary>获取时代的建造速度乘数。</summary>
    public float GetEraBuildSpeedMultiplier(int teamId)
    {
        var ep = _eraProgress[teamId];
        return ep != null ? EraSystem.GetBuildSpeedMultiplier(ep.CurrentEra) : 1f;
    }

    /// <summary>检查指定建筑类型在某阵营当前时代是否可建造。</summary>
    public bool IsBuildingUnlockedByEra(int teamId, BuildingType type)
    {
        var ep = _eraProgress[teamId];
        if (ep == null) return true; // 时代系统未初始化时不限制
        return EraSystem.CanBuildBuilding(ep.CurrentEra, type);
    }

    /// <summary>检查指定单位类型在某阵营当前时代是否可生产。</summary>
    public bool IsUnitUnlockedByEra(int teamId, UnitType type)
    {
        var ep = _eraProgress[teamId];
        if (ep == null) return true;
        return EraSystem.CanProduceUnit(ep.CurrentEra, type);
    }

    // ======== G3: 战术卡系统方法 ========

    /// <summary>显示战术卡选择面板（带可点击卡片按钮）。</summary>
    private void ShowCardSelection()
    {
        _cardSelectionPending = false;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _cardChoices = TacticalCards.DrawRandom(3, rng);

        // 清除旧按钮
        foreach (var child in _cardButtonContainer.GetChildren())
            child.QueueFree();

        // 为每张卡创建结构化卡片面板（图标区 + 名称 + 描述 + 编号）
        for (int i = 0; i < _cardChoices.Length; i++)
        {
            var card = TacticalCards.Cards[_cardChoices[i]];
            int cardIndex = i; // 闭包捕获

            // 用 Button 作为可点击容器（保留点击交互），但不用 Text
            var cardButton = new Button
            {
                CustomMinimumSize = new Vector2(210, 320),
                ClipText = false,
                Text = "",
            };
            cardButton.MouseFilter = Control.MouseFilterEnum.Stop;

            // 军工风深色背景样式（三态）
            static StyleBoxFlat MakeCardStyle(Color bg, Color border, bool pressed = false)
            {
                var s = new StyleBoxFlat();
                s.BgColor = bg;
                s.BorderWidthLeft = 2; s.BorderWidthRight = 2;
                s.BorderWidthTop = 2; s.BorderWidthBottom = 2;
                s.BorderColor = border;
                s.CornerRadiusTopLeft = 6; s.CornerRadiusTopRight = 6;
                s.CornerRadiusBottomLeft = 6; s.CornerRadiusBottomRight = 6;
                s.ContentMarginLeft = 10; s.ContentMarginRight = 10;
                s.ContentMarginTop = 10; s.ContentMarginBottom = 10;
                return s;
            }
            cardButton.AddThemeStyleboxOverride("normal",
                MakeCardStyle(new Color(0.1f, 0.15f, 0.12f, 0.95f), new Color(0.35f, 0.55f, 0.4f)));
            cardButton.AddThemeStyleboxOverride("hover",
                MakeCardStyle(new Color(0.15f, 0.22f, 0.18f, 0.98f), new Color(0.6f, 0.8f, 0.5f)));
            cardButton.AddThemeStyleboxOverride("pressed",
                MakeCardStyle(new Color(0.18f, 0.25f, 0.2f, 1f), new Color(0.7f, 1f, 0.6f), true));

            // 结构化内容容器
            var cardVBox = new VBoxContainer();
            cardVBox.AnchorRight = 1; cardVBox.AnchorBottom = 1;
            cardVBox.OffsetLeft = 10; cardVBox.OffsetTop = 10;
            cardVBox.OffsetRight = -10; cardVBox.OffsetBottom = -10;
            cardVBox.AddThemeConstantOverride("separation", 6);
            cardVBox.MouseFilter = Control.MouseFilterEnum.Ignore;
            cardVBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            cardButton.AddChild(cardVBox);

            // 图标区（大号文字符号，居中，占上方约40%）
            var iconLabel = new Label();
            iconLabel.Text = card.Icon;
            iconLabel.HorizontalAlignment = HorizontalAlignment.Center;
            iconLabel.AddThemeFontSizeOverride("font_size", 56);
            iconLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.5f));
            iconLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
            iconLabel.AddThemeConstantOverride("shadow_offset_x", 2);
            iconLabel.AddThemeConstantOverride("shadow_offset_y", 2);
            iconLabel.CustomMinimumSize = new Vector2(0, 80);
            iconLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            cardVBox.AddChild(iconLabel);

            // 分隔线
            var sep = new HSeparator();
            sep.Modulate = new Color(0.4f, 0.55f, 0.4f, 0.5f);
            sep.MouseFilter = Control.MouseFilterEnum.Ignore;
            cardVBox.AddChild(sep);

            // 卡片名称（较粗，居中）
            var nameLabel = new Label();
            nameLabel.Text = card.Name;
            nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
            nameLabel.AddThemeFontSizeOverride("font_size", 18);
            nameLabel.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.8f));
            nameLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            cardVBox.AddChild(nameLabel);

            // 描述（自动换行，左对齐）
            var descLabel = new Label();
            descLabel.Text = card.Description;
            descLabel.HorizontalAlignment = HorizontalAlignment.Center;
            descLabel.AddThemeFontSizeOverride("font_size", 12);
            descLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.78f, 0.72f));
            descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            descLabel.CustomMinimumSize = new Vector2(180, 0);
            descLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            descLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            cardVBox.AddChild(descLabel);

            // 底部编号提示
            var numLabel = new Label();
            numLabel.Text = $"[{i + 1}]";
            numLabel.HorizontalAlignment = HorizontalAlignment.Center;
            numLabel.AddThemeFontSizeOverride("font_size", 14);
            numLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.7f, 0.5f));
            numLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
            cardVBox.AddChild(numLabel);

            cardButton.Pressed += () => SelectPlayerCard(_cardChoices[cardIndex]);
            _cardButtonContainer.AddChild(cardButton);
        }

        _cardPanel.Visible = true;
        GameLog.Debug("[G3] 战术卡选择面板已弹出 — 点击卡片或按1/2/3选择");
    }

    /// <summary>玩家选择战术卡后应用效果。</summary>
    private void SelectPlayerCard(TacticalCards.CardId card)
    {
        _playerCard = card;
        _cardPanel.Visible = false;
        ReplayRecorder.Record(ReplayRecorder.ActionType.SelectCard, new { Card = card.ToString() });
        var info = TacticalCards.Cards[card];
        GameLog.Debug($"[G3] 玩家选择战术卡: {info.Name} — {info.Description}");
        ShowToast(TrManager.Tr("card.toast_selected", info.Name));

        // 应用即时效果
        // 闪电经济：起始资金+50%（额外加钱）
        if (card == TacticalCards.CardId.BlitzEconomy)
        {
            int bonus = (int)(_blueStartMoney * 0.5f);
            _money[PlayerTeamId] += bonus;
            GameLog.Debug($"[G3] 闪电经济: +${bonus} 起始资金");
        }

        // 快速部署：单位上限+10
        // （GetUnitCapBonus方法中处理）

        // 应用被动效果到现有单位
        ApplyCardEffectsToUnits(PlayerTeamId);

        // AI随机选卡（联机模式下只有Host执行，避免desync）
        if (!NetworkManager.IsOnline || NetworkManager.IsHost)
        {
            var rng = new RandomNumberGenerator();
            rng.Randomize();
            for (int team = 1; team < TotalTeamCount; team++)
            {
                // 联机模式下跳过已被其他真人玩家占据的team
                if (NetworkManager.IsOnline && NetworkManager.IsPlayerTeam(team))
                    continue;
                var aiPick = TacticalCards.DrawRandom(1, rng)[0];
                _aiCards[team - 1] = aiPick;
                GameLog.Debug($"[G3] AI Team {team} 战术卡: {TacticalCards.Cards[aiPick].Name}");
                // AI闪电经济即时效果
                if (aiPick == TacticalCards.CardId.BlitzEconomy)
                {
                    int aiBonus = (int)(_aiStartMoney * 0.5f);
                    _money[team] += aiBonus;
                }
                ApplyCardEffectsToUnits(team);
            }
        }

        ShowCardStatus();
    }

    /// <summary>将战术卡效果应用到阵营现有单位。</summary>
    private void ApplyCardEffectsToUnits(int teamId)
    {
        TacticalCards.CardId? card = GetCardForTeam(teamId);
        if (card == null) return;

        float allHealthMul = TacticalCards.GetAllHealthMul(card);
        float allDamageMul = TacticalCards.GetAllDamageMul(card);
        float tankHealthMul = TacticalCards.GetTankHealthMul(card);
        float tankDamageMul = TacticalCards.GetTankDamageMul(card);
        float infHealthMul = TacticalCards.GetInfantryHealthMul(card);

        foreach (var c in _unitsNode.GetChildren())
        {
            if (c is not Unit u || u.TeamId != teamId || !IsInstanceValid(u)) continue;
            float healthMul = allHealthMul;
            float damageMul = allDamageMul;
            if (IsTankType(u.Type))
            {
                healthMul *= tankHealthMul;
                damageMul *= tankDamageMul;
            }
            if (IsInfantryType(u.Type))
            {
                healthMul *= infHealthMul;
            }
            if (healthMul != 1f)
            {
                float ratio = u.Health / u.MaxHealth;
                u.ApplyTechHealthMultiplier(healthMul);
                u.SetHealth(u.MaxHealth * ratio);
            }
            if (damageMul != 1f)
            {
                u.ApplyTechDamageMultiplier(damageMul);
            }
            // S2修复: 战术卡移动速度加成应用到已有单位
            float moveSpeedMul = TacticalCards.GetMoveSpeedMul(card);
            if (moveSpeedMul != 1f)
            {
                u.ApplyTechMoveSpeedMultiplier(moveSpeedMul);
            }
        }

        // 要塞防御：建筑血量+30%
        float bldHealthMul = TacticalCards.GetBuildingHealthMul(card);
        if (bldHealthMul != 1f)
        {
            foreach (var c in _buildingsNode.GetChildren())
            {
                if (c is not Building b || b.TeamId != teamId || !IsInstanceValid(b)) continue;
                float ratio = b.Health / b.MaxHealth;
                b.ApplyTechHealthMultiplier(bldHealthMul);
                b.SetHealth(b.MaxHealth * ratio);
            }
        }
    }

    /// <summary>S1修复: 获取G3战术卡的生产速度乘数（转换为速度乘数，越大越快）。</summary>
    public float GetCardProduceSpeedMul(int teamId)
    {
        TacticalCards.CardId? card = GetCardForTeam(teamId);
        if (!card.HasValue) return 1f;
        float timeMul = TacticalCards.GetProduceTimeMul(card); // <1表示生产时间缩短=速度提升
        return timeMul < 1f ? 1f / timeMul : 1f;
    }

    /// <summary>判断是否为步兵类单位。</summary>
    private static bool IsInfantryType(UnitType type) => type switch
    {
        UnitType.Infantry or UnitType.Sapper or UnitType.Grenadier
        or UnitType.Sniper or UnitType.FlameInfantry or UnitType.RocketInfantry
        or UnitType.Hero or UnitType.Spy or UnitType.Thief => true,
        _ => false,
    };

    /// <summary>显示当前战术卡状态（T键）。</summary>
    private void ShowCardStatus()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(TrManager.Tr("card.panel_title"));
        if (_playerCard.HasValue)
        {
            var card = TacticalCards.Cards[_playerCard.Value];
            sb.AppendLine(TrManager.Tr("card.your_card", card.Icon, card.Name));
            sb.AppendLine(TrManager.Tr("card.desc", card.Description));
        }
        else if (_cardSelectionPending)
        {
            sb.AppendLine(TrManager.Tr("card.selection_pending"));
        }
        else if (_cardPanel.Visible)
        {
            sb.AppendLine(TrManager.Tr("card.please_select"));
        }
        else
        {
            sb.AppendLine(TrManager.Tr("card.none"));
        }
        sb.AppendLine();
        sb.AppendLine(TrManager.Tr("card.ai_cards"));
        for (int i = 1; i <= 7; i++)
        {
            var aiCard = _aiCards[i - 1];
            if (aiCard.HasValue)
                sb.AppendLine(TrManager.Tr("card.ai_card_line", i, TacticalCards.Cards[aiCard.Value].Name));
        }
        _cardStatusLabel.Text = sb.ToString();
        _cardStatusLabel.Visible = true;

        // 3秒后自动隐藏
        if (!_cardPanel.Visible) // 选择面板不显示时才自动隐藏
        {
            _cardStatusHideTimer = 3f;
        }
    }

    private float _cardStatusHideTimer = 0f;

    /// <summary>获取玩家战术卡。</summary>
    public TacticalCards.CardId? GetPlayerCard() => _playerCard;

    /// <summary>获取AI战术卡。</summary>
    public TacticalCards.CardId? GetAiCard(int teamId)
    {
        if (teamId < 1 || teamId >= MaxTeamCount) return null;
        return _aiCards[teamId - 1];
    }

    /// <summary>获取指定阵营的战术卡（联机版：本地玩家用_playerCard，其他用_aiCards）。</summary>
    public TacticalCards.CardId? GetCardForTeam(int teamId)
    {
        if (teamId == PlayerTeamId) return _playerCard;
        if (teamId >= 1 && teamId < MaxTeamCount) return _aiCards[teamId - 1];
        return null;
    }

    /// <summary>获取阵营战术卡的单位上限加成。</summary>
    public int GetCardUnitCapBonus(int teamId)
    {
        var card = GetCardForTeam(teamId);
        return TacticalCards.GetUnitCapBonus(card);
    }

    /// <summary>获取阵营战术卡的矿车收益乘数。</summary>
    public float GetCardMiningMul(int teamId)
    {
        var card = GetCardForTeam(teamId);
        return TacticalCards.GetMiningMul(card);
    }

    /// <summary>获取阵营战术卡的研究速度乘数。</summary>
    public float GetCardResearchSpeedMul(int teamId)
    {
        var card = GetCardForTeam(teamId);
        return TacticalCards.GetResearchSpeedMul(card);
    }

    /// <summary>获取某阵营所有建筑列表。</summary>
    public List<Building> GetTeamBuildings(int teamId)
    {
        var result = new List<Building>();
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b.TeamId == teamId && IsInstanceValid(b))
                result.Add(b);
        }
        return result;
    }

    /// <summary>检查指定建筑是否在电网供电范围内。</summary>
    public bool IsBuildingPowered(Building target)
    {
        if (target.Type == BuildingType.PowerPlant || target.Type == BuildingType.Base)
            return true; // 电站和基地自给自足
        return PowerGrid.IsInRange(target, GetTeamBuildings(target.TeamId));
    }

    /// <summary>更新电网分布面板。</summary>
    private void UpdatePowerGridPanel()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(TrManager.Tr("power.panel_title"));
        sb.AppendLine(TrManager.Tr("power.radius_info", PowerGrid.PowerRadius, PowerGrid.BasePowerRadius));
        sb.AppendLine(TrManager.Tr("power.offline_speed", $"{PowerGrid.OfflineProduceMul*100:F0}"));
        sb.AppendLine();

        var buildings = GetTeamBuildings(PlayerTeamId);
        int powerPlants = 0;
        int powered = 0;
        int offline = 0;
        int totalSupply = 0;
        int totalDemand = 0;

        sb.AppendLine(TrManager.Tr("power.section_player"));
        foreach (var b in buildings)
        {
            bool isOnline = IsBuildingPowered(b);
            string status = b.Type == BuildingType.PowerPlant || b.Type == BuildingType.Base
                ? TrManager.Tr("power.source") : isOnline ? TrManager.Tr("power.online") : TrManager.Tr("power.offline");
            if (b.Type == BuildingType.PowerPlant) { powerPlants++; totalSupply += b.PowerProvided; }
            if (b.PowerConsumed > 0) { totalDemand += b.PowerConsumed; if (isOnline) powered++; else offline++; }
            sb.AppendLine(TrManager.Tr("power.bld_line", b.BuildingName, status, b.PowerProvided, b.PowerConsumed));
        }
        sb.AppendLine();
        sb.AppendLine(TrManager.Tr("power.summary", powerPlants, powered, offline));
        sb.AppendLine(TrManager.Tr("power.supply_demand", totalSupply, totalDemand));

        // P2-8修复：使用PowerGrid.CalculateGridPower展示每个供电源的分区电力
        var powerSources = buildings.Where(b => b.Type == BuildingType.PowerPlant || b.Type == BuildingType.Base).ToList();
        if (powerSources.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(TrManager.Tr("power.section_detail"));
            foreach (var ps in powerSources)
            {
                var (supplied, gridConsumed) = PowerGrid.CalculateGridPower(ps, buildings);
                float radius = ps.Type == BuildingType.Base ? PowerGrid.BasePowerRadius : PowerGrid.PowerRadius;
                sb.AppendLine(TrManager.Tr("power.detail_line", ps.BuildingName, $"{radius:F0}", supplied, gridConsumed, supplied - gridConsumed));
            }
        }

        if (offline > 0)
            sb.AppendLine(TrManager.Tr("power.warn_offline", offline));
        _powerGridLabel.Text = sb.ToString();
    }

    /// <summary>获取建筑的生产速度乘数（G4电网 + G6邻接）。</summary>
    public float GetAdjacencyProduceMul(Building b)
    {
        var buildings = GetTeamBuildings(b.TeamId);
        float adjMul = AdjacencyBonus.GetProduceSpeedMultiplier(buildings, b);
        return adjMul;
    }

    /// <summary>获取建筑的发电量加成乘数（G6邻接）。</summary>
    public float GetAdjacencyPowerMul(Building b)
    {
        if (b.Type != BuildingType.PowerPlant) return 1f;
        var buildings = GetTeamBuildings(b.TeamId);
        return AdjacencyBonus.GetPowerMultiplier(buildings, b);
    }

    /// <summary>获取防御塔的射程加成乘数（G6邻接）。</summary>
    public float GetAdjacencyRangeMul(Building b)
    {
        if (!b.IsDefensive) return 1f;
        var buildings = GetTeamBuildings(b.TeamId);
        return AdjacencyBonus.GetAttackRangeMultiplier(buildings, b);
    }

    /// <summary>获取维修厂的维修速度加成乘数（G6邻接）。</summary>
    public float GetAdjacencyRepairMul(Building b)
    {
        if (!b.IsRepairStation) return 1f;
        var buildings = GetTeamBuildings(b.TeamId);
        return AdjacencyBonus.GetRepairSpeedMultiplier(buildings, b);
    }

    /// <summary>获取阵营的研究速度加成乘数（G6邻接：科技中心靠电站）。</summary>
    public float GetAdjacencyResearchMul(int teamId)
    {
        var buildings = GetTeamBuildings(teamId);
        return AdjacencyBonus.GetResearchMultiplier(buildings, teamId);
    }

    /// <summary>更新邻接加成面板（J键）。</summary>
    private void UpdateAdjacencyPanel()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(TrManager.Tr("adj.panel_title"));
        sb.AppendLine(TrManager.Tr("adj.range_info", AdjacencyBonus.AdjacencyRange));
        sb.AppendLine();
        sb.AppendLine(TrManager.Tr("adj.rules_header"));
        sb.AppendLine(TrManager.Tr("adj.rule_pp_pp"));
        sb.AppendLine(TrManager.Tr("adj.rule_pp_base"));
        sb.AppendLine(TrManager.Tr("adj.rule_bar_bar"));
        sb.AppendLine(TrManager.Tr("adj.rule_wf_wf"));
        sb.AppendLine(TrManager.Tr("adj.rule_turret_bar"));
        sb.AppendLine(TrManager.Tr("adj.rule_repair_wf"));
        sb.AppendLine(TrManager.Tr("adj.rule_tech_pp"));
        sb.AppendLine();

        var buildings = GetTeamBuildings(PlayerTeamId);
        sb.AppendLine(TrManager.Tr("adj.section_player"));
        bool anyBonus = false;
        foreach (var b in buildings)
        {
            if (!IsInstanceValid(b)) continue;
            var bonuses = new List<string>();

            if (b.Type == BuildingType.PowerPlant)
            {
                float powMul = AdjacencyBonus.GetPowerMultiplier(buildings, b);
                if (powMul > 1f)
                {
                    bonuses.Add(TrManager.Tr("adj.bonus_power", $"{(powMul - 1f) * 100:F0}"));
                    anyBonus = true;
                }
            }
            if (b.Type == BuildingType.Barracks || b.Type == BuildingType.WarFactory)
            {
                float prodMul = AdjacencyBonus.GetProduceSpeedMultiplier(buildings, b);
                if (prodMul > 1f)
                {
                    bonuses.Add(TrManager.Tr("adj.bonus_produce", $"{(prodMul - 1f) * 100:F0}"));
                    anyBonus = true;
                }
            }
            if (b.IsDefensive)
            {
                float rangeMul = AdjacencyBonus.GetAttackRangeMultiplier(buildings, b);
                if (rangeMul > 1f)
                {
                    bonuses.Add(TrManager.Tr("adj.bonus_range", $"{(rangeMul - 1f) * 100:F0}"));
                    anyBonus = true;
                }
            }
            if (b.IsRepairStation)
            {
                float repMul = AdjacencyBonus.GetRepairSpeedMultiplier(buildings, b);
                if (repMul > 1f)
                {
                    bonuses.Add(TrManager.Tr("adj.bonus_repair", $"{(repMul - 1f) * 100:F0}"));
                    anyBonus = true;
                }
            }

            string bonusStr = bonuses.Count > 0 ? string.Join(" ", bonuses) : TrManager.Tr("adj.no_bonus");
            sb.AppendLine(TrManager.Tr("adj.bld_line", b.BuildingName, bonusStr));
        }

        if (!anyBonus)
            sb.AppendLine(TrManager.Tr("adj.hint", "\n"));

        // 研究速度加成
        float resMul = AdjacencyBonus.GetResearchMultiplier(buildings, PlayerTeamId);
        if (resMul > 1f)
            sb.AppendLine(TrManager.Tr("adj.research_bonus", "\n", $"{(resMul - 1f) * 100:F0}"));

        _adjacencyLabel.Text = sb.ToString();
    }

    /// <summary>G7: 更新间谍任务面板（N键）。</summary>
    private void UpdateSpyMissionPanel()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(TrManager.Tr("spy.panel_title"));
        sb.AppendLine(TrManager.Tr("spy.header", (int)(SpyMission.SuccessRate * 100), (int)SpyMission.InfiltrateTime));
        sb.AppendLine();
        sb.AppendLine(TrManager.Tr("spy.mission_types"));
        sb.AppendLine(TrManager.Tr("spy.type_steal_tech"));
        sb.AppendLine(TrManager.Tr("spy.type_sabotage_power"));
        sb.AppendLine(TrManager.Tr("spy.type_steal_money"));
        sb.AppendLine(TrManager.Tr("spy.type_paralyze_prod"));
        sb.AppendLine(TrManager.Tr("spy.type_recon"));
        sb.AppendLine();
        sb.AppendLine(TrManager.Tr("spy.operation_hint"));
        sb.AppendLine();

        // 显示玩家方间谍状态
        sb.AppendLine(TrManager.Tr("spy.section_player"));
        bool anySpy = false;
        foreach (var c in _unitsNode.GetChildren())
        {
            if (c is Unit u && u.TeamId == PlayerTeamId && u.Type == UnitType.Spy && IsInstanceValid(u))
            {
                anySpy = true;
                if (u.IsSpyOnMission)
                {
                    string mName = u._spyMission.HasValue ? SpyMission.MissionName(u._spyMission.Value) : TrManager.Tr("spy.none");
                    string target = u._spyTargetBuilding != null && IsInstanceValid(u._spyTargetBuilding)
                        ? u._spyTargetBuilding.BuildingName : "?";
                    sb.AppendLine(TrManager.Tr("spy.on_mission", mName, target, $"{u._spyMissionTimer:F1}"));
                }
                else
                {
                    sb.AppendLine(TrManager.Tr("spy.idle"));
                }
            }
        }
        if (!anySpy) sb.AppendLine(TrManager.Tr("spy.no_spy"));

        _spyMissionLabel.Text = sb.ToString();
    }

    /// <summary>G8: 更新占领面板（K键）。</summary>
    private void UpdateCapturePanel()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(TrManager.Tr("capture.panel_title"));
        sb.AppendLine(TrManager.Tr("capture.reward", CaptureBonus.CaptureMoneyReward));
        sb.AppendLine(TrManager.Tr("capture.boost", (int)CaptureBonus.CapturedProduceDuration));
        sb.AppendLine(TrManager.Tr("capture.chain_range", CaptureBonus.ChainRange));
        sb.AppendLine(TrManager.Tr("capture.defection_risk", (int)(CaptureBonus.DefectionChance * 100), (int)CaptureBonus.DefectionRiskDuration));
        sb.AppendLine();

        // 显示被占领建筑状态
        sb.AppendLine(TrManager.Tr("capture.section_captured"));
        bool any = false;
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b._originalTeamId >= 0 && IsInstanceValid(b))
            {
                any = true;
                string status = "";
                if (b.IsCapturedProduceBoost) status += TrManager.Tr("capture.boosted");
                if (b.IsDefectionRisk) status += TrManager.Tr("capture.defection_active", (int)b._defectionTimer);
                sb.AppendLine(TrManager.Tr("capture.line", b.BuildingName, b.TeamId, b._originalTeamId, status));
            }
        }
        if (!any) sb.AppendLine(TrManager.Tr("capture.none"));

        _captureLabel.Text = sb.ToString();
    }

    // ======== G5: 尤里卡时刻方法 ========

    /// <summary>击杀单位触发尤里卡（军事分支）。</summary>
    public void OnEurekaKill(int teamId)
    {
        if (teamId < 0 || teamId >= _eureka.Length) return;
        if (_eureka[teamId] == null) return;
        if (!_eureka[teamId].OnKill()) return;
        TriggerEureka(teamId, "军事", TrManager.Tr("eureka.reason_kill"));
    }

    /// <summary>建造建筑触发尤里卡（防御分支）。</summary>
    public void OnEurekaBuild(int teamId)
    {
        if (teamId < 0 || teamId >= _eureka.Length) return;
        if (_eureka[teamId] == null) return;
        if (!_eureka[teamId].OnBuild()) return;
        TriggerEureka(teamId, "防御", TrManager.Tr("eureka.reason_build"));
    }

    /// <summary>采集资金触发尤里卡（经济分支）。</summary>
    public void OnEurekaMoney(int teamId, int amount)
    {
        if (teamId < 0 || teamId >= _eureka.Length) return;
        if (_eureka[teamId] == null) return;
        int triggers = _eureka[teamId].OnMoneyGained(amount);
        for (int i = 0; i < triggers; i++)
            TriggerEureka(teamId, "经济", TrManager.Tr("eureka.reason_money"));
    }

    /// <summary>击毁敌方建筑触发尤里卡（随机分支）。</summary>
    public void OnEurekaDestroy(int teamId)
    {
        if (teamId < 0 || teamId >= _eureka.Length) return;
        if (_eureka[teamId] == null) return;
        if (!_eureka[teamId].OnDestroy()) return;
        // 击毁建筑触发随机分支尤里卡
        string[] branches = { TrManager.Tr("tech.branch_military"), TrManager.Tr("tech.branch_economy"), TrManager.Tr("tech.branch_defense") };
        TriggerEureka(teamId, branches[DeterministicRng.RandRange(0, 2)], TrManager.Tr("eureka.reason_destroy"));
    }

    /// <summary>执行尤里卡：找到该分支未研究的科技并直接完成。</summary>
    private void TriggerEureka(int teamId, string branch, string reason)
    {
        if (teamId < 0 || teamId >= _techProgress.Length) return;
        var tp = _techProgress[teamId];
        if (tp == null) return;

        var techId = EurekaSystem.GetUnresearchedInBranch(tp.Completed, tp.CurrentlyResearching, branch);
        if (techId == null)
        {
            // 该分支已全部研究完成 → 资金补偿
            int compensation = 200;
            _money[teamId] += compensation;
            if (teamId == PlayerTeamId)
                ShowToast(TrManager.Tr("eureka.toast_graduated", reason, branch, compensation), new Color(1f, 0.85f, 0.3f));
            GameLog.Debug($"[G5] Team {teamId} {reason}({branch}) — 分支已毕业，+${compensation}补偿");
            return;
        }

        var tid = techId.Value;
        var node = TechTree.Nodes[tid];

        // 尤里卡强制完成该科技
        tp.ForceComplete(tid);

        ApplyTechEffects(teamId);

        if (teamId == PlayerTeamId)
            ShowToast(TrManager.Tr("eureka.toast_free_tech", reason, node.Name), new Color(0.7f, 1f, 0.7f));
        GameLog.Debug($"[G5] Team {teamId} {reason} — 免费获得{branch}科技: {node.Name}");

        // 刷新UI
        if (_eurekaLabel.Visible) UpdateEurekaPanel();
        if (_techTreePanelVisible) UpdateTechTreePanel();
    }

    /// <summary>更新尤里卡进度面板（H键）。</summary>
    private void UpdateEurekaPanel()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(TrManager.Tr("eureka.panel_title"));
        sb.AppendLine(TrManager.Tr("eureka.threshold_kill_money", EurekaSystem.KillThreshold, EurekaSystem.MoneyThreshold));
        sb.AppendLine(TrManager.Tr("eureka.threshold_build_destroy", EurekaSystem.BuildThreshold, EurekaSystem.DestroyThreshold));
        sb.AppendLine();

        // 玩家方
        var p = _eureka[PlayerTeamId];
        sb.AppendLine(TrManager.Tr("eureka.section_player"));
        sb.AppendLine(TrManager.Tr("eureka.player_line1", p.KillCounter, EurekaSystem.KillThreshold, p.MoneyAccumulated, EurekaSystem.MoneyThreshold));
        sb.AppendLine(TrManager.Tr("eureka.player_line2", p.BuildCounter, EurekaSystem.BuildThreshold, p.DestroyCounter, EurekaSystem.DestroyThreshold));
        sb.AppendLine();

        // AI方（活跃阵营）
        for (int t = 1; t <= _activeAiCount; t++)
        {
            var a = _eureka[t];
            sb.AppendLine(TrManager.Tr("eureka.ai_line", t, a.KillCounter, EurekaSystem.KillThreshold, a.MoneyAccumulated, EurekaSystem.MoneyThreshold, a.BuildCounter, EurekaSystem.BuildThreshold, a.DestroyCounter, EurekaSystem.DestroyThreshold));
        }

        _eurekaLabel.Text = sb.ToString();
    }

}