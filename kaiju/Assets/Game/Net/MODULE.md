# Net
Purpose: host-authoritative co-op transport for M1 (T-029): intents up from clients, state snapshots down from the host. No client authority over simulation state.

Breakdown IDs: F-050 area (co-op), M-057 (bandwidth/latency check). Research: `research/R-023.md`. Tasks: T-029 (core), T-030/T-030b (control bridge, seat), T-031 (host + two clients).
Layer: L3. References only `Game.Core.Contracts` and its own assemblies (`Game.Net.ControlBridge` also references `Game.Control.Contracts`).

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Net.Contracts` | `Contracts/` | none | `ITransport`, intent packet, handshake, state snapshot, `NetContractVersions` |
| `Game.Net` | `Runtime/` | none | `NetSession` host/client, `HandshakeValidator` (flag `net.coop`), `IntentHostBuffer` (hold 5 steps), `IntentSender`, `StaleSequenceFilter`, `SnapshotCodec`, `SnapshotInterpolator`, `SimulatedLink`, `LoopbackTransport`, `NetMeter`, `NetLog` |
| `Game.Net.ControlBridge` | `ControlBridge/` | none | `IntentCodec`, `RemotePilotSource`, `LocalSeatIntentPublisher`, `MechSeat`, `HostSeatPump`, `ClientSeatLink` (flag `net.seatBridge`) |
| `Game.Net.ControlBridge.Unity` | `ControlBridge/Unity/` | Unity | `MechSeatBehaviour`: thin shell, inert until a composition root configures it |
| tests | `Tests/EditMode`, `ControlBridge/Tests`, `ControlBridge/Unity/Tests` | test | `dotnet test DotnetTests/Net.Tests`, `DotnetTests/Net.ControlBridge.Tests`; Unity EditMode for the behaviour |

## Contracts and flow
- Client sends `IntentPacket`s (stamped, with redundancy); host validates the handshake, drops stale/duplicate sequences, holds the last intent for up to 5 steps, steps the simulation, sends snapshots (25 Hz, 13 B header + 40 B per body).
- Slots map to mechs on the host (`HostSeatPump`); unassigned slots are dropped. `Advance(stamp)` is idempotent per stamp.

## Feature flags
`net.coop` (handshake refuses without it on both sides) and `net.seatBridge` (seat bridge); both default off (absent from the flag files, which reads as off). Registering them in `Config/FeatureFlags` is open (LEDGER).

## Known gaps
Slot release/reconnect, a two-process run, and the M-057 join-burst/outage cells. The N=2 vs N=4 clause is read as host egress (decision in `ledger/HANDOFF.md`).
