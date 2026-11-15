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

    public abstract string Title { get; }
    public abstract string Subtitle { get; }
    /// Chance a zombie rises from the ground inside the map instead of entering from a spawn gate.
    public virtual float RiseChance(int wave) => wave >= 3 ? 0.28f : 0f;
    protected virtual float NavExtent => 45f;

    protected abstract void Build();

    public override void _Ready()
    {
        Build();
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
        float t = Time.GetTicksMsec() / 1000f;
        for (int i = 0; i < _flicker.Count; i++)
        {
            var (l, e, sp) = _flicker[i];
            l.LightEnergy = e * (0.85f + 0.15f * Mathf.Sin(t * 13f * sp + i * 1.7f) * Mathf.Sin(t * 7.3f * sp + i));
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
        if (collide) Assets.AddCollision(m, 1, convex);
        return m;
    }

    protected StaticBody3D Box(Vector3 center, Vector3 size, float rotY = 0, uint layer = 1)
    {
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
        var l = new OmniLight3D { LightColor = color, LightEnergy = energy, OmniRange = range, ShadowEnabled = shadows, Position = pos };
        AddChild(l);
        if (flicker) _flicker.Add((l, energy, flickerSpeed));
        return l;
    }

    protected void Flame(Vector3 pos, Color color, float size = 0.22f)
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

    protected void Environment(Color skyTop, Color horizon, Color groundHorizon, float ambient, Color sunColor, float sunEnergy,
        Vector3 sunRot, Color fogColor, float fogDensity, float glow = 0.7f, float exposure = 1.05f, Color? ambientColor = null)
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = skyTop, SkyHorizonColor = horizon, SkyCurve = 0.12f,
            GroundBottomColor = skyTop.Darkened(0.4f), GroundHorizonColor = groundHorizon,
            SunAngleMax = 25f, SunCurve = 0.08f,
        };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = ambientColor.HasValue ? Godot.Environment.AmbientSource.Color : Godot.Environment.AmbientSource.Sky,
            AmbientLightColor = ambientColor ?? Colors.White,
            AmbientLightEnergy = ambient,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = exposure,
            SsaoEnabled = true, SsaoIntensity = 1.6f,
            GlowEnabled = true, GlowIntensity = glow, GlowBloom = 0.05f, GlowHdrThreshold = 1.1f,
            FogEnabled = true, FogLightColor = fogColor, FogDensity = fogDensity, FogSkyAffect = 0.15f,
            AdjustmentEnabled = true, AdjustmentSaturation = 1.12f, AdjustmentContrast = 1.05f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D
        {
            LightColor = sunColor, LightEnergy = sunEnergy, ShadowEnabled = true,
            DirectionalShadowMaxDistance = 80f, RotationDegrees = sunRot,
        });
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
