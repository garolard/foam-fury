using Godot;
using System;

/// What a server reports to the server browser.
public class ServerInfo
{
    public string Name, Motd;
    public int Map, Players, MaxPlayers, Wave, Protocol, PingMs;
    public bool Locked;
}

/// One short-lived "what's on this server?" probe. Runs its own SceneMultiplayer on this node's
/// branch so several can run in parallel without touching the main session.
public partial class ServerQuery : Node
{
    /// (info or null, error text)
    public event Action<ServerInfo, string> Done;
    public const float Timeout = 3f;

    SceneMultiplayer _mp;
    ENetMultiplayerPeer _peer;
    ulong _sent;
    float _t;
    bool _finished;
    string _host;
    int _port;

    public static ServerQuery Start(Node parent, string address)
    {
        var q = new ServerQuery();
        parent.AddChild(q);
        if (!Net.ParseAddress(address, out q._host, out q._port)) { q.Finish(null, "BAD ADDRESS"); return q; }
        q.Begin();
        return q;
    }

    void Begin()
    {
        var ip = Net.Resolve(_host);
        if (string.IsNullOrEmpty(ip)) { Finish(null, "UNKNOWN HOST"); return; }
        _mp = new SceneMultiplayer { AuthTimeout = Timeout };
        _peer = new ENetMultiplayerPeer();
        if (_peer.CreateClient(ip, _port, Net.Channels) != Error.Ok) { Finish(null, "OFFLINE"); return; }
        GetTree().SetMultiplayer(_mp, GetPath());
        _mp.AuthCallback = Callable.From<long, byte[]>(OnAuth);
        _mp.PeerAuthenticating += id =>
        {
            _sent = Time.GetTicksMsec();
            _mp.SendAuth((int)id, NetGame.Hello(true, "", 0, ""));
        };
        _mp.ConnectionFailed += () => Finish(null, "OFFLINE");
        _mp.MultiplayerPeer = _peer;
    }

    void OnAuth(long id, byte[] data)
    {
        var r = new NetReader(data);
        if (r.B() != NetGame.AuthInfo) { Finish(null, "BAD REPLY"); return; }
        var info = NetGame.ReadInfo(r);
        info.PingMs = (int)(Time.GetTicksMsec() - _sent);
        Finish(info, info.Protocol != Net.Protocol ? "VERSION MISMATCH" : null);
    }

    public override void _Process(double delta)
    {
        if (_finished) return;
        _t += (float)delta;
        if (_t > Timeout) Finish(null, "OFFLINE");
    }

    void Finish(ServerInfo info, string error)
    {
        if (_finished) return;
        _finished = true;
        // deferred: Start() can fail before the caller has subscribed to Done
        Callable.From(() => { if (!IsInstanceValid(this) || !IsInsideTree()) return; Done?.Invoke(info, error); Cleanup(); }).CallDeferred();
    }

    void Cleanup()
    {
        if (_peer == null && _mp == null) { QueueFree(); return; }
        _peer?.Close();
        _peer = null;
        if (_mp != null && IsInsideTree()) GetTree().SetMultiplayer(null, GetPath());
        _mp = null;
        QueueFree();
    }

    // the menu went away mid-probe (we joined a server): hang up
    public override void _ExitTree()
    {
        _finished = true;
        _peer?.Close();
        _peer = null;
        if (_mp != null) GetTree().SetMultiplayer(null, GetPath());
        _mp = null;
    }
}
