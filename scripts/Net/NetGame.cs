using Godot;
using System;

/// The online session, a child of the root named "Net" (same node path /root/Net on every peer).
/// It sits beside Main, not under it, so a client stays connected while Main reloads (match -> lobby).
/// Every message is a byte[] whose first byte is a Msg id, sent through one of three RPCs:
/// R reliable events, U unreliable player traffic, S unreliable world snapshots.
public partial class NetGame : Node
{
    public const int AuthHello = 1, AuthInfo = 10, AuthAccept = 11, AuthReject = 12;

    SceneMultiplayer _mp;
    ENetMultiplayerPeer _peer;
    int _tick;

    public NetGame() { ProcessMode = ProcessModeEnum.Always; }
    public override void _EnterTree() => Net.Game = this;
    public override void _ExitTree() { if (Net.Game == this) Net.Game = null; }

    /// Null for the frame or so while a client's Main reloads.
    static Main M => GodotObject.IsInstanceValid(Main.Instance) ? Main.Instance : null;
    /// Usable before this node has entered the tree (it is added deferred).
    static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

    // ------------------------------------------------------------------ auth payloads
    public static byte[] Hello(bool query, string name, int character, string password) =>
        new NetWriter().B(AuthHello).I(Net.Protocol).Bool(query).S(name).I(character).S(password).Bytes();

    public static ServerInfo ReadInfo(NetReader r) => new()
    {
        Name = r.S(), Motd = r.S(), Map = r.I(), Players = r.I(), MaxPlayers = r.I(), Locked = r.Bool(), Wave = r.I(), Protocol = r.I(),
    };

    // ------------------------------------------------------------------ transport
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 0)]
    void R(byte[] d) => Handle(d);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    void U(byte[] d) => Handle(d);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void S(byte[] d) => Handle(d);

    void Handle(byte[] d)
    {
        if (d == null || d.Length == 0) return;
        int from = Multiplayer.GetRemoteSenderId();
        var r = new NetReader(d);
        var m = (Msg)r.B();
        try
        {
            if (Net.IsServer) ServerMsg(from, m, r);
            else if (Net.IsClient && from == 1) ClientMsg(m, r);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[net] bad {m} from {from}: {e.Message}");
        }
    }

    void SendRaw(int peer, byte[] b, int ch)
    {
        if (_mp?.MultiplayerPeer == null) return;
        switch (ch)
        {
            case 1: RpcId(peer, MethodName.U, b); break;
            case 2: RpcId(peer, MethodName.S, b); break;
            default: RpcId(peer, MethodName.R, b); break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Net.Online || _mp?.MultiplayerPeer == null) return;
        _tick++;
        if (Net.IsServer) ServerTick();
        else ClientTick();
    }

    public override void _Process(double delta)
    {
        if (_connecting) ConnectTimeout((float)delta);
    }

    /// Hang up and forget the session (the next scene starts from a clean SceneMultiplayer).
    public void Close()
    {
        _connecting = false;
        _live = false;
        _peer?.Close();
        _peer = null;
        if (_mp != null) _mp.MultiplayerPeer = null;
        _mp = null;
        Tree.SetMultiplayer(new SceneMultiplayer());
        Net.Mode = NetMode.Offline;
        ClearLobby();
    }
}
