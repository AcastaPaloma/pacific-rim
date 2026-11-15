#!/usr/bin/env python
"""T-005 r6: contact diagnostics of a gait log (works for qp_foot 0 and 1). Usage: diag97.py log.jsonl [tmin]
Per run: touchdowns (rising contact edges of a foot after >= 0.5 s in the air): sole pitch at first contact and 0.3 s later (deg, + toe down, from the foot quaternion),
CoP error = |measured total CoP (forward) - DCM CoP target k[25]| over loaded ticks, horizontal force = measured sum of foot shear along forward / W against the commanded
CoM-x acceleration / g (k[26]), ankle saturation share of the QP ankle torque (>= 0.97 limit) split swing / stance (k[6] = swing leg), share of ticks with a partial-contact stance foot."""
import json, sys, math
def analyse(f, tmin=5.0):
    W = 1.0e7 * 9.81; LIM = 1.0e9
    rows = []
    for l in open(f):
        if not l.startswith('{"rec":"step"'): continue
        r = json.loads(l)
        q = r.get('qp')
        if not q or not q.get('k') or len(q['k']) < 30: continue
        if r['ctl']['phase'] == 'Fallen': break
        rows.append(r)
    if not rows: return None
    tend = rows[-1]['t']
    def pitch(ft):
        qq = ft['q']; return math.degrees(2 * math.atan2(qq[0], qq[3]))
    air_since = [0.0, 0.0]; prev_c = [0, 0]; tds = []; pend = []
    cop_err = []; hf = []; sat_sw = sat_st = n_sw = n_st = 0; partial = loaded = nrow = 0; stance_pitch = []
    for r in rows:
        t = r['t']; fe = r['feet']; k = r['qp']['k']; tau = r['qp'].get('tau') or [0] * 6
        for i in (0, 1):
            c = fe[i]['contact']
            if c and not prev_c[i] and t - air_since[i] >= 0.5 and t > 1.0: tds.append([t, i, pitch(fe[i]), None])
            if prev_c[i] and not c: air_since[i] = t
            prev_c[i] = c
        for e in tds:
            if e[3] is None and t >= e[0] + 0.3: e[3] = pitch(fe[e[1]])
        if t < tmin: continue
        nrow += 1
        fn = [ft['fn'] for ft in fe]; tot = sum(fn)
        if tot > 0.3 * W:
            cop = sum(ft['fn'] * ft['cop'][2] for ft in fe if ft['fn'] > 0) / tot
            cop_err.append(abs(cop - k[25]))
            shear = sum(ft['ft'][2] for ft in fe) / W
            hf.append((shear, k[26] / 9.81))
            loaded += 1
            for ft in fe:
                if ft['fn'] > 0.3 * W: stance_pitch.append(abs(pitch(ft)))
        sw = int(round(k[6]))
        for leg in (0, 1):
            a = abs(tau[3 * leg + 2]) / LIM
            if leg == sw: n_sw += 1; sat_sw += a >= 0.97
            else: n_st += 1; sat_st += a >= 0.97
        if len(k) >= 52:
            ph = k[38 + 4:38 + 6]
            if any(p in (1, 3) for p in ph): partial += 1
    def med(x): x = sorted(x); return x[len(x) // 2] if x else float('nan')
    res = dict(name=f.split('@')[-1].split('.')[0], fall=tend, ntd=len(tds), p0=med([e[2] for e in tds if e[2] is not None]), p3=med([e[3] for e in tds if e[3] is not None]),
               cop=med(cop_err), sp=med(stance_pitch), hreg=(sum(a * b for a, b in hf) / max(sum(b * b for a, b in hf), 1e-12)) if hf else float('nan'),
               sat_sw=100 * sat_sw / max(n_sw, 1), sat_st=100 * sat_st / max(n_st, 1), part=100 * partial / max(nrow, 1))
    return res

if __name__ == '__main__':
    import collections, statistics
    groups = collections.OrderedDict()
    for f in sys.argv[1:]:
        r = analyse(f)
        if r: groups.setdefault(r['name'][:-2], []).append(r)
    print('variant | fall s (4 seeds) | sole pitch at first contact deg / +0.3 s (median) | CoP err m | stance |pitch| deg | shear meas/cmd | ankle sat swing/stance % | partial %')
    for k, v in groups.items():
        m = lambda key: statistics.median([x[key] for x in v])
        print('%-12s | %s | %5.1f / %5.1f | %4.2f | %4.1f | %5.2f | %4.1f / %4.1f | %4.1f' % (k, ' '.join('%.1f' % x['fall'] for x in v), m('p0'), m('p3'), m('cop'), m('sp'), m('hreg'), m('sat_sw'), m('sat_st'), m('part')))
