# KCOM-refreshed

Kiwi's Co-Op Mod for Half-Life: Alyx: a C# WinForms app (`KCOM.sln`) plus game-side Lua
(VScript) in `WorkshopAddon/` and `KiwisCoOpMod/base_scripts/`.

## The plan

`UPGRADE_PLAN.md` is the roadmap. Each issue maps to items there: name them (S1, B4, W8…)
in the pull request, follow the plan's fix, and keep to its phase order (§8). When an item's
fix turns out wrong or the code has moved, say so in the pull request and update the plan's
text for that item in the same pull request. Issues are labelled `agent-ok` (doable here) or
`needs-game` (needs a PC with the game).

## Toolchain

The image (`.deckhand/Dockerfile`) has the .NET 10 SDK, `pwsh` 7.6 and `luacheck` on the
`PATH`, and sets `USERPROFILE` for the `CopyDLLs` targets (until W3 is fixed). When a change
needs another tool, add it to the Dockerfile. `global.json` (W2) must use
`"rollForward": "latestFeature"`, so the image's newer SDK still matches it.

## Checks

- Build: `dotnet build KCOM.sln -c Release -p:EnableWindowsTargeting=true`. No new warnings.
- Tests: `dotnet test` on the test projects. Only plain `net10.0` projects run on Linux:
  put protocol, validation, number formatting and VConsole framing there (W14), and their
  tests (W8) next to them. Culture tests (`fr-FR`, B1) work here (ICU is installed).
- Lua: `luacheck WorkshopAddon KiwisCoOpMod/base_scripts`. Until W13 adds a `.luacheckrc`,
  report only the warnings your change adds.
- PowerShell scripts (W9, `tools/check-game-compat.ps1`): at least parse them with
  `pwsh -NoProfile`. They target Windows PowerShell 5.1 and pwsh 7 on Windows.
- The WinForms app and the game can't run here: never claim an in-game check you didn't run.

## Limits of these runs

- A push that changes `.github/workflows/` may be refused, and a workflow with write
  permissions (W6's release job, W7's CodeQL) always is. Then put the workflow in the
  pull request description for the maintainer to add, and do the rest in the pull request.
  CodeQL's default setup is turned on in the repository's settings, not with a file.
- Never commit Valve game files or binaries, even partly (plan §7).

## In-game checks

Every in-game check needs two players, so they are all run together in the current phase's
exit issue (#17, #25, #32, #58), never one change at a time (plan §7).
`needs-game` still means "needs a PC with the game"; only the timing of the tests changes.

- In a pull request, never ask for an in-game test before merging. Put the checks it needs
  under an `## In-game checks (phase exit)` heading in its description, as a checklist (two
  players, a campaign map such as `a3_hotel_lobby_basement`). The pull request can be merged
  without them, and the acceptance criteria they cover count as met by the code.
- Never file an issue just for a manual or in-game test, and never make one a blocker: such a
  test belongs to the current phase's exit issue. Work that needs the game (S8's port check,
  W10's asset compiling) stays a `needs-game` issue of its own.
- When filing or grooming an issue, put its in-game checks under an "In-game checks (phase
  exit)" heading rather than in its acceptance criteria, so the issue can close once its
  code is merged.
- A run on a phase's exit issue, once every other issue of the phase is closed, gathers the
  "In-game checks (phase exit)" sections of the phase's merged pull requests (and of its
  closed issues) into one checklist in the exit issue's body, grouped by pull request and
  linking each one, with `tools/check-game-compat.ps1` as the last step. It edits only the
  exit issue's body: the maintainer runs the checklist in one two-player session and ticks it.

## Code style

- VScript functions available to the addon's Lua: `WorkshopAddon/game/hlvr_addons/kiwimp_alyx/scripts/vlua_globals.lua`.
- Keep line endings (LF in the repo) and the UTF-8 BOM most `.cs` files have.
