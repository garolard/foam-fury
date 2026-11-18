# Lobby Flow (local co-op + online) — Plan

## Context
Today the two multiplayer front ends don't match the flow we want:
- **Local co-op** (`MainMenu.cs` lobby): press A to join a slot, ◀ ▶ to change survivor. **Any** player presses START (or P1 presses A) and the game jumps to stage select. Nobody confirms a pick. On stage select every device drives the carousel through the global `menu_*` actions.
- **Online:** the client picks a survivor *before* the server list (`Screen.Survivor → Screen.Servers`). The server loads `cfg.Map` at boot (`Main.ServerReady`), sits in `State.Waiting`, and the **first peer to connect starts the run** (`ServerPeerJoined → ServerBeginRun`). Players are never asked anything, and the map can only be changed in `server.cfg`.

### Target flow
**Local co-op**
1. The character select shows 4 slots. Each player joins a slot, picks a survivor, and **confirms with A**.
2. When there are 2+ players and **all** of them have confirmed, the menu moves to stage select.
3. **Only P1** controls stage select. P1 confirms and the match starts.

**Online**
1. The player connects to a server. The server list is now the first online screen.
2. The same 4-slot character select, shared by everyone on the server.
3. When there are **2+ players connected** and **all** of them have confirmed, everyone moves to stage select.
4. **The host** controls stage select (the earliest connected player who is still on the server). The match starts when the host confirms.

### Agreed decisions
- **Online gate:** 2+ connected players, all confirmed. No solo online runs.
- **After game over (online):** everyone goes back to the lobby (character select, then stage select), so the map can change between runs.
- **Late joiners (match already running):** they pick a survivor first, then spectate until the wave is cleared and drop in (same rule as today). While spectating they can go **back to the lobby to change survivor**.
- **Duplicate survivors are allowed.**
- **Solo play is unchanged** (survivor carousel, then stage carousel).

## Design

### 1. Local co-op lobby — `scripts/MainMenu.cs`
- **`Joined` gets `bool Confirmed`.** Each slot card has three states:

  | State | Card shows | Input |
  |---|---|---|
  | Empty | `PRESS (A) / ENTER TO JOIN` | A: join |
  | Picking | `◀ NAME ▶`, title, perk, weapon, `(A) CONFIRM` | ◀ ▶: change · A: confirm · B: leave slot |
  | Confirmed | same, locked, with a `READY` badge in the team colour | B: unconfirm |

  B with an empty team still goes back to the title screen.
- **Remove the START / P1-A shortcut** (`LobbyCmd.Start`, `TryStart`). Add `CheckAllReady()` after every change. When `_team.Count >= 2` and everyone is confirmed, show `ALL READY` for about 0.6 s, fill `GameData.Slots`, then `Go(Screen.Stage)`. If someone unconfirms during the 0.6 s, cancel.
- **Stage select in co-op is P1 only.**
  - `_Input` handles `Screen.Stage` when `_coop` is set. `Decode` maps the device. Only `_team[0].Device` can move ◀ ▶, accept, or go back.
  - The global-action branch in `_Process` skips the stage screen in co-op.
  - P1 pressing B goes back to the lobby and unconfirms only P1, so everyone else keeps their pick.
  - The footer reads `P1 CHOOSES THE STAGE`.
- **Opening the menu at `Screen.Lobby` after a co-op run** (`Open`) rebuilds `_team` from `GameData.Slots` with `Confirmed = false`.

### 2. Online lobby protocol — `scripts/Net/Net.cs`, `NetGame*.cs`
- **Bump `Net.Protocol` to 2.** Old clients get the existing `VERSION MISMATCH` rejection.
- **New `Msg` ids:**

  | Direction | Msg | Payload | Meaning |
  |---|---|---|---|
  | C→S | `LobbyPick` | `character` | Change my survivor (only while unconfirmed) |
  | C→S | `LobbyConfirm` | `bool` | Confirm / unconfirm my pick |
  | C→S | `LobbyHover` | `map` | Host only: stage carousel moved |
  | C→S | `LobbyChooseMap` | `map` | Host only: start the match on this map |
  | C→S | `BackToLobby` | — | Spectator only: leave the match to change survivor |
  | S→C | `Lobby` | `phase, hostId, n × (id, name, character, confirmed, inMatch)` | Full lobby state, sent on every change |
  | S→C | `LobbyHover` | `map` | Relayed to non-hosts so their carousel follows the host |
  | S→C | `Enter` | `map` | Load this map and spawn your player. `Welcome` follows on the same reliable channel |
  | S→C | `ToLobby` | — | The match is over (or you left it): return to character select |

- **Server lobby state** goes in a new file, `NetGame.Lobby.cs`.
  - An ordered `List<LobbyEntry>` holds `{ id, name, character, confirmed, inMatch }`. Connection order defines the host (`entries[0]`) and the slot index P1 to P4.
  - `Phase` is `Lobby | MapSelect | Playing`. Outside a match it is **derived, not stored:** `MapSelect` when there are 2+ entries and all are confirmed, else `Lobby`. Any change (join, leave, unconfirm) that breaks the condition drops back to `Lobby` automatically.
  - When the host leaves, the next entry becomes host. If that happens during `MapSelect`, the new host takes over the carousel.
  - `Lobby` is broadcast to every joined peer after each change.
- **Auth and join changes** (`NetGame.Server.cs`):
  - `Hello` keeps its `character` field as the *initial* pick (the last survivor used on this machine).
  - `Accept` sends `{ phase, map }` instead of only `map`.
  - The capacity check counts lobby entries + pending, not `M.Players`.
  - `ServerPeerJoined` **no longer creates a `Player` or starts a run.** It adds a `LobbyEntry`, sends `Lobby`, and toasts `NAME JOINED` to anyone in the match.
  - `ServerPeerLeft` removes the entry and, if the peer was in the match, also does today's Player removal. During a match, if fewer than 1 player remains in it, the run ends and the server returns to the lobby (§3).
  - Lobby messages from a peer that is not the host are ignored where host rights are needed. `LobbyPick` and `LobbyConfirm` are ignored for an entry that is `inMatch`.
- **Query info:** `InfoBytes` sends `map = -1` while not `Playing`, and the server browser shows `LOBBY` instead of a map name.

### 3. Server match lifecycle — `scripts/Main.cs`
- **Replace `State.Waiting` with `State.Lobby`.** The server boots into `Lobby` and builds `cfg.Map` (it is now only the stage carousel's starting position).
- **`ServerStartMatch(map)`** runs on `LobbyChooseMap` from the host during `MapSelect`:
  1. `_map = map; LoadMap(Full)`.
  2. For each entry, create the remote `Player` (today's code from `ServerPeerJoined`), set `inMatch`, and add it to `Players`.
  3. Broadcast `Enter{map}`, then send `Welcome` to each peer.
  4. `ServerBeginRun()` (existing `ServerResetRun(true)` + `RunStart`).
- **Late joiner** (`Phase == Playing`): the entry starts unconfirmed. On `LobbyConfirm(true)` the server creates their Player **benched** (or spawns them at once if the wave isn't active, as today), broadcasts `PlayerJoined`, then sends `Enter{map}` + `Welcome` to that peer only.
- **`BackToLobby` from a benched player:** the server removes their Player (broadcasts `PlayerLeft`), sets `inMatch = false, confirmed = false`, sends `ToLobby` to that peer, and broadcasts `Lobby`. Active (non-benched) players can't do this; they use LEAVE SERVER as today.
- **Game over:** `ServerEndRun` is unchanged (results + countdown, READY skips it). When the countdown ends, it now calls `ServerReturnToLobby()` instead of `ServerBeginRun()`. That clears world entities, frees all `Players`, clears `inMatch` and `confirmed` on every entry (survivor picks are kept), broadcasts `ToLobby` + `Lobby`, and sets `Mode = Lobby`. The `RunOver` panel countdown label reads `BACK TO LOBBY IN N`.
- **`ServerConfig.restart_delay`** keeps its meaning (seconds on the results screen). Update the `map=` comment in `ServerConfig.Default`, `server.cfg.example` and `server/SERVER.md` to say it is the default stage shown in stage select.

### 4. Client — keep the connection across scene reloads
The client returns to the menu with `Restart()` → `ReloadCurrentScene()`. That frees `Main` and its `Net` child, which today drops the connection. Fix: **`NetGame` lives at `/root/Net`** (a sibling of `Main`), on both client and server.
- In `Main._Ready`, add the `NetGame` to `GetTree().Root` (deferred) only when there isn't one already. Its RPC path `/root/Net` is the same on every peer.
- `NetGame` uses `Main.Instance` through `M`. Every client handler must guard against `M == null` or a freed `Main` during the one frame of a reload. Lobby state is mirrored inside `NetGame` (`ClientLobby`: phase, hostId, entries, hover map) and exposed through an `event Action LobbyChanged`, so no lobby message depends on `Main` existing.
- **`LeaveServer`** still calls `Net.Game.Close()` and then reloads, so the menu opens on the server list.
- **`ToLobby` on the client:** set `GameData.MenuStart = Screen.Lobby` and `Restart()`. After the reload, `Main._Ready` sees `Net.IsClient` and opens the menu at the lobby.

### 5. Online menu — `scripts/MainMenu.cs`, `MainMenu.Servers.cs`
- **ONLINE goes straight to `Screen.Servers`.** The survivor carousel step is removed from the online path. The `AS <SURVIVOR>` label and `_btnBack → Survivor` change to go back to Title.
- **`OnlineAccepted` no longer loads a map or starts the game.** It switches the menu to `Screen.Lobby` in online mode (`_online = true`, `_coop = false`). The map is loaded only on `Enter`.
- **Online lobby screen** reuses the 4 cards:
  - Slots come from `ClientLobby.entries` in order, labelled `P1 · HOST`, `P2`, …, with the player name where the device label is today. Our own card shows `YOU`. Empty slots read `WAITING FOR PLAYERS…`.
  - Badges: `READY` for confirmed, `IN MATCH` for entries already playing.
  - Local input from any device (or keyboard) drives **our own entry only**:
    - ◀ ▶ → `LobbyPick` (applied locally right away and reconciled by the next `Lobby`).
    - A → `LobbyConfirm(true)`.
    - B → unconfirm if confirmed, otherwise leave the server (back to the server list).
  - Status line: `NEED AT LEAST 2 PLAYERS`, or `WAITING FOR N TO CONFIRM`, or for a late joiner `MATCH IN PROGRESS — CONFIRM TO SPECTATE AND JOIN`.
- **Stage select:** when `ClientLobby.phase` becomes `MapSelect`, the menu goes to `Screen.Stage`.
  - **The host** drives the carousel as in solo. Each move sends `LobbyHover`, and A sends `LobbyChooseMap`. B unconfirms the host, which drops everyone back to `Lobby`.
  - **Non-hosts** have no carousel input. Their carousel follows `LobbyHover`. The footer reads `<HOST> IS CHOOSING THE STAGE    (B) BACK`, and B unconfirms them.
  - When the phase falls back to `Lobby`, the menu returns to the lobby screen.
- **`Enter{map}`** frees the menu, then does today's `OnlineAccepted` body (`LoadMap`, `StartGame`, set `NetId`/`NetName`). The character comes from our lobby entry.
- **Spectating → change survivor:** the online pause panel (`ToggleOnlinePause`) gets a `CHANGE SURVIVOR` button, shown only while `LocalPlayer.Benched`. It sends `BackToLobby`, and the server answers with `ToLobby`. The `SPECTATING` banner sub-text mentions it: `… · START to change survivor`.

### 6. Debug / test hooks
- `FF_NET=join:HOST:PORT` now lands in the online lobby.
- **`FF_READY=1`** auto-confirms the current pick on the first `Lobby` message.
- **`FF_MAP=n`** with `FF_READY` makes a host auto-send `LobbyChooseMap(n)` when `MapSelect` begins.

  Two headless-ish clients (`FF_NET` + `FF_READY=1`, one with `FF_MAP`) then reach a running match with no input.

## Files
| File | Change |
|---|---|
| `scripts/MainMenu.cs` | Confirm state on cards, auto-advance, P1-only stage in co-op, online lobby/stage modes, online path skips survivor carousel |
| `scripts/MainMenu.Servers.cs` | Back → Title, drop `AS <SURVIVOR>`, `LOBBY` in server rows, join lands in lobby |
| `scripts/Net/Net.cs` | `Protocol = 2`, new `Msg` ids |
| `scripts/Net/NetGame.cs` | Lives at `/root/Net`, survives reloads, null-safe `M` |
| `scripts/Net/NetGame.Lobby.cs` (new) | `LobbyEntry`, phase/host logic, server lobby handlers, client `ClientLobby` mirror + `LobbyChanged` |
| `scripts/Net/NetGame.Server.cs` | Auth/capacity on entries, join/leave no longer spawn/start, `Enter` + per-peer `Welcome`, `BackToLobby`, `map=-1` in info |
| `scripts/Net/NetGame.Client.cs` | `Accept{phase,map}`, handle `Lobby`, `LobbyHover`, `Enter`, `ToLobby`, `SendLobby*` |
| `scripts/Main.cs` | `State.Lobby`, `ServerStartMatch`, `ServerReturnToLobby`, client `Enter`/`ToLobby`, menu start at lobby when connected, `CHANGE SURVIVOR` in online pause |
| `scripts/Net/ServerConfig.cs`, `server/server.cfg.example`, `server/SERVER.md` | `map` = default stage in stage select. Update the online flow description |

## Verification
1. **Local co-op, 2 pads + keyboard:**
   - Joining and confirming 1 player does nothing.
   - Confirming 2 advances to stage select.
   - P2's input on stage select is ignored. P1 B returns to the lobby with P2 still `READY`.
   - P1 A starts the run.
   - After game over → MAIN MENU / retry still behave as today.
2. **Solo:** the survivor → stage flow is unchanged.
3. **Online, local server** (`--server`) + 2 clients with `FF_NET`:
   - The server log shows no run until both confirm.
   - Only the first client can move the stage carousel, and the second sees it follow.
   - Choosing a map starts the match on that map for both.
4. **Host leaves during stage select** → the remaining client is alone, so the phase drops to `Lobby` (`NEED AT LEAST 2`). A third client joining makes the old P2 the host.
5. **Late joiner:** a third client connects mid-wave → lobby with 2 `IN MATCH` cards → confirm → spectates → drops in at wave clear. Pausing while spectating → CHANGE SURVIVOR → back in the lobby → re-confirm a different survivor → spectating again.
6. **Game over online** → results → countdown (or all READY) → every client is back in the lobby, still connected, previous picks kept and unconfirmed. A different map can then be chosen.
7. **Server browser** shows `LOBBY` for an idle server and the map name during a match. An old (protocol 1) client is rejected with `VERSION MISMATCH`.
8. `dotnet build` is clean, and the dedicated server export (CI) still builds.

## Open risks
- **Moving `NetGame` to `/root/Net`** is the riskiest change. Every `Net.Game` caller and the `Main.Instance` lifetime during a reload need a careful pass. The alternative is to tear down the game in place on the client (free players, HUDs and split screen without a reload). That avoids the reparent but duplicates `Restart()` cleanup. The plan uses the reparent.
- **Stage-select dioramas** are built per client. Online, `LobbyHover` only moves the carousel index, so nothing heavy crosses the wire.
