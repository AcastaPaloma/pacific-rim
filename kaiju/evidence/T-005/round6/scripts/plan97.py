#!/usr/bin/env python
"""T-005 r6: planner timeline of a qp_plan log: DCM, CoP target, foothold and CoM relative to the stance heel, swing heel error / height. Usage: plan97.py log t0 t1 [stride]"""
import json, sys
f = sys.argv[1]; t0 = float(sys.argv[2]); t1 = float(sys.argv[3]); stride = int(sys.argv[4]) if len(sys.argv) > 4 else 9; n = 0
for l in open(f):
    if not l.startswith('{"rec":"step"'): continue
    r = json.loads(l); t = r['t']
    if not (t0 <= t <= t1): continue
    q = r.get('qp')
    if not q or not q.get('k') or len(q['k']) < 38: continue
    n += 1
    if n % stride: continue
    k = q['k']; h = k[27]
    print('t%6.2f sw%d tau %4.2f | xi-heel %6.1f xcd-heel %6.1f xfp-heel %6.1f cx-heel %6.1f | swhx_err %6.1f swz %4.1f ztgt %4.1f | ikfail %d td %d lastdur %.1f' % (t, k[6], k[28], k[23] - h, k[25] - h, k[22] - h, k[29] - h, k[12], k[18], k[17], k[33], k[20], k[32]))
