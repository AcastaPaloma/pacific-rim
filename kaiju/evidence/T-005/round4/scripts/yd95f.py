import json,math,sys
R=[json.loads(l) for l in open(sys.argv[1])]
R=[r for r in R if r.get('rec')=='step']
qp=[r for r in R if r.get('qp')]
nxt=0; dt=float(sys.argv[2]) if len(sys.argv)>2 else 0.5
for r in qp:
    if r['t']<nxt: continue
    nxt=r['t']+dt
    q=r['qp']; yd=q['yd']; a=r['angles']
    print(f"t={r['t']:.2f} {r['ctl']['phase'][:4]} yaw={math.degrees(r['yaw']):6.1f} row={q.get('row')} fr={q['fr']:.2f} v={r['com_v'][2]:5.2f} Llegs={yd[7]:9.2e} Larms={yd[8]:9.2e} Lrest={yd[4]:9.2e} LT={yd[0]:9.2e} tgt={yd[3]:9.2e} share={yd[9]:.2f} ff={yd[10]:.3f},{yd[11]:.3f}")
