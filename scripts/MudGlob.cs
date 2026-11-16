using Godot;

/// A lobbed glob of sticky mud. Hits for light damage, slows what it touches,
/// and leaves a flammable MudPuddle wherever it lands.
public partial class MudGlob : Node3D
{
    const float Gravity = 16f, HitSlow = 0.4f, HitSlowTime = 2.5f;
    public static readonly Color MudColor = new(0.16f, 0.14f, 0.05f); // dark oily sludge: reads against dirt floors

    Vector3 _vel;
    float _damage, _life = 3f, _drip;
    readonly Godot.Collections.Array<Rid> _exclude = new();
    Player _source;

    static SphereMesh _mesh;
    static StandardMaterial3D _mat;

    public static StandardMaterial3D WetMud => _mat ??= new StandardMaterial3D { AlbedoColor = MudColor, Roughness = 0.08f, Metallic = 0.25f };

    public static void Fire(Vector3 pos, Vector3 dir, WeaponDef w, float damageMult, Rid ownerRid, Player source = null)
    {
        var g = new MudGlob
        {
            // slight upward lob so it arcs like a thrown blob instead of flying like a dart
            _vel = dir.Normalized() * w.ProjectileSpeed + Vector3.Up * 2.5f,
            _damage = w.Damage * damageMult,
            _source = source,
        };
        g._exclude.Add(ownerRid);
        Main.Instance.World.AddChild(g);
        g.GlobalPosition = pos;
    }

    public override void _Ready()
    {
        _mesh ??= new SphereMesh { Radius = 0.17f, Height = 0.3f, RadialSegments = 10, Rings = 5 };
        AddChild(new MeshInstance3D { Mesh = _mesh, MaterialOverride = WetMud });
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _life -= dt;
        if (_life <= 0) { QueueFree(); return; }
        _vel.Y -= Gravity * dt;
        var from = GlobalPosition;
        var to = from + _vel * dt;
        if (_vel.LengthSquared() > 0.01f) LookAt(to, Mathf.Abs(_vel.Normalized().Y) > 0.98f ? Vector3.Right : Vector3.Up);
        Scale = new Vector3(0.85f, 0.85f, 1.25f); // stretched along travel

        _drip -= dt;
        if (_drip <= 0) { _drip = 0.1f; Fx.Burst(from, MudColor, 1, 0.5f, 0.07f, 0.4f, Vector3.Down, 30f, 12f); }

        var space = GetWorld3D().DirectSpaceState;
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, 1 | 4, _exclude));
        if (hit.Count == 0) { GlobalPosition = to; return; }

        var pos = hit["position"].AsVector3();
        var n = hit["normal"].AsVector3();
        var dir = _vel.Normalized();
        Vector3? floor;
        if (hit["collider"].AsGodotObject() is Zombie z)
        {
            z.TakeDamage(_damage, pos, dir, z.IsHeadshot(hit["shape"].AsInt32()), 1.5f, source: _source);
            z.Slow(HitSlow, HitSlowTime);
            floor = MudPuddle.FindFloor(space, new Vector3(z.GlobalPosition.X, pos.Y, z.GlobalPosition.Z) + Vector3.Up * 0.5f);
        }
        else
        {
            // landed on the ground -> puddle right here; hit a wall -> slide down to the floor below
            floor = n.Y > 0.6f ? pos : MudPuddle.FindFloor(space, pos + n * 0.3f);
        }
        Fx.Burst(pos + n * 0.05f, MudColor, 12, 4f, 0.12f, 0.55f, n + Vector3.Up * 0.5f, 70f);
        Sfx.Play3D("splat", pos, -2f, 0.15f);
        if (floor.HasValue) MudPuddle.Spawn(floor.Value);
        QueueFree();
    }
}
