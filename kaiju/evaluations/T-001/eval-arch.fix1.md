PASS

# eval-arch re-check, T-001 fix cycle 1 (branch task/T-001-fix1)

Re-run: `dotnet test` in `DotnetTests/Core.Tests`: 96 total, 96 passed, 0 failed (matches fix1.md). Allocation/burst/throwing-sink subset: 21 passed. Unity results (fix1/unity-editmode.xml, 101/101): stored, not independently re-run.
Diff scope: only `Assets/Game/Core/`, `evidence/T-001/`, `tasks/`; nothing outside the allowed paths. No public contract changed; the throwing-sink rule is recorded in MODULE.md as a clarification of v1 (no version bump needed, behaviour was undefined before).

| Defect | Status | Evidence |
|---|---|---|
| DEF-T001-arch-1 | closed | `EventBus.cs:73-97`: dispatch loop in try/finally; `_dispatching=false` always; `ResetAfterFault` (`:149`) clears touched channels' pending, queue, dirty/cascade counters, abort flag when the loop did not complete. Exit paths: throwing handler is caught per handler (unchanged), so the loop completes; throwing sink (from handler report or from a nested cap Abort reported inside a handler) leaves the loop, finally resets, sink exception propagates; nested publish returns early at `:72` and touches no flag; cap exceeded sets `_aborted`, loop drains the cleared queue, completed, `EndCascade` resets then throws `EventCascadeLimitException`; a top-level Abort cannot occur because the cap is >= 1 (`:35`). Tests `Bus_ThrowingErrorSink_FailsLoudly_ThenRecovers` and `_OnCascadeLimit_Recovers` (EventBusTests) would fail on old code (bus stayed `_dispatching`, so zero deliveries afterwards); they also check no stale queued event and the counters reset. Steady-state allocation: `Bus_Publish_AllocatesNothing_SteadyState` and `Bus_Burst_SecondBurstAllocatesNothing(5000)` pass, 0 B; try/finally adds no allocation. |
| DEF-T001-arch-2 | closed | `TestSupport.cs` tracked dirs, `[SetUpFixture] TempDirCleanup` deletes after run; `TestSupportTests.TempDirs_AreDeletedByCleanup` (does not compile on old code). Run-to-run count not re-measured by me. |
| DEF-T001-arch-3 | closed | Tautology removed; `IdTests.BuildingId_SameRawIdOtherSource_UnequalHashAndSortApart` and `..._RawIdsDifferingOnlyAboveBit32_StayDistinct` assert equality, hash, order and ToString, and fail if source or high bits were ignored. |
| DEF-T001-arch-4 | closed | MODULE.md "Allocation": states the error path allocates and that tests fail on any handler exception. |
| DEF-T001-arch-5 | closed | MODULE.md Tests: names the `[UnityTest]` Play Mode re-entry and why no PlayMode assembly exists yet. |
| DEF-T001-arch-6 | closed | `Save.cs:152-153` `Directory.CreateDirectory(parent)` inside the existing try; `SaveTests.Save_MissingParentDirectory_IsCreated` (old code threw `SaveWriteException`); MODULE.md notes rename-only durability. |
| DEF-T001-perf-1 | closed | `AllocationTests.Bus_Burst_SecondBurstAllocatesNothing(5000)`: 5,000 nested events in one cascade, second burst measured, asserts 0 bytes and 0 count where the meter reports; my dotnet re-run passed. |
| DEF-T001-perf-2 | closed (no code change requested) | Mono count-only note already in EVIDENCE.md; Unity number not independently re-run. |

## New defects
- DEF-T001-arch-7 (nit): fix1.md says the rest-of-cascade drop on a throwing sink is "loud"; the dropped events are not named in the exception, only the sink's own exception surfaces. Acceptable for a contract violation ("sink must not throw"); no action required.
- No blocker or major defects introduced. MODULE.md ID fixes (M-017, Q-032) match the fix's claim; I did not re-verify them against `ledger/ID_MAP.md` content beyond the stated IDs.
