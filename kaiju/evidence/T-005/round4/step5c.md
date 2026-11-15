# T-005 r4 step 9.5c: state-matched hand-off onto the speed ladder: NOT STABLE (best 26.5 s, up from 24.2 s)
Harness = step 5b (`scripts/run95.sh`, ScenarioCli `walk_fr010`, batch Unity, 1 other Unity running); variants `scripts/variants95c_m{1..4}.txt`, summaries `step5c/summary_m{1..4}.txt`, best gait log `step5c/R1repro.first28s.gait.jsonl.gz`. Stage 2 on (`qp_lateral`, om_yaw 0.5, om_y 0.3, w_tau 1). No assist (extMax 0), no threshold / mech_frame / body_tuning / walk_tuning.json edit. All new code behind imp >= 2.5 and `qp_match*` flags (default off); dotnet Units.Mech 125 total / 118 pass / 7 KnownGap fail (unchanged), Physics.Body 112/0; Unity EditMode (ImpByteIdentityTests) not re-run (imp 0/1/2 paths untouched: new code only in ApplyQp, the QP log block is guarded).

## Built
1. State matching (`qp_match`): measured state vs every node of every ladder row. 9 features in the heading frame relative to the landed (stance) heel: CoM x and z, CoM vx and vz, trunk pitch, swing heel x / z, swing toe x / z. Distance d = RMS of the differences scaled by 0.05 leg length (lengths), 0.05 sqrt(g L) (velocities), 1 rad (pitch, effectively unweighted); hand off at best row / node when d <= `qp_match_thr` (0.8; 0.6 tried) and every scaled difference <= `qp_match_zmax` 1.5. The QP starts AT that node: contact flags, first-contact forces, torques, clock (t = k H) and the Xoff heel anchor are those of node k (`SagittalQpController.StartNode`).
2. Cycle 1 evaluated this at imp 2 touchdowns (`qp_match` only), cycle 2 on every single-support tick (`qp_match_ss`: loaded leg = planar leg B >= 0.85 W, other <= 0.05 W, only reference nodes with the same contact set). Ladder climb: next row entered at its best node when d <= `qp_match_up_thr` 1.2 (`qp_match_hold` touchdowns on the row, `qp_match_max_row` cap).
3. Not built: the transition-stride optimisation (rule: only if no row matches within 10 touchdowns; rows did match, d 0.35-0.78).

## Results (3 diagnose -> fix -> Unity cycles + 1 reproduction run of the code as committed)
| run | start | falls |
|---|---|---|
| m1 touchdown match, d <= 0.6-1.0 (all variants hand off at 11.38 s, d 0.35, row Fr 0.04 node 25, 3.6 m/s) | DS touchdown | 15.4 s (imp 2 control 14.8) |
| m2 single-support match, thr 0.8 (hand-off 7.48 s, d 0.78, row Fr 0.02 node 29, 1.3 m/s) / thr 0.6 (9.87 s, d 0.51, node 14, 2.5 m/s) | SS | stay on row 0: 18.6-20.2; with climb (hold 2): 21.5, **26.5** |
| m3 QP-touchdown climb gating, thr 0.6 | SS | 18.1-19.9 (next row never matched, d > 2; V4 / V9 repeated JSON keys: the repeated om_yaw was ignored, those two are copies of the base) |
| m4 = m2 climb rule as committed (`qp_match_climb_td`), cap row 0 / 1 / 2 / none | SS | 19.0 / 20.6 / 19.8 / **26.5** |
Best run (m2 S08h2 = m4 R1): QP from 7.5 s, ladder Fr 0.02 -> 0.04 (10.5 s) -> 0.06 (13.6 s) -> 0.10 (14.7 s), yaw within +-10 deg and lateral within +-1.5 m until 14.7 s, then 11.7 s on the Fr 0.10 row, falls at 26.5 s, 87 m walked.

## What binds
1. The start is solved: from a state-matched node the QP (stage 2 on) walks 11-19 s instead of 3-4 s, with no brake at the friction cap at the hand-off (cycle 1's DS touchdown start still failed: the landing impact, the stance foot at 1.6 W and the still-loaded rear foot 1.2 W, vz to +2.4 m/s, because the reference at that node has the rear foot unloaded).
2. What fails now is yaw: every run spins in a single support while friction sits at the cap (frc 0.9, realised friction / (0.4 fn) 0.8-2.4): yaw 7 -> -24 deg in 1.5 s (m2 thr 0.6), 2.3 -> 25 deg in 1 s (row 0 hold). The swing-leg yaw momentum needs about 1e8 N m from the stance foot, the sagittal CoM task already uses the cone (the row speed is overshot: 3.3 on the 2.9 m/s row, 5.9 on the 4.1 m/s row). The run that climbed spins right after the Fr 0.06 -> 0.10 switch (yaw 2 -> 40 deg in 1.5 s at 15.5-17 s) and recovers badly.
3. Row-to-row climbing from a settled row never matches (d > 2 between rows 2.9 / 4.1 m/s steady); it only matched in the transient of the early walk. A per-row settling stride or a re-optimised row between Fr 0.06 and 0.10 (the planned Fr 0.08) is the next step; the 0.06 -> 0.10 jump itself falls (yaw spin) and is reported, not fixed.

## Next (not done)
Yaw authority on the QP: reduce the sagittal friction use (CoM gains / margin fed back from the realised cone ratio), or put the swing-leg yaw momentum into the reference (3D rows). M-001@v1.1 / M-002@v1.1: not computed (no stable walk, never 3 steady strides without a fall); both stay FAIL / not evaluated for imp 3.

## Code
`qp_match`, `qp_match_ss`, `qp_match_thr / zmax / up_thr / up_zmax / hold / max_row / climb_td / *_scale / load_hi / load_lo` (WalkTuning); `SagittalQpController.StartNode / Reference`, `QpReference.TauAt`, `WholeBodyQpDriver.Reset(anchor, startNode)`, `LocomotionController` (BuildMatchFeatures, MeasureMatch, BestMatch, SsConfig, hand-off / climb), gait log `qm` block (match distance, row, node). Offline: `scripts/match95c.py`, `scripts/ss95c.py` (match distance per imp 2 touchdown / single-support tick from a gait log).
