# T-005 round 2, step 6.6b: load-sized stance stiffness; Froude continuation ladder (evidence)

Branch `task/T-005`, from 89d4fe9. `imp` stays 0 in walk_tuning.json; imp = 0 path untouched (StanceDof's legacy branch is not reached); no mech_frame_balanced.json / threshold / Physics.Body / assist change. Swing gains unchanged.

## Change (imp > 0 stance joints only, `StanceImpedance.LoadGains`, called in `StanceDof`)
K_j = imp_kload_j * W_leg * l (N m/rad), W_leg = filtered normal load on that foot (`leg.Load`), l = 42 m.
D_j = 2 * imp_kload_zeta * sqrt(K_j * I_eff), I_eff = M * l^2 * share, share = clamp(W_leg / (M g), 0, 1) (1 in single support, about 0.5 in double).
Final gains = Max(load gain, Hz gain) for K and D separately. Keys (walk_tuning.json, WalkTuning): imp_kload_hip 0.5, knee 1.0, ankle 0.5, imp_kload_zeta 1.0. kload_j = 0 returns the Hz gains.
Scale check: K_load/K_Hz = kload g / (l w^2) with w = 2 pi f: at 0.5 Hz this is 0.024 kload. The load-sized stiffness at the shipped kload corresponds to about 0.08 Hz x sqrt(kload), far below the 0.5-0.8 Hz gains, so Max leaves the Hz gains in force (hip/knee; only a joint with a small DofInertia could change). Shipped defaults are therefore nearly a no-op.

## Ladder (walk_fr010; walk_fr015 identical in run 8 because the step cap binds before the speed; 63 s; Fr = v10^2/(9.81 x 42))
Run 8 (shipped kload 0.5/1.0/0.5): fell at s (v10 m/s, Fr, yawErrMax deg)
| cap | fell | v10 | Fr | yawErr |
| 0.25 | no, 63 s | 2.57 | 0.0160 | 11.3 |
| 0.30 | 25.2 | 0 | - | 19.8 |
| 0.35 | 26.6 | 0 | - | 12.9 |
| 0.40 | 27.6 | 0 | - | 18.3 |
| 0.45 | 18.6 | 0 | - | 20.0 |
| 0.50 | 24.1 | 0.01 | - | 55.4 |
| 0.55 | 29.2 | 0.01 | - | 43.4 |
Run 9 (hypothesis: stiffness above the Hz gains, kload 50/100/50): fell 21.8 (cap 0.25), 39.4, 32.7, 22.6, 21.3, 21.1, 15.5. Worse: even cap 0.25 falls.
Run 10 (hypothesis: knee only, kload_knee 100, hip/ankle 0): fell 41.5 (cap 0.25), 35.2, 37.6, 26.7, 22.5, 46.2, 20.6. Worse at 0.25 too.
Best: cap 0.25, Fr 0.0160, stable 63 s (same as step 5/6a; no better rung, the legacy walk is Fr 0.022).
Raising the load stiffness (runs 9, 10) hurt; the defaults are kept (cap025 still walks), higher kload not shipped.

## First failing rung: cap 0.30 (run 8), fell 25.2 s (/private/tmp/claude-501/t005r2/dbg/ang.py)
Single support, left stance, t 24.7 to 25.2: stance KNEE goes from -9 deg (t 24.4) to +18 deg past the reference (t 24.9) while CoM height drops 40.5 -> 37.8 m, then falls (collapse, as at 0.5/0.66). Throughout t 23.8-25.2 the swing KNEE is +33 to +45 deg off its reference (more flexed) and the swing HIP lags 3-23 deg (growing 19 -> 23); stance hip within +-7 deg, ankle within 5 deg. So the stance knee gives way while the swing leg is not following the reference.

## Swing hip torque saturation
Not observable: the gait log has no per-DOF applied/drive torque or limit field (only total positive joint power p_loco_W per step). Not reported as saturated or not; adding a log field would be needed.

## Tests
`dotnet test DotnetTests/Units.Mech.Tests`: 84 total, 78 pass, 6 fail = the known KnownGap reds (new test LoadGains_... passes).
Commands: `RUN=N SKIPTEST=1 SCN=... /private/tmp/claude-501/t005r2/run65.sh` (absolute path needed), variants8/9/10.txt.
