using Godot;
using System;
using System.Collections.Generic;

/// Client half: connecting, streaming our player to the server, applying the server's world.
public partial class NetGame
{
    /// Let in (to the lobby): the server's LobbyPhase. The Lobby message follows once the handshake completes.
    public event Action<int> Accepted;
    /// Couldn't connect / refused / timed out (reason).
    public event Action<string> Failed;
    public int MyId;

    bool _connecting;
    /// Handshake finished: RPCs to the server work from here on.
    bool _live;
    float _connectT;
    string _rejectReason;

    public void Connect(string address, string name, int character, string password)
    {
        if (!Net.ParseAddress(address, out var host, out var port)) { Fail("BAD ADDRESS"); return; }
        var ip = Net.Resolve(host);
        if (string.IsNullOrEmpty(ip)) { Fail($"CAN'T FIND {host.ToUpper()}"); return; }
        _rejectReason = null;
        ClearLobby();
        _mp = new SceneMultiplayer { AuthTimeout = 8 };
        Tree.SetMultiplayer(_mp);
        _mp.AuthCallback = Callable.From<long, byte[]>(ClientAuth);
        _mp.PeerAuthenticating += id => _mp?.SendAuth((int)id, Hello(false, name, character, password));
        _mp.PeerAuthenticationFailed += _ => Fail(_rejectReason ?? "CONNECTION REFUSED");
        _mp.ConnectionFailed += () => Fail("COULD NOT REACH THE SERVER");
        _mp.ServerDisconnected += OnServerLost;
        _mp.ConnectedToServer += () =>
        {
            _live = true;
            // notice a dead server in seconds, not ENet's default half minute
            _peer?.GetPeer(1)?.SetTimeout(32, 3000, 8000);
        };
        _live = false;
        _peer = new ENetMultiplayerPeer();
        if (_peer.CreateClient(ip, port, Net.Channels) != Error.Ok) { Fail("COULD NOT REACH THE SERVER"); return; }
        _mp.MultiplayerPeer = _peer;
        _connecting = true;
        _connectT = 0;
        GD.Print($"[net] connecting to {host}:{port} ({ip})");
    }

    void ConnectTimeout(float dt)
    {
        _connectT += dt;
        if (_connectT > 8f) Fail("SERVER DID NOT ANSWER");
    }

    void Fail(string reason)
    {
        if (Net.IsClient) return; // already in the game: ServerDisconnected handles it
        _connecting = false;
        GD.Print($"[net] connection failed: {reason}");
        Callable.From(() => { Close(); Failed?.Invoke(reason); }).CallDeferred();
    }

    void ClientAuth(long id, byte[] data)
    {
        var r = new NetReader(data);
        switch (r.B())
        {
            case AuthAccept:
            {
                int phase = r.B(), map = r.I();
                _connecting = false;
                Net.Mode = NetMode.Client;
                MyId = _mp.GetUniqueId();
                Phase = (LobbyPhase)phase;
                HoverMap = map;
                GD.Print($"[net] joined as peer {MyId} · {Phase}");
                Accepted?.Invoke(phase);
                _mp.CompleteAuth((int)id);
                break;
            }
            case AuthReject:
                _rejectReason = r.S();
                Fail(_rejectReason);
                break;
        }
    }

    void OnServerLost()
    {
        if (!Net.IsClient) return;
        GD.Print("[net] lost connection to the server");
        GameData.NetMessage = "LOST CONNECTION TO THE SERVER";
        Callable.From(() => M?.LeaveServer()).CallDeferred();
    }

    void ClientTick()
    {
        // 30 Hz own-player state
        if (!_live || _tick % 2 != 0) return;
        var p = M?.LocalPlayer;
        if (p == null) return;
        SendRaw(1, new NetWriter(Msg.State).V(p.GlobalPosition).V(p.Velocity).Yaw(p.FacingYaw).V(p.AimPoint).B(p.NetFlags).B(p.WeaponIndex).Bytes(), 1);
    }

    // ------------------------------------------------------------------ outgoing (client -> server)
    void ToServer(NetWriter w, int ch = 0) { if (Net.IsClient && _live) SendRaw(1, w.Bytes(), ch); }

    public void SendFired(int weapon, Vector3 muzzle, List<Vector3> dirs)
    {
        var w = new NetWriter(Msg.Fired).B(weapon).V(muzzle).B(dirs.Count);
        foreach (var d in dirs) w.V(d);
        ToServer(w, 1);
    }

    public void SendHit(Zombie z, float dmg, Vector3 pos, Vector3 dir, bool head, float knock, bool explosive, bool minor) =>
        ToServer(new NetWriter(Msg.Hit).I(z.NetId).F(dmg).V(pos).V(dir).B((head ? 1 : 0) | (explosive ? 2 : 0) | (minor ? 4 : 0)).F(knock));
    public void SendSlow(Zombie z, float mul, float time) => ToServer(new NetWriter(Msg.Slow).I(z.NetId).F(mul).F(time));
    public void SendPinata(Zombie z) => ToServer(new NetWriter(Msg.Pinata).I(z.NetId));
    public void SendMudLanded(Vector3 pos) => ToServer(new NetWriter(Msg.MudLanded).V(pos));
    public void SendGrenade(Vector3 pos, Vector3 vel, int type) => ToServer(new NetWriter(Msg.Grenade).V(pos).V(vel).I(type));
    public void SendTakePickup(Pickup p) => ToServer(new NetWriter(Msg.TakePickup).I(p.NetId));
    public void SendReady() => ToServer(new NetWriter(Msg.Ready));

    // ------------------------------------------------------------------ incoming (server -> client)
    static void ReadGameState(NetReader r)
    {
        var m = M;
        m.Score = r.I(); m.ComboMult = r.B(); m.WaveNumber = r.I();
        m.NetZombiesLeft = r.I(); m.NetBreakTimer = r.F();
        r.B(); // server mode (run state changes arrive as RunStart / RunOver)
    }

    void ReadPlayerFull(NetReader r)
    {
        int id = r.I(), ch = r.B();
        string name = r.S();
        bool benched = r.Bool(), downed = r.Bool(), dead = r.Bool();
        var pos = r.V();
        float hp = r.F();
        int kills = r.I();
        var main = M;
        var p = main.PlayerById(id);
        if (id == MyId && p != null)
        {
            p.NetName = name;
            p.SetBenched(benched);
            if (!benched) p.Teleport(pos);
        }
        else if (p == null) p = main.AddPuppet(id, ch, name, benched, pos);
        p.Hp = hp; p.Kills = kills;
        if (id != MyId)
        {
            if (dead) p.NetDie(Vector3.Forward);
            else if (downed) p.NetGoDown();
        }
    }

    void ClientMsg(Msg m, NetReader r)
    {
        var main = M;
        switch (m)
        {
            // lobby traffic works with no world (and while Main reloads)
            case Msg.Lobby: ClientLobbyMsg(r); return;
            case Msg.LobbyHover: HoverMap = r.B(); HoverChanged?.Invoke(HoverMap); return;
            case Msg.Enter: main?.ClientEnterMatch(r.B()); return;
            case Msg.ToLobby: main?.ClientToLobby(); return;
        }
        // world traffic only means something once we're in the match
        if (main == null || main.Mode == Main.State.Menu) return;
        switch (m)
        {
            case Msg.Welcome:
            {
                ReadGameState(r);
                int np = r.B();
                for (int i = 0; i < np; i++) ReadPlayerFull(r);
                int nz = r.I();
                for (int i = 0; i < nz; i++) ReadZombie(r);
                int npk = r.I();
                for (int i = 0; i < npk; i++) Pickup.SpawnReplica(r.I(), (PickupKind)r.B(), r.V(), r.V());
                int npd = r.I();
                for (int i = 0; i < npd; i++) MudPuddle.SpawnReplica(r.I(), r.V(), r.F(), r.Bool());
                if (main.LocalPlayer is { Benched: true }) main.Banner("SPECTATING", "you'll join the fight when this wave is cleared  ·  START to change survivor", 4f);
                break;
            }
            case Msg.Snapshot:
            {
                ReadGameState(r);
                int np = r.B();
                double now = Interp.Now;
                for (int i = 0; i < np; i++)
                {
                    int id = r.I();
                    var pos = r.V(); var vel = r.V(); float yaw = r.Yaw(); var aim = r.V();
                    int flags = r.B(), weapon = r.B();
                    float hp = r.F(), bleed = r.F(), revive = r.F();
                    int kills = r.I();
                    var p = main.PlayerById(id);
                    if (p == null) continue;
                    p.Hp = hp; p.BleedOut = bleed; p.ReviveProgress = revive; p.Kills = kills;
                    if (p.NetRole == Player.Role.Remote) p.PushNetState(now, pos, vel, yaw, aim, flags, weapon);
                }
                int nz = r.I();
                for (int i = 0; i < nz; i++)
                {
                    int id = r.I();
                    var pos = r.V(); float yaw = r.Yaw();
                    int flags = r.B(), windup = r.B(), strikes = r.B();
                    NetIds.Get<Zombie>(id)?.PushNetState(now, pos, yaw, flags, windup, strikes);
                }
                break;
            }
            case Msg.PlayerJoined:
            {
                ReadPlayerFull(r);
                break;
            }
            case Msg.PlayerLeft:
                main.RemovePuppet(r.I());
                break;
            case Msg.PlayerSpawned:
            {
                var p = main.PlayerById(r.I());
                var pos = r.V(); float hp = r.F();
                if (p == null) break;
                p.SetBenched(false);
                p.Teleport(pos);
                p.Hp = hp;
                break;
            }
            case Msg.PlayerHurt:
            {
                var p = main.PlayerById(r.I());
                float dmg = r.F(); var dir = r.V(); float knock = r.F();
                p?.HurtFeedback(dmg, dir, knock);
                break;
            }
            case Msg.PlayerDowned:
                main.PlayerById(r.I())?.NetGoDown();
                break;
            case Msg.PlayerDied:
            {
                var p = main.PlayerById(r.I());
                var dir = r.V();
                p?.NetDie(dir);
                break;
            }
            case Msg.PlayerRevived:
            {
                var p = main.PlayerById(r.I());
                float hp = r.F(); bool moved = r.Bool(); var pos = r.V();
                if (p == null) break;
                p.Revive(0.4f);
                p.Hp = hp;
                if (moved) p.Teleport(pos);
                break;
            }
            case Msg.ZombieSpawned:
                ReadZombie(r);
                break;
            case Msg.ZombieHit:
            {
                var z = NetIds.Get<Zombie>(r.I());
                var pos = r.V(); var dir = r.V(); float dmg = r.F(); int f = r.B();
                z?.HitFeedback(pos, dir, dmg, (f & 1) != 0, (f & 4) != 0, (f & 2) != 0);
                break;
            }
            case Msg.ZombieDied:
            {
                var z = NetIds.Get<Zombie>(r.I());
                var dir = r.V(); bool head = r.Bool(); var kind = (DeathKind)r.B(); int killer = r.I();
                z?.DieReplica(dir, head, kind, main.PlayerById(killer));
                break;
            }
            case Msg.PickupSpawned:
                Pickup.SpawnReplica(r.I(), (PickupKind)r.B(), r.V(), r.V());
                break;
            case Msg.PickupTaken:
            {
                int id = r.I(), peer = r.I();
                var kind = (PickupKind)r.B();
                NetIds.Get<Pickup>(id)?.QueueFree();
                if (peer == MyId) main.LocalPlayer?.ApplyCollect(kind, false, true);
                break;
            }
            case Msg.PickupGone:
                NetIds.Get<Pickup>(r.I())?.QueueFree();
                break;
            case Msg.GrenadeSpawned:
                Grenade.SpawnReplica(r.I(), r.V(), r.V(), r.V(), r.I());
                break;
            case Msg.GrenadeExploded:
                Grenade.ExplodeReplica(r.I(), r.V(), r.F(), r.I());
                break;
            case Msg.PinataSpawned:
                Pinata.SpawnReplica(r.I(), r.V(), r.F(), r.F(), r.F());
                break;
            case Msg.PinataExploded:
                Pinata.ExplodeReplica(r.I(), r.V(), r.F());
                break;
            case Msg.PuddleSpawned:
                MudPuddle.SpawnReplica(r.I(), r.V(), r.F(), r.Bool());
                break;
            case Msg.PuddleGrew:
            {
                var p = NetIds.Get<MudPuddle>(r.I());
                float radius = r.F();
                p?.SetRadius(radius);
                break;
            }
            case Msg.PuddleIgnited:
                NetIds.Get<MudPuddle>(r.I())?.Ignite();
                break;
            case Msg.PuddleRemoved:
                NetIds.Get<MudPuddle>(r.I())?.Remove();
                break;
            case Msg.Banner:
                main.Banner(r.S(), r.S(), r.F());
                break;
            case Msg.Toast:
            {
                string text = r.S();
                main.Toast(text, new Color(r.F(), r.F(), r.F()));
                break;
            }
            case Msg.Score:
            {
                int total = r.I(); var at = r.V(); bool combo = r.Bool();
                Fx.Number(at, $"+{total}", combo ? new Color(1f, 0.55f, 0.3f) : new Color(0.7f, 1f, 0.6f), 1.1f);
                break;
            }
            case Msg.Sound:
                Sfx.Play(r.S(), r.F());
                break;
            case Msg.RunStart:
            {
                int n = r.B();
                var spots = new List<(int id, Vector3 pos, bool benched)>();
                for (int i = 0; i < n; i++) spots.Add((r.I(), r.V(), r.Bool()));
                main.ClientRunStart(spots);
                break;
            }
            case Msg.RunOver:
                main.ClientRunOver(r.S(), r.S(), r.F());
                break;
            case Msg.FiredRelay:
            {
                var p = main.PlayerById(r.I());
                int w = r.B(); var muzzle = r.V(); int n = r.B();
                var dirs = new Vector3[n];
                for (int i = 0; i < n; i++) dirs[i] = r.V();
                p?.RemoteFired(w, muzzle, dirs);
                break;
            }
        }
    }

    static void ReadZombie(NetReader r)
    {
        int id = r.I();
        var type = (ZombieType)r.B();
        int wave = r.I();
        bool rise = r.Bool();
        int skin = r.B();
        var pos = r.V();
        float yaw = r.Yaw();
        if (NetIds.Get<Zombie>(id) != null) return;
        Zombie.SpawnReplica(id, type, wave, rise, skin, pos, yaw);
    }
}
