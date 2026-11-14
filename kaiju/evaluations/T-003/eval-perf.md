PASS

Scope: criterion C20 (budgets, Assumed) and the C14 removability constraint. Artifacts only: `evidence/T-003/EVIDENCE.md`, `tile_stats.json`, the committed tile folder, and `Assets/Game/World/Geo/{Runtime/RtinMesher.cs,Unity/GeoTileInstance.cs,Editor/GeoBatch.cs}` on `task/T-003`.
Machine: Apple-silicon MacBook Pro (Unity batch mode). No target PC is defined yet; every budget is Assumed (C20), so this PASS is conditional on the founders' later spec.

| Budget | Reported | Verified how | Verdict |
|---|---|---|---|
| Triangles <= 1.5 M | 685,685 (terrain 589,457; buildings 87,924; water 8,304) | `tile_stats.json` budgets/terrain_lod; parts sum to the total (589,457+87,924+8,304). Counted from the built meshes (`inst.TriangleCount`, GeoBatch.cs:86/94), not a GPU counter. Roads: no geometry (decals), so 0 by design | PASS (46 % of budget) |
| Terrain LOD error stated | RTIN setting 0.1 m; max 0.196 m, RMS 0.033 m | `terrain_lod` note: error is rendered mesh vs the 1 m surface over all 2049^2 nodes; RtinMesher.cs takes maxError and tracks Max/Rms. Measured over every node, not sampled. Max (0.196) is about 2x the setting (0.1) and is not explained | PASS (see DEF-1) |
| Draw calls <= 1,500, target 200 | 118 (max over views; 55 to 118 across views) | `tile_stats.json` views: this is a count of visible renderers (one call each), an estimate by renderer count, not the stats API or frame debugger. Buildings are combined per spatial chunk (`CombineChunk`, GeoTileInstance.cs:52), so 2,423 buildings render in about 100 calls | PASS as estimate (see DEF-1, DEF-2) |
| Memory | 71.2 MB | GeoBatch.cs:156-159: sum of `Profiler.GetRuntimeMemorySizeLong` over unique meshes, mesh colliders, albedo and terrainData. Excludes shadow/GPU resources, materials, and the 2,423 per-building GameObjects | PASS, partial (DEF-3) |
| Committed data <= 100 MB | 23.53 MB | `git lfs ls-files -s`: `terrain.r16` 8.4 MB + `terrain.tif` 12 MB (LFS); `du -sh` of the tile folder 22 MB, `Geo/` 24 MB, byte sum 25.4 MB incl. global faults. Consistent with 23.5 MB | PASS |
| Bake <= 5 min | 13.97 s | `tile_stats.json` times.bake_s; `logs/final_run.log` cited by EVIDENCE.md. Not re-run (not allowed to run Unity; bake not rerun) | PASS (as reported) |
| Editor build <= 60 s | 2.63 s (import 2.09 s) | `tile_stats.json` times; GeoBatch.cs logs the stopwatch (line 86) | PASS (as reported) |
| Extrapolation to M1 and the Bay | M1 (70 tiles) 1.65 GB, 16.3 min bake; Bay 4,624 tiles 108.8 GB at 1 m, 17.9 h bake | See below | PASS (stated, arithmetic checks) |

Extrapolation check. Committed 23.53 MB x 70 = 1.65 GB; x 4,624 = 108.8 GB; bake 13.97 s x 70 = 16.3 min; x 4,624 = 17.9 h. All match. Triangles in view: 0.69 M per tile; a streamed ring of about 9 tiles (the note's figure) at full detail would be about 6 M triangles and about 1,000 renderers if every tile were as dense as this one. That would exceed the 1.5 M triangle figure and the 200-call target, but only if LOD is not reduced with distance. The tile is dense downtown SF; most Bay tiles are far lighter. Memory at 9 tiles about 640 MB by the same ratio. 109 GB of Bay data is not shippable; the Q-027 default (1 m only for urban and shoreline tiles, 2 m elsewhere) is the stated mitigation and is not built here.

Findings for later tasks (not T-003 failures):
- One GameObject plus collider per building (2,423 here; about 0.4 M buildings Bay-wide) will not scale; pool or lazily create building objects per streamed tile.
- Terrain LOD is a single level per tile. Streaming needs distance LODs, or the 9-tile ring blows the triangle budget.
- Removing a building rebuilds its whole chunk mesh (17.5 ms for 20 removals here). Fine now; with destruction at 60 fps in a dense chunk, measure again.
- A single material per renderer means no instancing or atlas pressure now; real materials (T-004) will raise draw calls and the shadow pass.

Defects (none blocks C20):
- DEF-T003-perf-1 (minor): draw calls are an estimate (renderer count), not measured with the stats API or frame debugger; the shadow pass, depth prepass and any camera or reflection pass are not counted, so real calls can be 2 to 3x 118 (still far under 1,500; the 200 target is at risk). Also, all views report the same 118 for four views, so the count does not model culling. PASS would look like: `UnityStats.drawCalls` or a frame-debugger capture from a play-mode camera at the oblique views, listed beside the estimate. Same for the RTIN max error being about 2x its setting: state why (error is measured to the 1 m source, setting applies to the RTIN threshold).
- DEF-T003-perf-2 (minor): the "individually removable" requirement (C14) is met by a per-building GameObject plus collider while the render path is chunked; the 118 figure holds with every building removable because render is combined and the chunk is rebuilt on removal (GeoTileInstance.cs:111-126). It is not "every building its own renderer". PASS would look like: a sentence in EVIDENCE.md saying so, plus the chunk rebuild cost per removal.
- DEF-T003-perf-3 (minor): memory 71.2 MB covers meshes, colliders, albedo and terrain data only (GeoBatch.cs:156-159), not GPU-resident or per-GameObject overhead, and no frame time was measured at all. PASS would look like: Profiler or `GetTotalAllocatedMemory` before and after load, and a frame time on the stated machine.
- DEF-T003-perf-4 (minor): bake and build times are single runs on an Apple-silicon MacBook Pro; no target PC exists, so no number is evidence about the intended hardware. Re-measure when the founders define it (propose Q for target PC; default stays Assumed).

Counts: blocker 0, major 0, minor 4.
