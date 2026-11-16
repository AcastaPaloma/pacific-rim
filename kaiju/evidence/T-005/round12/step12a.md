# T-005 round 12: rear-toe force cap in double support (qp_r12_reartoe); NOT STABLE, DS braking not removed (pass rule failed), one Unity cycle

Branch task/T-005-r12 (from r11 25cedf7). Scenario walk_fr010 (63 s), walk_mode 0, L7 control (v5 rows, qp_plan = 1), 4 seeds = qp_pd_scale + k x 1e-4 as r8-r11. No threshold, limit, body, gate or requirement changed; no assist.

## Verdict: NOT STABLE; pass rule for continuing NOT met
No seed survives 63 s. Best median 23.2 s (all three m values) against the L7 control's 30.8 s (25.6 31.2 38.7 30.4, bit-identical, with and without qp_r10_diag). The DS mean measured acceleration is -0.97..-1.23 m/s2 on all 4 seeds (rule: >= -0.2), worse than the control's -0.67..-0.96. The load did not move to the rear heel and the rear foot did not rock back, so the front-foot impedance-landing change (the note's conditional second step) was not triggered and no cycle 2 was run.

## 1. The change (default 0 = bit-identical)
- `qp_r12_reartoe` (default 0; needs qp_plan = 1) and `qp_r12_reartoe_m` (default 0), in WalkTuning (`MechContracts.cs`; walk_tuning.json carries no r9-r11 flags either, the class default is the off state). `SagittalQpOptions.R12RearToe`, `R12RearToeM`.
- `SagittalQpController.Step`, right after the force caps (after the 7c unload ramp): while the swing has not started (`double.IsNaN(_swT0)`), the swing-parity leg `rl = _sch.Parity` has its toe in the contact schedule, and the other leg also has a contact point in the schedule, `if (_px[2*rl+1] > Com[0] - m) _fmax[2*rl+1] = 0`. Sign check: the x axis is the walking direction (`com_p[2]` in the log), heel = point 2*leg, toe = point 2*leg+1, the toe has the larger x, "ahead of the CoM" = larger x; m > 0 widens the zone backwards. The "other leg in the schedule" guard is mine: without it the QP would have only the rear heel to stand on before the front touchdown.
- Sole-flat task kept on the rear foot: in the foot-pitch row loop the rear leg (`leg == swLeg`, swing not started) with both of its points in the schedule (ns == 2, which used to `continue` with no foot row) now gets the foot pitch row to FootCmd (slews to level, weight qp_foot_w) like a toe-only foot; it ends when the swing starts.
- dotnet: new `R12RearToeTests` (flag defaults and the qp_plan gate; golden replay with Plan on: 122 DS ticks with a rear toe in the schedule, the cap is zeroed on 93 of them at m = 1000, never outside DS, the flag only lowers caps, at m = 0 only while the toe is ahead of the CoM). Units.Mech `Category!=KnownGap` 125/0 (123 + 2 new), Physics.Body 112/0.

## 2. Unity cycle 1 (20 runs: control plain, control with qp_r10_diag, m = 0 / 0.5 / 1.0 with diag; `cycle1/`, `scripts/mkvar.py`, `scripts/run12.sh`)
Measured 7.7-22 s over all double-support ticks (`scripts/agg.py`: tick-weighted per seed, mean over seeds; `scripts/ds.py`, `scripts/corr.py`, `scripts/rear.py`; per-seed detail in `traces/`).

| config | DS mean measured a, seeds 0-3 (m/s2) | mean | DS mean commanded cdx | rear load share | rear CoP - CoM (m) | front foot < 0.1 W share | fall time s, seeds 0-3 | median |
|---|---|---|---|---|---|---|---|---|
| L7 control (qp_r10_diag on and off) | -0.67 -0.96 -0.74 -0.77 | -0.79 | +0.10 | 0.53 | +4.46 | 0.30 | 25.6 31.2 38.7 30.4 | **30.8** |
| reartoe m = 0 | -1.04 -1.23 -1.14 -0.97 | -1.09 | -0.89 | 0.54 | +4.94 | 0.34 | 28.6 22.8 23.6 17.5 | 23.2 |
| reartoe m = 0.5 | identical to m = 0 (to the printed digits) | | | | | | 28.6 22.8 23.6 17.5 | 23.2 |
| reartoe m = 1.0 | identical to m = 0 (to the printed digits) | | | | | | 28.6 22.8 23.6 17.5 | 23.2 |

m makes no difference because the rear toe lies 15 m ahead of its heel and 3-8 m ahead of the CoM for the whole DS, so the cap is on whenever m is in 0..1 m. The control reproduces 25.6 / 31.2 / 38.7 / 30.4 s exactly (the r11 DS means -0.67 -0.96 -0.74 -0.77 also reproduce).

## 3. What the cycle shows
- **The QP-side cap does not move the plant's rear load.** Rear share 0.53 -> 0.54, rear CoP - CoM +4.5 -> +4.9 m. Measured against the rear heel (`traces/rear_cop_pitch.txt`) the rear CoP stays at 5.4-6.7 m of the 15 m sole (control 2.9-6.4 m), mid-sole to toe, with the rear pitch up to +14..15 deg (heel-up, as in the control; no rock-back, no heel-only load). Time series of DS1 seed 0 (`traces/rt05s0_ds1_ts.txt` vs `traces/ctl_s0_ds1_ts.txt`): the rear foot still carries its CoP at 8-9 m with the CoM at 3.4-5.8 m, as in the control.
- **The QP command became consistent and negative, the plant braked harder.** With no toe force available the QP's only rear force is at the heel (behind the CoM), so the commanded DS cdx fell from +0.10 to -0.89 and the measured a from -0.79 to -1.09. The plant's rear-foot CoP did not follow the QP's force plan before, and still does not. This is an inference, not a measured torque budget: contact force and CoP in the plant come from the joint torques and the foot's pitch/ankle state, of which the QP's planned contact force is only the feed-forward part (the legacy imp-2 PD adds 391-435 MN m per joint in DS, round 11, comparable to the QP).
- Front foot: unloaded share 0.30 -> 0.34, chatter not reduced (first DS: 8 contact_begin events on seeds 0-1 against 4 in the control, `traces/ds_load.txt`).
- Falls moved the wrong way on three seeds (31.2 -> 22.8, 38.7 -> 23.6, 30.4 -> 17.5), one up (25.6 -> 28.6): median 23.2 against the control's 30.8.

## 4. Next step
The rear toe cap shows the QP's force plan is not what loads the rear toe. Recommended for r13, in this order (all behind default-0 flags):
1. Make the rear foot's own ankle the actuator of the DS load transfer: replace the rear leg's PD + QP ankle drive with a measured-CoP servo, i.e. command the rear ankle torque so that the measured rear CoP sits at or behind CoM - m (CoP = ankle x + tau_ankle / fn), keeping the foot flat; remove the legacy PD drive on the rear ankle only while the rear leg is in DS (r11: the PD adds +185..+395 MN m mean to both ankles in DS with the same sign across seeds). Acceptance on the same 4 seeds: rear CoP - CoM <= 0, DS a >= -0.2, front >= 0.3 W within 0.3 s of contact.
2. If that moves the load to the front foot but the front foot chatters or slides, then the note's front-foot impedance landing (not run here because its trigger, load on the rear heel or rock-back, was not observed).
3. The shorter-sole body option (9-10 m instead of 15 m) stays the lead/founder decision from the r11 note and now looks more attractive: it is the geometry that puts the rear toe ahead of the CoM, and no QP-side cap reaches it.

## 5. Deliverables
Code: flags `qp_r12_reartoe`, `qp_r12_reartoe_m` (default 0, not recommended for enabling). Evidence: `cycle1/` (summary.txt with the 20 runs and EFFECTIVE lines, variants.txt, agg.txt), `traces/`, `scripts/` (`agg.py`, `rear.py` new; `ds.py`, `corr.py` from r11; `mkvar.py`, `run12.sh`). Raw Unity and gait logs deleted after analysis (the summary line `commit 25cedf7` is the base HEAD; Unity compiled the working tree that the commit carrying this file holds). Unity runs: 1 cycle, 20 runs, about 80 s wall.
