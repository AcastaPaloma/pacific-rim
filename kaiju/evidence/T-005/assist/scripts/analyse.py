#!/usr/bin/env python
"""T-005 assist: per gait log: fall time, mean speed, steps, assist magnitudes (force as % of body weight W, torque as % of W l), foot slip, speed vs command.
Usage: analyse.py gait.jsonl [t0 t1]   (forward = +z; W = M g; l = 42 m leg length of the balanced frame; slip = horizontal path of a foot during stance after 0.3 s of contact)"""
import json, sys, math
f = sys.argv[1]
M = 1e7; G = 9.81; W = M * G; L = 42.0
recs = []
for l in open(f):
    if l.startswith('{"rec":"step"'): recs.append(json.loads(l))
T0 = float(sys.argv[2]) if len(sys.argv) > 2 else 0; T1 = float(sys.argv[3]) if len(sys.argv) > 3 else 1e9
R = [r for r in recs if T0 <= r['t'] <= T1]
fall = next((r['t'] for r in recs if r['ctl']['phase'] in ('Falling', 'Fallen')), None)
last = recs[-1]
# assist
fm = []; tm = []; fup = []; fsv = []
for r in R:
    a = r.get('assist')
    if not a: continue
    fx, fy, fz = a['f']; fm.append(math.sqrt(fx * fx + fy * fy + fz * fz) / W); fup.append(fy / W); fsv.append(math.hypot(fx, fz) / W)
    tx, ty, tz = a['t']; tm.append(math.sqrt(tx * tx + ty * ty + tz * tz) / (W * L))
def pct(a, q): s = sorted(a); return s[min(len(s) - 1, int(q * len(s)))] if s else 0
def stat(a): return 'mean %.1f%% p95 %.1f%% max %.1f%%' % (100 * sum(a) / max(1, len(a)), 100 * pct(a, .95), 100 * (max(a) if a else 0))
# slip: horizontal travel of the lowest sole end (heel or toe, +-7.5 m from the sole centre) while a foot is in contact and that end is within 0.4 m of the ground; one value per contact stretch (m)
def rot(q, v):
    x, y, z, w = q; vx, vy, vz = v
    tx = 2 * (y * vz - z * vy); ty = 2 * (z * vx - x * vz); tz = 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty), vy + w * ty + (z * tx - x * tz), vz + w * tz + (x * ty - y * tx))
slips = []
for k in range(2):
    run = 0.0; prev = None; start = None; n = 0
    for r in recs + [None]:
        ok = False
        if r and r['feet'][k]['contact']:
            ft = r['feet'][k]; pts = []
            for dz in (-7.5, 7.5):
                o = rot(ft['q'], (0, 0, dz)); pts.append((ft['p'][1] + o[1], (ft['p'][0] + o[0], ft['p'][2] + o[2]), dz))
            low = min(pts)
            if low[0] < 0.4:
                ok = True
                if prev is not None and prev[0] == low[2]:
                    run += math.hypot(low[1][0] - prev[1][0], low[1][1] - prev[1][1])
                prev = (low[2], low[1])
                if start is None: start = r['t']
        if not ok:
            prev = None
            if start is not None and n >= 0:
                if T0 <= start <= T1 and run >= 0: slips.append((run, run, 0))
                start = None; run = 0.0
# speed + command
sp = [math.hypot(r['com_v'][0], r['com_v'][2]) for r in R]; cmd = [r['intent']['cmd_speed_mps'] for r in R]
t_end = R[-1]['t'] if R else 0
comz0 = R[0]['com_p'][2] if R else 0; comz1 = R[-1]['com_p'][2] if R else 0
tilt = []
for r in R:
    q = r['root_q']; x, y, z, w = q; upy = 1 - 2 * (x * x + z * z); tilt.append(math.degrees(math.acos(max(-1, min(1, upy)))))
print('file %s' % f.split('/')[-1])
print('fall at %s ; log end %.1f s ; window %.1f-%.1f s' % (fall, last['t'], R[0]['t'] if R else 0, t_end))
print('forward progress %.1f m, mean speed %.2f m/s (cmd mean %.2f), steps(stances) %d' % (comz1 - comz0, (comz1 - comz0) / max(1e-9, t_end - (R[0]['t'] if R else 0)), sum(cmd) / max(1, len(cmd)), len(slips)))
print('pelvis tilt: mean %.1f deg max %.1f deg' % (sum(tilt) / max(1, len(tilt)), max(tilt) if tilt else 0))
print('assist force |F|/W   : ' + stat(fm)); print('  vertical support /W: ' + stat(fup)); print('  servo (horizontal)/W: ' + stat(fsv)); print('assist torque |T|/(W l): ' + stat(tm))
if slips:
    p = [s[0] for s in slips]; n = [s[1] for s in slips]
    print('foot slip per ground contact (m, lowest sole end): mean %.2f p90 %.2f max %.2f ; contacts %d' % (sum(p) / len(p), pct(p, .9), max(p), len(p)))
