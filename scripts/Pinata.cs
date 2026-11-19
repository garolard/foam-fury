using Godot;
using System.Collections.Generic;

/// A zombie turned into a donkey piñata by the Fiesta Popper. Nearby zombies forget the players and swarm it,
/// whacking it like a piñata (each whack shortens the fuse); then it bursts in a confetti cloud that hurts zombies only.
/// Server/solo own the fuse, lure and damage; clients get replicas that wobble and burst when told.
public partial class Pinata : Node3D
{
    public const float LureRadius = 14f;
    const float FuseTime = 4f, WhackFuse = 0.2f, BaseRadius = 6f, BaseDamage = 150f;
    /// Only one piñata on the map at a time; the Popper's confetti just stings until it bursts.
    const int MaxActive = 1;

    public static readonly List<Pinata> Active = new();

    public int NetId;
    public float Size { get; private set; }
    public float Radius { get; private set; }
    public float Yaw { get; private set; }
    public bool Alive => !_exploded && IsInstanceValid(this);

    bool _replica, _exploded;
    float _fuse = FuseTime, _damage, _wobble, _age, _tick;
    Player _source;
    Node3D _model;

    /// Brutes are too big to stuff in a piñata, and there's only room for one at a party.
    public static bool CanConvert(Zombie z) => !z.Dead && !z.Rising && z.Type != ZombieType.Brute && AliveCount() < MaxActive;

    static int AliveCount()
    {
        int n = 0;
        foreach (var p in Active) if (p.Alive) n++;
        return n;
    }

    /// Nearest unbroken piñata within `range` (zombie lure).
    public static Pinata Nearest(Vector3 from, float range)
    {
        Pinata best = null;
        float bd = range * range;
        foreach (var p in Active)
        {
            if (!p.Alive) continue;
            float d = p.GlobalPosition.DistanceSquaredTo(from);
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    public static Pinata Spawn(Vector3 pos, float yaw, float size, Player source, float blastMult)
    {
        var p = new Pinata { _source = source, Size = size, Yaw = yaw, Radius = BaseRadius * Mathf.Sqrt(blastMult), _damage = BaseDamage * blastMult };
        if (Net.IsServer) p.NetId = NetIds.Next();
        Main.Instance.World.AddChild(p);
        p.GlobalPosition = pos;
        p.Rotation = new Vector3(0, yaw, 0);
        if (Net.IsServer) Net.Game?.SendPinataSpawned(p);
        return p;
    }

    public static void SpawnReplica(int id, Vector3 pos, float yaw, float size, float radius)
    {
        var p = new Pinata { NetId = id, _replica = true, Size = size, Yaw = yaw, Radius = radius };
        Main.Instance.World.AddChild(p);
        p.GlobalPosition = pos;
        p.Rotation = new Vector3(0, yaw, 0);
    }

    /// The server's piñata burst: drop our copy and play the cloud.
    public static void ExplodeReplica(int id, Vector3 pos, float radius)
    {
        var p = NetIds.Get<Pinata>(id);
        if (p != null) { p._exploded = true; p.QueueFree(); }
        Burst(pos, radius);
    }

    public override void _EnterTree() { NetIds.Add(NetId, this); Active.Add(this); }
    public override void _ExitTree() { NetIds.Remove(NetId, this); Active.Remove(this); }

    public override void _Ready()
    {
        _model = Assets.Model("custom-weapons", "pinata-donkey");
        _model.Scale = Vector3.One * Size * 0.1f;
        AddChild(_model);
        var tw = _model.CreateTween();
        tw.TweenProperty(_model, "scale", Vector3.One * Size, 0.55f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        Fx.Confetti(GlobalPosition + Vector3.Up * Size * 0.6f, 60, 7f, 1.6f, Vector3.Up, 70f);
        Fx.Flash(GlobalPosition + Vector3.Up, Fx.FiestaColors[0], 4f, 6f, 0.25f);
        Sfx.Play3D("pinata_pop", GlobalPosition, -1f, 0.08f);
    }

    /// A zombie (or the server telling us about one) clobbered it.
    public void Whack(Vector3 dir)
    {
        if (_exploded) return;
        _wobble = 1f;
        if (!_replica) _fuse -= WhackFuse;
        Fx.Confetti(GlobalPosition + Vector3.Up * Size * 0.6f - dir * 0.3f, 8, 4f, 1.1f, -dir + Vector3.Up, 60f, 0.8f);
        Sfx.Play3D("pinata_whack", GlobalPosition, -5f, 0.15f);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        if (_replica) _fuse -= dt;
        _wobble = Mathf.Max(0, _wobble - dt * 2.5f);
        // idle sway, knocked about by whacks, trembling and swelling as it's about to burst
        float danger = 1f - Mathf.Clamp(_fuse / 1.2f, 0, 1);
        float amp = 0.05f + _wobble * 0.35f + danger * 0.12f;
        float fr = 3f + _wobble * 9f + danger * 22f;
        _model.Rotation = new Vector3(Mathf.Sin(_age * fr * 0.8f) * amp * 0.6f, Mathf.Sin(_age * 1.3f) * 0.08f, Mathf.Sin(_age * fr) * amp);
        if (_age > 0.6f) _model.Scale = Vector3.One * Size * (1f + danger * (0.15f + 0.06f * Mathf.Sin(_age * 40f)));
        if (danger > 0)
        {
            _tick -= dt;
            if (_tick <= 0) { _tick = Mathf.Lerp(0.35f, 0.1f, danger); Sfx.Play3D("pinata_whack", GlobalPosition, -10f, 0f, 1.6f + danger); }
        }
        if (_replica && _fuse < -3f) QueueFree(); // the server's burst normally arrives first
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_replica || _exploded) return;
        _fuse -= (float)delta;
        if (_fuse <= 0) Explode();
    }

    void Explode()
    {
        _exploded = true;
        var pos = GlobalPosition;
        if (Net.IsServer) Net.Game?.SendPinataExploded(this);
        Burst(pos, Radius);
        // party's over: confetti shrapnel for every zombie in range (players are guests, not targets)
        foreach (var z in Main.Instance.Zombies.ToArray())
        {
            if (z.Dead) continue;
            float d = z.GlobalPosition.DistanceTo(pos);
            if (d > Radius) continue;
            float f = 1f - d / Radius * 0.6f;
            var dir = (z.GlobalPosition - pos).Normalized();
            z.TakeDamage(_damage * f, z.GlobalPosition + Vector3.Up, dir, false, 12f * f, true, source: _source);
        }
        QueueFree();
    }

    static void Burst(Vector3 pos, float radius)
    {
        var c = pos + Vector3.Up * 1.1f;
        Fx.Confetti(c, 260, radius * 2.6f, 3.2f, Vector3.Up, 180f, 1.4f, 1.6f);
        Fx.Confetti(c, 80, radius * 1.6f, 2.2f, Vector3.Up, 50f, 2f, 2.5f);
        for (int i = 0; i < 3; i++) Fx.Flash(c, Fx.FiestaColors[i * 2], 7f, radius * 2.5f, 0.35f);
        Fx.Number(c + Vector3.Up * 1.2f, "¡FIESTA!", Fx.FiestaColors[0], 1.6f);
        Sfx.Play3D("pinata_boom", pos, 3f, 0.08f);
        Main.Instance.Shake(pos, 0.5f, 0.6f, 0.7f, 0.35f);
    }
}
