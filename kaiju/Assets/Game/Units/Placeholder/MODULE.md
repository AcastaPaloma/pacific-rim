# Units.Placeholder (T-030c) — TEMPORARY

**Delete this module when T-005's walking mech passes its walk gate.** It exists only so the co-op slice, the first playable scene (T-039b) and the hangar stub (T-034) have a body to drive and damage before the walk lands.

Purpose: a kinematic capsule body (no physics, no feet, no gait) that consumes the seat's `IntentFrame`, obeys the mech's declared accel / decel / yaw-rate / yaw-accel limits, implements `ICameraTarget`, and carries a tiny damage record.
Breakdown IDs: F-050 area (co-op), F-060 (hangar reads the damage record). Layer: L3, references Core.Contracts, Control.Contracts, Camera.Contracts, Units.Mech.Contracts only. Never references Net (the composition root wires seat to body).

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Units.Placeholder.Contracts` | `Contracts/` | none | `PlaceholderDamageRecord`, `PlaceholderDamageData` (`placeholder_damage@v1`), `PlaceholderDamageState`, `PlaceholderFlags`, versions |
| `Game.Units.Placeholder` | `Runtime/` | none | `PlaceholderBody` (`Step(IntentFrame, dt)`, `ICameraTarget`), `PlaceholderLimits` (`Default()`, `FromWalkTuning`) |
| `Game.Units.Placeholder.Unity` | `Unity/` | Unity | `PlaceholderBodyBehaviour`: capsule transform driver, thin |
| tests | `Tests/EditMode` | test | `dotnet test DotnetTests/Units.Placeholder.Tests`; same sources run in Unity EditMode |

## Behaviour
- `Walk` heads for the target (turn first when the heading error exceeds `turn_first_deg`), speed capped by effort * max speed and by the braking distance, accel and decel limited; `Turn` faces the target; `Stop`/`Stand`/`Stomp` come to rest at the normal decel; `EmergencyStop` at the emergency decel. A destroyed body (0 hit points) emergency-stops and ignores intents.
- `PlaceholderLimits.FromWalkTuning(WalkTuning, legLength)` reads the same `walk_tuning@v1` fields the real limiter will (g-scaled accel and decel, Froude max speed, yaw limits per t*). `walk_tuning.json` is not in the tree yet, so `Default()` supplies round numbers for an ~80 m mech.
- Damage: `PlaceholderDamageRecord` (max/current hit points, hits taken, state Intact > 2/3, Damaged > 1/3, Critical > 0, Destroyed = 0). `Export()`/`Import()` give the plain data a save section stores (T-034).

## Feature flag
`units.placeholderBody` (default off, `Config/FeatureFlags/Units.Placeholder.json`). The composition installer only builds bodies with it on.

## Known gaps
No terrain following (y fixed), no collision, no snapshot interpolation of remote bodies (the net layer owns that), no damage sources yet (T-034 and the scene call `ApplyDamage`).
