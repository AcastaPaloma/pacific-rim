# Meta.Salvage
Purpose: F-010 / D-024 / D-020 / Q-206. What a wreck gives back: recoverable mass by part from fragmentation and where it lies (land, shallow Bay, deep channel), material classes including kaiju tissue (rare parts as unlocks), building debris as scrap, and a capped credit into `Meta.Economy`.

Breakdown IDs: F-010, D-024, M-106@v1 (salvage part), M-104 (consumer: T-123 simulator). Research: `research/R-104.md` sections 5, 7, 10.2, 10.5 and its Erratum (T-119). Task: T-119. Layer: L4. References only `Game.Core.Contracts`, `Game.Core` (Json parser), `Game.Meta.Economy.Contracts` and its own assemblies.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Meta.Salvage.Contracts` | `Contracts/` | none | `ISalvageService`, `SalvageInput`, `SalvagePart`, `SalvageResult`, `PartRecovery`, `SalvageClaim`, `SalvageLocation`, `SalvageSourceKind`, event `SalvageRecovered`, `SalvageContractVersions`, `SalvageFlags` |
| `Game.Meta.Salvage` | `Runtime/` | none | `SalvageService` (service + `ISaveParticipant`), `YieldsTable` (parser, fragmentation law, kaiju mass), `SalvageModule.Install` |
| tests | `DotnetTests/Meta.Salvage.Tests` | test | NUnit, dotnet only: fractions vs data file, mass conservation, cap, kaiju and debris, claim idempotency, M-106 salvage part |

## Handoff input (wreck to salvage; T-120 Roster and the composition adapters, T-122)
`SalvageInput { WreckId (>0, stable, idempotency key), SourceKind (Mech/Kaiju/Building), Location, FallEnergyJPerKg (g x CoM height, 392 for the 40 m reference), BuildPriceUsd (Mech only, > 0), Parts[] }`. Each `SalvagePart { PartId, ClassId, MassKg, AbsorbedEnergyJ, Integrity, MaxIntegrity, PriceUsd }`. Mech class ids are the economy price classes (structure, armour, drive, reactor, titanium, composite, weapons) and `PriceUsd` is the `Meta.Economy` quote of the part. Kaiju parts are the tissue classes (bone, hide, tendon; `YieldsTable.KaijuTissueMassKg(length, class)` gives the carcass budget). Building parts are `debris_steel` and `debris_other` from `BuildingDamaged.DebrisKg` (increments summed by the caller, split by the building's steel share). The adapter from `IBodyDamageRecord` / the kaiju damage record / `BuildingDamaged` to this input lives in the composition layer; this module never sees Hangar or Destruction types. Invalid input throws `ArgumentException` before anything changes.

## Model (all constants in `salvage_yields@v1`)
- Fragmentation: `f = 1 - exp(-e / e_c)`, `e` = fall energy per kg + absorbed energy / part mass, `e_c` = 10 kJ/kg. Debris classes do not fragment again.
- Recovered mass of a part = `m (1 - f) eta_loc`, `eta_loc` = 0.90 land, 0.70 shallow Bay, 0.35 deep channel (non-increasing with depth, validated). Recovered never exceeds available (per part and per wreck).
- Headline fractions for a part that absorbed the whole 392 J/kg fall: **0.86 land / 0.67 shallow Bay / 0.34 deep channel** (unrounded 0.865, 0.673, 0.337). The first draft of R-104 section 1 said 0.60 and 0.30; corrected by the Erratum. The data file carries these with a 0.01 tolerance and a test recomputes them from the model.
- Value: mech part with integrity above 0.33 and a price: reuse = 0.6 x I x price x recovered fraction; otherwise scrap = scrap price x recovered tonnes (steel $294/t for structure, armour, drive; titanium $8,000/t; composite $2,750/t); reactor and weapons are reuse-only (no scrap cash). Kaiju bone is $54,000/t (0.45 x $120k/t titanium parity); hide and tendon have no sourced cash value (0). Building debris is steel scrap only ($294/t), so a 100,000 t collapse pays about $26M and is not a farming route.
- Wreck-credit cap (Mech): credit <= 12% of `BuildPriceUsd`, so a loss always costs at least 88%. `SalvageResult` carries `RawCreditUsd`, `CreditUsd` and `CapApplied`.
- Kaiju tissue is throughput-limited: each class keeps at most its priority share of its mass (bone 5%) and all classes draw from one 80 t/h x 48 h = 3,840 t budget in priority order. A heavy kaiju (165 m) gives about 340 t of bone and about $18M.
- Rare parts are unlocks, not currency: sets earned = floor(total recovered class mass / minimum mass) (osteoderm_plate from bone 25 t; keratin_liner from hide 10 t and actuator_fibre from tendon 20 t, both Assumed). `TryConsumeRare` is called by Meta.Refit (T-121) on fitting.

## Service
`Assess(input)` is pure. `Claim(input, step)` is idempotent by `WreckId`: the first call credits `IEconomyService.Credit(credit, TransactionReason.SalvageCredit)` (so arrears are collected first), adds recovered mass to the per-class inventory, stores the wreck and publishes `SalvageRecovered` once (unmanaged, IDs and numbers only: WreckId, SourceKind, Location, RecoveredKg, CreditUsd); a replay, also after a save reload, returns the stored summary and changes nothing.

## Save: `salvage@v1`
Section `salvage`, version 1. Records: `totals` (credit_usd, wrecks), `wreck:<12-digit id>` (source, location, available_kg and recovered_kg as Float64 bits, credit_usd, raw_credit_usd, step), `inv:<class>` (mass_kg Float64 bits), `rare:<part>` (earned, consumed). `Read` validates first (counts, totals equal sums, recovered <= available, only mech credit may be capped, inventory equals recovered mass within 1e-9, rare sets equal floor(inventory / minimum)) and throws `FormatException`, so `SaveSystem.Restore` returns `Corrupt` and the service is untouched. No migration yet (v1).

## Metric M-106@v1 (salvage part)
Producer `DotnetTests/Meta.Salvage.Tests/Dotnet/M106SalvageTests.cs`: 3 seeds x 3 cycles x 2 phases, file save and simulated process restart after claims and after rare consumption (12 comparisons per seed, 36 total), each restarted run compared with an un-restarted twin, floats as raw bits; flipped-bit control; 19 corruption cases give `Corrupt`. Gate: 0 differences. Evidence `evidence/T-119/m106_salvage.json` (run with `KAIJU_EVIDENCE_DIR`).

## Feature flag
`meta.salvage` (`Config/FeatureFlags/Meta.Salvage.json`, default off). `SalvageModule.Install(registry, flags, yields, economy, bus)` registers `ISalvageService` only when on and an economy exists, and returns the service; the composition root loads `Config/Salvage/salvage_yields@v1.json`, registers the service with `SaveSystem` and passes the Core bus. **Not wired** into `Game.Composition` (another track's file).

## Known limits / TODO
- Math.Exp is used for fragmentation: results are identical on one runtime, bit-exactness across CPU architectures is not claimed (saved values are stored, never recomputed).
- Hide/tendon properties, priority shares, rare minimum masses for hide and tendon, salvage window and throughput, and the drive scrap class are Assumed (see `assumed` flags and R-104 Q-NEW-f, Q-NEW-g). Retune in T-123, never to pass a gate.
- A Kaiju or Building wreck has no build price, so only the physical limits (throughput, steel scrap price) bound its credit.
- Roster loss rule and WreckId derivation (T-120), refit consumption of rare sets (T-121), the `IBodyDamageRecord` adapter (T-122), the pacing simulator (T-123) are not here.
