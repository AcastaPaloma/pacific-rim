"""T-005 r7: make a warm-start stride json for the flat-foot schedule from an existing stride json (heel/toe forces of each foot split evenly, step time scaled).
Usage: mkwarm.py <in.json> <out.json> [step_time_s]"""
import json, sys
d = json.load(open(sys.argv[1])); ts_new = float(sys.argv[3]) if len(sys.argv) > 3 else None
for n in d['nodes']:
    c = n['contact_N']
    for f in ('A', 'B'):
        h, t = c[f + '_heel'], c[f + '_toe']; ft, fn = h[0] + t[0], h[1] + t[1]
        # a foot that is on the ground in the old schedule (any force) is split evenly; unloaded stays zero
        c[f + '_heel'] = [ft / 2, fn / 2]; c[f + '_toe'] = [ft / 2, fn / 2]
if ts_new:
    d['summary']['step_time_s'] = ts_new
json.dump(d, open(sys.argv[2], 'w'))
