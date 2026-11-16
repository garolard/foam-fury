using Godot;

/// Registers all input actions at runtime (Xbox controller + keyboard/mouse).
/// Gameplay actions exist twice: global ("fire", any device — menus, debug) and per player slot
/// ("p0_fire", bound to one pad and/or the keyboard+mouse) so local co-op players don't share controls.
public static class InputSetup
{
    static bool _done;
    /// PlayerSlot.Pad values besides a real pad id: listen to every pad / to none (keyboard-only player).
    public const int AnyPad = -1, NoPad = -2;

    /// One gameplay action and every way to trigger it.
    record Bind(string Name, float Deadzone, JoyAxis? Axis = null, float AxisDir = 0, JoyButton? Button = null,
        Key[] Keys = null, MouseButton[] Mouse = null);

    static readonly Bind[] Gameplay =
    {
        new("move_left", 0.18f, JoyAxis.LeftX, -1, Keys: new[] { Key.A, Key.Left }),
        new("move_right", 0.18f, JoyAxis.LeftX, 1, Keys: new[] { Key.D, Key.Right }),
        new("move_forward", 0.18f, JoyAxis.LeftY, -1, Keys: new[] { Key.W, Key.Up }),
        new("move_back", 0.18f, JoyAxis.LeftY, 1, Keys: new[] { Key.S, Key.Down }),
        new("look_left", 0.18f, JoyAxis.RightX, -1),
        new("look_right", 0.18f, JoyAxis.RightX, 1),
        new("look_up", 0.18f, JoyAxis.RightY, -1),
        new("look_down", 0.18f, JoyAxis.RightY, 1),
        new("fire", 0.25f, JoyAxis.TriggerRight, 1, Mouse: new[] { MouseButton.Left }),
        new("aim", 0.25f, JoyAxis.TriggerLeft, 1, Mouse: new[] { MouseButton.Right }),
        new("jump", 0.5f, Button: JoyButton.A, Keys: new[] { Key.Space }),
        new("dash", 0.5f, Button: JoyButton.B, Keys: new[] { Key.Shift }),
        new("reload", 0.5f, Button: JoyButton.X, Keys: new[] { Key.R }),
        new("grenade", 0.5f, Button: JoyButton.Y, Keys: new[] { Key.G }),
        new("weapon_next", 0.5f, Button: JoyButton.RightShoulder, Keys: new[] { Key.E }, Mouse: new[] { MouseButton.WheelDown }),
        new("weapon_prev", 0.5f, Button: JoyButton.LeftShoulder, Keys: new[] { Key.Q }, Mouse: new[] { MouseButton.WheelUp }),
        new("weapon_1", 0.5f, Button: JoyButton.DpadLeft, Keys: new[] { Key.Key1 }),
        new("weapon_2", 0.5f, Button: JoyButton.DpadUp, Keys: new[] { Key.Key2 }),
        new("weapon_3", 0.5f, Button: JoyButton.DpadRight, Keys: new[] { Key.Key3 }),
        new("weapon_4", 0.5f, Button: JoyButton.DpadDown, Keys: new[] { Key.Key4 }),
        // signature weapon slot (pad reaches it with LB/RB cycling; the d-pad is full)
        new("weapon_5", 0.5f, Keys: new[] { Key.Key5 }),
    };

    public static void Init()
    {
        if (_done) return;
        _done = true;

        foreach (var b in Gameplay) Register(b.Name, b, -1, true);

        Button("pause", JoyButton.Start, Key.Escape);

        // Godot's default ui_accept/ui_cancel have no gamepad bindings, so menu buttons
        // (pause, game over) couldn't be pressed with a controller.
        AddJoyButton("ui_accept", JoyButton.A);
        AddJoyButton("ui_cancel", JoyButton.B);
        // Menu navigation (in addition to Godot's ui_* defaults which already cover d-pad/A/B)
        Button("menu_accept", JoyButton.A, Key.Enter);
        AddKey("menu_accept", Key.Space);
        Button("menu_back", JoyButton.B, Key.Escape);
        Axis("menu_left", JoyAxis.LeftX, -1, Key.A, Key.Left);
        AddJoyButton("menu_left", JoyButton.DpadLeft);
        Axis("menu_right", JoyAxis.LeftX, 1, Key.D, Key.Right);
        Axis("menu_up", JoyAxis.LeftY, -1, Key.W, Key.Up);
        AddJoyButton("menu_up", JoyButton.DpadUp);
        Axis("menu_down", JoyAxis.LeftY, 1, Key.S, Key.Down);
        AddJoyButton("menu_down", JoyButton.DpadDown);
        AddJoyButton("menu_right", JoyButton.DpadRight);
    }

    /// (Re)create the "p{slot}_*" gameplay actions. `pad` AnyPad listens to every pad, NoPad to none;
    /// keyboard and mouse are bound only when `kbm` is set.
    public static void BindSlot(int slot, int pad, bool kbm)
    {
        Init();
        foreach (var b in Gameplay)
        {
            string name = SlotAction(slot, b.Name);
            if (InputMap.HasAction(name)) InputMap.ActionEraseEvents(name);
            Register(name, b, pad, kbm);
        }
    }

    public static string SlotAction(int slot, string action) => $"p{slot}_{action}";

    static void Register(string name, Bind b, int pad, bool kbm)
    {
        Add(name, b.Deadzone);
        if (pad != NoPad)
        {
            if (b.Axis is { } axis) AddJoyAxis(name, axis, b.AxisDir, pad);
            if (b.Button is { } button) AddJoyButton(name, button, pad);
        }
        if (!kbm) return;
        foreach (var k in b.Keys ?? System.Array.Empty<Key>()) AddKey(name, k);
        foreach (var m in b.Mouse ?? System.Array.Empty<MouseButton>()) AddMouse(name, m);
    }

    static void Add(string name, float deadzone = 0.2f)
    {
        if (!InputMap.HasAction(name)) InputMap.AddAction(name, deadzone);
    }

    static void Axis(string name, JoyAxis axis, float dir, params Key[] keys)
    {
        Add(name, 0.18f);
        AddJoyAxis(name, axis, dir);
        foreach (var k in keys) AddKey(name, k);
    }

    static void Button(string name, JoyButton b, params Key[] keys)
    {
        Add(name, 0.5f);
        AddJoyButton(name, b);
        foreach (var k in keys) AddKey(name, k);
    }

    static void AddJoyAxis(string name, JoyAxis axis, float dir, int device = -1) =>
        InputMap.ActionAddEvent(name, new InputEventJoypadMotion { Axis = axis, AxisValue = dir, Device = device });

    static void AddJoyButton(string name, JoyButton b, int device = -1) =>
        InputMap.ActionAddEvent(name, new InputEventJoypadButton { ButtonIndex = b, Device = device });

    static void AddKey(string name, Key k) =>
        InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = k });

    static void AddMouse(string name, MouseButton b) =>
        InputMap.ActionAddEvent(name, new InputEventMouseButton { ButtonIndex = b });

    /// Vibrate every connected pad.
    public static void Rumble(float weak, float strong, float duration) => Rumble(AnyPad, weak, strong, duration);

    /// Vibrate one pad (AnyPad = all of them, NoPad = none).
    public static void Rumble(int pad, float weak, float strong, float duration)
    {
        if (pad == NoPad) return;
        foreach (int id in Input.GetConnectedJoypads())
            if (pad < 0 || id == pad)
                Input.StartJoyVibration(id, Mathf.Clamp(weak, 0, 1), Mathf.Clamp(strong, 0, 1), duration);
    }
}
