#!/usr/bin/env python3
"""T-201b variants builder. Usage: mk.py out.txt scenarios 'name:cap:key=val,key=val' ... --seeds N"""
import json, sys, math
def stime(v, l=42.0, g=9.81): fr = v*v/(g*l); return min(1.9*math.sqrt(l/g), 0.5*2.3*fr**0.3*l/v)
def cap(v): return round(v*stime(v)/42.0+0.002, 3)
BASE = {"walk_mode":2,"kin_hz":1.0,"kin_arm":1.0,"kin_hdelay":0.0,"kin_lift_m":1.0,"kin_ds":0.05,"kin_clear":0.12,"kin_clear_fast":0.13,"kin_clear_vref":0.6,"kin_tscale":0.55,"kin_gain":1.5}
out, scn = sys.argv[1], sys.argv[2]; n = 4; specs = []
a = sys.argv[3:]
if '--seeds' in a: i = a.index('--seeds'); n = int(a[i+1]); a = a[:i]+a[i+2:]
with open(out, 'w') as f:
    for sp in a:
        name, v, kv = (sp.split(':')+[''])[:3]
        d = dict(BASE, step_length_max_frac=cap(float(v)))
        for p in filter(None, kv.split(',')): k, x = p.split('='); d[k] = float(x)
        for s in range(n): f.write('%s#%ss%d %s\n' % (scn, name, s, json.dumps(dict(d, kin_noise=0.05, kin_seed=s))))
