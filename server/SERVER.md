# Running a Foam Fury server

A server is the game itself running headless. It owns the zombies, waves, loot, HP and score; players connect with the
normal game (**ONLINE → pick survivor → add your server's address**). One server process = one room (one map, up to 4 players).

## Quick start (no export, from the project folder)

```sh
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . -- --server --port=7777
```

## Linux box / VPS

1. In Godot: **Project → Export → "Linux Server" → Export Project** (writes `binaries/server/`).
   Exports with `dedicated_server` start as a server automatically. Resources are kept in full: the map collision
   is built from the meshes, so do not switch the preset to "strip visuals".
2. Copy `binaries/server/` to the machine, put a `server.cfg` next to the binary (see `server.cfg.example`) and run:
   ```sh
   ./foam-fury-server.x86_64 --headless
   ```
   The .NET runtime needs ICU: `apt install libicu72` (Debian 12) / `libicu-dev` / `icu-libs` (Alpine).

### Docker

```sh
docker build -f server/Dockerfile -t foam-fury-server .
docker run -d --name foam-fury -p 7777:7777/udp \
  -v "$PWD/server/server.cfg:/srv/server.cfg" foam-fury-server
docker logs -f foam-fury
```

## Options

| Where | What |
|---|---|
| `server.cfg` `[server]` | `name`, `motd`, `port`, `max_players` (1-4), `password`, `map` (0-3), `restart_delay` |
| `--config=path` | config file (default: `server.cfg` next to the binary, else `user://server.cfg`, created on first run) |
| `--port=N`, `--map=N` | override the config |

Open **UDP** on the port (default 7777) in your firewall / router. Players add `your.host:7777` (or just `your.host`
for the default port, or a URL such as `ff://your.host:7777`).

## What players see

- Joining mid-wave: spectate until the wave is cleared, then drop in next to a teammate.
- Game over: results, then a new run after `restart_delay` seconds (or once everyone presses READY).
- Wrong password / full server / different game version: refused with the reason in the server browser.

## Logs

One line per join, leave, wave, and game over, e.g.
```
[server] ALICE joined as REX (peer 335031804) · 1/4
[server] wave 3 · 3 players · score 8900
[server] game over · wave 7 · score 61200 · kills 214 · ALICE=80, BOB=71, CARL=63
```

## Testing locally

```sh
# server
Godot --headless --path . -- --server --port=7790
# clients that join straight away and auto-play (debug env)
FF_NET=join:127.0.0.1:7790 FF_NAME=alice FF_CHAR=0 FF_AUTO=1 Godot --path .
FF_NET=join:127.0.0.1:7790 FF_NAME=bob   FF_CHAR=1 FF_AUTO=1 Godot --path .
```
`FF_PASS` sets the password; `FF_SHOT`/`FF_SHOT_T`/`FF_QUIT` work as in single player.
