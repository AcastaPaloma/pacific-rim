# World.Infrastructure
Purpose: the F-044 targets of the Bay (bridges, power towers/lines/substations/plants, port cranes, airport runways/terminals/hangars/control towers) as an engine-free registry the AI pack, the kaiju brains and Emergence can query, with damage stored by World.Destruction's `IDamageModel`. Task T-105 (M2 Track 1). Layer L2.

Breakdown IDs: F-044, F-022 (infrastructure destroyed as a reward term), F-020 (Emergence targets), F-043 (persistent ids), Q-NEW-c (OSM only). Data: `Pipelines/geo/bake_infra.py` (`infra@v1`).

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.World.Infrastructure.Contracts` | `Contracts/` | none | `InfraKind` (byte = `kind_id` of `config/infra.json`), `InfraSource`, `InfraId` (+ `ToBuildingId` / `TryFromBuildingId`), `InfraKinds` masks, `InfraRecord`, `InfraDamageChange`, `IInfrastructureRegistry`, `InfrastructureFlags`, `InfrastructureContractVersions` (RegistryApi 1, ModelRevision 1). References Core.Contracts and World.Destruction.Contracts only |
| `Game.World.Infrastructure` | `Runtime/` | none | `InfraRegistry` (records + 512 m grid, tile add/remove, radius and nearest queries, damage forwarding), `InfraTileParser` (reads the sidecar, checks sha256 and count against its manifest), `InfraGeometry`, `InfraDamageProfile` (Assumed proxies), `InfrastructureModule.Install` |
| tests | `DotnetTests/WorldInfrastructure.Tests` | dotnet | 17 NUnit tests (ids, queries, damage through the real `DamageModel` and bus, tile parser, module flag; one optional test parses a real baked tile when `KAIJU_INFRA_TILE_DIR` is set) |

## The baked layer `infra@v1`
- Per L0 tile that owns features: `infrastructure.geojson` (FeatureCollection in BayTM, `spec` `infra@v1`, features sorted by id, mm coordinates, OSM names dropped) and `infrastructure.manifest.json` (sha256, count, kind counts, input fingerprint, OSM source entry, excluded classes). It is a **sidecar**: the tile's `geo_tile@v1` manifest is not edited, so no L0 tile and no LOD tile is invalidated (lod.py fingerprints the L0 manifests). Tiles without features get no files. The slot "after landmarks" of the layer order is therefore a sidecar read by this module, not a `geo_tile@v1` manifest layer; `IGeoLayer` is not used because `GeoLayerOutput` has no shape for it.
- Id: feature `properties.osm` is `w<way id>` or `n<node id>`; `kind_id` as `InfraKind`. `InfraId` = (kind, source, 52-bit id), string `inf:bridge:osm-way:123`. A bridge is one OSM way (a long bridge is several ids, `landmark` carries the F-041 slug for the T-104 ones).
- Kinds (selectors in `Pipelines/geo/config/infra.json`): bridge (way `bridge=*` + `highway`/`railway`, footways kept with `footway=1`), power_tower (node), power_line (way `line`/`minor_line`), substation, power_plant, port_crane (`man_made=crane`), runway, terminal, hangar, control_tower. Excluded: poles, generators, taxiways, aerodrome areas, relations.
- Owner rule: a record belongs to the tile containing its node, the arc-length midpoint of its line, or an interior point of its area; geometry is not clipped.
- Values are tags or geometry measurements only (`length_m`, `area_m2`, `lanes`, `width_m`, `height_m`, `voltage_v`), `null` when OSM has none. Nothing is invented in the bake.
- Bay result (2026-11-15 snapshot, `evidence/T-105/`): 30,936 features in 2,189 tiles of the cache bake (bridges 12,854, towers 9,862, lines 6,743, substations 529, plants 203, cranes 80, runways 68, terminals 22, hangars 575, control towers 0); 614 further features are owned by 81 tiles that are not baked (outside the world box). Counts agree with R-100 section 5 (12,855 / 9,862 / 3,436 + 3,310 / 529 / 203 / 67 nodes + ways / 67 / 22 / 575).
- Run: `KAIJU_GEO_CACHE=<integration cache> /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/bake_infra.py` (offline, about 4 min for the PBF pass, cached in `<cache>/infra/infra_osm_v1.json` keyed by the input fingerprint; a re-run is a no-op: 2,189 tiles unchanged). Run it again after any tile is first baked (`bake_bay.py`), the sidecars are separate files.

## Damage hookup
`InfraRegistry.AddTile` registers every record with `IDamageModel.Register(BuildingId, BuildingAttributes)` under `InfraId.ToBuildingId()` (new `BuildingSource.Infrastructure = 4`, token `infra`, packed (kind 8 bits, source 4 bits, id 52 bits) in the 64-bit source id). `Apply` forwards to `IDamageModel.Apply`, so the five states None..Complete, cumulative energy, saves (`DestructionSaveParticipant`) and the `BuildingDamaged` event work unchanged; a consumer maps the event back with `InfraId.TryFromBuildingId`. Unloading a tile drops the record, never the damage; a reload re-registers and the damage model keeps (or restores) the state. `IsOperational` is false at `Complete` (collapsed bridge, destroyed crane): the five-state bridge target of the deferred R-135 work builds on the same states.

## Assumed (bump `ModelRevision` on change)
`IDamageModel` takes only `BuildingAttributes`, so each target is described as an equivalent structure by height and footprint (`InfraDamageProfile`): defaults bridge 12 m high with length x (width or lanes x 3.5 m, else 7 m), tower 40 m / 100 m2, line 15 m x length x 3 m, substation 8 m, plant 20 m, crane 50 m / 400 m2, runway 1 m x length x 45 m, terminal 15 m, hangar 20 m, control tower 40 m / 150 m2; tagged heights and measured lengths and areas replace the defaults. These give capacity energies from the R-021 building model, not an infrastructure fragility curve (HAZUS bridge and utility curves are not read yet).

## Not done / open
- Infrastructure-specific fragility (bridge classes, substation and tower curves, cranes) and per-span power line damage; the capacity here is the building model.
- Heights of towers and cranes (lidar, T-104 measured the Port of Oakland cranes) and bridge deck heights are not in the layer; `HeightM` is the OSM tag or NaN.
- Liquefaction start state (R-103 4.5 step 4) needs the susceptibility layer.
- Consumers: an `IEmergenceTargets` adapter (cluster centres from `QueryRadius`), the AI.Pack `IInfrastructureSource` adapter and the Unity loader (reads the sidecar next to `manifest.json`) are not wired here.
- The 15 committed district tiles in `Assets/StreamingAssets` carry no sidecar (the bake wrote the cache tiles only).
- Feature flag file `Config/FeatureFlags/WorldInfrastructure.json` (`infrastructure.enabled`, default off) is new: `GameCoreConfig.asset` needs the batched regeneration.
