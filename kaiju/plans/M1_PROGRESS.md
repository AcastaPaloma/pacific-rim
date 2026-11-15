# M1 progress (orchestrator state; reload from here)

Orchestrator: M1 lead, session 2 (2026-11-15). User offline: decisions are made autonomously and logged in `ledger/HANDOFF.md` ("M1 lead orchestrator, session 2").
Integration branch: `claude/fable-orchestrator-prompt-83a0a8`. Task worktrees: `<worktree root>/.wt/T-0xx`, branches `task/T-0xx` (pushed).
Never merge to `main`. Run every git command as `git -C <integration worktree>` (an earlier `cd ..` merged into the founders' local main; reverted, see HANDOFF). T-005 (`.wt/T-005`, its QUEUE section) belongs to orchestrator session 4's successor. Unity: at most 1 batch process from M1, only while fewer than 2 run machine-wide. The founders' editor (port 7800) is never touched.
Workers: one Sonnet (medium) per step. After each worker the orchestrator re-runs its tests; Unity runs are started by the orchestrator in the background. Hand-written `.meta` files must have `fileFormatVersion: 2` and `guid:` on separate lines (one worker merged them; `grep -rl "fileFormatVersion: 2guid" Assets` before every Unity run).
Integration Unity script: `unity run <kaiju> -- -executeMethod Game.Core.Unity.CoreConfigAsset.CreateOrUpdate`, then `unity test <kaiju> --mode EditMode`.

## Merged into the integration branch (regressions in `evidence/regression/M1-merge-{1,2,3}.md`; last EditMode 436/0/2)
- T-020 district bake (14 tiles) and **T-020b** default-height fill (3DEP lidar median nDSM, then Assessor stories × levels height; only formerly-default buildings change). M-005@v1.2 default share 0.0992 → 0.0218.
- T-024 damage model (4 steps) + **T-024b** Hazus code era from `year_built` (pre-1941 PC, 1941–75 MC, post-1975 HC per ShakeCast App. D; all 12 rows from Hazus fragility.csv), destruction `model_rev` 2.
- **T-025** save section `destruction@v1` + test-only v2 migration, stream failure, 20k ids (save 39 ms, load 88 ms).
- **T-029** Net core (4 steps): contracts, loopback, snapshots, intents, `net_log@v1`, two-peer `NetSession`. M-055 p95 43/67/93 ms at RTT 50/100/150 (limits 59/84/109); M-057 per client 14.5 kB/s down, 10.9 up at N=4 (limits 64/16).
- **T-027** World.Seismic (4 steps): Wells & Coppersmith scaling; ruptures on mapped fault **systems** (sections chained along strike, gaps ≤ 5 km bridged without offset; San Andreas 189 km, 141 mapped); BSSA14 PGA/PGV/MMI (R-022 table within 3 %); `IShakingField`; `SeismicShakingBridge` in Destruction; flag `seismic.enabled` (off). No composition root calls Install yet.
- **T-021** steps 1–3: engine-free tile set, multi-tile Unity adapter (staged `BuildNext`), one edge-height rule for render and collider (0.005 m lattice, corners included). Worst seam 4.5 mm.
- **T-035** step 1: M-005@v1.2 district checks (`m005_check.py district`). **T-036** step 2: M-050, M-052 tools.

## Next (in order)
1. **Marin default heights** (M-005@v1.2 still FAIL, 0.0218): L0_25_38, L0_26_38, L0_27_38 have no usable CA_SanFrancisco_B23 returns over land; find a 3DEP EPT project covering Marin (Sausalito/Marin Headlands) and add it to `fetch_lidar_dsm.py`; re-run `Pipelines/geo/scripts/rebake_district.sh`. L0_27_37 (0.17) and L0_26_36 (0.06): check why the fill misses (coverage < 70 % rule?).
2. T-021 step 4: M-015 floating origin over a GG→SoMa traversal (> 4096 m) and M-018 tile render budget (needs graphics; Mac relative only).
3. T-036 step 3 (M-051, needs R-021 C-3) and step 4 (M-053/M-054 Python from seismic output); the C# `destruction_dump@v1` exporter (fields in `Tools/Metrics/.../m052.py` docstring).
4. T-035 steps 2–3 (count/perimeter shares, `calibrate` support), then T-022 landmarks → T-023 bridge (R-020 §2–§3).
5. Composition root: install Destruction + Seismic + bridge behind flags (T-039 slice scene or a small step).
6. T-030 intent replication (needs Q-100 seat contracts, landed 395e7f9) and T-032 brain (pure C#); T-037 tools (M-055–M-058 from `net_log@v1`).
7. Evaluators per track once implementation is done (eval-arch, eval-physics, eval-perf), Sonnet medium.

## Open worktrees (safe to remove once merged): .wt/T-020b, .wt/T-021, .wt/T-024b, .wt/T-027, .wt/T-035, .wt/T-036 (all merged at their current heads).
