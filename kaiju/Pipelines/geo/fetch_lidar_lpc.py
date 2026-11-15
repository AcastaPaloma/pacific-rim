#!/usr/bin/env python
"""T-103b: height fill for the tiles outside every EPT project, from TNM Lidar Point Cloud (LPC) LAZ files (R-100 §3, Q-NEW-d).

Same reduction as fetch_lidar_bay.py (1 m first-return grid p95/max/multi/count, only the 128 m cells that hold a default-height
building, written as cache/lidar/rind_v11__<tile>.npz) but the source is the TNM `Lidar Point Cloud (LPC)` index: per tile the
projects covering it are tried newest first, each LAZ file is downloaded to cache/lidar_bay/lpc_tmp/, reduced and DELETED at once
(never more than --workers files on disk). Resumable via cache/lidar_bay/status_lpc.jsonl (last line per tile wins).
Usage: python -I Pipelines/geo/fetch_lidar_lpc.py run [--workers 1] [--min-free-gb 25] [--tile ID ...] [--limit N]
"""
from __future__ import annotations

import argparse, json, os, re, sys, time, urllib.parse, urllib.request
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402

import fetch_lidar_bay as B  # noqa: E402
from kaiju_geo.common import baytm_crs, finish_fetch, load_config, parse_tile, transformer, utc_now  # noqa: E402

TMP = B.WORK / "lpc_tmp"
STATUS = B.WORK / "status_lpc.jsonl"
API = "https://tnmaccess.nationalmap.gov/api/v1/products"


def query(bbox):
    items, off = [], 0
    while True:
        q = urllib.parse.urlencode({"datasets": "Lidar Point Cloud (LPC)", "bbox": ",".join(f"{v:.5f}" for v in bbox), "max": 200, "offset": off})
        d = json.load(urllib.request.urlopen(f"{API}?{q}", timeout=60))
        items += d.get("items", [])
        off += 200
        if off >= d.get("total", 0):
            return items


def project_of(item):
    m = re.search(r"/Projects/(?:legacy/)?([^/]+)/", item["downloadURL"])
    return m.group(1) if m else item["title"]


def download(url, dest):
    tmp = dest.with_name(dest.name + ".part")
    for k in range(4):
        try:
            with urllib.request.urlopen(url, timeout=120) as r, open(tmp, "wb") as f:
                while chunk := r.read(1 << 22):
                    f.write(chunk)
            os.replace(tmp, dest)
            return dest.stat().st_size
        except OSError:
            time.sleep(5 * (k + 1))
    raise RuntimeError(f"download failed {url}")


def fill_tile(tid):
    import pdal, shapely
    from shapely.geometry import shape, box
    t0 = time.time()
    tile = parse_tile(tid)
    x0, z0, x1, z1 = tile.bounds
    plan = json.loads(B.PLAN.read_text())["tiles"][tid]
    lcfg = load_config("bake.json")["buildings"]["lidar_fill"]
    fe = json.loads((B.tile_dir(tid) / "buildings.geojson").read_text())["features"]
    todo = {f["properties"]["bid"]: shape(f["geometry"]) for f in fe if f["properties"].get("height_source") == "default"}
    todo = {k: g for k, g in todo.items() if not g.buffer(-lcfg["erode_m"]).is_empty}
    ll = transformer(baytm_crs(), "EPSG:4326")
    lon0, lat0 = ll.transform(x0, z0); lon1, lat1 = ll.transform(x1, z1)
    corners = [ll.transform(x, z) for x in (x0, x1) for z in (z0, z1)]
    bbox = (min(c[0] for c in corners), min(c[1] for c in corners), max(c[0] for c in corners), max(c[1] for c in corners))
    items = query(bbox)
    projs = {}
    for it in items:
        projs.setdefault(project_of(it), []).append(it)
    order = sorted(projs, key=lambda p: (-B.year_of(p) if B.year_of(p) else -max(int(i["publicationDate"][:4]) for i in projs[p]), p))
    grid = {"bounds": [x0, z0, x1, z1], "count": np.zeros((B.N, B.N), np.int32)}
    out = {k: np.full((B.N, B.N), np.nan, np.float32) for k in ("p95_10", "max_10", "multi_10")}
    out["count_10"] = grid["count"]
    to_baytm = transformer("EPSG:26910", baytm_crs())
    pending, used, nbytes, npts, nfiles = dict(todo), {}, 0, 0, 0
    import lidar_ref_v11
    TMP.mkdir(parents=True, exist_ok=True)
    for proj in order:
        if not pending:
            break
        need = set()
        for g in pending.values():
            bx0, bz0, bx1, bz1 = g.bounds
            for cj in range(int(max(bz0 - z0, 0) // 128), int(min(bz1 - z0, B.N - 1) // 128) + 1):
                for ci in range(int(max(bx0 - x0, 0) // 128), int(min(bx1 - x0, B.N - 1) // 128) + 1):
                    need.add((cj, ci))
        needmask = np.zeros((16, 16), bool)
        for cj, ci in need:
            needmask[cj, ci] = True
        got = 0
        for it in projs[proj]:
            b = it["boundingBox"]
            fb = box(b["minX"], b["minY"], b["maxX"], b["maxY"])
            cells_in = [(cj, ci) for (cj, ci) in need
                        if fb.intersects(box(*[v for v in (lambda a, c: (min(a[0], c[0]), min(a[1], c[1]), max(a[0], c[0]), max(a[1], c[1])))(
                            ll.transform(x0 + ci * 128, z0 + cj * 128), ll.transform(x0 + (ci + 1) * 128, z0 + (cj + 1) * 128))]))]
            if not cells_in:
                continue
            f = TMP / f"{tid}__{Path(it['downloadURL']).name}"
            nbytes += download(it["downloadURL"], f); nfiles += 1
            try:
                p = pdal.Pipeline(json.dumps([{"type": "readers.las", "filename": str(f)},
                    {"type": "filters.expression", "expression": B.EXPR},
                    {"type": "filters.reprojection", "out_srs": "EPSG:26910+5703"}]))
                n = p.execute()
                if n == 0 or not p.arrays:
                    continue
                a = p.arrays[0]
                bx, bz = to_baytm.transform(a["X"], a["Y"])
                bx, bz = np.asarray(bx), np.asarray(bz)
                cx = np.floor(bx - x0).astype(np.int64); cz = np.floor(bz - z0).astype(np.int64)
                ok = (cx >= 0) & (cx < B.N) & (cz >= 0) & (cz < B.N)
                ok &= needmask[np.clip(cz // 128, 0, 15), np.clip(cx // 128, 0, 15)]
                npts += int(ok.sum())
                zz = a["Z"].astype(np.float64)
                p95, mx, sh, cnt = lidar_ref_v11.grid_stats(cx[ok], cz[ok], zz[ok], (a["NumberOfReturns"] >= 2)[ok], B.N)
                new = (cnt > 0) & (grid["count"] == 0)
                out["p95_10"][new] = p95[new]; out["max_10"][new] = mx[new]; out["multi_10"][new] = sh[new]; out["count_10"][new] = cnt[new]
                got += int(new.sum())
            finally:
                f.unlink(missing_ok=True)
        used[proj] = got
        for bid in list(pending):
            c = B.cover_of(pending[bid], grid, lcfg["erode_m"])
            if c is not None and c >= lcfg["min_coverage"]:
                del pending[bid]
    if int(np.count_nonzero(out["count_10"])) == 0:
        return {"tile": tid, "status": "nolidar", "county": plan["county"], "default": len(todo), "projects_seen": order, "secs": round(time.time() - t0, 1)}
    dest = B.LIDAR / f"rind_v11__{tid}.npz"
    tmp = dest.with_name(dest.name + ".part.npz")
    np.savez_compressed(tmp, bounds_baytm=np.array([x0, z0, x1, z1]), points=np.array([npts]), **out)
    finish_fetch(dest, tmp, "https://tnmaccess.nationalmap.gov/api/v1/products?datasets=Lidar+Point+Cloud+(LPC)",
                 {"points_kept": npts, "filter": B.EXPR, "projects_used_cells": used, "files": nfiles,
                  "grid": "BayTM 1 m cells over the tile, row 0 = south; only 128 m cells holding a default-height building were read (T-103b, TNM LPC)"})
    return {"tile": tid, "status": "done", "county": plan["county"], "default": len(todo), "targets": len(todo), "satisfied": len(todo) - len(pending),
            "unsatisfied": len(pending), "files": nfiles, "bytes_in": nbytes, "points": npts, "bytes_out": dest.stat().st_size,
            "projects_used_cells": used, "secs": round(time.time() - t0, 1), "utc": utc_now()}


def _w(tid):
    try:
        return fill_tile(tid)
    except BaseException as e:  # noqa: BLE001
        return {"tile": tid, "status": "failed", "error": f"{type(e).__name__}: {e}"[:400], "utc": utc_now()}


def main(argv=None):
    ap = argparse.ArgumentParser(); sp = ap.add_subparsers(dest="cmd", required=True)
    r = sp.add_parser("run"); r.add_argument("--workers", type=int, default=1); r.add_argument("--tile", action="append")
    r.add_argument("--limit", type=int); r.add_argument("--min-free-gb", type=float, default=25.0)
    a = ap.parse_args(argv)
    plan = json.loads(B.PLAN.read_text())["tiles"]
    last = {}
    if STATUS.exists():
        for l in STATUS.read_text().splitlines():
            try: x = json.loads(l); last[x["tile"]] = x
            except ValueError: pass
    ids = [t for t, v in plan.items() if v["n_default"] and not v["projects"] and not v["has_rind"] and (not a.tile or t in a.tile)
           and last.get(t, {}).get("status") not in ("done", "nolidar")]
    ids.sort(key=lambda t: -plan[t]["area_default"])
    ids = ids[:a.limit] if a.limit else ids
    print(f"[lpc] {len(ids)} tiles", flush=True)
    with ProcessPoolExecutor(a.workers) as ex:
        it = iter(ids)
        for rec in ex.map(_w, ids):
            with open(STATUS, "a") as fh:
                fh.write(json.dumps(rec, sort_keys=True) + "\n")
            print(f"[lpc] {rec['tile']} {rec['status']} {rec.get('secs')} s in {rec.get('bytes_in')} sat {rec.get('satisfied')}/{rec.get('targets')} {rec.get('error','')}", flush=True)
            if B.free_gb() < a.min_free_gb:
                print("[lpc] disk_low stop", flush=True); return 3
    return 0


if __name__ == "__main__":
    sys.exit(main())
