import json,sys,math,gzip
fn=sys.argv[1]; op=gzip.open if fn.endswith('.gz') else open
R=[json.loads(l) for l in op(fn,'rt') if l.startswith('{"rec":"step"')]
MU=0.4; nxt=0
for r in R:
    if r['t']<nxt: continue
    nxt=r['t']+0.25
    y=r['yaw']; fw=(math.sin(y),math.cos(y)); rt=(math.cos(y),-math.sin(y))
    s=f"t={r['t']:5.2f} {r['ctl']['phase'][:4]} st={r['ctl']['stance']} c=({r['com_p'][0]:6.2f},{r['com_p'][2]:6.2f}) vlat={r['com_v'][0]*rt[0]+r['com_v'][2]*rt[1]:5.2f} vf={r['com_v'][0]*fw[0]+r['com_v'][2]*fw[1]:5.2f} yaw={math.degrees(y):6.1f} yr={r['yaw_rate']:5.2f}"
    for k,f in enumerate(r['feet']):
        n=f['fn']; t=f['ft']
        if n>1: s+=f" | f{k} n={n/1e8:4.2f} sag={(t[0]*fw[0]+t[2]*fw[1])/(MU*n):5.2f} lat={(t[0]*rt[0]+t[2]*rt[1])/(MU*n):5.2f}"
        else: s+=f" | f{k} --"
    q=r.get('qp')
    if q: s+=f" qp frc={q["fr"]:.2f} ts={q["ts"]:.2f} lat={[round(x,3) for x in q.get("lat",[])]}"
    print(s)
