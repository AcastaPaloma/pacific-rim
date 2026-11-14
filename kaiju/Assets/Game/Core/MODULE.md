# Core
Purpose: the L0 foundation every module stands on: typed event bus, service registry, stable IDs, feature flags, fixed-step clock, versioned save contract, and the thin Unity adapter that boots them. Small and boring on purpose: no DI container, no reflection wiring, no async queues, no priorities, no gameplay or network types.

Breakdown IDs: F-009 (same-step delivery for footfall coupling), F-043 (stable building IDs + save contract), D-004 (N player slots), D-003 (no one-pilot/one-mech assumption), M-008 (zero-alloc publish), MODULARITY §1 rules 1–7, §3 layer L0. Task: T-001.

## Assemblies (layer L0: Core depends on nothing but the .NET base library)
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Core.Contracts` | `Contracts/` | none (`noEngineReferences`) | `IEvent`, `IEventHandler<T>`, `IEventBus`, `IServiceRegistry`, `EntityId`, `BuildingId`, `PlayerSlot`, `IFeatureFlags` + `FeatureFlagSnapshot`, `ISimulationClock`, `ISaveStore`, `SaveDocument/Section/Record/Value`, `ISaveParticipant`, `ISaveSectionMigration`, `ICoreErrorSink`, `CoreContractVersions` |
| `Game.Core` | `Runtime/` | none (`noEngineReferences`) | `EventBus`, `ServiceRegistry`, `EntityIdAllocator`, `SimulationClock`, `FeatureFlagLoader`, `CoreSettings`, `CoreServices` (engine-free composition root), `Json`, `SaveCodec`, `JsonFileSaveStore`, `SaveSystem`, `CollectingErrorSink`, `FixedStepCheck` |
| `Game.Core.Unity` | `Unity/` | UnityEngine (the only one) | `CoreRuntime` (static root), `CoreRoot`, `CoreClockDriver`, `CoreConfigAsset`, `UnityLogErrorSink` |
| `Game.Core.Tests` | `Tests/EditMode/` | test | Shared NUnit sources, also compiled by `DotnetTests/Core.Tests` |
| `Game.Core.Unity.Tests` | `Tests/UnityOnly/` | test | Adapter tests that need the engine |

Other modules reference **`Game.Core.Contracts`** (and `Game.Core` only where they construct Core types, e.g. tests or a composition root). Size: Contracts + Runtime ≈ 1,540 lines including comments and blank lines (guideline ~1,500).

## Contracts (versions in `CoreContractVersions`)
| Contract | Version | Notes |
|---|---|---|
| Event bus | 1 | `where T : unmanaged, IEvent`; rules below |
| Service registry | 1 | register/resolve by contract type |
| IDs | 1 | canonical strings `ent:<u64>`, `bld:<osm-way\|osm-relation\|landmark>:<u64>`, `slot:<index>`; golden values in `Tests/EditMode/Fixtures/ids_golden.txt` |
| Feature flags | 1 (file schema 1) | snapshot fixed per session |
| Clock | 1 | `long Step`, `StepSeconds`, `TimeSeconds = Step * StepSeconds` |
| Save envelope | schema 1 | golden `Tests/EditMode/Fixtures/save_v1.json` must load forever |
| CoreSettings file | schema 1 | |

### Event bus rules (the contract other modules rely on)
- **Typed only.** Events are `unmanaged` structs implementing `IEvent` (no strings, arrays or object references: carry IDs instead). The compiler enforces it; `EventTypes_AreUnmanagedStructs` scans every loaded type as a backstop. No string topics.
- **Order.** Per event type, handlers run in subscription order. Independent of hash codes, type names, GC and load order.
- **Same step.** `Publish` is synchronous. An event published inside a handler is queued FIFO (across all types) and delivered after the current event has reached all its handlers, but **before the outermost `Publish` returns**, so before the step ends. Never deferred to a later step, frame or coroutine.
- **Re-entrancy.** A handler receives exactly the events *published after it subscribed and before it unsubscribed*: one removed mid-dispatch gets nothing more (not even the rest of the current event); one added mid-dispatch starts with the next published event (nested events published before its subscription are not delivered to it). Unsubscribing yourself or another handler during dispatch is safe. Subscribing the same handler twice throws.
- **Runaway cap.** One outermost publish may cause at most `maxEventsPerCascade` events (`Config/CoreSettings.json`, 10,000). Beyond that the queued events are dropped, a `CascadeLimit` error is reported, and the outermost `Publish` throws `EventCascadeLimitException` naming the event types and counts. No stack overflow, no hang; the bus is usable afterwards.
- **Exceptions.** A throwing handler is caught; the others and later events still run. Each exception goes to the `ICoreErrorSink` with event type and subscriber (`<handler type>#<slot>`), and `HandlerExceptionCount` counts it. Tests use `CollectingErrorSink` and assert on it; the Unity adapter logs once per (kind, event type, subscriber) and counts every one.
- **Thread.** The bus belongs to the thread that created it (the simulation thread). Use from another thread reports `WrongThread` and throws.
- **Allocation.** Steady-state `Publish` allocates 0 bytes (measured: `evidence/T-001/alloc.txt`, both runtimes, 1/8/64 subscribers, 4-byte and 96-byte events, nested publish). `Subscribe`, `Unsubscribe`, the first publish of a new event type and buffer growth may allocate. Flag reads and clock reads allocate 0 bytes.

### Registry rules
`Register<TContract>(impl)` throws `DuplicateServiceException` (names the contract and both implementations); `Resolve<T>` throws `ServiceNotRegisteredException` (names the contract); `TryResolve` for optional services. `Dispose()` disposes `IDisposable` services once each, in reverse registration order. There is no static registry anywhere in Core.

### IDs
- `EntityId` (runtime objects): issued by `EntityIdAllocator` as 1, 2, 3…; never reused in a session (`Release` marks it dead, `IsLive` detects stale references). Save `NextValue` to continue numbering after a load.
- `BuildingId` = (`BuildingSource`, 64-bit source id), e.g. `BuildingId.From(BuildingSource.OsmWay, 123456789012)`. Independent of load order and of which other buildings exist; namespaces cannot collide. `BuildingSource` is append-only (values are saved).
- `PlayerSlot`: zero-based index, `PlayerSlot.Range(n)` for any n. There is no maximum and no count in Core.
- All: `default` is invalid, no implicit conversions, no public constructors, value equality/ordering/hashing (deterministic `IdHash.Mix`, never string or object hashing), `ToString`/`TryParse` round-trip.

### Feature flags
Folder `Assets/Game/Config/FeatureFlags/`, **one JSON file per module** (Core's is `Core.json`):
```json
{ "schema": 1, "flags": { "<module>.<feature>": false } }
```
- Files (`*.json`, top level only) are read in ordinal file-name order and merged into one `FeatureFlagSnapshot`, enumerable in ordinal flag-name order (`NameAt/ValueAt`, `ToCanonicalString()` for saves/replays/host sync).
- A flag name defined in two files, a malformed file (reported with file, line, column) or a wrong schema: every flag reads **off** and one `ConfigMalformed` error is reported. Missing folder or no files: all off plus one `ConfigMissing` warning. An unlisted name reads off. Values must be `true`/`false`; new features ship `false` (Rule 5; `Flags_RepoFile_NewFeaturesDefaultOff` checks every committed file).
- The snapshot is fixed for the session; there is no runtime toggle in M0.
- Core's flags: `core.verboseBoot` (adapter logs a boot summary).

### Clock
`Config/CoreSettings.json` `fixedStepSeconds` is the single source of truth (**0.02 s, Assumed**; `research/R-005.md` R2 recommends 1/60 s, a one-value change). The adapter sets `Time.fixedDeltaTime` to it at boot and reports `FixedStepMismatch` if the project setting differed. `CoreClockDriver` (execution order −32000) calls `Clock.Advance()` once per `FixedUpdate`. Core never reads wall-clock time, `UnityEngine.Time` or unseeded randomness.

### Save
- `SaveDocument` (envelope schema 1, writing build version) → named sections (one per module, each with the module's own version) → ID-keyed `SaveRecord`s → typed fields (`Int64`, `Float64` stored as IEEE-754 bits, `Bool`, `String`; anything else is kept verbatim as `Raw`).
- `SaveCodec` writes canonical bytes: ordinal order everywhere, one record per line, `\n`, invariant culture. Same state → same bytes under any culture (`de-DE`, `tr-TR` tested).
- `JsonFileSaveStore` writes `<path>.tmp`, flushes to disk, then `File.Replace`s the old file: a failed write leaves the previous save loadable. Load refuses a newer schema (`NewerSchema`, file untouched) and an unknown/missing schema (`UnknownSchema`); never loads silently wrong.
- `SaveSystem`: modules register an `ISaveParticipant` (own section) and `ISaveSectionMigration`s (vN→vN+1). `Restore` validates every section first (too new → `SectionTooNew`; gap in the chain → `MissingMigration`) and applies nothing unless all can be read. `Capture(loaded)` keeps sections no participant owns, unchanged. Core knows nothing about buildings.
- Compatibility promise (Assumed, proposed Q below): every schema from v1 loads forever via migration.

## How to add an event and consume it (≤ 20 lines)
```csharp
// In YourModule.Contracts:
public struct FootContact : IEvent { public EntityId Mech; public int Foot; public float ImpulseNs; }
// Publisher (any module holding IEventBus):
bus.Publish(new FootContact { Mech = id, Foot = 0, ImpulseNs = j });
// Consumer:
sealed class DustOnFootfall : IEventHandler<FootContact>, IDisposable
{
    readonly IEventBus _bus;
    public DustOnFootfall(IServiceRegistry services) { _bus = services.Resolve<IEventBus>(); _bus.Subscribe(this); }
    public void Handle(in FootContact e) { /* runs in the same step, before Publish returns */ }
    public void Dispose() => _bus.Unsubscribe(this);
}
```

## How to add a service, a flag and a save section
1. Put `IYourService` in your module's Contracts; implement it in your module.
2. In your module's bootstrap: `registry.Register<IYourService>(new YourService(registry));` (resolve dependencies from the registry in the constructor; implement `IDisposable` to unsubscribe at teardown).
3. Flag: add `Assets/Game/Config/FeatureFlags/<YourModule>.json` with your flags set to `false`; read with `flags.IsOn("yourmodule.feature")`. No Core edit.
4. Save: implement `ISaveParticipant` (section name `yourmodule.*`, your own version), register it with the `SaveSystem` from the registry; add an `ISaveSectionMigration` when you bump the version.
Worked end-to-end example: `Tests/EditMode/ExtensionProofTests.cs` (`Game.Samples.Weather`).

## Unity adapter (the only engine code)
- `CoreRuntime.Root` is the **one static composition root** in the project. It is reset on every Play Mode entry (`RuntimeInitializeOnLoadMethod(SubsystemRegistration)`), booted at `BeforeSceneLoad`, and shut down on `Application.quitting` (also fired when leaving Play Mode), so it works with domain reload disabled. Outside Play Mode `IsBooted` is false.
- Config reaches the editor and players through `Resources/GameCoreConfig.asset` (`CoreConfigAsset`), which only references the committed TextAssets. In the editor it re-reads the flag folder at each boot and before each build (`CoreConfigBuildHook`); `-executeMethod Game.Core.Unity.CoreConfigAsset.CreateOrUpdate` regenerates it.
- The adapter registers `ISaveStore` (`<persistentDataPath>/world.save.json`) and `SaveSystem` (build version = `Application.version`).

Consumes events: none. Emits events: none (Core defines no gameplay events; test events live in tests).
Config assets: `Assets/Game/Config/CoreSettings.json` (schema 1: `fixedStepSeconds`, `maxEventsPerCascade`), `Assets/Game/Config/FeatureFlags/*.json` (schema 1).
Feature flags: `core.verboseBoot` (off).

## Tests
- Shared NUnit sources `Tests/EditMode/*.cs`, run by Unity EditMode **and** `dotnet test DotnetTests/Core.Tests` (Core compiled as netstandard2.1 + C# 9 there, matching Unity 6000.6). Fixtures in `Tests/EditMode/Fixtures/`.
- Unity-only: `Tests/UnityOnly/AdapterTests.cs` (config asset, boot from committed config, fixed-step reconcile, log sink, Play Mode re-entry without domain reload).
- `BuildingId_NoCollisions_10M` is tagged `Slow`; `SuiteHygieneTests` re-runs every other shared test twice in shuffled order in one process.
- Measurements: set `KAIJU_EVIDENCE_DIR` to collect `alloc.txt`, `bench.txt`, `trace-hashes.txt`, `save-baseline.txt` lines per runner. Golden fixtures are only rewritten with `KAIJU_WRITE_GOLDEN=1` (a deliberate act).
- Metrics: proposed M-NEW (bus ns/publish, bytes/publish = 0); baseline in `evidence/T-001/bench.txt`.

Owner role: implementation/core (`impl-core`).

## Known limits / TODO
- Fixed step 0.02 s is Assumed; R-005 recommends 1/60 s (proposed Q-NEW-step).
- Save format is JSON: fine for M0 and the 100k-record baseline (`evidence/T-001/save-baseline.txt`); Bay-scale world saves may need chunked/binary per tile in the World task, behind the same `ISaveStore` contract (Q-NEW-d of draft B).
- Save compatibility promise (proposed Q-NEW "save compatibility"): every schema from v1 loads forever via migration; pre-M3 dev saves dropped only by founder decision.
- Player builds: config loading through Resources is exercised in the editor only; no player build was made in T-001.
- Threading: single simulation thread by design (reconciliation decision); no thread-safe publish.
