# World.Emergence
Purpose: where and when the kaiju rise (F-020, R-103 4.5, Q-NEW-b). A World.Seismic `FaultRupture` selects 1..N emergence sites on the ruptured, mapped part of the fault (preferring 5-20 km from an F-044 infrastructure cluster), each site raises a cluster of 1-5 kaiju of mixed species within 4 km along the rupture and 120 s. Pack formation itself is the pack layer's job (R-103 4.5 step 2), not this module's. Task T-114. Layer L2.

Breakdown IDs: F-020, F-044, D-026, Q-204. Check: M-007@v2 clause F1 (`m007_bay_emerge`).

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.World.Emergence.Contracts` | `Contracts/` | none | events `EmergenceSiteSelected`, `KaijuEmerged` (carries the allocated `EntityId`), `EmergenceSequenceDone`; `EmergenceTrace` (arrays), `IEmergenceTraceSource`, `IEmergenceTargets`/`EmergenceTarget`, `EmergenceSpecies` (1 Strider, 2 Breacher, 3 Bastion = AI.Pack `SpeciesRole.SpeciesId`), `EmergenceFlags`, `EmergenceContractVersions` (EventApi 1, ModelRevision 1) |
| `Game.World.Emergence` | `Runtime/` | none | `EmergencePlanner` (pure, deterministic), `EmergenceOptions` (all Assumed constants), `EmergenceDirector` (bus handler of `QuakeBegan` + `FaultRupture`), `EmergenceModule.Install` |
| tests | `DotnetTests/WorldEmergence.Tests` | dotnet | 13 NUnit tests (planner properties; bus path with the real `QuakeDirector`) |

## Flow
`QuakeDirector.Run` flushes `QuakeBegan`, `FaultRupture`, `EmergencePoint`. The director reads Mw and Seed from `QuakeBegan`, the vertex range and `EndS` from `FaultRupture`, and publishes (nested, so before Seismic's own `EmergencePoint`): per site `EmergenceSiteSelected`, then its `KaijuEmerged` events in time order, finally `EmergenceSequenceDone`. Once per EventId. Seismic's single `EmergencePoint` stays unchanged and is the M1 path (it equals the district-nearest site).
- Site candidates: vertices inside the ruptured range that touch no bridged segment. Weighted draw (weight = score^2; in-band 0.5..1, out-of-band decays) with a minimum spacing (3 km) between sites; fewer sites than requested when the range cannot fit them (`SitesRequested` vs `Sites`).
- Cluster: size 1..KMax (KMax from Mw: round(2 + 2 (Mw - 6)), 1..5), each kaiju at an arc offset within +-4 km of the site on the same mapped run (never across a bridge, clamped to the ruptured range), at least 250 m apart where possible; first kaiju at the site time (rupture end + 15 s, the Seismic constants), the rest uniformly inside +120 s.
- Species per kaiju from the Mw-keyed mix table (larger event, larger species). Heading = toward the nearest infrastructure cluster.
- Deterministic for (EventId, Seed, trace, targets, options); no wall clock.

## Feature flag
`emergence.enabled` in `Config/FeatureFlags/WorldEmergence.json` (default off). `EmergenceModule.Install(registry, flags, traces, targets?, options?)` returns null and subscribes nothing when off or when `IEventBus` / `EntityIdAllocator` are missing. Composition: `EmergenceInstaller` (needs `seismic.enabled`; adapts `FaultTraceSet` via `SeismicTraceSource`), targets from `GameCompositionInputs.EmergenceTargets`.

## Assumed (bump `ModelRevision` on change)
Band 5-20 km (R-103 Q-NEW-b), spacing 3 km, spread 4 km and window 120 s (R-103 4.5), KMax rule, kaiju spacing 250 m, mix table rows (Mw < 6.5: 0.70/0.25/0.05, < 7.0: 0.50/0.35/0.15, < 7.5: 0.35/0.40/0.25, else 0.20/0.40/0.40). Every species has a role fit >= 0.6 (Flanker/Anchor), so every cluster of 2-5 is a valid pack by the R-103 4.5 composition rule.

## Open
- Formation (auto-form within call range, singleton wait 180 s, fission-fusion) is not here: it belongs with the pack layer's host and the `m007_bay_emerge` harness (tracked, T-115 step 3).
- Liquefaction start state of infrastructure (R-103 4.5 step 4) needs the Track 1 susceptibility data; tracked.
- Hosts spawn the bodies from `KaijuEmerged`; no body, brain or pack is created here.
