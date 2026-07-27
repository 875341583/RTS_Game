using RTSGame;
using Xunit;

namespace RTSGame.Tests;

/// <summary>
/// P2-4: TerrainModifiers 数据驱动查表测试 — 验证所有速度修正值与原始硬编码完全一致。
/// </summary>
public class TerrainModifiersTests
{
    public TerrainModifiersTests()
    {
        TestAssemblyInitializer.EnsureFallbackDataLoaded();
    }

    // ===== 速度修正：Road =====

    [Theory]
    [InlineData(TerrainUnitCategory.Infantry, 1.2f)]
    [InlineData(TerrainUnitCategory.LightVehicle, 1.3f)]
    [InlineData(TerrainUnitCategory.HeavyVehicle, 1.2f)]
    [InlineData(TerrainUnitCategory.Harvester, 1.2f)]
    [InlineData(TerrainUnitCategory.Engineer, 1.2f)]
    [InlineData(TerrainUnitCategory.EngineerVehicle, 1.2f)]
    [InlineData(TerrainUnitCategory.Naval, 0.0f)]
    public void SpeedMod_Road_ReturnsCorrectValues(TerrainUnitCategory cat, float expected)
    {
        Assert.Equal(expected, TerrainModifiers.GetSpeedMod(TerrainType.Road, cat), 0.001f);
    }

    // ===== 速度修正：Sand =====

    [Theory]
    [InlineData(TerrainUnitCategory.Infantry, 0.8f)]
    [InlineData(TerrainUnitCategory.LightVehicle, 0.6f)]
    [InlineData(TerrainUnitCategory.HeavyVehicle, 0.4f)]
    [InlineData(TerrainUnitCategory.Harvester, 0.7f)]
    [InlineData(TerrainUnitCategory.Naval, 0.0f)]
    public void SpeedMod_Sand_ReturnsCorrectValues(TerrainUnitCategory cat, float expected)
    {
        Assert.Equal(expected, TerrainModifiers.GetSpeedMod(TerrainType.Sand, cat), 0.001f);
    }

    // ===== 速度修正：Grass =====

    [Fact]
    public void SpeedMod_Grass_Returns1()
    {
        Assert.Equal(1.0f, TerrainModifiers.GetSpeedMod(TerrainType.Grass, TerrainUnitCategory.Infantry), 0.001f);
        Assert.Equal(1.0f, TerrainModifiers.GetSpeedMod(TerrainType.Grass, TerrainUnitCategory.HeavyVehicle), 0.001f);
    }

    // ===== 速度修正：DeepWater =====

    [Fact]
    public void SpeedMod_DeepWater_Naval1_Others0()
    {
        Assert.Equal(1.0f, TerrainModifiers.GetSpeedMod(TerrainType.DeepWater, TerrainUnitCategory.Naval), 0.001f);
        Assert.Equal(0.0f, TerrainModifiers.GetSpeedMod(TerrainType.DeepWater, TerrainUnitCategory.Infantry), 0.001f);
        Assert.Equal(0.0f, TerrainModifiers.GetSpeedMod(TerrainType.DeepWater, TerrainUnitCategory.HeavyVehicle), 0.001f);
    }

    // ===== 速度修正：Cliff =====

    [Fact]
    public void SpeedMod_Cliff_Always0()
    {
        Assert.Equal(0.0f, TerrainModifiers.GetSpeedMod(TerrainType.Cliff, TerrainUnitCategory.Infantry), 0.001f);
        Assert.Equal(0.0f, TerrainModifiers.GetSpeedMod(TerrainType.Cliff, TerrainUnitCategory.LightVehicle), 0.001f);
    }

    // ===== 速度修正：Mountain =====

    [Fact]
    public void SpeedMod_Mountain_InfantryAndEngineerOnly()
    {
        Assert.Equal(0.3f, TerrainModifiers.GetSpeedMod(TerrainType.Mountain, TerrainUnitCategory.Infantry), 0.001f);
        Assert.Equal(0.3f, TerrainModifiers.GetSpeedMod(TerrainType.Mountain, TerrainUnitCategory.Engineer), 0.001f);
        Assert.Equal(0.0f, TerrainModifiers.GetSpeedMod(TerrainType.Mountain, TerrainUnitCategory.HeavyVehicle), 0.001f);
        Assert.Equal(0.0f, TerrainModifiers.GetSpeedMod(TerrainType.Mountain, TerrainUnitCategory.Harvester), 0.001f);
    }

    // ===== 速度修正：Bridge =====

    [Fact]
    public void SpeedMod_Bridge_HeavyVehicle9_Others1()
    {
        Assert.Equal(1.0f, TerrainModifiers.GetSpeedMod(TerrainType.Bridge, TerrainUnitCategory.Infantry), 0.001f);
        Assert.Equal(0.9f, TerrainModifiers.GetSpeedMod(TerrainType.Bridge, TerrainUnitCategory.HeavyVehicle), 0.001f);
        Assert.Equal(0.0f, TerrainModifiers.GetSpeedMod(TerrainType.Bridge, TerrainUnitCategory.Naval), 0.001f);
    }

    // ===== 缓坡修正 =====

    [Theory]
    [InlineData(TerrainUnitCategory.Infantry, 0.5f)]
    [InlineData(TerrainUnitCategory.LightVehicle, 0.3f)]
    [InlineData(TerrainUnitCategory.HeavyVehicle, 0.2f)]
    [InlineData(TerrainUnitCategory.Harvester, 0.3f)]
    [InlineData(TerrainUnitCategory.Engineer, 0.5f)]
    [InlineData(TerrainUnitCategory.EngineerVehicle, 0.3f)]
    public void SlopeMod_ReturnsCorrectValues(TerrainUnitCategory cat, float expected)
    {
        Assert.Equal(expected, TerrainModifiers.GetSlopeMod(cat), 0.001f);
    }

    // ===== TerrainGrid.GetSpeedModifier 委托验证 =====

    [Fact]
    public void TerrainGrid_GetSpeedModifier_DelegatesCorrectly()
    {
        // 验证TerrainGrid.GetSpeedModifier委托给TerrainModifiers
        float result = TerrainGrid.GetSpeedModifier(TerrainUnitCategory.Infantry, TerrainType.Road, 1, 1);
        Assert.Equal(1.2f, result, 0.001f);

        result = TerrainGrid.GetSpeedModifier(TerrainUnitCategory.HeavyVehicle, TerrainType.Sand, 1, 1);
        Assert.Equal(0.4f, result, 0.001f);

        // Air不受地形影响
        result = TerrainGrid.GetSpeedModifier(TerrainUnitCategory.Air, TerrainType.DeepWater, 1, 1);
        Assert.Equal(1.0f, result, 0.001f);

        // 悬崖（elevDiff>=2）
        result = TerrainGrid.GetSpeedModifier(TerrainUnitCategory.Infantry, TerrainType.Grass, 0, 2);
        Assert.Equal(0.0f, result, 0.001f);

        // 缓坡
        result = TerrainGrid.GetSpeedModifier(TerrainUnitCategory.Infantry, TerrainType.Grass, 1, 2);
        Assert.Equal(0.5f, result, 0.001f); // 1.0 * 0.5
    }
}
