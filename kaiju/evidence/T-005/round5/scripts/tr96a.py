#!/usr/bin/env python
"""T-005 r5 6a: per-step trace of a qp_plan run from the gait log. Usage: tr96a.py log.jsonl [t0 t1] ; prints row, speed, CoM z, planned foothold, DCM, desired CoP, a_x, touchdown events"""
import json,math,sys
f=sys.argv[1]; t0=float(sys.argv[2]) if len(sys.argv)>2 else 0; t1=float(sys.argv[3]) if len(sys.argv)>3 else 1e9; every=int(sys.argv[4]) if len(sys.argv)>4 else 12
G=9.81e7; n=0
for l in open(f):
    if not l.startswith('{"rec":"step"'): continue
    r=json.loads(l)
    if r['t']<t0 or r['t']>t1: continue
    n+=1
    q=r.get('qp')
    if not q or n%every: continue
    yaw=r['yaw']; fw=(math.sin(yaw),math.cos(yaw)); fx=lambda p:p[0]*fw[0]+p[2]*fw[1]
    k=q.get('k')
    fn=[ft['fn']/G for ft in r['feet']]
    s='t%6.2f row %s v %.2f z %.2f feet x %6.1f %6.1f fn %.2f %.2f'%(r['t'],q.get('row'),fx(r['com_v']),r['com_p'][1],fx(r['feet'][0]['p']),fx(r['feet'][1]['p']),fn[0],fn[1])
    if k and len(k)>=30:
        s+=' | par %d tref %.2f tau %.2f late %.2f | xfp %.1f xi %.1f xir %.1f xcd %.1f ax %.2f xst %.1f cx %.1f | td %d'%(k[6],k[7],k[8],k[9],k[22],k[23],k[24],k[25],k[26],k[27],k[29],k[20])
    print(s)
