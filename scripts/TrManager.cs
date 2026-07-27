using Godot;
using System.Collections.Generic;
using System.IO;

namespace RTSGame;

/// <summary>
/// P2-1: 国际化(i18n)翻译管理器。
/// 加载CSV格式的翻译文件，提供键值对翻译查询。
/// Godot原生支持.csv翻译：项目根目录放置zh-CN.csv/en.csv等，
/// Godot自动编译为.translation并按系统语言加载。
/// 
/// 此类提供运行时翻译键查询，用于代码中动态生成的文本。
/// 静态UI文本（如Label节点）建议直接用Godot的内置翻译系统。
/// </summary>
public static class TrManager
{
    private static readonly Dictionary<string, string> _strings = new();
    private static string _currentLang = "zh-CN";

    /// <summary>当前语言代码</summary>
    public static string CurrentLang => _currentLang;

    /// <summary>
    /// 获取翻译文本。键格式建议：类别.名称（如 ui.build、unit.light_tank）
    /// 如果找不到翻译，返回key本身（开发阶段友好）
    /// </summary>
    public static string Tr(string key)
    {
        return _strings.GetValueOrDefault(key, key);
    }

    /// <summary>带参数的翻译，替换{0}{1}...占位符</summary>
    public static string Tr(string key, params object[] args)
    {
        var template = Tr(key);
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    /// <summary>
    /// 从CSV文件加载翻译。CSV格式：key,translation
    /// 文件路径相对于user:// 或 res://
    /// </summary>
    public static void LoadFromCsv(string path)
    {
        var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GameLog.Warning($"[i18n] 无法打开翻译文件: {path}");
            return;
        }

        int count = 0;
        while (!file.EofReached())
        {
            var line = file.GetLine();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            var parts = line.Split(',', 2);
            if (parts.Length == 2)
            {
                _strings[parts[0].Trim()] = parts[1].Trim();
                count++;
            }
        }
        file.Close();
        GameLog.Info($"[i18n] 加载 {count} 条翻译 ({path})");
    }

    /// <summary>切换语言并重新加载翻译</summary>
    public static void SetLanguage(string lang)
    {
        _currentLang = lang;
        _strings.Clear();
        TranslationServer.SetLocale(lang);
        LoadFromCsv($"res://i18n/{lang}.csv");
        GameLog.Info($"[i18n] 语言切换为: {lang}");
    }

    /// <summary>初始化默认语言</summary>
    public static void Initialize()
    {
        _strings.Clear();
        // 尝试加载当前语言的翻译文件
        var lang = TranslationServer.GetLocale();
        _currentLang = lang;
        LoadFromCsv($"res://i18n/{lang}.csv");
        GameLog.Info($"[i18n] 初始化完成，语言: {lang}，{_strings.Count} 条翻译");
    }
}
