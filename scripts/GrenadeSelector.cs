using Godot;

/// D-pad shaped grenade picker. Pops up around the crosshair when the player picks a grenade
/// (d-pad / 1-4), with each type at the d-pad direction that selects it, then fades away.
/// The same drawing (scaled down, no text) is used as the permanent mini indicator in the corner.
public partial class GrenadeSelector : Control
{
    public Player Player;
    /// Mini mode: tiny always-on cross for the bottom-right HUD.
    public bool Mini;

    const float ShowTime = 1.6f, FadeTime = 0.3f;
    static readonly Vector2[] Dirs = { Vector2.Left, Vector2.Up, Vector2.Right, Vector2.Down }; // matches grenade_type_1..4

    float _show, _pop, _t;
    int _last = -1;

    public void Open()
    {
        _show = ShowTime;
        _pop = 1f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        _show = Mathf.Max(0, _show - dt);
        _pop = Mathf.Max(0, _pop - dt * 5f);
        if (Player != null && Player.GrenadeType != _last) { if (_last >= 0 && Mini) _pop = 1f; _last = Player.GrenadeType; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Player == null) return;
        if (Mini) { DrawMini(); return; }
        if (_show <= 0 || Player.Dead || Player.Downed) return;
        float a = Mathf.Clamp(_show / FadeTime, 0f, 1f) * Mathf.Clamp((ShowTime - _show) / 0.08f + 0.3f, 0f, 1f);
        float open = Mathf.Clamp((ShowTime - _show) / 0.15f, 0f, 1f);
        open = 1f - Mathf.Pow(1f - open, 3);
        var c = Size / 2f;
        float spread = 128f * Mathf.Lerp(0.6f, 1f, open);
        int sel = Player.GrenadeType;
        var font = Main.TitleFont;

        // backdrop: soft dark disc + faint d-pad cross
        for (int i = 0; i < 6; i++) DrawCircle(c, spread + 80 - i * 7, new Color(0.03f, 0.02f, 0.05f, 0.09f * a));
        var cross = new Color(1, 1, 1, 0.06f * a);
        DrawRect(new Rect2(c.X - spread - 10, c.Y - 20, (spread + 10) * 2, 40), cross);
        DrawRect(new Rect2(c.X - 20, c.Y - spread - 10, 40, (spread + 10) * 2), cross);

        for (int i = 0; i < 4; i++)
        {
            var col = GrenadeTypes.Colors[i];
            bool on = i == sel;
            var p = c + Dirs[i] * spread * (on ? 1f + 0.08f * _pop : 1f);
            float r = on ? 50f * (1f + 0.25f * _pop) : 38f;
            if (on)
            {
                // halo + bright ring
                for (int k = 0; k < 5; k++) DrawCircle(p, r + 22 - k * 4, col with { A = 0.07f * a });
                DrawCircle(p, r, new Color(col.R * 0.25f, col.G * 0.25f, col.B * 0.25f, 0.92f * a));
                DrawArc(p, r, 0, Mathf.Tau, 48, col with { A = a }, 5f, true);
                float spin = _t * 2.5f;
                DrawArc(p, r + 9, spin, spin + 1.2f, 16, col with { A = 0.8f * a }, 3f, true);
                DrawArc(p, r + 9, spin + Mathf.Pi, spin + Mathf.Pi + 1.2f, 16, col with { A = 0.8f * a }, 3f, true);
                DrawIcon(this, p, r * 0.72f, i, col with { A = a }, _t);
            }
            else
            {
                var dim = new Color(0.55f, 0.55f, 0.6f).Lerp(col, 0.25f);
                DrawCircle(p, r, new Color(0.08f, 0.07f, 0.1f, 0.75f * a));
                DrawArc(p, r, 0, Mathf.Tau, 40, dim with { A = 0.5f * a }, 2.5f, true);
                DrawIcon(this, p, r * 0.68f, i, dim with { A = 0.75f * a }, 0f);
            }
            // which button picks it: d-pad arrow on pad, number key on keyboard
            var badge = p + Dirs[i] * (r + 20);
            if (Player.UsingPad) DrawArrow(badge, Dirs[i], on ? 11f : 8f, (on ? Colors.White : new Color(0.7f, 0.7f, 0.75f)) with { A = a });
            else
            {
                DrawCircle(badge, 13, new Color(0, 0, 0, 0.6f * a));
                DrawString(font, badge + new Vector2(-20, 7), (i + 1).ToString(), HorizontalAlignment.Center, 40, 20,
                    (on ? Colors.White : new Color(0.7f, 0.7f, 0.75f)) with { A = a });
            }
        }

        // name of the pick in the middle of the cross
        var selCol = GrenadeTypes.Colors[sel];
        string name = GrenadeTypes.Names[sel];
        int size = 30;
        var namePos = c + new Vector2(-150, 10);
        DrawStringOutline(font, namePos, name, HorizontalAlignment.Center, 300, size, 10, new Color(0.03f, 0.02f, 0.05f, 0.9f * a));
        DrawString(font, namePos, name, HorizontalAlignment.Center, 300, size, selCol.Lightened(0.25f) with { A = a });
        var blurbPos = c + new Vector2(-150, 34);
        DrawStringOutline(font, blurbPos, GrenadeTypes.Blurbs[sel], HorizontalAlignment.Center, 300, 14, 6, new Color(0.03f, 0.02f, 0.05f, 0.9f * a));
        DrawString(font, blurbPos, GrenadeTypes.Blurbs[sel], HorizontalAlignment.Center, 300, 14, new Color(0.9f, 0.9f, 0.95f, 0.9f * a));
    }

    void DrawMini()
    {
        var c = Size / 2f;
        int sel = Player.GrenadeType;
        float spread = Size.X * 0.32f;
        for (int i = 0; i < 4; i++)
        {
            var col = GrenadeTypes.Colors[i];
            bool on = i == sel;
            var p = c + Dirs[i] * spread;
            float r = on ? Size.X * 0.17f * (1f + 0.3f * _pop) : Size.X * 0.12f;
            DrawCircle(p, r, on ? new Color(col.R * 0.3f, col.G * 0.3f, col.B * 0.3f, 0.9f) : new Color(0.06f, 0.05f, 0.08f, 0.7f));
            DrawArc(p, r, 0, Mathf.Tau, 24, on ? col : new Color(0.5f, 0.5f, 0.55f, 0.5f), on ? 2.5f : 1.5f, true);
            DrawIcon(this, p, r * 0.7f, i, on ? col : new Color(0.6f, 0.6f, 0.65f, 0.6f), on ? _t : 0f);
        }
    }

    void DrawArrow(Vector2 at, Vector2 dir, float s, Color col)
    {
        var side = new Vector2(-dir.Y, dir.X);
        var pts = new[] { at + dir * s, at - dir * s * 0.6f + side * s, at - dir * s * 0.6f - side * s };
        var shadow = new[] { pts[0] + new Vector2(1, 2), pts[1] + new Vector2(1, 2), pts[2] + new Vector2(1, 2) };
        DrawColoredPolygon(shadow, new Color(0, 0, 0, 0.6f * col.A));
        DrawColoredPolygon(pts, col);
    }

    /// Procedural grenade icon of radius `r` centred on `c`. `t` animates it (0 = still).
    public static void DrawIcon(CanvasItem ci, Vector2 c, float r, int type, Color col, float t)
    {
        var dark = new Color(0.04f, 0.03f, 0.06f, col.A);
        float w = Mathf.Max(1.5f, r * 0.12f);
        switch (type)
        {
            case Grenade.Frag:
            {
                var body = c + new Vector2(0, r * 0.15f);
                ci.DrawCircle(body, r * 0.62f, col);
                ci.DrawRect(new Rect2(c.X - r * 0.22f, c.Y - r * 0.72f, r * 0.44f, r * 0.3f), col);
                ci.DrawArc(c + new Vector2(r * 0.42f, -r * 0.62f), r * 0.2f, 0, Mathf.Tau, 16, col, w * 0.8f, true);
                ci.DrawLine(body + new Vector2(-r * 0.62f, 0), body + new Vector2(r * 0.62f, 0), dark, w);
                ci.DrawLine(body + new Vector2(0, -r * 0.62f), body + new Vector2(0, r * 0.62f), dark, w);
                break;
            }
            case Grenade.BlackHole:
            {
                for (int arm = 0; arm < 3; arm++)
                {
                    var pts = new Vector2[14];
                    for (int k = 0; k < pts.Length; k++)
                    {
                        float f = k / (float)(pts.Length - 1);
                        float ang = arm * Mathf.Tau / 3f + f * 2.6f - t * 3f;
                        pts[k] = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Mathf.Lerp(r * 1.0f, r * 0.3f, f);
                    }
                    ci.DrawPolyline(pts, col, w, true);
                }
                ci.DrawCircle(c, r * 0.36f, dark);
                ci.DrawArc(c, r * 0.36f, 0, Mathf.Tau, 20, col, w * 0.8f, true);
                break;
            }
            case Grenade.Incendiary:
            {
                float flick = t > 0 ? Mathf.Sin(t * 14f) * 0.06f : 0f;
                Vector2[] Flame(float s, float lean) => new[]
                {
                    c + new Vector2(lean, -1f - flick) * r * s, c + new Vector2(0.35f, -0.45f) * r * s, c + new Vector2(0.6f, 0.1f) * r * s,
                    c + new Vector2(0.5f, 0.55f) * r * s, c + new Vector2(0.2f, 0.82f) * r * s, c + new Vector2(-0.2f, 0.82f) * r * s,
                    c + new Vector2(-0.5f, 0.55f) * r * s, c + new Vector2(-0.6f, 0.1f) * r * s, c + new Vector2(-0.3f, -0.35f) * r * s,
                };
                ci.DrawColoredPolygon(Flame(1f, 0.12f), col);
                var inner = new Color(1f, 0.92f, 0.55f, col.A).Lerp(col, col.S < 0.4f ? 1f : 0f);
                var ip = Flame(0.5f, -0.1f);
                for (int k = 0; k < ip.Length; k++) ip[k] += new Vector2(0, r * 0.38f);
                ci.DrawColoredPolygon(ip, inner);
                break;
            }
            case Grenade.Shockwave:
            {
                float ph = t > 0 ? Mathf.PosMod(t * 1.5f, 1f) : 0.5f;
                ci.DrawCircle(c, r * 0.22f, col);
                for (int k = 0; k < 3; k++)
                {
                    float rr = r * (0.42f + 0.28f * k + 0.28f * ph);
                    var cc = col with { A = col.A * (1f - (k + ph) / 3.2f) };
                    ci.DrawArc(c, rr, -0.7f, 0.7f, 12, cc, w, true);
                    ci.DrawArc(c, rr, Mathf.Pi - 0.7f, Mathf.Pi + 0.7f, 12, cc, w, true);
                }
                ci.DrawLine(c + new Vector2(-r, r * 0.75f), c + new Vector2(r, r * 0.75f), col, w);
                break;
            }
        }
    }
}
