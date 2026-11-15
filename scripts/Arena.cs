using Godot;

/// "Dusk Coliseum": a walled sand arena with four open gates, ringed by forest.
public partial class Arena : MapBase
{
    public const float Half = 18f;
    public override string Title => "DUSK COLISEUM";
    public override string Subtitle => "Sunset sand arena. Four gates, lots of cover.";

    protected override void Build()
    {
        Environment(new Color(0.16f, 0.2f, 0.42f), new Color(0.98f, 0.58f, 0.38f), new Color(0.85f, 0.5f, 0.35f), 0.75f,
            new Color(1f, 0.8f, 0.6f), 1.5f, new Vector3(-32f, -50f, 0), new Color(0.78f, 0.52f, 0.45f), 0.006f);
        GroundCollider();
        Plane(260, new Color(0.36f, 0.5f, 0.27f));
        Plane(Half * 2 + 10, new Color(0.72f, 0.55f, 0.4f), -0.01f);
        Tiles("arena", "floor", "floor-detail", Half);
        BuildWalls();
        BuildInterior();
        BuildOutside();
        foreach (var c in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            Light(new Vector3(c.X * 10f, 3.5f, c.Y * 10f), new Color(1f, 0.6f, 0.3f), 1.6f, 12f);
    }

    void BuildWalls()
    {
        Enclosure(Half, 1.8f,
            (i, p, rot) =>
            {
                Place("arena", "wall", p, rot);
                var basis = new Basis(Vector3.Up, Mathf.DegToRad(rot));
                var local = basis.Inverse() * p;
                if (Rng.Randf() < 0.25f)
                    Place("arena", "banner", basis * new Vector3(local.X, 0.6f, -Half + 0.05f), rot, 2.6f, false);
                else if (Rng.Randf() < 0.18f)
                    Place("arena", "weapon-rack", basis * new Vector3(local.X, 0, -Half + 0.6f), rot, 2.2f, true, true);
            },
            (basis, rot, off) =>
            {
                foreach (float gx in new[] { -Tile - 0.3f, Tile + 0.3f })
                {
                    var cp = basis * new Vector3(gx, 0, -off);
                    Place("arena", "column", cp, rot, 4.2f);
                    Light(cp + Vector3.Up * 4.6f + basis * new Vector3(0, 0, 1.0f), new Color(1f, 0.55f, 0.25f), 2.5f, 9f);
                    Flame(cp + Vector3.Up * 4.45f, new Color(1f, 0.5f, 0.15f));
                }
                Place("arena", "bricks", basis * new Vector3(-Tile * 2.2f, 0, -off - 2.2f), rot + 30, 2.4f, true, true);
                Place("arena", "column-damaged", basis * new Vector3(Tile * 2.5f, 0, -off - 3f), rot, 3.5f);
            });
        float o = Half + 0.9f;
        foreach (var c in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            var p = new Vector3(c.X * o, 0, c.Y * o);
            Place("arena", "block", p, 0, 3.6f);
            Place("arena", "column", p + Vector3.Up * 1.8f, 0, 4.0f);
            Place("arena", "statue", p + Vector3.Up * 5.8f, Mathf.RadToDeg(Mathf.Atan2(-c.X, -c.Y)), 2.2f, false);
        }
    }

    void BuildInterior()
    {
        foreach (var bp in new[] { new Vector3(-0.75f, 0, -0.75f), new Vector3(0.75f, 0, -0.75f), new Vector3(-0.75f, 0, 0.75f), new Vector3(0.75f, 0, 0.75f) })
            Place("arena", "block", bp, 0, 1.5f);
        Place("arena", "statue", new Vector3(0, 0.75f, 0), 180, 2.4f);
        Place("arena", "trophy", new Vector3(0, 0.75f, 1.2f), 0, 1.6f, false);

        foreach (var c in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            var b = new Vector3(c.X * 10f, 0, c.Y * 10f);
            float r = Mathf.RadToDeg(Mathf.Atan2(c.X, c.Y));
            Place("arena", "column", b + new Vector3(c.X * 2.5f, 0, 0), r, 3.8f);
            Place("arena", "column-damaged", b + new Vector3(0, 0, c.Y * 2.5f), r + 45, 3.8f);
            Place("arena", "bricks", b + new Vector3(c.X * 0.6f, 0, c.Y * 0.6f), r, 2.6f, true, true);
            Place("arena", "tree", b + new Vector3(-c.X * 2.6f, 0, c.Y * 3.5f), Rng.RandfRange(0, 360), 3.4f, true, true);
        }

        var covers = new (string pack, string name, Vector3 pos, float rot, float s)[]
        {
            ("kit", "crate-wide", new Vector3(-6, 0, 4), 90, 2.8f),
            ("kit", "crate-medium", new Vector3(6.5f, 0, -4), 20, 2.8f),
            ("kit", "crate-small", new Vector3(-5, 0, -7), -15, 2.8f),
            ("kit", "crate-wide", new Vector3(5, 0, 8), 0, 2.8f),
            ("kit", "crate-medium", new Vector3(14, 0, 0), 90, 2.8f),
            ("kit", "crate-medium", new Vector3(-14, 0, 1), 90, 2.8f),
            ("kit", "crate-small", new Vector3(0, 0, -13), 0, 2.8f),
            ("kit", "crate-small", new Vector3(1, 0, 13.5f), 70, 2.8f),
            ("dungeon", "barrel", new Vector3(-6.8f, 0, 5.8f), 0, 2.6f),
            ("dungeon", "barrel", new Vector3(7.5f, 0, -5.5f), 0, 2.6f),
            ("dungeon", "barrel", new Vector3(-15.5f, 0, -14.5f), 0, 2.6f),
            ("dungeon", "barrel", new Vector3(15.5f, 0, 14.5f), 0, 2.6f),
            ("dungeon", "barrel", new Vector3(14.5f, 0, -15.5f), 0, 2.6f),
            ("dungeon", "chest", new Vector3(-15.5f, 0, 15.2f), 45, 2.4f),
            ("forest", "rocks-low", new Vector3(9, 0, 1), 30, 2.6f),
            ("forest", "rocks-low", new Vector3(-9.5f, 0, -1.5f), 200, 2.6f),
            ("forest", "stones", new Vector3(2, 0, 6), 10, 2.4f),
            ("forest", "stones", new Vector3(-3, 0, -5), 120, 2.4f),
        };
        foreach (var c in covers) Place(c.pack, c.name, c.pos, c.rot, c.s, true, c.name.StartsWith("crate") || c.name == "barrel" || c.name == "chest");

        for (int i = 0; i < 16; i++)
        {
            float a = i / 16f * Mathf.Tau + 0.2f;
            float rad = i % 2 == 0 ? 13.5f : 7.5f;
            InnerSpawns.Add(new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad));
        }
    }

    void BuildOutside()
    {
        for (int i = 0; i < 90; i++)
        {
            float a = Rng.RandfRange(0, Mathf.Tau);
            float d = Rng.RandfRange(30f, 60f);
            var p = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
            if ((Mathf.Abs(p.X) < 5f || Mathf.Abs(p.Z) < 5f) && d < 45f) continue;
            Place("forest", Rng.Randf() < 0.5f ? "tree-high" : "tree", p, Rng.RandfRange(0, 360), Rng.RandfRange(3.5f, 5f), d < 40f, true);
        }
        for (int i = 0; i < 30; i++)
        {
            float a = Rng.RandfRange(0, Mathf.Tau);
            float d = Rng.RandfRange(23f, 40f);
            var p = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
            if (Mathf.Abs(p.X) < 6f || Mathf.Abs(p.Z) < 6f) continue;
            string name = Rng.Randf() < 0.4f ? "rocks-low" : Rng.Randf() < 0.5f ? "plant" : "patch-grass";
            Place("forest", name, p, Rng.RandfRange(0, 360), Rng.RandfRange(2.5f, 3.5f), false);
        }
        Place("forest", "tent", new Vector3(-30, 0, 26), 40, 3.5f, true, true);
        Place("forest", "tent", new Vector3(31, 0, -27), -130, 3.5f, true, true);
        Place("forest", "flag", new Vector3(-27, 0, 29), 0, 3.5f, false);
        Boundary(62);
    }
}
