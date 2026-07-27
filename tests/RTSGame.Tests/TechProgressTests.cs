using System.Collections.Generic;
using Xunit;
using RTSGame;

namespace RTSGame.Tests;

/// <summary>
/// TechProgress（科技研究进度状态机）单元测试。
/// 覆盖审查报告指定的"科技研究进度"计算逻辑。
/// </summary>
public class TechProgressTests
{
    public TechProgressTests()
    {
        // P2-4: 确保在非Godot进程（单元测试）中使用硬编码fallback数据
        TestAssemblyInitializer.EnsureFallbackDataLoaded();
    }

    [Fact]
    public void StartResearch_SetsCurrentlyResearchingAndTimer()
    {
        var prog = new TechProgress();
        prog.StartResearch(TechTree.TechId.Mil_ArmorUpgrade);
        Assert.Equal(TechTree.TechId.Mil_ArmorUpgrade, prog.CurrentlyResearching);
        // ResearchTime=30f
        Assert.Equal(30f, prog.ResearchTimer);
        Assert.Equal(0f, prog.Progress); // 刚开始，进度0
    }

    [Fact]
    public void UpdateResearch_ReturnsNull_WhenNotResearching()
    {
        var prog = new TechProgress();
        var completed = prog.UpdateResearch(1f);
        Assert.Null(completed);
    }

    [Fact]
    public void UpdateResearch_ReducesTimer_AndReturnsNull_WhenNotFinished()
    {
        var prog = new TechProgress();
        prog.StartResearch(TechTree.TechId.Mil_ArmorUpgrade); // 30s
        var completed = prog.UpdateResearch(10f);
        Assert.Null(completed);
        Assert.Equal(20f, prog.ResearchTimer);
        // 进度 = 1 - 20/30 = 1/3
        Assert.True(prog.Progress > 0.30f && prog.Progress < 0.40f);
    }

    [Fact]
    public void UpdateResearch_ReturnsCompletedTech_WhenTimerReachesZero()
    {
        var prog = new TechProgress();
        prog.StartResearch(TechTree.TechId.Mil_ArmorUpgrade); // 30s
        var completed = prog.UpdateResearch(30f);
        Assert.Equal(TechTree.TechId.Mil_ArmorUpgrade, completed);
        Assert.Contains(TechTree.TechId.Mil_ArmorUpgrade, prog.Completed);
        Assert.Null(prog.CurrentlyResearching);
        Assert.Equal(0f, prog.ResearchTimer);
    }

    [Fact]
    public void UpdateResearch_OvershootTimer_StillCompletes_AndClampsTimerToZero()
    {
        var prog = new TechProgress();
        prog.StartResearch(TechTree.TechId.Eco_MiningEfficiency); // 25s
        var completed = prog.UpdateResearch(100f); // 远超所需
        Assert.Equal(TechTree.TechId.Eco_MiningEfficiency, completed);
        Assert.Equal(0f, prog.ResearchTimer);
    }

    [Fact]
    public void Progress_ReturnsZero_WhenNotResearching()
    {
        var prog = new TechProgress();
        Assert.Equal(0f, prog.Progress);
    }

    [Fact]
    public void ForceComplete_AddsToCompleted_AndClearsCurrentIfMatching()
    {
        var prog = new TechProgress();
        prog.StartResearch(TechTree.TechId.Mil_ArmorUpgrade);
        // 尤里卡强制完成当前研究中的科技
        prog.ForceComplete(TechTree.TechId.Mil_ArmorUpgrade);
        Assert.Contains(TechTree.TechId.Mil_ArmorUpgrade, prog.Completed);
        Assert.Null(prog.CurrentlyResearching);
    }

    [Fact]
    public void ForceComplete_OtherTech_DoesNotDisturbCurrentResearch()
    {
        var prog = new TechProgress();
        prog.StartResearch(TechTree.TechId.Mil_ArmorUpgrade);
        // 尤里卡强制完成另一个科技（分支已满时的金币补偿场景）
        prog.ForceComplete(TechTree.TechId.Def_Fortification);
        Assert.Contains(TechTree.TechId.Def_Fortification, prog.Completed);
        Assert.Equal(TechTree.TechId.Mil_ArmorUpgrade, prog.CurrentlyResearching);
    }

    [Fact]
    public void ForceClearResearch_ClearsCurrentWithoutAddingToCompleted()
    {
        var prog = new TechProgress();
        prog.StartResearch(TechTree.TechId.Mil_ArmorUpgrade);
        prog.ForceClearResearch();
        Assert.Null(prog.CurrentlyResearching);
        Assert.Empty(prog.Completed);
        Assert.Equal(0f, prog.ResearchTimer);
    }

    // ===== P0-2 存档/读档恢复逻辑 =====

    [Fact]
    public void Clear_ResetsAllState()
    {
        var prog = new TechProgress();
        prog.StartResearch(TechTree.TechId.Mil_ArmorUpgrade);
        prog.SetQueuedTech(TechTree.TechId.Mil_AmmoUpgrade);
        prog.Completed.Add(TechTree.TechId.Def_Fortification);

        prog.Clear();

        Assert.Empty(prog.Completed);
        Assert.Null(prog.CurrentlyResearching);
        Assert.Equal(0f, prog.ResearchTimer);
        Assert.Null(prog.QueuedTech);
    }

    [Fact]
    public void RestoreResearching_RebuildsInProgressState()
    {
        var prog = new TechProgress();
        prog.RestoreResearching(TechTree.TechId.Mil_AmmoUpgrade, timer: 12.5f);
        Assert.Equal(TechTree.TechId.Mil_AmmoUpgrade, prog.CurrentlyResearching);
        Assert.Equal(12.5f, prog.ResearchTimer);
    }

    [Fact]
    public void RestoreResearching_ClampsNegativeTimerToZero()
    {
        var prog = new TechProgress();
        prog.RestoreResearching(TechTree.TechId.Mil_AmmoUpgrade, timer: -5f);
        Assert.Equal(0f, prog.ResearchTimer);
    }

    [Fact]
    public void SetQueuedTech_StoresNextTech()
    {
        var prog = new TechProgress();
        prog.SetQueuedTech(TechTree.TechId.Eco_MassProduction);
        Assert.Equal(TechTree.TechId.Eco_MassProduction, prog.QueuedTech);
    }
}
