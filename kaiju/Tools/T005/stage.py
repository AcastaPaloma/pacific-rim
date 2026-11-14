#!/usr/bin/env python
"""One progress-table row per gait log (fix-cycle staging): steady speed, Froude, slip (T-002 m002 on the single capture), peak foot force, peak and mean positive joint power.
Usage: python Tools/T005/stage.py <T-002 kaiju dir> <label> <gait log> [...]   (kaiju conda env)"""
import gzip, json, math, os, re, subprocess, sys, tempfile

def rows(path):
    op = gzip.open if path.endswith(".gz") else open
    with op(path, "rt") as f: return [json.loads(l) for l in f if l.strip()]

t2, label = sys.argv[1], sys.argv[2]
for path in sys.argv[3:]:
    g = rows(path); h = g[0]; st = [r for r in g if r["rec"] == "step"]; mg = h["mass_kg"] * h["g_mps2"]
    fall = next((r["t"] for r in st if r["ctl"]["phase"] in ("Falling", "Fallen")), None)
    end = fall - 2 if fall else st[-1]["t"]; a = [r for r in st if end - 20 <= r["t"] <= end]
    v = math.hypot(a[-1]["com_p"][0] - a[0]["com_p"][0], a[-1]["com_p"][2] - a[0]["com_p"][2]) / max(1e-9, a[-1]["t"] - a[0]["t"]) if len(a) > 1 else 0
    fr = v * v / (h["g_mps2"] * h["leg_length_m"])
    late = [r for r in st if 25 <= r["t"] <= end] or st
    peak = max(max(f["fn"] for f in r["feet"]) for r in late) / mg
    pw = [r["p_loco_W"] for r in late]
    b = [r["t"] for r in g if r["rec"] == "event" and r["type"] == "contact_begin" and r["foot"] == h["foot_ids"][0] and 25 <= r["t"] <= end]
    strides = [y - x for x, y in zip(b, b[1:]) if y - x > 1.0]
    slip = "n/a"
    try:
        out = tempfile.mkdtemp()
        p = subprocess.run([sys.executable, "Tools/Metrics/metrics.py", "m002", "--no-images", "--out", out, os.path.abspath(path)], cwd=t2, capture_output=True, text=True)
        m1 = re.search(r"M-002\.1\s+([\d.eE+-]+)", p.stdout); m2 = re.search(r"M-002\.2\s+([\d.eE+-]+)", p.stdout)
        L = h["feet"][0]["length_m"]
        slip = "max %.2f m, p95 %.2f m" % (float(m1.group(1)) * L, float(m2.group(1)) * L) if m1 and m2 else "tool exit %d" % p.returncode
    except Exception as e: slip = "error %s" % e
    print("| %s | %s | %s | %.2f | %.3f | %.1f | %s | %.2f | %.0f | %.0f |" % (label, os.path.basename(path).split(".")[0], "fell %.0f s" % fall if fall else "no fall", v, fr, sum(strides) / len(strides) if strides else float("nan"), slip, peak, max(pw) / 1e6, sum(pw) / len(pw) / 1e6))
