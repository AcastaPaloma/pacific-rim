# AI (kaiju brains)
Purpose: decide kaiju behaviour from a bounded observation, as an intent. M1 ships the scripted baseline (R-024 rev 2); an RL tactical policy (`Policy/`, empty) comes later behind the same contract.

Breakdown IDs: pack behaviour P5, M-007 (win rate, flank/focus metrics). Research: `research/R-024.md`. Tasks: T-032, T-033, T-039 (composition).
Layer: L3. References only `Game.Core.Contracts` and its own assemblies.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.AI.Contracts` | `Contracts/` | none | `BrainContracts` (ContractVersion 1, MaxTargets 8, MaxAllies 7), `BrainObservation`, `BrainIntent`, `BrainConfig`, `ReasonCode`, `IKaijuBrain`, `BrainFlags`, `IKaijuBrainFactory` |
| `Game.AI.Scripted` | `Scripted/` | none | `ScriptedKaijuBrain` (R-024 1.4 state machine, 1.5 target selection), `ScriptedBrainModule`, `Harness/` (2D `KaijuHarness`, `BrainLogWriter` -> `brain_log@v1`, `PackMetrics` M-007 C1-C4) |
| tests | `DotnetTests/AI.Tests` | test | NUnit, dotnet only (M-007 sweep and evidence writers are `[Explicit]`); Unity mirror not done |

## Contract
`IKaijuBrain.Reset(seed, cfg)` then `Decide(in obs, ref intent)`, deterministic for a seed, no wall clock, no allocation (M-058: 0.21 us mean, 0 B). Decides every `BrainPeriodSteps`, holds the last intent otherwise. The observation lists targets and allies with a last-seen age; the brain filters allies by `AllyAwarenessM`.

## Feature flag
`kaiju.scriptedBrain` (`Config/FeatureFlags/AI.json`, default off). `ScriptedBrainModule.Install(registry, flags)` registers an `IKaijuBrainFactory` only when on; Game.Composition calls it.

## Known gaps (see `evaluations/M1-ai/eval-combined.md`)
Harness ships in the runtime assembly; "death is public" visibility rule not in the spec; M-007 C5 panel pending (needs a game build); C2/C3 do not discriminate against the null with 2 mechs.
