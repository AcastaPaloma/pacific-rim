import sys; sys.path.insert(0,'.')
from train import *
W="/private/tmp/claude-501/t005r16"
def v(name,d,k): 
    d=dict(d); d["qp_pd_scale"]=round(0.25+1e-4*k,4); return f"{name}s{k} "+json.dumps(d,separators=(",",":"))
Y8={**L7,**N1,"qp_om_y":0.8}
for nm,d in (("y8off",Y8),("y8on0",{**Y8,"qp_r16":1})):
    res,o=rollout([v(nm,d,k) for k in range(4)],"s2"+nm,W); print(nm,[res[f"{nm}s{k}"] for k in range(4)])
