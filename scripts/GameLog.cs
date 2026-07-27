using Godot;

namespace RTSGame;

/// <summary>
/// P2-1: 统一日志系统，替代分散的GD.Print调用。
/// 支持Debug/Info/Warning/Error四个级别，Release构建中仅保留Warning及以上。
/// 使用方式：GameLog.Info("消息")、GameLog.Debug("调试") 等。
/// </summary>
public static class GameLog
{
    /// <summary>日志级别</summary>
    public enum Level
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    /// <summary>
    /// 当前最低输出级别。Debug/Editor下为Debug，Release下为Warning。
    /// 可在运行时调整以临时启用更详细的日志。
    /// </summary>
    public static Level MinLevel { get; set; } =
#if DEBUG
        Level.Debug;
#else
        Level.Warning;
#endif

    /// <summary>是否在日志前添加级别标签</summary>
    public static bool ShowLevelTag { get; set; } = true;

    /// <summary>Debug级别日志，仅Debug构建中输出</summary>
    public static void Debug(string message)
    {
        if (MinLevel <= Level.Debug)
            PrintLog(Level.Debug, message);
    }

    /// <summary>Info级别日志，用于一般运行信息</summary>
    public static void Info(string message)
    {
        if (MinLevel <= Level.Info)
            PrintLog(Level.Info, message);
    }

    /// <summary>Warning级别日志，用于潜在问题</summary>
    public static void Warning(string message)
    {
        if (MinLevel <= Level.Warning)
            PrintLog(Level.Warning, message);
    }

    /// <summary>Error级别日志，用于错误情况</summary>
    public static void Error(string message)
    {
        if (MinLevel <= Level.Error)
            PrintLog(Level.Error, message);
    }

    /// <summary>条件性Debug日志</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void DebugConditional(string message)
    {
        if (MinLevel <= Level.Debug)
            PrintLog(Level.Debug, message);
    }

    /// <summary>是否在Godot运行时外安全降级（单元测试等场景自动启用）</summary>
    public static bool SafeMode { get; set; } = false;

    private static void PrintLog(Level level, string message)
    {
        if (SafeMode)
        {
            // 非Godot运行时中不能调用GD.Print等native方法
            System.Console.WriteLine($"[{level}] {message}");
            return;
        }
        var tag = ShowLevelTag ? $"[{level.ToString().ToUpper()}] " : "";
        switch (level)
        {
            case Level.Warning:
                GD.PushWarning($"{tag}{message}");
                break;
            case Level.Error:
                GD.PushError($"{tag}{message}");
                break;
            default:
                GD.Print($"{tag}{message}");
                break;
        }
    }
}
