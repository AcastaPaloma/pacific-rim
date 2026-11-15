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
| `Game.World.Geo.Unity` | `Unity/` | UnityEngine | `GeoTileBuilder`, `GeoTileInstance`, `GeoBuilding`, `GeoMaterials`, `GeoWorldService : IGeoWorld` (multi-tile: tile picked by `GeoTileId.Containing`, global building-owner dictionary), `GeoTileSetBuilder` (staged: one tile per `BuildNext()`, roots at tile origin minus floating origin, `ShiftOrigin` moves all roots; seams are step 3), `GeoModule.Install` / `CreateTileSetBuilder(keys = M0 tile)`, `GeoAttribution` (ScriptableObject) |
| `Game.World.Geo.Editor` | `Editor/` | Editor | `GeoBatch` (batch entry points), `GeoCaptures` |
| `Game.World.Geo.Tests` | `Tests/EditMode/` | test | engine-free NUnit, also run by `dotnet test DotnetTests/WorldGeo.Tests` |
| `Game.World.Geo.Unity.Tests` | `Tests/UnityOnly/` | test | scene build, colliders, removal, origin shift, flag, module rules |

## Contracts (versions in `GeoContractVersions`)
- `geo_tile@v1` (R-004 §3.3) is the only input. `GeoTileContext` refuses a tile whose manifest lacks any required
  field, carries an unaccepted licence, or whose file hashes differ (`GeoTileRejectedException` lists every problem).
  Same rules as `Pipelines/geo/kaiju_geo/manifest.py`.
- `IGeoLayer.Build(IGeoTileContext) -> GeoLayerOutput` (LayerApi 1). Order: terrain, water, buildings, roads, faults.
  **Add a layer:** implement `IGeoLayer`, pass it in the list given to `GeoTileModel`; nothing else changes.
- Faults: `GeoFaultData.FaultId` is the baked `fault_id` string (T-036c; `Id` stays the feature id). `GeoFaultsLoader.LoadGlobal(path)` reads the global `faults_bay.geojson` (manifest hash checked) as the same records, origin 0: the production fault source for World.Seismic, since every baked district tile carries none (empty by design). The tile bake already writes `fault_id` in feature properties, so no re-bake was needed.
- `geo_export@v1`: as-built facts in float64 BayTM (scene position + floating origin): buildings with `bid`, Core id,
  `part_of`, base, top, as-built height, anchor, footprint; terrain samples (physics, render, data surfaces) at given
  control points; statistics; attributions.

## Geometry rules
- Axes: x = BayTM easting, y = NAVD88, z = BayTM northing; 1 unit = 1 m; no vertical exaggeration.
- Tile root at (tile origin - floating origin), always a multiple of 2048 m, so placement and rebases are exact.
  `GeoTileInstance.ShiftOrigin` rebases (origin must be a tile corner).
- Terrain render: RTIN (crack-free right-triangle network) of the 2049^2 surface, split threshold `terrainMaxErrorM`
  = 0.1 m; measured on L0_30_36: max 0.196 m, RMS 0.033 m, 589,457 triangles, 64 chunks of 256 m. Physics: a
  full-resolution `TerrainCollider` (no Terrain renderer). RTIN accumulates per-split error, so the measured deviation
  is about twice the split threshold.
- Buildings: watertight flat-faced prisms (walls, roof, floor), consistent clockwise winding, walls 2 m below base
  (skirt) unless `min_height_m` > 0; pyramidal/spire/cone apex only with `roof:height`; max(y) = base + `height_m`.
  Rendering is combined per 256 m chunk (draw calls); every building also has its own GameObject with `GeoBuilding`
  and a `MeshCollider`. `RemoveBuilding(BuildingId)` removes one building (a structure takes its parts) and rebuilds
  only the touched chunk: no re-bake.
- Roads: records plus a ground decal in the terrain albedo (no coplanar geometry, so no flicker). Bridges and tunnels
  are flagged and not drawn at M0 (deck heights are not in the data).
- Water: flat mesh at MSL (+0.969 m NAVD88) over water cells only (piers and diked land stay dry).

Consumes events: none.   Emits events: `GeoBuildingRemoved` (Core bus, same step, when the `IGeoWorld` was given a bus).
Config assets: `Assets/Game/Config/WorldGeo.json` (single source of truth: RTIN error, chunk sizes, skirt, albedo ramps, road minimum width, palette, smoothness; the importer refuses to build if a key or the file is missing).
Feature flags: `world.geo.enabled` in `Assets/Game/Config/FeatureFlags/WorldGeo.json` (default off). `GeoModule.Install(registry, flags)` registers `IGeoWorld` only when it is on. **Wiring status (M0):** no game composition root calls `GeoModule.Install` yet (only tests do); the Editor batch tools build tiles explicitly and ignore the flag.
Tests: EditMode `Tests/EditMode` (also `dotnet test DotnetTests/WorldGeo.Tests`), Unity-only `Tests/UnityOnly`, Metrics M-005@v1 via `Pipelines/geo/m005_check.py`, budgets M-008 (proposed sub-metrics M-013/M-014) via `GeoBatch.All` render statistics.
Owner role: implementation/geo (`impl-geo`).
Attribution asset: `Assets/Game/World/Geo/Attribution/GeoAttribution.asset` (every distinct manifest attribution, written by `GeoBatch.All`; credits/splash must show it; `m005_check.py` diffs it against the manifests).

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
removal through `IGeoWorld`, 20 random removals with every other export record and the terrain identical, asmdef
references. Fix cycle 2 (engine-free): config keys required, importer refuses missing settings, underground/ship
records not extruded, fallback part heights never above their structure, no building floats. Metrics: `M-005@v1` via `Pipelines/geo/m005_check.py`.


## Known limits / TODO (with Q links)
- Streaming, LOD tiers beyond L0 and multi-tile seams in Unity are not built (F-042 later). r16 rows of neighbouring tiles differ by up to one quantisation step (per-tile range); float32 master rows are identical.
- Bridge decks (Bay Bridge) and tunnels are flagged, not drawn (F-041; proposed Q-NEW-b of T-003).
- Non-surface features (`location=underground` stations, `layer<0` without above-ground levels, ships) are records with `excluded`, never extruded.
- Pier edges are stair-stepped at 1 m (heightfield). Roof shapes other than apex roofs are flat.
- Height default share above 10 % under the R-004 §1.4 rule (proposed Q-NEW-a of T-003; research question).
- Target PC undefined (Q-019): budgets and timings are Apple-silicon editor measurements.
- Player builds were not made; StreamingAssets paths are used in the editor only so far.
