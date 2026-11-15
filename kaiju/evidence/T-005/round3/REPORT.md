# T-005 round 3 report: offline stride optimisation (R-008 option c), then tracking

**Result:** the round stopped by its bound in step 8.5, and the gate is not met. The optimised strides are dynamically feasible up to Fr 0.20 under the shipped body data. The joint-space tracking controller (imp = 2) does not walk them in Unity: every walk falls within 12–28 s. The best stable unassisted walk is still the shipped legacy controller (imp = 0), M-001.1 Fr 0.0226 (`evidence/T-005/round2/final/METRICS.md`). T-005 is not merged, and `imp` stays 0 in the shipped config.

## What was done (branch `task/T-005`; per-step evidence in this folder)
| step | result | evidence |
|---|---|---|
| 8.1 | SLSQP collocation at Fr 0.10: NOT CONVERGED (residual 6.4e-3) | `step1.md` |
| 8.1b | trust-constr with scaling: **FEASIBLE**, residual 1.6e-7, τ ≤ 0.55 of limit | `step1b.md` |
| 8.2 | yaw/lateral check: YAW-FEASIBLE (waist 11–18°, 0.26 of its 5e8 limit). Lateral shear pushes push-off friction to 1.045 | `step2.md` |
| 8.3 | lateral shear added to the friction cone (tighter); **FEASIBLE at Fr 0.10 / 0.15 / 0.20**, τ ≤ 0.70, 3D friction ≤ 1.000 | `step3.md` |
| 8.4 | strides exported as data (`Config/Mech/gait_reference_opt.json`), read at imp = 2 with Froude interpolation; the imp = 1 schedule moved to config (closes DEF-T005r2-arch-2) | `step4.md`, `step4b.md` |
| 8.5 | imp = 2: PD to the optimised angles, optimised τ as feed-forward, existing DCM step correction. Three diagnose → fix → run cycles: **all walks fall** | `step5*.md`, `run1..3/` |

Binding constraints in every optimised stride:
- the 5° ROM margins (knee extension, swing ankle dorsiflexion, stance hip extension);
- push-off friction.

Torque headroom is ≥ 30 %. The body is strong enough on paper; the walk is limited by kinematics and friction.

## 8.5 cycles (EditMode 215/222 each, the 7 KnownGap reds; stand holds in every variant)
| run | change | falls (walk_fr010 / 015 / 020) |
|---|---|---|
| 1 (0934756) | imp = 2 as designed; variants ff 1 / 0.5 / 0, ankle sign flip | 17.8–28.3 s, all |
| 2 (077e9f0) | reference phase synced to touchdown; swing gain ×3; swing reach clamp off | 12.0–28.3 s, all |
| 3 (2beb69c) | low-Froude rows 0.02–0.08 (FEASIBLE) so the speed ramp has a matching reference | 15.7–22.6 s, all |

How it fails (`step5_diag.md`, `run3/diag.txt`):
- The signs are right: hip tracking correlates +0.8 to +0.95 with the reference.
- The mech reaches only about half the commanded speed (3.2–3.9 against 6.4 m/s at Fr 0.10).
- Swing-knee tracking is poor (slope 0.10–0.64).
- Yaw drifts from about 9 s and the yaw utilisation passes 1 just before each fall.

A joint-space replay of a planar stride, corrected only by foot placement, has no feedback on whole-body momentum or contact forces. Once the body lags the reference, nothing re-plans the contact forces, which is the gap R-008 §3 described.

## Rules kept
- No assist (extMax 0 N in every run).
- No change to `mech_frame_balanced.json` or `body_tuning.json`.
- No threshold or band edits.
- Not merged.

Not done:
- 8.6, the Froude continuation, because no stable imp = 2 gait exists.
- Round-3 evaluators, skipped to conserve usage. The round-2 eval-physics and eval-arch defects stay open, except arch-2, now closed.

## Decision for the founders: Q-074
Next walk approach. **Recommendation: R-008 option (b), a whole-body QP controller.** It solves the contact forces and joint torques each tick, tracking the CoM, momentum and foot tasks of these optimised strides, under the same friction-cone and torque limits. It uses `gait_reference_opt.json` as its reference, so round 3's data carries over. Alternatives:
- (a) accept the legacy Fr 0.023 walk and change the gate, which is a founder-only call;
- (c′) more tuning of imp = 2, which is not recommended: three cycles showed no trend toward stability.
