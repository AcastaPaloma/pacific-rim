# T-005 r4 step 9.3b-7: plant tangential-anchor / realised-shear observer in the QP (preset b7)
**Result: NO-GO, and the observer made it worse.** b5 stays the config of record. All new options default off (b5 re-run unchanged: 3.0676 % ticks, speed 1.0188, pytest 4/4). Two full A/B/C/D sets run: b7 (observer) and b7b (b6c + lower contact thresholds, observer off).
## 1. What the b5 bad ticks are (b5_A, steps >= 2, per foot; 137 bad foot-ticks)
- 76 (55 %): the foot has NO load in the QP contact set at the previous call (command Fn 0): first strike (ratio 1.8-2.0, heel arrives at 11-13 m/s tangential, so the plant damper slides at the cap 0.8 Fn), heel rebound / hover / re-landing at Fn 0.02-0.1 W (sliding at the cap or a light anchored spring). No QP row on commands can act on a point that is not in the set.
- 61: foot in the set; there the realised shear tracks the command (RMS error 0.05 W) but the realised load does not (RMS 0.17 W; unloading toe: cmd Fs 0.34, real 0.13 W). Realised shear in the plant is the per-foot patch sum (shared by Fn, so the ratio is a per-FOOT quantity), and the stiff anchor spring (kt 2e9) makes it the dynamic constraint force, not a free state that carries over.
## 2. What was implemented (`controller_fid.py`, `anc*`, `ramp_*`, all default 0)
- `anc 1`: per-foot offset observer c = filtered (measured tick-mean (T, Fs) - the command applied in that tick); predicted realised shear T_cmd + cT, load Fs_cmd + cn (cn only if `anc_pos`); hard linear row |T_cmd + cT| <= 0.9 mu (Fs_cmd + cn), relaxed to stay feasible at beta = 0. `anc_kt`: the spring increment -kt dx/2 from the foot-point motion (sim-only: needs ~1e-9 m resolution, not measurable in Unity float32; PhysX anchors are internal). The measured shear/load per foot (contact-impulse read-back) IS available in Unity; the anchor displacement is NOT, hence the observer works on the measured force, not on plant internals.
- Offline check on b5_A: the offset predictor cuts the load error 0.17 -> 0.13 W, not the shear error (0.052 -> 0.054); detects 31 % of the bad ticks (cmd alone 12 %), false alarms 0.3 %.
- `anc_vt` (hover-heel tangential-velocity task): no effect (identical 3.0676 % at weights 1e2-1e4 and heights 0.04-0.2 m; 3.8 % at 1e5). `ramp_t/ramp_bw` (first-step rate limit on Fn): the first step falls at 1-1.5 steps (the rate limit starts from measured Fn 0).
## 3. A 20-step screening (b5 + options, fr>1 % ticks; b5 = 3.07)
Observer variants: 4.9 (lag 1, g 0.5, pos) / 5.8 (+ start at step 2) / 8.6-12 (shear-only, clipped, margin 1.0, g 0.3) ; 12 variants fall at 1-20 steps (hard row conflicts with the torque rows: solver 98-99.7 %). On b6c: 9.3 (no fall) / fall at 9 steps. Lower contact thresholds on b6c (on 0.01, off 0.003 W): 1.99 (b6c 2.05).
## 4. Sets (`run_9_3b7.sh PREFIX --preset b7|b7b`; logs out/proto/b7.log, b7b.log; T = fell, steps; fr = % ticks > 1, max 2.00 everywhere except mu 0.6: 1.50)
| run | b5 (step3b5) | b7 observer: fell / fr % / items | b7b: fell / fr % / items |
|---|---|---|---|
| A / Amir | F / 3.07 / 7 of 8 | F / 4.94 / 7 | F / 1.99 / 7 |
| Bp / Bm | pass / pass | Bp rec 1 / **Bm falls 5.8** | Bp rec 1 / **Bm falls 6.0** |
| Cm09, Cm11 | F 2.94, 3.14 | F 5.98, 4.75 | F 2.05, 2.27 |
| Cmu06 | F 2.84 (1.50) | F 9.01 (1.50) | F 1.91 (1.50) |
| Clim08 (unaware) | F 4.07, 6/8 | F 7.12, 6/8 (sat 14 %) | F 2.44, 6/8 (sat 17 %) |
| Clim08aw | F 5.98 | **T 1.9** | **T 1.4** |
| Cdel2 | F 2.99 | **T 3.6** | **T (plant blew up at 24 s)** |
| Dp / Dm (info) | rec 1 / T 5.9 | T 5.4 / F, rec 1 | T 5.6 / T 8.4 |
Every surviving 7/8 row fails only `friction_le_1` (max 2.00 = a strike or a light-contact tick sliding at the cap 0.8 Fn).
## 5. Conclusion and next step
- The friction item is not reachable from the QP command side with b5 geometry: 55 % of the bad foot-ticks are contacts the QP does not model (strike at 11-13 m/s, heel rebound), and a strike tick needs a tangential heel speed < ~2 mm/s (damper 1e5 N s/m against 0.4 Fn), i.e. the heel x/pitch lead of b6 (4 % slower step, B and C robustness cost). Observer rows on the in-set feet add chatter and fall risk.
- Founder decision still needed: how strike / first-contact / sub-0.1 W ticks count against the 0.4 budget (they are plant-damper impacts, not control); or accept b6c/b7b (about 2 % ticks, robust except Bm / Clim08aw for b7b) with the item reworded.
**Paths:** Tools/GaitOpt/qp_proto/{controller_fid.py, run_fid.py (b7, b7b), run_9_3b7.sh}; out/proto/{b7*, b7b*}.
