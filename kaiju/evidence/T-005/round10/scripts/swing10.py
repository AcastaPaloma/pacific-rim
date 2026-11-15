#!/usr/bin/env python
"""T-005 r10: per-step planner view of a qp_r10_diag gait log: planned foothold relative to the stance heel late in the stance (xfp - heel, m),
landing error vs the plan at the touchdown (m), swing apex (m), swing duration, rear foot unload; plus measured/commanded CoM speed along the stance. Usage: swing10.py <gait.jsonl> [nsteps]"""
import json, sys
f = sys.argv[1]; ns = int(sys.argv[2]) if len(sys.argv) > 2 else 6
rows = []
for l in open(f):
    if '"rec":"step"' not in l or '"qp"' not in l: continue
    d = json.loads(l); k = d['qp'].get('k')
    if k and len(k) >= 70: rows.append((d['t'], k, d['com_v'][2], d['com_p'][2]))
idx = []; last = None
for i, r in enumerate(rows):
    if r[1][55] != last: idx.append(i); last = r[1][55]
print('step t_td(abs) | planned foothold-heel at 25/50/75% of the step: | land_err apex dur reach(m) ikfail | vz at 0/25/50/75/100% of the step | com z progress')
for s in range(1, min(ns, len(idx) - 1)):
    a, b = idx[s], idx[s + 1]; n = b - a; t0 = rows[a][0]
    fp = [rows[a + int(n * q)][1][22] - rows[a + int(n * q)][1][27] for q in (0.25, 0.5, 0.75)]
    k = rows[b][1]; vz = [rows[min(a + int(n * q), b - 1)][2] for q in (0, 0.25, 0.5, 0.75, 1.0)]
    print(f"{s:3d} {t0 + 7.617 - rows[0][0] + rows[0][0] - rows[0][0]:6.2f} | " + ' '.join(f'{x:6.2f}' for x in fp) + f" | {k[31]:6.2f} {k[30]:5.2f} {k[32]:5.2f} {k[36]:6.2f} {int(k[33])} | " + ' '.join(f'{x:5.2f}' for x in vz) + f" | {rows[a][3]:.1f}->{rows[b-1][3]:.1f}")
