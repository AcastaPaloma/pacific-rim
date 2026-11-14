# T-001 evidence (Core)

**Code under test:** commit `b03202f` on branch `task/T-001` (recorded in `raw/commit.txt`). Every artifact below was produced by one command, run from the project root (`kaiju/`):

```
evidence/T-001/tools/run_all.sh
```

It runs, in order: `dotnet build DotnetTests/Core.Tests`; `dotnet test DotnetTests/Core.Tests` **twice as separate processes** (`KAIJU_EVIDENCE_DIR=evidence/T-001/raw/dotnet-runN`, TRX logger); a forced Unity recompile (`rm -rf Library/ScriptAssemblies/Game.Core* Library/Bee/artifacts/*/Game.Core*`, then `unity run <project> --no-tail --log-file raw/unity-compile.log --timeout 1500 -- -executeMethod Game.Core.Unity.CoreConfigAsset.CreateOrUpdate`); `unity test <project> --mode EditMode -o evidence/T-001/unity-editmode.xml` (`KAIJU_EVIDENCE_DIR=evidence/T-001/raw/unity`); `tools/test_names.py`; `tools/grep.sh`; `git check-ignore -v`. Measurement files merge the lines the tests themselves write (`Evidence.Line`) per runner/process.

**Tools:** dotnet SDK 11.0.100 (tests on net11.0; Core compiled as netstandard2.1, C# 9.0); Unity 6000.6.9f1 batch mode, Mono (`clr-version 4.0.30319.42000` in the XML), NUnit 3.5 (Unity) / 3.14 (dotnet); macOS 26.6.2, Apple M5 Pro (`versions.txt`).

## Headline results
| Runner | Total | Passed | Failed | Artifact |
|---|---|---|---|---|
| `dotnet test` (run 1 / run 2) | 89 / 89 | 89 / 89 | 0 / 0 | `dotnet-test.trx`, `dotnet-test.log`, `raw/dotnet-test-run2.trx` |
| Unity EditMode | 94 (89 shared + 5 Unity-only) | 94 | 0 | `unity-editmode.xml` |
| Shared-name diff | 0 only-in-dotnet, 0 only-in-Unity | | | `test-name-diff.txt` |
| Unity compile (forced) | 0 `error CS`, 0 `warning CS` (all assemblies) | | | `compile.log`, `raw/unity-compile.log` |
| Allocation, steady state | 0 bytes (dotnet) / 0 GC allocations (Unity) in all 12 publish scenarios + flag/clock + ID ops | | | `alloc.txt` |

## Criteria
Test names are `Game.Core.Tests.<Fixture>.<Test>` (shared: both runners) unless marked **(Unity-only)** = `Game.Core.Unity.Tests.AdapterTests.*`. Result is "pass" in both runners unless stated. Commit `b03202f` for all rows.

| C# | Test(s) / artifact | Result / measured value |
|---|---|---|
| C1 Layering | `StructureTests.Layering_CoreAsmdefsAreEngineFree`, `StructureTests.Layering_NoNetOrGameplayTypesInCore`; asmdefs `Assets/Game/Core/Contracts/Game.Core.Contracts.asmdef`, `Assets/Game/Core/Runtime/Game.Core.asmdef` (`noEngineReferences: true`, `overrideReferences: true`, no precompiled refs), `Assets/Game/Core/Unity/Game.Core.Unity.asmdef` (only engine assembly); `raw/dotnet-build.log` (0 warnings, 0 errors); `dotnet-references.txt` (only NUnit/Test SDK packages and the two Core projects; no Unity DLLs); `grep-counts.txt` §C1 | pass; 0 Unity mentions in Contracts/Runtime; exactly 1 non-test engine assembly |
| C2 One source, two runners | `dotnet-test.trx` + `unity-editmode.xml`; `test-name-diff.txt` (sorted names, explicit Unity-only list of 5, empty diff); dotnet Core build targets netstandard2.1 / LangVersion 9.0 (`DotnetTests/Directory.Build.props`, `DotnetTests/Core/Core.csproj`) | 89 shared names identical; all pass |
| C3 Clean compile | `compile.log` (Csc lines for Game.Core.Contracts, Game.Core, Game.Core.Unity, Game.Core.Tests, Game.Core.Unity.Tests after deleting their outputs) | 0 errors, 0 warnings |
| C4 Extension proof | `ExtensionProofTests.ExtensionProof_SampleModule_NoCoreChanges` (namespace `Game.Samples.Weather`: own event `WeatherChanged`, service `IWeatherService`, flag file `SampleWeather.json` added beside the committed flag files, save section `sample.weather`) | pass; no Core file touched |
| C5 MODULE.md | `Assets/Game/Core/MODULE.md` | all §6 fields, contract versions, ordering/re-entrancy/exception/cap rules, allocation guarantee, how to add an event/service/flag/save section, 14-line example, Q links |
| C6 Repo hygiene | `check-ignore.txt` (`DotnetTests/**/*.csproj` tracked via `!/DotnetTests/**/*.csproj`; root `Assembly-CSharp.csproj`/`Game.Core.csproj` still ignored; `DotnetTests/**/bin`, `obj` ignored; metas present for every file/folder under `Assets/Game/Core` and `Assets/Game/Config`) | missing metas: 0 |
| C7 Same-step delivery | `EventBusTests.Bus_DeliversAllEventsWithinPublishingStep`, `EventBusTests.Bus_PublishFromHandler_IsQueuedFifoAfterCurrentEvent`, `ClockTests.Clock_ManualStepping_DrivesBus` | pass |
| C8 Deterministic order | `EventBusTests.Bus_DispatchTrace_IsReproducible` (12 handlers, 2 event types, nested publishes, 200 random subscribe/unsubscribe/publish ops, 100 repeats, compared with committed golden hash); `trace-hashes.txt` | `dispatch 6F72236CAACE7678` in dotnet run 1, dotnet run 2 and Unity |
| C9 Re-entrancy | `EventBusTests.Bus_PublishFromHandler_IsQueuedFifoAfterCurrentEvent`, `Bus_UnsubscribeOtherDuringDispatch_RemovedGetsNothingMore`, `Bus_UnsubscribeSelfDuringDispatch_IsSafe`, `Bus_SubscribeDuringDispatch_StartsWithNextPublishedEvent`, `Bus_DoubleSubscribe_Throws` | pass |
| C10 Runaway protection | `EventBusTests.Bus_PublishCycle_FailsLoudlyNamingEventTypes` (cap 100 via constructor; committed cap `maxEventsPerCascade` 10000 read in `ClockTests.Clock_StepComesFromCommittedConfig`) | throws `EventCascadeLimitException` naming PingEvent and PongEvent; bus usable afterwards |
| C11 Fault isolation | `EventBusTests.Bus_ThrowingHandler_OthersStillReceive_ErrorReported`; **(Unity-only)** `Adapter_LogSink_LogsOncePerSubscriberAndEventType_CountsAll`; wrong thread: `EventBusTests.Bus_PublishFromAnotherThread_FailsLoudly` | pass |
| C12 Zero allocation | `AllocationTests.Bus_Publish_AllocatesNothing_SteadyState(subs ∈ {1,8,64}, event ∈ {PingEvent 4 B, BigEvent 96 B}, nested ∈ {no, yes})` = 12 cases, 1,000 warm-up + 10,000 measured publishes each; `AllocationTests.FlagAndClockReads_AllocateNothing`; `IdTests.Ids_ValueSemantics_AllocationFree`; positive control `AllocationTests.AllocMeter_DetectsAllocations`; `alloc.txt` | dotnet: 0 bytes (`GC.GetAllocatedBytesForCurrentThread`; control 88,000 bytes). Unity/Mono: 0 GC allocations (`ProfilerRecorder("GC.Alloc")`, the Test Framework's own method; control 1,000). Mono does not report bytes (control shows 0), so in Unity "0 allocations" is the 0-byte proof. Subscribe/unsubscribe may allocate (stated in MODULE.md). |
| C13 Typed events only | `EventBusTests.Bus_TypedIsolation`, `EventBusTests.EventTypes_AreUnmanagedStructs` (scans every loaded `IEvent` type; checker's negative controls included); `where T : unmanaged, IEvent` in `Contracts/Events.cs`; `grep-counts.txt` §C13 | pass; 0 string topics |
| C14 Throughput (informational) | `AllocationTests.Bus_Throughput_Baseline(1/8/64)`; `bench.txt` | ns/publish dotnet ≈ 73 / 99–103 / 311–348; Unity Mono 72.5 / 81.9 / 216.5 (1/8/64 subscribers) |
| C15 Explicit failures | `RegistryTests.Registry_ResolveMissing_NamesContract`, `Registry_DuplicateRegistration_FailsNamingBothImplementations` | pass |
| C16 No hidden global state | `RegistryTests.Registry_And_Bus_InstancesAreIndependent`; `SuiteHygieneTests.Suite_PassesTwiceInShuffledOrder_InOneProcess` (87 cases × 2 shuffled passes, seeds 1 and 0xC0FFEE; `test-hygiene.txt`: 0 failures in all three runs); `StructureTests.NoMutableStaticFieldsInCore` (reflection over both Core assemblies) + `grep-counts.txt` §C16 (0 in Core; adapter has exactly `CoreRuntime.s_root`); **(Unity-only)** `Adapter_EnterPlayModeTwice_NoDomainReload_FreshStateEachTime` (EditMode `[UnityTest]` that sets Enter Play Mode Options = DisableDomainReload + DisableSceneReload and enters Play Mode twice: fresh root, no leaked subscriber or service, no duplicate-registration error, clock driven by FixedUpdate, `Time.fixedDeltaTime` = config; log line "play mode re-entry: first root …, second root …" in `unity-editmode.xml`). Composition root documented in MODULE.md "Unity adapter". | pass. Note: this re-entry check runs inside the EditMode run, so there is no separate `unity-playmode.xml`. |
| C17 Orderly teardown | `RegistryTests.Registry_Teardown_DisposesReverseOrderOnce` | pass |
| C18 Typed and safe IDs | `IdTests.Ids_NoImplicitConversions_NoPublicConstructors`, `Ids_DefaultIsInvalid`, `Ids_ValueSemantics_AllocationFree` | pass |
| C19 Stable across processes | `IdTests.Ids_SerialiseRoundTrip_AndCrossProcessStable`; golden fixture `Assets/Game/Core/Tests/EditMode/Fixtures/ids_golden.txt`; `trace-hashes.txt` (`ids_golden 62A5FD0C1BB7FD0E` in all three runs); `grep-counts.txt` §C27/C19 (0 `NewGuid`/`GetInstanceID`; 0 `GetHashCode()` calls in `Ids.cs`) | pass |
| C20 Building IDs from source key | `IdTests.BuildingId_FromSourceKey_OrderIndependent`, `BuildingId_LargeValues_NoCrossSourceCollision`, `BuildingId_NoCollisions_10M` (category `Slow`, runs in both runners) | 10,000,002 IDs, 0 collisions (`grep-counts.txt`) |
| C21 No stale aliasing | `IdTests.EntityId_NoReuseWithinSession` (1,000,000 allocate/release cycles; never-reuse) | pass |
| C22 No hard-coded counts | `IdTests.PlayerSlots_SupportN(1,2,3,16,64)`, `StructureTests.NoHardCodedCountsInCore`; `grep-counts.txt` §C22 | pass; 0 hits |
| C23 Off by default, loudly | `FlagTests.Flags_Unknown_IsOff`, `Flags_MissingFile_AllOffOneWarning` (missing folder and empty folder: one warning each), `Flags_Malformed_ReportsAndAllOff` (7 cases, line/column checked), `Flags_Folder_MergesFilesInOrdinalOrder_DuplicateIsError` | pass |
| C24 Committed file tested | `FlagTests.Flags_RepoFile_NewFeaturesDefaultOff` loads every committed file in `Assets/Game/Config/FeatureFlags/` (currently `Core.json`: `core.verboseBoot=0`) in both runners; **(Unity-only)** `Adapter_ConfigAsset_PointsAtCommittedFiles`, `Adapter_CreateRoot_FromCommittedConfig_AllFlagsOff` (the player path through Resources); a module adds a flag by adding its own file (C4 test) | pass |
| C25 Session snapshot | `FlagTests.Flags_Snapshot_StableAndSerialisable` (ordinal order independent of file order; `ToCanonicalString`; no setter) | pass |
| C26 Step index is time | `ClockTests.Clock_StepComesFromCommittedConfig` (`Assets/Game/Config/CoreSettings.json` `fixedStepSeconds` 0.02, marked Assumed), `Clock_NoDriftAfter10MSteps` (10^7 steps: derived 200000 exactly vs naive float sum 191361.89; `bench.txt`), `Clock_FixedStepCheck_DetectsMismatch`; **(Unity-only)** `Adapter_ReconcileFixedStep_ReportsMismatch`, and `Time.fixedDeltaTime` checked inside Play Mode in the C16 test | pass. `research/R-005.md` R2 now recommends 1/60 s; the value stays 0.02 s Assumed per the brief (one config value to change). |
| C27 No hidden wall clock | `StructureTests.NoWallClockOrUnseededRandomInCore`, `ClockTests.Clock_ManualStepping_DrivesBus`; `grep-counts.txt` §C27 | 0 hits |
| C28 Step replay | `ClockTests.Core_StepReplay_Deterministic` (CoreServices: clock, bus, registry, entity IDs, flags; 6 agents, nested events, mid-run unsubscribe; 1,000 steps twice, byte comparison, golden hash); `trace-hashes.txt` | `replay1000 CE7CBB4259A6A39B` in all three runs |
| C29 Round-trip | `SaveTests.Save_RoundTrip_100k` (100,000 records keyed by BuildingId incl. > 2^32, random float bit patterns incl. NaN payloads, Int64, Bool); `save-baseline.txt` | 0 mismatches; 13,544,521 bytes; write ≈ 130 ms, read ≈ 430 ms (dotnet) / 488 ms (Unity) |
| C30 Versioned | `SaveTests.Save_GoldenV1_Loads` (fixture `Assets/Game/Core/Tests/EditMode/Fixtures/save_v1.json`, carries `schema` and `build`), `Save_NewerVersion_RefusedUntouched` (bytes and mtime unchanged), `Save_UnknownVersion_NoSilentLoad` (7 cases), `Save_Migration_V1ToV2_Sample` (test-only section v2), `Save_SectionTooNewOrUnmigratable_Refused`; sample file `sample-save.json` | pass |
| C31 Crash-safe | `SaveTests.Save_InterruptedWrite_PreviousIntact` (stream fails after half the bytes; previous file byte-identical and loadable; stale `.tmp` from a "crash" ignored) | pass |
| C32 Locale-proof bytes | `SaveTests.Save_ByteIdentical_UnderForeignCulture(de-DE)`, `(tr-TR)` | pass |
| C33 Unknown data preserved | `SaveTests.Save_UnknownSections_PreservedThroughLoadSave` (future section with arrays, fractions, huge numbers, non-hex `f64`); `StructureTests.SaveCodeKnowsNothingAboutBuildings`; `grep-counts.txt` §C33 | pass; 0 hits |

## Criteria not met
None. Qualifications, stated rather than hidden:
- C12 under Unity is measured as allocation **count** (0), because Unity's Mono returns 0 from `GC.GetAllocatedBytesForCurrentThread` even for known allocations (positive control in `alloc.txt`).
- C16's Play Mode check is a `[UnityTest]` inside the EditMode results, not a separate PlayMode XML.
- The size guideline (soft): Contracts + Runtime = 1,537 lines in total, 1,293 non-blank non-comment lines (`grep-counts.txt`).
- Player builds were not made; the config path through `Resources` is exercised in the editor only.

## Artifact index
`dotnet-test.trx`, `dotnet-test.log`, `unity-editmode.xml`, `test-name-diff.txt`, `alloc.txt`, `bench.txt`, `trace-hashes.txt`, `save-baseline.txt`, `test-hygiene.txt`, `grep-counts.txt`, `compile.log`, `check-ignore.txt`, `dotnet-references.txt`, `versions.txt`, `sample-save.json`, golden fixtures `Assets/Game/Core/Tests/EditMode/Fixtures/{ids_golden.txt,save_v1.json}`, raw logs and second-process TRX in `raw/`, scripts in `tools/`.
