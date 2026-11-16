"""T-005 r12: where the rear foot carries its load in double support (7.7 s .. tmax). Per DS tick with rear fn > 0.1 W: rear CoP relative to the rear foot's heel point (m; sole 15 m), rear pitch (deg, + = heel up), load-weighted means.
Usage: rear.py <gait.jsonl> [tmax]"""
import json,sys,math
f=sys.argv[1]; tmax=float(sys.argv[2]) if len(sys.argv)>2 else 22; W=9.81e7
def pitch(q):
    x,y,z,w=q; return math.degrees(math.asin(max(-1,min(1,2*(w*x-y*z)))))
n=0; cop=[]; pit=[]; wt=[]; heel_rel=[]; prmax=-99; prmin=99; ncross=0
for l in open(f):
    if '"rec":"step"' not in l: continue
    d=json.loads(l)
    if d['t']<7.7 or d['t']>tmax or d['ctl']['phase']!='DoubleSupport': continue
    fe=d['feet']; fi=0 if fe[0]['p'][2]>fe[1]['p'][2] else 1; ri=1-fi; r=fe[ri]
    if r['fn']<0.1*W: continue
    cop.append(r['cop'][2]-r['p'][2]); pit.append(pitch(r['q'])); wt.append(r['fn']); heel_rel.append(r['cop'][2]-d['com_p'][2])
if not cop: print("no rear-loaded DS ticks"); sys.exit()
sw=sum(wt); m=lambda a: sum(x*w for x,w in zip(a,wt))/sw
print(f"n={len(cop)} rear CoP - rear heel x (load-weighted) {m(cop):5.2f} m (sole 0..15)  rear pitch {m(pit):5.1f} deg  min {min(pit):5.1f} max {max(pit):5.1f}  rear CoP - CoM {m(heel_rel):5.2f} m")
