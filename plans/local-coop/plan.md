# Local Co-op (2–4 players, split-screen) — Plan


## Context
Foam Fury is single-player only. The title screen already shows a disabled "MULTIPLAYER — SOON" button (`MainMenu.BuildTitle`). Local co-op comes first because the groundwork it needs is the same groundwork online play needs later: per-player input, a list of players instead of one, and per-player camera and HUD. Agreed decisions:
- **Up to 4 players** on split-screen: 2 players side by side, 3–4 players in a 2×2 grid.
- **Downed + revive.** A downed player bleeds out over 15 s. A teammate holds a button nearby to revive them. When every player is down, the game is over. At wave clear, downed players are revived.
- **Shared team score and combo.** The game-over screen shows kills per player. The co-op best is saved separately (`coop_best_{map}`).
- **Solo must behave exactly as it does today:** root viewport, input from any device, keyboard/mouse and pad auto-detect.

## Single-player assumptions to remove (found with codegraph)
- **Global input.** `Player._PhysicsProcess`, `_Process` and `_UnhandledInput` read the global actions (`Input.IsActionPressed("fire")` etc.). Gamepad events are registered with `Device = -1` in `InputSetup.AddJoyAxis/AddJoyButton`.
- **`Main.Player` / `Main.Rig` / `Main.Hud` singletons.** They are used by:
  - `Zombie._PhysicsProcess:104` (targeting and attack)
  - `Pickup._Process:62`
  - `Main.OnZombieKilled` (drop multiplier, low-HP bias)
  - wave-clear heal (`Main.cs:425`)
  - `Grenade.cs:52` (camera trauma)
  - `Player.TakeDamage` / `TryCollect` / `EquipWeapon` (`Main.Instance.Hud`)
  - `AutoPlay` / `AnimTest`
- **Camera and HUD.** `CameraRig._Ready` makes a camera with `Current = true` in the root viewport. `Player.UpdateAimTarget` / `TryAimSnap` use `GetViewport()`. `Hud.Init(Player)` uses full-screen anchors.
- **`InputSetup.Rumble`** vibrates every connected pad.
- **`Player.Die`** calls `Main.OnPlayerDied` directly, so one death means game over.
- **`GameData.SelectedCharacter`** holds a single character.

Things that already work and need no change:
- **No friendly fire.** Projectile and mud rays use mask `1|4`, and players are on layer 2.
- **Players don't collide with each other.** The player mask is `1|4|16`.
- **The post shader works across viewports.** `shaders/post.gdshader` uses `hint_screen_texture` on a root CanvasLayer, so it applies to the composited split-screen.
- **Menu navigation stays on the global `menu_*` actions.**

## Design

### 1. Per-player input — new `scripts/PlayerInput.cs`, edit `scripts/InputSetup.cs`
- In `InputSetup`, move the gameplay bindings (move/look/fire/aim/jump/dash/reload/grenade/weapon_*) into a static binding table. Keep `Init()` registering the existing global actions from that table, so solo and menus are unchanged.
- Add `InputSetup.BindSlot(int slot, int pad, bool kbm)`:
  - It erases and re-creates the actions `p{slot}_{name}`.
  - Pad events get `Device = pad`. Key and mouse events are added only when `kbm` is true.
  - A pad of `-1` means any pad. Solo uses slot 0 with `kbm = true, pad = -1`, which reproduces today's behaviour.
- `PlayerInput` holds `Slot`, `Pad`, `Kbm` and `string A(string n) => $"p{Slot}_{n}"`. It exposes:
  - `Move`, `Look` (via `Input.GetVector` on slot actions)
  - `Pressed(n)`, `JustPressed(n)`
  - `Rumble(weak, strong, dur)`: vibrates `Pad`, or all pads when `Pad == -1`
- Using real InputMap actions keeps deadzones, just-pressed handling and the mouse wheel working with no custom polling.

### 2. Session data — `scripts/GameData.cs`
- Add `record PlayerSlot(int Pad, bool Kbm, int Character)`, plus `List<PlayerSlot> Slots` (static, so it survives `ReloadCurrentScene`) and `bool Coop => Slots.Count > 1`.
- Solo leaves `Slots` empty, and Main builds one slot from `SelectedCharacter`.
- Make `LoadBest/SaveBest` take a `coop` flag, which selects the `coop_best_{map}` key.

### 3. Player — `scripts/Player.cs`
- Change the signature to `Setup(CharacterDef def, CameraRig rig, PlayerInput input, Hud hud)`.
- Replace every `Input.*` gameplay call with `_in.*`.
- `_UnhandledInput` applies mouse look only when `_in.Kbm`.
- `UsingPad` keeps auto-detection in solo. In co-op it is fixed to `_in.Pad >= 0`.
- `GetViewport()` becomes `_rig.Camera.GetViewport()` (aim assist and snap).
- `Main.Instance.Hud` becomes `_hud`, and `InputSetup.Rumble` becomes `_in.Rumble`.
- **Downed state:**
  - Add `public bool Downed; public float BleedOut, ReviveProgress; public int Kills;`
  - In `TakeDamage`, when Hp reaches 0: in co-op call `GoDown()`, otherwise call `Die()` as today.
  - `GoDown()`: set `Downed = true`, `BleedOut = 15`, call `Model.PlayDowned()`, hide the gun, and call `Main.OnPlayerDowned(this)`.
  - While downed: no shooting or dashing, crawl at 1.2 m/s, and tick bleed-out. At 0 the player becomes `Die()` (permanently dead until wave clear).
  - **Revive:** an alive teammate within 2.2 m holds `reload` (X / R) for 2 s. Main accumulates `ReviveProgress` and calls `Revive(0.4f)` when it reaches 1. A press still triggers a reload, which is harmless.
  - `Revive(hpFrac)`: clear `Downed`/`Dead`, set Hp, call `Model.Recover()`, show the gun, and grant 1.5 s invulnerability.
- Rename the zombie-targeting test to `public bool Targetable => !Dead && !Downed;`.

### 4. Character model — `scripts/CharacterModel.cs`
- `PlayDowned()`: the kneel/slump pose, which is the first tween of `PlayDeath(Crumple)` without the face-plant. It sets `Dying = true`.
- `Recover()`: kill the tweens, reset `Position`, `Quaternion` and pose leg/arm weights, set `Dying = false`, and `Play("idle")`.

### 5. Main — `scripts/Main.cs`
- Replace `Player`/`Rig`/`Hud` with `List<Player> Players`, `List<CameraRig> Rigs` and `List<Hud> Huds`. Keep a `Player` getter that returns `Players[0]` for the debug paths (`AutoPlay`, `AnimTest`, screenshot print).
- Add helpers:
  - `Player ClosestTarget(Vector3 p)`: nearest `Targetable` player
  - `bool AnyStanding`
  - `float TeamDropMult`: max `Def.DropMult` among alive players
- `StartGame()`:
  - Build the slots: `GameData.Slots`, or one solo slot.
  - Call `BindSlot` for each slot.
  - Call `SplitScreen.Build(this, n)` to get N viewports. For N == 1 it returns the root viewport.
  - For each slot, create a `CameraRig` and a `Hud` inside that viewport, plus a `Player` in `World`. Spawn players spaced 1.5 m apart around `(0, 0.1, 6)`.
  - Capture the mouse only if some slot has `Kbm`.
- `OnZombieKilled`: use `TeamDropMult`, and apply the low-HP bias when any player is below 50 %.
- Wave clear: revive every downed or dead player at 40 % HP (respawn the dead next to a teammate), then apply the 25 % heal to all.
- `OnPlayerDowned` / `OnPlayerDied`: go to game over only when `!AnyStanding`. A solo death keeps the current path.
- **Revive tick in `_Process`:** for each downed player, find an alive teammate within 2.2 m holding that teammate's `reload`. Accumulate progress, and show it through that teammate's HUD hint ("REVIVING NAME") and the downed player's HUD ("BLEEDING OUT 12s").
- `ShowGameOver`: in co-op, show per-player `NAME — kills` lines and use the co-op best. "CHANGE SURVIVOR" sets `MenuStart` to the Lobby screen when in co-op.
- `AutoPlay`/`AnimTest` press `Players[0].Input.A("fire")` instead of `"fire"`.

### 6. Split-screen — new `scripts/SplitScreen.cs`
- `static List<Viewport> Build(Node root, int n)`:
  - For n == 1, return `[root.GetViewport()]` with no change.
  - Otherwise add a full-rect `GridContainer` (`Columns = n == 2 ? 2 : 2`; for n == 2, use one row of two vertical halves) to a CanvasLayer at layer 0, below post (1) and Hud.
  - For each player, add a `SubViewportContainer { Stretch = true, MouseFilter = Ignore }`.
  - Inside it, add a `SubViewport` with `World3D = root.GetViewport().World3D` and `Msaa3D = Msaa2X` (matches `project.godot`).
  - For n == 3, the 4th cell shows a dark panel with the wave and score.
- The rigs are added as children of their SubViewport, so their `Camera3D.Current` applies to that viewport. `SpringArm3D` and the raycasts use the shared World3D.
- **Audio:** when n > 1, add an `AudioListener3D` to `World`, call `MakeCurrent()`, and move it each frame to the centroid of standing players. `Sfx.Play3D` nodes live in the root tree, so the root listener hears them.

### 7. HUD — `scripts/Hud.cs`
- Change the signature to `Init(Player p, float uiScale)`. The Hud CanvasLayer is parented inside the player's viewport, so the crosshair centres on that view.
- Scale the root control: set `root.Scale = uiScale` and `root.Size = vpSize / uiScale` (replacing the full-rect anchors). `uiScale = clamp(vpHeight / 900, 0.6, 1)`.
- Add a downed overlay: grey vignette, "DOWN — BLEEDING OUT Ns", and a revive progress bar. Add a teammate "REVIVING" hint.
- Wave, score and banner keep reading `Main.Instance` and are shown in every viewport.

### 8. Other singleton call sites
- `scripts/Zombie.cs`: in `_PhysicsProcess`, cache `_target = main.ClosestTarget(GlobalPosition)` on each 0.3 s repath tick, and use `_target` for the chase and for `TakeDamage`. Replace the `player.Dead` checks with `_target?.Targetable`.
- `scripts/Pickup.cs`: magnet toward and collect by the nearest standing player within 3.5 m.
- `scripts/Grenade.cs:52`: apply trauma to every rig, scaled by distance.
- **Per-player kills:** add an optional `Player source = null` argument to `Zombie.TakeDamage`. Thread it through `Projectile.Fire`, `MudGlob.Fire`, `FlameJet.Fire`, `Grenade.Throw` and `MudPuddle` (burn credit goes to the igniter, or null). `Main.OnZombieKilled(z, headshot, source)` increments `source.Kills`.

### 9. Menu — `scripts/MainMenu.cs`
- The title's "MULTIPLAYER" button becomes active "LOCAL CO-OP", which goes to a new `Screen.Lobby`. Remove the SOON tag and `ComingSoon`.
- **Lobby:** 4 slot cards built with the existing `Hud.MakeLabel/MakeButton` helpers. Each card shows the character name, title, perk and the signature weapon icon (`WeaponDef.Icon`).
- Device-aware input goes in `MainMenu._Input`:
  - **Join:** a pad's A, or the keyboard's Enter/Space, joins once per device.
  - **Pick a character:** left/right (pad stick or d-pad edge, or A/D) on that device's card.
  - **Leave:** B / Esc. If no one has joined, it goes back to the title.
  - **Proceed:** START / Enter from slot 0 with ≥ 2 players goes to `Screen.Stage`.
- Stage select reuses the global input. `Finished` fills `GameData.Slots`. Single-player clears `Slots`.
- Change the event to `Finished(int[] characters, int map)` and update the subscriber in `Main.ShowMenu`.

## Order of work (each step is a commit and keeps solo playable)
1. `PlayerInput` and `InputSetup.BindSlot`. Player reads `_in`. Solo uses slot 0 in any-device mode, which is a no-behaviour-change refactor.
2. Main uses the `Players`/`Rigs`/`Huds` lists. Zombie, Pickup and Grenade use the nearest-player helpers. Still N = 1.
3. `SplitScreen` and per-viewport Hud and rig, plus the audio listener. An env var `FF_COOP=2..4` that auto-assigns pads gives fast testing.
4. Downed/revive (`Player`, `CharacterModel`, `Main`, Hud overlay) and the all-down game over.
5. Per-player kills (damage source threading) and the co-op game-over screen and best score.
6. Lobby screen and `GameData.Slots` persisted across retry.

## Verification
- `dotnet build` the project after each step.
- **Solo regression:** run with `FF_PLAY=1 FF_AUTO=1 FF_SHOT=/tmp/s.png FF_SHOT_T=5,15 FF_QUIT=1` (existing debug hooks in `Main._Ready`/`_Process`). Check that the screenshots and printed stats (zombies, wave, hp, fps) match the pre-change behaviour.
- **Split-screen layout:** run `FF_COOP=2` and `FF_COOP=4` with `FF_SHOT` for 2-way and 4-way screenshots. Check that each viewport has its own crosshair, HUD and camera, and that zombies chase different players. Also check the fps line.
- **Manual with 2 pads (or 1 pad + keyboard):** join in the lobby and pick different characters. Check that rumble only reaches the right pad and mouse look only moves the keyboard player. Take damage until downed, revive by holding X, and let bleed-out happen. Check that everyone down gives game over with per-player kills, and that RETRY keeps the same slots.
- Run the Godot MCP `run_project` / `get_debug_output` to catch runtime errors.
