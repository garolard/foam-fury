using Godot;
using System.Collections.Generic;

/// One Fiesta Popper blast: a cone of paper bits that flutter out a short way (~9 m) and sting for light damage.
/// The first zombie the blast reaches is turned into a donkey Pinata (Brutes are too big and just get stung).
public partial class ConfettiShot : Node3D
{
    const float Drag = 3.2f, Gravity = 5f;

    sealed class Bit
    {
        public Vector3 Pos, Vel, SpinAxis, Side;
        public float Life, Age, Spin, Phase, Freq, Curl, Trail;
        public bool Ribbon;
        public MeshInstance3D Mesh;
    }
    readonly List<Bit> _bits = new();
    readonly Godot.Collections.Array<Rid> _exclude = new();
    float _damage, _blastMult;
    Player _source;
    /// A teammate's shot on our screen (online): looks the same, hurts nothing.
    bool _visual, _converted;
    Zombie _pinataZombie;

    static readonly Dictionary<Color, StandardMaterial3D> _mats = new();

    public static void Fire(Vector3 muzzle, IReadOnlyList<Vector3> dirs, WeaponDef w, float damageMult, float blastMult, Rid ownerRid,
        Player source = null, bool visual = false)
    {
        var s = new ConfettiShot { _damage = w.Damage * damageMult, _blastMult = blastMult, _source = source, _visual = visual };
        s._exclude.Add(ownerRid);
        foreach (var d in dirs)
            s._bits.Add(new Bit
            {
                Pos = muzzle, Vel = d.Normalized() * w.ProjectileSpeed * (float)GD.RandRange(0.75, 1.1),
                SpinAxis = new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f).Normalized(),
                Spin = (float)GD.RandRange(14, 26), Life = (float)GD.RandRange(0.4, 0.55),
                // each bit snakes along its own wavy path instead of flying dead straight
                Side = d.Cross(Mathf.Abs(d.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized().Rotated(d.Normalized(), GD.Randf() * Mathf.Tau),
                Phase = GD.Randf() * Mathf.Tau, Freq = (float)GD.RandRange(12, 20), Curl = (float)GD.RandRange(18, 34),
                Ribbon = GD.Randf() < 0.3f, Trail = GD.Randf() * 0.04f,
            });
        Main.Instance.World.AddChild(s);
        // the pop: a cone of loose confetti on top of the damaging bits
        var fwd = dirs.Count > 0 ? dirs[0] : Vector3.Forward;
        Fx.Confetti(muzzle, 40, w.ProjectileSpeed * 0.45f, 1.4f, fwd, 22f, 0.8f, 2.5f);
        ConfettiField.Emit(muzzle, 16, w.ProjectileSpeed * 0.3f, 0.6f, fwd, 35f, 1f, 1f, 0f, 1f); // glitter puff
    }

    public override void _Ready()
    {
        if (Net.IsServer) return;
        foreach (var b in _bits)
        {
            var c = Fx.FiestaColors[GD.RandRange(0, Fx.FiestaColors.Length - 1)];
            if (!_mats.TryGetValue(c, out var m))
                _mats[c] = m = new StandardMaterial3D
                {
                    AlbedoColor = c, Roughness = 0.38f, MetallicSpecular = 0.9f, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 0.6f,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled, RimEnabled = true, Rim = 0.4f, RimTint = 0.6f,
                };
            b.Mesh = new MeshInstance3D
            {
                Mesh = b.Ribbon ? ConfettiField.StreamerMesh : ConfettiField.ChipMesh, MaterialOverride = m, TopLevel = true,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(b.Mesh);
            b.Mesh.GlobalTransform = new Transform3D(new Basis(b.SpinAxis, GD.Randf() * Mathf.Tau).Scaled(Vector3.One * 1.6f), b.Pos);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var space = GetWorld3D().DirectSpaceState;
        int alive = 0;
        foreach (var b in _bits)
        {
            if (b.Life <= 0) continue;
            b.Life -= dt;
            if (b.Life <= 0) { b.Mesh?.Hide(); continue; }
            alive++;
            b.Age += dt;
            // sideways wiggle (perpendicular to the flight), fading as the bit slows down
            b.Vel += b.Side * (Mathf.Sin(b.Age * b.Freq + b.Phase) * b.Curl * dt);
            b.Vel *= Mathf.Exp(-Drag * dt);
            b.Vel.Y -= Gravity * dt;
            var to = b.Pos + b.Vel * dt;
            var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(b.Pos, to, 1 | 4, _exclude));
            if (hit.Count > 0)
            {
                b.Life = 0;
                b.Mesh?.Hide();
                var pos = hit["position"].AsVector3();
                if (hit["collider"].AsGodotObject() is Zombie z) HitZombie(z, pos, b.Vel.Normalized(), hit["shape"].AsInt32());
                else Fx.Confetti(pos, 4, 2f, 0.8f, hit["normal"].AsVector3(), 60f, 0.7f);
                continue;
            }
            b.Pos = to;
            if (b.Mesh != null)
            {
                float sc = 1.6f * Mathf.Clamp(b.Life / 0.15f, 0.2f, 1f);
                var rot = new Basis(b.SpinAxis, b.Spin * dt) * b.Mesh.GlobalBasis.Orthonormalized();
                b.Mesh.GlobalTransform = new Transform3D(rot.Scaled(Vector3.One * sc), to);
                // sparkly wake
                b.Trail -= dt;
                if (b.Trail <= 0)
                {
                    b.Trail = 0.035f;
                    ConfettiField.Trail(to, b.Vel * 0.08f, (float)GD.RandRange(0.25, 0.5), GD.Randf() < 0.75f);
                }
            }
        }
        if (alive == 0) QueueFree();
    }

    void HitZombie(Zombie z, Vector3 pos, Vector3 dir, int shape)
    {
        if (_visual || z.Dead || z == _pinataZombie) return;
        if (!_converted && Pinata.CanConvert(z))
        {
            _converted = true;
            _pinataZombie = z;
            z.Pinatafy(_source, _blastMult);
            return;
        }
        z.TakeDamage(_damage, pos, dir, z.IsHeadshot(shape), 0.6f, source: _source);
    }
}
