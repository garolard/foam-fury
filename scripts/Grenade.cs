using Godot;

public partial class Grenade : RigidBody3D
{
    public const int Frag = 0, BlackHole = 1, Incendiary = 2, Shockwave = 3;
    float _fuse = 1.5f;
    float _radius = 5.5f, _damage = 160f;
    MeshInstance3D _blink;
    Player _source;
    /// Online: server id; replicas are client copies that only blink until the server says boom.
    public int NetId;
    bool _replica;
    int _type;
    float _pullTime;

    public static void Throw(Vector3 pos, Vector3 vel, float blastMult, Player source = null, int type = Frag)
    {
        var g = new Grenade { _source = source, _type = type };
        g._radius *= Mathf.Sqrt(blastMult);
        g._damage *= blastMult;
        if (Net.IsServer) g.NetId = NetIds.Next();
        Main.Instance.World.AddChild(g);
        g.GlobalPosition = pos;
        g.LinearVelocity = vel;
        g.AngularVelocity = new Vector3((float)GD.RandRange(-10, 10), (float)GD.RandRange(-10, 10), 0);
        if (Net.IsServer) Net.Game?.SendGrenadeSpawned(g, vel, g.AngularVelocity, type);
    }

    public static void SpawnReplica(int id, Vector3 pos, Vector3 vel, Vector3 spin, int type = Frag)
    {
        var g = new Grenade { NetId = id, _replica = true, _type = type };
        Main.Instance.World.AddChild(g);
        g.GlobalPosition = pos;
        g.LinearVelocity = vel;
        g.AngularVelocity = spin;
        Sfx.Play3D("throw", pos, -4f);
    }

    /// The server's grenade went off: snap our copy there and play the blast.
    public static void ExplodeReplica(int id, Vector3 pos, float radius, int type = Frag)
    {
        NetIds.Get<Grenade>(id)?.QueueFree();
        if (type == BlackHole) GrenadeFx.BlackHoleDetonate(pos, radius);
        else if (type == Shockwave) GrenadeFx.Shockwave(pos, radius);
        else Blast(pos, radius);
    }

    public override void _EnterTree() => NetIds.Add(NetId, this);
    public override void _ExitTree() => NetIds.Remove(NetId, this);

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 1;
        ContinuousCd = true;
        ContactMonitor = true;
        MaxContactsReported = 2;
        PhysicsMaterialOverride = new PhysicsMaterial { Bounce = 0.35f, Friction = 0.8f };
        AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.18f } });
        var m = Assets.Model("kit", "grenade-a");
        m.Scale = Vector3.One * 1.6f;
        m.Position = new Vector3(0, -0.18f, 0);
        AddChild(m);
        _blink = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f }, MaterialOverride = Fx.Glow(new Color(1, 0.2f, 0.1f)),
            Position = new Vector3(0, 0.25f, 0) };
        AddChild(_blink);
        BodyEntered += _ => { if (LinearVelocity.Length() > 2f) Sfx.Play3D("bounce", GlobalPosition, -6f); };
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_pullTime > 0)
        {
            if (_replica) { _pullTime -= dt; if (_pullTime <= 0) QueueFree(); return; } // the server's boom normally arrives first
            _pullTime -= dt;
            PullTargets(dt);
            if (_pullTime <= 0) FinishBlackHole();
            return;
        }
        _fuse -= dt;
        _blink.Visible = Mathf.PosMod(_fuse, _fuse < 0.6f ? 0.12f : 0.3f) < 0.07f;
        if (_replica)
        {
            if (_fuse <= 0 && _type == BlackHole && _pullTime <= 0) ActivateBlackHoleVisual();
            if (_pullTime > 0) _pullTime -= dt;
            if (_fuse < -4f) QueueFree();
            return;
        } // replicas show the pull phase; the server owns damage and the final explosion
        if (_fuse <= 0) Explode();
    }

    void Explode()
    {
        var pos = GlobalPosition;
        if (_type == BlackHole)
        {
            _pullTime = 2.2f;
            _fuse = 99f;
            LinearVelocity = Vector3.Zero;
            Freeze = true;
            ActivateBlackHoleVisual();
            return;
        }
        if (Net.IsServer) Net.Game?.SendGrenadeExploded(this, _radius, _type);
        if (_type == Shockwave) GrenadeFx.Shockwave(pos, _radius);
        else Blast(pos, _radius);
        if (_type == Incendiary)
        {
            var floor = MudPuddle.FindFloor(GetWorld3D().DirectSpaceState, pos + Vector3.Up, 4f) ?? pos;
            MudPuddle.Spawn(floor);
            MudPuddle.IgniteAt(floor, _radius * 0.8f, _source);
        }
        else if (_type != Shockwave) MudPuddle.IgniteAt(pos, _radius * 0.8f, _source); // a pressure wave doesn't light mud
        float damage = _type == Shockwave ? _damage * 0.7f : _damage;
        foreach (var z in Main.Instance.Zombies.ToArray())
        {
            float d = z.GlobalPosition.DistanceTo(pos);
            if (d > _radius || z.Dead) continue;
            float f = 1f - d / _radius * 0.6f;
            var dir = (z.GlobalPosition - pos).Normalized();
            z.TakeDamage(damage * f, z.GlobalPosition + Vector3.Up, dir, false, (_type == Shockwave ? 28f : 14f) * f, true, source: _source);
        }
        foreach (var p in Main.Instance.Players)
        {
            if (p == _source || p.Dead || p.Downed || p.Benched) continue;
            float d = p.GlobalPosition.DistanceTo(pos);
            if (d <= _radius) p.TakeDamage(damage * (1f - d / _radius * 0.6f), (p.GlobalPosition - pos).Normalized(), 12f);
        }
        QueueFree();
    }

    void ActivateBlackHoleVisual()
    {
        _pullTime = 2.2f; _fuse = 99f; Freeze = true; LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        foreach (Node child in GetChildren()) if (child is Node3D n) n.Visible = false; // the grenade becomes the singularity
        Sfx.Play3D("explosion", GlobalPosition, -2f, 0.05f, 0.45f);
        Sfx.Play3D("flame", GlobalPosition, -4f, 0.05f, 0.5f);
        if (!Net.IsServer) AddChild(new BlackHoleVortex { Radius = _radius, Duration = _pullTime });
        if (_replica) _pullTime += 0.6f; // grace for the server's boom message before tidying up
    }

    void PullTargets(float dt)
    {
        Vector3 center = GlobalPosition;
        foreach (var z in Main.Instance.Zombies.ToArray())
        {
            if (z.Dead) continue;
            Vector3 delta = center - z.GlobalPosition;
            float d = delta.Length();
            if (d < _radius && d > 0.05f) z.AddGravityPull(delta / d * Mathf.Min(32f, 60f / Mathf.Max(1f, d)) * dt);
        }
        foreach (var p in Main.Instance.Players)
        {
            if (p.Dead || p.Downed || p.Benched) continue;
            Vector3 delta = center - p.GlobalPosition;
            float d = delta.Length();
            if (d < _radius && d > 0.05f)
            {
                var v = p.Velocity;
                v += delta / d * Mathf.Min(24f, 48f / Mathf.Max(1f, d)) * dt;
                p.Velocity = v;
            }
        }
    }

    void FinishBlackHole()
    {
        Vector3 pos = GlobalPosition;
        if (Net.IsServer) Net.Game?.SendGrenadeExploded(this, _radius, _type);
        GrenadeFx.BlackHoleDetonate(pos, _radius * 1.1f);
        foreach (var z in Main.Instance.Zombies.ToArray())
        {
            float d = z.GlobalPosition.DistanceTo(pos);
            if (!z.Dead && d <= _radius) z.TakeDamage(_damage * 1.25f * (1f - d / _radius * 0.5f), z.GlobalPosition + Vector3.Up, (z.GlobalPosition - pos).Normalized(), false, 18f, true, source: _source);
        }
        foreach (var p in Main.Instance.Players)
        {
            float d = p.GlobalPosition.DistanceTo(pos);
            if (!p.Dead && !p.Downed && !p.Benched && d <= _radius)
                p.TakeDamage(_damage * 0.8f * (1f - d / _radius * 0.5f), (p.GlobalPosition - pos).Normalized(), 16f);
        }
        QueueFree();
    }

    static void Blast(Vector3 pos, float radius)
    {
        Fx.Explosion(pos, radius);
        Sfx.Play3D("explosion", pos, 4f, 0.1f);
        Main.Instance.Shake(pos, 0.7f, 0.8f, 1f, 0.45f);
    }
}
