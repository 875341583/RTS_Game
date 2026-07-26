using System.Collections.Generic;
using Xunit;
using RTSGame;

namespace RTSGame.Tests;

/// <summary>
/// EraProgress（时代进度状态机）单元测试。
/// 覆盖时代升级计时与 P0-2 存档恢复逻辑。
/// </summary>
public class EraProgressTests
{
    [Fact]
    public void InitialState_IsStoneEra_NotUpgrading()
    {
        var prog = new EraProgress();
        Assert.Equal(EraSystem.Era.Stone, prog.CurrentEra);
        Assert.False(prog.IsUpgrading);
        Assert.Equal(0f, prog.UpgradeTimer);
        Assert.Equal(0f, prog.Progress);
    }

    [Fact]
    public void StartUpgrade_FromStone_SetsBronzeTimer()
    {
        var prog = new EraProgress();
        prog.StartUpgrade();
        Assert.True(prog.IsUpgrading);
        // 石器→青铜：UpgradeTime=30s
        Assert.Equal(30f, prog.UpgradeTimer);
        // 仍是石器时代，升级完成后才变青铜
        Assert.Equal(EraSystem.Era.Stone, prog.CurrentEra);
    }

    [Fact]
    public void UpdateUpgrade_ReducesTimer_ReturnsFalse_WhenNotFinished()
    {
        var prog = new EraProgress();
        prog.StartUpgrade(); // 30s
        bool done = prog.UpdateUpgrade(10f);
        Assert.False(done);
        Assert.True(prog.IsUpgrading);
        Assert.Equal(20f, prog.UpgradeTimer);
    }

    [Fact]
    public void UpdateUpgrade_ReturnsTrue_AndAdvancesEra_WhenFinished()
    {
        var prog = new EraProgress();
        prog.StartUpgrade(); // 30s
        bool done = prog.UpdateUpgrade(30f);
        Assert.True(done);
        Assert.False(prog.IsUpgrading);
        Assert.Equal(EraSystem.Era.Bronze, prog.CurrentEra);
        Assert.Equal(0f, prog.UpgradeTimer);
    }

    [Fact]
    public void UpdateUpgrade_Overshoot_StillCompletes()
    {
        var prog = new EraProgress();
        prog.StartUpgrade();
        bool done = prog.UpdateUpgrade(999f);
        Assert.True(done);
        Assert.Equal(EraSystem.Era.Bronze, prog.CurrentEra);
    }

    [Fact]
    public void FullProgression_StoneToInformation()
    {
        var prog = new EraProgress();
        // 石器→青铜
        prog.StartUpgrade();
        Assert.True(prog.UpdateUpgrade(30f));
        Assert.Equal(EraSystem.Era.Bronze, prog.CurrentEra);
        // 青铜→工业
        prog.StartUpgrade();
        Assert.True(prog.UpdateUpgrade(45f));
        Assert.Equal(EraSystem.Era.Industrial, prog.CurrentEra);
        // 工业→信息
        prog.StartUpgrade();
        Assert.True(prog.UpdateUpgrade(60f));
        Assert.Equal(EraSystem.Era.Information, prog.CurrentEra);
    }

    [Fact]
    public void StartUpgrade_AtMaxEra_DoesNothing()
    {
        // 跳到信息时代后尝试升级
        var prog = new EraProgress();
        prog.Restore(EraSystem.Era.Information, false, 0f);
        prog.StartUpgrade(); // GetNextEra返回null，应安全无操作
        Assert.False(prog.IsUpgrading);
    }

    // ===== P0-2 存档/读档恢复逻辑 =====

    [Fact]
    public void Reset_ReturnsToStoneEra()
    {
        var prog = new EraProgress();
        prog.Restore(EraSystem.Era.Industrial, true, 20f);
        prog.Reset();
        Assert.Equal(EraSystem.Era.Stone, prog.CurrentEra);
        Assert.False(prog.IsUpgrading);
        Assert.Equal(0f, prog.UpgradeTimer);
    }

    [Fact]
    public void Restore_SetsEraAndUpgradeState()
    {
        var prog = new EraProgress();
        prog.Restore(EraSystem.Era.Bronze, true, 15.5f);
        Assert.Equal(EraSystem.Era.Bronze, prog.CurrentEra);
        Assert.True(prog.IsUpgrading);
        Assert.Equal(15.5f, prog.UpgradeTimer);
    }

    [Fact]
    public void Restore_ClampsNegativeTimerToZero()
    {
        var prog = new EraProgress();
        prog.Restore(EraSystem.Era.Bronze, true, -10f);
        Assert.Equal(0f, prog.UpgradeTimer);
    }
}
