# World.Seismic
Purpose: a scripted quake on a mapped fault. Scenario (Mw, fault, hypocentre) -> rupture on the dissolved Qfaults trace (WC94 size, tapered slip) -> BSSA14 median shaking field -> events `QuakeBegan`, `FaultRupture`, `EmergencePoint` on the Core bus, in that order, once per EventId.

Breakdown IDs: F-020, D-014, Q-030 (no fictional trace), R-022 (all numbers), M-053/M-054 (checks). Task: T-027.
Layer: L2. References only `Game.Core.Contracts`, `Game.World.Geo.Contracts` and its own assemblies. Task numbers and sources: `research/R-022.md`.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.World.Seismic.Contracts` | `Contracts/` | none | `QuakeScenario`, events `QuakeBegan` / `FaultRupture` / `EmergencePoint`, `SlipSense`, `IShakingField`, `ShakingSample`, `SeismicFlags`, `SeismicContractVersions` (EventApi 1, ModelRevision 1) |
| `Game.World.Seismic` | `Runtime/` | none | `FaultTraceSet` (dissolve, chain, bridge), `RuptureScaling` (WC94 Table 2A), `RuptureSolver`, `QuakeSequence`, `Bssa14`, `ShakingField`, `Vs30Provider`, `SeismicAssumptions`, `SeismicModule`, `QuakeDirector`, `CoreBusSeismicSink`, `SeismicDumpExporter` (`fault_rupture_dump@v1` for M-053, `rupture_dump@v1` for M-054) |
| `Game.World.Seismic.Tests` | `Tests/EditMode/` | test | engine-free NUnit, also run by `dotnet test DotnetTests/WorldSeismic.Tests` (plus dotnet-only real-fault tests in `DotnetTests/WorldSeismic.Tests/Dotnet/`); references Destruction assemblies for the integration test only |

## Contracts and flow
- `QuakeDirector.Run(scenario)`: finds the fault system by `FaultId`, solves the rupture, adds it to `ShakingField`, then flushes the three events to the bus in order (one burst; the field already holds the rupture when `FaultRupture` is delivered). Repeat of an EventId or unknown FaultId: returns null, publishes nothing.
- `IShakingField.TryGetShaking(eventId, x, z, vs30, out ShakingSample{PgaG, PgvCmS, Mmi, RjbM})`: median BSSA14 strike-slip, Rjb to the non-bridged ruptured segments. Consumers (Destruction) see only this interface.
- Rupture rules: system = all sections of one `fault_name`, chained; unmapped gaps up to 5 km are bridged (zero slip, never emergence). If the system is shorter than SRL, Mw is reduced, the trace is never stretched. Emergence = ruptured vertex nearest the target district reference, time = rupture end + 15 s.
- Events consumed: none. Emitted: `QuakeBegan`, `FaultRupture`, `EmergencePoint`.

## Metric dumps (T-036c)
`SeismicDumpExporter.BuildFaultRuptureDump(results, faultsPath, faultsSha256)` and `BuildRuptureDump(results)` turn the `RuptureResult`s that `QuakeDirector.Run` returns into the Python tools' input (schemas in the docstrings of `Tools/Metrics/kaiju_metrics/destruction/m053.py` and `m054.py`). A rupture is exported as *runs*: maximal vertex ranges joined by baked piece segments inside one section (a bridge, a join chord or a section change ends a run); `fault_id` is the baked string kept on `FaultSystem.SectionBakedIds` (pieces built from `GeoFaultData` carry none: `GeoFaultData.Id` is the feature id, so the Geo contract would need a `fault_id` field; the dotnet loader `RealFaults` reads it from the geojson). Applied slip is `RuptureResult.Slip`; expected slip is regenerated from the event fields (float MdM/AdM, seed). Dotnet round trip into the real tools: `PythonRoundTripTests`; real run: `DistrictQuakeRun` (`KAIJU_DUMP_DIR`).
`FaultSystem.Chord[i]`: segment i joins two baked pieces across a digitisation gap (at most 50 m) and is on no piece. It is ruptured and shaken like any segment (the gap is far below the map-scale uncertainty of 25 to 137 m) but it is not mapped trace; M-053 measured 20.5 m off the trace on such chords before the dump left them out.
`RuptureResult.SegmentSlip(i)`: slip of a segment, 0 on bridges (DEF-M1ds-4: `Slip[]` is per vertex and keeps the profile value at a mapped chain end next to a bridge; that is the trace end, not a bug; use `SegmentSlip` or `MeanSlipM` for offset on the ground).

## Feature flag
`seismic.enabled` in `Assets/Game/Config/FeatureFlags/WorldSeismic.json` (default off). `SeismicModule.Install(registry, flags, faults, districts, vs30?)` returns null and registers nothing when off; when on it registers `QuakeDirector`, `IShakingField`, `Vs30Provider` (uses `IEventBus` from the registry if present). Damage from shaking additionally needs `destruction.enabled`: the Destruction-side `SeismicShakingBridge` is created only when both are on. **Wiring status:** no game composition root calls `Install` yet (only tests do); the root also must build the `FaultTraceSet` from baked faults (`FaultTraceSet.PiecesFromGeo`, about 0.1 s) and supply `DistrictRefs`.

## Dependencies
Core contracts (`IEvent`, `IEventBus`, `IServiceRegistry`, `IFeatureFlags`), Geo contracts (`GeoFaultData`). Nothing else. Destruction depends on `Game.World.Seismic.Contracts` (bridge), not the reverse.

## Assumed constants (not sourced; change = bump `ModelRevision`)
Vs30 rock 760 / fill 270 m/s; default Mw 7.2; hypocentre depth 8 km; rupture speed 2800 m/s; timing offsets (shaking after rupture 10 s, emergence after shaking 5 s); `MaxBridgeGapM` 5 km; slip noise +-15 %; delta z1 = 0 (basin depth omitted); district references are points, not boundaries; BSSA14 coefficients are a 3 s.f. transcription (paper table not read).

## Checks (shapes in `SeismicModuleTests`)
- M-053 shape: max distance of ruptured vertices to the mapped trace is 0 (vertices are polyline vertices); emergence point is on a mapped (non-bridged) vertex; FaultId resolves to a source system. The 10 m-sampled shapely check against `faults_bay.geojson` and manifest hash is the tool's job.
- M-054 shape: dM_L, dAD, dMD against WC94 sigma (0.28, 0.28, 0.34): pass within 1 sigma, warn to 2, fail beyond; default scenario passes, HayWired (Mw 7.0, 84 km) warns, Mw 7.2 on 5 km fails.

## Open questions
- Q-NEW-a: dissolved trace length, section names, `linetype`/`mapped_scale` of the chosen section vs R-004 distances (section names and lengths now known from the baked data; linetype and mapped_scale not checked).
- Q-NEW-c: Vs30 grid; fill polygons need real BayTM outlines (provider is rock everywhere until fed).
- Q-NEW-e: timing constants Assumed.
- (Also open: Q-NEW-d, sigma not modelled, median only.)

## Known limits
- Shaking is applied at `FaultRupture` publication; the S-wave arrival delay (R-022 section 4) is not applied by the bridge, deliberately (no clock or scheduler exists in M1; see the Destruction MODULE.md).
- `EmergencePoint.HeightM` is 0 (no CUDEM lookup). No aftershocks, liquefaction, tsunami, directivity or basin effects (Q-105).
- Branch faults beyond the bridge limit are dropped (San Andreas: 276 km of branches).
- Dotnet-only tests (`DotnetTests/WorldSeismic.Tests/Dotnet/`: real faults, real district tiles, Python round trip) reference World.Geo for loading tiles; the shared EditMode sources do not.
