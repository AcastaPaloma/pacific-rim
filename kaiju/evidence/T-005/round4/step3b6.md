# T-005 r4 step 9.3b-6: realised plant friction vs the 0.4 budget (controller-side fixes, reference v3)
**Result: NO-GO.** Every new option defaults off (b5 re-run unchanged: speed 1.0188, 3.07 % ticks, pytest 4/4). The friction item still fails in A, B and every C run. Best friction numbers cost robustness: b6 (cone 0.6, x lead 0.1) A 7/8 at 0.70 % ticks, but B and 4 of 6 C runs fall; b6b keeps B and 5/6 C (no fall) at 1.0-3.9 % ticks, Clim08aw falls.
## 1. What the ticks were (b5_A, 20 steps, 5281 ticks; tick = 1/60 s, step = 255 ticks)
- Strike tick: the swing heel arrives at 11-13 m/s tangential, vz -4 m/s (the x quintic ends at ts but z touches ~4 ticks earlier); the plant damper gives Ft = 0.8 Fn (the slip cap, ratio 2.0).
- After the strike the heel REBOUNDS (+0.7 m/s, foot pitching -37 deg) and hovers up to 0.16 m for 40 ticks (stance rule is a velocity damper kvs 5 only); it re-lands at rel 40-42 at 0.04-0.1 W with the anchored spring from first contact (ratio 1.1-2.0). The old toe unloads 46-49 with its spring still loaded.
- Realised shear is the plant spring state, not the command: toe at the landing tick, cmd Ft -0.07 W (Fn 0.27) vs realised Ft -0.16 W (Fn 0.33).
## 2. Options added (`controller_fid.py`, all default 0 = b5)
- `sh_unl` (s): per-point shear authority s = clip((t_off - tref)/sh_unl, 0, 1) scales the point's friction coefficient and its stance-accel weight (unload shear before normal). `sh_land` (s): same after first contact (landing point starts shear-free).
- `st_zk/st_kv`: vertical pull-down of a hovering loaded point. Result: contact-set chatter, 6.9 % ticks: dropped. `sh_floor`: Fn_cmd per foot >= measured shear / (mu m): 3.6-8.5 % ticks: dropped.
- Existing `land_xlead` (heel x and pitch stop before z) + `late_vz 8`: strike ratio 2.0 -> 0.27-0.7 (vx 0.2-0.7 m/s at touch). `gi_qp.py`: a numerical dead end (IndexError, `fmin_td`/`rate_bw` rows) now returns "infeasible" (torque rows dropped as before).
## 3. A 20-step screening (preset b5 + options; fr>1 % ticks): b5 3.07; sh_unl 0.5 + sh_land 0.4: 2.05; + xlead 0.03 + late_vz 8: 1.48; + xlead 0.1: 1.99; + cone 0.8 / 0.7 / 0.6 / 0.5: 1.48 / 2.7 / 0.70 / fall; xlead 0.06-0.08: 4.5-7.9 (chaotic).
## 4. Sets (`run_9_3b6.sh PREFIX --preset b6|b6b|b6c`; logs out/proto/b6*.log; 4 runs in parallel)
b6 = b5 + sh_unl 0.5, sh_land 0.4, land_xlead 0.1, late_vz 8, fr_margin 0.6. b6b = b5 + sh_unl 0.5, sh_land 0.4, land_xlead 0.03, late_vz 8 (cone 0.9). b6c = b5 + sh_unl 0.5, sh_land 0.4.

| run | b6: fell / steps / speed / fr>1 % / items | b6b: fell / steps / speed / fr>1 % / items | b6c: fr>1 % / items |
|---|---|---|---|
| A / Amir | F / 20 / 0.959 / 0.70 / 7/8 | F / 20 / 1.030 / 1.48 / 7/8 | 2.05 / 7/8 |
| Bp / Bm | T 6.3 / T 7.4: both FALL | F: rec 1 step / rec 2 steps (Bm 6/8: + solver) **B PASS** | rec 1 / 1, **B PASS** |
| Cm09 / Cm11 | T 3.2 / F 20: 3/8, 7/8 (0.74) | F / F: 7/8 (1.33, 2.01) | F / F 7/8 (2.01, 2.25) |
| Cmu06 | F 20: 7/8 (0.64) | F: 7/8 (1.04) | 7/8 (2.01) |
| Clim08 (unaware) | T 2.2 | F: 6/8 (sat 17.8 %) | 6/8 (sat 17.5 %) |
| Clim08aw (lim_frac 0.784) | T 2.5 | **T 1.4** | **T 1.4** |
| Cdel2 | T 3.3 | F: 7/8 (3.88) | T 9.1 |
| Dp / Dm (informational) | T 5.8 / T 7.5 | T 5.6 / T 5.7 | T 5.6 / T 6.2 |

Every 7/8 row fails only `friction_le_1` (max ratio 2.00, 1.50 at mu 0.6). b5 for comparison (step3b5.md): A 3.07 %, B pass, all C no fall.
## 5. What is left (b6, A, steady state): ONE bad tick per step plus 19 start ticks
- Steady: the old toe at the landing tick (rel 1: Fn 0.33 W, Ft 0.16 W, ratio 1.2): the plant spring holds the pre-strike shear for a tick while the QP unloads the toe at once. 18 ticks of 5281.
- Start: 19 ticks in the first 70: the first step starts at node 0 with the heel pressing into a stiff contact (5 cm for 1 W); the two loaded points (old toe, new heel) trade the load every 3 ticks (hold chatter, no equivalent in later steps). `w_smooth` 1e-2..1e3 and `w_prior` do not remove it (or fall).
- The robustness cost comes from the x lead (step 4 % slow) and the 0.6 cone (less shear authority at low load), not from sh_unl/sh_land.
## 6. Best next step
1. The start transient and the landing-tick toe need a controller that knows the plant tangential spring (anchor state estimated from the measured shear and foot motion, then a shear row on the realised value): the QP rows tested only constrain the command.
2. Or the founders decide how the first step and the strike/landing ticks count (currently every tick counts).
**Paths:** Tools/GaitOpt/qp_proto/{controller_fid.py, gi_qp.py, run_fid.py (b6, b6b, b6c), run_9_3b6.sh}; out/proto/{b6*, b6b*, b6c*}.
