# T-005 fix cycle 2: progress table

One row per stage. Capture `walk_fr020` (63 s, intent: stand 3 s, then walk at the Froude 0.20 effort), all assistance off (none exists). Measured by `Tools/T005/stage.py` (speed and Froude over the last 20 s; slip = M-002.1 max and M-002.2 p95 from the T-002 tool at 0db318e on this one capture; peak foot force raw per step after t = 25 s; power = sum of positive joint power from the gait log).

| Stage | Capture | Fall | Speed m/s | Froude | Stride s | Slip | Peak foot force (body weights) | Peak power MW | Mean power MW |
|---|---|---|---|---|---|---|---|---|---|
| 0 baseline (17d2e9a) | walk_fr020 | no fall | 2.88 | 0.020 | 7.6 | max 0.53 m, p95 0.24 m | 4.49 | 2485 | 128 |
| A1 swing-leg gains softened before contact (x0.15) | walk_fr020 | no fall | 3.13 | 0.024 | 4.4 | max 1.45 m, p95 1.12 m | 6.15 | 2748 | 154 |
| A2 landing damping + lower, later foot lift (clearance 3 m) | walk_fr020 | no fall | 2.91 | 0.021 | 5.8 | max 0.34 m, p95 0.25 m | 5.03 | 1119 | 102 |
| A3 ankle passive while the foot settles | walk_fr020 | no fall | 2.89 | 0.020 | 7.5 | max 0.38 m, p95 0.24 m | 4.96 | 1011 | 104 |
| A4 weight transfer limited by the landed foot's measured load (kept) | walk_fr020 | no fall | 2.99 | 0.022 | 4.9 | max 0.97 m, p95 0.36 m | 3.36 | 515 | 95 |

Notes. A1 was rejected (worse on every column) and its gain scale set back to 1. The touchdown peak was traced in A3's log: the heel touches at 1.4 to 1.7 m/s, the plan moved the body weight to the new leg on a timer while only the heel edge carried load, the body dropped for 0.2 s and landed on the flattening foot. A4 makes the trailing leg keep the load until the landed foot bears it.
