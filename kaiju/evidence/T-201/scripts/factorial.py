#!/usr/bin/env python3
"""T-201: full-factorial variants around a base at rung speed v. Usage: factorial.py <v> <out.txt> '<base json>' '<{"knob":[values]}>' <nseeds> [scenarios a+b]
Variant line: '<scn>#<name>s<i> <json>' (kin_noise 0.05, kin_seed i). Default scenario walk_v<v>."""
import json, sys, itertools
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from mkvar import cap
v = sys.argv[1]; out = sys.argv[2]; base_over = json.loads(sys.argv[3]); grid = json.loads(sys.argv[4]); ns = int(sys.argv[5]); scn = sys.argv[6] if len(sys.argv) > 6 else f'walk_v{v}'
base = dict({"walk_mode": 2, "step_length_max_frac": cap(float(v)), "kin_noise": 0.05}, **base_over)
keys = list(grid); lines = []
for combo in itertools.product(*[grid[k] for k in keys]):
    d = dict(zip(keys, combo)); name = '_'.join(f'{k[4:] if k.startswith("kin_") else k}{x}' for k, x in d.items())
    for i in range(ns): lines.append(f'{scn}#{name}s{i} ' + json.dumps(dict(base, **d, kin_seed=i), separators=(',', ':')))
open(out, 'w').write('\n'.join(lines) + '\n'); print(len(lines), 'variants')
