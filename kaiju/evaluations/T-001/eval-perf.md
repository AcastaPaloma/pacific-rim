PASS

# eval-perf T-001 (Core) - code under test: task/T-001 @ b03202f
Machine: Apple M5 Pro, macOS, dotnet SDK 11 (net11.0 tests, Core netstandard2.1), Debug config. Unity numbers are stored values, not independently re-run.
Re-run: `dotnet test DotnetTests/Core.Tests --filter AllocationTests|Save_RoundTrip_100k` -> 18/18 passed; output in /private/tmp/claude-501/eval-T-001-perf/.

## C12 Zero steady-state allocation - PASS
- Stored: dotnet 0 bytes in all 12 publish scenarios (subs 1/8/64 x 4 B/96 B event x nested no/yes), flag+clock reads 0, ID ops 0; Unity/Mono 0 GC.Alloc counts (bytes not reportable on Mono; control shows it).
- Re-measured (dotnet, this machine): identical, 0 bytes in all 12 scenarios and flag/clock reads; positive control 88,000 bytes (so the meter works).
- Unity: not independently re-run; stored alloc.txt `## unity` shows 0 counts, control 1000.
- Code review of the publish path (Runtime/EventBus.cs): no closures, no boxing (`in T` with `unmanaged, IEvent` constraint, typed `Channel<T>` with `T[]` ring buffers, handlers via `IEventHandler<T>` interface call), no foreach over interface enumerables (index loops only), no string work on the hot path (type names are built only in Abort/ReportHandlerException, with a comment about Type.Name on Mono), dictionary keyed by `Type` reference (no struct-key boxing, no allocation on lookup). `CheckThread` reads Environment.CurrentManagedThreadId (no allocation). Only `try/catch` per handler, no allocation unless a handler throws.
- Test critique (Tests/EditMode/AllocationTests.cs, TestSupport.cs AllocMeter): measured on the same thread as the publisher (GC.GetAllocatedBytesForCurrentThread; Unity ProfilerRecorder); delegate `run` is created before measuring; 1,000 warm-up publishes then 10,000 measured; delivery counts asserted (subs x (10,000+1,000), nested pong count), so a no-op bus cannot pass; the nested case has a handler that publishes a second type. The test cannot hide a per-publish allocation: warm-up is only 10 percent of the load and ring-buffer growth is bounded by the first few publishes. I found no way for it to pass while the code allocates per publish.
- Stated limits (not defects): subscribe/unsubscribe, first publish of a new event type, and queue/ring growth allocate (documented in the EventBus class comment and MODULE.md).

## C14 Throughput baseline (informational) - recorded
- Stored (dotnet run 1/2): 73 / 99-103 / 311-348 ns per publish at 1/8/64 subs; Unity Mono 72.5 / 81.9 / 216.5.
- Re-measured: 72.2 / 98.8 / 303.5 ns (ns/delivery 72.2 / 12.4 / 4.7). Within 3 percent of stored for 1 and 8, 3-13 percent faster at 64.
- Plausible: a fixed cost of about 65-70 ns per publish (thread check, dictionary lookup, enqueue to two rings, cascade bookkeeping) plus about 5 ns per extra delivery. Debug build, so Release would be lower. Implication: 5,000 events per 20 ms step with 8 subscribers is about 0.5 ms (about 3 percent of a 16.7 ms frame); 20,000 events x 8 subs would be about 2 ms. Fixed per-publish overhead dominates over delivery; fine for the planned load.

## C29 Save round-trip baseline (informational) - recorded
- Stored: 100,000 records, 13,544,521 bytes (about 135 B/record), write about 131 ms, read about 430 ms (dotnet), 488 ms (Unity).
- Re-measured: 13,544,521 bytes (identical), write 141.8 ms, read 474.2 ms; test passed (0 mismatches). Run-to-run spread of about 10 percent, with a concurrent build in the same worktree.
- Plausible for text JSON with hex-float encoding. Implication: a 100k-record save would block about 0.5 s on load and 0.13 s on save if done on the simulation thread in one frame; at that scale it needs a background thread or a loading screen, and the file size (13.5 MB) argues for compression or a binary format later. Read is 3.3x slower than write, so the parser is the thing to optimise first if it matters.

## Defects
- DEF-T001-perf-1 (minor): the allocation test never exercises a burst deeper than the warm-up (queue/ring growth after warm-up is not covered); a module that publishes 1,000 events in one step the first time will allocate once, which is stated but not measured. PASS would look like: one extra case with a burst of N events (for example 5,000 in one cascade) measured on the second burst, 0 bytes.
- DEF-T001-perf-2 (minor): C12 in Unity is count-only (Mono reports no bytes) and I did not re-run it; a ProfilerRecorder count of 0 is accepted as the proof. PASS would look like: the same, plus a note in EVIDENCE.md (already present) - no code change needed.
- No blocker or major defects. 0 blocker, 0 major, 2 minor.
