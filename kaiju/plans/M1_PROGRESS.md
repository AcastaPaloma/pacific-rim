# M1 progress (orchestrator state; reload from here)

Orchestrator: M1 lead, session 3 (2026-11-15). User offline: decisions are made autonomously and logged in `ledger/HANDOFF.md` ("M1 lead orchestrator, session 3").
Integration branch: `claude/fable-orchestrator-prompt-83a0a8`. Task worktrees: `<worktree root>/.wt/T-0xx`, branches `task/T-0xx` (pushed).
Never merge to `main`. Run every git command as `git -C <integration worktree>` and check `git branch --show-current` first. T-005 (`.wt/T-005`, its QUEUE section) belongs to orchestrator session 5; it also commits QUEUE.md on the integration branch (fine, different files). Unity: at most 1 batch process from M1, only while fewer than 2 run machine-wide. The founders' editor (port 7800) is never touched.
Workers: one Sonnet (medium) per step, code only; the orchestrator re-runs dotnet tests and runs Unity in the background. Hand-written `.meta` files: `grep -rl "fileFormatVersion: 2guid" Assets` before every Unity run.
Integration Unity script: `unity run <kaiju> -- -executeMethod Game.Core.Unity.CoreConfigAsset.CreateOrUpdate`, then `unity test <kaiju> --mode EditMode`.
Geo cache (`Pipelines/geo/cache`, git-ignored, 1.7 GB+): task worktrees get an APFS clone (`cp -cR`); never commit it or a symlink to it.

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
1. **T-023 step 2** (Unity, World.Geo): optional `landmarks` layer reader (manifest without it stays valid; validator accepts the new licence id), static colliders per R-020 §3 (4 leg boxes, deck BoxColliders per 40 m segment following `y_navd88_m`, cable capsule chain r 0.46 m), meshes, layer order terrain, water, buildings, landmarks; `geo_export@v1` landmarks array; then `m005_check.py district` landmarks row (T-035 later step) and M-060. Branch from the integration head (task/T-022 is merged).
2. R-024 revision for M-007 C1 (flank timeout from arc length/speed, pack-assembly gate), then re-run the harness. T-033 (brain_log Python tool, M-007).
3. ~~T-030 step 2 + T-031~~ DONE (task/T-030b): see below. Remaining: T-030 step 3 placeholder body consuming `MechSeatBehaviour.Frame`.
4. T-036 step 3 (M-051, needs R-021 C-3), step 4 (M-053/M-054), C# `destruction_dump@v1` exporter; T-035 steps 2–3.
5. Composition root: install Destruction + Seismic + bridge + brain behind flags (T-039 or a small step).
6. Evaluators per track (eval-arch, eval-physics, eval-perf), Sonnet medium.

## Open worktrees (merged at their current heads, safe to remove): .wt/T-020b, .wt/T-021, .wt/T-022, .wt/T-024b, .wt/T-027, .wt/T-030, .wt/T-032, .wt/T-035, .wt/T-036.
