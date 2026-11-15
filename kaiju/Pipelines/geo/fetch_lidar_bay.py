#!/usr/bin/env python
"""T-103b: Bay-wide lidar height fill, reduced as it streams (R-100 §2, Q-202).

For every baked L0 tile that holds buildings whose height is still `default`, read 3DEP EPT first returns ONLY in the 128 m
cells that contain such a building (runs of adjacent cells are one read), newest EPT project first and the next project only
for buildings the previous one did not cover (>= lidar_fill.min_coverage of the eroded footprint cells), and keep only the
reduced 1 m first-return grid (p95, max, multi-return share, count) as Pipelines/geo/cache/lidar/rind_v11__<tile>.npz, the file
`bake.bake` already reads (same keys as lidar_ref_v11, 1 m only). No point cloud is ever stored. Tiles that already have a
rind_v11 file (the 15 M1 tiles and the SF set) are never touched.

Usage (kaiju env, KAIJU_GEO_CACHE = the integration cache that holds the baked L0 tiles):
  python -I Pipelines/geo/fetch_lidar_bay.py plan                       # scan baked L0 buildings -> cache/lidar_bay/plan.json
  python -I Pipelines/geo/fetch_lidar_bay.py run [--county NAME ...] [--tile L0_x_y ...] [--workers 3] [--limit N]
        [--min-free-gb 25] [--cell-m 128] [--max-run 4]                 # resumable; status in cache/lidar_bay/status.jsonl
  python -I Pipelines/geo/fetch_lidar_bay.py report                     # totals by county from the status file
Status: one JSON line per tile (the LAST line per tile wins): {"tile", "status": done|failed|nolidar, "secs", "bytes_in",
"bytes_out", "cells", "reads", "satisfied", "unsatisfied", ...}. Disk guard: before each tile free space on `/` is compared
with --min-free-gb (default 25); below it the driver stops cleanly (exit 3). SIGINT/SIGTERM stop cleanly (exit 130).
"""
from __future__ import annotations

import argparse
import json
import os
import re
import signal
import shutil
import sys
import threading
import time
from concurrent.futures import ProcessPoolExecutor, FIRST_COMPLETED, wait
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402

from kaiju_geo.common import CACHE, GLOBAL_ROOT, baytm_crs, canonical_json, finish_fetch, load_config, parse_tile, transformer, utc_now  # noqa: E402

WORK = CACHE / "lidar_bay"
PLAN = WORK / "plan.json"
STATUS = WORK / "status.jsonl"
BAKED = CACHE / "bake" / "tiles" / "v1" / "L0"
LIDAR = CACHE / "lidar"
EPT_BASE = "https://s3-us-west-2.amazonaws.com/usgs-lidar-public/{p}/ept.json"
EXPR = "ReturnNumber == 1 && Classification != 7 && Classification != 18 && Classification != 22"
N = 2048  # 1 m cells per tile
CELL_M = 128
RES = 0.7  # EPT resolution (m), same as lidar_ref_v11
_stop = threading.Event()


def year_of(name: str) -> int:
    m = re.findall(r"(20\d\d)", name)
    return int(m[-1]) if m else 0


def project_order(names):
    """Newest first (the newest project is the most likely to show today's roofs); ties by name."""
    return sorted(names, key=lambda n: (-year_of(n), n))


def tile_dir(tid: str) -> Path:
    t = parse_tile(tid)
    return BAKED / f"{t.i:02d}_{t.j:02d}"


# ---------------------------------------------------------------- plan
def _scan_one(tid: str):
    from shapely.geometry import shape
    p = tile_dir(tid) / "buildings.geojson"
    if not p.exists():
        return tid, None
    fe = json.loads(p.read_text())["features"]
    rows = [(f["properties"]["bid"], shape(f["geometry"])) for f in fe if f["properties"].get("height_source") == "default"]
    if not rows:
        return tid, {"n_default": 0, "n_total": len(fe), "area_default": 0.0}
    a = sum(g.area for _, g in rows)
    return tid, {"n_default": len(rows), "n_total": len(fe), "area_default": round(a, 1)}


def cmd_plan(a):
    from concurrent.futures import ProcessPoolExecutor as PPE
    man = json.loads((GLOBAL_ROOT / "bay_manifest.json").read_text())
    cand = [t for t in man["tiles"] if t.get("osm_buildings")]
    ids = [t["id"] for t in cand]
    out = {}
    with PPE(a.workers) as ex:
        for tid, r in ex.map(_scan_one, ids, chunksize=8):
            if r is not None:
                out[tid] = r
    byid = {t["id"]: t for t in cand}
    cs = load_counties()
    for tid, r in out.items():
        t = byid[tid]
        r["projects"] = project_order(t.get("sources", {}).get("lidar_ept", []))
        r["county"] = county_of(cs, t["bbox_baytm"])
        r["has_rind"] = (LIDAR / f"rind_v11__{tid}.npz").exists()
    WORK.mkdir(parents=True, exist_ok=True)
    PLAN.write_text(canonical_json({"generated_utc": utc_now(), "tiles": out}, indent=0) + "\n")
    need = {k: v for k, v in out.items() if v["n_default"] and not v["has_rind"]}
    print(f"plan: {len(out)} tiles with buildings, {len(need)} need a fill "
          f"({sum(v['n_default'] for v in need.values())} default buildings, {sum(v['area_default'] for v in need.values())/1e6:.2f} km2); "
          f"no EPT project: {sum(1 for v in need.values() if not v['projects'])} tiles -> {PLAN}")


def load_counties():
    from shapely.geometry import shape
    p = CACHE / "m005_bay" / "counties_osm_adm6.geojson"
    gj = json.loads(p.read_text())
    return [(f["properties"].get("name") or f["properties"].get("NAME"), shape(f["geometry"])) for f in gj["features"]]


def county_of(cs, bbox):
    from shapely.geometry import Point
    t = transformer(baytm_crs(), "EPSG:4326")
    lon, lat = t.transform((bbox[0] + bbox[2]) / 2, (bbox[1] + bbox[3]) / 2)
    pt = Point(lon, lat)
    for n, g in cs:
        if g.contains(pt):
            return n
    return None


# ---------------------------------------------------------------- reading
def cover_of(geom, grid, erode_m=1.0):
    """Share of the eroded footprint's 1 m cells with first returns (same cells as vector.lidar_height); None = not measurable."""
    import shapely
    eg = geom.buffer(-erode_m)
    if eg.is_empty:
        return None
    x0, z0, x1, z1 = grid["bounds"]
    res = (x1 - x0) / N
    bx0, bz0, bx1, bz1 = eg.bounds
    i0, i1 = max(int(np.floor((bx0 - x0) / res)), 0), min(int(np.ceil((bx1 - x0) / res)), N)
    j0, j1 = max(int(np.floor((bz0 - z0) / res)), 0), min(int(np.ceil((bz1 - z0) / res)), N)
    if i1 <= i0 or j1 <= j0:
        return None
    jj, ii = np.meshgrid(np.arange(j0, j1), np.arange(i0, i1), indexing="ij")
    inside = shapely.contains_xy(eg, x0 + (ii + 0.5) * res, z0 + (jj + 0.5) * res)
    if not inside.any():
        return None
    return float((grid["count"][j0:j1, i0:i1][inside] > 0).mean())


def _read_run_inproc(project, rect, threads):
    """First returns of one BayTM rectangle (x0, z0, x1, z1) -> (bx, bz, z, multi, bytes_in_estimate) with only points inside rect."""
    import pdal
    t = transformer(baytm_crs(), "EPSG:3857")
    xs, ys = zip(*[t.transform(x, z) for x in (rect[0] - 2, rect[2] + 2) for z in (rect[1] - 2, rect[3] + 2)])
    pipe = [{"type": "readers.ept", "filename": EPT_BASE.format(p=project), "bounds": f"([{min(xs):.2f},{max(xs):.2f}],[{min(ys):.2f},{max(ys):.2f}])",
             "resolution": RES, "threads": threads},
            {"type": "filters.expression", "expression": EXPR},
            {"type": "filters.reprojection", "out_srs": "EPSG:26910+5703"}]
    for attempt in range(5):  # transient S3 / ept.json read failures are retried with a back-off
        p = pdal.Pipeline(json.dumps(pipe))
        try:
            n = p.execute()
            break
        except RuntimeError as e:
            if "No data" in str(e) or "empty" in str(e).lower():
                return None
            if attempt == 4:
                raise
            time.sleep(10 * (attempt + 1))
    if n == 0 or not p.arrays:
        return None
    arr = p.arrays[0]
    bx, bz = transformer("EPSG:26910", baytm_crs()).transform(arr["X"], arr["Y"])
    bx, bz = np.asarray(bx), np.asarray(bz)
    ok = (bx >= rect[0]) & (bx < rect[2]) & (bz >= rect[1]) & (bz < rect[3])
    return bx[ok], bz[ok], arr["Z"].astype(np.float64)[ok], (arr["NumberOfReturns"] >= 2)[ok], int(n)


def read_run(project, rect, threads, tries=5):
    """Run _read_run_inproc in a child process: PDAL's EPT reader aborts the whole process (uncatchable C++ exception) on a
    transient S3 read error, so each read is isolated and retried with a back-off."""
    import subprocess, tempfile
    for attempt in range(tries):
        fd, name = tempfile.mkstemp(suffix=".npz", dir=str(WORK)); os.close(fd)
        try:
            cp = subprocess.run([sys.executable, "-I", os.path.abspath(__file__), "readrun", project, *[repr(float(v)) for v in rect], str(threads), name],
                                capture_output=True, text=True)
            if cp.returncode == 0:
                z = np.load(name)
                if not z.files:
                    return None
                return z["bx"], z["bz"], z["z"], z["multi"].astype(bool), int(z["n"])
            if attempt == tries - 1:
                raise RuntimeError(f"read {project} {rect} failed rc={cp.returncode}: {cp.stderr[-300:]}")
        finally:
            try:
                os.unlink(name)
            except OSError:
                pass
        time.sleep(10 * (attempt + 1))


def cmd_readrun(argv):
    project, *rest = argv
    rect = tuple(float(v) for v in rest[:4]); threads = int(rest[4]); out = rest[5]
    r = _read_run_inproc(project, rect, threads)
    with open(out, "wb") as fh:
        if r is None:
            np.savez(fh)
        else:
            np.savez(fh, bx=r[0], bz=r[1], z=r[2], multi=r[3], n=np.array(r[4]))
    return 0



def project_geoms():
    from shapely import wkt
    cfg = load_config("bay.json")
    return {e["name"]: wkt.loads(e["wkt"]) for e in cfg["snapshots"]["lidar_ept"]}


def runs_of(cells, max_run):
    """cells = set of (cj, ci) in 128 m units -> list of (cj, ci0, ci1) horizontal runs of at most max_run cells."""
    out = []
    for cj in sorted({c[0] for c in cells}):
        cis = sorted(ci for (j, ci) in cells if j == cj)
        start = prev = None
        for ci in cis:
            if start is None:
                start = prev = ci
            elif ci == prev + 1 and ci - start < max_run:
                prev = ci
            else:
                out.append((cj, start, prev)); start = prev = ci
        if start is not None:
            out.append((cj, start, prev))
    return out


def fill_tile(tid, cell_m=CELL_M, max_run=4, threads=4, validate=False):
    """Read, reduce and write rind_v11 for one tile. Returns the status record."""
    import lidar_ref_v11
    from shapely.geometry import shape, box
    plan = json.loads(PLAN.read_text())["tiles"][tid]
    t0 = time.time()
    tile = parse_tile(tid)
    x0, z0, x1, z1 = tile.bounds
    fe = json.loads((tile_dir(tid) / "buildings.geojson").read_text())["features"]
    lcfg = load_config("bake.json")["buildings"]["lidar_fill"]
    todo = {}
    for f in fe:
        pr = f["properties"]
        if pr.get("height_source") == "default" or (validate and pr.get("height_source") == "osm_height" and len(todo) < 40):
            g = shape(f["geometry"])
            if not g.buffer(-lcfg["erode_m"]).is_empty:
                todo[pr["bid"]] = g
    default_ids = {f["properties"]["bid"] for f in fe if f["properties"].get("height_source") == "default"}
    grid = {"bounds": [x0, z0, x1, z1], "count": np.zeros((N, N), np.int32)}
    out = {k: np.full((N, N), np.nan, np.float32) for k in ("p95_10", "max_10", "multi_10")}
    out["count_10"] = grid["count"]
    ll = transformer(baytm_crs(), "EPSG:4326")
    geoms = project_geoms()
    pending = dict(todo)
    n_read = n_pts = 0
    used = {}
    gs = lidar_ref_v11.grid_stats
    for proj in plan["projects"]:
        if not pending:
            break
        pg = geoms[proj]
        cells = set()
        for g in pending.values():
            bx0, bz0, bx1, bz1 = g.bounds
            for cj in range(int(max(bz0 - z0, 0) // cell_m), int(min(bz1 - z0, N - 1) // cell_m) + 1):
                for ci in range(int(max(bx0 - x0, 0) // cell_m), int(min(bx1 - x0, N - 1) // cell_m) + 1):
                    cells.add((cj, ci))
        keep = set()
        for (cj, ci) in cells:  # only cells inside the project footprint
            cx0, cz0 = x0 + ci * cell_m, z0 + cj * cell_m
            lons, lats = zip(*[ll.transform(x, z) for x in (cx0, cx0 + cell_m) for z in (cz0, cz0 + cell_m)])
            if pg.intersects(box(min(lons), min(lats), max(lons), max(lats))):
                keep.add((cj, ci))
        if not keep:
            continue
        got = 0
        for cj, ci0, ci1 in runs_of(keep, max_run):
            rect = (x0 + ci0 * cell_m, z0 + cj * cell_m, x0 + (ci1 + 1) * cell_m, z0 + (cj + 1) * cell_m)
            r = read_run(proj, rect, threads)
            n_read += 1
            if r is None:
                continue
            bx, bz, zz, multi, n = r
            n_pts += n
            cx = np.floor((bx - x0)).astype(np.int64); cz = np.floor((bz - z0)).astype(np.int64)
            ok = (cx >= 0) & (cx < N) & (cz >= 0) & (cz < N)
            p95, mx, sh, cnt = gs(cx[ok], cz[ok], zz[ok], multi[ok], N)
            new = (cnt > 0) & (grid["count"] == 0)  # earlier (newer) projects keep their cells
            out["p95_10"][new] = p95[new]; out["max_10"][new] = mx[new]; out["multi_10"][new] = sh[new]
            out["count_10"][new] = cnt[new]
            got += int(new.sum())
        used[proj] = got
        for bid in list(pending):
            c = cover_of(pending[bid], grid, lcfg["erode_m"])
            if c is not None and c >= lcfg["min_coverage"]:
                del pending[bid]
    sat = len(todo) - len(pending)
    dest = LIDAR / f"rind_v11__{tid}.npz"
    LIDAR.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_name(dest.name + ".part.npz")
    cov_cells = int(np.count_nonzero(out["count_10"]))
    if cov_cells == 0:
        return {"tile": tid, "status": "nolidar", "reads": n_read, "default": len(default_ids), "secs": round(time.time() - t0, 1),
                "projects": plan["projects"], "county": plan["county"]}
    np.savez_compressed(tmp, bounds_baytm=np.array([x0, z0, x1, z1]), points=np.array([n_pts]), **out)
    url = ",".join(EPT_BASE.format(p=p) for p in used)
    finish_fetch(dest, tmp, url, {"points_kept": int(n_pts), "filter": EXPR, "ept_resolution": RES, "projects_used_cells": used,
                                  "grid": "BayTM 1 m cells over the tile, row 0 = south; only 128 m cells holding a default-height building were read (T-103b)"})
    rec = {"tile": tid, "status": "done", "county": plan["county"], "default": len(default_ids), "targets": len(todo), "satisfied": sat,
           "unsatisfied": len(pending), "reads": n_read, "points": n_pts, "bytes_out": dest.stat().st_size, "projects_used_cells": used,
           "secs": round(time.time() - t0, 1), "utc": utc_now()}
    return rec


def _worker(args):
    tid, kw = args
    try:
        return fill_tile(tid, **kw)
    except BaseException as e:  # noqa: BLE001
        return {"tile": tid, "status": "failed", "error": f"{type(e).__name__}: {e}"[:400], "utc": utc_now()}


# ---------------------------------------------------------------- run
def read_status():
    last = {}
    if STATUS.exists():
        for line in STATUS.read_text().splitlines():
            try:
                r = json.loads(line)
            except json.JSONDecodeError:
                continue
            last[r["tile"]] = r
    return last


def free_gb():
    return shutil.disk_usage("/").free / 1e9


def cmd_run(a):
    plan = json.loads(PLAN.read_text())["tiles"]
    last = read_status()
    ids = []
    for tid, r in plan.items():
        if not r["n_default"] or r["has_rind"] or not r["projects"]:
            continue
        if a.tile and tid not in a.tile:
            continue
        if a.county and r["county"] not in a.county:
            continue
        st = last.get(tid)
        if st and st["status"] in ("done", "nolidar"):
            continue
        if st and st["status"] == "failed" and a.no_retry_failed:
            continue
        ids.append(tid)
    ids.sort(key=lambda t: -plan[t]["area_default"])  # largest default share first
    if a.limit:
        ids = ids[:a.limit]
    print(f"[run] {len(ids)} tiles planned, free {free_gb():.1f} GB", flush=True)
    WORK.mkdir(parents=True, exist_ok=True)
    stop = {"why": None}

    def on_sig(*_):
        stop["why"] = "signal"
    signal.signal(signal.SIGTERM, on_sig); signal.signal(signal.SIGINT, on_sig)
    kw = {"cell_m": a.cell_m, "max_run": a.max_run, "threads": a.threads, "validate": a.validate}
    done = failed = 0
    t_start = time.time()
    with ProcessPoolExecutor(a.workers) as ex:
        inflight = {}
        it = iter(ids)
        while True:
            while len(inflight) < a.workers and not stop["why"]:
                if free_gb() < a.min_free_gb:
                    stop["why"] = "disk_low"; break
                tid = next(it, None)
                if tid is None:
                    break
                inflight[ex.submit(_worker, (tid, kw))] = tid
            if not inflight:
                break
            fin, _ = wait(list(inflight), return_when=FIRST_COMPLETED, timeout=30)
            for fu in fin:
                rec = fu.result(); inflight.pop(fu)
                rec.setdefault("utc", utc_now())
                with open(STATUS, "a") as fh:
                    fh.write(json.dumps(rec, sort_keys=True) + "\n")
                done += rec["status"] != "failed"; failed += rec["status"] == "failed"
                print(f"[run] {rec['tile']} {rec['status']} {rec.get('secs')} s out {rec.get('bytes_out')} sat {rec.get('satisfied')}/{rec.get('targets')} free {free_gb():.1f} GB", flush=True)
            (WORK / "summary.json").write_text(json.dumps({"done": done, "failed": failed, "planned": len(ids), "stop_reason": stop["why"],
                                                          "elapsed_s": round(time.time() - t_start), "free_gb": round(free_gb(), 1), "utc": utc_now()}) + "\n")
    print(f"[run] finished: done {done}, failed {failed}, stop {stop['why']}", flush=True)
    return 3 if stop["why"] == "disk_low" else 130 if stop["why"] else (1 if failed else 0)


def cmd_report(a):
    plan = json.loads(PLAN.read_text())["tiles"]
    last = read_status()
    by = {}
    for tid, r in last.items():
        c = by.setdefault(r.get("county"), {"tiles": 0, "bytes_out": 0, "secs": 0.0, "failed": 0, "targets": 0, "satisfied": 0})
        c["tiles"] += 1; c["bytes_out"] += r.get("bytes_out", 0); c["secs"] += r.get("secs", 0); c["failed"] += r["status"] == "failed"
        c["targets"] += r.get("targets", 0); c["satisfied"] += r.get("satisfied", 0)
    print(json.dumps(by, indent=1, default=str))


def main(argv=None):
    if (argv or sys.argv[1:])[:1] == ["readrun"]:
        return cmd_readrun((argv or sys.argv[1:])[1:])
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sp = ap.add_subparsers(dest="cmd", required=True)
    p = sp.add_parser("plan"); p.add_argument("--workers", type=int, default=6)
    r = sp.add_parser("run")
    r.add_argument("--county", nargs="*"); r.add_argument("--tile", nargs="*"); r.add_argument("--workers", type=int, default=3)
    r.add_argument("--threads", type=int, default=4); r.add_argument("--limit", type=int); r.add_argument("--min-free-gb", type=float, default=25.0)
    r.add_argument("--cell-m", type=int, default=CELL_M); r.add_argument("--max-run", type=int, default=4)
    r.add_argument("--no-retry-failed", action="store_true"); r.add_argument("--validate", action="store_true")
    sp.add_parser("report")
    a = ap.parse_args(argv)
    return {"plan": cmd_plan, "run": cmd_run, "report": cmd_report}[a.cmd](a) or 0


if __name__ == "__main__":
    sys.exit(main())
