using Godot;

/// Registers all input actions at runtime (Xbox controller + keyboard/mouse).
public static class InputSetup
{
    static bool _done;

    public static void Init()
    {
        if (_done) return;
        _done = true;

        Axis("move_left", JoyAxis.LeftX, -1, Key.A, Key.Left);
        Axis("move_right", JoyAxis.LeftX, 1, Key.D, Key.Right);
        Axis("move_forward", JoyAxis.LeftY, -1, Key.W, Key.Up);
        Axis("move_back", JoyAxis.LeftY, 1, Key.S, Key.Down);
        Axis("look_left", JoyAxis.RightX, -1);
        Axis("look_right", JoyAxis.RightX, 1);
        Axis("look_up", JoyAxis.RightY, -1);
        Axis("look_down", JoyAxis.RightY, 1);

        Add("fire", 0.25f);
        AddJoyAxis("fire", JoyAxis.TriggerRight, 1);
        AddMouse("fire", MouseButton.Left);
        Add("aim", 0.25f);
        AddJoyAxis("aim", JoyAxis.TriggerLeft, 1);
        AddMouse("aim", MouseButton.Right);

        Button("jump", JoyButton.A, Key.Space);
        Button("dash", JoyButton.B, Key.Shift);
        Button("reload", JoyButton.X, Key.R);
        Button("grenade", JoyButton.Y, Key.G);
        Button("weapon_next", JoyButton.RightShoulder, Key.E);
        Button("weapon_prev", JoyButton.LeftShoulder, Key.Q);
        AddMouse("weapon_next", MouseButton.WheelDown);
        AddMouse("weapon_prev", MouseButton.WheelUp);
        Button("pause", JoyButton.Start, Key.Escape);
        Button("weapon_1", JoyButton.DpadLeft, Key.Key1);
        Button("weapon_2", JoyButton.DpadUp, Key.Key2);
        Button("weapon_3", JoyButton.DpadRight, Key.Key3);
        Button("weapon_4", JoyButton.DpadDown, Key.Key4);
        // signature weapon slot (pad reaches it with LB/RB cycling; the d-pad is full)
        Add("weapon_5", 0.5f);
        AddKey("weapon_5", Key.Key5);

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

    static void AddJoyAxis(string name, JoyAxis axis, float dir) =>
        InputMap.ActionAddEvent(name, new InputEventJoypadMotion { Axis = axis, AxisValue = dir, Device = -1 });

    static void AddJoyButton(string name, JoyButton b) =>
        InputMap.ActionAddEvent(name, new InputEventJoypadButton { ButtonIndex = b, Device = -1 });

    static void AddKey(string name, Key k) =>
        InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = k });

    static void AddMouse(string name, MouseButton b) =>
        InputMap.ActionAddEvent(name, new InputEventMouseButton { ButtonIndex = b });

    public static void Rumble(float weak, float strong, float duration)
    {
        foreach (int id in Input.GetConnectedJoypads())
            Input.StartJoyVibration(id, Mathf.Clamp(weak, 0, 1), Mathf.Clamp(strong, 0, 1), duration);
    }
}
