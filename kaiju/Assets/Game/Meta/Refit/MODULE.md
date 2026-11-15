# Meta.Refit
Purpose: D-017 / D-019 / D-025 / F-062. One modular mech frame with chassis classes as constraint sets over it, a part catalogue (`parts@v1`), validation of every build from physics (mass, reactor power, heat, actuator torque, centre of mass, tilt margin), the output `MechBuildSpec@v1` that Physics.Body reads once T-005 lands, and hangar refits paid through Meta.Economy with rare salvage parts consumed from Meta.Salvage.

Breakdown IDs: D-017, D-019, D-025, F-062, M-105@v1, M-106@v1 (refit part), M-104 (consumer: T-123 simulator). Research: `research/R-104.md` sections 2, 5.4, 7, 9, 10.4, 10.5. Task: T-121. Layer: L4. References only `Game.Core.Contracts`, `Game.Core` (Json, save), `Game.Meta.Economy.Contracts` (funds, purchase), `Game.Meta.Salvage.Contracts` (rare sets) and its own assemblies. Nothing from Hangar, Roster or Physics.Body: the hangar phase and roster state reach it through `IRefitGate`, prices through `IPartPricing`.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Meta.Refit.Contracts` | `Contracts/` | none | `IRefitService`, `IPartCatalog`, `IChassisCatalog`, `IPartPricing`, `IRefitGate`, data types `PartDef` `SlotDef` `ChassisDef`, `MechBuildSpec`, `RefitReport` `ConstraintMargin` `RefitViolation`, `RefitResult` `RefitStatus`, `MechBuild`, event `RefitApplied`, `RefitContractVersions`, `RefitFlags` |
| `Game.Meta.Refit` | `Runtime/` | none | `PartCatalog` + `ChassisCatalog` (parse and validate the two json files), `RefitPhysics` (the section 9 formulas, spec hash), `RefitService` (service + `ISaveParticipant`), `ClassPricing`, `RefitModule.Install` |
| tests | `DotnetTests/Meta.Refit.Tests` | test | NUnit, dotnet only: catalogue and anchors, validation order, apply/refuse/atomicity, rare parts, sell-back loop, save corruption, M-105 (independent oracle), M-106 refit part |

## Data (the numbers are data, not code)
- `Config/Refit/parts@v1.json`: 42 parts over 9 slot types. Each part: class composition in kg (the Meta.Economy price classes), box size and CoM offset, power peak (continuous or pulse), sustained and burst-average heat, radiator and coolant sub-masses, torque bonus (cap +15%), rare part, tier (basic / standard / advanced / rare), the chassis it fits, `source` and `assumed`.
- `Config/Refit/chassis@v1.json`: three chassis (`balanced`, `heavy_brawler`, `basic_balanced` = the city floor hull) over the same 14 slots, with the section 9 limits as data: mass cap (checked at load to be at most p_g A_foot / g with p_g = 700 kPa), CoM box, tilt limit, reactor 0.2 MW per tonne, buffer 1 GJ over 20 s, walking C_T 0.34 at 2.5 m/s, passive heat 30 MW, radiators 0.2 MW/t, coolant 4.2 kJ/kg K x 60 K over 600 s, actuators 2,000 N m/kg x 0.8, alpha 1.0 (1.2 Brawler), the default build and the limbs checked for the tilt margin.
- `Tools/refit/gen_refit_data.py` is the calibration record that writes both files: it solves each actuated part's drive mass from the torque rule (106% of the default-build demand), the radiator and coolant masses from the heat rule, makes the torso the residual so that each default build has exactly the class tonnage of `economy_prices@v1` (so a default Balanced build costs $551M, Brawler $563M, floor hull $331M, asserted in the tests), and checks the three defaults against the limits. Run `/opt/miniconda3/envs/kaiju/bin/python Tools/refit/gen_refit_data.py --write`.
- The R-104 text says "13 slots" but its own list (head, torso, 2 shoulders, 2 upper arms, 2 forearms, 2 hip-legs, 2 feet, back mount, reactor bay) is 14; the list is used. The mass caps are R-104's table values (9,900 t, 13,000 t), below the formula limits (9,990 t, 13,558 t).

## Rules (R-104 section 9)
Checks run in order and the first violated one is reported with every margin (`RefitReport.Margins`): slot structure (unknown chassis, unknown or missing slot, unknown part, wrong slot type, wrong chassis), then
1. **Mass** M <= cap.
2. **Power** peak - rated <= buffer / window. Rated = 0.2 MW/t x reactor-class mass; peak = walking power C_T M g v + continuous part demands + the largest pulse (weapons overlap through one pulse, R-104's "one cannon").
3. **Heat rejection** sum of part heat <= passive + radiator mass x 0.2 MW/t; **heat storage** (burst heat - rejection)+ x 600 s <= coolant mass x c x dT.
4. **Torque** per actuated slot: rated (drive-class mass of the slot's part x 2000 N m/kg x 0.8 x (1 + bonus)) / demand >= 1, demand = sum over the moved parts of m g r + (m r^2 + m L^2 / 12) alpha (r = joint to part CoM, L = largest box side). Moved = every descendant slot, plus the slot itself for a leg.
5. **CoM** height within 0.45-0.55 l, lateral within 1.5 m, fore-aft within [-2, +3] m.
6. **Tilt margin** atan2(w - |x|, y) of the CoM, intact and after losing each arm, >= 12 degrees.
Inertia about the CoM is the parallel-axis sum of part boxes. `MechBuildSpec@v1`: chassis, slot parts and masses, total mass, CoM, inertia (6), power rated and peak, heat rejection and storage, rated torque per slot, FNV-1a 64 hash (never 0). Pure functions of the catalogue: same build, same bits.

## Service
`Register` (a purchased or city-issued initial build, nothing charged; rare parts only through `Apply`), `Quote`, `Apply` (hangar refit): checks the gate, validates, then rare sets (`ISalvageService.RareAvailable`), then funds; pays new parts at price minus the 40% sell-back of the parts taken out (city-issued parts return nothing, so a floor hull cannot be farmed), consumes one rare set per newly fitted rare part, publishes `RefitApplied` once, resets wear on changed slots. Any refusal (`UnknownMech`, `NotAllowed`, `Invalid`, `NoChange`, `RareUnavailable`, `InsufficientFunds`) changes nothing. The floor hull is held to the `basic` catalogue (R-104 6.6), so its only legal changes are the light cannon or the blade. Buy-and-sell-back loses 60% of the part (tested). Rare parts: `upper_arm_osteoderm` (armour mass x 0.64, `osteoderm_plate`) and `hip_leg_fibre` (+15% torque so a lighter actuator, `actuator_fibre`); `keratin_liner` has no part yet.

## Save: `refit@v1`
Section `refit`, version 1: `totals` (mechs), `mech:<12-digit id>` (slot, chassis, spec_hash, refits, registered_step, applied_step), `mech:<id>/slot:<name>` (part_id, wear Float64 bits, issued). Catalogues are data and are not saved. `Read` validates first and throws `FormatException` (so `SaveSystem.Restore` returns `Corrupt` and the service is untouched): known chassis, a record for every slot and no others, part fits slot type and chassis, wear in 0..1, counters, and the stored spec hash must equal the hash recomputed from the saved parts (a data change that alters a saved mech is a migration job, not silently accepted).

## Metrics
- **M-105@v1** (`DotnetTests/Meta.Refit.Tests/Dotnet/M105RefitTests.cs`): per chassis 10,000 seeded builds reachable from the catalogue (5,000 uniform, 5,000 mutations of the default) plus 2,000 structurally broken ones; an independent oracle recomputes mass, CoM, inertia, power, heat, torque and tilt and decides validity and the first violation; 0 disagreements, 0 out-of-limit valid builds, 0 broken builds accepted, spec mass equals the sum of part masses, same seed gives the same hashes. Random swaps never reach the CoM box or the tilt margin with the shipped limits, so a second test tightens each of those four limits in turn and runs the same comparison (each check fires on thousands of builds). Evidence `evidence/T-121/m105_refit.json`.
- **M-106@v1, refit part** (`M106RefitTests.cs`): 3 seeds x 3 cycles x 2 phases file save and process restart, 36 comparisons against an un-restarted twin, raw float bits, 0 differences; flipped-bit control; 25 corruption cases give `Corrupt`. Evidence `evidence/T-121/m106_refit.json`.

## Feature flag
`meta.refit` (`Config/FeatureFlags/Meta.Refit.json`, default off). `RefitModule.Install(registry, flags, parts, chassis, pricing, economy, bus, salvage, gate)` registers `IRefitService` only when on and the required services exist, and returns it; the composition root registers it with `SaveSystem`. **Not wired** into `Game.Composition` (T-122: load the two json files, build `ClassPricing` from the economy price table, implement `IRefitGate` from Hangar phase + Roster state, register mechs as the roster creates them).

## Known limits / TODO
- Roster stores `SpecHash` but has no update call (T-120 sets it at `Create`): T-122 or a Roster follow-up needs `SetSpecHash` so a refit reaches the roster record; `MechBuildSpec` is also not yet read by Physics.Body (pending T-005, `pendingT005Body` stays on).
- Wear is recorded and saved but does not yet change repair or sell-back prices (R-104 does not specify it); no keratin-liner part.
- The tilt check covers losing one arm (data: `limbs`), not a leg; torque uses a worst-case horizontal lever and a slender-limb own inertia. Part prices of reactor and weapons classes are Assumed (Q-NEW-e).
- Mass, power and heat caps are enforced at validation (hangar exit), not during a fight; heat storage uses the burst-average heat of each part (assumed values).
