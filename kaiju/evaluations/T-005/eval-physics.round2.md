VERDICT: FAIL

# T-005 eval-physics, round 2 (head bd8c19e; evidence `evidence/T-005/round2/final`, commit c1ba9af)

Scope: honesty and the walk gate from `tasks/T-005.round2.md` and the round-2 section of `expectations/T-005.md`. Judged from stored logs, summaries and metric outputs; I read code only to spot-check. Unity was not run. Written before reading the round-2 REPORT.md.

## Answers to the four checks
1. **Honest physics: yes.** `git diff dd936b9~1 bd8c19e -- Assets/Game/Config/Mech/mech_frame_balanced.json` is empty. No metric tool or threshold file changed (only `walk_tuning.json` and Mech runtime, contracts, scenarios). `extMax = extWork = 0` in every non-shove capture of `final/summary.txt` and `final/imp1/summary.txt`; shoves are the declared scripted pushes only. The only `AddForceAtPosition` under Physics/Body is the declared external-force path (`ArticulatedBody.cs:328`); LocomotionController has no AddForce or velocity writes. The imp path is flag-gated and off in the shipped config (`imp = 0`).
2. **Gate (stable unassisted walk at Fr >= 0.10): NOT MET.** Shipped imp=0 steady walk: v10 3.10 m/s, Fr 0.0226 (M-001.1, band [0.17,0.23] at the nominal; far below the 0.10 floor). All ten walk_fr* scenarios give the same 3.10 m/s, so the speed command does not change the gait. Best imp=1 stable rung: step cap 0.25, Fr 0.0160 in `step6b.md` (METRICS.md lists 0.0103), slower than imp=0 and slip 0.35 foot lengths. That is no gain over cycle 3 (0.0226) and about one fifth of the floor.
3. **Stop rule: applied, with one gap.** Step 6b ladder: cap 0.30, 0.35, 0.40 all fall (25.2, 26.6, 27.6 s), then 0.45 to 0.55 also fall: three consecutive failures, so stop is justified. Evidence was regenerated whole-capture for the shipped imp=0 gait (`final/`) and a separate imp=1 cap-0.25 comparison (`final/imp1/`, only 3 captures, so 4 M-001 items NOT_RUN). Gap: the rule says regenerate for the best stable speed. imp=0 (0.0226) is the better rung, so that choice is right, but the imp=1 set is incomplete.
4. **C20 and M-006@v1.1 readings: NOT reported.** `final/metrics/M-006.{txt,json}` contain no v1.1 block (0 matches); the verdict line is `M-006@v1` only; .1, .2, .3, .5 are "not measured: no truth-scene logs". No C20 ensemble test or number appears in `final/tests` (editmode xml and units_mech.trx: no match for ensemble, C20 or 1e-4). The spec required both v1 and v1.1 values.

## Defect table
| id | sev | defect | evidence |
|---|---|---|---|
| R2-1 | blocker | Walk gate not met: unassisted walk reaches Fr 0.0226 (imp=0) and 0.016 (imp=1), floor is 0.10. | final/summary.txt (v10 3.10 m/s); METRICS.md M-001.1; step6b.md ladder |
| R2-2 | blocker | M-006.4 touchdown peak 4.08 BW (walk_fr020) against 1.0-2.0 BW. Reproduces cycle-1 DEF-1 (4.49 BW). | METRICS.md; M-006.txt |
| R2-3 | blocker | M-006.6 peak sum P+ 2,068 MW against 450 MW over the whole capture. Reproduces DEF-2 (2,992 MW). | M-006.txt |
| R2-4 | major | M-006@v1.1 block absent and M-006.1/.2/.3/.5 not measured in the final set; the limp topple (.5, DEF-8) has no scene at all. | M-006.txt last line |
| R2-5 | major | C20 round-2 test (first divergence <= 1e-4 m, 20 s divergence inside a self-perturbation ensemble, 1 cm reported) not run or not in the evidence. DEF-9 stays open. | final/tests has no C20 result |
| R2-6 | major | The imp (impedance) path never beats the legacy walk: stable only at cap 0.25, falls at every larger step cap, and raising stiffness made it worse (runs 9, 10). Walk quality metrics M-001.2 (0.26 against [0.85,1.15]), .3 duty 0.329, M-002.1 slip 0.246 remain failing. | METRICS.md, step6b.md |
| R2-7 | major | Spec round-2 item 2 asks for yaw moment on the stance foot against the friction budget and progress-table columns (slip, touchdown peak, power peak, yaw moment). Not in the final evidence set. Step 6b says swing-hip saturation is "not observable" (no per-DOF torque field in the gait log), so the 80% angular-momentum compensation cannot be checked from logs. | step6b.md "Swing hip torque saturation"; final/ has no yaw-moment output |
| R2-8 | minor | Evidence mismatch: step6b reports imp=1 cap 0.25 as Fr 0.0160, METRICS.md as 0.01026 (steady window against 63 s). Unreconciled. | step6b.md, METRICS.md |
| R2-9 | minor | Known test reds carried: 6 KnownGap failures in Units.Mech tests (78 of 84 pass). | step6b.md Tests |
| R2-10 | minor | `intent_over_limit` fell at 61.1 s as designed, but `Clamps` logging (DEF-7) cannot be verified from this evidence. | summary.txt |

## Cycle-1 eval-physics defects
| DEF | status | note |
|---|---|---|
| 1 touchdown force | OPEN | 4.08 BW (R2-2) |
| 2 peak power | OPEN | 2,068 MW whole capture (R2-3) |
| 3 mean P+ / CoT | CLOSED by number only | 97 MW in [30,120], but at Fr 0.023, so it says nothing about 0.2 |
| 4 waist saturation | CLOSED | most saturated joint 0.71 % of steps (<= 5 %) |
| 5, 6 footfall payload / spurious event | NOT VERIFIED | latency 776 / 776 same step; payload content not re-checked |
| 7 leg joint speed / clamps | CLOSED for speed (max 2.19 rad/s, <= 3), clamps NOT VERIFIED | M-006.7 |
| 8 limp energy | OPEN | .5 not measured |
| 9 C20 | OPEN | R2-5 |
| 10, 11 mass-unit docs | NOT VERIFIED | outside the evidence read |

Open or unverified: 7 of 11. Closed: 3 (DEF-3 by number only, 4, 7 speed part).

## Report cross-check
REPORT.md is consistent with the evidence on Froude (0.0226 / 0.0103 at imp=1), the ladder, no assist, no data or threshold change, and the stop rule. It is candid that yaw_moment_nm is whole-body dL_z/dt, not the foot free moment (supports R2-7). Gaps: it never mentions C20 or M-006@v1.1 (R2-4, R2-5 are omissions, not contradictions); it counts 7 test reds (EditMode 215/222, dotnet 77/84) where step6b.md counted 6 of 84; it says truth scenes were "not re-run", so M-006.1/.2/.3/.5 have no round-2 evidence. Its Q-073 decision (round 3 trajectory optimisation) is the founders' call, not judged here.
