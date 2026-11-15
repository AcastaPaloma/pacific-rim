# T-005 r4 step 9.5e: yaw authority in single support: yaw spin fixed to 9.6 deg (best run), walk still NOT STABLE (falls 13.5-26.9 s, 3 cycles)
Harness = step 5c (`scripts/run95.sh`, `walk_fr010`, 5c best config; variants `scripts/variants95e{1,2,3}.txt`, summaries `step5e/summary_e{1,2,3}.txt`, best yaw run `step5e/E4om2mu5.first22s.gait.jsonl.gz`, tool `scripts/yaw95e.py`). No assist (extMax 0), no threshold / mech_frame / body_tuning / walk_tuning.json edit; new code behind imp >= 2.5 + `qp_lateral` + `qp_yaw_trunk` / `qp_arm_yaw` (default 0). dotnet Units.Mech 125 / 118 pass / 7 KnownGap (unchanged); Unity EditMode `ImpByteIdentityTests` 1/1 pass (`step5e/editmode.xml`).

## Diagnosis (existing 5c log, best run, spin 15.2-16.6 s)
1. NOT friction torsion: the stance foot never slips (foot yaw constant 3.6 deg while the pelvis goes 2 -> 39 deg); the QP commands exactly its target yaw moment (cmd = target to 1 %) until ~16.0 s, and the cone (fr) is 0.2-0.5 at the onset. The ankle has no yaw DOF (swingY locked), the foot's torsion arm is the 4 corners (QP models them: sole 10 x 15 m); no separate torsional term exists or is needed.
2. Wrong controlled quantity: the yaw row regulates the WHOLE-body yaw momentum and heading with the whole-body inertia (6.2e8, log header); the pelvis/trunk (I = 1.6e8) is a free body at the stance hip (hip yaw stiffness 0, stance_hip_damping_ratio 0, legacy trunk-yaw PD removed in 9.5b), driven by the swing leg and arms: realised dL/dt (+1e8, 0.3 s mean) had the opposite sign of the commanded ground moment (-5e7 .. -1e8) and the pelvis stopped at 39 deg, 36 deg from the stance foot, about the hip-yaw range (-40 .. +35 deg). The swing foot yaw went 36 -> 4 deg in the same 1.4 s (leg/arm yaw momentum, cancelled by the trunk).
3. Priority/weights: the yaw row (w 1e6) already outranks the sagittal rows (1e4); it only wins by cone use, so when it is large the CoM / pitch tasks starve (speed 5.8 -> 2.1 m/s, then a pitch collapse).

## Built (`qp_yaw_trunk`, in SagittalQpController.LateralRows / WholeBodyQpDriver.LateralInputs)
Ground yaw-moment target = -(I_trunk om^2 e + 2 om L_trunk) + qp_yaw_ff x d(L_legs+arms)/dt (finite difference, low pass `qp_yaw_ff_alpha`, optional spike clamp `qp_yaw_ff_max`, target cap `qp_yaw_cap` W l); trunk = pelvis, torso, head. Log `qp.yd` = [L_trunk, I_trunk, dL_rest/dt, target, L_rest, trim L, trim R]. Also `qp_arm_yaw`: shoulder-flexion trim that servos L_rest to zero (rate = -k L_rest / (2 S), S from link states).

## Results (all fall; none reaches 3 steady strides; table = fall s / yaw peak before the fall)
| variant | fall | yaw peak |
|---|---|---|
| 5c best (reference) | 26.5 | 49.6 deg (2 -> 40 deg in 1.5 s) |
| e1 trunk yaw om 0.5 / om 2 (ff 1) | 16.0 / 15.6 | 18.1 / 52.9 |
| e1 om 2, ff 0 | 26.9 | 20.5 |
| e1 om 2, qp_mu 0.5, margin 0.95 (E4) | 20.6 | **9.6** (peak yaw rate 0.56 rad/s, stays within +-7 deg 12-20 s) |
| e2 w_yaw 1e5 / cap 0.05 / arm trim +2 / -2 | 19.0 / 18.6 / 15.0 / 13.5 | 28 / 23 / 20 / 72 |
| e3 ff clamp 0.05: alpha 0.15 / 0.3 (the om 1.0 and mu 0.55 variants repeated JSON keys, copies of alpha 0.15) | 16.5 / 17.0 | 15 / 36 |

## What binds now
1. Yaw is controllable: with the trunk task (E4) the heading error stays under 10 deg for 20 s on the Fr 0.06 -> 0.10 rows; the limit is demand vs cone: dL_rest/dt of legs + arms swings +-1.5e8 N m every 0.4 s (stance-foot budget ~1.7e8, QP cone mu 0.5 x 0.95), so every yaw peak starves the sagittal tasks.
2. The E4 fall (20.6 s) is sagittal: trunk pitch 10 -> 35 deg over 1.4 s in a single support with the stance load going to 0 and lateral drift 1 m/s, fr at the cap; not yaw.
3. Arm servo did not help (sign + : 15.0 s, sign - : spin to 72 deg); the arms still follow the Fr 0.10 imp 2 reference at the legacy phase, not the QP stride. Run-to-run spread (13.5-26.9 s on small changes) is larger than any single effect here.
## Next (not done)
Take the yaw demand out of the ground: arm / waist yaw reference from the QP row's own stride (3D rows), or a yaw-aware sagittal budget (reduce the CoM x task weight proportionally to the cone share the yaw row takes). M-001@v1.1 / M-002@v1.1: not computed (no stable walk); both stay FAIL / not evaluated for imp 3.
