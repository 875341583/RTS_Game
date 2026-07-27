using System;
using System.Collections.Generic;
using Godot;

namespace RTSGame;

/// <summary>
/// P1-3: 地图编辑器 — 独立于游戏运行时的地图数据结构。
///
/// 设计理念：
/// - 地图文件 = 种子 + 增量修改列表（TerrainModSave）
/// - 编辑器修改的是增量列表，不触碰种子生成逻辑
/// - 游戏加载时：先用种子生成基础地形，再依次应用增量修改
/// - 这样地图文件极小（只存差异），且向前兼容现有SaveLoadSystem
///
/// 地图文件格式(.rmap)：
/// {
///   "version": 1,
///   "name": "自定义地图名",
///   "author": "作者",
///   "seed": 42,
///   "description": "地图描述",
///   "baseCount": 8,           // 基地数量（2-8）
///   "terrainMods": [           // 增量修改列表
///     { "gx": 5, "gy": 3, "terrainType": 6, "elevation": 0, "hasBridge": false, "hasTunnel": false }
///   ],
///   "resourceNodes": [         // 矿点放置
///     { "gx": 10, "gy": 10, "amount": 5000 }
///   ],
///   "strategicPoints": [       // 战略点放置
///     { "gx": 16, "gy": 16 }
///   ]
/// }
/// </summary>
public class MapData
{
    /// <summary>地图文件格式版本。</summary>
    public int Version = 1;
    /// <summary>地图名称。</summary>
    public string Name = "新地图";
    /// <summary>作者。</summary>
    public string Author = "";
    /// <summary>描述。</summary>
    public string Description = "";
    /// <summary>地形生成种子（0=空白地图，不生成基础地形）。</summary>
    public ulong Seed = 42;
    /// <summary>基地数量（2-8）。</summary>
    public int BaseCount = 8;
    /// <summary>地形增量修改列表。</summary>
    public List<SaveLoadSystem.TerrainModSave> TerrainMods = new();
    /// <summary>矿点放置列表。</summary>
    public List<ResourceNodeSave> ResourceNodes = new();
    /// <summary>战略点放置列表。</summary>
    public List<StrategicPointSave> StrategicPoints = new();

    /// <summary>矿点数据。</summary>
    public class ResourceNodeSave
    {
        public int Gx, Gy;
        public int Amount = 5000;
    }

    /// <summary>战略点数据。</summary>
    public class StrategicPointSave
    {
        public int Gx, Gy;
    }

    // ======== JSON 序列化/反序列化 ========

    /// <summary>序列化为Godot Variant字典（用于Json.Stringify）。</summary>
    public Godot.Collections.Dictionary ToVariant()
    {
        var d = new Godot.Collections.Dictionary
        {
            ["version"] = Version,
            ["name"] = Name,
            ["author"] = Author,
            ["description"] = Description,
            ["seed"] = (long)Seed,
            ["baseCount"] = BaseCount
        };

        var mods = new Godot.Collections.Array();
        foreach (var m in TerrainMods)
        {
            mods.Add(new Godot.Collections.Dictionary
            {
                ["gx"] = m.Gx,
                ["gy"] = m.Gy,
                ["terrainType"] = m.TerrainType,
                ["elevation"] = m.Elevation,
                ["hasBridge"] = m.HasBridge,
                ["hasTunnel"] = m.HasTunnel
            });
        }
        d["terrainMods"] = mods;

        var resources = new Godot.Collections.Array();
        foreach (var r in ResourceNodes)
        {
            resources.Add(new Godot.Collections.Dictionary
            {
                ["gx"] = r.Gx,
                ["gy"] = r.Gy,
                ["amount"] = r.Amount
            });
        }
        d["resourceNodes"] = resources;

        var points = new Godot.Collections.Array();
        foreach (var p in StrategicPoints)
        {
            points.Add(new Godot.Collections.Dictionary
            {
                ["gx"] = p.Gx,
                ["gy"] = p.Gy
            });
        }
        d["strategicPoints"] = points;

        return d;
    }

    /// <summary>从Godot Variant字典反序列化。</summary>
    public static MapData? FromVariant(Variant v)
    {
        if (v.VariantType != Variant.Type.Dictionary) return null;
        var d = v.AsGodotDictionary();
        var data = new MapData();

        if (d.ContainsKey("version")) data.Version = (int)d["version"].AsInt32();
        if (d.ContainsKey("name")) data.Name = d["name"].AsString();
        if (d.ContainsKey("author")) data.Author = d["author"].AsString();
        if (d.ContainsKey("description")) data.Description = d["description"].AsString();
        if (d.ContainsKey("seed")) data.Seed = (ulong)d["seed"].AsInt64();
        if (d.ContainsKey("baseCount")) data.BaseCount = (int)d["baseCount"].AsInt32();

        if (d.ContainsKey("terrainMods"))
        {
            var arr = d["terrainMods"].AsGodotArray();
            foreach (var item in arr)
            {
                var m = item.AsGodotDictionary();
                data.TerrainMods.Add(new SaveLoadSystem.TerrainModSave
                {
                    Gx = (int)m["gx"].AsInt32(),
                    Gy = (int)m["gy"].AsInt32(),
                    TerrainType = (int)m["terrainType"].AsInt32(),
                    Elevation = (int)m["elevation"].AsInt32(),
                    HasBridge = m.ContainsKey("hasBridge") && m["hasBridge"].AsBool(),
                    HasTunnel = m.ContainsKey("hasTunnel") && m["hasTunnel"].AsBool()
                });
            }
        }

        if (d.ContainsKey("resourceNodes"))
        {
            var arr = d["resourceNodes"].AsGodotArray();
            foreach (var item in arr)
            {
                var r = item.AsGodotDictionary();
                data.ResourceNodes.Add(new ResourceNodeSave
                {
                    Gx = (int)r["gx"].AsInt32(),
                    Gy = (int)r["gy"].AsInt32(),
                    Amount = r.ContainsKey("amount") ? (int)r["amount"].AsInt32() : 5000
                });
            }
        }

        if (d.ContainsKey("strategicPoints"))
        {
            var arr = d["strategicPoints"].AsGodotArray();
            foreach (var item in arr)
            {
                var p = item.AsGodotDictionary();
                data.StrategicPoints.Add(new StrategicPointSave
                {
                    Gx = (int)p["gx"].AsInt32(),
                    Gy = (int)p["gy"].AsInt32()
                });
            }
        }

        return data;
    }

    // ======== 文件 I/O ========

    /// <summary>保存地图到文件（.rmap格式，实为JSON）。</summary>
    public static bool SaveToFile(MapData data, string filePath)
    {
        try
        {
            string json = Json.Stringify(data.ToVariant());
            // 用户目录下的地图文件用 FileAccess，绝对路径用 System.IO
            if (filePath.StartsWith("user://"))
            {
                using var file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Write);
                if (file == null)
                {
                    GameLog.Error($"[MapData] 无法写入地图文件: {filePath}");
                    return false;
                }
                file.StoreString(json);
            }
            else
            {
                System.IO.File.WriteAllText(filePath, json);
            }
            GameLog.Debug($"[MapData] 地图已保存: {filePath} ({data.TerrainMods.Count}个修改, {data.ResourceNodes.Count}个矿点, {data.StrategicPoints.Count}个战略点)");
            return true;
        }
        catch (Exception ex)
        {
            GameLog.Error($"[MapData] 保存地图失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>从文件加载地图。</summary>
    public static MapData? LoadFromFile(string filePath)
    {
        try
        {
            string json;
            if (filePath.StartsWith("user://") || filePath.StartsWith("res://"))
            {
                using var file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Read);
                if (file == null)
                {
                    GameLog.Error($"[MapData] 无法读取地图文件: {filePath}");
                    return null;
                }
                json = file.GetAsText();
            }
            else
            {
                json = System.IO.File.ReadAllText(filePath);
            }
            var parsed = Json.ParseString(json);
            if (parsed.VariantType != Variant.Type.Dictionary)
            {
                GameLog.Error($"[MapData] 地图文件解析失败: {filePath}");
                return null;
            }
            var data = FromVariant(parsed);
            if (data != null)
                GameLog.Debug($"[MapData] 地图已加载: {data.Name} (seed={data.Seed}, {data.TerrainMods.Count}个修改)");
            return data;
        }
        catch (Exception ex)
        {
            GameLog.Error($"[MapData] 加载地图失败: {ex.Message}");
            return null;
        }
    }

    // ======== 快捷操作 ========

    /// <summary>添加或更新某个格子的地形修改。</summary>
    public void SetCellMod(int gx, int gy, TerrainType type, int elevation, bool hasBridge = false, bool hasTunnel = false)
    {
        // 移除同位置的旧修改
        TerrainMods.RemoveAll(m => m.Gx == gx && m.Gy == gy);
        // 添加新修改
        TerrainMods.Add(new SaveLoadSystem.TerrainModSave
        {
            Gx = gx, Gy = gy,
            TerrainType = (int)type,
            Elevation = elevation,
            HasBridge = hasBridge,
            HasTunnel = hasTunnel
        });
    }

    /// <summary>移除某个格子的地形修改（恢复种子生成的基础地形）。</summary>
    public void RemoveCellMod(int gx, int gy)
    {
        TerrainMods.RemoveAll(m => m.Gx == gx && m.Gy == gy);
    }

    /// <summary>获取某个格子的修改（无则返回null）。</summary>
    public SaveLoadSystem.TerrainModSave? GetCellMod(int gx, int gy)
    {
        foreach (var m in TerrainMods)
            if (m.Gx == gx && m.Gy == gy) return m;
        return null;
    }

    /// <summary>添加矿点。同位置只保留一个（后者覆盖）。</summary>
    public void AddResourceNode(int gx, int gy, int amount = 5000)
    {
        ResourceNodes.RemoveAll(r => r.Gx == gx && r.Gy == gy);
        ResourceNodes.Add(new ResourceNodeSave { Gx = gx, Gy = gy, Amount = amount });
    }

    /// <summary>移除矿点。</summary>
    public void RemoveResourceNode(int gx, int gy)
    {
        ResourceNodes.RemoveAll(r => r.Gx == gx && r.Gy == gy);
    }

    /// <summary>添加战略点。</summary>
    public void AddStrategicPoint(int gx, int gy)
    {
        if (!StrategicPoints.Exists(p => p.Gx == gx && p.Gy == gy))
            StrategicPoints.Add(new StrategicPointSave { Gx = gx, Gy = gy });
    }

    /// <summary>移除战略点。</summary>
    public void RemoveStrategicPoint(int gx, int gy)
    {
        StrategicPoints.RemoveAll(p => p.Gx == gx && p.Gy == gy);
    }

    /// <summary>清空所有修改（恢复到纯种子生成状态）。</summary>
    public void ClearAll()
    {
        TerrainMods.Clear();
        ResourceNodes.Clear();
        StrategicPoints.Clear();
    }
}

/// <summary>
/// P1-3: 地图编辑器笔刷工具。
/// 定义编辑器可用的笔刷类型和操作模式。
/// </summary>
public static class MapEditorBrush
{
    /// <summary>笔刷模式。</summary>
    public enum BrushMode
    {
        /// <summary>单格笔刷（修改点击格子）。</summary>
        Single,
        /// <summary>方形笔刷（修改3×3区域）。</summary>
        Square3,
        /// <summary>圆形笔刷（修改半径2的圆）。</summary>
        Circle5,
        /// <summary>填充笔刷（洪水填充同类型区域）。</summary>
        Fill,
        /// <summary>橡皮擦（移除该格修改）。</summary>
        Eraser,
    }

    /// <summary>当前选中的地形类型。</summary>
    public static TerrainType SelectedTerrain = TerrainType.Grass;

    /// <summary>当前选中的海拔等级。</summary>
    public static int SelectedElevation = 1;

    /// <summary>当前笔刷模式。</summary>
    public static BrushMode CurrentMode = BrushMode.Single;

    /// <summary>是否绘制桥梁。</summary>
    public static bool PaintBridge = false;

    /// <summary>是否绘制隧道。</summary>
    public static bool PaintTunnel = false;

    /// <summary>获取笔刷影响的所有格子坐标。</summary>
    public static List<(int gx, int gy)> GetBrushCells(int cx, int cy, BrushMode mode)
    {
        var cells = new List<(int gx, int gy)>();
        switch (mode)
        {
            case BrushMode.Single:
                cells.Add((cx, cy));
                break;
            case BrushMode.Square3:
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        cells.Add((cx + dx, cy + dy));
                break;
            case BrushMode.Circle5:
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        if (dx * dx + dy * dy <= 4) // 半径2
                            cells.Add((cx + dx, cy + dy));
                break;
            // Fill 和 Eraser 在调用方特殊处理
            case BrushMode.Fill:
            case BrushMode.Eraser:
                cells.Add((cx, cy));
                break;
        }
        // 钳制到地图范围内
        cells = cells.FindAll(c =>
            c.gx >= 0 && c.gx < TerrainGrid.GridSize &&
            c.gy >= 0 && c.gy < TerrainGrid.GridSize);
        return cells;
    }

    /// <summary>洪水填充：从起点开始，把所有与起点同类型且连通的格子加入修改列表。
    /// 注意：填充基于**编辑后**的地形状态，不是种子生成状态。</summary>
    public static List<(int gx, int gy)> FloodFill(int startX, int startY, TerrainGrid grid, MapData mapData)
    {
        var result = new List<(int gx, int gy)>();
        var visited = new bool[TerrainGrid.GridSize, TerrainGrid.GridSize];
        var queue = new Queue<(int x, int y)>();
        var startCell = GetEffectiveCell(startX, startY, grid, mapData);
        queue.Enqueue((startX, startY));
        visited[startX, startY] = true;

        while (queue.Count > 0 && result.Count < TerrainGrid.GridSize * TerrainGrid.GridSize)
        {
            var (x, y) = queue.Dequeue();
            result.Add((x, y));

            // 4方向扩展
            int[][] dirs = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
            foreach (var d in dirs)
            {
                int nx = x + d[0], ny = y + d[1];
                if (nx < 0 || nx >= TerrainGrid.GridSize || ny < 0 || ny >= TerrainGrid.GridSize) continue;
                if (visited[nx, ny]) continue;
                var cell = GetEffectiveCell(nx, ny, grid, mapData);
                if (cell.Type == startCell.Type && cell.Elevation == startCell.Elevation)
                {
                    visited[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }
        return result;
    }

    /// <summary>获取格子的有效地形（优先取mapData中的修改，否则取grid基础值）。</summary>
    private static TerrainCell GetEffectiveCell(int gx, int gy, TerrainGrid grid, MapData mapData)
    {
        var mod = mapData.GetCellMod(gx, gy);
        if (mod != null)
        {
            return new TerrainCell
            {
                Type = (TerrainType)mod.TerrainType,
                Elevation = mod.Elevation,
                HasBridge = mod.HasBridge,
                HasTunnel = mod.HasTunnel,
            };
        }
        return grid.GetCell(gx, gy);
    }

    /// <summary>应用笔刷到地图数据。</summary>
    public static void ApplyBrush(int gx, int gy, TerrainGrid grid, MapData mapData)
    {
        if (CurrentMode == BrushMode.Fill)
        {
            var cells = FloodFill(gx, gy, grid, mapData);
            foreach (var (x, y) in cells)
                mapData.SetCellMod(x, y, SelectedTerrain, SelectedElevation, PaintBridge, PaintTunnel);
        }
        else if (CurrentMode == BrushMode.Eraser)
        {
            var cells = GetBrushCells(gx, gy, CurrentMode);
            foreach (var (x, y) in cells)
                mapData.RemoveCellMod(x, y);
        }
        else
        {
            var cells = GetBrushCells(gx, gy, CurrentMode);
            foreach (var (x, y) in cells)
                mapData.SetCellMod(x, y, SelectedTerrain, SelectedElevation, PaintBridge, PaintTunnel);
        }
    }
}
