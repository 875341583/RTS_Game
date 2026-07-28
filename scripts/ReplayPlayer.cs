using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace RTSGame;

/// <summary>
/// P3-1: 回放播放器 — 读取 .replay 文件并按帧回放玩家操作。
/// 设计要点：
///   - 从 ReplayFile 加载操作序列，逐帧推进到下一操作并触发对应逻辑
///   - 与 ReplayRecorder 解耦：播放器只读不写，不修改录制状态
///   - 回放期间禁用玩家输入（AI不受影响，因为是确定性驱动）
///   - 支持暂停/倍速/逐帧步进
///
/// 用法：
///   var replay = ReplayRecorder.Load("replay.replay");
///   ReplayPlayer.Start(replay, mainInstance);
///   // 在 Main._Process 中调用 ReplayPlayer.Tick()
/// </summary>
public static class ReplayPlayer
{
    /// <summary>回放播放状态。</summary>
    public enum PlaybackState
    {
        /// <summary>未在播放。</summary>
        Stopped,
        /// <summary>正在播放。</summary>
        Playing,
        /// <summary>暂停中。</summary>
        Paused,
        /// <summary>播放完成。</summary>
        Finished,
    }

    // ---- 内部状态 ----
    private static ReplayRecorder.ReplayFile? _file;
    private static int _currentIndex;
    private static long _frameCounter;
    private static PlaybackState _state = PlaybackState.Stopped;
    private static float _playbackSpeed = 1f;
    private static Main? _main;

    /// <summary>当前播放状态。</summary>
    public static PlaybackState State => _state;

    /// <summary>当前播放帧号。</summary>
    public static long CurrentFrame => _frameCounter;

    /// <summary>总操作数。</summary>
    public static int TotalActions => _file?.Records.Count ?? 0;

    /// <summary>已回放操作数。</summary>
    public static int PlayedActions => _currentIndex;

    /// <summary>回放速度倍率。</summary>
    public static float PlaybackSpeed
    {
        get => _playbackSpeed;
        set => _playbackSpeed = value > 0 ? value : 1f;
    }

    /// <summary>是否正在回放（播放或暂停中）。</summary>
    public static bool IsReplaying => _state == PlaybackState.Playing || _state == PlaybackState.Paused;

    /// <summary>开始回放。</summary>
    /// <param name="replayFile">从 ReplayRecorder.Load() 加载的回放文件。</param>
    /// <param name="main">Main 实例引用（用于触发操作）。</param>
    public static void Start(ReplayRecorder.ReplayFile replayFile, Main main)
    {
        _file = replayFile;
        _currentIndex = 0;
        _frameCounter = 0;
        _state = PlaybackState.Playing;
        _main = main;
        _playbackSpeed = 1f;
        GameLog.Info($"[Replay] 开始回放: {_file.Header.MapSeed}/{_file.Header.Difficulty} ({_file.Records.Count} 操作)");
    }

    /// <summary>暂停回放。</summary>
    public static void Pause()
    {
        if (_state == PlaybackState.Playing)
        {
            _state = PlaybackState.Paused;
            GameLog.Debug("[Replay] 回放暂停");
        }
    }

    /// <summary>恢复回放。</summary>
    public static void Resume()
    {
        if (_state == PlaybackState.Paused)
        {
            _state = PlaybackState.Playing;
            GameLog.Debug("[Replay] 回放继续");
        }
    }

    /// <summary>停止回放并重置状态。</summary>
    public static void Stop()
    {
        _state = PlaybackState.Stopped;
        _file = null;
        _currentIndex = 0;
        _frameCounter = 0;
        _main = null;
        GameLog.Debug("[Replay] 回放已停止");
    }

    /// <summary>每帧调用（由 Main._Process 驱动）。</summary>
    public static void Tick()
    {
        if (_state != PlaybackState.Playing || _file == null) return;

        // 按倍速推进帧数
        int framesToAdvance = Mathf.Max(1, Mathf.RoundToInt(_playbackSpeed));
        for (int f = 0; f < framesToAdvance; f++)
        {
            _frameCounter++;

            // 回放在当前帧的所有操作
            while (_currentIndex < _file.Records.Count
                   && _file.Records[_currentIndex].Frame <= _frameCounter)
            {
                ExecuteAction(_file.Records[_currentIndex]);
                _currentIndex++;
            }

            // 检查是否已全部回放完成
            if (_currentIndex >= _file.Records.Count)
            {
                _state = PlaybackState.Finished;
                GameLog.Info($"[Replay] 回放结束 (帧 {_frameCounter}, {_file.Records.Count} 操作已全部执行)");
                return;
            }
        }
    }

    /// <summary>单帧步进（暂停状态下推进一步）。</summary>
    public static void StepOnce()
    {
        if (_file == null) return;
        if (_state != PlaybackState.Paused && _state != PlaybackState.Playing) return;

        _frameCounter++;
        while (_currentIndex < _file.Records.Count
               && _file.Records[_currentIndex].Frame <= _frameCounter)
        {
            ExecuteAction(_file.Records[_currentIndex]);
            _currentIndex++;
        }

        if (_currentIndex >= _file.Records.Count)
        {
            _state = PlaybackState.Finished;
            GameLog.Info("[Replay] 单步回放结束");
        }
    }

    /// <summary>获取回放进度摘要。</summary>
    public static string GetSummary()
    {
        if (_file == null) return "未加载回放";
        float progress = _file.Records.Count > 0 ? (_currentIndex * 100f / _file.Records.Count) : 0f;
        return $"状态={_state} 帧={_frameCounter} 进度={progress:F1}% ({_currentIndex}/{_file.Records.Count}) 速度={_playbackSpeed:F1}x";
    }

    // ---- 内部方法 ----

    /// <summary>执行单条回放操作。</summary>
    private static void ExecuteAction(ReplayRecorder.ReplayRecord record)
    {
        if (_main == null) return;

        try
        {
            var parms = record.Params;
            switch (record.Action)
            {
                case ReplayRecorder.ActionType.CommandMove:
                    _main.ReplayCommandMove(parms);
                    break;
                case ReplayRecorder.ActionType.CommandAttackMove:
                    _main.ReplayCommandAttackMove(parms);
                    break;
                case ReplayRecorder.ActionType.CommandAttack:
                    _main.ReplayCommandAttack(parms);
                    break;
                case ReplayRecorder.ActionType.CommandAttackBuilding:
                    _main.ReplayCommandAttackBuilding(parms);
                    break;
                case ReplayRecorder.ActionType.CommandStop:
                    _main.ReplayCommandStop();
                    break;
                case ReplayRecorder.ActionType.CommandSpyMission:
                    _main.ReplayCommandSpyMission(parms);
                    break;
                case ReplayRecorder.ActionType.CommandTerrainMod:
                    _main.ReplayCommandTerrainMod(parms);
                    break;
                case ReplayRecorder.ActionType.SaveSquad:
                    _main.ReplaySaveSquad(parms);
                    break;
                case ReplayRecorder.ActionType.SelectSquad:
                    _main.ReplaySelectSquad(parms);
                    break;
                case ReplayRecorder.ActionType.PlaceBuilding:
                    _main.ReplayPlaceBuilding(parms);
                    break;
                case ReplayRecorder.ActionType.CancelPlacement:
                    _main.ReplayCancelPlacement();
                    break;
                case ReplayRecorder.ActionType.SpawnUnit:
                    _main.ReplaySpawnUnit(parms);
                    break;
                case ReplayRecorder.ActionType.SpawnHarvester:
                    _main.ReplaySpawnHarvester();
                    break;
                case ReplayRecorder.ActionType.CancelProduction:
                    _main.ReplayCancelProduction(parms);
                    break;
                case ReplayRecorder.ActionType.SetRallyPoint:
                    _main.ReplaySetRallyPoint(parms);
                    break;
                case ReplayRecorder.ActionType.Nuke:
                    _main.ReplayNuke(parms);
                    break;
                case ReplayRecorder.ActionType.Lightning:
                    _main.ReplayLightning(parms);
                    break;
                case ReplayRecorder.ActionType.CruiseMissile:
                    _main.ReplayCruiseMissile(parms);
                    break;
                case ReplayRecorder.ActionType.RepairBuilding:
                    _main.ReplayRepairBuilding(parms);
                    break;
                case ReplayRecorder.ActionType.SellBuilding:
                    _main.ReplaySellBuilding(parms);
                    break;
                case ReplayRecorder.ActionType.ResearchTech:
                    _main.ReplayResearchTech(parms);
                    break;
                case ReplayRecorder.ActionType.AdvanceEra:
                    _main.ReplayAdvanceEra(parms);
                    break;
                case ReplayRecorder.ActionType.SelectCard:
                    _main.ReplaySelectCard(parms);
                    break;
                default:
                    GameLog.Warning($"[Replay] 未知操作类型: {record.Action}");
                    break;
            }
        }
        catch (System.Exception ex)
        {
            GameLog.Warning($"[Replay] 执行操作失败: {record.Action} — {ex.Message}");
        }
    }
}
