"""T-005 r4 9.5g: reference-to-reference hop distances of the 3D-aware (v4) rows, same 9 features / scales as 9.5c / 9.5d (switch95d.py). Usage: switch95g.py <fr008 stride file name>"""
import sys, numpy as np
sys.path.insert(0,"Tools/GaitOpt"); import planar_model as P
m=P.PlanarModel(); O="Tools/GaitOpt/out/"
rows={"0.06":"stride_fr006_lat.json","0.07":"stride_fr007_v4_b.json","0.08":sys.argv[1],"0.09":"stride_fr009_v4_b.json","0.10":"stride_fr010_v4_b.json",
      "0.07v3":"stride_fr007_v3_a.json","0.08v3":"stride_fr008_v3_a.json","0.09v3":"stride_fr009_v3_a.json","0.10v3":"stride_fr010_v3_n40p2.json"}
L=42.4; g=9.81
def feats(ref):
    out=[]
    for k in range(ref.N):
        c=ref.com[k]; cd=ref.comd[k]; q=ref.q[k]
        coms,Jc,Jw,pts,Jp,ba,bp=m.kin(ref.q[k],ref.qd[k]); pts=np.asarray(pts).reshape(4,2); hl=pts[2]
        out.append([c[0]-hl[0],c[1]-hl[1],cd[0],cd[1],q[2],*(pts[0]-hl),*(pts[1]-hl)])
    return np.array(out)
R={k:P.load_reference(O+v,m,tol_frac=0.25) if False else P.load_reference(O+v,m) for k,v in rows.items()}; F={k:feats(r) for k,r in R.items()}
sc=np.array([.05*L,.05*L,.05*np.sqrt(g*L),.05*np.sqrt(g*L),1,.05*L,.05*L,.05*L,.05*L])
def d(a,b): x=(F[a][:,None,:]-F[b][None,:,:])/sc; return np.sqrt((x**2).mean(2))
for a,b in [("0.06","0.07"),("0.07","0.08"),("0.08","0.09"),("0.09","0.10"),("0.06","0.08"),("0.08","0.10"),("0.06","0.10"),("0.06","0.07v3"),("0.07v3","0.08v3"),("0.08v3","0.09v3"),("0.09v3","0.10v3"),("0.06","0.08v3"),("0.08v3","0.10v3"),("0.06","0.10v3"),
            ("0.09","0.10v3"),("0.08","0.10v3"),("0.07","0.08v3")]:
    D=d(a,b); s=np.diag(D); print(f"{a:7s}->{b:7s} same-node median {np.median(s):.2f} max {s.max():.2f} | best-node median {np.median(D.min(1)):.2f} max {D.min(1).max():.2f}")
print("fc_resid (x m g):",{k:round(r.fc_resid,3) for k,r in R.items()}, "Ldot_resid", {k:round(r.Ldot_resid,3) for k,r in R.items()})
