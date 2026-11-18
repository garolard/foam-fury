using Godot;
using System.Collections.Generic;

/// Dedicated server half: auth, joins/leaves, validating client reports, broadcasting world state.
public partial class NetGame
{
    public ServerConfig Cfg;
    /// Peers in the running match (world traffic goes to them only). Everyone connected is in Entries.
    readonly HashSet<int> _joined = new();
    readonly HashSet<int> _ready = new();
    readonly Dictionary<int, (string name, int character)> _pending = new();
    readonly Dictionary<int, (double start, int count)> _hitRate = new();
    /// Peer whose hit report is being applied right now (they already showed the hit).
    int _reporter;

    public bool AllReady => _joined.Count > 0 && _ready.IsSupersetOf(_joined);
    public void ClearReady() => _ready.Clear();

    public bool StartServer(ServerConfig cfg)
    {
        Cfg = cfg;
        Net.Mode = NetMode.Server;
        _mp = new SceneMultiplayer { AuthTimeout = 5 };
        Tree.SetMultiplayer(_mp);
        _mp.AuthCallback = Callable.From<long, byte[]>(ServerAuth);
        _mp.PeerConnected += id => ServerPeerJoined((int)id);
        _mp.PeerDisconnected += id => ServerPeerLeft((int)id);
        _peer = new ENetMultiplayerPeer();
        var err = _peer.CreateServer(cfg.Port, cfg.MaxPlayers + 4, Net.Channels);
        if (err != Error.Ok)
        {
            GD.PrintErr($"[server] cannot listen on UDP {cfg.Port}: {err}");
            return false;
        }
        _mp.MultiplayerPeer = _peer;
        HoverMap = cfg.Map;
        GD.Print($"[server] \"{cfg.Name}\" listening on UDP {cfg.Port} · default stage {MapBase.Names[cfg.Map]} · max {cfg.MaxPlayers}"
            + (cfg.Password != "" ? " · password" : "") + $" · config {ProjectSettings.GlobalizePath(cfg.Path)}");
        return true;
    }

    /// Map is -1 while the server sits in the lobby.
    byte[] InfoBytes() => new NetWriter().B(AuthInfo).S(Cfg.Name).S(Cfg.Motd).I(_matchRunning ? M.MapIndex : -1).I(Entries.Count).I(Cfg.MaxPlayers)
        .Bool(Cfg.Password != "").I(_matchRunning ? M.WaveNumber : 0).I(Net.Protocol).Bytes();

    void ServerAuth(long lid, byte[] data)
    {
        int id = (int)lid;
        var r = new NetReader(data);
        if (r.B() != AuthHello) { _peer.DisconnectPeer(id); return; }
        int proto = r.I();
        bool query = r.Bool();
        string name = r.S().StripEdges();
        int character = r.I();
        string pw = r.S();
        if (query) { _mp.SendAuth(id, InfoBytes()); return; } // the browser hangs up after reading this

        string err = proto != Net.Protocol ? $"VERSION MISMATCH (SERVER v{Net.Protocol}, YOU v{proto})"
            : Cfg.Password != "" && pw != Cfg.Password ? "WRONG PASSWORD"
            : Entries.Count + _pending.Count >= Cfg.MaxPlayers ? "SERVER IS FULL"
            : null;
        if (err != null)
        {
            GD.Print($"[server] refused peer {id}: {err}");
            _mp.SendAuth(id, new NetWriter().B(AuthReject).S(err).Bytes());
            Tree.CreateTimer(0.5).Timeout += () =>
            {
                // usually the client has hung up by now
                if (_peer != null && System.Array.IndexOf(_mp.GetAuthenticatingPeers(), id) >= 0) _peer.DisconnectPeer(id);
            };
            return;
        }
        if (name.Length == 0) name = "SURVIVOR";
        if (name.Length > 16) name = name.Substring(0, 16);
        _pending[id] = (name.ToUpper(), Mathf.Clamp(character, 0, GameData.Characters.Length - 1));
        _mp.SendAuth(id, new NetWriter().B(AuthAccept).B((int)ServerPhase).I(_matchRunning ? M.MapIndex : HoverMap).Bytes());
        _mp.CompleteAuth(id);
    }

    /// In the lobby first: a Player only exists once they're in a match (ServerStartMatch / ServerLateJoin).
    void ServerPeerJoined(int id)
    {
        if (!_pending.Remove(id, out var info)) { _peer.DisconnectPeer(id); return; }
        _peer.GetPeer(id)?.SetTimeout(32, 3000, 8000); // drop crashed clients quickly
        Entries.Add(new LobbyEntry { Id = id, Name = info.name, Character = info.character });
        GD.Print($"[server] {info.name} connected (peer {id}) · {Entries.Count}/{Cfg.MaxPlayers}" + (_matchRunning ? " · match running" : ""));
        if (_matchRunning) M.Toast($"{info.name} IS IN THE LOBBY", new Color(0.5f, 0.9f, 1f));
        BroadcastLobby();
    }

    void ServerPeerLeft(int id)
    {
        _pending.Remove(id);
        var e = Entry(id);
        if (e == null) return;
        Entries.Remove(e);
        var main = M;
        bool inMatch = main.PlayerById(id) != null;
        ServerRemovePlayer(id);
        GD.Print($"[server] {e.Name} left (peer {id}) · {Entries.Count}/{Cfg.MaxPlayers}");
        if (_matchRunning && main.Players.Count == 0) { ServerReturnToLobby(); return; }
        if (inMatch)
        {
            main.Toast($"{e.Name} LEFT", new Color(0.8f, 0.7f, 0.75f));
            if (main.Mode == Main.State.Playing && !main.AnyStanding) main.EndRun();
        }
        BroadcastLobby();
    }

    void ServerTick()
    {
        // 20 Hz world snapshot
        if (_tick % 3 == 0 && _joined.Count > 0) Broadcast(Snapshot(), 2);
    }

    // ------------------------------------------------------------------ incoming
    void ServerMsg(int from, Msg m, NetReader r)
    {
        if (IsLobbyMsg(m)) { ServerLobbyMsg(from, m, r); return; }
        if (!_joined.Contains(from)) return;
        var main = M;
        var p = main.PlayerById(from);
        if (p == null) return;
        switch (m)
        {
            case Msg.State:
                p.ApplyNetState(r.V(), r.V(), r.Yaw(), r.V(), r.B(), r.B());
                break;
            case Msg.Fired:
            {
                int w = r.B();
                var muzzle = r.V();
                int n = Mathf.Min(r.B(), 16);
                var dirs = new Vector3[n];
                for (int i = 0; i < n; i++) dirs[i] = r.V().Normalized();
                if (w >= p.Weapons.Length || !p.Targetable) break;
                p.ServerFired(w, muzzle, dirs);
                var o = new NetWriter(Msg.FiredRelay).I(from).B(w).V(muzzle).B(n);
                foreach (var d in dirs) o.V(d);
                Broadcast(o, 1, from);
                break;
            }
            case Msg.Hit: ServerHit(from, p, r); break;
            case Msg.Slow:
            {
                var z = NetIds.Get<Zombie>(r.I());
                float mul = Mathf.Clamp(r.F(), 0.3f, 1f), t = Mathf.Clamp(r.F(), 0f, 3f);
                if (z != null && !z.Dead && p.Targetable) z.Slow(mul, t);
                break;
            }
            case Msg.MudLanded:
            {
                var pos = r.V();
                if (!p.Targetable || pos.DistanceTo(p.GlobalPosition) > 80f) break;
                var space = main.World.GetWorld3D().DirectSpaceState;
                MudPuddle.Spawn(MudPuddle.FindFloor(space, pos + Vector3.Up * 0.5f, 2f) ?? pos);
                break;
            }
            case Msg.Grenade:
            {
                var pos = r.V(); var vel = r.V();
                if (!p.Targetable || pos.DistanceTo(p.GlobalPosition) > 8f || vel.Length() > 45f) break;
                Grenade.Throw(pos, vel, p.Def.BlastMult, p);
                break;
            }
            case Msg.TakePickup:
            {
                var pk = NetIds.Get<Pickup>(r.I());
                if (pk == null || pk.Taken || !p.Targetable || pk.GlobalPosition.DistanceTo(p.GlobalPosition) > 6f) break;
                pk.Taken = true;
                p.ApplyCollect(pk.Kind, true, false);
                Broadcast(new NetWriter(Msg.PickupTaken).I(pk.NetId).I(from).B((int)pk.Kind));
                pk.QueueFree();
                break;
            }
            case Msg.Ready:
                _ready.Add(from);
                break;
        }
    }

    void ServerHit(int from, Player p, NetReader r)
    {
        int id = r.I();
        float dmg = r.F();
        var pos = r.V(); var dir = r.V();
        int flags = r.B();
        float knock = Mathf.Clamp(r.F(), 0f, 4f);
        var z = NetIds.Get<Zombie>(id);
        if (z == null || z.Dead || z.Rising || !p.Targetable) return;
        if (z.GlobalPosition.DistanceTo(p.GlobalPosition) > 120f) return;
        if (!(dmg > 0) || dmg > p.MaxHitDamage * 1.01f) return;
        // a few dozen hits a second is plenty even for a piercing shotgun
        double now = Time.GetTicksMsec() / 1000.0;
        var (start, count) = _hitRate.GetValueOrDefault(from, (now, 0));
        if (now - start > 1.0) (start, count) = (now, 0);
        if (++count > 80) return;
        _hitRate[from] = (start, count);
        if (pos.DistanceTo(z.GlobalPosition) > z.Height + 3f) pos = z.GlobalPosition + Vector3.Up * z.Height * 0.6f;
        _reporter = from;
        z.TakeDamage(dmg, pos, dir.Normalized(), (flags & 1) != 0, knock, (flags & 2) != 0, (flags & 4) != 0, p);
        _reporter = 0;
    }

    // ------------------------------------------------------------------ outgoing
    public void Broadcast(NetWriter w, int ch = 0, int except = 0)
    {
        if (_joined.Count == 0) return;
        var b = w.Bytes();
        var live = _mp.GetPeers();
        foreach (var id in _joined)
        {
            if (id == except || System.Array.IndexOf(live, id) < 0) continue;
            // a peer can be mid-disconnect (its leave event not processed yet)
            if (_peer.GetPeer(id) is not { } pp || pp.GetState() != ENetPacketPeer.PeerState.Connected) continue;
            SendRaw(id, b, ch);
        }
    }

    static void GameState(NetWriter w)
    {
        var m = M;
        w.I(m.Score).B(m.ComboMult).I(m.WaveNumber).I(m.ZombiesRemaining).F(m.BreakTimer).B((int)m.Mode);
    }

    static NetWriter PlayerFull(NetWriter w, Player p) =>
        w.I(p.NetId).B(p.CharIndex).S(p.NetName).Bool(p.Benched).Bool(p.Downed).Bool(p.Dead).V(p.GlobalPosition).F(p.Hp).I(p.Kills);

    public static void ZombieFull(NetWriter w, Zombie z) =>
        w.I(z.NetId).B((int)z.Type).I(z.Wave).Bool(z.Rising).B(z.Skin).V(z.GlobalPosition).Yaw(z.Yaw);

    static void PickupFull(NetWriter w, Pickup p, Vector3 vel) => w.I(p.NetId).B((int)p.Kind).V(p.GlobalPosition).V(vel);
    static void PuddleFull(NetWriter w, MudPuddle p) => w.I(p.NetId).V(p.GlobalPosition).F(p.Radius).Bool(p.Burning);

    NetWriter Welcome()
    {
        var main = M;
        var w = new NetWriter(Msg.Welcome);
        GameState(w);
        w.B(main.Players.Count);
        foreach (var p in main.Players) PlayerFull(w, p);
        var zs = main.Zombies.FindAll(z => !z.Dead && GodotObject.IsInstanceValid(z));
        w.I(zs.Count);
        foreach (var z in zs) ZombieFull(w, z);
        var pickups = new List<Pickup>();
        var puddles = new List<MudPuddle>();
        foreach (var n in NetIds.All())
        {
            if (n is Pickup pk && !pk.Taken) pickups.Add(pk);
            else if (n is MudPuddle pd && !pd.Removed) puddles.Add(pd);
        }
        w.I(pickups.Count);
        foreach (var pk in pickups) PickupFull(w, pk, Vector3.Zero);
        w.I(puddles.Count);
        foreach (var pd in puddles) PuddleFull(w, pd);
        return w;
    }

    NetWriter Snapshot()
    {
        var main = M;
        var w = new NetWriter(Msg.Snapshot);
        GameState(w);
        w.B(main.Players.Count);
        foreach (var p in main.Players)
            w.I(p.NetId).V(p.GlobalPosition).V(p.Velocity).Yaw(p.FacingYaw).V(p.AimPoint).B(p.NetFlags).B(p.WeaponIndex)
             .F(p.Hp).F(p.BleedOut).F(p.ReviveProgress).I(p.Kills);
        int n = 0;
        foreach (var z in main.Zombies) if (!z.Dead) n++;
        w.I(n);
        foreach (var z in main.Zombies)
            if (!z.Dead) w.I(z.NetId).V(z.GlobalPosition).Yaw(z.Yaw).B(z.NetFlags).B(z.WindupByte).B(z.Strikes & 255);
        return w;
    }

    // events raised by gameplay code (server only)
    public void SendZombieSpawned(Zombie z) { var w = new NetWriter(Msg.ZombieSpawned); ZombieFull(w, z); Broadcast(w); }

    public void SendZombieHit(Zombie z, Vector3 pos, Vector3 dir, float shownDmg, bool head, bool minor, bool explosive) =>
        Broadcast(new NetWriter(Msg.ZombieHit).I(z.NetId).V(pos).V(dir).F(shownDmg).B((head ? 1 : 0) | (explosive ? 2 : 0) | (minor ? 4 : 0)),
            minor ? 1 : 0, _reporter);

    public void SendZombieDied(Zombie z, Vector3 dir, bool head, DeathKind kind, Player killer) =>
        Broadcast(new NetWriter(Msg.ZombieDied).I(z.NetId).V(dir).Bool(head).B((int)kind).I(killer?.NetId ?? 0));

    public void SendPlayerHurt(Player p, float dmg, Vector3 dir, float knock) =>
        Broadcast(new NetWriter(Msg.PlayerHurt).I(p.NetId).F(dmg).V(dir).F(knock));
    public void SendPlayerDowned(Player p) => Broadcast(new NetWriter(Msg.PlayerDowned).I(p.NetId));
    public void SendPlayerDied(Player p, Vector3 dir) => Broadcast(new NetWriter(Msg.PlayerDied).I(p.NetId).V(dir));
    public void SendPlayerRevived(Player p, bool moved) =>
        Broadcast(new NetWriter(Msg.PlayerRevived).I(p.NetId).F(p.Hp).Bool(moved).V(p.GlobalPosition));
    public void SendPlayerSpawned(Player p) => Broadcast(new NetWriter(Msg.PlayerSpawned).I(p.NetId).V(p.GlobalPosition).F(p.Hp));

    public void SendPickupSpawned(Pickup p, Vector3 vel) { var w = new NetWriter(Msg.PickupSpawned); PickupFull(w, p, vel); Broadcast(w); }
    public void SendPickupGone(Pickup p) => Broadcast(new NetWriter(Msg.PickupGone).I(p.NetId));

    public void SendGrenadeSpawned(Grenade g, Vector3 vel, Vector3 spin) =>
        Broadcast(new NetWriter(Msg.GrenadeSpawned).I(g.NetId).V(g.GlobalPosition).V(vel).V(spin));
    public void SendGrenadeExploded(Grenade g, float radius) =>
        Broadcast(new NetWriter(Msg.GrenadeExploded).I(g.NetId).V(g.GlobalPosition).F(radius));

    public void SendPuddleSpawned(MudPuddle p) { var w = new NetWriter(Msg.PuddleSpawned); PuddleFull(w, p); Broadcast(w); }
    public void SendPuddleGrew(MudPuddle p) => Broadcast(new NetWriter(Msg.PuddleGrew).I(p.NetId).F(p.Radius));
    public void SendPuddleIgnited(MudPuddle p) => Broadcast(new NetWriter(Msg.PuddleIgnited).I(p.NetId));
    public void SendPuddleRemoved(MudPuddle p) => Broadcast(new NetWriter(Msg.PuddleRemoved).I(p.NetId));

    public void SendBanner(string text, string sub, float hold) => Broadcast(new NetWriter(Msg.Banner).S(text).S(sub).F(hold));
    public void SendToast(string text, Color c) => Broadcast(new NetWriter(Msg.Toast).S(text).F(c.R).F(c.G).F(c.B));
    public void SendScore(int total, Vector3 at, bool combo) => Broadcast(new NetWriter(Msg.Score).I(total).V(at).Bool(combo));
    public void SendSound(string name, float db) => Broadcast(new NetWriter(Msg.Sound).S(name).F(db));

    public void SendRunStart()
    {
        var main = M;
        var w = new NetWriter(Msg.RunStart).B(main.Players.Count);
        foreach (var p in main.Players) w.I(p.NetId).V(p.GlobalPosition).Bool(p.Benched);
        Broadcast(w);
    }

    public void SendRunOver(string title, string stats, float delay) => Broadcast(new NetWriter(Msg.RunOver).S(title).S(stats).F(delay));
}
