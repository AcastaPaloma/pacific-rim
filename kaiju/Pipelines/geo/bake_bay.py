#!/usr/bin/env python
"""T-102: resumable Bay-wide bake driver over bay_manifest@v1 (R-100): L0 geo_tile@v1 tiles, then the L1..L4 geo_lod@v1 pyramid.

Usage (kaiju env, from the checkout whose git-ignored cache holds the T-101 fetches; run it with nohup, it costs no tokens):
  /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/bake_bay.py --stage datasf|l0|lod [--workers 4] [--tile L0_30_36 ...]
        [--class urban rural water_only ocean_only] [--levels 1 2 3 4] [--limit N] [--force] [--min-free-gb 60] [--dry-run]

Output (all git-ignored): Pipelines/geo/cache/bake/tiles/v1/L0/{ii}_{jj}/ (Q-200) and .../L{n}/{ii}_{jj}/ for the pyramid.
The baked bytes are the same `bake.bake` / `lod.bake_level_tile` the single-tile commands use; this driver only schedules.
Network is forbidden for the l0 and lod stages (offline guard): every source comes from the T-101 cache; a missing source
fails that tile, it is never fetched. The `datasf` stage is the only online step: DataSF footprints and Assessor windows for the
tiles flagged `datasf` in the manifest that the cache lacks (small public-domain requests, same as the M1 tiles used). Which sources a tile has comes from bay_manifest@v1 (`plan` of bake.bake).
Per-tile state: cache/status/bake_<stage>.jsonl, one JSON object per line, the LAST line per tile wins:
  {"tile": id, "status": "done" | "failed" | "skipped", "result": "baked" | "up_to_date", "secs", "bytes", ...}
cache/status/bake_<stage>.summary.json holds the totals, rate and ETA. Re-runs skip done tiles whose outputs are still current
(L0: the bake's own input fingerprint; lod: generator + contributing L0 manifests) and retry failed ones. Disk guard: below
--min-free-gb free on `/` the driver stops cleanly (exit 3). SIGINT/SIGTERM stop the same way (exit 130). Exit 0 = all done.
"""
from __future__ import annotations

import argparse
import json
import multiprocessing as mp
import os
import shutil
import signal
import sys
import time
from concurrent.futures import FIRST_COMPLETED, ProcessPoolExecutor, wait
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo.common import CACHE, GLOBAL_ROOT, canonical_json, load_config, parse_tile, sha256_bytes, sha256_file, utc_now  # noqa: E402

MANIFEST = GLOBAL_ROOT / "bay_manifest.json"
OUT_ROOT = CACHE / "bake" / "tiles" / "v1"
STATUS_DIR = CACHE / "status"
DISK_PATH = "/"
DEFAULT_MIN_FREE_GB = 60.0
CLASSES = ("urban", "rural", "water_only", "ocean_only")


# ---------------------------------------------------------------- planning
def dep3_fill_tiles() -> set:
    """T-103: tiles whose newest 3DEP project leaves holes; their older same-key projects are read too (config/dep3_fill.json)."""
    try:
        return set(load_config("dep3_fill.json")["tiles"])
    except OSError:
        return set()


def plan_of(t: dict) -> dict:
    """What the bake may use for this tile: the manifest's sources, plus anything the cache already holds (the 15 district tiles were
    fetched before bay_manifest@v1, e.g. L0_31_36 has a 3DEP window the manifest lists no item for; ignoring it would change its bytes)."""
    s = t.get("sources", {})
    tid = t["id"]
    fill = tid in dep3_fill_tiles()
    return {**({"dep3_fill": True} if fill else {}), "cudem": bool(s.get("cudem")),
            "dep3": bool(s.get("dep3_1m")) or (CACHE / "3dep" / f"tnm_query__{tid}.json").exists(),
            "osm": bool(s.get("osm")),
            "datasf": bool(s.get("datasf")) or (CACHE / "datasf" / f"ynuv-fyni__{tid}.geojson").exists()}


def load_tiles(path: Path = MANIFEST) -> list[dict]:
    return [t for t in json.loads(Path(path).read_text(encoding="utf-8"))["tiles"] if t.get("class") != "outside"]


def read_status(path: Path) -> dict[str, dict]:
    last: dict[str, dict] = {}
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            try:
                r = json.loads(line)
            except json.JSONDecodeError:
                continue  # torn last line of a killed run
            if "tile" in r and "status" in r:
                last[r["tile"]] = r
    return last


def dir_bytes(d: Path) -> int:
    return sum(f.stat().st_size for f in d.iterdir() if f.is_file())


# ---------------------------------------------------------------- workers (module level: picklable under spawn)
_W: dict = {}


def _init_worker(out_root: str, offline: bool):
    for k in ("OMP_NUM_THREADS", "OPENBLAS_NUM_THREADS", "VECLIB_MAXIMUM_THREADS", "MKL_NUM_THREADS"):
        os.environ[k] = "1"
    signal.signal(signal.SIGINT, signal.SIG_IGN)  # the parent decides when to stop
    if offline:
        from kaiju_geo import fetch
        from kaiju_geo.common import read_meta, set_offline
        set_offline(True)
        _orig = fetch._window_read

        def _cache_only(url, dest, bounds, bounds_crs, label, extra):
            # fetch_3dep skips a TNM item whose window misses the tile (ValueError at fetch time, nothing cached). Offline
            # that item is simply absent from the cache: treat it the same way instead of failing with NetworkForbidden.
            if label == "3dep" and read_meta(dest) is None:
                raise ValueError("3dep item not in cache (window did not intersect the tile at fetch time)")
            return _orig(url, dest, bounds, bounds_crs, label, extra)
        fetch._window_read = _cache_only
    _W["out"] = Path(out_root)


def _bake_l0(tid: str, plan: dict, force: bool) -> dict:
    from kaiju_geo import bake
    t0 = time.time()
    r = bake.bake(parse_tile(tid), force=force, threads=1, out_root=_W["out"], plan=plan)
    man = r["manifest"]
    lay = {L["layer"]: L for L in man["layers"]}
    d = _W["out"] / parse_tile(tid).folder
    return {"result": r["status"], "secs": round(time.time() - t0, 1), "bytes": dir_bytes(d),
            "buildings": lay["buildings"]["count"], "roads": lay["roads"]["count"],
            "placeholder_nodes": lay["terrain"].get("placeholder_nodes", 0),
            "inputs_sha256": man["build"]["inputs_sha256"]}


def _lod_src():
    from kaiju_geo import lod
    if "src" not in _W:
        _W["src"] = lod.L0Source(_W["out"])
    return lod, _W["src"]


def lod_current(lod, src, out: Path, level: int, I: int, J: int) -> bool:
    d = out / f"L{level}" / f"{I:02d}_{J:02d}"
    mp_ = d / "manifest.json"
    if not mp_.exists():
        return False
    man = json.loads(mp_.read_text())
    if man.get("generator") != lod.GENERATOR:
        # @5 manifests equal what @6 writes for tiles whose forms fit their caps at the nominal tolerance (T-102b)
        forms = ((man.get("errors") or {}).get("buildings") or {}).get("forms") or {}
        if not (man.get("generator") in lod.GENERATOR_COMPAT and forms and all(f.get("within_cap") for f in forms.values())):
            return False
    tiles = lod.contributing(level, I, J, src)
    fp = sha256_bytes("\n".join(sorted(sha256_file(src.tiles[k] / "manifest.json") for k in tiles)).encode())
    return man.get("source_fingerprint") == fp and all((d / n).exists() for n in man.get("files", {}))


def _bake_lod(tid: str, force: bool) -> dict:
    lod, src = _lod_src()
    t = parse_tile(tid)
    out = _W["out"]
    t0 = time.time()
    if not force and lod_current(lod, src, out, t.level, t.i, t.j):
        return {"result": "up_to_date", "secs": 0.0}
    r = lod.bake_level_tile(t.level, t.i, t.j, src, out)
    return {"result": "baked", "secs": round(time.time() - t0, 1), "bytes": r["bytes_total"], "sources": r["sources"],
            "terrain_triangles": r["terrain_triangles"],
            "within_budget": {"terrain": r["budget"]["terrain_within_cap"],
                              **{k: v["within_cap"] for k, v in r["forms"].items()}}}


def _fetch_datasf(tid: str) -> dict:
    """The one online step: DataSF footprints + Assessor roll windows for the tiles the manifest flags `datasf` (T-101 did not
    bulk-fetch them: the 15 M1 tiles had them, the other ~130 San Francisco / west-ocean tiles need one small request each)."""
    from kaiju_geo import fetch
    t0 = time.time()
    t = parse_tile(tid)
    m = fetch.fetch_datasf(t)
    try:
        fetch.fetch_assessor(t)
        asr = "ok"
    except (RuntimeError, OSError) as e:
        asr = f"unavailable: {str(e)[:120]}"
    return {"result": "fetched", "secs": round(time.time() - t0, 1), "bytes": m.get("bytes", 0), "assessor": asr}


def _run_task(kind: str, tid: str, plan: dict | None, force: bool) -> dict:
    if kind == "datasf":
        return _fetch_datasf(tid)
    return _bake_l0(tid, plan or {}, force) if kind == "l0" else _bake_lod(tid, force)


# ---------------------------------------------------------------- driver
class Driver:
    def __init__(self, stage: str, out_root: Path, status_dir: Path = STATUS_DIR, workers: int = 4,
                 min_free_gb: float = DEFAULT_MIN_FREE_GB, runner=None, disk_usage=None, offline: bool = True):
        self.stage, self.out, self.dir, self.workers = stage, Path(out_root), Path(status_dir), max(1, workers)
        self.min_free, self.offline = min_free_gb * 1e9, offline
        self.runner = runner
        self.disk_usage = disk_usage or (lambda: shutil.disk_usage(DISK_PATH).free)
        self.log = self.dir / f"bake_{stage}.jsonl"
        self.summary_path = self.dir / f"bake_{stage}.summary.json"
        self.stop = False
        self.stop_reason: str | None = None
        self.counts = {"done": 0, "failed": 0}
        self.t_start = time.time()

    def request_stop(self, reason: str):
        if self.stop_reason is None:
            self.stop_reason = reason
        self.stop = True

    def _append(self, rec: dict):
        rec = dict(rec, utc=utc_now())
        with open(self.log, "a", encoding="utf-8") as f:
            f.write(json.dumps(rec, sort_keys=True) + "\n")

    def write_summary(self, final: bool, total_planned: int, extra: dict | None = None) -> dict:
        last = read_status(self.log)
        tally = {"done": 0, "failed": 0, "skipped": 0}
        res: dict[str, int] = {}
        failed, nbytes, secs, placeholder, bld = {}, 0, 0.0, 0, 0
        for tid, r in last.items():
            tally[r["status"]] = tally.get(r["status"], 0) + 1
            if r["status"] == "failed":
                failed[tid] = r.get("reason", "")
            if r["status"] == "done":
                res[r.get("result", "")] = res.get(r.get("result", ""), 0) + 1
                nbytes += r.get("bytes", 0)
                secs += r.get("secs", 0.0)
                placeholder += 1 if r.get("placeholder_nodes") else 0
                bld += r.get("buildings", 0)
        el = max(time.time() - self.t_start, 1e-9)
        rate = self.counts["done"] / el
        remaining = max(total_planned - tally["done"], 0)
        s = {"stage": self.stage, "updated_utc": utc_now(), "final": final, "stop_reason": self.stop_reason,
             "tiles_planned": total_planned, "status": tally, "results": res, "failed_total": len(failed),
             "failed": dict(sorted(failed.items())[:50]), "bytes_done": nbytes, "tile_cpu_secs_done": round(secs, 1),
             "tiles_with_placeholder_nodes": placeholder, "buildings_done": bld,
             "this_run": dict(self.counts), "rate_tiles_per_s": round(rate, 4),
             "eta_h": round(remaining / rate / 3600, 2) if rate > 0 and remaining else 0.0,
             "free_gb": round(self.disk_usage() / 1e9, 1), "min_free_gb": self.min_free / 1e9, "workers": self.workers,
             "out": str(self.out)}
        s.update(extra or {})
        self.dir.mkdir(parents=True, exist_ok=True)
        tmp = self.summary_path.with_name(self.summary_path.name + ".tmp")
        tmp.write_text(canonical_json(s, indent=1) + "\n", encoding="utf-8")
        os.replace(tmp, self.summary_path)
        return s

    def run(self, jobs: list[tuple[str, dict | None]], force: bool = False, retry_failed: bool = True, limit: int | None = None,
            dry_run: bool = False, currency=None) -> int:
        """jobs: [(tile id, plan)] in the order to run. `currency(tid)` (optional) says a done tile is still current."""
        self.dir.mkdir(parents=True, exist_ok=True)
        last = read_status(self.log)
        todo = []
        for tid, plan in jobs:
            prev = last.get(tid)
            if not force and prev and prev["status"] == "done" and (currency is None or currency(tid)):
                continue
            if prev and prev["status"] == "failed" and not retry_failed:
                continue
            todo.append((tid, plan))
        if limit is not None:
            todo = todo[:limit]
        print(f"[bake_bay:{self.stage}] planned {len(jobs)}, to run {len(todo)}, workers {self.workers}", flush=True)
        if dry_run:
            return 0
        if todo:
            ex = ProcessPoolExecutor(max_workers=self.workers, mp_context=mp.get_context("spawn"),
                                     initializer=_init_worker, initargs=(str(self.out), self.offline)) if self.runner is None else None
            inflight: dict = {}
            it = iter(todo)
            exhausted = False
            try:
                while True:
                    while not exhausted and not self.stop and len(inflight) < self.workers * 2:
                        free = self.disk_usage()
                        if free < self.min_free:
                            self.request_stop(f"disk_low: {free / 1e9:.1f} GB free < {self.min_free / 1e9:.0f} GB")
                            break
                        nxt = next(it, None)
                        if nxt is None:
                            exhausted = True
                            break
                        tid, plan = nxt
                        t0 = time.time()
                        if self.runner is not None:
                            inflight[_Done(tid, self.runner(tid, plan))] = (tid, t0)
                        else:
                            inflight[ex.submit(_run_task, self.stage, tid, plan, force)] = (tid, t0)
                    if not inflight:
                        break
                    if self.runner is not None:
                        done = set(inflight)
                    else:
                        done, _ = wait(set(inflight), timeout=5, return_when=FIRST_COMPLETED)
                    for f in done:
                        tid, t0 = inflight.pop(f)
                        try:
                            r = f.result()
                            self._append({"tile": tid, "status": "done", **r})
                            self.counts["done"] += 1
                        except BaseException as e:  # noqa: BLE001  (a failed tile must not stop the run)
                            reason = f"{type(e).__name__}: {e}".replace("\n", " ")[:300]
                            self._append({"tile": tid, "status": "failed", "reason": reason, "secs": round(time.time() - t0, 1)})
                            self.counts["failed"] += 1
                        if (self.counts["done"] + self.counts["failed"]) % 10 == 0:
                            self.write_summary(False, len(jobs))
            finally:
                if ex is not None:
                    ex.shutdown(wait=True, cancel_futures=True)
        s = self.write_summary(True, len(jobs), {"pending_left": max(len(todo) - self.counts["done"] - self.counts["failed"], 0)})
        print(f"[bake_bay:{self.stage}] done {s['status']['done']} failed {s['status']['failed']}; "
              f"results {s['results']}; stop_reason {self.stop_reason}", flush=True)
        if self.stop_reason and self.stop_reason.startswith("disk_low"):
            return 3
        if self.stop_reason:
            return 130
        return 1 if s["status"]["failed"] else 0


class _Done:
    """Stand-in future for injected (test) runners: runs synchronously, result()/exception re-raised at result()."""

    def __init__(self, tid, value):
        self._v = value

    def result(self):
        if isinstance(self._v, BaseException):
            raise self._v
        return self._v


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--stage", required=True, choices=("datasf", "l0", "lod"))
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--tile", action="append", help="restrict to this tile id (repeatable; L0_ii_jj, or L<n>_ii_jj for lod)")
    ap.add_argument("--class", dest="classes", nargs="*", choices=CLASSES, help="l0: restrict to these coverage classes")
    ap.add_argument("--levels", type=int, nargs="*", default=[1, 2, 3, 4], help="lod: pyramid levels")
    ap.add_argument("--limit", type=int)
    ap.add_argument("--dep3-fill", action="store_true", help="l0: restrict to the tiles in config/dep3_fill.json (T-103 older-3DEP-project fill)")
    ap.add_argument("--landmarks-bay", action="store_true", help="l0: restrict to the tiles that emit a landmark of config/landmarks_bay.json (T-104b)")
    ap.add_argument("--force", action="store_true", help="re-bake even when the input fingerprint matches")
    ap.add_argument("--no-retry-failed", action="store_true")
    ap.add_argument("--min-free-gb", type=float, default=DEFAULT_MIN_FREE_GB)
    ap.add_argument("--manifest", default=str(MANIFEST))
    ap.add_argument("--out", default=str(OUT_ROOT))
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--allow-network", action="store_true", help="debug only: lift the offline guard")
    a = ap.parse_args(argv)
    out = Path(a.out).resolve()
    if "StreamingAssets" in out.parts:
        raise SystemExit("refusing to write Bay bake output under StreamingAssets (git-ignored cache only, Q-200)")
    d = Driver(a.stage, out, workers=a.workers, min_free_gb=a.min_free_gb, offline=not (a.allow_network or a.stage == "datasf"))
    for sig in (signal.SIGINT, signal.SIGTERM):
        signal.signal(sig, lambda *_: d.request_stop("interrupted"))
    want = set(a.tile) if a.tile else None
    landmark_tiles = set()
    if a.landmarks_bay:
        from kaiju_geo import landmarks_bay_emit
        landmark_tiles = set(landmarks_bay_emit.affected_tiles())
    currency = None
    if a.stage in ("l0", "datasf"):
        tiles = [t for t in load_tiles(Path(a.manifest)) if (want is None or t["id"] in want) and (not a.classes or t["class"] in a.classes)
                 and (not a.dep3_fill or t["id"] in dep3_fill_tiles()) and (not a.landmarks_bay or t["id"] in landmark_tiles)]
        if a.stage == "datasf":
            tiles = [t for t in tiles if t.get("sources", {}).get("datasf")]
        jobs = [(t["id"], plan_of(t)) for t in sorted(tiles, key=lambda t: t["id"])]
    else:
        from kaiju_geo import lod
        src = lod.L0Source(out)
        if not src.tiles:
            raise SystemExit(f"no baked L0 tiles under {out}/L0: run --stage l0 first")
        jobs = []
        for level in sorted(a.levels):
            for (I, J) in lod.pyramid_tiles(level, src):
                tid = f"L{level}_{I:02d}_{J:02d}"
                if want is None or tid in want:
                    jobs.append((tid, None))

        def currency(tid, _lod=lod, _src=src, _out=out):
            t = parse_tile(tid)
            return lod_current(_lod, _src, _out, t.level, t.i, t.j)
    return d.run(jobs, force=a.force, retry_failed=not a.no_retry_failed, limit=a.limit, dry_run=a.dry_run, currency=currency)


if __name__ == "__main__":
    sys.exit(main())
