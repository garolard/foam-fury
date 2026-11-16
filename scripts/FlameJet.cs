using Godot;
using System.Collections.Generic;

/// One puff of the flamethrower stream: a short-lived, growing fireball that drifts forward,
/// licks every zombie it touches once, sets them on fire and ignites mud puddles.
public partial class FlameJet : Node3D
{
    const float MaxLife = 0.5f, BurnDps = 14f, BurnTime = 3f;

    Vector3 _vel;
    float _life, _damage;
    readonly HashSet<Zombie> _hit = new();
    Player _source;
    MeshInstance3D _ball;
    StandardMaterial3D _mat;

    static Gradient _ramp;
    static StandardMaterial3D _particleMat;
    static SphereMesh _ballMesh;

    /// Hot white-yellow core -> orange -> dark smoke, fading out. HDR values so it glows.
    public static Gradient FireRamp => _ramp ??= new Gradient
    {
        Offsets = new[] { 0f, 0.25f, 0.6f, 1f },
        Colors = new[] { new Color(3f, 2.4f, 1.2f, 1f), new Color(2.6f, 1.1f, 0.25f, 0.95f), new Color(1.4f, 0.3f, 0.08f, 0.7f), new Color(0.12f, 0.1f, 0.1f, 0f) },
    };

    /// Shared material for fire particles: unshaded, colored by the particle ColorRamp.
    public static StandardMaterial3D ParticleMaterial => _particleMat ??= new StandardMaterial3D
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    public static void Fire(Vector3 pos, Vector3 dir, Vector3 inheritVel, WeaponDef w, float damageMult, Player source = null)
    {
        var j = new FlameJet
        {
            _vel = dir.Normalized() * w.ProjectileSpeed * (float)GD.RandRange(0.85, 1.1) + inheritVel * 0.5f,
            _damage = w.Damage * damageMult,
            _source = source,
        };
        Main.Instance.World.AddChild(j);
        j.GlobalPosition = pos;
    }

    public override void _Ready()
    {
        _ballMesh ??= new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 8, Rings = 4 };
        _mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = FireRamp.Sample(0),
        };
        _ball = new MeshInstance3D { Mesh = _ballMesh, MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _ball.Scale = Vector3.One * 0.2f;
        AddChild(_ball);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _life += dt;
        float t = _life / MaxLife;
        if (t >= 1f) { QueueFree(); return; }
        float radius = Mathf.Lerp(0.15f, 0.9f, Mathf.Sqrt(t));

        // drift: drag, hot air rises, slide along walls/floor instead of passing through
        var from = GlobalPosition;
        var to = from + _vel * dt;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, 1));
        if (hit.Count > 0)
        {
            var n = hit["normal"].AsVector3();
            to = hit["position"].AsVector3() + n * 0.15f;
            _vel = (_vel - n * _vel.Dot(n)) * 0.4f;
            MudPuddle.IgniteAt(to, radius, _source); // flame splashing on the floor lights any mud there
        }
        GlobalPosition = to;
        _vel *= Mathf.Exp(-2.2f * dt);
        _vel.Y += 3f * dt;

        _ball.Scale = Vector3.One * radius * 2f * (0.9f + 0.2f * GD.Randf());
        _mat.AlbedoColor = FireRamp.Sample(t);

        var pos = GlobalPosition;
        foreach (var z in Main.Instance.Zombies.ToArray())
        {
            if (z.Dead || _hit.Contains(z)) continue;
            var d = z.GlobalPosition - pos; d.Y = 0;
            float zr = z.Height * 0.17f;
            if (d.Length() > radius + zr) continue;
            if (pos.Y < z.GlobalPosition.Y - radius || pos.Y > z.GlobalPosition.Y + z.Height + radius) continue;
            _hit.Add(z);
            z.Ignite(BurnDps, BurnTime, _source);
            z.TakeDamage(_damage, pos, _vel.Normalized(), false, 0.4f, false, true, _source);
        }
        MudPuddle.IgniteAt(pos, radius * 0.6f, _source);
    }
}
