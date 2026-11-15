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
| `Game.AI.Tests` | `Tests/EditMode/` | Editor | brain unit tests (`ScriptedKaijuBrainTests`); `DotnetTests/AI.Tests` compiles the same file plus the dotnet-only harness tests (`[Explicit]` M-007 sweeps) |

## Contract
`IKaijuBrain.Reset(seed, cfg)` then `Decide(in obs, ref intent)`, deterministic for a seed, no wall clock, no allocation (M-058: 0.21 us mean, 0 B). Decides every `BrainPeriodSteps`, holds the last intent otherwise. The observation lists targets and allies with a last-seen age; the brain filters allies by `AllyAwarenessM`.

## Feature flag
`kaiju.scriptedBrain` (`Config/FeatureFlags/AI.json`, default off). `ScriptedBrainModule.Install(registry, flags)` registers an `IKaijuBrainFactory` only when on; Game.Composition calls it.

## Known gaps (see `evaluations/M1-ai/eval-combined.md`, `evidence/T-033b/`)
M-007 C5 panel pending (needs a game build). In `m007_3v4` (4 kaiju vs 3 shooting mechs) C2/C3 do not discriminate against the `directcharge@1` null (same target scoring) but do against the `nofocus@1` control; C1 is 0.49 (< 0.60) under fire; thrash fraction is 0 everywhere. Visibility: `TargetObs.Alive` is the truth inside the memory window ("death is public", R-024 rev 3). Policy/ still empty.
