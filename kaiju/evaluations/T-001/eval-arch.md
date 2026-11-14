PASS

# T-001 Core: eval-arch

Code under test: branch `task/T-001`, HEAD `75839dc` (evidence built from `b03202f`, same Core sources). Spec: `expectations/T-001.md` v1.
Own runs: `dotnet test DotnetTests/Core.Tests` = 89/89 passed (matches `evidence/T-001/dotnet-test.trx`). `unity test <WT> --mode EditMode` = 94/94 passed, 0 failed (`/private/tmp/claude-501/eval-T-001-arch/editmode.xml`); the 5 extra names are exactly `AdapterTests.*` (Unity-only list). Tree stayed clean after both runs. No other Unity batch process was running. Two runs are not a full independent proof for C8 cross-process, C12 Unity numbers, C14 or C29 timing: those are judged from the stored `evidence/T-001/*.txt` (marked "stored").

Method: read all Core Contracts/Runtime/Unity sources for bus, IDs, flags, save store; read MODULE.md; read 10 tests critically (Bus_DeliversAllEventsWithinPublishingStep, Bus_PublishFromHandler_IsQueuedFifo..., Bus_SubscribeDuringDispatch..., Bus_PublishCycle_FailsLoudly..., Bus_ThrowingHandler..., Bus_Publish_AllocatesNothing_SteadyState, AllocMeter_DetectsAllocations, Ids_NoImplicitConversions, EntityId_NoReuseWithinSession, BuildingId_NoCollisions_10M, Flags_RepoFile_NewFeaturesDefaultOff, Flags_Malformed..., Save_NewerVersion_RefusedUntouched, Save_UnknownSections_Preserved..., StructureTests, SuiteHygieneTests); wrote a throwaway probe against the built Core DLLs (`/private/tmp/claude-501/eval-T-001-arch/probe/P.cs`) for sink-throws, unsub/resubscribe in dispatch, cap abort, 200,000-deep nesting.

## Criteria
| C | Verdict | Support |
|---|---|---|
| C1 | PASS | `Contracts/Game.Core.Contracts.asmdef`, `Runtime/Game.Core.asmdef`: `noEngineReferences:true`, `references` only Contracts, `precompiledReferences:[]`; `grep UnityEngine Contracts Runtime` = 0 (own); one adapter `Unity/Game.Core.Unity.asmdef` (`noEngineReferences:false`); `StructureTests.Layering_*`; Core.csproj netstandard2.1/C# 9, no Unity DLLs (own `dotnet test` built clean) |
| C2 | PASS | own runs: 89 dotnet / 94 Unity; Unity-only = 5 `AdapterTests` (own XML); `evidence/T-001/test-name-diff.txt` empty diff; `DotnetTests/Core.Tests/Core.Tests.csproj` compiles `Assets/Game/Core/Tests/EditMode/**/*.cs` |
| C3 | PASS (stored) | `evidence/T-001/compile.log`: forced recompile of 5 Core assemblies, 0 error CS, 0 warning CS. Not re-forced by me (my run reused the compiled assemblies) |
| C4 | PASS | `ExtensionProofTests.cs:54-59` builds flag folder in a temp dir (copies committed files, adds `SampleWeather.json`); sample event/service/save section in namespace `Game.Samples.Weather`; diff touches no sample code in Core |
| C5 | PASS | `Assets/Game/Core/MODULE.md`: all 9 template fields (Purpose, Breakdown IDs, Contracts + versions, Consumes/Emits, Config assets, Feature flags, Tests, Owner role, Known limits with Q-NEW links); order/re-entrancy/exception/cap/alloc rules; add event/service/flag/save recipes; 14-line example. See DEF-5 for the missing PlayMode line |
| C6 | PASS | `.gitignore` diff adds `!/DotnetTests/**/*.csproj` plus bin/obj/TestResults ignores; own `git check-ignore -v`: `DotnetTests/Core/Core.csproj` not ignored by `*.csproj`... tracked via the `!` rule, `foo/Bar.csproj` still ignored (rule 11), `DotnetTests/TestResults/x.trx` ignored. Own scan of `git ls-files Assets/Game`: every file and folder added by the task has a `.meta` (only pre-existing `.gitkeep` lack one) |
| C7 | PASS | `EventBus.cs:61-81` (queue drained inside outermost Publish); `Bus_DeliversAllEventsWithinPublishingStep` asserts the nested pong arrived at the same clock step before the next `Advance` (can fail: uses `Has.Member` after each Publish) |
| C8 | PASS (stored for cross-process) | order = subscription order, FIFO queue across types, no hash use (`EventBus.cs`); `Bus_DispatchTrace_IsReproducible` 100 repeats + golden hash `6F72236CAACE7678`, which passed in my two runs (dotnet and Unity); `trace-hashes.txt` shows same hash in 3 processes |
| C9 | PASS | tests `Bus_PublishFromHandler_IsQueuedFifo...`, `Bus_UnsubscribeOther...`, `Bus_UnsubscribeSelf...`, `Bus_SubscribeDuringDispatch...`; semantics via `_since[i] >= seq` (`EventBus.cs:233`) and null-slot removal. My probe: unsubscribe+resubscribe same handler inside dispatch behaves per documented rule |
| C10 | PASS | `Bus_PublishCycle_FailsLoudly...`; probe: cap 5 gives "Event cascade started by A exceeded maxEventsPerCascade=5 (refused B). Events in cascade: A x3, B x2"; 200,000-deep nested publish via queue did not overflow the stack |
| C11 | PASS with DEF-1 | `Bus_ThrowingHandler_OthersStillReceive_ErrorReported` asserts others run, type, subscriber, count; `HandlerExceptionCount`. Fails only when the sink itself throws (DEF-1) |
| C12 | PASS (stored for Unity) | `AllocationTests`: 12 publish cases + flag/clock + positive control `AllocMeter_DetectsAllocations`; passed in both my runs; `evidence/T-001/alloc.txt` 0 bytes / 0 GC.Alloc. Unity cannot report bytes (Mono), so count is used and stated. Event struct 96 B covers the >= 64 B case |
| C13 | PASS | `where T : unmanaged, IEvent` on every bus method (compile-time); `EventTypes_AreUnmanagedStructs` with negative controls; no string-keyed Subscribe/Publish (own grep) |
| C14 | PASS (informational) | `bench.txt` stored: dotnet ~73/100/330 ns, Unity 72/82/217 ns for 1/8/64 subs |
| C15 | PASS | `Services.cs` Duplicate/NotRegistered exceptions; `RegistryTests.Registry_ResolveMissing_NamesContract`, `..._DuplicateRegistration_FailsNamingBothImplementations` |
| C16 | PASS | own grep `static` in Contracts/Runtime: only operators, one get-only `FeatureFlagSnapshot.AllOff` (immutable) and no `static readonly` collections; `StructureTests.NoMutableStaticFieldsInCore` (reflection); `SuiteHygieneTests` double shuffled pass (passed in my runs); adapter has one static `CoreRuntime.s_root` reset on `SubsystemRegistration`; `Adapter_EnterPlayModeTwice_NoDomainReload...` passed in my EditMode run. See DEF-5 |
| C17 | PASS | `RegistryTests.Registry_Teardown_DisposesReverseOrderOnce` |
| C18 | PASS | `Ids.cs`: readonly structs, private ctors, no op_Implicit/Explicit, default invalid; `IdTests.Ids_NoImplicit...`, `Ids_DefaultIsInvalid`, `Ids_ValueSemantics_AllocationFree` |
| C19 | PASS | `IdHash.Mix` only (no string/object hash, no Guid/InstanceID: own grep 0); golden `Fixtures/ids_golden.txt`; `ids_golden` hash equal in 3 processes (stored); `IdTests.Ids_SerialiseRoundTrip_AndCrossProcessStable` |
| C20 | PASS | `BuildingId` = (`BuildingSource` byte, ulong); `BuildingId_FromSourceKey_OrderIndependent`, `_LargeValues_NoCrossSourceCollision`, `_NoCollisions_10M` (10,000,002 ids). See DEF-3 on how strong the 10M test is |
| C21 | PASS | `EntityIdAllocator` never reuses; `EntityId_NoReuseWithinSession` 1,000,000 cycles asserts strictly increasing ids and stale `IsLive` false |
| C22 | PASS | `PlayerSlots_SupportN(1,2,3,16,64)`; `PlayerSlot.Range(n)`; `StructureTests.NoHardCodedCountsInCore`; no `Player1/2` names; no type pairs a player with one mech |
| C23 | PASS | `Flags_Unknown_IsOff`, `Flags_MissingFile_AllOffOneWarning` (folder missing and empty), `Flags_Malformed_ReportsAndAllOff` (7 cases incl. line/column) |
| C24 | PASS | `Assets/Game/Config/FeatureFlags/Core.json` (`core.verboseBoot:false`) + `.meta`; `Flags_RepoFile_NewFeaturesDefaultOff` loads the committed folder and asserts every flag off, in both runners; folder scheme one file per module, no Core edit to add a flag (C4 proof); Unity side `CoreConfigAsset.RefreshFromFolder` + `CoreConfigBuildHook` pick up new files |
| C25 | PASS | `FeatureFlagSnapshot` immutable, ordinal sorted, `NameAt/ValueAt/ToCanonicalString`; `Flags_Snapshot_StableAndSerialisable` incl. no `Set` method |
| C26 | PASS | `Config/CoreSettings.json` `fixedStepSeconds 0.02` marked Assumed; `ClockTests.Clock_NoDriftAfter10MSteps`, `Clock_StepComesFromCommittedConfig`, `Clock_FixedStepCheck_DetectsMismatch`; adapter sets `Time.fixedDeltaTime` and reports mismatch (`CoreRuntime.cs` `ReconcileFixedStep`); no hard-coded 0.02/10000 in Core (own grep) |
| C27 | PASS | own grep of Contracts/Runtime for DateTime/Stopwatch/Random/Guid/Time.: 0 (only the test project uses Stopwatch); `StructureTests.NoWallClockOrUnseededRandomInCore` |
| C28 | PASS | `ClockTests.Core_StepReplay_Deterministic` 1,000 steps twice + golden hash `CE7CBB4259A6A39B` (passed in my runs) |
| C29 | PASS (stored for timing) | `SaveTests.Save_RoundTrip_100k` passed in my runs (floats as IEEE bits incl. NaN payloads, ids > 2^32); `save-baseline.txt` 13.5 MB, write ~130 ms, read ~430 ms |
| C30 | PASS | `Save_GoldenV1_Loads` with committed `Fixtures/save_v1.json`; `Save_NewerVersion_RefusedUntouched` (bytes and mtime unchanged); `Save_UnknownVersion_NoSilentLoad` 7 cases; `Save_Migration_V1ToV2_Sample` |
| C31 | PASS | `Save.cs:147-165` temp file + flush + `File.Replace`/`Move`; `Save_InterruptedWrite_PreviousIntact` (injected stream failure mid-write). Simulated failure, not a process kill; see DEF-6 |
| C32 | PASS | canonical writer ordinal/invariant (`SaveCodec`); `Save_ByteIdentical_UnderForeignCulture(de-DE, tr-TR)` |
| C33 | PASS | `Save_UnknownSections_PreservedThroughLoadSave` (arrays, nulls, huge numbers, bad `f64`) round-trips byte-identically; `SaveCodeKnowsNothingAboutBuildings`; own grep "Building" in both Save.cs = 0 |

## Other checks (no spec letter)
- `.unity`/`.prefab` text edits: none in the diff; `GameCoreConfig.asset` was produced by the Editor method (`CreateOrUpdate`) and has its `.meta`.
- Hard-coded tuning in Core: none (step and cascade cap come from `CoreSettings.json`; `CoreSettings.Parse` has no silent default).
- LINQ / reflection / allocation in the publish path: none (`EventBus.Publish` uses ring buffers and typeof keys; the `Type.Name` calls are only on the abort path, `EventBus.cs:121`).
- Dependencies: Core -> nothing, adapter -> Contracts + Core, test asmdefs `autoReferenced:false` with `UNITY_INCLUDE_TESTS`.
- Size: Contracts + Runtime 1,537 lines against the ~1,500 soft guideline (stored `grep-counts.txt`); acceptable.

## Defects (all minor; none fails a criterion)
**DEF-T001-arch-1 (minor): a throwing `ICoreErrorSink` permanently wedges the bus.**
Evidence: probe `P.cs`: bus built with a sink whose `Report` throws; first `Publish` threw "sink boom" and the second subscriber got nothing; the second `Publish` returned normally with `ok.N=0` (events silently never delivered). Cause: `EventBus.cs:70-81` sets `_dispatching = true` with no `try/finally`, and `ReportHandlerException` (`EventBus.cs:90-96`) is not guarded.
PASS would look like: `_dispatching`/queue state restored in a `finally` (or the sink call wrapped), plus a test with a throwing sink; or `ICoreErrorSink` documents "must not throw" and the bus fails loudly rather than going silent.

**DEF-T001-arch-2 (minor): tests leave temp directories behind.**
Evidence: `ProjectPaths.NewTempDir()` (`TestSupport.cs:162-166`) creates `$TMPDIR/kaiju-core-tests/<guid>` and nothing deletes them; 123 directories present after the task's runs plus mine.
PASS: a per-fixture `[TearDown]`/`[OneTimeTearDown]` that deletes them.

**DEF-T001-arch-3 (minor): two assertions cannot fail meaningfully.**
Evidence: `IdTests.cs:23` `Assert.That(typeof(EntityId), Is.Not.EqualTo(typeof(BuildingId)))` is always true. `BuildingId_NoCollisions_10M` (`IdTests.cs:163-177`) checks sort-adjacent equality of (source, id) pairs generated with an odd multiplier, which is collision-free by construction; it would only fail if `CompareTo`/`==` broke. The structural property the spec wants (source namespace plus 64-bit id) is still enforced by the type, so no criterion is unmet.
PASS: drop the tautology; add a case where two different sources with the same raw id compare unequal AND hash/sort apart, plus a check that two raw ids differing only above bit 32 stay distinct (partly present in `_LargeValues_...`).

**DEF-T001-arch-4 (minor): error path allocates every time; MODULE.md claims are silent on it.**
Evidence: `EventBus.ReportHandlerException` builds strings per exception (`EventBus.cs:90-96`) and `UnityLogErrorSink.Report` builds a key string on every report (`UnityLogErrorSink.cs:19-20`), so one persistently throwing handler allocates on every publish, despite the "0 bytes" headline for M-008.
PASS: MODULE.md "Allocation" states that handler exceptions allocate (and that tests fail on any), or the dedupe key avoids per-call strings.

**DEF-T001-arch-5 (minor): no PlayMode assembly; MODULE.md "Tests" line omits PlayMode.**
Evidence: `MODULE.md` Tests section lists EditMode and Unity-only EditMode; the Play Mode re-entry proof is a `[UnityTest]` in the EditMode run (`Adapter_EnterPlayModeTwice_NoDomainReload_FreshStateEachTime`), as the implementer's EVIDENCE.md itself says. MODULARITY §5 step 5 and §6 name a PlayMode entry. Passed in my run, so C16 holds.
PASS: one sentence in MODULE.md stating that PlayMode coverage is the `[UnityTest]` above and why no PlayMode assembly exists yet.

**DEF-T001-arch-6 (minor): save store assumes the directory exists and does not sync it.**
Evidence: `JsonFileSaveStore.Save` (`Save.cs:147-165`) opens `<path>.tmp` with `FileMode.Create` without creating the parent directory; a missing directory becomes a `SaveWriteException`. No directory fsync after rename (power loss window), and crash safety is only tested by an injected stream failure, not a killed process.
PASS: `Directory.CreateDirectory(parent)` before writing (with a test) and a note in MODULE.md about the rename-only durability.

## Not independently re-run
C3 compile log (stored; my run reused compiled assemblies), cross-process trace hashes (C8, C19, C28: stored 3-process file, my two runners reproduced the same golden hashes), C12 Unity allocation numbers and C14/C29 timings (my Unity run passed the same assertions; numbers stored), player-build config path (not built, stated by implementer).
