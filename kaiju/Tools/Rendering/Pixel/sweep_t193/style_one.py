#!/usr/bin/env python3
"""T-193: M-003 on every <root>/<variant>/<config>/p3_a... set of a round-2 sweep (one frame each); prints DIG_max, c1 span/rho/drop, e."""
import glob, json, os, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__)); K = os.path.abspath(os.path.join(HERE, "..", "..", "..", "..")); root = sys.argv[1]
rows = []
for vdir in sorted(x for x in glob.glob(os.path.join(root, "*", "*")) if not x.endswith("_style")):
    out = vdir + "_style"
    if not os.path.isdir(os.path.join(out, "p3_a_golden_gate_v3")):
        subprocess.run(["/opt/miniconda3/envs/kaiju/bin/python", "capture.py", "style", "--set", vdir, "--out", out], cwd=os.path.join(K, "Pipelines", "capture"), stdout=subprocess.DEVNULL)
    rp = glob.glob(os.path.join(out, "*", "m003", "report.json"))
    if not rp: rows.append((vdir, "no report")); continue
    d = json.load(open(rp[0])); f = d["extra"]["frames"]; dep = f[0]["depth"]; c = f[0]["composition"]
    rows.append((os.path.relpath(vdir, root), f"DIG_max {max(x['DIG'] for x in f):.4f}  c1 span {dep.get('span')} rho {dep.get('rho')} drop {dep.get('max_drop')}  sky {c['sky_share']:.3f} hor {c['horizon']:.3f} con {c['contrast']:.1f}  levels {f[0]['levels']}"))
for r in rows: print(*r)
