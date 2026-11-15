# Game.Perf.Bay (T-124, M2 Track 5)

Editor-only batch harness for **M-008@v1** (performance at Bay scale). Not part of the player build; no feature flag; touches no other module's behaviour.

- Entry: `Game.Perf.Bay.M008BayRun.Run` (`unity -batchmode -nographics -projectPath <p> -executeMethod ... -m008Out DIR -m008Tiles <tiles/v1> [-m008Scenario bay|ref|sweep] [-m008Seeds 1,2,3,4,5] [-m008Warmup 60] [-m008Measure 600] [-m008Kaiju N] [-m008Mechs N] [-m008Fragments N] [-m008Spawn m]`).
- Drives, at 60 Hz paced to wall time: the m007_bay `BayHarness` (scripted brains + `PackCoordinator` on the T-102 raster, `mixed` scenario on the real Bay layout; the harness gained an `AfterStep` hook, accessors and `MechCountOverride`, all inert by default), the real `StreamingHostCore` over the baked Bay tiles (L0 via `GeoL0TileReader`, L1..L4 via `GeoLodTileReader`), `UnityTileBuilder` with one `PhysicsScene` per cluster, kinematic **placeholder** boxes for 2+ mechs and 3+ kaiju (T-005 walk and T-117 bodies are not built), a mech cannon raycast at 2 Hz, and scripted structural impacts every 8 s (real `DamageModel` + `DebrisAccounting`, up to 64 dynamic rigid-body fragments per building, 16 spawned per frame, rubble stays).
- Output: `m008_run@v1` JSON per seed (schema in `Tools/Metrics/kaiju_metrics/perf/m008.py`), judged by `Tools/Metrics/metrics.py m008`.
- Frame time here is main-thread **host simulation** ms (no rendering in batch). The rendered-frame clause M-008.b stays NOT_RUN until a rendering run exists; the absolute row M-008.abs is PENDING (target PC, Q-019 / Q-207).
