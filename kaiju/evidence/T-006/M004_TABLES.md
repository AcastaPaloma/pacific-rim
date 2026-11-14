Per-sequence (library functions), motion re-captured with warm-up (6deae3d + motion warm-up): the static frame is exactly stable in all three modes (direction, hybrid, screen); push-in passes S and F in direction and screen but misses crawl C_px.T12 (0.164 vs 0.15), hybrid passes S and C but misses flicker F.T12 (0.0080 vs 0.005); the orbit misses all three yaw thresholds in every mode (it runs at 0.61 px/frame, 2.4x the Y1 nominal 0.25, so it is outside the class tolerance); the full M-004 gate is INCOMPLETE because the set has one profile and one viewpoint by design (T-004 holds the full gate).

Note: the first motion run's screen-mode static frame 0 differed from frames 1-29; that was the same unsettled-first-capture effect fixed for stills in 6deae3d, not dither instability. Values below the per-sequence heading are from the re-captured set (per_sequence.json).
- direction: INCOMPLETE (exit 3). No M-004 sub-measure was evaluated; the static frame is bit-identical (30/30 frames, one SHA-256).
- hybrid: INCOMPLETE (exit 3). No M-004 sub-measure was evaluated; the static frame is bit-identical (30/30 frames, one SHA-256).
- screen: INCOMPLETE (exit 3). No M-004 sub-measure was evaluated; the static frame is NOT bit-identical (frame 0 differs from frames 1-29).

Source commit: `git rev-parse --short HEAD` = d10ec34 (branch task/T-006, tool kaiju_metrics 1.0.0, registry M-004@v1.1 sha256:5f39e5531f9e).

## Why the tool returns INCOMPLETE

`metrics.py m004` requires named sequences S0, T1, T2, T3, Y1, Y2 for each of 3 profiles x 2 viewpoints (per the --help text and the registry). The capture folders contain only three sequences per mode, named by `sequence_id` in each frame JSON:

| folder sequence | sequence_id | motion | frames | mapping asked for | tool sequence |
|---|---|---|---|---|---|
| hero_static | hero_static | none (static) | 30 | static -> S0 | S0 |
| hero_pushin | hero_pushin | camera_forward, dolly | 120 | push-in -> T-type | T1/T2/T3 (not provided) |
| hero_orbit | hero_orbit | scripted orbit, 10 deg in 120 frames | 120 | orbit -> Y-type | Y1/Y2 (not provided) |

Tool coverage (identical in all three modes): profiles provided = slate_teal only (required 3); viewpoints not identified in the frame metadata (required 2); hero_static has 30 frames (< 60 required; `--fixture` would allow 60-frame sequences but does not lift the M-004 coverage gaps). The tool also has no per-sequence mode; `--help` offers only the profile/viewpoint narrowing options, which cannot supply the missing T/Y sequences. Captures were not renamed or edited.

## Sub-measure table (all modes)

Every gating sub-measure is NOT_RUN in every mode, so no value or threshold verdict exists. The report records the threshold status but not the threshold values (they are populated only when a sub-measure runs).

| Sub-measure | Kind | Status (direction / hybrid / screen) | Value | Assumed threshold | Pass/miss |
|---|---|---|---|---|---|
| M-004.S0 (static) | gating | NOT_RUN / NOT_RUN / NOT_RUN | - | derived | not evaluated |
| M-004.C_px.S0 | gating | NOT_RUN x3 | - | derived | not evaluated |
| M-004.F.S0 | gating | NOT_RUN x3 | - | derived | not evaluated |
| M-004.S_px.Y (orbit) | gating | NOT_RUN x3 | - | assumed_pending_calibration | not evaluated (miss not shown: no value) |
| M-004.C_px.Y (orbit) | gating | NOT_RUN x3 | - | assumed_pending_calibration | not evaluated |
| M-004.F.Y (orbit) | gating | NOT_RUN x3 | - | assumed_pending_calibration | not evaluated |
| M-004.S_px.T12 (push-in) | gating | NOT_RUN x3 | - | assumed_pending_calibration | not evaluated |
| M-004.C_px.T12 (push-in) | gating | NOT_RUN x3 | - | assumed_pending_calibration | not evaluated |
| M-004.F.T12 (push-in) | gating | NOT_RUN x3 | - | assumed_pending_calibration | not evaluated |
| M-004.S_px.T3 (push-in) | gating | NOT_RUN x3 | - | assumed_pending_calibration | not evaluated |
| M-004.C_px.T3 (push-in) | gating | NOT_RUN x3 | - | assumed_pending_calibration | not evaluated |
| M-004.F.T3 (push-in) | gating | NOT_RUN x3 | - | assumed_pending_calibration | not evaluated |

## Static frame stability (hero_static, color_output PNGs, hashed with hashlib.sha256)

| Mode | Frames | Distinct SHA-256 | Bit-identical | Detail |
|---|---|---|---|---|
| direction | 30 | 1 | yes | all frames share one hash |
| hybrid | 30 | 1 | yes | all frames share one hash |
| screen | 30 | 2 | NO | frame 0 differs from frames 1-29; 638,012 px changed, max per-channel difference 56; frames 1-29 identical to each other |

Requirement C14 says "the static frame is exactly stable": this holds for direction and hybrid and fails for screen.

## Commands

```
/opt/miniconda3/envs/kaiju/bin/python Tools/Metrics/metrics.py m004 --help
/opt/miniconda3/envs/kaiju/bin/python Tools/Metrics/metrics.py m004 evidence/T-006/captures_local/motion_direction --out evidence/T-006/m004/direction --label motion_direction --no-images --quiet   # exit 3
/opt/miniconda3/envs/kaiju/bin/python Tools/Metrics/metrics.py m004 evidence/T-006/captures_local/motion_hybrid    --out evidence/T-006/m004/hybrid    --label motion_hybrid    --no-images --quiet   # exit 3
/opt/miniconda3/envs/kaiju/bin/python Tools/Metrics/metrics.py m004 evidence/T-006/captures_local/motion_screen    --out evidence/T-006/m004/screen    --label motion_screen    --no-images --quiet   # exit 3
/opt/miniconda3/envs/kaiju/bin/python <scratchpad>/m004_hash_gif.py evidence/T-006/captures_local evidence/T-006/m004   # SHA-256 of static color_output PNGs, GIF previews
```

Reports: `evidence/T-006/m004/{direction,hybrid,screen}/report.json.gz` (gzipped as a precaution; raw reports were 347-352 KB, under the 1 MB limit) and `summary.txt`. Hash data: `evidence/T-006/m004/static_hash_report.json`.

## Previews

`evidence/T-006/m004/previews/<mode>_<sequence>.gif`: every 2nd color_output frame, PIL, 67 ms/frame, 128-colour quantised, no dithering, scaled to 960x540 (exact 2x box reduction, an integer factor, done to keep the set under 15 MB; total 8.5 MB). The integer-scale upscale was not used because the source is 1920x1080.

## Not done

- The orbit and push-in values, the S_px/C_px/F sub-measures, and any pass/miss verdict need the S0/T1/T2/T3/Y1/Y2 sequences for 3 profiles x 2 viewpoints. These are not in the captures provided. Re-run `m004` once those captures exist.
- The sequence-to-tool mapping (static→S0, push-in→T-type, orbit→Y-type) is a label only; the tool did not accept it.

## Per-sequence values (library functions, not the full M-004 gate)

Values come from `kaiju_metrics.style.m004.sequence_metrics` (called per sequence; no re-implemented maths) against the registry M-004@v1.1 thresholds (`max`, status assumed_pending_calibration for T/Y; derived for S0). hero_pushin is scored as a translation sequence (group T12), hero_orbit as a yaw sequence (group Y), hero_static as S0. A miss is shown as MISS.

Fixed inputs, not in the sidecar: the sidecar speed is 0 for scripted sequences, so the speed is the library's own `measured_speed` (600 m reference depth): hero_pushin 0.1386 px/frame (nearest nominal T1 0.1, off by 39 %), hero_orbit 0.6050 px/frame (nearest nominal Y1 0.25, off by 142 %; Y2 is 1.0). Both are outside the library's +-10 % class tolerance, so the class is the nearest nominal; the threshold group is T12 and Y for either neighbour class. Lag k = ceil(1/speed) = 8 (push-in) and 2 (orbit). The viewpoint is the sidecar's `hero`; the profile is slate_teal. Static frames: 30 (below the 60 the full gate wants).

| Mode | Sequence | Sub-measure | Value | Threshold (max) | Result |
|---|---|---|---|---|---|
| direction | hero_static | M-004.S0 | 0.000000 | 0.0 | pass |
| direction | hero_static | M-004.C_px.S0 | 0.000000 | 0.0 | pass |
| direction | hero_static | M-004.F.S0 | 0.000000 | 0.0 | pass |
| direction | hero_pushin | M-004.S_px.T12 | 0.087411 | 0.12 | pass |
| direction | hero_pushin | M-004.C_px.T12 | 0.163797 | 0.15 | MISS |
| direction | hero_pushin | M-004.F.T12 | 0.004167 | 0.005 | pass |
| direction | hero_orbit | M-004.S_px.Y | 0.072918 | 0.01 | MISS |
| direction | hero_orbit | M-004.C_px.Y | 0.138841 | 0.01 | MISS |
| direction | hero_orbit | M-004.F.Y | 0.013117 | 0.001 | MISS |
| hybrid | hero_static | M-004.S0 | 0.000000 | 0.0 | pass |
| hybrid | hero_static | M-004.C_px.S0 | 0.000000 | 0.0 | pass |
| hybrid | hero_static | M-004.F.S0 | 0.000000 | 0.0 | pass |
| hybrid | hero_pushin | M-004.S_px.T12 | 0.074438 | 0.12 | pass |
| hybrid | hero_pushin | M-004.C_px.T12 | 0.108531 | 0.15 | pass |
| hybrid | hero_pushin | M-004.F.T12 | 0.007955 | 0.005 | MISS |
| hybrid | hero_orbit | M-004.S_px.Y | 0.050215 | 0.01 | MISS |
| hybrid | hero_orbit | M-004.C_px.Y | 0.085130 | 0.01 | MISS |
| hybrid | hero_orbit | M-004.F.Y | 0.015603 | 0.001 | MISS |
| screen | hero_static | M-004.S0 | 0.010607 | 0.0 | MISS |
| screen | hero_static | M-004.C_px.S0 | 0.007170 | 0.0 | MISS |
| screen | hero_static | M-004.F.S0 | 0.000000 | 0.0 | pass |
| screen | hero_pushin | M-004.S_px.T12 | 0.087465 | 0.12 | pass |
| screen | hero_pushin | M-004.C_px.T12 | 0.164209 | 0.15 | MISS |
| screen | hero_pushin | M-004.F.T12 | 0.004252 | 0.005 | pass |
| screen | hero_orbit | M-004.S_px.Y | 0.180084 | 0.01 | MISS |
| screen | hero_orbit | M-004.C_px.Y | 0.509838 | 0.01 | MISS |
| screen | hero_orbit | M-004.F.Y | 0.067907 | 0.001 | MISS |

Result: the static frame is exactly stable (S0, C_px.S0, F.S0 all 0) in direction and hybrid, and not in screen (S0 0.010607, C_px.S0 0.007170: MISS). Push-in: direction misses C_px.T12 only, hybrid misses F.T12 only, screen misses C_px.T12 only. Orbit misses all three Y sub-measures in every mode.

Command: `/opt/miniconda3/envs/kaiju/bin/python evidence/T-006/tools/m004_per_sequence.py` (run from the repo root; writes `evidence/T-006/m004/per_sequence.json`). Tool commit: 17ce5cf (kaiju_metrics 1.0.0, registry M-004@v1.1 sha256:5f39e5531f9e).
