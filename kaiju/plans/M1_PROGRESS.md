# M1 progress (orchestrator state; reload from here)

Orchestrator: M1 session started from the M0 orchestrator. The user is offline, so decisions are made autonomously and logged in `ledger/HANDOFF.md`.
Integration branch: `claude/fable-orchestrator-prompt-83a0a8`. Task worktrees: `<worktree root>/.wt/T-0xx`, branches `task/T-0xx` (pushed).
Never merge to `main`. T-005 (`.wt/T-005`, its QUEUE section) belongs to orchestrator session 4. Unity: at most 1 batch process from M1, and only while fewer than 2 run machine-wide (check: `ps ax -o command | grep -i "[U]nity.app/Contents/MacOS/Unity .*-batchmode" | grep -vc AssetImportWorker`). The founders' editor (`/Users/kuanw/Me/code/pacific-rim/kaiju`, port 7800) is never touched.
Workers: one Sonnet (medium; low for checks) per step, briefed in the QUEUE.md template. After each worker, the orchestrator re-runs its dotnet tests.
`$SCRATCH` = `/private/tmp/claude-501/-Users-kuanw-Me-code-pacific-rim--claude-worktrees-fable-orchestrator-prompt-83a0a8/82a000e8-e3b9-4808-b557-7401587b69e2/scratchpad` (session-local, may vanish).

## Done
- Research R-020..R-026 and `research/M1_W1_SUMMARY.md` (9d16027); decisions are in HANDOFF.md (2741a29).
- **T-020 district bake** (`task/T-020`): 14 tiles, ee238c2. `evidence/T-020/BAKE.md`: 8–37 s per tile, 297 MB, 8 of 14 tiles > 60 % water.
- **T-024 damage model** (`task/T-024`), 4/4 steps:
  - d838a98: contracts and capacity model.
  - 5294484: shaking route plus the M-051 probability function.
  - f512e45: debris and mass/energy account (E_frac capped at 25 % of ΔPE, Assumed).
  - 3a9861f: `destruction.enabled` flag (default off), Geo adapter, `DestructionModule.Install`, MODULE.md.
  - Tests: dotnet 39/39, Core 96/96.
- **T-029 Net core** step 1/4 (`task/T-029`), 24e68ac: contracts, loopback with seeded delay/jitter/loss, handshake refusal. Tests 26/26.

- T-024 Unity EditMode (`.wt/T-024`): 266/268, all 39 Destruction cases pass. The one failure was Core `Adapter_ConfigAsset_PointsAtCommittedFiles` (5 flag files vs 4 in the asset), fixed by regenerating `GameCoreConfig.asset` with `-executeMethod Game.Core.Unity.CoreConfigAsset.CreateOrUpdate` (c810c81). The next Unity run on the branch confirms it. **Every new flag file needs this regeneration.**
- T-029 step 2, 2d41593: snapshot codec and Hermite interpolation. Tests 37/37.

## Hand-over (2026-11-15 ~00:55; orchestrator context near 150k). Workers still running when this was written; check their branches for these commits:
- T-029 step 3 is DONE: 7aab2c4, 50/50 tests (verified). Accepted deviation: host steps with no fresh intent can run to ≤ 2 in a row (50 Hz host steps against 60 Hz datagrams); lost intents still run to ≤ 1. Decided autonomously (user offline): the brief said ≤ 1, but ≤ 2 is the sampling limit. Log this in HANDOFF at the next ledger pass.
- T-025 step 1 is DONE: f3d3ff3, 53/53 tests (verified). Its test assembly now references Game.Core for SaveCodec and SaveSystem, which is test-only and acceptable. Left for step 2: the MODULE.md save note, and the M-050 scale of 20,000 buildings (the tests use 2,000).
- T-020 add-on is DONE: 55ca01e (columns `year_property_built`, `number_of_stories`, `construction_type`, roll 2025). Re-bake of all 15 tiles in c35ccd1; pytest 72/72; match is about 75 % downtown and 43 % in the Presidio. Note: L0_30_36 is re-baked on `task/T-020` too. T-024's Geo adapter can now read `year_built` to choose Hazus code-era rows (a small follow-up step on T-024 or T-025).
- For each: re-run its dotnet tests or pytest. If its commit is missing, read `tasks/T-0xx.progress.md` and re-brief one fresh worker for that step.

## Next (in order)
1. T-029 step 4 (`net_log@v1`, two-peer in-process session, M-055/M-057 draft numbers). The brief bullet is in `tasks/T-029.progress.md`.
2. T-025 step 2: migration v1 → v2 test (M-050 "across one migration").
3. T-027 seismic core (4 steps, pure C#): R-022 §1–§4; the PGA feed goes to `IDamageModel.ApplyShaking`. Vs30 is an open question (R-022 Q-NEW-c); proposed default rock 760 / fill 270 m/s, mark it Assumed.
4. T-035/T-036 metric tools (Python, R-026 §1, §5). M-060 is T-035's fourth step.
5. Unity steps one at a time: T-021 multi-tile load (after T-020), then T-022 landmarks and T-023 bridge (R-020 §2–§3).
6. T-030 and T-032 wait for the Q-100 contracts from session 4 (check its branch and HANDOFF).
7. Evaluators per track once implementation is done (eval-arch, eval-physics, eval-perf), Sonnet medium.
