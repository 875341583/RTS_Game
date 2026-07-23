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
                    DrawDiamondSide(img, cx, cy, sidePx, cell);
                }

                // 画顶面（菱形裁剪）
                DrawDiamondTop(img, cx, cy, topImg, cell);

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

    private static void DrawDiamondTop(Image img, int cx, int cy, Image tileImg, TerrainCell cell)
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

                // 水面半透明
                if (cell.Type == TerrainType.ShallowWater)
                    c = new Color(c.R, c.G, c.B, 0.85f);

                img.SetPixel(imgX, imgY, c);
            }
        }
    }

    private static void DrawDiamondSide(Image img, int cx, int cy, int sidePx, TerrainCell cell)
    {
        // 悬崖用棕灰色，普通高地用地形暗色
        Color sideColor = cell.Type switch
        {
            TerrainType.Mountain => new Color(0.38f, 0.32f, 0.24f, 1f),
            TerrainType.Snow => new Color(0.55f, 0.55f, 0.60f, 1f),
            TerrainType.Sand => new Color(0.45f, 0.38f, 0.25f, 1f),
            _ => new Color(0.32f, 0.28f, 0.20f, 1f),
        };

        // 画侧面：菱形下方延伸 sidePx 像素
        // 左下→右下两条边可见（等距视角下方两条边）
        for (int py = 0; py < sidePx; py++)
        {
            float t = (float)py / sidePx; // 0=顶部(接近顶面)，1=底部
            // 亮度渐变：顶部亮→底部暗
            float dim = 1f - t * 0.35f;
            Color c = new Color(sideColor.R * dim, sideColor.G * dim, sideColor.B * dim, 1f);

            // 当前行的宽度：从菱形底部宽度渐变到底部（宽度不变，因为是垂直侧面）
            // 菱形底部的左右点：(-HalfW, 0) 和 (HalfW, 0)，py=0时
            // 但侧面只显示下半部分（左下和右下边）
            // 左下边：从 (-HalfW, 0) 到 (0, HalfH)
            // 右下边：从 (HalfW, 0) 到 (0, HalfH)
            // 侧面区域的宽度等于菱形在该行y=HalfH处的宽度，但向四周收窄

            int y = cy + (int)IsoCoords.HalfH + py;

            // 左下边和右下边之间的区域
            for (int px = -(int)IsoCoords.HalfW; px <= (int)IsoCoords.HalfW; px++)
            {
                int imgX = cx + px;
                if (imgX < 0 || imgX >= img.GetWidth() || y < 0 || y >= img.GetHeight())
                    continue;

                // 检查这个点是否在菱形下半部分的外侧（即侧面区域）
                // 菱形在 y=HalfH 处宽度为0（底部尖端）
                // 侧面应该覆盖菱形下半部分投影下的区域
                float halfWAtY = IsoCoords.HalfW * (1f - (float)py / IsoCoords.HalfH);
                if (py < IsoCoords.HalfH)
                {
                    halfWAtY = IsoCoords.HalfW * ((float)py / IsoCoords.HalfH);
                }
                else
                {
                    halfWAtY = IsoCoords.HalfW;
                }

                // 简化：侧面直接画完整宽度
                // 层理线
                Color finalC = c;
                if (py % 4 == 0)
                    finalC = new Color(c.R * 0.88f, c.G * 0.88f, c.B * 0.88f, 1f);

                img.SetPixel(imgX, y, finalC);
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
