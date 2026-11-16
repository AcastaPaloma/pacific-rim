# T-005 round 11: the ~30 s plateau is a double-support braking problem in the plant, not the hand-off speed; four cycles, NOT STABLE

Branch task/T-005-r11 (from r10 79c9b5e). Opus 5.5 worker, decided autonomously (founder offline). Scenario walk_fr010 (63 s, M-001 run), walk_mode 0, L7 control (v5 rows, qp_plan = 1), 4 seeds = qp_pd_scale + k x 1e-4 as in r8-r10. No threshold, limit, body, gate or requirement changed; no assist.

## Verdict: NOT STABLE
No configuration survives 63 s on any seed (116 runs in 4 cycles). Best 4-seed median 30.9 s (M2b5: 28.6 32.3 31.5 30.2) vs the control's 30.8 s (25.6 31.2 38.7 30.4): equal, inside the control spread. Next approach: `research_note.md`.

## 1. Checking the r10 hypothesis against the traces (control logs, `traces/ctl_*`)
The r10 chain (hand-off at 2.0 vs 2.9 m/s, CoP target pinned behind the heel, speed lost in stance) does not hold up as the cause:
- **LIPM says a heel CoP accelerates.** With the CoP at the heel and the CoM 0.2 m ahead at 2.0 m/s (DCM offset 4.36 m), x(t) = p + (x0 - p) cosh wt + v0/w sinh wt (w = 0.483 /s) gives 4.7 m/s after a 3 s stance. The speed deficit could be repaired from inside the stance if the commanded acceleration were realised.
- **The speed is lost in double support, not in single support** (seed 0, `ctl_s0_ts_7-17s.txt`): DS1 (8.7-10.3 s) 2.24 -> 1.18 m/s; SS1 (10.4-13.8 s) 0.93 -> 1.5 m/s (net gain); DS2 (13.9-15.4 s) 1.37 -> 0.13 m/s. DS takes 42-45 % of the QP ticks (348-376 of 827-860) against the reference's 20 % (0.6 of 3.0 s).
- **The commanded CoM acceleration is not realised in DS** (`ctl_accel_realisation.txt`, 0.5 s box, 7.7-22 s, `scripts/corr.py`): mean commanded cdx in DS +0.06 / +0.06 / +0.17 / +0.11 m/s2, measured -0.67 / -0.96 / -0.74 / -0.77 m/s2 (seeds 0-3); r = 0.51 / -0.11 / 0.33 / 0.11. In SS the mean is roughly realised (0.28-0.42 vs 0.39-0.56) at gain 0.45 and r 0.2-0.45. Median |measured CoP - CoP of the QP's forces| 1.2 m (SS) and 1.2-2.7 m (DS), p90 4.5-11.4 m (`ctl_realisation.txt`).
- **What carries the load in DS** (`ds_load.txt`, `scripts/ds.py`): in every DS episode of every seed the REAR foot carries 32-92 % of the load with its CoP 2.9-7.7 m AHEAD of the CoM (it is pivoting on its toe, heel-off: its pitch rises 0 -> 12.5 deg in DS1). The 15 m sole is longer than the 8.65 m step, so the rear toe lies ~5 m ahead of the front heel and of the CoM. W x 0.5 at 4.5 m ahead of a 42 m CoM gives -0.53 m/s2, the size of the measured braking. The front foot is unloaded (< 0.1 W) on 52-64 % of the first-DS ticks and chatters (3-5 contact_begin events, up to 14 in D1 seed 1). In seed 0 it lands at 9.6 m and slides forward to 13.35 m while lifted 0.1-0.55 m before it takes load at 10.32 s (the step-1 "lands short / IK failure" anomaly of r10 is this phase).
- **The QP's own DS CoP plan also starts ahead of the CoM**: the step-6c sweep (qp_plan_ds) runs from the rear foot's most forward contact point, its toe, to the front foot (first DS: target 7.1 -> 4.0 -> 7.3 m with the CoM at 2.9-5.7 m, commanded cdx -0.98 .. -0.38 m/s2).
- **The legacy PD is a second controller of the same joints**: the imp-2 controller keeps writing its own drive targets; at qp_pd_scale 0.25 the PD adds a mean 225-251 MN m per joint in SS and 391-435 MN m in DS, against a mean |QP torque| of 295-347 MN m (larger than the QP in DS). The bias is systematic (DS: stance hip -230..-236, both ankles +185..+395 MN m mean, same sign in both seeds checked).
- **The single-support heel margin is a smaller effect**: the clamp interval starts at heel + 8 % of the sole (+1.2 m), which lies 0.3-1.0 m ahead of the CoM early in SS1, so the QP commands -0.34..-0.05 m/s2 there (`ctl_s0_acc.txt`).
- **Step-1 swing**: plan 4.4-5.6 m (lower clamp 4.33), landing error -1.59 m, apex 2.4 m, 87 IK failures. In later steps the swing misses by 1.6-18 m whichever way the stance fails (too slow in the control, too fast in M2: plan on its 8.65 m upper clamp, landing -16.7 / -18.2 m, forced touchdowns at 3.9 s; `c2_M2s*_swing.txt`). Swing execution is the second binding constraint.

## 2. What was changed (all default 0 = bit-identical; flags in WalkTuning / walk_tuning.json schema)
- `qp_r11_pdq` = b (and `qp_r11_pdq_k`, `qp_r11_pdq_mode`; imp 3): the PD of the six sagittal leg DOFs is retargeted from the legacy imp-2 target to the state the QP intends one tick ahead (q + qd dt + qdd* dt^2/2, rate qd + qdd* dt, qdd* = the QP solution mapped to the joint; new `SagittalQpController.SolQdd`), blended by b; mode 0 both legs, 1 stance leg only, 2 both legs only in double support, 3 all but the airborne swing leg; `_k` scales the PD gains. `LocomotionController.QpSwingPd`.
- `qp_r11_ds` (needs qp_plan): 1 = no rear-to-front sweep in DS (DCM CoP target on the union of the contacts), 2 = the sweep starts at the rear-foot point nearest the DCM target instead of its toe. `SagittalQpController.Step`.
- The r10 hypothesis (a), a reference speed matched / ramped to the measured speed, was tested with the time-warp that already exists (`qp_scale_min`: reference clock rate = clamp(v / v_ref, min, 1), tau 1 s), so no new code was needed.
- dotnet: Units.Mech `Category!=KnownGap` 123/0 (new `R11PdqTests`), Physics.Body 112/0.

## 3. Unity cycles (4 seeds each, fall time s, median; `cycle1..4/`, `scripts/mkvar.py`, `scripts/run11.sh`)
| cycle | config | seeds 0-3 | median |
|---|---|---|---|
| 1, 2, 4 | **L7 control** (code at each cycle, flags off) | 25.6 31.2 38.7 30.4 | **30.8** (bit-identical in all three) |
| 1 | pdq 1 (mode 0) / 0.5 | 19.2 19.9 18.1 22.1 / 13.5 13.5 13.4 13.5 | 19.5 / 13.5 |
| 1 | pdq 1 k 2 / k 4 | 14.7 19.9 19.0 16.7 / 17.0 19.3 18.3 16.3 | 17.9 / 17.6 |
| 1 | pdq 1 + heel margin 0.3 m / + pd_swing 0.25 | 17.3 19.8 21.7 19.6 / 15.1 17.4 24.0 20.8 | 19.7 / 19.1 |
| 2 | pdq 1 mode 1 / 2 / 3 | 18.0 21.6 24.3 19.9 / 24.1 25.9 22.5 21.7 / 20.1 20.3 20.9 18.8 | 20.8 / 23.3 / 20.2 |
| 2 | pdq 0.5 mode 3 / pdq 1 mode 3 + pd_swing 0.25 | 30.4 25.4 26.4 34.2 / 18.5 15.1 24.6 19.4 | 28.4 / 18.9 |
| 2 | **hypothesis (a)**: time-warp qp_scale_min 0.6 / 0.4 / 0.6 + pdq mode 3 | 25.2 15.0 21.1 21.5 / 18.6 18.2 16.6 16.2 / 21.5 20.8 21.3 17.5 | 21.3 / 17.4 / 21.1 |
| 3 | pdq 0.5 mode 2 (M2b5) | 28.6 32.3 31.5 30.2 | **30.9** |
| 3 | mode 2 + lmax 1.3 / + pd_swing 0.25 / pdq 0.5 mode 2 + lmax 1.3 / pdq 0.5 mode 3 + lmax 1.3 | 20.1 22.8 20.1 20.7 / 15.1 18.4 19.3 24.0 / 34.8 24.3 29.1 22.9 / 29.4 22.9 22.5 31.7 | 20.4 / 18.9 / 26.7 / 26.1 |
| 3 | legacy PD gain qp_pd_scale 0.10 / 0.50 / 1.0 | 30.1 26.0 31.3 24.9 / 20.6 25.0 26.3 12.3 / 24.4 15.5 15.6 21.1 | 28.1 / 22.8 / 18.4 |
| 4 | ds 1 / ds 2 | 26.6 24.4 22.8 23.8 / 24.9 24.2 26.6 21.9 | 24.1 / 24.5 |
| 4 | ds 1 + pdq 0.5 mode 2 / ds 2 + pdq 0.5 mode 2 / ds 1 + lmax 1.3 / ds 1 + pd 0.10 | 24.3 27.8 24.5 25.4 / 29.8 24.6 23.0 22.4 / 25.8 22.1 23.9 26.4 / 26.2 22.4 22.9 25.4 | 24.9 / 23.8 / 24.9 / 24.1 |

## 4. What the cycles show
- **Hypothesis (a) is refuted**: a reference whose speed follows the measured speed is worse (21.3 / 17.4 s), as were the r8 slow rows (Fr 0.010 row = 2.04 m/s, 18.0 s). The hand-off deficit is a symptom: speed is lost again in every DS.
- **Retargeting the legacy PD to the QP's intent** removes the DS braking when all joints are retargeted (P1 DS mean a +0.34 / +0.05 / +0.61 / -0.23 m/s2, `c1_P1_accel_realisation.txt`), but then the swing foot no longer lifts: it drags in contact through SS1 and slides back (`c1_P1s0_ts.txt`). The legacy swing PD does real work, and the QP's swing task cannot replace it. Partial retargets either keep the braking (pdq 0.5 mode 2: DS mean -0.84..-1.26 m/s2) or let the DCM run away from a swing that cannot follow (mode 2: DCM at touchdown 2 = 18.8 m = 3.1 x the reference, landing -9.3 m).
- **The braking does not come from the legacy PD gain or from the QP's DS CoP plan**: at qp_pd_scale 0.10 the DS mean is still -0.13..-0.70 m/s2 (`c3_accel_realisation.txt`). With qp_r11_ds 1 the commanded DS cdx becomes neutral or positive (+0.24 / -0.28 / -0.03 / +0.30) and the plant still brakes -0.78..-1.06 (r about 0; `c4_accel_realisation.txt`). In single support the same runs realise the command well (D1 seeds 1-3: gain 0.85-0.99, r 0.82-0.87).
- **Root cause of the plateau**: the double-support transition does not happen in the plant. The rear foot stays loaded at its toe, 3-8 m ahead of the CoM (geometry: 15 m sole > 8.65 m step, heel-off pivot), while the front foot lands, chatters and slides, and stays unloaded for ~1 s. Every DS brakes the CoM by about 0.8 m/s2 for ~1.5 s, against about 0.5 m/s gained in SS. The speed ratchets down, the planner sits on its lower clamp, the swing (which already misses by metres) cannot recover, touchdowns become the forced 3.9 s ones, and the CoM runs backward by steps 4-5. No planner-level change (reference speed, foothold, DS CoP target) can fix this: the QP's force distribution in DS is not realised at all (r about 0).

## 5. Deliverables
Flags (default off): qp_r11_pdq, qp_r11_pdq_k, qp_r11_pdq_mode, qp_r11_ds. None is recommended for enabling (none beats the control). Traces: `traces/` (ctl_* = r10 control logs re-analysed; c1..c4_* = this round). Raw Unity logs deleted after analysis. The summaries' `commit 79c9b5e` line is the base HEAD; Unity compiled the r11 working tree, which is the commit that carries this file.
