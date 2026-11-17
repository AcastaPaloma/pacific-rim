#!/usr/bin/env python3
"""T-201b: per-contact foot slip with time and command speed. Usage: slipt.py <gait.jsonl> [t0] [min_slip]"""
import json, sys, math
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from analyse2 import rot
path = sys.argv[1]; t0 = float(sys.argv[2]) if len(sys.argv) > 2 else 8.0; mn = float(sys.argv[3]) if len(sys.argv) > 3 else 0.0
recs = [json.loads(l) for l in open(path) if l.startswith('{"rec":"step"')]
out = []
for k in range(2):
    run = 0.0; prev = None; start = None; v0 = 0
    for r in recs + [None]:
        ok = False
        if r and r['feet'][k]['contact']:
            ft = r['feet'][k]; pts = []
            for dz in (-7.5, 7.5):
                o = rot(ft['q'], (0, 0, dz)); pts.append((ft['p'][1] + o[1], (ft['p'][0] + o[0], ft['p'][2] + o[2]), dz))
            low = min(pts)
            if low[0] < 0.4:
                ok = True
                if prev is not None and prev[0] == low[2]: run += math.hypot(low[1][0] - prev[1][0], low[1][1] - prev[1][1])
                prev = (low[2], low[1])
                if start is None: start = r['t']; v0 = r['intent']['cmd_speed_mps']
                te = r['t']
        if not ok:
            prev = None
            if start is not None:
                if start >= t0: out.append((start, te, k, run, v0))
                start = None; run = 0.0
out.sort()
n = len(out); print('contacts', n, 'mean slip %.3f' % (sum(o[3] for o in out) / max(1, n)))
for o in out:
    if o[3] >= mn: print('t %6.2f-%6.2f foot %d slip %.2f cmd_v_at_start %.2f' % o)
