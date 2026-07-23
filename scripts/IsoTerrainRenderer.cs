using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// 等距地形渲染器：将 TerrainGrid 数据渲染为等距菱形瓦片 + 高度侧面（路线C）。
/// 每个格子渲染3层：
/// 1. 顶面：等距菱形裁剪的地形贴图
/// 2. 侧面厚度：根据 Elevation 画对应像素高度的侧面
/// 3. 阴影渐变：侧面底部加深
/// 全部预渲染到一张大图上，运行时零开销（仅一个Sprite2D）。
/// </summary>
public static class IsoTerrainRenderer
{
    // 等距菱形瓦片像素尺寸
    public const int TileW = 64;   // 菱形宽
    public const int TileH = 32;   // 菱形高
    public const int MaxElevPx = 24; // 最高海拔的侧面像素高度

    // 每级海拔的侧面像素高度
    // Elevation 0（水面）= 0px, 1（平地）= 0px, 2（丘陵）= 12px, 3（山顶）= 24px
    private static readonly int[] ElevSidePx = { 0, 0, 12, 24 };

    /// <summary>
    /// 渲染整个地形为一张大等距图。
    /// 菱形地图的边界框：宽 = (GridSize + GridSize) * HalfW，高 = (GridSize + GridSize) * HalfH + MaxElevPx
    /// </summary>
    public static Image RenderTerrain(TerrainGrid terrain, Random rng)
    {
        int gs = TerrainGrid.GridSize;
        // 等距地图边界框
        // 最大X = (gs-1 - 0) * HalfW = (gs-1) * 32
        // 最小X = (0 - (gs-1)) * HalfW = -(gs-1) * 32
        // 宽度 = (2*(gs-1)+1) * HalfW ≈ 2*gs*HalfW
        // 最大Y = (gs-1 + gs-1) * HalfH + MaxElevPx = 2*(gs-1)*16 + 24
        // 高度 = (2*(gs-1)+1) * HalfH + MaxElevPx
        int imgW = (gs * 2 + 1) * (int)IsoCoords.HalfW;
        int imgH = (gs * 2 + 1) * (int)IsoCoords.HalfH + MaxElevPx + 4;
        // 偏移：让最小X对应到 imgX=0
        int offX = gs * (int)IsoCoords.HalfW;
        int offY = 0; // 顶部留 MaxElevPx 空间

        var img = Image.CreateEmpty(imgW, imgH, false, Image.Format.Rgba8);
        // 透明背景
        img.Fill(new Color(0, 0, 0, 0));

        // 确保地形贴图已加载
        EnsureTerrainTextures();

        // 预加载 tile Image
        var grassImgs = LoadImageArray(_grassTexs);
        var sandImgs = LoadImageArray(_sandTexs);
        var shallowImgs = LoadImageArray(_shallowTexs);
        var deepImgs = LoadImageArray(_deepTexs);
        var mountainImgs = LoadImageArray(_mountainTexs);
        var snowImgs = LoadImageArray(_snowTexs);
        var cityImgs = LoadImageArray(_cityTexs);
        var fieldImgs = LoadImageArray(_fieldTexs);

        // 按等距渲染顺序：从后往前（gx+gy越小越在后面）
        for (int sum = 0; sum <= 2 * (gs - 1); sum++)
        {
            for (int gx = Math.Max(0, sum - gs + 1); gx <= Math.Min(gs - 1, sum); gx++)
            {
                int gy = sum - gx;
                if (gy < 0 || gy >= gs) continue;

                var cell = terrain.GetCell(gx, gy);
                var screenPos = IsoCoords.GridToScreen(gx, gy);
                int cx = offX + (int)screenPos.X;
                int cy = offY + (int)screenPos.Y + MaxElevPx; // 顶部留空给最高海拔侧面

                // 获取顶面贴图
                Image topImg = GetTileImage(cell, terrain, gx, gy, rng, grassImgs, sandImgs,
                    shallowImgs, deepImgs, mountainImgs, snowImgs, cityImgs, fieldImgs);

                // 计算侧面高度
                int sidePx = cell.Elevation >= 0 && cell.Elevation < ElevSidePx.Length
                    ? ElevSidePx[cell.Elevation] : 0;

                // 先画侧面（在顶面下方）
                if (sidePx > 0)
                {
                    DrawDiamondSide(img, cx, cy, sidePx, cell, rng);
                }

                // 画顶面（菱形裁剪）
                DrawDiamondTop(img, cx, cy, topImg, cell, rng);

                // 画水面波纹（仅水面类型）
                if (cell.Type == TerrainType.ShallowWater || cell.Type == TerrainType.DeepWater)
                    DrawWaterRipples(img, cx, cy, cell, rng);

                // 画悬崖（高差≥2的边缘画深色陡崖）
                DrawCliffEdges(img, cx, cy, cell, terrain, gx, gy);
            }
        }

        return img;
    }

    /// <summary>获取渲染后地形图的偏移量（用于Sprite2D定位）。</summary>
    public static (int offX, int offY) GetRenderOffset()
    {
        int gs = TerrainGrid.GridSize;
        return (gs * (int)IsoCoords.HalfW, 0);
    }

    // ======== 内部渲染方法 ========

    private static void DrawDiamondTop(Image img, int cx, int cy, Image tileImg, TerrainCell cell, Random rng)
    {
        if (tileImg == null) return;

        // 高度亮度调整
        float brightness = cell.Elevation switch
        {
            2 => 1.08f,
            3 => 1.15f,
            _ => 1.0f,
        };

        // 菱形裁剪：遍历菱形范围内的像素
        for (int py = -(int)IsoCoords.HalfH; py <= (int)IsoCoords.HalfH; py++)
        {
            // 当前行的左右边界（菱形）
            float ratio = 1f - Math.Abs(py) / IsoCoords.HalfH;
            int halfW = (int)(IsoCoords.HalfW * ratio);

            for (int px = -halfW; px <= halfW; px++)
            {
                int imgX = cx + px;
                int imgY = cy + py;
                if (imgX < 0 || imgX >= img.GetWidth() || imgY < 0 || imgY >= img.GetHeight())
                    continue;

                // 从源图采样
                int srcX = (int)((px + IsoCoords.HalfW) / IsoCoords.TileWidth * tileImg.GetWidth());
                int srcY = (int)((py + IsoCoords.HalfH) / IsoCoords.TileHeight * tileImg.GetHeight());
                srcX = Math.Clamp(srcX, 0, tileImg.GetWidth() - 1);
                srcY = Math.Clamp(srcY, 0, tileImg.GetHeight() - 1);

                var c = tileImg.GetPixel(srcX, srcY);
                if (c.A < 0.01f) continue;

                // 亮度调整
                if (brightness != 1.0f)
                {
                    c = new Color(
                        Math.Min(c.R * brightness, 1f),
                        Math.Min(c.G * brightness, 1f),
                        Math.Min(c.B * brightness, 1f),
                        c.A
                    );
                }

                // 水面处理
                if (cell.Type == TerrainType.ShallowWater)
                    c = new Color(c.R * 0.85f, c.G * 0.9f, c.B * 1.0f, 0.88f);
                else if (cell.Type == TerrainType.DeepWater)
                    c = new Color(c.R * 0.7f, c.G * 0.75f, c.B * 0.95f, 0.92f);

                img.SetPixel(imgX, imgY, c);
            }
        }
    }

    private static void DrawDiamondSide(Image img, int cx, int cy, int sidePx, TerrainCell cell, Random rng)
    {
        if (sidePx <= 0) return;

        // 侧面颜色按地形类型
        Color baseColor = cell.Type switch
        {
            TerrainType.Mountain => new Color(0.42f, 0.35f, 0.26f, 1f),
            TerrainType.Snow => new Color(0.58f, 0.58f, 0.63f, 1f),
            TerrainType.Sand => new Color(0.50f, 0.42f, 0.28f, 1f),
            TerrainType.Grass => new Color(0.36f, 0.30f, 0.20f, 1f),
            _ => new Color(0.34f, 0.28f, 0.20f, 1f),
        };

        // 左面（南西）比右面（南东）暗一些，模拟光源来自右上方
        float leftShade = 0.75f;
        float rightShade = 1.0f;

        // 等距侧面正确形状：
        // 菱形下半部分的两条边（左下边+右下边）向下延伸 sidePx 像素
        // 左面平行四边形：顶点 (-HalfW,0)→(0,HalfH)→(0,HalfH+sidePx)→(-HalfW,sidePx)
        // 右面平行四边形：顶点 (HalfW,0)→(0,HalfH)→(0,HalfH+sidePx)→(HalfW,sidePx)
        // 可见区域：y 从 HalfH 到 HalfH+sidePx
        //   - 当 HalfH+py < sidePx 时：全宽（-HalfW 到 HalfW）
        //   - 当 HalfH+py >= sidePx 时：宽度逐渐收窄到底部尖角

        int halfW = (int)IsoCoords.HalfW;
        int halfH = (int)IsoCoords.HalfH;

        for (int py = 0; py < sidePx; py++)
        {
            int y = cy + halfH + py;
            if (y < 0 || y >= img.GetHeight()) continue;

            float t = (float)py / sidePx; // 0=顶部，1=底部
            float dim = 1f - t * 0.3f; // 亮度渐变

            // 计算当前行的左右边界
            int leftBound, rightBound;
            if (halfH + py < sidePx)
            {
                // 宽行：左/右墙壁竖直部分
                leftBound = -halfW;
                rightBound = halfW;
            }
            else
            {
                // 收窄行：沿底部V形边收窄
                float vt = (float)(halfH + py - sidePx) / halfH;
                leftBound = -(int)(halfW * (1f - vt));
                rightBound = (int)(halfW * (1f - vt));
            }

            for (int px = leftBound; px <= rightBound; px++)
            {
                int imgX = cx + px;
                if (imgX < 0 || imgX >= img.GetWidth()) continue;

                // 左面/右面着色（以x=0为分界线）
                float faceShade = px < 0 ? leftShade : rightShade;

                // 程序化噪声（基于位置的确定性噪声）
                float noise = ((px * 37 + py * 53 + cx * 7) % 23) / 23f * 0.15f - 0.075f;

                // 层理线（每4像素一条暗线）
                float layerLine = (py % 4 == 0) ? 0.88f : 1.0f;

                float r = Math.Clamp(baseColor.R * dim * faceShade * layerLine + noise, 0f, 1f);
                float g = Math.Clamp(baseColor.G * dim * faceShade * layerLine + noise, 0f, 1f);
                float b = Math.Clamp(baseColor.B * dim * faceShade * layerLine + noise, 0f, 1f);

                img.SetPixel(imgX, y, new Color(r, g, b, 1f));
            }
        }
    }

    private static void DrawCliffEdges(Image img, int cx, int cy, TerrainCell cell,
        TerrainGrid terrain, int gx, int gy)
    {
        // 悬崖：高差≥2的边界画深色陡崖效果
        if (cell.Elevation < 2) return;

        var neighbors = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        foreach (var (dx, dy) in neighbors)
        {
            int nx = gx + dx, ny = gy + dy;
            if (nx < 0 || nx >= TerrainGrid.GridSize || ny < 0 || ny >= TerrainGrid.GridSize)
                continue;
            var neighbor = terrain.GetCell(nx, ny);
            int elevDiff = cell.Elevation - neighbor.Elevation;
            if (elevDiff < 2) continue;

            // 在该方向边缘画深色悬崖线
            // 等距视角中，不同方向的边缘对应菱形的不同边
            // (0,-1)→左上边, (0,1)→右下边, (-1,0)→左下边, (1,0)→右上边
            DrawCliffLine(img, cx, cy, dx, dy);
        }
    }

    private static void DrawCliffLine(Image img, int cx, int cy, int dx, int dy)
    {
        Color cliffColor = new(0.15f, 0.12f, 0.08f, 0.9f);
        // 菱形4条边的方向（等距视角）：
        // 北→左上边: 从(0,-HalfH)到(-HalfW,0)
        // 东→右上边: 从(0,-HalfH)到(HalfW,0)
        // 南→右下边: 从(HalfW,0)到(0,HalfH)
        // 西→左下边: 从(-HalfW,0)到(0,HalfH)

        int sidePx = ElevSidePx[3]; // 用最大侧面高度

        for (int i = 0; i <= (int)IsoCoords.HalfW; i++)
        {
            float t = (float)i / IsoCoords.HalfW;
            int px, py;
            if (dx == 0 && dy == -1) // 北→左上边
            {
                px = -(int)(IsoCoords.HalfW * t);
                py = -(int)(IsoCoords.HalfH * (1f - t));
            }
            else if (dx == 1 && dy == 0) // 东→右上边
            {
                px = (int)(IsoCoords.HalfW * t);
                py = -(int)(IsoCoords.HalfH * (1f - t));
            }
            else if (dx == 0 && dy == 1) // 南→右下边
            {
                px = (int)(IsoCoords.HalfW * (1f - t));
                py = (int)(IsoCoords.HalfH * t);
            }
            else // dx==-1, dy==0 → 西→左下边
            {
                px = -(int)(IsoCoords.HalfW * (1f - t));
                py = (int)(IsoCoords.HalfH * t);
            }

            int imgX = cx + px;
            int imgY = cy + py;
            if (imgX < 0 || imgX >= img.GetWidth() || imgY < 0 || imgY >= img.GetHeight())
                continue;

            // 画悬崖线 + 向下延伸的深色
            img.SetPixel(imgX, imgY, cliffColor);
            for (int s = 1; s <= sidePx; s++)
            {
                int sy = imgY + s;
                if (sy >= img.GetHeight()) break;
                float fade = 1f - (float)s / sidePx * 0.3f;
                img.SetPixel(imgX, sy, new Color(cliffColor.R * fade, cliffColor.G * fade, cliffColor.B * fade, 0.8f));
            }
        }
    }

    // ======== 水面波纹 ========

    private static void DrawWaterRipples(Image img, int cx, int cy, TerrainCell cell, Random rng)
    {
        // 在水面菱形上画几条随机的波纹线
        Color rippleColor = cell.Type == TerrainType.DeepWater
            ? new Color(0.5f, 0.6f, 0.8f, 0.35f)
            : new Color(0.6f, 0.7f, 0.85f, 0.4f);

        int halfH = (int)IsoCoords.HalfH;
        int halfW = (int)IsoCoords.HalfW;

        // 2-3条波纹
        int rippleCount = 2 + rng.Next(2);
        for (int i = 0; i < rippleCount; i++)
        {
            int ry = rng.Next(-halfH + 2, halfH - 1);
            int rw = (int)(halfW * (1f - Math.Abs(ry) / (float)halfW)) - 2;
            if (rw <= 0) continue;
            int startX = rng.Next(-rw, rw - 3);
            int len = rng.Next(3, Math.Min(8, rw * 2));

            for (int dx = 0; dx < len && startX + dx < rw; dx++)
            {
                int px = startX + dx;
                int imgX = cx + px;
                int imgY = cy + ry;
                if (imgX >= 0 && imgX < img.GetWidth() && imgY >= 0 && imgY < img.GetHeight())
                {
                    var existing = img.GetPixel(imgX, imgY);
                    if (existing.A > 0.5f)
                        img.SetPixel(imgX, imgY, new Color(
                            Math.Min(existing.R + rippleColor.R * 0.3f, 1f),
                            Math.Min(existing.G + rippleColor.G * 0.3f, 1f),
                            Math.Min(existing.B + rippleColor.B * 0.3f, 1f),
                            existing.A));
                }
            }
        }
    }

    // ======== 贴图获取 ========

    private static Image GetTileImage(TerrainCell cell, TerrainGrid terrain, int gx, int gy,
        Random rng, Image[][] grass, Image[][] sand, Image[][] shallow, Image[][] deep,
        Image[][] mountain, Image[][] snow, Image[][] city, Image[][] field)
    {
        var effType = terrain.GetEffectiveType(gx, gy);
        return effType switch
        {
            TerrainType.Grass => grass[0][rng.Next(grass[0].Length)],
            TerrainType.Sand => sand[0][rng.Next(sand[0].Length)],
            TerrainType.ShallowWater => shallow[0][rng.Next(shallow[0].Length)],
            TerrainType.DeepWater => deep[0][rng.Next(deep[0].Length)],
            TerrainType.Mountain => mountain[0][rng.Next(mountain[0].Length)],
            TerrainType.Snow => snow[0][rng.Next(snow[0].Length)],
            TerrainType.City => city[0][rng.Next(city[0].Length)],
            TerrainType.Field => field[0][rng.Next(field[0].Length)],
            TerrainType.Road => GetRoadTile(terrain, gx, gy, rng),
            TerrainType.Bridge => _bridgeTex?.GetImage() ?? grass[0][0],
            TerrainType.Tunnel => _tunnelTex?.GetImage() ?? grass[0][0],
            TerrainType.Cliff => _cliffTex?.GetImage() ?? grass[0][0],
            _ => grass[0][0],
        };
    }

    private static Image GetRoadTile(TerrainGrid terrain, int gx, int gy, Random rng)
    {
        // 简单返回道路贴图（后续可根据邻接关系选择方向）
        bool north = gy > 0 && terrain.GetEffectiveType(gx, gy - 1) == TerrainType.Road;
        bool south = gy < TerrainGrid.GridSize - 1 && terrain.GetEffectiveType(gx, gy + 1) == TerrainType.Road;
        bool east = gx < TerrainGrid.GridSize - 1 && terrain.GetEffectiveType(gx + 1, gy) == TerrainType.Road;
        bool west = gx > 0 && terrain.GetEffectiveType(gx - 1, gy) == TerrainType.Road;

        if ((north || south) && (east || west))
            return _roadCrossTex?.GetImage() ?? _roadETex?.GetImage()!;
        if (north || south)
            return _roadNTex?.GetImage() ?? _roadETex?.GetImage()!;
        return _roadETex?.GetImage()!;
    }

    // ======== 贴图加载 ========

    private static Texture2D?[] _grassTexs = null!;
    private static Texture2D?[] _sandTexs = null!;
    private static Texture2D?[] _shallowTexs = null!;
    private static Texture2D?[] _deepTexs = null!;
    private static Texture2D?[] _mountainTexs = null!;
    private static Texture2D?[] _snowTexs = null!;
    private static Texture2D?[] _cityTexs = null!;
    private static Texture2D?[] _fieldTexs = null!;
    private static Texture2D? _roadETex, _roadNTex, _roadCrossTex;
    private static Texture2D? _bridgeTex, _tunnelTex, _cliffTex;
    private static bool _texturesLoaded = false;

    private static void EnsureTerrainTextures()
    {
        if (_texturesLoaded) return;
        _grassTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileGrass1.png",
            "res://assets/sprites/terrain/tileGrass2.png",
            "res://assets/sprites/terrain/tileGrass3.png",
            "res://assets/sprites/terrain/tileGrass4.png" });
        _sandTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileSand1.png",
            "res://assets/sprites/terrain/tileSand2.png",
            "res://assets/sprites/terrain/tileSand3.png" });
        _shallowTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileShallow1.png",
            "res://assets/sprites/terrain/tileShallow2.png",
            "res://assets/sprites/terrain/tileShallow3.png" });
        _deepTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileDeep1.png",
            "res://assets/sprites/terrain/tileDeep2.png",
            "res://assets/sprites/terrain/tileDeep3.png" });
        _mountainTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileMountain1.png",
            "res://assets/sprites/terrain/tileMountain2.png",
            "res://assets/sprites/terrain/tileMountain3.png" });
        _snowTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileSnow1.png",
            "res://assets/sprites/terrain/tileSnow2.png",
            "res://assets/sprites/terrain/tileSnow3.png" });
        _cityTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileCity1.png",
            "res://assets/sprites/terrain/tileCity2.png" });
        _fieldTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileField1.png",
            "res://assets/sprites/terrain/tileField2.png" });
        _roadETex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass_roadEast.png");
        _roadNTex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass_roadNorth.png");
        _roadCrossTex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileGrass_roadCrossing.png");
        _bridgeTex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileBridge.png");
        _tunnelTex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileTunnel.png");
        _cliffTex = GD.Load<Texture2D>("res://assets/sprites/terrain/tileCliff.png");
        _texturesLoaded = true;
    }

    private static Texture2D?[] LoadTexArray(string[] paths)
    {
        var arr = new Texture2D?[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            arr[i] = GD.Load<Texture2D>(paths[i]);
            if (arr[i] == null)
                GD.PrintErr($"[IsoTerrain] Failed to load: {paths[i]}");
        }
        return arr;
    }

    private static Image[][] LoadImageArray(Texture2D?[] texs)
    {
        var arr = new Image[1][];
        arr[0] = new Image[texs.Length];
        for (int i = 0; i < texs.Length; i++)
        {
            arr[0][i] = texs[i]?.GetImage() ?? Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        }
        return arr;
    }
}
