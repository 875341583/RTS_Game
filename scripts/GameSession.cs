namespace RTSGame;

/// <summary>
/// 全局游戏会话：在主菜单和游戏场景间传递难度选择、地图种子和地图尺寸。
/// 纯静态类，场景切换由调用方节点执行。
/// </summary>
public static class GameSession
{
    /// <summary>菜单选中的难度，默认 Normal。游戏场景 _Ready 时读取。</summary>
    public static Main.Difficulty SelectedDifficulty { get; set; } = Main.Difficulty.Normal;

    /// <summary>地图种子。0=随机生成（由 Main._Ready 处理）。可由主菜单输入或 --seed 命令行传入。</summary>
    public static ulong MapSeed { get; set; } = 0;

    /// <summary>P2-2: 地图尺寸预设。默认 Small(32)。游戏场景 _Ready 时读取并调用 MapConfig.SetSize()。</summary>
    public static MapConfig.SizePreset SelectedMapSize { get; set; } = MapConfig.SizePreset.Medium;

    /// <summary>P2-2: 地图主题。默认 Default（混合地形）。</summary>
    public static MapConfig.MapTheme SelectedMapTheme { get; set; } = MapConfig.MapTheme.Default;

    /// <summary>P1-2: 玩家选择的阵营ID。默认 "Allies"（同盟军）。游戏场景 _Ready 时 FactionManager 已加载，用于设置玩家阵营。</summary>
    public static string PlayerFactionId { get; set; } = "Allies";

    /// <summary>是否处于联机模式（由 NetworkManager 设置）。</summary>
    public static bool IsMultiplayer { get; set; } = false;
}
