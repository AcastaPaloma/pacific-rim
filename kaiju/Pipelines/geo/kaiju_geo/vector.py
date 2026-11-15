"""OSM / DataSF vector layers: buildings (with parts), roads, piers (R-004 §1.4, §1.5).

IDs come only from source ids: `osm:w<way id>` / `osm:r<relation id>` (Core BuildingId
`bld:osm-way:<id>` / `bld:osm-relation:<id>`). Output order is sorted by id, so results do not
depend on input order or thread count (C14).
"""
from __future__ import annotations

import math
import re

import numpy as np
import shapely
from shapely import wkb
from shapely.geometry import MultiPolygon, Polygon, mapping
from shapely.ops import transform as shp_transform

from .common import baytm_crs, transformer

FT = 0.3048


# ---------------------------------------------------------------- tag parsing
def parse_length_m(v):
    """OSM length value -> metres, or None. Accepts '326', '326 m', '12.5m', "100'", "10'6\"", '~20'."""
    if v is None:
        return None
    s = str(v).strip().replace(",", ".").lstrip("~").strip()
    m = re.fullmatch(r"(-?\d+(?:\.\d+)?)\s*(m|meters?|metres?)?", s, flags=re.I)
    if m:
        return float(m.group(1))
    m = re.fullmatch(r"(\d+(?:\.\d+)?)\s*(?:'|ft|feet)\s*(?:(\d+(?:\.\d+)?)\s*(?:\"|in))?", s, flags=re.I)
    if m:
        return float(m.group(1)) * FT + (float(m.group(2)) * 0.0254 if m.group(2) else 0.0)
    return None


def parse_number(v):
    if v is None:
        return None
    try:
        x = float(str(v).strip().replace(",", "."))
        return x if math.isfinite(x) else None
    except ValueError:
        return None


# ---------------------------------------------------------------- reading OSM
def _to_baytm_geom(g):
    t = transformer("EPSG:6318", baytm_crs())
    return shp_transform(lambda x, y, z=None: t.transform(x, y), g)


def round_geom(g, nd=3):
    return shapely.set_precision(g, 10 ** -nd, mode="valid_output")


def clean_polygonal(g):
    """Valid (Multi)Polygon in BayTM at mm precision, or None."""
    if g is None or g.is_empty:
        return None
    if not g.is_valid:
        g = shapely.make_valid(g)
    g = round_geom(g)
    polys = [p for p in getattr(g, "geoms", [g]) if isinstance(p, Polygon) and p.area > 0.05]
    if not polys:
        sub = [q for p in getattr(g, "geoms", [g]) if hasattr(p, "geoms") for q in p.geoms if isinstance(q, Polygon)]
        polys = [p for p in sub if p.area > 0.05]
    if not polys:
        return None
    polys = [shapely.geometry.polygon.orient(p, 1.0) for p in polys]  # CCW outer (x east, z north)
    polys.sort(key=lambda p: (round(p.bounds[0], 3), round(p.bounds[1], 3), -p.area))
    return polys[0] if len(polys) == 1 else MultiPolygon(polys)


def read_osm(path):
    """Return dict with lists of buildings, parts, roads, piers (all BayTM shapely geometries)."""
    import osmium
    fab = osmium.geom.WKBFactory()
    buildings, parts, roads, piers = [], [], [], []
    for o in osmium.FileProcessor(str(path)).with_areas():
        tags = {t.k: t.v for t in o.tags}
        if not tags:
            continue
        if o.is_area():
            b = tags.get("building")
            bp = tags.get("building:part")
            mm = tags.get("man_made")
            if not (b or bp or mm == "pier"):
                continue
            try:
                g = wkb.loads(fab.create_multipolygon(o), hex=True)
            except Exception:  # noqa: BLE001 (broken multipolygon in source)
                continue
            src = "w" if o.from_way() else "r"
            rec = {"oid": f"osm:{src}{o.orig_id()}", "tags": tags, "geom": _to_baytm_geom(g)}
            if bp and bp != "no":
                parts.append(rec)
            elif b and b != "no":
                buildings.append(rec)
            if mm == "pier":
                piers.append(rec)
        elif o.is_way() and "highway" in tags:
            try:
                g = wkb.loads(fab.create_linestring(o), hex=True)
            except Exception:  # noqa: BLE001
                continue
            roads.append({"oid": f"osm:w{o.id}", "tags": tags, "geom": _to_baytm_geom(g)})
        elif o.is_way() and tags.get("man_made") == "pier" and not o.is_closed():
            try:
                g = wkb.loads(fab.create_linestring(o), hex=True)
            except Exception:  # noqa: BLE001
                continue
            w = parse_length_m(tags.get("width")) or 6.0
            piers.append({"oid": f"osm:w{o.id}", "tags": tags, "geom": _to_baytm_geom(g).buffer(w / 2, cap_style="flat")})
    for lst in (buildings, parts, roads, piers):
        lst.sort(key=lambda r: oid_key(r["oid"]))
    return {"buildings": buildings, "parts": parts, "roads": roads, "piers": piers}


def oid_key(oid: str):
    """Deterministic sort key: ways before relations, then numeric id."""
    kind = oid[4]
    return (0 if kind == "w" else 1, int(oid[5:]))


def core_building_id(oid: str) -> str:
    """geo_tile bid -> Core BuildingId canonical string (Assets/Game/Core/MODULE.md, IDs v1)."""
    return ("bld:osm-way:" if oid[4] == "w" else "bld:osm-relation:") + oid[5:]


# ---------------------------------------------------------------- DataSF
def read_datasf(path):
    import json
    j = json.load(open(path, encoding="utf-8"))
    out = []
    for f in j["features"]:
        p = f["properties"]
        g = shapely.geometry.shape(f["geometry"])
        g = clean_polygonal(_to_baytm_geom(g))
        if g is None:
            continue
        out.append({"id": p.get("sf16_bldgid"), "geom": g,
                    "hgt_median_m": parse_number(p.get("hgt_median_m")),
                    "peak_1st_m": parse_number(p.get("peak_1st_m")),
                    "gnd_min_m": parse_number(p.get("gnd_min_m"))})
    out.sort(key=lambda r: str(r["id"]))
    return out


# ---------------------------------------------------------------- Assessor attributes (optional, additive)
def read_assessor(path, cfg):
    """Parcel centroids of the pinned roll as [{x, z (BayTM), year_built, stories, struct_class}] (None = unknown)."""
    import json
    rows = json.load(open(path, encoding="utf-8"))
    acfg = cfg["assessor"]
    t = transformer("EPSG:6318", baytm_crs())
    out = []
    for r in rows:
        geom = r.get("the_geom") or {}
        c = geom.get("coordinates")
        if geom.get("type") != "Point" or not c or len(c) < 2:
            continue
        x, z = t.transform(float(c[0]), float(c[1]))
        yb = parse_number(r.get("year_property_built"))
        yb = int(yb) if yb is not None and yb == int(yb) and acfg["min_year_built"] <= yb <= int(r.get("closed_roll_year") or 9999) else None
        st = parse_number(r.get("number_of_stories"))
        st = None if st is None or not (0 < st <= acfg["max_stories"]) else (int(st) if st == int(st) else round(st, 1))
        sc = (r.get("construction_type") or "").strip() or None
        if yb is None and st is None and sc is None:
            continue
        out.append({"x": x, "z": z, "year_built": yb, "stories": st, "struct_class": sc})
    return out


def _mode(vals, prefer_high=False):
    vals = [v for v in vals if v is not None]
    if not vals:
        return None
    n = {}
    for v in vals:
        n[v] = n.get(v, 0) + 1
    top = max(n.values())
    c = sorted(v for v, k in n.items() if k == top)
    return c[-1] if prefer_high else c[0]


def join_assessor(items, points):
    """items: [(key, polygon)]; points: read_assessor output. Each point goes to the smallest footprint containing it
    (ties by key). Per footprint: year_built = most common (ties earlier), stories = most common (ties taller),
    struct_class = most common raw code (ties lexicographic). Returns {key: {only the known fields}}."""
    if not items or not points:
        return {}
    tree = shapely.STRtree([p for _, p in items])
    pts = shapely.points([(p["x"], p["z"]) for p in points])
    hit_pt, hit_g = tree.query(pts, predicate="within")
    best = {}
    for pi, gi in zip(hit_pt.tolist(), hit_g.tolist()):
        k = (items[gi][1].area, str(items[gi][0]))
        if pi not in best or k < best[pi][0]:
            best[pi] = (k, gi)
    groups = {}
    for pi, (_, gi) in best.items():
        groups.setdefault(gi, []).append(points[pi])
    res = {}
    for gi, ps in groups.items():
        a = {"year_built": _mode([p["year_built"] for p in ps]),
             "stories": _mode([p["stories"] for p in ps], prefer_high=True),
             "struct_class": _mode([p["struct_class"] for p in ps])}
        a = {k: v for k, v in a.items() if v is not None}
        if a:
            res[items[gi][0]] = a
    return res


def iou(a, b) -> float:
    inter = a.intersection(b).area
    if inter <= 0:
        return 0.0
    return inter / (a.area + b.area - inter)


# ---------------------------------------------------------------- heights (R-004 §1.4 fallback order)
def resolve_height(tags, geom, datasf_tree, datasf, cfg):
    """Returns (height_m, height_source, extra). Order: OSM height -> DataSF (IoU > 0.6 and
    peak_1st - gnd_min > 3 m) -> building:levels x 4.22 m -> default 3 levels (12.66 m)."""
    h = parse_length_m(tags.get("height"))
    if h is not None and h > 0:
        return h, "osm_height", {}
    best, best_iou = None, 0.0
    if datasf_tree is not None:
        for k in datasf_tree.query(geom):
            d = datasf[int(k)]
            v = iou(geom, d["geom"])
            if v > best_iou:
                best, best_iou = d, v
    if (best is not None and best_iou > cfg["datasf_min_iou"] and best["hgt_median_m"] is not None
            and best["peak_1st_m"] is not None and best["gnd_min_m"] is not None
            and best["peak_1st_m"] - best["gnd_min_m"] > cfg["datasf_min_relief_m"] and best["hgt_median_m"] > 0):
        return best["hgt_median_m"], "datasf", {"datasf_id": best["id"], "datasf_iou": round(best_iou, 3)}
    lv = parse_number(tags.get("building:levels"))
    if lv is not None and lv > 0:
        return lv * cfg["metres_per_level"], "osm_levels", {}
    return cfg["default_levels"] * cfg["metres_per_level"], "default", {}


def resolve_min_height(tags, cfg):
    mh = parse_length_m(tags.get("min_height"))
    if mh is not None and mh >= 0:
        return mh
    ml = parse_number(tags.get("building:min_level"))
    if ml is not None and ml > 0:
        return ml * cfg["metres_per_level"]
    return 0.0


def polygon_area(g) -> float:
    return float(g.area)


def geom_to_coords(g):
    """GeoJSON coordinates (x, z) rounded to mm."""
    return mapping(g)


def road_width(tags, cfg):
    w = parse_length_m(tags.get("width"))
    if w is not None and w > 0:
        return w
    lanes = parse_number(tags.get("lanes"))
    if lanes is not None and lanes > 0:
        return lanes * cfg["metres_per_lane"]
    return cfg["class_width_m"].get(tags.get("highway"), cfg["class_width_m"]["_default"])


def as_int_bool(v) -> int:
    return 0 if v in (None, "no", "0", "false") else 1


def layer_of(tags) -> int:
    v = parse_number(tags.get("layer"))
    return int(v) if v is not None else 0


def sample_points_in_polygon(g, spacing=1.0):
    """Deterministic interior sample grid (node lattice) plus all vertices."""
    minx, miny, maxx, maxy = g.bounds
    xs = np.arange(math.floor(minx), math.ceil(maxx) + 1, spacing)
    zs = np.arange(math.floor(miny), math.ceil(maxy) + 1, spacing)
    pts = []
    if len(xs) * len(zs) <= 400000:
        X, Z = np.meshgrid(xs, zs)
        inside = shapely.contains_xy(g, X.ravel(), Z.ravel())
        pts = list(zip(X.ravel()[inside], Z.ravel()[inside]))
    for p in getattr(g, "geoms", [g]):
        pts += list(p.exterior.coords)
        for r in p.interiors:
            pts += list(r.coords)
    a = np.asarray(pts, dtype=np.float64)
    return a[:, 0], a[:, 1]


def exclusion_reason(tags):
    """Features that are not surface buildings (DEF-T003-bay-1): kept as records, never extruded."""
    if tags.get("building") == "ship":
        return "ship"
    if tags.get("location") in ("underground", "underwater"):
        return "underground"
    lay = parse_number(tags.get("layer"))
    lvl = parse_number(tags.get("level"))
    above = (parse_number(tags.get("building:levels")) or 0) > 0 or (parse_length_m(tags.get("height")) or 0) > 0
    # layer < 0 with underground levels marks a below-ground structure, unless the source also gives it above-ground
    # levels or a height (Salesforce Transit Center: layer -1, 2 underground + 5 levels, height 30 -> a surface building)
    if lay is not None and lay < 0 and not above and (tags.get("building:levels:underground") is not None or (lvl is not None and lvl < 0)):
        return "underground"
    return None


def ring_points(g, step=1.0):
    """Points every `step` metres along every exterior ring (deterministic)."""
    xs, zs = [], []
    for p in getattr(g, "geoms", [g]):
        ring = p.exterior
        n = max(int(math.ceil(ring.length / step)), 4)
        for k in range(n):
            q = ring.interpolate(k * ring.length / n)
            xs.append(q.x); zs.append(q.y)
    return np.asarray(xs), np.asarray(zs)
