using Godot;
using System.Collections.Generic;

/// Local co-op split-screen: one SubViewport per player, all rendering the same World3D.
/// 2 players sit side by side; 3-4 players get a 2x2 grid (the spare cell of a 3-player game shows the logo).
public static class SplitScreen
{
    /// Builds the viewports under `owner` and returns them (one per player).
    /// Solo returns null: the player keeps using the root viewport, exactly as before.
    public static List<SubViewport> Build(Node owner, int players)
    {
        if (players <= 1) return null;
        var root = owner.GetViewport();
        var world = root.FindWorld3D();

        // layer 0 sits under the post-process pass (layer 1), so the screen-space grade still covers every view
        var layer = new CanvasLayer { Layer = 0, Name = "SplitScreen" };
        owner.AddChild(layer);
        var back = new ColorRect { Color = new Color(0.02f, 0.01f, 0.03f), MouseFilter = Control.MouseFilterEnum.Ignore };
        back.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(back);
        var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        layer.AddChild(grid);

        var views = new List<SubViewport>();
        int cells = players == 2 ? 2 : 4;
        for (int i = 0; i < cells; i++)
        {
            if (i >= players)
            {
                grid.AddChild(Filler());
                continue;
            }
            var box = new SubViewportContainer
            {
                Stretch = true, MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };
            grid.AddChild(box);
            var vp = new SubViewport
            {
                Name = $"P{i + 1}View", World3D = world,
                Msaa3D = root.Msaa3D, ScreenSpaceAA = root.ScreenSpaceAA, UseTaa = root.UseTaa,
                // input goes straight to the players (root viewport); nothing in here needs the mouse
                GuiDisableInput = true, AudioListenerEnable3D = false,
            };
            box.AddChild(vp);
            views.Add(vp);
        }
        return views;
    }

    static Control Filler()
    {
        var c = new ColorRect
        {
            Color = new Color(0.06f, 0.03f, 0.08f), MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        var logo = Hud.MakeLabel("FOAM FURY", 96, new Color(1f, 0.8f, 0.3f), 18, HorizontalAlignment.Center);
        logo.VerticalAlignment = VerticalAlignment.Center;
        logo.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        logo.Rotation = Mathf.DegToRad(-2.5f);
        c.AddChild(logo);
        return c;
    }
}
