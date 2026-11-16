"""T-005 r11: does the plant realise the QP's commanded CoM x-acceleration? Both smoothed with a 0.5 s box; regression a_meas = g * cdx + c and correlation, per phase, 7.7 s .. tmax.
Usage: corr.py <gait.jsonl> [tmax] [win_s]"""
import json,sys
import numpy as np
f=sys.argv[1]; tmax=float(sys.argv[2]) if len(sys.argv)>2 else 22; win=float(sys.argv[3]) if len(sys.argv)>3 else 0.5
T=[];V=[];C=[];P=[];X=[];XC=[];CX=[]
for l in open(f):
    if '"rec":"step"' not in l or '"qp"' not in l: continue
    d=json.loads(l); k=d['qp'].get('k')
    if not k or len(k)<38: continue
    T.append(d['t']);V.append(d['com_v'][2]);C.append(k[26]);P.append(d['ctl']['phase'][:3])
T=np.array(T);V=np.array(V);C=np.array(C)
n=int(round(win/(1/60.)))
A=(np.roll(V,-n//2)-np.roll(V,n//2))/(T[1]-T[0])/n
Cs=np.convolve(C,np.ones(n)/n,mode='same')
m=(T>7.7+win)&(T<tmax)
for ph in ('Sin','Dou','all'):
    mm=m&(np.array(P)==ph) if ph!='all' else m
    if mm.sum()<20: continue
    g,c=np.polyfit(Cs[mm],A[mm],1); r=np.corrcoef(Cs[mm],A[mm])[0,1]
    print(f"{ph} n={mm.sum()} gain={g:5.2f} offset={c:5.2f} r={r:5.2f}  mean cdx={Cs[mm].mean():5.2f} mean a={A[mm].mean():5.2f} rms(a-cdx)={np.sqrt(((A[mm]-Cs[mm])**2).mean()):4.2f}")
