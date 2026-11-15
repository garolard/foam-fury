using Godot;
using System.Collections.Generic;

/// Loading/instancing helpers for the Kenney packs.
public static class Assets
{
    static readonly Dictionary<string, PackedScene> _scenes = new();

    public static PackedScene Scene(string path)
    {
        if (!_scenes.TryGetValue(path, out var s))
        {
            s = GD.Load<PackedScene>(path);
            _scenes[path] = s;
        }
        return s;
    }

    public static string Glb(string pack, string name) => $"res://assets/{pack}/Models/GLB format/{name}.glb";

    /// Instance a model from a pack. pack: "arena","forest","dungeon","kit","arcade"
    public static Node3D Model(string pack, string name)
    {
        string dir = pack switch
        {
            "arena" => "kenney_mini-arena",
            "forest" => "kenney_mini-forest_1",
            "dungeon" => "kenney_mini-dungeon",
            "kit" => "kenney_blaster-kit_2",
            "arcade" => "kenney_mini-arcade",
            _ => pack
        };
        return Scene(Glb(dir, name)).Instantiate<Node3D>();
    }

    /// Adds static trimesh collision to every mesh in the model.
    public static void AddCollision(Node node, uint layer = 1, bool convex = false)
    {
        foreach (var mi in FindMeshes(node))
        {
            if (convex) mi.CreateConvexCollision(true, true);
            else mi.CreateTrimeshCollision();
            foreach (var c in mi.GetChildren())
                if (c is StaticBody3D sb) { sb.CollisionLayer = layer; sb.CollisionMask = 0; }
        }
    }

    public static List<MeshInstance3D> FindMeshes(Node n, List<MeshInstance3D> acc = null)
    {
        acc ??= new List<MeshInstance3D>();
        if (n is MeshInstance3D mi) acc.Add(mi);
        foreach (var c in n.GetChildren()) FindMeshes(c, acc);
        return acc;
    }

    static readonly Dictionary<string, AudioStream> _audio = new();
    public static AudioStream Audio(string name)
    {
        if (!_audio.TryGetValue(name, out var a))
        {
            a = GD.Load<AudioStream>($"res://audio/{name}.wav");
            _audio[name] = a;
        }
        return a;
    }
}
