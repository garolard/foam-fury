using Godot;
using System.Collections.Generic;

/// Sticky, flammable mud left by the Mug Gun. Zombies wading through it are slowed.
/// Fire (flamethrower, burning zombies, grenades) ignites it; a burning puddle sets everything
/// inside ablaze and spreads to neighbouring puddles, so a trail of mud becomes a fuse.
public partial class MudPuddle : Node3D
{
    public static readonly List<MudPuddle> All = new();

    const int MaxPuddles = 24;
    const float Life = 14f, BurnTime = 5f, SlowMul = 0.45f, BurnDps = 22f, ZombieBurnTime = 3f;

    public float Radius = 1.5f;
    public bool Burning => _burnT >= 0;

    float _age, _burnT = -1f, _spreadCd;
    bool _dying;
    Node3D _visual;
    StandardMaterial3D _mat;
    CpuParticles3D _fire;
    OmniLight3D _light;
    /// Who lit this puddle (kill credit for the burn), if anyone.
    Player _igniter;

    public static Vector3? FindFloor(PhysicsDirectSpaceState3D space, Vector3 from, float depth = 8f)
    {
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * depth, 1));
        if (hit.Count == 0 || hit["normal"].AsVector3().Y < 0.6f) return null;
        return hit["position"].AsVector3();
    }

    public static void Spawn(Vector3 pos, float radius = 1.5f)
    {
        // landing on an existing puddle makes it bigger instead of stacking discs
        foreach (var p in All)
        {
            if (p._dying || Mathf.Abs(p.GlobalPosition.Y - pos.Y) > 0.5f) continue;
            if (Flat(p.GlobalPosition - pos) < p.Radius * 0.75f) { p.Grow(); return; }
        }
        while (All.Count >= MaxPuddles) All[0].Remove();
        var np = new MudPuddle { Radius = radius * (float)GD.RandRange(0.9, 1.1) };
        Main.Instance.World.AddChild(np);
        np.GlobalPosition = pos;
    }

    /// Ignite every puddle touched by a fire of the given radius at `pos`.
    public static void IgniteAt(Vector3 pos, float radius, Player source = null)
    {
        foreach (var p in All.ToArray())
            if (!p.Burning && p.Touches(pos, radius)) p.Ignite(source);
    }

    static float Flat(Vector3 v) => new Vector2(v.X, v.Z).Length();

    bool Touches(Vector3 point, float extra = 0f)
    {
        float dy = point.Y - GlobalPosition.Y;
        return dy > -0.8f - extra && dy < 1.2f + extra && Flat(point - GlobalPosition) < Radius + extra;
    }

    public override void _EnterTree() => All.Add(this);
    public override void _ExitTree() => All.Remove(this);

    public override void _Ready()
    {
        _mat = (StandardMaterial3D)MudGlob.WetMud.Duplicate();
        _visual = new Node3D { Position = Vector3.Up * 0.025f };
        AddChild(_visual);
        // a few overlapping blobs read as a splatter rather than a perfect disc
        var disc = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 0.04f, RadialSegments = 18, Rings = 1 };
        AddBlob(disc, Vector3.Zero, 1f);
        for (int i = 0; i < 4; i++)
        {
            float a = GD.Randf() * Mathf.Tau;
            AddBlob(disc, new Vector3(Mathf.Cos(a), 0.003f * (i + 1), Mathf.Sin(a)) * (float)GD.RandRange(0.55, 0.8), (float)GD.RandRange(0.3, 0.5));
        }
        _visual.Scale = new Vector3(0.2f, 1f, 0.2f) * Radius;
        _visual.CreateTween().TweenProperty(_visual, "scale", new Vector3(Radius, 1f, Radius), 0.18f)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
    }

    void AddBlob(Mesh mesh, Vector3 offset, float scale)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = offset, Scale = new Vector3(scale, 1f, scale) };
        _visual.AddChild(mi);
    }

    void Grow()
    {
        _age = Mathf.Min(_age, 2f);
        if (Radius >= 2.6f) return;
        Radius = Mathf.Min(2.6f, Radius + 0.3f);
        _visual.CreateTween().TweenProperty(_visual, "scale", new Vector3(Radius, 1f, Radius), 0.15f);
    }

    public void Ignite(Player source = null)
    {
        if (Burning || _dying) return;
        _igniter = source;
        _burnT = 0f;
        _spreadCd = 0.15f;
        _fire = new CpuParticles3D
        {
            Amount = (int)(28 * Radius), Lifetime = 0.7f, Mesh = new BoxMesh { Size = Vector3.One * 0.22f, Material = FlameJet.ParticleMaterial },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Ring, EmissionRingAxis = Vector3.Up, EmissionRingRadius = Radius * 0.85f,
            EmissionRingInnerRadius = 0f, EmissionRingHeight = 0.1f,
            Direction = Vector3.Up, Spread = 15f, Gravity = new Vector3(0, 4f, 0), InitialVelocityMin = 0.8f, InitialVelocityMax = 2.2f,
            ScaleAmountMin = 0.6f, ScaleAmountMax = 1.6f, ColorRamp = FlameJet.FireRamp, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = Vector3.Up * 0.15f,
        };
        AddChild(_fire);
        _light = new OmniLight3D { LightColor = new Color(1f, 0.5f, 0.15f), LightEnergy = 3f, OmniRange = Radius * 3.5f, ShadowEnabled = false,
            Position = Vector3.Up * 0.8f, LightVolumetricFogEnergy = 1.5f };
        AddChild(_light);
        Fx.Flash(GlobalPosition + Vector3.Up, new Color(1f, 0.55f, 0.15f), 6f, Radius * 4f, 0.25f);
        Fx.Burst(GlobalPosition + Vector3.Up * 0.2f, new Color(1f, 0.6f, 0.15f), 14, 6f, 0.18f, 0.5f, Vector3.Up, 50f, 4f);
        Sfx.Play3D("ignite", GlobalPosition, 0f, 0.15f);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_dying) return;
        float dt = (float)delta;
        var zombies = Main.Instance.Zombies;

        if (!Burning)
        {
            _age += dt;
            if (_age >= Life) { Remove(); return; }
            foreach (var z in zombies)
            {
                if (z.Dead || !Touches(z.GlobalPosition, 0.3f)) continue;
                z.Slow(SlowMul, 0.35f);
                if (z.Burning) { Ignite(z.BurnSource); break; } // a burning zombie wading in lights the mud
            }
            return;
        }

        _burnT += dt;
        float k = _burnT / BurnTime;
        _mat.AlbedoColor = MudGlob.MudColor.Lerp(new Color(0.06f, 0.05f, 0.05f), Mathf.Min(1, k * 1.5f)); // char as it burns
        _light.LightEnergy = (3f + Mathf.Sin(_burnT * 23f) * 0.7f) * (1f - k * k);
        foreach (var z in zombies)
        {
            if (z.Dead || !Touches(z.GlobalPosition, 0.3f)) continue;
            z.Slow(SlowMul, 0.35f);
            z.Ignite(BurnDps, ZombieBurnTime, _igniter);
        }
        // spread the fire along connected mud
        _spreadCd -= dt;
        if (_spreadCd <= 0)
        {
            _spreadCd = 0.15f;
            foreach (var p in All.ToArray())
                if (p != this && !p.Burning && p.Touches(GlobalPosition, Radius + 0.4f)) p.Ignite(_igniter);
        }
        if (_burnT >= BurnTime) Remove();
    }

    void Remove()
    {
        if (_dying) return;
        _dying = true;
        All.Remove(this);
        if (_fire != null) _fire.Emitting = false;
        var tw = CreateTween().SetParallel();
        tw.TweenProperty(_visual, "scale", new Vector3(0.01f, 1f, 0.01f), 0.6f);
        if (_light != null) tw.TweenProperty(_light, "light_energy", 0f, 0.4f);
        tw.Chain().TweenInterval(0.4f);
        tw.Chain().TweenCallback(Callable.From(QueueFree));
    }
}
