namespace RTSGame;

/// <summary>
/// 全局游戏会话：在主菜单和游戏场景间传递难度选择和地图种子。
/// 纯静态类，场景切换由调用方节点执行。
/// </summary>
public static class GameSession
{
    /// <summary>菜单选中的难度，默认 Normal。游戏场景 _Ready 时读取。</summary>
    public static Main.Difficulty SelectedDifficulty { get; set; } = Main.Difficulty.Normal;

    /// <summary>地图种子。0=随机生成（由 Main._Ready 处理）。可由主菜单输入或 --seed 命令行传入。</summary>
    public static ulong MapSeed { get; set; } = 0;
}
