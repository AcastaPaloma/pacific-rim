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

## Blockers / founder decisions pending
- Style-frame approval, feel approval and metric calibration need the founders (§8). Thresholds are `Assumed, pending calibration` until then.
- Midjourney (D-023): a founder said on 2026-11-14 that Midjourney is already open in one of the Chrome browsers and may be used. T-007 Phase A drives it through the Chrome tools: no purchases, no settings changes, at most 16 generations, no reference-image uploads.
- Paid or credit-based 3D generation (Hunyuan3D / Meshy) is not used without a founder decision; T-007 Phase B models the greybox by Blender script instead.

## Next actions
1. Launch R-001 to R-005. Brief T-001 and run its expectation pair meanwhile.
2. When research lands: scribe records R-/M- entries; brief T-002 to T-005 and T-007; run expectation pairs; reconcile.
3. Implement with at most 2 Unity-touching worktrees at once.
