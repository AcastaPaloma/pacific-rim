#!/usr/bin/env python
"""T-005 r5 6a: per QP touchdown (qp.k td count change): time, row, interval, speed, CoM z, planned foothold at the touchdown vs the new stance heel x (feet link x + -5.25 approx), row ref v"""
import json,math,sys
f=sys.argv[1]; RV={0:2.88}
prev=None; last=None; heel=-5.25
for l in open(f):
    if not l.startswith('{"rec":"step"'): continue
    r=json.loads(l); q=r.get('qp')
    if not q or not q.get('k') or len(q['k'])<30: continue
    k=q['k']; td=k[20]; yaw=r['yaw']; fw=(math.sin(yaw),math.cos(yaw)); fx=lambda p:p[0]*fw[0]+p[2]*fw[1]
    if prev is not None and td!=prev[0]:
        par=int(k[6]); st=1-par if False else None
        # new stance leg: after swap parity=par -> swing leg par (planar A=left?), stance = other; use the foot with larger fn
        fn=[ft['fn'] for ft in r['feet']]; s=0 if fn[0]>fn[1] else 1
        hx=fx(r['feet'][s]['p'])
        print('t%6.2f td#%d row %s dt %.2f v %.2f z %.2f | planned xfp(last tick) %.1f stHeel(prev) %.1f -> steplen_planned %.1f | stance foot x %.1f ankle; step len %.1f'%(r['t'],td,q.get('row'),r['t']-(last or r['t']),fx(r['com_v']),r['com_p'][1],prev[1],prev[2],prev[1]-prev[2],hx,(hx-(last_hx or hx)) if last else 0))
        last=r['t']; last_hx=hx
    prev=(td,k[22],k[27]); 
    if last is None: last=None; last_hx=None
