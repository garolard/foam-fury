using Godot;

/// Grenade type metadata shared by the HUD and the effects.
public static class GrenadeTypes
{
    public static readonly string[] Names = { "FRAG", "BLACK HOLE", "INCENDIARY", "SHOCKWAVE" };
    public static readonly string[] Blurbs = { "CLASSIC BIG BOOM", "SUCKS THEM IN, THEN POPS", "LEAVES A BURNING PATCH", "MASSIVE KNOCKBACK" };
    public static readonly Color[] Colors =
    {
        new(0.55f, 0.95f, 0.4f),  // frag
        new(0.72f, 0.38f, 1f),    // black hole
        new(1f, 0.55f, 0.15f),    // incendiary
        new(0.35f, 0.85f, 1f),    // shockwave
    };
}

/// Custom grenade visuals: the black hole vortex, its detonation and the shockwave blast.
public static class GrenadeFx
{
    static Node3D World => Main.Instance.World;

    // ---------------------------------------------------------------- shaders

    /// Log-spiral accretion disc, drawn additively on a 2x2 plane (UV 0..1).
    public static readonly Shader SpiralShader = new()
    {
        Code = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec4 inner_col : source_color = vec4(1.0, 0.85, 1.0, 1.0);
uniform vec4 outer_col : source_color = vec4(0.45, 0.1, 0.9, 1.0);
uniform float intensity = 1.0;
uniform float spin = 3.0;
uniform float arms = 3.0;
uniform float twist = 5.0;
uniform float inner = 0.18;
uniform float edge_ring = 0.0;
void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float r = length(p);
    float a = atan(p.y, p.x);
    float lr = log(max(r, 0.001));
    float s1 = sin(a * arms + lr * twist + TIME * spin);
    float s2 = sin(a * (arms + 2.0) + lr * twist * 1.6 + TIME * spin * 1.7 + 1.3);
    float band = smoothstep(0.1, 1.0, 0.5 + 0.5 * s1) * 0.75 + smoothstep(0.4, 1.0, 0.5 + 0.5 * s2) * 0.45;
    float radial = smoothstep(1.0, 0.6, r) * smoothstep(inner, inner + 0.15, r);
    float hot = smoothstep(0.65, inner, r);
    vec3 col = mix(outer_col.rgb, inner_col.rgb, hot);
    float ring = edge_ring * smoothstep(0.88, 0.96, r) * smoothstep(1.0, 0.96, r) * (0.7 + 0.3 * sin(a * 12.0 - TIME * 4.0));
    ALBEDO = col * ((band + 0.2) * radial * (1.0 + hot * 2.5) * intensity) + outer_col.rgb * ring * 1.5;
    ALPHA = 1.0;
}"
    };

    /// Sphere that refracts the screen behind it. `fill` 0 = only the rim shows (shock dome), 1 = whole disc (lens).
    public static readonly Shader DistortShader = new()
    {
        Code = @"shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never, shadows_disabled;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform vec4 color : source_color = vec4(0.5, 0.9, 1.0, 1.0);
uniform float strength = 0.04;
uniform float swirl = 0.0;
uniform float rim_power = 2.5;
uniform float rim_glow = 2.0;
uniform float fill = 0.0;
uniform float darken = 0.0;
uniform float fade = 1.0;
void fragment() {
    float nv = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    float rim = pow(1.0 - nv, rim_power);
    float k = smoothstep(0.0, 1.0, nv);
    vec2 n = NORMAL.xy * vec2(1.0, -1.0);
    float ang = swirl * k * k + TIME * swirl * 0.3;
    vec2 off = mat2(vec2(cos(ang), sin(ang)), vec2(-sin(ang), cos(ang))) * n * strength * mix(rim, k, fill) * fade;
    vec3 bg = texture(screen_tex, SCREEN_UV + off).rgb;
    ALBEDO = bg * (1.0 - darken * k * k) + color.rgb * rim * rim_glow * fade;
    ALPHA = mix(clamp(rim * 1.3 + 0.1, 0.0, 1.0), smoothstep(0.0, 0.35, nv), fill) * fade;
}"
    };

    /// Expanding ring on a flat plane that refracts and glows. `progress` 0..1 = ring radius in UV units.
    public static readonly Shader RingShader = new()
    {
        Code = @"shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform vec4 color : source_color = vec4(0.5, 0.9, 1.0, 1.0);
uniform float progress = 0.0;
uniform float width = 0.08;
uniform float strength = 0.035;
uniform float fade = 1.0;
void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float r = length(p);
    float d = (r - progress) / width;
    float ring = exp(-d * d * 2.0);
    float trail = step(r, progress) * smoothstep(0.0, progress + 0.001, r) * 0.25;
    vec2 dir = r > 0.001 ? p / r : vec2(0.0);
    vec3 wdir = (MODEL_MATRIX * vec4(dir.x, 0.0, dir.y, 0.0)).xyz;
    vec2 sdir = (VIEW_MATRIX * vec4(wdir, 0.0)).xy * vec2(1.0, -1.0);
    vec2 off = normalize(sdir + vec2(1e-5)) * ring * strength * fade;
    vec3 bg = texture(screen_tex, SCREEN_UV - off).rgb;
    float a = clamp(ring * 1.4 + trail, 0.0, 1.0) * fade * smoothstep(1.0, 0.92, r);
    ALBEDO = bg + color.rgb * (ring * 3.2 + trail) * fade;
    ALPHA = a;
}"
    };

    static ShaderMaterial Mat(Shader s, params (string, Variant)[] p)
    {
        var m = new ShaderMaterial { Shader = s };
        foreach (var (k, v) in p) m.SetShaderParameter(k, v);
        return m;
    }

    public static ShaderMaterial SpiralMat(params (string, Variant)[] p) => Mat(SpiralShader, p);
    public static ShaderMaterial DistortMat(params (string, Variant)[] p) => Mat(DistortShader, p);
    public static ShaderMaterial RingMat(params (string, Variant)[] p) => Mat(RingShader, p);

    static void Fade(Tween tw, ShaderMaterial m, string param, float from, float to, float time) =>
        tw.TweenMethod(Callable.From<float>(v => m.SetShaderParameter(param, v)), from, to, time);

    static Vector3 Floor(Vector3 pos) => MudPuddle.FindFloor(World.GetWorld3D().DirectSpaceState, pos + Vector3.Up, 4f) ?? pos;

    /// Flat expanding refraction ring on the ground.
    static void GroundRing(Vector3 floor, float radius, Color color, float time, float delay = 0f, float width = 0.08f, float strength = 0.035f)
    {
        var mat = RingMat(("color", color), ("progress", 0f), ("width", width), ("strength", strength), ("fade", 1f));
        var ring = new MeshInstance3D { Mesh = new PlaneMesh { Size = Vector2.One * 2f }, MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        World.AddChild(ring);
        ring.GlobalPosition = floor + Vector3.Up * 0.06f;
        ring.Scale = Vector3.One * radius * 1.15f;
        var tw = ring.CreateTween().SetParallel();
        if (delay > 0) tw.TweenInterval(delay);
        tw.Chain();
        tw.TweenMethod(Callable.From<float>(v => mat.SetShaderParameter("progress", v)), 0.05f, 0.95f, time)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tw.TweenMethod(Callable.From<float>(v => mat.SetShaderParameter("fade", v)), 1f, 0f, time).SetEase(Tween.EaseType.In);
        tw.Chain().TweenCallback(Callable.From(ring.QueueFree));
    }

    /// Refracting bubble that inflates and fades.
    static void Dome(Vector3 center, float radius, Color color, float time, float strength = 0.05f)
    {
        var mat = DistortMat(("color", color), ("strength", strength), ("rim_glow", 2.5f), ("fade", 1f));
        var dome = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16 }, MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        World.AddChild(dome);
        dome.GlobalPosition = center;
        dome.Scale = Vector3.One * 0.3f;
        var tw = dome.CreateTween().SetParallel();
        tw.TweenProperty(dome, "scale", Vector3.One * radius, time).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
        Fade(tw, mat, "fade", 1f, 0f, time);
        tw.Chain().TweenCallback(Callable.From(dome.QueueFree));
    }

    /// Smoke puffs that skid outward along the floor.
    static void DustSkirt(Vector3 floor, float radius, int count, float time)
    {
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.Tau * i / count + (float)GD.RandRange(-0.2, 0.2);
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var s = Assets.Model("kit", "smoke");
            World.AddChild(s);
            s.GlobalPosition = floor + dir * 0.6f + Vector3.Up * 0.2f;
            s.Scale = Vector3.One * 1.5f;
            s.RotationDegrees = new Vector3(0, (float)GD.RandRange(0, 360), 0);
            var tw = s.CreateTween().SetParallel();
            tw.TweenProperty(s, "global_position", floor + dir * radius * (float)GD.RandRange(0.85, 1.1) + Vector3.Up * 0.5f, time)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            tw.TweenProperty(s, "scale", Vector3.One * (float)GD.RandRange(4, 6), time * 0.6f).SetEase(Tween.EaseType.Out);
            tw.Chain().TweenProperty(s, "scale", Vector3.One * 0.01f, 0.45f);
            tw.Chain().TweenCallback(Callable.From(s.QueueFree));
        }
    }

    // ---------------------------------------------------------------- blasts

    /// Shockwave grenade: no fireball, just a hard concussive ring of air.
    public static void Shockwave(Vector3 pos, float radius)
    {
        Sfx.Play3D("explosion", pos, 3f, 0.05f, 0.65f);
        Sfx.Play3D("impact", pos, 2f, 0.05f, 0.7f);
        Main.Instance.Shake(pos, 0.9f, 1f, 1f, 0.5f);
        if (Net.IsServer) return; // headless server: effects are drawn by the clients
        var floor = Floor(pos);
        var col = GrenadeTypes.Colors[Grenade.Shockwave];
        Fx.Flash(pos + Vector3.Up, new Color(0.7f, 0.95f, 1f), 10f, radius * 2.5f, 0.18f);
        GroundRing(floor, radius, col, 0.45f, 0f, 0.07f, 0.05f);
        GroundRing(floor, radius * 0.8f, new Color(0.8f, 0.95f, 1f), 0.6f, 0.08f, 0.05f, 0.025f);
        Dome(pos + Vector3.Up * 0.3f, radius * 0.9f, col, 0.35f, 0.06f);
        DustSkirt(floor, radius, 10, 0.5f);
        Fx.Burst(floor + Vector3.Up * 0.2f, new Color(0.5f, 0.42f, 0.32f), 26, 12f, 0.18f, 0.6f, Vector3.Up, 85f, 18f);
        Fx.Burst(pos + Vector3.Up * 0.3f, new Color(0.85f, 0.97f, 1f), 16, 9f, 0.08f, 0.35f, Vector3.Up, 70f, 2f);
        // white-hot core blink
        var core = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f }, MaterialOverride = Fx.Glow(new Color(0.75f, 0.95f, 1f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        World.AddChild(core);
        core.GlobalPosition = pos + Vector3.Up * 0.3f;
        core.Scale = Vector3.One * 0.2f;
        var tw = core.CreateTween();
        tw.TweenProperty(core, "scale", Vector3.One * 0.9f, 0.05f);
        tw.TweenProperty(core, "scale", Vector3.One * 0.01f, 0.12f);
        tw.TweenCallback(Callable.From(core.QueueFree));
    }

    /// The vortex collapses and blows outward in violet.
    public static void BlackHoleDetonate(Vector3 pos, float radius)
    {
        Sfx.Play3D("explosion", pos, 5f, 0.05f, 0.8f);
        Main.Instance.Shake(pos, 0.8f, 0.9f, 1f, 0.5f);
        if (Net.IsServer) return;
        var col = GrenadeTypes.Colors[Grenade.BlackHole];
        var center = pos + Vector3.Up * BlackHoleVortex.Lift;
        var floor = Floor(pos);
        Fx.Flash(center, new Color(0.8f, 0.5f, 1f), 16f, radius * 3f, 0.45f);
        Fx.Burst(center, new Color(0.85f, 0.55f, 1f), 36, 16f, 0.14f, 0.6f, Vector3.Up, 180f, 4f);
        Fx.Burst(center, new Color(1f, 0.9f, 1f), 18, 10f, 0.08f, 0.4f, Vector3.Up, 180f, 2f);
        Fx.Burst(floor + Vector3.Up * 0.2f, new Color(0.3f, 0.22f, 0.3f), 20, 9f, 0.3f, 1.1f, Vector3.Up, 70f, 6f);
        GroundRing(floor, radius * 1.1f, col, 0.5f, 0f, 0.09f, 0.05f);
        Dome(center, radius * 0.85f, col, 0.4f, 0.07f);
        // bright violet fireball that flares and implodes back
        var ball = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f }, MaterialOverride = Fx.Glow(new Color(0.85f, 0.55f, 1f)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        World.AddChild(ball);
        ball.GlobalPosition = center;
        ball.Scale = Vector3.One * 0.1f;
        var tw = ball.CreateTween();
        tw.TweenProperty(ball, "scale", Vector3.One * radius * 0.35f, 0.08f).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(ball, "scale", Vector3.One * 0.01f, 0.25f).SetEase(Tween.EaseType.In);
        tw.TweenCallback(Callable.From(ball.QueueFree));
        DustSkirt(floor, radius, 8, 0.6f);
    }
}

/// The animated black hole: floating event horizon, lensing bubble, spinning accretion disc,
/// a ground vortex marking the pull radius and streaks + debris spiralling in.
public partial class BlackHoleVortex : Node3D
{
    public const float Lift = 1.1f;   // how high the singularity floats above the grenade
    const float CollapseTime = 0.45f;
    const int StreakCount = 140, DebrisCount = 34;

    public float Radius = 5.5f, Duration = 2.2f;
    float _t;
    Node3D _anchor;
    float _floorY;
    MeshInstance3D _core, _lens, _disc, _disc2, _ground;
    ShaderMaterial _coreMat, _lensMat, _discMat, _disc2Mat, _groundMat;
    OmniLight3D _light;
    MultiMesh _streakMM, _debrisMM;
    // per-particle polar state around the core: radius, angle, height, starting radius, age
    float[] _sr, _sa, _sy, _sr0, _sy0, _sAge;
    float[] _dr, _da, _dy, _dr0, _dy0, _dSpin;
    Vector3[] _dAxis;

    public override void _Ready()
    {
        TopLevel = true; // ignore the (spinning) grenade's transform
        _anchor = GetParent<Node3D>();
        var start = _anchor.GlobalPosition;
        _floorY = (MudPuddle.FindFloor(GetWorld3D().DirectSpaceState, start + Vector3.Up, 4f) ?? start).Y;
        GlobalPosition = start;
        var col = GrenadeTypes.Colors[Grenade.BlackHole];

        // event horizon: pitch black with a hot violet rim
        _coreMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = @"shader_type spatial;
render_mode unshaded;
uniform vec4 rim : source_color = vec4(0.75, 0.4, 1.0, 1.0);
uniform float intensity = 1.0;
void fragment() {
    float f = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
    ALBEDO = rim.rgb * f * 5.0 * intensity;
}"
            }
        };
        _coreMat.SetShaderParameter("rim", col);
        _core = Add(new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 32, Rings = 16 }, _coreMat);

        // gravitational lens around the core
        _lensMat = GrenadeFx.DistortMat(("color", col), ("strength", -0.06f), ("swirl", 2.2f), ("rim_power", 3f),
            ("rim_glow", 1.2f), ("fill", 1f), ("darken", 0.35f), ("fade", 1f));
        _lens = Add(new SphereMesh { Radius = 1.4f, Height = 2.8f, RadialSegments = 32, Rings = 16 }, _lensMat);

        // tight accretion disc, tilted a little so it reads from any camera angle
        _discMat = GrenadeFx.SpiralMat(("inner_col", new Color(1f, 0.85f, 1f)), ("outer_col", col), ("spin", 7f), ("arms", 3f),
            ("twist", 4f), ("inner", 0.24f), ("intensity", 1.3f));
        _disc = Add(new PlaneMesh { Size = Vector2.One * 2f }, _discMat);
        _disc.Rotation = new Vector3(0.3f, 0, 0.15f);
        _disc2Mat = GrenadeFx.SpiralMat(("inner_col", new Color(0.9f, 0.6f, 1f)), ("outer_col", new Color(0.3f, 0.05f, 0.6f)), ("spin", -4.5f),
            ("arms", 2f), ("twist", -6f), ("inner", 0.3f), ("intensity", 0.7f));
        _disc2 = Add(new PlaneMesh { Size = Vector2.One * 2f }, _disc2Mat);
        _disc2.Rotation = new Vector3(-0.5f, 0, -0.35f);

        // ground vortex = the pull radius
        _groundMat = GrenadeFx.SpiralMat(("inner_col", new Color(0.8f, 0.45f, 1f)), ("outer_col", new Color(0.25f, 0.05f, 0.5f)), ("spin", 3.5f),
            ("arms", 4f), ("twist", 3f), ("inner", 0.05f), ("intensity", 1.1f), ("edge_ring", 1.6f));
        _ground = new MeshInstance3D { Mesh = new PlaneMesh { Size = Vector2.One * 2f }, MaterialOverride = _groundMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_ground);

        _light = new OmniLight3D { LightColor = new Color(0.7f, 0.35f, 1f), LightEnergy = 0f, OmniRange = Radius * 1.6f, ShadowEnabled = false,
            LightVolumetricFogEnergy = 2f };
        AddChild(_light);

        // streaks of light spiralling in
        _streakMM = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = StreakCount,
            Mesh = new BoxMesh { Size = new Vector3(0.035f, 0.035f, 0.4f) } };
        AddChild(new MultiMeshInstance3D { Multimesh = _streakMM, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoColor = new Color(2.5f, 2.5f, 2.5f), NoDepthTest = false } });
        _sr = new float[StreakCount]; _sa = new float[StreakCount]; _sy = new float[StreakCount];
        _sr0 = new float[StreakCount]; _sy0 = new float[StreakCount]; _sAge = new float[StreakCount];
        for (int i = 0; i < StreakCount; i++) { SpawnStreak(i); _sr[i] = Mathf.Lerp(0.6f, _sr0[i], GD.Randf()); }

        // ground debris torn up and dragged in
        _debrisMM = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = DebrisCount,
            Mesh = new BoxMesh { Size = Vector3.One * 0.14f } };
        AddChild(new MultiMeshInstance3D { Multimesh = _debrisMM,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f } });
        _dr = new float[DebrisCount]; _da = new float[DebrisCount]; _dy = new float[DebrisCount];
        _dr0 = new float[DebrisCount]; _dy0 = new float[DebrisCount]; _dSpin = new float[DebrisCount]; _dAxis = new Vector3[DebrisCount];
        for (int i = 0; i < DebrisCount; i++)
        {
            SpawnDebris(i);
            _dr[i] = Mathf.Lerp(0.8f, _dr0[i], GD.Randf());
            float c = (float)GD.RandRange(0.18, 0.4);
            _debrisMM.SetInstanceColor(i, GD.Randf() < 0.5f ? new Color(c * 1.3f, c, c * 0.7f) : new Color(c, c, c * 1.1f));
        }

        Scale = Vector3.One;
        Update(0f);
        Fx.Flash(start + Vector3.Up * Lift, new Color(0.75f, 0.4f, 1f), 8f, Radius * 2f, 0.3f);
        Fx.Burst(start + Vector3.Up * 0.3f, new Color(0.3f, 0.2f, 0.25f), 14, 6f, 0.2f, 0.6f, Vector3.Up, 50f, 12f);
    }

    MeshInstance3D Add(Mesh mesh, Material mat)
    {
        var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(m);
        return m;
    }

    void SpawnStreak(int i)
    {
        _sr0[i] = _sr[i] = Radius * (float)GD.RandRange(0.55, 1.05);
        _sa[i] = GD.Randf() * Mathf.Tau;
        _sy0[i] = _sy[i] = (float)GD.RandRange(-Lift + 0.1f, 1.4f);
        _sAge[i] = 0f;
    }

    void SpawnDebris(int i)
    {
        _dr0[i] = _dr[i] = Radius * (float)GD.RandRange(0.5, 1.0);
        _da[i] = GD.Randf() * Mathf.Tau;
        _dy0[i] = _dy[i] = _floorY - GlobalPosition.Y - Lift + 0.07f; // on the floor, relative to the core
        _dSpin[i] = (float)GD.RandRange(4, 12);
        _dAxis[i] = new Vector3(GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f).Normalized();
    }

    public override void _Process(double delta) => Update((float)delta);

    void Update(float dt)
    {
        if (GodotObject.IsInstanceValid(_anchor)) GlobalPosition = _anchor.GlobalPosition;
        _t += dt;
        float grow = Mathf.Clamp(_t / 0.35f, 0f, 1f);
        grow = 1f + 2.7f * Mathf.Pow(grow - 1f, 3) + 1.7f * Mathf.Pow(grow - 1f, 2); // ease-out-back
        float collapse = Mathf.Clamp((_t - (Duration - CollapseTime)) / CollapseTime, 0f, 1f);
        float shrink = 1f - 0.75f * collapse * collapse;
        float pulse = 1f + 0.06f * Mathf.Sin(_t * 18f);
        var center = new Vector3(0, Lift * Mathf.Clamp(_t / 0.3f, 0f, 1f), 0);

        _core.Position = _lens.Position = _disc.Position = _disc2.Position = _light.Position = center;
        _core.Scale = Vector3.One * Mathf.Max(0.01f, grow * shrink * pulse * (1f + collapse * 0.4f));
        _lens.Scale = Vector3.One * Mathf.Max(0.01f, grow * (1f - 0.5f * collapse));
        _disc.Scale = Vector3.One * Mathf.Max(0.01f, grow * 1.9f * shrink);
        _disc2.Scale = Vector3.One * Mathf.Max(0.01f, grow * 1.5f * shrink);
        _disc.RotateY(dt * 0.8f);
        _disc2.RotateY(-dt * 0.5f);
        _ground.Position = new Vector3(0, _floorY - GlobalPosition.Y + 0.05f, 0);
        _ground.Scale = Vector3.One * Mathf.Max(0.01f, Radius * Mathf.Clamp(_t / 0.5f, 0f, 1f) * (1f - 0.6f * collapse));

        _coreMat.SetShaderParameter("intensity", 1f + collapse * 3f);
        _discMat.SetShaderParameter("intensity", 1.3f * (1f + collapse * 2.5f));
        _disc2Mat.SetShaderParameter("intensity", 0.7f * (1f + collapse * 2f));
        _groundMat.SetShaderParameter("intensity", 1.1f * (1f + collapse));
        _lensMat.SetShaderParameter("strength", -0.06f - collapse * 0.08f);
        _light.LightEnergy = grow * (2.5f + Mathf.Sin(_t * 23f) * 0.6f + collapse * 6f);

        float suck = 1f + collapse * 3f;
        for (int i = 0; i < StreakCount; i++)
        {
            float r = _sr[i];
            float inward = (1.5f + 9f / Mathf.Max(r, 0.4f)) * suck;
            float omega = 4.5f / Mathf.Max(r, 0.5f);
            _sr[i] = r - inward * dt;
            _sa[i] += omega * dt;
            _sAge[i] += dt;
            if (_sr[i] < 0.45f) { SpawnStreak(i); r = _sr[i]; }
            float frac = Mathf.Clamp(_sr[i] / _sr0[i], 0f, 1f);
            _sy[i] = Mathf.Lerp(0f, _sy0[i], frac * frac);
            var p = center + new Vector3(Mathf.Cos(_sa[i]) * _sr[i], _sy[i], Mathf.Sin(_sa[i]) * _sr[i]);
            var vel = new Vector3(-Mathf.Sin(_sa[i]), 0, Mathf.Cos(_sa[i])) * omega * _sr[i]
                      - new Vector3(Mathf.Cos(_sa[i]), 0, Mathf.Sin(_sa[i])) * inward;
            float fadeIn = Mathf.Clamp(_sAge[i] / 0.15f, 0f, 1f);
            float s = grow * fadeIn * Mathf.Lerp(1.2f, 0.4f, frac);
            var basis = Basis.LookingAt(vel.Normalized(), Vector3.Up) * Basis.FromScale(new Vector3(s, s, s * Mathf.Lerp(2.2f, 0.8f, frac)));
            _streakMM.SetInstanceTransform(i, new Transform3D(basis, p));
            float hot = 1f - frac;
            _streakMM.SetInstanceColor(i, new Color(0.45f + hot * 0.55f, 0.15f + hot * 0.7f, 1f) * (0.5f + hot));
        }

        for (int i = 0; i < DebrisCount; i++)
        {
            float r = _dr[i];
            float inward = (0.8f + 5f / Mathf.Max(r, 0.5f)) * suck;
            float omega = 2.8f / Mathf.Max(r, 0.6f);
            _dr[i] = r - inward * dt;
            _da[i] += omega * dt;
            if (_dr[i] < 0.5f) SpawnDebris(i);
            float frac = Mathf.Clamp(_dr[i] / _dr0[i], 0f, 1f);
            _dy[i] = Mathf.Lerp(0f, _dy0[i], Mathf.Pow(frac, 0.6f)); // lift off the floor as it nears the core
            var p = center + new Vector3(Mathf.Cos(_da[i]) * _dr[i], _dy[i], Mathf.Sin(_da[i]) * _dr[i]);
            float s = grow * Mathf.Lerp(0.3f, 1f, frac) * (0.7f + 0.6f * ((i * 37) % 10) / 10f);
            var basis = new Basis(_dAxis[i], _t * _dSpin[i]) * Basis.FromScale(Vector3.One * Mathf.Max(0.01f, s));
            _debrisMM.SetInstanceTransform(i, new Transform3D(basis, p));
        }
    }
}
