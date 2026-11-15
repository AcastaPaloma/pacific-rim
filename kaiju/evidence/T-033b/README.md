# T-033b: M-007 3v4 (4 kaiju vs 3 shooting mechs), 100 seeds per brain

Reproduce (logs are ~4 MB each, 300 files, not committed; deterministic, 13 s):
```
KAIJU_M007_SCENARIO=3v4 KAIJU_M007_NOFOCUS=1 KAIJU_M007_OUT=<dir> KAIJU_M007_N=100 \
  dotnet test DotnetTests/AI.Tests --filter Name~M007_Scenario_Sweep -- NUnit.RunExplicit=true
/opt/miniconda3/envs/kaiju/bin/python Tools/Metrics/metrics.py m007 <dir> --out evidence/T-033b/m007_3v4
```
`m007_3v4/report.json` and `summary.txt` are the tool output (300 inputs hashed: `scripted@1`, `directcharge@1`, `nofocus@1`, seeds 1-100).

Harness combat (Assumed, harness only): kaiju HP 400, mech 6 dmg/s within 300 m (1 shot/s on the nearest live kaiju), kaiju dead at 0; Retreat from the brain's own hp < 0.35 rule. Retreat entered in 93/100 scripted runs (77/100 directcharge), 74 kaiju deaths over 100 scripted runs (45 directcharge, 81 nofocus); mechs killed 62 / 189 / 59.

| Clause | scripted@1 | directcharge@1 (null) | nofocus@1 | discriminates? |
|---|---|---|---|---|
| C1 FR_commit (>= 0.60, >= 2 x null) | 0.494, CI 0.38-0.60 **FAIL** | 0.019, CI 0-0.04 | - | yes vs null (non-overlap) |
| C2 FE (>= 0.50) | 0.923, CI 0.87-0.96 | 0.906, CI 0.85-0.95 | 0.731, CI 0.65-0.80 | **no vs null (overlap)**, yes vs nofocus |
| C3 switch rate (<= 3 /kaiju-min) | 0.075, CI 0.04-0.11 | 0.118, CI 0.07-0.18 | 0.208, CI 0.12-0.32 | **no vs null**, yes vs nofocus |
| C3 thrash fraction (<= 0.10) | 0 | 0 | 0 | no (carries no information here) |
| C4 leaks | 0 | 0 | - | n/a |

Diagnosis (one cycle): the damage scenario did not fix C2/C3 against `directcharge@1`, because that null shares the scripted target scoring (distance, hp, facing, and the focus bonus), so nearest-target preference alone yields FE 0.91; the metric's chance level (uniform random targets) is not an uncoordinated-but-sensible baseline. Scenario not at fault. Fix cycle: a second control `nofocus@1` (WeightFocus 0), diagnostic CI-overlap fields in the tool; thresholds untouched. Against it C2 and C3 (switch rate) discriminate with non-overlapping CIs. C1 dropped from 0.915 (2 invulnerable-target decoys, 3 kaiju) to 0.494 under fire with 4 kaiju: left as a finding, not tuned.

Unity EditMode (asmdef split): `unity_editmode.log`; dotnet and pytest counts in `../regression/T-033b.md`.

## Rev 3 re-run (supersedes the table above for C2/C3)
`m007_3v4_rev3/` and `m007_churn_rev3/` (tool output, 300 logs each, scripted/directcharge/nofocus). Reproduce with `KAIJU_M007_SCENARIO=3v4|churn KAIJU_M007_NOFOCUS=1` as above. See R-024 "Revision 3 applied" for the numbers.
