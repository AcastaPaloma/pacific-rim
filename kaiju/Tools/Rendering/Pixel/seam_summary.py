#!/usr/bin/env python
"""T-192: collect the M-003 report.json files that `capture.py style` wrote for each configuration (<root>/<config>/<set>/m003/report.json)
into one table of A2 (palette adherence output / internal), c1 (drop, rho, span of the worst frame), c2, DIG and d-iv. Usage: seam_summary.py <style_root> [--out md]"""
import argparse, glob, json, os

IDS = ["M-003.a.output", "M-003.a.internal", "M-003.c1", "M-003.c2", "M-003.b.DIG", "M-003.d-iv"]


def worst(sub):
    return {p["name"].split(": ")[-1] if ": " in p["name"] else p["name"]: p for p in sub.get("parts", [])}


def row(rep):
    out = {}
    for s in rep["submeasures"]:
        if s["id"] not in IDS: continue
        parts = s.get("parts", []); out[s["id"]] = {"status": s["status"], "parts": [(p["name"], p.get("value"), p.get("threshold"), p.get("status")) for p in parts],
                                                   "eval": (s.get("diagnostics") or {}).get("frames_evaluated") if s["id"] == "M-003.c1" else None}
    return out


def _v2(report_path):
    # d-iv@v2 (T-196e: rule for layered frames): div_v2.json next to report.json, written by capture_tools.style.divv2_set
    try: return json.load(open(os.path.join(os.path.dirname(report_path), "div_v2.json"))).get("gate_value")
    except (OSError, ValueError): return None


def fmt(v):
    return "-" if v is None else ("%.3g" % v if isinstance(v, (int, float)) else str(v))


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("root"); ap.add_argument("--out"); a = ap.parse_args()
    lines = []; allrows = {}
    for rp in sorted(glob.glob(os.path.join(a.root, "*", "*", "m003", "report.json"))):
        cfg, st = rp.split(os.sep)[-4], rp.split(os.sep)[-3]
        rep = json.load(open(rp)); r = row(rep); r['_v2'] = _v2(rp); allrows.setdefault(st, {})[cfg] = r
    cfgs = sorted({c for v in allrows.values() for c in v})
    lines.append("| set | config | A2 out | A2 internal | c1 drop (<=2) | c1 rho (>=0.9) | c1 span (>=15) | frames c1 | c2 | DIG share (>=0.8) | d-iv@v1 (>=0.995) | d-iv@v2 (gate for layered frames; v1 reported) |")
    lines.append("|---|---|---|---|---|---|---|---|---|---|---|---|")
    for st, v in allrows.items():
        for cfg in cfgs:
            if cfg not in v: continue
            r = v[cfg]; g = lambda i, k=0: (r.get(i, {}).get("parts") or [(None, None, None, None)])
            c1 = {p[0].split(": ")[-1]: p for p in r.get("M-003.c1", {}).get("parts", [])}
            drop = next((p for k, p in c1.items() if "drop" in k), (0, None, 0, ""))[1]; rho = next((p for k, p in c1.items() if "rho" in k), (0, None, 0, ""))[1]; span = next((p for k, p in c1.items() if "span" in k), (0, None, 0, ""))[1]
            lines.append("| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |" % (st, cfg, fmt(g("M-003.a.output")[0][1]), fmt(g("M-003.a.internal")[0][1]), fmt(drop), fmt(rho), fmt(span),
                         fmt(r.get("M-003.c1", {}).get("eval")), r.get("M-003.c2", {}).get("status", "-") + (" " + fmt(g("M-003.c2")[0][1]) if r.get("M-003.c2", {}).get("parts") else ""),
                         fmt(g("M-003.b.DIG")[0][1]), fmt(g("M-003.d-iv")[0][1]), fmt(r.get("_v2"))))
    text = "\n".join(lines)
    print(text)
    if a.out: open(a.out, "w").write(text + "\n")


if __name__ == "__main__":
    main()
