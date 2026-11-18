using Godot;
using System;
using System.Collections.Generic;

public enum LobbyPhase { Lobby, MapSelect, Playing }

/// One connected player in the online lobby (server: the real thing; client: a mirror).
public class LobbyEntry
{
    public int Id, Character;
    public string Name = "";
    public bool Confirmed, InMatch;
}

/// Online lobby: everyone picks + confirms a survivor, then the host (first to connect) picks the stage.
public partial class NetGame
{
    /// Server: connection order = slot order; entries[0] is the host. Client: the server's list as last received.
    public readonly List<LobbyEntry> Entries = new();
    /// Stage the host's carousel is on.
    public int HoverMap;
    /// Client: lobby state as last received.
    public LobbyPhase Phase { get; private set; }
    public int HostId { get; private set; }
    /// Client: the lobby list changed (or the phase / host did).
    public event Action LobbyChanged;
    /// Client: the host moved the stage carousel.
    public event Action<int> HoverChanged;

    bool _matchRunning;

    public LobbyEntry Entry(int id) => Entries.Find(e => e.Id == id);
    public LobbyEntry Me => Entry(MyId);
    public bool IsHost => Entries.Count > 0 && Entries[0].Id == MyId;

    void ClearLobby()
    {
        Entries.Clear();
        Phase = LobbyPhase.Lobby;
        HostId = 0;
        _matchRunning = false;
    }

    // ------------------------------------------------------------------ server
    /// Outside a match the phase is derived: stage select once 2+ players are in and all have confirmed.
    public LobbyPhase ServerPhase =>
        _matchRunning ? LobbyPhase.Playing
        : Entries.Count >= 2 && Entries.TrueForAll(e => e.Confirmed) ? LobbyPhase.MapSelect
        : LobbyPhase.Lobby;

    public bool MatchRunning => _matchRunning;

    void BroadcastLobby()
    {
        var w = new NetWriter(Msg.Lobby).B((int)ServerPhase).I(Entries.Count > 0 ? Entries[0].Id : 0).B(HoverMap).B(Entries.Count);
        foreach (var e in Entries) w.I(e.Id).S(e.Name).B(e.Character).Bool(e.Confirmed).Bool(e.InMatch);
        SendToLobby(w);
    }

    /// To every connected player, in the match or not (Broadcast only reaches the match).
    void SendToLobby(NetWriter w, int except = 0)
    {
        if (_peer == null || Entries.Count == 0) return;
        var b = w.Bytes();
        foreach (var e in Entries)
        {
            if (e.Id == except) continue;
            if (_peer.GetPeer(e.Id) is not { } pp || pp.GetState() != ENetPacketPeer.PeerState.Connected) continue;
            SendRaw(e.Id, b, 0);
        }
    }

    static bool IsLobbyMsg(Msg m) => m is Msg.LobbyPick or Msg.LobbyConfirm or Msg.LobbyHover or Msg.LobbyChooseMap or Msg.BackToLobby;

    void ServerLobbyMsg(int from, Msg m, NetReader r)
    {
        var e = Entry(from);
        if (e == null) return;
        bool host = Entries[0] == e;
        switch (m)
        {
            case Msg.LobbyPick:
            {
                int c = r.B();
                if (e.Confirmed || e.InMatch) return;
                e.Character = Mathf.Clamp(c, 0, GameData.Characters.Length - 1);
                break;
            }
            case Msg.LobbyConfirm:
            {
                bool on = r.Bool();
                if (e.InMatch || e.Confirmed == on) return;
                e.Confirmed = on;
                GD.Print($"[server] {e.Name} {(on ? "confirmed" : "unconfirmed")} {GameData.Characters[e.Character].Name}");
                if (on && _matchRunning) ServerLateJoin(e);
                break;
            }
            case Msg.LobbyHover:
            {
                int map = r.B();
                if (!host || ServerPhase != LobbyPhase.MapSelect) return;
                HoverMap = Mathf.Clamp(map, 0, MapBase.Names.Length - 1);
                SendToLobby(new NetWriter(Msg.LobbyHover).B(HoverMap), from);
                return;
            }
            case Msg.LobbyChooseMap:
            {
                int map = r.B();
                if (!host || ServerPhase != LobbyPhase.MapSelect) return;
                ServerStartMatch(Mathf.Clamp(map, 0, MapBase.Names.Length - 1));
                break;
            }
            case Msg.BackToLobby:
            {
                var p = M.PlayerById(from);
                if (!e.InMatch || p == null || !p.Benched) return;
                ServerRemovePlayer(from);
                e.InMatch = false;
                e.Confirmed = false;
                SendRaw(from, new NetWriter(Msg.ToLobby).Bytes(), 0);
                GD.Print($"[server] {e.Name} went back to the lobby");
                break;
            }
        }
        BroadcastLobby();
    }

    /// The host picked a stage: everyone in the lobby goes in together.
    void ServerStartMatch(int map)
    {
        var main = M;
        HoverMap = map;
        _matchRunning = true;
        main.ServerLoadStage(map);
        for (int i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            e.InMatch = true;
            var p = new Player();
            p.SetupRemote(e.Character, e.Id, e.Name);
            main.World.AddChild(p);
            main.Players.Add(p);
            p.GlobalPosition = main.StartLine(i, Entries.Count);
        }
        foreach (var e in Entries) _joined.Add(e.Id);
        var welcome = Welcome().Bytes();
        foreach (var e in Entries)
        {
            SendRaw(e.Id, new NetWriter(Msg.Enter).B(map).Bytes(), 0);
            SendRaw(e.Id, welcome, 0);
        }
        GD.Print($"[server] {Entries[0].Name} picked {MapBase.Names[map]}");
        main.ServerBeginRun();
    }

    /// Confirmed while a match is running: in as a spectator until the wave is cleared (or straight in during a break).
    void ServerLateJoin(LobbyEntry e)
    {
        var main = M;
        var p = new Player();
        p.SetupRemote(e.Character, e.Id, e.Name);
        main.World.AddChild(p);
        main.Players.Add(p);
        bool spawnNow = main.Mode == Main.State.Playing && !main.WaveActive;
        p.SetBenched(!spawnNow);
        p.GlobalPosition = spawnNow ? main.BuddySpot(p) : main.StartLine(main.Players.Count - 1, main.Players.Count);
        e.InMatch = true;
        Broadcast(PlayerFull(new NetWriter(Msg.PlayerJoined), p));
        SendRaw(e.Id, new NetWriter(Msg.Enter).B(main.MapIndex).Bytes(), 0);
        SendRaw(e.Id, Welcome().Bytes(), 0);
        _joined.Add(e.Id);
        GD.Print($"[server] {e.Name} joined the match as {p.Def.Name}" + (spawnNow ? "" : " · spectating"));
        main.Toast($"{e.Name} JOINED", new Color(0.5f, 0.9f, 1f));
    }

    /// Take a player out of the match (left the server, or back to the lobby).
    void ServerRemovePlayer(int id)
    {
        _joined.Remove(id);
        _ready.Remove(id);
        _hitRate.Remove(id);
        var main = M;
        var p = main.PlayerById(id);
        if (p == null) return;
        p.Dead = true; // zombies drop them as a target right away
        main.Players.Remove(p);
        p.QueueFree();
        Broadcast(new NetWriter(Msg.PlayerLeft).I(id));
    }

    /// Match over (or nobody left in it): everyone back to survivor select, picks kept but unconfirmed.
    public void ServerReturnToLobby()
    {
        M.ServerClearMatch();
        foreach (var e in Entries)
        {
            if (e.InMatch) SendRaw(e.Id, new NetWriter(Msg.ToLobby).Bytes(), 0);
            e.InMatch = false;
            e.Confirmed = false;
        }
        _joined.Clear();
        _ready.Clear();
        _matchRunning = false;
        GD.Print($"[server] back to the lobby · {Entries.Count} connected");
        BroadcastLobby();
    }

    // ------------------------------------------------------------------ client
    void ClientLobbyMsg(NetReader r)
    {
        Phase = (LobbyPhase)r.B();
        HostId = r.I();
        HoverMap = r.B();
        int n = r.B();
        // keep our own unconfirmed pick if the server hasn't caught up with it yet
        int? myPick = Me is { Confirmed: false } me0 ? me0.Character : null;
        Entries.Clear();
        for (int i = 0; i < n; i++)
            Entries.Add(new LobbyEntry { Id = r.I(), Name = r.S(), Character = r.B(), Confirmed = r.Bool(), InMatch = r.Bool() });
        if (myPick is { } c && Me is { Confirmed: false } me && _pickPending) me.Character = c;
        _pickPending = false;
        LobbyChanged?.Invoke();
    }

    bool _pickPending;

    public void SendLobbyPick(int character)
    {
        if (Me is { } me) me.Character = character;
        _pickPending = true;
        ToServer(new NetWriter(Msg.LobbyPick).B(character));
    }

    public void SendLobbyConfirm(bool on)
    {
        if (Me is { } me) me.Confirmed = on;
        ToServer(new NetWriter(Msg.LobbyConfirm).Bool(on));
    }

    public void SendLobbyHover(int map) { HoverMap = map; ToServer(new NetWriter(Msg.LobbyHover).B(map)); }
    public void SendChooseMap(int map) => ToServer(new NetWriter(Msg.LobbyChooseMap).B(map));
    public void SendBackToLobby() => ToServer(new NetWriter(Msg.BackToLobby));
}
