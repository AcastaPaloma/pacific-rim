#!/usr/bin/env python
"""T-005 r5 6a: QP step durations (touchdown to touchdown of the QP, same driver), max late, mean CoM z and speed over the QP stance, per run. Usage: st96a.py log..."""
import json,sys,statistics as st
for f in sys.argv[1:]:
    prev=None; tlast=None; dts=[]; late=0; zs=[]; vs=[]; rows=set(); tfall=None; n=0
    for l in open(f):
        if not l.startswith('{"rec":"step"'): continue
        r=json.loads(l); q=r.get('qp')
        if not q or not q.get('k') or len(q['k'])<30: continue
        k=q['k']; td=k[20]; n+=1; rows.add(q.get('row',0)); late=max(late,k[9]); zs.append(r['com_p'][1]); vs.append(r['com_v'][0]**2+r['com_v'][2]**2)
        if prev is not None and td>prev and tlast is not None: dts.append(r['t']-tlast)
        if prev is None or td!=prev: tlast=r['t']
        prev=td
    print(f.split('@')[1][:12],'QP ticks',n,'td intervals',len(dts),'median step %.1f s'%(st.median(dts) if dts else 0),'max late %.1f'%late,'mean z %.2f'%st.mean(zs),'mean v %.2f'%(st.mean(vs)**.5 if vs else 0),'rows',sorted(rows))
