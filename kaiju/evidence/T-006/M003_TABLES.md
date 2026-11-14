Note: hero_1080p/slate_teal re-captured after warm-up fix 6deae3d; hero rows, line 1 verdicts and hero_1080p report refreshed (other captures unchanged); fix cycle 2 recapture a867e67 (all stills re-captured, every table and report below re-run).

C4 PASS (hero 1080p slate_teal c1 span 20.2 >= 15, 20.3 at 1440p; mech L* vs buildings/sky not measurable, c2 N/A for a single still) | C5 PASS (d-iv 100% at s=4 and s=5; DIG 0.349 on sky/lit surface; dither_off DIG 0.015) | C6 PARTIAL (A2, levels, families pass; slate_teal accentIndex -1 so the tool scores no accent and d-iii is N/A; the buoy is tinted to the most saturated palette entry but is not scored; unquantised control rejected by the tool, bilinear control fails A2 out and d-iv) | C11 FAIL (c1 fails in 4 of 5 profiles at both resolutions) | C12 PARTIAL (waterfront c1 now passes: rho 1.00, span 17.4 L*, sky 0.46 <= 0.50; closer/wider c1 pass; horizon advisory 0.51 > 0.45 for waterfront; alt tool verdict INCOMPLETE, no gating fail; C8-C9 out of scope)

Tool: Tools/Metrics/metrics.py m003 (tool 1.0.0, registry v1.1), git commit d10ec34. Frame sets are single stills (1 frame per profile/framing/control).

## Commands
```
PY=/opt/miniconda3/envs/kaiju/bin/python
for s in hero_1080p hero_1440p alt_1080p alt_1440p; do $PY Tools/Metrics/metrics.py m003 evidence/T-006/captures_local/$s --out evidence/T-006/m003/$s --label $s --quiet; done
# controls: each control copied alone into <scratch>/controls_hero_1080p/<control> (the tool requires dir names to match capture_set_id / sequence_id)
for c in bilinear dither_off haze_off unquantised; do $PY Tools/Metrics/metrics.py m003 <scratch>/k_$c/controls_hero_1080p --profiles slate_teal --out evidence/T-006/m003/ctl_$c --label ctl_$c --quiet; done
```
The tool accepts single-frame sets but flags "< 12 gameplay frames" and "no haze_cal" as missing inputs; pooled tool exit was 1 (FAIL) for hero sets and controls and 3 (INCOMPLETE, no gating failure) for the alt sets. No relaxed mode exists, so per-frame values were read from report.json (A2, DIG, d-i, d-ii, d-iv, sky are listed per frame; c1 parts list per-frame only for failing frames, the worst frame otherwise).

## Not measured / not applicable
- c2 (haze calibration): N/A, needs a haze_cal sequence; out of scope here.
- Mech mean L* vs buildings/terrain/sky and vs frame median (C4): not measured. mask_animated is all zero in the sidecar (mech not in it) and there is no class mask, so the tool cannot isolate the mech.
- d-iii accent (C6, one accent 0.1-15%): tool reports NOT_APPLICABLE, no accent in any frame. slate_teal has accentIndex -1 (accent_share null); the other four profiles report accent share 0 (accent not visible). The buoy is tinted to the most saturated palette entry but the tool does not score it; the C6 one-accent requirement is unverified.
- M-003.f panel: PENDING. e.contrast advisory fails only for slate_crimson hero (9.7 < 25).
- The unquantised control is rejected by the tool as INVALID_INPUT (index/colour disagree, 5814 px), so it does not "fail" on a metric; bilinear fails A2 output and d-iv; dither_off fails DIG; haze_off fails DIG and c1 span.
- Alt framings: tool verdict INCOMPLETE (exit 3), 0 failing gating; the only failing item is advisory e.horizon 0.511 (waterfront, > 0.45). Waterfront sky share is now 0.46 (was 0.53). Per-frame c1 is read from extra.frames (the tool's pooled c1 value is the worst frame, rho 0.90 at closer).

## Hero, 5 profiles, 1080p (s=4)
Report: `evidence/T-006/m003/hero_1080p/` (tool verdict FAIL). Pooled-set note: m003 reports only a pooled verdict; per-frame values come from report.json extra.frames.

| frame | A2 int/out | DIG | c1 drop (L*) / rho / span (L*) | c1 | value levels | hue fam. | accent share | grid integrity (d-iv) | sky share | C4 / C5 / C6 |
|---|---|---|---|---|---|---|---|---|---|---|
| lavender_peach | 1.000/1.000 | 0.353 | 9.85 / 0.80 / 7.63 | FAIL | 5 | 1 | 0 (accent not visible; tool N/A) | 1.000 | 0.364 | F / P / P |
| slate_crimson | 1.000/1.000 | 0.260 | 8.40 / 0.80 / 3.75 | FAIL | 5 | 2 | 0 (accent not visible; tool N/A) | 1.000 | 0.364 | F / P / P |
| slate_teal | 1.000/1.000 | 0.349 | 0.00 / 1.00 / 20.18 | PASS | 6 | 1 | none (accentIndex -1, d-iii N/A) | 1.000 | 0.364 | P / P / P |
| warm_amber_haze | 1.000/1.000 | 0.286 | 7.21 / 0.80 / 14.01 | FAIL | 8 | 1 | 0 (accent not visible; tool N/A) | 1.000 | 0.364 | F / P / P |
| warm_rust_sepia | 1.000/1.000 | 0.290 | 4.48 / 0.80 / 12.38 | FAIL | 7 | 1 | 0 (accent not visible; tool N/A) | 1.000 | 0.364 | F / P / P |

## Hero, 5 profiles, 1440p (s=5)
Report: `evidence/T-006/m003/hero_1440p/` (tool verdict FAIL). Pooled-set note: m003 reports only a pooled verdict; per-frame values come from report.json extra.frames.

| frame | A2 int/out | DIG | c1 drop (L*) / rho / span (L*) | c1 | value levels | hue fam. | accent share | grid integrity (d-iv) | sky share | C4 / C5 / C6 |
|---|---|---|---|---|---|---|---|---|---|---|
| lavender_peach | 1.000/1.000 | 0.358 | 9.98 / 0.80 / 7.71 | FAIL | 5 | 1 | 0 (accent not visible; tool N/A) | 1.000 | 0.362 | F / P / P |
| slate_crimson | 1.000/1.000 | 0.268 | 8.32 / 0.80 / 3.89 | FAIL | 5 | 2 | 0 (accent not visible; tool N/A) | 1.000 | 0.362 | F / P / P |
| slate_teal | 1.000/1.000 | 0.352 | 0.00 / 1.00 / 20.26 | PASS | 6 | 1 | none (accentIndex -1, d-iii N/A) | 1.000 | 0.362 | P / P / P |
| warm_amber_haze | 1.000/1.000 | 0.285 | 7.27 / 0.80 / 14.07 | FAIL | 7 | 1 | 0 (accent not visible; tool N/A) | 1.000 | 0.362 | F / P / P |
| warm_rust_sepia | 1.000/1.000 | 0.280 | 4.46 / 0.80 / 12.55 | FAIL | 7 | 1 | 0 (accent not visible; tool N/A) | 1.000 | 0.362 | F / P / P |

## Alternative framings (slate_teal), 1080p
Report: `evidence/T-006/m003/alt_1080p/` (tool verdict INCOMPLETE). Pooled-set note: m003 reports only a pooled verdict; per-frame values come from report.json extra.frames.

| frame | A2 int/out | DIG | c1 drop (L*) / rho / span (L*) | c1 | value levels | hue fam. | accent share | grid integrity (d-iv) | sky share | C4 / C5 / C6 |
|---|---|---|---|---|---|---|---|---|---|---|
| slate_teal_closer | 1.000/1.000 | 0.292 | 0.39 / 0.90 / 20.21 | PASS | 6 | 1 | none (accentIndex -1, d-iii N/A) | 1.000 | 0.384 | P / P / P |
| slate_teal_waterfront | 1.000/1.000 | 0.490 | 0.00 / 1.00 / 17.38 | PASS | 6 | 1 | none (accentIndex -1, d-iii N/A) | 1.000 | 0.460 | P / P / P |
| slate_teal_wider | 1.000/1.000 | 0.412 | 0.00 / 1.00 / 16.44 | PASS | 6 | 1 | none (accentIndex -1, d-iii N/A) | 1.000 | 0.420 | P / P / P |

## Alternative framings (slate_teal), 1440p
Report: `evidence/T-006/m003/alt_1440p/` (tool verdict INCOMPLETE). Pooled-set note: m003 reports only a pooled verdict; per-frame values come from report.json extra.frames.

| frame | A2 int/out | DIG | c1 drop (L*) / rho / span (L*) | c1 | value levels | hue fam. | accent share | grid integrity (d-iv) | sky share | C4 / C5 / C6 |
|---|---|---|---|---|---|---|---|---|---|---|
| slate_teal_closer | 1.000/1.000 | 0.284 | 0.39 / 0.90 / 20.06 | PASS | 6 | 1 | none (accentIndex -1, d-iii N/A) | 1.000 | 0.382 | P / P / P |
| slate_teal_waterfront | 1.000/1.000 | 0.484 | 0.00 / 1.00 / 17.53 | PASS | 6 | 1 | none (accentIndex -1, d-iii N/A) | 1.000 | 0.460 | P / P / P |
| slate_teal_wider | 1.000/1.000 | 0.417 | 0.00 / 1.00 / 16.44 | PASS | 6 | 1 | none (accentIndex -1, d-iii N/A) | 1.000 | 0.419 | P / P / P |

## Controls (hero view, slate_teal, 1080p) - each run as its own set

| control | tool verdict | A2 int/out | DIG | c1 drop/rho/span | levels | families | d-iv blocks | what it shows |
|---|---|---|---|---|---|---|---|---|
| bilinear | FAIL | 1.000/0.864 | 0.349 | 0.00/1.00/20.18 | 6 | 1 | 0.595 | fails A2 output (0.864 < 0.95) and d-iv (0.595): detected |
| dither_off | FAIL | 1.000/1.000 | 0.015 | 0.00/1.00/20.52 | 5 | 1 | 1.000 | DIG 0.015 < 0.15: C5 off-state satisfied |
| haze_off | FAIL | 1.000/1.000 | 0.063 | 0.54/0.80/10.00 | 5 | 1 | 1.000 | DIG 0.063 and c1 rho 0.80, span 10.0 < 15: haze matters to c1 |
| unquantised | INVALID_INPUT (exit 2) | not computed | not computed | not computed | - | - | - | tool refuses the set: palette_index and color_internal disagree (5814 px) |
