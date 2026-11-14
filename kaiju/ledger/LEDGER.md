# LEDGER.md: state of every requirement

> One row per `GAME_BREAKDOWN.md` ID. Maintained by the scribe and the orchestrator only (`ledger/AGENT_PROTOCOL.md` §3).
> Status: `todo` / `in-progress` / `eval` / `accepted` / `rejected`. `accepted` needs evidence links, every linked metric passing with stored values, and a passed evaluation panel (`FABLE_ORCHESTRATOR_PROMPT.md` §10).
> Decisions (`D-`) are constraints: their row tracks the tasks that must honour them. Questions (`Q-`) stay `todo` until a founder answers.

## Requirements

| ID | Status (todo/in-progress/eval/accepted/rejected) | Tasks | Evidence links | Open defects | Last update |
|---|---|---|---|---|---|
| D-001 | todo |  | | | 2026-11-14 |
| D-002 | todo |  | | | 2026-11-14 |
| D-003 | in-progress | T-001 (Core part done) | evaluations/T-001/eval-arch.md (PASS); evaluations/T-001/eval-perf.md (PASS); evidence/regression/T-001.md (merge 61aee09, dotnet 89/89, EditMode 94/94); evidence/T-001/EVIDENCE.md | | 2026-11-14 |
| D-004 | in-progress | T-001 (Core part done) | evaluations/T-001/eval-arch.md (PASS); evaluations/T-001/eval-perf.md (PASS); evidence/regression/T-001.md (merge 61aee09, dotnet 89/89, EditMode 94/94); evidence/T-001/EVIDENCE.md | | 2026-11-14 |
| D-005 | todo |  | | | 2026-11-14 |
| D-006 | in-progress | T-004 | | | 2026-11-14 |
| D-007 | in-progress | T-005 | | | 2026-11-14 |
| D-008 | in-progress | R-005, T-005 | | | 2026-11-14 |
| D-009 | in-progress | T-007 (all tasks) | | | 2026-11-14 |
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
| D-022 | in-progress | R-002, R-003, T-004, T-006 | | | 2026-11-14 |
| D-023 | in-progress | T-007 | | | 2026-11-14 |
| D-024 | todo |  | | | 2026-11-14 |
| D-025 | todo |  | | | 2026-11-14 |
| D-026 | todo |  | | | 2026-11-14 |
| D-027 | in-progress | T-004 | | | 2026-11-14 |
| F-001 | in-progress | R-005, T-005 | | | 2026-11-14 |
| F-002 | in-progress | T-005 | | | 2026-11-14 |
| F-003 | in-progress | T-005 | | | 2026-11-14 |
| F-004 | todo |  | | | 2026-11-14 |
| F-005 | todo |  | | | 2026-11-14 |
| F-006 | todo |  | | | 2026-11-14 |
| F-007 | todo |  | | | 2026-11-14 |
| F-008 | todo |  | | | 2026-11-14 |
| F-009 | in-progress | T-001 (Core part done), T-005 | evaluations/T-001/eval-arch.md (PASS); evaluations/T-001/eval-perf.md (PASS); evidence/regression/T-001.md (merge 61aee09, dotnet 89/89, EditMode 94/94); evidence/T-001/EVIDENCE.md | | 2026-11-14 |
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
| F-043 | in-progress | T-001 (Core part done) | evaluations/T-001/eval-arch.md (PASS); evaluations/T-001/eval-perf.md (PASS); evidence/regression/T-001.md (merge 61aee09, dotnet 89/89, EditMode 94/94); evidence/T-001/EVIDENCE.md | | 2026-11-14 |
| F-044 | todo |  | | | 2026-11-14 |
| F-045 | todo |  | | | 2026-11-14 |
| F-046 | in-progress | T-004 | | | 2026-11-14 |
| F-047 | todo |  | | | 2026-11-14 |
| F-060 | todo |  | | | 2026-11-14 |
| F-061 | todo |  | | | 2026-11-14 |
| F-062 | todo |  | | | 2026-11-14 |
| F-063 | todo |  | | | 2026-11-14 |
| F-064 | todo |  | | | 2026-11-14 |
| M-001 | in-progress | R-001, T-002, T-005 | research/R-001.md §9 (M-001@v1) | | 2026-11-14 |
| M-002 | in-progress | R-001, T-002, T-005 | research/R-001.md §9 (M-002@v1) | | 2026-11-14 |
| M-003 | in-progress | R-002, T-002, T-004, T-006 | research/R-002.md §4 (M-003@v1) | | 2026-11-14 |
| M-004 | in-progress | R-002, T-002, T-004 | research/R-002.md §5 (M-004@v1) | | 2026-11-14 |
| M-005 | in-progress | R-004, T-003 | research/R-004.md §6 (M-005@v1) | | 2026-11-14 |
| M-006 | in-progress | R-005, T-005 | research/R-005.md §8 (M-006@v1) | | 2026-11-14 |
| M-007 | todo |  | | | 2026-11-14 |
| M-008 | in-progress | R-003, T-001 (Core part done) | evaluations/T-001/eval-arch.md (PASS); evaluations/T-001/eval-perf.md (PASS); evidence/regression/T-001.md (merge 61aee09, dotnet 89/89, EditMode 94/94); evidence/T-001/EVIDENCE.md | | 2026-11-14 |
| M-009 | todo | R-001 | | | 2026-11-14 |
| M-010 | todo | R-001 | | | 2026-11-14 |
| M-011 | todo | R-002 | | | 2026-11-14 |
| M-012 | todo | R-002 | | | 2026-11-14 |
| M-013 | todo | R-003 | | | 2026-11-14 |
| M-014 | todo | R-003 | | | 2026-11-14 |
| M-015 | todo | R-004 | | | 2026-11-14 |
| M-016 | todo | R-005 | | | 2026-11-14 |
| M-017 | todo | T-001 expectations | | | 2026-11-14 |
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

## Research findings (R-###)

| ID | Topic | Status | File | Last update |
|---|---|---|---|---|
| R-001 | Weight and gait metric specs: M-001, M-002 | recorded | [research/R-001.md](../research/R-001.md) | 2026-11-14 |
| R-002 | Style metric specs: M-003, M-004; palettes extracted from the references | recorded | [research/R-002.md](../research/R-002.md) | 2026-11-14 |
| R-003 | Pixel-pipeline technique survey for URP 17 (Unity 6.6); M-008 render budget | recorded | [research/R-003.md](../research/R-003.md) | 2026-11-14 |
| R-004 | GIS data sources for the M0 tile and the Bay; M-005 spec | recorded | [research/R-004.md](../research/R-004.md) | 2026-11-14 |
| R-005 | Articulated biped at 10,000 t: mass properties, PD drives, balance, walking control; M-006 spec | recorded | [research/R-005.md](../research/R-005.md) | 2026-11-14 |

## Tasks (T-###)

| Task | Slice | Module(s) | Milestone | State | Branch | Cycle | Spec | Last update |
|---|---|---|---|---|---|---|---|---|
| T-001 | Core: events bus, service registry, IDs, feature flags | Core | M0 | integrated (cycle 1 PASS; fix cycle 1 in progress on task/T-001-fix1 for minor defects) | task/T-001 | 0 | expectations/T-001.md | 2026-11-14 |
| T-002 | Headless metric tools for M-001 to M-004 | Tools/Metrics | M0 | implementing (task/T-002) | task/T-002 | 0 | expectations/T-002.md | 2026-11-14 |
| T-003 | One real SF tile: terrain, bathymetry, OSM buildings, faults | Pipelines/geo, World.Geo | M0 | implementing (task/T-003) | task/T-003 | 0 | expectations/T-003.md | 2026-11-14 |
| T-004 | Pixel pipeline renderer features + third-person rig stabilisation | Rendering.Pixel, Camera | M0 | implementing (task/T-004) | task/T-004 | 0 | expectations/T-004.md | 2026-11-14 |
| T-005 | One mech body that walks with physics and intent control | Physics.Body, Control, Units.Mech | M0 | implementing (task/T-005) | task/T-005 | 0 | expectations/T-005.md | 2026-11-14 |
| T-006 | M0 style frame: SF tile + static mech through the pixel pipeline | integration of T-003/4/5/7 | M0 | planned (after T-003, T-004, T-007) | task/T-006 | 0 | none yet (planned) | 2026-11-14 |
| T-007 | Original greybox mech asset at true scale + concept prompt sheets | Units.Mech (asset) | M0 | implementing (task/T-007; concept phase done) | task/T-007 | 0 | expectations/T-007.md | 2026-11-14 |

## Defects

One line per evaluator defect until it is fixed and re-evaluated, or a founder rejects it.

| Defect | Task | Severity | Summary | Evidence | State (open/fixed-pending-eval/closed/rejected) | Last update |
|---|---|---|---|---|---|---|
| DEF-T001-perf-1 | T-001 | minor | the allocation test never exercises a burst deeper than the warm-up (queue/ring growth after warm-up is not covered); a module that publishes 1,000 events in one step the first time will allocate once, which is stated but not measured. PASS would look like: one extra case with a burst of N events (for example 5,000 in one cascade) measured on the second burst, 0 bytes. | evaluations/T-001/eval-perf.md | open (fix cycle 1) | 2026-11-14 |
| DEF-T001-perf-2 | T-001 | minor | C12 in Unity is count-only (Mono reports no bytes) and I did not re-run it; a ProfilerRecorder count of 0 is accepted as the proof. PASS would look like: the same, plus a note in EVIDENCE.md (already present) - no code change needed. | evaluations/T-001/eval-perf.md | open (fix cycle 1) | 2026-11-14 |
| DEF-T001-arch-1 | T-001 | minor | a throwing `ICoreErrorSink` permanently wedges the bus. | evaluations/T-001/eval-arch.md | open (fix cycle 1) | 2026-11-14 |
| DEF-T001-arch-2 | T-001 | minor | tests leave temp directories behind. | evaluations/T-001/eval-arch.md | open (fix cycle 1) | 2026-11-14 |
| DEF-T001-arch-3 | T-001 | minor | two assertions cannot fail meaningfully. | evaluations/T-001/eval-arch.md | open (fix cycle 1) | 2026-11-14 |
| DEF-T001-arch-4 | T-001 | minor | error path allocates every time; MODULE.md claims are silent on it. | evaluations/T-001/eval-arch.md | open (fix cycle 1) | 2026-11-14 |
| DEF-T001-arch-5 | T-001 | minor | no PlayMode assembly; MODULE.md "Tests" line omits PlayMode. | evaluations/T-001/eval-arch.md | open (fix cycle 1) | 2026-11-14 |
| DEF-T001-arch-6 | T-001 | minor | save store assumes the directory exists and does not sync it. | evaluations/T-001/eval-arch.md | open (fix cycle 1) | 2026-11-14 |

## Founder questions raised by the swarm

Mirrors `GAME_BREAKDOWN.md` §11 for questions raised after the initial breakdown. Each has a recommended default in use (`Assumed`).

| ID | Question | Recommended default | Raised by | Blocking? |
|---|---|---|---|---|
| Q-006 | Cruise Froude number for the reference mech | Fr 0.20, founders calibrate with the M-001 Froude-sweep captures. | R-001, T-005 | No |
| Q-007 | Foot shape and size | yes, 150 m², 15 × 10 m (T-005); M-002 scales with L_f from the log header. | R-001, T-005, R-005 | No |
| Q-008 | Reactor power for the reference mech | not reconciled; M0 walking needs ≈ 50-100 MW mean and ≈ 400 MW peak (R-005), so neither figure blocks M0. | R-001, R-005 | Yes, for F-004 and the Power subsystem (not M0 walking), per R-005 |
| Q-009 | Minimum mass versus height | lower bound M ≥ 8,500 t × (H/80)³; the 6,000 t corner loads with a warning. | R-005, T-005 | No |
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
