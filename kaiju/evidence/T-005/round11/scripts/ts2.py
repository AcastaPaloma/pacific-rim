import json,sys,math
f=sys.argv[1]; t0=float(sys.argv[2]); t1=float(sys.argv[3]); dt=float(sys.argv[4]) if len(sys.argv)>4 else 0.25
last=-1; W=9.81e7
def pitch(q):
    x,y,z,w=q; return math.degrees(math.asin(max(-1,min(1,2*(w*x-y*z)))))
print("  t   ph st| comz  vz   vy  | dcmz copcmd swtgt refz| L z    y   pit c  fn   copz  ftz | R z    y   pit c  fn   copz  ftz | ang st_ref/meas sw_ref/meas")
for l in open(f):
    if '"rec":"step"' not in l: continue
    d=json.loads(l); t=d['t']
    if t<t0 or t>t1: continue
    if t-last < dt-1e-6: continue
    last=t; c=d['ctl']
    s=f"{t:5.2f} {c['phase'][:3]} {c['stance']}|{d['com_p'][2]:6.2f}{d['com_v'][2]:5.2f}{d['com_v'][1]:5.2f} |{c['dcm'][1]:6.2f}{c['cop_cmd'][1]:6.2f}{c['swing_target'][1]:6.2f}{c['ref'][1]:6.2f}|"
    for ft in d['feet']:
        s+=f"{ft['p'][2]:6.2f}{ft['p'][1]:5.2f}{pitch(ft['q']):5.1f} {ft['contact']}{ft['fn']/W:5.2f}{ft['cop'][2]:6.2f}{ft['ft'][2]/W:5.2f}|"
    a=d.get('angles')
    if a: s+=" "+" ".join(f"{x:5.0f}" for x in a['st_ref']+a['st_meas'])+" /"+" ".join(f"{x:5.0f}" for x in a['sw_ref']+a['sw_meas'])
    print(s)
