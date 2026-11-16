"""T-005 r11: realisation of the QP command per phase (from t = 7.7 s to tmax): |measured CoP - QP-force CoP| (qp.k[45] vs [46]), |measured CoM accel (0.2 s central difference) - commanded cdx (qp.k[26])|,
mean |PD torque added by the drives| (qp.k[0..5]) and mean |QP torque|, per joint. Usage: real.py <gait.jsonl> [tmax]"""
import json,sys,statistics as st
f=sys.argv[1]; tmax=float(sys.argv[2]) if len(sys.argv)>2 else 22
R=[]
for l in open(f):
    if '"rec":"step"' not in l or '"qp"' not in l: continue
    d=json.loads(l); k=d['qp'].get('k')
    if not k or len(k)<53: continue
    R.append((d['t'],d['ctl']['phase'][:3],k,d['com_v'][2],d['qp'].get('tau') or [0]*6))
out={}
for i in range(6,len(R)-6):
    t,ph,k,v,tau=R[i]
    if t>tmax or t<7.7: continue
    a=(R[i+6][3]-R[i-6][3])/(R[i+6][0]-R[i-6][0])
    o=out.setdefault(ph,{'dcop':[],'dacc':[],'pd':[],'qp':[],'n':0})
    if k[45]!=0 and k[46]!=0: o['dcop'].append(abs(k[45]-k[46]))
    o['dacc'].append(abs(a-k[26])); o['pd'].append(sum(abs(x) for x in k[:6])/6/1e6); o['qp'].append(sum(abs(x) for x in tau)/6/1e6); o['n']+=1
for ph,o in out.items():
    print(ph,o['n'],"median|cop_meas-cop_cmd| m",round(st.median(o['dcop']),2),"p90",round(sorted(o['dcop'])[int(.9*len(o['dcop']))],2),"| median|a-cdx|",round(st.median(o['dacc']),2),"| mean|PD| MNm",round(st.mean(o['pd'])),"mean|QP| MNm",round(st.mean(o['qp'])))
