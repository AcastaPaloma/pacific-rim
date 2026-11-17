# T-106c evidence

| Artifact | Command / producer | Commit |
|---|---|---|
| `m005_bay.json`, `m005_bay_tiles.csv.gz`, `m005_bay_rerun.log` (BEFORE) | `m005_check.py bay --workers 2` on the T-103b bake | f006dee |
| `after/m005_bay.json`, `after/m005_bay_tiles.csv.gz`, `after/m005_bay.log` (AFTER, all 4311 tiles, 1316 s) | `nice -n 10 python -I Pipelines/geo/m005_check.py bay --workers 2 --evid evidence/T-106c/after` | this branch head |
| `seam_scan.py`, `seam_edges_before.json` (29), `seam_edges_after.json` (2) | per-edge scan of terrain.r16 edge rows with the M-005 thresholds (p99 0.2, max 0.5) | same |
| `overlap_scan.py`, `overlap_pairs_before.json` (16 pairs) | edge-band collider pair scan (reproduces the M-005 16 pairs; 0 after) | same |

Before -> after (M-005@v2, bay): pass 15 -> 16, fail 6 -> 5.
- collider_overlap: 16 pairs, 1659 m2 -> 0 pairs (PASS)
- terrain_seam_all_edges: 29 edges, max 7.998 m -> 2 edges (L0_26_44|27_44 4.9 m, L0_33_39|33_40 6.8 m), p99 0.0048
- default_height_share_bay: 0.1038 -> 0.1035 (469 -> 468 tiles over); the cause is unfetched lidar, see tasks/T-106c.impl.md
- bathymetry_bay_wide_shallowness: 0.6095 unchanged (report only)
