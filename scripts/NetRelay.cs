using Godot;

namespace RTSGame;

/// <summary>
/// 网络消息中转节点 — 挂在场景树上，用RPC方法接收/转发网络消息。
/// 由NetworkManager统一管理，不直接被游戏逻辑调用。
/// </summary>
public partial class NetRelay : Node
{
    public static NetRelay? Instance { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        // 确保这个节点的Multiplayer走SceneMultiplayer
        SetMultiplayerAuthority(1, true);
    }

    /// <summary>
    /// 接收到网络消息的RPC入口（所有peer都会调用）。
    /// 由发送方通过 RpcId 调用。
    /// </summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ReceivePacket(byte[] data, int fromPeer)
    {
        NetworkManager.HandlePacket(fromPeer, data);
    }

    /// <summary>
    /// 通过RPC发送消息到指定peer（或所有peer）。
    /// </summary>
    public void SendPacket(int targetPeer, byte[] data)
    {
        if (targetPeer == 0)
        {
            // 广播给所有人（包括自己，CallLocal=true）
            Rpc("ReceivePacket", data, NetworkManager.LocalPeerId);
        }
        else if (targetPeer == NetworkManager.LocalPeerId)
        {
            // 给自己
            ReceivePacket(data, NetworkManager.LocalPeerId);
        }
        else
        {
            RpcId(targetPeer, "ReceivePacket", data, NetworkManager.LocalPeerId);
        }
    }
}
