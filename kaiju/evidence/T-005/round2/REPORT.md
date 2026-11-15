# T-005 round 2: report (stop rule hit)

Branch `task/T-005`, final head c1ba9af (evidence 3879bc6). Approach: R-008 option (a), stance-leg joint impedance around a corrected reference. No assist, no change to `mech_frame_balanced.json`, no threshold edits, no lower band. Not merged.

## Result
- **Gate not met.** Round 2 did not reach the band (Froude ≥ 0.10). The stop rule was hit: three consecutive failed continuation increments (step cap 0.30, 0.35, 0.40 l).
- **Best stable unassisted walk:** the shipped legacy controller (`imp` = 0), M-001.1 whole-capture Froude **0.0226** (cycle 3: 0.0226), steady v10 3.10 m/s (Fr 0.023). The new impedance controller (`imp` = 1) walks stably only at the legacy step cap 0.25 l: M-001.1 0.0103, steady v10 2.57 m/s (Fr 0.016).
- Shipped config keeps `imp` = 0. The round-2 controller stays in the code behind the flag.

## Steps (evidence files in this folder)
| Step | Commit | What | Outcome |
|---|---|---|---|
| 6.1 | dd936b9 | Corrected reference (heel strike 25°, toe-off, 10° pelvic tilt, trunk share 0.9), M-NEW-c validity tests | Valid at Fr 0.10 and 0.15 (joint margin 5.30°, LIPM 0.092 / 0.081 g); not valid above 0.15 (`step1.md`) |
| 6.2 | 9f0c6f9 | Stance hip/knee/ankle joint PD to reference per support phase; virtual force as correction; CoP to toe/heel line | `step2.md` |
| 6.3 | 63f0450 | Waist and arm full tracking; yaw moment / budget / trunk share in gait_log | `step3.md` |
| 6.4 | f457759 | Feed-forward from the body's inverse dynamics (gravity, Coriolis, torque for reference acceleration) | `step4.md` |
| 6.5 | 7a5d8ab, 71f4a62 | First Unity runs: fell at 14–15 s; cause stance torque budget (vf 0.25 + double-counted distal weight); fix `imp_vf_scale` 1.0, stance gravity share 0 | Walks 63 s at Fr 0.016 (`step5.md`) |
| 6.6a | 89d4fe9 | Step length/time from the reference (cap 0.66 l); per-DOF angle log; Froude ladder scenarios | Falls at 0.5–0.66 l (`step6a.md`) |
| 6.6b | 0cb6dd6 | Load-sized stance stiffness; continuation ladder over the step cap | Stop rule (`step6b.md`) |
| 6.7 | c1ba9af, 3879bc6 | Legacy waist regression fixed (`curve` fell at 54.6 s); evidence regenerated | `final/` |

## Continuation ladder (walk_fr010, imp = 1; walk_fr015 identical, the cap binds first)
| step cap (l) | 0.25 | 0.30 | 0.35 | 0.40 | 0.45 | 0.50 | 0.55 |
|---|---|---|---|---|---|---|---|
| fell at (s) | stable 63 s, Fr 0.016 | 25.2 | 26.6 | 27.6 | 18.6 | 24.1 | 29.2 |

Stiffer stance (kload 50/100, knee-only 100) made every rung worse, including 0.25.

## Why it stops (from the per-DOF angle log)
At the first failing rung the **stance knee collapses** (−9° → +18° past the reference in 0.5 s, CoM height 40.5 → 37.8 m). At the same time the swing knee is 33–45° off and the swing hip lags its reference by 3–50°. At longer steps the hand-tuned joint impedance cannot hold the reference: stiffer gains destabilise it, softer gains let the knee fold. Swing-hip torque saturation could not be checked (no per-DOF torque in the gait log). This matches R-008 §3's risk note for option (a): "still hand-tuned".

## Metrics at c1ba9af (`final/METRICS.md`)
- Legacy (imp = 0): M-001 FAIL (6 gating: .1 Fr 0.0226 vs [0.17, 0.23]; .2 0.259; .3 0.329; .5 2.42 vs ≤ 1.148; .8 77.0 vs ≤ 4.04 deg/s²; .11 2.24 t*), M-002 FAIL (.1 slip 0.246 L_f vs < 0.005, same as cycle 3), M-006@v1 FAIL (.4 touchdown 4.08 BW, .5 limp topple 0.913, .6 power 2068 MW; .1/.2/.3/.7/.8 pass; truth scenes re-run, METRICS.md addendum). Same failing set as cycle 3.
- imp = 1 (cap 0.25): all three FAIL (M-001 also fails .4, 3.27).
- Tests: EditMode 215/222, dotnet 77/84. The 7 reds are the KnownGap tests on the legacy reference (5 × EveryJoint_HasFiveDegreesToSpare, YawMoment…AtCruiseAndAbove 0.2 and 0.3).
- Scenarios: everything holds except `intent_over_limit` (falls 61.1 s) and `shove_060` (15.0 s), both as in cycle 3.

## Evaluations
- eval-physics round 2: **FAIL** (3 blocker, 4 major, 3 minor; `evaluations/T-005/eval-physics.round2.md`). Confirms honest physics (no assist, body data unchanged, no threshold edits) and an honest stop rule. Blockers: gate not met; M-006.4 and M-006.6 (cycle-1 DEF-1/DEF-2 still open).
- eval-arch round 2: **FAIL** (0 blocker, 3 major, 6 minor; `evaluations/T-005/eval-arch.round2.md`). Majors: Physics.Body contract v1→v2 (three inverse-dynamics members) without MODULE.md updates or a wrapper test; stance schedule embedded in `ReferenceGait.cs` with a hard-coded `l / 42.0`; N-mech independence test still KnownFailing (cycle-1 arch-2).

## Not done in round 2 (brief item 6)
- The round-2 readings of C20 (seeded ensemble independence) and `M-006@v1.1` are not implemented or reported. They are evaluation work, not gait work, and do not change the gate verdict.

## Open items
- Per-DOF drive torque vs limit in the gait log (needed to diagnose the swing hip).
- `yaw_moment_nm` is whole-body dL_z/dt, not the foot free moment (no contact torque in the body contract).
- The 5-row stance schedule is still embedded in `ReferenceGait.cs` (move to config/contract).
- Reference rows below Fr 0.10 hold the 0.10 row and are outside joint range there.

## Decision needed (Q-073)
Hand-tuned option (a) stalls at the legacy speed. R-008 §5 recommends the next approach in this order: (c) offline trajectory optimisation of a full stride at Fr 0.10/0.15/0.20 under torque, friction and ROM limits (engine-free, 1–2 cycles; proves feasibility and gives consistent feed-forward), then (b) whole-body QP (2–3 cycles, expected Fr 0.17–0.20). Recommendation: approve (c) as round 3, then decide on (b) from its result. Alternatives: a declared assist (Q-014), accepting M0 below the band, or (d) a learned policy.

## Commands
- Evidence: `scripts/final.sh` (copy of the run script) (EditMode, dotnet, `ScenarioCli.Run -scenarios all -strips -physlist …`, `repeat60 -repeat 3`, imp1 cap 0.25 via `-variants`). Ladder: `scripts/run65.sh` with `scripts/variants8.txt`.
- Metrics: `final/METRICS.md`.
