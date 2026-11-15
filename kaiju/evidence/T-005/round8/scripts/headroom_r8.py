#!/usr/bin/env python
"""T-005 r8 8.1: per-row table of the hard-limit ladder with swing-torque headroom. Usage (project root): headroom_r8.py <stem> ...   (stems of Tools/GaitOpt/out/r8|r7/<stem>.json)
Swing leg = leg A of the step NLP (the leg that swings in the step); headroom = 1 - peak |tau| / limit of hip_A and knee_A."""
import json, sys, pathlib
R = pathlib.Path('Tools/GaitOpt/out')
print('| row | Fr | T s | L m | v m/s | hip_A | knee_A | ankle_A | swing headroom (min hip, knee) | F_z max W | residual |')
print('|---|---|---|---|---|---|---|---|---|---|---|')
for st in sys.argv[1:]:
    f = next(p for p in (R / 'r8' / (st + '.json'), R / 'r7' / (st + '.json')) if p.exists()); s = json.load(open(f))['summary']
    tf = 0.7 if st.startswith(('u_', 't7_')) else 1.0   # rows solved with limits x 0.7: the stored ratio is against the derated limit
    pk = s['peak_torque_over_limit']; h, k, a = pk['hip_A'] * tf, pk['knee_A'] * tf, pk['ankle_A'] * tf
    res = max(s['max_dyn_residual'], s['max_eq_violation'], s['max_ineq_violation'])
    print(f"| {st} | {s['fr']:.3f} | {s['step_time_s']:.2f} | {s['step_length_m']:.1f} | {s['v']:.2f} | {h:.3f} | {k:.3f} | {a:.3f} | {1 - max(h, k):+.3f} | {s['peak_normal_force_total_BW']:.2f} | {res:.1e} |")
