FAIL

# M1 net and co-op evaluation (T-029, T-030, T-030b, T-031), combined arch + netcode

Run: `DotnetTests/Net.Tests` 62 passed, 0 failed (the Explicit ratio test is not run). `DotnetTests/Net.ControlBridge.Tests` 12/12 passed. Unity not run. Read only: `Assets/Game/Net/**`, `research/R-023.md` (M-057), `tasks/T-029.progress.md`, `ledger/HANDOFF.md`, and `evidence/regression/T-030b-merge.md` and `M1-merge-*.md`.

## Scores (0-5)
| Criterion | Score | Note |
|---|---|---|
| Host authority | 5 | The host decides slot assignment. The host binds each connection to a slot and drops packets claiming another slot (`IntentHostBuffer.Receive`). Clients only send intents. Snapshots flow host to client only. |
| Handshake and flag validation | 4 | All differences are collected without short-circuiting. Protocol, contract versions, canonical flag string and hash, step length (bit-exact), `net.coop` on both sides, and slot capacity are checked. Build version mismatch is a warning only. No leave/reconnect path (DEF-2). |
| Flags default off | 3 | Behaviour is off by default: `net.coop` is never granted without an explicit "1" in the flag snapshot, and `MechSeat.AddRemote` throws when `net.seatBridge` is off. But neither flag is registered in `Config/FeatureFlags/*.json` (DEF-3). |
| Determinism and ordering | 5 | `StaleSequenceFilter` uses wrap-safe serial arithmetic, so duplicates and old packets are dropped. `IntentHostBuffer` delivers in ascending slot order, independent of arrival order, with one intent per slot per step (fresh, held for 5 steps = 100 ms, then neutral). The wire repeat of 2 previous packets covers single losses. Tests exist for out-of-order, duplicate, wrap, burst, 2% loss and silent slots. |
| Loss, reordering, RTT | 4 | The snapshot interpolator has a stale flag. The tests run RTT 50/100/150 at 0% and 2% loss. No test for the outage cell (400 ms) or the 5% stress cell from M-055/M-056. |
| Bandwidth vs M-057 | 4 | Numbers match the code (see below). The N=2 vs N=4 clause is deliberately left unresolved (DEF-5). |
| Engine-free split and asmdefs | 5 | Contracts, Net and ControlBridge all have `noEngineReferences: true`. The Unity shell `Game.Net.ControlBridge.Unity` is the only engine-referencing assembly and is thin. Dependencies run downward only: Contracts to Core.Contracts, Net to Contracts, ControlBridge to Net and Control.Contracts. |
| Docs and tests with module | 2 | Tests live with the module. `MODULE.md` is missing (DEF-1). |

## M-057 arithmetic check (from `tasks/T-029.progress.md` and the code)
- Snapshot size is 13 + 40 per body. At N=2 (11 bodies) that is 453 B; at N=4 (13 bodies) it is 533 B. Add 60 B assumed overhead, send at 25 Hz (every 2nd of 50 Hz steps), and apply about 2% loss: 12.5 kB/s (N=2) and 14.5 kB/s (N=4). These match the logged 12543 and 14499 B/s. The per-client ratio is 0.865 and the arithmetic is correct. The host-egress reading (1 client vs 3 clients) gives about 0.29, which the log rounds to 1/3. That is correct, so the ratio-test decision is not an arithmetic error.
- Absolute limits pass: down about 14.5 kB/s against a 64 kB/s limit, up about 10.9 kB/s against a 16 kB/s limit.
- The measured parameters differ from the R-023 M-057 text: 25 Hz instead of 20 Hz, 40 B per body instead of 32 B, and a 120 s steady run only (no 10 s join-burst cell). See DEF-4 and DEF-5.

## Break attempts (all covered by existing tests or by code reading, none broke)
- Duplicate and out-of-order datagrams: dropped (`OutOfOrderAndDuplicates_AppliedOnce_InSequenceOrder`).
- A packet claiming another slot, an unassigned slot, or a malformed bundle: dropped and counted. `HostSeatPump.ApplyIntent` ignores slots with no mech.
- Overflow: a fifth client receives `SessionFull`. Sequence wrap is accepted. A bundle with a count outside 1..3 or the wrong length is rejected.
- Flag off: `AddRemote` throws, and `ClientSeatLink.Advance` never sends.

## Defects
- **DEF-M1net-1 (blocking)**: `Assets/Game/Net/MODULE.md` does not exist (it exists for Core, Control, Camera and others). MODULARITY.md rule 1 requires one per module, covering purpose, interfaces, events, config, tests, owner role and GAME_BREAKDOWN IDs. A single file is also expected to cover the Contracts, Runtime and ControlBridge sub-assemblies. PASS: a `Net/MODULE.md` that lists F-063 and the public types (`ITransport`, `NetHostSession`, `NetClientSession`, `HostSeatPump`, `MechSeat`), flags, the `net_log@v1` and wire versions, and the test locations.
- **DEF-M1net-2 (minor)**: `NetHostSession` has no leave, disconnect or reconnect path. `OccupiedSlots` only ever increases (`NetSession.cs`, HandleHello `OccupiedSlots++`). A refused connection is dead, and a hello from an already-joined connection is silently ignored. A dropped client permanently leaks its slot, and a client that rejoins gets a new slot. `IntentHostBuffer.UnregisterSlot` exists but nothing calls it. PASS: leave and timeout release a slot, or a documented M1 limitation with a test.
- **DEF-M1net-3 (minor)**: `net.coop` and `net.seatBridge` are absent from `Assets/Game/Config/FeatureFlags/` (no `Net.json`). MODULARITY.md rule 5 wants the flags registered and default off there. PASS: `Net.json` with both flags set to false, and a test that loads it.
- **DEF-M1net-4 (minor)**: Evidence for T-029, T-030 and T-031 lives only in `tasks/*.progress.md` and the regression notes. There is no `evidence/T-029`, `evidence/T-030` or `evidence/T-031` directory with the M-055/M-057 run output or a `net_log` sample. The M-057 tests print their numbers only to the test console. PASS: committed logs or tables under `evidence/<task>/` that reproduce the quoted figures.
- **DEF-M1net-5 (minor)**: The test `M057_TwoSlotsAtMostEightyPercentOfFour` is `[Explicit]` and would fail if run (ratio 0.865). The decision to read the clause as host egress is logged in `ledger/HANDOFF.md`, but no test asserts that reading, so the clause has no enforced check. The M-057 text in R-023 also still shows 20 Hz and 32 B per body, which no longer match the code (25 Hz, 40 B). PASS: a host-egress ratio test (N=2 <= 0.8 of N=4), and the spec text updated to the real rates (or ratified as is).
- **DEF-M1net-6 (minor)**: There is no test for the M-055/M-056 stress cell: RTT 150 ms, 5% loss, forced 400 ms outage, recovery within 1 s. The stale-snapshot path is exercised only lightly. PASS: a scripted outage test showing the stale flag, hold then neutral on the host, and recovery.
