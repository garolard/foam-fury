using Godot;

public enum DeathKind { FallBack, Crumple, Launch }

/// Wraps the Kenney animated character: applies a skin, wires idle/run/jump animations,
/// procedural pose layer, hand attachment, hit-flash and procedural death animations.
public partial class CharacterModel : Node3D
{
    static AnimationLibrary _lib;

    public AnimationPlayer Anim;
    public Skeleton3D Skeleton;
    public PoseModifier Pose;
    public BoneAttachment3D RightHand;
    public StandardMaterial3D Material;
    public bool Dying;
    string _current = "";
    float _flash;

    public CharacterModel(string skinPath, float scale, Color? tint = null)
    {
        var inst = Assets.Scene(GameData.SurvivorDir + "Model/characterMedium.fbx").Instantiate<Node3D>();
        AddChild(inst);
        Scale = Vector3.One * scale;

        Skeleton = inst.GetNode<Skeleton3D>("Root/Skeleton3D");
        var mesh = Skeleton.GetNode<MeshInstance3D>("characterMedium");
        Material = new StandardMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>(skinPath),
            AlbedoColor = tint ?? Colors.White,
            Roughness = 0.75f,
            RimEnabled = true, Rim = 0.55f, RimTint = 0.6f,
            EmissionEnabled = true,
            Emission = Colors.White,
            EmissionEnergyMultiplier = 0f,
        };
        mesh.MaterialOverride = Material;

        Anim = new AnimationPlayer();
        inst.AddChild(Anim);
        Anim.AddAnimationLibrary("", Library());

        Pose = new PoseModifier { Space = this };
        Skeleton.AddChild(Pose);

        RightHand = new BoneAttachment3D { BoneName = "RightHand" };
        Skeleton.AddChild(RightHand);
    }

    public static AnimationLibrary Library()
    {
        if (_lib != null) return _lib;
        _lib = new AnimationLibrary();
        foreach (var (file, src, name, loop) in new[] {
            ("idle", "Root|Idle", "idle", true), ("run", "Root|Run", "run", true), ("jump", "Root|Jump", "jump", false) })
        {
            var scn = Assets.Scene(GameData.SurvivorDir + $"Animations/{file}.fbx").Instantiate();
            var ap = scn.GetNode<AnimationPlayer>("AnimationPlayer");
            var a = (Animation)ap.GetAnimation(src).Duplicate();
            a.LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
            _lib.AddAnimation(name, a);
            scn.Free();
        }
        return _lib;
    }

    public void Play(string name, float speed = 1f, float blend = 0.2f)
    {
        Anim.SpeedScale = speed;
        if (_current == name) return;
        _current = name;
        Anim.Play(name, blend);
    }

    public void Flash(float amount = 1f) => _flash = Mathf.Max(_flash, amount);

    /// World-space direction -> model space (unit).
    public Vector3 ToModel(Vector3 world) => (GlobalBasis.Inverse() * world).Normalized();

    public override void _Process(double delta)
    {
        if (_flash > 0)
        {
            _flash = Mathf.Max(0, _flash - (float)delta * 8f);
            Material.EmissionEnergyMultiplier = _flash * _flash * 0.5f;
        }
    }

    /// Procedural death animation. `dir` is the world direction of the killing blow (body falls along it).
    /// Returns the duration until the body is at rest.
    public float PlayDeath(DeathKind kind, Vector3 dir, float power = 1f)
    {
        Dying = true;
        Play("idle", 0.15f, 0.15f);
        var flat = new Vector3(dir.X, 0, dir.Z);
        if (flat.LengthSquared() < 0.01f) flat = -GlobalBasis.Z;
        flat = flat.Normalized();
        float s = Scale.X;
        var q0 = Quaternion;
        var p0 = Position;
        var fwdWorld = new Vector3(GlobalBasis.Z.X, 0, GlobalBasis.Z.Z).Normalized();
        float side = GD.Randf() < 0.5f ? -1f : 1f;
        var pose = Pose;
        var tw = CreateTween();

        switch (kind)
        {
            case DeathKind.Crumple:
            {
                // knees buckle, slump, then pitch forward onto the face
                var axis = Vector3.Up.Cross(fwdWorld).Normalized();
                var qEnd = new Quaternion(axis, Mathf.DegToRad(84f)) * q0;
                float drop = 0.42f * s;
                tw.TweenMethod(Callable.From<float>(t =>
                {
                    float k = Mathf.SmoothStep(0, 1, t);
                    pose.LegsWeight = k;
                    pose.RightUpLeg = pose.LeftUpLeg = new Vector3(0, -0.55f, 0.85f).Normalized();
                    pose.RightLeg = pose.LeftLeg = new Vector3(0, -0.8f, -0.6f).Normalized();
                    pose.ArmsWeight = k * 0.8f;
                    pose.RightArm = new Vector3(-0.25f, -1f, 0.3f).Normalized();
                    pose.LeftArm = new Vector3(0.25f, -1f, 0.3f).Normalized();
                    pose.RightFore = pose.LeftFore = null;
                    pose.SpinePitch = 0.5f * k; pose.HeadPitch = 0.7f * k; pose.HeadRoll = 0.35f * side * k;
                    Position = p0 + Vector3.Down * drop * k;
                }), 0f, 1f, 0.32f).SetEase(Tween.EaseType.Out);
                tw.TweenInterval(0.08f);
                tw.TweenMethod(Callable.From<float>(t =>
                {
                    Quaternion = q0.Slerp(qEnd, t);
                    Position = p0 + Vector3.Down * drop * (1f - t) + fwdWorld * t * 0.9f * s;
                    pose.ArmsWeight = 0.8f;
                    pose.RightArm = new Vector3(-0.4f, 0.2f * t - 1f * (1 - t), 0.9f).Normalized();
                    pose.LeftArm = new Vector3(0.4f, 0.2f * t - 1f * (1 - t), 0.9f).Normalized();
                    pose.LegsWeight = 1f - t * 0.6f;
                }), 0f, 1f, 0.42f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
                Bounce(tw, axis, qEnd, 0.06f);
                return 0.32f + 0.08f + 0.42f + 0.2f;
            }
            case DeathKind.Launch:
            {
                // blown off the feet: arc through the air, flip, land spread-eagle
                var axis = Vector3.Up.Cross(flat).Normalized();
                float apex = 1.6f * power, dist = 3.2f * power;
                float spin = Mathf.Pi / 2f + Mathf.Tau * (power > 1.2f ? 1 : 0);
                var qEnd = new Quaternion(axis, Mathf.Pi / 2f) * q0;
                tw.TweenMethod(Callable.From<float>(t =>
                {
                    Position = p0 + flat * dist * t + Vector3.Up * 4f * apex * t * (1 - t);
                    Quaternion = new Quaternion(axis, spin * Mathf.SmoothStep(0, 1, t)) * q0;
                    float k = Mathf.Min(1, t * 4f);
                    pose.ArmsWeight = k; pose.LegsWeight = k;
                    pose.RightArm = new Vector3(-1f, 0.45f, -0.1f).Normalized();
                    pose.LeftArm = new Vector3(1f, 0.45f, -0.1f).Normalized();
                    pose.RightFore = pose.LeftFore = null;
                    pose.RightUpLeg = new Vector3(-0.45f, -1f, 0.1f).Normalized(); pose.RightLeg = pose.RightUpLeg;
                    pose.LeftUpLeg = new Vector3(0.45f, -1f, 0.1f).Normalized(); pose.LeftLeg = pose.LeftUpLeg;
                    pose.SpinePitch = -0.3f * k; pose.HeadPitch = -0.5f * k;
                }), 0f, 1f, 0.75f);
                tw.TweenCallback(Callable.From(() => Quaternion = qEnd));
                Bounce(tw, axis, qEnd, 0.1f);
                return 0.75f + 0.2f;
            }
            default:
            {
                // shot off the feet: stagger back, legs give way, slam down on the back
                var axis = Vector3.Up.Cross(flat).Normalized();
                var qEnd = new Quaternion(axis, Mathf.Pi / 2f) * q0;
                tw.TweenMethod(Callable.From<float>(t =>
                {
                    Quaternion = q0.Slerp(qEnd, t);
                    Position = p0 + flat * t * 0.6f * s + Vector3.Up * Mathf.Sin(t * Mathf.Pi) * 0.12f;
                    float k = Mathf.Min(1, t * 2.5f);
                    pose.ArmsWeight = k * 0.9f;
                    pose.RightArm = new Vector3(-0.7f, 0.25f, 0.65f).Normalized();
                    pose.LeftArm = new Vector3(0.7f, 0.35f, 0.6f).Normalized();
                    pose.RightFore = new Vector3(-0.3f, 0.6f, 0.75f).Normalized();
                    pose.LeftFore = new Vector3(0.3f, 0.7f, 0.65f).Normalized();
                    pose.LegsWeight = k * 0.85f;
                    pose.RightUpLeg = new Vector3(-0.1f, -0.55f, 0.85f).Normalized();
                    pose.RightLeg = new Vector3(0, -0.95f, 0.3f).Normalized();
                    pose.LeftUpLeg = new Vector3(0.12f, -1f, 0.15f).Normalized();
                    pose.LeftLeg = pose.LeftUpLeg;
                    pose.SpinePitch = 0.3f * k;
                    pose.HeadPitch = 0.45f * k - (t > 0.9f ? (t - 0.9f) * 9f : 0f);
                    pose.HeadRoll = 0.3f * side * k;
                }), 0f, 1f, 0.55f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
                Bounce(tw, axis, qEnd, 0.08f);
                return 0.55f + 0.2f;
            }
        }
    }

    /// Small impact bounce when the body hits the ground.
    void Bounce(Tween tw, Vector3 axis, Quaternion rest, float amount)
    {
        var up = new Quaternion(axis, -amount * 1.5f) * rest;
        tw.TweenProperty(this, "quaternion", up, 0.08f).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(this, "quaternion", rest, 0.12f).SetEase(Tween.EaseType.In);
    }
}
