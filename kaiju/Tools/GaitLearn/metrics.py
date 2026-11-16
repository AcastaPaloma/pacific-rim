"""T-005 r16: per gait log, double-support (DS) metrics over 7.7 s .. end of log (fall or 63 s): mean measured forward CoM acceleration a (0.5 s box, as round14/agg.py) over DS ticks, and mean DS episode length (s).
Usage: metrics.py <gait.jsonl> -> prints 'a len n_eps'"""
import json, sys
import numpy as np
T=[];V=[];P=[]
for l in open(sys.argv[1]):
    if '"rec":"step"' not in l: continue
    d=json.loads(l); T.append(d['t']);V.append(d['com_v'][2]);P.append(d['ctl']['phase'])
T=np.array(T);V=np.array(V);P=np.array(P); n=int(round(0.5*60))
A=(np.roll(V,-n//2)-np.roll(V,n//2))/(T[1]-T[0])/n
ds=(P=='DoubleSupport'); mm=(T>7.7+0.5)&ds
a=float(np.mean(A[mm])) if mm.any() else float('nan')
eps=[];s=None
for i,t in enumerate(T):
    if t<7.7: continue
    if ds[i] and s is None: s=t
    if not ds[i] and s is not None: eps.append(T[i-1]-s+(T[1]-T[0])); s=None
print(f"{a:.3f} {np.mean(eps) if eps else float('nan'):.3f} {len(eps)}")
