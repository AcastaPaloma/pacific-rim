#!/usr/bin/env python
"""T-005 r5 6a: LIPM sanity check of the qp_plan step planner + DCM CoP feedback on the Fr 0.10 row (conda env kaiju).
Start = measured hand-off state (CoM over the stance ankle, v 4.55 m/s, z 42.07). Foot polygon heel -5.25 / toe +9.75 about the ankle (ankle = heel + 5.25)."""
import json, math, numpy as np, sys
ROOT='/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/.wt/T-005-r5/kaiju/'
row=json.load(open(ROOT+'Assets/Game/Config/Mech/gait_reference_qp_v4.json'))['rows'][0]
nd=row['nodes']; N=len(nd); T=row['step_time_s']; L=row['step_length_m']; H=T/N; g=9.81
z0=float(np.mean([n['com'][1] for n in nd])); w=math.sqrt(g/z0)
hx0=nd[0]['pts'][4]                      # reference heel of the landed leg at node 0
tt=np.arange(N)*H
cx=np.array([n['com'][0]-hx0 for n in nd]); cv=np.array([n['comd'][0] for n in nd]); ca=np.array([n['comdd'][0] for n in nd])
def ref(t):                                # CoM x rel. landed heel, speed, acc at step time t
    t=min(t,T-1e-6); return np.interp(t,tt,cx),np.interp(t,tt,cv),np.interp(t,tt,ca)
b=L/(math.exp(w*T)-1)
HEEL,TOE=-5.25,9.75
def run(kxi=1.0,steps=20,dt=0.0167,x0=29.04,v0=4.55,heel0=29.1-5.25,lo=0.5,hi=1.3,planner=True):
    x,v=x0,v0; hl=heel0; out=[]
    for s in range(steps):
        t=0.0; xcop=hl+(HEEL*0)+5.25*0; xfp=None; minv=1e9;maxv=0
        while t<T:
            xr,vr,ar=ref(t); xr+=hl; xi=x+v/w; xir=xr+vr/w; xird=vr+ar/w
            xcop_des=xi-(xird-kxi*(xi-xir))/w
            xcop=min(max(xcop_des,hl),hl+(TOE-HEEL))
            a=w*w*(x-xcop); 
            trem=max(T-t,0.05)
            xip=xcop+(xi-xcop)*math.exp(w*trem)
            xfp=min(max(xip-b,hl+lo*L),hl+hi*L)
            x+=v*dt+0.5*a*dt*dt; v+=a*dt; t+=dt; minv=min(minv,v);maxv=max(maxv,v)
        out.append((s,xfp-hl,v,x-xfp,minv,maxv)); hl=xfp
    return out
if __name__=='__main__':
    print('z0 %.2f w %.4f T %.3f L %.2f vref %.2f b %.2f'%(z0,w,T,L,row['v'],b))
    for kxi in (0.0,0.5,1.0,2.0):
        o=run(kxi); print('k_xi',kxi,'last5 step len',[round(a[1],1) for a in o[-5:]],'v end-of-step',[round(a[2],2) for a in o[-5:]],'v min/max last',round(o[-1][4],2),round(o[-1][5],2))
    o=run(1.0); print('k=1 all steps len',[round(a[1],1) for a in o]); print('v at step end',[round(a[2],2) for a in o])
