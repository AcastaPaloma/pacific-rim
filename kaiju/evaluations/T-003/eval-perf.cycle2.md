PASS

Re-check of C20 at `task/T-003` 192012b. Artifacts: `evidence/T-003/EVIDENCE.md` (fix cycle 2), `tile_stats.json`, `export/render_stats_playmode.json`, R-006 §4. Apple-silicon dev Mac; no target PC (Q-019); budgets still Assumed.

| Budget | Reported | Verified how | Verdict |
|---|---|---|---|
| Triangles <= 1.5 M (tile) | 684,625 (coordinator message) | `tile_stats.json` has 671,523 (terrain 577,265 + buildings 87,040 + water 7,218; sums correctly). The 684,625 is not in any artifact I read | PASS; number mismatch is DEF-5 |
| Triangles submitted per frame | 1.37 M all passes | `render_stats_playmode.json` Triangles Count max 1,343,052 (obliques), 1,187,937 (V4); the 1.37 M is not in the file | PASS (under 1.5 M, 10 % margin only in the worst view) |
| Draw calls <= 1,500 | 242 max | ProfilerRecorder, Play mode, Metal, 1920x1080, 30 warm-up + 120 frames per view, max and median recorded (file protocol line): matches R-006 §4 on device, resolution, frames and counter names | PASS |
| Draw calls target 200 | 242 (obliques), 206 (V4), 134 (V5) | same file; 21 % over target in obliques, in 2 of 5 views | Over target: finding DEF-6, named mitigation below |
| Terrain LOD error | max 0.196 m, RMS 0.033 m | unchanged; 2x setting now explained in MODULE.md (accumulated split error) per EVIDENCE | PASS |
| Memory | tile objects 70.8 MB (tile_stats; EVIDENCE says 71.1); process deltas native +91.5, reserved +269, graphics +16.8, managed +94.7 MB | EVIDENCE row perf-3; `tile_stats_unity.json` not re-opened | PASS |
| Committed <= 100 MB | 23.74 MB | `du -sh` tile folder 23 M; `git lfs ls-files -s`: r16 8.4 + tif 12 MB | PASS |
| Bake <= 5 min / build <= 60 s | 15.63 s / 2.65 s | `tile_stats.json` | PASS (as reported) |

Measurement method vs R-006 §4: graphics device on, Play mode, 1920x1080, warm-up and 120 frames, ProfilerRecorder, max reported: followed. Gaps: Batches Count reads 0 in every view and Shadow Casters Count reads 0 in the obliques (main-light shadow should have casters; V4 shows 12), so the counters are partly unreliable; no per-pass Frame Debugger breakdown (R-006 asks for one per URP-asset change); frame time is an editor-batch figure (0.6 to 0.8 ms), not a player figure.

Extrapolation (updated): committed 23.74 MB x 70 = 1.66 GB; x 4,624 = 109.8 GB. Bake 15.63 s x 70 = 18.2 min; x 4,624 = 20.1 h. Arithmetic checks. In view: draw calls scale with chunks; a 9-tile ring at this density would be about 2,000 draw calls (over 1,500) and about 12 M submitted triangles unless distance LOD and chunk culling cut them. Not scalable: one GameObject plus collider per building (2,423 here; hundreds of thousands Bay-wide), single terrain LOD per tile, 109.8 GB at 1 m everywhere (Q-027 default of 2 m outside urban tiles is not built). Findings for later tasks, not T-003 failures.

Cycle-1 defect status
- DEF-T003-perf-1 closed: draw calls now measured with profiler counters per R-006 §4. Residual gaps (Batches 0, shadow counter 0, no pass breakdown) go into DEF-6.
- DEF-T003-perf-2 closed: chunked render with per-building objects stated; 0.51 ms per removed building (10.2 ms for 20) in `geo_export_after_removal.json` per EVIDENCE.
- DEF-T003-perf-3 closed: process-level memory deltas and frame time reported; frame time is editor-only, a limitation noted.
- DEF-T003-perf-4 correctly routed: target PC is Q-019 for the founders; stays open there, Assumed.

New defects
- DEF-T003-perf-5 (minor): reported tile triangles 684,625 and per-frame 1.37 M do not match `tile_stats.json` (671,523) or `render_stats_playmode.json` (1,343,052 max). PASS would look like: one set of numbers, from the same commit as the artifacts.
- DEF-T003-perf-6 (minor, finding): measured draw calls 242 exceed the 200 target (gate 1,500 met), in 3 of 5 views. Mitigation (named): merge building chunks per material or per 512 m, and cull chunks beyond range; re-measure at the oblique views; add a Frame Debugger per-pass list and fix or explain the zero Batches and Shadow Casters counters. Real materials from T-004 will add calls, so re-measure then.

Counts: closed 3, routed 1 (perf-4, founders), new 2 (both minor). Blocker 0, major 0.
