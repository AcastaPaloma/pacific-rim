Note: hero_1080p/slate_teal re-captured after warm-up fix 6deae3d; hero rows, line 1 verdicts and hero_1080p report refreshed (other captures unchanged).

C4 PASS (hero 1080p slate_teal c1 span 20.3 >= 15, same as 1440p; mech L* vs buildings/sky not measurable, c2 N/A for a single still) | C5 PASS (d-iv 100% at s=4 and s=5; DIG on sky/lit surface passes; dither_off DIG 0.016) | C6 FAIL (A2, levels, families pass; no accent present so d-iii N/A; unquantised control rejected by the tool, bilinear control fails A2 out and d-iv) | C11 FAIL (c1 fails in 4 of 5 profiles at both resolutions) | C12 FAIL (waterfront c1 rho 0.80 and sky 0.53; closer/wider c1 pass; C8-C9 out of scope)

Tool: Tools/Metrics/metrics.py m003 (tool 1.0.0, registry v1.1), git commit d10ec34. Frame sets are single stills (1 frame per profile/framing/control).

## Commands
```
PY=/opt/miniconda3/envs/kaiju/bin/python
for s in hero_1080p hero_1440p alt_1080p alt_1440p; do $PY Tools/Metrics/metrics.py m003 evidence/T-006/captures_local/$s --out evidence/T-006/m003/$s --label $s --quiet; done
# controls: each control copied alone into <scratch>/controls_hero_1080p/<control> (the tool requires dir names to match capture_set_id / sequence_id)
for c in bilinear dither_off haze_off unquantised; do $PY Tools/Metrics/metrics.py m003 <scratch>/k_$c/controls_hero_1080p --profiles slate_teal --out evidence/T-006/m003/ctl_$c --label ctl_$c --quiet; done
```
The tool accepts single-frame sets but flags "< 12 gameplay frames" and "no haze_cal" as missing inputs; pooled tool exit was 1 (FAIL) for each. No relaxed mode exists, so per-frame values were read from report.json (A2, DIG, d-i, d-ii, d-iv, sky are listed per frame; c1 parts list per-frame only for failing frames, the worst frame otherwise).

## Not measured / not applicable
- c2 (haze calibration): N/A, needs a haze_cal sequence; out of scope here.
- Mech mean L* vs buildings/terrain/sky and vs frame median (C4): not measured. mask_animated is all zero in the sidecar (mech not in it) and there is no class mask, so the tool cannot isolate the mech.
- d-iii accent (C6, one accent 0.1-15%): tool reports NOT_APPLICABLE, no accent in any frame; the C6 requirement of one accent is therefore unmet/unverified.
- M-003.f panel: PENDING. e.contrast advisory fails only for slate_crimson hero 1080p (9.7 < 25).
- Resolved: the earlier hero 1080p slate_teal c1 span of 12.78 came from the unsettled first capture; the re-capture gives 20.34 (pass), consistent with the controls and hero 1440p (about 20).
- The unquantised control is rejected by the tool as INVALID_INPUT (index/colour disagree, 5803 px), so it does not "fail" on a metric; bilinear fails A2 output and d-iv; dither_off fails DIG; haze_off fails DIG and c1 span.
- Advisory: waterfront sky share 0.53 (> 0.50).

## Hero, 5 profiles, 1080p (s=4)
Report: `evidence/T-006/m003/hero_1080p/` (tool verdict FAIL). Pooled-set note: m003 reports only a pooled verdict; per-frame values above come from the report parts.

| frame | A2 int/out | DIG | c1 drop (L*) / rho / span (L*) | c1 | value levels | hue fam. | accent share | grid integrity (d-iv) | sky share | C4 / C5 / C6 |
|---|---|---|---|---|---|---|---|---|---|---|
| lavender_peach | 1.000/1.000 | 0.354 | 9.87 / 0.80 / 7.60 | FAIL | 5 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.364 | F / P / P |
| slate_crimson | 1.000/1.000 | 0.260 | 8.40 / 0.80 / 3.75 | FAIL | 5 | 2 | 0 (none present, d-iii N/A) | 1.000 | 0.364 | F / P / P |
| slate_teal | 1.000/1.000 | 0.351 | 0.00 / 1.00 / 20.34 | PASS | 6 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.364 | P / P / P |
| warm_amber_haze | 1.000/1.000 | 0.289 | 7.09 / 0.80 / 14.13 | FAIL | 8 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.364 | F / P / P |
| warm_rust_sepia | 1.000/1.000 | 0.292 | 4.41 / 0.80 / 12.45 | FAIL | 7 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.364 | F / P / P |

## Hero, 5 profiles, 1440p (s=5)
Report: `evidence/T-006/m003/hero_1440p/` (tool verdict FAIL). Pooled-set note: m003 reports only a pooled verdict; per-frame values above come from the report parts.

| frame | A2 int/out | DIG | c1 drop (L*) / rho / span (L*) | c1 | value levels | hue fam. | accent share | grid integrity (d-iv) | sky share | C4 / C5 / C6 |
|---|---|---|---|---|---|---|---|---|---|---|
| lavender_peach | 1.000/1.000 | 0.359 | 10.01 / 0.80 / 7.69 | FAIL | 5 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.362 | F / P / P |
| slate_crimson | 1.000/1.000 | 0.268 | 8.33 / 0.80 / 3.89 | FAIL | 5 | 2 | 0 (none present, d-iii N/A) | 1.000 | 0.362 | F / P / P |
| slate_teal | 1.000/1.000 | 0.355 | 0.00 / 1.00 / 20.42 | PASS | 6 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.362 | P / P / P |
| warm_amber_haze | 1.000/1.000 | 0.288 | 7.16 / 0.80 / 14.19 | FAIL | 7 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.362 | F / P / P |
| warm_rust_sepia | 1.000/1.000 | 0.282 | 4.39 / 0.80 / 12.62 | FAIL | 7 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.362 | F / P / P |

## Alternative framings (slate_teal), 1080p
Report: `evidence/T-006/m003/alt_1080p/` (tool verdict FAIL). Pooled-set note: m003 reports only a pooled verdict; per-frame values above come from the report parts.

| frame | A2 int/out | DIG | c1 drop (L*) / rho / span (L*) | c1 | value levels | hue fam. | accent share | grid integrity (d-iv) | sky share | C4 / C5 / C6 |
|---|---|---|---|---|---|---|---|---|---|---|
| slate_teal_closer | 1.000/1.000 | 0.298 | - / - / - | PASS (not itemised: tool lists only the worst frame) | 6 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.384 | P / P / P |
| slate_teal_waterfront | 1.000/1.000 | 0.461 | 1.92 / 0.80 / 11.85 | FAIL | 6 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.533 | F / P / P |
| slate_teal_wider | 1.000/1.000 | 0.413 | - / - / - | PASS (not itemised: tool lists only the worst frame) | 6 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.420 | P / P / P |

## Alternative framings (slate_teal), 1440p
Report: `evidence/T-006/m003/alt_1440p/` (tool verdict FAIL). Pooled-set note: m003 reports only a pooled verdict; per-frame values above come from the report parts.

| frame | A2 int/out | DIG | c1 drop (L*) / rho / span (L*) | c1 | value levels | hue fam. | accent share | grid integrity (d-iv) | sky share | C4 / C5 / C6 |
|---|---|---|---|---|---|---|---|---|---|---|
| slate_teal_closer | 1.000/1.000 | 0.290 | - / - / - | PASS (not itemised: tool lists only the worst frame) | 6 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.382 | P / P / P |
| slate_teal_waterfront | 1.000/1.000 | 0.463 | 1.95 / 0.80 / 12.23 | FAIL | 6 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.531 | F / P / P |
| slate_teal_wider | 1.000/1.000 | 0.418 | - / - / - | PASS (not itemised: tool lists only the worst frame) | 6 | 1 | 0 (none present, d-iii N/A) | 1.000 | 0.419 | P / P / P |

## Controls (hero view, slate_teal, 1080p) - each run as its own set

| control | tool verdict | A2 int/out | DIG | c1 drop/rho/span | levels | families | d-iv blocks | what it shows |
|---|---|---|---|---|---|---|---|---|
| bilinear | FAIL | 1.000/0.864 | 0.351 | 0.00/1.00/20.34 | 6 | 1 | 0.596 | fails A2 output (0.864 < 0.95) and d-iv (0.596): detected |
| dither_off | FAIL | 1.000/1.000 | 0.016 | 0.00/1.00/20.69 | 5 | 1 | 1.000 | DIG 0.016 < 0.15: C5 off-state satisfied |
| haze_off | FAIL | 1.000/1.000 | 0.064 | 0.37/0.80/10.17 | 5 | 1 | 1.000 | DIG 0.064 and c1 span 10.2 < 15: haze matters to c1 |
| unquantised | INVALID_INPUT (exit 2) | not computed | not computed | not computed | - | - | - | tool refuses the set: palette_index and color_internal disagree (5803 px) |
