using Godot;

namespace RTSGame;

/// <summary>
/// P2-2: 地图配置 — 统一管理地图尺寸和布局参数。
/// 支持三种预设尺寸（32/64/96），所有组件通过 MapConfig 查询动态尺寸，
/// 替代编译期 const GridSize = 32。
///
/// 用法：
///   MapConfig.SetSize(64);           // 设置为64×64地图
///   int gs = MapConfig.GridSize;     // 查询当前尺寸
///   var bases = MapConfig.BasePositions; // 自动适配的基地位置
/// </summary>
public static class MapConfig
{
    /// <summary>地图尺寸预设。</summary>
    public enum SizePreset
    {
        Small = 32,     // 32×32 — 小型快速对战
        Medium = 64,    // 64×64 — 标准对战
        Large = 96,     // 96×96 — 大规模战役
    }

    private static int _gridSize = 32;
    private static SizePreset _preset = SizePreset.Small;

    /// <summary>当前地图网格边长（格数）。</summary>
    public static int GridSize => _gridSize;

    /// <summary>当前地图尺寸预设。</summary>
    public static SizePreset Preset => _preset;

    /// <summary>2D版每格像素大小（保持64不变）。</summary>
    public static int TileSize => 64;

    /// <summary>2D版地图像素大小 = GridSize * TileSize。</summary>
    public static float MapPixelSize => _gridSize * TileSize;

    /// <summary>3D版每格3D单位大小（米），保持4.0不变。</summary>
    public static float CellSize3D => 4.0f;

    /// <summary>3D版地图世界大小 = GridSize * CellSize3D。</summary>
    public static float MapWorldSize3D => _gridSize * CellSize3D;

    /// <summary>设置地图尺寸。</summary>
    public static void SetSize(SizePreset preset)
    {
        _preset = preset;
        _gridSize = (int)preset;
    }

    /// <summary>设置地图尺寸（自定义值）。</summary>
    public static void SetSize(int gridSize)
    {
        _gridSize = gridSize;
        _preset = gridSize switch
        {
            32 => SizePreset.Small,
            64 => SizePreset.Medium,
            96 => SizePreset.Large,
            _ => SizePreset.Small,
        };
    }

    // ===== 动态布局参数（根据GridSize自动计算）=====

    /// <summary>地图中心格子索引。</summary>
    public static int Center => _gridSize / 2;

    /// <summary>
    /// 8个阵营的基地起始位置（根据GridSize动态计算）。
    /// Small(32): 四角+四边中点
    /// Medium/Large: 按比例分布
    /// </summary>
    public static (int x, int y)[] BasePositions
    {
        get
        {
            int g = _gridSize;
            int edge = g - 5;  // 边缘偏移（留5格缓冲）
            int mid = g / 2;
            return new (int, int)[]
            {
                (0, 0), (edge, edge), (edge, 0), (0, edge),
                (mid, 0), (mid, edge), (0, mid), (edge, mid),
            };
        }
    }

    /// <summary>战略点位置（地图中央8×8区域）。</summary>
    public static (int x, int y, int w, int h) StrategicPointArea
    {
        get
        {
            int mid = _gridSize / 2;
            int half = 4;
            return (mid - half, mid - half, half * 2, half * 2);
        }
    }

    /// <summary>城市区域中心（用于地形生成）。</summary>
    public static (int x, int y) CityCenter => (_gridSize / 2 - 2, _gridSize / 2 - 2);

    /// <summary>道路中心线。</summary>
    public static int RoadCenter => _gridSize / 2;

    /// <summary>判断是否在基地区域（3格范围）。</summary>
    public static bool IsBaseArea(int x, int y)
    {
        foreach (var (bx, by) in BasePositions)
            if (System.Math.Abs(x - bx) <= 2 && System.Math.Abs(y - by) <= 2)
                return true;
        return false;
    }

    // ===== 地图主题 =====

    /// <summary>地图主题预设。影响地形生成中各类型地形的权重和面积。</summary>
    public enum MapTheme
    {
        Default,  // 默认：混合地形
        Snow,     // 雪地：大量雪山+冻原，少量水域
        Desert,   // 沙漠：大面积沙地+绿洲，少水域
        City,     // 城市：大量铺装路面+建筑废墟，少自然地形
        Island,   // 海岛：大量水域+岛屿，陆地面积小
    }

    /// <summary>P2-2: 当前地图主题（由 GameSession 在场景切换前设置）。</summary>
    public static MapTheme Theme { get; set; } = MapTheme.Default;
}
