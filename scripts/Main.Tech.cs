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
        var tp = _techProgress[0]; // 玩家阵营
        var node = TechTree.Nodes[techId];
        bool hasTech = HasBuilding(0, BuildingType.TechCenter) || !node.RequiresTechCenter;

        if (tp.Completed.Contains(techId))
        {
            GD.Print($"[G1] {node.Name} 已研究完成");
            return;
        }
        if (tp.CurrentlyResearching.HasValue)
        {
            GD.Print($"[G1] 正在研究中: {TechTree.Nodes[tp.CurrentlyResearching.Value].Name} ({tp.Progress*100:F0}%)");
            return;
        }
        if (!TechTree.CanResearch(tp.Completed, techId, hasTech, _money[0]))
        {
            if (!hasTech) GD.Print($"[G1] {node.Name} 需要科技中心");
            else if (_money[0] < node.Cost) GD.Print($"[G1] 资金不足: {node.Name} 需要${node.Cost}，当前${_money[0]}");
            else GD.Print($"[G1] {node.Name} 需要前置科技");
            return;
        }
        _money[0] -= node.Cost;
        tp.StartResearch(techId);
        GD.Print($"[G1] 开始研究: {node.Name} (成本${node.Cost}，{node.ResearchTime:F0}秒) — 资金剩余${_money[0]}");
        ShowToast($"开始研究: {node.Name}");
    }

    /// <summary>更新科技树面板显示文本。</summary>
    private void UpdateTechTreePanel()
    {
        var tp = _techProgress[0];
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══════════ 科技树 ═══════════ (Tab关闭)");
        sb.AppendLine($"资金: ${_money[0]}  科技中心: {(HasBuilding(0, BuildingType.TechCenter) ? "有" : "无")}  研究中: {(tp.CurrentlyResearching.HasValue ? $"{TechTree.Nodes[tp.CurrentlyResearching.Value].Name} {tp.Progress*100:F0}%" : "无")}");
        sb.AppendLine();

        string[] branches = { "军事", "经济", "防御" };
        int techIdx = 0;
        foreach (var branch in branches)
        {
            sb.AppendLine($"【{branch}分支】");
            for (int tier = 1; tier <= 4; tier++)
            {
                var node = TechTree.GetByBranchTier(branch, tier);
                if (node == null) continue;
                bool done = tp.Completed.Contains(node.Id);
                bool researching = tp.CurrentlyResearching == node.Id;
                bool available = TechTree.CanResearch(tp.Completed, node.Id, HasBuilding(0, BuildingType.TechCenter) || !node.RequiresTechCenter, _money[0]);
                string status = done ? "[已完成]" : researching ? $"[研究中{tp.Progress*100:F0}%]" : available ? "[可研究]" : "[锁定]";
                string keyHint = done ? "  " : $"({techIdx})";
                sb.AppendLine($"  {keyHint} T{tier} {node.Name} {status} — ${node.Cost} / {node.ResearchTime:F0}s");
                sb.AppendLine($"       {node.Description}");
                techIdx++;
            }
            sb.AppendLine();
        }
        sb.AppendLine("按数字键0-9/-= 研究对应科技");
        _techTreeLabel.Text = sb.ToString();
    }

    /// <summary>G1: 更新所有阵营的科技研究进度（每帧调用）。</summary>
    private void UpdateTechResearch(float dt)
    {
        // G3: 战术卡研究速度加成 + G6: 邻接加成研究速度
        float playerResearchMul = GetCardResearchSpeedMul(0) * GetAdjacencyResearchMul(0);
        // 玩家阵营
        var completed = _techProgress[0].UpdateResearch(dt * playerResearchMul);
        if (completed.HasValue)
        {
            var node = TechTree.Nodes[completed.Value];
            GD.Print($"[G1] 科技研究完成: {node.Name} — {node.Description}");
            ShowToast($"科技完成: {node.Name}");
            ApplyTechEffects(0);
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
                    GD.Print($"[G1] AI Team {team} 开始研究: {node.Name}");
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
                GD.Print($"[G1] AI Team {team} 科技完成: {node.Name}");
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
        TacticalCards.CardId? card = teamId == 0 ? _playerCard : _aiCards[teamId - 1];
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

    // ======== G2: 时代系统方法 ========

    /// <summary>玩家尝试升级时代。</summary>
    private void TryAdvanceEra()
    {
        var ep = _eraProgress[0];
        if (ep.IsUpgrading)
        {
            GD.Print($"[G2] 时代升级进行中... {ep.Progress*100:F0}%");
            return;
        }
        var next = EraSystem.GetNextEra(ep.CurrentEra);
        if (next == null)
        {
            GD.Print("[G2] 已达到最高时代（信息时代）");
            return;
        }
        if (!EraSystem.CanAdvance(ep.CurrentEra, t => HasBuilding(0, t), _money[0]))
        {
            if (_money[0] < next.UpgradeCost)
                GD.Print($"[G2] 资金不足：升级到{next.Name}需要${next.UpgradeCost}，当前${_money[0]}");
            else
            {
                string missing = "";
                foreach (var req in next.RequiredBuildings)
                    if (!HasBuilding(0, req)) missing += $" {req}";
                GD.Print($"[G2] 缺少前置建筑：{missing}");
            }
            return;
        }
        _money[0] -= next.UpgradeCost;
        ep.StartUpgrade();
        GD.Print($"[G2] 开始时代升级：{EraSystem.Eras[(int)ep.CurrentEra].Name} → {next.Name} (成本${next.UpgradeCost}，{next.UpgradeTime:F0}秒)");
        ShowToast($"时代升级中: → {next.Name}");
    }

    /// <summary>更新时代面板显示。</summary>
    private void UpdateEraPanel()
    {
        var ep = _eraProgress[0];
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══════════ 时代系统 ═══════════ (Y关闭)");
        sb.AppendLine($"当前时代: {EraSystem.Eras[(int)ep.CurrentEra].Name}  资金: ${_money[0]}");
        if (ep.IsUpgrading)
        {
            var next = EraSystem.GetNextEra(ep.CurrentEra);
            sb.AppendLine($"升级中: → {next?.Name} ({ep.Progress*100:F0}%)");
        }
        sb.AppendLine();

        for (int i = 0; i < EraSystem.Eras.Length; i++)
        {
            var era = EraSystem.Eras[i];
            string marker = era.Id == ep.CurrentEra ? "▶" : (int)era.Id < (int)ep.CurrentEra ? "✓" : " ";
            string status = era.Id == ep.CurrentEra ? "[当前]" : (int)era.Id < (int)ep.CurrentEra ? "[已完成]" : "";
            sb.AppendLine($"{marker} {era.Name} {status}");
            sb.AppendLine($"  {era.Description}");
            if ((int)era.Id == (int)ep.CurrentEra + 1 && !ep.IsUpgrading)
            {
                bool canAdv = EraSystem.CanAdvance(ep.CurrentEra, t => HasBuilding(0, t), _money[0]);
                string reqStr = era.RequiredBuildings.Length > 0
                    ? string.Join("/", System.Array.ConvertAll(era.RequiredBuildings, b => b.ToString()))
                    : "无";
                sb.AppendLine($"  升级条件: ${era.UpgradeCost} + {reqStr} + {era.UpgradeTime:F0}秒");
                sb.AppendLine($"  状态: {(canAdv ? "[可升级] 按U键升级" : "[条件不足]")}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("时代加成: 每时代 +5%攻击/+5%血量/+10%采矿/+10%建造");
        sb.AppendLine("按U键升级时代");
        _eraLabel.Text = sb.ToString();
    }

    /// <summary>G2: 更新所有阵营的时代升级进度（每帧调用）。</summary>
    private void UpdateEraProgress(float dt)
    {
        // 玩家阵营
        var ep = _eraProgress[0];
        float eraUpgradeMul = TacticalCards.GetEraUpgradeSpeedMul(_playerCard);
        if (ep.UpdateUpgrade(dt * eraUpgradeMul))
        {
            var eraInfo = EraSystem.Eras[(int)ep.CurrentEra];
            GD.Print($"[G2] 时代升级完成: {eraInfo.Name} — {eraInfo.Description}");
            ShowToast($"进入{eraInfo.Name}!");
            ApplyEraEffects(0);
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
                        GD.Print($"[G2] AI Team {team} 开始时代升级: → {next.Name}");
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
                GD.Print($"[G2] AI Team {team} 进入{eraInfo.Name}");
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

    /// <summary>显示战术卡选择面板。</summary>
    private void ShowCardSelection()
    {
        _cardSelectionPending = false;
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        _cardChoices = TacticalCards.DrawRandom(3, rng);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══════════ 战术卡选择 ═══════════");
        sb.AppendLine("开局战略卡 — 选择1张影响整局走向！");
        sb.AppendLine();
        for (int i = 0; i < _cardChoices.Length; i++)
        {
            var card = TacticalCards.Cards[_cardChoices[i]];
            sb.AppendLine($"  ({i + 1}) {card.Icon} {card.Name}");
            sb.AppendLine($"       {card.Description}");
            sb.AppendLine();
        }
        sb.AppendLine("按 1/2/3 键选择对应战术卡");
        _cardLabel.Text = sb.ToString();
        _cardLabel.Visible = true;
        GD.Print("[G3] 战术卡选择面板已弹出 — 按1/2/3选择");
    }

    /// <summary>玩家选择战术卡后应用效果。</summary>
    private void SelectPlayerCard(TacticalCards.CardId card)
    {
        _playerCard = card;
        _cardLabel.Visible = false;
        var info = TacticalCards.Cards[card];
        GD.Print($"[G3] 玩家选择战术卡: {info.Name} — {info.Description}");
        ShowToast($"战术卡: {info.Name}");

        // 应用即时效果
        // 闪电经济：起始资金+50%（额外加钱）
        if (card == TacticalCards.CardId.BlitzEconomy)
        {
            int bonus = (int)(_blueStartMoney * 0.5f);
            _money[0] += bonus;
            GD.Print($"[G3] 闪电经济: +${bonus} 起始资金");
        }

        // 快速部署：单位上限+10
        // （GetUnitCapBonus方法中处理）

        // 应用被动效果到现有单位
        ApplyCardEffectsToUnits(0);

        // AI随机选卡
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        for (int team = 1; team < TotalTeamCount; team++)
        {
            var aiPick = TacticalCards.DrawRandom(1, rng)[0];
            _aiCards[team - 1] = aiPick;
            GD.Print($"[G3] AI Team {team} 战术卡: {TacticalCards.Cards[aiPick].Name}");
            // AI闪电经济即时效果
            if (aiPick == TacticalCards.CardId.BlitzEconomy)
            {
                int aiBonus = (int)(_aiStartMoney * 0.5f);
                _money[team] += aiBonus;
            }
            ApplyCardEffectsToUnits(team);
        }

        ShowCardStatus();
    }

    /// <summary>将战术卡效果应用到阵营现有单位。</summary>
    private void ApplyCardEffectsToUnits(int teamId)
    {
        TacticalCards.CardId? card = teamId == 0 ? _playerCard : _aiCards[teamId - 1];
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
        TacticalCards.CardId? card = teamId == 0 ? _playerCard : _aiCards[teamId - 1];
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
        sb.AppendLine("════ 战术卡 ════ (T关闭)");
        if (_playerCard.HasValue)
        {
            var card = TacticalCards.Cards[_playerCard.Value];
            sb.AppendLine($"你的卡: {card.Icon} {card.Name}");
            sb.AppendLine($"  {card.Description}");
        }
        else if (_cardSelectionPending)
        {
            sb.AppendLine("战术卡选择即将开始...");
        }
        else if (_cardLabel.Visible)
        {
            sb.AppendLine("请选择战术卡！(1/2/3)");
        }
        else
        {
            sb.AppendLine("未选择战术卡");
        }
        sb.AppendLine();
        sb.AppendLine("AI战术卡:");
        for (int i = 1; i <= 7; i++)
        {
            var aiCard = _aiCards[i - 1];
            if (aiCard.HasValue)
                sb.AppendLine($"  Team{i}: {TacticalCards.Cards[aiCard.Value].Name}");
        }
        _cardStatusLabel.Text = sb.ToString();
        _cardStatusLabel.Visible = true;

        // 3秒后自动隐藏
        if (!_cardLabel.Visible) // 选择面板不显示时才自动隐藏
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
        if (teamId < 1 || teamId > 7) return null;
        return _aiCards[teamId - 1];
    }

    /// <summary>获取阵营战术卡的单位上限加成。</summary>
    public int GetCardUnitCapBonus(int teamId)
    {
        var card = teamId == 0 ? _playerCard : _aiCards[teamId - 1];
        return TacticalCards.GetUnitCapBonus(card);
    }

    /// <summary>获取阵营战术卡的矿车收益乘数。</summary>
    public float GetCardMiningMul(int teamId)
    {
        var card = teamId == 0 ? _playerCard : _aiCards[teamId - 1];
        return TacticalCards.GetMiningMul(card);
    }

    /// <summary>获取阵营战术卡的研究速度乘数。</summary>
    public float GetCardResearchSpeedMul(int teamId)
    {
        var card = teamId == 0 ? _playerCard : _aiCards[teamId - 1];
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
        sb.AppendLine("═══════════ 电网分区 ═══════════ (G关闭)");
        sb.AppendLine($"电站供电半径: {PowerGrid.PowerRadius}px  基地自供电: {PowerGrid.BasePowerRadius}px");
        sb.AppendLine($"离线建筑生产速度: {PowerGrid.OfflineProduceMul*100:F0}%");
        sb.AppendLine();

        var buildings = GetTeamBuildings(0);
        int powerPlants = 0;
        int powered = 0;
        int offline = 0;
        int totalSupply = 0;
        int totalDemand = 0;

        sb.AppendLine("【玩家方建筑供电状态】");
        foreach (var b in buildings)
        {
            bool isOnline = IsBuildingPowered(b);
            string status = b.Type == BuildingType.PowerPlant || b.Type == BuildingType.Base
                ? "供电源" : isOnline ? "在线" : "离线!";
            if (b.Type == BuildingType.PowerPlant) { powerPlants++; totalSupply += b.PowerProvided; }
            if (b.PowerConsumed > 0) { totalDemand += b.PowerConsumed; if (isOnline) powered++; else offline++; }
            sb.AppendLine($"  {b.BuildingName} [{status}] 供{b.PowerProvided} 耗{b.PowerConsumed}");
        }
        sb.AppendLine();
        sb.AppendLine($"电站: {powerPlants}  在线耗电建筑: {powered}  离线: {offline}");
        sb.AppendLine($"总供电: {totalSupply}  总需求: {totalDemand}");
        if (offline > 0)
            sb.AppendLine($"⚠ {offline}个建筑离线！建造电站靠近它们");
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
        sb.AppendLine("═══════════ 邻接加成 ═══════════ (J关闭)");
        sb.AppendLine($"邻接范围: {AdjacencyBonus.AdjacencyRange}px");
        sb.AppendLine();
        sb.AppendLine("加成规则:");
        sb.AppendLine("  电站+电站 → +15%发电/座");
        sb.AppendLine("  电站+基地 → +10%发电");
        sb.AppendLine("  兵营+兵营 → +10%生产/座");
        sb.AppendLine("  车厂+车厂 → +10%生产/座");
        sb.AppendLine("  炮塔+兵营 → +15%射程");
        sb.AppendLine("  维修厂+车厂 → +25%维修速度");
        sb.AppendLine("  科技+电站 → +15%研究速度");
        sb.AppendLine();

        var buildings = GetTeamBuildings(0);
        sb.AppendLine("【玩家方建筑邻接状态】");
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
                    bonuses.Add($"+{(powMul - 1f) * 100:F0}%发电");
                    anyBonus = true;
                }
            }
            if (b.Type == BuildingType.Barracks || b.Type == BuildingType.WarFactory)
            {
                float prodMul = AdjacencyBonus.GetProduceSpeedMultiplier(buildings, b);
                if (prodMul > 1f)
                {
                    bonuses.Add($"+{(prodMul - 1f) * 100:F0}%生产");
                    anyBonus = true;
                }
            }
            if (b.IsDefensive)
            {
                float rangeMul = AdjacencyBonus.GetAttackRangeMultiplier(buildings, b);
                if (rangeMul > 1f)
                {
                    bonuses.Add($"+{(rangeMul - 1f) * 100:F0}%射程");
                    anyBonus = true;
                }
            }
            if (b.IsRepairStation)
            {
                float repMul = AdjacencyBonus.GetRepairSpeedMultiplier(buildings, b);
                if (repMul > 1f)
                {
                    bonuses.Add($"+{(repMul - 1f) * 100:F0}%维修");
                    anyBonus = true;
                }
            }

            string bonusStr = bonuses.Count > 0 ? string.Join(" ", bonuses) : "无加成";
            sb.AppendLine($"  {b.BuildingName} [{bonusStr}]");
        }

        if (!anyBonus)
            sb.AppendLine("\n提示: 将同类型建筑建在一起获得加成！");

        // 研究速度加成
        float resMul = AdjacencyBonus.GetResearchMultiplier(buildings, 0);
        if (resMul > 1f)
            sb.AppendLine($"\n研究速度加成: +{(resMul - 1f) * 100:F0}% (科技中心靠近电站)");

        _adjacencyLabel.Text = sb.ToString();
    }

    /// <summary>G7: 更新间谍任务面板（N键）。</summary>
    private void UpdateSpyMissionPanel()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══ 间谍任务 ═══ (N关闭)");
        sb.AppendLine($"成功率: {(int)(SpyMission.SuccessRate * 100)}% | 渗透: {(int)SpyMission.InfiltrateTime}秒");
        sb.AppendLine();
        sb.AppendLine("任务类型:");
        sb.AppendLine("  窃取科技 → 科技中心");
        sb.AppendLine("  破坏电网 → 电站");
        sb.AppendLine("  窃取资金 → 基地");
        sb.AppendLine("  瘫痪生产 → 兵营/车厂");
        sb.AppendLine("  侦察 → 任意建筑");
        sb.AppendLine();
        sb.AppendLine("操作: 选中间谍 + 右键敌方建筑");
        sb.AppendLine();

        // 显示玩家方间谍状态
        sb.AppendLine("【玩家方间谍状态】");
        bool anySpy = false;
        foreach (var c in _unitsNode.GetChildren())
        {
            if (c is Unit u && u.TeamId == 0 && u.Type == UnitType.Spy && IsInstanceValid(u))
            {
                anySpy = true;
                if (u.IsSpyOnMission)
                {
                    string mName = u._spyMission.HasValue ? SpyMission.MissionName(u._spyMission.Value) : "无";
                    string target = u._spyTargetBuilding != null && IsInstanceValid(u._spyTargetBuilding)
                        ? u._spyTargetBuilding.BuildingName : "?";
                    sb.AppendLine($"  间谍 → {mName}({target}) 剩余{u._spyMissionTimer:F1}秒");
                }
                else
                {
                    sb.AppendLine($"  间谍 → 待命");
                }
            }
        }
        if (!anySpy) sb.AppendLine("  (无间谍单位)");

        _spyMissionLabel.Text = sb.ToString();
    }

    /// <summary>G8: 更新占领面板（K键）。</summary>
    private void UpdateCapturePanel()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("════ 占领强化 ════ (K关闭)");
        sb.AppendLine($"占领奖励: +${CaptureBonus.CaptureMoneyReward}");
        sb.AppendLine($"缴获加速: +30%生产/{(int)CaptureBonus.CapturedProduceDuration}秒");
        sb.AppendLine($"连锁范围: {CaptureBonus.ChainRange}px (+50%占领速)");
        sb.AppendLine($"叛变风险: {(int)(CaptureBonus.DefectionChance * 100)}%持续{(int)CaptureBonus.DefectionRiskDuration}秒");
        sb.AppendLine();

        // 显示被占领建筑状态
        sb.AppendLine("【被占领建筑状态】");
        bool any = false;
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b._originalTeamId >= 0 && IsInstanceValid(b))
            {
                any = true;
                string status = "";
                if (b.IsCapturedProduceBoost) status += " 缴获加速";
                if (b.IsDefectionRisk) status += $" 叛变风险{(int)b._defectionTimer}s";
                sb.AppendLine($"  {b.BuildingName}(T{b.TeamId}) 原:T{b._originalTeamId}{status}");
            }
        }
        if (!any) sb.AppendLine("  (无被占领建筑)");

        _captureLabel.Text = sb.ToString();
    }

    // ======== G5: 尤里卡时刻方法 ========

    /// <summary>击杀单位触发尤里卡（军事分支）。</summary>
    public void OnEurekaKill(int teamId)
    {
        if (teamId < 0 || teamId >= _eureka.Length) return;
        if (_eureka[teamId] == null) return;
        if (!_eureka[teamId].OnKill()) return;
        TriggerEureka(teamId, "军事", "击杀尤里卡");
    }

    /// <summary>建造建筑触发尤里卡（防御分支）。</summary>
    public void OnEurekaBuild(int teamId)
    {
        if (teamId < 0 || teamId >= _eureka.Length) return;
        if (_eureka[teamId] == null) return;
        if (!_eureka[teamId].OnBuild()) return;
        TriggerEureka(teamId, "防御", "建造尤里卡");
    }

    /// <summary>采集资金触发尤里卡（经济分支）。</summary>
    public void OnEurekaMoney(int teamId, int amount)
    {
        if (teamId < 0 || teamId >= _eureka.Length) return;
        if (_eureka[teamId] == null) return;
        int triggers = _eureka[teamId].OnMoneyGained(amount);
        for (int i = 0; i < triggers; i++)
            TriggerEureka(teamId, "经济", "采集尤里卡");
    }

    /// <summary>击毁敌方建筑触发尤里卡（随机分支）。</summary>
    public void OnEurekaDestroy(int teamId)
    {
        if (teamId < 0 || teamId >= _eureka.Length) return;
        if (_eureka[teamId] == null) return;
        if (!_eureka[teamId].OnDestroy()) return;
        // 击毁建筑触发随机分支尤里卡
        string[] branches = { "军事", "经济", "防御" };
        TriggerEureka(teamId, branches[GD.RandRange(0, 2)], "摧毁尤里卡");
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
            if (teamId == 0)
                ShowToast($"★ {reason}: {branch}分支已毕业！+${compensation}补偿", new Color(1f, 0.85f, 0.3f));
            GD.Print($"[G5] Team {teamId} {reason}({branch}) — 分支已毕业，+${compensation}补偿");
            return;
        }

        var tid = techId.Value;
        var node = TechTree.Nodes[tid];

        // 尤里卡强制完成该科技
        tp.ForceComplete(tid);

        ApplyTechEffects(teamId);

        if (teamId == 0)
            ShowToast($"★ {reason}！免费获得科技: {node.Name}", new Color(0.7f, 1f, 0.7f));
        GD.Print($"[G5] Team {teamId} {reason} — 免费获得{branch}科技: {node.Name}");

        // 刷新UI
        if (_eurekaLabel.Visible) UpdateEurekaPanel();
        if (_techTreePanelVisible) UpdateTechTreePanel();
    }

    /// <summary>更新尤里卡进度面板（H键）。</summary>
    private void UpdateEurekaPanel()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══════════ 尤里卡时刻 ═══════════ (H关闭)");
        sb.AppendLine($"击杀{EurekaSystem.KillThreshold}单位→军事 | 采集${EurekaSystem.MoneyThreshold}→经济");
        sb.AppendLine($"建造{EurekaSystem.BuildThreshold}建筑→防御 | 摧毁{EurekaSystem.DestroyThreshold}建筑→随机");
        sb.AppendLine();

        // 玩家方
        var p = _eureka[0];
        sb.AppendLine("【玩家方】");
        sb.AppendLine($"  击杀: {p.KillCounter}/{EurekaSystem.KillThreshold}  采集: ${p.MoneyAccumulated}/{EurekaSystem.MoneyThreshold}");
        sb.AppendLine($"  建造: {p.BuildCounter}/{EurekaSystem.BuildThreshold}  摧毁: {p.DestroyCounter}/{EurekaSystem.DestroyThreshold}");
        sb.AppendLine();

        // AI方（活跃阵营）
        for (int t = 1; t <= _activeAiCount; t++)
        {
            var a = _eureka[t];
            sb.AppendLine($"【AI Team {t}】 杀{a.KillCounter}/{EurekaSystem.KillThreshold} 钱${a.MoneyAccumulated}/{EurekaSystem.MoneyThreshold} 建造{a.BuildCounter}/{EurekaSystem.BuildThreshold} 摧毁{a.DestroyCounter}/{EurekaSystem.DestroyThreshold}");
        }

        _eurekaLabel.Text = sb.ToString();
    }

}