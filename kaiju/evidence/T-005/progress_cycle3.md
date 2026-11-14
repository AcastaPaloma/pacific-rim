# T-005 fix cycle 3: progress table

Capture `walk_fr020` (63 s; intent: stand 3 s, then walk at the Froude 0.20 effort), no assistance. Measured by `Tools/PhysicsMetrics/stage.py`: speed and Froude over the last 20 s before the end or a fall; slip = M-002.1 max and M-002.2 p95 from the T-002 tool on this one capture; peak foot force and peak power after t = 25 s (the whole-capture values of the adopted version are in `EVIDENCE.md` section 0); foot v_y = median vertical foot speed at `contact_begin`.

## A. Does the tracked reference reproduce the stable walk? (step cap 0.25 leg lengths)
| Stage | Fall | Speed m/s | Froude | Slip max, p95 | Peak foot force BW | Peak power MW | Mean power MW | Foot v_y m/s |
|---|---|---|---|---|---|---|---|---|
| base = cycle 2 result (3a0f3ca) | no | 2.96 | 0.021 | 0.79 m, 0.58 m | 3.88 | 479 | 100 | -1.07 |
| G0 whole reference tracked (swing path, pitch, arms, trunk) | fell 32 s | 3.61 | 0.032 | metres | 9.97 | 2936 | 500 | |
| G7 inverse-dynamics feed-forward alone, gain 1.0 | fell 9 s | | | | 15.3 | 6629 | | |
| G8 inverse-dynamics feed-forward alone, gain 0.5 | no | 3.01 | 0.022 | 1.63 m, 0.85 m | 4.15 | 1335 | 129 | -1.19 |
| Hs swing path and foot pitch from the reference, upper body as before | no | 2.55 | 0.016 | 2.04 m, 1.84 m | 8.09 | 2614 | 150 | |
| Hsp0 swing path from the reference without the foot pitch | no | 2.42 | 0.014 | 1.19 m, 0.97 m | 2.75 | 1948 | 121 | |
| Hu arms and trunk from the reference (weight 1.0), swing as before | no | 3.13 | 0.024 | 0.87 m, 0.63 m | 3.37 | 312 | 94 | -0.26 |
| **Hu5 arms and trunk from the reference, weight 0.5 (adopted, bedecf4)** | no | **3.07** | **0.023** | **0.51 m, 0.32 m** | 3.77 | **278** | 94 | -1.04 |

R-007's expectation for its stage 3 and 4 (pre-swing start with inverse-dynamics feed-forward; arm and trunk angular-momentum compensation): swing-start power from 2,485 MW to under 450 MW, free moment inside the 1.65e8 N m budget. Measured: the trunk and arm compensation alone brought the steady-walk power peak to 278 to 312 MW and cut slip; the feed-forward as implemented here (target acceleration by finite differences) made the walk worse and is off (`swing_ff_gain` 0).

## B. Continuation in step length (commanded Froude 0.20 throughout; the step cap is what limits speed)
| Step cap (leg lengths) | Fall | Speed m/s | Froude | Slip max, p95 | Peak foot force BW | Peak power MW | Mean power MW |
|---|---|---|---|---|---|---|---|
| 0.25 (adopted) | no | 3.07 | 0.023 | 0.51 m, 0.32 m | 3.77 | 278 | 94 |
| 0.27 | no | 3.20 | 0.025 | 0.99 m, 0.45 m | 5.14 | 661 | 119 |
| 0.29 | no | 3.39 | 0.028 | 2.04 m, 1.03 m | 5.29 | 1604 | 143 |
| 0.31 (last increment that stays up for 63 s) | no | 3.54 | 0.030 | 2.90 m, 1.34 m | 6.81 | 1610 | 172 |
| 0.33 | fell 32 s | | | | | | |
| 0.35 | fell 22 s | | | | | | |
| 0.37 | fell 20 s | | | | | | |

Three consecutive increments fell (0.33, 0.35, 0.37): stop rule reached. With the upper-body weight at 1.0 the cap 0.27 walk is cleaner (slip 0.99 m / 0.26 m, 3.99 BW, 443 MW) but `curve` falls and `walk_scn` falls at weight 1.0, so the adopted version is cap 0.25 with weight 0.5, the only one of these in which `curve`, `turn90_inplace`, `stop`, `estop`, `stomp`, `walk_scn` and the shoves all behave. The caps 0.27 to 0.31 walk without falling but slide metres per contact; they are recorded, not adopted.
