#!/usr/bin/env python
"""T-102b: building-budget report over the baked geo_lod pyramid (every tile manifest under the bake cache).

Lists every B1/B2 form that is over its triangle cap, with exact numbers: triangles, cap, footprint tolerance used / nominal,
measured footprint max and rms error, the error budget, the fit class (nominal | adaptive | over_cap_error_budget) and the
triangle floor of the tolerance ladder (what the largest rung tried left). Writes JSON; prints the counts.
Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/lod_budget_report.py [--root DIR] [--out FILE] [--before FILE]
"""
import argparse
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo.common import CACHE, canonical_json  # noqa: E402


def scan(root: Path) -> dict:
    rows, counts, fit, n = [], {}, {}, 0
    for man_path in sorted(root.glob("L[1-4]/*/manifest.json")):
        m = json.loads(man_path.read_text())
        n += 1
        lvl = f"L{m['level']}"
        for form, f in m["errors"]["buildings"]["forms"].items():
            key = f"{lvl}_{form}"
            counts.setdefault(key, {"tiles": 0, "over_cap": 0})
            counts[key]["tiles"] += 1
            fit[f.get("footprint_fit", "nominal")] = fit.get(f.get("footprint_fit", "nominal"), 0) + 1
            if f["within_cap"]:
                continue
            counts[key]["over_cap"] += 1
            lad = f.get("footprint_ladder") or []
            bud = f.get("footprint_error_budget_m") or {}
            floor = min((r["prism_triangles"] for r in lad), default=None)
            rows.append({"tile": m["tile"], "form": form, "triangles": f["triangles"], "cap": f["tri_cap"], "over_by": f["triangles"] - f["tri_cap"],
                         "buildings": f["buildings"], "tol_m": f["footprint_tol_m"], "tol_nominal_m": f.get("footprint_tol_nominal_m", f["footprint_tol_m"]),
                         "err_max_m": f["footprint_max_m"], "err_rms_m": f["footprint_rms_m"],
                         "budget_max_m": bud.get("max"), "budget_rms_m": bud.get("rms"), "fit": f.get("footprint_fit", "nominal"),
                         "ladder_floor_prism_triangles": floor, "ladder_rungs": len(lad),
                         "ladder_last_tol_m": lad[-1]["tol_m"] if lad else None, "ladder_last_err_max_m": lad[-1]["err_max_m"] if lad else None})
    rows.sort(key=lambda r: (r["tile"], r["form"]))
    return {"tiles": n, "counts": counts, "fit_classes": fit, "over_cap_total": len(rows), "over_cap": rows}


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", type=Path, default=CACHE / "bake" / "tiles" / "v1")
    ap.add_argument("--out", type=Path, default=None)
    ap.add_argument("--before", type=Path, default=None, help="evidence/T-102/lod_over_cap.json for the before counts")
    a = ap.parse_args(argv)
    doc = scan(a.root)
    if a.before and a.before.exists():
        b = json.loads(a.before.read_text())["over_cap"]
        c: dict = {}
        for tile, form, tris, cap, tol in b:
            k = f"L{tile[1]}_{form}"
            c[k] = c.get(k, 0) + 1
        doc["before_over_cap"] = {"total": len(b), "by_level_form": c,
                                  "by_tile_form": {f"{t}/{f}": {"triangles": tr, "cap": cp, "tol_m": tl} for t, f, tr, cp, tl in b}}
    print(json.dumps({k: doc[k] for k in ("tiles", "counts", "fit_classes", "over_cap_total")}, indent=1))
    if "before_over_cap" in doc:
        print("before:", doc["before_over_cap"]["total"], doc["before_over_cap"]["by_level_form"])
    if a.out:
        a.out.parent.mkdir(parents=True, exist_ok=True)
        a.out.write_text(canonical_json(doc, indent=1) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
