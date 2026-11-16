#!/usr/bin/env python3
"""T-193: reads the sweep output (<root>/<variant>/<config>/<path_id>/...) and tabulates per variant and configuration
   DIG max per M-003 set (p3_a, p4_a) and C_px (M-004 definition, Tools/Metrics sequence_metrics) per Y sequence.
   Usage: analyse.py <sweep out root> <out.json> ; python -I not needed (reads only local captures)."""
import glob, json, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.abspath(os.path.join(HERE, "..", "..", "..", "Metrics")))
import numpy as np
from kaiju_metrics.contracts.palette_set import load_palette_set
from kaiju_metrics.contracts.style_capture import load_capture_set
from kaiju_metrics.registry import load_registry
from kaiju_metrics.style import m003, m004 as m4, measures as M

pal = load_palette_set(None); r3 = load_registry("M-003"); r4 = load_registry("M-004")
P = {k: r3.p(k) for k in m003.DITHER_KEYS}
root, out = sys.argv[1], sys.argv[2]
only = sys.argv[3] if len(sys.argv) > 3 else "*"   # variant glob, e.g. L0*
res = {}
for vdir in sorted(glob.glob(os.path.join(root, only, "*"))):
    if not os.path.isdir(vdir): continue
    v, c = vdir.split(os.sep)[-2:]
    row = res.setdefault(v, {}).setdefault(c, {})
    for pdir in sorted(glob.glob(os.path.join(vdir, "*"))):
        pid = os.path.basename(pdir)
        if not os.path.isdir(pdir): continue
        cs, e = load_capture_set(pdir, pal, collect_only=True)
        if cs is None: continue
        for sq in cs.sequences:
            prof = pal.profiles[sq.frames[0].profile_id]
            if sq.purpose == "M-004":
                cls = m4.classify(sq, r4, [])
                m = m4.sequence_metrics(sq, prof, r4, cls)
                row[f"{pid}/{sq.id}"] = {"C": m["C"], "S": m["S"], "F": m["F"], "inter_share": m["interleaved_share"]}
            elif sq.purpose == "M-003":
                digs = []
                for fr in sq.frames:
                    d = m003.frame_data(fr, prof, need_output=False)
                    dm, _ = M.dither_metrics(d["idx"], d["L_idx"], P)
                    if dm["gradient_share"] >= r3.p("min_gradient_share") and np.isfinite(dm["DIG"]): digs.append(dm["DIG"])
                row[f"{pid}/DIG"] = {"max": max(digs) if digs else None, "mean": float(np.mean(digs)) if digs else None, "n": len(digs),
                                     "min": min(digs) if digs else None}
json.dump(res, open(out, "w"), indent=1)
for v, cs_ in res.items():
    for c, rows in cs_.items():
        print(v, c, " | ".join(f"{k.split('_',1)[0] if 'DIG' in k else k.split('/')[0][5:7]+k.split('/')[1][-2:]}:{(x.get('C') if 'C' in x else x.get('max')) if x else None:.4f}"
                               for k, x in sorted(rows.items()) if (x.get('C', x.get('max')) is not None)))
