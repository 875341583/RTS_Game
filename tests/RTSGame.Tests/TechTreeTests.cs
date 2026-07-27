using System.Collections.Generic;
using Xunit;
using RTSGame;

namespace RTSGame.Tests;

/// <summary>
/// TechTree 与 TechProgress 单元测试。
/// 覆盖审查报告指定的"科技研究进度"可测试点。
/// </summary>
public class TechTreeTests
{
    public TechTreeTests()
    {
        // P2-4: 确保在非Godot进程（单元测试）中使用硬编码fallback数据
        TestAssemblyInitializer.EnsureFallbackDataLoaded();
    }

    // ===== TechTree.CanResearch 前置条件逻辑 =====

    [Fact]
    public void CanResearch_Tier1_NoPrerequisite_ReturnsTrue_WhenMoneyEnoughAndNoTechCenterNeeded()
    {
        // Tier1军事科技 Mil_ArmorUpgrade 不需要科技中心（RequiresTechCenter=false），无前置
        var completed = new HashSet<TechTree.TechId>();
        bool ok = TechTree.CanResearch(completed, TechTree.TechId.Mil_ArmorUpgrade, hasTechCenter: false, money: 500);
        Assert.True(ok);
    }

    [Fact]
    public void CanResearch_Tier1_RequiresTechCenter_ReturnsFalse_WhenNoTechCenter()
    {
        // Tier2军事科技 Mil_AmmoUpgrade 需要科技中心，即便资金够、前置满足
        var completed = new HashSet<TechTree.TechId> { TechTree.TechId.Mil_ArmorUpgrade };
        bool ok = TechTree.CanResearch(completed, TechTree.TechId.Mil_AmmoUpgrade, hasTechCenter: false, money: 1000);
        Assert.False(ok);
    }

    [Fact]
    public void CanResearch_Tier2_ReturnsFalse_WhenPrerequisiteNotMet()
    {
        // Mil_AmmoUpgrade 前置是 Mil_ArmorUpgrade，未完成则不可研究
        var completed = new HashSet<TechTree.TechId>();
        bool ok = TechTree.CanResearch(completed, TechTree.TechId.Mil_AmmoUpgrade, hasTechCenter: true, money: 1000);
        Assert.False(ok);
    }

    [Fact]
    public void CanResearch_ReturnsFalse_WhenInsufficientMoney()
    {
        // Mil_ArmorUpgrade 成本500，资金不足不可研究
        var completed = new HashSet<TechTree.TechId>();
        bool ok = TechTree.CanResearch(completed, TechTree.TechId.Mil_ArmorUpgrade, hasTechCenter: false, money: 499);
        Assert.False(ok);
    }

    [Fact]
    public void CanResearch_ReturnsFalse_WhenAlreadyCompleted()
    {
        // 已研究的科技不可重复研究
        var completed = new HashSet<TechTree.TechId> { TechTree.TechId.Mil_ArmorUpgrade };
        bool ok = TechTree.CanResearch(completed, TechTree.TechId.Mil_ArmorUpgrade, hasTechCenter: false, money: 9999);
        Assert.False(ok);
    }

    [Fact]
    public void CanResearch_Tier4_AllPrerequisitesMet_ReturnsTrue()
    {
        // Mil_HeroTraining 是Tier4，前置链: Armor→Ammo→AdvancedTactics→HeroTraining
        var completed = new HashSet<TechTree.TechId>
        {
            TechTree.TechId.Mil_ArmorUpgrade,
            TechTree.TechId.Mil_AmmoUpgrade,
            TechTree.TechId.Mil_AdvancedTactics,
        };
        bool ok = TechTree.CanResearch(completed, TechTree.TechId.Mil_HeroTraining, hasTechCenter: true, money: 1500);
        Assert.True(ok);
    }

    // ===== 节点数据完整性 =====

    [Theory]
    [InlineData(TechTree.TechId.Mil_ArmorUpgrade, "军事", 1, 500)]
    [InlineData(TechTree.TechId.Mil_HeroTraining, "军事", 4, 1500)]
    [InlineData(TechTree.TechId.Eco_MiningEfficiency, "经济", 1, 400)]
    [InlineData(TechTree.TechId.Eco_AdvancedLogistics, "经济", 4, 1300)]
    [InlineData(TechTree.TechId.Def_Fortification, "防御", 1, 450)]
    [InlineData(TechTree.TechId.Def_RepairSystems, "防御", 4, 1200)]
    public void NodeData_BranchTierCost_ConsistentWithDefinition(TechTree.TechId id, string expectedBranch, int expectedTier, int expectedCost)
    {
        var node = TechTree.Nodes[id];
        Assert.Equal(expectedBranch, node.Branch);
        Assert.Equal(expectedTier, node.Tier);
        Assert.Equal(expectedCost, node.Cost);
    }

    [Fact]
    public void GetByBranchTier_ReturnsCorrectNode()
    {
        var node = TechTree.GetByBranchTier("军事", 2);
        Assert.NotNull(node);
        Assert.Equal(TechTree.TechId.Mil_AmmoUpgrade, node!.Id);
    }

    [Fact]
    public void GetByBranchTier_ReturnsNull_WhenTierOutOfRange()
    {
        var node = TechTree.GetByBranchTier("军事", 99);
        Assert.Null(node);
    }

    // ===== 前置链完整性：每个分支应为线性链 =====

    [Fact]
    public void MilitaryBranch_FormsLinearPrerequisiteChain()
    {
        // Armor(T1) → Ammo(T2) → AdvancedTactics(T3) → HeroTraining(T4)
        Assert.Empty(TechTree.Nodes[TechTree.TechId.Mil_ArmorUpgrade].Prerequisites);
        Assert.Equal(new[] { TechTree.TechId.Mil_ArmorUpgrade }, TechTree.Nodes[TechTree.TechId.Mil_AmmoUpgrade].Prerequisites);
        Assert.Equal(new[] { TechTree.TechId.Mil_AmmoUpgrade }, TechTree.Nodes[TechTree.TechId.Mil_AdvancedTactics].Prerequisites);
        Assert.Equal(new[] { TechTree.TechId.Mil_AdvancedTactics }, TechTree.Nodes[TechTree.TechId.Mil_HeroTraining].Prerequisites);
    }

    [Fact]
    public void EconomyBranch_FormsLinearPrerequisiteChain()
    {
        Assert.Empty(TechTree.Nodes[TechTree.TechId.Eco_MiningEfficiency].Prerequisites);
        Assert.Equal(new[] { TechTree.TechId.Eco_MiningEfficiency }, TechTree.Nodes[TechTree.TechId.Eco_MassProduction].Prerequisites);
        Assert.Equal(new[] { TechTree.TechId.Eco_MassProduction }, TechTree.Nodes[TechTree.TechId.Eco_ResourceNetwork].Prerequisites);
        Assert.Equal(new[] { TechTree.TechId.Eco_ResourceNetwork }, TechTree.Nodes[TechTree.TechId.Eco_AdvancedLogistics].Prerequisites);
    }

    [Fact]
    public void DefenseBranch_FormsLinearPrerequisiteChain()
    {
        Assert.Empty(TechTree.Nodes[TechTree.TechId.Def_Fortification].Prerequisites);
        Assert.Equal(new[] { TechTree.TechId.Def_Fortification }, TechTree.Nodes[TechTree.TechId.Def_PowerGrid].Prerequisites);
        Assert.Equal(new[] { TechTree.TechId.Def_PowerGrid }, TechTree.Nodes[TechTree.TechId.Def_AdvancedTurrets].Prerequisites);
        Assert.Equal(new[] { TechTree.TechId.Def_AdvancedTurrets }, TechTree.Nodes[TechTree.TechId.Def_RepairSystems].Prerequisites);
    }
}
