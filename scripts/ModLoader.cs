using System.Collections.Generic;
using System.Linq;
using Godot;

namespace RTSGame;

/// <summary>
/// P2-4: Mod加载系统 — 支持从 res://mods/ 目录加载Mod，
/// 用Mod数据覆盖基础JSON数据（科技树/战术卡/时代/难度/间谍任务等）。
///
/// Mod目录结构：
///   mods/MyMod/
///     mod.json          — Mod描述（name, version, description, priority, dataFiles[]）
///     data/techtree.json — 覆盖科技树数据（可选，仅列出需要覆盖的文件）
///     data/eras.json     — 覆盖时代数据（可选）
///     ...
///
/// 加载顺序：priority小的先加载，同priority按名称排序。
/// 后加载的Mod覆盖先加载的数据。
/// </summary>
public static class ModLoader
{
    /// <summary>Mod元信息。</summary>
    public class ModInfo
    {
        public string Name { get; init; } = "";
        public string Version { get; init; } = "";
        public string Description { get; init; } = "";
        public int Priority { get; init; } = 100;
        public string Directory { get; init; } = "";
    }

    private static readonly List<ModInfo> _loadedMods = new();
    private static readonly object _lock = new();
    private static bool _godotDisabled = false;

    /// <summary>禁用Godot IO调用（供单元测试使用，测试进程中无Godot运行时）。</summary>
    public static void DisableGodotIO() => _godotDisabled = true;

    /// <summary>已加载的Mod列表。</summary>
    public static IReadOnlyList<ModInfo> LoadedMods => _loadedMods;

    /// <summary>扫描并加载所有Mod。在游戏数据初始化之前调用。</summary>
    public static void LoadAllMods()
    {
        if (_godotDisabled) return;

        lock (_lock)
        {
            _loadedMods.Clear();

            var modDirs = ScanModDirectories();
            var modInfos = new List<ModInfo>();

            foreach (var modDir in modDirs)
            {
                var info = LoadModDescriptor(modDir);
                if (info != null)
                    modInfos.Add(info);
            }

            // 按priority排序，同priority按名称排序
            var sorted = modInfos.OrderBy(m => m.Priority).ThenBy(m => m.Name).ToList();

            foreach (var mod in sorted)
            {
                _loadedMods.Add(mod);
                GameLog.Info($"[ModLoader] 已加载Mod: {mod.Name} v{mod.Version} (priority={mod.Priority})");
            }

            if (_loadedMods.Count > 0)
                GameLog.Info($"[ModLoader] 共加载 {_loadedMods.Count} 个Mod");
            else
                GameLog.Debug("[ModLoader] 未发现任何Mod");
        }
    }

    /// <summary>获取指定数据文件的Mod覆盖路径列表（按优先级排序）。
    /// 返回的路径用于替代 res://data/xxx.json 或作为补充数据源。</summary>
    public static List<string> GetModDataPaths(string baseFileName)
    {
        var paths = new List<string>();
        lock (_lock)
        {
            foreach (var mod in _loadedMods)
            {
                var modPath = $"{mod.Directory}data/{baseFileName}";
                if (ResourceLoader.Exists(modPath))
                    paths.Add(modPath);
            }
        }
        return paths;
    }

    /// <summary>读取数据文件内容。如果有Mod覆盖，返回最后加载的Mod版本；
    /// 否则返回基础版本。用于所有数据驱动类的JSON加载。</summary>
    public static string ReadDataFile(string baseFileName)
    {
        // 测试环境中无Godot运行时，直接返回空字符串（触发调用方fallback）
        if (_godotDisabled) return "";

        // 检查Mod覆盖
        var modPaths = GetModDataPaths(baseFileName);
        if (modPaths.Count > 0)
        {
            // 最后一个Mod的覆盖优先级最高
            var lastModPath = modPaths[^1];
            var file = Godot.FileAccess.Open(lastModPath, Godot.FileAccess.ModeFlags.Read);
            if (file != null)
            {
                var content = file.GetAsText();
                file.Close();
                GameLog.Debug($"[ModLoader] 使用Mod数据: {lastModPath}");
                return content;
            }
        }

        // 无Mod覆盖，返回基础版本
        var basePath = $"res://data/{baseFileName}";
        var baseFile = Godot.FileAccess.Open(basePath, Godot.FileAccess.ModeFlags.Read);
        if (baseFile != null)
        {
            var content = baseFile.GetAsText();
            baseFile.Close();
            return content;
        }

        GameLog.Warning($"[ModLoader] 无法读取数据文件: {baseFileName}");
        return "";
    }

    /// <summary>扫描 res://mods/ 目录下的子目录。</summary>
    private static List<string> ScanModDirectories()
    {
        var dirs = new List<string>();
        var dir = Godot.DirAccess.Open("res://mods/");
        if (dir == null)
        {
            GameLog.Debug("[ModLoader] mods/ 目录不存在，跳过Mod扫描");
            return dirs;
        }

        dir.ListDirBegin();
        string name = dir.GetNext();
        while (name != "")
        {
            if (dir.CurrentIsDir() && !name.StartsWith("."))
                dirs.Add($"res://mods/{name}/");
            name = dir.GetNext();
        }
        dir.ListDirEnd();

        return dirs;
    }

    /// <summary>加载单个Mod的描述文件。</summary>
    private static ModInfo? LoadModDescriptor(string modDir)
    {
        var modJsonPath = $"{modDir}mod.json";
        if (!ResourceLoader.Exists(modJsonPath))
        {
            GameLog.Warning($"[ModLoader] Mod目录缺少mod.json: {modDir}");
            return null;
        }

        var file = Godot.FileAccess.Open(modJsonPath, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GameLog.Warning($"[ModLoader] 无法打开: {modJsonPath}");
            return null;
        }

        var jsonText = file.GetAsText();
        file.Close();

        var jsonResult = Json.ParseString(jsonText);
        if (jsonResult.VariantType != Variant.Type.Dictionary)
        {
            GameLog.Warning($"[ModLoader] mod.json格式错误: {modJsonPath}");
            return null;
        }

        var dict = jsonResult.AsGodotDictionary();
        return new ModInfo
        {
            Name = dict.ContainsKey("name") ? dict["name"].AsString() : "Unknown",
            Version = dict.ContainsKey("version") ? dict["version"].AsString() : "0.0",
            Description = dict.ContainsKey("description") ? dict["description"].AsString() : "",
            Priority = dict.ContainsKey("priority") ? (int)dict["priority"].AsInt64() : 100,
            Directory = modDir,
        };
    }
}
