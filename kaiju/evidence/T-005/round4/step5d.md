# T-005 r4 step 9.5d: re-optimised Fr 0.07 / 0.08 / 0.09 ladder rows (v3 constraints): all FEASIBLE, all walk b5 criterion A (20 steps, no fall)
Solve (project root, scipy trust-constr, same body data, mu 0.40, 40 nodes, 500 iterations max, 1 pass, 1-2 min each): `stride_opt_tc.py --fr F --lat --trans --frate 0.5 --vlift 0.5 --v3 --nodes 40 --maxiter 500 --warm <w> --out <o>`; logs `Tools/GaitOpt/out/run_9_5d_<o>.log`. No threshold, body or config edit, no assist.
| Fr | stride (out/) | warm | verdict | v m/s | step m | 3D friction use max | b5 A (fell / steps / speed ratio / pitch rms deg / td) |
|---|---|---|---|---|---|---|---|
| 0.07 | `Tools/GaitOpt/out/stride_fr007_v3_a.json` | stride_fr008_lat | FEASIBLE | 5.40 | 26.3 | 0.838 | no / 20.00 / 1.011 / 0.46 / 21 |
| 0.08 | `Tools/GaitOpt/out/stride_fr008_v3_a.json` | stride_fr010_v3_n40p2 | FEASIBLE | 5.77 | 27.4 | 0.846 | no / 20.00 / 1.023 / 0.17 / 20 |
| 0.09 | `Tools/GaitOpt/out/stride_fr009_v3_a.json` | stride_fr010_v3_n40p2 | FEASIBLE | 6.12 | 27.5 | 0.896 | no / 20.00 / 0.970 / 0.30 / 19 |
A second Fr 0.08 solve warm-started from the old stride_fr008_lat did not converge (0.880, NOT CONVERGED) and was discarded. Criterion A run: `run_fid.py --preset b5 --friction stick --quiet --ref <stride> --tag d5_00X_A` (`out/proto/d5_00X_A.{json,npz,log}`); 0 saturated ticks, solver ok >= 99.98 %. PASS_ALL stays false (planar plant mu 0.8 vs 0.4 friction ticks > 1: 6.7 / 7.0 / 9.1 % of contact ticks), same measure as the Fr 0.10 b5 rows, not a threshold used here.
## Switch distances between rows (reference to reference, offline, `scripts/switch95d.py`)
Same 9 features and scales as step 5c (CoM x z, vx vz, trunk pitch, swing heel and toe x z, all relative to the stance heel; 0.05 L, 0.05 sqrt(gL), pitch 1 rad), d = RMS; same node index / best node anywhere in the stride:
| pair | same-node median / max | best-node median / max |
|---|---|---|
| 0.06 -> 0.08 | 0.72 / 0.92 | 0.44 / 0.76 |
| 0.08 -> 0.10 | 0.28 / 0.36 | 0.27 / 0.33 |
| 0.06 -> 0.10 (the jump that fell) | 0.96 / 1.05 | 0.63 / 0.86 |
| 0.06 -> 0.07 -> 0.08 -> 0.09 -> 0.10 | 0.38, 0.37, 0.12, 0.20 | max 0.51, 0.37, 0.16, 0.28 |
All hops are below the 5c hand-off thresholds (0.8 match, 1.2 climb); the 0.08 row halves the 0.06 -> 0.10 gap (0.96 -> 0.72, 0.28). The Unity gap (d > 2 between settled rows, step 5c) is therefore the measured state vs the next row, not the references.
## Files
`Assets/Game/Config/Mech/gait_reference_qp_ladder_v3.json` (rows Fr 0.02 / 0.04 / 0.06 as in 9.5b plus the three new rows, @v2 schema; the old `gait_reference_qp_ladder.json` and `gait_reference_qp.json` untouched), made by `scripts/export_ladder95d.py` (unchanged `export_reference_v2.py`). No .meta for the new json (Unity creates it); no Unity run. Not done: wiring `qp_ladder` to the new file; ladder in Unity.
