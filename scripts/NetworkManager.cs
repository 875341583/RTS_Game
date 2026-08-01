using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace RTSGame;

/// <summary>
/// 联机模式管理器 — Godot ENet 高层封装。
/// 架构：Host权威 + 命令广播
///   - Host（房主）运行业务逻辑（经济、生产、战斗、AI）
///   - 客户端发送操作命令到Host，Host确认后广播给所有客户端
///   - 复用回放系统的ActionType作为网络命令格式
///   - 状态同步：Host定期广播关键状态快照
/// </summary>
public static class NetworkManager
{
    // ====== 联机会话状态 ======

    /// <summary>是否处于联机模式。</summary>
    public static bool IsOnline => _role != NetRole.Offline;

    /// <summary>网络角色。</summary>
    public static NetRole Role => _role;
    private static NetRole _role = NetRole.Offline;

    /// <summary>本机玩家的TeamId（由Host分配）。</summary>
    public static int LocalTeamId { get; private set; } = 0;

    /// <summary>本机玩家的PeerId。</summary>
    public static int LocalPeerId { get; private set; } = 1;

    /// <summary>当前房间配置。</summary>
    public static RoomConfig Room { get; private set; } = new();

    /// <summary>房间内所有玩家信息（PeerId → PlayerSlot）。</summary>
    public static Dictionary<int, PlayerSlot> Players { get; } = new();

    /// <summary>当前模式下的AI阵营数量 = 总槽位 - 真人玩家数。</summary>
    public static int AiTeamCount => Room.MaxPlayers - GetHumanPlayerCount();

    /// <summary>联机是否已进入游戏（非大厅状态）。</summary>
    public static bool InGame { get; set; }

    // ====== Godot multiplayer API ======

    private static SceneMultiplayer _mp = null!;
    private static ENetMultiplayerPeer _peer = null!;

    // ====== 事件回调 ======

    /// <summary>大厅玩家列表变更时触发（UI刷新用）。</summary>
    public static event Action? LobbyChanged;
    /// <summary>Host开始游戏时触发（所有客户端切换场景）。</summary>
    public static event Action? GameStarted;
    /// <summary>连接断开时触发。</summary>
    public static event Action<string>? Disconnected;

    // ====== 网络消息类型（1字节标识） ======

    public enum MsgType : byte
    {
        // 大厅消息（101-120）
        LobbyInfo = 101,        // Host→所有：广播大厅完整状态
        JoinRequest = 102,      // Client→Host：请求加入
        JoinAck = 103,          // Host→Client：加入确认（含分配的TeamId）
        KickPlayer = 104,       // Host→Client：踢出
        StartGame = 105,        // Host→所有：开始游戏（含种子/配置）
        ReadyToggle = 106,      // Client→Host：切换准备状态
        ChatMessage = 107,      // 任意→所有：大厅聊天

        // 游戏内消息（201-255）
        PlayerCommand = 201,    // Client→Host：玩家操作命令
        CommandBroadcast = 202, // Host→所有：广播确认的命令
        StateSnapshot = 203,    // Host→所有：状态快照
        GameOver = 204,         // Host→所有：游戏结束
    }

    // ====== 初始化（在MainMenu._Ready中调用） ======

    /// <summary>初始化SceneMultiplayer（在主菜单加载时调用）。</summary>
    public static void Init()
    {
        if (_mp != null) return;
        _mp = new SceneMultiplayer();
        _mp.PeerConnected += OnPeerConnected;
        _mp.PeerDisconnected += OnPeerDisconnected;

        // 创建NetRelay节点（用普通Node而非Godot Node，需要手动创建C#对象）
        // NetRelay必须是场景树中的节点才能使用RPC
        var sceneTree = Engine.GetMainLoop() as SceneTree;
        if (sceneTree != null && sceneTree.Root.GetNodeOrNull<NetRelay>("NetRelay") == null)
        {
            var relay = new NetRelay();
            relay.Name = "NetRelay";
            sceneTree.Root.CallDeferred(Node.MethodName.AddChild, relay);
        }

        GD.Print("[Net] NetworkManager 已初始化");
    }

    // ====== Host：创建房间 ======

    /// <summary>作为Host创建房间。</summary>
    public static bool CreateRoom(RoomConfig config)
    {
        try
        {
            Init();
            Room = config;
            _role = NetRole.Host;
            LocalPeerId = 1;
            LocalTeamId = 0;

            _peer = new ENetMultiplayerPeer();
            Error err = _peer.CreateServer(config.Port, config.MaxPlayers - 1); // 减1因为Host自己占一个槽
            if (err != Error.Ok)
            {
                GD.PrintErr($"[Net] 创建服务器失败: {err}");
                _role = NetRole.Offline;
                return false;
            }

            _mp.MultiplayerPeer = _peer;
            var st = Engine.GetMainLoop() as SceneTree;
            st?.SetMultiplayer(_mp);

            // Host自己作为PlayerSlot 0
            Players.Clear();
            Players[1] = new PlayerSlot
            {
                PeerId = 1,
                TeamId = 0,
                Name = config.HostName,
                Faction = config.HostFaction,
                IsReady = true,
                IsHost = true,
                IsAI = false
            };

            // 填充AI槽位
            FillAISlots();

            GD.Print($"[Net] 房间已创建 — 端口{config.Port} 模式{config.MaxPlayers}人");
            LobbyChanged?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] 创建房间异常: {e.Message}");
            _role = NetRole.Offline;
            return false;
        }
    }

    // ====== Client：加入房间 ======

    /// <summary>作为Client加入房间。</summary>
    public static bool JoinRoom(string ip, int port, string playerName, string faction)
    {
        try
        {
            Init();
            _role = NetRole.Client;

            _peer = new ENetMultiplayerPeer();
            Error err = _peer.CreateClient(ip, port);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[Net] 连接服务器失败: {err}");
                _role = NetRole.Offline;
                return false;
            }

            _mp.MultiplayerPeer = _peer;
            var st2 = Engine.GetMainLoop() as SceneTree;
            st2?.SetMultiplayer(_mp);

            // 存储待发送的加入请求信息
            _pendingJoinName = playerName;
            _pendingJoinFaction = faction;
            LocalPeerId = _mp.GetUniqueId();

            GD.Print($"[Net] 正在连接 {ip}:{port} ...");
            return true;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] 加入房间异常: {e.Message}");
            _role = NetRole.Offline;
            return false;
        }
    }

    private static string _pendingJoinName = "";
    private static string _pendingJoinFaction = "Allies";

    // ====== 断开连接 ======

    /// <summary>断开网络连接，回到离线状态。</summary>
    public static void Disconnect()
    {
        if (_peer != null)
        {
            _peer.Close();
            _peer = null!;
        }
        if (_mp != null)
        {
            _mp.MultiplayerPeer = null;
        }
        _role = NetRole.Offline;
        InGame = false;
        Players.Clear();
        LocalTeamId = 0;
        LocalPeerId = 1;
        GD.Print("[Net] 已断开连接");
        Disconnected?.Invoke("用户主动断开");
    }

    // ====== 每帧处理（由Main._Process调用） ======

    /// <summary>联机模式下的每帧处理（由Main或MainMenu调用）。</summary>
    public static void Poll()
    {
        if (!IsOnline) return;

        // Client: 连接成功后发送JoinRequest
        if (_role == NetRole.Client && _peer?.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected
            && !Players.ContainsKey(LocalPeerId) && !string.IsNullOrEmpty(_pendingJoinName))
        {
            SendJoinRequest(_pendingJoinName, _pendingJoinFaction);
            _pendingJoinName = ""; // 只发一次
        }

        // Host: 定期广播大厅状态（每0.5秒）
        _lobbyBroadcastTimer += 0.016f;
        if (_role == NetRole.Host && !InGame && _lobbyBroadcastTimer > 0.5f)
        {
            _lobbyBroadcastTimer = 0;
            BroadcastLobbyInfo();
        }

        // Host: 游戏中定期广播状态快照（每0.1秒）
        if (_role == NetRole.Host && InGame)
        {
            _snapshotTimer += 0.016f;
            if (_snapshotTimer > 0.1f)
            {
                _snapshotTimer = 0;
                // 状态快照由Main.GameSync.cs提供数据
                SnapshotData?.Invoke();
            }
        }
    }

    private static float _lobbyBroadcastTimer;
    private static float _snapshotTimer;

    /// <summary>Host回调：收集状态快照数据（由Main.GameSync.cs设置）。</summary>
    public static event Action? SnapshotData;

    // ====== Peer事件 ======

    private static void OnPeerConnected(long peerId)
    {
        GD.Print($"[Net] Peer {peerId} 已连接");
        // Client在连接成功后自己发JoinRequest，Host不需要主动发
    }

    private static void OnPeerDisconnected(long peerId)
    {
        GD.Print($"[Net] Peer {peerId} 已断开");
        if (_role == NetRole.Host)
        {
            // 移除该玩家，用AI填充
            Players.Remove((int)peerId);
            FillAISlots();
            LobbyChanged?.Invoke();
        }
        else if (_role == NetRole.Client && peerId == 1)
        {
            // Host断开了
            Disconnect();
            Disconnected?.Invoke("与房主断开连接");
        }
    }

    // ====== 玩家槽位管理 ======

    /// <summary>获取下一个空闲的TeamId。</summary>
    private static int GetNextFreeTeamId()
    {
        var used = new HashSet<int>();
        foreach (var p in Players.Values)
            used.Add(p.TeamId);
        for (int i = 0; i < Room.MaxPlayers; i++)
            if (!used.Contains(i)) return i;
        return -1;
    }

    /// <summary>用AI填充空槽位。</summary>
    private static void FillAISlots()
    {
        if (_role != NetRole.Host) return;

        // 先清除旧的AI槽
        var toRemove = new List<int>();
        foreach (var kv in Players)
            if (kv.Value.IsAI) toRemove.Add(kv.Key);
        foreach (var k in toRemove) Players.Remove(k);

        // 检查是否需要AI填充（只有Host点了"用AI填充"才加）
        if (!_fillWithAI) return;

        // 为每个空TeamId添加AI
        for (int teamId = 0; teamId < Room.MaxPlayers; teamId++)
        {
            bool hasPlayer = false;
            foreach (var p in Players.Values)
                if (p.TeamId == teamId) { hasPlayer = true; break; }
            if (!hasPlayer)
            {
                int aiPeerId = -(teamId + 100); // 负数PeerId表示AI
                Players[aiPeerId] = new PlayerSlot
                {
                    PeerId = aiPeerId,
                    TeamId = teamId,
                    Name = $"AI {teamId}",
                    Faction = "Allies",
                    IsReady = true,
                    IsHost = false,
                    IsAI = true
                };
            }
        }
    }

    /// <summary>Host手动切换"用AI填充"开关。</summary>
    public static bool _fillWithAI = false;

    /// <summary>Host切换AI填充模式。</summary>
    public static void ToggleFillAI()
    {
        if (_role != NetRole.Host) return;
        _fillWithAI = !_fillWithAI;
        FillAISlots();
        LobbyChanged?.Invoke();
    }

    /// <summary>获取当前真人玩家数量。</summary>
    public static int GetHumanPlayerCount()
    {
        int count = 0;
        foreach (var p in Players.Values)
            if (!p.IsAI) count++;
        return count;
    }

    /// <summary>Client切换准备状态。</summary>
    public static void ToggleReady()
    {
        if (_role != NetRole.Client) return;
        if (Players.TryGetValue(LocalPeerId, out var slot))
            slot.IsReady = !slot.IsReady;
        // 发送ReadyToggle消息
        var data = new { peerId = LocalPeerId, ready = Players[LocalPeerId].IsReady };
        SendToHost(MsgType.ReadyToggle, JsonSerializer.Serialize(data));
    }

    // ====== Host：开始游戏 ======

    /// <summary>Host调用：开始游戏（广播StartGame消息）。</summary>
    public static void HostStartGame(ulong seed, Main.Difficulty difficulty,
        MapConfig.SizePreset mapSize, MapConfig.MapTheme theme)
    {
        if (_role != NetRole.Host) return;

        var startInfo = new StartGameInfo
        {
            Seed = seed,
            Difficulty = difficulty,
            MapSize = mapSize,
            MapTheme = theme,
            MaxPlayers = Room.MaxPlayers,
            Players = new List<PlayerSlotInfo>()
        };

        foreach (var p in Players.Values)
        {
            startInfo.Players.Add(new PlayerSlotInfo
            {
                PeerId = p.PeerId,
                TeamId = p.TeamId,
                Name = p.Name,
                Faction = p.Faction,
                IsAI = p.IsAI,
                IsHost = p.IsHost
            });
        }

        string json = JsonSerializer.Serialize(startInfo);
        BroadcastAll(MsgType.StartGame, json);

        InGame = true;
        // 自己也进入游戏
        GameStarted?.Invoke();
    }

    // ====== 网络消息发送 ======

    private static void SendToHost(MsgType type, string json)
    {
        if (_role != NetRole.Client) return;
        byte[] data = MakePacket(type, json);
        NetRelay.Instance?.SendPacket(1, data); // PeerId 1 = Host
    }

    private static void BroadcastAll(MsgType type, string json)
    {
        if (_role != NetRole.Host) return;
        byte[] data = MakePacket(type, json);
        NetRelay.Instance?.SendPacket(0, data); // 0 = 所有peer
    }

    private static void SendToPeer(int peerId, MsgType type, string json)
    {
        if (_role != NetRole.Host) return;
        byte[] data = MakePacket(type, json);
        NetRelay.Instance?.SendPacket(peerId, data);
    }

    private static byte[] MakePacket(MsgType type, string json)
    {
        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        byte[] data = new byte[jsonBytes.Length + 1];
        data[0] = (byte)type;
        Buffer.BlockCopy(jsonBytes, 0, data, 1, jsonBytes.Length);
        return data;
    }

    // ====== 网络消息接收（由Main._Process调用CustomMultiplayer.Processing或由引擎自动分发） ======

    /// <summary>处理收到的网络消息（由_Mp.PeerPacket或手动调用）。</summary>
    public static void HandlePacket(int fromPeer, byte[] data)
    {
        if (data.Length < 1) return;
        MsgType type = (MsgType)data[0];
        string json = System.Text.Encoding.UTF8.GetString(data, 1, data.Length - 1);

        switch (type)
        {
            case MsgType.JoinRequest:
                HandleJoinRequest(fromPeer, json);
                break;
            case MsgType.JoinAck:
                HandleJoinAck(json);
                break;
            case MsgType.LobbyInfo:
                HandleLobbyInfo(json);
                break;
            case MsgType.ReadyToggle:
                HandleReadyToggle(json);
                break;
            case MsgType.StartGame:
                HandleStartGame(json);
                break;
            case MsgType.PlayerCommand:
                HandlePlayerCommand(fromPeer, json);
                break;
            case MsgType.CommandBroadcast:
                HandleCommandBroadcast(json);
                break;
            case MsgType.StateSnapshot:
                HandleStateSnapshot(json);
                break;
            case MsgType.GameOver:
                HandleGameOver(json);
                break;
            case MsgType.ChatMessage:
                HandleChatMessage(fromPeer, json);
                break;
        }
    }

    // ====== 大厅消息处理 ======

    private static void SendJoinRequest(string name, string faction)
    {
        var req = new { name, faction };
        SendToHost(MsgType.JoinRequest, JsonSerializer.Serialize(req));
        GD.Print($"[Net] 已发送加入请求: {name} / {faction}");
    }

    private static void HandleJoinRequest(int fromPeer, string json)
    {
        if (_role != NetRole.Host) return;
        try
        {
            var req = JsonSerializer.Deserialize<JsonElement>(json);
            string name = req.GetProperty("name").GetString() ?? "Player";
            string faction = req.GetProperty("faction").GetString() ?? "Allies";

            int teamId = GetNextFreeTeamId();
            if (teamId < 0)
            {
                // 房间已满
                SendToPeer(fromPeer, MsgType.JoinAck, JsonSerializer.Serialize(new { accepted = false, reason = "房间已满" }));
                return;
            }

            Players[fromPeer] = new PlayerSlot
            {
                PeerId = fromPeer,
                TeamId = teamId,
                Name = name,
                Faction = faction,
                IsReady = false,
                IsHost = false,
                IsAI = false
            };

            FillAISlots();

            // 回复JoinAck
            var ack = new
            {
                accepted = true,
                peerId = fromPeer,
                teamId = teamId,
                roomConfig = Room
            };
            SendToPeer(fromPeer, MsgType.JoinAck, JsonSerializer.Serialize(ack, new JsonSerializerOptions { IncludeFields = true }));

            // 广播更新后的大厅信息
            BroadcastLobbyInfo();
            LobbyChanged?.Invoke();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] 处理JoinRequest异常: {e.Message}");
        }
    }

    private static void HandleJoinAck(string json)
    {
        if (_role != NetRole.Client) return;
        try
        {
            var ack = JsonSerializer.Deserialize<JsonElement>(json);
            bool accepted = ack.GetProperty("accepted").GetBoolean();
            if (!accepted)
            {
                string reason = ack.GetProperty("reason").GetString() ?? "未知原因";
                GD.PrintErr($"[Net] 加入被拒绝: {reason}");
                Disconnected?.Invoke($"加入失败: {reason}");
                Disconnect();
                return;
            }

            LocalPeerId = ack.GetProperty("peerId").GetInt32();
            LocalTeamId = ack.GetProperty("teamId").GetInt32();

            // 解析RoomConfig
            if (ack.TryGetProperty("roomConfig", out var rcElem))
            {
                Room = JsonSerializer.Deserialize<RoomConfig>(rcElem.GetRawText()) ?? new RoomConfig();
            }

            GD.Print($"[Net] 加入成功 — TeamId={LocalTeamId} PeerId={LocalPeerId}");
            LobbyChanged?.Invoke();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] 处理JoinAck异常: {e.Message}");
        }
    }

    private static void BroadcastLobbyInfo()
    {
        if (_role != NetRole.Host) return;
        var info = new LobbyInfoData
        {
            Room = Room,
            FillWithAI = _fillWithAI,
            Players = new List<PlayerSlotInfo>()
        };
        foreach (var p in Players.Values)
        {
            info.Players.Add(new PlayerSlotInfo
            {
                PeerId = p.PeerId,
                TeamId = p.TeamId,
                Name = p.Name,
                Faction = p.Faction,
                IsReady = p.IsReady,
                IsHost = p.IsHost,
                IsAI = p.IsAI
            });
        }
        BroadcastAll(MsgType.LobbyInfo, JsonSerializer.Serialize(info, new JsonSerializerOptions { IncludeFields = true }));
    }

    private static void HandleLobbyInfo(string json)
    {
        if (_role != NetRole.Client) return;
        try
        {
            var info = JsonSerializer.Deserialize<LobbyInfoData>(json, new JsonSerializerOptions { IncludeFields = true });
            if (info == null) return;

            Room = info.Room ?? new RoomConfig();
            _fillWithAI = info.FillWithAI;

            Players.Clear();
            foreach (var p in info.Players ?? new List<PlayerSlotInfo>())
            {
                Players[p.PeerId] = new PlayerSlot
                {
                    PeerId = p.PeerId,
                    TeamId = p.TeamId,
                    Name = p.Name,
                    Faction = p.Faction,
                    IsReady = p.IsReady,
                    IsHost = p.IsHost,
                    IsAI = p.IsAI
                };
            }

            LobbyChanged?.Invoke();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] 处理LobbyInfo异常: {e.Message}");
        }
    }

    private static void HandleReadyToggle(string json)
    {
        if (_role != NetRole.Host) return;
        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(json);
            int peerId = data.GetProperty("peerId").GetInt32();
            bool ready = data.GetProperty("ready").GetBoolean();
            if (Players.ContainsKey(peerId))
            {
                Players[peerId].IsReady = ready;
                BroadcastLobbyInfo();
                LobbyChanged?.Invoke();
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] 处理ReadyToggle异常: {e.Message}");
        }
    }

    private static void HandleStartGame(string json)
    {
        if (_role != NetRole.Client) return;
        try
        {
            var info = JsonSerializer.Deserialize<StartGameInfo>(json, new JsonSerializerOptions { IncludeFields = true });
            if (info == null) return;

            // 更新本地配置
            Room = new RoomConfig
            {
                MaxPlayers = info.MaxPlayers,
                Port = Room.Port,
                HostName = Room.HostName,
                HostFaction = Room.HostFaction,
                ModeName = Room.ModeName
            };

            Players.Clear();
            foreach (var p in info.Players ?? new List<PlayerSlotInfo>())
            {
                Players[p.PeerId] = new PlayerSlot
                {
                    PeerId = p.PeerId,
                    TeamId = p.TeamId,
                    Name = p.Name,
                    Faction = p.Faction,
                    IsReady = p.IsReady,
                    IsHost = p.IsHost,
                    IsAI = p.IsAI
                };
            }

            // 写入GameSession供Main._Ready读取
            GameSession.MapSeed = info.Seed;
            GameSession.SelectedDifficulty = info.Difficulty;
            GameSession.SelectedMapSize = info.MapSize;
            GameSession.SelectedMapTheme = info.MapTheme;
            GameSession.IsMultiplayer = true;

            InGame = true;
            GameStarted?.Invoke();
            GD.Print("[Net] 收到StartGame，准备进入游戏场景");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] 处理StartGame异常: {e.Message}");
        }
    }

    // ====== 游戏内命令同步 ======

    /// <summary>Client→Host：发送玩家操作命令。参数已序列化为JSON。</summary>
    public static void SendCommand(ReplayRecorder.ActionType action, string jsonParams)
    {
        if (!IsOnline) return;
        var cmd = new NetCommand
        {
            TeamId = LocalTeamId,
            Action = action,
            Params = jsonParams,
            Frame = Godot.Time.GetTicksMsec()
        };
        string json = JsonSerializer.Serialize(cmd, new JsonSerializerOptions { IncludeFields = true });

        if (_role == NetRole.Host)
        {
            // Host本地直接处理
            CommandReceived?.Invoke(cmd);
        }
        else
        {
            SendToHost(MsgType.PlayerCommand, json);
        }
    }

    private static void HandlePlayerCommand(int fromPeer, string json)
    {
        if (_role != NetRole.Host) return;
        try
        {
            var cmd = JsonSerializer.Deserialize<NetCommand>(json, new JsonSerializerOptions { IncludeFields = true });
            if (cmd == null) return;

            // Host验证命令合法性（TeamId匹配）
            if (Players.TryGetValue(fromPeer, out var slot) && slot.TeamId == cmd.TeamId)
            {
                // 本地执行
                CommandReceived?.Invoke(cmd);

                // 广播给所有客户端
                BroadcastAll(MsgType.CommandBroadcast, json);
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] 处理PlayerCommand异常: {e.Message}");
        }
    }

    private static void HandleCommandBroadcast(string json)
    {
        if (_role != NetRole.Client) return;
        try
        {
            var cmd = JsonSerializer.Deserialize<NetCommand>(json, new JsonSerializerOptions { IncludeFields = true });
            if (cmd == null) return;
            // 只处理非本地玩家的命令
            if (cmd.TeamId != LocalTeamId)
            {
                CommandReceived?.Invoke(cmd);
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] 处理CommandBroadcast异常: {e.Message}");
        }
    }

    /// <summary>命令接收回调（由Main.GameSync.cs设置）。</summary>
    public static event Action<NetCommand>? CommandReceived;

    // ====== 状态快照 ======

    private static void HandleStateSnapshot(string json)
    {
        if (_role != NetRole.Client) return;
        try
        {
            var snap = JsonSerializer.Deserialize<StateSnapshotData>(json, new JsonSerializerOptions { IncludeFields = true });
            if (snap != null)
                SnapshotReceived?.Invoke(snap);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] 处理StateSnapshot异常: {e.Message}");
        }
    }

    /// <summary>状态快照接收回调（由Main.GameSync.cs设置）。</summary>
    public static event Action<StateSnapshotData>? SnapshotReceived;

    /// <summary>Host广播状态快照。</summary>
    public static void SendSnapshot(StateSnapshotData snapshot)
    {
        if (_role != NetRole.Host || !InGame) return;
        string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { IncludeFields = true });
        BroadcastAll(MsgType.StateSnapshot, json);
    }

    // ====== 游戏结束 ======

    public static void SendGameOver(string result)
    {
        if (_role != NetRole.Host) return;
        BroadcastAll(MsgType.GameOver, JsonSerializer.Serialize(new { result }));
    }

    private static void HandleGameOver(string json)
    {
        if (_role != NetRole.Client) return;
        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(json);
            string result = data.GetProperty("result").GetString() ?? "";
            GameOverReceived?.Invoke(result);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] 处理GameOver异常: {e.Message}");
        }
    }

    /// <summary>游戏结束接收回调。</summary>
    public static event Action<string>? GameOverReceived;

    // ====== 聊天 ======

    public static void SendChat(string message)
    {
        if (!IsOnline) return;
        var data = new { sender = Players.GetValueOrDefault(LocalPeerId)?.Name ?? "??", message };
        string json = JsonSerializer.Serialize(data);
        if (_role == NetRole.Host)
        {
            BroadcastAll(MsgType.ChatMessage, json);
            ChatReceived?.Invoke(data.sender, data.message);
        }
        else
        {
            SendToHost(MsgType.ChatMessage, json);
        }
    }

    private static void HandleChatMessage(int fromPeer, string json)
    {
        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(json);
            string sender = data.GetProperty("sender").GetString() ?? "??";
            string message = data.GetProperty("message").GetString() ?? "";
            
            // Host：收到原始消息时广播给所有人；收到自己的广播回显时不重复广播
            if (_role == NetRole.Host && fromPeer != LocalPeerId)
            {
                BroadcastAll(MsgType.ChatMessage, json);
            }
            // 所有端：触发聊天回调
            ChatReceived?.Invoke(sender, message);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Net] 处理ChatMessage异常: {e.Message}");
        }
    }

    /// <summary>聊天消息接收回调。</summary>
    public static event Action<string, string>? ChatReceived;

    // ====== 数据结构 ======

    public enum NetRole { Offline, Host, Client }

    /// <summary>房间配置。</summary>
    public class RoomConfig
    {
        public int MaxPlayers { get; set; } = 3;   // 3/5/7/9/11
        public int Port { get; set; } = 25565;
        public string HostName { get; set; } = "Host";
        public string HostFaction { get; set; } = "Allies";
        public string ModeName { get; set; } = "3人模式";
        public string MapName { get; set; } = "";
    }

    /// <summary>玩家槽位信息（运行时）。</summary>
    public class PlayerSlot
    {
        public int PeerId;
        public int TeamId;
        public string Name = "";
        public string Faction = "Allies";
        public bool IsReady;
        public bool IsHost;
        public bool IsAI;
    }

    /// <summary>网络命令。</summary>
    public class NetCommand
    {
        public int TeamId;
        public ReplayRecorder.ActionType Action;
        public string Params = "";
        public ulong Frame;
    }

    /// <summary>状态快照（Host→Client定期同步）。</summary>
    public class StateSnapshotData
    {
        public ulong Timestamp;
        public int[]? Money;
        public List<UnitState>? Units;
        public List<BuildingState>? Buildings;
    }

    public class UnitState
    {
        public int TeamId;
        public int UnitType;   // UnitType enum值
        public float X, Y;
        public float Health;
        public int UnitId; // 用于匹配/插值
    }

    public class BuildingState
    {
        public int TeamId;
        public int BuildingType; // BuildingType enum值
        public float X, Y;
        public float Health;
        public int BuildingId;
    }

    // --- 内部传输结构 ---

    private class LobbyInfoData
    {
        public RoomConfig? Room { get; set; }
        public bool FillWithAI { get; set; }
        public List<PlayerSlotInfo>? Players { get; set; }
    }

    private class PlayerSlotInfo
    {
        public int PeerId { get; set; }
        public int TeamId { get; set; }
        public string Name { get; set; } = "";
        public string Faction { get; set; } = "Allies";
        public bool IsReady { get; set; }
        public bool IsHost { get; set; }
        public bool IsAI { get; set; }
    }

    private class StartGameInfo
    {
        public ulong Seed { get; set; }
        public Main.Difficulty Difficulty { get; set; }
        public MapConfig.SizePreset MapSize { get; set; }
        public MapConfig.MapTheme MapTheme { get; set; }
        public int MaxPlayers { get; set; }
        public List<PlayerSlotInfo>? Players { get; set; }
    }
}
