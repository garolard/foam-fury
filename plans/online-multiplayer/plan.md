# Online Multiplayer (dedicated server + saved server list) — Plan

## Context
Foam Fury has solo play and 2–4 player local co-op: per-player `PlayerInput`, `Main.Players`, downed/revive, and kill credit threaded through every damage source. The next step is online play. Agreed decisions:
- **Dedicated server, same Godot project, run headless.** The server owns the simulation. Clients own only their own player and their presentation. One server process is one room (map, max players and password come from a config file). There is no central lobby service.
- **Saved server list in the client.** Players add a `host:port` or a URL. The client saves it, queries each server (name, map, players, lock, ping), then joins.
- **Shooter-reported hits** for darts and mud-glob direct hits. The server applies them after loose checks. Area damage (flame, grenades, burning puddles, burn ticks) is simulated on the server.
- **Late joiners spectate until wave clear,** then spawn next to a teammate. Players who join before wave 1 or during a break spawn immediately.
- **After game over,** the server shows results and restarts in 10 s, or sooner once everyone is READY.
- **One player per machine online.** Mixing couch players into an online client comes later.
- **Solo and local co-op must behave exactly as today.** Every online path is guarded by `Net.Mode != Offline`.
- **Out of scope (later plan):** a multi-room front door, in-game admin controls, and Docker.

## Single-process assumptions to change (from code review)
- **`Main` runs everything locally:**
  - waves and spawns: `SpawnZombie`, plus `GD.Randf` in `Main.cs:268-286` and `:537`
  - loot: `OnZombieKilled`
  - revive: `ReviveProcess`, and `Reviver` reads `o.In.Pressed("reload")`
  - game over: `EndRun` uses `Engine.TimeScale = 0.3`
  - pause: `GetTree().Paused`
  - retry: `ReloadCurrentScene`
- **`Player` assumes a local `CameraRig`, `PlayerInput` and `Hud`.**
  - `Setup` takes a rig and a hud; `_PhysicsProcess` and `_Process` read `In.*` and `_rig`.
  - `TakeDamage`, `GoDown`, `Die`, `Revive` and `TryCollect` mutate HP locally.
  - Regen runs in `_PhysicsProcess`.
- **`Zombie`:**
  - AI, nav and status effects run in `_PhysicsProcess`.
  - `TakeDamage` mixes gameplay with FX (`Fx.Burst`, `Fx.Number`, `Sfx.Play3D`, `HitMarker`).
  - `_Ready` rolls skin and anim randomness with `GD.Randf`.
- **Weapons apply damage directly:**
  - `Projectile._PhysicsProcess` → `z.TakeDamage`
  - `MudGlob` → `TakeDamage`, `Slow` and `MudPuddle.Spawn`
  - `FlameJet` → `Ignite`, `TakeDamage` and `MudPuddle.IgniteAt`
  - `Grenade.Explode` → area damage and `Main.Shake`
- **`Pickup._Process`** picks the nearest player and calls `TryCollect`. **`MudPuddle`** slows and ignites zombies.
- **Nodes are built with `new` in code and have no stable network identity.**
- **Things that already work:**
  - Map geometry is deterministic: `MapBase.Rng` uses `Seed = 1337`; the only `GD.RandRange` there is the flame-mesh rotation, which is visual. Server and client collision match.
  - `Hud` reads `Main.Instance` for wave, score, combo, `ZombiesRemaining`, `InBreak`, `BreakTimer`, `Coop`, `Reviver` and `DownedNear`. Mirroring those on the client keeps the HUD unchanged.

## Design

### 1. Network core — new `scripts/Net/`
- **`Net.cs`** (static):
  - `enum NetMode { Offline, Server, Client }`, plus `Mode`, `IsServer`, `IsClient` and `Online`.
  - `const int Protocol = 1`.
  - `Headless => DisplayServer.GetName() == "headless"`.
  - `ParseArgs()` reads `OS.GetCmdlineUserArgs()` (`--server`, `--port=`, `--config=`) and `OS.HasFeature("dedicated_server")`, so a dedicated export starts as a server with no args.
  - Channels: `0` reliable events, `1` player state (unreliable ordered), `2` world snapshots (unreliable ordered).
- **`NetIds.cs`:** `Dictionary<int, Node>` registry plus `Next()` on the server.
  - Players use their peer id as their net id.
  - Zombies, pickups, grenades and puddles get server-assigned ids.
  - Each networked class gets a `public int NetId`.
- **`ServerConfig.cs`:** loads a `ConfigFile` with these keys:

  | Key | Default |
  |---|---|
  | `name` | |
  | `port` | 7777 |
  | `max_players` | 4 (≤ 4) |
  | `password` | "" |
  | `map` | 0 |
  | `motd` | |
  | `restart_delay` | 10 |

  The path comes from `--config=`, or `server.cfg` next to the executable, or `user://server.cfg`. Write a commented default file when none exists.
- **`Wire.cs`:** pack and unpack helpers over `System.IO.BinaryWriter/Reader` on `byte[]`. They cover `Vector3`, yaw as a quantized `short`, and flags as `byte`. Snapshots travel as one `byte[]` per RPC.
- **`Interp.cs`:** a small buffer per entity, holding the last 4 `(recvTime, pos, yaw)` samples. `Sample(now - 0.1s)` lerps between samples and extrapolates for at most 0.1 s. Remote players and replica zombies both use it.
- **`NetGame.cs`:** a `Node` named `"Net"` added as a child of `Main`, so it has the same path `/root/Main/Net` on every peer.
  - Every RPC lives here, as `[Rpc]` methods called with `Rpc(MethodName.X, …)` / `RpcId(…)`.
  - Entity messages carry net ids, so the code-built nodes never need replicated node paths.
  - Split into two partial files, `NetGame.Server.cs` and `NetGame.Client.cs`.
- **Connection and auth** use `SceneMultiplayer.AuthCallback`, `SendAuth` and `CompleteAuth` on one ENet port.
  - The client sends `Hello{protocol, name, character, password, query}`.
  - **Query:** the server replies `Info{name, motd, map, players, max, locked, wave, protocol}` and the client disconnects. This gives the server-list ping with no second port. Ping is the time from connect to the `Info` reply.
  - **Join:** the server checks protocol, password and capacity, replies `Accept{map, peerId}` or `Reject{reason}`, then calls `CompleteAuth`.
  - ENet max clients is `max_players + 4`, leaving headroom for queries.

### 2. Server mode — `scripts/Main.cs` + `NetGame.Server.cs`
- In `_Ready`, when `Net.IsServer`, skip the menu, music, post-process and `InputSetup.Init`.
  - Load the map from config with `BuildMode.Full`, which still bakes the navmesh.
  - Create `ENetMultiplayerPeer.CreateServer(port, max)`.
  - `Mode = State.Waiting` (a new state).
  - Log `GD.Print` lines for start, join, leave, wave and game over.
- **Peer authenticated:**
  - Create a server-side `Player` in **Remote** mode (section 3) with the chosen character and add it to `Players`.
  - The new player spawns immediately if `Mode == Waiting`, before wave 1, or in a break. Otherwise it goes to `PendingSpawn` (spectating).
  - Send the joiner a full **Welcome** state: every player, zombie, pickup and puddle with its net id, plus the game state.
  - When the first player arrives in `Waiting`, the run starts: break timer 3.5 s, then the wave flow as today.
- **Server-only gameplay:**
  - waves and spawns
  - zombie AI, nav, status effects and damage
  - loot rolls
  - pickups (who takes what)
  - HP, regen, downed, bleed-out, revive and death
  - score, combo and kills
  - puddles, grenades and flame damage
- **Wave clear:** spawn the `PendingSpawn` players next to a teammate, using the existing respawn-next-to-buddy code in `Main._Process` wave clear.
- **`EndRun` on the server:**
  - No `TimeScale` change.
  - `Mode = GameOver`.
  - Send `RunOver{wave, kills, score, per-player kills}`.
  - Start a `restart_delay` timer; all peers sending `Ready` restarts early.
- **Restart without a scene reload:** a new `ResetRun()`
  - frees zombies, pickups, grenades, puddles, projectiles and darts (`Fx.ClearDarts`, `MudPuddle.All`)
  - resets the stats
  - respawns players at the start line
  - broadcasts `RunStart`
- **Last peer leaves:** `ResetRun()`, then `Mode = Waiting`.
- **Headless guards:**
  - `Sfx.Play/Play3D` and every `Fx.*` method return early when `Net.IsServer`.
  - `Main.Shake`, `Banner` and `Toast` become RPC broadcasts on the server and stay local otherwise.
  - Keep `CharacterModel` on server entities in v1; optimise later.

### 3. Player roles — `scripts/Player.cs`
- Add `public enum Role { Local, Remote }`.
  - Offline: every player is `Local`, which is today's code.
  - Online client: own player `Local`, teammates `Remote`.
  - Server: every player `Remote`.
- `SetupRemote(CharacterDef def, int netId)` creates no rig, input or hud, and guards every `_rig`/`In`/`_hud` use. `Rig`, `In` and `Hud` can be null.
- **Remote `_PhysicsProcess`:**
  - Apply the `Interp` sample as the position (teleport `GlobalPosition`) and the model yaw.
  - Drive `Model.Play("run"|"idle"|"jump")` from the synced velocity and on-floor flag.
  - No input, movement or firing.
- **Remote `_Process`:** reuse the pose code with the synced `AimPoint`, `aiming`, `reloading`, `_lastShot` (from fire events) and `WeaponIndex`.
  - Pull the aim, arms and gun block out into `PoseArmsAndGun(aimPoint, armsUp, reload)`, so Local and Remote share it.
- **Local player online:**
  - Every 1/30 s, send `PlayerState` on channel 1 with:
    - pos, vel, model yaw, `AimPoint`
    - flags: aiming, reloading, onFloor, dashing (invulnerable), reviveHeld
    - weapon index
  - The server relays it to the other peers inside its 20 Hz players snapshot.
- **HP is server-owned.**
  - On the server, `TakeDamage` from a zombie also respects the remote dash-invulnerable flag.
  - The server broadcasts `PlayerHurt{id, dmg, dir, knock}`. The owner applies the knock, flinch, trauma, rumble, sound and HUD effects; the others apply the flash.
  - Online clients never call `GoDown`/`Die`/`Revive` themselves; those arrive as `PlayerDowned`/`PlayerDied`/`PlayerRevived` events.
  - The players snapshot carries `hp`, `downed`, `dead`, `bleedOut` and `reviveProgress` for HUD mirroring.
  - Regen runs on the server only.
- **Ammo, magazines, grenades and weapon switching stay client-owned.**
- **`Reviver`** reads a new `p.ReviveHeld` instead of `In.Pressed("reload")`: `Local => In.Pressed("reload")`, `Remote =>` the synced flag.
- **`Shoot()` online:** run the local projectiles as today, then send `Fired{weapon, muzzle, dirs[]}` (unreliable, channel 0). Other clients spawn **visual-only** projectiles. For a flame weapon, the server also spawns damaging `FlameJet`s.
- **`ThrowGrenade()` online:** send `ThrowGrenade{pos, vel}` (reliable). Do not spawn locally.
- **Downed online:** the local client keeps controlling the crawl (`DownedProcess`), but the bleed-out tick moves to the server.

### 4. Zombies — `scripts/Zombie.cs`
- `bool Replica` is true on online clients. Replicas:
  - skip AI, nav (no `NavigationAgent3D`), status timers and `MoveAndSlide`
  - teleport to the `Interp` sample
  - derive run or idle from the interpolated speed
  - keep the procedural pose code, driven by synced flags
- **Spawn data:** `Setup(type, wave, rise)` plus a server-chosen `skin` and `seed`, so cosmetics match on every client. The `GD.Randf` calls in `_Ready` use an RNG seeded from `seed`.
- **Snapshot flags:** `burning`, `slowed`, `windup`, `strike` (a counter that triggers `_hitArmKick`) and `rising`. `UpdateStatus` keeps only its visual half (burn particles and light) on replicas.
- **Split `TakeDamage`:**
  - `ApplyDamage(...)`: gameplay — HP, knock, stun, death. Server and offline only.
  - `HitFeedback(pos, dir, dmg, headshot, minor, explosive)`: flash, flinch, `Fx`, `Sfx`. Runs everywhere.
  - Offline: `TakeDamage` = `ApplyDamage` + `HitFeedback`, unchanged.
  - Online client: `TakeDamage` from a **local** projectile or mud glob means `HitFeedback` plus a local hit marker plus `Net.ReportHit(NetId, dmg, pos, dir, headshot, knock, slow)`. Replicas never lose HP locally.
- **Server events:**
  - `ZombieHit{id, pos, dir, dmg, flags}` goes to every peer except the reporting shooter, who already showed it.
  - `ZombieDied{id, dir, headshot, explosive, kind, killerId}`: replicas run the existing death branch of `Die` (`PlayDeath`, burst, sink tween). The killer's HUD shows the kill marker.
  - `Die` picks the death kind on the server and sends it.
- **Collision stays on replicas** (layer 4), so local projectiles and the local player's `MoveAndSlide` still hit them.

### 5. Weapons, pickups, puddles
- **`Projectile` / `MudGlob`:**
  - Add a `visualOnly` flag for other players' shots: they still hit the world for FX but skip `TakeDamage`, `Slow` and puddle spawning.
  - The shooter's local glob reports a direct hit through `ReportHit` with slow, and its landing point through `MudLanded{pos}`.
  - The server re-runs `MudPuddle.FindFloor` and calls `MudPuddle.Spawn`.
- **`FlameJet`:** online clients spawn it visual-only (no `Ignite`, `TakeDamage` or `IgniteAt`). The server spawns damaging jets from `Fired` events.
- **`Grenade`:**
  - Server: authoritative `RigidBody3D` that broadcasts `GrenadeSpawned{id, pos, vel, spin, owner}`.
  - Clients: simulate a visual copy locally.
  - `GrenadeExploded{id, pos}` snaps the copy, plays `Fx.Explosion` and the sound, and runs the local `Shake`. Damage and `IgniteAt` happen on the server only.
- **`Pickup`:**
  - Server spawns it → `PickupSpawned{id, kind, pos, vel}`. Clients run the bounce and magnet visuals.
  - Collection is claimed by the owner: the local player within 1 m whose `TryCollect` check passes (health not full, grenades < 9) sends `TakePickup{id}`.
  - The server applies first-come-first-served and broadcasts `PickupTaken{id, peer}`.
  - The owner applies ammo and grenades locally. The server applies health (it owns HP) and coins (it owns score).
  - The server despawns expired pickups with `PickupGone{id}`.
- **`MudPuddle`:**
  - Server sim, broadcasting `PuddleSpawned{id, pos, radius}`, `PuddleGrew{id}`, `PuddleIgnited{id}` and `PuddleRemoved{id}`.
  - Online clients skip every zombie effect in `_PhysicsProcess`.
  - The client `IgniteAt` is a no-op when `Net.IsClient`.

### 6. Hit validation (server, loose)
Drop a hit report when any of these fails:
- The shooter exists and `Targetable`.
- The zombie exists, is alive and is not rising.
- The distance from shooter to zombie is < 120 m.
- `dmg ≤ weapon.Damage × Def.DamageMult × 2` (headshot), using the weapon index from the last `Fired`.
- At most 60 reports/s per peer.

Then call `ApplyDamage` with that `Player` as the source. Kill credit, combo and loot work unchanged.

### 7. Client session — `NetGame.Client.cs` + `Main.cs`
- **Join:**
  - `LoadMap(map)` from `Accept`.
  - Create the local `Player` (Local role) with a rig and hud via the same code as `StartGame` (one slot, kbm and any pad).
  - Apply `Welcome`: spawn replicas for every entity listed.
  - When pending spawn, the camera rig targets the nearest teammate's puppet (spectate, look input only) and the HUD shows "JOINING AT WAVE CLEAR".
- **Game state mirror:** `GameState{score, combo, wave, zombiesRemaining, breakTimer, mode}` rides in the 20 Hz snapshot.
  - Make `ZombiesRemaining`, `InBreak` and `BreakTimer` read mirrored fields when `Net.IsClient`.
  - `Coop` is true when `Players.Count > 1`, so teammate HUD prompts work online too.
- **Pause online** opens the panel without `GetTree().Paused`: RESUME and LEAVE SERVER. Local gameplay input is ignored while it's open.
- **`RunOver`:**
  - Local 1 s slow-mo, which is cosmetic and safe because the run is over.
  - Results panel showing "NEXT RUN IN Ns", READY (sends `Ready`) and LEAVE SERVER.
  - `RunStart`: clear the replicas and reset the local player.
- **Disconnect** (`ServerDisconnected` or `ConnectionFailed`): clean up the peer and `Restart()` into the menu at the Servers screen with the reason. `GameData.MenuStart` and a new `GameData.NetMessage` carry it.
- **Peer leaves:** `PlayerLeft{id}` frees that puppet.
- **Online best scores are not saved** in v1.

### 8. Menu — `scripts/MainMenu.cs`, `scripts/GameData.cs`
- **Title gets a third button, ONLINE.** The flow is ONLINE → Survivor select (reuse the screen, with an `_online` flag like `_coop`) → new `Screen.Servers`.
- **Servers screen:**
  - Player name field, saved in `user://save.cfg` under `profile/name`.
  - A list of saved servers, each row showing name, address, MOTD, map, `players/max`, a lock icon and ping or "offline".
  - Buttons: ADD (name and address fields), EDIT, REMOVE, REFRESH, JOIN.
  - JOIN asks for a password when the server is locked.
- **Storage:** `GameData.LoadServers/SaveServers` over `user://servers.cfg`, with a `record SavedServer(string Name, string Address)`.
- **Address parsing:**
  - Strip any `scheme://` and the path.
  - Split `host[:port]`, with 7777 as the default port.
  - Resolve with `IP.ResolveHostname`, so a bad address shows an error instead of hanging.
- **Querying:** all rows query in parallel when the screen opens, with a 3 s timeout each, via short-lived `ENetMultiplayerPeer`s in query mode.
  - Each query peer needs its own `SceneMultiplayer`, assigned with `GetTree().SetMultiplayer(mp, path)` on a per-row child node, so queries don't clobber the main peer.
- **Controller navigation:** reuse the existing `Hud.MakeButton/MakeLabel` helpers and the global `menu_*` navigation. Text entry uses `LineEdit`.

### 9. Build and deploy
- **New export preset "Linux Server":**
  - `platform=Linux`, `dedicated_server=true`.
  - Resource export mode **Keep** for all resources. Stripping visuals would turn meshes into placeholders, and `Assets.AddCollision` builds map collision from mesh geometry.
  - `custom_features="dedicated_server"`.
  - Export path `binaries/server/foam-fury-server.x86_64`.
- **Dev run without exporting:** `godot --headless --path . -- --server --port=7777`.
- **Add `server.cfg.example` and a short `SERVER.md`** covering: the run command, opening UDP port 7777, the config keys, and the logs.
- **Debug env (like `FF_COOP`):**
  - `FF_NET=server`
  - `FF_NET=join:HOST:PORT` (with `FF_NAME` and `FF_CHAR`): auto-connect, skipping the menus
  - Combines with `FF_AUTO`, `FF_SHOT` and `FF_QUIT`. `AutoPlay` already drives `Player.In`.

## Message summary (all on `NetGame`)

| Direction | Reliable (ch 0) | Unreliable (ch 1/2) |
|---|---|---|
| Client → server | `ReportHit`, `MudLanded`, `ThrowGrenade`, `TakePickup`, `Ready` | `PlayerState` 30 Hz, `Fired` |
| Server → clients | `Welcome`, `RunStart`, `RunOver`, `PlayerJoined/Left/Hurt/Downed/Died/Revived/Spawned`, `ZombieSpawned/Died`, `PickupSpawned/Taken/Gone`, `GrenadeSpawned/Exploded`, `Puddle*`, `Banner`, `Toast`, `Score{pts, at}` | `Snapshot` 20 Hz (game state + players + zombies), `ZombieHit`, relayed `Fired` |

Estimated bandwidth: 36 zombies × ~16 B × 20 Hz plus players and state ≈ 15 KB/s per client.

## Order of work (each step is a commit and keeps solo and local co-op unchanged)
1. **Net core:** `Net`, `NetIds`, `ServerConfig`, `Wire` and `Interp`. Headless server boot with no presentation: the `Sfx`/`Fx` guards, `State.Waiting`, and server-skips-menu. Auth handshake (join and query). `FF_NET=join` connects and logs.
2. **Players over the wire:** `Player` Remote role, the `PoseArmsAndGun` refactor, `PlayerState` relay, `Welcome`/`PlayerJoined`/`PlayerLeft`. Two clients see each other walk, aim and fire visual-only shots (`Fired`).
3. **Zombies on the server:**
   - Spawns, replicas, snapshot, `Interp`.
   - The `ApplyDamage`/`HitFeedback` split.
   - `ZombieDied`.
   - Server-side HP: `PlayerHurt`, downed, died, revived, `ReviveHeld`, bleed-out and regen.
   - Game state mirror on the HUD.
4. **Combat authority:** `ReportHit` with validation, mud (`MudLanded`, puddle replication), flame (server jets, visual client jets), grenades, pickups, kill credit and score events.
5. **Run lifecycle:** late join with spectating and spawn at wave clear, `RunOver`/READY/auto-restart, `ResetRun`, disconnect handling, online pause panel.
6. **Client UI:** ONLINE title button, Survivor → Servers screen, saved servers, query/ping, password prompt, player name.
7. **Deploy:** Linux Server export preset, `server.cfg.example`, `SERVER.md`.

## Verification
- `dotnet build` after every step.
- **Offline regression** (every step):
  - Solo: `FF_PLAY=1 FF_AUTO=1 FF_SHOT=/tmp/s.png FF_SHOT_T=5,15 FF_QUIT=1`.
  - Local co-op: `FF_COOP=2`.
  - Compare the screenshots and printed stats with the pre-change behaviour.
- **Local online loop on one Mac:**
  1. Run `godot --headless --path . -- --server`.
  2. Start two clients with `FF_NET=join:127.0.0.1:7777 FF_AUTO=1 FF_SHOT=… FF_SHOT_T=10,30`.
  3. Check that the server log shows both joins, waves advancing, kills credited to each peer, and no errors.
  4. Check that client screenshots show the other player and synced zombies.
  5. Use Godot MCP `run_project` / `get_debug_output` for runtime errors.
- **Latency test:** run the server and clients with simulated lag (`sudo dnctl`/`pfctl` dummynet, about 100 ms and 2 % loss). Check that aiming and hits feel like offline, zombie motion is smooth, and there's no rubber-banding of your own player.
- **Manual checks:**
  - Server-list query and ping, both with and without a password.
  - Wrong password or protocol is rejected with a message.
  - Late join spectates, then spawns at wave clear.
  - Downed, revive and bleed-out across machines.
  - Game over leads to the READY/auto-restart.
  - Killing the server returns clients to the Servers screen with a reason.
  - A client leaving mid-run removes its puppet.
- **Linux export:** the dedicated export boots in Docker or a VM and accepts a client from the Mac over LAN.
