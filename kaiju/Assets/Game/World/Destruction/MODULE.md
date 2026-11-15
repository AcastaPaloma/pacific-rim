# World.Destruction
Purpose: persistent per-building damage. Cumulative delivered energy (impact) or a ground-shaking draw moves a building
through five Hazus-style states, monotone, deterministic per `BuildingId`; every state change publishes `BuildingDamaged`
and fixes the debris mass and an energy account that the M-052 tool checks.

Breakdown IDs: F-043 (stable building IDs, persistent damage), R-021 (capacity, fragility, debris, energy), M-051/M-052 (checks), plans/M1.md Track 2. Task: T-024.
Layer: L2. References `Game.Core.Contracts`, `Game.World.Geo.Contracts`, `Game.World.Seismic.Contracts` (events and `IShakingField` for the bridge only, never the Seismic runtime) and its own assemblies; the Editor entry additionally references `Game.World.Geo` (engine-free loader).

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.World.Destruction.Contracts` | `Contracts/` | none | `IDamageModel`, `DamageState`, `DamageCause`, event `BuildingDamaged`, `BuildingDamageState`, `BuildingAttributes`, `DamageChange`, `DebrisAccount`, `DestructionFlags`, `DestructionContractVersions` |
| `Game.World.Destruction` | `Runtime/` | none | `DamageModel`, `CapacityModel`, `ShakingFragility`, `DebrisAccounting`, `DamageParameters` (every constant, tagged S9/S1/S3/Assumed), `BuildingAttributesFromGeo`, `DestructionModule`, `SeismicShakingBridge`, `DestructionSaveParticipant`, `DestructionDumpExporter` (`destruction_dump@v1`, `fragility_dump@v1`), `DistrictPiecesExporter` (`district_pieces@v1`) |
| `Game.World.Destruction.Editor` | `Editor/` | Editor | `DistrictPiecesBatch.Write` (`-executeMethod`; loads the district tiles with the engine-free `BuildingsLayer` and writes `district_pieces@v1`) |
| `Game.World.Destruction.Tests` | `Tests/EditMode/` | test | engine-free NUnit, also run by `dotnet test DotnetTests/WorldDestruction.Tests` |

## Contracts (versions in `DestructionContractVersions`: DamageApi 1, ModelRevision 2)
- `IDamageModel`: `Register(id, attributes)`, `TryGetState`, `Apply(id, energyJ, cause, source, step)`, `TryGetThresholds`, `TryGetDebrisAccount`. States 0..4, never lowered. Unknown building: `Known=false`, nothing applied.
- `BuildingDamaged` (published once per state change, same step, only when the model was given a bus): BuildingId, State, EnergyJ, PrevState, Cause, Source, DebrisKg (increment).
- Shaking route: `DamageModel.ApplyShaking(id, pgaG, eventIndex >= 1, step)` (PGA in g; PGA -> Sa -> Sd -> drift; `ShakingFragility.Exceedance` is what M-051 compares).
- Debris: `f_d(k) * M0` is cumulative, so any path to a state gives the same mass and energy account.
- Geo adapter: `BuildingAttributesFromGeo.From(GeoBuildingData)`: height = `HeightM`; footprint = outer rings minus holes of `Polygons` (else `AreaM2`, else 0); `Levels` = 0 and `Residential` = false because Geo carries neither (model then uses round(H / 4.22 m), min 1). Pure, no state.

Consumes events: none (the optional `SeismicShakingBridge` subscribes to `FaultRupture` from World.Seismic.Contracts, see below).   Emits events: `BuildingDamaged` (Core bus).
Feature flags: `destruction.enabled` in `Assets/Game/Config/FeatureFlags/WorldDestruction.json` (default off). `DestructionModule.Install(registry, flags)` registers `IDamageModel` only when it is on (and uses `IEventBus` from the registry if present). **Wiring status (M1):** no game composition root calls `Install` yet (only tests do).
Dependencies: Core contracts (`BuildingId`, `EntityId`, `IEvent`, `IEventBus`, `IServiceRegistry`, `IFeatureFlags`), Geo contracts (`GeoBuildingData`).
Tests: `Tests/EditMode` (also `dotnet test DotnetTests/WorldDestruction.Tests`, 86 tests): capacity ordering and scatter, monotone states, shaking frequencies, debris mass conservation, energy bounds, flag gate, Geo adapter.
Seismic bridge (T-027): `SeismicShakingBridge.TryCreate(flags, bus, model, IShakingField, vs30At, step)` returns null unless both `destruction.enabled` and `seismic.enabled` are on; on each `FaultRupture` EventId (once) it calls `DamageModel.ApplyShaking` with PGA from `IShakingField` for each building given to `AddBuilding(id, x, z)`, ordinal `BuildingId` order. Depends only on `Game.World.Seismic.Contracts`; S-wave delay not applied (deliberate, see Known limits).
Owner role: implementation/destruction.

## Known limits / TODO
- Capacity rows are MC (code-era-independent) only until `year_built` is in the data (proposed Q-NEW-b); code-era rows are not built.
- The `E_frac` cap (25 % of the potential energy released) is Assumed: a house's fall at about 34 J/kg cannot pay 500 J/kg. M-052 should treat a residual in [0, 5 %] of dPE as the check.
- Geo has no levels or use fields, so every building takes the documented defaults (levels from height, not residential; light wood frame is never chosen from Geo data).
- Save: `DestructionSaveParticipant` owns section `destruction` at version 1 (records keyed by `BuildingId`, sparse, `_meta.model_rev` header; unloaded ids are kept pending and re-applied at `Register`; `Read` validates all records first). Nobody registers it yet: no composition root exists, so the future root must create `DestructionSaveParticipant(damageModel)` and `SaveSystem.Register` it. Shipped version stays 1; a v2 bump would add an `ISaveSectionMigration` (Core's hook). The path is proven by a test-only v2 (extra defaulted field) over the committed golden `Tests/EditMode/Golden/destruction_v1.json` (regenerate only with `KAIJU_WRITE_GOLDEN=1`). 20,000 ids: save about 40 ms, load about 90 ms, 2.1 MB.
- No composition root calls `DestructionModule.Install` yet.
- Metric dumps (T-036/T-036c): `DestructionDumpExporter` writes `destruction_dump@v1` (M-052) and `fragility_dump@v1` (M-051); `DistrictPiecesExporter.Build(tiles)` + `ToJson` write `district_pieces@v1` for `Pipelines/geo/m005_check.py district --pieces` (one piece per building and district tile square; Sutherland-Hodgman per ring, holes subtracted). Real-input run and numbers: `evidence/T-036c/README.md`. Unity: `-executeMethod Game.World.Destruction.Editor.DistrictPiecesBatch.Write [-piecesOut f] [-piecesTiles L0_26_36,...]`.
- Seismic damage lands at `FaultRupture` publication. The S-wave arrival delay (R-022 §4) is not applied on purpose: M1 has no scheduler or clock that could delay a `BuildingDamaged`, damage values do not depend on arrival time, and a step stamp without a delayed event would only look like timing. Revisit with the first composition root that owns simulation time (a delayed `ApplyShaking` per building, arrival = hypocentral distance / 3500 m/s).
- Several constants are Assumed (beta_D, plateau and corner period, first event index); see `DamageParameters`.
