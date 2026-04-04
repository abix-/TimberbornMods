# Project State. Timberborn

> Git-tracked. NEVER put secrets, tokens, or credentials in this file.
> Updated by Claude at session end. Shared across all agent clones.

## Current Focus
v0.7.1 release. blocked on test failures in path routing

## Design Goals
- Timberbot API errors should be actionable: tell the caller what went wrong AND what to do about it
- Both toon and json error output should include the full structured response (not just error string)
- No Claude Code hooks shipped with the mod. they interfere with parallel tool calls

## Last Session (2026-04-03)
Attempted v0.7.1 release. Version already bumped in both files. Build succeeded (0 warnings, 0 errors). Tests: 464 passed, 5 failed, 48 skipped.

Failed tests (all path routing / map verification):
1. `verify demolish via map` . map state not refreshed after demolish
2. `diagonal: no errors` . path routing errors during diagonal A* placement
3. `diagonal2: no errors` . same issue, different diagonal case
4. `obstacle: detour taken` . obstacle avoidance returned paths=4 but expected >8 (straight=8)
5. `sections: paths placed` . placed 0 paths when some were expected

Release blocked at step 3 (tests). Did not commit, tag, or publish.

## Next Steps
- Diagnose path routing test failures (likely in A* pathfinding or path placement logic in the mod)
- Fix failures and re-run full test suite
- Resume release from step 3 onward (test, commit, push, release, notes, Steam Workshop reminder)

## Open Questions
- Are path routing failures a regression from recent changes or a pre-existing issue with map/game state?
- Is the `verify demolish via map` failure related to the path routing issues or independent?
