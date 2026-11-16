# T-124d evidence
- `bench_tick_ms.csv`: `dotnet test DotnetTests/WorldStreaming.Tests --filter StreamingSchedulerBenchTests` with `KAIJU_T124D_OUT=...` (commit: task/T-124d). Tick ms of `StreamingScheduler.Tick` alone, Bay-like foci (camera, 2 mechs, 3 kaiju, anchor; prefetch 768 m; DefaultTileCostModel), 7500 ticks after 500 warm-up, full recompute vs `ReplanMoveM = 3`.
- `dotnet_tests.log`: WorldStreaming.Tests 48/0 (39 before + 9 new), WorldStreaming.Host.Tests 35 pass / 2 skip / 0 fail.
- Not run: Unity batch (machine load average 16 at start; a 3-minute M008 run would only measure the load). The Bay `tickstages=` spikes were not reproduced.
