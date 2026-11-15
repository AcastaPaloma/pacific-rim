#!/usr/bin/env python
"""T-005 r6: time series of a qp_foot=1 log: planar foot pitch / commanded pitch / contact phase / ankle applied torque ratio and the foot-quaternion pitch. Usage: look97.py log t0 t1 [stride]"""
import json, sys, math
f = sys.argv[1]; t0 = float(sys.argv[2]); t1 = float(sys.argv[3]); stride = int(sys.argv[4]) if len(sys.argv) > 4 else 6; n = 0
for l in open(f):
    if not l.startswith('{"rec":"step"'): continue
    r = json.loads(l)
    if not (t0 <= r['t'] <= t1): continue
    n += 1
    if n % stride: continue
    k = (r.get('qp') or {}).get('k') or []
    fe = r['feet']; pq = [math.degrees(2 * math.atan2(x['q'][0], x['q'][3])) for x in fe]
    if len(k) >= 52:
        print('t%6.2f sw%d v%4.2f | planar f %6.1f %6.1f cmd %6.1f %6.1f ph %d %d | tdp %5.1f | ank tau/lim %5.2f %5.2f u %.2f | quat pitch %6.1f %6.1f c%d%d fn %.2f %.2f | cop m %.1f cmd %.1f | sh %.3f cmd %.3f' % (r['t'], k[6], math.hypot(r['com_v'][0], r['com_v'][2]), k[38], k[39], k[40], k[41], k[42], k[43], k[44], k[49], k[50], k[51], pq[0], pq[1], fe[0]['contact'], fe[1]['contact'], fe[0]['fn'] / 9.81e7, fe[1]['fn'] / 9.81e7, k[45], k[46], k[47], k[48]))
    else:
        print('t%6.2f sw%d quat pitch %6.1f %6.1f c%d%d' % (r['t'], k[6] if k else -1, pq[0], pq[1], fe[0]['contact'], fe[1]['contact']))
