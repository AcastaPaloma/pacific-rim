import json,math,sys
R=[json.loads(l) for l in open(sys.argv[1]) if l.startswith('{"rec":"step"')]
a,b,ev=float(sys.argv[2]),float(sys.argv[3]),int(sys.argv[4])
for r in R:
    if a<=r['t']<=b and int(round(r['t']*60))%ev==0:
        q=r.get('qp') or {}
        f=r['feet']; y=r['yaw']; fw=(math.sin(y),math.cos(y))
        print(f"{r['t']:6.2f} {r['ctl']['phase'][:4]} fn={f[0]['fn']/1e8:5.2f}/{f[1]['fn']/1e8:5.2f} c={int(f[0]['contact'])}{int(f[1]['contact'])} vz={r['com_v'][1]:5.2f} vf={r['com_v'][0]*fw[0]+r['com_v'][2]*fw[1]:5.2f} h={r['com_p'][1]:5.1f} pit={math.degrees(2*math.asin(r['root_q'][0])):5.1f} yaw={math.degrees(y):5.1f} row={q.get('row',0)} frc={q.get('fr',0):.2f} tau={[round(x/1e9,2) for x in q.get('tau',[])]}")
