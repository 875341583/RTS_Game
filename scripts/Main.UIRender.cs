using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RTSGame;

/// <summary>
/// Main 的UI/渲染/地图生成控制器（partial class）。
/// 包含：地面纹理刷新 + 地图生成(矿石/油田/障碍/战略点) + Toast通知 + UpdateUI聚合 + 建筑列表。
/// </summary>
public partial class Main
{
    /// <summary>重新生成地面纹理（地形改造后调用）。</summary>
    public void RefreshGroundTexture()
    {
        // 移除旧的地面精灵
        if (_groundSprite != null)
        {
            RemoveChild(_groundSprite);
            _groundSprite.QueueFree();
            _groundSprite = null!;
        }
        // 重新生成（使用同一TerrainGrid数据，已包含改造后的内容）
        CreateGround();
    }

    private void CreateGround()
    {
        // 等距地形渲染（路线C：菱形顶面 + 高度侧面 + 悬崖）
        var isoImg = IsoTerrainRenderer.RenderTerrain(_terrain, _mapRng);
        var (offX, offY) = IsoTerrainRenderer.GetRenderOffset();

        var groundTex = ImageTexture.CreateFromImage(isoImg);
        _groundSprite = new Sprite2D
        {
            Name = "Ground",
            Texture = groundTex,
            Centered = false,
            ZIndex = -3,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest
        };
        // 等距地图偏移：菱形地图原点在 (offX, offY)，.Sprite2D的OffsetLeft需要设置为使得
        // 网格(0,0)的等距屏幕坐标对应到世界坐标(0,0)
        // 等距地图左上角 = grid(0,0) 的屏幕坐标 = (0*HalfW, 0*HalfH) = (0, 0)
        // 但渲染时偏移了 offX = gs*HalfW，所以Sprite2D需要左移 offX
        _groundSprite.Position = new Vector2(-offX, offY);
        AddChild(_groundSprite);
        MoveChild(_groundSprite, 0); // 最底层

        GD.Print($"[IsoTerrain] 等距地形渲染完成，图尺寸: {isoImg.GetWidth()}x{isoImg.GetHeight()}，偏移: ({offX}, {offY})");
    }

    private static void EnsureGroundTileTextures()
    {
        if (_grass1Tex != null) return;
        _grass1Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass1.png");
        _grass2Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass2.png");
        _grass3Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass3.png");
        _grass4Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass4.png");
        _sand1Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileSand1.png");
        _sand2Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileSand2.png");
        _sand3Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileSand3.png");
        _roadETex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass_roadEast.png");
        _roadNTex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass_roadNorth.png");
        _roadCrossTex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass_roadCrossing.png");
        // E1 新增地形
        _shallow1Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileShallow1.png");
        _shallow2Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileShallow2.png");
        _shallow3Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileShallow3.png");
        _deep1Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileDeep1.png");
        _deep2Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileDeep2.png");
        _deep3Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileDeep3.png");
        _mountain1Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileMountain1.png");
        _mountain2Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileMountain2.png");
        _mountain3Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileMountain3.png");
        _snow1Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileSnow1.png");
        _snow2Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileSnow2.png");
        _snow3Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileSnow3.png");
        _city1Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileCity1.png");
        _city2Tex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileCity2.png");
        _field1Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileField1.png");
        _field2Tex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileField2.png");
        _bridgeTex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileBridge.png");
        _tunnelTex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileTunnel.png");
        _cliffTex  = GD.Load<Texture2D>("res://assets/sprites/terrain/tileCliff.png");
    }

    private void SpawnObstacle(Vector2 pos, Vector2 size)
    {
        EnsureObstacleTextures();
        var body = new StaticBody2D();
        body.GlobalPosition = pos;
        body.CollisionLayer = 1; // Terrain
        body.CollisionMask = 0;

        var shape = new CollisionShape2D();
        var rect = new RectangleShape2D();
        rect.Size = size;
        shape.Shape = rect;
        body.AddChild(shape);

        // Visual
        var sprite = new Sprite2D();
        bool isWall = size.X > size.Y * 1.5f || size.Y > size.X * 1.5f;
        sprite.Texture = isWall ? _wallTex! : _rockTex!;
        sprite.Scale = new Vector2(size.X / 80f, size.Y / 80f);
        body.AddChild(sprite);

        _obstaclesNode.AddChild(body);
    }

    private static void EnsureObstacleTextures()
    {
        if (_rockTex != null) return;

        // Kenney 环境素材（CC0）
        _rockTex = GD.Load<Texture2D>("res://assets/sprites/environment/crateMetal.png");
        if (_rockTex == null)
        {
            GD.PrintErr("[Obstacle] Failed to load crateMetal.png");
            var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            img.SetPixel(0, 0, Colors.Magenta);
            _rockTex = ImageTexture.CreateFromImage(img);
        }

        _wallTex = GD.Load<Texture2D>("res://assets/sprites/environment/sandbagBrown.png");
        if (_wallTex == null)
        {
            GD.PrintErr("[Obstacle] Failed to load sandbagBrown.png");
            var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
            img.SetPixel(0, 0, Colors.Magenta);
            _wallTex = ImageTexture.CreateFromImage(img);
        }
    }

    private void SpawnStrategicPoint(Vector2 pos)
    {
        var sp = new StrategicPoint();
        sp.GlobalPosition = pos;
        _strategicPointsNode.AddChild(sp);
    }

    // ========== 阶段12-B 种子驱动地图生成 ==========

    /// <summary>种子驱动生成中场争夺矿 + 中央高价值矿。位置围绕地图中央随机散布。</summary>
    private void GenerateRandomOreDeposits()
    {
        var center = new Vector2(MapSize * 0.5f, MapSize * 0.5f);

        // 4 个中场争夺矿（1200 资源）：在距中央 350-550px 的环形带上随机分布
        for (int i = 0; i < 4; i++)
        {
            float angle = (float)(_mapRng.NextDouble() * Mathf.Pi * 2);
            float dist = 350f + (float)(_mapRng.NextDouble() * 200f);
            var pos = center + new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);
            pos = ClampToMap(pos, 100f);
            SpawnOre(pos, 1200);
        }

        // 中央高价值矿（2000 资源）：在地图正中央附近小幅偏移
        float centralOffsetX = (float)(_mapRng.NextDouble() - 0.5) * 80f;
        float centralOffsetY = (float)(_mapRng.NextDouble() - 0.5) * 80f;
        SpawnOre(center + new Vector2(centralOffsetX, centralOffsetY), 2000);

        // 2 个中央外围矿（1500 资源）
        for (int i = 0; i < 2; i++)
        {
            float angle = (float)(_mapRng.NextDouble() * Mathf.Pi * 2);
            float dist = 120f + (float)(_mapRng.NextDouble() * 60f);
            var pos = center + new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);
            SpawnOre(pos, 1500);
        }

        GD.Print($"[Map] 矿点生成完毕（种子 {_mapSeed}）");
    }

    // ========== E5 资源扩展生成 ==========

    /// <summary>生成油田（占领后持续产钱）。3-4个，分布在道路附近和资源争夺区。</summary>
    private void GenerateOilFields()
    {
        var oilPositions = _terrain.GetOilFieldPositions();
        if (oilPositions.Count == 0)
        {
            GD.Print("[E5] 没有合适的油田位置，跳过");
            return;
        }

        // 随机选取3-4个位置
        int count = 3 + _mapRng.Next(2); // 3-4个
        // 打乱位置列表
        for (int i = oilPositions.Count - 1; i > 0; i--)
        {
            int j = _mapRng.Next(i + 1);
            (oilPositions[i], oilPositions[j]) = (oilPositions[j], oilPositions[i]);
        }

        int placed = 0;
        for (int i = 0; i < oilPositions.Count && placed < count; i++)
        {
            var (gx, gy) = oilPositions[i];
            var worldPos = new Vector2(gx * TerrainGrid.TileSize + TerrainGrid.TileSize / 2f,
                                        gy * TerrainGrid.TileSize + TerrainGrid.TileSize / 2f);

            // 避免和其他资源/战略点太近
            if (IsTooCloseToExistingResource(worldPos, 200f)) continue;
            // 避免在基地附近
            if (IsTooCloseToBasePos(worldPos, 250f)) continue;

            SpawnOilField(worldPos);
            placed++;
        }

        GD.Print($"[E5] 油田生成完毕：{placed} 个");
    }

    /// <summary>生成稀有矿（采集收益×2）。2-3个，分布在山脉附近高地。</summary>
    private void GenerateRareMinerals()
    {
        var rarePositions = _terrain.GetRareMineralPositions();
        if (rarePositions.Count == 0)
        {
            GD.Print("[E5] 没有合适的稀有矿位置，跳过");
            return;
        }

        int count = 2 + _mapRng.Next(2); // 2-3个
        for (int i = rarePositions.Count - 1; i > 0; i--)
        {
            int j = _mapRng.Next(i + 1);
            (rarePositions[i], rarePositions[j]) = (rarePositions[j], rarePositions[i]);
        }

        int placed = 0;
        for (int i = 0; i < rarePositions.Count && placed < count; i++)
        {
            var (gx, gy) = rarePositions[i];
            var worldPos = new Vector2(gx * TerrainGrid.TileSize + TerrainGrid.TileSize / 2f,
                                        gy * TerrainGrid.TileSize + TerrainGrid.TileSize / 2f);

            if (IsTooCloseToExistingResource(worldPos, 180f)) continue;
            if (IsTooCloseToBasePos(worldPos, 200f)) continue;

            SpawnRareMineral(worldPos, 1500 + _mapRng.Next(500)); // 1500-2000储量
            placed++;
        }

        GD.Print($"[E5] 稀有矿生成完毕：{placed} 个");
    }

    /// <summary>生成陆地矿脉（散布广、储值低、数量多）。8-12个，遍布可通行陆地。</summary>
    private void GenerateLandVeins()
    {
        var veinPositions = _terrain.GetSuitableResourcePositions(1, 1, false, false);
        if (veinPositions.Count == 0)
        {
            GD.Print("[E5] 没有合适的陆地矿脉位置，跳过");
            return;
        }

        int count = 8 + _mapRng.Next(5); // 8-12个
        for (int i = veinPositions.Count - 1; i > 0; i--)
        {
            int j = _mapRng.Next(i + 1);
            (veinPositions[i], veinPositions[j]) = (veinPositions[j], veinPositions[i]);
        }

        int placed = 0;
        for (int i = 0; i < veinPositions.Count && placed < count; i++)
        {
            var (gx, gy) = veinPositions[i];
            var worldPos = new Vector2(gx * TerrainGrid.TileSize + TerrainGrid.TileSize / 2f,
                                        gy * TerrainGrid.TileSize + TerrainGrid.TileSize / 2f);

            if (IsTooCloseToExistingResource(worldPos, 120f)) continue;
            if (IsTooCloseToBasePos(worldPos, 150f)) continue;

            SpawnLandVein(worldPos, 300 + _mapRng.Next(200)); // 300-500储量
            placed++;
        }

        GD.Print($"[E5] 陆地矿脉生成完毕：{placed} 个");
    }

    /// <summary>生成油田节点。</summary>
    private void SpawnOilField(Vector2 pos)
    {
        var o = _oreScene.Instantiate<ResourceNode>();
        o.ResourceType = ResourceType.OilField;
        o.InitialAmount = 0; // 油田不可被采集，无储量
        o.GlobalPosition = pos;
        _resourcesNode.AddChild(o);
    }

    /// <summary>生成稀有矿节点。</summary>
    private void SpawnRareMineral(Vector2 pos, int amount)
    {
        var o = _oreScene.Instantiate<ResourceNode>();
        o.ResourceType = ResourceType.RareMineral;
        o.InitialAmount = amount;
        o.GlobalPosition = pos;
        _resourcesNode.AddChild(o);
    }

    /// <summary>生成陆地矿脉节点。</summary>
    private void SpawnLandVein(Vector2 pos, int amount)
    {
        var o = _oreScene.Instantiate<ResourceNode>();
        o.ResourceType = ResourceType.LandVein;
        o.InitialAmount = amount;
        o.GlobalPosition = pos;
        _resourcesNode.AddChild(o);
    }

    /// <summary>检查世界坐标是否距离已有资源点太近。</summary>
    private bool IsTooCloseToExistingResource(Vector2 pos, float minDist)
    {
        foreach (var child in _resourcesNode.GetChildren())
        {
            if (child is ResourceNode rn && IsInstanceValid(rn))
            {
                if (rn.GlobalPosition.DistanceTo(pos) < minDist)
                    return true;
            }
        }
        // 也检查战略点
        foreach (var child in _strategicPointsNode.GetChildren())
        {
            if (child is Node2D n && IsInstanceValid(n))
            {
                if (n.GlobalPosition.DistanceTo(pos) < minDist)
                    return true;
            }
        }
        return false;
    }

    /// <summary>检查世界坐标是否距离基地位置太近。</summary>
    private bool IsTooCloseToBasePos(Vector2 pos, float minDist)
    {
        var basePositions = new Vector2[TotalTeamCount]
        {
            new(200, 200), new(1800, 1800), new(1800, 200), new(200, 1800),
            new(1000, 200), new(1000, 1800), new(200, 1000), new(1800, 1000),
        };
        foreach (var bp in basePositions)
        {
            if (pos.DistanceTo(bp) < minDist) return true;
        }
        return false;
    }

    /// <summary>种子驱动生成障碍物：中央保留 4 面墙 + 随机散布 6-10 个岩石。</summary>
    private void GenerateRandomObstacles()
    {
        var center = new Vector2(MapSize * 0.5f, MapSize * 0.5f);

        // 中央墙体（保留固定结构，形成战略通道）
        float wallOffset = 300f;
        SpawnObstacle(center + new Vector2(0, -wallOffset), new Vector2(120, 30));
        SpawnObstacle(center + new Vector2(0, wallOffset), new Vector2(120, 30));
        SpawnObstacle(center + new Vector2(-wallOffset, 0), new Vector2(30, 120));
        SpawnObstacle(center + new Vector2(wallOffset, 0), new Vector2(30, 120));

        // 随机散布岩石：6-10 个，位置避开基地（距任何基地 ≥250px）和中央墙
        int rockCount = _mapRng.Next(6, 11);
        int placed = 0;
        int attempts = 0;
        while (placed < rockCount && attempts < 50)
        {
            attempts++;
            float x = 300f + (float)(_mapRng.NextDouble() * (MapSize - 600f));
            float y = 300f + (float)(_mapRng.NextDouble() * (MapSize - 600f));
            var pos = new Vector2(x, y);

            // 避开基地附近（8 个基地位置）
            bool tooCloseToBase = false;
            var basePositions = new Vector2[]
            {
                new(200, 200), new(1800, 1800), new(1800, 200), new(200, 1800),
                new(1000, 200), new(1000, 1800), new(200, 1000), new(1800, 1000),
            };
            foreach (var bp in basePositions)
            {
                if (pos.DistanceTo(bp) < 250f) { tooCloseToBase = true; break; }
            }
            if (tooCloseToBase) continue;

            // 避开中央墙附近
            if (pos.DistanceTo(center) < 200f) continue;

            float size = 35f + (float)(_mapRng.NextDouble() * 25f);
            SpawnObstacle(pos, new Vector2(size, size));
            placed++;
        }

        GD.Print($"[Map] 障碍物生成完毕：4 墙 + {placed} 岩石（种子 {_mapSeed}）");
    }

    /// <summary>种子驱动生成战略要地：中央固定 + 2 个侧翼随机偏移。</summary>
    private void GenerateStrategicPoints()
    {
        var center = new Vector2(MapSize * 0.5f, MapSize * 0.5f);

        // 中央战略点固定
        SpawnStrategicPoint(center);

        // 两个侧翼战略点：在距中央 350-450px 处随机分布（对角线两侧）
        float angle1 = (float)(_mapRng.NextDouble() * Mathf.Pi * 2);
        float dist1 = 350f + (float)(_mapRng.NextDouble() * 100f);
        var pos1 = center + new Vector2(Mathf.Cos(angle1) * dist1, Mathf.Sin(angle1) * dist1);
        SpawnStrategicPoint(ClampToMap(pos1, 100f));

        // 第二个战略点在第一个的对角线方向
        float angle2 = angle1 + Mathf.Pi;
        float dist2 = 350f + (float)(_mapRng.NextDouble() * 100f);
        var pos2 = center + new Vector2(Mathf.Cos(angle2) * dist2, Mathf.Sin(angle2) * dist2);
        SpawnStrategicPoint(ClampToMap(pos2, 100f));

        GD.Print($"[Map] 战略点生成完毕：1 中央 + 2 侧翼（种子 {_mapSeed}）");
    }

    /// <summary>在画面顶部显示一条 Toast 通知，自动淡出。</summary>
    public void ShowToast(string message, Color? color = null)
    {
        if (_toastContainer == null) return;
        var label = new Label
        {
            Text = message,
        };
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AddThemeColorOverride("font_color", color ?? new Color(1f, 0.9f, 0.3f));
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        label.Modulate = new Color(1, 1, 1, 0); // 初始透明，淡入
        _toastContainer.AddChild(label);
        _activeToasts.Add(new ToastEntry { Label = label, Lifetime = 3f, Age = 0f });
    }

    private void UpdateUI()
    {
        int playerUnits = CountUnitsOfTeam(PlayerTeamId);
        int playerPower = GetTeamPower(PlayerTeamId);
        int oreCount = 0;
        foreach (var c in _resourcesNode.GetChildren())
            if (c is ResourceNode && IsInstanceValid((Node)c)) oreCount++;

        string playerBuildings = GetBuildingList(PlayerTeamId);
        string powerWarn = playerPower < 0 ? "  [电力不足!]" : "";

        // 汇总 7 个 AI 阵营（总单位、总资金、总电力）
        int aiTotalUnits = 0, aiTotalMoney = 0, aiTotalPower = 0;
        for (int t = 1; t <= AiTeamCount; t++)
        {
            aiTotalUnits += CountUnitsOfTeam(t);
            aiTotalMoney += _money[t];
            aiTotalPower += GetTeamPower(t);
        }

        // 阶段12-A4：核弹状态行
        bool hasTech = HasBuilding(PlayerTeamId, BuildingType.TechCenter);
        string nukeStatus;
        if (!hasTech) nukeStatus = "无科技中心";
        else if (_playerNukeCooldown > 0f)
        {
            int sec = Mathf.CeilToInt(_playerNukeCooldown);
            nukeStatus = $"冷却 {sec / 60}:{sec % 60:D2}";
        }
        else nukeStatus = "就绪 ★";
        string nukeLine = $"\n☢ 核弹: {nukeStatus}";

        // 阶段12-A4：闪电风暴状态行
        string lightStatus;
        if (!hasTech) lightStatus = "无科技中心";
        else if (_playerLightningCooldown > 0f)
        {
            int sec2 = Mathf.CeilToInt(_playerLightningCooldown);
            lightStatus = $"冷却 {sec2 / 60}:{sec2 % 60:D2}";
        }
        else lightStatus = "就绪 ★";
        string lightLine = $" | ⚡ 闪电: {lightStatus}";

        // E10：巡航导弹状态
        string missileStatus;
        if (!HasBuilding(PlayerTeamId, BuildingType.MissileSilo))
            missileStatus = "无导弹井";
        else if (_playerMissileCooldown > 0f)
        {
            int sec3 = Mathf.CeilToInt(_playerMissileCooldown);
            missileStatus = $"冷却 {sec3 / 60}:{sec3 % 60:D2}";
        }
        else missileStatus = "就绪 ★";
        string missileLine = $" | 🚀 导弹: {missileStatus}";

        string status = _gameOver ? _gameResult : "目标：消灭所有敌方阵营（8色对战，玩家为红色方）";
        string eraName = EraSystem.Eras[(int)_eraProgress[0].CurrentEra].Name;
        string eraUpgradeStr = _eraProgress[0].IsUpgrading ? $" (升级中{_eraProgress[0].Progress*100:F0}%)" : "";
        string cardStr = _playerCard.HasValue ? $" | 卡:{TacticalCards.Cards[_playerCard.Value].Name}" : "";
        _uiLabel.Text = $"难度: {_difficulty} [时代: {eraName}{eraUpgradeStr}]{cardStr} (科技Lv{_playerTechLevel} | 上限{_unitCap + GetTechUnitCapBonus(0) + GetCardUnitCapBonus(0)})    资金: ${_money[0]}    |    AI合计资金: ${aiTotalMoney}    [{QualitySettings.LevelName}]\n" +
                        $"电力: {playerPower}{powerWarn}    |    AI合计电力: {aiTotalPower}\n" +
                        $"玩家方: {playerUnits} 单位 / {playerBuildings}  · " +
                        $"AI合计: {aiTotalUnits} 单位 (7阵营)\n" +
                        $"地图剩余矿点: {oreCount}{nukeLine}{lightLine}{missileLine}\n" +
                        (string.IsNullOrEmpty(status) ? "" : $"\n★ {status}");

        _hintLabel.Text = "WASD 移动相机 | 滚轮 缩放 | 左键拖框 选择 | 右键 移动/攻击/集结点\n" +
                          "Q 攻击移动 | X 停止 | R 维修建筑 | V 出售建筑(回收50%) | Ctrl+1~9 编队 | 1~9 选编队\n" +
                          "选中建筑右键设集结点 | 选中受损建筑按R维修 | 选中建筑(非基地)按V出售\n" +
                          "B 轻坦$" + GetUnitCost(UnitType.LightTank) + " | N 重坦$" + GetUnitCost(UnitType.HeavyTank) +
                          " | M 炮兵$" + GetUnitCost(UnitType.Artillery) + " | H 矿车$" + GetUnitCost(UnitType.Harvester) + "\n" +
                          "K 火箭炮$" + GetUnitCost(UnitType.RocketLauncher) + " | L 导弹车$" + GetUnitCost(UnitType.MissileTank) + " (需科技中心)\n" +
                          "P 电站$" + GetBuildingCost(BuildingType.PowerPlant) + " | O 兵营$" + GetBuildingCost(BuildingType.Barracks) +
                          " | I 车厂$" + GetBuildingCost(BuildingType.WarFactory) + " | T 科技$" + GetBuildingCost(BuildingType.TechCenter) + " (需前置建筑)\n" +
                          "Z 核弹(需核弹井) | C 闪电(需闪电塔) | Shift+V 导弹(需导弹井)\n" +
                          "E11: 单位战斗获取经验→升级→随机能力(穿甲弹/双发/散射/反应装甲/自修复/烟幕/涡轮/侦察/狂热/掠夺/坚韧)\n" +
                          "G1: Tab 打开科技树面板 | 数字键研究科技 (军事/经济/防御三分支)\n" +
                          "G2: Y 打开时代面板 | U 升级时代 (石器→青铜→工业→信息)\n" +
                          "G3: T 查看战术卡 | 开局5秒后自动选卡(1/2/3)\n" +
                          "G4: G 查看电网分区 | 建筑需在电站280px范围内才有满功率\n" +
                          "G5: H 查看尤里卡进度 | 击杀/采集/建造/摧毁触发免费科技\n" +
                          "G6: J 查看邻接加成 | 同类建筑紧邻建造获得加成\n" +
                          "G7: N 查看间谍任务 | 选中间谍右键敌方建筑执行任务\n" +
                          "G8: K 查看占领状态 | 占领获$300+缴获加速+连锁+叛变风险";
        if (_attackMoveMode)
            _hintLabel.Text = "★ 攻击移动模式：左键点地发起 | 右键/Esc 取消";
        if (_nukeTargetMode)
            _hintLabel.Text = "★ 核弹目标模式：左键发射 | 右键取消";
        if (_lightningTargetMode)
            _hintLabel.Text = "★ 闪电风暴目标模式：左键发射 | 右键取消";
    }

    private string GetBuildingList(int teamId)
    {
        int baseN = 0, power = 0, barrack = 0, war = 0, tech = 0;
        foreach (var c in _buildingsNode.GetChildren())
        {
            if (c is Building b && b.TeamId == teamId && IsInstanceValid(b))
            {
                switch (b.Type)
                {
                    case BuildingType.Base: baseN++; break;
                    case BuildingType.PowerPlant: power++; break;
                    case BuildingType.Barracks: barrack++; break;
                    case BuildingType.WarFactory: war++; break;
                    case BuildingType.TechCenter: tech++; break;
                }
            }
        }
        var parts = new List<string>();
        if (baseN > 0) parts.Add($"基地{baseN}");
        if (power > 0) parts.Add($"电站{power}");
        if (barrack > 0) parts.Add($"兵营{barrack}");
        if (war > 0) parts.Add($"车厂{war}");
        if (tech > 0) parts.Add($"科技{tech}");
        return parts.Count > 0 ? string.Join(" ", parts) : "0建筑";
    }

}