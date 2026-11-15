using Godot;

/// Wraps the Kenney animated character: applies a skin, wires idle/run/jump animations,
/// arm-aim modifier, hand attachment and hit-flash.
public partial class CharacterModel : Node3D
{
    static AnimationLibrary _lib;

    public AnimationPlayer Anim;
    public Skeleton3D Skeleton;
    public ArmAimModifier Arms;
    public BoneAttachment3D RightHand;
    public StandardMaterial3D Material;
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
            Roughness = 0.85f,
            EmissionEnabled = true,
            Emission = Colors.White,
            EmissionEnergyMultiplier = 0f,
        };
        mesh.MaterialOverride = Material;

        Anim = new AnimationPlayer();
        inst.AddChild(Anim);
        Anim.AddAnimationLibrary("", Library());

        Arms = new ArmAimModifier { Influence = 0f };
        Skeleton.AddChild(Arms);

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

    public override void _Process(double delta)
    {
        if (_flash > 0)
        {
            _flash = Mathf.Max(0, _flash - (float)delta * 8f);
            Material.EmissionEnergyMultiplier = _flash * _flash * 0.5f;
        }
    }
}
