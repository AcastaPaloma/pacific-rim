# HANDOFF.md: orchestrator state (reload from here after any context reset)

Last update: 2026-11-14 (session 1, founders asleep, instruction: keep running, no approvals, agents commit and push often, build to the letter of `FABLE_ORCHESTRATOR_PROMPT.md`).

## Where things are
- **Integration worktree:** `/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8` (project root is its `kaiju/` folder).
- **Integration branch:** `claude/fable-orchestrator-prompt-83a0a8` on `origin` (github.com/AcastaPaloma/pacific-rim). Never merge to `main`; the founders do that by PR.
- **Task worktrees:** `<integration worktree>/.wt/<task>` on branch `task/<task>`, created with
  `git worktree add .wt/T-00X -b task/T-00X claude/fable-orchestrator-prompt-83a0a8` (run from the integration worktree root). `.wt/` is git-ignored.
- **Unity Library for a task worktree:** clone the integration one instead of re-importing:
  `cp -cR <integration>/kaiju/Library <integration>/.wt/<task>/kaiju/Library` (APFS clone, instant).
- **Founders' editor:** a Unity editor is open on the main checkout `/Users/kuanw/Me/code/pacific-rim/kaiju` (port 7800) and the Funplay / CoplayDev / Unity MCP servers are attached to it. **Do not drive it**: it is not our branch. Agents use the Unity CLI in batch mode on their own worktree project.
- **Python:** conda env `kaiju` only (`/opt/miniconda3/envs/kaiju/bin/python`). Founder instruction, 2026-11-14.
- **Commits:** `Tools/agent/commit.sh "<msg>" <paths…>` (path-limited commit + push, lock-serialised).
- **Machine:** MacBook Pro, 15 cores, 24 GB. At most 2 Unity batch processes at once next to the founders' editor.

## Phase
**M0 · Foundations.** Gate: founders approve the style frame (M-003); walk passes M-001 and M-002.

## Plan of record
1. Research (parallel, no Unity): R-001 weight/gait metrics (M-001, M-002) · R-002 style metrics (M-003, M-004) + reference palettes · R-003 pixel-pipeline survey for URP 17 · R-004 GIS sources + M-005 · R-005 articulated biped control + M-006.
2. Slices, each through the full loop (brief → 2× expectation → reconcile → implement in worktree → evaluation panel → ≤3 fix cycles → integrate → scribe):
   - T-001 Core (events, registry, IDs, flags). Panel: eval-arch.
   - T-002 Tools/Metrics for M-001 to M-004 (Python, headless). Panel: eval-arch, eval-style (tool validity on references), eval-feel (tool validity on synthetic gaits).
   - T-003 one real SF tile (Pipelines/geo + World.Geo). Panel: eval-bay, eval-arch, eval-perf.
   - T-004 Rendering.Pixel renderer features + ThirdPersonRig stabilisation. Panel: eval-style, eval-arch, eval-perf.
   - T-005 walking mech (Physics.Body, Control, Units.Mech). Panel: eval-feel, eval-physics, eval-arch.
   - T-007 original greybox mech asset + concept prompt sheets. Panel: eval-style (silhouette), eval-arch.
   - T-006 M0 style frame (tile + static mech + pixel pipeline). Panel: eval-style, eval-bay, eval-perf.
3. After M0 slices: realism research loop (§6), calibration packet for the founders (§4.3), milestone report (§8), then start M1 briefs that do not depend on founder answers.

## Active tasks
See `ledger/LEDGER.md` § Tasks for live state. Update this list at every phase change.

Snapshot (2026-11-14, after research):
- Research R-001 to R-005: all written and pushed. Scribe is registering them (new M from M-009, new Q from Q-006; map in `ledger/ID_MAP.md`).
- Shared contracts fixed by research: `gait_log@v1` (R-001), `physics_log@v1` and `mech_frame@v1` (R-005, data in `research/R-005/mech_frame_balanced.json`), `palette_set@v1` and `style_capture@v1` (R-002), `geo_tile@v1` and the BayTM projection (R-004). M0 tile: `L0_30_36` (downtown SF waterfront).
- T-001 Core: implemented on `task/T-001` (33 criteria claimed met). eval-perf PASS; eval-arch running. Then integrate + scribe.
- T-007 mech asset: Phase A concepts done (11/16 Midjourney generations, `art/concepts/mech_frame_balanced/`). Phase B/C greybox + Unity import running on `task/T-007`.
- T-005 walking mech: reconciled spec `expectations/T-005.md`. Implementation waits for T-001 to merge (needs Core contracts). Strongest model, high effort.
- T-002 metrics, T-004 pixel pipeline, T-003 SF tile: expectation pairs running; reconcile next. T-004 on the strongest model; T-002 and T-003 on Opus.
- T-006 style frame: after T-003, T-004, T-007 merge.
- Folder metas for the whole module tree live on the integration branch (commit "Scaffold: module folder skeleton"); task branches must not re-create them. Feature flags are one JSON per module in `Assets/Game/Config/FeatureFlags/`.
- Unity concurrency: at most 2 batch processes besides the founders' editor. Evaluators check `pgrep` before running Unity.

Snapshot (2026-11-14 09:55, after a usage-limit interruption):
- T-001 Core: done end to end (cycle 1 PASS, fix cycle 1 re-check PASS, merged twice, regression 96/96 dotnet and 101/101 Unity EditMode). Ledger closure of its defects is owed to the next scribe pass (`evaluations/T-001/eval-arch.fix1.md`, `evidence/regression/T-001-fix1.md`; one new nit DEF-T001-arch-7).
- All six reconciled specs exist: `expectations/T-00{1,2,3,4,5,7}.md`. Scribe has numbered questions up to Q-043 and metrics up to M-017.
- Five implementation agents were cut off by the account's 5-hour usage limit at about 09:20 and resumed at 09:52 with SendMessage (same agents, same worktrees). If it happens again: check each `.wt/<task>` for uncommitted work, then resume the same agent IDs rather than starting new agents.
  - T-002 metric tools: `task/T-002`, agent `ab89c54476f1f7d18` (validators, registry, M-001/M-002 pushed).
  - T-003 SF tile: `task/T-003`, agent `ae2324271d8dd1491` (Phase 1 pipeline and baked tile pushed; Unity side next).
  - T-004 pixel pipeline: `task/T-004`, agent `aa971810b2bbe7bf2` (smoke test, contracts, colour maths, palette profiles pushed).
  - T-005 walking mech: `task/T-005`, agent `a097de118370441a4` (work in progress, first commit pending at the time of the cut).
  - T-007 mech greybox + import: `task/T-007`, agent `a675cf8ecd50f6256` (Blender build and ROM evidence pushed; Unity import next).
- Next per slice when its implementer reports: run its evaluation panel (see Plan of record), defects back to the same implementer agent for at most 3 cycles, then `integrate` (sonnet, low) and a scribe pass. T-002 should be evaluated and merged first because T-004 and T-005 evidence depends on its tools.
- Then T-006 (style frame: tile + static mech through the pixel pipeline), the realism research loop (§6), the founders' calibration packet (§4.3), and the M0 milestone report (§8).

Snapshot (2026-11-14 11:20, after a second usage-limit interruption; all agents resumed with SendMessage):
- T-007 mech asset: cycle 1 panel = eval-arch PASS, eval-physics PASS, eval-style FAIL (weight 5/10, identity, mood frame). Fix cycle 2 done on `task/T-007` (commit cc3acf3) plus mood frame v2 in the integration checkout (15/16 Midjourney generations). Cycle-2 re-checks: eval-arch PASS, eval-physics PASS (files `evaluations/T-007/*.cycle2.md`); eval-style cycle 2 in progress (agent `aa9022dca69cf185e`). Open rulings it may ask for: C27 concept-overlap IoU (spec says "expected", not a hard gate) and C6 hand-to-thigh 1 px gap versus the dimension source.
- T-003 SF tile: implemented on `task/T-003` (commit 98254f9): 685,685 triangles, 118 draw calls, 23.5 MB; M-005 report 18 pass / 3 fail / 9 not applicable. eval-perf PASS; eval-bay (agent `a66a771fa5143b2c5`) and eval-arch (agent `a358ee9706cbc1b4b`) in progress. Known integration hazard: each task that adds a feature-flag file regenerates Core's `GameCoreConfig.asset`; on a merge conflict the integrator takes either side and regenerates it with Core's own hook.
- T-002 metric tools: 16 commits on `task/T-002`, finishing evidence (agent `ab89c54476f1f7d18`). Evaluate and merge it first.
- T-004 pixel pipeline (agent `aa971810b2bbe7bf2`) and T-005 walking mech (agent `a097de118370441a4`): building, several commits pushed each.
- Owed to the next scribe pass: T-001 fix-cycle closure (all 8 defects closed, DEF-T001-arch-7 new nit), T-007 and T-003 evaluation defects, new Q proposals in `tasks/T-003.impl.md` / `tasks/T-007.impl.md` (the scribe may read the "proposed Q" sections only).

## Blockers / founder decisions pending
- **Usage budget:** the account hit its 5-hour limit once during M0 implementation. Keep spawns on the tiering table and briefs lean; prefer resuming an agent over restarting it.
- Style-frame approval, feel approval and metric calibration need the founders (§8). Thresholds are `Assumed, pending calibration` until then.
- Midjourney (D-023): a founder said on 2026-11-14 that Midjourney is already open in one of the Chrome browsers and may be used. T-007 Phase A drives it through the Chrome tools: no purchases, no settings changes, at most 16 generations, no reference-image uploads.
- Paid or credit-based 3D generation (Hunyuan3D / Meshy) is not used without a founder decision; T-007 Phase B models the greybox by Blender script instead.

## Next actions
1. Launch R-001 to R-005. Brief T-001 and run its expectation pair meanwhile.
2. When research lands: scribe records R-/M- entries; brief T-002 to T-005 and T-007; run expectation pairs; reconcile.
3. Implement with at most 2 Unity-touching worktrees at once.

## Orchestration decisions log (append-only)
- 2026-11-14 · Token budget (founder instruction: right model, right effort, no waste; the account limit was hit twice). The strongest model is used for `eval-style` / `eval-feel` only when they judge game captures. For T-002, where the question is whether the metric tools are numerically valid, one Opus/medium agent played both roles and wrote `evaluations/T-002/eval-style.md` and `eval-feel.md`.
- 2026-11-14 · T-005 cycle 1: the implementer's own stored metrics show the walk gate failing (steady Froude 0.020 against 0.20), and the spec says the feel panel can never rescue a failed numeric sub-measure (C7). `eval-feel` on captures is therefore deferred until a cycle reaches the Froude band; `eval-physics` and `eval-arch` ran on a frozen copy (`.wt/T-005-eval`, commit 17d2e9a) while the implementer started cycle 2. Research R-007 was commissioned in parallel on why the target pace is missed (orchestrator prompt §5.5 allows research when a metric cannot be hit).
- 2026-11-14 · T-003: metric-definition problems found by `eval-bay` (shoreline seam, default-height share, independent skyline reference) went to research R-006 (`M-005@v1.1` proposal) instead of fix cycles; build defects went back to the implementer (cycle 2).
- 2026-11-14 · T-007: rulings on C6, C11 and C27 readings appended to `expectations/T-007.md`; cycle 3 (the last) is with the implementer.

## Snapshot at the third usage-limit stop (2026-11-14, afternoon): resume from here
Merged and green on the integration branch: T-001 Core (with fix cycle 1) and T-007 mech asset (cycle 3, all evaluators PASS; merge 93ad0b5). Research R-001 to R-007 written; `M-005@v1.1` proposed by R-006.
Agents that were running when the limit hit (resume each with SendMessage to its ID, after checking its worktree for uncommitted work; do not start new agents for the same work):
- T-002 metric tools, fix cycle 2: implementer `ab89c54476f1f7d18`, `.wt/T-002`. Defects: `evaluations/T-002/eval-arch.md` (blocker: pooled vacuous-input check gives a wrong PASS; major: truncated PNG crash) plus four minors and the M-001.12 ruling from `eval-style.md` / `eval-feel.md`. Then re-check with eval-arch (new agent, sonnet) and integrate.
- T-003 SF tile, end of fix cycle 2: implementer `ae2324271d8dd1491`, `.wt/T-003`, fixing the shoreline seam under M-005.9 (p99 0.276 m against 0.20 m). Then re-evaluate with eval-bay `a66a771fa5143b2c5`, eval-arch `a358ee9706cbc1b4b`, eval-perf `abe4068c0e1095ff6` (resume them), then integrate.
- T-004 pixel pipeline, first build: implementer `aa971810b2bbe7bf2`, `.wt/T-004`. Then panel eval-style (strongest model), eval-arch, eval-perf.
- T-005 walking mech, fix cycle 3 (the last): implementer `a097de118370441a4`, `.wt/T-005`, building R-007's coordinated reference gait. Gate not met after cycle 2 (steady Froude 0.021 against 0.20; no assistance used). After cycle 3: eval-physics `a6c1e3b7c78c89b12`, eval-arch `ab9e4d678f583c2f8`, and eval-feel on captures if the Froude band is reached; if the gate still fails, escalate to the founders with options (do not start a fourth cycle).
- Scribe pass `a97ad446546a5d418`: recording T-001 fix closure, T-007 integration, R-006/R-007, evaluation defects for T-002/T-003/T-005 and five new founder questions. If it was cut off, check `git log` for its commit and re-run the remainder.
Not started: T-006 M0 style frame (needs T-003 and T-004 merged; T-007 is in), the realism research loop (§6), the founders' calibration packet (§4.3: about 10 captures per metric), the M0 milestone report (§8).
- 2026-11-14 · T-005 escalated. Three cycles delivered an honest unassisted walk (stand, start, walk, stop, turn in place, capture steps, falls) at steady Froude 0.023 against the 0.20 band; cycle 3's executable reference gait contradicts part of R-007 (at Froude 0.20 it needs −24.5° ankle dorsiflexion against a −20° range and 3.5e8 N·m of yaw moment against a 1.65e8 N·m friction budget). No fourth cycle: research R-008 (agent `ac1d811b215b511e7`) is reconciling feasibility and costing options for the founders. `task/T-005` (last commit eec7332) stays unmerged; its panel re-check is deferred until the gait approach is decided. The M0 gate "walk passes M-001 and M-002" is therefore NOT met and goes into the milestone report as the main open decision.
- 2026-11-14 · R-008 back: the controller binds now (flat-footed force-loop stance capped at Froude 0.005–0.025); the body can reach 0.20 only with heel-to-toe roll, pelvic tilt and trunk counter-rotation cancelling 80–87% of leg angular momentum; R-007 was optimistic on ankle demand and yaw moment. Per the loop diagram (re-research feeds back into the loop), T-005 re-enters as **round 2** with R-008's approach (stance-leg joint impedance around a corrected reference), fix-cycle counter restarted; brief `tasks/T-005.round2.md`, spec addendum at the end of `expectations/T-005.md` (target: into the band, Froude ≥ 0.10; C20 independence ruling and `M-006@v1.1` adopted as Assumed). New implementer agent `abd7255b21f96df44` on the same branch and worktree. Not merged; whether M0 may be accepted at the band floor, any assist, and merging the modules as infrastructure stay founder decisions (Q-053).
- 2026-11-14 · T-003 merged (06a373f). The raw GIS cache (1.7 GB, git-ignored) was APFS-cloned from `.wt/T-003` into the integration checkout so the two cache-dependent pytest tests run there; on a fresh clone they fail instead of skipping (minor defect to log).
- 2026-11-14 · T-006 (style frame + checkpoint packet) briefed and spec reconciled (`expectations/T-006.md`); implementation waits for T-004 to merge. T-004 panel so far: eval-arch PASS (7 minor), eval-perf FAIL (timing method), eval-style running (agent `a24422f6af5c9fcd8`). T-002 is in its third and last fix cycle (agent `ab89c54476f1f7d18`).
- 2026-11-14 13:20 · Founder instruction: conserve usage; hand simple work to Haiku or Sonnet. New tiering and usage rules are in `ledger/AGENT_PROTOCOL.md` §10 (they override §8). Consequences: (1) T-005 round 2 was **paused** 20 minutes in (it was the most expensive and most speculative job, on the strongest model, and it is gated on founder question Q-053): `task/T-005` is clean at cae49c0 (cycle-3 state plus the integration merge); the paused agent's untested work in progress is saved on `task/T-005-round2-wip`. To restart: a fresh Opus/high agent with `tasks/T-005.round2.md`. (2) `eval-style` / `eval-feel` now run on Opus. (3) Remaining mechanical chains (re-check → integrate → ledger) run as one workflow or one cheap agent each.
