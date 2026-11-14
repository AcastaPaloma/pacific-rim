# World.Geo
Purpose: import baked `geo_tile@v1` tiles (Pipelines/geo) into Unity at 1:1 scale in BayTM@v1 metres: one continuous
terrain + seabed surface, individually addressable buildings with stable IDs, roads, faults and a static sea-level
water surface; export as-built facts for `M-005@v1`.

Breakdown IDs: F-040, F-042 (tile-local float32 + float64 tile origin, origin shift), F-043 (stable building IDs,
removal by ID), D-010, D-014 (fault layer), D-021, M-005@v1, M-008 (budgets), MODULARITY §4.6. Task: T-003.
Layer: L2. References only `Game.Core.Contracts` and its own assemblies (checked by `ModuleRules_AsmdefsReferenceOnlyCoreContractsAndOwnAssemblies`).

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.World.Geo.Contracts` | `Contracts/` | none | `IGeoLayer`, `IGeoTileContext`, `IGeoHeightField`, `GeoLayerOutput`, `GeoMeshData`, `GeoBuildingData`, `GeoRoadData`, `GeoFaultData`, `GeoTileId`, `IGeoWorld`, `GeoBuildingInfo`, event `GeoBuildingRemoved`, `GeoFlags`, `GeoContractVersions`, `GeoTileRejectedException` |
| `Game.World.Geo` | `Runtime/` | none | `GeoJson`, `GeoManifestValidator`, `GeoTileContext` (+ r16 / PNG decode), `RtinMesher`, `Triangulator`, `BuildingExtruder`, layers `TerrainLayer`, `WaterLayer`, `BuildingsLayer`, `RoadsLayer`, `FaultsLayer`, `GeoTileModel` |
| `Game.World.Geo.Unity` | `Unity/` | UnityEngine | `GeoTileBuilder`, `GeoTileInstance`, `GeoBuilding`, `GeoMaterials`, `GeoWorldService : IGeoWorld`, `GeoModule.Install`, `GeoAttribution` (ScriptableObject) |
| `Game.World.Geo.Editor` | `Editor/` | Editor | `GeoBatch` (batch entry points), `GeoCaptures` |
| `Game.World.Geo.Tests` | `Tests/EditMode/` | test | engine-free NUnit, also run by `dotnet test DotnetTests/WorldGeo.Tests` |
| `Game.World.Geo.Unity.Tests` | `Tests/UnityOnly/` | test | scene build, colliders, removal, origin shift, flag, module rules |

## Contracts (versions in `GeoContractVersions`)
- `geo_tile@v1` (R-004 §3.3) is the only input. `GeoTileContext` refuses a tile whose manifest lacks any required
  field, carries an unaccepted licence, or whose file hashes differ (`GeoTileRejectedException` lists every problem).
  Same rules as `Pipelines/geo/kaiju_geo/manifest.py`.
- `IGeoLayer.Build(IGeoTileContext) -> GeoLayerOutput` (LayerApi 1). Order: terrain, water, buildings, roads, faults.
  **Add a layer:** implement `IGeoLayer`, pass it in the list given to `GeoTileModel`; nothing else changes.
- `geo_export@v1`: as-built facts in float64 BayTM (scene position + floating origin): buildings with `bid`, Core id,
  `part_of`, base, top, as-built height, anchor, footprint; terrain samples (physics, render, data surfaces) at given
  control points; statistics; attributions.

## Geometry rules
- Axes: x = BayTM easting, y = NAVD88, z = BayTM northing; 1 unit = 1 m; no vertical exaggeration.
- Tile root at (tile origin - floating origin), always a multiple of 2048 m, so placement and rebases are exact.
  `GeoTileInstance.ShiftOrigin` rebases (origin must be a tile corner).
- Terrain render: RTIN (crack-free right-triangle network) of the 2049^2 surface, split threshold `terrainMaxErrorM`
  = 0.1 m; measured on L0_30_36: max 0.196 m, RMS 0.033 m, 589,457 triangles, 64 chunks of 256 m. Physics: a
  full-resolution `TerrainCollider` (no Terrain renderer).
- Buildings: watertight flat-faced prisms (walls, roof, floor), consistent clockwise winding, walls 2 m below base
  (skirt) unless `min_height_m` > 0; pyramidal/spire/cone apex only with `roof:height`; max(y) = base + `height_m`.
  Rendering is combined per 256 m chunk (draw calls); every building also has its own GameObject with `GeoBuilding`
  and a `MeshCollider`. `RemoveBuilding(BuildingId)` removes one building (a structure takes its parts) and rebuilds
  only the touched chunk: no re-bake.
- Roads: records plus a ground decal in the terrain albedo (no coplanar geometry, so no flicker). Bridges and tunnels
  are flagged and not drawn at M0 (deck heights are not in the data).
- Water: flat mesh at MSL (+0.969 m NAVD88) over water cells only (piers and diked land stay dry).

Consumes events: none. Emits: `GeoBuildingRemoved` (Core bus, same step, when `IGeoWorld` has a bus).
Config: `Assets/Game/Config/WorldGeo.json` (RTIN error, chunk sizes, skirt). Feature flag file
`Assets/Game/Config/FeatureFlags/WorldGeo.json`: `world.geo.enabled` (default off). `GeoModule.Install(registry, flags)`
registers `IGeoWorld` only when it is on. Editor batch tools build tiles regardless (they are explicit commands).
Attribution: `Assets/Game/World/Geo/Attribution/GeoAttribution.asset` (every distinct manifest attribution, written by
`GeoBatch.All`; credits/splash must show it; `m005_check.py` diffs it against the manifests).

## Commands
- Build, export, origin and removal checks, attribution asset, captures (one launch, about 15 s):
  `unity run <project> --no-tail --log-file <log> -- -executeMethod Game.World.Geo.Editor.GeoBatch.All [-geoTile L0_30_36] [-geoOut <dir>]`
- Tests: `unity test <project> --mode EditMode -o <xml>`; `dotnet test DotnetTests/WorldGeo.Tests`.

## Tests
EditMode (shared, engine-free): manifest completeness, ten doctored manifests refused, tampered file refused by
sha256, attribution strings, tile id and bid parsing, RTIN error/crack-free/upward, triangulation with holes,
extrusion watertight/outward/volume, pyramid roof, full tile import counts, Salesforce tallest, removal by bid.
Unity-only: flag file default off, module install gated by flag, builder refuses a doctored manifest, addressable
buildings with colliders and <= 200 renderers, physics surface vs data, floating-origin shift <= 1 mm, runtime
removal through `IGeoWorld`, asmdef references. Metrics: `M-005@v1` via `Pipelines/geo/m005_check.py`.

Owner role: implementation/geo (`impl-geo`).

## Known limits / TODO
- Streaming, LOD tiers beyond L0 and multi-tile seams in Unity are not built (F-042 later). r16 seams between tiles
  differ by up to one quantisation step (per-tile h_min/h_max); the float32 master rows are identical.
- Bridge decks (Bay Bridge) and tunnels are flagged, not drawn: F-041 hand-crafted landmarks.
- Pier edges are stair-stepped at 1 m (heightfield). Roof shapes other than apex roofs are flat.
- Height default share 13.3 % > 10 % under the R-004 rule (proposed Q in `tasks/T-003.impl.md`).
- Player builds were not made; StreamingAssets paths are used in the editor only so far.
