# T-005 r4 step 9.3 verdict: **NO-GO for 9.4**
Go/no-go rule (R-009 §7): A, B and C must all pass; D is informational. Status after 9.3b … 9.3b-4, i.e. several tuning rounds and two escalated fix cycles (§14). Details: step3b4.md.

| criterion | result | numbers (preset b4, v2 reference, stick plant μ 0.8) |
|---|---|---|
| A (Fr 0.10, 20 steps, start +2 % speed, +1°) | **FAIL, 7/8** | no fall; speed 0.999; CoM z 0.18 % l; pitch 0.32°; foot 4.56 %; sat 0 %; solver 100 %. **Plant friction ratio vs 0.4: max 2.00, > 1 on 3.81 % of ticks** |
| B (±0.15 v push at mid-stance) | **PASS** | no fall, back within 5 % after 1 step (both signs) |
| C (robustness at A) | **FAIL, 0/5 full** | mass 0.9: 7/8; **mass 1.1: falls at 1.7 steps**; μ 0.6: 7/8; delay 2: 7/8; limit 80 %: falls (controller unaware), 7/8 (controller told, lim_frac 0.784). Every 7/8 fails the friction item only |
| D (0.30 v push, Fr 0.15/0.20) | informational | +0.30 v falls at 5.5 steps, -0.30 v passes; Fr 0.15 (b3) falls at 1.8 steps; Fr 0.20 not run |

**What binds:**
- Friction at heel strike: the heel lands 0.2 s early with vx 17 m/s.
- Friction at toe-down: foot rocking on the soft contact.
- The +10 % model-mass fall: hip runs ahead, stance unloads before the swing foot lands.

**Best next step:**
1. Re-solve the reference with a smooth swing-heel descent and touchdown tangential speed ≤ 0.4 × normal, then use `land_xlead 0.2`.
2. Add online mass-scale estimation and a support guard.
3. The founders decide how impact ticks count for the friction item.

R-009's no-go clause says: stop and report to the founders before any Unity work.

**Controller config of record (for a later port, not to be started now):**
- FIDWalker DEFAULT plus `kvs 5, onset 1, land 1, land_act 1, land_k0 36, fr_meas 1, fr_margin 0.9` (`run_fid.py --preset b4`).
- Reference `out/stride_fr010_v2_n40p2.json`; μ 0.40; lim_frac 0.98; delay 1 tick.

Config/Mech/gait_reference_opt.json is not written: the 9.4 plan writes a new `gait_reference_qp.json` from v2 in 9.4 itself, not in 9.3.
