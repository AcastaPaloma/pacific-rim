# M1 progress (orchestrator state; reload from here)

Orchestrator: M1 lead, session 3 (2026-11-15). User offline: decisions are made autonomously and logged in `ledger/HANDOFF.md` ("M1 lead orchestrator, session 3").
Integration branch: `claude/fable-orchestrator-prompt-83a0a8`. Task worktrees: `<worktree root>/.wt/T-0xx`, branches `task/T-0xx` (pushed).
Never merge to `main`. Run every git command as `git -C <integration worktree>` and check `git branch --show-current` first. T-005 (`.wt/T-005`, its QUEUE section) belongs to orchestrator session 5; it also commits QUEUE.md on the integration branch (fine, different files). Unity: at most 1 batch process from M1, only while fewer than 2 run machine-wide. The founders' editor (port 7800) is never touched.
Workers: one Sonnet (medium) per step, code only; the orchestrator re-runs dotnet tests and runs Unity in the background. Hand-written `.meta` files: `grep -rl "fileFormatVersion: 2guid" Assets` before every Unity run.
Integration Unity script: `unity run <kaiju> -- -executeMethod Game.Core.Unity.CoreConfigAsset.CreateOrUpdate`, then `unity test <kaiju> --mode EditMode`.
Geo cache (`Pipelines/geo/cache`, git-ignored, 1.7 GB+): task worktrees get an APFS clone (`cp -cR`); never commit it or a symlink to it.

Scheduling: `ledger/AGENT_PROTOCOL.md` §12 (parallel track sessions on Sonnet/medium, shared merge lock `/tmp/kaiju-merge.lock`, never idle on a blocker). Running tracks (2026-11-15): T-023 step 2; AI (R-024 + T-033); co-op net (T-030 step 2 + T-031); destruction metrics (T-035/T-036); T-005 round 4.

## Merged into the integration branch
Session 2 (regressions `evidence/regression/M1-merge-{1,2,3}.md`): T-020/T-020b, T-021 1–3, T-024/T-024b, T-025, T-027 1–4, T-029 1–4, T-035 1, T-036 2.
Session 3 (regression `evidence/regression/M1-merge-4.md`):
- **M-005@v1.2 district PASS** (7a3b7c9): union default-height share 0.0144 (was 0.0218), every gated tile ≤ 0.05. Marin fill source `lidar_ept_fill` = ARRA-CA_GoldenGate_2010 EPT (only EPT covering Sausalito/Headlands), merged cell-wise for L0_25_38, 26_38, 27_38, 27_37, 26_36 (`fetch_lidar_dsm.py` FILL_TILES); `lidar_fill.low_floor` rule (clear low measurement → 2.0 m). All 15 tiles re-baked.
- **T-021 step 4** (T-021 complete): M-015 PASS (8444 m GG↔SoMa, 4 rebases at 3072 m, max |pos| 3072 m, ulp 0.24 mm, round-trip 0.12 mm); M-018 district render stats (Mac relative: SoMa obliques up to 2446 draw calls / 17.0 M tris; 1469 renderers). `evidence/T-021/`.
- **T-022 step 1**: `landmarks` layer in the bake (`kaiju_geo/landmarks.py`, `config/landmarks.json`, licence `LicenseRef-facts-cited`); prisms emit 0 parts (OSM already right for Salesforce/Transamerica; Ferry tower 11.5 m above sourced, reported only).
- **T-023 step 1**: Golden Gate Bridge features measured from lidar (`measure_ggb.py`, `config/ggb.json`, `evidence/T-023/ggb_lidar_measure.json`): 4 leg prisms (spacing 25.1/27.4 m), deck LineString with measured profile (75 m at towers → 81.7 m midspan), 2 cables (midspan 4.5 m above deck), cut at z = 77824; in 26_37/26_38 `landmarks.geojson`.
- **T-030 step 1**: `Game.Net.ControlBridge` (IntentCodec, RemotePilotSource, LocalSeatIntentPublisher), two-peer loopback into PilotSeat at RTT 0/100 ms. dotnet 6/6.
- **T-030 step 2 + T-031** (task/T-030b): `MechSeat`/`HostSeatPump`/`ClientSeatLink` (engine-free) + `MechSeatBehaviour` (Unity), flag `net.seatBridge` default off; host + 2 clients in one process at RTT 0/100 ms, each client's script reaches its own mech in order; Unity EditMode host+client behaviours. dotnet 12/12, Unity filter 14/14. Two-process run not done.
- **T-032 steps A–C**: `Game.AI.Contracts` + `Game.AI.Scripted` (ScriptedKaijuBrain per R-024 §1.4/§1.5), 2D harness, `brain_log@v1`, M-058 (0.21 µs mean per Decide, 0 B alloc). Flag `kaiju.scriptedBrain` false (FeatureFlags/AI.json). dotnet 26/26. M-007 C1 FR_commit 0.18 < 0.60 = R-024 spec finding (FlankTimeout 10 s infeasible).

## Next (in order)
1. ~~**T-023 step 2**~~ DONE (task/T-023, merge 5, `evidence/regression/M1-merge-5.md`): LandmarksLayer, static GGB colliders on `LandmarkStatic`, meshes, geo_export landmarks; EditMode 464/0/2. Remaining: `m005_check.py district` landmarks row (T-035 later step) and M-060. Original item: (Unity, World.Geo): optional `landmarks` layer reader (manifest without it stays valid; validator accepts the new licence id), static colliders per R-020 §3 (4 leg boxes, deck BoxColliders per 40 m segment following `y_navd88_m`, cable capsule chain r 0.46 m), meshes, layer order terrain, water, buildings, landmarks; `geo_export@v1` landmarks array; then `m005_check.py district` landmarks row (T-035 later step) and M-060. Branch from the integration head (task/T-022 is merged).
2. ~~R-024 revision for M-007 C1 + T-033~~ DONE (rev 2, merged; `evidence/regression/T-033.md`): FR_commit 0.18 -> 0.915 (null 0.015), `Tools/Metrics m007`, `validate brain_log`. Open: C5 panel (clips need a game build), Unity EditMode mirror of the brain tests.
3. ~~T-030 step 2 + T-031~~ DONE (task/T-030b): see below. Remaining: T-030 step 3 placeholder body consuming `MechSeatBehaviour.Frame`.
4. ~~T-036 step 3 (M-051), step 4 (M-053/M-054), C# `destruction_dump@v1` exporter~~ DONE (task/T-036b). T-035 step 2: `--pieces` input done; viewpoints/graphics blocked (Q-NEW-h, Q-072); step 3 landmarks row left to T-023 step 2. Still open: FaultRupture/QuakeBegan dump exporter (M-053/M-054 real input), Unity writer of `district_pieces@v1`.
5. ~~Composition root: install Destruction + Seismic + bridge + brain behind flags (T-039 or a small step).~~ DONE (T-039, `Game.Composition`).
6. ~~Evaluators per track~~ DONE (`evaluations/M1-*`, blocking fixed, minors in LEDGER). M1 report: `reports/M1.md`.

## Open worktrees (merged at their current heads, safe to remove): .wt/T-020b, .wt/T-021, .wt/T-022, .wt/T-024b, .wt/T-027, .wt/T-030, .wt/T-032, .wt/T-035, .wt/T-036.

## Hand-over (M1 lead session 3, after all four tracks merged; 2026-11-15)
- Batched EditMode on 89ccb70 (T-030b, T-023 step 2, T-036b, T-033 merged): 468/0/2, Core config unchanged (`evidence/regression/M1-merge-6.md`).
- Not started (session at its context threshold): "Next" item 5 composition root (Destruction + Seismic + bridge + brain behind default-off flags, one Sonnet/medium step) and item 6 combined evaluator per track (Sonnet/low, §13; fix cycles only for blocking defects). Run both in a fresh session; merge lock `/tmp/kaiju-merge.lock`, §13/§14 apply.

## Session 5 (M1 lead, 2026-11-15): walk-independent tracks
Rules for every track: side-session notes `m1-track-rules.md` (worktree `.wt/<TASK>`, merge lock, Unity < 2, §13/§14, hand-over at ~150k).
Running: **T-030c** (net.coop/net.seatBridge flag files, composition wiring of HostSeatPump/MechSeatBehaviour, TEMPORARY kinematic `Units.Placeholder` body + `PlaceholderDamageRecord`, slot release/reconnect, two-process run; owns DEF-M1net-minor) · **T-039b** (M1 slice scene: 15 tiles + GGB, quake + impactor destruction, kinematic kaiju stand-in on ScriptedKaijuBrain, placeholder mech after T-030c merges, smoke test, Mac build, flag `slice.m1`) · **T-033b** (harness damage, M007-3v4 ≥100 seeds vs directcharge@1, C2/C3 verdict; owns DEF-M1ai-minor) · **T-036c** (fault_rupture/rupture dump exporters, Unity `district_pieces@v1` writer, real-input M-053/M-054/M-005 pieces runs; owns DEF-M1ds-minor).
Queued (app cap of 5 concurrent sessions): **M1-cleanup** (full EditMode re-run after the save fix + GameCoreConfig; Ferry tower → 71.6 m and GGB N tower → 229.18 m sourced, re-bake affected tiles, M-005 district still PASS; DEF-M1save-minor, DEF-M1geo-minor) · **T-034** hangar stub (R-025 site, `Hangar` module, `hangar@v1`, M-059 over 3 cycles, flag `hangar.stub`; adapts to PlaceholderDamageRecord after T-030c merges).
Out of M1 scope now: melee and cannon (plans/M1.md "Blocked until T-005's walk gate passes").
After all tracks merge: one combined Sonnet/low evaluator per track (§13), batched EditMode, update reports/M1.md.

## Session 5 hand-over (§16, ~80k tokens), 2026-11-15 ~06:45 UTC
Merged: T-030c (0b37697, eeac0b3, 8b5cc99: flags, TEMPORARY placeholder body, slot release/rejoin, UDP, two-process co-op, hangar adapter + wiring) · T-034 (d8b9810: Crissy Field, hangar@v1, M-059 PASS) · M1-cleanup (09a23ee: EditMode 482/0/2, sourced landmark heights, M-005 district + landmarks row PASS, save Unreadable/.bak) · T-033b (64328d9, 8a95e9e) · T-036c (dbd3911, 831d963: exporters, district_pieces writer, M-050..054 on real Mw 7.2 input, baked fault_id) · T-040 (7474a66: Combat core cannon/melee/impact routing, 47/47, R-027).
Running: T-033b session (R-024 rev 3 applied to fix C1 0.494 < 0.60 in 3v4; one cycle, else R-024 research item) · T-039b session (slice scene; player smoke run in progress) · T-041 Agent worker of session 5 (Opus high, R-028 RL policy design + scaffold; if session 5 is gone, check `task/T-041` and `tasks/T-041.progress.md`).
Open for the successor:
1. When T-039b, T-033b, T-041 report: check `git log`, then one combined Sonnet/low evaluator per track (§13) for T-030c, T-034, T-036c, T-033b, T-039b, T-040, T-041.
2. Batched Unity EditMode + GameCoreConfig on the integration head (covers T-040's and T-034's new asmdefs, which were never run in Unity on the merged tree; watcher logs `evidence/T-040/unity-editmode.log`, `evidence/T-034/editmode.log` were task-branch only).
3. DEF-M1save-6: register `DestructionSaveParticipant` in `Game.Composition`.
4. T-041 implementation steps (Sonnet) from R-028 once the design lands.
5. Update `reports/M1.md` after the evaluators.
6. Archive finished sessions T-030c, T-036c (and later the others) once checked — the archive tool needs the user's go-ahead.
Push note: T-040's worker had an integration push refused by the permission classifier; the branch has since been pushed by another session (in sync at hand-over).
- Post-hand-over results: T-033b rev 3 merged (71903df). M-007 3v4: C2 PASS (CI non-overlap), C3 0.068, C4 0, but **C1 still FAIL 0.494 < 0.60** after the one cycle, so it is routed to R-024 as a research item, threshold untouched. T-036c: M-053 PASS on production faults (provisional, assumed thresholds). T-030c and T-036c are done and can be archived once the user agrees. T-039b: build + smoke running.
- T-041 merged (5f71430): R-028 design (313-float ego observation, masked target × 7 primitives × 3 speeds, shared actor + CTDE, C# 2D harness via headless bridge at ~10k decisions/s, reward with M-007 shaping annealed out, C6 = paired 200 held-out seeds, win share ≥ 0.55 with Wilson lower bound > 0.50; C1–C4 at registered thresholds), Policy encoder/decoder/brain (AI 42/0), Tools/Training smoke PPO deterministic (pytest 6/6). Not run: Unity EditMode for Game.AI.Policy (status file in .wt/T-041/kaiju/evidence/T-041/). Next for a Sonnet track (brief must list the Harness files): O1 harness BrainFactory seam (remove the bridge shim), then steps 2–6 in `tasks/T-041.progress.md`; open O2/O3/O5.
- T-039b merged (de8c4a4): M1 slice scene behind `slice.m1` (15 tiles + GGB, composition-root boot, destruction/seismic, kaiju stand-in on the scripted brain, placeholder mech), macOS dev build + smoke results in `evidence/T-039b/`. All seven session-5 tracks are now merged; the next step is the evaluators + batched EditMode (open list items 1–2).

## Session 6 (M1 lead, 2026-11-15)
Started: **R-024 rev 4 / T-042** session (local_0611b324-8cf7-4fdd-be61-259e1af1b19f, Opus 5.5 high, §14 escalation of M-007 C1 0.494 < 0.60 in m007_3v4; one escalation, threshold untouched) · **T-041b** session (local_c95ad84e-026a-483f-9438-f83801399f89, Sonnet/medium: O1 BrainFactory seam first, then T-041 steps 2–6).
Agent workers: combined §13 evaluators (net+hangar, destruction+slice, AI+combat+policy) writing `evaluations/M1-*/eval-final.md`; DEF-M1save-6 (register DestructionSaveParticipant in Game.Composition, branch task/DEF-M1save-6).
Batched Unity on f516412: CoreConfig + EditMode → `evidence/regression/M1-lead6-batch1.md`: 613 total, 610 pass, 1 fail (race with the T-118 merge of FeatureFlags/Meta.json, not a defect), 2 skip; re-run in the end batch.
Archived: T-030c, T-036c sessions. Archive of T-033b and T-039b sessions refused by the permission classifier: waiting for the founders (not retried).
Two-process co-op: done in T-030c (not open).
DEF-M1save-6 merged f8f4dde (DestructionSaveInstaller in GameComposition.Install; Composition 7/7, Core Save 105/105; Unity-only CompositionTests change not yet compiled → end batch).
Evaluators: M1-destruction-seismic (T-036c) PASS 0 blocking / 4 minor (DEF-M1ds-f1: M-053 assumed thresholds lack a calibration owner); M1-slice (T-039b) FAIL 1 blocking (DEF-M1slice-f1: player captures blank and frozen t=2–19 s, smoke passes on counters only) + 4 minor. Fix cycle T-039c runs as an Agent worker (session start refused on an event turn, §15).
Evaluators: M1-net (T-030c) PASS 0 blocking / 7 minor (incl. TEMPORARY placeholder lacks a LEDGER removal row, M-057 egress test passes by construction); M1-hangar (T-034) PASS 0 blocking / 4 minor (M-059 byte-identical on re-run). Minors → LEDGER in one scribe pass at the end.
Evaluators: M1-ai T-033b 0 blocking / 3 minor (C3 structurally 0: 12 s lockout = 12 s window → forwarded to T-042), T-041 0 / 5 (NaN obs pass-through → forwarded to T-041b); M1-combat T-040 0 / 5 (no Unity EditMode, no installer test). All 7 tracks evaluated; only blocking: DEF-M1slice-f1.
- T-041 Unity EditMode watcher gave up (3 Unity batch processes after 25 min): Game.AI.Policy still needs the batched EditMode run (open item 2). Session 5 stopped on the usage limit.
T-039c merged: DEF-M1slice-f1 fixed (spawn planner puts camera clear of buildings; player captures render and change; smoke now fails on blank/frozen via colour-richness, non-background share and pairwise frame-diff, plus destruction before/after diff 2.1%). Evidence evidence/T-039c/final, smoke pass, Slice.Tests 17/0.
Slice minor defects f2-f5 on LEDGER.md; M1 slice gate ready for re-evaluation (eval-style on final captures).

## Session 7 (build lead, 2026-11-15)
- T-039c ✓ merged 85d9ff7 (spawn planner, strengthened pixel smoke; player run pass, min share 0.506). T-119/T-112 ✓ (eae5dea). T-176 ✓ 6678a16 (C7/C6 haze model, C11 slab model; shader steps T-180..T-185 staged, need Unity + Rendering.Pixel worker). T-042 (R-024 rev 4) running. Open: final batched EditMode (after T-042), reports/M1.md.
Final batch ✓ (9a7908a): GameCoreConfig regen identical, Unity EditMode 675/673/0/2 (evidence/regression/M1-final-batch.md); LEDGER rows slice-f1, combat-f1, ai-f2 closed, ai-f5 part.
reports/M1.md updated with final status; remaining: T-005 walk, slice eval-style re-check, C5/C6, minors batch.
