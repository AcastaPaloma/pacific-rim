#!/usr/bin/env python3
"""T-201: batch metrics from gait_log@v1 files (same definitions as evidence/T-005/assist/scripts/analyse.py).
Usage: analyse2.py <gait dir> <glob-substring-or-'all'> [t0] -> one row per log.
fell, fall time, mean forward speed (window t0..end), foot slip per ground contact (mean, max; lowest sole end, per contact stretch), pelvis tilt (mean, max, whole log),
assist force |F| and torque |T| as % of W = M g and % of W l (mean, p95, window)."""
import json, sys, math, os
M = 1e7; G = 9.81; W = M * G; L = 42.0
def rot(q, v):
    x, y, z, w = q; vx, vy, vz = v
    tx = 2 * (y * vz - z * vy); ty = 2 * (z * vx - x * vz); tz = 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty), vy + w * ty + (z * tx - x * tz), vz + w * tz + (x * ty - y * tx))
def pct(a, q): s = sorted(a); return s[min(len(s) - 1, int(q * len(s)))] if s else 0.0
def metrics(path, t0=8.0):
    recs = []
    for l in open(path):
        if l.startswith('{"rec":"step"'): recs.append(json.loads(l))
    if not recs: return None
    fall = next((r['t'] for r in recs if r['ctl']['phase'] in ('Falling', 'Fallen')), None)
    R = [r for r in recs if r['t'] >= t0 and (fall is None or r['t'] < fall)]
    fm, tm = [], []
    for r in R:
        a = r.get('assist')
        if not a: continue
        fx, fy, fz = a['f']; fm.append(math.sqrt(fx * fx + fy * fy + fz * fz) / W)
        tx, ty, tz = a['t']; tm.append(math.sqrt(tx * tx + ty * ty + tz * tz) / (W * L))
    slips = []
    for k in range(2):
        run = 0.0; prev = None; start = None
        for r in recs + [None]:
            ok = False
            if r and r['feet'][k]['contact'] and (fall is None or r['t'] < fall):
                ft = r['feet'][k]; pts = []
                for dz in (-7.5, 7.5):
                    o = rot(ft['q'], (0, 0, dz)); pts.append((ft['p'][1] + o[1], (ft['p'][0] + o[0], ft['p'][2] + o[2]), dz))
                low = min(pts)
                if low[0] < 0.4:
                    ok = True
                    if prev is not None and prev[0] == low[2]: run += math.hypot(low[1][0] - prev[1][0], low[1][1] - prev[1][1])
                    prev = (low[2], low[1])
                    if start is None: start = r['t']
            if not ok:
                prev = None
                if start is not None:
                    if start >= t0: slips.append(run)
                    start = None; run = 0.0
    tilt = []
    for r in recs:
        if fall is not None and r['t'] >= fall: break
        x, y, z, w = r['root_q']; tilt.append(math.degrees(math.acos(max(-1.0, min(1.0, 1 - 2 * (x * x + z * z))))))
    # mean speed of the whole-body centre of mass over the window
    sp = None
    if len(R) > 1:
        d = math.hypot(R[-1]['com_p'][0] - R[0]['com_p'][0], R[-1]['com_p'][2] - R[0]['com_p'][2]); sp = d / (R[-1]['t'] - R[0]['t'])   # net horizontal displacement / time (as T-005 analyse.py; undercounts a curve)
    cmd = sum(r['intent']['cmd_speed_mps'] for r in R) / max(1, len(R)) if R else 0
    return dict(fell=fall is not None, fall_t=fall, t_end=recs[-1]['t'], v=sp, cmd=cmd, slip_mean=sum(slips) / len(slips) if slips else 0.0, slip_max=max(slips) if slips else 0.0, n_slip=len(slips),
                tilt_mean=sum(tilt) / max(1, len(tilt)), tilt_max=max(tilt) if tilt else 0.0, F_mean=100 * sum(fm) / max(1, len(fm)), F_p95=100 * pct(fm, .95), T_mean=100 * sum(tm) / max(1, len(tm)), T_p95=100 * pct(tm, .95),
                hmin=min(r['com_p'][1] for r in recs if fall is None or r['t'] < fall))
HDR = 'run                      fell  v_mean cmd   slip_mean slip_max tilt_mean tilt_max  F_mean F_p95  T_mean T_p95  hmin'
def row(name, m):
    if m is None: return name + ' NO DATA'
    return '%-24s %-5s %6s %5.2f %8.2f %8.2f %8.1f %8.1f %7.1f %6.1f %7.1f %6.1f %6.1f' % (name, ('FELL@%.1f' % m['fall_t']) if m['fell'] else 'no', ('%.2f' % m['v']) if m['v'] is not None else '-', m['cmd'], m['slip_mean'], m['slip_max'], m['tilt_mean'], m['tilt_max'], m['F_mean'], m['F_p95'], m['T_mean'], m['T_p95'], m['hmin'])
if __name__ == '__main__':
    d = sys.argv[1]; sub = sys.argv[2] if len(sys.argv) > 2 else 'all'; t0 = float(sys.argv[3]) if len(sys.argv) > 3 else 8.0
    print(HDR)
    for f in sorted(os.listdir(d)):
        if not f.endswith('.gait.jsonl') or (sub != 'all' and sub not in f): continue
        print(row(f[:-len('.gait.jsonl')], metrics(os.path.join(d, f), t0)))
