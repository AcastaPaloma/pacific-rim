# Meta.Roster
Purpose: D-020 / F-010 / D-019 / Q-206. Which mechs a slot owns and whether they are still flyable: the permadeath rule, the tombstone, the stable wreck id for salvage, the pilot record (strain) and the city-funded floor chassis that keeps a slot from ever being without a mech.

Breakdown IDs: F-010, D-020, D-019, D-004, M-106@v1 (roster part), M-104 (consumer: T-123 simulator). Research: `research/R-104.md` sections 6 and 10.3. Task: T-120. Layer: L4. References only `Game.Core.Contracts`, `Game.Core` (`EntityIdAllocator`, save), `Game.Hangar.Contracts` (`IBodyDamageRecord`), `Game.Meta.Economy.Contracts` (floor grant) and its own assemblies. Nothing else from Hangar or Salvage.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Meta.Roster.Contracts` | `Contracts/` | none | `IRosterService`, `MechInstance`, `PilotRecord`, `FightEndResult`, `MechState`, `LossCause`, event `MechLost`, `RosterContractVersions`, `RosterFlags` |
| `Game.Meta.Roster` | `Runtime/` | none | `RosterService` (service + `ISaveParticipant`), `WreckIdentity`, `RosterModule.Install` |
| tests | `DotnetTests/Meta.Roster.Tests` | test | NUnit, dotnet only: loss rule, absorbing Lost, wreck ids, floor, pilot, event, 30-seed invariant run, save corruption, M-106 roster part |

## Rules (R-104 section 6)
- **Loss.** `OnFightEnd` reads `IBodyDamageRecord.CopyParts`. The mech is Lost when any core part has integrity <= 0. Core parts are `body` (the placeholder body is a one-part core: Destroyed means Lost), `cockpit_torso` and `reactor`; configurable in the constructor. A record without a core part, NaN integrity, a record of another slot or a non-finite strain throws and changes nothing. Destroyed limbs, weapons and armour are not a loss. `Retire` (player scraps the mech) is a loss with cause `Scrapped`.
- **Absorbing.** Lost has no outgoing transition: `OnFightEnd` on a lost mech returns `Replay = true` (nothing changes, nothing is published, not a sortie), `Retire` returns the stored tombstone, `SetActive` refuses it. The record stays (chassis, spec hash, cycles, cause, loss step, wreck id).
- **Wreck id.** `WreckIdentity.Derive(mechId, step)`: splitmix of both, in 1..999,999,999,999 (fits the 12-digit salvage keys), probed forward on a collision within the roster. Set once at the loss, saved, so a replay or reload hands salvage the same id and `ISalvageService.Claim` stays idempotent. The `IBodyDamageRecord` to `SalvageInput` adapter is the composition layer's (T-122).
- **Event.** `MechLost { MechId, Slot, WreckId, Cause, Step }`, once per mech.
- **Pilot.** One record per slot: strain (0..1, as reported by the fight), sorties, recovery sorties, mechs lost. A loss sets strain to at least 0.5 and owes one recovery sortie (cleared by the next sortie that does not lose the mech). No pilot death (Q-NEW-d deferred).
- **Floor (Q-206).** After a loss or a scrap that leaves the slot with no flyable mech, `GrantFloor` asks `IEconomyService.TryGrantFloorChassis` (free, never stacked while a flyable mech exists) and adds a `IsFloor` mech of the price table's floor chassis. It is flyable at once, i.e. from the next sortie. The floor chassis cannot be scrapped (non-resellable; stops a grant-and-scrap loop). A slot is therefore never left without a flyable mech.
- **Ids.** `MechId` comes from the shared `EntityIdAllocator`; a load advances the allocator past every saved id.

## Save: `roster@v1`
Section `roster`, version 1: `totals` (next_entity, mechs), `mech:<12-digit id>` (slot, chassis, state, spec_hash, wreck_id, cycles, floor, cause, created_step, loss_step), `pilot:slot:<n>` (strain Float64 bits, sorties, recovery, lost, active), `floor:slot:<n>` (granted_count). `Read` validates first and throws `FormatException` (so `SaveSystem.Restore` returns `Corrupt` and the service is untouched): ids below next_entity, flyable carries no loss data, lost has cause and a unique wreck id in range, the floor chassis is never scrapped, pilot loss count equals lost mechs, sorties cover the fights fought, active mech is a flyable mech of the slot, floor count equals floor mechs. No migration yet (v1).

## Metric M-106@v1 (roster part)
Producer `DotnetTests/Meta.Roster.Tests/Dotnet/M106RosterTests.cs`: 3 seeds x 3 cycles x 2 phases, file save and simulated process restart after the fights and after the hangar actions (12 comparisons per seed, 36 total) against an un-restarted twin, floats as raw bits; flipped-bit control; 29 corruption cases give `Corrupt`. Gate: 0 differences. Evidence `evidence/T-120/m106_roster.json` (run with `KAIJU_EVIDENCE_DIR`).

## Feature flag
`meta.permadeath` (`Config/FeatureFlags/Meta.Roster.json`, default off). `RosterModule.Install(registry, flags, economy, allocator, bus)` registers `IRosterService` only when on and an economy and allocator exist, and returns the service; the composition root registers it with `SaveSystem`. **Not wired** into `Game.Composition` (T-122) and not called by Hangar yet (T-122 adapter: `EnterHangar` calls `OnFightEnd`).

## Known limits / TODO
- Pilot strain is passed in by the caller (F-008 lives in Control/Units); the 0.5 post-loss strain and one recovery sortie are R-104's wording, read literally.
- `SpecHash` is stored, never computed here: Meta.Refit (T-121) owns `MechBuildSpec@v1`.
- No refit-driven chassis catalogue check (T-121) and no co-op per-slot treasury split (treasury is shared by Economy).
