# QUEUE.md: what is left, as small tasks (read with `ledger/HANDOFF.md`)

Written 2026-11-14 afternoon by the orchestrator at a session hand-over. A fresh orchestrator session works this queue top to bottom. Rules for every task are in `ledger/AGENT_PROTOCOL.md` §11 (they override earlier sections): workers are Sonnet or Haiku at low or medium effort; each brief lists the exact files to read and write and nothing else; one purpose per worker, about five minutes of active work, never waiting on a long command; long Unity or bake runs are started by the orchestrator with a script in the background and their output file is read by a later worker; reap and hand over before a context grows large.

## State in one paragraph
Integration branch `claude/fable-orchestrator-prompt-83a0a8` (worktree `/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8`, project in `kaiju/`) holds: Core (T-001), the mech frame asset (T-007), the real downtown SF tile `L0_30_36` (T-003), research R-001 to R-008, six reconciled specs plus T-006's, ledger up to Q-054 / M-020. Unmerged task branches and worktrees (`.wt/<task>`): `task/T-002` metric tools (final re-check in progress), `task/T-004` pixel pipeline (fix cycle 2 interrupted on purpose; see its progress file), `task/T-005` walking mech (gate not met; round 2 paused; work in progress on `task/T-005-round2-wip`). Not started: T-006 style frame and checkpoint packet; the M0 report.

## Brief template (keep briefs this small)
```
You are <role> for <task>. Work in <absolute worktree path>. Read only: <files / sections>. Write only: <files>.
Requirement (verbatim): <the criterion or defect text, a few lines>.
Do: <one purpose, 3–6 concrete steps>. Do not start Unity / do not wait on long commands (or: run exactly this one command, which takes under N minutes).
Python: /opt/miniconda3/envs/kaiju/bin/python. Commit: Tools/agent/commit.sh "<msg>" <paths> (commits only those paths and pushes).
Stop and report if <condition>. Final message: at most 3 lines: <what to report>.
```
Unity (orchestrator runs these in the background, one at a time per worktree, at most 2 on the machine): `unity test <project> --mode EditMode -o <xml>`; `unity run <project> --no-tail --log-file <log> --timeout <s> -- -executeMethod <Class.Method>`; queue check: `ps ax -o command | grep -i "[U]nity.app/Contents/MacOS/Unity .*-batchmode" | grep -vc AssetImportWorker`. Never drive the founders' editor on port 7800.

## Queue
### 1. Merge T-002 (metric tools) — DONE (merge e104f31; regression `evidence/regression/T-002.md`, commit 285413e)
- Precondition: first line PASS in `evaluations/T-002/eval-arch.cycle3.md` (pending when this was written), `eval-style.cycle3.md` (PASS), `eval-feel.cycle3.md` (PASS). If eval-arch fails on its third cycle, do not loop: record the defect and ask the founders.
- Sonnet / low: in the integration worktree `git merge --no-ff origin/task/T-002` (head 4e29ee9; dry run was clean), run `/opt/miniconda3/envs/kaiju/bin/python -m pytest Tools/Metrics -q` (about 5 minutes: start it in the background from the orchestrator and give the worker the result file), push, write `evidence/regression/T-002.md`.

### 2. Ledger catch-up — DONE (2a bc6c615: tasks + 29 new defects incl. DEF-T003-geo-1; 2b 68aba8f/48eeba5: R-008 Proposed, M-006@v1.1 Assumed, Q-055–Q-067, next Q-068)
- 2a `ledger/LEDGER.md` only: Tasks rows for T-002, T-003 (merged 06a373f), T-004, T-005 (escalated, round 2 paused), T-006, T-007; Defects lines from the first-line verdicts and defect tables of `evaluations/T-002/*.cycle2.md`, `*.cycle3.md`, `evaluations/T-003/*.cycle2.md`, `evaluations/T-004/eval-*.md`; one minor defect: two `Pipelines/geo` tests fail instead of skipping when the raw cache is absent.
- 2b `GAME_BREAKDOWN.md`, `ledger/IDS.md`, `ledger/ID_MAP.md`: R-008 into §11a (`Proposed`); `M-006@v1.1` (Assumed) in §10a; new questions from R-008's "Proposed breakdown entries", from the "Questions for the founders" of `expectations/T-006.md`, and from the "proposed Q" sections only of `tasks/T-002.impl.md`, `tasks/T-003.impl.md`, `tasks/T-004.impl.md`, `tasks/T-005.impl.md`, `tasks/T-007.impl.md` (on their task branches; read with `git show origin/task/<task>:kaiju/tasks/<file>`), mapping to existing questions where they overlap; §12 change-log lines. Append only; nothing `Accepted`.

### 3. Finish T-004 fix cycle 2 (pixel pipeline), then re-check and merge
- IN PROGRESS: `.wt/T-004` now has `task/T-004-wip` checked out. Done on wip: c1/c2 restored by tuning (53ccabf, `tasks/T-004.c1c2.md`; thin margins); C6 table keys + C11 per-depth `smooth` (b698897; cycle-1 smoke: near 10.6, ref 1.04, far 3.4 px, all > 1 px → research/founders item); StyleCapture README (423eeb9, arch-6). Running: C6 amber/lavender seams + C7 1 km (writes `tasks/T-004.c6c7.md`).
- Remaining C# items (after the Unity tuning worker, to avoid recompiles under it): arch-1/-4 tests; arch-5 loud loaders; perf-4 GC ≥ 600 frames; perf-1/2/3 timing UnityTest; item 7 remove `.With(PixelFlags.SubPixelPresent, false)`; style-7 continuous-tone control in `StyleCaptureJobs.Controls` + board line; MODULE.md `direction`. Then arch-3 evidence size, arch-7 store log, full captures, tables, board, re-check.
- Start from `tasks/T-004.progress.md` on branch `task/T-004` (head ce9627c: the evaluated cycle-1 state plus that file; read it with `git show origin/task/T-004:kaiju/tasks/T-004.progress.md`). It lists 7 items partly done, 8 not started, 3 routed to research or the founders, the dither design, stale artifacts and regeneration commands. All fix-cycle-2 code so far is on **`task/T-004-wip`** (fc9b15e): it compiles and its dotnet tests pass (41/41), but Unity EditMode was not re-run there and M-003 c1 and c2 currently fail on it, so nothing from it is on `task/T-004` yet. First step: one Sonnet / medium worker finds why c1/c2 regressed on the wip branch (most likely the new gradient-only dither or the changed band assignment) and fixes it; then continue item by item on the wip branch and fast-forward `task/T-004` to it once EditMode and the M-003 gating sub-measures are green. Defect sources: `evaluations/T-004/eval-style.md`, `eval-perf.md`, `eval-arch.md`.
- One Sonnet / medium worker per open defect, each reading only the progress-file entry, the defect text and the one or two source files named there. Expected items: dither only in gradients (the main look defect; DIG into the references' 0.09–0.33 range while keeping DIG ≥ 0.15 where gradients exist and A₂ at 1.0); `direction` anchoring as the M0 default; lavender_peach seam (C6) and its check; haze-ramp band assignment for C7 (profile generation data only, never `research/R-002/palettes.json`); per-pass timing with profiler markers and the present pass isolated; a Rendering.Pixel assembly-reference test; non-trivial negative anchors.
- Then: orchestrator starts the capture run in the background (command in the progress file or `evidence/T-004/EVIDENCE.md`); a Haiku / low worker runs the metric CLI on the outputs and fills the tables; a Sonnet / low worker rebuilds the review board.
- Re-check with three small evaluators (Sonnet / medium for style on the new board plus numbers; Sonnet / low for architecture on the diff and for performance on the timing file), each told to read only the cycle-1 defect list, the changed files and the new artifacts. This is cycle 2 of at most 3. The yaw/translation shimmer thresholds and the single-plane smoothness finding are calibration and research items, not reasons to loop.
- Merge (Sonnet / low). Core's generated `GameCoreConfig.asset`: on conflict take either side and regenerate with Core's `CoreConfigAsset.CreateOrUpdate`.

### 4. T-006 style frame and checkpoint packet (needs step 3 merged)
Brief `tasks/T-006.md`, spec `expectations/T-006.md` (19 criteria). Worktree `.wt/T-006` from the integration branch, with the Library cloned (`cp -cR kaiju/Library .wt/T-006/kaiju/Library`). Steps, each one worker:
1. Sonnet / medium: Editor script under `Assets/Game/Tools/StyleFrame/` that builds the scene: tile via `World.Geo`, the mech prefab standing on the real seabed off the downtown waterfront, pipeline enabled by config. Reads: `Assets/Game/World/Geo/MODULE.md`, `Assets/Game/Rendering/Pixel/MODULE.md`, `Assets/Game/Camera/MODULE.md`, `Assets/Game/Units/Mech/Art/ART_ASSETS.md`, spec C3, C8, C10.
2. Sonnet / medium: placeholder props at true size and the hero camera through `ThirdPersonRig`; `shot_params.json`, `placeholder_props.json` (spec C1–C3).
3. Orchestrator: run the capture method in the background (hero at 1080p and 1440p, five profiles, three framings, controls); then Haiku / low: run `M-003` on every capture and write the tables.
4. Sonnet / low (Python only): `hero_thumb_test`, `scale_cues_plate`, in-frame skyline check and landmark projections, the review boards (spec C1–C4, C9, C13).
5. Orchestrator: orbit and push-in captures per anchoring mode and the timing run; Haiku / low: `M-004` tables and the timing table (C14, C15).
6. Sonnet / low: `checkpoints/M0/` (about 10 captures per metric from existing evidence folders, blind IDs, answer key, rating sheets in the `calibrate` format, the one-page guide, a dry run of `calibrate`), then the index `README.md` with at most seven decisions (C16–C19).
7. Sonnet / low: `evidence/T-006/EVIDENCE.md`.
8. Evaluation: style (Sonnet / medium, on the boards and numbers), bay (Sonnet / low, the truth checks), performance and architecture (Sonnet / low). Then merge.

### 5. M0 milestone report (orchestrator, medium effort)
`reports/M0.md` in the format of `FABLE_ORCHESTRATOR_PROMPT.md` §8: what passed with evidence links; what failed and why; decisions needed as numbered `Q-###` with recommendations (from the ledger's founder-questions table: the blocking one is the walk gate, Q-053); next actions. Then open a pull request from the integration branch only if the founders ask.

### 6. Paused or waiting on the founders (do not start without their word)
- **T-005 round 2** (walking mech): brief `tasks/T-005.round2.md`, design `research/R-008.md`; if approved, break R-008's plan into steps as in §11 (stance-leg impedance; corrected reference; trunk momentum compensation; continuation in small speed increments, each increment a background run plus a Haiku metric read).
- Metric calibration with the founders (orchestrator §4.3), using the T-006 packet.
- Any assist, body-data change, band change, or merging T-005's modules before its gate is met.

### 7. Later (after the report)
- Research follow-ups: the independent skyline reference recipe and a seabed-bias gate (`evaluations/T-003/eval-bay.cycle2.md`), shimmer threshold calibration and single-plane snapping (`evaluations/T-004/eval-style.md`), the mech-independence mechanism (R-008 §6).
- The realism research loop (orchestrator §6) on the merged M0 work.
- Minor-defect backlog in the ledger's Defects table.
