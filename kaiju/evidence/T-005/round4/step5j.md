# T-005 r4 9.5j: Opus fix plan (QP joint-range rows, foot-pitch ramp at the ankle stop, stance contact hold, rematch + late_cx) built and tested: still NO stable walk at Fr 0.10 (4-seed median 23.1 s best config / 15-21 s others vs base 18.9 s; best 27.7 s vs base 29.6 s)
Flags (all default 0, imp 3 only, in `MechContracts.cs`; none in a json): `qp_rom` (+ `qp_rom_margin_deg` 3, `qp_rom_horizon_s` 0.1, `qp_rom_amax` 30), `qp_fp_rom` (+ `qp_fp_rom_band_deg` 3), `qp_z_hold_ss` (m). No assist, no threshold / mech_frame / body_tuning / walk_tuning.json edit, no lower Froude target.
## Built
1. `SagittalQpController`: 12 inequality rows on qdd (hip / knee / ankle of both legs, lower and upper, via the CJ rows; ranges from the DOF's anatomical `LowerRad/UpperRad` set by `WholeBodyQpDriver`), velocity-damper form: qd + qdd h within +-sqrt(2 a d) of the margin point (d = distance to the limit minus margin; +-d/h back once past it), bound on qdd limited to +-2 a, so a joint already on the stop asks for a reachable push-off. The Opus form q + qd h + 0.5 qdd h^2 as a pure position row was tried first: on the golden replay it moved torques by 1.4 LIM (nominal knee motion trips it), so it was replaced by the damper (0.22 LIM). Infeasible tick: the ROM rows are dropped first, then the torque rows (prototype fallback); `RomRowsDropped` stayed 0 in the batches. `MaxIn` 64 -> 80.
2. `qp_fp_rom`: the foot-pitch task weight of a leg ramps 1 -> 0 as its ankle approaches the margin point (band 3 deg).
3. `qp_z_hold_ss`: ZHold = max(qp_z_hold, z): a scheduled-loaded point stays in the contact set through a force dropout while below z (1.0 / 3.0 m).
4. Gait log `qp.j` = [nS, rom rows, binding-joint mask, rows dropped, foot-pitch scale A, B] when `qp_rom` or `qp_z_hold_ss` is on. New dotnet test `RomRows_GoldenReplay_StayFeasibleAndMoveTheTorquesLittle` (golden plant has no ROM: knee runs to 1 deg, ankle to -23.5 deg).
## Results (3 Unity cycles, `step5j/summary_j{1,2,3}.txt`; base J1cap035 = v4 + arm_ref + yaw_cap 0.035 reproduces 9.5h: 29.6 / 18.7 / 19.1 / 18.7, median 18.9; fall s, 4 seeds s0..s3)
| cycle | config | seeds | median | best |
|---|---|---|---|---|
| 1 | rom | 21.9 18.5 20.5 20.2 | 20.4 | 21.9 |
| 1 | rom + fp | 20.7 16.7 21.4 20.3 | 20.5 | 21.4 |
| 1 | rom + fp + z_hold 1.0 | 22.0 16.7 15.5 19.9 | 18.3 | 22.0 |
| 1 | z_hold 1.0 only | 21.6 18.7 16.7 20.8 | 19.8 | 21.6 |
| 1 | rom + fp + z1 + rematch + late_cx | 21.4 20.6 23.2 20.9 | 21.1 | 23.2 |
| 2 | margin 6 / 10 deg (rom + fp + z1) | 20.8 17.4 20.4 18.6 / 14.8 15.2 13.2 16.2 | 19.5 / 15.0 | 20.8 / 16.2 |
| 2 | amax 60 / 15; horizon 0.05 | 19.8 / 19.4 / 17.3 | | 23.2 / 22.8 / 23.4 |
| 2 | margin 6 + fp band 8; z_hold 3.0 | 19.9; 19.6 | | 20.9; 21.1 |
| 2 | **margin 6 + z_hold 3 + rematch + late_cx** | 20.5 27.7 25.7 18.6 | **23.1** | **27.7** |
| 3 | margin 1; amax 100 + margin 6; rom + fp + rematch + late; horizon 0.2 + amax 60 | | 18.1; 15.9; 20.9; 18.1 | 21.5; 19.5; 21.0; 23.3 |
## Findings
- The rows act (bound on 12 % of the ankle-A ticks before the fall, 0 rows dropped) and shorten the ankle's time on the -20 deg stop (R swing 15.2 s: 50 ticks at -20 in the base, 4-11 with rom), but the swings stay 0.5-1.5 s (reference 3.3 s) and the knee still folds to 45-65 deg: the fold is not only the pitch task through the ankle. Every config falls at 13-28 s, inside the seed spread of the base; the larger the margin the earlier the fall (10 deg: 15.0 s).
- The only config with a visibly better median (rom margin 6 + z_hold 3 + rematch + late_cx, 23.1 s, 2 of 4 seeds 25.7 / 27.7) is the stack of all four plan parts; no single part moves the median beyond +-1.5 s.
## Not done
No stable walk (no 3 steady strides at Fr 0.10), so M-001@v1.1 and M-002@v1.1 were not computed (not evaluated, FAIL as before). dotnet Units.Mech (Category!=KnownGap) 119/0 (incl. the new test); EditMode `ImpByteIdentityTests` 1/1 pass (`step5j/editmode.xml`).
