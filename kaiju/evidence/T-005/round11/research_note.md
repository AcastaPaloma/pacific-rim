# T-005 research note after round 11: fix the double-support transition in the plant before any more planner work

## Why the planner rounds (5-11) plateau at ~30 s
Evidence from `step11a.md` (4 seeds, control and 27 flag configurations, 116 runs):
1. The CoM loses its speed in **double support**: measured mean -0.67..-0.96 m/s2 in DS in all four control seeds, while the QP commands +0.06..+0.17. DS lasts ~1.5 s, 42-45 % of the time (reference 20 %). In single support the speed rises again (SS1 seed 0: 0.93 -> 1.5 m/s). The hand-off deficit (2.0 vs 2.9 m/s) is only the first instance of this.
2. The braking is **independent of the planner and of the legacy PD gain**. A speed-matched reference (time-warp) gives 17-21 s. A neutral or positive DS CoP command (qp_r11_ds 1) still brakes -0.78..-1.06 m/s2 at r about 0. At legacy PD gain 0.10 it still brakes -0.13..-0.70. Only the full retarget of every leg joint removed it, and that broke the swing.
3. Mechanism (`traces/ds_load.txt`): the rear foot carries 32-92 % of the load at its toe, 2.9-7.7 m ahead of the CoM (heel-off pivot; the 15 m sole is longer than the 8.65 m step, so the rear toe is ~5 m ahead of the front heel). The front foot lands, chatters (3-14 contact begins) and slides, and stays below 0.1 W for 52-64 % of the first DS. W/2 at 4.5 m ahead of a 42 m CoM = -0.53 m/s2.
4. In single support the same QP realises its command well when the gait is clean (gain 0.85-0.99, r 0.82-0.87 in three seeds of qp_r11_ds 1). The whole-body QP is usable in SS. It fails exactly where contacts change.
5. The swing is the second binding stage: it misses the plan by 1.6-18 m in every configuration, and it depends on the legacy imp-2 PD to lift the foot (the QP-consistent PD left the foot dragging).

So the binding problem is the contact transition (rear-toe loading plus front-foot landing), plus a swing that the QP cannot drive alone. More reference, foothold or DCM-replanning work cannot help while the plant does the opposite of the DS command. A per-step capture-point foothold replanner already exists (PlanEval: predicted end-of-step DCM minus offset, every tick), and the reference-speed adaptation exists (qp_scale_min). Both were exercised and neither moves the plateau.

## Recommendation (in order)
**A. Double-support contact redesign for long soles (first, 1 round).**
- **Rear foot**: no load ahead of the CoM. Add a per-point force bound in the QP: the rear foot's toe point gets fmax = 0 while its x > CoM x - m (m ~ 0.5 m), and the rear sole is held flat (no heel-off pitch) until it is unloaded, then lifted as a whole. Alternatively ramp the rear foot to zero in ~0.3 s once the front foot carries more than 0.3 W.
- **Front foot**: an impedance landing. Arrive with zero horizontal velocity relative to the ground, hold the landed x once the heel touches (no forward slide), and count the contact as established only after fn > 0.3 W for 3 ticks. The leg stays under a landing task, not stance rows, until then.
- **Acceptance before any planner change**: DS mean measured CoM acceleration within +-0.2 m/s2 of the command, DS duration <= 0.9 s, front foot >= 0.3 W within 0.3 s of the first contact, on the 4 L7 seeds.

**B. One controller for the legs (1-2 rounds, if A gets the DS right but the swing still misses).**
Today the legacy imp-2 controller writes PD targets on the same six joints the QP feeds forward. Its PD is 225-435 MN m per joint, comparable to the QP. Replace it with a single source: joint-space swing trajectories from the QP planner (min-jerk heel path through IK, as `PlanIk` already computes), tracked by the QP-consistent PD of qp_r11_pdq. The QP's swing task currently cannot lift the foot alone, so the swing joint trajectory has to carry the lift (apex) explicitly. Test it with qp_r11_pdq mode 0 plus the new swing trajectory.

**C. Only then, planner or MPC work.** A receding-horizon DCM/footstep MPC (step timing as a variable) makes sense once the DS is realised. Without A it would only re-plan around a plant that brakes 0.8 m/s2 in every DS.

**Not recommended now**: more reference rows or ladders (r8: worse), time-warp or speed ramps (r11: worse), foothold offset scalings (r7/r10: worse), heel or toe margins (r11: no gain). A learned residual (RL) policy is possible, but its training plant would need the same contact-transition fix to be useful, and it costs much more (sim-farm set-up, 2-4 rounds).

**Body option (lead / founder decision, out of a worker's remit)**: a sole shorter than the step (for example 9-10 m instead of 15 m) removes the geometry that puts the rear toe ahead of the CoM. Not changed, because the body is a shared contract.

## First concrete experiment (round 12, about 1 Unity cycle)
Flag `qp_r12_reartoe` (default 0): in `SagittalQpController.Step`, after the force caps, for the swing-parity leg while it still has contact: `if (_px[2*rl+1] > cx - m) _fmax[2*rl+1] = 0` (toe), plus the sole-flat task weight on the rear foot until it is unloaded. Run the L7 4 seeds with m = 0 / 0.5 / 1.0 m (12 runs, plus the control). Measure with the r11 scripts: `scripts/ds.py` (rear share, rear CoP - CoM, front-foot unloaded share), `scripts/corr.py` (DS mean a vs cdx) and the fall times. Pass for continuing along A: DS mean a >= -0.2 m/s2 on all seeds. If the rear toe is not the carrier after the change (the load moves to the rear heel or the rear foot rocks back), add the front-foot landing change in the same round.
