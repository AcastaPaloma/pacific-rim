#!/usr/bin/env python
"""T-115c: side-by-side clause table of several M-007@v2 report.json files (first = reference). Usage: m007_compare.py label=report.json ..."""
import json, sys

def load(arg):
    label, path = arg.split("=", 1)
    r = json.load(open(path))
    m = {}
    for c in r["clauses"]:
        m[(c["clause"], "status")] = c["status"]
        for x in c.get("measures", []):
            m[(c["clause"], x["name"])] = (x["value"], x["op"], x["threshold"])
    return label, m

def fmt(v):
    if isinstance(v, str): return v
    if v is None: return "-"
    return "%.4f" % v[0]

def main(argv):
    runs = [load(a) for a in argv]
    keys = []
    for _, m in runs:
        for k in m:
            if k not in keys: keys.append(k)
    print("%-6s %-42s %-12s" % ("clause", "measure", "threshold") + "".join("%-14s" % l for l, _ in runs))
    for k in keys:
        thr = ""
        for _, m in runs:
            if k in m and not isinstance(m[k], str): thr = "%s %g" % (m[k][1], m[k][2]); break
        print("%-6s %-42s %-12s" % (k[0], k[1], thr) + "".join("%-14s" % (fmt(m.get(k)) if k in m else "-") for _, m in runs))

if __name__ == "__main__":
    main(sys.argv[1:])
