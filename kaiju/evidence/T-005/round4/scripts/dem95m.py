import json,math,sys
G=9.81; W=1e7*G
ref=json.load(open('/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/.wt/T-005-94b/kaiju/Assets/Game/Config/Mech/gait_reference_qp_v4.json'))['rows'][0]
N=len(ref['nodes']); H=ref['step_time_s']/N; nd=ref['nodes']
def interp(ch,i,tr):
    x=tr/H; k=int(x); k=max(0,min(N-2,k)); s=x-k
    return nd[k][ch][i]*(1-s)+nd[k+1][ch][i]*s
def ptsx(p,tr):
    x=tr/H; k=int(x); k=max(0,min(N-2,k)); s=x-k
    return nd[k]['pts'][2*p]*(1-s)+nd[k+1]['pts'][2*p]*s
f=sys.argv[1]; t0=float(sys.argv[2]); t1=float(sys.argv[3]); row=sys.argv[4] if len(sys.argv)>4 else '6'
rows=[]
for l in open(f):
    if l.startswith('{"rec":"step"'):
        try: rows.append(json.loads(l))
        except: pass
prev=None
for i,r in enumerate(rows):
    if r['t']<t0 or r['t']>t1 or i<2: continue
    q=r.get('qp')
    if not q or str(q.get('row'))!=row: continue
    k=q['k']; tr=min(k[7],ref['step_time_s']-0.01)
    yaw=r['yaw']; fw=(math.sin(yaw),math.cos(yaw))
    fx=lambda p:p[0]*fw[0]+p[2]*fw[1]
    v=lambda rr: rr['com_v'][0]*fw[0]+rr['com_v'][2]*fw[1]
    a=(v(rows[i+1])-v(rows[i-1]))/(rows[i+1]['t']-rows[i-1]['t'])
    if i%6: continue
    fn=[ft['fn']/W for ft in r['feet']]
    st=0 if fn[0]>fn[1] else 1
    # stance heel x: foot link pos is ankle; use cop?
    com=fx(r['com_p']); sh=fx(r['feet'][st]['p'])
    # ref com rel B heel
    rc=interp('com',0,tr)-ptsx(2,tr)
    e=rc-(com-sh) ; 
    vr=interp('comd',0,tr); ar=interp('comdd',0,tr)
    dem=ar+0.64*e+1.6*(vr-v(r))
    ftr=[ (ft['ft'][0]*fw[0]+ft['ft'][2]*fw[1])/W for ft in r['feet']]
    print('t%5.1f tref%4.2f v %.1f vref %.1f | ecx(ref-meas rel foot) %5.1f | demand %5.1f a_meas %5.1f | fn %.2f %.2f ftx %.2f %.2f fr %.2f'%(r['t'],tr,v(r),vr,e,dem,a,fn[0],fn[1],ftr[0],ftr[1],q['fr']))
