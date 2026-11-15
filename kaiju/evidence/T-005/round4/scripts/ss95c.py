import json,sys,math,numpy as np
sys.path.insert(0,'.')
from match95c import *
rows=load_rows(); W=1e7*9.81
ok=[[ (sum(row['nodes'][k]['f4x2'][1:4:2])<0.05*W and sum(row['nodes'][k]['f4x2'][5:8:2])>0.8*W) for k in range(40)] for row in rows]
fn=sys.argv[1]; t1=float(sys.argv[2]); out=[]
for l in open(fn):
    if not l.startswith('{"rec":"step"'): continue
    r=json.loads(l)
    if r['t']>t1: break
    if r['t']<5.4: continue
    f=r['feet']; fns=[x['fn'] for x in f]
    for i in (0,1):
        if fns[i]>0.85*W and fns[1-i]<0.05*W:
            m=meas_feat(r,i); best=(9,0,0,None)
            for ri,row in enumerate(rows):
                for k in range(40):
                    if not ok[ri][k]: continue
                    z=(m-ref_feat(row,k))/S; d=math.sqrt(np.mean(z*z))
                    if d<best[0]: best=(d,ri,k,np.abs(z).max())
            out.append((r['t'],i,best,m))
last=-1
for t,i,b,m in out:
    if int(t*10)!=last: last=int(t*10); print('t=%.2f st=%d d=%.2f row%d k%d zmax=%.2f  vf=%.2f z=%.1f'%(t,i,b[0],b[1],b[2],b[3],m[2],m[1]))
