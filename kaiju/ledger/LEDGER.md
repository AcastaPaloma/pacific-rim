# LEDGER.md: state of every requirement

> One row per `GAME_BREAKDOWN.md` ID. Maintained by the scribe and the orchestrator only (`ledger/AGENT_PROTOCOL.md` §3).
> Status: `todo` / `in-progress` / `eval` / `accepted` / `rejected`. `accepted` needs evidence links, every linked metric passing with stored values, and a passed evaluation panel (`FABLE_ORCHESTRATOR_PROMPT.md` §10).
> Decisions (`D-`) are constraints: their row tracks the tasks that must honour them. Questions (`Q-`) stay `todo` until a founder answers.

## Requirements

| ID | Status (todo/in-progress/eval/accepted/rejected) | Tasks | Evidence links | Open defects | Last update |
|---|---|---|---|---|---|
| D-001 | todo |  | | | 2026-11-14 |
| D-002 | todo |  | | | 2026-11-14 |
| D-003 | in-progress | T-001 (Core part done; fix cycle 1 integrated) | evaluations/T-001/eval-arch.md (PASS); evaluations/T-001/eval-perf.md (PASS); evidence/regression/T-001.md (merge 61aee09, dotnet 89/89, EditMode 94/94); evidence/T-001/EVIDENCE.md; evaluations/T-001/eval-arch.fix1.md (PASS, re-check of the fix cycle); evidence/regression/T-001-fix1.md (merge f2e71b5, dotnet 96/96, EditMode 101/101) | 1 minor: DEF-T001-arch-7 | 2026-11-14 |
| D-004 | in-progress | T-001 (Core part done; fix cycle 1 integrated) | evaluations/T-001/eval-arch.md (PASS); evaluations/T-001/eval-perf.md (PASS); evidence/regression/T-001.md (merge 61aee09, dotnet 89/89, EditMode 94/94); evidence/T-001/EVIDENCE.md; evaluations/T-001/eval-arch.fix1.md (PASS, re-check of the fix cycle); evidence/regression/T-001-fix1.md (merge f2e71b5, dotnet 96/96, EditMode 101/101) | 1 minor: DEF-T001-arch-7 | 2026-11-14 |
| D-005 | todo |  | | | 2026-11-14 |
| D-006 | in-progress | T-004 | | | 2026-11-14 |
| D-007 | in-progress | T-005, T-007 (mech frame asset done) | evidence/regression/T-007.md (merge 93ad0b5, dotnet 96/96, EditMode 111/111); evaluations/T-007/eval-style.cycle3.md, eval-physics.cycle3.md, eval-arch.cycle3.md (all PASS); evidence/T-007/EVIDENCE.md (mech frame asset done) | 2 minor: DEF-T007-style-11, DEF-T007-style-12 | 2026-11-14 |
| D-008 | in-progress | R-005, T-005, T-007 (mech frame asset done) | evidence/regression/T-007.md (merge 93ad0b5, dotnet 96/96, EditMode 111/111); evaluations/T-007/eval-style.cycle3.md, eval-physics.cycle3.md, eval-arch.cycle3.md (all PASS); evidence/T-007/EVIDENCE.md (mech frame asset done) | 2 minor: DEF-T007-style-11, DEF-T007-style-12 | 2026-11-14 |
| D-009 | in-progress | T-007 (all tasks; mech frame asset done) | evidence/regression/T-007.md (merge 93ad0b5, dotnet 96/96, EditMode 111/111); evaluations/T-007/eval-style.cycle3.md, eval-physics.cycle3.md, eval-arch.cycle3.md (all PASS); evidence/T-007/EVIDENCE.md (mech frame asset done) | 2 minor: DEF-T007-style-11, DEF-T007-style-12 | 2026-11-14 |
| D-010 | in-progress | T-003 | | | 2026-11-14 |
| D-011 | todo |  | | | 2026-11-14 |
| D-012 | todo |  | | | 2026-11-14 |
| D-013 | in-progress | T-005 | | | 2026-11-14 |
| D-014 | todo |  | | | 2026-11-14 |
| D-015 | todo |  | | | 2026-11-14 |
| D-016 | todo |  | | | 2026-11-14 |
| D-017 | todo |  | | | 2026-11-14 |
| D-018 | in-progress | T-005 | | | 2026-11-14 |
| D-019 | todo |  | | | 2026-11-14 |
| D-020 | todo |  | | | 2026-11-14 |
| D-021 | todo |  | | | 2026-11-14 |
| D-022 | in-progress | R-002, R-003, T-004, T-006, T-007 (mech frame asset done) | evidence/regression/T-007.md (merge 93ad0b5, dotnet 96/96, EditMode 111/111); evaluations/T-007/eval-style.cycle3.md, eval-physics.cycle3.md, eval-arch.cycle3.md (all PASS); evidence/T-007/EVIDENCE.md (mech frame asset done) | 2 minor: DEF-T007-style-11, DEF-T007-style-12 | 2026-11-14 |
| D-023 | in-progress | T-007, T-007 (mech frame asset done) | evidence/regression/T-007.md (merge 93ad0b5, dotnet 96/96, EditMode 111/111); evaluations/T-007/eval-style.cycle3.md, eval-physics.cycle3.md, eval-arch.cycle3.md (all PASS); evidence/T-007/EVIDENCE.md (mech frame asset done) | 2 minor: DEF-T007-style-11, DEF-T007-style-12 | 2026-11-14 |
| D-024 | todo |  | | | 2026-11-14 |
| D-025 | in-progress | T-007 (mech frame asset done) | evidence/regression/T-007.md (merge 93ad0b5, dotnet 96/96, EditMode 111/111); evaluations/T-007/eval-style.cycle3.md, eval-physics.cycle3.md, eval-arch.cycle3.md (all PASS); evidence/T-007/EVIDENCE.md (mech frame asset done) | 2 minor: DEF-T007-style-11, DEF-T007-style-12 | 2026-11-14 |
| D-026 | todo |  | | | 2026-11-14 |
| D-027 | in-progress | T-004 | | | 2026-11-14 |
| F-001 | in-progress | R-005, T-005, T-007 (mech frame asset done) | evidence/regression/T-007.md (merge 93ad0b5, dotnet 96/96, EditMode 111/111); evaluations/T-007/eval-style.cycle3.md, eval-physics.cycle3.md, eval-arch.cycle3.md (all PASS); evidence/T-007/EVIDENCE.md (mech frame asset done) | 2 minor: DEF-T007-style-11, DEF-T007-style-12 | 2026-11-14 |
| F-002 | in-progress | T-005 | | | 2026-11-14 |
| F-003 | in-progress | T-005 | | | 2026-11-14 |
| F-004 | todo |  | | | 2026-11-14 |
| F-005 | todo |  | | | 2026-11-14 |
| F-006 | todo |  | | | 2026-11-14 |
| F-007 | todo |  | | | 2026-11-14 |
| F-008 | todo |  | | | 2026-11-14 |
| F-009 | in-progress | T-001 (Core part done; fix cycle 1 integrated), T-005 | evaluations/T-001/eval-arch.md (PASS); evaluations/T-001/eval-perf.md (PASS); evidence/regression/T-001.md (merge 61aee09, dotnet 89/89, EditMode 94/94); evidence/T-001/EVIDENCE.md; evaluations/T-001/eval-arch.fix1.md (PASS, re-check of the fix cycle); evidence/regression/T-001-fix1.md (merge f2e71b5, dotnet 96/96, EditMode 101/101) | 1 minor: DEF-T001-arch-7 | 2026-11-14 |
| F-010 | todo |  | | | 2026-11-14 |
| F-011 | todo |  | | | 2026-11-14 |
| F-012 | todo |  | | | 2026-11-14 |
| F-020 | todo |  | | | 2026-11-14 |
| F-021 | todo |  | | | 2026-11-14 |
| F-022 | todo |  | | | 2026-11-14 |
| F-023 | todo |  | | | 2026-11-14 |
| F-024 | todo |  | | | 2026-11-14 |
| F-025 | todo |  | | | 2026-11-14 |
| F-026 | todo |  | | | 2026-11-14 |
| F-040 | in-progress | R-004, T-003 | | | 2026-11-14 |
| F-041 | todo |  | | | 2026-11-14 |
| F-042 | todo |  | | | 2026-11-14 |
| F-043 | in-progress | T-001 (Core part done; fix cycle 1 integrated) | evaluations/T-001/eval-arch.md (PASS); evaluations/T-001/eval-perf.md (PASS); evidence/regression/T-001.md (merge 61aee09, dotnet 89/89, EditMode 94/94); evidence/T-001/EVIDENCE.md; evaluations/T-001/eval-arch.fix1.md (PASS, re-check of the fix cycle); evidence/regression/T-001-fix1.md (merge f2e71b5, dotnet 96/96, EditMode 101/101) | 1 minor: DEF-T001-arch-7 | 2026-11-14 |
| F-044 | todo |  | | | 2026-11-14 |
| F-045 | todo |  | | | 2026-11-14 |
| F-046 | in-progress | T-004 | | | 2026-11-14 |
| F-047 | todo |  | | | 2026-11-14 |
| F-060 | todo |  | | | 2026-11-14 |
| F-061 | todo |  | | | 2026-11-14 |
| F-062 | todo |  | | | 2026-11-14 |
| F-063 | todo |  | | | 2026-11-14 |
| F-064 | todo |  | | | 2026-11-14 |
| M-001 | in-progress | R-001, R-007, T-002, T-005 | research/R-001.md §9 (M-001@v1) | | 2026-11-14 |
| M-002 | in-progress | R-001, T-002, T-005 | research/R-001.md §9 (M-002@v1) | | 2026-11-14 |
| M-003 | in-progress | R-002, T-002, T-004, T-006 | research/R-002.md §4 (M-003@v1) | | 2026-11-14 |
| M-004 | in-progress | R-002, T-002, T-004 | research/R-002.md §5 (M-004@v1) | | 2026-11-14 |
| M-005 | in-progress | R-004, R-006, T-003 | research/R-004.md §6 (M-005@v1); research/R-006.md §5 (M-005@v1.1, Assumed) | | 2026-11-14 |
| M-006 | in-progress | R-005, R-007, T-005 | research/R-005.md §8 (M-006@v1); research/R-007.md §3 (M-006@v1.1 clarifications, Proposed, not registered) | | 2026-11-14 |
| M-007 | todo |  | | | 2026-11-14 |
| M-008 | in-progress | R-003, T-001 (Core part done; fix cycle 1 integrated) | evaluations/T-001/eval-arch.md (PASS); evaluations/T-001/eval-perf.md (PASS); evidence/regression/T-001.md (merge 61aee09, dotnet 89/89, EditMode 94/94); evidence/T-001/EVIDENCE.md; evaluations/T-001/eval-arch.fix1.md (PASS, re-check of the fix cycle); evidence/regression/T-001-fix1.md (merge f2e71b5, dotnet 96/96, EditMode 101/101) | 1 minor: DEF-T001-arch-7 | 2026-11-14 |
| M-009 | todo | R-001 | | | 2026-11-14 |
| M-010 | todo | R-001 | | | 2026-11-14 |
| M-011 | todo | R-002 | | | 2026-11-14 |
| M-012 | todo | R-002 | | | 2026-11-14 |
| M-013 | todo | R-003 | | | 2026-11-14 |
| M-014 | todo | R-003 | | | 2026-11-14 |
| M-015 | todo | R-004 | | | 2026-11-14 |
| M-016 | todo | R-005 | | | 2026-11-14 |
| M-017 | todo | T-001 expectations | | | 2026-11-14 |
| M-018 | todo | R-006 | | | 2026-11-14 |
| M-019 | todo | R-007 | | | 2026-11-14 |
| M-020 | todo | R-007 | | | 2026-11-14 |
| Q-001 | todo |  | | | 2026-11-14 |
| Q-002 | todo |  | | | 2026-11-14 |
| Q-003 | todo |  | | | 2026-11-14 |
| Q-004 | todo |  | | | 2026-11-14 |
| Q-005 | todo |  | | | 2026-11-14 |
| Q-006 | todo | | | | 2026-11-14 |
| Q-007 | todo | | | | 2026-11-14 |
| Q-008 | todo | | | | 2026-11-14 |
| Q-009 | todo | | | | 2026-11-14 |
| Q-010 | todo | | | | 2026-11-14 |
| Q-011 | todo | | | | 2026-11-14 |
| Q-012 | todo | | | | 2026-11-14 |
| Q-013 | todo | | | | 2026-11-14 |
| Q-014 | todo | | | | 2026-11-14 |
| Q-015 | todo | | | | 2026-11-14 |
| Q-016 | todo | | | | 2026-11-14 |
| Q-017 | todo | | | | 2026-11-14 |
| Q-018 | todo | | | | 2026-11-14 |
| Q-019 | todo | | | | 2026-11-14 |
| Q-020 | todo | | | | 2026-11-14 |
| Q-021 | todo | | | | 2026-11-14 |
| Q-022 | todo | | | | 2026-11-14 |
| Q-023 | todo | | | | 2026-11-14 |
| Q-024 | todo | | | | 2026-11-14 |
| Q-025 | todo | | | | 2026-11-14 |
| Q-026 | todo | | | | 2026-11-14 |
| Q-027 | todo | | | | 2026-11-14 |
| Q-028 | todo | | | | 2026-11-14 |
| Q-029 | todo | | | | 2026-11-14 |
| Q-030 | todo | | | | 2026-11-14 |
| Q-031 | todo | | | | 2026-11-14 |
| Q-032 | todo | | | | 2026-11-14 |
| Q-033 | todo | | | | 2026-11-14 |
| Q-034 | todo | | | | 2026-11-14 |
| Q-035 | todo | | | | 2026-11-14 |
| Q-036 | todo | | | | 2026-11-14 |
| Q-037 | todo | | | | 2026-11-14 |
| Q-038 | todo | | | | 2026-11-14 |
| Q-039 | todo | | | | 2026-11-14 |
| Q-040 | todo | | | | 2026-11-14 |
| Q-041 | todo | | | | 2026-11-14 |
| Q-042 | todo | | | | 2026-11-14 |
| Q-043 | todo | | | | 2026-11-14 |
| Q-044 | todo | | | | 2026-11-14 |
| Q-045 | todo | | | | 2026-11-14 |
| Q-046 | todo | | | | 2026-11-14 |
| Q-047 | todo | | | | 2026-11-14 |
| Q-048 | todo | | | | 2026-11-14 |
| Q-049 | todo | | | | 2026-11-14 |
| Q-050 | todo | | | | 2026-11-14 |
| Q-051 | todo | | | | 2026-11-14 |
| Q-052 | todo | | | | 2026-11-14 |
| Q-053 | The M0 walk gate (walk passes M-001 and M-002) is not met after three implementation cycles: the mech walks under pure physics with no assistance, but at Froude 0.023 against a 0.20 target. Research R-008 is establishing whether the body data or the controller is the limit. Options to decide between: another engineering approach (stance-leg impedance around a corrected reference; whole-body inverse dynamics; offline trajectory optimisation; a learned tracking policy), a small declared stabilising assist (ties to Q-014), a change to the body data (for example ankle range), or an interim lower band for M0. | no assist, no threshold or data change; T-005 stays unmerged; research first. | T-005 (orchestrator, cycle 3 result); related to Q-014 | Yes, for the M0 gate |
| Q-054 | todo | | | | 2026-11-14 |

## Research findings (R-###)

| ID | Topic | Status | File | Last update |
|---|---|---|---|---|
| R-001 | Weight and gait metric specs: M-001, M-002 | recorded | [research/R-001.md](../research/R-001.md) | 2026-11-14 |
| R-002 | Style metric specs: M-003, M-004; palettes extracted from the references | recorded | [research/R-002.md](../research/R-002.md) | 2026-11-14 |
| R-003 | Pixel-pipeline technique survey for URP 17 (Unity 6.6); M-008 render budget | recorded | [research/R-003.md](../research/R-003.md) | 2026-11-14 |
| R-004 | GIS data sources for the M0 tile and the Bay; M-005 spec | recorded | [research/R-004.md](../research/R-004.md) | 2026-11-14 |
| R-005 | Articulated biped at 10,000 t: mass properties, PD drives, balance, walking control; M-006 spec | recorded | [research/R-005.md](../research/R-005.md) | 2026-11-14 |
| R-006 | M-005@v1.1 after first use on the M0 tile: shoreline seam (M-005.9), default-height share by area (M-005.4(d)), skyline reference R-ind, draw-call measurement | recorded | [research/R-006.md](../research/R-006.md) | 2026-11-14 |
| R-007 | Why the first walk misses M-001 and the ranked path to Froude 0.20; M-006@v1.1 clarifications | recorded | [research/R-007.md](../research/R-007.md) | 2026-11-14 |
| R-008 | Whether the body data or the controller limits the M0 walk (Froude 0.023 against 0.20 after three cycles); input to Q-053 | commissioned | none yet | 2026-11-14 |

## Tasks (T-###)

| Task | Slice | Module(s) | Milestone | State | Branch | Cycle | Spec | Last update |
|---|---|---|---|---|---|---|---|---|
| T-001 | Core: events bus, service registry, IDs, feature flags | Core | M0 | integrated; fix cycle 1 integrated (merge f2e71b5, eval-arch PASS); 1 minor open | task/T-001 | 1 | expectations/T-001.md | 2026-11-14 |
| T-002 | Headless metric tools for M-001 to M-004 | Tools/Metrics | M0 | merged (merge e104f31); fix cycle 3: eval-arch, eval-style, eval-feel all PASS; cycle 1: eval-arch FAIL (1 blocker, 1 major, 5 minor), eval-style PASS, eval-feel PASS | task/T-002 | 3 | expectations/T-002.md | 2026-11-14 | 2026-11-14 
| T-003 | One real SF tile: terrain, bathymetry, OSM buildings, faults | Pipelines/geo, World.Geo | M0 | merged 06a373f; cycle 2: eval-bay PASS, eval-arch PASS, eval-perf PASS (bay-4 minor, bay-5, bay-8, bay-9, arch-6, perf-4..6 remain open or routed) | task/T-003 | 2 | expectations/T-003.md | 2026-11-14 | 2026-11-14 
| T-004 | Pixel pipeline renderer features + third-person rig stabilisation | Rendering.Pixel, Camera | M0 | fix cycle 2 in progress on task/T-004-wip (WIP commit fc9b15e, not verified; M-003 c1/c2 currently fail); cycle 1: eval-arch PASS (7 minor), eval-perf FAIL (3 major, 2 minor), eval-style FAIL (3 major, 1 calibration, 4 minor) | task/T-004-wip | 2 | expectations/T-004.md | 2026-11-14 | 2026-11-14 
| T-005 | One mech body that walks with physics and intent control | Physics.Body, Control, Units.Mech | M0 | escalated after 3 cycles: walk gate (M-001, M-002) not met; escalated to founders; round 2 paused pending Q-053; research R-008 commissioned; not merged (task/T-005, last commit eec7332). Cycle 3 result: steady Froude 0.023 (3.07 m/s) against the band around 0.20, no assistance used; M-001 3 pass / 6 fail / 3 not run / 1 pending; M-002.1 and .2 fail; M-006.4, .5, .6 fail; evidence in the task worktree, not yet on the integration branch | task/T-005 | 3 | expectations/T-005.md | 2026-11-14 |
| T-006 | M0 style frame: SF tile + static mech through the pixel pipeline | integration of T-003/4/5/7 | M0 | briefed, spec reconciled, waiting on T-004 (T-003 and T-007 merged) | task/T-006 | 0 | none yet (planned) | 2026-11-14 | 2026-11-14 
| T-007 | Original greybox mech asset at true scale + concept prompt sheets | Units.Mech (asset) | M0 | integrated (cycle 3: all three evaluators PASS; 2 minor open); merge 93ad0b5 | task/T-007 | 3 | expectations/T-007.md | 2026-11-14 | 2026-11-14 

## Defects

One line per evaluator defect until it is fixed and re-evaluated, or a founder rejects it.

| Defect | Task | Severity | Summary | Evidence | State (open/fixed-pending-eval/closed/rejected) | Last update |
|---|---|---|---|---|---|---|
| DEF-T001-perf-1 | T-001 | minor | the allocation test never exercises a burst deeper than the warm-up (queue/ring growth after warm-up is not covered); a module that publishes 1,000 events in one step the first time will allocate once, which is stated but not measured. PASS would look like: one extra case with a burst of N events (for example 5,000 in one cascade) measured on the second burst, 0 bytes. | evaluations/T-001/eval-perf.md; evaluations/T-001/eval-arch.fix1.md | closed (fix cycle 1, re-check PASS) | 2026-11-14 |
| DEF-T001-perf-2 | T-001 | minor | C12 in Unity is count-only (Mono reports no bytes) and I did not re-run it; a ProfilerRecorder count of 0 is accepted as the proof. PASS would look like: the same, plus a note in EVIDENCE.md (already present) - no code change needed. | evaluations/T-001/eval-perf.md; evaluations/T-001/eval-arch.fix1.md | closed (fix cycle 1, re-check PASS) | 2026-11-14 |
| DEF-T001-arch-1 | T-001 | minor | a throwing `ICoreErrorSink` permanently wedges the bus. | evaluations/T-001/eval-arch.md; evaluations/T-001/eval-arch.fix1.md | closed (fix cycle 1, re-check PASS) | 2026-11-14 |
| DEF-T001-arch-2 | T-001 | minor | tests leave temp directories behind. | evaluations/T-001/eval-arch.md; evaluations/T-001/eval-arch.fix1.md | closed (fix cycle 1, re-check PASS) | 2026-11-14 |
| DEF-T001-arch-3 | T-001 | minor | two assertions cannot fail meaningfully. | evaluations/T-001/eval-arch.md; evaluations/T-001/eval-arch.fix1.md | closed (fix cycle 1, re-check PASS) | 2026-11-14 |
| DEF-T001-arch-4 | T-001 | minor | error path allocates every time; MODULE.md claims are silent on it. | evaluations/T-001/eval-arch.md; evaluations/T-001/eval-arch.fix1.md | closed (fix cycle 1, re-check PASS) | 2026-11-14 |
| DEF-T001-arch-5 | T-001 | minor | no PlayMode assembly; MODULE.md "Tests" line omits PlayMode. | evaluations/T-001/eval-arch.md; evaluations/T-001/eval-arch.fix1.md | closed (fix cycle 1, re-check PASS) | 2026-11-14 |
| DEF-T001-arch-6 | T-001 | minor | save store assumes the directory exists and does not sync it. | evaluations/T-001/eval-arch.md; evaluations/T-001/eval-arch.fix1.md | closed (fix cycle 1, re-check PASS) | 2026-11-14 |
| DEF-T001-arch-7 | T-001 | minor (nit) | fix1.md says the rest-of-cascade drop on a throwing sink is "loud"; the dropped events are not named in the exception, only the sink's own exception surfaces. Acceptable for a contract violation ("sink must not throw"); no action required. | evaluations/T-001/eval-arch.fix1.md | open (minor) | 2026-11-14 |
| DEF-T007-arch-1 | T-007 | minor | Test gap for "nothing else exported except named helper empties" (C20). | evaluations/T-007/eval-arch.md; eval-arch.cycle2.md | closed (cycle 2) | 2026-11-14 |
| DEF-T007-arch-2 | T-007 | minor | Hardpoint/socket placement is not in config. | evaluations/T-007/eval-arch.md; eval-arch.cycle2.md | closed (cycle 2, recorded limitation accepted at minor) | 2026-11-14 |
| DEF-T007-arch-3 | T-007 | minor | Swap test mounts on the parent of the elbow socket and never uses the hardpoint socket nor a distinct stand-in mesh. | evaluations/T-007/eval-arch.md; eval-arch.cycle2.md | closed (cycle 2) | 2026-11-14 |
| DEF-T007-arch-4 | T-007 | minor | `MechPartMap.Remap` re-roots by path prefix string; no test covers a failing Remap or a duplicated-prefix asset. | evaluations/T-007/eval-arch.md; eval-arch.cycle2.md | closed (cycle 2) | 2026-11-14 |
| DEF-T007-physics-1 | T-007 | minor | At rest the arms hang 0.15 m beside the 8 m thighs, so several M0-active DOFs push arm into leg (shoulder adduction -10°: hand into thigh 113.8 m³, depth 4.9 m). | evaluations/T-007/eval-physics.md; eval-physics.cycle2.md; eval-physics.cycle3.md | closed (cycle 2, recorded in EVIDENCE.md "Not met"; hand-off rule for T-005 stated in cycle 3) | 2026-11-14 |
| DEF-T007-physics-2 | T-007 | minor | Neck flexion -30°/+40° overlaps the collar by 2.54/2.70 % of the head against the 2 % neighbour limit. | evaluations/T-007/eval-physics.md; eval-physics.cycle2.md | closed (cycle 2: 0.11 % / 0.004 %) | 2026-11-14 |
| DEF-T007-physics-3 | T-007 | minor (evidence) | `rom_report.json` sweeps only the left and central joints (17 of 29 DOFs); right side inferred by symmetry, not stated. | evaluations/T-007/eval-physics.md; eval-physics.cycle2.md | closed (cycle 2: all 29 DOFs) | 2026-11-14 |
| DEF-T007-physics-4 | T-007 | minor (evidence) | The T-005 handoff rule for the arm-leg contacts (Q-NEW-b: collision filtering or range limits) appears only in `tasks/T-007.impl.md`, which evaluators may not read. | evaluations/T-007/eval-physics.cycle2.md; eval-physics.cycle3.md | closed (cycle 3) | 2026-11-14 |
| DEF-T007-style-1 | T-007 | major | Greybox does not carry the chosen concept's identity (C27, C5); front IoU 0.393, side 0.288. | evaluations/T-007/eval-style.md; eval-style.cycle2.md | closed (cycle 2) | 2026-11-14 |
| DEF-T007-style-2 | T-007 | major | Panel score 5/10 against ≥ 8 (C16 weight); cycle 2 narrowed it to 7/10 (arms). | evaluations/T-007/eval-style.md; eval-style.cycle2.md; eval-style.cycle3.md | closed (cycle 3, passes at the bar with no margin) | 2026-11-14 |
| DEF-T007-style-3 | T-007 | major | Final mood frame fails two of the four check items and the histogram artifact is absent (C28). | evaluations/T-007/eval-style.md; eval-style.cycle2.md | closed (cycle 2) | 2026-11-14 |
| DEF-T007-style-4 | T-007 | minor | At 24 px the top reads as a 1 px spike between two 2 px lumps: a horned or eared head (C3, C7). | evaluations/T-007/eval-style.md; eval-style.cycle2.md | closed (cycle 2; see DEF-T007-style-11 for the watch item) | 2026-11-14 |
| DEF-T007-style-5 | T-007 | minor | 48 px front: hands fuse with thighs; feet distinct for one row only (C6). | evaluations/T-007/eval-style.md; eval-style.cycle2.md | closed (cycle 2, PASS line withdrawn by ruling (a)) | 2026-11-14 |
| DEF-T007-style-6 | T-007 | minor | Kept concepts show thin mast rods and more than one accent (C4, C25). | evaluations/T-007/eval-style.md; eval-style.cycle2.md | closed (cycle 2) | 2026-11-14 |
| DEF-T007-style-7 | T-007 | minor (evidence) | The blind test does not discriminate: both decoys are non-humanoid and the mech is label A on both sheets (C7). | evaluations/T-007/eval-style.md; eval-style.cycle2.md | closed (cycle 2) | 2026-11-14 |
| DEF-T007-style-8 | T-007 | minor | The 3.2 m upper-arm strut and 3.8 m shoulder ball depart from the concept and are not in the departures list (C27, C5). | evaluations/T-007/eval-style.cycle2.md; eval-style.cycle3.md | closed (cycle 3) | 2026-11-14 |
| DEF-T007-style-9 | T-007 | minor | Knee sliver: the side silhouette is 1.4 m deep between 25.7 and 26.1 m (C6, C21). | evaluations/T-007/eval-style.cycle2.md; eval-style.cycle3.md | closed (cycle 3) | 2026-11-14 |
| DEF-T007-style-10 | T-007 | minor | Mood v2 keeps thin masts and a deck instead of a distinct head cap; do-not-follow note only in PROVENANCE (C4, C25, C28). | evaluations/T-007/eval-style.cycle2.md; eval-style.cycle3.md | closed (cycle 3) | 2026-11-14 |
| DEF-T007-style-11 | T-007 | minor | Small-scale read is sensitive to sampling phase now that the frame is an even number of pixels wide (24 px top reads as three lumps; forearms fuse with the pelvis at 24 px; 48 px forearm and upper arm are the same 3 px) (C3, C5, C7). PASS (later art pass, not this task): join the blocks to the cowl below about 75 m, give the forearm a full-pixel step at 48 px, test two sampling phases. | evaluations/T-007/eval-style.cycle3.md | open (minor) | 2026-11-14 |
| DEF-T007-style-12 | T-007 | minor (evidence) | `E/EVIDENCE.md` row C26 says 11 of 16 generations (PROVENANCE: 15 of 16); row C7 says head 1 px (JSON: 2 px); `pixel_checks.json` `24px_leg_rows_with_gap` is 0 while the image shows a 2 px gap on 12 rows. | evaluations/T-007/eval-style.cycle3.md | open (minor) | 2026-11-14 |
| DEF-T002-arch-1 | T-002 | blocker | (C7 and C6) M-002 reports PASS, exit 0, "failing gating: 0", with no warning, when most of the set has no contacts. | evaluations/T-002/eval-arch.md | closed (cycle 3, all PASS) | 2026-11-14 |
| DEF-T002-arch-2 | T-002 | major | (C7, C15, C18) A truncated or corrupt `color_output` or `color_internal` PNG is not detected, and the tool crashes. | evaluations/T-002/eval-arch.md | closed (cycle 3, all PASS) | 2026-11-14 |
| DEF-T002-arch-3 | T-002 | minor | (C9) M-004 never reads `color_internal` when `palette_index` is present, and does not cross-check them as M-003 does; a capture with black colour and a real index passes. | evaluations/T-002/eval-arch.md | closed (cycle 3, all PASS) | 2026-11-14 |
| DEF-T002-arch-4 | T-002 | minor | (C15) `conventions.pixel_origin` is checked only for presence; "bottom-left" validates, while handedness is enforced. | evaluations/T-002/eval-arch.md | closed (cycle 3, all PASS) | 2026-11-14 |
| DEF-T002-arch-5 | T-002 | minor | (C10) `measures.py:82` hard-codes the minimum of 3 depth bands, while `registry/M-003@v1.yaml:25` holds `min_bands` = 3; the literal-scan test skips registry values 0, 1, 0.5, 2, 3, 4. | evaluations/T-002/eval-arch.md | closed (cycle 3, all PASS) | 2026-11-14 |
| DEF-T002-arch-6 | T-002 | minor | (C1 and C11) `M-003.b.O` has kind `info`; the spec says advisory. A draft registry gives verdict INCOMPLETE while the summary still says "failing gating: 1". | evaluations/T-002/eval-arch.md | closed (cycle 3, all PASS) | 2026-11-14 |
| DEF-T002-arch-7 | T-002 | minor (hygiene) | `fixtures/gen_style.py --help` is taken as an output directory and writes `Tools/Metrics/fixtures/out/example_set/` into the module; MODULE.md says the tool writes only to its output directory. | evaluations/T-002/eval-arch.md | closed (cycle 3, all PASS) | 2026-11-14 |
| DEF-T002-style-1 | T-002 | minor | M-003 crashes when the band means are equal. | evaluations/T-002/eval-style.md | closed (cycle 3, all PASS) | 2026-11-14 |
| DEF-T002-style-2 | T-002 | minor | O reads 0.0 when it is undefined (`REPORTED 0.0`). | evaluations/T-002/eval-style.md | closed (cycle 3, all PASS) | 2026-11-14 |
| DEF-T002-feel-1 | T-002 | minor | M-001.12 compares unlike quantities: the measured time ends at speed < 0.3 m/s (orchestrator ruling on M-001.12 applies). | evaluations/T-002/eval-feel.md | closed (cycle 3, all PASS) | 2026-11-14 |
| DEF-T002-feel-2 | T-002 | minor | The calibrate diff keeps an outdated source when the proposed diff flips an entry. | evaluations/T-002/eval-feel.md | closed (cycle 3, all PASS) | 2026-11-14 |
| DEF-T003-bay-1 | T-003 | major | (C12; brief "no invented buildings"; C5) Underground features are built as surface buildings. | evaluations/T-003/eval-bay.md | closed (cycle 2) | 2026-11-14 |
| DEF-T003-bay-2 | T-003 | major | (C5 truth / C8) A part with only `building:levels` gets levels × 4.22 m even when its parent has a tagged `height`, so parts rise above their own building. | evaluations/T-003/eval-bay.md | closed (cycle 2) | 2026-11-14 |
| DEF-T003-bay-3 | T-003 | major | (C8 evidence) The independent lidar reference is built with a PDAL filter that removes nothing. R-ind recipe restated in R-006 §3. | evaluations/T-003/eval-bay.md; research/R-006.md | closed (cycle 2) | 2026-11-14 |
| DEF-T003-bay-4 | T-003 | minor | (C2/C12) Base = terrain minimum under the footprint picks up pits in the 3DEP bare-earth surface. | evaluations/T-003/eval-bay.md | open (cycle 2: minor residue) | 2026-11-14 |
| DEF-T003-bay-5 | T-003 | major (as a criterion gate) | (C5) Default share is 13.3 % (10.1 % by area): a data gap plus a research rule, not a coding defect. R-006 moves the limit into M-005.4(d) by area (M0 ≤ 10 %, M1+ ≤ 2 %, Assumed). | evaluations/T-003/eval-bay.md; research/R-006.md | open (routed: research R-006 / founders) | 2026-11-14 |
| DEF-T003-bay-6 | T-003 | minor | (C11) The seam figure measures the wrong thing (source disagreement, 0.457 m against 0.1 m). R-006 redefines it as M-005.9 (p99 excess jump ≤ 0.20 m); the implementer is finishing the seam fix (p99 0.276 m). | evaluations/T-003/eval-bay.md; research/R-006.md | closed (cycle 2, by M-005 v1.1) | 2026-11-14 |
| DEF-T003-bay-7 | T-003 | minor | (C8 / R-004 §6.7) The viewpoint and band definitions make the gate fragile (V4 leaves Salesforce out of frame). R-006 §3 sets V4 to 257.6° at 75° hFOV. | evaluations/T-003/eval-bay.md; research/R-006.md | closed (cycle 2, by M-005 v1.1) | 2026-11-14 |
| DEF-T003-arch-1 | T-003 | minor | Manifest not byte-identical across independent bakes (`build.built_utc`, `build.bake_seconds`, `build.git`); EVIDENCE omits the manifest while C15 says "byte-identical outputs". | evaluations/T-003/eval-arch.md | closed (cycle 2) | 2026-11-14 |
| DEF-T003-arch-2 | T-003 | minor | The removal tests cannot prove "others unchanged". | evaluations/T-003/eval-arch.md | closed (cycle 2) | 2026-11-14 |
| DEF-T003-arch-3 | T-003 | minor | Important guarantees depend on the git-ignored cache; two tests report green-by-skip on a clean checkout. | evaluations/T-003/eval-arch.md | closed (cycle 2) | 2026-11-14 |
| DEF-T003-arch-4 | T-003 | minor | Some tuning still in code, with a default that contradicts the config (`terrainMaxErrorM` 0.5 in code against 0.1 in `Config/WorldGeo.json`). | evaluations/T-003/eval-arch.md | closed (cycle 2) | 2026-11-14 |
| DEF-T003-arch-5 | T-003 | minor | Module wiring and doc shape: `GeoModule.Install` is called only by tests; MODULE.md mixes the §6 template headings into prose. | evaluations/T-003/eval-arch.md | closed (cycle 2) | 2026-11-14 |
| DEF-T003-perf-1 | T-003 | minor | Draw calls are an estimate (renderer count), not measured; real calls can be 2 to 3x 118 (the 200 target is at risk). R-006 §4 gives the ProfilerRecorder procedure. | evaluations/T-003/eval-perf.md; research/R-006.md | closed (cycle 2) | 2026-11-14 |
| DEF-T003-perf-2 | T-003 | minor | The "individually removable" requirement (C14) is met by a per-building GameObject plus collider while the render path is chunked; not every building has its own renderer. | evaluations/T-003/eval-perf.md | closed (cycle 2) | 2026-11-14 |
| DEF-T003-perf-3 | T-003 | minor | Memory 71.2 MB covers meshes, colliders, albedo and terrain data only; no frame time was measured. | evaluations/T-003/eval-perf.md | closed (cycle 2) | 2026-11-14 |
| DEF-T003-perf-4 | T-003 | minor | Bake and build times are single runs on an Apple-silicon MacBook Pro; no target PC exists (see Q-019). | evaluations/T-003/eval-perf.md | open (routed: founders, Q-019, Assumed) | 2026-11-14 |
| DEF-T005-arch-1 | T-005 | major | (C12) Tuning numbers hard-coded in behaviour code: `walk_tuning.json` carries 60+ values, but dozens of gains, thresholds and timings remain literals (e.g. `Math.Sqrt(_l / 42.0)`). | evaluations/T-005/eval-arch.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-arch-2 | T-005 | major | (C20) Mech A is not independent of other mechs: two PlayMode tests fail the 1 cm clause (2.0 cm with 2 mechs, 3.8 cm with 4). See Q-052. | evaluations/T-005/eval-arch.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-arch-3 | T-005 | minor | (MODULARITY §1.5) Feature flags are inert: declared in JSON and as constants but no code calls `IsEnabled`. | evaluations/T-005/eval-arch.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-arch-4 | T-005 | minor | Out-of-module changes: Core `MODULE.md` still says "0.02 s Assumed" after the data change to 1/60; Core AdapterTests still use 0.02. | evaluations/T-005/eval-arch.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-arch-5 | T-005 | minor (hygiene) | `Tools/T005/` is task-named; `m006.py` and `plots.py` belong with the metric tools. | evaluations/T-005/eval-arch.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-arch-6 | T-005 | minor | (test strength and layering) The reference check exempts any assembly named `*.Scenarios`/`*.Editor`/`*Tests*` by suffix. | evaluations/T-005/eval-arch.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-arch-7 | T-005 | minor | Mutable static state in `BodyProbe`; `LocomotionController` throws unless the body has exactly 2 feet and hard-codes 2 arms. | evaluations/T-005/eval-arch.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-arch-8 | T-005 | minor | (C23) No `physics_log@v1` validator; the gait validation was run from a T-002 worktree with 2 uncommitted files. | evaluations/T-005/eval-arch.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-physics-1 | T-005 | blocker | Touchdown force: `physics/walk_fr020` peak 4.49 BW; 15 of 21 contacts above 2 BW; v_approach median -1.50 m/s. PASS: every walking contact 1.0-2.0 BW, approach ≤ 1 m/s. | evaluations/T-005/eval-physics.md; research/R-007.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-physics-2 | T-005 | blocker | Swing-start power transients: peak Σ P+ 2,992 MW over the whole capture (the metric file omits the first step). PASS: ≤ 450 MW at every step. | evaluations/T-005/eval-physics.md; research/R-007.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-physics-3 | T-005 | major | Mean Σ P+ is 132 MW at 2.9 m/s (CoT 0.46), with 46 % negative work. PASS: CoT within R-005's 0.05-0.1. | evaluations/T-005/eval-physics.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-physics-4 | T-005 | major | Waist flexion saturated in 5.8 % of steps (t > 20 s). PASS: ≤ 5 % for every joint. | evaluations/T-005/eval-physics.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-physics-5 | T-005 | major | The `FootContact` / footfall payload `peak_Fn` and `VerticalImpulseNs` are the values at the F_on crossing (median 0.45 of the true peak); the orchestrator clarifies C17. | evaluations/T-005/eval-physics.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-physics-6 | T-005 | minor | Spurious footfall events with Jn = 0 at step 1 in the three airborne impact scenes (6 events). | evaluations/T-005/eval-physics.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-physics-7 | T-005 | major | `PhysicsLog.Clamps` is never incremented, so R-005 R11 and the M-006.7 clamp check cannot be verified; leg joints reach the 3 rad/s engine cap. | evaluations/T-005/eval-physics.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-physics-8 | T-005 | major | Limp-fall energy account 0.913; foot friction and joint-limit work are not logged (M-006.5 limp term counts only drive dissipation). Research plus implementer. | evaluations/T-005/eval-physics.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-physics-9 | T-005 | major | (C20) Mech A moves 2.0 / 3.8 cm when 1 / 3 other mechs stand 300 m away without touching it; repeat runs are bit-identical. See Q-052. | evaluations/T-005/eval-physics.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-physics-10 | T-005 | major | The project-wide mass unit is undeclared outside Physics.Body, and SI-kg Rigidbodies work at 1e7 kg, so a later module can silently mix units. See Q-051. | evaluations/T-005/eval-physics.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T005-physics-11 | T-005 | minor | `EVIDENCE.md` §3(a) and `Physics/Body/MODULE.md` state the probe limit as "1e6 kg and more" for both solvers; under TGS the limit lies between 1e6 and 1e7. | evaluations/T-005/eval-physics.md | open (cycle 3 delivered; panel re-check deferred until the gait approach is decided) | 2026-11-14 |
| DEF-T002-arch-8 | T-002 | major | M-004 PASS exit 0 while gating sub-measure C_px.Y is NOT_APPLICABLE (no dithered pixels). | evaluations/T-002/eval-arch.cycle2.md; evaluations/T-002/eval-arch.cycle3.md | closed (cycle 3, PASS) | 2026-11-14 |
| DEF-T002-arch-9 | T-002 | minor | A v1.0 rescore does not reproduce a cycle-1 M-004 report, cause unexplained. | evaluations/T-002/eval-arch.cycle2.md; evaluations/T-002/eval-arch.cycle3.md | closed (cycle 3, PASS) | 2026-11-14 |
| DEF-T002-feel-3 | T-002 | minor | M-001.4 fails a stop at exactly the 0.30 g limit. | evaluations/T-002/eval-feel.cycle2.md; evaluations/T-002/eval-feel.cycle3.md | closed (cycle 3, PASS) | 2026-11-14 |
| DEF-T003-bay-8 | T-003 | major | R-006 section 3 R-ind recipe is weak: vegetation test deletes 23-43 % of tower-roof cells, change mask is circular and masks a landmark, floor degenerates the gate to IoU >= 0.80 / MAE <= 15 px. Routed to research, not the implementer. | evaluations/T-003/eval-bay.cycle2.md | open (routed: research) | 2026-11-14 |
| DEF-T003-bay-9 | T-003 | minor | Seaward blend mixes hydro-flattened 3DEP (-1.07 m) into the seabed (+0.68 m mean, +5.9 m max over 3.8 % of tile); no bias gate. | evaluations/T-003/eval-bay.cycle2.md | open | 2026-11-14 |
| DEF-T003-arch-6 | T-003 | minor | GeoRenderStatsPlayModeTests writes into the tracked evidence folder on every run and hard-codes the 1500 draw-call ceiling and 1920x1080 / frame counts. | evaluations/T-003/eval-arch.cycle2.md | open | 2026-11-14 |
| DEF-T003-perf-5 | T-003 | minor | Reported tile triangles 684,625 and per-frame 1.37 M do not match tile_stats.json (671,523) or render_stats_playmode.json (1,343,052 max). | evaluations/T-003/eval-perf.cycle2.md | open | 2026-11-14 |
| DEF-T003-perf-6 | T-003 | minor | Measured draw calls 242 exceed the 200 target (gate 1,500 met) in 3 of 5 views; mitigation named (merge chunks, cull). | evaluations/T-003/eval-perf.cycle2.md | open | 2026-11-14 |
| DEF-T003-geo-1 | T-003 | minor | Two Pipelines/geo pytest tests fail instead of skipping when the git-ignored raw GIS cache is absent (fresh clone). | scribe note, M0 catch-up (no evaluation file) | open | 2026-11-14 |
| DEF-T004-arch-1 | T-004 | minor | No Rendering.Pixel assembly-reference test (C17); only the Camera half exists. | evaluations/T-004/eval-arch.md | open | 2026-11-14 |
| DEF-T004-arch-2 | T-004 | minor | Validator rejects per-frame resolution offsets (owner T-002/R-002): cut_check and c11_subpixel_on INVALID; M-004 sets captured off the shipped flag. | evaluations/T-004/eval-arch.md | open | 2026-11-14 |
| DEF-T004-arch-3 | T-004 | minor | Committed evidence is 149 MB of PNGs in plain git plus 52 MB of metrics, no LFS; permanent history cost after merge. | evaluations/T-004/eval-arch.md | open | 2026-11-14 |
| DEF-T004-arch-4 | T-004 | minor | No test pins the feature-flag files (Rendering.Pixel.json, Camera.json) to all-false and to PixelFlags.All / CameraFlags.All. | evaluations/T-004/eval-arch.md | open | 2026-11-14 |
| DEF-T004-arch-5 | T-004 | minor | Tuning defaults duplicated in code (e.g. ditherSpread 0.6 in code vs 0.4 in PaletteProfileTuning.json). | evaluations/T-004/eval-arch.md | open | 2026-11-14 |
| DEF-T004-arch-6 | T-004 | minor | Assets/Game/Tools/StyleCapture has no README or MODULE.md; batch command only in a doc comment and EVIDENCE.md. | evaluations/T-004/eval-arch.md | open | 2026-11-14 |
| DEF-T004-arch-7 | T-004 | minor | Raw capture-run Unity log not stored (2-line summary only); warning-free capture cannot be checked from artifacts. | evaluations/T-004/eval-arch.md | open | 2026-11-14 |
| DEF-T004-style-1 | T-004 | major | C6: dither seams mix three bands; hue-incoherent in lavender_peach (dither spread, band mapping, ramp order). | evaluations/T-004/eval-style.md | open (fix cycle 2 WIP, not verified) | 2026-11-14 |
| DEF-T004-style-2 | T-004 | major | C7: 80 m block at 1 km under one value band from its background in lavender_peach, marginal in teal and crimson. | evaluations/T-004/eval-style.md | open (fix cycle 2 WIP, not verified) | 2026-11-14 |
| DEF-T004-style-3 | T-004 | major | C11: off the reference plane the presented image is not smooth on T1 (far edges +-2.5 px, near 15 px jumps); inherent to single-plane snap, to research and founders. | evaluations/T-004/eval-style.md | open | 2026-11-14 |
| DEF-T004-style-4 | T-004 | major (milestone, not gating) | M0 default reads as mechanical dither: full-face lattice on flat faces and hybrid moire. | evaluations/T-004/eval-style.md | open (fix cycle 2 WIP, not verified) | 2026-11-14 |
| DEF-T004-style-5 | T-004 | calibration | C9, C10: no mode meets yaw and translation together (hybrid vs direction); to research and the founders calibration packet. | evaluations/T-004/eval-style.md | open | 2026-11-14 |
| DEF-T004-style-6 | T-004 | minor | C6: slate_crimson far ground seam non-monotone (32 of 44 seams); crimson ramp entries 5-7 within 1 L*. | evaluations/T-004/eval-style.md | open | 2026-11-14 |
| DEF-T004-style-7 | T-004 | minor | Panel board: negative anchors are greyscale, so raters identify them at once; panel not blind. | evaluations/T-004/eval-style.md | open (fix cycle 2 WIP, not verified) | 2026-11-14 |
| DEF-T004-style-8 | T-004 | minor | Evidence gaps: orbit only hybrid and one profile; dolly path through near block; one cut case; A/B on one profile; tool commit differs from stored reports. | evaluations/T-004/eval-style.md | open | 2026-11-14 |
| DEF-T004-perf-1 | T-004 | major | C20 needs GPU p95 per pass; only paired wall-time differences, per-pass means within noise. | evaluations/T-004/eval-perf.md | open | 2026-11-14 |
| DEF-T004-perf-2 | T-004 | major | Present figure is the whole second camera plus capture copy (p95 0.74/0.60 ms vs 0.5); does not isolate the present pass. | evaluations/T-004/eval-perf.md | open | 2026-11-14 |
| DEF-T004-perf-3 | T-004 | minor | Chain p95 0.52 ms at 1080p exceeds 0.5 by 0.02 ms on a measure with ~0.13 ms noise; 1440p cheaper than 1080p. | evaluations/T-004/eval-perf.md | open | 2026-11-14 |
| DEF-T004-perf-4 | T-004 | minor | GC witness is main-thread, Editor, 100 frames, 3 configs only. | evaluations/T-004/eval-perf.md | open | 2026-11-14 |
| DEF-T004-perf-5 | T-004 | major (founders / target PC) | M-008 cannot be verified without Windows target-class hardware (RTX 3060 / RX 6600 class); Mac TBDR numbers do not transfer. | evaluations/T-004/eval-perf.md | open (routed: founders, target PC) | 2026-11-14 |

## Founder questions raised by the swarm

Mirrors `GAME_BREAKDOWN.md` §11 for questions raised after the initial breakdown. Each has a recommended default in use (`Assumed`).

| ID | Question | Recommended default | Raised by | Blocking? |
|---|---|---|---|---|
| Q-006 | Cruise Froude number for the reference mech | Fr 0.20, founders calibrate with the M-001 Froude-sweep captures. | R-001, T-005 | No |
| Q-007 | Foot shape and size | yes, 150 m², 15 × 10 m (T-005); M-002 scales with L_f from the log header. | R-001, T-005, R-005 | No |
| Q-008 | Reactor power for the reference mech | not reconciled; M0 walking needs ≈ 50-100 MW mean and ≈ 400 MW peak (R-005), so neither figure blocks M0. | R-001, R-005 | Yes, for F-004 and the Power subsystem (not M0 walking), per R-005 |
| Q-009 | Minimum mass versus height | lower bound M ≥ 8,500 t × (H/80)³; the 6,000 t corner loads with a warning. | R-005, T-005, T-007 (implied density about 1,030 kg/m³ at 10,000 t, from the asset's 9,721 m³ volume: almost exactly sea water; relevant to wading and buoyancy later) | No |
| Q-010 | Start, stop and turn feel ceilings | keep the derived limits; decide from the calibration captures. | R-001, T-005 | No |
| Q-011 | Foot-ground friction coefficient on Bay soils and pavement | μ = 0.4, to be replaced by a follow-up R-task on soil bearing and shear (also needed for the §4 ground-pressure claims). | R-001 | No |
| Q-012 | Heel-strike harshness | ≤ 1.0 m/s, tune with the feel evaluator. | R-005 | No |
| Q-013 | Mech proportions in `mech_frame_balanced.json` | as published. | R-005 | No |
| Q-014 | Assisted balance | the gate is judged with assistance off. | T-005 | No |
| Q-015 | Dither type | Bayer 4×4 as §9 says, O advisory, the M-003 (f) panel compares a blue-noise variant. | R-002 | No |
| Q-016 | Translation stability | yes for terrain, buildings and giants; direction-locked for sky and fog; M-004 translation threshold stays 0.12. | R-002 | No |
| Q-017 | Style profile set | yes; ref_04B and ref_06A stay excluded. | R-002 | No |
| Q-018 | Internal resolution rule | 480×270 at 1080p, 4K undecided. | R-002, R-003 | No |
| Q-019 | Target PC and test hardware for M-008 | Windows 10/11 x64, RTX 3060 12 GB / RX 6600 class, 6-core CPU, 16 GB RAM, SSD, 1080p60 (1440p recommended tier). No Windows test hardware is listed in STACK.md (dev is an M5 Pro Mac): Mac numbers are relative regressions only and M-008 stays unverified until target-class hardware or a cloud GPU exists. | R-003 | Blocking for M-008 sign-off only (not development) |
| Q-020 | Placement of `PixelStabilisationHints` / `IPixelStabilisationSource` in `Rendering.Pixel.C | yes. | R-003 | No |
| Q-021 | Accept camera rotation quantisation | on, behind `Pixel.RotationQuantise`, re-judged by the M-004 and feel panels. | R-003 | No |
| Q-022 | `art/style_refs/ref_01.png` and `ref_02.png` are byte-identical | treat the set as 5 references. | R-003 | No |
| Q-023 | Is shipped OSM-derived tile data a Produced Work or a Derivative Database under ODbL | treat cooked OSM layers as a Derivative Database, publish those layers (geometry and tags only) under ODbL, credit OpenStreetMap contributors on the splash and in the credits, get legal review before the Steam release. | R-004 | Blocking for release, not M0 |
| Q-024 | Render Earth curvature | visual-only curvature drop in the far-terrain/LOD vertex shader beyond 2 km (R_eff = 7/6 R); physics stays flat. | R-004 | No |
| Q-025 | Maximum separation between co-op players under host-authoritative PhysX | one `PhysicsScene` per activity region with its own tile-corner origin whenever players are more than 4 km apart. | R-004 | No |
| Q-026 | OSM snapshot policy for persistent building IDs | pin one OSM replication timestamp per release; on update keep an ID remap table (old `bid` to new) from footprint IoU > 0.6. | R-004 | No |
| Q-027 | Install-size budget | 1 m for urban and shoreline tiles, 2 m elsewhere, zstd-compressed. | R-004 | No |
| Q-028 | Static water level or tides | static MSL +0.969 m NAVD88 for v1; tides become a later module. | R-004 | No |
| Q-029 | Add `pdal` and `python-pdal` | add when M-005.7 is first run. | R-004 | No |
| Q-030 | M1 district tile sets and whether any fictional fault splay may cross a district | GG = i 25-27, j 36-38; SoMa = i 29-31, j 35-36; no fictional faults; emergences on the real offshore San Andreas / Golden Gate fault and the Hayward fault. | R-004 | No |
| Q-031 | Bay Bridge west-span tower height conflict: 143 m vs 158 m vs 160 m in OSM | exclude these towers from M-005.1(d) until Caltrans drawings are sourced; F-041 modelling uses 160 m (Assumed). | R-004 | No |
| Q-032 | Save compatibility promise | every schema from v1 loads forever via migration; pre-M3 development saves may be dropped only by founder decision. | T-001 | No |
| Q-033 | Look of the Balanced chassis | industrial-naval (hull-plate armour, exposed hydraulics at knees and elbows, shipyard-built), gunmetal/slate primary with off-white secondary and one small accent, no emblems until a faction identity exists. | T-007 | No |
| Q-034 | Chassis heights within D-007 | 76-80 m and wider so the two classes share sockets. Not needed for M0. | T-007 | No |
| Q-035 | Do provisional passes count (T-002)? A PASS resting on Assumed thresholds before calibration | counts as provisional and is labelled so until the first calibration checkpoint. | T-002 | No |
| Q-036 | Incomplete metrics at the M0 gate (T-002): panel scores and the optional non-flat-terrain walk pending | the gate uses the headless verdict plus the agent panel proxy; pending items are listed for the founders. | T-002 | No |
| Q-037 | Calibration ratings procedure (T-002) | both founders rate the same ~10 captures per metric plus anchors, independently; the helper uses the median and reports disagreement. | T-002 | No |
| Q-038 | Version bump on calibration (T-002) | the spec stays `@v1`, each registry change creates revision `v1.N`, and reports name the revision they used. | T-002 | No |
| Q-039 | Recent buildings missing from the open data (T-003): add by hand? | no invented buildings at M0; candidates listed in `known_gaps.md`. | T-003 | No |
| Q-040 | Roads at M0 (T-003) | ribbons on the terrain with bridge decks separate. | T-003 | No |
| Q-041 | DataSF-derived heights shipped under PDDL with a City and County of San Francisco credit (T-003) | yes. | T-003 | No |
| Q-042 | Camera impulse envelope and default framing (T-004) | impulse cap about 0.5° and 0.5 m, decay about 1.2 s; the 80 m mech fills about half the frame height, low horizon. | T-004 | No |
| Q-043 | Outlines, light shafts and clustered smoke (T-004) | outlines optional and off in M0; shafts and smoke in a later slice. | T-004 | No |
| Q-044 | Use 3DEP lidar to fill missing building heights, at the cost of some independence of the lidar reference? | yes, `height_source = lidar`, reported separately in every lidar comparison. | R-006 | No |
| Q-045 | Default-height limit path (M-005.4(d)). | ≤ 10 % by area at M0, ≤ 2 % by area from M1 (Assumed). | R-006, T-003 | No |
| Q-046 | V4 framing for the founder scene. | one view at 257.6° with 75° hFOV, rather than two 60° views. | R-006 | No |
| Q-047 | Skyline margins against the noise floor (-0.02 IoU, +1 px, floor 0.80) and the vegetation (50 % multi-return) and crane (3 m) parameters. | as stated, calibrated at the first checkpoint. | R-006 | No |
| Q-048 | Widen hip extension from -20° to -25° in `mech_frame` for a 5° drive margin at Fr 0.23 with less pre-swing knee bend? | no change (case D reaches 33.6 m with the current ROM). | R-007 | No |
| Q-049 | Arm counter-swing amplitude is a look decision (±25° cancels the legs' fundamental; human-like ±12-15° leaves more for the waist, about ±10° thorax yaw). | ±20°, the waist absorbs the rest. | R-007 | No |
| Q-050 | Nominal duty factor within the registered 0.60-0.72. | β = 0.64 (swing 2.38 s); at β ≥ 0.70 hip power and yaw rise above the budget. | R-007 | No |
| Q-051 | Simulation mass unit: PhysX needs masses scaled (1e5 kg per unit) for contacts on 1e7 kg articulations to work; the rescaling is physically consistent, but without a project-wide declaration a later module can silently mix units (DEF-T005-physics-10). | one documented conversion at Core level, all logs and contracts in SI. | T-005 (eval-physics) | No |
| Q-052 | Mech independence tolerance (C20): one mech's motion shifts by 2-4 cm when another mech steps at the same time, against a 1 cm criterion; cause not isolated; repeat runs are bit-identical (DEF-T005-arch-2, DEF-T005-physics-9). | keep the test red, research the solver coupling, decide the tolerance afterwards. | T-005 (eval-arch, eval-physics) | No |
| Q-053 | The M0 walk gate (walk passes M-001 and M-002) is not met after three implementation cycles: the mech walks under pure physics with no assistance, but at Froude 0.023 against a 0.20 target. Research R-008 is establishing whether the body data or the controller is the limit. Options to decide between: another engineering approach (stance-leg impedance around a corrected reference; whole-body inverse dynamics; offline trajectory optimisation; a learned tracking policy), a small declared stabilising assist (ties to Q-014), a change to the body data (for example ankle range), or an interim lower band for M0. | no assist, no threshold or data change; T-005 stays unmerged; research first. | T-005 (orchestrator, cycle 3 result); related to Q-014 | Yes, for the M0 gate |
| Q-054 | Weight read of the first mech passed at exactly 8/10 (C16, DEF-T007-style-2) with no margin; more margin needs wider legs or a higher volume cap (shank and foot volume fractions 1.25 of a 1.30 limit, total volume 9,721 of 10,000 m³). | accept for M0, revisit with the founders' look review. | T-007 (eval-style cycle 3) | No |
