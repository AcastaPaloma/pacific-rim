"""T-104: measure a bridge from first-return lidar along an OSM centreline (generalises T-023 measure_ggb.py, which was
hand-tuned to the Golden Gate). Pure numpy/scipy on point arrays, so it is testable with synthetic clouds.

deck_profile: along the centreline in `bin_m` bins, deck = p25 of first returns within +-half_width_m that are at least
`min_h` above the datum (vehicles and rails add heights above the deck, water/low returns are dropped), n = point count.
towers: 1 m max grid of returns higher than deck_ref + min_above; a (3x3) opening removes cables and rails; connected
components of at least min_cells cells within `reach_m` of the centreline are towers (top = max, centroid, extent)."""
from __future__ import annotations

import math

import numpy as np
from scipy import ndimage


def polyline_len(pts):
    return float(sum(math.dist(a, b) for a, b in zip(pts[:-1], pts[1:])))


def locate(pts, x, z):
    """(u, v) of points (x, z) against polyline pts: u arclength of the nearest point, v signed lateral distance (left +)."""
    pts = np.asarray(pts, float)
    x, z = np.asarray(x, float), np.asarray(z, float)
    seg = pts[1:] - pts[:-1]
    sl = np.hypot(seg[:, 0], seg[:, 1])
    cum = np.concatenate([[0.0], np.cumsum(sl)])
    best = np.full(x.shape, np.inf)
    u = np.zeros(x.shape)
    v = np.zeros(x.shape)
    for i in range(len(seg)):
        if sl[i] == 0:
            continue
        d = seg[i] / sl[i]
        rx, rz = x - pts[i, 0], z - pts[i, 1]
        t = np.clip(rx * d[0] + rz * d[1], 0, sl[i])
        px, pz = pts[i, 0] + t * d[0], pts[i, 1] + t * d[1]
        dist = np.hypot(x - px, z - pz)
        k = dist < best
        best = np.where(k, dist, best)
        u = np.where(k, cum[i] + t, u)
        v = np.where(k, -d[1] * (x - px) + d[0] * (z - pz), v)
    return u, v


def deck_profile(pts, pc, half_width_m=8.0, bin_m=20.0, min_h=3.0, min_n=12):
    L = polyline_len(pts)
    u, v = locate(pts, pc["x"], pc["z"])
    ok = (np.abs(v) <= half_width_m) & (pc["h"] >= min_h) & (u > 0) & (u < L)
    uu, hh = u[ok], pc["h"][ok]
    rows = []
    for a in np.arange(0.0, L, bin_m):
        k = (uu >= a) & (uu < a + bin_m)
        n = int(k.sum())
        rows.append({"u": round(a + bin_m / 2, 1), "deck": round(float(np.percentile(hh[k], 25)), 2) if n >= min_n else None, "n": n})
    return rows


def towers(pts, pc, deck_ref, min_above=25.0, reach_m=90.0, min_cells=12, cell=1.0):
    """Components of high cells near the centreline. Returns a list sorted along the line."""
    u, v = locate(pts, pc["x"], pc["z"])
    ok = (np.abs(v) <= reach_m) & (pc["h"] >= deck_ref + min_above)
    if not ok.any():
        return []
    x, z, h = pc["x"][ok], pc["z"][ok], pc["h"][ok]
    x0, z0 = x.min() - 2, z.min() - 2
    ix, iz = ((x - x0) / cell).astype(int), ((z - z0) / cell).astype(int)
    g = np.full((iz.max() + 3, ix.max() + 3), -np.inf)
    np.maximum.at(g, (iz, ix), h)
    mask = ndimage.binary_opening(g > -np.inf, structure=np.ones((3, 3)))
    lab, n = ndimage.label(mask, structure=np.ones((3, 3)))
    out = []
    for k in range(1, n + 1):
        m = lab == k
        if m.sum() < min_cells:
            continue
        zz, xx = np.nonzero(m)
        cx, cz = x0 + (xx.mean() + 0.5) * cell, z0 + (zz.mean() + 0.5) * cell
        uc, vc = locate(pts, [cx], [cz])
        out.append({"centroid_baytm": [round(float(cx), 2), round(float(cz), 2)], "top_max_m": round(float(g[m].max()), 2),
                    "top_p99_m": round(float(np.percentile(g[m], 99)), 2), "n_cells": int(m.sum()),
                    "extent_m": [round(float(xx.max() - xx.min() + 1) * cell, 1), round(float(zz.max() - zz.min() + 1) * cell, 1)],
                    "u_m": round(float(uc[0]), 1), "v_m": round(float(vc[0]), 1)})
    return sorted(out, key=lambda r: r["u_m"])


def corridor_windows(pts, half_m=100.0, step_m=600.0):
    """Axis-aligned BayTM windows covering the polyline +- half_m, cut every ~step_m of arclength."""
    L = polyline_len(pts)
    P = np.asarray(pts, float)
    seg = np.hypot(*(P[1:] - P[:-1]).T)
    cum = np.concatenate([[0.0], np.cumsum(seg)])
    wins = []
    for a in np.arange(0.0, L, step_m):
        b = min(a + step_m, L)
        xs = np.interp([a, b], cum, P[:, 0])
        zs = np.interp([a, b], cum, P[:, 1])
        sel = (cum > a) & (cum < b)
        xs = np.concatenate([xs, P[sel, 0]])
        zs = np.concatenate([zs, P[sel, 1]])
        wins.append((float(xs.min() - half_m), float(zs.min() - half_m), float(xs.max() + half_m), float(zs.max() + half_m)))
    return wins


def group_towers(comps, within_m=40.0):
    """Merge tower components whose centroids are within `within_m` (the two legs of one tower, portal pieces) into one tower:
    position = mean centroid, top = max, legs = n components. Sorted along the line."""
    comps = sorted(comps, key=lambda c: -c["top_max_m"])
    groups = []
    for c in comps:
        for g in groups:
            if math.dist(c["centroid_baytm"], g["centroid_baytm"]) <= within_m:
                g["legs"].append(c)
                n = len(g["legs"])
                g["centroid_baytm"] = [round(sum(l["centroid_baytm"][i] for l in g["legs"]) / n, 2) for i in (0, 1)]
                g["top_max_m"] = max(g["top_max_m"], c["top_max_m"])
                break
        else:
            groups.append({"centroid_baytm": list(c["centroid_baytm"]), "top_max_m": c["top_max_m"], "legs": [c]})
    for g in groups:
        g["n_legs"] = len(g["legs"])
        g["extent_m"] = g["legs"][0]["extent_m"]
        g["u_m"] = g["legs"][0]["u_m"]
    return sorted(groups, key=lambda g: g["u_m"])
