using Godot;

public static class Sfx
{
    static Node Root => Main.Instance;

    public static void Play(string name, float db = 0f, float pitchVar = 0.05f)
    {
        var p = new AudioStreamPlayer { Stream = Assets.Audio(name), VolumeDb = db, Bus = "Master",
            PitchScale = 1f + (float)GD.RandRange(-pitchVar, pitchVar) };
        p.ProcessMode = Node.ProcessModeEnum.Always;
        Root.AddChild(p);
        p.Play();
        p.Finished += p.QueueFree;
    }

    public static void Play3D(string name, Vector3 pos, float db = 0f, float pitchVar = 0.08f, float pitch = 1f)
    {
        var p = new AudioStreamPlayer3D { Stream = Assets.Audio(name), VolumeDb = db, UnitSize = 12f, MaxDb = 4f,
            PitchScale = pitch * (1f + (float)GD.RandRange(-pitchVar, pitchVar)), AttenuationFilterCutoffHz = 12000 };
        Root.AddChild(p);
        p.GlobalPosition = pos;
        p.Play();
        p.Finished += p.QueueFree;
    }
}
