import sys,json,math
R=[r for r in (json.loads(l) for l in open(sys.argv[1])) if r.get('rec')=='step']
dtp=float(sys.argv[2]) if len(sys.argv)>2 else 0.5
qp=[r for r in R if r.get('qp')]
print('qp from',qp[0]['t'],'last',qp[-1]['t'])
nxt=0
for r in qp:
    if r['t']<nxt: continue
    nxt=r['t']+dtp
    q=r['qp']; yd=q.get('yd'); Wl=1e7*9.81*42.4
    s=f"t={r['t']:6.2f} {r['ctl']['phase'][:4]} yaw={math.degrees(r['yaw']):6.1f} row={q.get('row')} fr={q['fr']:.2f} v=({r['com_v'][0]:5.2f},{r['com_v'][2]:5.2f}) cmdM={q['lat'][2]*Wl:9.2e} tgt={q['lat'][5]*Wl:9.2e}"
    if yd: s+=f" LT={yd[0]:9.2e} dLr={yd[2]:9.2e} Lr={yd[4]:9.2e}"+(f" trim={math.degrees(yd[5]):5.1f},{math.degrees(yd[6]):5.1f}" if len(yd)>5 else "")
    print(s)
