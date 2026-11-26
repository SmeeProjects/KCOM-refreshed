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
- `needs-game` work: add a short manual test checklist (two players, a campaign map such as
  `a3_hotel_lobby_basement`) for the maintainer to run before merging.
- Never commit Valve game files or binaries, even partly (plan §7).

## Code style

- VScript functions available to the addon's Lua: `WorkshopAddon/game/hlvr_addons/kiwimp_alyx/scripts/vlua_globals.lua`.
- Keep line endings (LF in the repo) and the UTF-8 BOM most `.cs` files have.
