import json,sys,math
R=[json.loads(l) for l in open(sys.argv[1]) if l.startswith('{"rec":"step"')]
Wl=1e7*9.81*42.4; nxt=0; t0=float(sys.argv[2]) if len(sys.argv)>2 else 0
for r in R:
    q=r.get('qp')
    if not q or r['t']<nxt or r['t']<t0: continue
    nxt=r['t']+0.5
    lat=q.get('lat') or [0]*6
    print(f"t={r['t']:5.2f} {r['ctl']['phase'][:4]} yaw={math.degrees(r['yaw']):6.1f} yr={r['yaw_rate']:6.3f} cmdYawM={lat[2]*Wl:9.2e} tgt={lat[5]*Wl:9.2e} realLdot={-r['yaw_moment_nm']:9.2e} budget={r['yaw_budget_nm']:8.2e} roll c/t={lat[1]:.3f}/{lat[4]:.3f} fy c/t={lat[0]:.3f}/{lat[3]:.3f}")
