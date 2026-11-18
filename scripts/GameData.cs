using Godot;
using System.Collections.Generic;

public enum FireKind { Dart, Mud, Flame }

public class WeaponDef
{
    public string Name, Model, Sound;
    /// One-line description for the survivor select screen.
    public string Blurb;
    /// Asset folder under res://assets ("kit" = Kenney blaster kit).
    public string Pack = "kit";
    /// HUD icon; defaults to the Kenney kit preview for the model.
    public string Icon;
    public FireKind Kind = FireKind.Dart;
    /// Reserve ammo granted by an ammo pickup.
    public int AmmoPickup;
    public float Damage, FireRate, ReloadTime, Spread, Recoil, Scale = 0.9f;
    public int MagSize, StartReserve, MaxReserve, Pellets = 1, Pierce;
    public bool Auto;
    public Color Tracer;
    public float ProjectileSpeed = 75f;

    public string IconPath => Icon ?? GameData.Kit + $"Previews/{Model}.png";
}

public class CharacterDef
{
    public string Name, Title, Perk, Skin;
    public float MaxHp = 100, Speed = 6.2f, DamageMult = 1, ReloadMult = 1, DashMult = 1, BlastMult = 1, DropMult = 1;
    public int Grenades = 3;
    /// Character-exclusive weapon, added as an extra slot after the shared blasters.
    public WeaponDef Signature;
}

public static class GameData
{
    public const string Kit = "res://assets/kenney_blaster-kit_2/";
    public const string SurvivorDir = "res://assets/kenney_animated-characters-survivors/";
    public const string ProtagDir = "res://assets/kenney_animated-characters-protagonists/";
    public const string CustomDir = "res://assets/custom-weapons/";

    public static readonly WeaponDef[] Weapons =
    {
        new WeaponDef { Name = "Popper", Model = "blaster-a", Sound = "shot_pistol", Damage = 24, FireRate = 0.16f, ReloadTime = 0.9f,
            Spread = 1.0f, Recoil = 1.2f, MagSize = 12, StartReserve = -1, MaxReserve = -1, Tracer = new Color(1f, 0.85f, 0.2f), Scale = 0.8f },
        new WeaponDef { Name = "Buzzsaw", Model = "blaster-m", Sound = "shot_smg", Damage = 13, FireRate = 0.075f, ReloadTime = 1.3f,
            Spread = 3.2f, Recoil = 0.6f, MagSize = 40, StartReserve = 120, MaxReserve = 320, Auto = true, Tracer = new Color(1f, 0.55f, 0.1f), AmmoPickup = 40 },
        new WeaponDef { Name = "Twin Thunder", Model = "blaster-l", Sound = "shot_shotgun", Damage = 11, FireRate = 0.65f, ReloadTime = 1.5f,
            Spread = 8f, Recoil = 4f, MagSize = 6, StartReserve = 24, MaxReserve = 60, Pellets = 9, Tracer = new Color(0.75f, 0.4f, 1f), AmmoPickup = 8 },
        new WeaponDef { Name = "Longshot", Model = "blaster-f", Sound = "shot_rifle", Damage = 75, FireRate = 0.42f, ReloadTime = 1.6f,
            Spread = 0.2f, Recoil = 3f, MagSize = 6, StartReserve = 24, MaxReserve = 48, Pierce = 4, Tracer = new Color(0.3f, 0.9f, 1f),
            ProjectileSpeed = 140f, Scale = 0.8f, AmmoPickup = 6 },
    };

    /// REX: lobs globs of sticky mud that leave slowing, flammable puddles.
    public static readonly WeaponDef MudGun = new()
    {
        Name = "Mug Gun", Model = "blaster-mug", Pack = "custom-weapons", Icon = CustomDir + "Previews/blaster-mug.png", Kind = FireKind.Mud,
        Blurb = "Lobs sticky mug that slows zombies. Puddles are flammable!",
        Sound = "shot_mud", Damage = 18, FireRate = 0.45f, ReloadTime = 1.6f, Spread = 1.5f, Recoil = 2.2f, MagSize = 8,
        StartReserve = 32, MaxReserve = 64, AmmoPickup = 8, Tracer = new Color(0.42f, 0.32f, 0.12f), ProjectileSpeed = 24f, Scale = 0.85f,
    };

    /// NOVA: short-range stream of fire that sets zombies (and mud puddles) ablaze.
    public static readonly WeaponDef Flamethrower = new()
    {
        Name = "Scorcher", Model = "blaster-flame", Pack = "custom-weapons", Icon = CustomDir + "Previews/blaster-flame.png", Kind = FireKind.Flame,
        Blurb = "Short-range fire stream. Sets zombies ablaze and ignites mug puddles.",
        Sound = "flame", Damage = 7, FireRate = 0.05f, ReloadTime = 2.0f, Spread = 4f, Recoil = 0.15f, MagSize = 100,
        StartReserve = 200, MaxReserve = 400, AmmoPickup = 100, Auto = true, Tracer = new Color(1f, 0.5f, 0.1f), ProjectileSpeed = 17f, Scale = 0.75f,
    };

    public static readonly CharacterDef[] Characters =
    {
        new CharacterDef { Name = "REX", Title = "The Bruiser", Perk = "+25% blaster damage", Skin = ProtagDir + "Skins/criminalMaleA.png",
            MaxHp = 120, DamageMult = 1.25f, Signature = MudGun },
        new CharacterDef { Name = "NOVA", Title = "The Cyborg", Perk = "+60% max health", Skin = ProtagDir + "Skins/cyborgFemaleA.png",
            MaxHp = 160, Speed = 5.8f, Signature = Flamethrower },
        new CharacterDef { Name = "KAI", Title = "The Skater", Perk = "+20% speed, longer dash", Skin = ProtagDir + "Skins/skaterMaleA.png",
            Speed = 7.4f, DashMult = 1.4f },
        new CharacterDef { Name = "MIA", Title = "Quick Hands", Perk = "Reloads 45% faster", Skin = ProtagDir + "Skins/skaterFemaleA.png",
            Speed = 6.6f, ReloadMult = 0.55f },
        new CharacterDef { Name = "JO", Title = "Demolitions", Perk = "+3 grenades, bigger blasts", Skin = SurvivorDir + "Skins/survivorFemaleA.png",
            MaxHp = 110, Grenades = 6, BlastMult = 1.4f },
        new CharacterDef { Name = "SAM", Title = "The Scavenger", Perk = "Zombies drop loot twice as often", Skin = SurvivorDir + "Skins/survivorMaleB.png",
            MaxHp = 110, DropMult = 2f },
    };

    // Persisted across scene reloads
    public static int SelectedCharacter = 0;
    public static int SelectedMap = 0;
    public static bool SkipMenu = false;
    /// Menu screen to open on (see MainMenu.Screen: 0 title, 1 survivor select, 2 stage select, 3 co-op lobby).
    public static int MenuStart = 0;

    /// Local co-op players for the next run (empty = single player). Survives scene reloads so RETRY keeps the team.
    public static readonly List<PlayerSlot> Slots = new();
    public static bool Coop => Slots.Count > 1;

    /// Why we're back at the server list (lost connection...), shown once.
    public static string NetMessage;

    const string ServersPath = "user://servers.cfg";

    /// Servers saved in the online browser.
    public static List<SavedServer> LoadServers()
    {
        var list = new List<SavedServer>();
        var cfg = new ConfigFile();
        if (cfg.Load(ServersPath) != Error.Ok) return list;
        foreach (var sec in cfg.GetSections())
            list.Add(new SavedServer((string)cfg.GetValue(sec, "name", ""), (string)cfg.GetValue(sec, "address", "")));
        return list;
    }

    public static void SaveServers(List<SavedServer> list)
    {
        var cfg = new ConfigFile();
        for (int i = 0; i < list.Count; i++)
        {
            cfg.SetValue($"server{i}", "name", list[i].Name);
            cfg.SetValue($"server{i}", "address", list[i].Address);
        }
        cfg.Save(ServersPath);
    }

    public static string LoadPlayerName()
    {
        var cfg = new ConfigFile();
        return cfg.Load("user://save.cfg") == Error.Ok ? (string)cfg.GetValue("profile", "name", "") : "";
    }

    public static void SavePlayerName(string name)
    {
        var cfg = new ConfigFile();
        cfg.Load("user://save.cfg");
        cfg.SetValue("profile", "name", name);
        cfg.Save("user://save.cfg");
    }

    static string BestKey(int map, bool coop) => coop ? $"coop_best_{map}" : $"best_{map}";

    public static int LoadBest(int map, bool coop = false)
    {
        var cfg = new ConfigFile();
        return cfg.Load("user://save.cfg") == Error.Ok ? (int)cfg.GetValue("score", BestKey(map, coop), 0) : 0;
    }

    public static void SaveBest(int map, int score, bool coop = false)
    {
        var cfg = new ConfigFile();
        cfg.Load("user://save.cfg");
        cfg.SetValue("score", BestKey(map, coop), score);
        cfg.Save("user://save.cfg");
    }
}

/// One local player: which pad drives them (-1 = any pad), whether they use keyboard+mouse, and their survivor.
public record PlayerSlot(int Pad, bool Kbm, int Character);

/// A server in the online browser: display name and "host[:port]".
public record SavedServer(string Name, string Address);
