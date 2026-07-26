using System.Collections.Generic;
using Xunit;
using RTSGame;

namespace RTSGame.Tests;

/// <summary>
/// TacticalCards 战术卡乘数单元测试。
/// 覆盖战术卡对单位属性/经济/生产的影响计算逻辑。
/// </summary>
public class TacticalCardsTests
{
    // ===== 移动速度乘数 =====

    [Fact]
    public void GetMoveSpeedMul_BlitzTactics_Returns115Percent()
    {
        Assert.Equal(1.15f, TacticalCards.GetMoveSpeedMul(TacticalCards.CardId.BlitzTactics));
    }

    [Fact]
    public void GetMoveSpeedMul_OtherCards_Returns100Percent()
    {
        Assert.Equal(1f, TacticalCards.GetMoveSpeedMul(TacticalCards.CardId.IronFlood));
        Assert.Equal(1f, TacticalCards.GetMoveSpeedMul(TacticalCards.CardId.Fortress));
    }

    [Fact]
    public void GetMoveSpeedMul_NullCard_Returns100Percent()
    {
        Assert.Equal(1f, TacticalCards.GetMoveSpeedMul(null));
    }

    // ===== 生产时间乘数（越小越快）=====

    [Theory]
    [InlineData(TacticalCards.CardId.BlitzTactics, 0.85f)]
    [InlineData(TacticalCards.CardId.RapidDeploy, 0.80f)]
    [InlineData(TacticalCards.CardId.IronFlood, 1f)]
    [InlineData(null, 1f)]
    public void GetProduceTimeMul_ReturnsExpected(TacticalCards.CardId? card, float expected)
    {
        Assert.Equal(expected, TacticalCards.GetProduceTimeMul(card));
    }

    // ===== 坦克血量/攻击乘数 =====

    [Fact]
    public void GetTankHealthMul_IronFlood_Returns120Percent()
    {
        Assert.Equal(1.20f, TacticalCards.GetTankHealthMul(TacticalCards.CardId.IronFlood));
    }

    [Fact]
    public void GetTankHealthMul_WarMachine_Returns90Percent()
    {
        // 战争机器：血量-10%
        Assert.Equal(0.90f, TacticalCards.GetTankHealthMul(TacticalCards.CardId.WarMachine));
    }

    [Fact]
    public void GetTankDamageMul_IronFlood_Returns110Percent()
    {
        Assert.Equal(1.10f, TacticalCards.GetTankDamageMul(TacticalCards.CardId.IronFlood));
    }

    // ===== 步兵血量/成本乘数 =====

    [Fact]
    public void GetInfantryHealthMul_InfantryAssault_Returns125Percent()
    {
        Assert.Equal(1.25f, TacticalCards.GetInfantryHealthMul(TacticalCards.CardId.InfantryAssault));
    }

    [Fact]
    public void GetInfantryHealthMul_WarMachine_Returns90Percent()
    {
        Assert.Equal(0.90f, TacticalCards.GetInfantryHealthMul(TacticalCards.CardId.WarMachine));
    }

    [Fact]
    public void GetInfantryCostMul_InfantryAssault_Returns80Percent()
    {
        Assert.Equal(0.80f, TacticalCards.GetInfantryCostMul(TacticalCards.CardId.InfantryAssault));
    }

    // ===== 全单位攻击/血量乘数 =====

    [Fact]
    public void GetAllDamageMul_WarMachine_Returns115Percent()
    {
        Assert.Equal(1.15f, TacticalCards.GetAllDamageMul(TacticalCards.CardId.WarMachine));
    }

    [Fact]
    public void GetAllHealthMul_WarMachine_Returns90Percent()
    {
        Assert.Equal(0.90f, TacticalCards.GetAllHealthMul(TacticalCards.CardId.WarMachine));
    }

    // ===== 建筑相关乘数 =====

    [Fact]
    public void GetBuildingHealthMul_Fortress_Returns130Percent()
    {
        Assert.Equal(1.30f, TacticalCards.GetBuildingHealthMul(TacticalCards.CardId.Fortress));
    }

    [Fact]
    public void GetTurretRangeMul_Fortress_Returns115Percent()
    {
        Assert.Equal(1.15f, TacticalCards.GetTurretRangeMul(TacticalCards.CardId.Fortress));
    }

    // ===== 经济乘数 =====

    [Fact]
    public void GetMiningMul_BlitzEconomy_Returns120Percent()
    {
        Assert.Equal(1.20f, TacticalCards.GetMiningMul(TacticalCards.CardId.BlitzEconomy));
    }

    [Fact]
    public void GetStartMoneyMul_BlitzEconomy_Returns150Percent()
    {
        Assert.Equal(1.50f, TacticalCards.GetStartMoneyMul(TacticalCards.CardId.BlitzEconomy));
    }

    // ===== 科技/时代乘数 =====

    [Fact]
    public void GetResearchSpeedMul_TechLeap_Returns150Percent()
    {
        Assert.Equal(1.50f, TacticalCards.GetResearchSpeedMul(TacticalCards.CardId.TechLeap));
    }

    [Fact]
    public void GetEraUpgradeSpeedMul_TechLeap_Returns130Percent()
    {
        Assert.Equal(1.30f, TacticalCards.GetEraUpgradeSpeedMul(TacticalCards.CardId.TechLeap));
    }

    // ===== 单位上限加成 =====

    [Fact]
    public void GetUnitCapBonus_RapidDeploy_Returns10()
    {
        Assert.Equal(10, TacticalCards.GetUnitCapBonus(TacticalCards.CardId.RapidDeploy));
    }

    [Fact]
    public void GetUnitCapBonus_OtherCards_Returns0()
    {
        Assert.Equal(0, TacticalCards.GetUnitCapBonus(TacticalCards.CardId.IronFlood));
        Assert.Equal(0, TacticalCards.GetUnitCapBonus(null));
    }

    // ===== 卡片定义完整性 =====

    [Fact]
    public void Cards_Dictionary_ContainsAllEightCards()
    {
        Assert.Equal(8, TacticalCards.Cards.Count);
        Assert.Contains(TacticalCards.CardId.BlitzEconomy, TacticalCards.Cards.Keys);
        Assert.Contains(TacticalCards.CardId.WarMachine, TacticalCards.Cards.Keys);
        Assert.Contains(TacticalCards.CardId.RapidDeploy, TacticalCards.Cards.Keys);
    }

    [Theory]
    [InlineData(TacticalCards.CardId.BlitzEconomy, "闪电经济")]
    [InlineData(TacticalCards.CardId.IronFlood, "钢铁洪流")]
    [InlineData(TacticalCards.CardId.TechLeap, "科技跃进")]
    public void CardInfo_Name_MatchesDefinition(TacticalCards.CardId id, string expectedName)
    {
        Assert.Equal(expectedName, TacticalCards.Cards[id].Name);
    }
}
