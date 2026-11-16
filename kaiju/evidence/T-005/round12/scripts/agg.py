"""T-005 r12: per config and seed, tick-weighted over all double-support ticks 7.7 .. tmax s: rear load share, rear CoP - CoM x (m), front-foot unloaded (<0.1 W) share, DS mean measured a and mean commanded cdx (corr.py's 0.5 s box), and the fall time from summary.txt.
Usage: agg.py <gait_dir> <summary.txt> <tmax> <config prefix> ..."""
import json,sys,re,statistics as st
import numpy as np
gd,summ,tmax=sys.argv[1],sys.argv[2],float(sys.argv[3]); W=9.81e7
falls={}
for l in open(summ):
    m=re.match(r'walk_fr010@(\S+)\s+t=.*at=([\d.]+)',l)
    if m: falls[m.group(1)]=float(m.group(2))
for cfg in sys.argv[4:]:
    out=[]
    for k in range(4):
        f=f"{gd}/walk_fr010@{cfg}{k}.gait.jsonl"; nds=0; rs=[]; rc=[]; flo=0
        T=[];V=[];C=[];P=[]
        for l in open(f):
            if '"rec":"step"' not in l: continue
            d=json.loads(l); q=d.get('qp'); kk=q.get('k') if q else None
            if kk and len(kk)>=38: T.append(d['t']);V.append(d['com_v'][2]);C.append(kk[26]);P.append(d['ctl']['phase'][:3])
            if d['t']<7.7 or d['t']>tmax or d['ctl']['phase']!='DoubleSupport': continue
            fe=d['feet']; fi=0 if fe[0]['p'][2]>fe[1]['p'][2] else 1; ri=1-fi; fn=fe[0]['fn']+fe[1]['fn']; nds+=1
            if fe[fi]['fn']<0.1*W: flo+=1
            if fn>0.2*W: rs.append(fe[ri]['fn']/fn)
            if fe[ri]['fn']>0.1*W: rc.append(fe[ri]['cop'][2]-d['com_p'][2])
        T=np.array(T);V=np.array(V);C=np.array(C);n=int(round(0.5*60))
        A=(np.roll(V,-n//2)-np.roll(V,n//2))/(T[1]-T[0])/n; Cs=np.convolve(C,np.ones(n)/n,mode='same')
        mm=(T>7.7+0.5)&(T<tmax)&(np.array(P)=='Dou')
        out.append((np.mean(A[mm]),np.mean(Cs[mm]),st.mean(rs),st.mean(rc),flo/nds,falls[f"{cfg}{k}"]))
    a=np.array(out)
    print(f"{cfg:5s} DS a [{' '.join('%.2f'%x for x in a[:,0])}] mean {a[:,0].mean():.2f} | cdx mean {a[:,1].mean():.2f} | rear share {a[:,2].mean():.2f} | rear CoP-CoM {a[:,3].mean():.2f} m | front unloaded {a[:,4].mean():.2f} | falls {' '.join('%.1f'%x for x in a[:,5])} median {np.median(a[:,5]):.1f}")
