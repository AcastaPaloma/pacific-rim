import gzip,json,sys,math
R='/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/.wt/T-005-94b/kaiju/Assets/Game/Config/Mech/'
rows=json.load(open(R+'gait_reference_qp_ladder_v4.json'))['rows']+json.load(open(R+'gait_reference_qp_v4.json'))['rows']
n0={i+1:(r['nodes'][0]['com'][0]-r['nodes'][0]['pts'][4], r['step_time_s'], r['v']) for i,r in enumerate(rows)}
# row index in log: guess 1-based ladder index ; print table
print({k:(round(v[0],1),round(v[1],2),round(v[2],2)) for k,v in n0.items()})
def rot(q,v):
    x,y,z,w=q; ux,uy,uz=x,y,z; s=w
    dot=ux*v[0]+uy*v[1]+uz*v[2]; uu=ux*ux+uy*uy+uz*uz
    cr=(uy*v[2]-uz*v[1], uz*v[0]-ux*v[2], ux*v[1]-uy*v[0])
    return [2*dot*u+(s*s-uu)*vv+2*s*c for u,vv,c in zip((ux,uy,uz),v,cr)]
f=sys.argv[1]; W=9.81e7
recs=[json.loads(l) for l in gzip.open(f,'rt')]; S=[r for r in recs if r.get('rec')=='step']
on=[None,None]; lift=[None,None]; lasttd=None
for k,r in enumerate(S):
    if r['t']<9: continue
    if r['ctl']['phase']=='Fallen': break
    for i,fo in enumerate(r['feet']):
        o=fo['fn']>0.03*W
        if on[i] is None: on[i]=o; continue
        if on[i] and not o and fo['p'][1]>0.3: lift[i]=r['t']; on[i]=False
        elif not on[i] and o:
            on[i]=True
            if lift[i] is not None and r['t']-lift[i]>0.5:
                q=r.get('qp') or {}; row=q.get('row')
                yaw=r['yaw']; # heading frame: forward = (sin yaw, cos yaw) in (x,z)
                fw=(math.sin(yaw),math.cos(yaw))
                h=rot(fo['q'],[0,0,-7.5]); heel=(fo['p'][0]+h[0],fo['p'][2]+h[2])
                c=r['com_p']; dm=(c[0]-heel[0])*fw[0]+(c[2]-heel[1])*fw[1]
                vf=r['com_v'][0]*fw[0]+r['com_v'][2]*fw[1]
                fut=[x for x in S[k:k+90]]
                v15=fut[-1]['com_v'][0]*fw[0]+fut[-1]['com_v'][2]*fw[1]
                frm=sum((x.get('qp') or {}).get('fr',0) for x in fut)/len(fut)
                ref=n0.get(row,(float('nan'),)*3)
                stepdur=r['t']-lasttd if lasttd else float('nan')
                print('%s td %6.2f row %s step %4.2f (ref %4.2f) | com-heel meas %6.1f ref %6.1f  d=%6.1f m -> om_x^2 d = %5.1f m/s2 | v %4.2f -> +1.5s %4.2f  fr mean %.2f'%('LR'[i],r['t'],row,stepdur,ref[1],dm,ref[0],dm-ref[0],0.64*(dm-ref[0]),vf,v15,frm))
                lasttd=r['t']
            lift[i]=None
