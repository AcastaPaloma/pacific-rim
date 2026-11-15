import json,sys,math,gzip,os
# usage: diag95.py <gait.jsonl[.gz]> [every_s]
fn=sys.argv[1]; ev=float(sys.argv[2]) if len(sys.argv)>2 else 1.0
op=gzip.open if fn.endswith('.gz') else open
R=[];EV=[]
for l in op(fn,'rt'):
    if l.startswith('{"rec":"step"'): R.append(json.loads(l))
    elif l.startswith('{"rec":"event"'): EV.append(json.loads(l))
MU=0.4
def fr(r):
    out=[]
    for f in r['feet']:
        n=f['fn']; ft=f['ft']
        if f['contact'] and n>1: out.append(math.hypot(ft[0],ft[2])/(MU*n))  # tangential in ground plane x,z
        else: out.append(0.0)
    return out
fall=None
for r in R:
    if r['ctl']['phase'] in('Fallen','Falling'): fall=r['t'];break
mass=None
tds=[e for e in EV if e['type']=='contact_begin']
print('steps(contact_begin)=',len(tds),'fall t=',fall,'ticks',len(R))
ms=[r['qp']['ms'] for r in R if r.get('qp')]
if ms:
    ms2=sorted(ms); print('qp ms p50 %.3f p99 %.3f max %.3f; fail final %d; status!=0 ticks %d'%(ms2[len(ms2)//2],ms2[int(.99*len(ms2))],ms2[-1],R[-1]['qp']['fail'] if R[-1].get('qp') else -1,sum(1 for r in R if r.get('qp') and r['qp']['s']!=0)))
t0=R[0]['t']
nxt=0
for r in R:
    if r['t']<nxt: continue
    nxt=r['t']+ev
    q=r.get('qp'); c=r['com_p']; f=fr(r)
    s=f"t={r['t']:5.1f} ph={r['ctl']['phase'][:6]:6s} com=({c[0]:6.2f},{c[1]:5.2f},{c[2]:6.2f}) v=({r['com_v'][0]:5.2f},{r['com_v'][1]:5.2f},{r['com_v'][2]:5.2f}) yaw={math.degrees(r['yaw']):5.1f} cmd={r['intent']['cmd_speed_mps']:4.2f} fr={f[0]:4.2f}/{f[1]:4.2f} fn={r['feet'][0]['fn']:.2e}/{r['feet'][1]['fn']:.2e}"
    if q: s+=f" qp s={q['s']} it={q['it']} ms={q['ms']:.2f} fail={q['fail']} frc={q['fr']:.2f} msc={q['msc']:.2f}"
    print(s)
mx=[max(fr(r)) for r in R]
import statistics
if any(mx): 
    cont=[x for x in mx if x>0]
    print('realised friction ratio (max over feet, contact ticks): p50 %.2f p95 %.2f max %.2f; frac>1: %.3f'%(statistics.median(cont),sorted(cont)[int(.95*len(cont))],max(cont),sum(1 for x in cont if x>1)/len(cont)))
