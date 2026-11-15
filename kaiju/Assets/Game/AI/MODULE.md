# AI (kaiju brains)
Purpose: decide kaiju behaviour from a bounded observation, as an intent. M1 ships the scripted baseline (R-024 rev 2); an RL tactical policy (`Policy/`, empty) comes later behind the same contract.

Breakdown IDs: pack behaviour P5, M-007 (win rate, flank/focus metrics). Research: `research/R-024.md`. Tasks: T-032, T-033, T-039 (composition).
Layer: L3. References only `Game.Core.Contracts` and its own assemblies.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.AI.Contracts` | `Contracts/` | none | `BrainContracts` (ContractVersion 1, MaxTargets 8, MaxAllies 7), `BrainObservation`, `BrainIntent`, `BrainConfig`, `ReasonCode`, `IKaijuBrain`, `BrainFlags`, `IKaijuBrainFactory` |
| `Game.AI.Scripted` | `Scripted/` | none | `ScriptedKaijuBrain` (R-024 1.4 state machine, 1.5 target selection), `ScriptedBrainModule` (runtime only) |
| `Game.AI.Harness` | `Harness/` | none, Editor-only, not auto-referenced | 2D `KaijuHarness` (kaiju and mechs with declared-DPS damage, scenarios `m007` and `m007_3v4`), `BrainLogWriter` -> `brain_log@v1`, `PackMetrics` M-007 C1-C4 |
| `Game.AI.Pack` | `Pack/` | none | T-113 pack layer (R-103 s7): `PackCoordinator` (shared `PackBoard`, hunt-vs-raid goal with hysteresis, `RoleAllocator` for packs of 1-5 over `SpeciesRole`, per-member `PackDirective`), `PackMemberBrain` wrapper + `PackBrainFactory` (`pack@1`), `IDirectedBrain`. Consumes `Game.AI.Contracts` only. Flag `ai.pack` (`Config/FeatureFlags/AI.Pack.json`, default off). `PackModule.Install` + `PackDirectory` (member -> coordinator routing), registered by Composition `PackInstaller` (needs `kaiju.scriptedBrain`; `KaijuSpeciesRoles` builds `SpeciesRole` from the Units.Kaiju specs). Follow-ups: `IInfrastructureSource` adapter over `IInfrastructureRegistry`, CommsModel (delay/loss), Track 1 adapter implementing `IBayCostField` over the T-102 depth/softness bake (T-115c delivered the planner: `GridRoutePlanner`, A* on a 250 m `IBayCostField`, behind `PackConfig.RoutePlanning`, on in `Bay()`) |
| `Game.AI.PackHarness` | `PackHarness/` | none, Editor-only, not auto-referenced | T-115 `BayHarness` (m007_bay: hunt, churn, raid, mixed, emerge on the synthetic 60 km Bay `m007_bay_synth@v1`; kinematic mechs with patrol/guard/flee and a 3 s-lock cannon; F-044 infrastructure; formation after emergence), `BayLog` -> `brain_log@v2`, the 8 control ids of R-103 5.3 (`BayBrains`). Runner: `DotnetTests/AI.PackHarness.Run`; matrix + evaluation: `Tools/m007_bay_matrix.sh`; measures: `Tools/m007_bay.py` |
| `Game.AI.Tests` | `Tests/EditMode/` | Editor | brain unit tests (`ScriptedKaijuBrainTests`); `DotnetTests/AI.Tests` compiles the same file plus the dotnet-only harness tests (`[Explicit]` M-007 sweeps) |

## Contract
`IKaijuBrain.Reset(seed, cfg)` then `Decide(in obs, ref intent)`, deterministic for a seed, no wall clock, no allocation (M-058: 0.21 us mean, 0 B). Decides every `BrainPeriodSteps`, holds the last intent otherwise. The observation lists targets and allies with a last-seen age; the brain filters allies by `AllyAwarenessM`.

## Feature flag
`kaiju.scriptedBrain` (`Config/FeatureFlags/AI.json`, default off). `ScriptedBrainModule.Install(registry, flags)` registers an `IKaijuBrainFactory` only when on; Game.Composition calls it.

## Known gaps (see `evaluations/M1-ai/eval-combined.md`, `evidence/T-033b/`)
M-007 C5 panel pending (needs a game build). In `m007_3v4` (4 kaiju vs 3 shooting mechs) C2/C3 do not discriminate against the `directcharge@1` null (same target scoring) but do against the `nofocus@1` control; C1 is 0.49 (< 0.60) under fire; thrash fraction is 0 everywhere. Visibility: `TargetObs.Alive` is the truth inside the memory window ("death is public", R-024 rev 3). Policy/ still empty.
