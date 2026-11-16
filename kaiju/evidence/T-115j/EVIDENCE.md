# T-115j evidence (role-aware commit range, P4 under bay_cost@v2)
Code: `PackConfig.CommitRangeFracAnchor` (default 0 = off) + `CommitRangeFracFor(role)`; runner `--duration`; holdout.sh `M007_HUNT_DURATION`, `M007_ONLY_HUNT`. Default `PackConfig` output bit-identical (9 same-seed logs, `diff -r` empty vs the T-115i build). Evaluator rev 2, `bay_cost@v2`, no threshold touched.
- `base/`: default (Flanker/Bait 1.6, Anchor 1.6) re-run on this HEAD; reproduces evidence/T-115h/v2 numbers exactly.
- `ra1/`: the prescribed experiment, `M007_PACK="CommitRangeFrac=4.5,CommitRangeFracAnchor=1.6"` (Bait/Flanker wide, Anchor default). Config cycle 1.
- `ra5/`: the mirror, `M007_PACK="CommitRangeFrac=2.5,CommitRangeFracAnchor=4.5"` (Anchor wide, Flanker/Bait moderately wide), picked on dev seeds. Config cycle 2.
- `fresh_base/`, `fresh_ra5/`: same two configs on fresh hunt seeds 4001-4030 (hunt + hunt_mv2 only), a robustness read of the ra5 P4 pass.
- `h900_base/`, `h900_ra5/`: versioned 900 s hunt reading (hold-out seeds, hunt + hunt_mv2 only), side by side with the 600 s one.
- `clause_tables.txt`: all tables below. Commands: `M007_PACK=... KEEP=1 M007_LOGS=<scratch> Tools/m007_bay_holdout.sh evidence/T-115j/<tag> [hunt_seeds]`; 900 s adds `M007_HUNT_DURATION=900`; fresh adds `M007_ONLY_HUNT=1` and seeds `4001-4030`. Logs (about 7-9 GB each, gzipped) stay out of the repo.

## Hold-out clause table (rev 2; seeds 3001-3030 hunt, 3001-3010 mixed/raid)
| clause (measure) | thr | base (old) | ra1 4.5 / 1.6 | ra5 2.5 / 4.5 | cr45 (T-115i) |
|---|---|---|---|---|---|
| C1 fr_commit | >= 0.6 | PASS 0.9189 | PASS 0.9067 | PASS 0.9024 | PASS 0.8916 |
| C2 fe_vs_nofocus (hunt_mv@v2) | >= 0.5 | PASS 0.9576 | PASS 0.9575 | PASS 0.9702 | PASS 0.9702 |
| P1 member_fit_time_min | >= 0.7 | PASS 0.8319 | PASS 0.8283 | PASS 0.8458 | PASS 0.8458 |
| P4 bs_rate_minus_k_nobait (bs_rate >= 0.4) | >= 0 | FAIL -0.2542 (0.500) | FAIL -0.2406 (0.500) | **PASS +0.0010 (0.573)** | FAIL -0.0036 (0.573) |
| P5 kx_ratio_vs_noroles (CI lower >= 1) | >= 1.25 | FAIL 0.9919 (0.886) | FAIL 0.9852 (0.875) | FAIL 0.8146 (0.717) | FAIL 0.8181 (0.720) |
| P6 participation_share | >= 0.9 | FAIL 0.6629 | FAIL 0.6629 | FAIL 0.6554 | FAIL 0.6488 |
| I1 raid_completion_rate | >= 0.8 | PASS 1.0000 | PASS 1.0000 | PASS 1.0000 | PASS 1.0000 |
| I3 ival_per_kaiju_hour_ratio | >= 1.2 | FAIL 0.9716 | FAIL 0.9716 | FAIL 0.9716 | FAIL 0.9716 |
| hunt_mv@v2 C1 fr_commit (clause FAIL in every arm) | >= 0.6 | 0.6377 | 0.6099 | 0.5455 | 0.5820 |
| hunt_mv@v2 P6 | >= 0.9 | FAIL 0.5000 | FAIL 0.5027 | FAIL 0.5000 | FAIL 0.4966 |
P2, P3 PASS in every arm.

## Dev seeds 1-12 (never used for the verdict)
| config (Flanker/Bait, Anchor) | C1 | P1 | P4 | P5 | LH strict bait-strike events |
|---|---|---|---|---|---|
| default 1.6 / 1.6 | 0.688 | FAIL | 0.015 | 0.708 | 1/6 |
| 4.5 / 1.6 (ra1; 3.0 / 1.6 identical) | 0.647 | FAIL | 0.013 | 0.687 | 1/6 |
| 4.5 / 2.5 | 0.727 | FAIL | 0.023 | 0.812 | 4/6 |
| 1.6 / 4.5 | 0.682 | FAIL | 0.032 | 0.659 | |
| 2.5 / 4.5 (ra5) | 0.714 | PASS | 0.049 | 0.837 | |
| 4.5 / 4.5 (T-115i) and 4.5 / 3.5 | 0.778 | PASS | 0.050 | 0.869 | 5/6 |

## Robustness and horizon readings (side by side, not replacing the 600 s reading)
- Fresh seeds 4001-4030 (hunt only): P4 minus nobait base -0.0036 vs ra5 -0.0951 (both FAIL); C1 0.588 vs 0.531 (both FAIL; this seed set is much less favourable than 3001-3030 for every arm); P5 (hunt-only) 1.064 vs 1.013; hunt_mv@v2 C1 0.656 vs 0.585. The ra5 P4 pass does not replicate.
- 900 s hunt (3001-3030, hunt only; I1/I3 not rerun): base P4 -0.2542 (600 s) -> -0.1248 (900 s), still FAIL; ra5 +0.0010 -> -0.0198, FAIL. C1 0.919 -> 0.844 (base), 0.902 -> 0.787 (ra5); P1 base 0.832 -> 0.759. A longer horizon helps the default P4 (the soft-ground arrival delay is partly a horizon cut) but does not rescue any config; P5 here is hunt-only, so it is not comparable to the 600 s hunt+mixed value.
