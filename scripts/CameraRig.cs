using Godot;

/// Over-the-shoulder third-person camera with shake, recoil and aim zoom.
public partial class CameraRig : Node3D
{
    public Node3D Target;
    public float Yaw, Pitch = -0.15f;
    public Camera3D Camera;
    public SpringArm3D Arm;
    public bool Aiming;
    public float Recoil;
    public float FovKick;   // 0..1 punch-out on dash

    Node3D _pitchNode;
    float _trauma, _t;
    float _aimBlend;

    public override void _Ready()
    {
        TopLevel = true;
        _pitchNode = new Node3D();
        AddChild(_pitchNode);
        Arm = new SpringArm3D { SpringLength = 4.0f, CollisionMask = 1, Margin = 0.2f, Position = new Vector3(0.9f, 0, 0) };
        Arm.Shape = new SphereShape3D { Radius = 0.25f };
        _pitchNode.AddChild(Arm);
        Camera = new Camera3D { Fov = 72f, Current = true, Far = 400f };
        Arm.AddChild(Camera);
    }

    public void AddTrauma(float t) => _trauma = Mathf.Min(1f, _trauma + t);

    public void Snap()
    {
        if (Target != null) GlobalPosition = Target.GlobalPosition + Vector3.Up * 1.55f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        if (Target != null && GodotObject.IsInstanceValid(Target))
        {
            var goal = Target.GlobalPosition + Vector3.Up * 1.55f;
            var p = GlobalPosition;
            p.X = goal.X; p.Z = goal.Z;
            p.Y = Mathf.Lerp(p.Y, goal.Y, Mathf.Min(1, dt * 12f));
            GlobalPosition = p;
        }
        Pitch = Mathf.Clamp(Pitch, -1.15f, 0.75f);
        Recoil = Mathf.Lerp(Recoil, 0f, Mathf.Min(1f, dt * 10f));
        Rotation = new Vector3(0, Yaw, 0);
        _pitchNode.Rotation = new Vector3(Pitch + Recoil, 0, 0);

        _aimBlend = Mathf.MoveToward(_aimBlend, Aiming ? 1f : 0f, dt * 6f);
        float a = Mathf.SmoothStep(0, 1, _aimBlend);
        Arm.SpringLength = Mathf.Lerp(4.0f, 2.6f, a);
        Arm.Position = new Vector3(Mathf.Lerp(0.9f, 1.05f, a), Mathf.Lerp(0f, 0.15f, a), 0);
        FovKick = Mathf.MoveToward(FovKick, 0f, dt * 3.5f);
        Camera.Fov = Mathf.Lerp(72f, 55f, a) + Mathf.SmoothStep(0, 1, FovKick) * 9f;

        _trauma = Mathf.Max(0, _trauma - dt * 1.6f);
        float s = _trauma * _trauma;
        Camera.HOffset = s * 0.35f * Noise(_t * 30f, 1);
        Camera.VOffset = s * 0.35f * Noise(_t * 30f, 2);
        Camera.Rotation = new Vector3(0, 0, s * 0.08f * Noise(_t * 25f, 3));
    }

    static float Noise(float t, int seed) => Mathf.Sin(t * 1.3f + seed * 17.1f) * 0.6f + Mathf.Sin(t * 2.7f + seed * 3.3f) * 0.4f;
}
