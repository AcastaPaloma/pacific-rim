import sys; sys.path.insert(0,"Tools/GaitOpt/qp_proto"); sys.path.insert(0,"Tools/GaitOpt")
import numpy as np, planar_model as P
from controller import QPWalker
np.set_printoptions(linewidth=220,suppress=True,precision=2)
m=P.PlanarModel(); ref=P.load_reference(None,m)
for k in list(range(0,40,2))+[39]:
    c=QPWalker(ref,m); c.started=True; c.armed=False
    t=k*ref.h+1e-6; q=ref.qd[k]*0+ref.q[k]; qd=ref.qd[k]
    u=c(t,q,qd,0)
    L=c.log
    print(f"k={k:2d} ld={L['loaded'][0]:04b} u/LIM err={(u-ref.u_json[k])/m.LIM} |qdd-ref|max={np.abs(L['qdd'][0]-ref.qdd[k]).max():.2f} (ref max {np.abs(ref.qdd[k]).max():.1f}) F err/W={np.abs(L['F'][0]-ref.F[k]).max()/m.W:.2f} st={L['status1'][0]},{L['status2'][0]},{L['tdrop'][0]} resid={L['resid'][0]:.1e}")
