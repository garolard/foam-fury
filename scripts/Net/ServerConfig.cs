using Godot;

/// Dedicated server settings (server.cfg, INI style).
public class ServerConfig
{
    public string Name = "Foam Fury Server", Password = "", Motd = "";
    public int Port = Net.DefaultPort, MaxPlayers = 4, Map;
    public float RestartDelay = 10f;
    public string Path;

    const string Default = @"; Foam Fury dedicated server
[server]
; shown in the server list
name=""Foam Fury Server""
motd=""Hold the arena!""
; UDP port (open it on your router / firewall)
port=7777
; 1-4
max_players=4
; empty = no password
password=""""
; 0 Dusk Coliseum, 1 Neon Arcade, 2 Night Crypt, 3 Pine Camp
map=0
; seconds between game over and the next run
restart_delay=10
";

    /// `--config=path`, else server.cfg next to the executable, else user://server.cfg (created with defaults).
    public static ServerConfig Load()
    {
        string path = Net.Arg("--config");
        if (string.IsNullOrEmpty(path))
        {
            var beside = OS.GetExecutablePath().GetBaseDir().PathJoin("server.cfg");
            path = FileAccess.FileExists(beside) ? beside : "user://server.cfg";
        }
        if (!FileAccess.FileExists(path))
        {
            using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            f?.StoreString(Default);
            GD.Print($"[server] wrote default config to {ProjectSettings.GlobalizePath(path)}");
        }
        var cfg = new ConfigFile();
        var c = new ServerConfig { Path = path };
        if (cfg.Load(path) != Error.Ok) GD.PrintErr($"[server] could not read {path}, using defaults");
        else
        {
            c.Name = (string)cfg.GetValue("server", "name", c.Name);
            c.Motd = (string)cfg.GetValue("server", "motd", c.Motd);
            c.Port = (int)cfg.GetValue("server", "port", c.Port);
            c.MaxPlayers = Mathf.Clamp((int)cfg.GetValue("server", "max_players", c.MaxPlayers), 1, 4);
            c.Password = (string)cfg.GetValue("server", "password", c.Password);
            c.Map = Mathf.Clamp((int)cfg.GetValue("server", "map", c.Map), 0, MapBase.Names.Length - 1);
            c.RestartDelay = (float)cfg.GetValue("server", "restart_delay", c.RestartDelay);
        }
        if (Net.Arg("--port") is { } p && int.TryParse(p, out int port)) c.Port = port;
        if (Net.Arg("--map") is { } m && int.TryParse(m, out int map)) c.Map = Mathf.Clamp(map, 0, MapBase.Names.Length - 1);
        return c;
    }
}
