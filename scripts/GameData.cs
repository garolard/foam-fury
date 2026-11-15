using Godot;

public class WeaponDef
{
    public string Name, Model, Sound;
    public float Damage, FireRate, ReloadTime, Spread, Recoil, Scale = 0.9f;
    public int MagSize, StartReserve, MaxReserve, Pellets = 1, Pierce;
    public bool Auto;
    public Color Tracer;
    public float ProjectileSpeed = 75f;
}

public class CharacterDef
{
    public string Name, Title, Perk, Skin;
    public float MaxHp = 100, Speed = 6.2f, DamageMult = 1, ReloadMult = 1, DashMult = 1, BlastMult = 1, DropMult = 1;
    public int Grenades = 3;
}

public static class GameData
{
    public const string Kit = "res://assets/kenney_blaster-kit_2/";
    public const string SurvivorDir = "res://assets/kenney_animated-characters-survivors/";
    public const string ProtagDir = "res://assets/kenney_animated-characters-protagonists/";

    public static readonly WeaponDef[] Weapons =
    {
        new WeaponDef { Name = "Popper", Model = "blaster-a", Sound = "shot_pistol", Damage = 24, FireRate = 0.16f, ReloadTime = 0.9f,
            Spread = 1.0f, Recoil = 1.2f, MagSize = 12, StartReserve = -1, MaxReserve = -1, Tracer = new Color(1f, 0.85f, 0.2f), Scale = 0.8f },
        new WeaponDef { Name = "Buzzsaw", Model = "blaster-m", Sound = "shot_smg", Damage = 13, FireRate = 0.075f, ReloadTime = 1.3f,
            Spread = 3.2f, Recoil = 0.6f, MagSize = 40, StartReserve = 120, MaxReserve = 320, Auto = true, Tracer = new Color(1f, 0.55f, 0.1f) },
        new WeaponDef { Name = "Twin Thunder", Model = "blaster-l", Sound = "shot_shotgun", Damage = 11, FireRate = 0.65f, ReloadTime = 1.5f,
            Spread = 8f, Recoil = 4f, MagSize = 6, StartReserve = 24, MaxReserve = 60, Pellets = 9, Tracer = new Color(0.75f, 0.4f, 1f) },
        new WeaponDef { Name = "Longshot", Model = "blaster-f", Sound = "shot_rifle", Damage = 75, FireRate = 0.42f, ReloadTime = 1.6f,
            Spread = 0.2f, Recoil = 3f, MagSize = 6, StartReserve = 24, MaxReserve = 48, Pierce = 4, Tracer = new Color(0.3f, 0.9f, 1f),
            ProjectileSpeed = 140f, Scale = 0.8f },
    };

    public static readonly CharacterDef[] Characters =
    {
        new CharacterDef { Name = "REX", Title = "The Bruiser", Perk = "+25% blaster damage", Skin = ProtagDir + "Skins/criminalMaleA.png",
            MaxHp = 120, DamageMult = 1.25f },
        new CharacterDef { Name = "NOVA", Title = "The Cyborg", Perk = "+60% max health", Skin = ProtagDir + "Skins/cyborgFemaleA.png",
            MaxHp = 160, Speed = 5.8f },
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

    public static int LoadBest(int map)
    {
        var cfg = new ConfigFile();
        return cfg.Load("user://save.cfg") == Error.Ok ? (int)cfg.GetValue("score", $"best_{map}", 0) : 0;
    }

    public static void SaveBest(int map, int score)
    {
        var cfg = new ConfigFile();
        cfg.Load("user://save.cfg");
        cfg.SetValue("score", $"best_{map}", score);
        cfg.Save("user://save.cfg");
    }
}
