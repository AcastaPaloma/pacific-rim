#!/usr/bin/env python3
"""T-201: variants file builder (name + JSON per line). Usage: mkvar.py <set> <out.txt>"""
import json, sys
K = {"walk_mode": 2}
def seeds(name, d, n=4): return {f'{name}s{i}': dict(d, kin_noise=0.05, kin_seed=i) for i in range(n)}
SETS = {}
SETS['base'] = {'B0': dict(K)}
# first ladder, one seed: only the cap moves (step_length_max_frac for the rung speed, kin_fmax_frac scaled)
import math
def stime(v, l=42.0, g=9.81): fr = v * v / (g * l); return min(1.9 * math.sqrt(l / g), 0.5 * 2.3 * fr ** 0.3 * l / v)
def cap(v): return round(v * stime(v) / 42.0 + 0.002, 3)   # step_length_max_frac that makes the step-reach cap equal v
def rung(v, **kw): return dict(K, step_length_max_frac=cap(v), **kw)
SETS['r1'] = {f'V{v}': rung(v, kin_noise=0.05, kin_seed=0) for v in (3, 4, 5, 6, 7, 8, 9)}
def main():
    name, out = sys.argv[1], sys.argv[2]
    sets = SETS[name]
    open(out, 'w').write('\n'.join(vn + ' ' + json.dumps(d, separators=(',', ':')) for vn, d in sets.items()) + '\n'); print('ok', len(sets), out)
if __name__ == '__main__': main()
