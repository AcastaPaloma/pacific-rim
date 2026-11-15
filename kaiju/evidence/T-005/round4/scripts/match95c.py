import json,sys,math,numpy as np
LAD='/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/.wt/T-005-94b/kaiju/Assets/Game/Config/Mech/gait_reference_qp_ladder.json'
def qmat(q):
    x,y,z,w=q
    return np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
def load_rows(): return json.load(open(LAD))['rows']
def ref_feat(row,k):
    n=row['nodes'][k]; p=n['pts']; bh=p[4]
    return np.array([n['com'][0]-bh,n['com'][1],n['comd'][0],n['comd'][1],n['q9'][2],p[0]-bh,p[1],p[2]-bh,p[3]])
L=42.4; G=9.81
# scales: tolerance of "1" = 0.05 L = 2.1 m, 0.05*sqrt(gL)=1.02 m/s, 3 deg
S=np.array([0.05*L,0.05*L,0.05*math.sqrt(G*L),0.05*math.sqrt(G*L),0.05,0.05*L,0.05*L,0.05*L,0.05*L])
def meas_feat(rec,landed):
    yaw=rec['yaw']; fw=np.array([math.sin(yaw),0,math.cos(yaw)])
    R=qmat(rec['root_q']); up=R@np.array([0,1,0]); th=math.atan2(up@fw,up[1])
    def heel_toe(f):
        Rf=qmat(f['q']); p=np.array(f['p']); h=p+Rf@np.array([0,0,-7.5]); t=p+Rf@np.array([0,0,7.5]); return h,t
    fl=rec['feet'][landed]; fo=rec['feet'][1-landed]
    bh,bt=heel_toe(fl); ah,at=heel_toe(fo)
    com=np.array(rec['com_p']); v=np.array(rec['com_v'])
    X=lambda a:a@fw
    return np.array([X(com)-X(bh),com[1],X(v),v[1],th,X(ah)-X(bh),ah[1],X(at)-X(bh),at[1]])
def best(m,rows):
    out=[]
    for ri,row in enumerate(rows):
        for k in range(len(row['nodes'])):
            r=ref_feat(row,k); d=np.sqrt(np.mean(((m-r)/S)**2)); out.append((d,ri,k))
    out.sort(); return out
if __name__=='__main__':
    fn=sys.argv[1]; rows=load_rows(); prev=[0,0]; steps=[]
    for l in open(fn):
        if l.startswith('{"rec":"step"'):
            r=json.loads(l); c=[f['contact'] for f in r['feet']]
            for i in (0,1):
                if c[i] and not prev[i] and r['n']>100: steps.append((r,i))
            prev=c
    for r,i in steps:
        vf=np.array(r['com_v'])@np.array([math.sin(r['yaw']),0,math.cos(r['yaw'])])
        if r['t']>float(sys.argv[2]): break
        m=meas_feat(r,i); b=best(m,rows)[:2]
        print('t=%5.2f foot%d vf=%5.2f'%(r['t'],i,vf),'ph',r['ctl']['phase'][:6],' meas',np.round(m,2),' best',[(round(d,2),ri,k) for d,ri,k in b])
