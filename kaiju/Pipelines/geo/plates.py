#!/usr/bin/env python
"""Reported plates for T-003 (founders look at these first): height relations with an 80 m mech silhouette,
wading depth map + histogram, fault map with distances. Reads the committed tile and the Unity export.

Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/plates.py [--tile L0_30_36]
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import matplotlib  # noqa: E402

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402

from kaiju_geo.common import GLOBAL_ROOT, PROJECT, TILES_ROOT, parse_tile  # noqa: E402

EVID = PROJECT / "evidence" / "T-003"
MECH_H = 80.0  # D-007: 76-88 m


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--tile", default="L0_30_36")
    a = ap.parse_args(argv)
    tile = parse_tile(a.tile)
    out = EVID / "plates"
    out.mkdir(parents=True, exist_ok=True)
    rep = json.loads((EVID / "m005_report.json").read_text())
    import rasterio
    from PIL import Image
    d = TILES_ROOT / tile.folder
    hs = np.flipud(rasterio.open(d / "terrain.tif").read(1).astype(np.float64))
    water = np.flipud(np.array(Image.open(d / "water.png")) >= 128)
    msl = json.loads((d / "manifest.json").read_text())["water_level_navd88_m"]

    # ---- 1. height relations (side elevation, true scale, NAVD88)
    lms = rep["v1"]["m005"]["M-005.1"]["landmarks"]
    fig, ax = plt.subplots(figsize=(13, 6.5))
    ax.axhspan(-40, msl, color="#9fb8c8", alpha=0.5, lw=0)
    ax.axhline(msl, color="#5b7f99", lw=1)
    x = 0
    for l in sorted(lms, key=lambda l: -(l["top_navd88_m"] or 0)):
        base, top = l["base_navd88_m"], l["top_navd88_m"]
        ax.add_patch(plt.Rectangle((x, base), 40, top - base, color="#8c8c8c" if "Salesforce" not in l["name"] else "#555"))
        ax.text(x + 20, top + 4, f"{l['name']}\n{top - base:.0f} m above base\n(top {top:.0f} m NAVD88)", ha="center", va="bottom", fontsize=7.5)
        x += 70
    # mech wading off the Ferry Building: stands on the seabed at V4
    v4 = next(v for v in json.loads((GLOBAL_ROOT / "landmarks.json").read_text())["viewpoints"] if v["id"] == "V4")
    i, j = int(v4["baytm"][0] - tile.x0), int(v4["baytm"][1] - tile.z0)
    bed = hs[j, i]
    mx = x + 10
    ax.add_patch(plt.Rectangle((mx, bed), 16, MECH_H * 0.86, color="#b5482f"))
    ax.add_patch(plt.Circle((mx + 8, bed + MECH_H * 0.93), MECH_H * 0.07, color="#b5482f"))
    ax.text(mx + 8, bed + MECH_H + 4, f"80 m mech\nstanding on the seabed\nat V4 ({bed:.1f} m): head at {bed + MECH_H:.0f} m", ha="center", fontsize=7.5, color="#b5482f")
    ax.set_xlim(-20, mx + 60)
    ax.set_ylim(-40, 360)
    ax.set_xticks([])
    ax.set_ylabel("m NAVD88 (1:1, no exaggeration)")
    ax.set_title(f"{tile.id}: as-built landmark heights vs an 80 m mech (Unity export, M-005.1 table)")
    fig.tight_layout()
    fig.savefig(out / "height_relations_mech.png", dpi=110)
    plt.close(fig)

    # ---- 2. wading depth map + histogram
    cell = 0.25 * (hs[:-1, :-1] + hs[1:, :-1] + hs[:-1, 1:] + hs[1:, 1:])
    depth = np.where(water, msl - cell, np.nan)
    w = rep["t003"]["wading"]
    fig, (a1, a2) = plt.subplots(1, 2, figsize=(14, 6.5), gridspec_kw={"width_ratios": [1.2, 1]})
    im = a1.imshow(depth, origin="lower", extent=[tile.x0, tile.x0 + 2048, tile.z0, tile.z0 + 2048], cmap="viridis_r", vmin=0, vmax=32)
    a1.imshow(np.where(water, np.nan, cell), origin="lower", extent=[tile.x0, tile.x0 + 2048, tile.z0, tile.z0 + 2048], cmap="Greys", alpha=0.35)
    a1.plot(*w["deepest_baytm"], "rx", ms=10)
    a1.annotate(f"deepest {w['max_depth_m']} m", w["deepest_baytm"], xytext=(10, 10), textcoords="offset points", color="r")
    plt.colorbar(im, ax=a1, label="water depth below MSL (+0.969 m NAVD88), m")
    a1.set_title("Wading depth (BayTM, north up)")
    a1.set_xlabel("BayTM x (m)"); a1.set_ylabel("BayTM z (m)")
    dv = depth[np.isfinite(depth)]
    a2.hist(dv, bins=np.arange(0, 33, 1), color="#3d7ea6")
    a2.axvline(MECH_H * 0.5, color="#b5482f", ls="--", label="80 m mech: hip height (~40 m)")
    a2.set_xlabel("depth (m)"); a2.set_ylabel("water cells (1 m^2)")
    bands = ", ".join(f"{k} m: {v * 100:.1f}%" for k, v in w["share_by_depth_m"].items())
    a2.set_title(f"Depth histogram, median {w['median_depth_m']} m, max {w['max_depth_m']} m\n{bands}", fontsize=9)
    a2.legend()
    fig.tight_layout()
    fig.savefig(out / "wading_depth.png", dpi=100)
    plt.close(fig)

    # ---- 3. fault map with distances
    g = json.loads((GLOBAL_ROOT / "faults_bay.geojson").read_text())
    fc = json.loads((d / "faults.geojson").read_text())
    fig, ax = plt.subplots(figsize=(10, 10))
    for f in g["features"]:
        geom = f["geometry"]
        lines = [geom["coordinates"]] if geom["type"] == "LineString" else geom["coordinates"]
        nm = f["properties"]["fault_name"] or ""
        col = "#c0392b" if any(k in nm for k in ("San Andreas", "Hayward", "Golden Gate")) else "#7f8c8d"
        for ln in lines:
            c = np.asarray(ln)
            ax.plot(c[:, 0], c[:, 1], color=col, lw=0.7)
    x0, z0 = tile.x0, tile.z0
    ax.add_patch(plt.Rectangle((x0, z0), 2048, 2048, fill=False, color="b", lw=2))
    ax.text(x0 + 1024, z0 - 900, f"{tile.id} (M0)\n0 fault pieces: empty by design", ha="center", color="b")
    for k, n in enumerate(fc["nearest_faults"][:4]):
        ax.text(x0 + 2600, z0 + 2048 - k * 1500, f"{n['fault_name']} {n['section'] or ''}: {n['distance_m'] / 1000:.1f} km", fontsize=9)
    ax.set_xlim(x0 - 25000, x0 + 25000); ax.set_ylim(z0 - 25000, z0 + 25000)
    ax.set_aspect("equal")
    ax.set_title("USGS Qfaults (2020) baked to BayTM: faults_bay.geojson (3232 pieces), nearest traces to the M0 tile")
    ax.set_xlabel("BayTM x (m)"); ax.set_ylabel("BayTM z (m)")
    fig.tight_layout()
    fig.savefig(out / "fault_map.png", dpi=90)
    plt.close(fig)
    print("plates ->", out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
