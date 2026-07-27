using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RTSGame;

/// <summary>
/// Main 的战斗/超武/间谍控制器（partial class）。
/// 包含：核弹/闪电/导弹伤害结算 + 闪电绘制 + AI核弹目标 + 间谍任务 + 音效回调 + 建筑受袭。
/// </summary>
public partial class Main
{
    /// <summary>阶段12-A4：绘制程序化闪电柱折线（白蓝色，从地面向上抖动）。基于 seed 生成确定形状避免每帧变化太剧烈。</summary>
    private void DrawLightningBolt(Vector2 origin, float seed, float age)
    {
        // 闪烁强度：靠近生命末尾淡出
        float alpha = age < 0.2f ? age / 0.2f : (age > 4.5f ? (5f - age) / 0.5f : 1f);
        alpha = Mathf.Clamp(alpha, 0.2f, 1f);

        // 主闪电柱：从 origin 向上延伸约 60 像素，分 6 段折线
        int segments = 6;
        var points = new Vector2[segments + 1];
        points[0] = origin;
        for (int i = 1; i <= segments; i++)
        {
            float t = (float)i / segments;
            // 伪随机：基于 seed 和段落索引生成横向偏移
            float r1 = Mathf.Sin(seed * 0.7f + i * 2.3f) * 0.5f;
            float r2 = Mathf.Cos(seed * 1.1f + i * 3.7f) * 0.5f;
            float offset = (r1 + r2) * 8f;
            points[i] = new Vector2(origin.X + offset, origin.Y - t * 60f);
        }
        // 外层光晕（粗白线）
        var glowCol = new Color(0.8f, 0.95f, 1f, alpha * 0.6f);
        for (int i = 0; i < segments; i++)
            DrawLine(points[i], points[i + 1], glowCol, 5f);
        // 内层亮白核心（细线）
        var coreCol = new Color(1f, 1f, 1f, alpha);
        for (int i = 0; i < segments; i++)
            DrawLine(points[i], points[i + 1], coreCol, 2f);

        // 分叉闪电（左右两根更短的支线）
        for (int branch = 0; branch < 2; branch++)
        {
            int startIdx = 2 + branch * 2;
            if (startIdx >= segments) continue;
            var bp = new Vector2[3];
            bp[0] = points[startIdx];
            float dir = branch == 0 ? -1f : 1f;
            float br1 = Mathf.Sin(seed * 2.1f + branch * 5.3f) * 0.5f;
            bp[1] = bp[0] + new Vector2(dir * (12f + br1 * 8f), -10f);
            bp[2] = bp[1] + new Vector2(dir * (6f + br1 * 5f), -8f);
            for (int i = 0; i < 2; i++)
                DrawLine(bp[i], bp[i + 1], new Color(0.8f, 0.95f, 1f, alpha * 0.7f), 2f);
        }
    }

    /// <summary>在指定位置释放核弹：对范围内所有非己方单位/建筑造成 GameConst.NukeDamage 伤害，并播放冲击波 + 多层爆炸特效。</summary>
    private void ApplyNuke(Vector2 pos, int firingTeamId)
    {
        int unitHits = 0, bldHits = 0;

        // 1. 对范围内敌方单位造成伤害
        foreach (var c in _unitsNode.GetChildren())
        {
            if (c is Unit u && IsInstanceValid(u) && u.TeamId != firingTeamId
                && pos.DistanceTo(u.GlobalPosition) <= GameConst.NukeRadius)
            {
                u.TakeDamage(GameConst.NukeDamage);
                unitHits++;
            }
        }
        // 2. 对范围内敌方建筑造成伤害
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && IsInstanceValid(b) && b.TeamId != firingTeamId
                && pos.DistanceTo(b.GlobalPosition) <= GameConst.NukeRadius)
            {
                b.TakeDamage(GameConst.NukeDamage);
                bldHits++;
            }
        }

        // 3. 视觉特效：3 秒持续冲击波 + 辐射雾（由 _Draw 渲染）
        _activeNukeVisuals.Add(new NukeVisual
        {
            Position = pos,
            Age = 0f,
            Lifetime = 3f
        });

        // 4. 中心大爆炸特效（Kenney 烟雾 5 帧动画）
        AddChild(BattleEffect.BigExplosion(pos));
        // 5. 多重次级爆炸叠加，增强蘑菇云观感
        for (int i = 0; i < 6; i++)
        {
            float ang = i * Mathf.Tau / 6f + (float)GD.RandRange(-0.3, 0.3);
            float r = (float)GD.RandRange(30, 90);
            var offset = new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);
            AddChild(BattleEffect.Explosion(pos + offset));
        }

        // 6. 通知提示
        string who = firingTeamId == PlayerTeamId ? "我方" : $"敌方 Team {firingTeamId}";
        ShowToast($"☢ {who}释放核弹！命中 {unitHits} 单位 / {bldHits} 建筑",
            new Color(1f, 0.3f, 0.2f));
        GameLog.Debug($"[核弹] Team {firingTeamId} 于 {pos} 释放，命中 {unitHits} 单位 + {bldHits} 建筑");

        // 阶段12-C：核弹音效
        _audio?.PlaySfxForce(AudioManager.Sfx.Nuke);
        _audio?.PlaySfxForce(AudioManager.Sfx.BigExplosion);
        QueueRedraw();
    }

    // ---------- 阶段12-A4 闪电风暴 ----------

    /// <summary>在指定位置释放闪电风暴：立即造成一次 GameConst.LightningDps 伤害，并在接下来 GameConst.LightningDuration 秒内持续每秒造成同等伤害。
    /// 伤害结算由 _Process 中的 _activeLightnings 列表推进。</summary>
    private void ApplyLightning(Vector2 pos, int firingTeamId)
    {
        // 1. 立即造成一次伤害（首击）
        int unitHits = DamageLightningAreaOnce(pos, firingTeamId);

        // 2. 添加持续特效数据（5秒内每秒继续造成伤害）
        _activeLightnings.Add(new LightningVisual
        {
            Position = pos,
            FiringTeamId = firingTeamId,
            Age = 0f,
            Lifetime = GameConst.LightningDuration,
            DamageTickTimer = 0f, // 下次伤害在 1 秒后
            BoltRefreshTimer = 0f
        });

        // 3. 视觉特效：中心小爆炸（闪电击中地表的火花）
        AddChild(BattleEffect.Explosion(pos));
        // 4. 范围内多个次级火花
        for (int i = 0; i < 4; i++)
        {
            float ang = i * Mathf.Tau / 4f + (float)GD.RandRange(0, 1.5);
            float r = (float)GD.RandRange(30, 70);
            var offset = new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);
            AddChild(BattleEffect.Explosion(pos + offset));
        }

        // 5. 重置闪电形状种子，让 _Draw 生成新的折线形状
        _lightningBoltSeed = (float)GD.RandRange(0, 1000);

        // 6. 通知提示
        string who = firingTeamId == PlayerTeamId ? "我方" : $"敌方 Team {firingTeamId}";
        ShowToast($"⚡ {who}释放闪电风暴！初始命中 {unitHits} 敌方目标，持续 {GameConst.LightningDuration:F0}s",
            new Color(0.5f, 0.8f, 1f));
        GameLog.Debug($"[闪电] Team {firingTeamId} 于 {pos} 释放，初始命中 {unitHits}，持续 {GameConst.LightningDuration}s");

        // 阶段12-C：闪电风暴音效
        _audio?.PlaySfxForce(AudioManager.Sfx.Lightning);
        QueueRedraw();
    }

    /// <summary>E10：在指定位置释放巡航导弹——单次大范围高伤打击。</summary>
    private void ApplyCruiseMissile(Vector2 pos, int firingTeamId)
    {
        int unitHits = 0, bldHits = 0;
        foreach (var child in _unitsNode.GetChildren())
        {
            if (child is Unit u && IsInstanceValid(u) && u.TeamId != firingTeamId && !u.IsDead)
            {
                float d = u.GlobalPosition.DistanceTo(pos);
                if (d < GameConst.MissileRadius)
                {
                    float dmg = GameConst.MissileDamage * (1f - d / GameConst.MissileRadius);
                    u.TakeDamage(dmg);
                    unitHits++;
                }
            }
        }
        foreach (var child in _buildingsNode.GetChildren())
        {
            if (child is Building b && IsInstanceValid(b) && b.TeamId != firingTeamId)
            {
                float d = b.GlobalPosition.DistanceTo(pos);
                if (d < GameConst.MissileRadius)
                {
                    float dmg = GameConst.MissileDamage * (1f - d / GameConst.MissileRadius) * 0.8f; // 建筑伤害8折
                    b.TakeDamage(dmg);
                    bldHits++;
                }
            }
        }
        GameLog.Debug($"[巡航导弹] 位置{pos}，命中{unitHits}单位/{bldHits}建筑");
        // 视觉特效：复用核弹爆炸
        _activeNukeVisuals.Add(new NukeVisual { Position = pos, Age = 0f, Lifetime = 4f });
        QueueRedraw();
    }

    /// <summary>对闪电风暴作用半径内的所有非己方单位/建筑造成一次 GameConst.LightningDps 伤害，返回命中数量。</summary>
    private int DamageLightningAreaOnce(Vector2 pos, int firingTeamId)
    {
        int hits = 0;
        foreach (var c in _unitsNode.GetChildren())
        {
            if (c is Unit u && IsInstanceValid(u) && u.TeamId != firingTeamId
                && pos.DistanceTo(u.GlobalPosition) <= GameConst.LightningRadius)
            {
                u.TakeDamage(GameConst.LightningDps);
                hits++;
            }
        }
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && IsInstanceValid(b) && b.TeamId != firingTeamId
                && pos.DistanceTo(b.GlobalPosition) <= GameConst.LightningRadius)
            {
                b.TakeDamage(GameConst.LightningDps);
                hits++;
            }
        }
        return hits;
    }

    /// <summary>阶段12-A4：为 AI 选择核弹目标。50% 优先玩家基地，其余随机选其他非己方基地。</summary>
    private Vector2? FindNukeTargetForAi(int firingTeamId)
    {
        var candidates = new List<Building>();
        foreach (var kv in _bases)
        {
            if (kv.Key != firingTeamId && IsInstanceValid(kv.Value))
                candidates.Add(kv.Value);
        }
        if (candidates.Count == 0) return null;

        // 50% 概率优先打击玩家基地（若有）
        if (GD.Randf() < 0.5f
            && _bases.TryGetValue(PlayerTeamId, out var pb)
            && IsInstanceValid(pb)
            && firingTeamId != PlayerTeamId)
        {
            return pb.GlobalPosition;
        }

        // 否则随机任选一个非己方基地
        int idx = (int)GD.RandRange(0, candidates.Count - 1);
        return candidates[idx].GlobalPosition;
    }

    /// <summary>单位开火音效：根据单位类型选择不同音效和音调。</summary>
    public void PlayUnitFireSfx(UnitType type)
    {
        if (_audio == null) return;
        switch (type)
        {
            case UnitType.Infantry:
            case UnitType.Engineer:
            case UnitType.Sapper:
            case UnitType.ChiefEngineer:
                // 步兵用高频muzzle
                _audio.PlaySfx(AudioManager.Sfx.Muzzle, 1.2f);
                break;
            case UnitType.Artillery:
            case UnitType.RocketLauncher:
            case UnitType.MissileTank:
                // 远程用低沉cannon
                _audio.PlaySfx(AudioManager.Sfx.Cannon, 0.8f);
                _audio.PlaySfx(AudioManager.Sfx.Muzzle, 0.6f);
                break;
            default:
                // 坦克通用
                _audio.PlaySfx(AudioManager.Sfx.Cannon);
                _audio.PlaySfx(AudioManager.Sfx.Muzzle, 0.9f);
                break;
        }
    }

    /// <summary>单位死亡音效。</summary>
    public void PlayUnitDeathSfx(UnitType type)
    {
        if (_audio == null) return;
        switch (type)
        {
            case UnitType.HeavyTank:
                _audio.PlaySfx(AudioManager.Sfx.BigExplosion);
                break;
            default:
                _audio.PlaySfx(AudioManager.Sfx.UnitDie);
                break;
        }
    }

    /// <summary>建筑被毁音效。</summary>
    public void PlayBuildingDestroyedSfx()
    {
        _audio?.PlaySfxForce(AudioManager.Sfx.BigExplosion);
    }

    /// <summary>G7: 执行间谍任务效果（成功时由Unit.ProcessSpyInfiltrate调用）。</summary>
    public void ExecuteSpyMission(SpyMission.MissionType mission, Building target, int spyTeamId)
    {
        switch (mission)
        {
            case SpyMission.MissionType.StealTech:
                // 窃取科技：从敌方已研究中找一个己方未研究的，免费完成
                var enemyTeam = target.TeamId;
                if (_techProgress.Length > enemyTeam && _techProgress.Length > spyTeamId)
                {
                    var enemyCompleted = _techProgress[enemyTeam].Completed;
                    var myCompleted = _techProgress[spyTeamId].Completed;
                    TechTree.TechId? stolenTech = null;
                    foreach (var tid in enemyCompleted)
                    {
                        if (!myCompleted.Contains(tid))
                        {
                            stolenTech = tid;
                            break;
                        }
                    }
                    if (stolenTech.HasValue)
                    {
                        _techProgress[spyTeamId].ForceComplete(stolenTech.Value);
                        ApplyTechEffects(spyTeamId);
                        var nodeName = TechTree.Nodes[stolenTech.Value];
                        GameLog.Debug($"[G7] 间谍窃取科技成功: {nodeName.Name} (Team {spyTeamId})");
                        ShowToast(spyTeamId == 0 ? $"间谍窃取: {nodeName.Name}" : $"AI Team {spyTeamId} 间谍窃取科技");
                    }
                    else
                    {
                        // 敌方无可窃取科技，补偿$300
                        AddResourceForTeam(spyTeamId, 300);
                        GameLog.Debug($"[G7] 间谍无可窃取科技，获得$300补偿 (Team {spyTeamId})");
                        ShowToast(spyTeamId == 0 ? "间谍: 无可窃取科技, $300补偿" : "");
                    }
                }
                break;

            case SpyMission.MissionType.SabotagePower:
                // 破坏电网：电站断电8秒
                if (IsInstanceValid(target))
                {
                    target.PowerConsumed += 200;
                    GameLog.Debug($"[G7] 间谍破坏电网: {target.BuildingName} 断电{(int)SpyMission.SabotagePowerDuration}秒 (Team {target.TeamId})");
                    ShowToast(spyTeamId == 0 ? $"间谍破坏: {target.BuildingName}断电" : "");
                    // 延迟恢复
                    DelayedRestoreSpySabotage(target, SpyMission.SabotagePowerDuration, 200);
                }
                break;

            case SpyMission.MissionType.StealMoney:
                // 窃取资金
                int stolen = Mathf.Min(SpyMission.StealMoneyAmount, GetMoney(target.TeamId));
                if (stolen > 0)
                {
                    SpendMoney(target.TeamId, stolen);
                    AddResourceForTeam(spyTeamId, stolen);
                    GameLog.Debug($"[G7] 间谍窃取${stolen} (Team {target.TeamId} → Team {spyTeamId})");
                    ShowToast(spyTeamId == 0 ? $"间谍窃取: ${stolen}" : "");
                }
                break;

            case SpyMission.MissionType.SabotageProd:
                // 瘫痪生产：兵营/车厂暂停生产10秒
                if (IsInstanceValid(target))
                {
                    // 通过大幅增加PowerConsumed使建筑离线
                    target.PowerConsumed += 500;
                    GameLog.Debug($"[G7] 间谍瘫痪生产: {target.BuildingName} 暂停{(int)SpyMission.SabotageProdDuration}秒");
                    ShowToast(spyTeamId == 0 ? $"间谍瘫痪: {target.BuildingName}停工" : "");
                    DelayedRestoreSpySabotage(target, SpyMission.SabotageProdDuration, 500);
                }
                break;

            case SpyMission.MissionType.Recon:
                // 侦察：揭示敌方建筑/单位信息（通过Toast通知）
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"侦察: {target.BuildingName} (Team {target.TeamId})");
                sb.AppendLine($"  血量: {(int)target.Health}/{(int)target.MaxHealth}");
                sb.AppendLine($"  电力: +{target.PowerProvided}/-{target.PowerConsumed}");
                // 统计附近敌方单位
                int nearbyEnemies = 0;
                foreach (var c in _unitsNode.GetChildren())
                {
                    if (c is Unit u && u.TeamId == target.TeamId && IsInstanceValid(u)
                        && u.GlobalPosition.DistanceTo(target.GlobalPosition) < 300f)
                        nearbyEnemies++;
                }
                sb.AppendLine($"  附近敌方单位: {nearbyEnemies}");
                GameLog.Debug($"[G7] 间谍侦察: {sb.ToString().Trim()}");
                if (spyTeamId == 0) ShowToast(sb.ToString().Trim());
                break;
        }
    }

    /// <summary>G7: AI间谍任务 — 派空闲间谍渗透最近的敌方高价值建筑。</summary>
    private void AISpyMission(int teamId)
    {
        // 找到空闲间谍
        Unit? idleSpy = null;
        foreach (var c in _unitsNode.GetChildren())
        {
            if (c is Unit u && u.TeamId == teamId && u.Type == UnitType.Spy && IsInstanceValid(u) && !u.IsSpyOnMission)
            {
                idleSpy = u;
                break;
            }
        }
        if (idleSpy == null) return;

        // 找最近的敌方高价值建筑（优先科技中心 > 基地 > 电站 > 兵营/车厂）
        Building? target = null;
        float bestDist = float.MaxValue;
        BuildingType[] priority = { BuildingType.TechCenter, BuildingType.Base, BuildingType.PowerPlant, BuildingType.Barracks, BuildingType.WarFactory };
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b.TeamId != teamId && IsInstanceValid(b))
            {
                // 只选我们优先列表中的类型
                bool isPriority = false;
                foreach (var pt in priority)
                {
                    if (b.Type == pt) { isPriority = true; break; }
                }
                if (!isPriority) continue;

                float dist = idleSpy.GlobalPosition.DistanceTo(b.GlobalPosition);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    target = b;
                }
            }
        }

        if (target != null)
        {
            var mission = SpyMission.ChooseMission(target.Type);
            idleSpy.CommandSpyMission(target, mission);
            GameLog.Debug($"[G7] AI Team {teamId} 派间谍执行 {SpyMission.MissionName(mission)} → {target.BuildingName}");
        }
    }

    /// <summary>G7: 延迟恢复间谍破坏（电站/生产恢复）。</summary>
    private async void DelayedRestoreSpySabotage(Building b, float delay, int powerRestore)
    {
        await ToSignal(GetTree().CreateTimer(delay), "timeout");
        if (IsInstanceValid(b))
        {
            b.PowerConsumed -= powerRestore;
            if (b.PowerConsumed < 0) b.PowerConsumed = 0;
            GameLog.Debug($"[G7] 间谍破坏效果恢复: {b.BuildingName}");
        }
    }

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

}
