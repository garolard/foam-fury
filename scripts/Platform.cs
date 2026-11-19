using Godot;

/// Hardware quirks we adapt to.
public static class Platform
{
    static bool? _steamDeck;

    /// True on a Steam Deck: its Van Gogh APU/GPU, or Steam's own flag in Game Mode. FF_DECK=1 forces it for testing.
    public static bool IsSteamDeck => _steamDeck ??= DetectSteamDeck();

    static bool DetectSteamDeck()
    {
        if (OS.GetEnvironment("FF_DECK") is { Length: > 0 } fd) return fd == "1";
        if (OS.GetEnvironment("SteamDeck") == "1") return true;
        var gpu = RenderingServer.GetRenderingDevice()?.GetDeviceName() ?? RenderingServer.GetVideoAdapterName();
        return gpu.Contains("RADV VANGOGH") || OS.GetProcessorName().Contains("AMD CUSTOM APU 0405");
    }

    /// The Deck's screen is 1280x800 but our window defaults to 1600x900, so Game Mode crops the edges off.
    /// Going fullscreen makes the window match the screen; the canvas_items/expand stretch then fits the UI to 16:10.
    public static void ApplyDisplay()
    {
        if (!IsSteamDeck) return;
        if (DisplayServer.WindowGetMode() is not (DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen))
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
    }
}
