#!/usr/bin/env python
"""T-005 r9: start-up trace of gait logs: QP engage time and forward CoM speed (com_v z) along the run. Usage: startup_an.py <gait_dir> <name>..."""
import json, sys, os
d0 = sys.argv[1]
for n in sys.argv[2:]:
    f = os.path.join(d0, f'walk_fr010@{n}.gait.jsonl'); first = None; rows = []
    for l in open(f):
        if '"rec":"step"' not in l: continue
        d = json.loads(l)
        if 'qp' in d and first is None: first = d['t']
        rows.append(d)
    step = max(1, len(rows) // 14)
    print(n, 'qp engage t=', first, 'last t', round(rows[-1]['t'], 1))
    print('  ', [(round(r['t'], 1), round(r['com_p'][2], 1), round(r['com_v'][2], 2)) for r in rows[::step]])
