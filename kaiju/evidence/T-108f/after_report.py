"""T-108f: per-tile after table for the 109 flagged L1 tiles (reads the baked manifests)."""
import json, os, sys, statistics
from pathlib import Path
root = Path(os.environ["KAIJU_GEO_CACHE"]) / "bake/tiles/v1/L2"
here = Path(__file__).parent
rows = []
for t in (here / "tiles.txt").read_text().split():
    m = json.loads((root / t[3:] / "manifest.json").read_text())
    f = m["errors"]["buildings"]["forms"]["b1"]
    rows.append({"tile": t, "gen": m["generator"], "tris": f["triangles"], "cap": f["tri_cap"], "within_cap": f["within_cap"],
                 "fit": f.get("footprint_fit"), "scale": f.get("error_scale"), "max_m": f["footprint_max_m"], "rms_m": f["footprint_rms_m"],
                 "height_max_m": f.get("height_max_m"), "cell_m": f.get("surface_cell_m"), "switch": f.get("switch_m102"),
                 "gpu_mb": m["budget"]["gpu_mb_est"], "gpu_budget": m["budget"]["gpu_mb_budget_per_tile"], "gpu_ok": m["budget"]["gpu_within_budget"]})
fits = {}
for r in rows: fits[r["fit"]] = fits.get(r["fit"], 0) + 1
sk = [r for r in rows if r["fit"] == "skyline_fallback"]
ex = [r for r in rows if r["fit"] == "merged_blocks_extended"]
out = {"tiles": len(rows), "over_cap_after": sum(not r["within_cap"] for r in rows), "fit_classes": fits,
       "generators": sorted({r["gen"] for r in rows}),
       "extended_scale_hist": {str(s): sum(r["scale"] == s for r in ex) for s in sorted({r["scale"] for r in ex})},
       "extended_max_m": [min(r["max_m"] for r in ex), statistics.median(r["max_m"] for r in ex), max(r["max_m"] for r in ex)] if ex else None,
       "extended_rms_m": [min(r["rms_m"] for r in ex), statistics.median(r["rms_m"] for r in ex), max(r["rms_m"] for r in ex)] if ex else None,
       "gpu_over_budget": sum(r["gpu_ok"] is False for r in rows), "skyline_tiles": sk, "rows": rows}
(here / "after_tiles.json").write_text(json.dumps(out, indent=1))
print({k: v for k, v in out.items() if k not in ("rows", "skyline_tiles")}); print("skyline switch distances:", [(r["tile"], r["cell_m"], r["max_m"], r["rms_m"], r["switch"]) for r in sk])
