# T-005 round 9: stance-side start-up momentum (DCM lift-off gate, CoM-velocity launch): NO stable walk; no config beats the L7 control 30.8 s
Branch task/T-005-r9 (from r8 7101666), code e2738bf. New flags default 0 (dotnet Units.Mech `Category!=KnownGap` 121/0 incl. 2 new R9StartupTests, Physics.Body 112/0). No threshold, limit, body or band change, no assist. Decided autonomously (founder offline).

## 9.1 Offline row-0 DCM test (`scripts/dcm_row0.py`, output `dcm_row0.txt`; ladder v6)
At the reference lift-off node (k_ds = 8) the DCM offset over the stance heel is 12.8 / 14.5 / 15.8 / 17.2 m for Fr 0.005 / 0.010 / 0.015 / 0.020 (reference CoM speed at that node 3.65 / 4.46 / 5.07 / 5.74 m/s; sole 15 m). A standing mech holding the CoP at heel+1.5 m (LIPM, omega 0.485/s) needs 1.3 / 1.6 / 1.8 / 2.0 s to reach it (0.43-0.66 T), versus the 0.1-0.9 m/s the plant shows (r8). From Fr 0.015 on the reference DCM is already beyond the toe at lift-off (toe margin -0.8 .. -8 m), i.e. the reference relies on the swing foot catching it. So the lift-off state is not reachable inside 0.2 T of double support from standing, for any row; lower rows only shift the offset by 1.5-4 m.

## 9.2 Controller (C#, `SagittalQpController`, WalkTuning in `MechContracts.cs`, flags default 0, active only with qp_plan = 1)
- `qp_r9_gate` = f, `qp_r9_gate_tmax`: the swing does not start (reference clock held at the lift-off node) until measured DCM offset over the stance heel >= f x the reference's at that node, or tmax s after the reference lift-off time.
- `qp_r9_launch` = v, `qp_r9_ramp`, `qp_r9_vfrac`, `qp_r9_tau`: until lift-off the reference CoM x-velocity gets a bias v x deficit x ramp (deficit against vfrac x the reference speed at the lift-off node) and the reference CoM x its integral; the offset decays with tau after lift-off, reset at touchdown.
- `QpReference.NodeDcmOffsetAt(k, point, omega)`.

## 9.3 Unity (2 cycles, 4 seeds = qp_pd_scale + k 1e-4, walk_fr010 imp 3, `scripts/run99.sh`, `variants_r9.py` sets s1/s2, summaries `cycle1,2/`). Fall time s; none stable in 80 runs
Control L7 (v5 rows) 25.6 31.2 38.7 30.4 = **30.8** (reproduced bit-for-bit, again).
Cycle 1: gate 0.5 28.9; gate 0.7 21.9; gate 0.7 tmax 2 21.9; launch 1.0 23.0; launch 2.0 28.7; gate 0.6 + launch 1.5 24.5; same on v6g rows 20.7; on v6 rows 14.8.
Cycle 2: gate 0.3 32.3 (3 of 4 seeds identical to the control: the gate never fires, one seed differs by 3 s); gate 0.9 21.6; gate 0.9 tmax 0.5 20.9; gate 1.0 tmax 3 20.6; launch 0.5 24.3; launch 1.0 vfrac 0.7 27.9; ramp 1.0 24.8; tau 5 24.9; gate 0.5 + launch 0.5 25.2; gate 0.9 tmax 0.5 + launch 0.5 23.1.
Best: gate 0.3 at 32.3 s is within the control's 25.6-38.7 spread (3 of 4 seeds identical), not an improvement. Nothing leaves ladder row 0 or is stable.

## Diagnosis
- The start-up trace (`startup_trace.txt`, seed 0): the QP hands off at 7.6 s (v6 rows 5.8 s); before that the legacy imp-2 walk leans the CoM BACK (-0.43 m/s at 4.5 s) and only then accelerates to +2.0 m/s by 9 s; the control reaches 16 m of travel and then runs backward from ~22 s (v -1.4 .. -4.3 m/s). So the first step is not CoM-speed starved (2 m/s at hand-off, near the row-0 1.44-2.9 m/s mean); the collapse comes 10-15 s later, in steps 3-5, with DCM running past the foot.
- A gate that actually holds the swing (0.7-1.0) makes it worse (20.6-21.9 s): holding the swing at the lift-off node keeps the weight on a foot whose CoP cannot take the DCM (r6: ankle limit), and the forced-touchdown timer (1.3 T) then lands the foot late. Launch bias adds forward CoM momentum but the DCM is then further ahead of the foot: 23-29 s.
- So the round-8 reading (start-up momentum is the missing piece) is only partly right: it is a problem of v6 rows (14.6-18 s), where the 2 m/s hand-off leaves a 1.44 m/s row 0 with the front foot landing 5 m short; adding launch does not help there (v6 + gate + launch 14.8). On v5 rows the failure is the late steps 3-5, not the start.

## Recommended next approach (round 10, not done)
Look at steps 3-5 of the control (7.6 s to ~22 s), where speed falls from 2 m/s to negative: log per step DCM offset at touchdown vs the reference (`PlanDiag`, qp_r7_fp), CoP position on the stance sole and rear-foot unload time, and fix the first of them to diverge; the start-up is not the binding stage. Also add the R9 diagnostics (gate hold seconds, launch offset: `PlanDiag[16..17]`, not yet in the gait log) to GaitLogWriter (QpK size 55) before any further flag.
