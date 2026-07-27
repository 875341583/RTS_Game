using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RTSGame;

/// <summary>
/// Main 的输入/选择/热键控制器（partial class）。
/// 包含：_Input + 命令键 + 右键命令 + 框选 + Pick拾取 + 编队 + 截图。
/// </summary>
public partial class Main
{

    /// <summary>L1修复: 检查是否有任何信息面板处于打开状态（面板打开时禁用生产热键避免冲突）。</summary>
    private bool AnyPanelOpen()
    {
        return _techTreePanelVisible || _eraPanelVisible || _powerGridPanelVisible
            || _adjacencyPanelVisible || _spyMissionPanelVisible || _capturePanelVisible
            || _eurekaLabel.Visible || _cardLabel.Visible;
    }

    public override void _Input(InputEvent @event)
    {
        // P0-2: F5/F9 快捷键存读档 — 即使游戏结束也可用
        if (@event is InputEventKey k && k.Pressed && !k.Echo)
        {
            if (k.Keycode == Key.F5) { QuickSave(); return; }
            if (k.Keycode == Key.F9) { QuickLoad(); return; }
        }

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
                // 阶段12-A4：闪电风暴目标选择模式（与核弹互斥优先）
                if (_lightningTargetMode && !mouseOverPanel)
                {
                    ApplyLightning(worldPos, PlayerTeamId);
                    _lightningTargetMode = false;
                    _playerLightningCooldown = GameConst.LightningCooldown;
                    QueueRedraw();
                    return;
                }
                // 阶段12-A4：核弹目标选择模式优先（左键释放核弹）
                if (_nukeTargetMode && !mouseOverPanel)
                {
                    ApplyNuke(worldPos, PlayerTeamId);
                    _nukeTargetMode = false;
                    _playerNukeCooldown = GameConst.NukeCooldown;
                    QueueRedraw();
                    return;
                }
                // E10：巡航导弹目标选择模式
                if (_missileTargetMode && !mouseOverPanel)
                {
                    ApplyCruiseMissile(worldPos, PlayerTeamId);
                    _missileTargetMode = false;
                    _playerMissileCooldown = GameConst.MissileCooldown;
                    QueueRedraw();
                    return;
                }
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
                if (_lightningTargetMode) { _lightningTargetMode = false; QueueRedraw(); return; }
                if (_nukeTargetMode) { _nukeTargetMode = false; QueueRedraw(); return; }
                if (_missileTargetMode) { _missileTargetMode = false; QueueRedraw(); return; }
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

        // G1: Tab键打开/关闭科技树面板
        if (kc == Key.Tab)
        {
            _techTreePanelVisible = !_techTreePanelVisible;
            _techTreeLabel.Visible = _techTreePanelVisible;
            if (_techTreePanelVisible) UpdateTechTreePanel();
            return;
        }

        // G2: Y键打开/关闭时代面板
        if (kc == Key.Y)
        {
            _eraPanelVisible = !_eraPanelVisible;
            _eraLabel.Visible = _eraPanelVisible;
            if (_eraPanelVisible) UpdateEraPanel();
            return;
        }

        // G2: 时代面板可见时，按U键升级时代
        if (_eraPanelVisible && kc == Key.U)
        {
            TryAdvanceEra();
            UpdateEraPanel();
            return;
        }

        // G3: T键查看当前战术卡
        if (kc == Key.T)
        {
            ShowCardStatus();
            return;
        }

        // G4: G键查看电网分布
        if (kc == Key.G)
        {
            _powerGridPanelVisible = !_powerGridPanelVisible;
            _powerGridLabel.Visible = _powerGridPanelVisible;
            if (_powerGridPanelVisible) UpdatePowerGridPanel();
            return;
        }

        // G5: H键查看尤里卡进度
        if (kc == Key.H)
        {
            _eurekaLabel.Visible = !_eurekaLabel.Visible;
            if (_eurekaLabel.Visible) UpdateEurekaPanel();
            return;
        }

        // G6: J键查看邻接加成
        if (kc == Key.J)
        {
            _adjacencyPanelVisible = !_adjacencyPanelVisible;
            _adjacencyLabel.Visible = _adjacencyPanelVisible;
            if (_adjacencyPanelVisible) UpdateAdjacencyPanel();
            return;
        }

        // G7: N键查看间谍任务
        if (kc == Key.N)
        {
            _spyMissionPanelVisible = !_spyMissionPanelVisible;
            _spyMissionLabel.Visible = _spyMissionPanelVisible;
            if (_spyMissionPanelVisible) UpdateSpyMissionPanel();
            return;
        }

        // G8: K键查看占领状态
        if (kc == Key.K)
        {
            _capturePanelVisible = !_capturePanelVisible;
            _captureLabel.Visible = _capturePanelVisible;
            if (_capturePanelVisible) UpdateCapturePanel();
            return;
        }

        // G3: 战术卡选择面板可见时，1/2/3选择对应卡
        if (_cardLabel.Visible && _cardChoices.Length > 0)
        {
            int cardIdx = kc switch
            {
                Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2,
                _ => -1
            };
            if (cardIdx >= 0 && cardIdx < _cardChoices.Length)
            {
                SelectPlayerCard(_cardChoices[cardIdx]);
                return;
            }
        }

        // G1: 科技面板可见时，数字键1-9研究对应行科技
        if (_techTreePanelVisible)
        {
            int techNum = kc switch
            {
                Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2,
                Key.Key4 => 3, Key.Key5 => 4, Key.Key6 => 5,
                Key.Key7 => 6, Key.Key8 => 7, Key.Key9 => 8,
                Key.Key0 => 9, Key.Minus => 10, Key.Equal => 11,
                _ => -1
            };
            if (techNum >= 0)
            {
                TryResearchTech(techNum);
                UpdateTechTreePanel();
                return;
            }
        }

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
                GameLog.Debug($"[操控] 攻击移动模式 {(_attackMoveMode ? "开启 - 左键点地发起" : "关闭")}");
            }
        }
        else if (kc == Key.X)
        {
            var sel = GetSelectedFriendlyUnits();
            if (sel.Count > 0)
            {
                foreach (var u in sel) u.CommandStop();
                GameLog.Debug($"[操控] 停止 ({sel.Count} 单位)");
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
                        GameLog.Debug($"[维修] {b.BuildingName} 已修复满血，扣 ${cost}，剩余 ${_money[0]}");
                    }
                    else
                    {
                        GameLog.Debug($"[维修] 资金不足！维修{b.BuildingName}需要 ${cost}，当前 ${_money[0]}");
                    }
                }
            }
            if (repaired == 0)
            {
                GameLog.Debug("[维修] 没有可维修的建筑（需选中受损的蓝方建筑）");
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
                GameLog.Debug($"[出售] {b.BuildingName} 已出售，回收 ${refund}，资金 ${_money[0]}");
                // P0-1: 移除PathFinder障碍并取消事件订阅（H4修复）
                OnBuildingDestroyed(b);
                b.Destroyed -= OnBuildingDestroyed;
                b.QueueFree();
            }
            if (toSell.Count == 0)
            {
                GameLog.Debug("[出售] 没有可出售的建筑（基地不可出售）");
            }
        }
        else if (kc == Key.Z)
        {
            // 阶段12-A4：核弹超武（需科技中心，5分钟冷却）
            // 注：N键已被InputMap占用为spawn_heavy（重坦），故核弹改用Z键
            // E10：核弹需核弹发射井建筑
            if (!HasBuilding(PlayerTeamId, BuildingType.NukeSilo))
            {
                ShowToast("☢ 核弹不可用：需建造核弹发射井", new Color(1f, 0.5f, 0.3f));
                GameLog.Debug("[核弹] 不可用：需核弹发射井");
            }
            else if (_playerNukeCooldown > 0f)
            {
                int sec = Mathf.CeilToInt(_playerNukeCooldown);
                ShowToast($"☢ 核弹冷却中：{sec / 60}:{sec % 60:D2}", new Color(1f, 0.6f, 0.3f));
                GameLog.Debug($"[核弹] 冷却中：{sec}s");
            }
            else
            {
                _nukeTargetMode = !_nukeTargetMode;
                if (_nukeTargetMode) _lightningTargetMode = false; // 与闪电风暴互斥
                if (_nukeTargetMode) _missileTargetMode = false;   // 与巡航导弹互斥
                if (_nukeTargetMode)
                    ShowToast("☢ 核弹已就绪：左键点击目标 / 右键取消", new Color(1f, 0.3f, 0.2f));
                GameLog.Debug($"[核弹] 目标选择模式 {(_nukeTargetMode ? "开启" : "关闭")}");
                QueueRedraw();
            }
        }
        else if (kc == Key.C)
        {
            // 阶段12-A4：闪电风暴超武（需科技中心，4分钟冷却，持续5秒范围伤害）
            // 注：C 键原本未占用，用作闪电 Storm（雷电英文首字母冲突多，用 C 取"持续伤害"意）
            // E10：闪电风暴需闪电风暴塔建筑
            if (!HasBuilding(PlayerTeamId, BuildingType.LightningTower))
            {
                ShowToast("⚡ 闪电不可用：需建造闪电风暴塔", new Color(0.5f, 0.7f, 1f));
                GameLog.Debug("[闪电] 不可用：需闪电风暴塔");
            }
            else if (_playerLightningCooldown > 0f)
            {
                int sec = Mathf.CeilToInt(_playerLightningCooldown);
                ShowToast($"⚡ 闪电风暴冷却中：{sec / 60}:{sec % 60:D2}", new Color(0.5f, 0.7f, 1f));
                GameLog.Debug($"[闪电] 冷却中：{sec}s");
            }
            else
            {
                _lightningTargetMode = !_lightningTargetMode;
                if (_lightningTargetMode) _nukeTargetMode = false; // 与核弹互斥
                if (_lightningTargetMode) _missileTargetMode = false; // 与导弹互斥
                if (_lightningTargetMode)
                    ShowToast("⚡ 闪电风暴已就绪：左键点击目标 / 右键取消", new Color(0.5f, 0.8f, 1f));
                GameLog.Debug($"[闪电] 目标选择模式 {(_lightningTargetMode ? "开启" : "关闭")}");
                QueueRedraw();
            }
        }
        // E10：巡航导弹超武（Shift+V，需导弹发射井，3分钟冷却）
        else if (kc == Key.V && Input.IsKeyPressed(Key.Shift))
        {
            if (!HasBuilding(PlayerTeamId, BuildingType.MissileSilo))
            {
                ShowToast("🚀 导弹不可用：需建造导弹发射井", new Color(1f, 0.8f, 0.3f));
                GameLog.Debug("[导弹] 不可用：需导弹发射井");
            }
            else if (_playerMissileCooldown > 0f)
            {
                int sec = Mathf.CeilToInt(_playerMissileCooldown);
                ShowToast($"🚀 导弹冷却中：{sec / 60}:{sec % 60:D2}", new Color(1f, 0.8f, 0.5f));
                GameLog.Debug($"[导弹] 冷却中：{sec}s");
            }
            else
            {
                _missileTargetMode = !_missileTargetMode;
                if (_missileTargetMode) _nukeTargetMode = false;
                if (_missileTargetMode) _lightningTargetMode = false;
                if (_missileTargetMode)
                    ShowToast("🚀 巡航导弹已就绪：左键点击目标 / 右键取消", new Color(1f, 0.8f, 0.3f));
                GameLog.Debug($"[导弹] 目标选择模式 {(_missileTargetMode ? "开启" : "关闭")}");
                QueueRedraw();
            }
        }
    }

    private int SquadIndexFromKey(Key kc)
    {
        int v = (int)kc, a = (int)Key.Key1, b = (int)Key.Key9;
        return (v >= a && v <= b) ? v - a : -1;
    }

    private void SaveSquad(int idx)
    {
        _squads[idx] = GetSelectedFriendlyUnits();
        GameLog.Debug($"[编队] 编队{idx + 1} 已保存 ({_squads[idx].Count} 单位)");
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
        GameLog.Debug($"[编队] 选取编队{idx + 1} ({_selected.Count} 单位)");
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
        GameLog.Debug($"[操控] 攻击移动 -> {worldPos} ({list.Count} 单位)");
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
            GameLog.Debug($"[截图] 已保存: {ProjectSettings.GlobalizePath(path)} 尺寸={img.GetSize()}");
        }
        catch (Exception ex) { GameLog.Error($"[截图] 失败: {ex.Message}"); }
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
                // U1: 右键点在建筑自身上 → 取消队列中最后一个生产订单
                var clickedBuilding = PickBuildingAt(worldPos, requireEnemy: false);
                if (clickedBuilding == producer)
                {
                    var cancelled = producer.CancelLastProduction();
                    if (cancelled.HasValue)
                    {
                        GameLog.Debug($"[取消生产] {producer.BuildingName} 取消: {cancelled.Value}");
                        _audio?.PlaySfx(AudioManager.Sfx.UiClick);
                    }
                    return;
                }
                // 否则设集结点
                producer.SetRallyPoint(worldPos);
                GameLog.Debug($"[集结点] {producer.BuildingName} 集结点 -> {worldPos}");
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
            // P2-3: 播放攻击语音
            var attacker = friendlyUnits.FirstOrDefault();
            if (attacker != null)
                _audio?.PlayUnitVoice(attacker.Type, UnitVoice.VoiceType.Attack);
            return;
        }
        // 点击敌方建筑 → 攻击建筑（G7: 间谍则执行间谍任务）
        var enemyBuilding = PickBuildingAt(worldPos, requireEnemy: true);
        if (enemyBuilding != null)
        {
            // G7: 间谍右键敌方建筑 → 触发间谍任务
            var spyUnits = friendlyUnits.Where(u => u.Type == UnitType.Spy).ToList();
            var nonSpyUnits = friendlyUnits.Where(u => u.Type != UnitType.Spy).ToList();

            // 非间谍单位正常攻击建筑
            foreach (var unit in nonSpyUnits)
                unit.CommandAttackBuilding(enemyBuilding);

            // 间谍执行任务
            foreach (var spy in spyUnits)
            {
                if (spy.IsSpyOnMission) continue; // 已在执行任务
                var mission = SpyMission.ChooseMission(enemyBuilding.Type);
                spy.CommandSpyMission(enemyBuilding, mission);
            }
            // P2-3: 播放攻击语音（非间谍单位）
            var atkUnit = nonSpyUnits.FirstOrDefault();
            if (atkUnit != null)
                _audio?.PlayUnitVoice(atkUnit.Type, UnitVoice.VoiceType.Attack);
            return;
        }

        // E6：步兵点击友方运输车 → 上车
        var friendlyTransport = PickTransportAt(worldPos, requireFriendly: true);
        if (friendlyTransport != null)
        {
            foreach (var unit in friendlyUnits)
            {
                if (Unit.IsInfantryType(unit.Type) && unit != friendlyTransport)
                {
                    // 步兵移动到运输车位置后上车
                    unit.CommandMove(friendlyTransport.GlobalPosition);
                    // 在到达时通过 ProcessInteraction 完成上车
                    unit._embarkTarget = friendlyTransport;
                }
            }
            if (friendlyUnits.Any(u => Unit.IsInfantryType(u.Type)))
                return; // 有步兵上车命令，不执行移动
        }
        // 普通移动：保持简易队形
        int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(friendlyUnits.Count)));
        // E4：工程单位右键不可通行地形 → 触发地形改造
        var terrainCell = _terrain.GetCellAtWorld(worldPos.X, worldPos.Y);
        Unit.TerrainModType modType = DetectTerrainMod(terrainCell);
        if (modType != Unit.TerrainModType.None)
        {
            // 仅工程单位执行地形改造，非工程单位仍正常移动
            for (int i = 0; i < friendlyUnits.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                if (friendlyUnits[i].IsEngineerUnit)
                    friendlyUnits[i].CommandTerrainMod(modType, worldPos + new Vector2(col * 40, row * 40));
                else
                    friendlyUnits[i].CommandMove(worldPos + new Vector2(col * 40, row * 40));
            }
        }
        else
        {
            for (int i = 0; i < friendlyUnits.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                friendlyUnits[i].CommandMove(worldPos + new Vector2(col * 40, row * 40));
            }
        }
        // 阶段12-C：下令移动音效
        _audio?.PlaySfx(AudioManager.Sfx.Move);
        // P2-3: 播放移动语音
        var mover = friendlyUnits.FirstOrDefault();
        if (mover != null)
            _audio?.PlayUnitVoice(mover.Type, UnitVoice.VoiceType.Move);
    }

    /// <summary>E4：检测右键位置需要的地形改造类型。</summary>
    private Unit.TerrainModType DetectTerrainMod(TerrainCell cell)
    {
        // 山脉→削平
        if (cell.Type == TerrainType.Mountain && !cell.HasTunnel)
            return Unit.TerrainModType.Flatten;
        // 深水→架桥
        if (cell.Type == TerrainType.DeepWater && !cell.HasBridge && !cell.HasTunnel)
            return Unit.TerrainModType.Bridge;
        // 浅水→架桥
        if (cell.Type == TerrainType.ShallowWater && !cell.HasBridge)
            return Unit.TerrainModType.Bridge;
        return Unit.TerrainModType.None;
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

        // 阶段12-C：选中单位音效
        if (_selected.Count > 0)
        {
            _audio?.PlaySfx(AudioManager.Sfx.Select);
            // P2-3: 播放单位选择语音（取第一个单位类型）
            var firstUnit = _selected.OfType<Unit>().FirstOrDefault();
            if (firstUnit != null)
                _audio?.PlayUnitVoice(firstUnit.Type, UnitVoice.VoiceType.Select);
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

    // E6：拾取友方运输车
    private Unit? PickTransportAt(Vector2 worldPos, bool requireFriendly)
    {
        foreach (var child in _unitsNode.GetChildren())
        {
            if (child is Unit u && IsInstanceValid(u) && u.IsTransport)
            {
                if (requireFriendly && u.TeamId != 0) continue;
                if (!requireFriendly && u.TeamId == 0) continue;
                if (u.GlobalPosition.DistanceTo(worldPos) < 36f)
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
}
