#!/usr/bin/env python
"""T-113c: one-line-per-clause view of M-007 reports (status, headline measure, failing compositions). Usage: m007_brief.py label=report.json ..."""
import json, sys

KEY = {"C1": "fr_commit", "C2": "fe_vs_nofocus", "P1": "member_fit_time_min_over_species_x_comp", "P4": "bs_rate_minus_k_nobait",
       "P5": "kx_ratio_vs_noroles", "P6": "participation_share", "I3": "ival_per_kaiju_hour_ratio"}


def brief(path):
    r = json.load(open(path))
    out = {}
    for c in r["clauses"]:
        v = next((m["value"] for m in c.get("measures", []) if m["name"] == KEY.get(c["clause"])), None)
        bad = c.get("composition_fail") or ""
        out[c["clause"]] = (c["status"], v, bad)
    return out


def main(a):
    runs = [(x.split("=")[0], brief(x.split("=", 1)[1])) for x in a]
    print("%-4s" % "" + "".join("%-34s" % l for l, _ in runs))
    for k in runs[0][1]:
        if k in ("C3", "C4", "C5", "C6", "I2", "I4", "F1", "P2", "P3"): continue
        row = "%-4s" % k
        for _, m in runs:
            s, v, b = m.get(k, ("-", None, ""))
            row += "%-34s" % ("%s %s %s" % (s, "-" if v is None else "%.3f" % v, (",".join(b) if isinstance(b, list) else str(b))[:16]))
        print(row)

if __name__ == "__main__":
    main(sys.argv[1:])
