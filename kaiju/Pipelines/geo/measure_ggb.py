"""T-023 step 1: measure the Golden Gate Bridge from the 3DEP first-return grids (rind_v11 max_10, 1 m cells, row 0 = south).
Writes kaiju/evidence/T-023/ggb_lidar_measure.json (full report) and config/ggb.json (compact measured geometry that
kaiju_geo/landmarks.py turns into features; the bake never reads the lidar). Run: python -I measure_ggb.py"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from scipy import ndimage

HERE = Path(__file__).resolve().parent
EVID = HERE.parent.parent / "evidence" / "T-023" / "ggb_lidar_measure.json"
CFG = HERE / "config" / "ggb.json"
TILES = ["26_37", "26_38", "27_37"]
CENT_S, CENT_N = np.array([55248.0, 76752.0]), np.array([55132.3, 78027.3])
SRC = {"tower_top": 229.18, "span": 1280.2, "deck_tower": 76.78, "deck_mid": 68.85, "width": 18.9, "cable_d": 0.924,
       "leg_base": [10.1, 16.5], "side_span": 342.9}
TOWER_MIN_M, TOWER_R, BIN = 150.0, 60.0, 20.0


def load():
    g = []
    for t in TILES:
        z = np.load(HERE / "cache" / "lidar" / f"rind_v11__L0_{t}.npz")
        g.append((z["bounds_baytm"], z["max_10"]))
    return g


GRIDS = load()


def sample(x, z):
    """Nearest 1 m cell max_10 at BayTM points (NaN outside the three tiles or where there are no returns)."""
    x, z = np.asarray(x, float), np.asarray(z, float)
    out = np.full(x.shape, np.nan)
    for b, m in GRIDS:
        k = (x >= b[0]) & (x < b[2]) & (z >= b[1]) & (z < b[3])
        out[k] = m[np.floor(z[k] - b[1]).astype(int), np.floor(x[k] - b[0]).astype(int)]
    return out


def frame(s, n):
    d = (n - s) / np.linalg.norm(n - s)
    return d, np.array([-d[1], d[0]])


def pt(c, d, nrm, u, v):
    return c[0] + u * d[0] + v * nrm[0], c[1] + u * d[1] + v * nrm[1]


def find_cables(c0, d, nrm, length):
    """v offsets (from the centroid axis) of the two cable lines: peaks of the median first-return along the main span."""
    us = np.arange(200.0, length - 200.0, 2.0)
    vs = np.arange(-30.0, 30.01, 0.5)
    prof = []
    for v in vs:
        x, z = pt(c0, d, nrm, us, v)
        prof.append(np.nanmedian(np.where(sample(x, z) > 55, sample(x, z), np.nan)))
    prof = np.array(prof)
    order = np.argsort(-np.nan_to_num(prof, nan=-1))
    peaks = []
    for i in order:
        if all(abs(vs[i] - p) > 10 for p in peaks):
            peaks.append(vs[i])
        if len(peaks) == 2:
            break
    return sorted(peaks), prof, vs


def legs_of(c, d, nrm, vmid=None):
    """cells > 150 m within 60 m of the centroid, resampled at 1 m in the axis frame (u along, v across); a 9 (u) x 5 (v)
    opening removes the 3-cell cable strips and the 7-row portal struts, leaving the legs; the two largest components are the
    legs. Extents are oriented (u, v) cell spans + 1 m (>150 m level, so they under-state the tapered base)."""
    r = int(TOWER_R)
    uu, vv = np.meshgrid(np.arange(-r, r + 1) + 0.0, np.arange(-r, r + 1) + 0.0, indexing="ij")  # [iu, iv]
    gx, gz = c[0] + uu * d[0] + vv * nrm[0], c[1] + uu * d[1] + vv * nrm[1]
    h = sample(gx, gz)
    mask = (np.nan_to_num(h, nan=0) > TOWER_MIN_M) & (uu ** 2 + vv ** 2 <= TOWER_R ** 2)
    mask = ndimage.binary_opening(mask, structure=np.ones((9, 5)))
    lab, n = ndimage.label(mask, structure=np.ones((3, 3)))
    sizes = ndimage.sum(mask, lab, range(1, n + 1))
    legs = []
    for k in sorted(np.argsort(-sizes)[:2] + 1, key=lambda k: float(vv[lab == k].mean())):
        m = lab == k
        legs.append({"n_cells": int(m.sum()), "top_max_m": round(float(np.nanmax(h[m])), 2),
                     "centroid_baytm": [round(float(gx[m].mean()), 2), round(float(gz[m].mean()), 2)],
                     "extent_along_m": round(float(uu[m].max() - uu[m].min() + 1), 1), "extent_across_m": round(float(vv[m].max() - vv[m].min() + 1), 1),
                     "u_range": [float(uu[m].min()), float(uu[m].max())], "v_range": [float(vv[m].min()), float(vv[m].max())],
                     "centre_uv": [round(float(uu[m].mean()), 2), round(float(vv[m].mean()), 2)]})
    return legs


def main():
    d0, n0 = frame(CENT_S, CENT_N)
    L0 = float(np.linalg.norm(CENT_N - CENT_S))
    (cvn, cvp), prof, vs = find_cables(CENT_S, d0, n0, L0)
    vmid = (cvn + cvp) / 2
    tw = {}
    for nm, c in (("S", CENT_S), ("N", CENT_N)):
        lg = legs_of(c, d0, n0, vmid)
        tw[nm] = {"legs": lg, "top_max_m": max(l["top_max_m"] for l in lg),
                  "leg_spacing_centre_to_centre_m": round(abs(lg[1]["centre_uv"][1] - lg[0]["centre_uv"][1]), 2)}
    # measured tower centres (mean of the two leg centres) and axis
    cen = {}
    for nm, c in (("S", CENT_S), ("N", CENT_N)):
        cen[nm] = np.array([np.mean([l["centroid_baytm"][i] for l in tw[nm]["legs"]]) for i in (0, 1)])
    d, nrm = frame(cen["S"], cen["N"])
    L = float(np.linalg.norm(cen["N"] - cen["S"]))
    vc = (cvn + cvp) / 2
    # deck and cable profile along the measured axis; v offsets re-expressed in the new frame
    def to_new(c, v):  # point on old line -> v in new frame (about cen S)
        p = np.array(pt(c, d0, n0, 0, v))
        return float((p - cen["S"]) @ nrm)
    cv = [to_new(CENT_S, cvn), to_new(CENT_S, cvp)]
    vc = float(np.mean(cv))
    side = SRC["side_span"] + 0.4
    u0 = -(side // BIN + 1) * BIN  # a bin edge at or beyond the sourced end of the side span
    edges = np.arange(u0, L + side + BIN, BIN)
    rows = []
    for a, b in zip(edges[:-1], edges[1:]):
        uu = np.arange(a + 0.5, b, 1.0)
        dk, ck = [], []
        for v in np.arange(vc - 9, vc + 9.01, 1.0):
            x, z = pt(cen["S"], d, nrm, uu, v)
            h = sample(x, z)
            dk.append(h[(h >= 55) & (h <= 95)])
        dk = np.concatenate(dk)
        sw = []
        for v in list(np.arange(vc - 22, vc - 15.9, 1.0)) + list(np.arange(vc + 16, vc + 22.1, 1.0)):
            x, z = pt(cen["S"], d, nrm, uu, v)
            h = sample(x, z)
            sw.append(h[(h >= 55) & (h <= 95)])
        sw = np.concatenate(sw)
        cab = []
        for cvv in cv:
            best = np.nan
            for v in np.arange(cvv - 1.5, cvv + 1.51, 0.5):
                x, z = pt(cen["S"], d, nrm, uu, v)
                h = sample(x, z)
                if np.isfinite(h).any():
                    best = np.nanmax([best, np.nanmax(h)]) if np.isfinite(best) else np.nanmax(h)
            cab.append(best)
        rows.append({"u_mid_m": round(float((a + b) / 2), 1), "deck_median_m": None if len(dk) < 30 else round(float(np.median(dk)), 2),
                     "deck_n": int(len(dk)), "outer_strip_median_m": None if len(sw) < 30 else round(float(np.median(sw)), 2), "cable_max_m": [None if not np.isfinite(c) else round(float(c), 2) for c in cab]})
    def nearest(u):
        return min(rows, key=lambda r: abs(r["u_mid_m"] - u))
    mid = nearest(L / 2)
    dt = [nearest(0), nearest(L)]
    gaps = [None if (c is None or mid["deck_median_m"] is None) else round(c - mid["deck_median_m"], 2) for c in mid["cable_max_m"]]
    tops = [tw["S"]["top_max_m"], tw["N"]["top_max_m"]]
    rep = {"spec": "ggb_lidar_measure@v1", "grid": "rind_v11 max_10 (first return, 1 m, NAVD88 m), tiles " + ",".join(TILES),
           "tower_centroids_osm_baytm": {"S": CENT_S.tolist(), "N": CENT_N.tolist()},
           "tower_centres_measured_baytm": {k: [round(float(x), 2) for x in v] for k, v in cen.items()},
           "axis_unit": [round(float(x), 5) for x in d], "cable_v_offsets_from_axis_m": [round(x, 2) for x in cv], "deck_centre_v_m": round(vc, 2),
           "towers": tw,
           "span_measured_m": round(L, 2), "span_osm_centroids_m": round(L0, 2),
           "profile_bin_m": BIN, "profile": rows,
           "deck_at_towers_m": [nearest(0)["deck_median_m"], nearest(L)["deck_median_m"]], "deck_midspan_m": mid["deck_median_m"],
           "cable_midspan_m": mid["cable_max_m"], "midspan_cable_minus_deck_m": gaps,
           "sourced": SRC,
           "delta": {"tower_top_S": round(tops[0] - SRC["tower_top"], 2), "tower_top_N": round(tops[1] - SRC["tower_top"], 2),
                     "span": round(L - SRC["span"], 2),
                     "deck_tower_S": None if dt[0]["deck_median_m"] is None else round(dt[0]["deck_median_m"] - SRC["deck_tower"], 2),
                     "deck_tower_N": None if dt[1]["deck_median_m"] is None else round(dt[1]["deck_median_m"] - SRC["deck_tower"], 2),
                     "deck_mid": None if mid["deck_median_m"] is None else round(mid["deck_median_m"] - SRC["deck_mid"], 2),
                     "leg_spacing_vs_none_sourced": "leg spacing is not sourced; measured value reported"},
           "q_new_d": "R-020 Q-NEW-d: measured midspan cable-minus-deck gap given in midspan_cable_minus_deck_m (sourced cable sag implied about 16 m)"}
    EVID.parent.mkdir(parents=True, exist_ok=True)
    EVID.write_text(json.dumps(rep, indent=1) + "\n")
    cfg = {"_doc": "T-023 measured GGB geometry (output of measure_ggb.py; the bake never reads lidar). u along the axis from the S tower centre toward the N tower, v across (BayTM x,z = centre + u*axis + v*normal).",
           "axis_origin_baytm": [round(float(x), 3) for x in cen["S"]], "axis_unit": [round(float(x), 6) for x in d],
           "span_m": round(L, 3), "cable_v_m": [round(x, 3) for x in cv], "deck_v_m": round(vc, 3),
           "legs": [{"tower": nm, "leg": i, "centroid_baytm": l["centroid_baytm"], "extent_along_m": l["extent_along_m"],
                     "extent_across_m": l["extent_across_m"], "centre_uv": l["centre_uv"], "top_max_m": l["top_max_m"]}
                    for nm in ("S", "N") for i, l in enumerate(tw[nm]["legs"])],
           "profile": [{"u": r["u_mid_m"], "deck": r["deck_median_m"], "cable": r["cable_max_m"]} for r in rows]}
    CFG.write_text(json.dumps(cfg, indent=1) + "\n")
    print(json.dumps({k: rep[k] for k in ("tower_centres_measured_baytm", "cable_v_offsets_from_axis_m", "deck_centre_v_m", "span_measured_m",
                                          "deck_at_towers_m", "deck_midspan_m", "cable_midspan_m", "midspan_cable_minus_deck_m", "delta")}, indent=1))
    print(json.dumps(tw, indent=1))


if __name__ == "__main__":
    main()
