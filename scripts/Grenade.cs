using Godot;

public partial class Grenade : RigidBody3D
{
    float _fuse = 1.5f;
    float _radius = 5.5f, _damage = 160f;
    MeshInstance3D _blink;
    Player _source;
    /// Online: server id; replicas are client copies that only blink until the server says boom.
    public int NetId;
    bool _replica;

    public static void Throw(Vector3 pos, Vector3 vel, float blastMult, Player source = null)
    {
        var g = new Grenade { _source = source };
        g._radius *= Mathf.Sqrt(blastMult);
        g._damage *= blastMult;
        if (Net.IsServer) g.NetId = NetIds.Next();
        Main.Instance.World.AddChild(g);
        g.GlobalPosition = pos;
        g.LinearVelocity = vel;
        g.AngularVelocity = new Vector3((float)GD.RandRange(-10, 10), (float)GD.RandRange(-10, 10), 0);
        if (Net.IsServer) Net.Game?.SendGrenadeSpawned(g, vel, g.AngularVelocity);
    }

    public static void SpawnReplica(int id, Vector3 pos, Vector3 vel, Vector3 spin)
    {
        var g = new Grenade { NetId = id, _replica = true };
        Main.Instance.World.AddChild(g);
        g.GlobalPosition = pos;
        g.LinearVelocity = vel;
        g.AngularVelocity = spin;
        Sfx.Play3D("throw", pos, -4f);
    }

    /// The server's grenade went off: snap our copy there and play the blast.
    public static void ExplodeReplica(int id, Vector3 pos, float radius)
    {
        NetIds.Get<Grenade>(id)?.QueueFree();
        Blast(pos, radius);
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
        _fuse -= (float)delta;
        _blink.Visible = Mathf.PosMod(_fuse, _fuse < 0.6f ? 0.12f : 0.3f) < 0.07f;
        if (_replica) { if (_fuse < -2f) QueueFree(); return; } // lost the boom message: tidy up
        if (_fuse <= 0) Explode();
    }

    void Explode()
    {
        var pos = GlobalPosition;
        if (Net.IsServer) Net.Game?.SendGrenadeExploded(this, _radius);
        Blast(pos, _radius);
        MudPuddle.IgniteAt(pos, _radius * 0.8f, _source);
        foreach (var z in Main.Instance.Zombies.ToArray())
        {
            float d = z.GlobalPosition.DistanceTo(pos);
            if (d > _radius || z.Dead) continue;
            float f = 1f - d / _radius * 0.6f;
            var dir = (z.GlobalPosition - pos).Normalized();
            z.TakeDamage(_damage * f, z.GlobalPosition + Vector3.Up, dir, false, 14f * f, true, source: _source);
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
