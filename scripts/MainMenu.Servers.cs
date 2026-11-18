using Godot;
using System;
using System.Collections.Generic;

/// Online server browser: saved servers (host:port or URL), live info + ping, add / edit / remove, join.
public partial class MainMenu
{
    /// (address, player name, survivor, password) when the player picks a server.
    public event Action<string, string, int, string> JoinRequested;

    Control _serversUi, _editBox, _pwBox;
    VBoxContainer _srvList;
    LineEdit _nameEdit, _editName, _editAddr, _pwEdit;
    Label _srvStatus, _editTitle;
    Button _btnAdd, _btnEdit, _btnRemove, _btnRefresh, _btnBack;
    readonly List<SavedServer> _servers = new();
    readonly List<(Button row, Label info, Label motd)> _rows = new();
    readonly Dictionary<int, ServerInfo> _infos = new();
    int _selected = -1, _editing = -1, _joinIdx = -1;
    bool _online, _joining;
    int _queryGen;

    void BuildServers()
    {
        _serversUi = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _serversUi.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _pickUi.AddChild(_serversUi);

        var panel = new PanelContainer();
        panel.AnchorLeft = 0.1f; panel.AnchorRight = 0.9f; panel.AnchorTop = 0.13f; panel.AnchorBottom = 0.85f;
        var sb = Hud.Box(new Color(0.07f, 0.04f, 0.09f, 0.88f), 16, 3, new Color(Gold, 0.7f));
        sb.ContentMarginLeft = sb.ContentMarginRight = 28; sb.ContentMarginTop = sb.ContentMarginBottom = 22;
        panel.AddThemeStyleboxOverride("panel", sb);
        _serversUi.AddChild(panel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 14);
        panel.AddChild(col);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 16);
        col.AddChild(top);
        top.AddChild(Hud.MakeLabel("YOUR NAME", 26, Soft, 6));
        _nameEdit = MakeEdit("SURVIVOR", 360);
        _nameEdit.MaxLength = 16;
        _nameEdit.Text = GameData.LoadPlayerName();
        _nameEdit.TextChanged += t => GameData.SavePlayerName(t.StripEdges());
        top.AddChild(_nameEdit);

        _srvStatus = Hud.MakeLabel("", 24, Gold, 6);
        col.AddChild(_srvStatus);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        col.AddChild(scroll);
        _srvList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _srvList.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_srvList);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 14);
        col.AddChild(buttons);
        _btnAdd = SrvButton(buttons, "ADD SERVER", () => OpenEdit(-1));
        _btnEdit = SrvButton(buttons, "EDIT", () => { if (_selected >= 0) OpenEdit(_selected); });
        _btnRemove = SrvButton(buttons, "REMOVE", RemoveSelected);
        _btnRefresh = SrvButton(buttons, "REFRESH", QueryAll);
        _btnBack = SrvButton(buttons, "BACK", () => Go(Screen.Title));

        // add / edit dialog
        _editBox = Dialog(out var eb);
        _editTitle = Hud.MakeLabel("ADD SERVER", 40, Gold, 8);
        eb.AddChild(_editTitle);
        eb.AddChild(Hud.MakeLabel("NAME", 22, Soft, 5));
        _editName = MakeEdit("My friend's server", 560);
        eb.AddChild(_editName);
        eb.AddChild(Hud.MakeLabel("ADDRESS  (host:port or URL — port 7777 if left out)", 22, Soft, 5));
        _editAddr = MakeEdit("play.example.com:7777", 560);
        _editAddr.TextSubmitted += _ => SaveEdit();
        eb.AddChild(_editAddr);
        var er = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        er.AddThemeConstantOverride("separation", 14);
        eb.AddChild(er);
        SrvButton(er, "SAVE", SaveEdit);
        SrvButton(er, "CANCEL", CloseDialogs);

        // password prompt
        _pwBox = Dialog(out var pb);
        pb.AddChild(Hud.MakeLabel("PASSWORD", 40, Gold, 8));
        _pwEdit = MakeEdit("server password", 560);
        _pwEdit.Secret = true;
        _pwEdit.TextSubmitted += _ => { var pw = _pwEdit.Text; CloseDialogs(); Join(_joinIdx, pw); };
        pb.AddChild(_pwEdit);
        var pr = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pr.AddThemeConstantOverride("separation", 14);
        pb.AddChild(pr);
        SrvButton(pr, "JOIN", () => { var pw = _pwEdit.Text; CloseDialogs(); Join(_joinIdx, pw); });
        SrvButton(pr, "CANCEL", CloseDialogs);

        _serversUi.Visible = false;
    }

    static LineEdit MakeEdit(string placeholder, float width)
    {
        var e = new LineEdit { PlaceholderText = placeholder, CustomMinimumSize = new Vector2(width, 54) };
        e.AddThemeFontOverride("font", Main.TitleFont);
        e.AddThemeFontSizeOverride("font_size", 26);
        e.AddThemeStyleboxOverride("normal", Hud.Box(new Color(0.03f, 0.02f, 0.04f, 0.9f), 8, 2, new Color(0.4f, 0.3f, 0.45f)));
        e.AddThemeStyleboxOverride("focus", Hud.Box(new Color(0.03f, 0.02f, 0.04f, 0.9f), 8, 3, Gold));
        return e;
    }

    Button SrvButton(Control parent, string text, Action act)
    {
        var b = Hud.MakeButton(text, 24, new Vector2(190, 56));
        b.FocusEntered += () => Sfx.Play("ui_move", -6f);
        b.MouseEntered += b.GrabFocus;
        b.Pressed += () => { if (_joining) return; Sfx.Play("ui_select"); act(); };
        parent.AddChild(b);
        return b;
    }

    Control Dialog(out VBoxContainer box)
    {
        var dim = new ColorRect { Color = new Color(0.02f, 0.01f, 0.03f, 0.75f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _serversUi.AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.AddChild(center);
        var panel = new PanelContainer();
        var sb = Hud.Box(new Color(0.09f, 0.05f, 0.11f, 0.97f), 14, 3, Gold);
        sb.ContentMarginLeft = sb.ContentMarginRight = 30; sb.ContentMarginTop = sb.ContentMarginBottom = 24;
        panel.AddThemeStyleboxOverride("panel", sb);
        center.AddChild(panel);
        box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);
        dim.Visible = false;
        return dim;
    }

    bool DialogOpen => _editBox.Visible || _pwBox.Visible;

    void CloseDialogs()
    {
        _editBox.Visible = _pwBox.Visible = false;
        FocusList();
    }

    void OpenServers()
    {
        _joining = false;
        CloseDialogs();
        _servers.Clear();
        _servers.AddRange(GameData.LoadServers());
        if (!string.IsNullOrEmpty(GameData.NetMessage)) { ServerStatus(GameData.NetMessage, true); GameData.NetMessage = null; }
        else ServerStatus(_servers.Count == 0 ? "ADD A SERVER TO GET STARTED  ·  ASK YOUR HOST FOR ITS ADDRESS" : "", false);
        RebuildRows();
        QueryAll();
    }

    void RebuildRows()
    {
        foreach (var (row, _, _) in _rows) row.QueueFree();
        _rows.Clear();
        for (int i = 0; i < _servers.Count; i++)
        {
            int idx = i;
            var s = _servers[i];
            var row = Hud.MakeButton("", 24, new Vector2(0, 92));
            row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var h = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            h.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            h.OffsetLeft = 22; h.OffsetRight = -22;
            row.AddChild(h);
            var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            left.AddThemeConstantOverride("separation", 0);
            h.AddChild(left);
            left.AddChild(Hud.MakeLabel(string.IsNullOrWhiteSpace(s.Name) ? s.Address : s.Name.ToUpper(), 32, Colors.White, 7));
            var motd = Hud.MakeLabel(s.Address, 20, Dim, 5);
            left.AddChild(motd);
            var info = Hud.MakeLabel("…", 26, Soft, 6, HorizontalAlignment.Right);
            info.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            h.AddChild(info);
            row.FocusEntered += () => { _selected = idx; Sfx.Play("ui_move", -6f); };
            row.MouseEntered += row.GrabFocus;
            row.Pressed += () => { if (_joining) return; Sfx.Play("ui_select"); TryJoin(idx); };
            _srvList.AddChild(row);
            _rows.Add((row, info, motd));
        }
        if (_selected >= _servers.Count) _selected = _servers.Count - 1;
        FocusList();
    }

    void FocusList()
    {
        if (_screen != Screen.Servers) return;
        if (_rows.Count > 0) _rows[Mathf.Clamp(_selected, 0, _rows.Count - 1)].row.CallDeferred(Control.MethodName.GrabFocus);
        else _btnAdd.CallDeferred(Control.MethodName.GrabFocus);
    }

    void QueryAll()
    {
        int gen = ++_queryGen;
        _infos.Clear();
        for (int i = 0; i < _servers.Count; i++)
        {
            int idx = i;
            _rows[i].info.Text = "…";
            _rows[i].info.LabelSettings.FontColor = Soft;
            var q = ServerQuery.Start(this, _servers[i].Address);
            q.Done += (info, err) =>
            {
                if (gen != _queryGen || idx >= _rows.Count) return;
                var (_, label, motd) = _rows[idx];
                if (info == null || err != null)
                {
                    label.Text = err ?? "OFFLINE";
                    label.LabelSettings.FontColor = new Color(1f, 0.45f, 0.4f);
                    if (info == null) return;
                }
                _infos[idx] = info;
                if (err == null)
                {
                    string map = info.Map < 0 ? "LOBBY" : info.Map < MapBase.Names.Length ? MapBase.Names[info.Map] : "?";
                    label.Text = $"{map}   {info.Players}/{info.MaxPlayers}{(info.Locked ? "   LOCKED" : "")}   {info.PingMs} MS";
                    label.LabelSettings.FontColor = info.Players >= info.MaxPlayers ? Dim : Mint;
                }
                motd.Text = $"{(string.IsNullOrEmpty(info.Name) ? "" : info.Name.ToUpper() + "  ·  ")}{_servers[idx].Address}" +
                    (string.IsNullOrEmpty(info.Motd) ? "" : $"  ·  {info.Motd}");
            };
        }
    }

    void TryJoin(int idx)
    {
        if (idx < 0 || idx >= _servers.Count) return;
        _joinIdx = idx;
        if (_infos.TryGetValue(idx, out var info) && info.Locked)
        {
            _pwEdit.Text = "";
            _pwBox.Visible = true;
            _pwEdit.CallDeferred(Control.MethodName.GrabFocus);
            return;
        }
        Join(idx, "");
    }

    void Join(int idx, string password)
    {
        if (idx < 0 || idx >= _servers.Count) return;
        string name = _nameEdit.Text.StripEdges();
        if (name.Length == 0) { ServerStatus("ENTER YOUR NAME FIRST", true); _nameEdit.GrabFocus(); return; }
        GameData.SavePlayerName(name);
        _joining = true;
        var s = _servers[idx];
        ServerStatus($"CONNECTING TO {(string.IsNullOrWhiteSpace(s.Name) ? s.Address : s.Name).ToUpper()}…", false);
        // the survivor is picked in the lobby; this is just where our card starts
        JoinRequested?.Invoke(s.Address, name, GameData.SelectedCharacter, password);
    }

    /// Status line under the name (connection errors land here).
    public void ServerStatus(string text, bool error)
    {
        _joining = false;
        if (_srvStatus == null) return;
        _srvStatus.Text = text ?? "";
        _srvStatus.LabelSettings.FontColor = error ? new Color(1f, 0.45f, 0.4f) : Gold;
        if (error) Sfx.Play("empty");
    }

    void OpenEdit(int idx)
    {
        _editing = idx;
        _editTitle.Text = idx < 0 ? "ADD SERVER" : "EDIT SERVER";
        _editName.Text = idx < 0 ? "" : _servers[idx].Name;
        _editAddr.Text = idx < 0 ? "" : _servers[idx].Address;
        _editBox.Visible = true;
        _editName.CallDeferred(Control.MethodName.GrabFocus);
    }

    void SaveEdit()
    {
        var addr = _editAddr.Text.StripEdges();
        if (!Net.ParseAddress(addr, out _, out _)) { Sfx.Play("empty"); _editAddr.GrabFocus(); return; }
        var entry = new SavedServer(_editName.Text.StripEdges(), addr);
        if (_editing < 0) { _servers.Add(entry); _selected = _servers.Count - 1; }
        else _servers[_editing] = entry;
        GameData.SaveServers(_servers);
        _editBox.Visible = false;
        ServerStatus("", false);
        RebuildRows();
        QueryAll();
    }

    void RemoveSelected()
    {
        if (_selected < 0 || _selected >= _servers.Count) return;
        _servers.RemoveAt(_selected);
        GameData.SaveServers(_servers);
        RebuildRows();
        QueryAll();
    }

    /// Esc / (B) on the browser: close a dialog, else back to the title.
    void ServersBack()
    {
        if (_joining) return;
        if (DialogOpen) { CloseDialogs(); return; }
        Sfx.Play("ui_move");
        _online = false;
        Go(Screen.Title);
    }
}
