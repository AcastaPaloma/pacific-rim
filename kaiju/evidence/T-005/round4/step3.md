# T-005 r4 step 9.3 verdict: **NO-GO for 9.4** (after 9.3b-7)
Go/no-go rule (R-009 §7): A, B and all five C must pass at full thresholds; D is informational. Status after 9.3b … 9.3b-6. Details: step3b6.md (b5: step3b5.md, b4: step3b4.md). The table below is preset b5 (the robust config of record); 9.3b-6 results follow it.

| criterion | result | numbers (preset b5, reference v3, stick plant μ 0.8) |
|---|---|---|
| A (Fr 0.10, 20 steps, start +2 % speed, +1°) | **FAIL, 7/8** | no fall; speed 1.019; CoM z 0.14 % l; pitch 0.24°; foot 1.40 %; sat 0 %; solver 100 %. **Plant friction ratio vs 0.4: max 2.00, > 1 on 3.07 % of ticks** |
| B (±0.15 v push at mid-stance) | **PASS** | no fall, back within 5 % after 1 step (both signs) |
| C (robustness at A) | **FAIL, 0/5 full** | no fall in any run: mass 0.9: 7/8; **mass 1.1: 7/8 (online mass estimate; was a fall)**; μ 0.6: 7/8; delay 2: 7/8; limit 80 % (controller told, lim_frac 0.784): 7/8 (unaware: 6/8, sat 16.9 %). Every row fails the friction item only |
| D (0.30 v push) | informational | +0.30 v passes, -0.30 v falls at 5.9 steps |

**What binds (friction only):** the strike tick (heel Fn 1.2 W, shear 0.9 W), the tick after touchdown (old toe unloads to 0.1 W with its tangential spring still loaded), and the late single-support heel-off ticks (stance heel 0.09 W). All are ticks of a stick-slip patch at low or impact load, counted per point at Fn > 1e-3 W with impact ticks included (orchestrator decision).

**Improved in 9.3b-5 vs b4:** Cm11 fall removed (mass estimate), Clim08aw 7/8, friction ticks 3.81 % -> 3.07 % (v3 reference gave heel touchdown vx ≈ 0), B passes both ways; the 2-3 % floor did not go away in any variant (sweeps s1-s4: 0.8-7 %, chaotic).

**9.3b-6 (step3b6.md): still NO-GO, friction only.** New default-off options (per-point shear authority sh_unl/sh_land, heel x lead + late descent) cut the friction ticks to 1.48 % (b6b, A 7/8, B pass both, C no fall except Clim08aw falls) or 0.70 % (b6: A 7/8, but B and 4 of 6 C runs fall). Steady state is one bad tick per step (the old toe at the landing tick, ratio 1.2) plus 19 start-transient ticks; both come from the plant tangential spring state, which the QP rows tested (command only) do not see. b5 stays the config of record.

**9.3b-7 (step3b7.md): still NO-GO.** A per-foot observer of the plant's realised shear/load (offset of the measured tick mean from the applied command) with a predicted-realised-shear row in the QP did not reduce friction ticks (b7: A 4.94 % vs 3.07 %) and cost robustness (Bm, Clim08aw, Cdel2 fall). 55 % of b5's bad foot-ticks are contacts not in the QP set (strike at 11-13 m/s tangential, heel rebound/hover), where the plant damper slides at the cap 0.8 Fn; no row on commands reaches them. b7b (b6c + lower contact thresholds, observer off): A 1.99 %, B(-) and Clim08aw/Cdel2 fall. b5 stays the config of record.

**Best next step (after 9.3b-7):** founders decide how strike / first-contact / sub-0.1 W ticks count against the 0.4 budget (or accept the b6c-type 2 % with a reworded item); the only controller route left is the b6 heel x/pitch lead (vertical strike) with its robustness cost.

**Best next step (after 9.3b-6, superseded):**
1. A controller that estimates the plant's tangential anchor state (from measured shear and foot motion) and constrains the realised shear, plus a start-transient fix; or
2. The founders decide how the first step and the strike/landing ticks count against the 0.4 budget.

**Earlier best next step (9.3b-5, partly done in 9.3b-6):**
1. A per-point shear cap on the foot that lands or unloads (|Ft_cmd| <= 0.4 Fn_pred, zero shear while Fn is below ~0.15 W) and a slower vertical landing.
2. If that fails, the founders decide whether low-load (Fn < 0.15 W) and strike ticks count against the 0.4 budget.

R-009's no-go clause says: stop and report to the founders before any Unity work.

**Controller config of record (for a later port, not to be started now):**
- FIDWalker DEFAULT plus `kvs 5, onset 1, land 1, land_act 1, land_k0 35, fr_meas 1, fr_margin 0.9, mass_est 0.05, sg_floor 0.3` (`run_fid.py --preset b5`).
- Reference `out/stride_fr010_v3_n40p2.json`; μ 0.40; lim_frac 0.98 (0.784 when the controller is told an 80 % limit); delay 1 tick.

Config/Mech/gait_reference_opt.json is not written: the 9.4 plan writes a new `gait_reference_qp.json` from v3 in 9.4 itself, not in 9.3.
