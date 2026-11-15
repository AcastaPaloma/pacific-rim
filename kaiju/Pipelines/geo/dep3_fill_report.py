#!/usr/bin/env python
"""T-103: before/after report of the older-3DEP-project fill (placeholder terrain nodes of the config/dep3_fill.json tiles).

Before = evidence/T-102/placeholder_tiles.json (T-102 bake); after = the baked terrain layer manifests under the cache bake
dir. Also the vertical offset between the newest and each older project where both hold data (seam risk): median and p95 of
|newest - older| over the shared window cells. Writes evidence/T-103/fill_report.json and fill_summary.md.
Usage: KAIJU_GEO_CACHE=<integration cache> python -I Pipelines/geo/dep3_fill_report.py [--out evidence/T-103]
"""
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402

from kaiju_geo.common import CACHE, PROJECT, load_config, parse_tile, read_meta  # noqa: E402

BAKE = CACHE / "bake" / "tiles" / "v1"
NODES = 2049 * 2049


def layer(tid):
    m = json.loads((BAKE / parse_tile(tid).folder / "manifest.json").read_text())
    return [x for x in m["layers"] if x["layer"] == "terrain"][0]


def offsets(tid):
    """[(older item, newest item, n shared cells, median |d|, p95 |d|)] over the cached windows of one tile."""
    import rasterio
    files = sorted(CACHE.joinpath("3dep").glob(f"*__{tid}.tif"))
    wins = []
    for f in files:
        m = read_meta(f) or {}
        wins.append((m.get("published", ""), f.name.split("__")[0], f, m.get("fill_for")))
    out = []
    for pub, name, f, newer in wins:
        if not newer:
            continue
        new = [w for w in wins if w[1] == newer]
        if not new:
            continue
        with rasterio.open(f) as a, rasterio.open(new[0][2]) as b:
            if a.shape != b.shape:
                continue
            da, db = a.read(1).astype("f8"), b.read(1).astype("f8")
            ok = np.isfinite(da) & np.isfinite(db) & (da != a.nodata) & (db != b.nodata)
        if ok.any():
            d = np.abs(da[ok] - db[ok])
            out.append({"older": name, "newest": newer, "shared_cells": int(ok.sum()),
                        "median_abs_m": round(float(np.median(d)), 3), "p95_abs_m": round(float(np.percentile(d, 95)), 3)})
    return out


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    out = Path(argv[argv.index("--out") + 1]) if "--out" in argv else PROJECT / "evidence" / "T-103"
    before = json.loads((PROJECT / "evidence" / "T-102" / "placeholder_tiles.json").read_text())["tiles_with_placeholder_terrain"]
    tiles = load_config("dep3_fill.json")["tiles"]
    rows, offs = {}, []
    for t in tiles:
        lay = layer(t)
        b, a = before[t]["placeholder_nodes"], lay.get("placeholder_nodes", 0)
        rows[t] = {"class": before[t]["class"], "buildings": before[t]["buildings"], "before": b, "after": a,
                   "after_sea": lay.get("placeholder_nodes_sea", 0), "after_land": lay.get("placeholder_nodes_land", 0)}
        offs += [{"tile": t, **o} for o in offsets(t)]
    fixed = [t for t, r in rows.items() if r["after"] == 0]
    partial = [t for t, r in rows.items() if 0 < r["after"] < r["before"]]
    same = [t for t, r in rows.items() if r["after"] >= r["before"]]
    rem = {t: rows[t] for t in rows if rows[t]["after"] > 0}
    summ = {"tiles": len(tiles), "fixed_to_zero": len(fixed), "improved_not_zero": len(partial), "unchanged": len(same),
            "nodes_before": sum(r["before"] for r in rows.values()), "nodes_after": sum(r["after"] for r in rows.values()),
            "land_nodes_after": sum(r["after_land"] for r in rows.values()), "buildings_in_tiles_still_placeholder": sum(r["buildings"] for r in rem.values()),
            "tiles_still_entirely_placeholder": sum(1 for r in rem.values() if r["after"] >= NODES),
            "seam_offset_pairs": len(offs),
            "seam_median_abs_m_p50": round(float(np.median([o["median_abs_m"] for o in offs])), 3) if offs else None,
            "seam_p95_abs_m_max": max([o["p95_abs_m"] for o in offs], default=None)}
    out.mkdir(parents=True, exist_ok=True)
    (out / "fill_report.json").write_text(json.dumps({"summary": summ, "tiles": rows, "offsets": offs}, indent=1, sort_keys=True) + "\n")
    lines = ["# T-103 older-3DEP-project fill: summary", ""] + [f"- {k}: {v}" for k, v in summ.items()] + ["", "## Tiles still holding placeholder nodes", ""]
    lines += [f"- {t}: {r['class']}, {r['before']} -> {r['after']} nodes (land {r['after_land']}, sea {r['after_sea']}), {r['buildings']} buildings" for t, r in sorted(rem.items())]
    (out / "fill_summary.md").write_text("\n".join(lines) + "\n")
    print(json.dumps(summ, indent=1))


if __name__ == "__main__":
    main()
