using System.Collections.Generic;
using Xunit;
using RTSGame;

namespace RTSGame.Tests;

/// <summary>
/// EraSystem 时代系统单元测试。
/// 覆盖审查报告指定的"电力系统初始化"相关时代加成、时代门控与升级条件逻辑。
/// </summary>
public class EraSystemTests
{
    // ===== 时代加成乘数（累计式）=====

    [Theory]
    [InlineData(EraSystem.Era.Stone, 1.00f)]
    [InlineData(EraSystem.Era.Bronze, 1.05f)]
    [InlineData(EraSystem.Era.Industrial, 1.10f)]
    [InlineData(EraSystem.Era.Information, 1.15f)]
    public void GetDamageMultiplier_Increases5PercentPerEra(EraSystem.Era era, float expected)
    {
        Assert.Equal(expected, EraSystem.GetDamageMultiplier(era));
    }

    [Theory]
    [InlineData(EraSystem.Era.Stone, 1.00f)]
    [InlineData(EraSystem.Era.Bronze, 1.05f)]
    [InlineData(EraSystem.Era.Industrial, 1.10f)]
    [InlineData(EraSystem.Era.Information, 1.15f)]
    public void GetHealthMultiplier_Increases5PercentPerEra(EraSystem.Era era, float expected)
    {
        Assert.Equal(expected, EraSystem.GetHealthMultiplier(era));
    }

    [Theory]
    [InlineData(EraSystem.Era.Stone, 1.00f)]
    [InlineData(EraSystem.Era.Bronze, 1.10f)]
    [InlineData(EraSystem.Era.Industrial, 1.20f)]
    [InlineData(EraSystem.Era.Information, 1.30f)]
    public void GetMiningMultiplier_Increases10PercentPerEra(EraSystem.Era era, float expected)
    {
        Assert.Equal(expected, EraSystem.GetMiningMultiplier(era));
    }

    [Theory]
    [InlineData(EraSystem.Era.Stone, 1.00f)]
    [InlineData(EraSystem.Era.Bronze, 1.10f)]
    [InlineData(EraSystem.Era.Industrial, 1.20f)]
    [InlineData(EraSystem.Era.Information, 1.30f)]
    public void GetBuildSpeedMultiplier_Increases10PercentPerEra(EraSystem.Era era, float expected)
    {
        Assert.Equal(expected, EraSystem.GetBuildSpeedMultiplier(era));
    }

    // ===== 时代数据完整性 =====

    [Fact]
    public void Eras_HaveFourErasInOrder()
    {
        Assert.Equal(4, EraSystem.Eras.Length);
        Assert.Equal(EraSystem.Era.Stone, EraSystem.Eras[0].Id);
        Assert.Equal(EraSystem.Era.Information, EraSystem.Eras[3].Id);
    }

    [Theory]
    [InlineData(EraSystem.Era.Bronze, 800, 30f)]
    [InlineData(EraSystem.Era.Industrial, 1500, 45f)]
    [InlineData(EraSystem.Era.Information, 2500, 60f)]
    public void Era_UpgradeCostAndTime_MatchDefinition(EraSystem.Era era, int expectedCost, float expectedTime)
    {
        var info = EraSystem.Eras[(int)era];
        Assert.Equal(expectedCost, info.UpgradeCost);
        Assert.Equal(expectedTime, info.UpgradeTime);
    }

    // ===== CanBuildBuilding 时代门控 =====

    [Fact]
    public void CanBuildBuilding_StoneEra_OnlyAllowsBasePowerPlantBarracks()
    {
        Assert.True(EraSystem.CanBuildBuilding(EraSystem.Era.Stone, BuildingType.Base));
        Assert.True(EraSystem.CanBuildBuilding(EraSystem.Era.Stone, BuildingType.PowerPlant));
        Assert.True(EraSystem.CanBuildBuilding(EraSystem.Era.Stone, BuildingType.Barracks));
        // 石器时代不能造车厂
        Assert.False(EraSystem.CanBuildBuilding(EraSystem.Era.Stone, BuildingType.WarFactory));
        Assert.False(EraSystem.CanBuildBuilding(EraSystem.Era.Stone, BuildingType.TechCenter));
        Assert.False(EraSystem.CanBuildBuilding(EraSystem.Era.Stone, BuildingType.NukeSilo));
    }

    [Fact]
    public void CanBuildBuilding_BronzeEra_AllowsWarFactoryAndTurrets()
    {
        Assert.True(EraSystem.CanBuildBuilding(EraSystem.Era.Bronze, BuildingType.WarFactory));
        Assert.True(EraSystem.CanBuildBuilding(EraSystem.Era.Bronze, BuildingType.Turret));
        Assert.True(EraSystem.CanBuildBuilding(EraSystem.Era.Bronze, BuildingType.AntiAirTurret));
        Assert.True(EraSystem.CanBuildBuilding(EraSystem.Era.Bronze, BuildingType.RepairPad));
        // 青铜时代还不能造科技中心
        Assert.False(EraSystem.CanBuildBuilding(EraSystem.Era.Bronze, BuildingType.TechCenter));
    }

    [Fact]
    public void CanBuildBuilding_IndustrialEra_AllowsTechCenterAndAirfield()
    {
        Assert.True(EraSystem.CanBuildBuilding(EraSystem.Era.Industrial, BuildingType.TechCenter));
        // 工业时代还不能造船厂/超武
        Assert.False(EraSystem.CanBuildBuilding(EraSystem.Era.Industrial, BuildingType.Shipyard));
        Assert.False(EraSystem.CanBuildBuilding(EraSystem.Era.Industrial, BuildingType.NukeSilo));
        Assert.False(EraSystem.CanBuildBuilding(EraSystem.Era.Industrial, BuildingType.MissileSilo));
    }

    [Fact]
    public void CanBuildBuilding_InformationEra_AllowsEverything()
    {
        Assert.True(EraSystem.CanBuildBuilding(EraSystem.Era.Information, BuildingType.Shipyard));
        Assert.True(EraSystem.CanBuildBuilding(EraSystem.Era.Information, BuildingType.NukeSilo));
        Assert.True(EraSystem.CanBuildBuilding(EraSystem.Era.Information, BuildingType.MissileSilo));
        Assert.True(EraSystem.CanBuildBuilding(EraSystem.Era.Information, BuildingType.LightningTower));
    }

    // ===== CanProduceUnit 时代门控 =====

    [Fact]
    public void CanProduceUnit_StoneEra_OnlyBasicUnits()
    {
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Stone, UnitType.Infantry));
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Stone, UnitType.LightTank));
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Stone, UnitType.Harvester));
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Stone, UnitType.Sapper));
        // 石器时代不能造重坦
        Assert.False(EraSystem.CanProduceUnit(EraSystem.Era.Stone, UnitType.HeavyTank));
        Assert.False(EraSystem.CanProduceUnit(EraSystem.Era.Stone, UnitType.Artillery));
    }

    [Fact]
    public void CanProduceUnit_BronzeEra_UnlocksHeavyUnits()
    {
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Bronze, UnitType.HeavyTank));
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Bronze, UnitType.Artillery));
        // 青铜时代还不能造火箭炮/导弹车/英雄/间谍/空军/海军
        Assert.False(EraSystem.CanProduceUnit(EraSystem.Era.Bronze, UnitType.RocketLauncher));
        Assert.False(EraSystem.CanProduceUnit(EraSystem.Era.Bronze, UnitType.Hero));
        Assert.False(EraSystem.CanProduceUnit(EraSystem.Era.Bronze, UnitType.Spy));
        Assert.False(EraSystem.CanProduceUnit(EraSystem.Era.Bronze, UnitType.Fighter));
        Assert.False(EraSystem.CanProduceUnit(EraSystem.Era.Bronze, UnitType.Destroyer));
    }

    [Fact]
    public void CanProduceUnit_IndustrialEra_UnlocksRocketsAndAirForce()
    {
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Industrial, UnitType.RocketLauncher));
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Industrial, UnitType.MissileTank));
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Industrial, UnitType.Fighter));
        // 工业时代还不能造英雄/间谍/海军
        Assert.False(EraSystem.CanProduceUnit(EraSystem.Era.Industrial, UnitType.Hero));
        Assert.False(EraSystem.CanProduceUnit(EraSystem.Era.Industrial, UnitType.Spy));
        Assert.False(EraSystem.CanProduceUnit(EraSystem.Era.Industrial, UnitType.Destroyer));
    }

    [Fact]
    public void CanProduceUnit_InformationEra_AllowsEverything()
    {
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Information, UnitType.Hero));
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Information, UnitType.Spy));
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Information, UnitType.Destroyer));
        Assert.True(EraSystem.CanProduceUnit(EraSystem.Era.Information, UnitType.Submarine));
    }

    // ===== CanAdvance 升级条件 =====

    [Fact]
    public void CanAdvance_FromStone_RequiresBarracksAnd800Money()
    {
        // 升青铜需兵营+800资金
        Assert.True(EraSystem.CanAdvance(EraSystem.Era.Stone, _ => true, money: 800));
        Assert.False(EraSystem.CanAdvance(EraSystem.Era.Stone, _ => true, money: 799)); // 钱不够
        Assert.False(EraSystem.CanAdvance(EraSystem.Era.Stone, t => t != BuildingType.Barracks, money: 9999)); // 无兵营
    }

    [Fact]
    public void CanAdvance_FromBronze_RequiresWarFactoryAnd1500Money()
    {
        Assert.True(EraSystem.CanAdvance(EraSystem.Era.Bronze, t => t == BuildingType.WarFactory, money: 1500));
        Assert.False(EraSystem.CanAdvance(EraSystem.Era.Bronze, t => t != BuildingType.WarFactory, money: 9999));
    }

    [Fact]
    public void CanAdvance_FromIndustrial_RequiresTechCenterAnd2500Money()
    {
        Assert.True(EraSystem.CanAdvance(EraSystem.Era.Industrial, t => t == BuildingType.TechCenter, money: 2500));
        Assert.False(EraSystem.CanAdvance(EraSystem.Era.Industrial, t => t != BuildingType.TechCenter, money: 9999));
    }

    [Fact]
    public void CanAdvance_FromInformation_ReturnsFalse_AlreadyMaxEra()
    {
        Assert.False(EraSystem.CanAdvance(EraSystem.Era.Information, _ => true, money: 99999));
    }

    // ===== GetNextEra =====

    [Theory]
    [InlineData(EraSystem.Era.Stone, EraSystem.Era.Bronze)]
    [InlineData(EraSystem.Era.Bronze, EraSystem.Era.Industrial)]
    [InlineData(EraSystem.Era.Industrial, EraSystem.Era.Information)]
    public void GetNextEra_ReturnsNextEra(EraSystem.Era current, EraSystem.Era expectedNext)
    {
        var next = EraSystem.GetNextEra(current);
        Assert.NotNull(next);
        Assert.Equal(expectedNext, next!.Id);
    }

    [Fact]
    public void GetNextEra_InformationEra_ReturnsNull()
    {
        Assert.Null(EraSystem.GetNextEra(EraSystem.Era.Information));
    }
}
