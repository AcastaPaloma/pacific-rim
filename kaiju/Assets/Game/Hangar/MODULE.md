# Hangar (stub)
Purpose: D-017 / F-060 minimum. Between fights the body goes to a real Bay site, damage and repair state are carried over exactly, and the body spawns there again. Staging only: refit, upgrades, economy and the hangar scene stay on the plan (D-017); this module is the data path they will extend.

Breakdown IDs: D-017, F-060, M-059@v1. Research: `research/R-025.md` (site, M-059 draft). Task: T-034. Layer: L3. References only `Game.Core.Contracts`, `Game.World.Geo.Contracts` and its own assemblies.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Hangar.Contracts` | `Contracts/` | none | `IHangarService`, `IBodyDamageRecord` (the narrow damage input), `PartDamage`, `PartRepairState`, `HangarSite`, `HangarSpawn`, `HangarPhase`, `HangarFlags`, `HangarContractVersions` |
| `Game.Hangar` | `Runtime/` | none | `HangarService` (service + `ISaveParticipant`), `HangarSites` (Crissy Field), `HangarModule.Install` |
| tests | `DotnetTests/Hangar.Tests` | test | NUnit, dotnet only: lifecycle, save, corruption, spawn, M-059 |

## Site (R-025, Q-104)
Crissy Field airfield, Presidio, tile `L0_27_36` (Golden Gate district, no extra bake). Open-air pad on the airfield; BayTM@v1 (56821.48, 75637.64), heading 90 deg. Position is the *Assumed* airfield centre (+-300 m); confirm against the baked footprints when `L0_27_36` is baked (R-025 Q-NEW-b). The spawn is a `World.Geo` anchor: when `HangarSite.AnchorBuilding` is set and loaded, `IGeoWorld.TryGetBuilding` anchor plus offset is used; otherwise the site position. Height is `IGeoWorld.SampleTerrain`; `HangarSpawn.TerrainKnown` is false (Y = NaN) while the tile is not loaded.

## Contract
- Flow: `EnterHangar(record)` (fight over; every part copied) -> `QueueRepair` / `QueueAllDamaged` -> `ProcessRepairs` (instant, free: integrity = max, `Repaired` = true) -> `LeaveHangar(record)` (parts written into the record, cycle + 1, spawn returned). Misuse throws (`InvalidOperationException` outside the hangar, `ArgumentException` for unknown parts or invalid damage).
- A part hit since the last visit loses `Repaired`; an untouched part keeps its repair state and queue place; queued-but-unprocessed repairs are carried.
- `IBodyDamageRecord` is the only coupling to the body: `CopyParts` lists every part, `ApplyParts` replaces the record's part state. The adapter from `PlaceholderDamageRecord` (`Units.Placeholder`, T-030c) lives in the composition layer so neither Hangar nor Units references the other.

## Save: `hangar@v1`
Section `hangar`, version 1, keyed by `PlayerSlot` (D-003, N slots). Header record `slot:N` (phase, cycle, site) and one record per part `slot:N/<partId>` (integrity and max_integrity as Float64 bits, last_hit_step, hits, repaired, queue). `Read` validates everything first (finite, 0 <= integrity <= max, dense queue, known site, every part has a header) and throws `FormatException`, so `SaveSystem.Restore` returns `Corrupt` and the service is untouched. Part ids: non-empty, no `/`, no control characters. Unrelated sections are kept by `SaveSystem.Capture(loaded)`. No migration yet (v1).

## Metric M-059@v1
Registry `Tools/Metrics/registry/M-059@v1.yaml`; producer `DotnetTests/Hangar.Tests/Dotnet/M059Tests.cs` (3 seeds x 3 cycles, 30 comparisons, a file save and a simulated process restart at every phase change, floats as raw bits). Gate: 0 differences; a flipped damage bit must be counted. Evidence: `evidence/T-034/m059.json` (run with `KAIJU_EVIDENCE_DIR`).

## Feature flag
`hangar.stub` (`Config/FeatureFlags/Hangar.json`, default off). `HangarModule.Install(registry, flags, world, site)` registers `IHangarService` only when on and returns the service; the composition root registers it with `SaveSystem`. **Not wired** into `Game.Composition` yet (that file is another track's).

## Known limits / TODO
- Left out on purpose (R-025 section 5), all staged not cut: refit and part swap (needs the T-005 part catalogue), upgrades and progression, repair cost and time, hangar UI beyond a debug panel (none yet), scene art, co-op hangar layout.
- Site position is Assumed until `L0_27_36` is baked; no Unity-side scene or debug panel yet.
- `PlaceholderDamageRecord` adapter: see HANDOFF "T-034 hangar stub (2026-11-15)".
