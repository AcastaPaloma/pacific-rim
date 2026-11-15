# M1 merge 5 regression (T-023 step 2, 2026-11-15)
Integration branch `claude/fable-orchestrator-prompt-83a0a8`: merged `task/T-023` (f7b2741 engine-free layer, 8b83289 Unity side) as 9d7dc5b on top of 7a0da55 (already holding the T-030b merge). The merge was clean, with no conflicts. The three untracked Unity-generated `landmarks.geojson.meta` files in the integration checkout were byte-identical to the committed ones; they were removed before the merge.

**What landed**
- `Runtime/GeoLandmarks.cs`: `LandmarksLayer` is optional, so a manifest without `landmarks` builds with 0 landmarks.
  - Per tower leg: one oriented Box from `base_navd88_m` to `top_navd88_m`.
  - Deck: N = ceil(L/40) Boxes. Each box top follows `y_navd88_m`, width 18.9 m, depth 3.0 m (assumed).
  - Each cable: N = ceil(L/40) Capsules, r = diameter_m/2 = 0.462 m.
  - Simple render meshes.
- `GeoLayerOutput.Landmarks`, `GeoLandmarkData`, `GeoColliderData`, `GeoColliderShape` (additive).
- `DefaultLayers` order: terrain, water, buildings, landmarks, roads, faults.
- `Unity/GeoTileInstance.cs`:
  - A `Landmarks` child holding BoxCollider and CapsuleCollider children, all `isStatic` and on layer `LandmarkStatic` (new, user layer 8 in TagManager).
  - No Rigidbody, and nothing is in the BuildingId index, so there is no RemoveBuilding path.
  - `geo_export@v1` has a new `landmarks` array (lid, bid, kind, piece, base, top, height, anchor, footprint, colliders).
- Collider counts:
  - L0_26_37: 2 tower boxes + 36 deck boxes + 72 capsules.
  - L0_26_38: 2 tower boxes + 14 deck boxes + 28 capsules.
  - Continuity error at the z = 77824 cut: 0 (engine-free) and below 1e-3 m (Unity world space).

| Suite | Result | Log |
|---|---|---|
| Unity EditMode (merged integration) | 466 total, 464 passed, 0 failed, 2 skipped | `M1-merge-5.editmode.xml`, `.log` |
| Unity EditMode (task/T-023 before merge) | 458 / 456 / 0 / 2 | `evidence/T-023/step2_editmode.xml` |
| dotnet WorldGeo | 70/70 (63 old + 7 new) | — |
| pytest Pipelines/geo | 114 passed | — |
| M-005@v1.2 district | PASS, union default share 0.01443; landmarks row `not_evaluated` (T-035 later step) | `M1-merge-5.m005_district.txt` |

- `GameCoreConfig.asset` was not regenerated: no FeatureFlags or Config file changed since merge 4 (9ae42a2).
- Other dotnet suites were not re-run (§13: only the touched module). T-023 touches only World.Geo and TagManager.
- One fix cycle: the engine-free test referenced the dotnet-only `TileSetData`, which broke Unity compilation. It was inlined.
