#!/usr/bin/env python
"""T-005 r10: per-step trace of a qp_r10_diag gait log. Usage: steps10.py <gait.jsonl> [nsteps]
Touchdown rows come from the controller's R10Log (qp.k[55..65]); CoP clamp saturation and solver status are aggregated per step (touchdown to touchdown).
Columns: t_td, foot, DCM offset over the landed heel measured / reference (m), ratio, step length measured / reference (m), period / reference (s),
unload (s, previous step) / its reference DS, vx at td (m/s), then per-step stats over the stance phase: share of ticks with the DCM CoP target beyond the clamp interval
(high = past toe limit, low = behind heel limit), worst overshoot (m), mean measured CoP minus heel limit of the sole interval, QP failures added, max solver status."""
import json, sys
f = sys.argv[1]; ns = int(sys.argv[2]) if len(sys.argv) > 2 else 8
rows = []
for l in open(f):
    if '"rec":"step"' not in l or '"qp"' not in l: continue
    d = json.loads(l); k = d['qp'].get('k')
    if not k or len(k) < 70: continue
    rows.append((d['t'], k, d['qp'], d['com_v'], d['com_p']))
td = []; cur = None
for t, k, q, cv, cp in rows:
    if cur is None or k[55] != cur['t0']:
        cur = dict(t0=k[55], k=k, n=0, hi=0, lo=0, ov=0.0, f0=q['fail'], fmax=0, smax=0, xs=[], vz=[], z=[]); td.append(cur)
    cur['n'] += 1; raw, lo, hi = k[66], k[67], k[68]
    if hi >= lo:
        if raw > hi + 1e-6: cur['hi'] += 1; cur['ov'] = max(cur['ov'], raw - hi)
        if raw < lo - 1e-6: cur['lo'] += 1; cur['ov'] = max(cur['ov'], lo - raw)
    cur['f1'] = q['fail']; cur['smax'] = max(cur['smax'], q['s']); cur['vz'].append(cv[2]); cur['z'].append(cp[1])
print('step  t_td  ft  dcm_meas dcm_ref ratio | len_meas len_ref | period ref_T | unload ref_ds | vz_td | hiClamp% loClamp% ovshoot | qpfail+ smax | vz_min vz_end zmin')
for i, c in enumerate(td[:ns + 1]):
    k = c['k']; n = max(c['n'], 1)
    print(f"{i:3d} {k[55]:6.2f} {int(k[64])} {k[56]:8.2f} {k[57]:7.2f} {k[56]/max(k[57],1e-6):5.2f} | {k[58]:7.2f} {k[59]:6.2f} | {k[60]:6.2f} {k[61]:5.2f} | {k[62]:6.2f} {k[63]:5.2f} | {k[65]:5.2f} | {100*c['hi']/n:6.1f} {100*c['lo']/n:6.1f} {c['ov']:6.2f} | {int(c.get('f1',0)-c['f0']):3d} {c['smax']} | {min(c['vz']):5.2f} {c['vz'][-1]:5.2f} {min(c['z']):5.2f}")
