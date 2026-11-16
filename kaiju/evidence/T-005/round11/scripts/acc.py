import json,sys
f=sys.argv[1]; t0=float(sys.argv[2]); t1=float(sys.argv[3])
R=[]
for l in open(f):
    if '"rec":"step"' not in l or '"qp"' not in l: continue
    d=json.loads(l); k=d['qp'].get('k')
    if not k: continue
    R.append((d['t'],d['com_v'][2],k[24],k[25],k[26],k[29],d['ctl']['phase'][:3],d['feet']))
W=9.81e7
print("  t   ph  | cdx_cmd  a_meas(0.2s) | xir   xcd   cx  | cop_meas(z)  com_z")
for i in range(6,len(R)-6,15):
    t=R[i][0]
    if t<t0 or t>t1: continue
    a=(R[i+6][1]-R[i-6][1])/(R[i+6][0]-R[i-6][0])
    fe=R[i][7]; fn=sum(x['fn'] for x in fe); cop=sum(x['fn']*x['cop'][2] for x in fe)/fn if fn>0 else float('nan')
    print(f"{t:5.2f} {R[i][6]} | {R[i][4]:6.2f} {a:6.2f} | {R[i][2]:6.2f} {R[i][3]:6.2f} {R[i][5]:6.2f} | {cop:6.2f}")
