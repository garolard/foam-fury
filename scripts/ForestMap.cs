using Godot;

/// "Pine Camp": an open forest clearing around a campfire. No walls; the dead come out of the trees from every side.
public partial class ForestMap : MapBase
{
    const float Edge = 34f;
    public override string Title => "PINE CAMP";
    public override string Subtitle => "Open clearing, no walls. They come from every side.";
    protected override float NavExtent => 46f;

    protected override void Build()
    {
        Environment(new Look
        {
            SkyTop = new Color(0.22f, 0.42f, 0.82f), Horizon = new Color(0.78f, 0.88f, 0.93f), Ground = new Color(0.3f, 0.4f, 0.3f),
            Cloud = Colors.White, Clouds = 0.55f, SunTint = new Color(1f, 0.95f, 0.8f),
            Sun = new Color(1f, 0.94f, 0.8f), SunEnergy = 1.45f, SunRot = new Vector3(-38f, 35f, 0), Ambient = 0.6f, Glow = 0.45f,
            AmbientColor = new Color(0.6f, 0.7f, 0.85f),
            Fog = new Color(0.62f, 0.75f, 0.8f), FogDensity = 0.003f,
            VolDensity = 0.006f, VolAlbedo = new Color(0.95f, 1f, 0.95f), VolAniso = 0.8f,
            Mist = new Color(0.9f, 0.95f, 0.92f), MistDensity = 0.18f, MistHeight = 1.2f,
            CloudShadows = true,
            Motes = new Color(1f, 0.95f, 0.6f), MoteSize = 0.035f, MoteCount = 200, MoteGravity = -0.03f, MoteGlow = 1.6f,
            Tint = new Color(1f, 1.02f, 0.98f),
        });
        GroundCollider();
        Plane(260, new Color(0.34f, 0.55f, 0.26f));

        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.Tau + 0.39f;
            GateSpawns.Add(new Vector3(Mathf.Cos(a) * Edge, 0, Mathf.Sin(a) * Edge));
        }
        Camp();
        Clearing();
        TreeLine();
        for (int i = 0; i < 16; i++)
        {
            float a = i / 16f * Mathf.Tau + 0.3f;
            float rad = i % 2 == 0 ? 20f : 12f;
            InnerSpawns.Add(new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad));
        }
        Boundary(44);
    }

    bool NearSpawn(Vector3 p, float r)
    {
        foreach (var s in GateSpawns) if (s.DistanceTo(p) < r) return true;
        return false;
    }

    void Camp()
    {
        // campfire
        for (int k = 0; k < 7; k++)
        {
            float a = k / 7f * Mathf.Tau;
            Place("forest", "stones", new Vector3(Mathf.Cos(a) * 1.1f, 0, Mathf.Sin(a) * 1.1f), Rng.RandfRange(0, 360), 0.9f, false);
        }
        Flame(new Vector3(0, 0.35f, 0), new Color(1f, 0.5f, 0.15f), 0.35f);
        Flame(new Vector3(0.15f, 0.7f, 0.05f), new Color(1f, 0.75f, 0.25f), 0.2f);
        Light(new Vector3(0, 1.2f, 0), new Color(1f, 0.55f, 0.2f), 3f, 10f, true, 1.2f, shadows: true);
        Box(new Vector3(0, 0.3f, 0), new Vector3(1.6f, 0.6f, 1.6f));

        Place("forest", "tent", new Vector3(-8f, 0, -6f), 30, 3.5f, true, true);
        Place("forest", "tent", new Vector3(8.5f, 0, -5f), -35, 3.5f, true, true);
        Place("forest", "tent", new Vector3(-9f, 0, 9f), 140, 3.5f, true, true);
        Place("forest", "flag", new Vector3(-3.5f, 0, -8f), 0, 3.5f, false);
        Place("kit", "crate-wide", new Vector3(3.5f, 0, -3f), 70, 2.8f, true, true);
        Place("kit", "crate-small", new Vector3(5f, 0, 9f), 10, 2.8f, true, true);
        Place("dungeon", "barrel", new Vector3(-5f, 0, 3f), 0, 2.6f, true, true);
        Place("dungeon", "barrel", new Vector3(-5.8f, 0, 4.2f), 0, 2.6f, true, true);
        Place("dungeon", "table", new Vector3(10f, 0, 3f), 90, 2.6f, true, true);
    }

    void Clearing()
    {
        // watchtower north-west
        var tw = new Vector3(-14f, 0, -14f);
        Place("forest", "building-platform", tw, 0, Tile);
        Place("forest", "building-structure", tw + Vector3.Up * 1.5f, 0, Tile);
        Place("forest", "building-roof", tw + Vector3.Up * 4.5f, 0, Tile, false);
        Place("forest", "ladder", tw + new Vector3(0, 1.5f, 2f), 0, Tile, false);
        Light(tw + new Vector3(0, 4f, 0), new Color(1f, 0.7f, 0.4f), 1.5f, 8f);
        // archery range north-east
        for (int k = 0; k < 3; k++) Place("forest", "target", new Vector3(10f + k * 3f, 0, -17f), 0, 3f, true, true);
        for (int k = 0; k < 4; k++) Place("forest", "fence", new Vector3(8.5f + k * 3.2f, 0, -12f), 0, 3f, true, true);
        // boulders east
        Place("forest", "rocks-high", new Vector3(18f, 1.5f, 4f), 20, Tile);
        Place("forest", "rocks-low", new Vector3(18.5f, 0, 7.5f), 80, Tile, true, true);
        Place("forest", "rocks-ramp", new Vector3(15f, 0, 4.5f), 270, Tile);
        // log platforms south-west
        Place("forest", "platform", new Vector3(-16f, 0, 12f), 0, Tile);
        Place("forest", "platform", new Vector3(-13f, 0, 12f), 0, Tile);
        Place("forest", "platform", new Vector3(-16f, 0, 15f), 0, Tile);
        // fence line south
        for (int k = 0; k < 3; k++) Place("forest", "fence", new Vector3(9f + k * 3.2f, 0, 16f), 0, 3f, true, true);
        Place("forest", "rocks-low", new Vector3(-2f, 0, -16f), 0, Tile, true, true);
        Place("forest", "rocks-low", new Vector3(2f, 0, 20f), 140, Tile, true, true);
        // trees inside the clearing
        var trees = new[] { new Vector3(-20, 0, -2), new Vector3(-12, 0, 2), new Vector3(13, 0, 12), new Vector3(20, 0, -10),
            new Vector3(-4, 0, 22), new Vector3(4, 0, -22), new Vector3(-22, 0, 18), new Vector3(23, 0, 20), new Vector3(-24, 0, -20) };
        foreach (var t in trees) Place("forest", Rng.Randf() < 0.5f ? "tree" : "tree-high", t, Rng.RandfRange(0, 360), 4f, true, true);
        for (int i = 0; i < 40; i++)
        {
            var p = new Vector3(Rng.RandfRange(-30, 30), 0, Rng.RandfRange(-30, 30));
            Place("forest", Rng.Randf() < 0.5f ? "patch-grass" : "plant", p, Rng.RandfRange(0, 360), Rng.RandfRange(2.5f, 4f), false);
        }
    }

    void TreeLine()
    {
        for (int i = 0; i < 160; i++)
        {
            float a = Rng.RandfRange(0, Mathf.Tau);
            float d = Rng.RandfRange(29f, 62f);
            var p = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
            if (NearSpawn(p, 5f)) continue;
            Place("forest", Rng.Randf() < 0.6f ? "tree-high" : "tree", p, Rng.RandfRange(0, 360), Rng.RandfRange(3.8f, 5.5f), d < 44f, true);
        }
    }
}
