# T-005 round 13: measured rear-CoP servo on the rear ankle in double support (qp_r13_copservo); NOT STABLE, DS rule failed, 2 Unity cycles (24 runs)

Branch task/T-005-r13 (from r12 801976a), code commit 7ce060b. Scenario walk_fr010 (63 s), walk_mode 0, L7 control, 4 seeds (qp_pd_scale + k x 1e-4). No threshold, limit, body, gate or requirement changed.

## Change (default 0 = bit-identical; control reproduced 25.6 31.2 38.7 30.4 s, median 30.8)
`LocomotionController.QpR13CopServo` (after QpSwingPd): while the rear (swing-parity) leg and the other leg are both in the contact schedule and the rear foot carries >= qp_r13_fmin of the total load, the rear ankle drive stiffness is zero (damping qp_r13_kd x legacy), and the ankle feed-forward is replaced by tau = sign x fn x (x_target + I - x_ankle), x_target = max(heel + 0.3, CoM x - qp_r13_m), I = integral of the measured CoP error (gain qp_r13_gi). Flags in WalkTuning: qp_r13_copservo, _m, _sign, _gi, _kd, _heel, _fmin. dotnet: Units.Mech 126/0 (125 + 1 new default-flag test), Physics.Body 112/0.

## Results (DS mean measured a over DS ticks 7.7-22 s, scripts/agg.py; pass rule >= -0.2 on all seeds)
| config | DS a seeds 0-3 | mean | rear CoP - CoM (m) | rear share | fall times s | median |
|---|---|---|---|---|---|---|
| L7 control | -0.67 -0.96 -0.74 -0.77 | -0.79 | +4.46 | 0.53 | 25.6 31.2 38.7 30.4 | 30.8 |
| servo sign +1, m 0.5 | -0.78 -0.95 -0.77 -1.05 | -0.89 | +3.97 | 0.53 | 28.8 24.1 25.9 23.6 | 25.0 |
| servo sign -1, m 0.5 | -0.83 -1.05 -0.73 -1.18 | -0.95 | +3.42 | 0.46 | 22.6 21.7 25.7 19.5 | 22.1 |
| sign -1, gi 4 | -0.82 -0.26 -1.05 -1.03 | -0.79 | +3.80 | 0.50 | 25.9 20.6 25.4 22.1 | 23.8 |
| sign -1, kd 1.5 | -1.14 -1.29 -0.47 -1.16 | -1.02 | +4.46 | 0.55 | 22.4 21.2 37.3 23.8 | 23.1 |
| sign -1, gi 4, kd 1.5 | -0.84 -1.13 -1.07 -1.08 | -1.03 | +5.11 | 0.57 | 21.5 24.0 25.0 22.2 | 23.1 |

## Findings
1. Sign: with the ankle DOF convention the CoP-minus-ankle sign is negative (sign -1 moved the rear CoP toward the foot centre: 3.9 -> 1.4 m from sole centre, seed 0; sign +1 moved it to the toe: 4.9 m). My geometric derivation had the opposite sign.
2. Even with the right sign the servo cannot bring the rear CoP back to CoM - 0.5 m: the rear CoP stays +3.4..+5.1 m ahead of the CoM (control +4.5), rear share 0.46-0.57, DS mean a -0.79..-1.03 (worse or equal), falls earlier (median 22-25 s vs 30.8). Ankle torque is not saturated (ankle force limit 1e9 N m, the servo needs about 3e8). So the ankle torque is not the binding actuator of the rear CoP: with a 15 m sole, the rear foot's toe is the only part under load because the stance geometry puts the rear heel behind and the sole centre ahead of the CoM, and the rear leg's hip/knee rows (QP and legacy PD) set the foot's load line.
3. Combined with r12 (rear-toe force cap) and r11 (PD retarget): QP force plan, QP-consistent PD and now the ankle torque servo all fail to move the plant's rear load. Three attempts, same plateau. The remaining lever inside the controller (knee/hip torques re-planned jointly with the servo) is a rewrite of the DS contact model.

## Verdict
NOT STABLE; pass rule (DS a >= -0.2 on all seeds) not met on any config; best median 25.0 s vs 30.8 s. No further loop (lead rule). Routing per §13 below.

## Routing (research / founder item R-T005-DS)
- Option A, body/contact redesign (recommended): sole shorter than the step (9-10 m instead of 15 m; the step is 8.65 m) removes the geometry that puts the rear toe ahead of the CoM. A body/contact contract change, not a threshold change; resets the contact model and needs founder or lead sign-off since body is a shared contract. Test with the unchanged L7 control and the r11 scripts (acceptance: DS a within +-0.2 of command, DS <= 0.9 s).
- Option B, learned residual on the QP/PD (RL): possible but costs 2-4 rounds and its training plant has the same DS braking; only worthwhile after A.
- Not recommended: more servo or cap variants on the 15 m sole (r12 and r13 both show the plant ignores them).
