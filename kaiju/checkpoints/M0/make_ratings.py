#!/usr/bin/env python
"""Turn a filled sheet CSV (capture, founder_a, founder_b, ..., notes) into the panel_ratings@v1 file calibrate reads.
usage: python make_ratings.py SHEET.csv --metric M-003 --out ratings.json [--key answer_key.json] [--build M0-packet]
Blank cells are skipped. --key (open only AFTER rating) attaches anchor polarity and, for M-003, the palette profile."""
import argparse, csv, json
ap = argparse.ArgumentParser(); ap.add_argument('sheet'); ap.add_argument('--metric', required=True)
ap.add_argument('--out', required=True); ap.add_argument('--key'); ap.add_argument('--build', default='M0-packet')
a = ap.parse_args()
key = json.load(open(a.key)) if a.key else {}
rows = list(csv.DictReader(open(a.sheet)))
raters = [c for c in rows[0].keys() if c not in ('capture', 'notes')]
out = {"schema": "panel_ratings@v1", "metric": a.metric,
       "raters": [{"rater_id": r, "rater_type": "human", "blind": True} for r in raters], "ratings": []}
for r in rows:
    k = key.get(r['capture'], {})
    for rid in raters:
        if (r[rid] or '').strip() == '': continue
        x = {"rater_id": rid, "build": a.build, "capture": r['capture'], "anchor": k.get('anchor'),
             "score": float(r[rid]), "notes": r.get('notes', '')}
        if a.metric == 'M-001': x["scenario"] = "overall"
        if a.metric == 'M-003' and k.get('profile'): x["profile"] = k['profile']
        out["ratings"].append(x)
json.dump(out, open(a.out, 'w'), indent=1)
print(f"wrote {a.out}: {len(out['ratings'])} ratings from {len(raters)} raters")
