# T-001 fix cycle 1: evidence

Branch `task/T-001-fix1` (from `origin/claude/fable-orchestrator-prompt-83a0a8` @ d2a2249). Fix commits: dcfe309 (arch-1), 4f59d51 (arch-2, arch-3, arch-6, perf-1), 5d83439 (arch-4, arch-5, MODULE.md IDs). Tests run on 5d83439.

| Defect | What changed (file) | Test | Before fix | After fix |
|---|---|---|---|---|
| DEF-T001-arch-1 | `Publish` dispatch loop in `try/finally`; on an abnormal exit `ResetAfterFault()` clears queue/pending, cascade counters, abort flag; sink exception propagates (loud). `EndCascade` split into `ResetCascadeState` + throw. No allocation added (`Runtime/EventBus.cs`) | `EventBusTests.Bus_ThrowingErrorSink_FailsLoudly_ThenRecovers`, `EventBusTests.Bus_ThrowingErrorSink_OnCascadeLimit_Recovers` | Fail (bus wedged: 0 deliveries after fault) | Pass |
| DEF-T001-arch-2 | `ProjectPaths.NewTempDir` tracks dirs; `DeleteTempDirs()`; `[SetUpFixture] TempDirCleanup` deletes them after the run (`Tests/EditMode/TestSupport.cs`) | `TestSupportTests.TempDirs_AreDeletedByCleanup` | Fail (does not compile: no cleanup existed) | Pass; `$TMPDIR/kaiju-core-tests` count 153 before and after a full run (0 new) |
| DEF-T001-arch-3 | Tautological `typeof` assertion removed; two new ID tests (`Tests/EditMode/IdTests.cs`) | `IdTests.BuildingId_SameRawIdOtherSource_UnequalHashAndSortApart`, `IdTests.BuildingId_RawIdsDifferingOnlyAboveBit32_StayDistinct` | Pass (test-quality defect: no code bug to expose; they now fail if equality, hash or order ignores source or high bits) | Pass |
| DEF-T001-arch-4 | Doc: `MODULE.md` "Allocation" states the error path allocates (exception strings, sink dedupe key) | none (doc) | n/a | n/a |
| DEF-T001-arch-5 | Doc: `MODULE.md` Tests section names the Play Mode re-entry `[UnityTest]` and why no PlayMode assembly exists | none (doc) | n/a | Test itself passes in the Unity run |
| DEF-T001-arch-6 | `JsonFileSaveStore.Save` creates the parent directory (`Runtime/Save.cs`); `MODULE.md` notes rename-only durability (no directory fsync) | `SaveTests.Save_MissingParentDirectory_IsCreated` | Fail (`SaveWriteException`) | Pass |
| DEF-T001-perf-1 | New burst case (`Tests/EditMode/AllocationTests.cs`); `MODULE.md` Allocation line updated | `AllocationTests.Bus_Burst_SecondBurstAllocatesNothing(5000)` | Pass (coverage gap, unchanged code) | Pass; dotnet: first burst 328,072 B, second burst 0 B (`fix1/raw-dotnet/alloc.txt`); Unity: count-only meter, assertion passed |
| DEF-T001-perf-2 | No code change (as the evaluator stated); Mono count-only note already in `EVIDENCE.md` | `Bus_Publish_AllocatesNothing_SteadyState` (unchanged) | n/a | Pass |
| doc: MODULE.md IDs | M-NEW → M-017, save compatibility Q-NEW → Q-032 (`ledger/ID_MAP.md`); fixed step: "Assumed 0.02 s in Core config; R-005 recommends 1/60 s, applied by T-005 as a config change"; draft-B save-format proposal has no ID in `ID_MAP.md` (stated as such) | none (doc) | n/a | n/a |

Before-fix column: the new tests were run against the pre-fix `Runtime/EventBus.cs` and `Runtime/Save.cs` (from d2a2249): 4 failed (the 2 sink tests, the save test, and `SuiteHygieneTests` which re-runs them), 92 passed.

## Runs
| Runner | Command | Total | Passed | Failed | Artifact |
|---|---|---|---|---|---|
| dotnet | `cd DotnetTests/Core.Tests && dotnet test --logger "trx;LogFileName=<abs>/evidence/T-001/fix1/dotnet-test.trx"` | 96 | 96 | 0 | `fix1/dotnet-test.trx` |
| Unity 6000.6.9f1 EditMode | `unity test <project> --mode EditMode -o <abs>/evidence/T-001/fix1/unity-editmode.xml` | 101 (96 shared + 5 Unity-only) | 101 | 0 | `fix1/unity-editmode.xml` |
| Name diff | `python evidence/T-001/tools/test_names.py fix1/dotnet-test.trx fix1/unity-editmode.xml fix1/test-name-diff.txt` | | | | only-in-dotnet 0, only-in-Unity 0 |

Contracts: no version change. The throwing-sink behaviour is a clarification of event bus contract v1 (previously undefined), noted in `MODULE.md`.
