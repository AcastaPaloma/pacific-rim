PASS

# T-002 eval-style: are the M-003 / M-004 tools numerically valid?

Evaluator: eval-style, 2026-11-14. Subject: branch `task/T-002`, commit `0db318e`. Question: metric validity only
(criteria C3, C4, C5, C6, C12, C17 for M-003 / M-004, plus the style readings against `expectations/T-002.md`).

**Code under test.** While I was testing, the shared worktree `.wt/T-002` gained uncommitted edits (`gait/m001.py`,
`gait/m002.py`, `registry.py`, `tests/test_registry.py`) and new `registry/M-00x@v1.1.yaml` files, all at 11:41-11:42.
I did not use them. Every result below was produced on a clean `git archive 0db318e` copy
(`/private/tmp/claude-501/eval-T-002-validity/clean/kaiju`, registry revision v1.0 in every report). I also ran the
style runs once on the worktree before the edits and got identical numbers.

**Inputs (my own, independent of `fixtures/gen_*.py`).** Everything is in `/private/tmp/claude-501/eval-T-002-validity/`:
`capset.py` (my `style_capture@v1` writer), `colourref.py` (my sRGB to Lab D65 and CIEDE2000, Sharma 2005),
`gen_m003.py`, `gen_m004.py`, `run_m003.sh`. The scenes are slate_teal, 128x72 internal, scale 4, 12 gameplay frames
plus a `haze_cal` frame with `haze_targets`. Before scoring, every set passed `metrics.py validate style_capture`
(exit 0). Truth comes from my own code, never from the tool. Run: `metrics.py m003 <set> --profiles slate_teal` and
`metrics.py m004 <set> --profiles slate_teal --fixture`.

## Results: truth vs tool
| Sub-measure / criterion | Input | My truth | Tool | Verdict |
|---|---|---|---|---|
| C5 CIEDE2000, 13 Sharma pairs (1-4, 7, 13, 17-21, 25, 34), both orders | published table | table dE00 | max abs err 4.1e-5 (= table rounding) | ok |
| C5 sRGB to Lab D65 (white, black, R, G, B, grey 128) | known values | e.g. R 53.241/80.092/67.203 | within 0.005 | ok |
| M-003.a internal, frame 5 | 300 px of #595974 (dE00 10.08) + 40 px at dE00 0.34 | 0.967448 | 0.967448, FAIL (< 0.995) | ok |
| M-003.a output, same | nearest x4 | 0.967448 | 0.967448, PASS (>= 0.95) | ok |
| C12 worst offender | same | f000005, #595974, 4800 output px, internal box x 90-109, y 30-44 | `where` f000005, offender #595974 dE 10.077, 4800 px; heat-map hot box (output) x 360-439, y 120-179 = my box | ok |
| M-003.a, unquantised ramp | continuous L* ramp | 0.638889 | 0.6389 internal and output, FAIL | ok |
| M-003.a output, bilinear upscale | PIL bilinear x4 | 0.680725 | 0.6807, FAIL | ok |
| M-003.d-iv, point upscale | nearest x4 | 1.0 | 1.0 PASS | ok |
| M-003.d-iv, bilinear upscale | PIL bilinear x4 | 0.073676 (my block count) | 0.0737 FAIL | ok |
| M-003.b DIG / O, Bayer-4x4 gradient (0.84 L*/px) | R-002: 0.70 / 1.12 | ~0.70 / > 0.5 | 0.755 / 1.166; b PASS (12/12 frames) | ok |
| M-003.b DIG / O, white-noise dither | R-002: 0.58 / 0.06 | ~0.58 / ~0 | 0.525 / 0.101 | ok |
| M-003.b DIG, posterised, no dither | R-002: 0.00 | 0 | 0.000, b FAIL | ok |
| M-003.b DIG, continuous ramp | R-002: 0.01 | ~0 | 0.000, b FAIL | ok |
| M-003.b, flat fill, 1 depth band | no gradient | frames skipped, INCOMPLETE | b NOT_RUN, c1 NOT_RUN, INCOMPLETE (exit 3) | ok |
| M-003.b DIG_max (advisory) | Bayer | 0.755 > 0.5, warn only | FAIL advisory; headless PASS | ok |
| M-003.c1 monotone haze | band means (mine) 21.55 / 33.63 / 42.28 / 50.70 / 58.67 / 65.24 | pass: rho 1, drop 0, span 43.69 | same means, rho 1.0, span 43.688, PASS | ok |
| M-003.c1 reversed haze | means 68.49 to 24.47 | fail: drop 11.82, rho -1 | FAIL, max drop 11.824 | ok |
| M-003.c1 equal band means (no haze, flat L) | flat fill over 6 bands | FAIL (span 0 < 15) | **exit 4, TypeError** | DEF-style-1 |
| M-003.c2 pass case | signed dL 39.72, 19.01, 13.25, 8.48, 0, 0 | pass | same values, PASS | ok |
| M-003.c2 rising contrast | dL -12.62 at 4 km, -8.34 at 8 km | fail: rise +4.28 | FAIL 4.281 | ok |
| M-003.c2 sign | dL ... 8.48, -12.62, -20.96 | signed: pass; absolute: fails both rules | gate PASS; `info` abs_reading non_increasing false, far_ok false | as specified (see readings) |
| M-003.d-i value levels | 11 entries, chain merge at dL* < 4 | 8 | 8 | ok |
| M-003.d-ii hue families | one hue | 1 | 1 | ok |
| M-003.e sky / horizon (advisory) | 8 sky rows of 72; pitch 0 | 0.1111 / 0.5 | 0.1111 / 0.5 (advisory warn) | ok |
| M-004.S0 static, injected | 50 px toggling every frame, 590 px stepping once | S0 = 3540 / (59 x 9216) = 0.0065104; F = 50/9216 = 0.0054253 | 0.0065104 / 0.0054253, FAIL | ok |
| M-004.S0 static, temporal noise | each pixel redrawn per frame | 0.500167 / F 0.250595 | 0.500167 / 0.250595, FAIL | ok |
| M-004.S0 static, clean | identical frames | 0 / 0 / 0 | 0 / 0 / 0, PASS | ok |
| M-004.S_px, F, T3 (1.0 px/frame) | plane at 600 m, camera +5.4127 m/frame, 40 world texels toggling | S_px = 40/(72 x 127) = 0.0043745; F = 40/(72 x 126) = 0.0044092 | 0.0043745 / 0.0044092, lag 1, PASS | ok |
| C17 baseline ref_01A, ref_03B, ref_06A | `metrics.py baseline --panels ...` | R-002 tables / `m003_validation.json`: DIG 0.259, 0.331, 0.095; O -0.336, -0.251, 0.149; levels 4, 10, 3; hue 1, 1, 2; contrast 27.1, 49.5, 77.0; raw A_2 in 0.63-0.85 and 06A 0.16 | identical to 3 decimals; raw A_2 0.731, 0.764, 0.161; verdict NOT_APPLICABLE | ok |

**Largest truth-vs-tool error on a closed-form quantity: none above 1e-4.** A_2, uniform-block share, band means,
S0, S_px and F all match my own computation to the printed precision. CIEDE2000 matches to 4.1e-5, which is the
4-decimal rounding of Sharma's table. The DIG / O values differ from R-002's table by up to 0.06, which I expected:
my Bayer and noise gradients are not R-002's synthetic scene, and the ordering and signs are as specified.

## Spec readings reviewed (style S1-S19)
- **S1 `palette_sha256`: agrees** with the reconciled spec. I hashed compact JSON (lowercase `#rrggbb`, declared
  order, no spaces) from the spec text alone, got `a35522b1...` for slate_teal, and the validator accepted it. A hash
  of the same list with `", "` separators is rejected with expected and found values.
- **S7 c2 sign: implemented as the reconciled spec orders**: signed light-minus-dark gates, and the absolute reading
  is in `info`. **I disagree with the spec reading itself, not with the tool.** The signed rule passes a target pair
  whose contrast *inverts and grows* with distance: my `c2signed` case has |dL| rising 8.5, 12.6, 21.0. Koschmieder
  decay is about |contrast|, so this makes c2 easier to pass than the physics behind it. Question for the scribe:
  gate on the absolute value as well, or require light > dark at every distance.
- S2-S5, S8, S11 and S14-S16: literal, or fixed by the reconciled spec. My inputs found no case where one of them
  made a gate easier to pass. S3 reproduces R-002's Bayer and noise values on my inputs as well. S12 also excludes
  `scripted` sequences from a verdict, and the reading is listed. S13 (C_px on the M-003 b interleaved map, not
  R-002's 4-neighbour mask) is listed and does not change S0 / S_px.
- None of the style readings departs from the spec without being listed.

## Defects
- **DEF-T002-style-1 (minor): M-003 crashes when the band means are equal.** If every evaluated depth band of a
  gameplay frame has the same mean L* (for example a flat no-haze scene), Spearman rho is NaN and `_add_c1` raises
  `TypeError: '<' not supported between NoneType and float`. The run ends with exit 4 (TOOL_ERROR) and writes no
  report. This is never a false pass, but the R-002 "no haze" negative control has no verdict in this form.
  Repro: `python gen_m003.py`, then, from the clean copy, `metrics.py m003 .../style/flat --profiles slate_teal`.
  The flat set is a single palette colour over the monotone depth map. PASS would look like: c1 FAIL on span
  (0 < 15 L*) with rho reported as undefined, and exit 1.
- **DEF-T002-style-2 (minor): O reads 0.0 when it is undefined.** M-003.b.O shows `REPORTED 0.0` when no
  interleaved window exists (posterised and continuous sets). R-002's table lists O as undefined ("—") there, and a
  reader can mistake 0.0 for "noise-like". The value is informational only. Repro: the `poster` or `smooth` set
  above. PASS would look like: value null, with a note such as "no interleaved windows".

No blocker or major defect: every gating style number matches the truth, and no input produced a wrong pass.
