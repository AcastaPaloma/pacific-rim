# T-005 r3 8.5 fix cycle 2 (code only, not run in Unity)
- `swing_clamped` (LocomotionController.cs, SingleSupport capture-point foot placement) is a reach/target clamp, not a torque at force_limit: the DCM-derived landing spot relative to the stance foot is clamped to along in [-reach_back_frac, reach_forward_frac]*l and side in [foot gap, reach_side_frac]*l; the flag is true when either clamp changed the value. It sets `_swingTarget`, so at imp 2 (where the foot path is driven by it) a clamped target shortens the step.
- New key `imp2_swing_noclamp` (default 0): at imp 2 only, uses the unclamped along/side (the flag then logs false).
- New key `imp2_phase_sync` (default 0): at imp 2 the reference stance phase = (0 or 0.5 by measured stance foot) + 0.5*min(1, t_since_touchdown / data step time). `_syncT` resets at each measured touchdown (and at walk start / capture step) and runs at 1/`_ref.StepTime` (4.55 s) instead of the limiter step time; time-driven `_time` and DS/SS logic are unchanged. The two other direct phase formulas now call StancePhase() (identical when sync is 0).
- New key `imp2_swing_gain_scale` (default 1): multiplies the swing PD stiffness (damping by sqrt) at imp 2, on top of the touchdown softening `soft` (so default = run-1 behaviour; it does not remove `soft`).
- gait_log optional fields (null at imp 0/1): `tau_ff` {st, sw}: [hip, knee, ankle] N m after scale, sign, no limit clamp; `ref_phase` (stance phase, 0..1).
- imp 0/1 paths: unchanged in behaviour. Variants: scripts/variants85c2.txt (imp2, sync, sync_sw3, sw3, sync_sw3_nc).

## Run 2 (orchestrator, 077e9f0; `round3/run2/summary.txt`)
- EditMode 215/222: the 7 KnownGap reds. All walks fall:
  - imp2 28.3 s;
  - sync 12.0 s;
  - sync_sw3 12.6 s, with yawErrMax only 8.2°;
  - sw3 21.3 s;
  - sync_sw3_nc 18.3 s.
- The Froude targets give identical numbers in the sync variants, because the falls happen during the speed ramp.
- Log check: the falls start while the commanded speed ramps from 0, with fr_ref 0 to 0.06. At those speeds the reference is clamped to the Fr 0.10 row, a 29 m / 4.55 s stride. The Fr 0.20 run reaches fr_ref 0.157 at 14 s and diverges by 18 s.
- Cause taken for cycle 3: the reference has no rows below Fr 0.10. Fix: solve low-Froude rows 0.08–0.02 with the same optimiser and limits (`Tools/GaitOpt/run_8_5c3.sh`), then re-export.
