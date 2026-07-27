using Xunit;
using RTSGame;

namespace RTSGame.Tests;

/// <summary>P3-1: 回放录制器与回放文件序列化测试。</summary>
public class ReplayRecorderTests
{
    public ReplayRecorderTests()
    {
        // 单元测试在非Godot运行时中执行，必须开启静默模式避免 GD.Print 原生IO崩溃
        ReplayRecorder.SetSilent(true);
    }

    [Fact]
    public void Start_SetsRecording()
    {
        ReplayRecorder.Start(12345, "Normal", 32, "Default");
        Assert.True(ReplayRecorder.IsRecording);
        ReplayRecorder.Stop();
    }

    [Fact]
    public void Record_IncrementsRecordCount()
    {
        ReplayRecorder.Start(99999, "Hard", 64, "Snow");
        Assert.Equal(0, ReplayRecorder.RecordCount);

        ReplayRecorder.Record(ReplayRecorder.ActionType.CommandMove, new { X = 100f, Y = 200f });
        Assert.Equal(1, ReplayRecorder.RecordCount);

        ReplayRecorder.Record(ReplayRecorder.ActionType.CommandAttack);
        Assert.Equal(2, ReplayRecorder.RecordCount);

        ReplayRecorder.Stop();
    }

    [Fact]
    public void Record_WhenNotRecording_DoesNothing()
    {
        ReplayRecorder.Stop();
        var countBefore = ReplayRecorder.RecordCount;
        ReplayRecorder.Record(ReplayRecorder.ActionType.CommandMove);
        Assert.Equal(countBefore, ReplayRecorder.RecordCount);
    }

    [Fact]
    public void Tick_IncrementsFrameCounter()
    {
        ReplayRecorder.Start(1, "Easy", 32, "Default");
        ReplayRecorder.Tick();
        ReplayRecorder.Tick();
        ReplayRecorder.Tick();
        // Frame counter is internal, but we can verify via Save/Load
        ReplayRecorder.Record(ReplayRecorder.ActionType.CommandStop);
        ReplayRecorder.Stop();
        // After Stop, record should have frame=3 (0-based: Tick x3 = frame 3)
        // Verify through save/load
        var path = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "Godot", "app_userdata", "铁幕突袭",
            $"test_replay_{System.Guid.NewGuid():N}.replay");
        try
        {
            var savedPath = ReplayRecorder.Save(path);
            var loaded = ReplayRecorder.Load(savedPath);
            Assert.NotNull(loaded);
            Assert.Equal(1, loaded.Records.Count);
            Assert.Equal(3, loaded.Records[0].Frame); // Tick was called 3 times before Record
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Stop_StopsRecording()
    {
        ReplayRecorder.Start(42, "Normal", 32, "Default");
        Assert.True(ReplayRecorder.IsRecording);
        ReplayRecorder.Stop();
        Assert.False(ReplayRecorder.IsRecording);
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        ReplayRecorder.Start(77777, "Brutal", 96, "Desert");
        ReplayRecorder.Tick(); // frame 1
        ReplayRecorder.Record(ReplayRecorder.ActionType.SpawnUnit, new { Type = "LightTank" });
        ReplayRecorder.Tick(); // frame 2
        ReplayRecorder.Record(ReplayRecorder.ActionType.PlaceBuilding, new { Type = "PowerPlant" });
        ReplayRecorder.Stop();

        var path = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "Godot", "app_userdata", "铁幕突袭",
            $"test_replay_{System.Guid.NewGuid():N}.replay");
        try
        {
            ReplayRecorder.Save(path);
            var loaded = ReplayRecorder.Load(path);

            Assert.NotNull(loaded);
            Assert.Equal(2, loaded.Records.Count);
            Assert.Equal(77777ul, loaded.Header.MapSeed);
            Assert.Equal("Brutal", loaded.Header.Difficulty);
            Assert.Equal(96, loaded.Header.MapSize);
            Assert.Equal("Desert", loaded.Header.MapTheme);

            Assert.Equal(1, loaded.Records[0].Frame);
            Assert.Equal(ReplayRecorder.ActionType.SpawnUnit, loaded.Records[0].Action);
            Assert.Equal(2, loaded.Records[1].Frame);
            Assert.Equal(ReplayRecorder.ActionType.PlaceBuilding, loaded.Records[1].Action);
        }
        finally
        {
            if (System.IO.File.Exists(path))
                System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void Load_NonexistentFile_ReturnsNull()
    {
        var result = ReplayRecorder.Load("nonexistent_file.replay");
        Assert.Null(result);
    }

    [Fact]
    public void GetSummary_ReturnsInfo()
    {
        ReplayRecorder.Start(123, "Easy", 32, "Default");
        var summary = ReplayRecorder.GetSummary();
        Assert.Contains("123", summary);
        Assert.Contains("Easy", summary);
        ReplayRecorder.Stop();
    }

    [Fact]
    public void Record_AllActionTypes_DoNotThrow()
    {
        ReplayRecorder.Start(1, "Normal", 32, "Default");
        foreach (var actionType in System.Enum.GetValues(typeof(ReplayRecorder.ActionType)))
        {
            ReplayRecorder.Record((ReplayRecorder.ActionType)actionType, new { Test = "data" });
        }
        Assert.Equal(System.Enum.GetValues(typeof(ReplayRecorder.ActionType)).Length, ReplayRecorder.RecordCount);
        ReplayRecorder.Stop();
    }
}

/// <summary>P3-1: 回放播放器测试。</summary>
public class ReplayPlayerTests
{
    public ReplayPlayerTests()
    {
        ReplayRecorder.SetSilent(true);
    }

    [Fact]
    public void State_Initially_Stopped()
    {
        Assert.Equal(ReplayPlayer.PlaybackState.Stopped, ReplayPlayer.State);
    }

    [Fact]
    public void Stop_ResetsState()
    {
        ReplayPlayer.Stop();
        Assert.Equal(ReplayPlayer.PlaybackState.Stopped, ReplayPlayer.State);
        Assert.Equal(0, ReplayPlayer.TotalActions);
    }

    [Fact]
    public void GetSummary_NoFile_ReturnsNotLoaded()
    {
        var summary = ReplayPlayer.GetSummary();
        Assert.Contains("未加载", summary);
    }

    [Fact]
    public void Pause_WhenNotPlaying_DoesNotThrow()
    {
        ReplayPlayer.Pause(); // Should not throw
        Assert.Equal(ReplayPlayer.PlaybackState.Stopped, ReplayPlayer.State);
    }

    [Fact]
    public void Resume_WhenNotPaused_DoesNotThrow()
    {
        ReplayPlayer.Resume(); // Should not throw
        Assert.Equal(ReplayPlayer.PlaybackState.Stopped, ReplayPlayer.State);
    }
}
