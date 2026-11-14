# MODULARITY.md: how the game is split up, and how to add things

> Goal: adding a feature should be obvious. An agent looks up the module that owns the feature, implements that module's interface, registers it, and adds tests. It never edits another module's internals.

---

## 1. Rules (non-negotiable)

1. **One module = one folder + one assembly definition (`.asmdef`) + one `MODULE.md`.**
   - `MODULE.md` says: purpose, public interfaces, events emitted and consumed, config assets, tests, owner role, and linked `GAME_BREAKDOWN` IDs.
2. **Modules depend only on contracts.**
   - Each module exposes a `*.Contracts` assembly containing interfaces, data structs and event types.
   - Other modules reference the contracts, never the implementation.
   - Dependencies point downward only (see §3).
3. **Data-driven.** Tuning values (masses, limits, palettes, move definitions, reward weights) live in ScriptableObjects or JSON under `Config/`. Never hard-code numbers in behaviour code.
4. **Events over references.** Modules talk through the `Core.Events` bus (e.g. `FootContact`, `ImpactResolved`, `PartDestroyed`, `KaijuEmerged`) or through injected interfaces.
5. **Feature flags.** Every module and optional feature can be switched on or off in `Config/FeatureFlags`. New features start switched off.
6. **No hard-coded counts.** Players, mechs, kaiju and pilots-per-mech are all collections (D-004, D-003).
7. **Tests live with the module.**
   - `Tests/EditMode` and `Tests/PlayMode` assemblies per module.
   - Pure logic stays in engine-free code (`noEngineReferences: true`) where possible, so it runs under `dotnet test`.
8. **Never edit `.unity` or `.prefab` files as text.** Use Editor APIs or the Unity CLI.

---

## 2. Module map

```
Assets/Game/
├─ Core/              Time, events bus, service registry, IDs, feature flags, save system
├─ Physics/
│  ├─ Body/           Articulated bodies, PD joint drives, mass properties, balance/CoM
│  ├─ Contact/        Impact resolution (impulse → energy → damage events), ground pressure
│  └─ Water/          Buoyancy, drag, wading, splash events
├─ Damage/            Structural part graph shared by mechs, kaiju and buildings
├─ Units/
│  ├─ Mech/           Mech assembly: frame + parts + subsystems + loadout
│  └─ Kaiju/          Kaiju assembly: body plan + parts + motor layer
├─ Control/           Pilot input → IntentFrame → unit controller (multi-pilot extension point)
├─ Combat/
│  ├─ Melee/          Strike/grapple/shove/throw move definitions and execution
│  └─ Weapons/        IWeapon implementations (cannon, blade, rocket strike…)
├─ Systems/           ISubsystem: Power, Heat, Ammo, PilotStrain, Thrusters
├─ AI/
│  ├─ Contracts/      IKaijuBrain, ObservationSpec, ActionSpec
│  ├─ Policy/         ONNX policy runner (Unity Inference Engine)
│  └─ Scripted/       Fallback brains for tests and untrained species
├─ World/
│  ├─ Geo/            GIS importers: terrain, bathymetry, OSM buildings/roads, fault lines
│  ├─ Streaming/      Tiles, floating origin, LOD
│  ├─ Destruction/    Building fracture, persistence by building ID
│  ├─ Infrastructure/ Kaiju target registry (bridges, power, port, airport)
│  └─ Emergence/      Earthquake + fault-line spawn director
├─ Rendering/
│  └─ Pixel/          URP renderer features: low-res target, palette LUT, dithering, haze bands
├─ Camera/            ICameraRig: ThirdPersonRig (now), CockpitRig (later)
├─ Net/               INetSession: host-authoritative replication, N player slots
├─ Meta/
│  ├─ Hangar/         Repair, refit, upgrade
│  └─ Economy/        Contracts, salvage, permadeath bookkeeping
├─ Audio/             Event-driven audio (footfalls, impacts, servo, roar)
└─ UI/                HUD (power/heat/ammo/strain/part damage), hangar UI, menus
Training/             Python RL: tactical environment, training scripts, ONNX export
Pipelines/            Concept (Midjourney) → Blender → Unity import, GIS fetch scripts
Tools/                Editor-only validators, importers, metric capture tools
```

---

## 3. Dependency layers (only depend on layers below)

| Layer | Modules |
|---|---|
| L0 | Core |
| L1 | Physics.*, Damage, Rendering.Pixel, Audio |
| L2 | Systems, Combat.*, World.* |
| L3 | Units.Mech, Units.Kaiju, Control, AI.*, Camera |
| L4 | Net, Meta.*, UI |

`Net` replicates through the contracts of lower layers. Nothing in lower layers knows networking exists.

---

## 4. Key extension points (the "plug in here" list)

### 4.1 Control: one mech, many pilots (D-003)

```csharp
interface IPilotSource { PilotInput Read(); }                   // a device or a remote player
struct   IntentFrame  { MoveId move; Vector3 target; float effort; /* … */ }
interface IIntentArbiter { IntentFrame Merge(IReadOnlyList<PilotInput> inputs); }
class    PilotSeat    { List<IPilotSource> pilots; IIntentArbiter arbiter; }  // one per mech
```

- **Now:** each mech has one pilot, and `SinglePilotArbiter` passes that pilot's input straight through.
- **Later:** add `SplitLimbArbiter`, `ConsensusArbiter` and similar as new classes. Nothing else changes.

### 4.2 Camera (D-006)

`ICameraRig` with `ThirdPersonRig`. `CockpitRig` slots in later and is selected by feature flag. The pixel pipeline reads the active rig's stabilisation hints.

### 4.3 Weapons (F-006)

`IWeapon` defines:
- `CanFire(SubsystemState)`
- `Fire(IntentFrame)`
- power/heat/ammo cost data
- mount point requirements

To add a weapon:
1. Create a new class.
2. Create a config asset.
3. Add a loadout entry.

### 4.4 Subsystems (F-008)

`ISubsystem.Tick(dt, Bus)`. Power, heat, ammo, pilot strain and thrusters each implement it and communicate through the mech's subsystem bus. A new subsystem (e.g. hydraulics) is one more class.

### 4.5 Kaiju brains (F-021, F-026)

- **Interface:** `IKaijuBrain.Decide(Observation) → TacticalAction`.
- **Observation and action specs** are versioned data. A policy trained against spec `v3` refuses to load against `v4`.
- **New species** = body plan asset + motor clips + policy `.onnx` + species config. No code changes unless it needs a new action type.

### 4.6 World data (F-040)

`IGeoLayer` importers (terrain, bathymetry, buildings, roads, faults). Each produces cached tile data with a recorded source licence and attribution.

### 4.7 Rendering style (F-046, P4)

`PaletteProfile` assets per district/time. Pixel pipeline stages are separate renderer features that can be reordered and toggled.

### 4.8 Players (D-004)

`Net` exposes `PlayerSlot[]`. Hangar, economy and UI iterate over slots. Going from 2 players to N is a config change plus testing.

---

## 5. Recipe: adding a feature (agents follow this exactly)

1. **Find it.** Locate or create the `F-###` entry in `GAME_BREAKDOWN.md`. No ID means no work.
2. **Own it.** Identify the owning module from §2.
   - If none fits, propose a new module with a `MODULE.md` stub. The orchestrator approves it.
3. **Contract first.** Add or extend interfaces and events in the module's `Contracts`. Version any data spec you change.
4. **Implement** inside the module only, behind a feature flag. Put tuning values in a config asset.
5. **Test.**
   - EditMode tests for logic.
   - A PlayMode test or capture for behaviour.
   - Metric hooks for any linked `M-###`.
6. **Register** via the service registry or loadout config. Do not edit other modules' code.
7. **Document.**
   - Update the module's `MODULE.md`.
   - Add a change-log line to `GAME_BREAKDOWN.md`.
8. **Evaluate.** Hand off to the evaluation loop (see `FABLE_ORCHESTRATOR_PROMPT.md`).

---

## 6. `MODULE.md` template

```markdown
# <Module name>
Purpose: …
Breakdown IDs: F-…, D-…
Contracts: <interfaces / events / data specs + versions>
Consumes events: …   Emits events: …
Config assets: Config/<…>
Feature flags: …
Tests: EditMode …, PlayMode …, Metrics M-…
Owner role: implementation/<area>
Known limits / TODO (with Q-### links): …
```
