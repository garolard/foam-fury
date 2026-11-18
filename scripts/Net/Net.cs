using Godot;
using System.Collections.Generic;
using System.IO;

public enum NetMode { Offline, Server, Client }

/// Online session role and shared helpers. Offline (solo / local co-op) never touches any of this.
public static class Net
{
    public const int Protocol = 1, DefaultPort = 7777, Channels = 3;

    public static NetMode Mode = NetMode.Offline;
    public static bool IsServer => Mode == NetMode.Server;
    public static bool IsClient => Mode == NetMode.Client;
    public static bool Online => Mode != NetMode.Offline;
    public static bool Headless => DisplayServer.GetName() == "headless";
    /// The session node (/root/Main/Net); null outside a running Main.
    public static NetGame Game;

    /// Start as a dedicated server? (dedicated export, `-- --server`, or FF_NET=server)
    public static bool WantsServer =>
        OS.HasFeature("dedicated_server") || HasArg("--server") || OS.GetEnvironment("FF_NET") == "server";

    static IEnumerable<string> AllArgs()
    {
        foreach (var a in OS.GetCmdlineUserArgs()) yield return a;
        foreach (var a in OS.GetCmdlineArgs()) yield return a;
    }

    public static bool HasArg(string name)
    {
        foreach (var a in AllArgs()) if (a == name) return true;
        return false;
    }

    /// `--name=value` from the command line, or null.
    public static string Arg(string name)
    {
        foreach (var a in AllArgs()) if (a.StartsWith(name + "=")) return a.Substring(name.Length + 1);
        return null;
    }

    /// "host", "host:port", "ff://host:port/whatever" -> host + port (default 7777).
    public static bool ParseAddress(string s, out string host, out int port)
    {
        host = ""; port = DefaultPort;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        int scheme = s.IndexOf("://");
        if (scheme >= 0) s = s.Substring(scheme + 3);
        int slash = s.IndexOf('/');
        if (slash >= 0) s = s.Substring(0, slash);
        int colon = s.LastIndexOf(':');
        if (colon > 0 && s.IndexOf(':') == colon)
        {
            if (!int.TryParse(s.Substring(colon + 1), out port) || port <= 0 || port > 65535) return false;
            s = s.Substring(0, colon);
        }
        host = s;
        return host.Length > 0;
    }

    /// Hostname -> IP (IP literals pass straight through). Empty when it can't be resolved.
    public static string Resolve(string host)
    {
        if (host.IsValidIPAddress()) return host;
        var ip = IP.ResolveHostname(host, IP.Type.Ipv4);
        return string.IsNullOrEmpty(ip) ? IP.ResolveHostname(host) : ip;
    }
}

/// Wire message ids (first byte of every packet).
public enum Msg : byte
{
    // client -> server
    State = 1, Fired, Hit, Slow, MudLanded, Grenade, TakePickup, Ready,
    // server -> client
    Welcome = 20, Snapshot, PlayerJoined, PlayerLeft, PlayerSpawned, PlayerHurt, PlayerDowned, PlayerDied, PlayerRevived,
    ZombieSpawned, ZombieHit, ZombieDied, PickupSpawned, PickupTaken, PickupGone, GrenadeSpawned, GrenadeExploded,
    PuddleSpawned, PuddleGrew, PuddleIgnited, PuddleRemoved, Banner, Toast, Score, Sound, RunStart, RunOver, FiredRelay,
}

/// Packs a message into bytes.
public sealed class NetWriter
{
    readonly MemoryStream _ms = new();
    readonly BinaryWriter _w;

    public NetWriter(Msg m) { _w = new BinaryWriter(_ms); _w.Write((byte)m); }
    public NetWriter() { _w = new BinaryWriter(_ms); }

    public NetWriter B(int v) { _w.Write((byte)v); return this; }
    public NetWriter Bool(bool v) { _w.Write(v); return this; }
    public NetWriter I(int v) { _w.Write(v); return this; }
    public NetWriter F(float v) { _w.Write(v); return this; }
    public NetWriter S(string v) { _w.Write(v ?? ""); return this; }
    public NetWriter V(Vector3 v) { _w.Write(v.X); _w.Write(v.Y); _w.Write(v.Z); return this; }
    /// Angle quantized to 16 bits.
    public NetWriter Yaw(float a) { _w.Write((short)Mathf.RoundToInt(Mathf.Wrap(a, -Mathf.Pi, Mathf.Pi) / Mathf.Pi * 32767f)); return this; }
    public byte[] Bytes() { _w.Flush(); return _ms.ToArray(); }
}

/// Reads a message written by NetWriter.
public sealed class NetReader
{
    readonly BinaryReader _r;
    public NetReader(byte[] data) { _r = new BinaryReader(new MemoryStream(data)); }

    public int B() => _r.ReadByte();
    public bool Bool() => _r.ReadBoolean();
    public int I() => _r.ReadInt32();
    public float F() => _r.ReadSingle();
    public string S() => _r.ReadString();
    public Vector3 V() => new(_r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle());
    public float Yaw() => _r.ReadInt16() / 32767f * Mathf.Pi;
}

/// Server-assigned ids for networked world objects (zombies, pickups, grenades, puddles).
public static class NetIds
{
    static readonly Dictionary<int, Node> _map = new();
    static int _next;

    public static int Next() => ++_next;
    public static void Add(int id, Node n) { if (id != 0) _map[id] = n; }
    public static void Remove(int id, Node n) { if (id != 0 && _map.TryGetValue(id, out var cur) && cur == n) _map.Remove(id); }
    public static T Get<T>(int id) where T : Node =>
        _map.TryGetValue(id, out var n) && GodotObject.IsInstanceValid(n) ? n as T : null;
    public static List<Node> All() => new(_map.Values);
    public static void Clear() => _map.Clear();
}

/// Short history of network positions, sampled ~100 ms in the past for smooth motion.
public class Interp
{
    public const double Delay = 0.1;
    struct Sample { public double T; public Vector3 P; public float Yaw; }
    readonly Sample[] _s = new Sample[4];
    int _n;

    public bool Empty => _n == 0;

    public void Push(double t, Vector3 p, float yaw)
    {
        if (_n == _s.Length) { System.Array.Copy(_s, 1, _s, 0, _n - 1); _n--; }
        _s[_n++] = new Sample { T = t, P = p, Yaw = yaw };
    }

    public void Reset(Vector3 p, float yaw = 0f) { _n = 0; Push(Now, p, yaw); }

    public static double Now => Time.GetTicksMsec() / 1000.0;

    public bool Get(out Vector3 p, out float yaw)
    {
        p = Vector3.Zero; yaw = 0;
        if (_n == 0) return false;
        double t = Now - Delay;
        if (t <= _s[0].T || _n == 1) { p = _s[0].P; yaw = _s[0].Yaw; return true; }
        for (int i = 0; i < _n - 1; i++)
        {
            if (t > _s[i + 1].T) continue;
            float k = (float)((t - _s[i].T) / Mathf.Max(0.0001, _s[i + 1].T - _s[i].T));
            p = _s[i].P.Lerp(_s[i + 1].P, k);
            yaw = Mathf.LerpAngle(_s[i].Yaw, _s[i + 1].Yaw, k);
            return true;
        }
        // past the newest sample: extrapolate a little, then hold
        var a = _s[_n - 2]; var b = _s[_n - 1];
        double dt = Mathf.Max(0.0001, b.T - a.T);
        float ex = (float)Mathf.Min(t - b.T, 0.1);
        p = b.P + (b.P - a.P) / (float)dt * ex;
        yaw = b.Yaw;
        return true;
    }
}
