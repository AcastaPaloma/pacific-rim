# M0 packet: metric spread and intended order per set

Intended order = the pipeline control or mode ranking from the build script (1 = clearly good; ties allowed). Spearman is intended rank versus the severity number named per set (positive = the metric tracks the intended order). Values come from existing reports; nothing was re-captured. Calibration against founder ratings happens after rating, not here.

## M-003 (10 stills; 4 profiles + 6 controls; 2 hero slots TODO; 4 anchors)
Metric used for the order: number of gating sub-measures failing in the report that holds the capture (continuous_tone: the tool returned INVALID_INPUT, counted as 4, worse than every measured control).
Spearman(intended rank, gating fails) = 0.929, n=10.  Gating-fail counts: [0, 0, 0, 0, 1, 1, 2, 2, 3, 4].
Per-frame values (station frame 5):
| ID | intended | gating fails | a.output | b.DIG | d-iv | e.contrast |
|---|---|---|---|---|---|---|
| M003-01 | 6 | 4 | n/a | n/a | n/a | n/a |
| M003-02 | 1 | 0 | 1 | 0.3253 | 1 | 22.55 |
| M003-03 | 7 | 3 | 0 | 0 | 1 | 50.98 |
| M003-04 | 1 | 0 | 1 | 0.2731 | 1 | 34.9 |
| M003-05 | 4 | 2 | 0.8732 | 0.2878 | 0.7389 | 27.49 |
| M003-06 | 1 | 0 | 1 | 0.1785 | 1 | 41.55 |
| M003-07 | 5 | 1 | 1 | 0.329 | 1 | 14.98 |
| M003-08 | 1 | 0 | 1 | 0.2878 | 1 | 27.49 |
| M003-09 | 3 | 2 | 1 | 0.02888 | 1 | 31.44 |
| M003-10 | 2 | 1 | 1 | 0 | 1 | 27.49 |
Spread of a.output across the set: min 0, max 1. Caveat: palette share cannot rank dither_off, haze_off, reversed_haze (all 1.0); those controls fail other sub-measures (b.DIG, c1). The ordering among controls is my judgement of badness, not a measured fact. continuous_tone has no metric value (tool rejects its inputs).

## M-004 (10 sequences, 60 frames, eye70, slate_teal)
Metric used: worst value/threshold ratio over the shown sequence's S, C, F sub-measures (threshold 0 on S0 counted as 0.001). Y2 = 1 px/frame yaw, T2 = camera translate, S0 = static (frame-noise control).
Spearman(intended rank, severity) = 0.972, n=10.
| ID | mode | seq | intended | severity (x threshold) | S_px |
|---|---|---|---|---|---|
| M004-01 | hybrid_norotq | eye70_Y2 | 8 | 111 | 0.149644823286 |
| M004-02 | screen | eye70_T2 | 3 | 0.405 | 0.0205443507442 |
| M004-03 | screen | eye70_Y2 | 8 | 123 | 0.16579650661 |
| M004-04 | hybrid | eye70_Y2 | 2 | 0.55 | 0.00214345665599 |
| M004-05 | direction_nosnap | eye70_T2 | 5 | 1.09 | 0.0220129385419 |
| M004-06 | frame_noise | eye70_S0 | 10 | 411 | 0.0925839610797 |
| M004-07 | hybrid_nosnap | eye70_T2 | 4 | 0.771 | 0.0065944042626 |
| M004-08 | direction | eye70_T2 | 1 | 0.386 | 0.0202575943492 |
| M004-09 | direction | eye70_Y2 | 1 | 0.142 | 0.00077578593098 |
| M004-10 | direction_norotq | eye70_Y2 | 8 | 124 | 0.166042671826 |
Severity range 0.142 to 411 x threshold. Duplicates removed: direction, direction_nosnap and the base m004 run render identical Y2 frames (snap is irrelevant to yaw); screen_nostab equals direction_norotq on Y2; hybrid_nosnap equals hybrid on Y2. Translate sequences (T2) were used to show the snap difference instead. Fewer distinct modes exist than 10 on Y2, so 4 captures are T2 sequences.

## M-005 (10 captures, no deliberately bad end)
Only the four skyline captures have a per-capture number (IoU vs the independent lidar reference R-ind):
- M005-09: ind IoU 0.8275, src IoU 0.9946, gate pass
- M005-02: ind IoU 0.8566, src IoU 0.9942, gate pass
- M005-04: ind IoU 0.9011, src IoU 0.9964, gate pass
- M005-03: ind IoU 0.9113, src IoU 0.9974, gate fail
Spread of ind IoU 0.828 to 0.911. Intended order vs metric: not computable, because the pipeline has no control that degrades M-005 on purpose (nothing invented). The remaining six captures (oblique x2, shoreline, top-down, two plates) carry no per-capture number. Raters therefore judge recognisability only; the set does not span clearly bad.

## M-001/M-002 walk strips (10, UNMERGED, NOT AT TARGET PACE)
All ten strips come from one run at 3.07 m/s (Froude 0.023 against the 0.17-0.23 gate), M-001 FAIL, M-002 FAIL. The metrics are set-level (one value per run, not per strip), so there is no per-strip spread and no Spearman. The strips span scenarios (stand, start, walk, stop, estop, turn, curve, stomp, over-limit intent, shove-to-fall), not good-to-bad quality. walk_fr010/025/030 were left out because their strips are byte-identical in size to walk_fr020 (all capped at the same step length).
