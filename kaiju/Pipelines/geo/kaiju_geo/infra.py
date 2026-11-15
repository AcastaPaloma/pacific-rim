"""infra@v1: F-044 infrastructure targets (bridges, power, port cranes, airport assets) from OSM (T-105, R-100 section 5).

Pure functions plus the pyosmium extraction. The layer is a per-tile sidecar next to the geo_tile@v1 files
(`infrastructure.geojson` + `infrastructure.manifest.json`); the tile's own manifest is NOT edited, so no L0 tile and no LOD tile is
invalidated (lod.py fingerprints the L0 manifests). Tiles without features get no files.
"""
from __future__ import annotations

import json
import math
from pathlib import Path

import shapely
from shapely.geometry import LineString, Point, Polygon, mapping

from .common import BAYTM_ID, canonical_json, load_config, lonlat_to_baytm, sha256_bytes, tile_of

SPEC = "infra@v1"
FILE = "infrastructure.geojson"
MANIFEST_FILE = "infrastructure.manifest.json"
GRID_TILES = 68
KINDS = ("bridge", "power_tower", "power_line", "substation", "power_plant", "port_crane", "runway", "terminal", "hangar", "control_tower")
KEY_FILTER = ("bridge", "power", "man_made", "aeroway", "tower:type")


def kind_id(kind: str) -> int:
    return load_config("infra.json")["kinds"][kind]["id"]


# ---------------------------------------------------------------- classification
def _num(v):
    """First number of an OSM value ('12', '12 m', '12;14', '12.5')."""
    if v is None:
        return None
    s = str(v).replace(",", ".").split(";")[0].strip().split(" ")[0]
    try:
        x = float(s)
    except ValueError:
        return None
    return x if math.isfinite(x) else None


def length_m(v):
    """OSM length tag to metres (m, ft and ' " forms are not guessed: plain number = m, 'N ft' = ft)."""
    if v is None:
        return None
    s = str(v).strip().lower()
    x = _num(s)
    if x is None:
        return None
    return x * 0.3048 if s.endswith("ft") or s.endswith("'") else x


def voltage_v(v):
    """Highest voltage in a 'a;b;c' tag, volts; None when absent or not numeric."""
    if v is None:
        return None
    best = None
    for p in str(v).split(";"):
        try:
            x = float(p.strip())
        except ValueError:
            continue
        if x > 0 and (best is None or x > best):
            best = x
    return best


def classify(tags: dict, is_node: bool, closed: bool = False):
    """Kind of an OSM object (or None). `tags` is a plain dict."""
    g = tags.get
    if g("power") == "tower" and is_node:
        return "power_tower"
    if g("power") in ("line", "minor_line") and not is_node and g("location") not in ("underground", "underwater"):
        return "power_line"
    if g("power") == "substation":
        return "substation"
    if g("power") == "plant":
        return "power_plant"
    if g("man_made") == "crane":
        return "port_crane"
    a = g("aeroway")
    if a == "runway" and not is_node:
        return "runway"
    if a == "terminal" and not is_node:
        return "terminal"
    if a == "hangar" and not is_node:
        return "hangar"
    if a == "control_tower" or g("tower:type") == "air_traffic_control":
        return "control_tower"
    b = g("bridge")
    if b and b != "no" and not is_node and (g("highway") or g("railway")) and g("man_made") != "bridge":
        return "bridge"
    return None


def properties(kind: str, tags: dict, geom, bridge_slugs: dict | None = None, osm_id: str = "") -> dict:
    """Properties of one record: only values present in the OSM tags or measured on the geometry; names are dropped (T-144b)."""
    g = tags.get
    p = {"kind": kind, "kind_id": kind_id(kind), "osm": osm_id}
    if kind == "bridge":
        cls = g("highway") or g("railway")
        p.update({"class": cls, "carries": "highway" if g("highway") else "railway", "bridge_type": g("bridge"),
                  "structure": g("bridge:structure"), "movable": 1 if g("bridge:movable") else 0,
                  "layer": int(_num(g("layer")) or 0), "lanes": _num(g("lanes")), "width_m": length_m(g("width")),
                  "maxspan_m": length_m(g("bridge:span") or g("maxspan")), "length_m": round(geom.length, 3),
                  "footway": 1 if g("highway") in load_config("infra.json")["unpaved_footway_classes"] else 0,
                  "landmark": (bridge_slugs or {}).get(osm_id)})
    elif kind == "power_tower":
        p.update({"height_m": length_m(g("height")), "design": g("design"), "structure": g("structure"), "operator": None})
    elif kind == "power_line":
        p.update({"class": g("power"), "voltage_v": voltage_v(g("voltage")), "cables": _num(g("cables")),
                  "circuits": _num(g("circuits")), "length_m": round(geom.length, 3)})
    elif kind == "substation":
        p.update({"substation": g("substation"), "voltage_v": voltage_v(g("voltage")),
                  "area_m2": round(geom.area, 1) if geom.geom_type == "Polygon" else None})
    elif kind == "power_plant":
        p.update({"source": g("plant:source"), "output_w": g("plant:output:electricity"), "method": g("plant:method"),
                  "area_m2": round(geom.area, 1) if geom.geom_type == "Polygon" else None})
    elif kind == "port_crane":
        p.update({"crane_type": g("crane:type"), "height_m": length_m(g("height")), "port": 1 if g("industrial") == "port" or g("landuse") == "port" else 0})
    elif kind == "runway":
        p.update({"ref": g("ref"), "surface": g("surface"), "width_m": length_m(g("width")), "length_m": round(geom.length, 3)})
    elif kind in ("terminal", "hangar"):
        p.update({"height_m": length_m(g("height")), "levels": _num(g("building:levels")),
                  "area_m2": round(geom.area, 1) if geom.geom_type == "Polygon" else None})
    elif kind == "control_tower":
        p.update({"height_m": length_m(g("height"))})
    return p


# ---------------------------------------------------------------- geometry
def owner_point(geom):
    if geom.geom_type == "Point":
        return geom
    if geom.geom_type == "LineString":
        return geom.interpolate(0.5, normalized=True)
    return geom.point_on_surface()


def owner_tile(geom, level=0):
    p = owner_point(geom)
    t = tile_of(p.x, p.y, level)
    return t if 0 <= t.i < GRID_TILES and 0 <= t.j < GRID_TILES else None


def _round_coords(c, nd):
    if isinstance(c[0], (int, float)):
        return [round(v, nd) for v in c]
    return [_round_coords(x, nd) for x in c]


def to_geometry(kind: str, pts_xz: list, closed: bool):
    """BayTM geometry from projected points: area kinds become Polygons when closed with >= 4 points, otherwise lines."""
    if len(pts_xz) == 1:
        return Point(pts_xz[0])
    area = kind in ("substation", "power_plant", "terminal", "hangar", "control_tower", "port_crane")
    if area and closed and len(pts_xz) >= 4:
        poly = Polygon(pts_xz)
        if not poly.is_valid:
            poly = shapely.make_valid(poly)
            if poly.geom_type != "Polygon":
                parts = [q for q in getattr(poly, "geoms", []) if q.geom_type == "Polygon"]
                poly = max(parts, key=lambda q: q.area) if parts else None
        if poly is not None and poly.area > 0:
            return poly
    return LineString(pts_xz)


def feature(kind, osm_id, tags, geom, bridge_slugs=None):
    nd = load_config("infra.json")["round_mm"]
    gm = mapping(geom)
    gm = {"type": gm["type"], "coordinates": _round_coords(gm["coordinates"], nd)}
    props = properties(kind, tags, geom, bridge_slugs, osm_id)
    return {"type": "Feature", "id": f"{kind}:{osm_id}", "properties": props, "geometry": gm}


# ---------------------------------------------------------------- extraction (one pass over the cached PBF)
def extract(pbf: Path, bridge_slugs: dict | None = None, log=print) -> list[dict]:
    """All infra@v1 features of the PBF as BayTM features, sorted by id. Relations are ignored (as in R-100)."""
    import osmium
    fp = osmium.FileProcessor(str(pbf)).with_locations().with_filter(osmium.filter.KeyFilter(*KEY_FILTER))
    out, n_seen = [], 0
    for o in fp:
        if not (o.is_node() or o.is_way()):
            continue
        tags = {t.k: t.v for t in o.tags}
        is_node = o.is_node()
        kind = classify(tags, is_node)
        if kind is None:
            continue
        n_seen += 1
        try:
            if is_node:
                pts = [lonlat_to_baytm(o.location.lon, o.location.lat)]
            else:
                pts = [lonlat_to_baytm(n.lon, n.lat) for n in o.nodes]
        except Exception:  # noqa: BLE001  (a node without a location: way crosses the extract edge)
            continue
        if not is_node and len(pts) < 2:
            continue
        closed = (not is_node) and o.is_closed()
        geom = to_geometry(kind, pts, closed)
        if owner_tile(geom) is None:
            continue
        oid = ("n" if is_node else "w") + str(o.id)
        out.append(feature(kind, oid, tags, geom, bridge_slugs))
    out.sort(key=lambda f: f["id"])
    log(f"[infra] {n_seen} OSM objects classified, {len(out)} inside the tile grid")
    return out


def by_tile(feats: list[dict]) -> dict:
    """{tile_id: [feature,...]} (features sorted by id) by the owner rule."""
    res: dict = {}
    for f in feats:
        gm = f["geometry"]
        if gm["type"] == "Point":
            g = Point(gm["coordinates"])
        elif gm["type"] == "LineString":
            g = LineString(gm["coordinates"])
        else:
            g = Polygon(gm["coordinates"][0], gm["coordinates"][1:])
        t = owner_tile(g)
        if t is not None:
            res.setdefault(t.id, []).append(f)
    for v in res.values():
        v.sort(key=lambda f: f["id"])
    return res


# ---------------------------------------------------------------- tile files
def tile_files(tile_id: str, feats: list[dict], source_entry: dict, fingerprint: str) -> tuple[bytes, bytes]:
    """(infrastructure.geojson bytes, infrastructure.manifest.json bytes), deterministic."""
    counts: dict = {}
    for f in feats:
        k = f["properties"]["kind"]
        counts[k] = counts.get(k, 0) + 1
    fc = {"type": "FeatureCollection", "spec": SPEC,
          "crs": {"type": "name", "properties": {"name": BAYTM_ID, "wkt2": "crs/baytm_v1.wkt"}}, "features": feats}
    data = (canonical_json(fc) + "\n").encode("utf-8")
    man = {"spec": SPEC, "tile": tile_id, "file": FILE, "sha256": sha256_bytes(data), "count": len(feats),
           "kind_counts": dict(sorted(counts.items())), "inputs_sha256": fingerprint,
           "owner_rule": load_config("infra.json")["owner_rule"], "excluded": load_config("infra.json")["excluded"],
           "sources": [source_entry]}
    return data, (canonical_json(man, indent=1) + "\n").encode("utf-8")


def fingerprint(pbf_sha: str, slugs: dict) -> str:
    cfg = load_config("infra.json")
    code = sha256_bytes(Path(__file__).read_bytes())
    return sha256_bytes(canonical_json({"pbf": pbf_sha, "config": cfg, "code": code, "landmarks": slugs}).encode())
