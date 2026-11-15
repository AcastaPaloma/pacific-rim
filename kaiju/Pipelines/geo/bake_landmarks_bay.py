#!/usr/bin/env python
"""T-104: F-041 Bay-wide landmarks. Resolves OSM geometry (tile pbf cache), measures bridges/buildings/cranes/airports from 3DEP
first-return lidar (EPT windows), and writes config/landmarks_bay.json (+ evidence/T-104/landmarks_bay_measure.json).
Usage: KAIJU_GEO_CACHE=<integration cache> /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/bake_landmarks_bay.py [--only slug,...] [--offline]
Stages are cached (OSM resolution in CACHE/landmarks_bay/osm.json, lidar windows in CACHE/lidar_win), so a re-run is offline."""
from __future__ import annotations

import argparse
import glob
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402

from kaiju_geo import bridge_measure as bm  # noqa: E402
from kaiju_geo import landmarks_bay as lb  # noqa: E402
from kaiju_geo import lidar_win  # noqa: E402
from kaiju_geo.common import CACHE, CONFIG, PROJECT, apply_std, canonical_json, lonlat_to_baytm, std_args  # noqa: E402

OUT = CONFIG / "landmarks_bay.json"
EVID = PROJECT / "evidence" / "T-104" / "landmarks_bay_measure.json"
OSMC = CACHE / "landmarks_bay" / "osm.json"


# ------------------------------------------------------------------ OSM
def resolve_osm(reg):
    import osmium
    bridge_ids = {w for b in reg["bridges"] for w in b["osm"]["ways"]}
    bld_ids = {int(b["osm_id"][1:]) for b in reg["buildings"] if b.get("osm_id")}
    bld_names = {}
    tower_ids = {int(a["tower_osm"][1:]) for a in reg["airports"] if a.get("tower_osm")}
    lh = int(reg["alcatraz"]["osm_lighthouse"][1:])
    oak_box = lb.bbox_baytm([-122.29, 37.79, -122.25, 37.82])
    crane_box = lb.bbox_baytm(reg["cranes"]["bbox_lonlat"])
    air = {a["slug"]: lb.bbox_baytm(a["bbox_lonlat"]) for a in reg["airports"]}
    alc_box = lb.bbox_baytm([-122.43, 37.82, -122.41, 37.835])
    found = {"ways": {}, "cranes": [], "runways": {}, "terminals": {}, "buildings_by_name": {}, "alcatraz": {}}

    def keep(store, key, rec):
        old = store.get(key)
        if old is None or len(rec["pts"]) > len(old["pts"]):
            store[key] = rec

    class H(osmium.SimpleHandler):
        def way(self, w):
            t = {k: v for k, v in w.tags}
            wid = w.id
            want = wid in bridge_ids or wid in bld_ids or wid in tower_ids or wid == lh
            iscrane = t.get("man_made") == "crane"
            isrun = t.get("aeroway") in ("runway", "terminal")
            named = t.get("name") in bld_names and "building" in t
            alc = t.get("place") == "island" and t.get("name") == "Alcatraz Island"
            if not (want or iscrane or isrun or named or alc):
                return
            try:
                pts = [lonlat_to_baytm(n.lon, n.lat) for n in w.nodes]
            except Exception:  # noqa: BLE001
                return
            if not pts:
                return
            c = (sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts))
            rec = {"id": f"w{wid}", "tags": {k: t[k] for k in ("name", "ref", "highway", "railway", "bridge", "man_made", "aeroway", "building", "height", "layer", "place") if k in t},
                   "pts": [[round(p[0], 2), round(p[1], 2)] for p in pts], "c": [round(c[0], 2), round(c[1], 2)]}
            if want:
                keep(found["ways"], rec["id"], rec)
            if iscrane and lb.in_box(c[0], c[1], crane_box):
                keep(found["ways"], rec["id"], {**rec, "_crane": 1})
            for slug, bx in air.items():
                if isrun and lb.in_box(c[0], c[1], bx):
                    keep(found["runways"] if t["aeroway"] == "runway" else found["terminals"], f"{slug}:{rec['id']}", {**rec, "airport": slug})
            if named and lb.in_box(c[0], c[1], oak_box):
                keep(found["buildings_by_name"], rec["id"], {**rec, "slug": bld_names[t["name"]]})
            if alc and lb.in_box(c[0], c[1], alc_box):
                keep(found["alcatraz"], rec["id"], rec)

        def node(self, n):
            t = n.tags
            if t.get("man_made") == "crane" and n.location.valid():
                x, z = lonlat_to_baytm(n.location.lon, n.location.lat)
                if lb.in_box(x, z, crane_box):
                    found["cranes"].append({"id": f"n{n.id}", "pos": [round(x, 2), round(z, 2)], "tags": {k: v for k, v in t if k in ("name", "height", "crane:type")}})

    h = H()
    for f in sorted(glob.glob(str(CACHE / "osm" / "tiles" / "*.pbf"))):
        h.apply_file(f, locations=True)
    seen = set()
    cr = []
    for c in found["cranes"]:
        if c["id"] not in seen:
            seen.add(c["id"])
            cr.append(c)
    for wid, w in found["ways"].items():
        if w.get("_crane"):
            cr.append({"id": wid, "pos": w["c"], "tags": w["tags"]})
    found["cranes"] = sorted(cr, key=lambda c: c["id"])
    found["ways"] = {k: v for k, v in found["ways"].items() if not v.get("_crane")}
    return found


def osm_cached(reg, offline):
    if OSM_CACHE_OK():
        return json.loads(OSMC.read_text())
    if offline:
        raise SystemExit("osm cache missing and --offline")
    f = resolve_osm(reg)
    OSMC.parent.mkdir(parents=True, exist_ok=True)
    OSMC.write_text(canonical_json(f) + "\n")
    return f


def OSM_CACHE_OK():
    return OSMC.exists()


# ------------------------------------------------------------------ lidar helpers
def read_chain(projects, b, resolution=None):
    for p in projects:
        pc = lidar_win.read_window(p, b, resolution)
        if len(pc["h"]) > 50:
            return p, pc
    return None, {k: np.zeros(0) for k in ("x", "z", "h", "n_return", "cls")}


def measure_bridge(rb, osm):
    ways = []
    for wid in rb["osm"]["ways"]:
        w = osm["ways"].get(f"w{wid}")
        if w is None:
            ways.append({"id": f"w{wid}", "status": "not in OSM extract"})
            continue
        pts = [tuple(p) for p in w["pts"]]
        prof, used = [], set()
        L = bm.polyline_len(pts)
        for win in bm.corridor_windows(pts, 100.0, 600.0):
            rows = None
            for p in rb["lidar"]:  # first project that has the bin wins; later projects only fill bins that stay empty
                pc = lidar_win.read_window(p, win, 0.7)
                if len(pc["h"]) < 50:
                    continue
                r2 = bm.deck_profile(pts, pc)
                if rows is None:
                    rows = r2
                    if any(r["deck"] is not None for r in rows):
                        used.add(p)
                else:
                    filled = False
                    for r, q in zip(rows, r2):
                        if r["deck"] is None and q["deck"] is not None:
                            r.update(q)
                            filled = True
                    if filled:
                        used.add(p)
                if rows and all(r["deck"] is not None for r in rows if r["u"] <= L):
                    break
            prof += rows or []
        # window overlap: bins repeat; merge by u keeping the better-sampled
        best = {}
        for r in prof:
            if r["u"] not in best or r["n"] > best[r["u"]]["n"]:
                best[r["u"]] = r
        prof = [best[k] for k in sorted(best)]
        ways.append({"id": w["id"], "name": w["tags"].get("name"), "highway": w["tags"].get("highway") or w["tags"].get("railway"), "length_m": round(L, 1),
                     "pts_baytm": w["pts"] if L < 4000 else w["pts"][:: max(1, len(w["pts"]) // 400)], "lidar_projects": sorted(used),
                     "deck": lb.summarise_deck(prof), "deck_profile": lb.decimate(prof, 40.0), "_prof_all": prof, "_pts": pts})
    return ways


def measure_towers(rb, ways, osm):
    """Tower search along every way (deck median of the way as reference, lateral reach 45 m so neighbouring buildings are not
    towers), components pooled over the carriageways, grouped (legs of one tower, both carriageways), tops re-read at full resolution."""
    found = []
    for w in ways:
        if "_pts" not in w or not w.get("deck"):
            continue
        ref = w["deck"]["deck_median_navd88_m"]
        for win in bm.corridor_windows(w["_pts"], 100.0, 600.0):
            p, pc = read_chain(rb["lidar"], win, 0.7)
            for t in bm.towers(w["_pts"], pc, ref, min_above=25.0, reach_m=45.0):
                found.append(dict(t, way=w["id"], deck_ref_navd88_m=ref))
    dedup = []
    for t in sorted(found, key=lambda t: -t["top_max_m"]):
        if all(math.dist(t["centroid_baytm"], u["centroid_baytm"]) > 8 for u in dedup):
            dedup.append(t)
    tmax = max([t["top_max_m"] for t in dedup], default=0)
    out = []
    for t in bm.group_towers([t for t in dedup if t["top_max_m"] >= tmax - rb.get("tower_keep_within_m", 15.0)], 40.0):
        cx, cz = t["centroid_baytm"]
        p, pc = read_chain(rb["lidar"], (cx - 60, cz - 60, cx + 60, cz + 60), None)
        hi = pc["h"][np.hypot(pc["x"] - cx, pc["z"] - cz) <= 40] if len(pc["h"]) else np.zeros(0)
        out.append(dict(t, legs=None, way=t["legs"][0]["way"], deck_ref_navd88_m=t["legs"][0]["deck_ref_navd88_m"],
                        full_res_top_max_m=round(float(hi.max()), 2) if len(hi) else None,
                        full_res_top_p999_m=round(float(np.percentile(hi, 99.9)), 2) if len(hi) else None, lidar_project=p))
    return sorted(out, key=lambda t: (t["centroid_baytm"][0], t["centroid_baytm"][1]))


def assemble_bridge(rb, ways, towers):
    off = rb["official"]
    rec = {"slug": rb["slug"], "name": rb["name"], "kind": rb["kind"], "official": off, "osm_ways": [w["id"] for w in ways],
           "ways": [{k: v for k, v in w.items() if not k.startswith("_")} for w in ways], "towers": [], "lidar_candidates": rb["lidar"]}
    got = sorted({p for w in ways for p in w.get("lidar_projects", [])})
    rec["lidar_projects_used"] = got
    rec["lidar_status"] = ("measured from " + ", ".join(got)) if got else ("no 3DEP EPT first returns found in " + ", ".join(rb["lidar"]) +
                                                                          ": official figures and OSM geometry only, deck/tower heights Assumed from official clearance; tracked for a later lidar source")
    oh = off.get("overall_height_m") or off.get("zampa_tower_height_m")
    # towers: keep the highest components that look like towers (exceed 40 % of the best top above the deck reference)
    if towers:
        tmax = max(t["full_res_top_max_m"] or t["top_max_m"] for t in towers)
        ds = [t for t in towers if (t["full_res_top_max_m"] or t["top_max_m"]) >= tmax - 40]
        for i, t in enumerate(ds):
            top = t["full_res_top_max_m"] or t["top_max_m"]
            ref = oh["v"] if oh else None
            row = {"slug": f"{rb['slug']}_tower_{i}", "name": f"{rb['name']} tower {i}", "pos_baytm": t["centroid_baytm"], "top_navd88_m": top,
                   "top_p999_m": t["full_res_top_p999_m"], "extent_m": t["extent_m"], "deck_ref_navd88_m": t["deck_ref_navd88_m"], "way": t["way"], "n_legs": t.get("n_legs"), "lidar_project": t["lidar_project"]}
            if ref is not None:
                used, src, conf, d = lb.pick_height(ref, top)
                row.update(official_height_m=ref, delta_to_official_m=d, height_used_m=used, height_source=src, confidence=conf,
                           height_status="agrees with official within max(2 m, 1 %)" if src == "official" else "lidar differs from official: lidar used")
            else:
                row.update(height_status="lidar only")
            rec["towers"].append(row)
        xs = [t["pos_baytm"] for t in rec["towers"]]
        if len(xs) >= 2:
            rec["tower_spacing_m"] = sorted(round(math.dist(a, b), 1) for i, a in enumerate(xs) for b in xs[i + 1:])
    rec["position_baytm"] = ways[0]["pts_baytm"][len(ways[0]["pts_baytm"]) // 2] if ways and "pts_baytm" in ways[0] else None
    return rec


def measure_alcatraz(reg, osm):
    """The OSM lighthouse footprint is a 4 m circle; the lidar peak within 30 m is the tower top. Height = peak minus the island
    surface (median of first returns within 10 m of the OSM point, excluding returns above 3 m over that median)."""
    a = reg["alcatraz"]
    lh = osm["ways"].get(a["osm_lighthouse"])
    out = {"slug": "alcatraz", "official": a["official"]}
    if lh:
        cx, cz = lh["c"]
        p, pc = read_chain(a["lidar"], (cx - 60, cz - 60, cx + 60, cz + 60), None)
        if p:
            m = lb.peak_near(pc, (cx, cz), 30.0, 10.0)
            off = a["official"]["lighthouse_height_m"]["v"]
            used, src, conf, d = lb.pick_height(off, m["height_m"])
            rec = {"osm_id": lh["id"], "pos_baytm": lh["c"], "osm_height_tag": lh["tags"].get("height"), "lidar_project": p, "lidar": m,
                   "height_m": used, "height_source": src, "confidence": conf, "delta_to_official_m": d,
                   "status": "agrees with official within max(2 m, 1 %)" if src == "official" else "lidar differs from official: lidar used"}
            rec["osm_position_offset_m"] = round(math.dist(m["peak_baytm"], lh["c"]), 1)
            if rec["osm_position_offset_m"] > 5.0:
                rec["position_note"] = "lidar peak lies more than the 5 m tolerance from the OSM footprint centre; OSM position kept as the baked position (M-005.1 compares to the import), peak position reported"
            out["lighthouse"] = rec
            out["island_surface_navd88_m"] = m["ground_navd88_m"]
            out["island_height_check"] = {"official_m": a["official"]["island_height_m"]["v"], "lidar_surface_navd88_m": m["ground_navd88_m"],
                                          "note": "official 41 m is the island summit above sea level (R-004); NAVD88 ~ MSL within 1 m"}
    return out


def measure_airports(reg, osm):
    out = []
    for ap in reg["airports"]:
        runs = [v for k, v in osm["runways"].items() if k.startswith(ap["slug"] + ":")]
        terms = [v for k, v in osm["terminals"].items() if k.startswith(ap["slug"] + ":")]
        rec = {"slug": ap["slug"], "name": ap["name"], "icao": ap["icao"], "faa_source": ap["faa_src"], "elevation_m": ap["elevation_m"],
               "runways": lb.runway_report(runs, ap["runways_ft"]),
               "runway_ways": [{"id": r["id"], "ref": r["tags"].get("ref"), "pts_baytm": r["pts"]} for r in runs],
               "terminals": []}
        for t in sorted(terms, key=lambda t: t["id"]):
            poly = t["pts"]
            xs, zs = [p[0] for p in poly], [p[1] for p in poly]
            if len(poly) < 4:
                continue
            p, pc = read_chain(ap["lidar"], (min(xs) - 30, min(zs) - 30, max(xs) + 30, max(zs) + 30), None)
            m = lb.building_heights(poly, pc) if p else None
            rec["terminals"].append({"osm_id": t["id"], "pos_baytm": t["c"], "lidar_project": p, "lidar": m, "area_m2": round(abs(0.5 * sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(poly, poly[1:] + poly[:1]))), 0)})
        if ap.get("tower_osm"):
            w = osm["ways"].get(ap["tower_osm"])
            if w:
                cx, cz = w["c"]
                p, pc = read_chain(ap["lidar"], (cx - 60, cz - 60, cx + 60, cz + 60), None)
                m = lb.peak_near(pc, (cx, cz), 20.0, 40.0) if p else None
                tw = {"osm_id": w["id"], "pos_baytm": w["c"], "lidar_project": p, "lidar": m, "height_m": ap["tower_height_m"]["v"], "height_source": "osm",
                      "official": ap["tower_height_m"], "status": "OSM height (R-004 row 15); lidar cross-check below"}
                if m:
                    tw["delta_lidar_minus_osm_m"] = round(m["height_m"] - ap["tower_height_m"]["v"], 2)
                    tw["status"] = ("agrees with OSM within max(2 m, 1 %)" if abs(tw["delta_lidar_minus_osm_m"]) <= lb.height_tol(ap["tower_height_m"]["v"]) else
                                    "lidar differs from OSM height: lidar value reported, OSM kept until closed (Q-NEW)")
                rec["tower"] = tw
        out.append(rec)
    return out


def measure_cranes(reg, osm):
    c = reg["cranes"]
    cr = osm["cranes"]
    r = c["official"]
    doc = {"slug": c["slug"], "pos_tol_m": c["pos_tol_m"], "official": r, "n_osm": len(cr), "cranes": []}
    for k in cr:
        x, z = k["pos"]
        p, pc = read_chain(c["lidar"], (x - 30, z - 30, x + 30, z + 30), None)
        top = None
        if p:
            d = np.hypot(pc["x"] - x, pc["z"] - z)
            sel = pc["h"][d <= 20]
            if len(sel) >= 5:
                top = round(float(sel.max()), 2)
        doc["cranes"].append({"id": k["id"], "pos_baytm": k["pos"], "stowed_top_navd88_m": top, "lidar_project": p, "tags": k["tags"]})
    cl = lb.crane_clusters(cr)
    doc["clusters"] = []
    for i, idx in enumerate(cl):
        pts = [cr[j]["pos"] for j in idx]
        cen = [round(sum(p[0] for p in pts) / len(pts), 1), round(sum(p[1] for p in pts) / len(pts), 1)]
        tops = [doc["cranes"][j]["stowed_top_navd88_m"] for j in idx if (doc["cranes"][j]["stowed_top_navd88_m"] or 0) >= 20.0]
        doc["clusters"].append({"slug": f"port_oakland_cranes_{i}", "name": f"Port of Oakland crane cluster {i}", "n": len(idx), "centroid_baytm": cen,
                                "ids": [cr[j]["id"] for j in idx], "stowed_top_max_navd88_m": max(tops) if tops else None})
    return doc


def run(reg, only, offline):
    osm = osm_cached(reg, offline)
    doc = {"spec": "landmarks_bay@v1", "_doc": "T-104 output of bake_landmarks_bay.py from config/landmarks_bay_registry.json; positions BayTM m, heights NAVD88 m. Rows with status carry the reason when official and lidar disagree.",
           "sources": reg["sources"], "bridges": [], "buildings": [], "airports": [], "cranes": {}, "alcatraz": {}}
    full = {}
    for rb in reg["bridges"]:
        if only and rb["slug"] not in only:
            continue
        print("[bridge]", rb["slug"], flush=True)
        ways = measure_bridge(rb, osm)
        tw = measure_towers(rb, ways, osm) if rb.get("expect_towers") else []
        doc["bridges"].append(assemble_bridge(rb, ways, tw))
        full[rb["slug"]] = {"towers": tw, "profile": {w["id"]: w.get("_prof_all") for w in ways if "_prof_all" in w}}
    for rb in reg["buildings"]:
        if only and rb["slug"] not in only:
            continue
        w = osm["ways"].get(rb.get("osm_id") or "")
        row = {"slug": rb["slug"], "name": rb["name"], "official_height_m": rb["height_m"], "floors": rb["floors"]}
        if w is None:
            row.update(status="not resolved in OSM (excluded until resolved)", pos_baytm=None)
        else:
            poly = w["pts"]
            xs, zs = [p[0] for p in poly], [p[1] for p in poly]
            p, pc = read_chain(rb["lidar"], (min(xs) - 40, min(zs) - 40, max(xs) + 40, max(zs) + 40), None)
            m = lb.building_heights(poly, pc) if p else None
            row.update(osm_id=w["id"], pos_baytm=w["c"], footprint_baytm=poly, lidar_project=p, lidar=m, osm_height_tag=w["tags"].get("height"))
            if m and m["height_p99_m"]:
                used, src, conf, d = lb.pick_height(rb["height_m"]["v"], m["height_p99_m"])
                row.update(height_m=used, height_source=src, confidence=conf, delta_to_official_m=d,
                           status="agrees with official within max(2 m, 1 %)" if src == "official" else "lidar differs from official: lidar used")
            else:
                row.update(height_m=rb["height_m"]["v"], height_source="official", confidence="B", status="no lidar height")
        doc["buildings"].append(row)
    if not only or "alcatraz" in only:
        doc["alcatraz"] = measure_alcatraz(reg, osm)
    if not only or "airports" in only:
        doc["airports"] = measure_airports(reg, osm)
    if not only or "cranes" in only:
        doc["cranes"] = measure_cranes(reg, osm)
    return doc, osm, full


def merge_parts():
    reg = json.loads((CONFIG / "landmarks_bay_registry.json").read_text())
    order = {b["slug"]: i for i, b in enumerate(reg["bridges"])}
    border = {b["slug"]: i for i, b in enumerate(reg["buildings"])}
    doc = {"spec": "landmarks_bay@v1", "_doc": "T-104 output of bake_landmarks_bay.py from config/landmarks_bay_registry.json; positions BayTM m, heights NAVD88 m. Rows carry a status when official and lidar disagree.",
           "sources": reg["sources"], "bridges": [], "buildings": [], "airports": [], "cranes": {}, "alcatraz": {}}
    for f in sorted(CONFIG.glob("landmarks_bay.part.*.json")):
        d = json.loads(f.read_text())
        doc["bridges"] += d["bridges"]
        doc["buildings"] += d["buildings"]
        for k in ("airports", "cranes", "alcatraz"):
            if d[k]:
                doc[k] = d[k]
    rb = {b["slug"]: b for b in reg["bridges"]}
    for b in doc["bridges"]:
        b["towers_expected"] = rb[b["slug"]].get("expect_towers", 0)
        b["towers_found"] = len(b["towers"])
        if b["towers_found"] < b["towers_expected"]:
            b["tower_note"] = f"{b['towers_found']} of {b['towers_expected']} expected towers found in the lidar; the rest stay Assumed from official figures until a denser source is read"
    doc["bridges"].sort(key=lambda b: order[b["slug"]])
    doc["buildings"].sort(key=lambda b: border[b["slug"]])
    OUT.write_text(json.dumps(doc, indent=1) + "\n")
    print("merged ->", OUT, len(doc["bridges"]), "bridges", len(doc["buildings"]), "buildings")


def main(argv=None):
    ap = std_args(argparse.ArgumentParser(description=__doc__.splitlines()[0]))
    ap.add_argument("--only", default="")
    ap.add_argument("--part", default="", help="write config/landmarks_bay.part.<name>.json instead of the final file")
    ap.add_argument("--merge", action="store_true", help="merge config/landmarks_bay.part.*.json into config/landmarks_bay.json")
    a = ap.parse_args(argv)
    apply_std(a)
    if a.merge:
        return merge_parts()
    reg = json.loads((CONFIG / "landmarks_bay_registry.json").read_text())
    only = set(filter(None, a.only.split(",")))
    doc, osm, full = run(reg, only, getattr(a, "offline", False))
    tgt = OUT.with_name(f"landmarks_bay.part.{a.part}.json") if a.part else OUT
    tgt.write_text(json.dumps(doc, indent=1) + "\n")
    print("wrote", tgt)


if __name__ == "__main__":
    main()
