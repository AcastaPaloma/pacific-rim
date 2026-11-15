# Meta.Economy
Purpose: F-061 / D-024 / Q-205 / Q-206. One currency (whole US dollars of defence-contract funds), prices derived from mass x material cost index, contracts paid on protected value saved, a funds ledger that never goes negative, and the city-funded floor chassis so a campaign never dead-ends.

Breakdown IDs: F-061, D-024, Q-205, Q-206, M-106@v1 (economy part), M-104 (consumer: T-123 simulator). Research: `research/R-104.md` sections 2, 3, 4, 6, 7, 10.1. Task: T-118. Layer: L4. References only `Game.Core.Contracts`, `Game.Core` (Json parser) and its own assemblies.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Meta.Economy.Contracts` | `Contracts/` | none | `IEconomyService`, `ContractInfo`, `ContractAsset`, `ContractOutcome`, `AssetDamageState`, `ContractState`, `ContractResult`, `TransactionReason`, `PriceQuote`, `FloorGrant`, event `ContractCompleted`, `EconomyContractVersions`, `EconomyFlags` |
| `Game.Meta.Economy` | `Runtime/` | none | `EconomyService` (service + `ISaveParticipant`), `PriceTable` (parser, price function, payout, repair cost), `EconomyModule.Install` |
| tests | `DotnetTests/Meta.Economy.Tests` | test | NUnit, dotnet only: ledger invariants, payout vs R-104, save, floor, M-106 economy part |

## Money
Whole-USD `long`, ratios in integer basis points, mass in `long` kg; no floating point in any money path (repair cost takes the integrity fraction as a double and converts to bps once, deterministically). Overflow is `checked`.

## Data: `economy_prices@v1`
`Config/Economy/economy_prices@v1.json`: part classes (USD per tonne, each with source and `assumed`), chassis compositions (Balanced $550.8M, Heavy Brawler $562.8M, Basic Balanced floor $331.4M), fixed-asset replacement costs (Bay Bridge, Golden Gate, port crane, substation), parameters (fee 6%, advance 10%, failure below 25% saved, cap 35% of the Balanced price = $192.78M, sell-back 40%, upkeep 0.4%, repair overhead 1.5, Hazus loss 0/2/10/50/100%, repair caps, building $6,000/m2). A revised index changes data, not code. `PriceTable.Parse` validates everything and throws `FormatException`.

## Rules
- Price of a part = mass x class index (`PriceTable.QuotePart`); chassis price = sum over classes (`ChassisPriceUsd`).
- Payout = 0 when saved share < 25%, else min(fee x (V - L), cap), V = sum of asset replacement costs, L = sum of replacement x loss ratio of the final damage state. Advance = 10% of the undamaged expected payout, credited at `Accept`, repaid at `Complete`; a failed contract forfeits it.
- Ledger: `Funds >= 0` always; `Funds == TotalCredited - TotalDebited` (conservation). A purchase that does not fit returns false and changes nothing. A shortfall (upkeep, unrecovered advance) becomes `Arrears`, an explicit non-negative figure collected from later income; it is never negative funds. Transactions: ring of the last 256.
- Offers (`OfferContracts`): deterministic from (seed, step, pool); a new batch withdraws older offers; at least one offer has expected payout >= $25M when the pool allows (R-104 section 7d).
- Floor (Q-206): `TryGrantFloorChassis(slot, flyableCount, step)` grants the Basic Balanced chassis free, regardless of funds, only when the slot has no flyable mech, so never stacked. Per-slot grant count is saved. Roster (T-120) owns the "flyable" fact and calls this.
- Event `ContractCompleted` (unmanaged, IDs and numbers only) is published once per contract; `Complete` on a settled contract throws.

## Save: `economy@v1`
Section `economy`, version 1. Records: `treasury` (funds, arrears, credited, debited, next_contract_id, next_tx), `contract:<12-digit id>`, `asset:<id>:<4-digit index>`, `tx:<12-digit seq>` (last 256), `floor:slot:N` (granted). All Int64, so exact. `Read` validates first (non-negative, conservation, dense assets summing to the contract value, state/slot consistency, ids below the counters) and throws `FormatException`, so `SaveSystem.Restore` returns `Corrupt` and the service is untouched. No migration yet (v1).

## Metric M-106@v1 (economy part)
Producer `DotnetTests/Meta.Economy.Tests/Dotnet/M106EconomyTests.cs`: 3 seeds x 3 cycles x 4 sorties, file save and simulated process restart at accept and at settle (24 comparisons per seed, 72 total), each restarted run compared with an un-restarted twin; flipped-bit control; 13 corruption cases give `Corrupt`. Gate: 0 differences. Evidence `evidence/T-118/m106_economy.json` (run with `KAIJU_EVIDENCE_DIR`).

## Feature flag
`meta.economy` (`Config/FeatureFlags/Meta.json`, default off). `EconomyModule.Install(registry, flags, prices, bus, startingFunds)` registers `IEconomyService` only when on and returns the service; the composition root loads the price file, registers the service with `SaveSystem` and passes the Core bus. **Not wired** into `Game.Composition` (another track's file).

## Known limits / TODO
- Not here (other tasks): repair queue pricing wiring (Hangar calls `PriceTable.RepairCostUsd`), salvage credit amounts (T-119, call `Credit(..., SalvageCredit)`), roster loss rule (T-120), refit purchases and part catalogue (T-121), the pacing simulator (T-123).
- `IAssetValueProvider` (replacement costs from Geo/Destruction) is not built: `OfferContracts` takes a pool of `ContractAsset`; the adapter lives in the composition layer. Airport and rail rows are TODO (R-104 Q-NEW-c).
- Fee 6%, loss ratios (cross-check with R-021 pending), fabrication multipliers and the 10% advance are Assumed; see the `assumed` flags in the data file. Retune in T-123, never to pass a gate.
- Old finished contracts stay in the save (about 100 bytes each); prune policy when a campaign exceeds a few hundred.
