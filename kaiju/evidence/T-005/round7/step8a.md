# T-005 r7 8a: ladder re-optimised under hard limits (offline) + load-transfer / support-switch flags (Unity): a feasible row exists at Froude 0.10; no stable walk (best 4-seed median 27.6 s on the new rows, control 24.8 s)
Branch task/T-005-r7 (from r6 756497d). Defaults unchanged (dotnet Units.Mech Category!=KnownGap 119/0, one flaky timing run 118/1 then 119/0 under machine load 49).

## Part 1a: the optimiser (`Tools/GaitOpt/stride_opt_r7.py`, `run_r7.sh`, `chain_r7.sh`; stride_opt.py / stride_opt_tc.py untouched, patched as source at run time)
Hard limits added to the r4 stride NLP (planar 9 DOF, torque/ROM/friction/yaw rows as before, trust-constr, N = 40):
- step time T in [tmin, 3.0] s (omega T < 1.5; omega = 0.482), hip z >= 36 m (allowed dip 6.2 m; the optimiser used 0.5-0.7 m);
- flat-foot schedule: a foot loads and unloads with heel and toe together; the new front foot is flat and loaded from node 0 (touchdown node), the rear foot stays flat through a double support of 8 of 40 nodes (0.2 T) and unloads by node 8; so the support switch happens with the front foot already carrying load, load transfer and swing start sit at fixed reference nodes;
- per-foot CoP in [heel + 1.5, toe - 2.5] m (linear rows on the two point normal forces), total F_z <= 1.5 W at every node;
- everything else (ankle 1e9 N m, knee 8e8, hip 1.2e9, mu 0.4 lateral-derated, yaw for 0.07+ NOT included: planar rows only, see below).
## Result: rows (residual = max dynamics / equality residual, tolerance 1e-5; row files `Tools/GaitOpt/out/r7/*.json`, export `scripts/export_ladder_r7.py`)
| row | Fr | T s | L m | omega T | CoP from heel (m) | F_z max | CoM dip | residual |
|---|---|---|---|---|---|---|---|---|
| d_020 | 0.02 | 3.00 | 8.7 | 1.45 | 1.9-11.6 | 1.49 W | 0.66 | 2.6e-13 |
| d_030 | 0.03 | 2.61 | 9.2 | 1.26 | 1.9-10.9 | 1.49 W | 0.74 | 4.1e-5 |
| g_050 | 0.05 | 2.00 | 9.1 | 0.97 | 2.2-10.1 | 1.50 W | 0.67 | 1.8e-5 |
| g_070 | 0.07 | 2.00 | 10.8 | 0.96 | 2.3-9.8 | 1.50 W | 0.53 | 1.3e-5 |
| g_090 | 0.09 | 2.00 | 12.2 | 0.96 | 1.7-11.6 | 1.50 W | 0.63 | 1.4e-5 |
| h5 | 0.095 | 2.00 | 12.6 | 0.96 | | 1.50 W | | 2.8e-7 (feasible) |
| **h2b** | **0.10** | **1.77** | **11.4** | **0.85** | 1.6-11.7 | 1.50 W | 0.57 | **2.5e-5** (T bound lowered to 1.5 s; 400 its, still falling) |
Round-4 rows for comparison (`scripts/row_limits.py`): T 4.5-6.3 s (omega T 2.2-3.0), CoP 0-15 m of the 15 m sole (heel and toe points), F_z up to 2.2 W, heel-strike only schedule.
**Is there a feasible ladder row at Froude 0.1? Yes (local statement, trust-constr), with step time 1.77 s, step length 11.4 m (0.27 leg lengths), all five limits.** What binds at Fr 0.10: (a) with T >= 2.0 s and all limits the solver stops at an infeasible stationary point (residual 6-7e-2, g_100 / h4 even with T up to 4.5 s), friction use 2.1-6x; (b) the F_z cap is the limit: F_z <= 1.6 W and 1.7 W still infeasible (k16, k17), 1.8 W feasible (k18); no CoP / F_z rows at all feasible (h1, peak F_z 2.41 W); (c) T < 2 s (1.5 .. 3.0 allowed) is feasible with 1.5 W. So the plan ladder must use short, quick steps at higher speed (T 2.0 -> 1.77 s from Fr 0.05), not the 4.5-6.3 s of the r4 rows. The row steps are 0.2-0.3 leg lengths. A reduced-order check (`scripts/lipm_feas.py`, constant-height LIPM + CoP window + friction as an LP) says the CoP window and omega T alone never bind (feasible for every T 2-6 s, hip reach >= 8 m): the limits that bind are whole-body (hip/knee torque at 1.0, knee ROM, friction on the swing/touchdown impulses, F_z/ankle torque).
Not done: yaw (`--yaw`) rows, so the rows carry no arm trajectory (qp_arm_ref reads zero shoulder angles); the first --yaw attempt at T <= 3 s from the r4 warm start (a010_T30/T36) did not converge (shared cone use 60x). Rows 0.03-0.10 are within 1e-5 .. 4.1e-5 of the equality residual tolerance (exported with tol 5e-5, labelled FEASIBLE in the v5 files; honest verdict string of the solver: NOT CONVERGED on the iteration limit).

## Part 1b: Unity (controller flags, `qp_ref_v5` = 1 reads gait_reference_qp_ladder_v5.json / gait_reference_qp_v5.json, rows above 0.02-0.09 + main 0.10)
New flags (all default 0): `qp_r7_noforce` (no support switch without a measured contact of the swing foot), `qp_r7_clock` (swing clock starts at the reference end of the double support, row k_ds, even if the rear foot still carries load), `qp_foot_cop_heel_m` / `qp_foot_cop_toe_m` (measured-contact CoP window [heel + 1.5, toe - 2.5] m in the QP rows). Base config = 89fa14f (qp_foot 1, qp_foot_meas 1, qp_foot_drive 0). 4 seeds, fall time s (`variants98.py`, `run98.sh`; ~1 min per 24 runs):
- v4 rows (old): control 28.5 22.3 21.9 27.3 -> **24.8** (reproduced); noforce 18.5; clock 16.4; cop window 21.9; qp_fcap 1.5 22.0; all four 18.8.
- v5 rows: rows only 21.5 27.8 26.6 29.0 -> **27.2**; noforce 18.0; **clock 26.1 24.6 29.6 29.0 -> 27.6**; all four 17.5.
Reading: the new rows alone move the median from 24.8 to 27.2 (within the 21-29 seed spread of every config); forcing the rear unload at the reference time shortens the transfer from 2.55 s to the reference 0.6 s (diag `ls97.py` xfer) but not the survival; "no support switch without contact" (noforce) is clearly harmful (18 s). None is stable on any seed.
