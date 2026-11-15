using Godot;
using System.Collections.Generic;

public partial class Main : Node3D
{
    public static Main Instance;
    public static Font TitleFont;

    public enum State { Menu, Playing, Paused, GameOver }
    public State Mode;

    public Node3D World;
    public MapBase Arena;
    public Player Player;
    public CameraRig Rig;
    public Hud Hud;
    public readonly List<Zombie> Zombies = new();

    // game stats
    public int Score, WaveNumber, Kills, ComboMult = 1;
    public int ZombiesRemaining => _toSpawn + Zombies.Count;
    public bool InBreak => _breakTimer > 0;
    public float BreakTimer => _breakTimer;
    int _toSpawn, _combo, _best, _bruteQueue;
    float _spawnTimer, _breakTimer, _comboTimer, _time;
    bool _waveActive;

    // menu
    MainMenu _menu;
    Camera3D _menuCam;
    int _map;
    Control _pausePanel, _overPanel;
    AudioStreamPlayer _music;
    float _menuT;
    readonly List<double> _shotTimes = new();
    string _debugShotPath;
    int _shotIdx;
    bool _auto;
    Camera3D _topCam;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        InputSetup.Init();
        TitleFont ??= new SystemFont { FontNames = new[] { "Impact", "Arial Black", "Helvetica Neue", "Arial" }, FontWeight = 800 };
        Fx.ClearDarts();
        Engine.TimeScale = 1;

        World = new Node3D { Name = "World", ProcessMode = ProcessModeEnum.Pausable };
        AddChild(World);
        _map = GameData.SelectedMap;
        if (OS.GetEnvironment("FF_MAP") is { Length: > 0 } fm) GameData.SelectedMap = _map = int.Parse(fm);
        if (OS.GetEnvironment("FF_CHAR") is { Length: > 0 } fc) GameData.SelectedCharacter = int.Parse(fc);
        if (OS.GetEnvironment("FF_PLAY") == "1") GameData.SkipMenu = true;
        BuildPostProcess();
        // the menu only needs the map as scenery; the playable version is built when a run starts
        LoadMap(GameData.SkipMenu ? MapBase.BuildMode.Full : MapBase.BuildMode.Backdrop);

        var music = (AudioStreamWav)Assets.Audio("music");
        music.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        music.LoopBegin = 0;
        music.LoopEnd = (int)(music.GetLength() * music.MixRate);
        _music = new AudioStreamPlayer { Stream = music, VolumeDb = -11f, ProcessMode = ProcessModeEnum.Always };
        AddChild(_music);
        _music.Play();

        var shot = OS.GetEnvironment("FF_SHOT");
        if (!string.IsNullOrEmpty(shot))
        {
            _debugShotPath = shot;
            foreach (var t in (OS.GetEnvironment("FF_SHOT_T") is { Length: > 0 } ts ? ts : "3").Split(',')) _shotTimes.Add(double.Parse(t, System.Globalization.CultureInfo.InvariantCulture));
        }
        _auto = OS.GetEnvironment("FF_AUTO") == "1";

        if (GameData.SkipMenu) StartGame();
        else ShowMenu();
    }

    ShaderMaterial _post;

    void BuildPostProcess()
    {
        var layer = new CanvasLayer { Layer = 1 };
        AddChild(layer);
        _post = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/post.gdshader") };
        var rect = new ColorRect { Material = _post, MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(rect);
    }

    void LoadMap(MapBase.BuildMode detail = MapBase.BuildMode.Full)
    {
        if (Arena != null) { World.RemoveChild(Arena); Arena.QueueFree(); }
        Arena = MapBase.Create(_map);
        Arena.Detail = detail;
        World.AddChild(Arena);
        _best = GameData.LoadBest(_map);
        _post.SetShaderParameter("tint", Arena.Style.Tint);
        _post.SetShaderParameter("lift", Arena.Style.Lift);
    }

    // ------------------------------------------------------------------ MENU
    void ShowMenu()
    {
        Mode = State.Menu;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _menuCam = new Camera3D { Fov = 50, Current = true };
        World.AddChild(_menuCam);
        _menu = new MainMenu();
        AddChild(_menu);
        _menu.BackdropChanged += m => { _map = m; LoadMap(MapBase.BuildMode.Backdrop); };
        _menu.Finished += (c, m) =>
        {
            GameData.SelectedCharacter = c;
            GameData.SelectedMap = _map = m;
            _menu.QueueFree(); _menu = null;
            _menuCam.QueueFree(); _menuCam = null;
            LoadMap();
            StartGame();
        };
        int start = OS.GetEnvironment("FF_MENU") is { Length: > 0 } fs ? int.Parse(fs) : GameData.MenuStart;
        _menu.Open((MainMenu.Screen)start);
    }

    void MenuProcess(float dt)
    {
        // slow high orbit over the selected stage
        _menuT += dt;
        float a = 0.6f + _menuT * 0.04f;
        _menuCam.GlobalPosition = new Vector3(Mathf.Sin(a) * 16f, 10f, Mathf.Cos(a) * 16f);
        _menuCam.LookAt(new Vector3(0, 1f, 0), Vector3.Up);
    }

    // ------------------------------------------------------------------ GAME
    void StartGame()
    {
        Mode = State.Playing;
        GameData.SkipMenu = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        Rig = new CameraRig();
        World.AddChild(Rig);
        Rig.Yaw = 0; Rig.Pitch = -0.12f;
        Player = new Player();
        Player.Setup(GameData.Characters[GameData.SelectedCharacter], Rig);
        World.AddChild(Player);
        Player.GlobalPosition = new Vector3(0, 0.1f, 6f);
        if (OS.GetEnvironment("FF_GOD") == "1") { Player.MaxHp = Player.Hp = 99999; }
        if (OS.GetEnvironment("FF_WAVE") is { Length: > 0 } fw) WaveNumber = int.Parse(fw) - 1;
        Rig.Snap();
        Hud = new Hud();
        AddChild(Hud);
        Hud.Init(Player);
        _breakTimer = 3.5f;
        Hud.Banner("SURVIVE", "hold the arena as long as you can", 1.6f);
    }

    void StartWave()
    {
        WaveNumber++;
        _waveActive = true;
        _toSpawn = Mathf.Min(8 + WaveNumber * 4, 90);
        _bruteQueue = WaveNumber >= 3 ? (WaveNumber - 1) / 2 : 0;
        if (WaveNumber % 5 == 0) _bruteQueue += 2;
        _spawnTimer = 0.5f;
        bool boss = WaveNumber % 5 == 0;
        Hud.Banner($"WAVE {WaveNumber}", boss ? "BRUTE HORDE INCOMING" : WaveNumber == 1 ? "here they come" : $"{_toSpawn} zombies", 1.8f);
        Sfx.Play("wave_start", -2f);
        InputSetup.Rumble(0.3f, 0.3f, 0.4f);
    }

    void SpawnZombie()
    {
        _toSpawn--;
        ZombieType type = ZombieType.Walker;
        float runnerChance = WaveNumber >= 2 ? Mathf.Min(0.12f + 0.04f * WaveNumber, 0.38f) : 0f;
        if (_bruteQueue > 0 && GD.Randf() < (float)_bruteQueue / Mathf.Max(1, _toSpawn + 1) * 2f) { type = ZombieType.Brute; _bruteQueue--; }
        else if (GD.Randf() < runnerChance) type = ZombieType.Runner;

        bool rise = Arena.InnerSpawns.Count > 0 && GD.Randf() < Arena.RiseChance(WaveNumber);
        Vector3 pos;
        if (rise)
        {
            pos = Vector3.Zero;
            for (int k = 0; k < 8; k++)
            {
                pos = Arena.InnerSpawns[GD.RandRange(0, Arena.InnerSpawns.Count - 1)];
                if (pos.DistanceTo(Player.GlobalPosition) > 9f) break;
            }
        }
        else
        {
            var g = Arena.GateSpawns[GD.RandRange(0, Arena.GateSpawns.Count - 1)];
            pos = g + new Vector3((float)GD.RandRange(-3, 3), 0, (float)GD.RandRange(-3, 3));
        }
        var z = new Zombie();
        z.Setup(type, WaveNumber, rise);
        World.AddChild(z);
        z.GlobalPosition = pos + Vector3.Up * 0.05f;
        Zombies.Add(z);
    }

    public void OnZombieKilled(Zombie z, bool headshot)
    {
        Zombies.Remove(z);
        Kills++;
        _combo = _comboTimer > 0 ? _combo + 1 : 0;
        _comboTimer = 2.8f;
        ComboMult = Mathf.Min(1 + _combo / 3, 8);
        int pts = (int)(z.Points * (headshot ? 1.5f : 1f));
        AddScore(pts, z.GlobalPosition + Vector3.Up * (z.Height + 0.4f));
        if (headshot) Fx.Number(z.GlobalPosition + Vector3.Up * (z.Height + 1.1f), "HEADSHOT", new Color(1f, 0.85f, 0.25f), 1.2f);
        InputSetup.Rumble(0.2f, z.Type == ZombieType.Brute ? 0.6f : 0.15f, 0.1f);

        // loot
        float dropMult = Player?.Def.DropMult ?? 1f;
        int drops = z.Type == ZombieType.Brute ? 2 : GD.Randf() < 0.2f * dropMult ? 1 : 0;
        if (z.Type == ZombieType.Brute && dropMult > 1) drops++;
        for (int i = 0; i < drops; i++)
        {
            float lowHp = Player != null && Player.Hp < Player.MaxHp * 0.5f ? 20f : 0f;
            float r = GD.Randf() * (100f + lowHp);
            var kind = r < 45 ? PickupKind.Ammo : r < 60 ? PickupKind.Grenade : r < 75 ? PickupKind.Coin : PickupKind.Health;
            Pickup.Spawn(kind, z.GlobalPosition);
        }
    }

    public void AddScore(int pts, Vector3 at)
    {
        int total = pts * ComboMult;
        Score += total;
        Fx.Number(at, $"+{total}", ComboMult > 1 ? new Color(1f, 0.55f, 0.3f) : new Color(0.7f, 1f, 0.6f), 1.1f);
    }

    public void OnPlayerDied()
    {
        if (Mode != State.Playing) return;
        Mode = State.GameOver;
        Engine.TimeScale = 0.3f;
        Sfx.Play("game_over");
        GetTree().CreateTimer(1.0, true, false, true).Timeout += () =>
        {
            Engine.TimeScale = 1f;
            ShowGameOver();
        };
    }

    void ShowGameOver()
    {
        bool record = Score > _best;
        if (record) { _best = Score; GameData.SaveBest(_map, Score); }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _overPanel = MakePanel(record ? "NEW RECORD!" : "YOU WERE EATEN",
            $"WAVE {WaveNumber}     KILLS {Kills}\nSCORE {Score:N0}     BEST {_best:N0}",
            ("RETRY", () => { GameData.SkipMenu = true; Restart(); }),
            ("CHANGE SURVIVOR", () => { GameData.SkipMenu = false; GameData.MenuStart = 1; Restart(); }));
    }

    void Restart()
    {
        GetTree().Paused = false;
        Engine.TimeScale = 1;
        GetTree().ReloadCurrentScene();
    }

    void TogglePause()
    {
        if (Mode == State.Playing)
        {
            Mode = State.Paused;
            GetTree().Paused = true;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _pausePanel = MakePanel("PAUSED", $"WAVE {WaveNumber}     SCORE {Score:N0}",
                ("RESUME", TogglePause),
                ("RESTART", () => { GameData.SkipMenu = true; Restart(); }),
                ("QUIT TO MENU", () => { GameData.SkipMenu = false; GameData.MenuStart = 0; Restart(); }));
        }
        else if (Mode == State.Paused)
        {
            Mode = State.Playing;
            GetTree().Paused = false;
            Input.MouseMode = Input.MouseModeEnum.Captured;
            _pausePanel?.GetParent()?.QueueFree();
            _pausePanel = null;
        }
    }

    Control MakePanel(string title, string sub, params (string text, System.Action act)[] buttons)
    {
        var layer = new CanvasLayer { Layer = 20, ProcessMode = ProcessModeEnum.Always };
        AddChild(layer);
        var dim = new ColorRect { Color = new Color(0.04f, 0.02f, 0.06f, 0.65f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(dim);
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        box.AddThemeConstantOverride("separation", 14);
        layer.AddChild(box);
        box.AddChild(Hud.MakeLabel(title, 90, new Color(1f, 0.8f, 0.3f), 18, HorizontalAlignment.Center));
        box.AddChild(Hud.MakeLabel(sub, 30, Colors.White, 8, HorizontalAlignment.Center));
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });
        Button first = null;
        foreach (var (text, act) in buttons)
        {
            var b = Hud.MakeButton(text);
            b.FocusEntered += () => Sfx.Play("ui_move", -6f);
            b.Pressed += () => { Sfx.Play("ui_select"); act(); };
            b.MouseEntered += b.GrabFocus;
            box.AddChild(b);
            first ??= b;
        }
        first?.CallDeferred(Control.MethodName.GrabFocus);
        return box;
    }

    int _animStep;
    readonly List<Zombie> _testZ = new();
    void AnimTest()
    {
        _breakTimer = 999f;
        if (_animStep == 0)
        {
            _animStep = 1;
            for (int i = 0; i < 3; i++)
            {
                var z = new Zombie();
                z.Setup(i == 2 ? ZombieType.Brute : ZombieType.Walker, 1, false);
                World.AddChild(z);
                z.GlobalPosition = new Vector3(3f + i * 3f, 0.05f, 2.5f);
                Zombies.Add(z); _testZ.Add(z);
            }
            Rig.Yaw = 0; Rig.Pitch = -0.05f;
            var cam = new Camera3D { Fov = 50 };
            World.AddChild(cam);
            bool pc = OS.GetEnvironment("FF_PCAM") == "1";
            cam.GlobalPosition = pc ? new Vector3(-2.6f, 1.9f, 4.2f) : new Vector3(6f, 4.5f, 10.5f);
            cam.LookAt(pc ? new Vector3(0, 1.3f, 6f) : new Vector3(6f, 0.6f, 2.5f), Vector3.Up);
            cam.MakeCurrent();
        }
        Input.ActionPress("aim");
        float t = _time;
        if (_animStep == 1 && t > 2.0f) { _animStep = 2; _testZ[0].TakeDamage(999, _testZ[0].GlobalPosition + Vector3.Up, Vector3.Forward, false); }
        if (_animStep == 2 && t > 2.1f) { _animStep = 3; _testZ[1].TakeDamage(999, _testZ[1].GlobalPosition + Vector3.Up * 2, Vector3.Forward, true); }
        if (_animStep == 3 && t > 2.2f) { _animStep = 4; _testZ[2].TakeDamage(9999, _testZ[2].GlobalPosition + Vector3.Up, new Vector3(0.3f, 0, -1), false, 10, true); }
        if (t > 1.0f && Engine.GetProcessFrames() % 20 == 0) Input.ActionPress("fire"); else Input.ActionRelease("fire");
    }

    void AutoPlay()
    {
        Zombie best = null; float bd = 1e9f;
        foreach (var z in Zombies)
        {
            if (z.Dead) continue;
            float d = z.GlobalPosition.DistanceTo(Player.GlobalPosition);
            if (d < bd) { bd = d; best = z; }
        }
        Input.ActionRelease("fire");
        if (best == null || bd > 30f) { Input.ActionRelease("aim"); return; }
        Input.ActionPress("aim");
        var aimAt = OS.GetEnvironment("FF_HEAD") == "1" ? best.GlobalPosition + Vector3.Up * best.Height * 0.85f : best.AimPoint;
        var to = aimAt - Rig.GlobalPosition;
        Rig.Yaw = Mathf.LerpAngle(Rig.Yaw, Mathf.Atan2(-to.X, -to.Z) + 0.12f, 0.3f);
        Rig.Pitch = Mathf.Lerp(Rig.Pitch, Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()), 0.3f);
        if (Engine.GetProcessFrames() % 3 == 0) Input.ActionPress("fire");
        if (Engine.GetProcessFrames() % 500 == 0 && OS.GetEnvironment("FF_KEEPWEAPON") != "1") Input.ActionPress("weapon_next"); else Input.ActionRelease("weapon_next");
        if (Engine.GetProcessFrames() % 700 == 0) Input.ActionPress("grenade"); else Input.ActionRelease("grenade");
        if (OS.GetEnvironment("FF_TOP") == "1" && _topCam == null)
        {
            _topCam = new Camera3D { Current = true, Fov = 60 };
            World.AddChild(_topCam);
            _topCam.GlobalPosition = new Vector3(0, 55, 22);
            _topCam.LookAt(new Vector3(0, 0, 2), Vector3.Up);
        }
        if (bd < 4f && Engine.GetProcessFrames() % 120 == 0) Input.ActionPress("dash"); else Input.ActionRelease("dash");
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        if (_shotIdx < _shotTimes.Count && _time > _shotTimes[_shotIdx])
        {
            var path = _debugShotPath.Replace(".png", $"_{_shotIdx}.png");
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print("screenshot saved ", path, " zombies=", Zombies.Count, " wave=", WaveNumber, " score=", Score, " hp=", Player?.Hp, " fps=", Engine.GetFramesPerSecond(), " heads=", Zombie.Headshots, "/", Zombie.Hits, " bruteHeads=", Zombie.BruteHeadshots);
            _shotIdx++;
            if (_shotIdx >= _shotTimes.Count && OS.GetEnvironment("FF_QUIT") == "1") GetTree().Quit();
        }
        if (_auto && Mode == State.Playing) AutoPlay();
        if (OS.GetEnvironment("FF_ANIMTEST") == "1" && Mode == State.Playing) AnimTest();

        if (Mode == State.Menu) { MenuProcess(dt); return; }
        if (Input.IsActionJustPressed("pause") && (Mode == State.Playing || Mode == State.Paused)) { TogglePause(); return; }
        if (Mode == State.Paused && Input.IsActionJustPressed("menu_back")) { TogglePause(); return; }
        if (Mode != State.Playing) return;

        if (Input.MouseMode != Input.MouseModeEnum.Captured && Input.IsMouseButtonPressed(MouseButton.Left))
            Input.MouseMode = Input.MouseModeEnum.Captured;

        _comboTimer -= dt;
        if (_comboTimer <= 0 && _combo > 0) { _combo = 0; ComboMult = 1; }

        if (_breakTimer > 0)
        {
            _breakTimer -= dt;
            if (_breakTimer <= 0) StartWave();
            return;
        }
        if (!_waveActive) return;

        int maxAlive = Mathf.Min(14 + WaveNumber * 2, 36);
        _spawnTimer -= dt;
        if (_toSpawn > 0 && _spawnTimer <= 0 && Zombies.Count < maxAlive)
        {
            SpawnZombie();
            _spawnTimer = Mathf.Max(0.3f, 1.25f - WaveNumber * 0.08f) * (float)GD.RandRange(0.6, 1.3);
        }
        if (_toSpawn <= 0 && Zombies.Count == 0)
        {
            _waveActive = false;
            int bonus = WaveNumber * 500;
            Score += bonus;
            Hud.Banner($"WAVE {WaveNumber} CLEARED", $"+{bonus:N0} bonus   ·   health restored", 2.2f);
            Sfx.Play("wave_clear");
            Player.Hp = Mathf.Min(Player.MaxHp, Player.Hp + Player.MaxHp * 0.25f);
            _breakTimer = 7f;
        }
    }
}
