using Godot;
using System;
using System.Collections.Generic;

/// Ring of 3D items that rolls so the selected one faces the camera (which sits on +Z).
public partial class Carousel : Node3D
{
    public float Radius = 2.5f;
    /// Turntable speed (rad/s) of the front item; 0 = gentle idle sway instead.
    public float SpinSpeed;
    /// Scale of items away from the front; at 0 they shrink away completely (and are hidden).
    public float MinScale = 0.8f;
    /// Called every frame per item with its focus (1 = front, 0 = off to the side).
    public Action<int, float> Focus;
    public int Index { get; private set; }
    public int Count => _items.Count;

    readonly List<Node3D> _items = new();
    readonly List<float> _spin = new();
    float _angle, _target, _t;
    float Step => Mathf.Tau / _items.Count;

    public void Add(Node3D n)
    {
        var holder = new Node3D();
        AddChild(holder);
        holder.AddChild(n);
        _items.Add(holder);
        _spin.Add(0f);
    }

    public void Select(int i)
    {
        Index = i;
        _angle = _target = -i * Step;
        Layout(0);
    }

    /// Roll one slot; the target accumulates so the ring always takes the short way and wraps seamlessly.
    public void Move(int d)
    {
        Index = (Index + d + Count) % Count;
        _target -= d * Step;
    }

    public override void _Process(double delta)
    {
        if (!Visible || Count == 0) return;
        float dt = (float)delta;
        _t += dt;
        _angle = Mathf.Lerp(_angle, _target, 1f - Mathf.Exp(-dt * 8f));
        Layout(dt);
    }

    void Layout(float dt)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            float th = i * Step + _angle;
            float f = Mathf.Clamp(1f - Mathf.Abs(Mathf.Wrap(th, -Mathf.Pi, Mathf.Pi)) / Step, 0f, 1f);
            f = f * f * (3f - 2f * f);
            var h = _items[i];
            h.Position = new Vector3(Mathf.Sin(th) * Radius, 0, Mathf.Cos(th) * Radius);
            _spin[i] += dt * SpinSpeed * f;
            float sway = SpinSpeed == 0 ? Mathf.Sin(_t * 0.9f + i) * 0.3f * f : 0f;
            h.Rotation = new Vector3(0, th + _spin[i] + sway, 0);
            float sc = Mathf.Lerp(MinScale, 1f, f);
            h.Visible = sc > 0.01f;
            if (h.Visible) h.Scale = Vector3.One * sc;
            Focus?.Invoke(i, f);
        }
    }
}

/// Horizontal attribute bar that eases toward its target.
public partial class StatBar : Control
{
    public float Target, Value;
    public Color Fill = Colors.White;

    public StatBar() { CustomMinimumSize = new Vector2(0, 18); SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ShrinkCenter; }

    public override void _Process(double delta)
    {
        Value = Mathf.Lerp(Value, Target, 1f - Mathf.Exp(-(float)delta * 10f));
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawStyleBox(Hud.Box(new Color(0.05f, 0.03f, 0.07f, 0.7f), 5, 2, new Color(0, 0, 0, 0.8f)), new Rect2(Vector2.Zero, Size));
        float w = (Size.X - 6) * Mathf.Clamp(Value, 0f, 1f);
        if (w > 1) DrawStyleBox(Hud.Box(Fill, 3), new Rect2(3, 3, w, Size.Y - 6));
        for (int i = 1; i < 10; i++) DrawLine(new Vector2(Size.X * i / 10f, 3), new Vector2(Size.X * i / 10f, Size.Y - 3), new Color(0, 0, 0, 0.35f), 2f);
    }
}

/// Front end: title -> survivor select -> stage select.
public partial class MainMenu : CanvasLayer
{
    public enum Screen { Title, Survivor, Stage }

    /// (character, map) when the player starts a run.
    public event Action<int, int> Finished;
    /// Map to show behind the menu; raised once the stage carousel settles on a new stage.
    public event Action<int> BackdropChanged;

    static readonly Color Gold = new(1f, 0.8f, 0.3f), Mint = new(0.65f, 1f, 0.55f), Soft = new(0.85f, 0.85f, 0.9f), Dim = new(0.6f, 0.58f, 0.68f);
    const float MiniScale = 0.055f;

    Screen _screen;
    Control _root, _titleUi, _pickUi, _survInfo, _stageInfo, _shadeTitle, _shadePick;
    SubViewportContainer _showcase;
    Camera3D _cam;
    SpotLight3D _spot;
    Node3D _charSet, _mapSet;
    Carousel _chars, _maps;
    readonly List<CharacterModel> _models = new();
    readonly List<MapBase> _minis = new();
    Button _single, _multi;
    Label _soon, _footer, _arrowL, _arrowR;
    readonly Label[] _crumbs = new Label[3];
    float _cool, _settle;
    int _backdrop;
    bool _done;

    // survivor card
    Label _cIndex, _cName, _cTitle, _cPerk;
    readonly List<(StatBar bar, Label value)> _cStats = new();
    // stage card
    Label _sIndex, _sName, _sSub, _sBest, _sGates, _sRise;

    public MainMenu() { Layer = 10; }

    public void Open(Screen start)
    {
        _backdrop = GameData.SelectedMap;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        _shadeTitle = new ColorRect { Color = new Color(0.04f, 0.02f, 0.07f, 0.4f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _shadeTitle.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_shadeTitle);
        BuildShowcase();
        // over the showcase so neighbours drifting behind the info card get darkened too
        var g = new Gradient();
        g.SetColor(0, new Color(0.04f, 0.02f, 0.07f, 0.2f));
        g.SetColor(1, new Color(0.04f, 0.02f, 0.07f, 0.9f));
        g.AddPoint(0.42f, new Color(0.04f, 0.02f, 0.07f, 0.2f));
        _shadePick = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = g, FillFrom = Vector2.Zero, FillTo = new Vector2(1, 0) },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _shadePick.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_shadePick);

        BuildTitle();
        BuildPicker();
        _chars.Select(GameData.SelectedCharacter);
        _maps.Select(GameData.SelectedMap);
        Go(start, true);
    }

    // ------------------------------------------------------------------ 3D showcase
    void BuildShowcase()
    {
        _showcase = new SubViewportContainer { Stretch = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        _showcase.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_showcase);
        var vp = new SubViewport { OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X };
        _showcase.AddChild(vp);
        var studio = new Node3D();
        vp.AddChild(studio);

        studio.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor, BackgroundColor = new Color(0, 0, 0, 0),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.55f, 0.55f, 0.72f),
                AmbientLightEnergy = 0.4f, TonemapMode = Godot.Environment.ToneMapper.Filmic, TonemapExposure = 1.1f,
            },
        });
        studio.AddChild(new DirectionalLight3D { LightEnergy = 0.45f, RotationDegrees = new Vector3(-40, 30, 0), LightColor = new Color(0.85f, 0.85f, 1f) });
        // rim light from behind the ring, toward the camera
        studio.AddChild(new DirectionalLight3D { LightEnergy = 1.3f, RotationDegrees = new Vector3(-20, 180, 0), LightColor = new Color(0.55f, 0.75f, 1f) });
        _spot = new SpotLight3D { LightEnergy = 9f, SpotRange = 16f, ShadowEnabled = true, LightColor = new Color(1f, 0.92f, 0.8f), SpotAttenuation = 0.6f };
        studio.AddChild(_spot);
        _cam = new Camera3D { Current = true };
        studio.AddChild(_cam);

        // survivors
        _charSet = new Node3D();
        studio.AddChild(_charSet);
        _chars = new Carousel { Radius = 2.6f };
        _charSet.AddChild(_chars);
        foreach (var c in GameData.Characters)
        {
            var m = new CharacterModel(c.Skin, 0.62f);
            _chars.Add(m);
            m.Play("idle", 1f, 0f);
            _models.Add(m);
        }
        var dark = new Color(0.32f, 0.3f, 0.38f);
        _chars.Focus = (i, f) => _models[i].Material.AlbedoColor = dark.Lerp(Colors.White, f);
        _charSet.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.85f, BottomRadius = 0.95f, Height = 0.14f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.09f, 0.14f), Roughness = 0.35f, Metallic = 0.4f },
            Position = new Vector3(0, -0.07f, _chars.Radius),
        });
        _charSet.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.86f, OuterRadius = 0.93f }, MaterialOverride = Fx.Glow(Gold),
            Position = new Vector3(0, 0.005f, _chars.Radius), Scale = new Vector3(1, 0.3f, 1),
        });

        // stage dioramas
        _mapSet = new Node3D();
        studio.AddChild(_mapSet);
        _maps = new Carousel { Radius = 3.6f, SpinSpeed = 0.18f, MinScale = 0f };
        _mapSet.AddChild(_maps);
        float w = MapBase.MiniExtent * 2f * MiniScale;
        for (int i = 0; i < MapBase.Names.Length; i++)
        {
            var d = new Node3D();
            d.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(w + 0.14f, 0.3f, w + 0.14f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.13f, 0.1f, 0.14f), Roughness = 0.5f, Metallic = 0.3f },
                Position = new Vector3(0, -0.16f, 0),
            });
            var map = MapBase.Create(i);
            map.Detail = MapBase.BuildMode.Miniature;
            map.Scale = Vector3.One * MiniScale;
            d.AddChild(map);
            _maps.Add(d);
            _minis.Add(map);
        }
    }

    void FrameCamera(Screen s, bool instant)
    {
        bool stage = s == Screen.Stage;
        float r = stage ? _maps.Radius : _chars.Radius;
        var pos = stage ? new Vector3(0, 4.6f, r + 6.4f) : new Vector3(0, 1.6f, r + 6.2f);
        var look = stage ? new Vector3(0, -0.1f, r) : new Vector3(0, 1.05f, r);
        var xf = new Transform3D(Basis.Identity, pos).LookingAt(look, Vector3.Up);
        float fov = stage ? 38f : 34f, hoff = stage ? 1.9f : 1.35f;
        _spot.SpotAngle = stage ? 24f : 17f;
        _spot.Transform = new Transform3D(Basis.Identity, stage ? new Vector3(0, 6.5f, r + 1.6f) : new Vector3(0, 4.8f, r + 1.8f))
            .LookingAt(stage ? new Vector3(0, 0, r) : new Vector3(0, 0.9f, r), Vector3.Up);
        if (instant) { _cam.Transform = xf; _cam.Fov = fov; _cam.HOffset = hoff; return; }
        var tw = _cam.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(_cam, "transform", xf, 0.5f);
        tw.TweenProperty(_cam, "fov", fov, 0.5f);
        tw.TweenProperty(_cam, "h_offset", hoff, 0.5f);
    }

    // ------------------------------------------------------------------ title
    void BuildTitle()
    {
        _titleUi = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _titleUi.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_titleUi);

        var logo = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        logo.AnchorRight = 1; logo.AnchorTop = 0.08f; logo.AnchorBottom = 0.52f;
        logo.AddThemeConstantOverride("separation", -18);
        _titleUi.AddChild(logo);
        var big = Hud.MakeLabel("FOAM FURY", 210, Gold, 36, HorizontalAlignment.Center);
        big.LabelSettings.ShadowColor = new Color(0.55f, 0.16f, 0.06f, 1f);
        big.LabelSettings.ShadowOffset = new Vector2(0, 14);
        big.LabelSettings.ShadowSize = 2;
        logo.AddChild(big);
        logo.AddChild(Hud.MakeLabel("Z O M B I E   A R E N A", 58, Mint, 14, HorizontalAlignment.Center));
        logo.Resized += () => logo.PivotOffset = logo.Size / 2f;
        logo.Rotation = Mathf.DegToRad(-2.5f);
        var bob = logo.CreateTween().SetLoops().SetTrans(Tween.TransitionType.Sine);
        bob.TweenProperty(logo, "scale", Vector2.One * 1.035f, 1.4f);
        bob.TweenProperty(logo, "scale", Vector2.One, 1.4f);

        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Begin };
        col.AnchorLeft = 0.5f; col.AnchorRight = 0.5f; col.AnchorTop = 0.58f; col.AnchorBottom = 0.95f;
        col.OffsetLeft = -260; col.OffsetRight = 260;
        col.AddThemeConstantOverride("separation", 18);
        _titleUi.AddChild(col);
        _single = Hud.MakeButton("SINGLE PLAYER", 36, new Vector2(480, 80));
        _multi = Hud.MakeButton("MULTIPLAYER", 36, new Vector2(480, 80));
        _multi.AddThemeColorOverride("font_color", Dim);
        var tag = Hud.MakeLabel("SOON", 18, Gold, 5);
        tag.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        tag.OffsetLeft = -78; tag.OffsetTop = -14; tag.Rotation = Mathf.DegToRad(-8);
        _multi.AddChild(tag);
        foreach (var b in new[] { _single, _multi })
        {
            b.FocusEntered += () => { if (_screen == Screen.Title) Sfx.Play("ui_move", -6f); };
            b.MouseEntered += b.GrabFocus;
            b.PivotOffset = new Vector2(240, 40);
            col.AddChild(b);
        }
        _single.Pressed += () => { if (_cool > 0) return; Sfx.Play("ui_select"); Go(Screen.Survivor); };
        _multi.Pressed += ComingSoon;
        _soon = Hud.MakeLabel("ONLINE MULTIPLAYER IS COMING SOON", 24, Gold, 8, HorizontalAlignment.Center);
        _soon.Modulate = new Color(1, 1, 1, 0);
        col.AddChild(_soon);

        var help = new VBoxContainer();
        _titleUi.AddChild(help);
        help.AnchorLeft = 1; help.AnchorRight = 1; help.AnchorTop = 1; help.AnchorBottom = 1;
        help.OffsetLeft = -480; help.OffsetRight = -44; help.OffsetTop = -270; help.OffsetBottom = -36;
        help.Alignment = BoxContainer.AlignmentMode.End;
        help.AddChild(Hud.MakeLabel("CONTROLS", 24, Gold, 8, HorizontalAlignment.Right));
        foreach (var line in new[] {
            "LS  move     RS  look", "RT  fire     LT  aim (snaps to target)", "A  jump     B  dash", "X  reload     Y  grenade",
            "LB / RB  switch blaster     START  pause", "keyboard + mouse also supported" })
            help.AddChild(Hud.MakeLabel(line, 18, new Color(0.9f, 0.9f, 0.95f), 6, HorizontalAlignment.Right));
    }

    void ComingSoon()
    {
        Sfx.Play("ui_move", -2f, 0f);
        var wig = _multi.CreateTween();
        foreach (float a in new[] { 3f, -3f, 2f, -1f, 0f }) wig.TweenProperty(_multi, "rotation", Mathf.DegToRad(a), 0.05f);
        _soon.Modulate = Colors.White;
        var fade = _soon.CreateTween();
        fade.TweenInterval(1.2f);
        fade.TweenProperty(_soon, "modulate:a", 0f, 0.5f);
    }

    // ------------------------------------------------------------------ survivor / stage
    void BuildPicker()
    {
        _pickUi = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _pickUi.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_pickUi);

        var crumbs = new HBoxContainer { Position = new Vector2(56, 36) };
        crumbs.AddThemeConstantOverride("separation", 14);
        _pickUi.AddChild(crumbs);
        string[] steps = { "SINGLE PLAYER", "SURVIVOR", "STAGE" };
        for (int i = 0; i < 3; i++)
        {
            if (i > 0) crumbs.AddChild(Hud.MakeLabel("▸", 22, Dim, 6));
            _crumbs[i] = Hud.MakeLabel(steps[i], 26, Dim, 6);
            crumbs.AddChild(_crumbs[i]);
        }

        _arrowL = Hud.MakeLabel("◀", 64, Colors.White, 12, HorizontalAlignment.Center);
        _arrowR = Hud.MakeLabel("▶", 64, Colors.White, 12, HorizontalAlignment.Center);
        foreach (var (a, x) in new[] { (_arrowL, 0.07f), (_arrowR, 0.5f) })
        {
            a.AnchorLeft = a.AnchorRight = x; a.AnchorTop = a.AnchorBottom = 0.5f;
            a.OffsetLeft = -40; a.OffsetRight = 40; a.OffsetTop = -44; a.OffsetBottom = 44;
            a.PivotOffset = new Vector2(40, 44);
            a.Modulate = new Color(1, 1, 1, 0.75f);
            _pickUi.AddChild(a);
        }

        _survInfo = InfoCard();
        _cIndex = Add(_survInfo, Hud.MakeLabel("", 22, Dim, 6));
        _cName = Add(_survInfo, Hud.MakeLabel("", 120, Colors.White, 20));
        _cTitle = Add(_survInfo, Hud.MakeLabel("", 36, Gold, 9));
        Add(_survInfo, new Control { CustomMinimumSize = new Vector2(0, 8) });
        var perk = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        var pb = Hud.Box(new Color(0.2f, 0.45f, 0.18f, 0.55f), 8, 2, new Color(0.55f, 1f, 0.5f, 0.8f));
        pb.ContentMarginLeft = pb.ContentMarginRight = 18; pb.ContentMarginTop = pb.ContentMarginBottom = 8;
        perk.AddThemeStyleboxOverride("panel", pb);
        _cPerk = Hud.MakeLabel("", 26, new Color(0.75f, 1f, 0.7f), 6);
        perk.AddChild(_cPerk);
        Add(_survInfo, perk);
        Add(_survInfo, new Control { CustomMinimumSize = new Vector2(0, 22) });
        foreach (var (name, color) in new[] {
            ("HEALTH", new Color(0.95f, 0.3f, 0.32f)), ("SPEED", new Color(0.35f, 0.85f, 1f)), ("DAMAGE", new Color(1f, 0.6f, 0.2f)),
            ("RELOAD", new Color(1f, 0.85f, 0.3f)), ("GRENADES", new Color(0.55f, 1f, 0.45f)) })
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 16);
            var n = Hud.MakeLabel(name, 24, Soft, 6);
            n.CustomMinimumSize = new Vector2(150, 0);
            var bar = new StatBar { Fill = color };
            var v = Hud.MakeLabel("", 24, Colors.White, 6, HorizontalAlignment.Right);
            v.CustomMinimumSize = new Vector2(84, 0);
            row.AddChild(n); row.AddChild(bar); row.AddChild(v);
            Add(_survInfo, row);
            _cStats.Add((bar, v));
        }

        _stageInfo = InfoCard();
        _sIndex = Add(_stageInfo, Hud.MakeLabel("", 22, Dim, 6));
        _sName = Add(_stageInfo, Hud.MakeLabel("", 96, Colors.White, 18));
        _sSub = Add(_stageInfo, Hud.MakeLabel("", 30, Soft, 7));
        _sSub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Add(_stageInfo, new Control { CustomMinimumSize = new Vector2(0, 26) });
        _sBest = StageRow("BEST SCORE");
        _sGates = StageRow("SPAWN GATES");
        _sRise = StageRow("RISING DEAD");

        _footer = Hud.MakeLabel("", 24, Soft, 7, HorizontalAlignment.Center);
        _footer.AnchorLeft = 0; _footer.AnchorRight = 1; _footer.AnchorTop = 1; _footer.AnchorBottom = 1;
        _footer.OffsetTop = -74; _footer.OffsetBottom = -34;
        _pickUi.AddChild(_footer);
    }

    VBoxContainer InfoCard()
    {
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        holder.AnchorLeft = 0.57f; holder.AnchorRight = 0.95f; holder.AnchorTop = 0.13f; holder.AnchorBottom = 0.86f;
        _pickUi.AddChild(holder);
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.AddThemeConstantOverride("separation", 6);
        box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        holder.AddChild(box);
        return box;
    }

    static T Add<T>(Control parent, T c) where T : Control { parent.AddChild(c); return c; }

    Label StageRow(string name)
    {
        var row = new HBoxContainer();
        var n = Hud.MakeLabel(name, 26, Soft, 6);
        n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var v = Hud.MakeLabel("", 30, Colors.White, 7, HorizontalAlignment.Right);
        row.AddChild(n); row.AddChild(v);
        _stageInfo.AddChild(row);
        var line = new ColorRect { Color = new Color(1, 1, 1, 0.12f), CustomMinimumSize = new Vector2(0, 2) };
        _stageInfo.AddChild(line);
        return v;
    }

    void Refresh(int dir)
    {
        if (_screen == Screen.Survivor)
        {
            int i = _chars.Index;
            var c = GameData.Characters[i];
            _cIndex.Text = $"SURVIVOR  {i + 1} / {_chars.Count}";
            _cName.Text = c.Name;
            _cTitle.Text = c.Title.ToUpper();
            _cPerk.Text = "★  " + c.Perk.ToUpper();
            float[] vals = { c.MaxHp / 160f, c.Speed / 7.4f, c.DamageMult / 1.25f, 0.55f / c.ReloadMult, c.Grenades / 6f };
            string[] txt = { $"{c.MaxHp:0}", $"{c.Speed:0.0}", $"{c.DamageMult * 100:0}%", $"{100 / c.ReloadMult:0}%", $"{c.Grenades}" };
            for (int k = 0; k < _cStats.Count; k++) { _cStats[k].bar.Target = vals[k]; _cStats[k].value.Text = txt[k]; }
            if (dir != 0) _models[i].Flash(0.5f);
            Slide(_survInfo, dir);
        }
        else if (_screen == Screen.Stage)
        {
            int i = _maps.Index;
            var m = _minis[i];
            _sIndex.Text = $"STAGE  {i + 1} / {_maps.Count}";
            _sName.Text = m.Title;
            _sSub.Text = m.Subtitle;
            int best = GameData.LoadBest(i);
            _sBest.Text = best > 0 ? best.ToString("N0") : "—";
            _sBest.LabelSettings.FontColor = Gold;
            _sGates.Text = m.GateSpawns.Count.ToString();
            bool rise = m.InnerSpawns.Count > 0 && m.RiseChance(10) > 0;
            _sRise.Text = rise ? "YES" : "NO";
            _sRise.LabelSettings.FontColor = rise ? new Color(1f, 0.45f, 0.35f) : Mint;
            Slide(_stageInfo, dir);
        }
    }

    static void Slide(Control box, int dir)
    {
        box.Position = new Vector2(dir * 60f, 0);
        box.Modulate = new Color(1, 1, 1, dir == 0 ? 0.3f : 0f);
        var tw = box.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(box, "position:x", 0f, 0.28f);
        tw.TweenProperty(box, "modulate:a", 1f, 0.22f);
    }

    void Go(Screen s, bool instant = false)
    {
        _screen = s;
        _cool = 0.2f;
        bool title = s == Screen.Title;
        Fade(_titleUi, title, instant);
        Fade(_shadeTitle, title, instant);
        Fade(_pickUi, !title, instant);
        Fade(_shadePick, !title, instant);
        Fade(_showcase, !title, instant);
        _charSet.Visible = s == Screen.Survivor;
        _mapSet.Visible = s == Screen.Stage;
        _survInfo.Visible = s == Screen.Survivor;
        _stageInfo.Visible = s == Screen.Stage;
        for (int i = 0; i < 3; i++) _crumbs[i].LabelSettings.FontColor = i == (int)s ? Gold : i < (int)s ? Soft : Dim;
        _footer.Text = s == Screen.Stage ? "◀ ▶  CHOOSE STAGE        (A)  FIGHT!        (B)  BACK"
                                         : "◀ ▶  CHOOSE SURVIVOR        (A)  SELECT        (B)  BACK";
        if (title) _single.CallDeferred(Control.MethodName.GrabFocus);
        else { GetViewport().GuiReleaseFocus(); FrameCamera(s, instant); Refresh(0); }
    }

    static void Fade(CanvasItem c, bool on, bool instant)
    {
        if (instant || !on) { c.Visible = on; c.Modulate = Colors.White; return; }
        c.Visible = true;
        c.Modulate = new Color(1, 1, 1, 0);
        c.CreateTween().TweenProperty(c, "modulate:a", 1f, 0.25f);
    }

    void Pulse(Label arrow)
    {
        arrow.Scale = Vector2.One * 1.4f;
        arrow.Modulate = Gold;
        var tw = arrow.CreateTween().SetParallel();
        tw.TweenProperty(arrow, "scale", Vector2.One, 0.2f);
        tw.TweenProperty(arrow, "modulate", new Color(1, 1, 1, 0.75f), 0.3f);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _cool -= dt;
        if (_settle > 0 && (_settle -= dt) <= 0 && _maps.Index != _backdrop)
        {
            _backdrop = _maps.Index;
            BackdropChanged?.Invoke(_backdrop);
        }
        if (_done || _cool > 0 || _screen == Screen.Title) return;

        var car = _screen == Screen.Survivor ? _chars : _maps;
        int d = Input.IsActionJustPressed("menu_left") ? -1 : Input.IsActionJustPressed("menu_right") ? 1 : 0;
        if (d != 0)
        {
            car.Move(d);
            Sfx.Play("ui_move");
            Pulse(d < 0 ? _arrowL : _arrowR);
            Refresh(d);
            if (_screen == Screen.Stage) _settle = 0.45f;
        }
        else if (Input.IsActionJustPressed("menu_accept") || Input.IsActionJustPressed("fire"))
        {
            Sfx.Play("ui_select");
            if (_screen == Screen.Survivor) Go(Screen.Stage);
            else { _done = true; Finished?.Invoke(_chars.Index, _maps.Index); }
        }
        else if (Input.IsActionJustPressed("menu_back"))
        {
            Sfx.Play("ui_move");
            Go(_screen == Screen.Stage ? Screen.Survivor : Screen.Title);
        }
    }
}
