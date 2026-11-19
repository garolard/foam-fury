using Godot;

/// Lightweight paper-confetti simulation drawn with MultiMeshes: flat chips that tumble in full 3D,
/// curly streamers that twirl, and HDR glitter that twinkles. Bits shoot out, get caught by air drag,
/// then sway, bob and spiral as they drift down like real paper.
/// One instance lives under Main.World and is (re)created on demand.
public partial class ConfettiField : Node3D
{
    enum Kind { Chip, Streamer, Glitter }

    struct Bit
    {
        public Vector3 Pos, Vel, SpinAxis, Sway;
        public Basis Rot;
        public float Spin, Life, MaxLife, Size, Drag, Grav, Phase, Freq;
        public Color Col;
    }

    sealed class Pool
    {
        public Bit[] Items;
        public int Count;
        public float[] Buf;
        public MultiMesh Mm;
    }

    const int ChipCap = 3000, StreamerCap = 700, GlitterCap = 1200;
    const int Stride = 16; // 12 floats transform + 4 colour

    static ConfettiField _inst;
    static Mesh _chip, _streamer, _glitter;
    static StandardMaterial3D _paperMat, _glitterMat;

    readonly Pool[] _pools = new Pool[3];

    /// The flat paper chip and the curled ribbon, for anything else that wants to draw matching confetti.
    public static Mesh ChipMesh { get { BuildMeshes(); return _chip; } }
    public static Mesh StreamerMesh { get { BuildMeshes(); return _streamer; } }
    public static StandardMaterial3D PaperMaterial { get { BuildMeshes(); return _paperMat; } }

    static ConfettiField Get()
    {
        var world = Main.Instance?.World;
        if (world == null) return null;
        if (_inst == null || !IsInstanceValid(_inst) || _inst.GetParent() != world)
        {
            _inst = new ConfettiField();
            world.AddChild(_inst);
        }
        return _inst;
    }

    /// `count` bits fired from `pos` into a cone of `spread` degrees around `dir`, at up to `speed` m/s.
    /// `streamers` / `glitter` are the share of bits that become ribbons / sparkles instead of chips.
    public static void Emit(Vector3 pos, int count, float speed, float life, Vector3 dir, float spread, float size, float gravity,
        float streamers = 0.18f, float glitter = 0.2f)
    {
        if (Net.IsServer) return;
        var f = Get();
        if (f == null) return;
        dir = dir.LengthSquared() > 0.0001f ? dir.Normalized() : Vector3.Up;
        for (int i = 0; i < count; i++)
        {
            float r = GD.Randf();
            var kind = r < glitter ? Kind.Glitter : r < glitter + streamers ? Kind.Streamer : Kind.Chip;
            var v = ConeDir(dir, spread) * speed * Mathf.Lerp(0.35f, 1f, Mathf.Sqrt(GD.Randf()));
            f.Spawn(kind, pos, v, life * (float)GD.RandRange(0.7, 1.25), size, gravity);
        }
    }

    /// A single trailing bit (used by the Popper's shot to leave a sparkly wake).
    public static void Trail(Vector3 pos, Vector3 vel, float life, bool glitter)
    {
        if (Net.IsServer) return;
        Get()?.Spawn(glitter ? Kind.Glitter : Kind.Chip, pos, vel, life, glitter ? 1f : 0.8f, 2.5f);
    }

    void Spawn(Kind kind, Vector3 pos, Vector3 vel, float life, float size, float gravity)
    {
        var p = _pools[(int)kind];
        if (p.Count >= p.Items.Length) return;
        var c = Fx.FiestaColors[GD.RandRange(0, Fx.FiestaColors.Length - 1)];
        var spinAxis = RandUnit();
        float spin = (float)GD.RandRange(6, 16);
        if (kind == Kind.Streamer)
        {
            // ribbons twirl mostly around their long axis, which makes the curl ripple
            spinAxis = (Vector3.Back + RandUnit() * 0.35f).Normalized();
            spin = (float)GD.RandRange(8, 18);
        }
        if (kind == Kind.Glitter) c = c.Lerp(Colors.White, 0.35f) * 3.2f;
        else c = c * (float)GD.RandRange(0.85, 1.1);
        var sway = new Vector3(GD.Randf() - 0.5f, 0, GD.Randf() - 0.5f);
        p.Items[p.Count++] = new Bit
        {
            Pos = pos, Vel = vel, Rot = new Basis(RandUnit(), GD.Randf() * Mathf.Tau), SpinAxis = spinAxis, Spin = spin * (GD.Randf() < 0.5f ? -1 : 1),
            Life = life, MaxLife = life, Size = size * (float)GD.RandRange(0.7, 1.3),
            Drag = kind == Kind.Glitter ? (float)GD.RandRange(3.5, 5.5) : (float)GD.RandRange(2.2, 3.6),
            Grav = kind == Kind.Glitter ? gravity * 0.4f : gravity * (float)GD.RandRange(0.8, 1.2),
            Phase = GD.Randf() * Mathf.Tau, Freq = (float)GD.RandRange(4.5, 9), Sway = sway.LengthSquared() > 0.001f ? sway.Normalized() : Vector3.Right,
            Col = new Color(c.R, c.G, c.B, 1f),
        };
    }

    public override void _Ready()
    {
        BuildMeshes();
        _pools[(int)Kind.Chip] = MakePool(_chip, _paperMat, ChipCap);
        _pools[(int)Kind.Streamer] = MakePool(_streamer, _paperMat, StreamerCap);
        _pools[(int)Kind.Glitter] = MakePool(_glitter, _glitterMat, GlitterCap);
    }

    Pool MakePool(Mesh mesh, Material mat, int cap)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = cap,
            VisibleInstanceCount = 0, CustomAabb = new Aabb(new Vector3(-2000, -500, -2000), new Vector3(4000, 1000, 4000)),
        };
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        return new Pool { Items = new Bit[cap], Buf = new float[cap * Stride], Mm = mm };
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.05f);
        for (int k = 0; k < 3; k++) Step(_pools[k], (Kind)k, dt);
    }

    void Step(Pool p, Kind kind, float dt)
    {
        if (p.Count == 0 && p.Mm.VisibleInstanceCount == 0) return;
        var buf = p.Buf;
        for (int i = 0; i < p.Count; i++)
        {
            ref var b = ref p.Items[i];
            b.Life -= dt;
            if (b.Life <= 0) { p.Items[i] = p.Items[--p.Count]; i--; continue; }
            float age = b.MaxLife - b.Life;
            // once the air has caught it, paper starts to flutter: sideways sway that slowly spirals, plus a lift bob
            float calm = 1f - Mathf.Clamp(b.Vel.Length() / 5f, 0f, 1f);
            float s = Mathf.Sin(age * b.Freq + b.Phase);
            b.Sway = b.Sway.Rotated(Vector3.Up, dt * b.Freq * 0.35f);
            b.Vel += b.Sway * (s * 10f * calm * dt);
            b.Vel.Y += Mathf.Cos(age * b.Freq * 2f + b.Phase) * 3.5f * calm * dt - b.Grav * dt;
            b.Vel *= Mathf.Exp(-b.Drag * dt);
            b.Pos += b.Vel * dt;
            // tumble harder while fluttering, like a falling leaf
            b.Rot = (new Basis(b.SpinAxis, b.Spin * dt * (0.6f + calm * 0.8f)) * b.Rot).Orthonormalized();

            float fade = Mathf.Clamp(b.Life / Mathf.Min(0.35f, b.MaxLife * 0.4f), 0f, 1f);
            float sc = b.Size * Mathf.Min(1f, age * 12f + 0.2f) * fade;
            if (kind == Kind.Glitter) sc *= 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(age * 22f + b.Phase * 3f));
            var x = b.Rot.Column0 * sc; var y = b.Rot.Column1 * sc; var z = b.Rot.Column2 * sc;
            int o = i * Stride;
            buf[o] = x.X; buf[o + 1] = y.X; buf[o + 2] = z.X; buf[o + 3] = b.Pos.X;
            buf[o + 4] = x.Y; buf[o + 5] = y.Y; buf[o + 6] = z.Y; buf[o + 7] = b.Pos.Y;
            buf[o + 8] = x.Z; buf[o + 9] = y.Z; buf[o + 10] = z.Z; buf[o + 11] = b.Pos.Z;
            buf[o + 12] = b.Col.R; buf[o + 13] = b.Col.G; buf[o + 14] = b.Col.B; buf[o + 15] = 1f;
        }
        p.Mm.Buffer = buf;
        p.Mm.VisibleInstanceCount = p.Count;
    }

    static void BuildMeshes()
    {
        if (_chip != null) return;
        _paperMat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, Roughness = 0.38f, MetallicSpecular = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            RimEnabled = true, Rim = 0.4f, RimTint = 0.6f,
        };
        _glitterMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _chip = new PlaneMesh { Size = new Vector2(0.1f, 0.065f) };
        _glitter = Sparkle();
        _streamer = Ribbon();
    }

    /// A curled paper ribbon: a thin strip along Z that waves side to side and twists a little.
    static Mesh Ribbon()
    {
        const int Segs = 10;
        const float Len = 0.26f, W = 0.028f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < Segs; i++)
        {
            Vector3 A(int j, float side)
            {
                float t = (float)j / Segs;
                float twist = t * 1.6f;
                var across = new Vector3(Mathf.Cos(twist), Mathf.Sin(twist), 0) * side * W * 0.5f;
                return new Vector3(Mathf.Sin(t * Mathf.Tau * 1.25f) * 0.035f, Mathf.Cos(t * Mathf.Tau) * 0.012f, (t - 0.5f) * Len) + across;
            }
            Vector3 a = A(i, -1), b = A(i, 1), c = A(i + 1, -1), d = A(i + 1, 1);
            var n = (b - a).Cross(c - a).Normalized();
            st.SetNormal(n); st.AddVertex(a); st.SetNormal(n); st.AddVertex(b); st.SetNormal(n); st.AddVertex(c);
            st.SetNormal(n); st.AddVertex(b); st.SetNormal(n); st.AddVertex(d); st.SetNormal(n); st.AddVertex(c);
        }
        return st.Commit();
    }

    /// A four-pointed glint for glitter (billboarded).
    static Mesh Sparkle()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const float L = 0.07f, T = 0.014f;
        void Tri(Vector3 a, Vector3 b, Vector3 c) { st.AddVertex(a); st.AddVertex(b); st.AddVertex(c); }
        Tri(new Vector3(0, L, 0), new Vector3(T, 0, 0), new Vector3(-T, 0, 0));
        Tri(new Vector3(0, -L, 0), new Vector3(-T, 0, 0), new Vector3(T, 0, 0));
        Tri(new Vector3(L, 0, 0), new Vector3(0, -T, 0), new Vector3(0, T, 0));
        Tri(new Vector3(-L, 0, 0), new Vector3(0, T, 0), new Vector3(0, -T, 0));
        return st.Commit();
    }

    static Vector3 RandUnit()
    {
        var v = new Vector3(GD.Randf() * 2 - 1, GD.Randf() * 2 - 1, GD.Randf() * 2 - 1);
        return v.LengthSquared() > 0.0001f ? v.Normalized() : Vector3.Up;
    }

    /// Uniform random direction within `spreadDeg` degrees of `dir`.
    static Vector3 ConeDir(Vector3 dir, float spreadDeg)
    {
        float cosMax = Mathf.Cos(Mathf.DegToRad(Mathf.Clamp(spreadDeg, 0f, 180f)));
        float cos = Mathf.Lerp(1f, cosMax, GD.Randf());
        float sin = Mathf.Sqrt(Mathf.Max(0f, 1f - cos * cos));
        float phi = GD.Randf() * Mathf.Tau;
        var t = Mathf.Abs(dir.Y) < 0.99f ? dir.Cross(Vector3.Up).Normalized() : dir.Cross(Vector3.Right).Normalized();
        var u = dir.Cross(t);
        return (dir * cos + (t * Mathf.Cos(phi) + u * Mathf.Sin(phi)) * sin).Normalized();
    }
}
