using Godot;
using System.Collections.Generic;

public partial class Projectile : Node3D
{
    Vector3 _dir;
    float _speed, _damage, _life = 1.4f;
    int _pierce;
    Color _color;
    readonly Godot.Collections.Array<Rid> _exclude = new();
    readonly HashSet<Zombie> _hit = new();
    Player _source;

    static CylinderMesh _tracerMesh;

    public static void Fire(Vector3 pos, Vector3 dir, WeaponDef w, float damageMult, Rid ownerRid, Player source = null)
    {
        var p = new Projectile { _source = source };
        p._dir = dir.Normalized();
        p._speed = w.ProjectileSpeed;
        p._damage = w.Damage * damageMult;
        p._pierce = w.Pierce;
        p._color = w.Tracer;
        p._exclude.Add(ownerRid);
        Main.Instance.World.AddChild(p);
        p.GlobalPosition = pos;
        p.GlobalBasis = new Basis(new Quaternion(Vector3.Up, p._dir));
    }

    public override void _Ready()
    {
        var dart = Assets.Model("kit", "bullet-foam-tip");
        dart.Scale = Vector3.One * 5f;
        dart.RotationDegrees = new Vector3(180, 0, 0); // tip leading (+Y is travel)
        AddChild(dart);
        _tracerMesh ??= new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.005f, Height = 1.2f, RadialSegments = 6, Rings = 1 };
        var tr = new MeshInstance3D { Mesh = _tracerMesh, MaterialOverride = Fx.Glow(_color), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        tr.Position = new Vector3(0, -0.6f, 0);
        AddChild(tr);
        // each dart carries a little light so tracers paint the walls and zombies as they fly
        AddChild(new OmniLight3D { LightColor = _color, LightEnergy = 1.6f, OmniRange = 2.6f, ShadowEnabled = false, LightVolumetricFogEnergy = 0.6f });
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _life -= dt;
        if (_life <= 0) { QueueFree(); return; }
        var from = GlobalPosition;
        var to = from + _dir * _speed * dt;
        var space = GetWorld3D().DirectSpaceState;
        for (int guard = 0; guard < 6; guard++)
        {
            var q = PhysicsRayQueryParameters3D.Create(from, to, 1 | 4, _exclude);
            var hit = space.IntersectRay(q);
            if (hit.Count == 0) break;
            var pos = hit["position"].AsVector3();
            var col = hit["collider"].AsGodotObject();
            if (col is Zombie z)
            {
                if (!_hit.Contains(z))
                {
                    _hit.Add(z);
                    z.TakeDamage(_damage, pos, _dir, z.IsHeadshot(hit["shape"].AsInt32()), source: _source);
                }
                _exclude.Add(hit["rid"].AsRid());
                if (_pierce-- <= 0) { QueueFree(); return; }
                from = pos;
                continue;
            }
            // world
            var n = hit["normal"].AsVector3();
            Fx.Burst(pos + n * 0.05f, _color, 6, 4f, 0.07f, 0.35f, n, 50f);
            if (GD.Randf() < 0.5f) Fx.StuckDart(pos, _dir, _color);
            if (GD.Randf() < 0.3f) Sfx.Play3D("impact", pos, -10f, 0.3f);
            QueueFree();
            return;
        }
        GlobalPosition = to;
    }
}
