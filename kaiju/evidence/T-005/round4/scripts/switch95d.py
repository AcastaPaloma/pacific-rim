import sys, numpy as np
sys.path.insert(0,"Tools/GaitOpt"); import planar_model as P
m=P.PlanarModel(); O="Tools/GaitOpt/out/"
rows={"0.06":"stride_fr006_lat.json","0.08":"stride_fr008_v3_a.json","0.10":"stride_fr010_v3_n40p2.json","0.07":"stride_fr007_v3_a.json","0.09":"stride_fr009_v3_a.json"}
L=42.4; g=getattr(m,"g",9.81)
def feats(ref):
    out=[]
    for k in range(ref.N):
        c=ref.com[k]; cd=ref.comd[k]; q=ref.q[k]
        coms,Jc,Jw,pts,Jp,ba,bp=m.kin(ref.q[k],ref.qd[k]); pts=np.asarray(pts).reshape(4,2); hl=pts[2]
        f=[c[0]-hl[0],c[1]-hl[1],cd[0],cd[1],q[2],*(pts[0]-hl),*(pts[1]-hl)]
        out.append(f)
    return np.array(out)
R={k:P.load_reference(O+v,m) for k,v in rows.items()}; F={k:feats(r) for k,r in R.items()}
sc=np.array([.05*L,.05*L,.05*np.sqrt(g*L),.05*np.sqrt(g*L),1,.05*L,.05*L,.05*L,.05*L])
def d(a,b): 
    x=(F[a][:,None,:]-F[b][None,:,:])/sc; D=np.sqrt((x**2).mean(2)); return D
for a,b in [("0.06","0.08"),("0.08","0.10"),("0.06","0.10"),("0.06","0.07"),("0.07","0.08"),("0.08","0.09"),("0.09","0.10")]:
    D=d(a,b); same=np.diag(D); print(a,b,"same-node d: median %.2f max %.2f; best-node-per-node: median %.2f max %.2f; same-phase nodes 5/15/25/35: %s"%(np.median(same),same.max(),np.median(D.min(1)),D.min(1).max(),np.round(same[[5,15,25,35]],2)))
print({k:(round(float(r.d),2),round(float(r.d)/ (R[k].N*R[k].h),2)) for k,r in R.items()}, g, L)
