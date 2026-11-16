#!/usr/bin/env python3
"""T-201: one-knob-at-a-time sweep around a base at rung speed v (m/s).
Usage: sweep.py <v> <out.txt> '<base overrides json>' '<{"knob":[values]}>' [nseeds]
Writes variants 'walk_v<v>#<name>s<i> <json>' (kin_noise 0.05, kin_seed i). 'full' as grid key list entry 'base' = the base alone."""
import json, sys
sys.path.insert(0, __file__.rsplit('/', 1)[0])
from mkvar import cap
v = sys.argv[1]; out = sys.argv[2]; base_over = json.loads(sys.argv[3]); grid = json.loads(sys.argv[4]); ns = int(sys.argv[5]) if len(sys.argv) > 5 else 1
base = dict({"walk_mode": 2, "step_length_max_frac": cap(float(v)), "kin_noise": 0.05}, **base_over)
S = {'base': {}}
for k, vals in grid.items():
    for x in vals: S[f'{k[4:] if k.startswith("kin_") else k}{x}'] = {k: x}
lines = []
for n, d in S.items():
    for i in range(ns): lines.append(f'walk_v{v}#{n}s{i} ' + json.dumps(dict(base, **d, kin_seed=i), separators=(',', ':')))
open(out, 'w').write('\n'.join(lines) + '\n'); print(len(lines), 'variants')
