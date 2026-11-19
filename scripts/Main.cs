using Godot;
using System.Collections.Generic;

public partial class Main : Node3D
{
    const float BulletTimeScale = 0.2f;
    public static Main Instance;
    public static Font TitleFont;

    public enum State { Menu, Playing, Paused, GameOver, Lobby }
    public State Mode;

    public Node3D World;
    public MapBase Arena;
    /// Everyone in the run (1 in single player, 2-4 in local co-op).
    public readonly List<Player> Players = new();
    /// Player 1 (debug hooks, solo code paths).
    public Player Player => Players.Count > 0 ? Players[0] : null;
    /// The only player in a solo run, null in co-op.
    public Player SoloPlayer => Players.Count == 1 ? Players[0] : null;
    public bool Coop => Players.Count > 1;
    public readonly List<Zombie> Zombies = new();
    AudioListener3D _listener;

    // game stats
    public int Score, WaveNumber, Kills, ComboMult = 1;
    public int ZombiesRemaining => Net.IsClient ? NetZombiesLeft : _toSpawn + Zombies.Count;
    public bool InBreak => BreakTimer > 0;
    public float BreakTimer => Net.IsClient ? NetBreakTimer : _breakTimer;
    public bool WaveActive => _waveActive;
    /// Online client: wave counters mirrored from the server.
    public int NetZombiesLeft;
    public float NetBreakTimer;
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
    bool _debugNade, _debugPinata;
    string _debugShotPath;
    int _shotIdx;
    bool _auto;
    Camera3D _topCam;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        if (!IsInstanceValid(Net.Game))
        {
            // beside Main so an online client keeps its connection across scene reloads
            Net.Game = new NetGame { Name = "Net" };
            GetTree().Root.CallDeferred(Node.MethodName.AddChild, Net.Game);
        }
        if (Net.WantsServer) { ServerReady(); return; }
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

        if (!_debugJoined && OS.GetEnvironment("FF_NET") is { Length: > 5 } fn && fn.StartsWith("join:"))
        {
            _debugJoined = true;
            // FF_NET=join:HOST:PORT (+ FF_NAME, FF_CHAR): straight into a server, no menus
            string name = OS.GetEnvironment("FF_NAME") is { Length: > 0 } nm ? nm : "TESTER";
            JoinServer(fn.Substring(5), name, GameData.SelectedCharacter, OS.GetEnvironment("FF_PASS"), err =>
            {
                GD.PrintErr("[net] " + err);
                if (OS.GetEnvironment("FF_QUIT") == "1") GetTree().Quit(1);
                else ShowMenu();
            });
            return;
        }
        if (Net.IsClient) ShowMenu((int)MainMenu.Screen.Lobby); // back from a match, still connected
        else if (GameData.SkipMenu) StartGame();
        else ShowMenu();
    }

    public override void _ExitTree() { if (Instance == this) Instance = null; }

    /// Stage currently loaded.
    public int MapIndex => _map;

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
        _post?.SetShaderParameter("tint", Arena.Style.Tint);
        _post?.SetShaderParameter("lift", Arena.Style.Lift);
    }

    // ------------------------------------------------------------------ MENU
    void ShowMenu(int? screen = null)
    {
        Mode = State.Menu;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _menuCam = new Camera3D { Fov = 50, Current = true };
        World.AddChild(_menuCam);
        _menu = new MainMenu();
        AddChild(_menu);
        _menu.BackdropChanged += m => { _map = m; LoadMap(MapBase.BuildMode.Backdrop); };
        _menu.JoinRequested += (address, name, character, password) =>
            JoinServer(address, name, character, password, err => _menu?.ServerStatus(err, true));
        _menu.Finished += (c, m) =>
        {
            GameData.SelectedCharacter = c;
            GameData.SelectedMap = _map = m;
            _menu.QueueFree(); _menu = null;
            _menuCam.QueueFree(); _menuCam = null;
            LoadMap();
            StartGame();
        };
        int start = screen ?? (OS.GetEnvironment("FF_MENU") is { Length: > 0 } fs ? int.Parse(fs) : GameData.MenuStart);
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
        var slots = GameData.Slots.Count > 0
            ? new List<PlayerSlot>(GameData.Slots)
            : new List<PlayerSlot> { new(InputSetup.AnyPad, true, GameData.SelectedCharacter) };
        if (OS.GetEnvironment("FF_COOP") is { Length: > 0 } fc && int.TryParse(fc, out int nc) && nc > 1) slots = DebugSlots(nc);
        if (slots.Exists(s => s.Kbm)) Input.MouseMode = Input.MouseModeEnum.Captured;

        var views = SplitScreen.Build(this, slots.Count);
        float uiScale = 1f;
        if (views != null)
        {
            var screen = GetViewport().GetVisibleRect().Size;
            var cell = slots.Count == 2 ? new Vector2(screen.X / 2, screen.Y) : screen / 2;
            uiScale = Mathf.Clamp(Mathf.Min(cell.X / 1600f, cell.Y / 900f), 0.6f, 1f);
        }
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var rig = new CameraRig();
            if (views != null) views[i].AddChild(rig); else World.AddChild(rig);
            rig.Yaw = 0; rig.Pitch = -0.12f;
            var hud = new Hud();
            var p = new Player();
            p.Setup(GameData.Characters[slot.Character], rig, new PlayerInput(i, slot.Pad, slot.Kbm), hud);
            World.AddChild(p);
            // line the team up across the start area
            p.GlobalPosition = new Vector3((i - (slots.Count - 1) / 2f) * 1.5f, 0.1f, 6f);
            if (OS.GetEnvironment("FF_GOD") == "1") { p.MaxHp = p.Hp = 99999; }
            rig.Snap();
            if (views != null) views[i].AddChild(hud); else AddChild(hud);
            hud.Init(p, uiScale);
            Players.Add(p);
        }
        if (Coop)
        {
            // one shared ear between the players; the split cameras don't listen
            _listener = new AudioListener3D();
            World.AddChild(_listener);
            _listener.MakeCurrent();
            UpdateListener();
        }
        if (OS.GetEnvironment("FF_WAVE") is { Length: > 0 } fw) WaveNumber = int.Parse(fw) - 1;
        _best = GameData.LoadBest(_map, Coop);
        _breakTimer = 3.5f;
        Banner("SURVIVE", Coop ? "stick together — hold (X) near a downed friend to revive" : "hold the arena as long as you can", 1.6f);
    }

    /// FF_COOP=n: n players, pads handed out in connection order, player 1 also on keyboard+mouse.
    static List<PlayerSlot> DebugSlots(int n)
    {
        var pads = Input.GetConnectedJoypads();
        var list = new List<PlayerSlot>();
        for (int i = 0; i < Mathf.Min(n, 4); i++)
            list.Add(new PlayerSlot(i < pads.Count ? pads[i] : InputSetup.NoPad, i == 0, i % GameData.Characters.Length));
        return list;
    }

    public void Banner(string text, string sub, float hold)
    {
        if (Net.IsServer) { Net.Game?.SendBanner(text, sub, hold); return; }
        foreach (var p in Players) p.Hud?.Banner(text, sub, hold);
    }

    public void Toast(string text, Color color)
    {
        if (Net.IsServer) { Net.Game?.SendToast(text, color); return; }
        foreach (var p in Players) p.Hud?.Toast(text, color);
    }

    /// A sound everyone hears (wave start, wave clear...). The server forwards it to the clients.
    void GlobalSfx(string name, float db = 0f)
    {
        if (Net.IsServer) Net.Game?.SendSound(name, db);
        else Sfx.Play(name, db);
    }

    /// Nearest player still on their feet (zombie targeting), or null.
    public Player ClosestTarget(Vector3 from)
    {
        Player best = null;
        float bd = float.MaxValue;
        foreach (var p in Players)
        {
            if (!p.Targetable) continue;
            float d = p.GlobalPosition.DistanceSquaredTo(from);
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    public bool AnyStanding => Players.Exists(p => p.Targetable);

    /// Camera shake (+ optional rumble) from an event at `at`; in co-op it fades with distance from each player.
    public void Shake(Vector3 at, float trauma, float weak = 0f, float strong = 0f, float duration = 0f)
    {
        foreach (var p in Players)
        {
            if (p.Rig == null) continue; // network puppets have no camera
            float k = Coop ? Mathf.Clamp(1.4f - p.GlobalPosition.DistanceTo(at) / 25f, 0.25f, 1f) : 1f;
            p.Rig.AddTrauma(trauma * k);
            if (duration > 0) p.In.Rumble(weak * k, strong * k, duration);
        }
    }

    void UpdateListener()
    {
        if (_listener == null) return;
        Vector3 sum = Vector3.Zero;
        int n = 0;
        foreach (var p in Players) if (!p.Dead) { sum += p.GlobalPosition; n++; }
        if (n == 0) return;
        var rig = Players[0].Rig;
        _listener.GlobalTransform = new Transform3D(new Basis(Vector3.Up, rig.Yaw), sum / n + Vector3.Up * 1.6f);
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
        Banner($"WAVE {WaveNumber}", boss ? "BRUTE HORDE INCOMING" : WaveNumber == 1 ? "here they come" : $"{_toSpawn} zombies", 1.8f);
        GlobalSfx("wave_start", -2f);
        InputSetup.Rumble(0.3f, 0.3f, 0.4f);
        if (Net.IsServer) GD.Print($"[server] wave {WaveNumber} · {Players.Count} players · score {Score}");
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
                var near = ClosestTarget(pos);
                if (near == null || pos.DistanceTo(near.GlobalPosition) > 9f) break;
            }
        }
        else
        {
            var g = Arena.GateSpawns[GD.RandRange(0, Arena.GateSpawns.Count - 1)];
            pos = g + new Vector3((float)GD.RandRange(-3, 3), 0, (float)GD.RandRange(-3, 3));
        }
        var z = new Zombie();
        if (Net.IsServer) z.NetId = NetIds.Next();
        z.Setup(type, WaveNumber, rise);
        World.AddChild(z);
        z.GlobalPosition = pos + Vector3.Up * 0.05f;
        Zombies.Add(z);
        if (Net.IsServer) Net.Game?.SendZombieSpawned(z);
    }

    public void OnZombieKilled(Zombie z, bool headshot, Player killer = null)
    {
        Zombies.Remove(z);
        Kills++;
        if (killer != null) killer.Kills++;
        _combo = _comboTimer > 0 ? _combo + 1 : 0;
        _comboTimer = 2.8f;
        ComboMult = Mathf.Min(1 + _combo / 3, 8);
        int pts = (int)(z.Points * (headshot ? 1.5f : 1f));
        AddScore(pts, z.GlobalPosition + Vector3.Up * (z.Height + 0.4f));
        if (headshot) Fx.Number(z.GlobalPosition + Vector3.Up * (z.Height + 1.1f), "HEADSHOT", new Color(1f, 0.85f, 0.25f), 1.2f);
        InputSetup.Rumble(0.2f, z.Type == ZombieType.Brute ? 0.6f : 0.15f, 0.1f);

        // loot
        // the best scavenger still alive sets the team's loot luck
        float dropMult = 1f;
        foreach (var p in Players) if (!p.Dead) dropMult = Mathf.Max(dropMult, p.Def.DropMult);
        int drops = z.Type == ZombieType.Brute ? 2 : GD.Randf() < 0.2f * dropMult ? 1 : 0;
        if (z.Type == ZombieType.Brute && dropMult > 1) drops++;
        for (int i = 0; i < drops; i++)
        {
            float lowHp = Players.Exists(p => p.Targetable && p.Hp < p.MaxHp * 0.5f) ? 20f : 0f;
            float r = GD.Randf() * (100f + lowHp);
            var kind = r < 45 ? PickupKind.Ammo : r < 60 ? PickupKind.Grenade : r < 75 ? PickupKind.Coin : PickupKind.Health;
            Pickup.Spawn(kind, z.GlobalPosition);
        }
    }

    public void AddScore(int pts, Vector3 at)
    {
        int total = pts * ComboMult;
        Score += total;
        if (Net.IsServer) Net.Game?.SendScore(total, at, ComboMult > 1);
        Fx.Number(at, $"+{total}", ComboMult > 1 ? new Color(1f, 0.55f, 0.3f) : new Color(0.7f, 1f, 0.6f), 1.1f);
    }

    /// Co-op: someone is down. The run only ends once nobody is left standing.
    public void OnPlayerDowned(Player p)
    {
        if (Net.IsClient) { Sfx.Play("game_over", -8f); return; } // the server decides what it means
        if (Mode != State.Playing) return;
        Sfx.Play("game_over", -8f);
        if (AnyStanding) { Toast($"{p.DisplayName} IS DOWN!", new Color(1f, 0.35f, 0.3f)); return; }
        EndRun();
    }

    public void OnPlayerDied(Player p)
    {
        if (Net.IsClient || Mode != State.Playing) return;
        if ((Coop || Net.IsServer) && AnyStanding) { Toast($"{p.DisplayName} BLED OUT", new Color(0.8f, 0.6f, 0.6f)); return; }
        EndRun();
    }

    public void EndRun()
    {
        if (Net.IsServer) { ServerEndRun(); return; }
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
        if (record) { _best = Score; GameData.SaveBest(_map, Score, Coop); }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        string stats = $"WAVE {WaveNumber}     KILLS {Kills}\nSCORE {Score:N0}     BEST {_best:N0}";
        if (Coop) stats += "\n" + string.Join("     ", Players.ConvertAll(p => $"{p.Def.Name} {p.Kills}"));
        _overPanel = MakePanel(record ? "NEW RECORD!" : Coop ? "THE HORDE WINS" : "YOU WERE EATEN", stats,
            ("RETRY", () => { GameData.SkipMenu = true; Restart(); }),
            (Coop ? "CHANGE TEAM" : "CHANGE SURVIVOR", ChangeTeam));
    }

    void ChangeTeam()
    {
        GameData.SkipMenu = false;
        GameData.MenuStart = (int)(GameData.Coop ? MainMenu.Screen.Lobby : MainMenu.Screen.Survivor);
        Restart();
    }

    void Restart()
    {
        GetTree().Paused = false;
        Engine.TimeScale = 1;
        GetTree().ReloadCurrentScene();
    }

    void TogglePause()
    {
        if (Net.IsClient) { ToggleOnlinePause(); return; }
        if (Mode == State.Playing)
        {
            Mode = State.Paused;
            Engine.TimeScale = 1f;
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
            Player.Rig.Yaw = 0; Player.Rig.Pitch = -0.05f;
            var cam = new Camera3D { Fov = 50 };
            World.AddChild(cam);
            bool pc = OS.GetEnvironment("FF_PCAM") == "1";
            cam.GlobalPosition = pc ? new Vector3(-2.6f, 1.9f, 4.2f) : new Vector3(6f, 4.5f, 10.5f);
            cam.LookAt(pc ? new Vector3(0, 1.3f, 6f) : new Vector3(6f, 0.6f, 2.5f), Vector3.Up);
            cam.MakeCurrent();
        }
        Input.ActionPress(Player.In.A("aim"));
        float t = _time;
        if (_animStep == 1 && t > 2.0f) { _animStep = 2; _testZ[0].TakeDamage(999, _testZ[0].GlobalPosition + Vector3.Up, Vector3.Forward, false); }
        if (_animStep == 2 && t > 2.1f) { _animStep = 3; _testZ[1].TakeDamage(999, _testZ[1].GlobalPosition + Vector3.Up * 2, Vector3.Forward, true); }
        if (_animStep == 3 && t > 2.2f) { _animStep = 4; _testZ[2].TakeDamage(9999, _testZ[2].GlobalPosition + Vector3.Up, new Vector3(0.3f, 0, -1), false, 10, true); }
        if (t > 1.0f && Engine.GetProcessFrames() % 20 == 0) Input.ActionPress(Player.In.A("fire")); else Input.ActionRelease(Player.In.A("fire"));
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
        Input.ActionRelease(Player.In.A("fire"));
        if (best == null || bd > 30f) { Input.ActionRelease(Player.In.A("aim")); return; }
        Input.ActionPress(Player.In.A("aim"));
        var aimAt = OS.GetEnvironment("FF_HEAD") == "1" ? best.GlobalPosition + Vector3.Up * best.Height * 0.85f : best.AimPoint;
        var rig = Player.Rig;
        var to = aimAt - rig.GlobalPosition;
        rig.Yaw = Mathf.LerpAngle(rig.Yaw, Mathf.Atan2(-to.X, -to.Z) + 0.12f, 0.3f);
        rig.Pitch = Mathf.Lerp(rig.Pitch, Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()), 0.3f);
        if (Engine.GetProcessFrames() % 3 == 0) Input.ActionPress(Player.In.A("fire"));
        if (Engine.GetProcessFrames() % 500 == 0 && OS.GetEnvironment("FF_KEEPWEAPON") != "1") Input.ActionPress(Player.In.A("weapon_next")); else Input.ActionRelease(Player.In.A("weapon_next"));
        if (Engine.GetProcessFrames() % 700 == 0) Input.ActionPress(Player.In.A("grenade")); else Input.ActionRelease(Player.In.A("grenade"));
        if (OS.GetEnvironment("FF_TOP") == "1" && _topCam == null)
        {
            _topCam = new Camera3D { Current = true, Fov = 60 };
            World.AddChild(_topCam);
            _topCam.GlobalPosition = new Vector3(0, 55, 22);
            _topCam.LookAt(new Vector3(0, 0, 2), Vector3.Up);
        }
        if (bd < 4f && Engine.GetProcessFrames() % 120 == 0) Input.ActionPress(Player.In.A("dash")); else Input.ActionRelease(Player.In.A("dash"));
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
        // FF_GRENADE=type (+ FF_GRENADE_T=seconds): pick that grenade (shows the picker) and drop one ahead of the player
        if (!_debugNade && _time > (OS.GetEnvironment("FF_GRENADE_T") is { Length: > 0 } fgt ? float.Parse(fgt, System.Globalization.CultureInfo.InvariantCulture) : 2.5f) && Mode == State.Playing && Player != null && OS.GetEnvironment("FF_GRENADE") is { Length: > 0 } fg && int.TryParse(fg, out int gt))
        {
            _debugNade = true;
            Player.GrenadeType = gt;
            Player.Hud?.GrenadeSelected();
            var fwd = -Player.Rig.GlobalBasis.Z with { Y = 0 };
            Grenade.Throw(Player.GlobalPosition + fwd.Normalized() * 8f + fwd.Normalized().Cross(Vector3.Up) * 3f + Vector3.Up * 1.5f, Vector3.Zero, 1f, Player, gt);
        }

        // FF_PINATA=seconds: ring a few walkers 7 m ahead of the player and turn one of them into a piñata
        if (!_debugPinata && Mode == State.Playing && Player != null && OS.GetEnvironment("FF_PINATA") is { Length: > 0 } fp
            && _time > float.Parse(fp, System.Globalization.CultureInfo.InvariantCulture))
        {
            _debugPinata = true;
            var fwd = (-Player.Rig.GlobalBasis.Z with { Y = 0 }).Normalized();
            var c = Player.GlobalPosition + fwd * 5f - fwd.Cross(Vector3.Up) * 7f; // ahead and to the left, clear of the arena centrepiece
            Zombie first = null;
            for (int i = 0; i < 7; i++)
            {
                var z = new Zombie();
                z.Setup(i == 6 ? ZombieType.Runner : ZombieType.Walker, 1, false);
                World.AddChild(z);
                float a = Mathf.Tau * i / 7f;
                z.GlobalPosition = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (i == 0 ? 0f : 6f) + Vector3.Up * 0.05f;
                Zombies.Add(z);
                first ??= z;
            }
            first.Pinatafy(Player, Player.Def.BlastMult);
            if (OS.GetEnvironment("FF_PCAM") == "1")
            {
                var cam = new Camera3D { Fov = 55 };
                World.AddChild(cam);
                cam.GlobalPosition = c + fwd.Cross(Vector3.Up) * 4.5f + Vector3.Up * 2.6f - fwd * 2.5f;
                cam.LookAt(c + Vector3.Up * 0.8f, Vector3.Up);
                cam.MakeCurrent();
            }
        }

        if (Net.IsServer) { if (!ServerProcess(dt)) return; }
        else
        {
            if (Mode == State.Menu) { if (_menuCam != null) MenuProcess(dt); return; }
            if (Net.IsClient && Mode == State.GameOver) { OnlineOverProcess(dt); return; }
            if (Input.IsActionJustPressed("pause") && (Mode == State.Playing || Mode == State.Paused)) { TogglePause(); return; }
            if (Mode == State.Paused && Input.IsActionJustPressed("menu_back")) { TogglePause(); return; }
            if (Mode != State.Playing) return;
            if (!Net.Online) Engine.TimeScale = Players.Exists(p => p.BulletTimeActive) ? BulletTimeScale : 1f;

            if (Input.MouseMode != Input.MouseModeEnum.Captured && Input.IsMouseButtonPressed(MouseButton.Left) && Players.Exists(p => p.In is { Kbm: true }))
                Input.MouseMode = Input.MouseModeEnum.Captured;
            // online the server runs waves, spawns, revives and the clock; we just play
            if (Net.IsClient) return;
        }

        UpdateListener();
        if (Coop) ReviveProcess(dt);
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
            bool revived = false;
            foreach (var p in Players)
            {
                if (!p.Downed && !p.Dead) continue;
                // bled out: back in the fight next to a teammate
                bool moved = false;
                if (p.Dead && ClosestTarget(p.GlobalPosition) is { } buddy) { p.GlobalPosition = buddy.GlobalPosition + new Vector3(1.2f, 0.3f, 0); moved = true; }
                p.Revive(0.4f, moved);
                revived = true;
            }
            // online: whoever joined mid-wave drops in now
            foreach (var p in Players)
            {
                if (!p.Benched) continue;
                p.SetBenched(false);
                p.Hp = p.MaxHp;
                p.GlobalPosition = BuddySpot(p);
                Net.Game?.SendPlayerSpawned(p);
            }
            Banner($"WAVE {WaveNumber} CLEARED", $"+{bonus:N0} bonus   ·   health restored" + (revived ? "   ·   team revived" : ""), 2.2f);
            GlobalSfx("wave_clear");
            foreach (var p in Players) p.Hp = Mathf.Min(p.MaxHp, p.Hp + p.MaxHp * 0.25f);
            _breakTimer = 7f;
        }
    }

    const float ReviveRange = 2.2f, ReviveTime = 2f;

    /// A teammate standing next to a downed player and holding (X) brings them back up.
    void ReviveProcess(float dt)
    {
        foreach (var p in Players)
        {
            if (!p.Downed) continue;
            var helper = Reviver(p);
            if (helper == null) { p.ReviveProgress = Mathf.Max(0, p.ReviveProgress - dt / ReviveTime); continue; }
            p.BleedOut += dt; // the clock stops while someone is helping
            p.ReviveProgress += dt / ReviveTime;
            if (p.ReviveProgress >= 1f)
            {
                p.Revive(0.4f);
                Toast($"{helper.DisplayName} REVIVED {p.DisplayName}", new Color(0.5f, 1f, 0.5f));
                AddScore(200, p.GlobalPosition + Vector3.Up * 2f);
            }
        }
    }

    /// Teammate currently reviving `downed` (on their feet, in range, holding reload), or null.
    public Player Reviver(Player downed)
    {
        foreach (var o in Players)
            if (o != downed && o.Targetable && o.ReviveHeld && o.GlobalPosition.DistanceTo(downed.GlobalPosition) < ReviveRange)
                return o;
        return null;
    }

    /// Downed teammate close enough for `p` to revive, or null.
    public Player DownedNear(Player p)
    {
        foreach (var o in Players)
            if (o != p && o.Downed && !o.Dead && o.GlobalPosition.DistanceTo(p.GlobalPosition) < ReviveRange)
                return o;
        return null;
    }

    // ================================================================== ONLINE
    static bool _debugJoined;
    float _restartT, _overT;
    Label _overCountdown;
    Button _readyBtn;

    public Player PlayerById(int id) => id == 0 ? null : Players.Find(p => p.NetId == id);
    /// Online client: our own player.
    public Player LocalPlayer => Players.Find(p => p.NetRole == Player.Role.Local);

    /// Spot `i` of `n` on the start line (same as a local co-op run).
    public Vector3 StartLine(int i, int n) => new((i - (n - 1) / 2f) * 1.5f, 0.1f, 6f);

    /// Next to someone already in the fight (or the start line if nobody is).
    public Vector3 BuddySpot(Player p)
    {
        foreach (var o in Players)
            if (o != p && o.Targetable) return o.GlobalPosition + new Vector3(1.2f, 0.3f, 0);
        return StartLine(0, 1);
    }

    // ---------------------------------------------------------------- dedicated server
    void ServerReady()
    {
        Engine.TimeScale = 1;
        Engine.MaxFps = 60;
        Fx.ClearDarts();
        World = new Node3D { Name = "World", ProcessMode = ProcessModeEnum.Pausable };
        AddChild(World);
        var cfg = ServerConfig.Load();
        _map = cfg.Map;
        LoadMap(MapBase.BuildMode.Full);
        Mode = State.Lobby;
        if (!Net.Game.StartServer(cfg)) GetTree().Quit(1);
    }

    /// Server side of the frame. Returns true when the shared wave loop should run.
    bool ServerProcess(float dt)
    {
        if (Mode == State.GameOver)
        {
            _restartT -= dt;
            if (_restartT <= 0 || Net.Game.AllReady) Net.Game.ServerReturnToLobby();
            return false;
        }
        return Mode == State.Playing;
    }

    /// Fresh run for everyone connected.
    public void ServerBeginRun()
    {
        ServerResetRun(true);
        GD.Print($"[server] run started · {Players.Count} players");
    }

    /// Wipe the arena and line everyone up again (no scene reload: the session lives on Main).
    public void ServerResetRun(bool announce)
    {
        ClearWorldEntities();
        Score = WaveNumber = Kills = 0;
        ComboMult = 1;
        _combo = 0; _comboTimer = 0; _toSpawn = 0; _bruteQueue = 0; _spawnTimer = 0;
        _waveActive = false;
        _breakTimer = 3.5f;
        for (int i = 0; i < Players.Count; i++)
        {
            Players[i].ResetForRun();
            Players[i].GlobalPosition = StartLine(i, Players.Count);
        }
        Net.Game.ClearReady();
        Mode = State.Playing;
        if (!announce) return;
        Net.Game.SendRunStart();
        Banner("SURVIVE", "stick together — hold (X) near a downed friend to revive", 1.6f);
    }

    /// The host picked a stage: build it (players are added by NetGame right after).
    public void ServerLoadStage(int map)
    {
        ServerClearMatch();
        _map = map;
        LoadMap(MapBase.BuildMode.Full);
    }

    /// Back to an empty arena with nobody in it (the lobby).
    public void ServerClearMatch()
    {
        ClearWorldEntities();
        foreach (var p in Players) { p.Dead = true; p.QueueFree(); }
        Players.Clear();
        Score = WaveNumber = Kills = 0;
        ComboMult = 1;
        _combo = 0; _comboTimer = 0; _toSpawn = 0; _bruteQueue = 0; _spawnTimer = 0;
        _waveActive = false;
        _breakTimer = 0;
        Mode = State.Lobby;
    }

    /// Zombies, pickups, grenades, puddles: everything a run leaves behind.
    void ClearWorldEntities()
    {
        foreach (var z in Zombies) if (IsInstanceValid(z)) z.QueueFree();
        Zombies.Clear();
        foreach (var n in NetIds.All()) if (IsInstanceValid(n)) n.QueueFree();
        NetIds.Clear();
        foreach (var pd in MudPuddle.All.ToArray()) if (IsInstanceValid(pd)) pd.QueueFree();
        MudPuddle.All.Clear();
    }

    void ServerEndRun()
    {
        if (Mode != State.Playing) return;
        Mode = State.GameOver;
        GlobalSfx("game_over");
        float delay = Net.Game.Cfg.RestartDelay;
        _restartT = delay;
        Net.Game.ClearReady();
        string stats = $"WAVE {WaveNumber}     KILLS {Kills}\nSCORE {Score:N0}\n"
            + string.Join("     ", Players.ConvertAll(p => $"{p.DisplayName} {p.Kills}"));
        Net.Game.SendRunOver("THE HORDE WINS", stats, delay);
        GD.Print($"[server] game over · wave {WaveNumber} · score {Score} · kills {Kills} · "
            + string.Join(", ", Players.ConvertAll(p => $"{p.DisplayName}={p.Kills}")));
    }

    // ---------------------------------------------------------------- client
    /// Connect to a server; on success the menu switches to the online lobby. `fail` gets the reason otherwise.
    public void JoinServer(string address, string name, int character, string password, System.Action<string> fail)
    {
        var net = Net.Game;
        System.Action<int> ok = null;
        System.Action<string> bad = null;
        ok = _ => { net.Accepted -= ok; net.Failed -= bad; OnlineAccepted(); };
        bad = err => { net.Accepted -= ok; net.Failed -= bad; fail?.Invoke(err); };
        net.Accepted += ok;
        net.Failed += bad;
        net.Connect(address, name, character, password ?? "");
    }

    void OnlineAccepted()
    {
        GameData.Slots.Clear();
        if (_menu != null) _menu.EnterOnlineLobby();
        else ShowMenu((int)MainMenu.Screen.Lobby);
    }

    /// The match is starting (or we confirmed into a running one): load the stage and spawn our player.
    public void ClientEnterMatch(int map)
    {
        var me = Net.Game.Me;
        _menu?.QueueFree(); _menu = null;
        _menuCam?.QueueFree(); _menuCam = null;
        GameData.Slots.Clear();
        GameData.SelectedCharacter = me?.Character ?? GameData.SelectedCharacter;
        GameData.SelectedMap = _map = Mathf.Clamp(map, 0, MapBase.Names.Length - 1);
        LoadMap();
        StartGame();
        var p = Players[0];
        p.NetId = Net.Game.MyId;
        p.NetName = me?.Name ?? GameData.LoadPlayerName().ToUpper();
    }

    /// The server sent us back to the lobby (match over, or we asked to change survivor). Still connected.
    public void ClientToLobby()
    {
        PlayerInput.Blocked = false;
        GameData.SkipMenu = false;
        GameData.MenuStart = (int)MainMenu.Screen.Lobby;
        Restart();
    }

    /// A teammate on another machine.
    public Player AddPuppet(int id, int character, string name, bool benched, Vector3 pos)
    {
        var p = new Player();
        p.SetupRemote(Mathf.Clamp(character, 0, GameData.Characters.Length - 1), id, name);
        World.AddChild(p);
        p.Teleport(pos);
        p.SetBenched(benched);
        Players.Add(p);
        return p;
    }

    public void RemovePuppet(int id)
    {
        var p = PlayerById(id);
        if (p == null || p.NetRole == Player.Role.Local) return;
        Players.Remove(p);
        p.QueueFree();
    }

    public void ClientRunStart(List<(int id, Vector3 pos, bool benched)> spots)
    {
        ClearWorldEntities();
        _overPanel?.GetParent()?.QueueFree();
        _overPanel = null; _overCountdown = null; _readyBtn = null;
        _pausePanel?.GetParent()?.QueueFree();
        _pausePanel = null;
        PlayerInput.Blocked = false;
        Engine.TimeScale = 1;
        foreach (var (id, pos, benched) in spots)
        {
            var p = PlayerById(id);
            if (p == null) continue;
            p.ResetForRun();
            p.SetBenched(benched);
            p.Teleport(pos);
        }
        Mode = State.Playing;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public void ClientRunOver(string title, string stats, float delay)
    {
        _pausePanel?.GetParent()?.QueueFree();
        _pausePanel = null;
        PlayerInput.Blocked = false;
        Mode = State.GameOver;
        Sfx.Play("game_over");
        Engine.TimeScale = 0.3f;
        GetTree().CreateTimer(1.0, true, false, true).Timeout += () =>
        {
            if (!IsInstanceValid(this)) return;
            Engine.TimeScale = 1f;
            if (Mode != State.GameOver) return;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _overT = delay - 1f;
            _overPanel = MakePanel(title, stats,
                ("READY", () => { Net.Game?.SendReady(); if (_readyBtn != null) { _readyBtn.Text = "WAITING FOR TEAM…"; _readyBtn.Disabled = true; } }),
                ("LEAVE SERVER", () => { GameData.NetMessage = null; LeaveServer(); }));
            _readyBtn = _overPanel.GetChildOrNull<Button>(3);
            _overCountdown = Hud.MakeLabel("", 26, new Color(0.8f, 0.8f, 0.9f), 6, HorizontalAlignment.Center);
            _overPanel.AddChild(_overCountdown);
        };
    }

    void OnlineOverProcess(float dt)
    {
        _overT -= dt;
        if (_overCountdown != null) _overCountdown.Text = $"BACK TO THE LOBBY IN {Mathf.Max(0, Mathf.CeilToInt(_overT))}";
    }

    /// Online pause: a menu over a world that keeps going.
    void ToggleOnlinePause()
    {
        if (_pausePanel == null)
        {
            Mode = State.Paused;
            PlayerInput.Blocked = true;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            var buttons = new List<(string, System.Action)> { ("RESUME", ToggleOnlinePause) };
            // spectators can go back and pick someone else
            if (LocalPlayer is { Benched: true }) buttons.Add(("CHANGE SURVIVOR", () => Net.Game?.SendBackToLobby()));
            buttons.Add(("LEAVE SERVER", () => { GameData.NetMessage = null; LeaveServer(); }));
            _pausePanel = MakePanel("PAUSED", $"WAVE {WaveNumber}     SCORE {Score:N0}\n(the horde doesn't wait online)", buttons.ToArray());
        }
        else
        {
            Mode = State.Playing;
            PlayerInput.Blocked = false;
            Input.MouseMode = Input.MouseModeEnum.Captured;
            _pausePanel.GetParent()?.QueueFree();
            _pausePanel = null;
        }
    }

    /// Hang up and go back to the server list.
    public void LeaveServer()
    {
        Net.Game?.Close();
        PlayerInput.Blocked = false;
        GameData.SkipMenu = false;
        GameData.MenuStart = (int)MainMenu.Screen.Servers;
        Restart();
    }
}
