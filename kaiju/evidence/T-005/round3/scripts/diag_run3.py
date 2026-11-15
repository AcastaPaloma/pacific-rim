import json,sys,math
D='/private/tmp/claude-501/t005r3/run3/out/gait/'
def load(name):
    R=[]
    for l in open(D+name+'.gait.jsonl'):
        if '"rec":"step"' in l[:16]: R.append(json.loads(l))
    return R
def series(name,tag):
    R=load(name); out=[]
    prev=[0,0]; td=[]
    for r in R:
        c=[f['contact'] for f in r['feet']]
        for i in (0,1):
            if c[i] and not prev[i]: td.append((r['t'],i,r['angles']['fr_ref'] if r['angles'] else None))
        prev=c
    fall=None
    for r in R:
        if r['ctl']['phase']=='Fallen': fall=r['t'];break
    print('==',name,'fall t',fall,'touchdowns',len(td))
    ts=[t for t,_,_ in td]
    print(' TD t:',[round(t,1) for t in ts[:12]],' dt:',[round(b-a,2) for a,b in zip(ts,ts[1:12])])
    print(' TD fr_ref at touchdown:',[round(x,2) if x is not None else None for _,_,x in td[:12]])
    T=fall or R[-1]['t']
    for r in R[::120]:
        if r['t']>T+1: break
        a=r['angles'];i=r['intent'];c=r['com_p']
        s=f"t={r['t']:5.1f} cmd={i['cmd_speed_mps']:5.2f} vz={r['com_v'][2]:6.2f} vx={r['com_v'][0]:6.2f} x={c[0]:7.1f} z={c[2]:7.1f} h={c[1]:5.1f} yaw={math.degrees(r['yaw']):6.1f} hd={math.degrees(i['cmd_heading_rad']):5.1f} ph={r['ctl']['phase'][:6]}"
        if a:
            e=[round(m-f,1) for m,f in zip(a['st_meas'],a['st_ref'])]; w=[round(m-f,1) for m,f in zip(a['sw_meas'],a['sw_ref'])]
            s+=f" fr={a['fr_ref']:.2f} stErr={e} swErr={w}"
        print(s)
    # tracking error stats and sign correlation
    import statistics as S
    for k,(rk,mk) in {'st':('st_ref','st_meas'),'sw':('sw_ref','sw_meas')}.items():
        for j,jn in enumerate(('hip','knee','ankle')):
            for lo,hi in ((0,5),(5,10),(10,T)):
                ref=[r['angles'][rk][j] for r in R if r['angles'] and lo<=r['t']<hi]
                mea=[r['angles'][mk][j] for r in R if r['angles'] and lo<=r['t']<hi]
                if len(ref)<10: continue
                er=[m-f for m,f in zip(mea,ref)]
                mr=S.mean(ref);mm=S.mean(mea)
                num=sum((a-mr)*(b-mm) for a,b in zip(ref,mea));den=math.sqrt(sum((a-mr)**2 for a in ref)*sum((b-mm)**2 for b in mea))+1e-9
                cer=sum((a-mr)*(b-S.mean(er)) for a,b in zip(ref,er))/(math.sqrt(sum((a-mr)**2 for a in ref)*sum((b-S.mean(er))**2 for b in er))+1e-9)
                print(f" {k} {jn:5s} t[{lo},{hi:.0f}) rms_err={math.sqrt(S.mean([e*e for e in er])):6.1f} ref_rms={math.sqrt(S.mean([a*a for a in ref])):5.1f} corr(ref,meas)={num/den:5.2f} corr(ref,err)={cer:5.2f}")
for n in sys.argv[1:] or ['walk_fr010@imp2','walk_fr010@sync_sw3']:
    series(n,n)

# ---- part 2: step timing from stance switches, yaw onset, amplitude gain/lag, lateral vs forward
def part2(name):
    R=load(name); import numpy as np
    sw=[];ps=None
    for r in R:
        s=r['ctl']['stance']
        if ps is not None and s!=ps and r['ctl']['phase'] in('SingleSupport','DoubleSupport'): sw.append(r['t'])
        ps=s
    dts=np.diff(sw)
    fall=next((r['t'] for r in R if r['ctl']['phase'] in('Falling','Fallen')),R[-1]['t'])
    print('==',name,'part2; fall(Falling) t',round(fall,1),'stance switches',len(sw))
    print(' switch dt (s) before fall:',[round(x,1) for x in dts[:14]],' median',round(float(np.median(dts[np.array(sw[1:])<fall])),2) if len(dts) else None)
    t=np.array([r['t'] for r in R]);yaw=np.degrees([r['yaw'] for r in R]);hd=np.degrees([r['intent']['cmd_heading_rad'] for r in R])
    for th in (5,10,20): 
        i=np.argmax(np.abs(yaw)>th); print(f' |yaw|>{th} deg first at t={t[i]:.1f}')
    z=np.array([r['com_p'][2] for r in R]);x=np.array([r['com_p'][0] for r in R]);cmd=np.array([r['intent']['cmd_speed_mps'] for r in R])
    m=(t>12)&(t<min(fall,28))
    print(f' fwd(z) mean speed t12..{min(fall,28):.0f}: {(z[m][-1]-z[m][0])/(t[m][-1]-t[m][0]):.2f} m/s vs cmd {cmd[m].mean():.2f}; lateral(x) {(x[m][-1]-x[m][0])/(t[m][-1]-t[m][0]):.2f} m/s; at fall z={z[t<=fall][-1]:.0f} x={x[t<=fall][-1]:.0f}')
    # per-joint first time smoothed |err| > 15 deg (stance), and amplitude gain / lag (cross-correlation)
    for k,(rk,mk) in {'st':('st_ref','st_meas'),'sw':('sw_ref','sw_meas')}.items():
        for j,jn in enumerate(('hip','knee','ankle')):
            sel=[(r['t'],r['angles'][rk][j],r['angles'][mk][j]) for r in R if r['angles'] and 10<=r['t']<min(fall,28)]
            if len(sel)<60: continue
            a=np.array(sel);ref=a[:,1]-a[:,1].mean();me=a[:,2]-a[:,2].mean()
            gain=(ref@me)/(ref@ref); e=np.abs(a[:,2]-a[:,1]); k5=np.convolve(e,np.ones(30)/30,'same')
            big=np.argmax(k5>15); first=a[big,0] if k5.max()>15 else None
            print(f'  {k} {jn:5s} slope(meas on ref)={gain:5.2f} first smoothed |err|>15deg t={first}')
for n in sys.argv[1:] or ['walk_fr010@imp2','walk_fr010@sync_sw3']: part2(n)

# ---- part 3: yaw authority around the yaw onset
def part3(name):
    import numpy as np
    R=load(name); print('==',name,'part3 yaw authority')
    for r in R[480:1200:60]:
        if r['ctl']['phase'] in('Falling','Fallen'): break
        print(f" t={r['t']:5.1f} yaw={math.degrees(r['yaw']):6.1f} yawrate={math.degrees(r['yaw_rate']):6.2f}d/s moment={r['yaw_moment_nm']/1e6:7.1f}MNm budget={r['yaw_budget_nm']/1e6:6.1f} util={r['yaw_util']:.2f} stance={r['ctl']['stance']} contacts={[f['contact'] for f in r['feet']]} swclamp={r['ctl']['swing_clamped']}")
    u=np.array([r['yaw_util'] for r in R if 8<r['t']<17.5]);print(' yaw_util 8..17.5 s: mean %.2f max %.2f frac>0.9 %.2f'%(u.mean(),u.max(),(u>0.9).mean()))
for n in sys.argv[1:] or ['walk_fr010@imp2','walk_fr010@sync_sw3']: part3(n)
