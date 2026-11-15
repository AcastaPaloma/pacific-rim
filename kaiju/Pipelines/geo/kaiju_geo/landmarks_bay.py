"""T-104 (F-041 Bay-wide landmarks): OSM resolution, lidar measurement and assembly of config/landmarks_bay.json from the
hand-curated, fully sourced config/landmarks_bay_registry.json. Pure functions here (tested offline); the network/cache driver is
bake_landmarks_bay.py. Positions BayTM float64; heights NAVD88 m. Nothing is invented: an official figure that lidar contradicts
beyond tolerance is reported with both values and the lidar value is used (confidence B)."""
from __future__ import annotations

import math
import statistics

import numpy as np

from . import bridge_measure as bm
from .common import lonlat_to_baytm

M_PER_FT = 0.3048
TOL_ABS_M, TOL_REL = 2.0, 0.01


def height_tol(h: float) -> float:
    """R-100 section 4 rule: hand-built height within max(2 m, 1 %) of the lidar-measured height."""
    return max(TOL_ABS_M, TOL_REL * abs(h))


def pick_height(official, measured):
    """-> (value used, source, confidence, delta). Official kept when lidar agrees within tolerance, else lidar wins."""
    if measured is None:
        return official, "official", "B", None
    d = round(measured - official, 2)
    if abs(d) <= height_tol(official):
        return official, "official", "A", d
    return measured, "lidar_3dep", "B", d


def bbox_baytm(ll):
    x0, z0 = lonlat_to_baytm(ll[0], ll[1])
    x1, z1 = lonlat_to_baytm(ll[2], ll[3])
    return min(x0, x1), min(z0, z1), max(x0, x1), max(z0, z1)


def in_box(x, z, b):
    return b[0] <= x <= b[2] and b[1] <= z <= b[3]


def way_length(pts):
    return bm.polyline_len(pts)


def _des(ref):
    return frozenset(x.strip().lstrip("0") for x in str(ref or "").replace(";", "/").split("/") if x.strip())


def runway_length(ways):
    """Length of a runway that OSM may split into several ways: the farthest pair of node positions (runways are straight)."""
    pts = [p for w in ways for p in w["pts"]]
    best = 0.0
    for i, a in enumerate(pts):
        for b in pts[i + 1:]:
            best = max(best, math.dist(a, b))
    return best


def runway_report(osm_runways, faa_ft):
    """Match OSM runway ways to FAA designators by `ref` (order and `;` insensitive, pieces of one runway joined); lengths in m,
    delta vs FAA (OSM paved length vs declared length)."""
    out = []
    for des, ft in faa_ft.items():
        ways = [w for w in osm_runways if _des(w["tags"].get("ref")) == _des(des)]
        faa_m = round(ft * M_PER_FT, 2)
        rec = {"ref": des, "faa_length_m": faa_m, "osm_ways": [w["id"] for w in ways], "osm_length_m": None, "delta_m": None}
        if ways:
            L = round(runway_length(ways), 2)
            rec.update(osm_length_m=L, delta_m=round(L - faa_m, 2))
        out.append(rec)
    return out


def point_in_poly(poly, x, z):
    """Vectorised even-odd test; poly = [(x, z)...] closed or not."""
    P = np.asarray(poly, float)
    x, z = np.asarray(x, float), np.asarray(z, float)
    inside = np.zeros(x.shape, bool)
    n = len(P)
    j = n - 1
    for i in range(n):
        xi, zi, xj, zj = P[i, 0], P[i, 1], P[j, 0], P[j, 1]
        cond = (zi > z) != (zj > z)
        with np.errstate(divide="ignore", invalid="ignore"):
            xint = (xj - xi) * (z - zi) / (zj - zi) + xi
        inside ^= cond & (x < xint)
        j = i
    return inside


def building_heights(poly, pc, ring_m=25.0):
    """Top = p99 of first returns inside the footprint (robust to a single antenna/bird); ground = p10 of returns in a ring
    around the footprint's bounding circle. -> dict or None when no points."""
    P = np.asarray(poly, float)
    inside = point_in_poly(P, pc["x"], pc["z"])
    if inside.sum() < 20:
        return None
    cx, cz = P[:, 0].mean(), P[:, 1].mean()
    r = float(np.hypot(P[:, 0] - cx, P[:, 1] - cz).max())
    d = np.hypot(pc["x"] - cx, pc["z"] - cz)
    ring = (d > r) & (d <= r + ring_m) & ~inside
    ground = float(np.percentile(pc["h"][ring], 10)) if ring.sum() >= 20 else None
    top99, topmax = float(np.percentile(pc["h"][inside], 99)), float(pc["h"][inside].max())
    return {"top_p99_navd88_m": round(top99, 2), "top_max_navd88_m": round(topmax, 2), "ground_ring_p10_navd88_m": None if ground is None else round(ground, 2),
            "height_p99_m": None if ground is None else round(top99 - ground, 2), "n_inside": int(inside.sum())}


def peak_near(pc, c, r, r_ground):
    """Highest first return within r of c, and the local surface = median of returns within r_ground of c that are within 3 m of that
    median's first estimate (drops the structure itself). -> dict with peak, ground and height above ground."""
    d = np.hypot(pc["x"] - c[0], pc["z"] - c[1])
    near = d <= r
    i = int(np.argmax(np.where(near, pc["h"], -np.inf)))
    g = pc["h"][d <= r_ground]
    m0 = float(np.median(g))
    ground = float(np.median(g[g <= m0 + 3.0]))
    return {"peak_navd88_m": round(float(pc["h"][i]), 2), "peak_baytm": [round(float(pc["x"][i]), 2), round(float(pc["z"][i]), 2)],
            "ground_navd88_m": round(ground, 2), "height_m": round(float(pc["h"][i]) - ground, 2)}


def crane_clusters(cranes, gap_m=300.0):
    """Single-link clusters of crane positions (greedy union by distance); -> list of lists of indices."""
    n = len(cranes)
    par = list(range(n))

    def f(i):
        while par[i] != i:
            par[i] = par[par[i]]
            i = par[i]
        return i
    for i in range(n):
        for j in range(i + 1, n):
            if math.dist(cranes[i]["pos"], cranes[j]["pos"]) <= gap_m:
                par[f(i)] = f(j)
    g = {}
    for i in range(n):
        g.setdefault(f(i), []).append(i)
    return sorted(g.values(), key=lambda v: (cranes[v[0]]["pos"][0], cranes[v[0]]["pos"][1]))


def summarise_deck(rows):
    d = [r["deck"] for r in rows if r["deck"] is not None]
    if not d:
        return None
    return {"n_bins": len(d), "deck_median_navd88_m": round(statistics.median(d), 2), "deck_min_navd88_m": min(d), "deck_max_navd88_m": max(d)}


def decimate(profile, keep=40.0):
    """Keep every bin whose u is at least `keep` m from the previous kept bin (committed profile stays small)."""
    out, last = [], None
    for r in profile:
        if r["deck"] is None:
            continue
        if last is None or r["u"] - last >= keep:
            out.append({"u": r["u"], "deck": r["deck"]})
            last = r["u"]
    return out


def check_rows(doc):
    """M-005.1 rows for every landmark of the document: [{slug, name, pos_baytm, tol_pos_m, height_ref_m|None, height_tol_m|None, status}]
    (consumed by m005_check; cranes 50 m, rest 5 m)."""
    rows = []
    for b in doc["bridges"]:
        for t in b.get("towers", []):
            rows.append({"slug": t["slug"], "name": t["name"], "pos_baytm": t["pos_baytm"], "tol_pos_m": 5.0,
                         "height_ref_m": t["top_navd88_m"], "height_tol_m": height_tol(t["top_navd88_m"]), "status": t["height_status"]})
    for x in doc["buildings"]:
        if x.get("pos_baytm"):
            rows.append({"slug": x["slug"], "name": x["name"], "pos_baytm": x["pos_baytm"], "tol_pos_m": 5.0,
                         "height_ref_m": x.get("height_m"), "height_tol_m": height_tol(x["height_m"]) if x.get("height_m") else None, "status": x["status"]})
    for k in ("alcatraz",):
        a = doc.get(k)
        if a and a.get("lighthouse", {}).get("pos_baytm"):
            rows.append({"slug": "alcatraz_lighthouse", "name": "Alcatraz lighthouse", "pos_baytm": a["lighthouse"]["pos_baytm"], "tol_pos_m": 5.0,
                         "height_ref_m": a["lighthouse"]["height_m"], "height_tol_m": height_tol(a["lighthouse"]["height_m"]), "status": a["lighthouse"]["status"]})
    for ap in doc["airports"]:
        if ap.get("tower", {}).get("pos_baytm"):
            rows.append({"slug": f"{ap['slug']}_tower", "name": f"{ap['name']} control tower", "pos_baytm": ap["tower"]["pos_baytm"], "tol_pos_m": 5.0,
                         "height_ref_m": ap["tower"].get("height_m"), "height_tol_m": None, "status": ap["tower"]["status"]})
    for cl in doc["cranes"].get("clusters", []):
        rows.append({"slug": cl["slug"], "name": cl["name"], "pos_baytm": cl["centroid_baytm"], "tol_pos_m": doc["cranes"]["pos_tol_m"],
                     "height_ref_m": None, "height_tol_m": None, "status": "position only (cranes move)"})
    return rows
