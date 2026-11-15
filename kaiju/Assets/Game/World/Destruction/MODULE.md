# World.Destruction
Purpose: persistent per-building damage. Cumulative delivered energy (impact) or a ground-shaking draw moves a building
through five Hazus-style states, monotone, deterministic per `BuildingId`; every state change publishes `BuildingDamaged`
and fixes the debris mass and an energy account that the M-052 tool checks.

Breakdown IDs: F-043 (stable building IDs, persistent damage), R-021 (capacity, fragility, debris, energy), M-051/M-052 (checks), plans/M1.md Track 2. Task: T-024.
Layer: L2. References only `Game.Core.Contracts`, `Game.World.Geo.Contracts` and its own assemblies.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.World.Destruction.Contracts` | `Contracts/` | none | `IDamageModel`, `DamageState`, `DamageCause`, event `BuildingDamaged`, `BuildingDamageState`, `BuildingAttributes`, `DamageChange`, `DebrisAccount`, `DestructionFlags`, `DestructionContractVersions` |
| `Game.World.Destruction` | `Runtime/` | none | `DamageModel`, `CapacityModel`, `ShakingFragility`, `DebrisAccounting`, `DamageParameters` (every constant, tagged S9/S1/S3/Assumed), `BuildingAttributesFromGeo`, `DestructionModule` |
| `Game.World.Destruction.Tests` | `Tests/EditMode/` | test | engine-free NUnit, also run by `dotnet test DotnetTests/WorldDestruction.Tests` |

## Contracts (versions in `DestructionContractVersions`: DamageApi 1, ModelRevision 1)
- `IDamageModel`: `Register(id, attributes)`, `TryGetState`, `Apply(id, energyJ, cause, source, step)`, `TryGetThresholds`, `TryGetDebrisAccount`. States 0..4, never lowered. Unknown building: `Known=false`, nothing applied.
- `BuildingDamaged` (published once per state change, same step, only when the model was given a bus): BuildingId, State, EnergyJ, PrevState, Cause, Source, DebrisKg (increment).
- Shaking route: `DamageModel.ApplyShaking(id, pgaG, eventIndex >= 1, step)` (PGA in g; PGA -> Sa -> Sd -> drift; `ShakingFragility.Exceedance` is what M-051 compares).
- Debris: `f_d(k) * M0` is cumulative, so any path to a state gives the same mass and energy account.
- Geo adapter: `BuildingAttributesFromGeo.From(GeoBuildingData)`: height = `HeightM`; footprint = outer rings minus holes of `Polygons` (else `AreaM2`, else 0); `Levels` = 0 and `Residential` = false because Geo carries neither (model then uses round(H / 4.22 m), min 1). Pure, no state.

Consumes events: none.   Emits events: `BuildingDamaged` (Core bus).
Feature flags: `destruction.enabled` in `Assets/Game/Config/FeatureFlags/WorldDestruction.json` (default off). `DestructionModule.Install(registry, flags)` registers `IDamageModel` only when it is on (and uses `IEventBus` from the registry if present). **Wiring status (M1):** no game composition root calls `Install` yet (only tests do).
Dependencies: Core contracts (`BuildingId`, `EntityId`, `IEvent`, `IEventBus`, `IServiceRegistry`, `IFeatureFlags`), Geo contracts (`GeoBuildingData`).
Tests: `Tests/EditMode` (also `dotnet test DotnetTests/WorldDestruction.Tests`, 39 tests): capacity ordering and scatter, monotone states, shaking frequencies, debris mass conservation, energy bounds, flag gate, Geo adapter.
Owner role: implementation/destruction.

## Known limits / TODO
- Capacity rows are MC (code-era-independent) only until `year_built` is in the data (proposed Q-NEW-b); code-era rows are not built.
- The `E_frac` cap (25 % of the potential energy released) is Assumed: a house's fall at about 34 J/kg cannot pay 500 J/kg. M-052 should treat a residual in [0, 5 %] of dPE as the check.
- Geo has no levels or use fields, so every building takes the documented defaults (levels from height, not residential; light wood frame is never chosen from Geo data).
- Save: `DestructionSaveParticipant` owns section `destruction` at version 1 (records keyed by `BuildingId`, sparse, `_meta.model_rev` header; unloaded ids are kept pending and re-applied at `Register`; `Read` validates all records first). Nobody registers it yet: no composition root exists, so the future root must create `DestructionSaveParticipant(damageModel)` and `SaveSystem.Register` it. Shipped version stays 1; a v2 bump would add an `ISaveSectionMigration` (Core's hook). The path is proven by a test-only v2 (extra defaulted field) over the committed golden `Tests/EditMode/Golden/destruction_v1.json` (regenerate only with `KAIJU_WRITE_GOLDEN=1`). 20,000 ids: save about 40 ms, load about 90 ms, 2.1 MB.
- No composition root calls `DestructionModule.Install` yet. Unity compile and EditMode run are not done (meta files hand-written).
- Several constants are Assumed (beta_D, plateau and corner period, first event index); see `DamageParameters`.
