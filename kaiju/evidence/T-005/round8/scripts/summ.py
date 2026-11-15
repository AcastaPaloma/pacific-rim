#!/usr/bin/env python
"""Per-config 4-seed fall times from a ScenarioCli summary.txt: summ.py summary.txt  (prints name, seeds, median, max)."""
import re, sys, statistics, collections
d = collections.OrderedDict()
for l in open(sys.argv[1]):
    m = re.match(r'walk_\w+@(\S+?)s(\d)\s+t=\s*([\d.]+)s fell=(\w+)(?: at=([\d.]+))?', l)
    if m:
        n, k, t, f, at = m.groups(); d.setdefault(n, []).append(float(at) if f == 'True' else float(t) + 0.0)
for n, v in d.items(): print(f"{n:12s} {' '.join(f'{x:5.1f}' for x in v)}  median {statistics.median(v):5.1f}  max {max(v):5.1f}")
