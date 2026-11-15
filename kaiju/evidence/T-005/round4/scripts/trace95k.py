import json,sys,math
f=sys.argv[1]; t0=float(sys.argv[2]); t1=float(sys.argv[3]); ev=int(sys.argv[4]); W=9.81e7
for l in open(f):
    if not l.startswith('{"rec":"step"'): continue
    r=json.loads(l)
    if not(t0<=r['t']<=t1) or r['n']%ev: continue
    q=r.get('qp') or {}; k=q.get('k'); tau=q.get('tau')
    if not k: continue
    sw=int(k[6]); a=r.get('angles') or {}
    print('t %6.2f par %d tref %4.2f tau %4.2f late %4.2f bl %.2f inc %2d | heel ex %5.1f ez %5.1f a %6.1f %6.1f land %d | tauQP %s PD %s | fnL %.2f fnR %.2f hL %.1f hR %.1f | sw_meas %s'%(r['t'],sw,k[7],k[8],k[9],k[10],k[11],k[12],k[13],k[14],k[15],k[16],[round(x/1e6) for x in tau[3*sw:3*sw+3]],[round(x/1e6) for x in k[3*sw:3*sw+3]],r['feet'][0]['fn']/W,r['feet'][1]['fn']/W,r['feet'][0]['p'][1],r['feet'][1]['p'][1],[round(x) for x in (a.get('sw_meas') or [0,0,0])]))
