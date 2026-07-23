using Godot;

namespace RTSGame;

/// <summary>
/// 等距(Isometric)坐标系转换工具。
/// 菱形瓦片：宽 TileWidth=64px × 高 TileHeight=32px（2:1比例）。
/// 网格坐标(gx,gy) → 等距屏幕坐标的转换公式：
///   screenX = (gx - gy) * (TileWidth / 2)
///   screenY = (gx + gy) * (TileHeight / 2)
/// 反向：屏幕坐标 → 网格坐标
///   gx = (screenX / (TileWidth/2) + screenY / (TileHeight/2)) / 2
///   gy = (screenY / (TileHeight/2) - screenX / (TileWidth/2)) / 2
/// </summary>
public static class IsoCoords
{
    /// <summary>菱形瓦片宽度（像素）。</summary>
    public const float TileWidth = 64f;
    /// <summary>菱形瓦片高度（像素）。</summary>
    public const float TileHeight = 32f;
    /// <summary>半宽。</summary>
    public const float HalfW = TileWidth / 2f;   // 32
    /// <summary>半高。</summary>
    public const float HalfH = TileHeight / 2f;  // 16

    /// <summary>网格坐标 → 等距屏幕坐标。</summary>
    public static Vector2 GridToScreen(int gx, int gy)
    {
        return new Vector2(
            (gx - gy) * HalfW,
            (gx + gy) * HalfH
        );
    }

    /// <summary>网格坐标(float) → 等距屏幕坐标（用于单位平滑移动）。</summary>
    public static Vector2 GridToScreenF(float gx, float gy)
    {
        return new Vector2(
            (gx - gy) * HalfW,
            (gx + gy) * HalfH
        );
    }

    /// <summary>等距屏幕坐标 → 网格坐标(浮点)。</summary>
    public static Vector2 ScreenToGridF(float screenX, float screenY)
    {
        float gx = (screenX / HalfW + screenY / HalfH) * 0.5f;
        float gy = (screenY / HalfH - screenX / HalfW) * 0.5f;
        return new Vector2(gx, gy);
    }

    /// <summary>等距屏幕坐标 → 网格坐标(整数)。</summary>
    public static (int gx, int gy) ScreenToGrid(float screenX, float screenY)
    {
        var f = ScreenToGridF(screenX, screenY);
        return ((int) Mathf.Floor(f.X), (int) Mathf.Floor(f.Y));
    }

    /// <summary>旧版正交世界坐标 → 等距屏幕坐标（用于渐进迁移）。
    /// 旧坐标中 worldX = gx * 64, worldY = gy * 64。
    /// 先转回网格坐标再转等距。</summary>
    public static Vector2 OldWorldToIso(float worldX, float worldY)
    {
        float gx = worldX / TerrainGrid.TileSize;
        float gy = worldY / TerrainGrid.TileSize;
        return GridToScreenF(gx, gy);
    }

    /// <summary>等距屏幕坐标 → 旧版正交世界坐标（用于渐进迁移）。</summary>
    public static Vector2 IsoToOldWorld(float screenX, float screenY)
    {
        var grid = ScreenToGridF(screenX, screenY);
        return new Vector2(grid.X * TerrainGrid.TileSize, grid.Y * TerrainGrid.TileSize);
    }

    /// <summary>获取等距视角下的深度排序值（Y轴越大越靠前）。
    /// 用于 Y-Sort：同一行的格子深度相同，行数越大画在越上面。</summary>
    public static float GetSortY(int gx, int gy)
    {
        return (gx + gy) * HalfH;
    }

    /// <summary>获取等距视角下的深度排序值（浮点版，考虑高度修正）。</summary>
    public static float GetSortYF(float gx, float gy, float elevationOffset = 0f)
    {
        return (gx + gy) * HalfH - elevationOffset;
    }

    /// <summary>获取等距视角中8个方向的枚举索引。
    /// 0=E, 1=SE, 2=S, 3=SW, 4=W, 5=NW, 6=N, 7=NE
    /// 基于移动方向向量在等距投影中的角度。</summary>
    public static int GetDirectionIndex(Vector2 moveDir)
    {
        if (moveDir.LengthSquared() < 0.001f) return 0;
        // 在等距视角中，屏幕上的方向需要转换回网格方向
        var gridDir = ScreenToGridF(moveDir.X, moveDir.Y);
        float angle = Mathf.Atan2(gridDir.Y, gridDir.X);
        // angle: 0=E, Pi/2=S, Pi=W, -Pi/2=N
        // 转为0-7的8方向索引
        float deg = Mathf.RadToDeg(angle);
        if (deg < 0) deg += 360f;
        // 每45度一个方向
        int idx = Mathf.RoundToInt(deg / 45f) % 8;
        return idx;
    }

    /// <summary>8方向名称（用于调试）。</summary>
    public static readonly string[] DirNames = { "E", "SE", "S", "SW", "W", "NW", "N", "NE" };

    /// <summary>等距菱形的4个顶点（相对于格子中心）。</summary>
    public static readonly Vector2[] DiamondVerts =
    {
        new(0, -HalfH),   // 上
        new(HalfW, 0),    // 右
        new(0, HalfH),    // 下
        new(-HalfW, 0),   // 左
    };
}
