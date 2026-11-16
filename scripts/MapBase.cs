using Godot;
using System.Collections.Generic;

/// Shared building blocks for every map: placement, collisions, lighting, environment, navmesh.
public abstract partial class MapBase : NavigationRegion3D
{
    public const float Tile = 3f;
    public readonly List<Vector3> GateSpawns = new();
    public readonly List<Vector3> InnerSpawns = new();
    protected RandomNumberGenerator Rng = new() { Seed = 1337 };
    readonly List<(OmniLight3D light, float baseEnergy, float speed)> _flicker = new();
    readonly List<(Node3D node, Vector3 baseScale)> _flames = new();

    public abstract string Title { get; }
    public abstract string Subtitle { get; }
    /// Chance a zombie rises from the ground inside the map instead of entering from a spawn gate.
    public virtual float RiseChance(int wave) => wave >= 3 ? 0.28f : 0f;
    protected virtual float NavExtent => 45f;

    /// Full: playable. Backdrop: atmosphere only (menu background, no physics/navmesh).
    /// Miniature: bare geometry for the stage-select diorama (no environment, lights or physics).
    public enum BuildMode { Full, Backdrop, Miniature }
    public BuildMode Detail = BuildMode.Full;
    /// Miniature maps are clipped to this half-extent so they fit their plinth.
    public const float MiniExtent = 27f;

    protected abstract void Build();

    public override void _Ready()
    {
        Build();
        if (Detail != BuildMode.Full) return;
        NavigationMesh = new NavigationMesh
        {
            CellSize = 0.25f, CellHeight = 0.25f,
            GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
            GeometryCollisionMask = 1,
            AgentRadius = 0.75f, AgentHeight = 2.0f, AgentMaxClimb = 0.5f, AgentMaxSlope = 40f,
            FilterBakingAabb = new Aabb(new Vector3(-NavExtent, -2, -NavExtent), new Vector3(NavExtent * 2, 8, NavExtent * 2)),
        };
        BakeNavigationMesh(false);
    }

    public override void _Process(double delta)
    {
        if (_cloudShadow != null)
        {
            // slow drift that reverses every few minutes (a decal can't tile, so wrapping would pop)
            var p = _cloudShadow.Position + _cloudVel * (float)delta;
            if (Mathf.Abs(p.X) > 60) _cloudVel = -_cloudVel;
            _cloudShadow.Position = p;
        }
        float t = Time.GetTicksMsec() / 1000f;
        for (int i = 0; i < _flicker.Count; i++)
        {
            var (l, e, sp) = _flicker[i];
            l.LightEnergy = e * (0.85f + 0.15f * Mathf.Sin(t * 13f * sp + i * 1.7f) * Mathf.Sin(t * 7.3f * sp + i));
        }
        // flames lick: stretch/squash out of phase with each other and slowly turn so the twist reads as motion
        for (int i = 0; i < _flames.Count; i++)
        {
            var (n, s) = _flames[i];
            float k = Mathf.Sin(t * 11f + i * 2.3f) * Mathf.Sin(t * 6.1f + i * 0.9f);
            n.Scale = new Vector3(s.X * (1f - 0.08f * k), s.Y * (1f + 0.16f * k), s.Z * (1f - 0.08f * k));
            n.Rotation = new Vector3(0, n.Rotation.Y + (float)delta * (1.6f + (i % 3) * 0.5f), 0);
        }
    }

    // ------------------------------------------------------------ helpers
    protected Node3D Place(string pack, string name, Vector3 pos, float rotY = 0, float scale = Tile, bool collide = true, bool convex = false)
    {
        var m = Assets.Model(pack, name);
        AddChild(m);
        m.Position = pos;
        m.Rotation = new Vector3(0, Mathf.DegToRad(rotY), 0);
        m.Scale = Vector3.One * scale;
        if (collide && Detail == BuildMode.Full) Assets.AddCollision(m, 1, convex);
        if (Detail == BuildMode.Miniature && (Mathf.Abs(pos.X) > MiniExtent || Mathf.Abs(pos.Z) > MiniExtent)) m.Visible = false;
        return m;
    }

    protected StaticBody3D Box(Vector3 center, Vector3 size, float rotY = 0, uint layer = 1)
    {
        if (Detail != BuildMode.Full) return null;
        var b = new StaticBody3D { CollisionLayer = layer, CollisionMask = 0 };
        b.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(b);
        b.Position = center;
        b.Rotation = new Vector3(0, Mathf.DegToRad(rotY), 0);
        return b;
    }

    protected void GroundCollider(float size = 140f) => Box(new Vector3(0, -0.5f, 0), new Vector3(size, 1, size));

    protected MeshInstance3D Plane(float size, Color color, float y = -0.02f)
    {
        if (Detail == BuildMode.Miniature) size = Mathf.Min(size, MiniExtent * 2f);
        var m = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(size, size) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 1f },
            Position = new Vector3(0, y, 0),
        };
        AddChild(m);
        return m;
    }

    /// Floor tiles covering [-half, half]^2.
    protected void Tiles(string pack, string plain, string detail, float half, float detailChance = 0.3f)
    {
        int n = (int)(half * 2 / Tile);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            var p = new Vector3(-half + Tile * (x + 0.5f), 0, -half + Tile * (z + 0.5f));
            Place(pack, Rng.Randf() < detailChance ? detail : plain, p, 90 * Rng.RandiRange(0, 3), Tile, false);
        }
    }

    protected OmniLight3D Light(Vector3 pos, Color color, float energy, float range, bool flicker = true, float flickerSpeed = 1f, bool shadows = false)
    {
        var l = new OmniLight3D { LightColor = color, LightEnergy = energy, OmniRange = range, ShadowEnabled = shadows, Position = pos,
            LightVolumetricFogEnergy = 2f, ShadowBlur = 1.5f };
        AddChild(l);
        // light range ignores node scale, so a miniature's lamps would flood the whole showcase
        if (Detail == BuildMode.Miniature) { l.Visible = false; return l; }
        if (flicker) _flicker.Add((l, energy, flickerSpeed));
        return l;
    }

    /// Animated low-poly flame centred roughly on `pos`; `size` ~ the radius of the fire.
    protected void Flame(Vector3 pos, Color color, float size = 0.22f, bool small = false)
    {
        var h = size * (small ? 2.8f : 3.2f);
        var m = new MeshInstance3D
        {
            Mesh = Fx.FireMesh(small ? "flame-small" : "flame-cluster"), MaterialOverride = Fx.FireMat(color),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = pos - Vector3.Up * size * 0.9f,
            Scale = Vector3.One * h, Rotation = new Vector3(0, (float)GD.RandRange(0, Mathf.Tau), 0),
        };
        AddChild(m);
        _flames.Add((m, m.Scale));
    }

    /// Glowing lamp bulb (not fire).
    protected void Bulb(Vector3 pos, Color color, float size = 0.22f)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = size, Height = size * 2.2f }, MaterialOverride = Fx.Glow(color),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = pos,
        });
    }

    protected void GlowBar(Vector3 center, Vector3 size, Color color, float rotY = 0)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size }, MaterialOverride = Fx.Glow(color),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = center,
            Rotation = new Vector3(0, Mathf.DegToRad(rotY), 0),
        });
    }

    /// Wall gap that only blocks the player (zombies and navmesh ignore layer 16).
    protected void PlayerBlocker(Vector3 center, float width, float rotY) => Box(center, new Vector3(width, 6, 0.6f), rotY, 16);

    /// Invisible square boundary at +-half.
    protected void Boundary(float half)
    {
        for (int s = 0; s < 4; s++)
        {
            var basis = new Basis(Vector3.Up, s * Mathf.Pi / 2);
            Box(basis * new Vector3(0, 10, -half), new Vector3(half * 2 + 4, 20, 2), s * 90f);
        }
    }

    /// Everything that defines a map's lighting and atmosphere.
    public class Look
    {
        // sky
        public Color SkyTop, Horizon, Ground, Cloud = Colors.White, SunTint = new(1f, 0.85f, 0.65f);
        public float Clouds = 0.45f, CloudSpeed = 0.012f, Stars, SunSize = 1f;
        // light
        public Color Sun = Colors.White; public float SunEnergy = 1.3f; public Vector3 SunRot = new(-40, -50, 0);
        public float Ambient = 0.7f; public Color? AmbientColor;
        public float Glow = 0.7f, Exposure = 1.05f;
        // fog
        public Color Fog = Colors.Gray; public float FogDensity = 0.004f;
        public float VolDensity = 0.012f; public Color VolAlbedo = Colors.White; public Color VolEmission = Colors.Black; public float VolAniso = 0.65f;
        public Color? Mist; public float MistDensity = 0.6f, MistHeight = 2.5f; public Color MistGlow = Colors.Black;
        // dynamics
        public bool CloudShadows;
        public Color Motes = new(1f, 0.85f, 0.6f); public int MoteCount = 160; public float MoteSize = 0.05f, MoteGravity = 0.05f, MoteGlow = 2.5f;
        // screen grade
        public Color Tint = Colors.White; public Color Lift = Colors.Black;
    }

    public Look Style { get; private set; }
    Decal _cloudShadow;
    Vector3 _cloudVel = new(0.9f, 0, 0.35f);

    protected void Environment(Look l)
    {
        Style = l;
        if (Detail == BuildMode.Miniature) return;
        var skyMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky.gdshader") };
        skyMat.SetShaderParameter("top_color", l.SkyTop);
        skyMat.SetShaderParameter("horizon_color", l.Horizon);
        skyMat.SetShaderParameter("ground_color", l.Ground);
        skyMat.SetShaderParameter("cloud_color", l.Cloud);
        skyMat.SetShaderParameter("sun_tint", l.SunTint);
        skyMat.SetShaderParameter("cloud_coverage", l.Clouds);
        skyMat.SetShaderParameter("cloud_speed", l.CloudSpeed);
        skyMat.SetShaderParameter("stars", l.Stars);
        skyMat.SetShaderParameter("sun_size", l.SunSize);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = skyMat, ProcessMode = Sky.ProcessModeEnum.Realtime, RadianceSize = Sky.RadianceSizeEnum.Size256 },
            AmbientLightSource = l.AmbientColor.HasValue ? Godot.Environment.AmbientSource.Color : Godot.Environment.AmbientSource.Sky,
            AmbientLightColor = l.AmbientColor ?? Colors.White,
            AmbientLightEnergy = l.Ambient,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = l.Exposure,
            TonemapWhite = 6f,
            // contact shadows + bounce light so props sit in the scene instead of floating on it
            SsaoEnabled = true, SsaoIntensity = 2.2f, SsaoRadius = 1.4f, SsaoPower = 1.6f, SsaoLightAffect = 0.15f,
            SsilEnabled = true, SsilIntensity = 1.4f, SsilRadius = 6f,
            SsrEnabled = false,
            GlowEnabled = true, GlowIntensity = l.Glow, GlowBloom = 0.06f, GlowHdrThreshold = 1.0f, GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            FogEnabled = true, FogLightColor = l.Fog, FogDensity = l.FogDensity, FogSkyAffect = 0.1f, FogAerialPerspective = 0.4f,
            VolumetricFogEnabled = true, VolumetricFogDensity = l.VolDensity, VolumetricFogAlbedo = l.VolAlbedo,
            VolumetricFogEmission = l.VolEmission, VolumetricFogAnisotropy = l.VolAniso, VolumetricFogLength = 80f,
            VolumetricFogDetailSpread = 2f, VolumetricFogSkyAffect = 0.0f, VolumetricFogTemporalReprojectionEnabled = true,
            AdjustmentEnabled = true, AdjustmentSaturation = 1.15f, AdjustmentContrast = 1.08f,
        };
        AddChild(new WorldEnvironment
        {
            Environment = env,
            CameraAttributes = new CameraAttributesPractical
            {
                // gentle far blur gives depth without hurting readability at combat range
                DofBlurFarEnabled = true, DofBlurFarDistance = 38f, DofBlurFarTransition = 30f, DofBlurAmount = 0.05f,
            },
        });
        AddChild(new DirectionalLight3D
        {
            LightColor = l.Sun, LightEnergy = l.SunEnergy, ShadowEnabled = true, ShadowBlur = 1.5f,
            LightAngularDistance = 1.2f, DirectionalShadowMaxDistance = 80f, RotationDegrees = l.SunRot,
            LightVolumetricFogEnergy = 1.4f,
        });
        if (l.Mist.HasValue) GroundMist(l);
        Motes(l);
        if (l.CloudShadows) CloudShadows();
    }

    void GroundMist(Look l)
    {
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/mist.gdshader") };
        mat.SetShaderParameter("density", l.MistDensity);
        mat.SetShaderParameter("albedo", l.Mist.Value);
        mat.SetShaderParameter("emission", l.MistGlow);
        mat.SetShaderParameter("height", l.MistHeight);
        AddChild(new FogVolume
        {
            Size = new Vector3(110, l.MistHeight * 2f, 110), Shape = RenderingServer.FogVolumeShape.Box, Material = mat,
            Position = Vector3.Zero,
        });
    }

    /// Drifting dust / embers / fireflies / pollen around the play area.
    void Motes(Look l)
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(30, 3.5f, 30),
            Direction = Vector3.Up, Spread = 180f, InitialVelocityMin = 0.05f, InitialVelocityMax = 0.35f,
            Gravity = new Vector3(0, l.MoteGravity, 0),
            TurbulenceEnabled = true, TurbulenceNoiseStrength = 1.2f, TurbulenceNoiseScale = 6f, TurbulenceNoiseSpeed = new Vector3(0.1f, 0.05f, 0.1f),
            TurbulenceInfluenceMin = 0.05f, TurbulenceInfluenceMax = 0.15f,
            ScaleMin = 0.5f, ScaleMax = 1.4f,
        };
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(1, 1, 1, 0));
        ramp.SetColor(1, new Color(1, 1, 1, 0));
        ramp.AddPoint(0.2f, Colors.White);
        ramp.AddPoint(0.8f, Colors.White);
        pm.ColorRamp = new GradientTexture1D { Gradient = ramp };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(l.Motes.R * l.MoteGlow, l.Motes.G * l.MoteGlow, l.Motes.B * l.MoteGlow, 1f),
            AlbedoTexture = MoteTexture(),
        };
        var p = new GpuParticles3D
        {
            Amount = l.MoteCount, Lifetime = 9f, Preprocess = 9f, ProcessMaterial = pm,
            DrawPass1 = new QuadMesh { Size = Vector2.One * l.MoteSize * 2f, Material = mat },
            VisibilityAabb = new Aabb(new Vector3(-40, -5, -40), new Vector3(80, 20, 80)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = new Vector3(0, 3.5f, 0),
        };
        AddChild(p);
    }

    static Texture2D _mote;
    static Texture2D MoteTexture()
    {
        if (_mote != null) return _mote;
        var g = new Gradient();
        g.SetColor(0, Colors.White);
        g.SetColor(1, new Color(1, 1, 1, 0));
        _mote = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f), Width = 64, Height = 64 };
        return _mote;
    }

    /// Big projected noise decal that slides across the map: drifting cloud shadows.
    void CloudShadows()
    {
        var noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Frequency = 0.004f, FractalOctaves = 3 };
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(0, 0, 0, 0));
        ramp.SetColor(1, new Color(0, 0, 0.02f, 0.55f));
        ramp.SetOffset(0, 0.45f);
        ramp.SetOffset(1, 0.7f);
        var tex = new NoiseTexture2D { Noise = noise, Width = 512, Height = 512, Seamless = true, ColorRamp = ramp };
        _cloudShadow = new Decal
        {
            Size = new Vector3(260, 60, 260), TextureAlbedo = tex, AlbedoMix = 1f, Modulate = Colors.White,
            UpperFade = 0.05f, LowerFade = 0.05f, Position = new Vector3(0, 10, 0),
        };
        AddChild(_cloudShadow);
    }

    /// Four-sided walled enclosure with a gate gap (two tiles wide) centered on each side.
    /// wallAt(side, i, worldPos, rotY) places one wall segment; gateAt(side, basis, off) decorates the gate.
    protected void Enclosure(float half, float wallDepth, System.Action<int, Vector3, float> wallAt, System.Action<Basis, float, float> gateAt, float spawnOut = 9f)
    {
        int n = (int)(half * 2 / Tile);
        float off = half + wallDepth / 2f;
        for (int side = 0; side < 4; side++)
        {
            float rot = side * 90f;
            var basis = new Basis(Vector3.Up, Mathf.DegToRad(rot));
            for (int i = 0; i < n; i++)
            {
                if (i == n / 2 - 1 || i == n / 2) continue;
                wallAt(i, basis * new Vector3(-half + Tile * (i + 0.5f), 0, -off), rot);
            }
            gateAt?.Invoke(basis, rot, off);
            GateSpawns.Add(basis * new Vector3(0, 0, -off - spawnOut));
            PlayerBlocker(basis * new Vector3(0, 3, -off - 0.4f), Tile * 2, rot);
        }
    }

    public static MapBase Create(int index) => index switch
    {
        1 => new ArcadeMap(),
        2 => new CryptMap(),
        3 => new ForestMap(),
        _ => new Arena(),
    };

    public static readonly string[] Names = { "DUSK COLISEUM", "NEON ARCADE", "NIGHT CRYPT", "PINE CAMP" };
}
