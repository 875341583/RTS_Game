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
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        int gs = TerrainGrid.GridSize;
        // 等距地图边界框
        int imgW = (gs * 2 + 1) * (int)IsoCoords.HalfW;
        int imgH = (gs * 2 + 1) * (int)IsoCoords.HalfH + MaxElevPx + 4;
        // 偏移：让最小X对应到 imgX=0
        int offX = gs * (int)IsoCoords.HalfW;
        int offY = 0; // 顶部留 MaxElevPx 空间

        var img = Image.CreateEmpty(imgW, imgH, false, Image.Format.Rgba8);
        // 透明背景
        img.Fill(new Color(0, 0, 0, 0));

        // P2-11优化：提取原始字节缓冲区，避免逐像素GetPixel/SetPixel调用开销
        byte[] imgData = img.GetData();
        int imgStride = imgW * 4; // 每行字节数（Rgba8 = 4字节/像素）

        // 确保地形贴图已加载
        EnsureTerrainTextures();

        // 预加载 tile Image
        var grassImgs = LoadImageArray(_grassTexs!);
        var sandImgs = LoadImageArray(_sandTexs!);
        var shallowImgs = LoadImageArray(_shallowTexs!);
        var deepImgs = LoadImageArray(_deepTexs!);
        var mountainImgs = LoadImageArray(_mountainTexs!);
        var snowImgs = LoadImageArray(_snowTexs!);
        var cityImgs = LoadImageArray(_cityTexs!);
        var fieldImgs = LoadImageArray(_fieldTexs!);
        var coastImgs = LoadImageArray(_coastTexs!);
        var sandCoastImgs = LoadImageArray(_sandCoastTexs!);
        var waterDepthImgs = LoadImageArray(_waterDepthTexs!);

        // 预提取tile贴图的字节缓冲区（避免在循环内重复GetPixel）
        var tileBuffers = new Dictionary<Image, (byte[] data, int w, int h)>();
        System.Func<Image, (byte[], int, int)> getTileBuf = (tileImg) =>
        {
            if (tileImg == null) return (Array.Empty<byte>(), 0, 0);
            if (!tileBuffers.TryGetValue(tileImg, out var buf))
            {
                buf = (tileImg.GetData(), tileImg.GetWidth(), tileImg.GetHeight());
                tileBuffers[tileImg] = buf;
            }
            return buf;
        };

        int halfW = (int)IsoCoords.HalfW;
        int halfH = (int)IsoCoords.HalfH;
        int tileW = (int)IsoCoords.TileWidth;
        int tileH = (int)IsoCoords.TileHeight;

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
                int cy = offY + (int)screenPos.Y + MaxElevPx;

                // 获取顶面贴图（邻接感知选择）
                var (topImg, isTransition) = GetTileWithAdjacency(cell, terrain, gx, gy, rng,
                    grassImgs, sandImgs, shallowImgs, deepImgs, mountainImgs, snowImgs,
                    cityImgs, fieldImgs, coastImgs, sandCoastImgs, waterDepthImgs);
                var (tileData, tileImgW, tileImgH) = getTileBuf(topImg);

                // 计算侧面高度
                int sidePx = cell.Elevation >= 0 && cell.Elevation < ElevSidePx.Length
                    ? ElevSidePx[cell.Elevation] : 0;

                // 先画侧面（在顶面下方）
                if (sidePx > 0)
                    DrawDiamondSideFast(imgData, imgStride, imgW, imgH, cx, cy, sidePx, cell, rng);

                // 画顶面（菱形裁剪）—— 快速字节版
                if (tileData.Length > 0)
                    DrawDiamondTopFast(imgData, imgStride, imgW, imgH, cx, cy,
                        tileData, tileImgW, tileImgH, cell, halfW, halfH, tileW, tileH, isTransition);

                // 画水面波纹（仅水面类型）
                if (cell.Type == TerrainType.ShallowWater || cell.Type == TerrainType.DeepWater)
                    DrawWaterRipplesFast(imgData, imgStride, imgW, imgH, cx, cy, cell, rng, halfW, halfH);

                // 画悬崖（高差≥2的边缘画深色陡崖）— 字节缓冲区版
                DrawCliffEdgesFast(imgData, imgStride, imgW, imgH, cx, cy, cell, terrain, gx, gy);
            }
        }

        long tilesMs = swTotal.ElapsedMilliseconds;
        GameLog.Info($"[IsoTerrain] Tiles render: {tilesMs}ms");

        // P1: 渲染Overlay装饰物到字节缓冲区（在SetData之前，避免二次GetPixel/SetPixel开销）
        var swOverlay = System.Diagnostics.Stopwatch.StartNew();
        RenderOverlays(imgData, imgStride, imgW, imgH, terrain, rng, offX, offY);
        swOverlay.Stop();
        GameLog.Info($"[IsoTerrain] Overlay render: {swOverlay.ElapsedMilliseconds}ms");

        // 将修改后的字节数组写回Image
        img.SetData(imgW, imgH, false, Image.Format.Rgba8, imgData);

        // 导出完整地形预览图（无UI/迷雾遮挡，用于评估地形质量）
        try
        {
            var previewDir = @"C:\Users\Administrator\AppData\Roaming\Godot\app_userdata\RTS_Game";
            var previewPath = System.IO.Path.Combine(previewDir, "terrain_full_preview.png");
            var pngData = img.SavePngToBuffer();
            System.IO.File.WriteAllBytes(previewPath, pngData);
            GameLog.Info($"[IsoTerrain] Terrain preview saved: {previewPath} ({imgW}x{imgH}, {pngData.Length} bytes)");
        }
        catch (Exception ex)
        {
            GameLog.Warning($"[IsoTerrain] Failed to save terrain preview: {ex.Message}");
        }

        swTotal.Stop();
        GameLog.Info($"[IsoTerrain] Total render: {swTotal.ElapsedMilliseconds}ms, image: {imgW}x{imgH}");

        return img;
    }

    /// <summary>获取渲染后地形图的偏移量（用于Sprite2D定位）。</summary>
    public static (int offX, int offY) GetRenderOffset()
    {
        int gs = TerrainGrid.GridSize;
        return (gs * (int)IsoCoords.HalfW, 0);
    }

    // ======== P2-11优化：快速字节缓冲区绘制方法 ========

    // ======== P1: Overlay装饰物渲染 ========

    /// <summary>在地形图上渲染装饰物（树木、岩石、灌木）。种子驱动确保确定性。字节缓冲区版。</summary>
    private static void RenderOverlays(byte[] imgData, int imgStride, int imgW, int imgH,
        TerrainGrid terrain, Random rng, int offX, int offY)
    {
        if (_treeTexs == null || _rockTexs == null || _bushTexs == null) return;

        int gs = TerrainGrid.GridSize;
        int halfH = (int)IsoCoords.HalfH;

        // 预加载overlay Image并提取字节缓冲区
        var treeBufs = LoadOverlayByteBuffers(_treeTexs);
        var rockBufs = LoadOverlayByteBuffers(_rockTexs);
        var bushBufs = LoadOverlayByteBuffers(_bushTexs);
        if (treeBufs.Length == 0 && rockBufs.Length == 0 && bushBufs.Length == 0) return;

        // Phase 1: 生成森林聚集点（种子驱动，成片树林而非均匀散布）
        // 增大半径和密度，确保形成大面积成片森林
        var forestClusters = new System.Collections.Generic.List<(int gx, int gy, int radius, float density)>();
        int numForests = Math.Max(10, gs / 4); // 64格地图约16片树林
        for (int f = 0; f < numForests; f++)
        {
            int fx = rng.Next(2, gs - 2);
            int fy = rng.Next(2, gs - 2);
            int radius = 4 + rng.Next(5); // 4-8格半径（大片树林）
            // 密度提高：中心更高
            float density = 0.65f + (float)rng.NextDouble() * 0.25f; // 0.65-0.90
            forestClusters.Add((fx, fy, radius, density));
        }

        // 判断某格子是否在森林范围内，返回最近的森林信息
        (bool inForest, float density, float edgeDist) ClassifyForest(int gx, int gy)
        {
            float bestDensity = 0;
            float bestEdgeDist = float.MaxValue;
            foreach (var (fx, fy, fr, fd) in forestClusters)
            {
                float dx = gx - fx, dy = gy - fy;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dist <= fr)
                {
                    // 距离边缘越近，密度越低（自然渐变）
                    float edgeFactor = 1.0f - (dist / fr); // 0=边缘 1=中心
                    float effectiveDensity = fd * (0.3f + 0.7f * edgeFactor);
                    if (effectiveDensity > bestDensity)
                    {
                        bestDensity = effectiveDensity;
                        bestEdgeDist = fr - dist;
                    }
                }
            }
            return (bestDensity > 0, bestDensity, bestEdgeDist);
        }

        for (int gx = 0; gx < gs; gx++)
        {
            for (int gy = 0; gy < gs; gy++)
            {
                var cell = terrain.GetCell(gx, gy);
                // 不在水面、悬崖、桥梁、隧道上放置装饰物
                if (cell.Type == TerrainType.ShallowWater || cell.Type == TerrainType.DeepWater ||
                    cell.Type == TerrainType.Cliff || cell.Type == TerrainType.Bridge ||
                    cell.Type == TerrainType.Tunnel || cell.Type == TerrainType.Road)
                    continue;

                // 基于网格坐标的确定性随机
                int cellSeed = (gx * 73856093) ^ (gy * 19349663);
                var cellRng = new Random(cellSeed);

                var (inForest, forestDensity, edgeDist) = ClassifyForest(gx, gy);

                // 山脉地形放岩石（密度提高）
                if (cell.Type == TerrainType.Mountain)
                {
                    if (cellRng.NextDouble() < 0.50 && rockBufs.Length > 0)
                    {
                        var rb = rockBufs[4 + cellRng.Next(Math.Min(4, rockBufs.Length - 4))]; // 大岩石
                        var screenPos = IsoCoords.GridToScreen(gx, gy);
                        int ox = offX + (int)screenPos.X - rb.w / 2 + cellRng.Next(-6, 7);
                        int oy = offY + (int)screenPos.Y - rb.h + halfH + cellRng.Next(-2, 3);
                        BlitOverlayFast(imgData, imgStride, imgW, imgH, rb, ox, oy);
                    }
                    // 山顶偶尔放小树
                    if (cellRng.NextDouble() < 0.10 && treeBufs.Length > 0)
                    {
                        var tb = treeBufs[cellRng.Next(4)]; // 松树
                        var screenPos = IsoCoords.GridToScreen(gx, gy);
                        int ox = offX + (int)screenPos.X - tb.w / 2 + cellRng.Next(-8, 9);
                        int oy = offY + (int)screenPos.Y - tb.h + halfH + cellRng.Next(-4, 5);
                        BlitOverlayFast(imgData, imgStride, imgW, imgH, tb, ox, oy);
                    }
                    continue;
                }

                // 草地/田地放树木和灌木
                if (cell.Type == TerrainType.Grass || cell.Type == TerrainType.Field)
                {
                    double roll = cellRng.NextDouble();
                    if (inForest)
                    {
                        // 森林范围内：按密度概率放树（中心密、边缘疏）
                        // 每格可放1-3棵树（森林中心密度高时）
                        if (roll < forestDensity && treeBufs.Length > 0)
                        {
                            // 森林中心放2-3棵树，边缘放1棵
                            int treeCount = forestDensity > 0.5f ? (1 + cellRng.Next(3)) : 1;
                            for (int ti = 0; ti < treeCount; ti++)
                            {
                                var tb = treeBufs[cellRng.Next(8)]; // pine + oak (0-7)
                                var screenPos = IsoCoords.GridToScreen(gx, gy);
                                int ox = offX + (int)screenPos.X - tb.w / 2 + cellRng.Next(-14, 15);
                                int oy = offY + (int)screenPos.Y - tb.h + halfH + cellRng.Next(-8, 9);
                                BlitOverlayFast(imgData, imgStride, imgW, imgH, tb, ox, oy);
                            }
                        }
                        else if (roll < forestDensity + 0.15 && bushBufs.Length > 0)
                        {
                            var bb = bushBufs[cellRng.Next(bushBufs.Length)];
                            var screenPos = IsoCoords.GridToScreen(gx, gy);
                            int ox = offX + (int)screenPos.X - bb.w / 2 + cellRng.Next(-8, 9);
                            int oy = offY + (int)screenPos.Y - bb.h + halfH + cellRng.Next(-3, 4);
                            BlitOverlayFast(imgData, imgStride, imgW, imgH, bb, ox, oy);
                        }
                    }
                    else
                    {
                        // 非森林区域：散布树木(10%) + 灌木(15%) + 岩石(8%)
                        if (roll < 0.10 && treeBufs.Length > 0)
                        {
                            var tb = treeBufs[cellRng.Next(8)]; // pine + oak
                            var screenPos = IsoCoords.GridToScreen(gx, gy);
                            int ox = offX + (int)screenPos.X - tb.w / 2 + cellRng.Next(-10, 11);
                            int oy = offY + (int)screenPos.Y - tb.h + halfH + cellRng.Next(-6, 7);
                            BlitOverlayFast(imgData, imgStride, imgW, imgH, tb, ox, oy);
                        }
                        else if (roll < 0.25 && bushBufs.Length > 0)
                        {
                            var bb = bushBufs[cellRng.Next(bushBufs.Length)];
                            var screenPos = IsoCoords.GridToScreen(gx, gy);
                            int ox = offX + (int)screenPos.X - bb.w / 2 + cellRng.Next(-8, 9);
                            int oy = offY + (int)screenPos.Y - bb.h + halfH + cellRng.Next(-3, 4);
                            BlitOverlayFast(imgData, imgStride, imgW, imgH, bb, ox, oy);
                        }
                        else if (roll < 0.33 && rockBufs.Length > 0)
                        {
                            int rockMax = Math.Min(4, rockBufs.Length);
                            var rb = rockBufs[cellRng.Next(rockMax)];
                            var screenPos = IsoCoords.GridToScreen(gx, gy);
                            int ox = offX + (int)screenPos.X - rb.w / 2 + cellRng.Next(-10, 11);
                            int oy = offY + (int)screenPos.Y - rb.h + halfH + cellRng.Next(-6, 7);
                            BlitOverlayFast(imgData, imgStride, imgW, imgH, rb, ox, oy);
                        }
                    }
                }

                // 沙地放岩石和枯树
                if (cell.Type == TerrainType.Sand)
                {
                    double roll = cellRng.NextDouble();
                    if (roll < 0.15 && rockBufs.Length > 0)
                    {
                        var rb = rockBufs[cellRng.Next(rockBufs.Length)];
                        var screenPos = IsoCoords.GridToScreen(gx, gy);
                        int ox = offX + (int)screenPos.X - rb.w / 2 + cellRng.Next(-10, 11);
                        int oy = offY + (int)screenPos.Y - rb.h + halfH + cellRng.Next(-6, 7);
                        BlitOverlayFast(imgData, imgStride, imgW, imgH, rb, ox, oy);
                    }
                    else if (roll < 0.22 && treeBufs.Length > 8)
                    {
                        // 枯树（索引8-11为dead树）
                        var tb = treeBufs[8 + cellRng.Next(Math.Min(4, treeBufs.Length - 8))];
                        var screenPos = IsoCoords.GridToScreen(gx, gy);
                        int ox = offX + (int)screenPos.X - tb.w / 2 + cellRng.Next(-10, 11);
                        int oy = offY + (int)screenPos.Y - tb.h + halfH + cellRng.Next(-6, 7);
                        BlitOverlayFast(imgData, imgStride, imgW, imgH, tb, ox, oy);
                    }
                }

                // 雪地放岩石和雪松
                if (cell.Type == TerrainType.Snow)
                {
                    double roll = cellRng.NextDouble();
                    if (roll < 0.15 && rockBufs.Length > 0)
                    {
                        var rb = rockBufs[cellRng.Next(rockBufs.Length)];
                        var screenPos = IsoCoords.GridToScreen(gx, gy);
                        int ox = offX + (int)screenPos.X - rb.w / 2 + cellRng.Next(-10, 11);
                        int oy = offY + (int)screenPos.Y - rb.h + halfH + cellRng.Next(-6, 7);
                        BlitOverlayFast(imgData, imgStride, imgW, imgH, rb, ox, oy);
                    }
                    else if (roll < 0.25 && treeBufs.Length > 0)
                    {
                        // 雪地松树
                        var tb = treeBufs[cellRng.Next(4)]; // pine
                        var screenPos = IsoCoords.GridToScreen(gx, gy);
                        int ox = offX + (int)screenPos.X - tb.w / 2 + cellRng.Next(-10, 11);
                        int oy = offY + (int)screenPos.Y - tb.h + halfH + cellRng.Next(-6, 7);
                        BlitOverlayFast(imgData, imgStride, imgW, imgH, tb, ox, oy);
                    }
                }
            }
        }
    }

    /// <summary>加载overlay纹理为字节缓冲区数组（性能优化：避免逐像素GetPixel）。</summary>
    private static (byte[] data, int w, int h)[] LoadOverlayByteBuffers(Texture2D?[] texs)
    {
        var list = new System.Collections.Generic.List<(byte[], int, int)>();
        foreach (var tex in texs)
        {
            if (tex != null)
            {
                var img = tex.GetImage();
                if (img != null)
                {
                    list.Add((img.GetData(), img.GetWidth(), img.GetHeight()));
                }
            }
        }
        return list.ToArray();
    }

    /// <summary>将overlay精灵alpha混合到目标字节缓冲区上（快速版，避免GetPixel/SetPixel）。</summary>
    private static void BlitOverlayFast(byte[] imgData, int imgStride, int imgW, int imgH,
        (byte[] data, int w, int h) src, int ox, int oy)
    {
        for (int sy = 0; sy < src.h; sy++)
        {
            int dy = oy + sy;
            if (dy < 0 || dy >= imgH) continue;
            int dstRow = dy * imgStride;
            int srcRow = sy * src.w * 4;
            for (int sx = 0; sx < src.w; sx++)
            {
                int dx = ox + sx;
                if (dx < 0 || dx >= imgW) continue;

                int srcIdx = srcRow + sx * 4;
                byte srcA = src.data[srcIdx + 3];
                if (srcA < 3) continue; // 近乎透明，跳过

                int dstIdx = dstRow + dx * 4;

                if (srcA >= 252)
                {
                    // 完全不透明，直接拷贝
                    imgData[dstIdx]     = src.data[srcIdx];
                    imgData[dstIdx + 1] = src.data[srcIdx + 1];
                    imgData[dstIdx + 2] = src.data[srcIdx + 2];
                    imgData[dstIdx + 3] = 255;
                }
                else
                {
                    // Alpha混合
                    float alpha = srcA / 255f;
                    float invA = 1f - alpha;
                    imgData[dstIdx]     = (byte)(imgData[dstIdx]     * invA + src.data[srcIdx]     * alpha);
                    imgData[dstIdx + 1] = (byte)(imgData[dstIdx + 1] * invA + src.data[srcIdx + 1] * alpha);
                    imgData[dstIdx + 2] = (byte)(imgData[dstIdx + 2] * invA + src.data[srcIdx + 2] * alpha);
                    imgData[dstIdx + 3] = 255;
                }
            }
        }
    }

    // 保留旧版Image方法供MapEditor等引用
    private static void RenderOverlays(Image img, TerrainGrid terrain, Random rng, int offX, int offY)
    {
        // 委托到字节缓冲区版：提取Image数据后调用
        var imgData = img.GetData();
        int imgStride = img.GetWidth() * 4;
        RenderOverlays(imgData, imgStride, img.GetWidth(), img.GetHeight(), terrain, rng, offX, offY);
        img.SetData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, imgData);
    }

    // ======== P2-11优化：快速字节缓冲区绘制方法原 ========

    /// <summary>直接操作字节数组绘制菱形顶面，避免逐像素GetPixel/SetPixel开销。</summary>
    private static void DrawDiamondTopFast(byte[] imgData, int imgStride, int imgW, int imgH,
        int cx, int cy, byte[] tileData, int tileW, int tileH,
        TerrainCell cell, int halfW, int halfH, int tileSW, int tileSH, bool skipColorAdjust = false)
    {
        float brightness = skipColorAdjust ? 1.0f : cell.Elevation switch
        {
            2 => 1.08f,
            3 => 1.15f,
            _ => 1.0f,
        };
        bool isShallow = !skipColorAdjust && cell.Type == TerrainType.ShallowWater;
        bool isDeep = !skipColorAdjust && cell.Type == TerrainType.DeepWater;

        for (int py = -halfH; py <= halfH; py++)
        {
            float ratio = 1f - Math.Abs(py) / (float)halfH;
            int rowHalfW = (int)(halfW * ratio);

            for (int px = -rowHalfW; px <= rowHalfW; px++)
            {
                int imgX = cx + px;
                int imgY = cy + py;
                if (imgX < 0 || imgX >= imgW || imgY < 0 || imgY >= imgH) continue;

                // 从源图采样（直接读字节）
                int srcX = (int)((px + halfW) / (float)tileSW * tileW);
                int srcY = (int)((py + halfH) / (float)tileSH * tileH);
                srcX = Math.Clamp(srcX, 0, tileW - 1);
                srcY = Math.Clamp(srcY, 0, tileH - 1);
                int srcIdx = (srcY * tileW + srcX) * 4;

                byte a = tileData[srcIdx + 3];
                if (a < 3) continue; // alpha < ~0.01f

                float r = tileData[srcIdx] / 255f;
                float g = tileData[srcIdx + 1] / 255f;
                float b = tileData[srcIdx + 2] / 255f;

                // 亮度调整
                if (brightness != 1.0f)
                {
                    r = Math.Min(r * brightness, 1f);
                    g = Math.Min(g * brightness, 1f);
                    b = Math.Min(b * brightness, 1f);
                }

                // 水面处理
                if (isShallow)
                {
                    r *= 0.85f; g *= 0.9f; a = 224;
                }
                else if (isDeep)
                {
                    r *= 0.7f; g *= 0.75f; b *= 0.95f; a = 235;
                }

                int dstIdx = imgY * imgStride + imgX * 4;
                imgData[dstIdx]     = (byte)Math.Clamp(r * 255f, 0, 255);
                imgData[dstIdx + 1] = (byte)Math.Clamp(g * 255f, 0, 255);
                imgData[dstIdx + 2] = (byte)Math.Clamp(b * 255f, 0, 255);
                imgData[dstIdx + 3] = a;
            }
        }
    }

    /// <summary>直接操作字节数组绘制菱形侧面。</summary>
    private static void DrawDiamondSideFast(byte[] imgData, int imgStride, int imgW, int imgH,
        int cx, int cy, int sidePx, TerrainCell cell, Random rng)
    {
        // P1: 尝试使用崖壁纹理贴图
        string cliffType = cell.Type switch
        {
            TerrainType.Mountain => "mountain",
            TerrainType.Snow => "snow",
            TerrainType.Sand => "sand",
            TerrainType.Grass or TerrainType.Field => "grass",
            _ => "rock",
        };
        byte[]? cliffTexData = null;
        int cliffTexW = 0, cliffTexH = 0;
        if (_cliffSideTexs != null && _cliffSideTexs.TryGetValue(cliffType, out var cliffArr))
        {
            var tex = cliffArr[rng.Next(cliffArr.Length)];
            if (tex != null)
            {
                var cImg = tex.GetImage();
                cliffTexData = cImg.GetData();
                cliffTexW = cImg.GetWidth();
                cliffTexH = cImg.GetHeight();
            }
        }

        Color baseColor = cell.Type switch
        {
            TerrainType.Mountain => new Color(0.42f, 0.35f, 0.26f, 1f),
            TerrainType.Snow => new Color(0.58f, 0.58f, 0.63f, 1f),
            TerrainType.Sand => new Color(0.50f, 0.42f, 0.28f, 1f),
            TerrainType.Grass => new Color(0.36f, 0.30f, 0.20f, 1f),
            _ => new Color(0.34f, 0.28f, 0.20f, 1f),
        };

        float leftShade = 0.75f;
        float rightShade = 1.0f;
        int halfW = (int)IsoCoords.HalfW;
        int halfH = (int)IsoCoords.HalfH;

        for (int py = 0; py < sidePx; py++)
        {
            int y = cy + halfH + py;
            if (y < 0 || y >= imgH) continue;

            float t = (float)py / sidePx;
            float dim = 1f - t * 0.3f;

            int leftBound, rightBound;
            if (halfH + py < sidePx)
            {
                leftBound = -halfW;
                rightBound = halfW;
            }
            else
            {
                float vt = (float)(halfH + py - sidePx) / halfH;
                leftBound = -(int)(halfW * (1f - vt));
                rightBound = (int)(halfW * (1f - vt));
            }

            int rowOffset = y * imgStride;
            for (int px = leftBound; px <= rightBound; px++)
            {
                int imgX = cx + px;
                if (imgX < 0 || imgX >= imgW) continue;

                float faceShade = px < 0 ? leftShade : rightShade;

                if (cliffTexData != null && cliffTexW > 0 && cliffTexH > 0)
                {
                    // P1: 从崖壁纹理采样
                    int tx = ((px + halfW) * cliffTexW) / (halfW * 2);
                    int ty = (py * cliffTexH) / sidePx;
                    if (tx >= 0 && tx < cliffTexW && ty >= 0 && ty < cliffTexH)
                    {
                        int srcIdx = (ty * cliffTexW + tx) * 4;
                        float r = cliffTexData[srcIdx] / 255f * faceShade;
                        float g = cliffTexData[srcIdx + 1] / 255f * faceShade;
                        float b = cliffTexData[srcIdx + 2] / 255f * faceShade;
                        int dstIdx = rowOffset + imgX * 4;
                        imgData[dstIdx]     = (byte)(r * 255f);
                        imgData[dstIdx + 1] = (byte)(g * 255f);
                        imgData[dstIdx + 2] = (byte)(b * 255f);
                        imgData[dstIdx + 3] = 255;
                        continue;
                    }
                }

                // 回退：纯色+噪声
                float noise = ((px * 37 + py * 53 + cx * 7) % 23) / 23f * 0.15f - 0.075f;
                float layerLine = (py % 4 == 0) ? 0.88f : 1.0f;

                float r2 = Math.Clamp(baseColor.R * dim * faceShade * layerLine + noise, 0f, 1f);
                float g2 = Math.Clamp(baseColor.G * dim * faceShade * layerLine + noise, 0f, 1f);
                float b2 = Math.Clamp(baseColor.B * dim * faceShade * layerLine + noise, 0f, 1f);

                int dstIdx2 = rowOffset + imgX * 4;
                imgData[dstIdx2]     = (byte)(r2 * 255f);
                imgData[dstIdx2 + 1] = (byte)(g2 * 255f);
                imgData[dstIdx2 + 2] = (byte)(b2 * 255f);
                imgData[dstIdx2 + 3] = 255;
            }
        }
    }

    /// <summary>直接操作字节数组绘制水面波纹。</summary>
    private static void DrawWaterRipplesFast(byte[] imgData, int imgStride, int imgW, int imgH,
        int cx, int cy, TerrainCell cell, Random rng, int halfW, int halfH)
    {
        float rippleR = cell.Type == TerrainType.DeepWater ? 0.5f : 0.6f;
        float rippleG = cell.Type == TerrainType.DeepWater ? 0.6f : 0.7f;
        float rippleB = cell.Type == TerrainType.DeepWater ? 0.8f : 0.85f;

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
                if (imgX >= 0 && imgX < imgW && imgY >= 0 && imgY < imgH)
                {
                    int dstIdx = imgY * imgStride + imgX * 4;
                    byte ea = imgData[dstIdx + 3];
                    if (ea > 128)
                    {
                        imgData[dstIdx]     = (byte)Math.Min(imgData[dstIdx]     + rippleR * 76.5f, 255);
                        imgData[dstIdx + 1] = (byte)Math.Min(imgData[dstIdx + 1] + rippleG * 76.5f, 255);
                        imgData[dstIdx + 2] = (byte)Math.Min(imgData[dstIdx + 2] + rippleB * 76.5f, 255);
                    }
                }
            }
        }
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

    // ===== DrawCliffEdges 字节缓冲区版（修复被SetData覆盖的bug） =====

    private static void DrawCliffEdgesFast(byte[] imgData, int imgStride, int imgW, int imgH,
        int cx, int cy, TerrainCell cell, TerrainGrid terrain, int gx, int gy)
    {
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

            DrawCliffLineFast(imgData, imgStride, imgW, imgH, cx, cy, dx, dy);
        }
    }

    private static void DrawCliffLineFast(byte[] imgData, int imgStride, int imgW, int imgH,
        int cx, int cy, int dx, int dy)
    {
        byte cr = 38, cg = 30, cb = 20; // 0.15f, 0.12f, 0.08f * 255
        int sidePx = ElevSidePx[3];

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
            if (imgX < 0 || imgX >= imgW || imgY < 0 || imgY >= imgH)
                continue;

            int dstIdx = imgY * imgStride + imgX * 4;
            imgData[dstIdx] = cr; imgData[dstIdx + 1] = cg; imgData[dstIdx + 2] = cb; imgData[dstIdx + 3] = 255;

            for (int s = 1; s <= sidePx; s++)
            {
                int sy = imgY + s;
                if (sy >= imgH) break;
                float fade = 1f - (float)s / sidePx * 0.3f;
                int sIdx = sy * imgStride + imgX * 4;
                imgData[sIdx]     = (byte)(cr * fade);
                imgData[sIdx + 1] = (byte)(cg * fade);
                imgData[sIdx + 2] = (byte)(cb * fade);
                imgData[sIdx + 3] = 255;
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

    // ======== 邻接感知贴图选择 ========

    /// <summary>
    /// 判断地形类型是否为水域。
    /// </summary>
    private static bool IsWaterType(TerrainType t)
        => t == TerrainType.ShallowWater || t == TerrainType.DeepWater;

    /// <summary>
    /// 邻接感知tile选择：根据周围邻居类型选择最合适的tile变体。
    /// 实现海岸线过渡、水深过渡和道路连接。
    /// </summary>
    private static (Image img, bool isTransition) GetTileWithAdjacency(
        TerrainCell cell, TerrainGrid terrain, int gx, int gy, Random rng,
        Image[][] grass, Image[][] sand, Image[][] shallow, Image[][] deep,
        Image[][] mountain, Image[][] snow, Image[][] city, Image[][] field,
        Image[][] coast, Image[][] sandCoast, Image[][] waterDepth)
    {
        var effType = terrain.GetEffectiveType(gx, gy);

        // 道路 - 邻接感知连接
        if (effType == TerrainType.Road)
            return (GetRoadTileWithConnections(terrain, gx, gy), false);

        // 桥梁/隧道/悬崖 - 保持原有逻辑
        if (effType == TerrainType.Bridge)
            return (_bridgeTex?.GetImage() ?? grass[0][0], false);
        if (effType == TerrainType.Tunnel)
            return (_tunnelTex?.GetImage() ?? grass[0][0], false);
        if (effType == TerrainType.Cliff)
            return (_cliffTex?.GetImage() ?? grass[0][0], false);

        // 浅水 - 检查深水邻居，选择水深过渡tile
        if (effType == TerrainType.ShallowWater)
        {
            var depthTile = GetDepthTransitionTile(terrain, gx, gy, waterDepth);
            if (depthTile != null)
                return (depthTile, true);
            return (shallow[0][rng.Next(shallow[0].Length)], false);
        }

        // 深水 - 内部变体
        if (effType == TerrainType.DeepWater)
            return (deep[0][rng.Next(deep[0].Length)], false);

        // 陆地 - 检查水域邻居，选择海岸线过渡tile
        bool isLand = effType == TerrainType.Grass || effType == TerrainType.Sand ||
                      effType == TerrainType.Snow || effType == TerrainType.City ||
                      effType == TerrainType.Field || effType == TerrainType.Mountain;
        if (isLand)
        {
            var coastTile = GetCoastTile(terrain, gx, gy, effType, coast, sandCoast, grass, sand, snow, field);
            if (coastTile != null)
                return (coastTile, true);

            // 内部陆地tile - 使用多变体
            return effType switch
            {
                TerrainType.Grass => (grass[0][rng.Next(grass[0].Length)], false),
                TerrainType.Sand => (sand[0][rng.Next(sand[0].Length)], false),
                TerrainType.Snow => (snow[0][rng.Next(snow[0].Length)], false),
                TerrainType.City => (city[0][rng.Next(city[0].Length)], false),
                TerrainType.Field => (field[0][rng.Next(field[0].Length)], false),
                TerrainType.Mountain => (mountain[0][rng.Next(mountain[0].Length)], false),
                _ => (grass[0][0], false),
            };
        }

        return (grass[0][0], false);
    }

    /// <summary>
    /// 海岸线过渡tile选择。
    /// 等距视角中4个网格邻居的视觉方向：
    ///   (gx,gy-1)=右上, (gx,gy+1)=左下, (gx+1,gy)=右下, (gx-1,gy)=左上
    /// coast tile索引：0=N(水在南), 1=S(水在北), 2=E(水在西), 3=W(水在东),
    ///   4=NE(水在西南), 5=NW(水在东南), 6=SE(水在西北), 7=SW(水在东北)
    /// </summary>
    private static Image? GetCoastTile(TerrainGrid terrain, int gx, int gy,
        TerrainType landType, Image[][] coast, Image[][] sandCoast,
        Image[][] grass, Image[][] sand, Image[][] snow, Image[][] field)
    {
        // 检查4个网格邻居是否为水域
        bool wUR = IsWaterType(terrain.GetEffectiveType(gx, gy - 1)); // 右上
        bool wDL = IsWaterType(terrain.GetEffectiveType(gx, gy + 1)); // 左下
        bool wDR = IsWaterType(terrain.GetEffectiveType(gx + 1, gy)); // 右下
        bool wUL = IsWaterType(terrain.GetEffectiveType(gx - 1, gy)); // 左上

        int count = (wUR ? 1 : 0) + (wDL ? 1 : 0) + (wDR ? 1 : 0) + (wUL ? 1 : 0);
        if (count == 0) return null;
        // 沙地类型使用沙滩过渡tile，其他类型使用普通海岸线tile
        var coastSet = (landType == TerrainType.Sand && sandCoast[0].Length > 0 && sandCoast[0][0] != null)
            ? sandCoast : coast;
        if (coastSet[0].Length == 0 || coastSet[0][0] == null) return null;

        int idx;
        if (count == 1)
        {
            // 单方向水域
            if (wUR) idx = 7; // 水在东北 → SW tile
            else if (wDL) idx = 4; // 水在西南 → NE tile
            else if (wDR) idx = 5; // 水在东南 → NW tile
            else idx = 6; // 水在西北 → SE tile
        }
        else if (count >= 3)
        {
            // 三面环水 - 选择最强的两个方向
            if (wUR && wUL) idx = 1; // 上方两面水 → S tile (水在北)
            else if (wDR && wDL) idx = 0; // 下方两面水 → N tile (水在南)
            else if (wUR && wDR) idx = 3; // 右方两面水 → W tile (水在东)
            else idx = 2; // 左方两面水 → E tile (水在西)
        }
        else
        {
            // count == 2
            if (wUR && wUL) idx = 1; // 上方 → S
            else if (wDR && wDL) idx = 0; // 下方 → N
            else if (wUR && wDR) idx = 3; // 右方 → W
            else if (wUL && wDL) idx = 2; // 左方 → E
            else if (wUR && wDL) idx = 7; // 对角线 → 选一个方向
            else idx = 6; // (wUL && wDR) 对角线
        }

        return coastSet[0][idx];
    }

    /// <summary>
    /// 浅水→深水过渡tile选择。逻辑同海岸线。
    /// </summary>
    private static Image? GetDepthTransitionTile(TerrainGrid terrain, int gx, int gy, Image[][] waterDepth)
    {
        bool dUR = terrain.GetEffectiveType(gx, gy - 1) == TerrainType.DeepWater;
        bool dDL = terrain.GetEffectiveType(gx, gy + 1) == TerrainType.DeepWater;
        bool dDR = terrain.GetEffectiveType(gx + 1, gy) == TerrainType.DeepWater;
        bool dUL = terrain.GetEffectiveType(gx - 1, gy) == TerrainType.DeepWater;

        int count = (dUR ? 1 : 0) + (dDL ? 1 : 0) + (dDR ? 1 : 0) + (dUL ? 1 : 0);
        if (count == 0) return null;
        if (waterDepth[0].Length == 0 || waterDepth[0][0] == null) return null;

        int idx;
        if (count == 1)
        {
            if (dUR) idx = 7;
            else if (dDL) idx = 4;
            else if (dDR) idx = 5;
            else idx = 6;
        }
        else if (count >= 3)
        {
            if (dUR && dUL) idx = 1;
            else if (dDR && dDL) idx = 0;
            else if (dUR && dDR) idx = 3;
            else idx = 2;
        }
        else
        {
            if (dUR && dUL) idx = 1;
            else if (dDR && dDL) idx = 0;
            else if (dUR && dDR) idx = 3;
            else if (dUL && dDL) idx = 2;
            else if (dUR && dDL) idx = 7;
            else idx = 6;
        }

        return waterDepth[0][idx];
    }

    /// <summary>
    /// 道路tile选择：根据4方向道路连接选择正确的道路变体。
    /// 等距视角中：N(gy-1)=右上, S(gy+1)=左下, E(gx+1)=右下, W(gx-1)=左上
    /// </summary>
    private static Image GetRoadTileWithConnections(TerrainGrid terrain, int gx, int gy)
    {
        bool n = terrain.GetEffectiveType(gx, gy - 1) == TerrainType.Road;
        bool s = terrain.GetEffectiveType(gx, gy + 1) == TerrainType.Road;
        bool e = terrain.GetEffectiveType(gx + 1, gy) == TerrainType.Road;
        bool w = terrain.GetEffectiveType(gx - 1, gy) == TerrainType.Road;

        int count = (n ? 1 : 0) + (s ? 1 : 0) + (e ? 1 : 0) + (w ? 1 : 0);

        string key;
        if (count == 0) key = "E";
        else if (count == 4) key = "Cross";
        else if (count == 3)
        {
            if (!n) key = "T_S";
            else if (!s) key = "T_N";
            else if (!e) key = "T_W";
            else key = "T_E";
        }
        else if (count == 2)
        {
            if (n && s) key = "N";
            else if (e && w) key = "E";
            else if (n && e) key = "NE";
            else if (n && w) key = "NW";
            else if (s && e) key = "SE";
            else key = "SW";
        }
        else
        {
            // count == 1: 端点，用直道
            if (n || s) key = "N";
            else key = "E";
        }

        if (_roadTexs != null && _roadTexs.TryGetValue(key, out var tex) && tex != null)
            return tex.GetImage();

        // 回退：尝试旧的3个道路纹理
        var fallback = _roadETex?.GetImage() ?? GetFallbackTile(new Color(0.3f, 0.3f, 0.3f));
        return fallback;
    }

    // ======== 贴图加载 ========

    private static Texture2D?[]? _grassTexs;
    private static Texture2D?[]? _sandTexs;
    private static Texture2D?[]? _shallowTexs;
    private static Texture2D?[]? _deepTexs;
    private static Texture2D?[]? _mountainTexs;
    private static Texture2D?[]? _snowTexs;
    private static Texture2D?[]? _cityTexs;
    private static Texture2D?[]? _fieldTexs;
    // 海岸线过渡tile: N,S,E,W,NE,NW,SE,SW (8方向)
    private static Texture2D?[]? _coastTexs;
    private static Texture2D?[]? _sandCoastTexs;
    // 浅水→深水过渡tile: 同上8方向
    private static Texture2D?[]? _waterDepthTexs;
    // 道路tile字典（邻接感知）
    private static Dictionary<string, Texture2D?>? _roadTexs;
    // 旧版道路纹理（回退用）
    private static Texture2D? _roadETex, _roadNTex, _roadCrossTex;
    private static Texture2D? _bridgeTex, _tunnelTex, _cliffTex;
    // P1: 悬崖侧面纹理（按地形类型分组的多变体纹理）
    private static Dictionary<string, Texture2D?[]>? _cliffSideTexs;
    // P1: Overlay装饰物纹理（树木/岩石/灌木）
    private static Texture2D?[]? _treeTexs;
    private static Texture2D?[]? _rockTexs;
    private static Texture2D?[]? _bushTexs;
    private static bool _texturesLoaded = false;

    private static void EnsureTerrainTextures()
    {
        if (_texturesLoaded) return;
        _grassTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileGrass1.png",
            "res://assets/sprites/terrain/tileGrass2.png",
            "res://assets/sprites/terrain/tileGrass3.png",
            "res://assets/sprites/terrain/tileGrass4.png",
            "res://assets/sprites/terrain/tileGrass5.png",
            "res://assets/sprites/terrain/tileGrass6.png",
            "res://assets/sprites/terrain/tileGrass7.png",
            "res://assets/sprites/terrain/tileGrass8.png" });
        _sandTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileSand1.png",
            "res://assets/sprites/terrain/tileSand2.png",
            "res://assets/sprites/terrain/tileSand3.png",
            "res://assets/sprites/terrain/tileSand4.png",
            "res://assets/sprites/terrain/tileSand5.png",
            "res://assets/sprites/terrain/tileSand6.png" });
        _shallowTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileShallow1.png",
            "res://assets/sprites/terrain/tileShallow2.png",
            "res://assets/sprites/terrain/tileShallow3.png",
            "res://assets/sprites/terrain/tileShallow4.png",
            "res://assets/sprites/terrain/tileShallow5.png",
            "res://assets/sprites/terrain/tileShallow6.png" });
        _deepTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileDeep1.png",
            "res://assets/sprites/terrain/tileDeep2.png",
            "res://assets/sprites/terrain/tileDeep3.png",
            "res://assets/sprites/terrain/tileDeep4.png",
            "res://assets/sprites/terrain/tileDeep5.png",
            "res://assets/sprites/terrain/tileDeep6.png" });
        _mountainTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileMountain1.png",
            "res://assets/sprites/terrain/tileMountain2.png",
            "res://assets/sprites/terrain/tileMountain3.png",
            "res://assets/sprites/terrain/tileMountain4.png",
            "res://assets/sprites/terrain/tileMountain5.png",
            "res://assets/sprites/terrain/tileMountain6.png" });
        _snowTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileSnow1.png",
            "res://assets/sprites/terrain/tileSnow2.png",
            "res://assets/sprites/terrain/tileSnow3.png",
            "res://assets/sprites/terrain/tileSnow4.png",
            "res://assets/sprites/terrain/tileSnow5.png",
            "res://assets/sprites/terrain/tileSnow6.png" });
        _cityTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileCity1.png",
            "res://assets/sprites/terrain/tileCity2.png",
            "res://assets/sprites/terrain/tileCity3.png",
            "res://assets/sprites/terrain/tileCity4.png" });
        _fieldTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileField1.png",
            "res://assets/sprites/terrain/tileField2.png",
            "res://assets/sprites/terrain/tileField3.png",
            "res://assets/sprites/terrain/tileField4.png" });
        // 海岸线过渡tile (8方向)
        _coastTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileCoast_N.png",
            "res://assets/sprites/terrain/tileCoast_S.png",
            "res://assets/sprites/terrain/tileCoast_E.png",
            "res://assets/sprites/terrain/tileCoast_W.png",
            "res://assets/sprites/terrain/tileCoast_NE.png",
            "res://assets/sprites/terrain/tileCoast_NW.png",
            "res://assets/sprites/terrain/tileCoast_SE.png",
            "res://assets/sprites/terrain/tileCoast_SW.png" });
        // 沙滩海岸线过渡tile (8方向) — 水体边缘沙地过渡
        _sandCoastTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileCoastSand_0.png",
            "res://assets/sprites/terrain/tileCoastSand_1.png",
            "res://assets/sprites/terrain/tileCoastSand_2.png",
            "res://assets/sprites/terrain/tileCoastSand_3.png",
            "res://assets/sprites/terrain/tileCoastSand_4.png",
            "res://assets/sprites/terrain/tileCoastSand_5.png",
            "res://assets/sprites/terrain/tileCoastSand_6.png",
            "res://assets/sprites/terrain/tileCoastSand_7.png" });
        // 浅水→深水过渡tile (8方向)
        _waterDepthTexs = LoadTexArray(new[] {
            "res://assets/sprites/terrain/tileWaterDepth_N.png",
            "res://assets/sprites/terrain/tileWaterDepth_S.png",
            "res://assets/sprites/terrain/tileWaterDepth_E.png",
            "res://assets/sprites/terrain/tileWaterDepth_W.png",
            "res://assets/sprites/terrain/tileWaterDepth_NE.png",
            "res://assets/sprites/terrain/tileWaterDepth_NW.png",
            "res://assets/sprites/terrain/tileWaterDepth_SE.png",
            "res://assets/sprites/terrain/tileWaterDepth_SW.png" });
        // 邻接感知道路tile
        _roadTexs = new Dictionary<string, Texture2D?>();
        string[] roadKeys = { "E", "N", "NE", "NW", "SE", "SW", "Cross", "T_N", "T_S", "T_E", "T_W" };
        foreach (var rk in roadKeys)
        {
            _roadTexs[rk] = LoadTexSafe($"res://assets/sprites/terrain/tileRoad_{rk}.png");
        }
        // 旧版道路纹理（回退用）
        _roadETex = LoadTexSafe("res://assets/sprites/terrain/tileGrass_roadEast.png");
        _roadNTex = LoadTexSafe("res://assets/sprites/terrain/tileGrass_roadNorth.png");
        _roadCrossTex = LoadTexSafe("res://assets/sprites/terrain/tileGrass_roadCrossing.png");
        _bridgeTex = LoadTexSafe("res://assets/sprites/terrain/tileBridge.png");
        _tunnelTex = LoadTexSafe("res://assets/sprites/terrain/tileTunnel.png");
        _cliffTex = LoadTexSafe("res://assets/sprites/terrain/tileCliff.png");
        // P1: 加载悬崖侧面纹理（5种地形类型×3变体）
        _cliffSideTexs = new Dictionary<string, Texture2D?[]>();
        foreach (var type in new[] { "grass", "mountain", "snow", "sand", "rock" })
        {
            var arr = new Texture2D?[3];
            for (int i = 0; i < 3; i++)
                arr[i] = LoadTexSafe($"res://assets/sprites/terrain/tileCliffSide_{type}{i+1}.png");
            _cliffSideTexs[type] = arr;
        }
        // P1: 加载Overlay装饰物纹理
        _treeTexs = LoadTexArray(new[] {
            "res://assets/sprites/overlay/tree_pine1.png", "res://assets/sprites/overlay/tree_pine2.png",
            "res://assets/sprites/overlay/tree_pine3.png", "res://assets/sprites/overlay/tree_pine4.png",
            "res://assets/sprites/overlay/tree_oak1.png", "res://assets/sprites/overlay/tree_oak2.png",
            "res://assets/sprites/overlay/tree_oak3.png", "res://assets/sprites/overlay/tree_oak4.png",
            "res://assets/sprites/overlay/tree_dead1.png", "res://assets/sprites/overlay/tree_dead2.png",
            "res://assets/sprites/overlay/tree_dead3.png", "res://assets/sprites/overlay/tree_dead4.png" });
        _rockTexs = LoadTexArray(new[] {
            "res://assets/sprites/overlay/rock_small1.png", "res://assets/sprites/overlay/rock_small2.png",
            "res://assets/sprites/overlay/rock_small3.png", "res://assets/sprites/overlay/rock_small4.png",
            "res://assets/sprites/overlay/rock_large1.png", "res://assets/sprites/overlay/rock_large2.png",
            "res://assets/sprites/overlay/rock_large3.png", "res://assets/sprites/overlay/rock_large4.png" });
        _bushTexs = LoadTexArray(new[] {
            "res://assets/sprites/overlay/bush_green1.png", "res://assets/sprites/overlay/bush_green2.png",
            "res://assets/sprites/overlay/bush_green3.png", "res://assets/sprites/overlay/bush_dry1.png",
            "res://assets/sprites/overlay/bush_dry2.png", "res://assets/sprites/overlay/bush_dry3.png",
            "res://assets/sprites/overlay/bush_mixed1.png", "res://assets/sprites/overlay/bush_mixed2.png",
            "res://assets/sprites/overlay/bush_mixed3.png" });
        _texturesLoaded = true;
    }

    private static Texture2D?[] LoadTexArray(string[] paths)
    {
        var arr = new Texture2D?[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            arr[i] = LoadTexSafe(paths[i]);
        }
        return arr;
    }

    /// <summary>带预检的纹理加载，避免GD.Load失败产生错误日志。</summary>
    private static Texture2D? LoadTexSafe(string path)
    {
        if (!ResourceLoader.Exists(path, "Texture2D"))
        {
            GameLog.Error($"[IsoTerrain] Texture not found: {path}");
            return null;
        }
        var tex = GD.Load<Texture2D>(path);
        if (tex == null)
            GameLog.Error($"[IsoTerrain] GD.Load returned null: {path}");
        return tex;
    }

    private static Image? _fallbackTile;

    /// <summary>获取64×32纯色占位图（地形贴图加载失败时的回退）。</summary>
    private static Image GetFallbackTile(Color color)
    {
        if (_fallbackTile == null)
        {
            _fallbackTile = Image.CreateEmpty(TileW, TileH, false, Image.Format.Rgba8);
        }
        _fallbackTile.Fill(color);
        return _fallbackTile;
    }

    private static Image[][] LoadImageArray(Texture2D?[] texs)
    {
        var arr = new Image[1][];
        arr[0] = new Image[texs.Length];
        for (int i = 0; i < texs.Length; i++)
        {
            arr[0][i] = texs[i]?.GetImage()!;
            if (arr[0][i] == null)
            {
                // 加载失败时使用纯色占位图而非1×1透明图，避免渲染为透明块
                arr[0][i] = GetFallbackTile(new Color(0.35f, 0.45f, 0.25f));
                GameLog.Error($"[IsoTerrain] Texture load failed, using solid color placeholder");
            }
        }
        return arr;
    }
}
