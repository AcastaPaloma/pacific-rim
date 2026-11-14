# Control
Purpose: turn pilots into one intent per mech (layer L3). A pilot is a device or a remote player; a seat holds any number of them; an arbiter merges their inputs into one `IntentFrame` (the move and where it lands: D-013). Nothing here knows a body, a joint or a camera, and no other module reads a device.

Breakdown IDs: F-002, D-013, D-003 (several pilots later), D-004 (N players). Extension point: MODULARITY 4.1. Task: T-005.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Control.Contracts` | `Contracts/` | none | exactly the MODULARITY 4.1 types: `IPilotSource { PilotInput Read(); }`, `struct IntentFrame { MoveId move; Vector3 target; float effort; int pilots; }`, `IIntentArbiter { IntentFrame Merge(IReadOnlyList<PilotInput> inputs); }`, `class PilotSeat { List<IPilotSource> pilots; IIntentArbiter arbiter; IntentFrame Read(); }`, plus `PilotInput`, `MoveId` (`Vector3` is `System.Numerics.Vector3`: the contract is engine-free) |
| `Game.Control` | `Runtime/` | none | `SinglePilotArbiter` (the only shipped arbiter), `ScriptedPilotSource` (timeline keyed by Core clock step), `StickMapping`, `ControlTuning` = `control_tuning@v1` |
| `Game.Control.Unity` | `Unity/` | UnityEngine + Input System | `DevicePilotSource` (gamepad left stick, WASD, stop button) |
| `Game.Control.Tests` / `.PlayTests` | `Tests/` | test | below |

## Contract (version 1)
- `MoveId`: `Stand`, `Walk` (to `target`, `effort` scales speed), `Turn` (face `target` on the spot), `Stop`, `EmergencyStop`, `Stomp`. Append-only (values are logged). Brace, GetUp and strikes are later tasks: new enum values, no change to the seat or the arbiter.
- `PilotInput.valid = false` means "this pilot says nothing this step".
- Limits are not applied here: an intent may ask for anything; the unit controller limits it and reports why (`Units.Mech` `IntentLimiter`).
- To add an arbiter (split limbs, consensus): one new class implementing `IIntentArbiter`, assigned to `PilotSeat.arbiter`. Proven by `MeanArbiter`, which exists only in the test assemblies and merges 2 and 3 pilots.

Consumes events: none. Emits events: none.
Config assets: `Config/Control/control_tuning.json` (dead zone, target distance, emergency-stop hold time).
Feature flags (`Config/FeatureFlags/Control.json`, default off): `control.devicePilot`.

## Tests
- Engine-free (EditMode and `dotnet test DotnetTests/Control.Tests`): the contract's shape by reflection, single arbiter passes through, an empty seat stands, 2 and 3 pilots through a test-only arbiter, scripted timeline by clock step, stick mapping.
- PlayMode: `DevicePilotSource` is an `IPilotSource` interchangeable with the scripted one in the same seat; runs with no device present; refuses another tuning schema.

Owner role: implementation/mech (`impl-mech`).

## Known limits / TODO
- The device source's stick and stop readers are delegates (default: Input System `Gamepad.current` / `Keyboard.current`). The default readers are exercised without a device only: injecting device state in a batch-mode play test did not reach the device (focus), so real-device reading is unverified by automation.
- No rebinding, no per-player device assignment (Net / UI tasks).
