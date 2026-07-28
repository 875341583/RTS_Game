using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace RTSGame;

/// <summary>
/// P3-1: 回放录制器 — 记录每局游戏中玩家发出的操作命令序列。
/// 设计要点：
///   - 命令流模式：只记录玩家操作，不记录AI操作（AI由种子+难度确定性驱动）
///   - 每条记录包含：帧号 + 操作类型 + 参数（JSON序列化）
///   - 与存档系统解耦：回放文件仅包含操作序列，不含世界状态
///   - 文件格式：.replay JSON（可读可编辑）
///
/// 用法：
///   ReplayRecorder.Start(mapSeed, difficulty, mapSize, theme);
///   // 游戏中自动通过 RecordXxx 方法记录操作
///   ReplayRecorder.Save("replay_20260727.replay");
/// </summary>
public static class ReplayRecorder
{
    /// <summary>回放操作类型。</summary>
    public enum ActionType
    {
        // 单位命令
        CommandMove,           // 选中的单位移动到目标位置
        CommandAttackMove,     // 攻击移动
        CommandAttack,         // 攻击敌方单位
        CommandAttackBuilding, // 攻击敌方建筑
        CommandStop,           // 停止
        CommandSpyMission,     // 间谍任务
        CommandTerrainMod,     // 地形改造

        // 选择/编队
        SaveSquad,             // 保存编队
        SelectSquad,           // 选择编队

        // 建筑
        PlaceBuilding,         // 放置建筑
        CancelPlacement,       // 取消放置

        // 生产
        SpawnUnit,             // 生产单位
        SpawnHarvester,        // 生产矿车
        CancelProduction,      // 取消生产
        SetRallyPoint,         // 设置集结点

        // 超武
        Nuke,                  // 核弹
        Lightning,             // 闪电风暴
        CruiseMissile,         // 巡航导弹

        // 建筑操作
        RepairBuilding,        // 维修建筑
        SellBuilding,          // 出售建筑

        // 科技
        ResearchTech,          // 研究科技
        AdvanceEra,            // 时代升级
        SelectCard,            // 选择战术卡
    }

    /// <summary>单条回放记录。</summary>
    public class ReplayRecord
    {
        /// <summary>游戏帧号（从0开始）。</summary>
        public long Frame { get; set; }
        /// <summary>操作类型。</summary>
        public ActionType Action { get; set; }
        /// <summary>操作参数（JSON序列化）。</summary>
        public string Params { get; set; } = "";
    }

    /// <summary>回放文件头。</summary>
    public class ReplayHeader
    {
        /// <summary>回放格式版本。</summary>
        public int Version { get; set; } = 1;
        /// <summary>地图种子。</summary>
        public ulong MapSeed { get; set; }
        /// <summary>难度名称。</summary>
        public string Difficulty { get; set; } = "Normal";
        /// <summary>地图尺寸。</summary>
        public int MapSize { get; set; } = 32;
        /// <summary>地图主题。</summary>
        public string MapTheme { get; set; } = "Default";
        /// <summary>录制时间（ISO 8601）。</summary>
        public string Timestamp { get; set; } = "";
        /// <summary>游戏版本。</summary>
        public string GameVersion { get; set; } = "3.0";
    }

    /// <summary>完整的回放文件。</summary>
    public class ReplayFile
    {
        public ReplayHeader Header { get; set; } = new();
        public List<ReplayRecord> Records { get; set; } = new();
    }

    // ---- 内部状态 ----
    private static readonly List<ReplayRecord> _records = new();
    private static long _frameCounter = 0;
    private static bool _recording = false;
    private static ReplayHeader? _header;
    private static bool _silent = false;

    /// <summary>设置静默模式（单元测试中必须开启，避免 Godot native IO 崩溃）。</summary>
    public static void SetSilent(bool silent) => _silent = silent;

    private static void Log(string msg) { if (!_silent) GameLog.Debug(msg); }
    private static void LogInfo(string msg) { if (!_silent) GameLog.Info(msg); }
    private static void LogWarning(string msg) { if (!_silent) GameLog.Warning(msg); }

    /// <summary>是否正在录制。</summary>
    public static bool IsRecording => _recording;

    /// <summary>已记录的操作数。</summary>
    public static int RecordCount => _records.Count;

    /// <summary>开始录制。</summary>
    public static void Start(ulong mapSeed, string difficulty, int mapSize, string mapTheme)
    {
        _records.Clear();
        _frameCounter = 0;
        _recording = true;
        _header = new ReplayHeader
        {
            MapSeed = mapSeed,
            Difficulty = difficulty,
            MapSize = mapSize,
            MapTheme = mapTheme,
            Timestamp = System.DateTime.UtcNow.ToString("o"),
        };
        Log($"[Replay] 开始录制 (种子={mapSeed}, 难度={difficulty}, 尺寸={mapSize}, 主题={mapTheme})");
    }

    /// <summary>停止录制。</summary>
    public static void Stop()
    {
        _recording = false;
        Log($"[Replay] 录制结束，共 {_records.Count} 条操作记录");
    }

    /// <summary>每帧递增帧计数器（由 Main._Process 调用）。</summary>
    public static void Tick()
    {
        if (_recording) _frameCounter++;
    }

    /// <summary>记录一条操作。</summary>
    public static void Record(ActionType action, object? parameters = null)
    {
        if (!_recording) return;
        var record = new ReplayRecord
        {
            Frame = _frameCounter,
            Action = action,
            Params = parameters != null ? JsonSerializer.Serialize(parameters) : "",
        };
        _records.Add(record);
    }

    /// <summary>保存回放文件。</summary>
    public static string Save(string? path = null)
    {
        var file = new ReplayFile
        {
            Header = _header ?? new ReplayHeader(),
            Records = new List<ReplayRecord>(_records),
        };

        var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
        path ??= System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "Godot", "app_userdata", "铁幕突袭",
            $"replay_{System.DateTime.Now:yyyyMMdd_HHmmss}.replay");

        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);

        System.IO.File.WriteAllText(path, json);
        LogInfo($"[Replay] 回放已保存: {path} ({_records.Count} 条记录)");
        return path;
    }

    /// <summary>加载回放文件。</summary>
    public static ReplayFile? Load(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            LogWarning($"[Replay] 文件不存在: {path}");
            return null;
        }
        try
        {
            var json = System.IO.File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<ReplayFile>(json);
            if (file == null)
            {
                    LogWarning($"[Replay] 反序列化失败: {path}");
                return null;
            }
            LogInfo($"[Replay] 加载回放: {path} ({file.Records.Count} 条记录)");
            return file;
        }
        catch (System.Exception ex)
        {
            LogWarning($"[Replay] 加载失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>获取录制统计摘要。</summary>
    public static string GetSummary()
    {
        if (_header == null) return TrManager.Tr("replay.summary.not_recording");
        return TrManager.Tr("replay.summary.format", _header.MapSeed, _header.Difficulty, _header.MapSize, _header.MapTheme, _records.Count, _frameCounter);
    }
}
