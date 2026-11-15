# Net
Purpose: host-authoritative co-op transport for M1 (T-029): intents up from clients, state snapshots down from the host. No client authority over simulation state.

Breakdown IDs: F-050 area (co-op), M-057 (bandwidth/latency check). Research: `research/R-023.md`. Tasks: T-029 (core), T-030/T-030b (control bridge, seat), T-031 (host + two clients), T-030c (slots, UDP, composition, placeholder body).
Layer: L3. References only `Game.Core.Contracts` and its own assemblies (`Game.Net.ControlBridge` also references `Game.Control.Contracts`).

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Net.Contracts` | `Contracts/` | none | `ITransport`, intent packet, handshake, state snapshot, `NetContractVersions` |
| `Game.Net` | `Runtime/` | none | `NetSession` host/client, `HandshakeValidator` (flag `net.coop`), `IntentHostBuffer` (hold 5 steps), `IntentSender`, `StaleSequenceFilter`, `SnapshotCodec`, `SnapshotInterpolator`, `SimulatedLink`, `LoopbackTransport`, `UdpPeer`/`UdpHostListener` (real UDP, reliable channel with resend), `NetMeter`, `NetLog` |
| `Game.Net.ControlBridge` | `ControlBridge/` | none | `IntentCodec`, `RemotePilotSource`, `LocalSeatIntentPublisher`, `MechSeat`, `HostSeatPump`, `ClientSeatLink` (flag `net.seatBridge`) |
| `Game.Net.ControlBridge.Unity` | `ControlBridge/Unity/` | Unity | `MechSeatBehaviour`: thin shell, inert until a composition root configures it |
| tests | `Tests/EditMode`, `ControlBridge/Tests`, `ControlBridge/Unity/Tests` | test | `dotnet test DotnetTests/Net.Tests`, `DotnetTests/Net.ControlBridge.Tests`; Unity EditMode for the behaviour |

## Contracts and flow
- Client sends `IntentPacket`s (stamped, with redundancy); host validates the handshake, drops stale/duplicate sequences, holds the last intent for up to 5 steps, steps the simulation, sends snapshots (25 Hz, 13 B header + 40 B per body).
- Slots map to mechs on the host (`HostSeatPump`); unassigned slots are dropped. `Advance(stamp)` is idempotent per stamp.

## Slots: leave, timeout, reconnect (T-030c)
- The host hands out the lowest free slot (1..MaxSlots-1; 0 is the host's own). A slot is freed when the client sends `Leave` (`NetClientSession.Leave`), its link dies, it is silent for more than `NetHostSession.SlotTimeoutSteps` (0 = never; the composition root sets 250), or the host calls `Release`. Each release logs `slot_release` (A = 0 left, 1 timeout, 2 link down; B = silent steps), resets the slot's stale-sequence filter, and fires `SlotReleased`.
- A client rejoins with a fresh `NetClientSession` on a new transport and goes through the whole handshake again (flag snapshot, contract versions, step length, `net.coop`); it gets the lowest free slot, which is its old one when nothing else freed. `HostSeatPump` unassigns a released slot's remote pilot and, with `AssignPolicy` set, reassigns a joined slot to a mech.
- Refused connections are closed after `RefusedLingerSteps` (100). A client whose link dies reports `ClientState.Lost` and `link_lost`.

## Feature flags
`net.coop` (handshake refuses without it on both sides) and `net.seatBridge` (seat bridge); both registered in `Config/FeatureFlags/Net.json`, default off. The composition root (`Game.Composition.NetCoopInstaller`) installs the session and pump only with both on.

## Known gaps
No Steam adapter (F-063); no client-side prediction (the placeholder body is host-stepped, clients interpolate); a rejoining client does not recover state beyond the first snapshot. The N=2 vs N=4 clause is read as host egress and now asserted (`M057_HostEgress_TwoSlotsAtMostEightyPercentOfFour`). A two-process run (host and client as separate OS processes over UDP) is `evidence/T-030c/run.sh` (`DotnetTests/Net.CoopRun`).
