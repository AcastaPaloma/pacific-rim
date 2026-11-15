#!/usr/bin/env python
"""T-005 r7: time series of the walk (forward = world z in the log): com z (forward), speed, both feet forward position + height (ankle p), contact, normal load / W, swing leg (qp.k[6]), row (qp.row), step clock.
Usage: steps98.py log t0 t1 [stride]"""
import json, sys, math
f = sys.argv[1]; t0 = float(sys.argv[2]); t1 = float(sys.argv[3]); stride = int(sys.argv[4]) if len(sys.argv) > 4 else 10; n = 0
for l in open(f):
    if not l.startswith('{"rec":"step"'): continue
    r = json.loads(l)
    if not (t0 <= r['t'] <= t1): continue
    n += 1
    if n % stride: continue
    k = (r.get('qp') or {}).get('k') or []; fe = r['feet']; W = 9.81e7
    print('t%6.2f row %s sw%s com_f %7.2f h %5.2f v %5.2f | foot0 f %7.2f h %5.2f c%d fn %4.2f | foot1 f %7.2f h %5.2f c%d fn %4.2f | ikfail %s' % (
        r['t'], (r.get('qp') or {}).get('row'), k[6] if k else '-', r['com_p'][2], r['com_p'][1], math.hypot(r['com_v'][0], r['com_v'][2]),
        fe[0]['p'][2], fe[0]['p'][1], fe[0]['contact'], fe[0]['fn'] / W, fe[1]['p'][2], fe[1]['p'][1], fe[1]['contact'], fe[1]['fn'] / W, k[33] if len(k) > 33 else '-'))
