import json,sys
sys.path.insert(0,'.')
from train import *
W="/private/tmp/claude-501/t005r16"; os.makedirs(W,exist_ok=True)
def v(name,d,k): 
    d=dict(d); d["qp_pd_scale"]=round(0.25+1e-4*k,4); return f"{name}s{k} "+json.dumps(d,separators=(",",":"))
Y8={**L7,**N1,"qp_om_y":0.8}
lines=[v("ctl",{**L7,"qp_r10_diag":1},k) for k in range(4)]+[v("y8off",Y8,k) for k in range(4)]+[v("y8on0",{**Y8,"qp_r16":1},k) for k in range(4)]
# a crude hand-shaped probe: dy tracks (ys - yref)
lines+=[v("probe",{**Y8,"qp_r16":1,"qp_r16_wy2":1.0},k) for k in range(4)]
res,o=rollout(lines,"sanity",W)
for k_,x in sorted(res.items()): print(k_,x)
