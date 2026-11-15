import gzip,json,sys,math,bisect
ref=json.load(open(sys.argv[1]))['rows'][0]; N=ref['nodes']; ts=ref['step_time_s']
f=sys.argv[2]; tA=float(sys.argv[3]); stIdx=int(sys.argv[4]); t1=float(sys.argv[5])
def rat(tau,leg,key):
    tau=min(tau,N[-1]['t_s']); i=min(int(tau/(ts/len(N))),len(N)-2); a=(tau-N[i]['t_s'])/(N[i+1]['t_s']-N[i]['t_s'])
    return N[i][leg][key]*(1-a)+N[i+1][leg][key]*a
def eul(q):
    x,y,z,w=q; return math.degrees(math.asin(max(-1,min(1,2*(w*x-y*z)))))
for l in gzip.open(f,'rt'):
    r=json.loads(l)
    if r.get('rec')!='step' or not(tA<=r['t']<=t1) or r['n']%9: continue
    tau=r['t']-tA; a=r['angles']; c=r['com_p'][2]; S=r['feet'][stIdx]; Wf=r['feet'][1-stIdx]
    # reference positions rel pelvis: stance sole center ~ ankle_x+2.25
    rp=lambda leg: rat(tau,leg,'ankle_x_m')
    print('tau %5.2f | st hip/knee/ank ref %5.1f %5.1f %5.1f meas %5.1f %5.1f %5.1f | sw ref %5.1f %5.1f %5.1f meas %5.1f %5.1f %5.1f | ankX-pel ref st %6.1f sw %6.1f ; foot-com meas st %6.1f sw %6.1f | sw h ref %4.1f meas %4.1f fp ref %5.1f meas %5.1f | vz %4.2f'%(tau,rat(tau,'stance','hip_deg'),rat(tau,'stance','knee_deg'),rat(tau,'stance','ankle_deg'),*a['st_meas'],rat(tau,'swing','hip_deg'),rat(tau,'swing','knee_deg'),rat(tau,'swing','ankle_deg'),*a['sw_meas'],rp('stance'),rp('swing'),S['p'][2]-c,Wf['p'][2]-c,rat(tau,'swing','ankle_height_m'),Wf['p'][1],rat(tau,'swing','foot_pitch_deg'),eul(Wf['q']),r['com_v'][2]))
