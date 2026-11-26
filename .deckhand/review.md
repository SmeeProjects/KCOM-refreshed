# Reviewing KCOM-refreshed pull requests

## In-game checks

The game can't run in Deckhand's runs, and every in-game check needs two players, so all of
them are run together in the current phase's exit issue (#17, #25, #32,
#58), after the pull requests are merged (`UPGRADE_PLAN.md` §7 and §8).

- An in-game check that hasn't been run is neither a **Must fix** finding nor a
  **follow-up**: never file one as an issue, and never count an acceptance criterion as unmet
  only because it needs the game. Judge the code, the tests and the checks that run here.
- The pull request lists the in-game checks it needs under an `## In-game checks (phase exit)`
  heading in its description. When one is missing there (a behaviour the change touches that
  only the game can confirm), list it under Comments as a **note** to add to that section.
- A pull request that asks for an in-game test before merging, or a manual test checklist
  outside that section, gets a **note** too: move it to "In-game checks (phase exit)".
- Work that needs the game and isn't a check of this change (S8's port check, W10's asset
  compiling) keeps its own `needs-game` issue: don't ask for it here.
