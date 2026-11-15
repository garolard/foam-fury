using Godot;

/// "Neon Arcade": a roofless arcade hall at night, lit by neon. Zombies push in through the four doors.
public partial class ArcadeMap : MapBase
{
    const float Half = 18f;
    static readonly Color Magenta = new(1f, 0.2f, 0.85f), Cyan = new(0.2f, 0.9f, 1f), Yellow = new(1f, 0.8f, 0.2f), Lime = new(0.5f, 1f, 0.3f);
    static readonly Color[] Neon = { Magenta, Cyan, Yellow, Lime };

    public override string Title => "NEON ARCADE";
    public override string Subtitle => "Midnight arcade. Tight aisles between the cabinets.";

    protected override void Build()
    {
        Environment(new Color(0.03f, 0.02f, 0.08f), new Color(0.32f, 0.12f, 0.42f), new Color(0.2f, 0.08f, 0.3f), 0.6f,
            new Color(0.55f, 0.6f, 1f), 0.45f, new Vector3(-48f, 30f, 0), new Color(0.3f, 0.12f, 0.4f), 0.01f, 1.1f, 1.1f,
            new Color(0.45f, 0.4f, 0.65f));
        GroundCollider();
        Plane(260, new Color(0.13f, 0.12f, 0.15f));                     // parking lot asphalt
        Tiles("arcade", "floor", "floor", Half, 0f);
        Walls();
        Interior();
        Outside();
        for (int i = 0; i < 16; i++)
        {
            float a = i / 16f * Mathf.Tau + 0.1f;
            float rad = i % 2 == 0 ? 14f : 4.5f;
            InnerSpawns.Add(new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad));
        }
    }

    void Walls()
    {
        Enclosure(Half, 1.8f,
            (i, p, rot) => Place("arcade", i % 3 == 1 ? "wall-window" : "wall", p, rot + 180),
            (basis, rot, off) =>
            {
                var c = Neon[(int)(rot / 90) % 4];
                foreach (float gx in new[] { -Tile - 0.2f, Tile + 0.2f })
                    Place("arcade", "column", basis * new Vector3(gx, 0, -off), rot, 3.4f);
                // neon "EXIT" frame over the doorway
                GlowBar(basis * new Vector3(0, 3.4f, -off), new Vector3(Tile * 2 + 1.2f, 0.18f, 0.18f), c, rot);
                Light(basis * new Vector3(0, 3.0f, -off + 1.5f), c, 2.5f, 9f, true, 0.4f);
            });
        // neon trim along the inside of every wall
        for (int s = 0; s < 4; s++)
        {
            var basis = new Basis(Vector3.Up, s * Mathf.Pi / 2);
            var c = Neon[s];
            foreach (float x in new[] { -Half / 2 - Tile / 2, Half / 2 + Tile / 2 })
            {
                GlowBar(basis * new Vector3(x, 3.05f, -Half + 0.05f), new Vector3(Half - Tile * 1.6f, 0.12f, 0.12f), c, s * 90);
                GlowBar(basis * new Vector3(x, 0.9f, -Half + 0.05f), new Vector3(Half - Tile * 1.6f, 0.06f, 0.06f), c, s * 90);
            }
        }
        float o = Half + 0.9f;
        foreach (var c in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            Place("arcade", "column", new Vector3(c.X * o, 0, c.Y * o), 0, 3.6f);
    }

    void Interior()
    {
        // two back-to-back cabinet islands, north side
        foreach (float sx in new[] { -1f, 1f })
        {
            for (int k = 0; k < 5; k++)
            {
                float x = sx * (3.5f + k * 1.25f);
                Place("arcade", "arcade-machine", new Vector3(x, 0, -6.2f), 0, 2.6f, true, true);
                Place("arcade", "arcade-machine", new Vector3(x, 0, -7.9f), 180, 2.6f, true, true);
            }
            Light(new Vector3(sx * 6f, 3.2f, -7f), sx < 0 ? Magenta : Cyan, 2.2f, 8f, true, 0.3f);
        }
        // pinball row, far north
        for (int k = -3; k <= 3; k++)
            if (k != 0) Place("arcade", "pinball", new Vector3(k * 2.2f, 0, -14.5f), 0, 2.6f, true, true);
        // claw machines east/west
        foreach (float sx in new[] { -1f, 1f })
        {
            for (int k = 0; k < 3; k++)
                Place("arcade", "claw-machine", new Vector3(sx * 15.6f, 0, -3f + k * 2.2f), sx * -90f, 2.6f, true, true);
            Place("arcade", "air-hockey", new Vector3(sx * 8f, 0, 2.5f), 90, 2.6f, true, true);
            Place("arcade", "dance-machine", new Vector3(sx * 11f, 0, 10.5f), sx * -60f, 2.6f, true, true);
            Light(new Vector3(sx * 12f, 3.2f, 0f), sx < 0 ? Yellow : Lime, 2f, 9f, true, 0.3f);
            Light(new Vector3(sx * 9f, 3.2f, 11f), sx < 0 ? Cyan : Magenta, 2f, 9f, true, 0.3f);
        }
        // prize counter, north-west corner
        Place("arcade", "cash-register", new Vector3(-14.5f, 0, -12f), 90, 2.8f, true, true);
        Place("arcade", "prizes", new Vector3(-16.6f, 0, -14.5f), 90, 2.8f, true, true);
        Place("arcade", "prize-wheel", new Vector3(-12.5f, 0, -16.8f), 0, 2.8f, true, true);
        Place("arcade", "ticket-machine", new Vector3(-10.5f, 0, -16.6f), 0, 2.6f, true, true);
        // vending row north-east
        for (int k = 0; k < 3; k++) Place("arcade", "vending-machine", new Vector3(10.5f + k * 1.6f, 0, -16.8f), 0, 2.6f, true, true);
        // basketball games south-east, gambling south-west
        for (int k = 0; k < 2; k++) Place("arcade", "basketball-game", new Vector3(16f, 0, 14f - k * 2.8f), -90, 2.6f, true, true);
        for (int k = 0; k < 3; k++) Place("arcade", "gambling-machine", new Vector3(-16.4f, 0, 15.5f - k * 1.6f), 90, 2.6f, true, true);
        Light(new Vector3(-13f, 3f, -14f), Yellow, 2f, 8f, true, 0.3f);
        Light(new Vector3(14f, 3f, 14f), Magenta, 2f, 8f, true, 0.3f);
        Light(new Vector3(0f, 4f, 6f), new Color(0.7f, 0.6f, 1f), 1.2f, 12f, false);
        // a couple of loose crates of foam darts
        Place("kit", "crate-wide", new Vector3(5.5f, 0, 6f), 90, 2.8f, true, true);
        Place("kit", "crate-medium", new Vector3(-6f, 0, 11f), 30, 2.8f, true, true);
        Place("arcade", "column", new Vector3(-4.5f, 0, 1f), 0, 2.2f);
        Place("arcade", "column", new Vector3(4.5f, 0, -2f), 0, 2.2f);
    }

    void Outside()
    {
        // parking stripes and lamp posts
        for (int s = 0; s < 4; s++)
        {
            var basis = new Basis(Vector3.Up, s * Mathf.Pi / 2);
            for (int k = -4; k <= 4; k++)
            {
                if (Mathf.Abs(k) < 2) continue;
                AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.15f, 0.02f, 4.5f) },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.85f, 0.8f) },
                    Position = basis * new Vector3(k * 3f, 0.01f, -Half - 7f), Rotation = new Vector3(0, s * Mathf.Pi / 2, 0),
                });
            }
            foreach (float x in new[] { -12f, 12f })
            {
                var lp = basis * new Vector3(x, 0, -Half - 12f);
                Place("arcade", "column", lp, s * 90, 1.2f);
                Box(lp + Vector3.Up * 2.5f, new Vector3(0.3f, 5f, 0.3f));
                AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 5f },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.2f, 0.25f) }, Position = lp + Vector3.Up * 2.5f });
                Flame(lp + Vector3.Up * 5f, new Color(1f, 0.85f, 0.6f), 0.25f);
                Light(lp + Vector3.Up * 4.8f, new Color(1f, 0.8f, 0.55f), 2.5f, 12f, false);
            }
        }
        Place("arcade", "vending-machine", new Vector3(-22f, 0, 8f), 90, 2.6f, true, true);
        Place("arcade", "vending-machine", new Vector3(23f, 0, -9f), -90, 2.6f, true, true);
        // dark treeline far away
        for (int i = 0; i < 60; i++)
        {
            float a = Rng.RandfRange(0, Mathf.Tau);
            float d = Rng.RandfRange(42f, 62f);
            Place("forest", "tree-high", new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d), Rng.RandfRange(0, 360), Rng.RandfRange(3.5f, 5f), false);
        }
        Boundary(40);
    }
}
