using Godot;

/// "Night Crypt": a foggy dungeon courtyard. Most of the dead claw their way up out of the graves.
public partial class CryptMap : MapBase
{
    const float Half = 18f;
    static readonly Color TorchColor = new(1f, 0.55f, 0.2f);
    public override string Title => "NIGHT CRYPT";
    public override string Subtitle => "Foggy graveyard keep. The dead rise from the ground.";
    public override float RiseChance(int wave) => 0.45f;

    protected override void Build()
    {
        Environment(new Look
        {
            SkyTop = new Color(0.02f, 0.04f, 0.07f), Horizon = new Color(0.14f, 0.28f, 0.28f), Ground = new Color(0.04f, 0.07f, 0.07f),
            Cloud = new Color(0.18f, 0.28f, 0.3f), Clouds = 0.55f, Stars = 0.6f, SunTint = new Color(0.7f, 0.9f, 1f), SunSize = 3f,
            Sun = new Color(0.6f, 0.8f, 1f), SunEnergy = 0.55f, SunRot = new Vector3(-55f, 140f, 0),
            Ambient = 0.55f, AmbientColor = new Color(0.45f, 0.55f, 0.6f), Glow = 0.9f, Exposure = 1.15f,
            Fog = new Color(0.2f, 0.36f, 0.32f), FogDensity = 0.008f,
            VolDensity = 0.028f, VolAlbedo = new Color(0.6f, 0.85f, 0.75f), VolAniso = 0.55f,
            Mist = new Color(0.55f, 0.78f, 0.68f), MistDensity = 0.9f, MistHeight = 2.0f, MistGlow = new Color(0.01f, 0.03f, 0.02f),
            Motes = new Color(0.55f, 1f, 0.45f), MoteSize = 0.06f, MoteCount = 110, MoteGravity = 0f, MoteGlow = 4f,
            Tint = new Color(0.95f, 1.03f, 1f),
        });
        GroundCollider();
        Plane(260, new Color(0.13f, 0.17f, 0.13f));
        Tiles("dungeon", "floor", "floor-detail", Half, 0.35f);
        Walls();
        Interior();
        Outside();
    }

    void Torch(Vector3 at, bool shadows = false)
    {
        Flame(at, TorchColor, 0.2f, small: true);
        Light(at + Vector3.Up * 0.2f, TorchColor, 2.6f, 10f, shadows: shadows);
    }

    void Walls()
    {
        Enclosure(Half, Tile,
            (i, p, rot) =>
            {
                Place("dungeon", "wall", p, rot);
                if (i % 4 == 1) Place("dungeon", "banner", p, rot + 180, Tile, false);
            },
            (basis, rot, off) =>
            {
                foreach (float gx in new[] { -Tile - 0.4f, Tile + 0.4f })
                {
                    var cp = basis * new Vector3(gx, 0, -off + 1f);
                    Place("dungeon", "column", cp, rot, 3.6f);
                    Torch(cp + Vector3.Up * 4.25f);
                }
                Place("dungeon", "gate", basis * new Vector3(-Tile * 1.6f, 0, -off - 2.2f), rot + 70, 3f, false);
                Place("dungeon", "rocks", basis * new Vector3(Tile * 2.4f, 0, -off - 3f), rot, 2.4f, true, true);
            });
        float o = Half + 1.5f;
        foreach (var c in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            Place("dungeon", "wall", new Vector3(c.X * o, 0, c.Y * o), 0, Tile);
            Place("dungeon", "wall-narrow", new Vector3(c.X * o, 3.3f, c.Y * o), 0, Tile, false);
        }
    }

    void Interior()
    {
        // L-shaped ruin walls in each quadrant create pockets of cover
        foreach (var c in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            Place("dungeon", "wall", new Vector3(c.X * 7.5f, 0, c.Y * 10.5f), 0, Tile);
            Place("dungeon", "wall-half", new Vector3(c.X * 10.5f, 0, c.Y * 10.5f), 0, Tile);
            Place("dungeon", "wall", new Vector3(c.X * 10.5f, 0, c.Y * 7.5f), 0, Tile);
            Torch(new Vector3(c.X * 10.5f, 3.6f, c.Y * 10.5f), true);
            Place("dungeon", "barrel", new Vector3(c.X * 13.5f, 0, c.Y * 13.8f), 0, 2.6f, true, true);
            Place("dungeon", "pot", new Vector3(c.X * 14.8f, 0, c.Y * 12.4f), Rng.RandfRange(0, 360), 2.6f, true, true);
        }
        // central shrine
        foreach (var c in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            Place("dungeon", "column", new Vector3(c.X * 3f, 0, c.Y * 3f), 0, 3f);
        Place("dungeon", "chest", new Vector3(0, 0, 0), 180, 2.6f, true, true);
        Place("dungeon", "coin", new Vector3(0.9f, 0, 0.8f), 30, 1.6f, false);
        Place("dungeon", "key", new Vector3(-0.8f, 0, 0.9f), 70, 1.6f, false);
        Light(new Vector3(0, 2.5f, 0), new Color(1f, 0.75f, 0.3f), 2.2f, 8f, true, 0.5f, shadows: true);
        // scaffolds east and west
        foreach (float sx in new[] { -1f, 1f })
        {
            Place("dungeon", "wood-structure", new Vector3(sx * 14.5f, 0, -1.5f), 0, Tile);
            Place("dungeon", "wood-support", new Vector3(sx * 14.5f, 0, 1.6f), 0, Tile);
            Place("dungeon", "barrel", new Vector3(sx * 14.5f, 0, -1.5f), 0, 2.4f, true, true);
            Place("dungeon", "table", new Vector3(sx * 7f, 0, -2f), 90, 2.6f, true, true);
            Place("dungeon", "chair", new Vector3(sx * 7f + 1.4f, 0, -2.4f), -90, 2.6f, true, true);
        }
        Place("dungeon", "rocks", new Vector3(5f, 0, -13.5f), 40, 2.6f, true, true);
        Place("dungeon", "stones", new Vector3(-4.5f, 0, -12f), 0, 2.6f, false);
        Place("dungeon", "trap", new Vector3(-6f, 0.01f, 4f), 0, 2.4f, false);
        Place("dungeon", "trap", new Vector3(6f, 0.01f, -6f), 0, 2.4f, false);
        Place("kit", "crate-medium", new Vector3(5.5f, 0, 5f), 15, 2.8f, true, true);
        Place("kit", "crate-small", new Vector3(-12.5f, 0, 4.5f), -30, 2.8f, true, true);

        // graves: rubble markers where zombies dig out
        var graves = new[]
        {
            new Vector3(-5f, 0, -4.5f), new Vector3(4.5f, 0, -9.5f), new Vector3(-9f, 0, -15f), new Vector3(9f, 0, -15f),
            new Vector3(-15f, 0, -8f), new Vector3(15f, 0, -8.5f), new Vector3(-15f, 0, 8.5f), new Vector3(15.5f, 0, 8f),
            new Vector3(-4.5f, 0, 15f), new Vector3(5f, 0, 15f), new Vector3(-10.5f, 0, 1.5f), new Vector3(10.5f, 0, 4.5f),
        };
        foreach (var g in graves)
        {
            Place("dungeon", "stones", g, Rng.RandfRange(0, 360), 2.2f, false);
            InnerSpawns.Add(g);
        }
    }

    void Outside()
    {
        for (int i = 0; i < 70; i++)
        {
            float a = Rng.RandfRange(0, Mathf.Tau);
            float d = Rng.RandfRange(32f, 60f);
            var p = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
            if ((Mathf.Abs(p.X) < 5f || Mathf.Abs(p.Z) < 5f) && d < 45f) continue;
            Place("forest", "tree-high", p, Rng.RandfRange(0, 360), Rng.RandfRange(3.5f, 5.5f), d < 40f, true);
        }
        for (int i = 0; i < 24; i++)
        {
            float a = Rng.RandfRange(0, Mathf.Tau);
            float d = Rng.RandfRange(23f, 34f);
            var p = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
            if (Mathf.Abs(p.X) < 6f || Mathf.Abs(p.Z) < 6f) continue;
            Place("dungeon", Rng.Randf() < 0.5f ? "stones" : "rocks", p, Rng.RandfRange(0, 360), 2.4f, false);
        }
        Boundary(62);
    }
}
