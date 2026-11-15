#!/usr/bin/env python
"""T-005 r5 6b: per QP step (touchdown event): time, row, step duration, swing apex, landing error vs x_fp, speed, CoM z, IK failures. Usage: ls96b.py log [maxlines]"""
import json,sys,math
f=sys.argv[1]; mx=int(sys.argv[2]) if len(sys.argv)>2 else 40; prev=None; n=0
for l in open(f):
    if not l.startswith('{"rec":"step"'): continue
    r=json.loads(l); q=r.get('qp')
    if not q or not q.get('k') or len(q['k'])<34: continue
    k=q['k']; key=(k[30],k[31],k[32])
    if prev is not None and key!=prev and n<mx:
        n+=1; print('t%6.2f row %s dur %.2f apex %.1f landerr %.1f | v %.2f z %.2f ikfail %d'%(r['t'],q.get('row',0),k[32],k[30],k[31],math.hypot(r['com_v'][0],r['com_v'][2]),r['com_p'][1],k[33]))
    prev=key
