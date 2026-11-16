using Godot;
using System.Collections.Generic;

/// One player's controls: reads that player's slot actions ("p{slot}_*").
public class PlayerInput
{
    public readonly int Slot, Pad;
    public readonly bool Kbm;
    readonly Dictionary<string, StringName> _names = new();

    public PlayerInput(int slot, int pad, bool kbm)
    {
        Slot = slot; Pad = pad; Kbm = kbm;
        InputSetup.BindSlot(slot, pad, kbm);
    }

    /// Slot-specific action name for a gameplay action ("fire" -> "p1_fire").
    public StringName A(string action)
    {
        if (!_names.TryGetValue(action, out var n)) _names[action] = n = InputSetup.SlotAction(Slot, action);
        return n;
    }

    public bool Pressed(string action) => Input.IsActionPressed(A(action));
    public bool JustPressed(string action) => Input.IsActionJustPressed(A(action));
    public Vector2 Move => Input.GetVector(A("move_left"), A("move_right"), A("move_forward"), A("move_back"));
    public Vector2 Look => Input.GetVector(A("look_left"), A("look_right"), A("look_up"), A("look_down"));
    public void Rumble(float weak, float strong, float duration) => InputSetup.Rumble(Pad, weak, strong, duration);
}
