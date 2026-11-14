# T-005 fix cycle 2: progress table

One row per stage. Capture `walk_fr020` (63 s, intent: stand 3 s, then walk at the Froude 0.20 effort), all assistance off (none exists). Measured by `Tools/T005/stage.py` (speed and Froude over the last 20 s; slip = M-002.1 max and M-002.2 p95 from the T-002 tool at 0db318e on this one capture; peak foot force raw per step after t = 25 s; power = sum of positive joint power from the gait log).

| Stage | Fall | Speed m/s | Froude | Slip (M-002.1 max, M-002.2 p95) | Peak foot force, body weights (t > 25 s) | Peak power MW (t > 25 s) | Mean power MW | Median foot v_y at contact m/s | R-007 expectation for the stage |
|---|---|---|---|---|---|---|---|---|---|
| 0 baseline (17d2e9a) | no | 2.88 | 0.020 | 0.53 m, 0.24 m | 4.49 | 2485 | 128 | -1.50 | |
| A1 swing-leg gains softened before contact (x0.15) | no | 3.13 | 0.024 | 1.45 m, 1.12 m | 6.15 | 2748 | 154 | not measured | R-007 #1 (softer gains on loading): peak <= 1.3 BW |
| A2 landing damping + lower, later foot lift (clearance 4 m -> 3 m) | no | 2.91 | 0.021 | 0.34 m, 0.25 m | 5.03 | 1119 | 102 | not measured | |
| A3 ankle passive while the foot settles | no | 2.89 | 0.020 | 0.38 m, 0.24 m | 4.96 | 1011 | 104 | not measured | |
| A4 weight transfer limited by the landed foot's measured load (lead 0.2) | no | 2.99 | 0.022 | 0.97 m, 0.36 m | 3.36 | 515 | 95 | not measured | |
| B1 longer steps by parameters (cap 0.30 to 0.55 l, less crouch) | all 7 variants fall (18 to 32 s) | up to 3.8 before the fall | up to 0.035 | metres | 6 to 17 | 1400 to 4300 | | | R-007 #1: 5.5 m/s |
| B2 toes-up swing (8 deg), clearance 4 m, duty 0.60 at cap 0.35 l | all 6 variants fall (18 to 45 s) | up to 4.26 before the fall | up to 0.044 | metres | 3.8 to 12 | 1300 to 4300 | | | |
| C trunk counter-rotation (waist yaw against the legs, gain 0.6), cap 0.25 l | no | 2.98 | 0.022 | 0.74 m, 0.34 m | **2.74** | 498 | 104 | -0.95 | R-007 #4: trunk yaw +/- 3 deg, same speed |
| C same at cap 0.35 l with B2's swing | no fall in 63 s | 3.60 | 0.031 | 3.25 m, 2.16 m | 9.33 | 3099 | 241 | | |
| D vaulting stance: near-straight knee (15 deg at loading, 5 deg mid-stance), hip height from leg geometry (R-007 #1), cap 0.25 to 0.45 l | cap 0.25: no; cap 0.35 and 0.45: fall at 21 to 33 s | 2.70 (cap 0.25) | 0.018 | 1.83 m, 1.01 m | 5.75 | 1536 | 135 | | R-007 #1: 5.5 m/s, <= 1.3 BW, < 450 MW |
| E earlier hover and slower final descent (hover at 0.50 to 0.65 of swing, 0.4 m/s) | 5 of 7 variants fall | | | | 1.4 to 10 | 1000 to 4500 | | best non-falling -0.62 | R-007 #1: foot v_y <= 1.0 m/s |
| **F adopted: A2 + A3 + A4 with lead 0.35 + C (gain 0.6)** (3a0f3ca) | no; `curve` and `turn90_inplace` no longer fall | 2.96 | 0.021 | 0.79 m, 0.58 m | 3.88 | 479 | 100 | -1.07 | |

Whole-capture values for F (start included, as the physics evaluator requires): peak foot force 5.75 body weights at t = 13.1 s, peak power 2,012 MW at t = 7.9 s (the first step), mean 105 MW.

Notes.
- A1 was rejected (worse on every column) and its gain scale set back to 1. The touchdown peak was traced in A3's log: the heel touches at 1.4 to 1.7 m/s, the plan moved the body weight to the new leg on a timer while only the heel edge carried load, the body dropped for 0.2 s and landed on the flattening foot. A4 makes the trailing leg keep the load until the landed foot bears it.
- A4 with lead 0.2 gave the lowest steady-walk peak (2.74 with C) but `turn90_inplace` then fell at 29 s; lead 0.35 keeps the turn, `walk_scn` and `curve` on their feet, so it was adopted.
- B1, B2, D and E are the "two consecutive stages without measurable improvement" of the cycle brief (four, in fact): every attempt to lengthen the step or re-shape the swing left the narrow stable basin around the 10.5 m step. The vault (D) and toes-up (B2) code stays in the controller behind config values that are off (`vault` 0, `swing_toe_up_deg` 0); they are not assistance, only unused gait options.
- R-007's stage 1 expectation (5.5 m/s, 1.3 BW, under 450 MW) was not reached: measured 2.96 m/s, 3.88 BW (5.75 over the whole capture), 479 MW (2,012 over the whole capture).
