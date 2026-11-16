import sys,glob,math
sys.path.insert(0,'Tools'); import m007_bay as M
from collections import defaultdict
eid=M.eid
def diag(f):
    run=M.load_run(f); cfg=M.CFG
    by=defaultdict(list)
    for a in sorted(run.aim,key=lambda r:r["t"]): by[(a["mech"],a["target"])].append(a["t"])
    gap=2*run.period+1e-9
    decs=defaultdict(list)
    for r in run.dec: decs[eid(r["ent"])].append(r)
    mpos=defaultdict(list)
    for r in run.smp: mpos[eid(r["ent"])].append((r["t"],r["x"],r["y"]))
    at=lambda lst,t,k=None: next((x for x in reversed(lst) if (x[0] if k is None else x["t"])<=t+1e-9),None)
    out=[]
    for (mech,tgt),times in by.items():
        eps=[];cur=None
        for t in times:
            if cur and t-cur[1]<=gap: cur[1]=t
            else: cur=[t,t];eps.append(cur)
        b,m=eid(tgt),eid(mech)
        for a,z in eps:
            if z-a+run.period<3.0: continue
            drow=at(decs[b],a,"t"); 
            if not drow: continue
            mp=at(mpos[m],a)
            info=[]
            for e,L in decs.items():
                if e==b: continue
                r=at(L,a,"t")
                if r is None or r["pack"]!=drow["pack"]: continue
                d=math.hypot(r["x"]-mp[1],r["y"]-mp[2])
                # first charge on mech in window
                ch=[x for x in L if x["state"]==3 and x["prev_state"]!=3 and eid(x["target"])==m and a<=x["t"]<=a+30]
                info.append((r["role"],int(r["state"]),int(d),round(ch[0]["t"]-a,1) if ch else None))
            bd=math.hypot(drow["x"]-mp[1],drow["y"]-mp[2])
            out.append((round(a),drow["role"],int(bd),info))
    return out
for f in sorted(glob.glob(sys.argv[1]+'/*'+sys.argv[2]+'*'))[:int(sys.argv[3])]:
    print(f.split('/')[-1]);  [print('  ',o) for o in diag(f)]
