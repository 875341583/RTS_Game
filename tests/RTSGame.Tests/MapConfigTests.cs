using RTSGame;
using Xunit;

namespace RTSGame.Tests;

/// <summary>
/// P2-2: MapConfig 单元测试 — 验证动态地图尺寸和布局参数。
/// </summary>
public class MapConfigTests
{
    [Fact]
    public void DefaultSize_Is32()
    {
        // 默认为Small(32)
        MapConfig.SetSize(MapConfig.SizePreset.Small);
        Assert.Equal(32, MapConfig.GridSize);
    }

    [Fact]
    public void SetSize_Medium_Returns64()
    {
        MapConfig.SetSize(MapConfig.SizePreset.Medium);
        Assert.Equal(64, MapConfig.GridSize);
    }

    [Fact]
    public void SetSize_Large_Returns96()
    {
        MapConfig.SetSize(MapConfig.SizePreset.Large);
        Assert.Equal(96, MapConfig.GridSize);
    }

    [Fact]
    public void SetSize_Int_ReturnsCustomValue()
    {
        MapConfig.SetSize(48);
        Assert.Equal(48, MapConfig.GridSize);
    }

    [Fact]
    public void BasePositions_Small_8Positions()
    {
        MapConfig.SetSize(MapConfig.SizePreset.Small);
        var bases = MapConfig.BasePositions;
        Assert.Equal(8, bases.Length);
        // 四角
        Assert.Equal((0, 0), bases[0]);
        Assert.Equal((27, 27), bases[1]);
    }

    [Fact]
    public void BasePositions_Medium_ScalesUp()
    {
        MapConfig.SetSize(MapConfig.SizePreset.Medium);
        var bases = MapConfig.BasePositions;
        Assert.Equal(8, bases.Length);
        // Medium: edge = 64-5 = 59
        Assert.Equal((0, 0), bases[0]);
        Assert.Equal((59, 59), bases[1]);
    }

    [Fact]
    public void IsBaseArea_Small_CornerBase()
    {
        MapConfig.SetSize(MapConfig.SizePreset.Small);
        Assert.True(MapConfig.IsBaseArea(0, 0));
        Assert.True(MapConfig.IsBaseArea(1, 1));
        Assert.False(MapConfig.IsBaseArea(5, 5));
    }

    [Fact]
    public void Center_Small_Is16()
    {
        MapConfig.SetSize(MapConfig.SizePreset.Small);
        Assert.Equal(16, MapConfig.Center);
    }

    [Fact]
    public void Center_Medium_Is32()
    {
        MapConfig.SetSize(MapConfig.SizePreset.Medium);
        Assert.Equal(32, MapConfig.Center);
    }

    [Fact]
    public void MapPixelSize_Small_2048()
    {
        MapConfig.SetSize(MapConfig.SizePreset.Small);
        Assert.Equal(2048f, MapConfig.MapPixelSize);
    }

    [Fact]
    public void MapPixelSize_Medium_4096()
    {
        MapConfig.SetSize(MapConfig.SizePreset.Medium);
        Assert.Equal(4096f, MapConfig.MapPixelSize);
    }

    [Fact]
    public void MapWorldSize3D_Small_128()
    {
        MapConfig.SetSize(MapConfig.SizePreset.Small);
        Assert.Equal(128f, MapConfig.MapWorldSize3D);
    }

    [Fact]
    public void MapWorldSize3D_Medium_256()
    {
        MapConfig.SetSize(MapConfig.SizePreset.Medium);
        Assert.Equal(256f, MapConfig.MapWorldSize3D);
    }
}
