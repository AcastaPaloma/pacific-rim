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

## Running
- T-029 step 2 (snapshot codec and interpolation): a worker is on `.wt/T-029`.
- T-020 add-on (optional Assessor `year_built`/`stories`/`struct_class` from DataSF `wv5m-vpq2`): a worker is on `.wt/T-020`. The tiles must be re-baked afterwards; the command will be in `tasks/T-020.progress.md`, rerun `$SCRATCH/t020/bake.sh` minus the fetches.
- T-024 Unity EditMode compile check (the hand-written `.meta` and asmdef files): background `unity test` in `.wt/T-024/kaiju`, log `$SCRATCH/t024u/run.log`, results `editmode.xml`. If it fails, send one fix worker for the meta/asmdef errors only.

## Next (in order)
1. T-029 steps 3 (host intent buffer; payload stays opaque until Q-100) and 4 (`net_log@v1`, two-peer in-process session, M-055/M-057 draft numbers). The brief bullets are in `tasks/T-029.progress.md`.
2. T-025 save section `destruction@v1` + migration (2 steps). Branch from `task/T-024`; the spec is in `tasks/T-024.progress.md` "Step 4, save" and R-021 §5.
3. T-027 seismic core (4 steps, pure C#): R-022 §1–§4; the PGA feed goes to `IDamageModel.ApplyShaking`. Vs30 is an open question (R-022 Q-NEW-c); proposed default rock 760 / fill 270 m/s, mark it Assumed.
4. T-035/T-036 metric tools (Python, R-026 §1, §5). M-060 is T-035's fourth step.
5. Unity steps one at a time: T-021 multi-tile load (after T-020), then T-022 landmarks and T-023 bridge (R-020 §2–§3).
6. T-030 and T-032 wait for the Q-100 contracts from session 4 (check its branch and HANDOFF).
7. Evaluators per track once implementation is done (eval-arch, eval-physics, eval-perf), Sonnet medium.
