using Godot;
using System.Collections.Generic;

public partial class Crosshair : Control
{
    public Player Player;
    public float Hit, Kill;
    public bool HitHead;

    public override void _Process(double delta)
    {
        Hit = Mathf.Max(0, Hit - (float)delta * 4f);
        Kill = Mathf.Max(0, Kill - (float)delta * 3f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Player == null || Player.Dead || Player.Downed) return;
        var c = Size / 2f;
        float gap = 7f + Player.Spread * 4.5f;
        var col = Player.AimingAtEnemy ? new Color(1f, 0.3f, 0.25f) : Colors.White;
        var sh = new Color(0, 0, 0, 0.55f);
        float len = 9f;
        foreach (var d in new[] { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right })
        {
            DrawLine(c + d * gap + Vector2.One, c + d * (gap + len) + Vector2.One, sh, 3f);
            DrawLine(c + d * gap, c + d * (gap + len), col, 2f);
        }
        DrawCircle(c, 2f, col);
        if (Hit > 0)
        {
            var hc = (Kill > 0 ? new Color(1f, 0.25f, 0.2f) : HitHead ? new Color(1f, 0.85f, 0.2f) : Colors.White) with { A = Hit };
            float s = 6f + (1f - Hit) * 4f + (Kill > 0 ? 4f : 0f);
            foreach (var d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
                DrawLine(c + d * s, c + d * (s + 8f), hc, Kill > 0 ? 3.5f : 2.5f);
        }
        if (Player.ReloadProgress >= 0)
        {
            DrawArc(c, 26f, 0, Mathf.Tau, 48, new Color(0, 0, 0, 0.4f), 5f);
            DrawArc(c, 26f, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * Player.ReloadProgress, 48, new Color(1f, 0.85f, 0.3f), 4f);
        }
    }
}

public partial class Hud : CanvasLayer
{
    Player _player;
    Crosshair _cross;
    ProgressBar _hp;
    Label _hpText, _ammo, _reserve, _weaponName, _grenades, _wave, _left, _score, _combo, _banner, _bannerSub, _hint;
    TextureRect _weaponIcon;
    ColorRect _vignette;
    ShaderMaterial _vigMat;
    VBoxContainer _toasts;
    Control _dash;
    float _hurt, _lowPulse;
    Tween _bannerTween;
    // split-screen: the HUD is laid out on a larger virtual canvas and scaled down into the player's view
    Control _root;
    float _uiScale = 1f;
    // co-op: downed / revive panel
    Control _downBox;
    Label _downTitle, _downSub;
    ProgressBar _reviveBar;
    bool _greyTint;

    public static Label MakeLabel(string text, int size, Color? color = null, int outline = 8, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        return new Label
        {
            Text = text, HorizontalAlignment = align,
            LabelSettings = new LabelSettings
            {
                Font = Main.TitleFont, FontSize = size, FontColor = color ?? Colors.White,
                OutlineSize = outline, OutlineColor = new Color(0.05f, 0.03f, 0.06f, 0.9f),
                ShadowSize = 0, ShadowColor = new Color(0, 0, 0, 0.5f), ShadowOffset = new Vector2(2, 3),
            },
        };
    }

    public void Init(Player p, float uiScale = 1f)
    {
        _player = p;
        Layer = 5;
        _uiScale = uiScale;
        var root = _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        if (uiScale >= 1f) root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        if (uiScale < 1f) { root.Scale = Vector2.One * uiScale; FitRoot(); }

        _vignette = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
        _vignette.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _vigMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = @"shader_type canvas_item;
uniform float amount = 0.0;
uniform vec4 tint : source_color = vec4(0.8, 0.0, 0.05, 1.0);
void fragment() {
    vec2 uv = UV - 0.5;
    float d = length(uv * vec2(1.6, 1.0));
    float v = smoothstep(0.35, 0.95, d);
    COLOR = vec4(tint.rgb, v * amount);
}"
            }
        };
        _vignette.Material = _vigMat;
        root.AddChild(_vignette);

        _cross = new Crosshair { Player = p, MouseFilter = Control.MouseFilterEnum.Ignore };
        _cross.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(_cross);

        // ---- bottom-left: name + health ----
        var bl = new VBoxContainer();
        bl.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        bl.Position = new Vector2(36, -150);
        bl.GrowVertical = Control.GrowDirection.Begin;
        root.AddChild(bl);
        bl.AnchorTop = 1; bl.AnchorBottom = 1; bl.OffsetTop = -128; bl.OffsetLeft = 36;
        bl.AddChild(MakeLabel(p.Def.Name, 30, new Color(1f, 0.85f, 0.4f)));
        _hp = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(340, 26) };
        _hp.AddThemeStyleboxOverride("background", Box(new Color(0.08f, 0.05f, 0.08f, 0.75f), 6, 2, new Color(0, 0, 0, 0.8f)));
        _hp.AddThemeStyleboxOverride("fill", Box(new Color(0.85f, 0.2f, 0.25f), 6));
        bl.AddChild(_hp);
        _hpText = MakeLabel("100", 20);
        _hpText.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hpText.HorizontalAlignment = HorizontalAlignment.Center;
        _hpText.VerticalAlignment = VerticalAlignment.Center;
        _hp.AddChild(_hpText);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);
        bl.AddChild(row);
        _dash = new Control { CustomMinimumSize = new Vector2(120, 22) };
        _dash.Draw += () =>
        {
            float r = _player.DashReady;
            _dash.DrawRect(new Rect2(0, 6, 110, 10), new Color(0, 0, 0, 0.5f));
            _dash.DrawRect(new Rect2(0, 6, 110 * r, 10), r >= 1 ? new Color(0.4f, 0.85f, 1f) : new Color(0.3f, 0.5f, 0.6f));
        };
        row.AddChild(MakeLabel("DASH", 16, new Color(0.6f, 0.9f, 1f), 6));
        row.AddChild(_dash);

        // ---- bottom-right: weapon ----
        var br = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        root.AddChild(br);
        br.AnchorLeft = 1; br.AnchorRight = 1; br.AnchorTop = 1; br.AnchorBottom = 1;
        br.OffsetLeft = -380; br.OffsetRight = -36; br.OffsetTop = -190; br.OffsetBottom = -24;
        var wrow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        br.AddChild(wrow);
        _weaponIcon = new TextureRect { CustomMinimumSize = new Vector2(96, 96), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        wrow.AddChild(_weaponIcon);
        var ammoCol = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        wrow.AddChild(ammoCol);
        _weaponName = MakeLabel("", 22, new Color(1f, 0.85f, 0.4f), 6, HorizontalAlignment.Right);
        ammoCol.AddChild(_weaponName);
        var arow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        ammoCol.AddChild(arow);
        _ammo = MakeLabel("12", 56, null, 10, HorizontalAlignment.Right);
        _reserve = MakeLabel("/ ∞", 26, new Color(0.85f, 0.85f, 0.85f), 6);
        _reserve.VerticalAlignment = VerticalAlignment.Bottom;
        _reserve.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        arow.AddChild(_ammo); arow.AddChild(_reserve);
        _grenades = MakeLabel("", 20, new Color(0.5f, 1f, 0.5f), 6, HorizontalAlignment.Right);
        br.AddChild(_grenades);

        // ---- top center: wave ----
        var tc = new VBoxContainer();
        root.AddChild(tc);
        tc.AnchorLeft = 0.5f; tc.AnchorRight = 0.5f; tc.OffsetLeft = -200; tc.OffsetRight = 200; tc.OffsetTop = 18;
        _wave = MakeLabel("WAVE 1", 36, null, 10, HorizontalAlignment.Center);
        _left = MakeLabel("", 20, new Color(0.75f, 1f, 0.7f), 6, HorizontalAlignment.Center);
        tc.AddChild(_wave); tc.AddChild(_left);

        // ---- top right: score ----
        var tr = new VBoxContainer();
        root.AddChild(tr);
        tr.AnchorLeft = 1; tr.AnchorRight = 1; tr.OffsetLeft = -400; tr.OffsetRight = -36; tr.OffsetTop = 18;
        _score = MakeLabel("0", 44, new Color(1f, 0.9f, 0.5f), 10, HorizontalAlignment.Right);
        _combo = MakeLabel("", 26, new Color(1f, 0.5f, 0.3f), 8, HorizontalAlignment.Right);
        tr.AddChild(_score); tr.AddChild(_combo);

        // ---- center banner ----
        var bc = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(bc);
        bc.AnchorLeft = 0; bc.AnchorRight = 1; bc.AnchorTop = 0.22f; bc.AnchorBottom = 0.22f;
        _banner = MakeLabel("", 84, new Color(1f, 0.85f, 0.35f), 16, HorizontalAlignment.Center);
        _bannerSub = MakeLabel("", 28, Colors.White, 8, HorizontalAlignment.Center);
        bc.AddChild(_banner); bc.AddChild(_bannerSub);
        bc.Modulate = new Color(1, 1, 1, 0);
        _banner.GetParent<Control>().Name = "BannerBox";

        _toasts = new VBoxContainer();
        root.AddChild(_toasts);
        _toasts.AnchorLeft = 0.5f; _toasts.AnchorRight = 0.5f; _toasts.AnchorTop = 0.62f; _toasts.AnchorBottom = 0.62f;
        _toasts.OffsetLeft = -200; _toasts.OffsetRight = 200;

        _hint = MakeLabel("", 22, new Color(1f, 0.6f, 0.4f), 6, HorizontalAlignment.Center);
        root.AddChild(_hint);
        _hint.AnchorLeft = 0.5f; _hint.AnchorRight = 0.5f; _hint.AnchorTop = 0.56f; _hint.AnchorBottom = 0.56f;
        _hint.OffsetLeft = -200; _hint.OffsetRight = 200;

        // ---- co-op: downed / reviving panel ----
        _downBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(_downBox);
        _downBox.AnchorLeft = 0.5f; _downBox.AnchorRight = 0.5f; _downBox.AnchorTop = 0.4f; _downBox.AnchorBottom = 0.4f;
        _downBox.OffsetLeft = -320; _downBox.OffsetRight = 320;
        _downTitle = MakeLabel("", 56, new Color(1f, 0.35f, 0.3f), 12, HorizontalAlignment.Center);
        _downSub = MakeLabel("", 24, Colors.White, 7, HorizontalAlignment.Center);
        _reviveBar = new ProgressBar { MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(360, 22), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        _reviveBar.AddThemeStyleboxOverride("background", Box(new Color(0.08f, 0.05f, 0.08f, 0.75f), 6, 2, new Color(0, 0, 0, 0.8f)));
        _reviveBar.AddThemeStyleboxOverride("fill", Box(new Color(0.45f, 1f, 0.45f), 6));
        _downBox.AddChild(_downTitle); _downBox.AddChild(_downSub); _downBox.AddChild(_reviveBar);
        _downBox.Visible = false;

        WeaponChanged();
    }

    void FitRoot()
    {
        var size = GetViewport().GetVisibleRect().Size / _uiScale;
        if (_root.Size != size) _root.Size = size;
    }

    public static Button MakeButton(string text, int fontSize = 30, Vector2? size = null)
    {
        var b = new Button { Text = text, CustomMinimumSize = size ?? new Vector2(380, 64), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        b.AddThemeFontOverride("font", Main.TitleFont);
        b.AddThemeFontSizeOverride("font_size", fontSize);
        b.AddThemeStyleboxOverride("normal", Box(new Color(0.12f, 0.08f, 0.14f, 0.9f), 10, 3, new Color(0.4f, 0.3f, 0.45f)));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.25f, 0.15f, 0.2f, 0.95f), 10, 3, new Color(1f, 0.75f, 0.3f)));
        b.AddThemeStyleboxOverride("focus", Box(new Color(0.85f, 0.5f, 0.15f, 0.95f), 10, 4, new Color(1f, 0.9f, 0.5f)));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(1f, 0.6f, 0.2f), 10, 4, Colors.White));
        return b;
    }

    public static StyleBoxFlat Box(Color c, int radius, int border = 0, Color? borderColor = null)
    {
        var s = new StyleBoxFlat { BgColor = c };
        s.SetCornerRadiusAll(radius);
        if (border > 0) { s.SetBorderWidthAll(border); s.BorderColor = borderColor ?? Colors.Black; }
        return s;
    }

    public void WeaponChanged()
    {
        if (_player == null) return;
        var w = _player.Weapon;
        _weaponName.Text = w.Name.ToUpper();
        _weaponIcon.Texture = ResourceLoader.Exists(w.IconPath) ? GD.Load<Texture2D>(w.IconPath) : null;
    }

    public void HitMarker(bool head, bool kill)
    {
        _cross.Hit = 1f; _cross.HitHead = head;
        if (kill) _cross.Kill = 1f;
    }

    public void Hurt(float frac) => _hurt = Mathf.Min(1f, _hurt + 0.4f + frac * 2f);

    public void Toast(string text, Color color)
    {
        var l = MakeLabel(text, 26, color, 8, HorizontalAlignment.Center);
        _toasts.AddChild(l);
        var tw = l.CreateTween();
        tw.TweenInterval(0.9f);
        tw.TweenProperty(l, "modulate:a", 0f, 0.4f);
        tw.TweenCallback(Callable.From(l.QueueFree));
    }

    public void Banner(string text, string sub, float hold = 1.8f)
    {
        _banner.Text = text; _bannerSub.Text = sub;
        var box = _banner.GetParent<Control>();
        _bannerTween?.Kill();
        box.Modulate = new Color(1, 1, 1, 0);
        box.Scale = Vector2.One;
        _bannerTween = box.CreateTween();
        _bannerTween.TweenProperty(box, "modulate:a", 1f, 0.2f);
        _bannerTween.TweenInterval(hold);
        _bannerTween.TweenProperty(box, "modulate:a", 0f, 0.5f);
    }

    public void Pop(Control c)
    {
        c.PivotOffset = c.Size / 2f;
        var tw = c.CreateTween();
        c.Scale = Vector2.One * 1.25f;
        tw.TweenProperty(c, "scale", Vector2.One, 0.15f);
    }

    int _lastScore = -1, _lastCombo = -1;

    public override void _Process(double delta)
    {
        if (_player == null) return;
        float dt = (float)delta;
        var m = Main.Instance;
        if (_uiScale < 1f) FitRoot();
        _hp.Value = _player.Hp / _player.MaxHp;
        _hpText.Text = $"{Mathf.CeilToInt(_player.Hp)} / {_player.MaxHp}";
        int i = _player.WeaponIndex;
        _ammo.Text = _player.Mag[i].ToString();
        _ammo.LabelSettings.FontColor = _player.Mag[i] <= Mathf.Max(1, _player.Weapon.MagSize / 4) ? new Color(1f, 0.4f, 0.3f) : Colors.White;
        _reserve.Text = _player.Reserve[i] < 0 ? " / ∞" : $" / {_player.Reserve[i]}";
        _grenades.Text = $"GRENADES  {_player.Grenades}";
        _dash.QueueRedraw();

        _wave.Text = m.WaveNumber > 0 ? $"WAVE {m.WaveNumber}" : "GET READY";
        _left.Text = m.InBreak ? $"next wave in {Mathf.CeilToInt(m.BreakTimer)}" : $"{m.ZombiesRemaining} zombies left";
        if (m.Score != _lastScore) { _lastScore = m.Score; _score.Text = m.Score.ToString("N0"); Pop(_score); }
        if (m.ComboMult != _lastCombo)
        {
            _lastCombo = m.ComboMult;
            _combo.Text = m.ComboMult > 1 ? $"COMBO x{m.ComboMult}" : "";
            if (m.ComboMult > 1) Pop(_combo);
        }

        if (_player.ReloadProgress >= 0) _hint.Text = "RELOADING";
        else if (_player.Mag[i] == 0 && _player.Reserve[i] == 0) _hint.Text = "OUT OF AMMO — SWITCH WEAPON (LB/RB)";
        else if (_player.Mag[i] <= _player.Weapon.MagSize / 4) _hint.Text = "RELOAD (X)";
        else _hint.Text = "";

        CoopStatus(m);

        _hurt = Mathf.Max(0, _hurt - dt * 1.5f);
        float low = _player.Hp / _player.MaxHp < 0.3f && !_player.Dead ? 0.35f + 0.15f * Mathf.Sin(Time.GetTicksMsec() * 0.008f) : 0f;
        bool grey = _player.Downed || _player.Dead;
        if (grey != _greyTint)
        {
            _greyTint = grey;
            _vigMat.SetShaderParameter("tint", grey ? new Color(0.12f, 0.12f, 0.14f) : new Color(0.8f, 0f, 0.05f));
        }
        float amount = grey ? 0.85f : Mathf.Max(_hurt, low);
        _vigMat.SetShaderParameter("amount", Mathf.Clamp(amount, 0, 1));
        // solo: the HUD fades with the death; co-op: a fallen player keeps watching the team's run
        Visible = !_player.Dead || _hurt > 0 || m.Coop;
    }

    /// Downed countdown for this player, or a revive prompt when standing next to a downed teammate.
    void CoopStatus(Main m)
    {
        if (!m.Coop) return;
        string key = _player.UsingPad ? "X" : "R";
        if (_player.Downed)
        {
            _downBox.Visible = true;
            _downTitle.Text = "YOU'RE DOWN!";
            _downTitle.LabelSettings.FontColor = new Color(1f, 0.35f, 0.3f);
            var helper = m.Reviver(_player);
            _downSub.Text = helper != null ? $"{helper.DisplayName} IS REVIVING YOU…" : $"BLEEDING OUT  {Mathf.CeilToInt(_player.BleedOut)}s   ·   CRAWL TO A TEAMMATE";
            _reviveBar.Visible = _player.ReviveProgress > 0;
            _reviveBar.Value = _player.ReviveProgress;
            _hint.Text = "";
        }
        else if (_player.Dead)
        {
            _downBox.Visible = true;
            _downTitle.Text = "BLED OUT";
            _downTitle.LabelSettings.FontColor = new Color(0.8f, 0.7f, 0.75f);
            _downSub.Text = "YOU'LL BE BACK WHEN THE WAVE IS CLEARED";
            _reviveBar.Visible = false;
            _hint.Text = "";
        }
        else if (m.DownedNear(_player) is { } mate)
        {
            _downBox.Visible = true;
            bool reviving = m.Reviver(mate) == _player;
            _downTitle.Text = reviving ? $"REVIVING {mate.DisplayName}" : $"HOLD ({key})  REVIVE {mate.DisplayName}";
            _downTitle.LabelSettings.FontColor = new Color(0.5f, 1f, 0.5f);
            _downSub.Text = $"{Mathf.CeilToInt(mate.BleedOut)}s LEFT";
            _reviveBar.Visible = true;
            _reviveBar.Value = mate.ReviveProgress;
        }
        else _downBox.Visible = false;
    }
}
