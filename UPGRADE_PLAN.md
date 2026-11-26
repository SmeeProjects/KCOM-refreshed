# KCOM-refreshed: upgrade plan

This plan covers what it takes to make KCOM current, stable and safe again.

- **Baseline:** upstream KCOM v3.1.0.0 (`KiwifruitDev/KCOM`, archived 2025-11-27). This fork currently matches it: all projects target `net6.0-windows`.
- **Game build checked:** Half-Life: Alyx, Steam build 26446291 (November 2026).
- **Analysis date:** 2026-11-26.

Each work item is labelled for how it can be done:

- `agent-ok`: can be built and tested without the game, for example by a Deckhand run.
- `needs-game`: needs a PC with Half-Life: Alyx (and sometimes the Workshop Tools) to finish or verify.

---

## 1. Summary

- **The game itself has not broken KCOM.** Everything KCOM relies on is still in the current game build:
  - The VConsole protocol version is still **211**.
  - Every VScript function, game event, entity class, model and console command KCOM uses is still there.
- **The platform is out of support.**
  - .NET 6 lost support on 2024-11-12. .NET 8 and .NET 9 both lost support on 2026-11-10.
  - **Target .NET 10 LTS, supported until 2028-11-14.** Skip .NET 11, which is STS (short-term support).
- **The Steam Workshop addon (item 2739356543) has been deleted.** The game half of the mod has to be installed from `WorkshopAddon/` in this repo.
- **The biggest risks are in KCOM's own code:**
  - Players can inject console commands into each other's games (S1).
  - Hosting on a Windows locale that uses a comma for decimals, such as `fr-FR` or `de-DE`, breaks sync (B1).
  - Shared state is accessed from several threads with no locking (B2).

---

## 2. What was verified against the game

This is a record of how compatibility was checked, so it can be checked again after future game updates.

| What KCOM relies on | Where it was found in the game | Result |
|---|---|---|
| VConsole protocol `0xD3` (211), the value hard-coded in `Settings.VconsoleProtocol` | `game/bin/win64/vconcomm.dll` (constant next to the `PRNT`/`CMND`/`VFCS`/`AINF` packet tags) | Still 211 |
| VConsole TCP port 29000 when the game is launched with `-vconsole` | `Settings.VconsolePort` | Unchanged |
| VScript functions used by `kcom_interval.lua`: `GetHMDAvatar`, `GetVRHand`, `GetHMDAnchor`, `GetAnglesAsVector`, `RedirectOutput`, `SetSpawnCallback`, `SpawnEntityFromTableSynchronous`, `FireOutput`, `DoEntFireByInstanceHandle`, `ListenToGameEvent`, `Convars:RegisterCommand`, `SendToConsole`, `FindAllByClassname`, `FindAllByName`, `SetEntityName`, `StartSoundEvent`, `GetMapName`, `PrecacheModel`, `GetChildren` | `game/hlvr/bin/win64/server.dll` | All present |
| Game events `item_pickup` and `player_drop_resin_in_backpack`; entity classes `hlvr_prop_renderable_glove` and `point_worldtext` | `server.dll` | Present |
| Console commands `addon_play`, `addon_enable` and `addon_tools_map` | `game/hlvr/bin/win64/client.dll` | Present. `addon_tools_map` now prints "Cannot use addon_tools_map when not in tools mode" outside the Workshop Tools. That's harmless noise. |
| Models `models/props/choreo_office/headset_prop.vmdl` and `models/hands/alyx_glove_{left,right}.vmdl` | `game/hlvr/pak01_dir.vpk` | Present |
| Local addon folder | `gameinfo.gi` → `AddonRoot hlvr_addons` | `game/hlvr_addons/<name>` is the local addon location |

**`needs-game`:** add a script `tools/check-game-compat.ps1` that repeats these checks against a local install. Run it after every Valve update. Never commit or upload Valve's binaries.

---

## 3. Platform and dependencies

### 3.1 Runtime (`agent-ok`)

| Item | Now | Target | Notes |
|---|---|---|---|
| Target framework | `net6.0-windows` | `net10.0-windows` | Put it in one `Directory.Build.props` (see W2). |
| SDK | none pinned | 10.0.4xx (10.0.404 / runtime 10.0.15 as of 2026-11-10) | Pin it with `global.json`. |
| How players get the runtime | They must install it themselves | Publish **self-contained** for `win-x64` | Players then don't need to install any .NET runtime. |

### 3.2 NuGet packages (`agent-ok`)

| Package | Used | Latest (2026-11) | Action |
|---|---|---|---|
| Newtonsoft.Json | 13.0.1 | 13.0.4 | **Updated** (#7). 13.0.1 already contains the fix for CVE-2024-21907. Keep Newtonsoft, because the Lua scripts `import` it by name (`base_scripts/base/__init.lua`). |
| Websocket.Client | 4.4.43 | 5.5.1 | Maintained. Major-version jump: read the v5 release notes and adjust `ClientProgram.cs`. |
| NLua | 1.6.0 | 1.7.9 | Update together with KeraLua. |
| KeraLua (via NLua) | 1.3.1 | 1.4.11 | Comes with NLua. The copy steps no longer hard-code its version (W3). |
| DiscordRichPresence | 1.0.175 | 1.6.1.70 | Update. Expect small API changes in `DiscordRpcPlugin.cs`. The copy step no longer hard-codes its version or `net35` (W3). |
| System.Drawing.Common | 6.0.2-mauipre (a preview build) | 10.0.x | **Removed** (#7): `Color` is part of the base framework. |
| **Fleck** (WebSocket server) | 1.2.0 | 1.2.0 (2021-04) | **Unmaintained.** It has no known security advisories, but no message-size limits or TLS support. Medium-term, replace it with ASP.NET Core (Kestrel) WebSockets. |
| **Open.NAT** (UPnP) | 2.1.0 | 2.1.0 (2016-07) | **Unmaintained**, and built only for .NET Framework, which causes build warning NU1701. Replace it with a maintained UPnP/NAT-PMP library, or drop UPnP and document manual port forwarding. |

`dotnet list package --vulnerable --include-transitive` reports no known vulnerabilities, and no package is deprecated.

### 3.3 Game side

- **Scripting.** The game's built-in Lua (VScript) is fixed by Valve and can't be upgraded. `WorkshopAddon/game/hlvr_addons/kiwimp_alyx/scripts/vlua_globals.lua` is a usable offline list of the game's Lua functions.
- **Workshop addon deleted.** `AlyxGamemode.cs:51` still runs `addon_enable 2739356543`, which points at the deleted Workshop item. Switch it to the local addon name, `kiwimp_alyx` (`agent-ok`). Then add an installer (W9).

---

## 4. Security findings (most severe first)

Today every client trusts the server completely, and the server passes data from one client to all other clients with almost no checking. Player-controlled text ends up in other players' game consoles, and KCOM turns on `sv_cheats 1` for everyone.

### S1. Critical: one player can inject console commands into other players' games (`agent-ok`)

`AlyxGamemode.cs` builds console commands by joining strings from a client's packet, then sends them to all other clients:

| Line | Packet | Command built |
|---|---|---|
| 129 | `SPWN` | `ent_create <arg0> {targetname "<arg1>" ...}` |
| 261 | `PHYS` | `kcom_setlocation <arg0> ...` |
| 309 | `BRAK` | `kcom_break <arg0>` |
| 321 | `EREM` | `ent_remove <arg0>` |
| 333 | `FIRE` | `kcom_fireoutput <arg0> <arg1>` |
| 344 | `NPHP` | `kcom_npc_sethealth <arg0> <arg1>`, sent to **all** players |
| 354 | `TENT` | `kcom_cache_entity <arg0>` |

Arguments are split on spaces only, so command separators and quotes pass straight through. A modified client can therefore add arbitrary console commands to these messages.

**Fix:**
- **Check every argument before using it:**
  - Entity names: `^[A-Za-z0-9_+\-.]{1,128}$`.
  - Class names: only those in the addon's `kcom_trackers` list.
  - Numbers: parse with `float.TryParse(..., CultureInfo.InvariantCulture)` and format them again, rather than passing the text through.
- **Drop invalid packets explicitly** instead of relying on the blanket `catch {}`.
- **Unit-test the checks** (W8).

### S2. High: clients run whatever the server sends (`agent-ok`)

`ClientProgram.cs:119-124` writes `response.data` straight into VConsole. This means:
- A malicious server can run any console command on every player.
- The `kcom://connect/<host>` link handler pre-fills a server address. It doesn't auto-connect, but one click on Start hands that server control of your game console.

**Fix:**
- **Add a client-side allowlist of command prefixes:** `kcom_*`, `ent_fire kcom_*`, `ent_create logic_script` / `logic_timer` (used during setup), `play kcom/*`, `addon_play`, `addon_enable`, `buddha`, `unpause`, `sv_cheats`, `ent_remove kcom_script`, `echo`, `disconnect`.
- **Reject separators, newlines and quotes** in anything that isn't on the list.
- **Show the target host clearly** when a `kcom://` link is used.

### S3. High: no transport security, and messages are handled before login (`agent-ok`)

- **Plaintext.** The connection is `ws://` with no encryption, so passwords travel in plain text (`ServerProgram.cs:67`). The server listens on every network interface (`[::]`).
- **Wrong password doesn't disconnect** (`ServerProgram.cs:215-219`). The client gets a status message, and the socket stays open.
- **No login gate.** `OnMessage` passes every message from every socket to plugins, Lua and the gamemode *before* checking whether that socket has logged in (`ServerProgram.cs:204-209`).
- **No limits.** There's no limit on message size, connection rate or password attempts.

**Fix:**
- Close the socket on a wrong password.
- Ignore everything except `client` (the login message) until a socket is logged in.
- Cap message size at a few KB.
- Limit login attempts per IP address.
- Offer `wss://` (TLS) through Kestrel, or document putting KCOM behind a TLS reverse proxy for public servers.

### S4. Medium: anyone can kick anyone, and username bans don't hold (`agent-ok`)

When someone logs in with a username already in use (`ServerProgram.cs:222-232`), the code does the wrong thing:
1. It drops the **existing** player from the server's player list.
2. It closes the **new** socket.
3. It then adds the closed new socket to the list anyway.

So anyone can knock a player off by joining with their name, and bans by username are easy to get around.

**Fix:** reject the new login, or require a per-player secret, and remove the stale entry correctly.

### S5. Medium: weak username rules (`agent-ok`)

Usernames are limited to 32 characters and can't contain `;` or `"` (`ServerProgram.cs:235`). Newlines, control characters and other console-special characters are still allowed. The name is put into `ent_fire kcom_text_N setmessage "<name>"` on every client (`AlyxGamemode.cs:183`).

**Fix:** use an allowlist such as `^[A-Za-z0-9 _\-]{1,32}$`.

### S6. Medium: Lua scripts and plugins can do anything on the host's PC (by design; document it)

- `LuaEnvironment` calls `LoadCLRPackage()`, so Lua scripts get full .NET access. The bundled scripts already use `io.popen` and `io.open`.
- `UserInterface.cs:86-151` loads every DLL in `libraries/`, `gamemodes/` and `plugins/` without any check.

Document clearly that **a community script or plugin is as dangerous as running an .exe**. Optionally prompt before loading a script or plugin the first time it appears (`agent-ok`).

### S7. Low: leftovers on the host PC (`agent-ok`)

- **The UPnP port mapping is permanent** (`UserInterface.cs:527`). It's never removed, so the port stays open on the router after KCOM closes. Fix: give the mapping a lifetime and delete it when the server stops.
- **The `kcom://` handler is rewritten in `HKCU\Software\Classes\kcom` on every launch** (`Program.cs:39-47`), pointing at whichever .exe ran last. Fix: only register it on request, and offer an "unregister" option.
- **Passwords are stored in plain text** in `user.config`. Fix: encrypt them with Windows DPAPI (`ProtectedData`).

### S8. To check: the VConsole port (`needs-game`)

With the game running with `-vconsole`, run `Get-NetTCPConnection -LocalPort 29000`.

If it's listening on `0.0.0.0` rather than `127.0.0.1`, anyone on your network can control your game console. Document a Windows Firewall rule that blocks inbound connections to port 29000.

---

## 5. Stability and correctness bugs

### B1. High: number formatting depends on the Windows locale (`agent-ok` to fix, `needs-game` to confirm)

`Vector.ToString()` and `Angle.ToString()` (in KiwisCoOpModCore) and the teleport command at `AlyxGamemode.cs:150` format numbers with the PC's culture. `AlyxGamemode.cs:258` also parses physics positions with the PC's culture.

A host on a comma-decimal locale sends `kcom_setlocation_nonuuid kcom_head_0 12,5 ...`. The game's Lua `tonumber("12,5")` returns `nil`, so heads, hands and teleports stop syncing. Physics positions are misread or throw an error, which `catch {}` hides.

**Fix:** use `CultureInfo.InvariantCulture` in every `ToString`/`Parse`. As a one-line stopgap, set `CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture` in `Program.Main`. Add unit tests that run under `fr-FR`.

### B2. High: shared lists aren't thread-safe (`agent-ok`)

`ServerProgram.connections` (a `List<T>`) and `AlyxGlobalData`'s player and entity lists are read and changed at the same time from Fleck's socket threads, the tick loop and the UI thread. That causes intermittent "Collection was modified" errors and lost players, all hidden by `catch {}`.

**Fix:** use `ConcurrentDictionary`, or a single lock, or route all server work through one `Channel<T>` queue.

### B3. High: every network message waits on the UI thread (`agent-ok`)

`LuaEnvironment.Handle` uses `Invoke` to wait for the UI thread, and it runs about 5 times per incoming message. With 16 players each sending about 13 packets a second, the server spends most of its time waiting for the window to process messages. This causes lag and can freeze the app on shutdown.

**Fix:** run Lua on its own dedicated thread, and use `BeginInvoke` only for UI logging.

### B4. Medium: VConsole message framing is wrong (`agent-ok`)

- **Sending** (`VConsole.cs:187`): the length is one byte, so any command over about 242 characters throws `OverflowException`.
- **Receiving** (`StreamWatcher.cs:73-81`): the code reads only the low byte of the length, and ignores how many bytes `ReadAsync` actually returned (build warning CA2022). Any split or merged TCP read throws the stream out of sync for good. (W4 already switched it to `ReadExactlyAsync`, which removed CA2022, but the one-byte length and a 10-byte header remained.)

**Fix:**
- Use a 16-bit big-endian length.
- Read the header and body with `ReadExactlyAsync`.
- Reconnect when the stream gets out of sync.
- Cover it with tests against the fake VConsole server (W8).

**Done** (#11): `KiwisCoOpModCore/VConsoleFraming.cs` encodes and reads protocol 211 packets (12-byte header, 16-bit big-endian length; commands over 65,522 bytes are dropped rather than sent). `StreamWatcher` moved to `KiwisCoOpModCore`: it stops and raises `Faulted` when the stream ends or its header makes no sense, instead of spinning in a `catch { }`. `VConsole` then closes the connection and raises `ConnectionLost`, and `ClientProgram` reconnects through `ConnectVConsole`. Tests in `KiwisCoOpModCore.Tests` cover long commands, split and merged reads, and desyncs, against `FakeVConsoleServer`.

### B5. Medium: errors are silently swallowed (`agent-ok`)

`AlyxGamemode.Handle`, `LuaEnvironment.Handle` and `StreamWatcher.Read` all use `catch { }`, so failures are invisible.

**Fix:** log errors to the UI and to a rolling log file, for example with `Microsoft.Extensions.Logging`.

**Done** (#9), without `Microsoft.Extensions.Logging`: it has no file provider of its own, so a small `KiwisCoOpModCore.Log` does both. It writes to `logs/kcom-<date>.log` next to the app (a new file each day or past 5 MB, newest 10 kept) and raises `Log.Logged`, which the app shows in an "Errors" tab. The same error repeated within 10 seconds is only counted, so a failing `Think` handler can't flood it. Gamemodes and plugins can call it too. `StreamWatcher.Read` now stops when the VConsole stream closes instead of spinning on the exception. Tests: `KiwisCoOpModCore.Tests/LogTests.cs`.

### B6. Low: smaller logic bugs

- **Backward animation outputs are sent as forward** (`agent-ok`). `kcom_interval.lua:392-397` maps each `KCOM_OnCompletion*_Backward` handler to the `*_Forward` output, a copy-paste error. Other players' games replay backward animations as forward.
- **Door check precedence** (`agent-ok`). `kcom_interval.lua:67`: Lua's `and`/`or` precedence turns the door condition into `(door and pitchChanged) or yawChanged or rollChanged`. Check what was intended and add brackets.
- **Text compared to a number** (`agent-ok`). `kcom_interval.lua:734` compares the text `health` to a number in the non-UUID path. That path is unused while `KCOM_USE_UUIDS = true`, but it would throw if enabled.
- **Version warning can't appear** (`agent-ok`). `ClientProgram.cs:95-101` tests the same condition twice, so the "server is older" warning never shows.
- **Thread per event** (`agent-ok`). `AlyxGamemode.cs:235` starts a new `Thread` for each `IENT` event. Use `Task.Delay` instead.
- **English-only message matching** (`agent-ok` to write, `needs-game` to verify). The join check matches the English console text "has joined the game" (`AlyxGamemode.cs:79`), which is why `-language english` is required (upstream issue #1). Send a KCOM packet from the addon's Lua script instead.
- **Map change prints an error** (`agent-ok`). `ServerProgram.ChangeMap` and `ClientProgram` send `addon_tools_map` alongside `addon_play`. Outside tools mode it only prints an error, so drop it or send it only when the game is in tools mode.

---

## 6. Build, release and workflow

| # | Today | Recommended | Label |
|---|---|---|---|
| W1 | Upstream is archived. | Keep this fork as the source of truth. Keep the MIT `LICENSE.md` and credit KiwifruitDev in the README. | — |
| W2 | Each csproj repeats the framework, version and company settings. | Add `Directory.Build.props` (`net10.0-windows`, `Nullable`, `LangVersion`, `AnalysisLevel=latest`, version in one place) and `global.json` (SDK `10.0.4xx`). `latest-recommended` adds about 130 CA warnings, so it waits for W11. **Done** (#7). | `agent-ok` |
| W3 | Copy steps hard-code `$(USERPROFILE)\.nuget\packages\nlua\1.6.0\...` and the KeraLua and DiscordRPC paths. They break on any version change and in CI. | Copy the DLLs NuGet resolved: `@(RuntimeCopyLocalItems)` / `@(RuntimeTargetsCopyLocalItems)` filtered on `%(NuGetPackageId)`. (`GeneratePathProperty` would still hard-code `lib\net6.0` and need a direct KeraLua reference.) **Done** (#7). | `agent-ok` |
| W4 | The `CopyDLLs` targets always copy into **both** `bin\Debug` and `bin\Release`. | Copy only into `$(Configuration)`. **Done** (#7). | `agent-ok` |
| W5 | Package versions are spread across four projects. | Central Package Management (`Directory.Packages.props`) plus `NuGetAuditMode=all`. | `agent-ok` |
| W6 | No CI. | A GitHub Actions workflow on `windows-latest`: `setup-dotnet` 10.0.x, `dotnet build -c Release`, `dotnet test`, then `dotnet publish -r win-x64 --self-contained`, zipped with the addon folder. Upload it as an artifact, and create a GitHub Release on `v*` tags. | `agent-ok` |
| W7 | No dependency or security automation. | Dependabot (`nuget` and `github-actions`), CodeQL for C#, and secret scanning. | `agent-ok` |
| W8 | No tests. | An xUnit project covering packet parsing and validation (S1/S5), number formatting under `fr-FR` (B1), the client command allowlist (S2) and VConsole framing (B4). Also add a **fake VConsole TCP server**: it speaks protocol 211, records the `CMND` packets it receives and sends scripted `PRNT` lines, so the whole client → server → client path can be tested without the game. **Started** (#8): `KiwisCoOpModCore.Tests` (xUnit v3, plain `net10.0`, in `KCOM.sln`) has smoke tests for `Channel`. #11 added the fake server, `KiwisCoOpModCore.Tests/FakeVConsoleServer.cs`: it listens on a free loopback port, records packets (`Packets`) and CMND text (`Commands`, `NextCommandAsync`), and sends PRNT lines whole, split (`SendSplitAsync`) or as raw bytes, for S1 and S2's end-to-end tests. `global.json` opts `dotnet test` into Microsoft.Testing.Platform, which xUnit v3 needs on the .NET 10 SDK. | `agent-ok` |
| W9 | The Workshop addon is deleted, so it has to be installed by hand. | `tools/install-addon.ps1`: find the game through Steam's `libraryfolders.vdf` and copy `WorkshopAddon/game/hlvr_addons/kiwimp_alyx` into `game/hlvr_addons/`. Also add an "Install addon" menu item in the app. | write: `agent-ok`, verify: `needs-game` |
| W10 | Addon sounds (`.wav`) and the test map (`.vmap`) are uncompiled sources, so players without the Workshop Tools get no sounds and no `mp_kiwitest` map. | Compile them once with the Workshop Tools, and attach the compiled `.vsnd_c` / `.vmap_c` files to releases. | `needs-game` |
| W11 | Build warnings (nullable, CA2022, NU1701). | Fix them, raise `AnalysisLevel` to `latest-recommended` (W2) and fix what it adds, then turn on `TreatWarningsAsErrors`. | `agent-ok` |
| W12 | Unsigned .exe, so Windows SmartScreen will warn. | Optional: sign releases, or document the SmartScreen warning in the README. | — |
| W13 | Game-side Lua isn't checked by any tool. | Run `luacheck` in CI on `WorkshopAddon/**/*.lua` and `KiwisCoOpMod/base_scripts/**/*.lua`, using the names in `vlua_globals.lua` as allowed globals. It would have caught the B6 copy-paste and precedence bugs. | `agent-ok` |
| W14 | Project structure. | Move protocol and logic code (packet parsing, validation, number formatting, VConsole framing) into a plain `net10.0` library. Builds and tests then work on any OS, and only the WinForms app stays `-windows`. Alternatively, set `EnableWindowsTargeting=true` to build on Linux runners. **Partly done** (#8): `KiwisCoOpModCore` is now plain `net10.0` (it only needed `System.Drawing.Color`, which plain `net10.0` has), and the `-windows` app, gamemode and plugin still reference it. VConsole framing and `StreamWatcher` moved there in #11 (B4). Left for phase 3: move the protocol and logic code that still lives in `KiwisCoOpMod` (packet parsing, validation, number formatting) into it. | `agent-ok` |

---

## 7. Working with agents (Deckhand) when runs have no game files

- **What runs can do.** Agent runs can do nearly everything above. All the `agent-ok` items are plain C#, Lua or build changes that can be checked with unit tests, the fake VConsole server and `luacheck`.
- **What needs a PC with the game:**
  - in-game end-to-end testing;
  - checking the game after a Valve update (`tools/check-game-compat.ps1`);
  - compiling addon assets (W10);
  - the S8 port check.
- **Labels and review.** Label each issue `agent-ok` or `needs-game`. A pull request on a `needs-game` issue should include a short manual test checklist, run locally before merging.
- **Keep Valve's files out.** Never commit Valve game files, even partly, or upload them to CI.

---

## 8. Suggested order of work

Each phase is a parent issue under #5, with one issue per item. Each phase ends with a two-player in-game test issue (`needs-game`), and every issue of the next phase is blocked by it.

1. **Phase 1, stability** (≈1 day, mostly `agent-ok`)
   - W2–W4, then retarget to .NET 10.
   - W14-lite: make `KiwisCoOpModCore` a plain `net10.0` library and add an xUnit test project (W8), so B1, B4 and phase 2 have somewhere to put their tests.
   - B1 (number formatting), B2 (thread safety), B3 (UI-thread waits), B4 (VConsole framing), B5 (error logging).
   - The small B6 fixes: backward outputs, door precedence, text compared to a number, version warning, thread per event, `addon_tools_map`.
   - Switch `addon_enable` to `kiwimp_alyx`, and add W9 (addon installer).
2. **Phase 2, security** (1–2 days, `agent-ok`)
   - S1 (packet validation), S2 (client allowlist), S3 (login gate and limits), S4/S5 (usernames), S6 (docs), S7 (leftovers), S8 (port check, `needs-game`).
   - Write the tests from W8 alongside each fix.
3. **Phase 3, workflow** (≈1 day, `agent-ok`)
   - W5 (central package versions), W6 (CI), W7 (Dependabot and CodeQL), W11 (warnings), W13 (`luacheck`), and the rest of W14 (move the remaining protocol and logic code into the plain library).
   - Self-contained release builds, and `tools/check-game-compat.ps1`.
4. **Phase 4, larger and optional**
   - Package upgrades (Websocket.Client 5, NLua 1.7, DiscordRichPresence 1.6).
   - Replace Fleck and Open.NAT.
   - Add `wss://` (TLS).
   - Replace console-text parsing with a structured message format, together with B6's English-only join matching.
   - W10 (compiled addon assets), W12 (SmartScreen).
5. **The in-game test issue at the end of each phase** (`needs-game`)
   - Two-player in-game test on a campaign map, for example `a3_hotel_lobby_basement`.
   - Run `tools/check-game-compat.ps1`.
