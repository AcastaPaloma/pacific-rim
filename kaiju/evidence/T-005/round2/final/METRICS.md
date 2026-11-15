# T-005 round 2 final metrics (commit c1ba9af; imp=0 shipped, imp=1 cap 0.25 comparison)
Tools: Tools/Metrics/metrics.py (merged T-002 tree); PY=/opt/miniconda3/envs/kaiju/bin/python. Logs gunzipped to a scratch dir
(captures renamed to the bare scenario name for imp1) before running; scratch not committed. Run from the task worktree root.
## Commands (each run once; all outputs in metrics/ and imp1/metrics/)
- `$PY metrics.py validate gait_log <g0>` -> VALID, 9 inputs; `$PY Tools/PhysicsMetrics/validate_physics_log.py <p0>` -> VALID, 8 inputs
- `$PY metrics.py m001 <g0> --out final/metrics/M-001 --label T-005` (walk_fr010/020/030, start, stop, estop, turn90_inplace, curve) exit 1
- `$PY metrics.py m002 <g0> --out final/metrics/M-002 --label T-005` (those + stomp) exit 1
- `$PY Tools/PhysicsMetrics/m006.py evidence/T-005/round2/final --commit c1ba9af` exit 0 (written to metrics/M-006.{json,txt})
- imp1: validate gait_log VALID (3); `m001`, `m002` on walk_fr010, start, stop: exit 1, 1; `m006.py final/imp1 --commit c1ba9af` exit 0
## Verdicts
| Metric | imp=0 (c1ba9af) | cycle 3 (bedecf4) | imp=1 cap 0.25 |
|---|---|---|---|
| M-001@v1 | FAIL (6 gating fail) | FAIL (6) | FAIL (6 fail, 4 NOT_RUN: only 3 captures) |
| M-002@v1 | FAIL (1 gating) | FAIL (1) | FAIL (1) |
| M-006@v1 | FAIL (.4, .6; .1/.2/.3/.5 not measured: no truth-scene logs here) | FAIL (.4, .5, .6) | FAIL (only .7 measured, pass) |
## Failing gating sub-measures
| Sub-measure | imp=0 value | threshold | cycle 3 | imp=1 |
|---|---|---|---|---|
| M-001.1 Froude steady walk | 0.02256 | [0.17, 0.23] | 0.0226 | 0.01026 (band [0.1, 0.3] in that run) |
| M-001.2 stride period/Alexander | 0.2589 | [0.85, 1.15] | 0.267 | 0.0523 |
| M-001.3 duty factor | 0.329 | [0.60, 0.72] | 0.329 | 0.404 |
| M-001.4 peak fore-aft CoM accel | 2.286 pass | <= 2.943 | 2.49 pass | 3.274 FAIL |
| M-001.5 peak lateral CoM accel | 2.423 (curve) | <= 1.148 | 2.48 | 3.037 |
| M-001.8 peak yaw accel | 76.97 deg/s2 (curve) | <= 4.038 | 85.7 | 70.7 |
| M-001.11 time to stop, normal | 2.239 t* | [2.4, 4.8] | 2.31 | NOT_RUN (no stop evaluated) |
| M-001.7 yaw rate / .12 estop time | 11.8 deg/s / 5.333 s: pass | | pass / 5.4 s pass | .7 pass 10.44 |
| M-001.6, .9, .10 | NOT_RUN | | NOT_RUN | NOT_RUN |
| M-001.13 panel | PENDING | | PENDING | PENDING |
| M-002.1 max slip / foot length | 0.2461 | < 0.005 | 0.2461 | 0.3496 |
| M-002.2 p95 slip (advisory) | 0.03731 | < 0.0025 | 0.034 | 0.1844 |
| M-006.4 walk_fr020 peak single-foot Fn (body weights) | 4.0784 | [1.0, 2.0] | 4.0266 | not measured |
| M-006.6 peak sum positive P walk_fr020/025 (MW) | 2067.8 | <= 450 | 2026.7 | not measured |
| M-006.5 limp topple energy ratio | not measured (no topple scene) | 1 +/- 0.05 | 0.913 FAIL | not measured |
M-002.3/.4/.5 pass in all three. M-006.7 passes (3.69e7 J vs 7.8e7 for imp=1 run); M-006.8 passes at imp=0.
Footfall latency: imp=0 776 contacts, same-step share 1.0; imp=1 209 contacts, 1.0.
Notes: all FAIL values are copied from the tool stdout (metrics/*.stdout.txt, imp1/metrics/*.stdout.txt); cycle 3 values from
evidence/T-005/metrics and EVIDENCE.md 0.3. imp1 M-001 reported "missing inputs" for the five captures not supplied.

## Addendum (orchestrator, after eval-physics round 2): truth scenes re-run
`unity run . -- -executeMethod Game.Physics.Body.Editor.TruthScenesCli.Run -out evidence/T-005/round2/final` (c1ba9af), then `m006.py evidence/T-005/round2/final --commit c1ba9af` again. M-006@v1 now measures every item: FAIL — .1 pass, .2 pass, .3 pass, .4 FAIL (4.08 BW vs [1, 2]), .5 FAIL (limp topple (KE + drive dissipation)/dPE 0.913 vs 1 ± 0.05; locked topple all pass, energy to rest 3.59 GJ), .6 FAIL (2068 MW vs ≤ 450), .7 pass, .8 pass. Same failing set as cycle 3. `M-006@v1.1` (passivity reading of .5) is not implemented in `m006.py`.
