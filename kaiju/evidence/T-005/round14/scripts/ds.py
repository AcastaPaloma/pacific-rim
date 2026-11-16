"""T-005 r11: what carries the load in double support. Per DS tick (ctl phase DoubleSupport, 7.7 s .. tmax): front foot (the one ahead) normal force / W, rear foot CoP relative to the CoM (m, + = ahead), rear-foot share of the load,
and contact begin events of the front foot per DS episode (chatter). Usage: ds.py <gait.jsonl> [tmax]"""
import json,sys,statistics as st
f=sys.argv[1]; tmax=float(sys.argv[2]) if len(sys.argv)>2 else 20; W=9.81e7
eps=[]; cur=None; begins=[]
for l in open(f):
    d=json.loads(l)
    if d['rec']=='event' and d.get('type')=='contact_begin': begins.append((d['t'],d['foot']))
    if d['rec']!='step' or d['t']<7.7 or d['t']>tmax: continue
    ph=d['ctl']['phase']
    if ph!='DoubleSupport':
        cur=None; continue
    fe=d['feet']; fi=0 if fe[0]['p'][2]>fe[1]['p'][2] else 1; ri=1-fi
    if cur is None: cur={'t0':d['t'],'front':'LR'[fi],'n':0,'flo':0,'rear_share':[],'rear_cop_rel':[]}; eps.append(cur)
    cur['n']+=1; cur['t1']=d['t']; fn=fe[0]['fn']+fe[1]['fn']
    if fe[fi]['fn']<0.1*W: cur['flo']+=1
    if fn>0.2*W: cur['rear_share'].append(fe[ri]['fn']/fn)
    if fe[ri]['fn']>0.1*W: cur['rear_cop_rel'].append(fe[ri]['cop'][2]-d['com_p'][2])
print("DS episode  t0-t1  front | front fn<0.1W share | rear load share (mean) | rear CoP - CoM x (mean, m) | front contact_begin events")
for e in eps:
    nb=sum(1 for t,ft in begins if e['t0']-0.05<=t<=e['t1'] and ft==e['front'])
    print(f"{e['t0']:6.2f}-{e['t1']:6.2f} {e['front']} | {e['flo']/e['n']:5.2f} | {st.mean(e['rear_share']) if e['rear_share'] else float('nan'):5.2f} | {st.mean(e['rear_cop_rel']) if e['rear_cop_rel'] else float('nan'):6.2f} | {nb}")
