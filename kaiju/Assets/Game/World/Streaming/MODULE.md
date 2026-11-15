# World.Streaming
Purpose: decide which tiles and which physics chunks of the 1:1 Bay are resident, and which cluster origin every body and camera lives in. Engine-free and deterministic: the scheduler consumes foci and the host's reports and emits a plan; it does no IO, owns no clock and sees no Unity type. The Unity loader (T-109), the persistence journal (T-110) and the flythrough harness (T-111) build on it.

Breakdown IDs: F-065 (proposal, streamed Bay), F-042, F-043, P3, P4, P1; metrics M-015@v2 (logic part: `Tests/EditMode/BayTraversalTests.cs`), M-101 (host side, T-109/T-111), M-102 (LOD error, T-108/T-111). Task: T-107. All numbers: `research/R-101.md`.
Layer: L2. References only `Game.Core.Contracts`, `Game.World.Geo.Contracts` and its own assemblies.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.World.Streaming.Contracts` | `Contracts/` | none | `FocusSample`, `IStreamingFocus`, `ResidencyKey`, `TileState`, events `TileResident` / `TileEvicted` / `ClusterOriginChanged`, `StreamingBudget`, `RingSpec`, `StreamingPlan`, `TileRequest`, `ITileCostModel`, `IStreamingScheduler`, `ClusterAssignment`, `ClusterInfo`, `StreamingFlags`, `StreamingContractVersions` |
| `Game.World.Streaming` | `Runtime/` | none | `StreamingScheduler`, `ClusterAssigner`, `StreamingConfig`, `DefaultTileCostModel` |
| `Game.World.Streaming.Host` | `Host/` | none | `StreamingHostCore` (runs the plan: worker-thread reads, main-thread build slice, eviction, render-origin rebase on the anchor cluster, hold queries), `ITileReader`, `ITileBuilder`, `IBuildSink`, `IWorkRunner`, `GeoL0TileReader` (L0 + 256 m physics chunks from `geo_tile@v1`), `LevelRoutingReader` (per level; geo_lod@v1 reader plugs in here) |
| `Game.World.Streaming.Unity` | `Unity/` | UnityEngine | `UnityTileBuilder` (meshes at tile origin minus render origin, `MeshCollider`s baked on a worker via `Physics.BakeMesh`, exact-integer `ShiftOrigin` + one `Physics.SyncTransforms`), `StreamingHost` (MonoBehaviour driver), `StreamingModule.Install` (null unless `world.streaming`) |
| `Game.World.Streaming.Tests` | `Tests/EditMode/` | test | engine-free NUnit, also run by `dotnet test DotnetTests/WorldStreaming.Tests` |

## How a host drives it
Each tick: fill `FocusSample[]` (every camera, mech, kaiju; debris too if it should be clustered), `scheduler.Tick(dt, foci, plan)`, then execute `plan.Load` (highest priority first; `Urgent` = physics chunk, may preempt the main-thread slice) and `plan.Evict`, shift cluster roots from `plan.Clusters` (`Rebased`), and place bodies by `plan.Assignments` (`LocalX/Y/Z`, float32, origin y = 0). Report back: `Advance(key, Reading|Building)` (optional), `Completed(key, gpuBytes, cpuBytes)` (fires `TileResident`), `Released(key)` after an eviction (frees the bytes), `Failed(key, reason)`. `TileEvicted` fires when the eviction is decided, so the host flushes the tile's destruction journal (F-043) before it releases meshes. A body whose `PhysicsRingResident(id)` is false is held kinematic by the host.

## Rules implemented (R-101 section 8.1)
1. Quadtree: an L(n) tile has four L(n-1) children (`GeoTileId` indexing); levels 0..4 (`StreamingConfig.MaxLevel`).
2. Render selection per focus, top-down: a tile refines while its output-pixel error `err_rms[n] x (H_out / (2 tan(fov/2))) / d` exceeds tau = `TauFactor` / k (k = the focus' integer scale, default 4; `TauFactor` is clamped to at most 1). d = distance to the closest point of the tile, minus the prefetch lead (speed x 4 s). Refine at `d_n x 1.0`, coarsen again only beyond `d_n x 1.15` (remembered per tile). Parent fallback: a wanted leaf that is not resident keeps its nearest resident ancestor alive (never evicted); with no resident ancestor the L4 cover is requested first.
3. Rings: render ring of the top level = clamp(V, 15 km, 60 km) per camera; level n < top is limited to its `RingSpec` radius (1.5, 3, 6, 12 km), raised to the next level's switch distance x 1.15 so a ring never leaves a hole. Physics ring: chunks of 256 m (8 x 8 per L0 tile) within 512 m of every body with `NeedsPhysics`; evicted beyond 768 m.
4. Priority `P = (1 + min(8, sse / tau)) / (t_reach + 0.25)`, `sse` = error of the parent level (what is seen if the tile is missing), `t_reach = max(0, d - R_ring) / max(v_closing, 1)`; physics chunks get P x 1000. Ties break on `ResidencyKey` order (kind, level, i, j, chunk).
5. Hysteresis: a resident key stays wanted until `radius x 1.25` (render) / 768 m (physics); a resident key that is not wanted is released once it is beyond that radius from every focus.
6. Budget (`StreamingBudget`: 1.5 GB GPU, 350 MB CPU, 4 reads in flight, 600 physics chunks) is never exceeded by what the scheduler requests: the estimate is reserved at request time; a request that does not fit waits (lower-priority requests of the same kind do not jump it) while unwanted residents are evicted (lowest priority, then least recently needed). If the host reports more bytes than the estimate and the total passes the budget, the scheduler evicts unwanted keys down to 90 %. Wanted keys are never shed. Bytes leave the budget at `Released`.
7. Failures: `Failed` retries after `0.5 s x 2^(n-1)` (max 30 s) and parks after 5 failures (`IsParked`).
8. Flag `world.streaming` (default off, `Assets/Game/Config/FeatureFlags/Streaming.json`). Nothing installs the scheduler yet (T-109); with the flag off M1 `GeoTileSetBuilder` is unchanged.

## Activity clusters (O6)
Foci link when their Chebyshev distance is below 3072 m and the merged AABB extent stays at most 5120 m (greedy, focus-id order, so deterministic). A cluster's origin is the tile corner (multiple of 2048 m) nearest its AABB centre: a member is at most extent/2 + 1024 = 3584 m from it, inside the 4096 m bound with 512 m to spare. A cluster keeps its origin until a member passes 3072 m on a quiet step (`StreamingScheduler.QuietStep`) or 3840 m on any step, and only rebases if that lowers the largest local coordinate (no thrash at 3584 m). Cluster ids persist across ticks by member overlap. `ClusterOriginChanged` goes on the bus on every rebase.

## Decisions (T-107, logged in ledger/HANDOFF.md)
- D1: `ResidencyKey` (tile + kind + chunk) replaces R-101's `GeoTileId`-only keys so physics chunks are addressable; events carry `Tile`, `Kind`, `Chunk`, `Generation`.
- D2: bodies drive the render pyramid only with `FocusSample.NeedsRender` (cameras always): an AI kaiju 40 km from every camera needs its physics ring, not a 800 MB visual ring.
- D3: eviction is two-step (`TileEvicted` at decision, `Released` when the host is done) so the budget reflects real memory.
- D4: rings per level cannot create holes (raised to the next switch distance); `RenderRings` radii are defaults to be calibrated with the real `geo_lod@v1` stats.

## Known limits
- The scheduler cannot cancel an in-flight request that became unwanted; it completes and is then evicted by retention.
- The cost model and error ladder (`DefaultTileCostModel`: R-101 table, 0.1 x 2^n m) are defaults until T-108 measures `geo_lod@v1`.
- Distance uses the tile's closest point in the horizontal plane plus the focus' altitude above NAVD88 0; terrain relief and the 1.2 m building tops are ignored (conservative at LOD scale, to be refined with `geo_lod@v1` AABBs).
- `MainThreadSliceMs` is carried for the Unity loader; the scheduler does no main-thread work itself.

## Unity loader (T-109)
- Flow per tick: `scheduler.Tick` -> evictions (`builder.Release`, `Released`) -> origin (anchor = cluster of the lowest-id camera, else lowest focus id; a changed cluster origin is one `ShiftOrigin`, counted in `RebaseCount`, first placement is not a rebase) -> dispatch `plan.Load` (urgent physics first, then priority; at most 4 render and 4 physics reads in flight) -> `Pump` builds finished payloads in a `MainThreadSliceMs` (2 ms) slice; urgent payloads ignore the slice. Physics chunks report `Built` only after their collider bake finished on a worker, so `PhysicsRingResident` stays false (body held) until the chunk really collides.
- Payload meshes are tile-local; every node (render or physics chunk) sits at the tile corner minus the render origin.
- Reader: L0 render and chunks use `GeoL0TileReader` (cache of 3 tile models so the 64 chunks of a tile build the model once). Levels 1..4 have no default reader until T-108's `geo_lod@v1` format is merged: register it with `LevelRoutingReader.Register(level, reader)`. A level without a reader fails (scheduler backoff and park; parent fallback keeps coverage).
- Not installed anywhere: nothing in Composition creates a `StreamingHost`; callers use `StreamingModule.Install(flags, configure)` (inert with `world.streaming` off).
- Known limits: one render origin (anchor cluster); bodies of other clusters are not given their own `PhysicsScene` yet (T-111 host harness); the destruction journal flush on `TileEvicted` is T-110; rebase cost (M-101) is measured by `UnityTileBuilder.LastShiftMs` but not yet gated.
