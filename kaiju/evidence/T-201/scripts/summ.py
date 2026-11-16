#!/usr/bin/env python3
"""T-201: group gait logs by variant (strip trailing seed 's<i>'), worst case over seeds. Usage: summ.py <gait dir> [substring] [--pass] ; --pass lists only variants that meet the rung thresholds (no fall, tilt<=5, slip mean<=0.5)."""
import sys, os, re, collections
sys.path.insert(0, __file__.rsplit('/', 1)[0])
import analyse2 as a
d = sys.argv[1]; sub = sys.argv[2] if len(sys.argv) > 2 and not sys.argv[2].startswith('--') else ''; only = '--pass' in sys.argv
G = collections.OrderedDict()
for f in sorted(os.listdir(d)):
    if not f.endswith('.gait.jsonl') or sub not in f: continue
    n = f[:-len('.gait.jsonl')]; key = re.sub(r's\d+$', '', n); G.setdefault(key, []).append(a.metrics(os.path.join(d, f)))
print('%-58s n fell  v_mean slipM(max) slipMax tiltMax  F%%W(mean/p95) T%%Wl(mean/p95)' % 'variant (worst over seeds)')
for k, ms in G.items():
    ms = [m for m in ms if m]
    if not ms: continue
    fell = sum(m['fell'] for m in ms); sm = max(m['slip_mean'] for m in ms); tm = max(m['tilt_max'] for m in ms); ok = fell == 0 and tm <= 5.0 and sm <= 0.5
    if only and not ok: continue
    print('%-58s %d %-4s %6.2f %9.2f %7.2f %7.1f %7.1f/%-5.1f %7.1f/%-5.1f %s' % (k.replace('walk_', '')[:58], len(ms), fell, min(m['v'] or 0 for m in ms), sm, max(m['slip_max'] for m in ms), tm, max(m['F_mean'] for m in ms), max(m['F_p95'] for m in ms), max(m['T_mean'] for m in ms), max(m['T_p95'] for m in ms), 'PASS' if ok else ''))
