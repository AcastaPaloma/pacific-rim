# T-005 round 3 · step 8.2: yaw and lateral check of the Fr 0.10 stride

Script `Tools/GaitOpt/yaw_check.py` (runtime 1 s), numbers in `Tools/GaitOpt/out/yaw_fr010.txt`. Input: 8.1b point (step 4.55 s, 29.3 m, v 6.45 m/s).

## Method
- yaw.py's `yaw_cycle` builds its own legs and keeps `total_L` as a closure, so it cannot take an external trajectory. I imported its helpers (`lp`, `dper`) and model.py's constants, and re-applied the same formulas to the 8.1b legs: segment list, L about the CoM, arms ±20°, fixed-point trunk share, τ_w = d/dt(I_up·ψ̇), caps μ_u·Fn·r·√(1−(Ft/μ_u Fn)²). R-008 is untouched.
- Stride = step 1 (R leg = B) followed by step 2 (R leg = A). Legs at ±HW = 6.5 m, 1 Hz band-limit (9 harmonics), Mz sampled at the 80 nodes.
- Caps, three ways: (a) yaw.py's (Fn = W, Ft = 0.2 W; flat 1.65e8, edge 8.5e7 N m); (b) the JSON's stance Fn and Fx per node; (c) (b) plus lateral LIPM shear Fn·a/g. Flat = heel and toe both loaded. Utilisation is taken in single support (72 % of the stride), as in yaw.py.
- The solution has no pelvis yaw. psi 0 is the solution as is. psi 8° (yaw.py S1's pelvis twist) is added on top without re-solving.

## Results (arms ±20°; share = trunk share needed; waist = angle / torque for the ≤0.67 share)
| caps | psi | util arms only | share ≤1 | share ≤0.67 | waist at ≤0.67 |
|---|---|---|---|---|---|
| (a) yaw.py | 0 | 1.07 | 0.07 | 0.38 | 4.2°, 5.0e7 N m |
| (a) yaw.py | 8 | 1.55 | 0.36 | 0.57 | 11.1°, 7.5e7 N m |
| (b) JSON forces | 0 / 8 | 5.31 / 7.67 | 0.81 / 0.87 | 0.87 / 0.91 | 9.8° / 17.9°, 1.2e8 N m |
| (c) + lateral | 0 / 8 | 5.88 / 8.48 | 0.83 / 0.88 | 0.89 / 0.92 | 10.0° / 18.1°, 1.2e8 N m |

- Peak Mz (single support): none 1.46e8, arms 1.32e8 (psi 0); 1.82e8 / 1.38e8 (psi 8). yaw.py's Fr 0.100 S1 row: 4.10e8 / 3.44e8, util 4.05, share 0.75 / 0.83, waist 18.1°, 2.2e8 N m.
- The yaw demand is about 3x smaller than yaw.py's row (longer step time 4.55 s, stride 9.1 s, against 7.6 s there).
- Full trunk (share 1): waist 11.2° (psi 0) or 19.6° (psi 8), τ 1.3e8 N m. Waist ROM is ±30°.
- Waist force_limit: not defined in the mech JSON, model.py or yaw.py. The only limits are hip 1.2e9, knee 8e8, ankle 1e9 N m. The 1.2e8 N m waist torque is 12 % of the ankle limit (context only).
- Lateral LIPM (HW 6.5 m, zc 42.5 m, ω 0.48 1/s, SS pivot over the whole step): peak a 1.50 m/s² (0.153 g), lateral Δv 4.98 m/s peak to peak (±2.5 m/s), sway ±2.6 m. yaw.py has no lateral row; its sagittal LIPM at Fr 0.10 is a 0.48 m/s², Δv 0.12.
- Friction cone per contact point: sagittal alone 0.997 (node 10 / 36, push-off, as in 8.1b). With lateral shear 1.045 at t 4.09 s (node 36).

## Verdict: YAW-FEASIBLE
Round-2 targets are met. Share ≤0.67 needs 0.57 (yaw.py caps) to 0.92 (JSON forces, psi 8), waist 11 to 18° (inside ±30°), and util ≤0.67 is reachable.
Binding item: the free-moment cap at node 37 (t 4.2 s), where the stance foot carries only 0.22 W just before heel strike. It pushes the share to 0.81–0.92 and the waist to 18°. Second: the lateral shear takes the push-off friction cone to 1.045, a small excess of the 0.997 that 8.1b already used.

## Assumptions
Waist torque has no limit to compare against. Torso x and tilt as in yaw.py (no pitch). Lateral orbit starts at the step boundary and is not coupled to the vertical dynamics, except that shear is proportional to Fn. Double-support nodes are not checked for yaw.
