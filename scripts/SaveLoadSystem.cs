using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace RTSGame
{
    /// <summary>
    /// P0-2: 存档/读档系统。使用JSON序列化游戏状态。
    /// 设计原则：地图种子驱动基础地图重建，增量保存地形修改、建筑、单位、资源点状态。
    /// </summary>
    public static class SaveLoadSystem
    {
        private const int SaveVersion = 2; // P0修复: 升级到v2并支持v1→v2迁移
        /// <summary>总阵营数量（玩家0 + AI 1-7）。</summary>
        public const int TeamCount = 8;
        /// <summary>AI阵营数量（teamId 1..7）。</summary>
        public const int AiTeamCount = 7;
        /// <summary>占领进度上限（运行时范围0~1，>=1f即占领完成）。</summary>
        public const float CaptureProgressMax = 1f;
        /// <summary>单位等级上限（1-4）。</summary>
        public const int MaxUnitLevel = 4;
        /// <summary>读档位置匹配容差（±像素）。</summary>
        public const float PositionMatchTolerance = 5f;

        // ========== 存档 ==========

        /// <summary>保存游戏到指定文件路径。</summary>
        /// <param name="main">主控制器，提供游戏状态访问器。</param>
        /// <param name="filePath">存档文件绝对路径。</param>
        /// <exception cref="System.Exception">文件写入失败时抛出，调用方需捕获。</exception>
        public static void SaveGame(Main main, string filePath)
        {
            try
            {
                _SaveGameInner(main, filePath);
            }
            catch (Exception ex)
            {
                GameLog.Error($"[SaveLoad] 保存异常: {ex.Message}");
                throw;
            }
        }

        private static void _SaveGameInner(Main main, string filePath)
        {
            var data = new SaveData
            {
                Version = SaveVersion,
                MapSeed = main.GetMapSeed(),
                Difficulty = (int)main.GetDifficulty(),
                ActiveAiCount = main.GetActiveAiCount(),
                GameOver = main.IsGameOver(),
                GameResult = main.GetGameResult() ?? "",
                AiGraceRemaining = Unit.AiGraceRemaining,
                Money = main.GetMoneyArray(),
                StrategicPointIncomeEnabled = main.StrategicPointIncomeEnabled,
                Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            };

            // 科技进度
            data.TechProgress = new TechSave[TeamCount];
            for (int i = 0; i < TeamCount; i++)
            {
                var tp = main.GetTechProgress(i);
                if (tp != null)
                {
                    data.TechProgress[i] = new TechSave
                    {
                        Completed = tp.Completed.Select(t => (int)t).ToList(),
                        CurrentlyResearching = tp.CurrentlyResearching.HasValue ? (int)tp.CurrentlyResearching.Value : -1,
                        ResearchTimer = tp.ResearchTimer,
                        QueuedTech = tp.QueuedTech.HasValue ? (int)tp.QueuedTech.Value : -1,
                    };
                }
            }

            // 时代进度
            data.EraProgress = new EraSave[TeamCount];
            for (int i = 0; i < TeamCount; i++)
            {
                var ep = main.GetEraProgress(i);
                if (ep != null)
                {
                    data.EraProgress[i] = new EraSave
                    {
                        CurrentEra = (int)ep.CurrentEra,
                        IsUpgrading = ep.IsUpgrading,
                        UpgradeTimer = ep.UpgradeTimer,
                    };
                }
            }

            // 战术卡
            data.PlayerCard = main.GetPlayerCardId();
            data.AiCards = main.GetAiCardIds();

            // 建筑
            data.Buildings = new List<BuildingSave>();
            foreach (var b in main.GetAllBuildings())
            {
                if (!GodotObject.IsInstanceValid(b)) continue;
                var bs = new BuildingSave
                {
                    Type = (int)b.Type,
                    TeamId = b.TeamId,
                    Health = b.Health,
                    MaxHealth = b.MaxHealth,
                    PosX = b.GlobalPosition.X,
                    PosY = b.GlobalPosition.Y,
                    CaptureProgress = b.CaptureProgress,
                    OriginalTeamId = b.GetOriginalTeamId(),
                    CapturingTeamId = b.GetCapturingTeamId(),
                };

                // 生产队列
                var (queue, current, timer, duration) = b.GetProductionState();
                bs.ProductionQueue = queue.Select(p => (int)p).ToList();
                bs.CurrentProduction = current.HasValue ? (int)current.Value : -1;
                bs.ProductionTimer = timer;
                bs.ProductionDuration = duration;

                // 集结点
                var rally = b.GetRallyPoint();
                bs.HasRallyPoint = rally.HasValue;
                if (rally.HasValue)
                {
                    bs.RallyX = rally.Value.X;
                    bs.RallyY = rally.Value.Y;
                }

                data.Buildings.Add(bs);
            }

            // 单位
            data.Units = new List<UnitSave>();
            foreach (var u in main.GetAllUnits())
            {
                if (!GodotObject.IsInstanceValid(u)) continue;
                var us = new UnitSave
                {
                    Type = (int)u.Type,
                    TeamId = u.TeamId,
                    Health = u.Health,
                    MaxHealth = u.MaxHealth,
                    PosX = u.GlobalPosition.X,
                    PosY = u.GlobalPosition.Y,
                    AutoAI = u.AutoAI,
                    AutoDefend = u.AutoDefend,
                    MoveTargetX = u.GetMoveTarget().X,
                    MoveTargetY = u.GetMoveTarget().Y,
                    HasMoveTarget = u.HasMoveTarget(),
                    GuardX = u.GetGuardPosition().X,
                    GuardY = u.GetGuardPosition().Y,
                    HasGuardPosition = u.HasGuardPosition(),
                    Level = u.GetLevel(),
                    Experience = u.GetExperience(),
                    Abilities = u.GetAbilities().Select(a => (int)a).ToList(),
                    HeroSkill = (int)u.GetHeroSkill(),
                    SpyDisguiseTeam = u.GetSpyDisguiseTeam(),
                    LastAttackerTeam = u.GetLastAttackerTeam(),
                };

                // 运输车载客
                var passengers = u.GetPassengerTypes();
                if (passengers.Count > 0)
                {
                    us.PassengerTypes = passengers.Select(p => (int)p).ToList();
                    us.PassengerHealths = u.GetPassengerHealths();
                    us.PassengerLevels = u.GetPassengerLevels();
                }

                data.Units.Add(us);
            }

            // 资源点
            data.Resources = new List<ResourceSave>();
            foreach (var r in main.GetAllResourceNodes())
            {
                if (!GodotObject.IsInstanceValid(r)) continue;
                data.Resources.Add(new ResourceSave
                {
                    PosX = r.GlobalPosition.X,
                    PosY = r.GlobalPosition.Y,
                    ResourceType = (int)r.ResourceType,
                    InitialAmount = r.InitialAmount,
                    Amount = r.GetAmount(),
                    OilOwner = r.OilOwner,
                });
            }

            // 战略点
            data.StrategicPoints = new List<StrategicPointSave>();
            foreach (var sp in main.GetAllStrategicPoints())
            {
                if (!GodotObject.IsInstanceValid(sp)) continue;
                data.StrategicPoints.Add(new StrategicPointSave
                {
                    PosX = sp.GlobalPosition.X,
                    PosY = sp.GlobalPosition.Y,
                    OwningTeam = sp.GetOwningTeam(),
                });
            }

            // 地形修改增量
            data.TerrainMods = main.GetTerrainModifications();

            // 超武冷却
            data.Cooldowns = main.GetSuperweaponCooldowns();

            // 序列化为JSON
            string json = Json.Stringify(data.ToGodotVariant());

            using var file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Write);
            if (file == null)
            {
                GameLog.Error($"[SaveLoad] 无法打开存档文件: {filePath}");
                return;
            }
            file.StoreString(json);
            GameLog.Info($"[SaveLoad] 游戏已保存到: {filePath} (建筑{data.Buildings.Count} 单位{data.Units.Count} 资源{data.Resources.Count})");
        }

        // ========== 读档 ==========

        /// <summary>从文件加载游戏状态。返回null表示加载失败。</summary>
        /// <param name="filePath">存档文件绝对路径。</param>
        /// <returns>解析后的 SaveData；文件不存在/解析失败返回 null。</returns>
        public static SaveData? LoadGame(string filePath)
        {
            try
            {
                return _LoadGameInner(filePath);
            }
            catch (Exception ex)
            {
                GameLog.Error($"[SaveLoad] 读档异常: {ex.Message}");
                return null;
            }
        }

        private static SaveData? _LoadGameInner(string filePath)
        {
            if (!Godot.FileAccess.FileExists(filePath))
            {
                GameLog.Error($"[SaveLoad] 存档文件不存在: {filePath}");
                return null;
            }

            using var file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                GameLog.Error($"[SaveLoad] 无法读取存档文件: {filePath}");
                return null;
            }

            string json = file.GetAsText();
            var data = Json.ParseString(json);
            if (data.VariantType == Variant.Type.Nil)
            {
                GameLog.Error($"[SaveLoad] JSON解析失败: {filePath}");
                return null;
            }

            // Godot的Json.ParseString返回Variant字典，转换为SaveData
            var saveData = VariantToSaveData(data);
            if (saveData == null)
            {
                GameLog.Error("[SaveLoad] 存档数据格式转换失败");
                return null;
            }

            // P0修复: 存档版本迁移 — 支持旧版本存档升级到当前版本
            if (saveData.Version < SaveVersion)
            {
                GameLog.Info($"[SaveLoad] 存档版本迁移: v{saveData.Version} → v{SaveVersion}");
                saveData = MigrateSaveData(saveData);
                if (saveData == null)
                {
                    GameLog.Error($"[SaveLoad] 存档迁移失败: v{saveData?.Version} → v{SaveVersion}");
                    return null;
                }
            }
            else if (saveData.Version > SaveVersion)
            {
                GameLog.Error($"[SaveLoad] 存档版本过高: 存档v{saveData.Version} 当前v{SaveVersion}，请更新游戏");
                return null;
            }

            GameLog.Info($"[SaveLoad] 存档加载成功: {filePath} (版本{saveData.Version} 建筑{saveData.Buildings.Count} 单位{saveData.Units.Count})");
            return saveData;
        }

        /// <summary>P0修复: 存档版本迁移 — 逐版本升级旧存档数据结构。</summary>
        private static SaveData? MigrateSaveData(SaveData data)
        {
            while (data.Version < SaveVersion)
            {
                switch (data.Version)
                {
                    case 1:
                        // v1→v2: 无结构性变化，仅版本号升级（v1存档完全兼容v2）
                        // 未来如有字段新增，在此处补全默认值
                        data.Version = 2;
                        GameLog.Info("[SaveLoad] 迁移 v1→v2: 版本号升级（无数据变更）");
                        break;
                    default:
                        GameLog.Error($"[SaveLoad] 未知存档版本: v{data.Version}");
                        return null;
                }
            }
            return data;
        }

        // ========== JSON转C#对象 ==========

        private static SaveData? VariantToSaveData(Variant v)
        {
            if (v.VariantType != Variant.Type.Dictionary) return null;
            var d = v.AsGodotDictionary();

            var data = new SaveData();
            // 6个核心标量字段：缺失任一即拒绝加载（精确报错优于外层catch兜底）
            string[] requiredScalarKeys = { "version", "mapSeed", "difficulty", "activeAiCount", "gameOver", "aiGraceRemaining" };
            foreach (var k in requiredScalarKeys)
            {
                if (!d.ContainsKey(k))
                {
                    GameLog.Error($"[SaveLoad] 存档缺少 {k} 字段");
                    return null;
                }
            }
            data.Version = (int)d["version"];
            data.MapSeed = (ulong)d["mapSeed"];
            data.Difficulty = (int)d["difficulty"];
            data.ActiveAiCount = (int)d["activeAiCount"];
            data.GameOver = (bool)d["gameOver"];
            data.GameResult = d.ContainsKey("gameResult") ? d["gameResult"].AsString() : "";
            data.AiGraceRemaining = (float)d["aiGraceRemaining"];
            data.Timestamp = d.ContainsKey("timestamp") ? d["timestamp"].AsString() : "";
            data.StrategicPointIncomeEnabled = d.ContainsKey("strategicPointIncomeEnabled") && (bool)d["strategicPointIncomeEnabled"];

            // Money数组（旧版存档可能缺键，容错为空数组）
            if (!d.ContainsKey("money"))
            {
                GameLog.Error("[SaveLoad] 存档缺少 money 字段");
                return null;
            }
            var moneyArr = d["money"].AsGodotArray();
            data.Money = new int[moneyArr.Count];
            for (int i = 0; i < moneyArr.Count; i++)
                data.Money[i] = (int)moneyArr[i];

            // 科技进度
            if (!d.ContainsKey("techProgress"))
            {
                GameLog.Error("[SaveLoad] 存档缺少 techProgress 字段");
                return null;
            }
            var techArr = d["techProgress"].AsGodotArray();
            data.TechProgress = new TechSave[techArr.Count];
            for (int i = 0; i < techArr.Count; i++)
            {
                if (techArr[i].VariantType == Variant.Type.Dictionary)
                {
                    var td = techArr[i].AsGodotDictionary();
                    data.TechProgress[i] = new TechSave
                    {
                        Completed = td["completed"].AsGodotArray().Select(x => (int)x).ToList(),
                        CurrentlyResearching = (int)td["currentlyResearching"],
                        ResearchTimer = (float)td["researchTimer"],
                        QueuedTech = (int)td["queuedTech"],
                    };
                }
            }

            // 时代进度
            if (!d.ContainsKey("eraProgress"))
            {
                GameLog.Error("[SaveLoad] 存档缺少 eraProgress 字段");
                return null;
            }
            var eraArr = d["eraProgress"].AsGodotArray();
            data.EraProgress = new EraSave[eraArr.Count];
            for (int i = 0; i < eraArr.Count; i++)
            {
                if (eraArr[i].VariantType == Variant.Type.Dictionary)
                {
                    var ed = eraArr[i].AsGodotDictionary();
                    data.EraProgress[i] = new EraSave
                    {
                        CurrentEra = (int)ed["currentEra"],
                        IsUpgrading = (bool)ed["isUpgrading"],
                        UpgradeTimer = (float)ed["upgradeTimer"],
                    };
                }
            }

            // 战术卡
            data.PlayerCard = d.ContainsKey("playerCard") && d["playerCard"].VariantType == Variant.Type.Int
                ? (int)d["playerCard"] : -1;
            var aiCardsArr = d.ContainsKey("aiCards") ? d["aiCards"].AsGodotArray() : new Godot.Collections.Array();
            data.AiCards = new int[aiCardsArr.Count];
            for (int i = 0; i < aiCardsArr.Count; i++)
                data.AiCards[i] = aiCardsArr[i].VariantType == Variant.Type.Int ? (int)aiCardsArr[i] : -1;

            // 建筑
            if (!d.ContainsKey("buildings"))
            {
                GameLog.Error("[SaveLoad] 存档缺少 buildings 字段");
                return null;
            }
            var bldgArr = d["buildings"].AsGodotArray();
            data.Buildings = new List<BuildingSave>(bldgArr.Count);
            foreach (var bv in bldgArr)
            {
                var bd = bv.AsGodotDictionary();
                var bs = new BuildingSave
                {
                    Type = (int)bd["type"],
                    TeamId = (int)bd["teamId"],
                    Health = (float)bd["health"],
                    MaxHealth = (float)bd["maxHealth"],
                    PosX = (float)bd["posX"],
                    PosY = (float)bd["posY"],
                    CaptureProgress = bd.ContainsKey("captureProgress") ? (float)bd["captureProgress"] : 0f,
                    OriginalTeamId = bd.ContainsKey("originalTeamId") ? (int)bd["originalTeamId"] : -1,
                    CapturingTeamId = bd.ContainsKey("capturingTeamId") ? (int)bd["capturingTeamId"] : -1,
                    CurrentProduction = bd.ContainsKey("currentProduction") ? (int)bd["currentProduction"] : -1,
                    ProductionTimer = bd.ContainsKey("productionTimer") ? (float)bd["productionTimer"] : 0f,
                    ProductionDuration = bd.ContainsKey("productionDuration") ? (float)bd["productionDuration"] : 0f,
                    HasRallyPoint = bd.ContainsKey("hasRallyPoint") && (bool)bd["hasRallyPoint"],
                    RallyX = bd.ContainsKey("rallyX") ? (float)bd["rallyX"] : 0f,
                    RallyY = bd.ContainsKey("rallyY") ? (float)bd["rallyY"] : 0f,
                };
                if (bd.ContainsKey("productionQueue"))
                    bs.ProductionQueue = bd["productionQueue"].AsGodotArray().Select(x => (int)x).ToList();
                else
                    bs.ProductionQueue = new List<int>();
                data.Buildings.Add(bs);
            }

            // 单位
            if (!d.ContainsKey("units"))
            {
                GameLog.Error("[SaveLoad] 存档缺少 units 字段");
                return null;
            }
            var unitArr = d["units"].AsGodotArray();
            data.Units = new List<UnitSave>(unitArr.Count);
            foreach (var uv in unitArr)
            {
                var ud = uv.AsGodotDictionary();
                var us = new UnitSave
                {
                    Type = (int)ud["type"],
                    TeamId = (int)ud["teamId"],
                    Health = (float)ud["health"],
                    MaxHealth = (float)ud["maxHealth"],
                    PosX = (float)ud["posX"],
                    PosY = (float)ud["posY"],
                    AutoAI = (bool)ud["autoAI"],
                    AutoDefend = (bool)ud["autoDefend"],
                    MoveTargetX = (float)ud["moveTargetX"],
                    MoveTargetY = (float)ud["moveTargetY"],
                    HasMoveTarget = (bool)ud["hasMoveTarget"],
                    GuardX = (float)ud["guardX"],
                    GuardY = (float)ud["guardY"],
                    HasGuardPosition = (bool)ud["hasGuardPosition"],
                    Level = (int)ud["level"],
                    Experience = (float)ud["experience"],
                    HeroSkill = (int)ud["heroSkill"],
                    SpyDisguiseTeam = (int)ud["spyDisguiseTeam"],
                    LastAttackerTeam = (int)ud["lastAttackerTeam"],
                };
                if (ud.ContainsKey("abilities"))
                    us.Abilities = ud["abilities"].AsGodotArray().Select(x => (int)x).ToList();
                else
                    us.Abilities = new List<int>();
                if (ud.ContainsKey("passengerTypes"))
                {
                    us.PassengerTypes = ud["passengerTypes"].AsGodotArray().Select(x => (int)x).ToList();
                    // 防御：三个乘客列表可能因旧版存档缺键而长度不一致，分别容错
                    us.PassengerHealths = ud.ContainsKey("passengerHealths")
                        ? ud["passengerHealths"].AsGodotArray().Select(x => (float)x).ToList()
                        : new List<float>();
                    us.PassengerLevels = ud.ContainsKey("passengerLevels")
                        ? ud["passengerLevels"].AsGodotArray().Select(x => (int)x).ToList()
                        : new List<int>();
                }
                else
                {
                    us.PassengerTypes = new List<int>();
                    us.PassengerHealths = new List<float>();
                    us.PassengerLevels = new List<int>();
                }
                data.Units.Add(us);
            }

            // 资源点
            if (!d.ContainsKey("resources"))
            {
                GameLog.Error("[SaveLoad] 存档缺少 resources 字段");
                return null;
            }
            var resArr = d["resources"].AsGodotArray();
            data.Resources = new List<ResourceSave>(resArr.Count);
            foreach (var rv in resArr)
            {
                var rd = rv.AsGodotDictionary();
                data.Resources.Add(new ResourceSave
                {
                    PosX = (float)rd["posX"],
                    PosY = (float)rd["posY"],
                    ResourceType = (int)rd["resourceType"],
                    InitialAmount = (int)rd["initialAmount"],
                    Amount = (int)rd["amount"],
                    OilOwner = rd.ContainsKey("oilOwner") ? (int)rd["oilOwner"] : -1,
                });
            }

            // 战略点
            if (d.ContainsKey("strategicPoints"))
            {
                var spArr = d["strategicPoints"].AsGodotArray();
                data.StrategicPoints = new List<StrategicPointSave>(spArr.Count);
                foreach (var spv in spArr)
                {
                    var spd = spv.AsGodotDictionary();
                    data.StrategicPoints.Add(new StrategicPointSave
                    {
                        PosX = (float)spd["posX"],
                        PosY = (float)spd["posY"],
                        OwningTeam = (int)spd["owningTeam"],
                    });
                }
            }
            else
            {
                data.StrategicPoints = new List<StrategicPointSave>();
            }

            // 地形修改增量
            if (d.ContainsKey("terrainMods"))
            {
                var tmArr = d["terrainMods"].AsGodotArray();
                data.TerrainMods = new List<TerrainModSave>(tmArr.Count);
                foreach (var tmv in tmArr)
                {
                    var tmd = tmv.AsGodotDictionary();
                    data.TerrainMods.Add(new TerrainModSave
                    {
                        Gx = (int)tmd["gx"],
                        Gy = (int)tmd["gy"],
                        TerrainType = (int)tmd["terrainType"],
                        Elevation = (int)tmd["elevation"],
                        HasBridge = (bool)tmd["hasBridge"],
                        HasTunnel = (bool)tmd["hasTunnel"],
                    });
                }
            }
            else
            {
                data.TerrainMods = new List<TerrainModSave>();
            }

            // 超武冷却
            if (d.ContainsKey("cooldowns"))
            {
                var cd = d["cooldowns"].AsGodotDictionary();
                data.Cooldowns = new CooldownSave
                {
                    PlayerNuke = cd.ContainsKey("playerNuke") ? (float)cd["playerNuke"] : 0f,
                    PlayerLightning = cd.ContainsKey("playerLightning") ? (float)cd["playerLightning"] : 0f,
                    PlayerMissile = cd.ContainsKey("playerMissile") ? (float)cd["playerMissile"] : 0f,
                    AiNukes = cd.ContainsKey("aiNukes") ? cd["aiNukes"].AsGodotDictionary().Select(kv => ((int)kv.Key.AsInt32(), (float)kv.Value.AsSingle())).ToDictionary() : new Dictionary<int, float>(),
                    AiLightnings = cd.ContainsKey("aiLightnings") ? cd["aiLightnings"].AsGodotDictionary().Select(kv => ((int)kv.Key.AsInt32(), (float)kv.Value.AsSingle())).ToDictionary() : new Dictionary<int, float>(),
                    AiMissiles = cd.ContainsKey("aiMissiles") ? cd["aiMissiles"].AsGodotDictionary().Select(kv => ((int)kv.Key.AsInt32(), (float)kv.Value.AsSingle())).ToDictionary() : new Dictionary<int, float>(),
                };
            }
            else
            {
                data.Cooldowns = new CooldownSave();
            }

            return data;
        }

        // ========== 存档数据结构 ==========

        public class SaveData
        {
            public int Version;
            public ulong MapSeed;
            public int Difficulty;
            public int ActiveAiCount;
            public bool GameOver;
            public string GameResult = "";
            public float AiGraceRemaining;
            public string Timestamp = "";
            public int[] Money = new int[TeamCount];
            public bool StrategicPointIncomeEnabled = true;
            public TechSave[] TechProgress = new TechSave[TeamCount];
            public EraSave[] EraProgress = new EraSave[TeamCount];
            public int PlayerCard = -1; // -1=未选
            public int[] AiCards = new int[AiTeamCount];
            public List<BuildingSave> Buildings = new();
            public List<UnitSave> Units = new();
            public List<ResourceSave> Resources = new();
            public List<StrategicPointSave> StrategicPoints = new();
            public List<TerrainModSave> TerrainMods = new();
            public CooldownSave Cooldowns = new();

            /// <summary>将存档数据序列化为 Godot Variant（Dictionary），用于 Json.Stringify 输出。</summary>
            /// <returns>包含全部存档字段的 Godot.Collections.Dictionary，包装为 Variant。</returns>
            public Godot.Variant ToGodotVariant()
            {
                var d = new Godot.Collections.Dictionary
                {
                    ["version"] = Version,
                    ["mapSeed"] = (long)MapSeed,
                    ["difficulty"] = Difficulty,
                    ["activeAiCount"] = ActiveAiCount,
                    ["gameOver"] = GameOver,
                    ["gameResult"] = GameResult,
                    ["aiGraceRemaining"] = AiGraceRemaining,
                    ["timestamp"] = Timestamp,
                    ["strategicPointIncomeEnabled"] = StrategicPointIncomeEnabled,
                };

                // Money
                var moneyArr = new Godot.Collections.Array();
                foreach (int m in Money) moneyArr.Add(m);
                d["money"] = moneyArr;

                // Tech
                var techArr = new Godot.Collections.Array();
                foreach (var tp in TechProgress)
                {
                    if (tp == null) { techArr.Add(new Godot.Collections.Dictionary()); continue; }
                    var td = new Godot.Collections.Dictionary
                    {
                        ["completed"] = new Godot.Collections.Array(tp.Completed.Select(c => (Variant)c)),
                        ["currentlyResearching"] = tp.CurrentlyResearching,
                        ["researchTimer"] = tp.ResearchTimer,
                        ["queuedTech"] = tp.QueuedTech,
                    };
                    techArr.Add(td);
                }
                d["techProgress"] = techArr;

                // Era
                var eraArr = new Godot.Collections.Array();
                foreach (var ep in EraProgress)
                {
                    if (ep == null) { eraArr.Add(new Godot.Collections.Dictionary()); continue; }
                    var ed = new Godot.Collections.Dictionary
                    {
                        ["currentEra"] = ep.CurrentEra,
                        ["isUpgrading"] = ep.IsUpgrading,
                        ["upgradeTimer"] = ep.UpgradeTimer,
                    };
                    eraArr.Add(ed);
                }
                d["eraProgress"] = eraArr;

                // Cards
                d["playerCard"] = PlayerCard;
                var cardArr = new Godot.Collections.Array();
                foreach (int c in AiCards) cardArr.Add(c);
                d["aiCards"] = cardArr;

                // Buildings
                var bldgArr = new Godot.Collections.Array();
                foreach (var b in Buildings)
                {
                    var bd = new Godot.Collections.Dictionary
                    {
                        ["type"] = b.Type,
                        ["teamId"] = b.TeamId,
                        ["health"] = b.Health,
                        ["maxHealth"] = b.MaxHealth,
                        ["posX"] = b.PosX,
                        ["posY"] = b.PosY,
                        ["captureProgress"] = b.CaptureProgress,
                        ["originalTeamId"] = b.OriginalTeamId,
                        ["capturingTeamId"] = b.CapturingTeamId,
                        ["productionQueue"] = new Godot.Collections.Array(b.ProductionQueue.Select(p => (Variant)p)),
                        ["currentProduction"] = b.CurrentProduction,
                        ["productionTimer"] = b.ProductionTimer,
                        ["productionDuration"] = b.ProductionDuration,
                        ["hasRallyPoint"] = b.HasRallyPoint,
                        ["rallyX"] = b.RallyX,
                        ["rallyY"] = b.RallyY,
                    };
                    bldgArr.Add(bd);
                }
                d["buildings"] = bldgArr;

                // Units
                var unitArr = new Godot.Collections.Array();
                foreach (var u in Units)
                {
                    var ud = new Godot.Collections.Dictionary
                    {
                        ["type"] = u.Type,
                        ["teamId"] = u.TeamId,
                        ["health"] = u.Health,
                        ["maxHealth"] = u.MaxHealth,
                        ["posX"] = u.PosX,
                        ["posY"] = u.PosY,
                        ["autoAI"] = u.AutoAI,
                        ["autoDefend"] = u.AutoDefend,
                        ["moveTargetX"] = u.MoveTargetX,
                        ["moveTargetY"] = u.MoveTargetY,
                        ["hasMoveTarget"] = u.HasMoveTarget,
                        ["guardX"] = u.GuardX,
                        ["guardY"] = u.GuardY,
                        ["hasGuardPosition"] = u.HasGuardPosition,
                        ["level"] = u.Level,
                        ["experience"] = u.Experience,
                        ["abilities"] = new Godot.Collections.Array(u.Abilities.Select(a => (Variant)a)),
                        ["heroSkill"] = u.HeroSkill,
                        ["spyDisguiseTeam"] = u.SpyDisguiseTeam,
                        ["lastAttackerTeam"] = u.LastAttackerTeam,
                    };
                    if (u.PassengerTypes.Count > 0)
                    {
                        ud["passengerTypes"] = new Godot.Collections.Array(u.PassengerTypes.Select(p => (Variant)p));
                        ud["passengerHealths"] = new Godot.Collections.Array(u.PassengerHealths.Select(h => (Variant)h));
                        ud["passengerLevels"] = new Godot.Collections.Array(u.PassengerLevels.Select(l => (Variant)l));
                    }
                    unitArr.Add(ud);
                }
                d["units"] = unitArr;

                // Resources
                var resArr = new Godot.Collections.Array();
                foreach (var r in Resources)
                {
                    resArr.Add(new Godot.Collections.Dictionary
                    {
                        ["posX"] = r.PosX,
                        ["posY"] = r.PosY,
                        ["resourceType"] = r.ResourceType,
                        ["initialAmount"] = r.InitialAmount,
                        ["amount"] = r.Amount,
                        ["oilOwner"] = r.OilOwner,
                    });
                }
                d["resources"] = resArr;

                // Strategic Points
                var spArr = new Godot.Collections.Array();
                foreach (var sp in StrategicPoints)
                {
                    spArr.Add(new Godot.Collections.Dictionary
                    {
                        ["posX"] = sp.PosX,
                        ["posY"] = sp.PosY,
                        ["owningTeam"] = sp.OwningTeam,
                    });
                }
                d["strategicPoints"] = spArr;

                // Terrain Mods
                var tmArr = new Godot.Collections.Array();
                foreach (var tm in TerrainMods)
                {
                    tmArr.Add(new Godot.Collections.Dictionary
                    {
                        ["gx"] = tm.Gx,
                        ["gy"] = tm.Gy,
                        ["terrainType"] = tm.TerrainType,
                        ["elevation"] = tm.Elevation,
                        ["hasBridge"] = tm.HasBridge,
                        ["hasTunnel"] = tm.HasTunnel,
                    });
                }
                d["terrainMods"] = tmArr;

                // Cooldowns
                var cdDict = new Godot.Collections.Dictionary
                {
                    ["playerNuke"] = Cooldowns.PlayerNuke,
                    ["playerLightning"] = Cooldowns.PlayerLightning,
                    ["playerMissile"] = Cooldowns.PlayerMissile,
                };
                var aiNukeDict = new Godot.Collections.Dictionary();
                foreach (var kv in Cooldowns.AiNukes) aiNukeDict[kv.Key.ToString()] = kv.Value;
                cdDict["aiNukes"] = aiNukeDict;
                var aiLightDict = new Godot.Collections.Dictionary();
                foreach (var kv in Cooldowns.AiLightnings) aiLightDict[kv.Key.ToString()] = kv.Value;
                cdDict["aiLightnings"] = aiLightDict;
                var aiMissileDict = new Godot.Collections.Dictionary();
                foreach (var kv in Cooldowns.AiMissiles) aiMissileDict[kv.Key.ToString()] = kv.Value;
                cdDict["aiMissiles"] = aiMissileDict;
                d["cooldowns"] = cdDict;

                return d;
            }
        }

        public class TechSave
        {
            public List<int> Completed = new();
            public int CurrentlyResearching;
            public float ResearchTimer;
            public int QueuedTech;
        }

        public class EraSave
        {
            public int CurrentEra;
            public bool IsUpgrading;
            public float UpgradeTimer;
        }

        public class BuildingSave
        {
            public int Type;
            public int TeamId;
            public float Health;
            public float MaxHealth;
            public float PosX, PosY;
            public float CaptureProgress;
            public int OriginalTeamId;
            public int CapturingTeamId;
            public List<int> ProductionQueue = new();
            public int CurrentProduction;
            public float ProductionTimer;
            public float ProductionDuration;
            public bool HasRallyPoint;
            public float RallyX, RallyY;
        }

        public class UnitSave
        {
            public int Type;
            public int TeamId;
            public float Health;
            public float MaxHealth;
            public float PosX, PosY;
            public bool AutoAI;
            public bool AutoDefend;
            public float MoveTargetX, MoveTargetY;
            public bool HasMoveTarget;
            public float GuardX, GuardY;
            public bool HasGuardPosition;
            public int Level;
            public float Experience;
            public List<int> Abilities = new();
            public int HeroSkill;
            public int SpyDisguiseTeam;
            public int LastAttackerTeam;
            public List<int> PassengerTypes = new();
            public List<float> PassengerHealths = new();
            public List<int> PassengerLevels = new();
        }

        public class ResourceSave
        {
            public float PosX, PosY;
            public int ResourceType;
            public int InitialAmount;
            public int Amount;
            public int OilOwner;
        }

        public class StrategicPointSave
        {
            public float PosX, PosY;
            public int OwningTeam;
        }

        public class TerrainModSave
        {
            public int Gx, Gy;
            public int TerrainType;
            public int Elevation;
            public bool HasBridge;
            public bool HasTunnel;
        }

        public class CooldownSave
        {
            public float PlayerNuke;
            public float PlayerLightning;
            public float PlayerMissile;
            public Dictionary<int, float> AiNukes = new();
            public Dictionary<int, float> AiLightnings = new();
            public Dictionary<int, float> AiMissiles = new();
        }
    }
}
