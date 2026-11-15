FAIL
# M1-ai combined evaluation (T-032 contract + scripted brain, T-033 R-024 rev 2 / M-007 tool). eval-arch + eval-ai, read-only.

Run by me: `DotnetTests/AI.Tests` 27 passed / 0 failed / 2 skipped-by-design (M007_Scenario_Sweep, WriteEvidence); Tools/Metrics `pytest -k "m007 or brain or registry"` 103 passed. Note: no `expectations/T-032|T-033.md` exist, so spec = R-024, M-007@v1.yaml, code.

## Scores
| Criterion | Score | Note |
|---|---|---|
| Contract version (BrainContracts.ContractVersion=1, brain exposes it, brain_log schema v1) | PASS | `Assets/Game/AI/Contracts/BrainContracts.cs:9` |
| Determinism (seeded, no wall clock) | PASS | no DateTime/Random in brain; Stopwatch only inside harness timing (`KaijuHarness.cs:160`), same seed same sha test passes; brain consumes no RNG |
| No allocation in Decide | PASS | `Decide_AllocatesNothing` passes; stackalloc Span only |
| Information fairness (observation spec) | PASS with minors | see DEF-M1ai-3, -4 |
| M-007 C1-C4 result honesty | PASS | report.json/summary.txt: FR_commit 0.9145 (107/117) vs null 0.0147, FE 0.936, switch 0.0155, leaks 0; C5 PENDING, C6 N/A, verdict INCOMPLETE exit 3, "PASS (provisional: 3 assumed)", all C1-C3 thresholds `assumed_pending_calibration`. Nothing overstated. |
| Flag `kaiju.scriptedBrain` default off | PASS | `Assets/Game/Config/FeatureFlags/AI.json:4` false; no consumer reads it yet |
| Validator on malformed brain_log | PASS | truncated file, NaN row -> INVALID_INPUT exit 2; 20 runs -> INCOMPLETE exit 3; injected 40 not-visible/age-9 target rows -> C4 FAIL 40 ticks exit 1 |
| Modularity (MODULE.md, tests with module) | FAIL | DEF-M1ai-1, -2 |
| Robustness of Decide to bad input | minor FAIL | DEF-M1ai-5 |

## Defects
- **DEF-M1ai-1 (blocking)**: `Assets/Game/AI/` has no `MODULE.md` (find returns none); MODULARITY requires folder + asmdef + MODULE.md per module, and the contract version, observation/action spec and flag need a home. PASS: `Assets/Game/AI/MODULE.md` listing Contracts/Scripted/Policy, contract version 1, observation spec, flag `kaiju.scriptedBrain`.
- **DEF-M1ai-2 (minor)**: tests live only in `DotnetTests/AI.Tests/Dotnet` (no `Assets/Game/AI/**/Tests` EditMode mirror, unlike Core), `Assets/Game/AI/Policy/` is an empty folder. Harness (file IO, Stopwatch, log writer) ships in the runtime asmdef `Game.AI.Scripted`. PASS: tests under the module, harness in an editor/tools asmdef.
- **DEF-M1ai-3 (minor)**: `Alive` of an out-of-sight remembered target is the truth (`KaijuHarness.cs:283`, comment "death is public"). A kaiju learns of a kill it cannot see; the C4 leak metric (visible/age check) does not catch it. PASS: Alive frozen at last-seen value unless visible, or the "death is public" rule written into R-024 section 2 and the M-007 spec.
- **DEF-M1ai-4 (minor)**: observation hands the brain every ally regardless of range (`FillObs`, lines 288-296); only the brain's own `AllyAwarenessM` filter (`ScriptedKaijuBrain.cs:77,104,...`) enforces the 1500 m limit. A future RL policy would see out-of-range allies. PASS: the observation builder filters by awareness range.
- **DEF-M1ai-5 (minor)**: `Wrap` (`ScriptedKaijuBrain.cs:56-62`) loops forever on a huge or infinite heading: probe with target `HeadingRad=1e10f` hangs Decide (3 s timeout). PASS: bounded wrap (`a - TwoPi*floor(...)`) or non-finite guard.
- **DEF-M1ai-6 (minor)**: M-007 C2/C3 do not discriminate: control (DirectCharge) FE 0.946 > scripted 0.936, thrash 0 for both (`evidence/T-033/m007/report.json` extra.m007; only 2 mechs, chance FF 0.75). Reported honestly as assumed thresholds, but a PASS on C2/C3 is not evidence of smartness. PASS: C2 compared against the null (scripted - null) or scenario with >=3 targets.
- **DEF-M1ai-7 (minor)**: harness kaiju never take damage (Hp stays 1, mechs do not fight back), so Retreat/Withdrawn paths are covered only by unit tests, not by the M-007 runs; the 200 input logs hashed in report.json live under `/private/tmp/claude-501/m007`, not in the repo, so the evidence is not reproducible from the repo alone. PASS: logs (or a reproduce command + seeds) in `evidence/T-033/`.

Counts: blocking 1, minor 6.
