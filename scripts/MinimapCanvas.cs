using Godot;

namespace RTSGame;

/// <summary>
/// 小地图Canvas — 自定义Control，在_Draw中调用Main3D.DrawMinimap绘制地形/建筑/单位点。
/// 每帧通过QueueRedraw触发重绘。
/// </summary>
public partial class MinimapCanvas : Control
{
    private Main3D _game;

    public MinimapCanvas(Main3D game)
    {
        _game = game;
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        _game.DrawMinimap(this);
    }
}
