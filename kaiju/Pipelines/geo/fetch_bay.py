#!/usr/bin/env python
"""T-101: resumable bulk-fetch driver over bay_manifest@v1 (R-100): one source per invocation, N workers.

Usage (kaiju env, from the integration checkout so downloads land in its git-ignored cache):
  /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/fetch_bay.py --source cudem|3dep|osm|water|lidar [--workers 4]
        [--tile L0_30_36 ...] [--limit N] [--no-retry-failed] [--min-free-gb 60] [--offline] [--dry-run]

Per-tile state is appended to cache/status/<source>.jsonl (one JSON object per line; the LAST line per tile wins):
  {"tile": id, "status": "done" | "failed" | "skipped", "reason": ..., "utc": ..., "secs": ...}
and cache/status/<source>.summary.json holds the totals. Re-runs are no-ops for done tiles (and for tiles already
skipped for the same reason); failed tiles are retried unless --no-retry-failed. Which tiles each source covers:
  cudem   manifest sources.cudem non-empty (else skipped "no_cudem")
  3dep    land tiles with sources.dep3_1m (else skipped "ocean_only" / "no_3dep_1m")
  osm     sources.osm true (ocean_only tiles have no OSM: skipped "ocean_only")
  water   not per tile: the one-time bulk files (OSM water polygons zip + tide datums), recorded as tile "_global"
  lidar   tiles with OSM buildings and an EPT project (else skipped "no_buildings" / "no_lidar_ept")
Tiles outside the world box are not in the manifest; a tile with class "outside" is skipped.
Disk guard: before starting each tile the free space on `/` is checked; below --min-free-gb (default 60) the driver
stops cleanly (in-flight tiles finish, the rest are left pending), writes the summary with stop_reason "disk_low" and
exits 3. SIGINT/SIGTERM stop the same way (exit 130). Exit 0 = every planned tile done/skipped, 1 = some failed.
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import signal
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo.common import CACHE, GLOBAL_ROOT, apply_std, canonical_json, parse_tile, utc_now  # noqa: E402

SOURCES = ("cudem", "3dep", "osm", "water", "lidar")
MANIFEST = GLOBAL_ROOT / "bay_manifest.json"
STATUS_DIR = CACHE / "status"
GLOBAL_TILE = "_global"
DEFAULT_MIN_FREE_GB = 60.0
DISK_PATH = "/"


# ---------------------------------------------------------------- fetchers (late-bound so tests can mock them)
def _fetch(source: str, tile_id: str):
    from kaiju_geo import fetch
    if source == "cudem":
        return fetch.fetch_cudem(parse_tile(tile_id))
    if source == "3dep":
        return fetch.fetch_3dep(parse_tile(tile_id))
    if source == "osm":
        return fetch.fetch_osm_tile(parse_tile(tile_id))
    if source == "water":
        fetch.fetch_osm_water()
        return fetch.fetch_tides()
    if source == "lidar":
        import fetch_lidar_dsm
        rc = fetch_lidar_dsm.main(["--tile", tile_id])
        if rc:
            raise RuntimeError(f"fetch_lidar_dsm exit code {rc}")
        return None
    raise ValueError(source)


def _prepare(source: str) -> None:
    """One-time work before the pool starts (osm: the NorCal PBF every tile extract reads)."""
    if source == "osm":
        from kaiju_geo import fetch
        fetch.fetch_osm_pbf()


# ---------------------------------------------------------------- planning
def load_manifest(path: Path = MANIFEST) -> list[dict]:
    return json.loads(Path(path).read_text(encoding="utf-8"))["tiles"]


def classify(source: str, t: dict) -> str | None:
    """None = fetch this tile for `source`, else the skip reason."""
    cls, src = t.get("class"), t.get("sources", {})
    if cls == "outside":
        return "outside"
    if source == "cudem":
        return None if src.get("cudem") else "no_cudem"
    if source == "3dep":
        if cls == "ocean_only":
            return "ocean_only"
        return None if src.get("dep3_1m") else "no_3dep_1m"
    if source == "osm":
        return None if src.get("osm") else "ocean_only"
    if source == "lidar":
        if not t.get("osm_buildings"):
            return "no_buildings"
        return None if src.get("lidar_ept") else "no_lidar_ept"
    raise ValueError(source)


def read_status(path: Path) -> dict[str, dict]:
    """Last record per tile; tolerates a torn last line from a killed run."""
    last: dict[str, dict] = {}
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            try:
                r = json.loads(line)
            except json.JSONDecodeError:
                continue
            if "tile" in r and "status" in r:
                last[r["tile"]] = r
    return last


def make_plan(source: str, tiles: list[dict], last: dict[str, dict], retry_failed: bool = True,
              only: set[str] | None = None, limit: int | None = None):
    """-> (to_run ids, new skip records [(id, reason)], counters of tiles left untouched)."""
    run, skips, noop = [], [], {"done": 0, "skipped": 0, "failed_kept": 0}
    if source == "water":
        rows = [{"id": GLOBAL_TILE, "class": "global", "sources": {}}]
        reasons = {GLOBAL_TILE: None}
    else:
        rows = [t for t in tiles if only is None or t["id"] in only]
        reasons = {t["id"]: classify(source, t) for t in rows}
    for t in rows:
        tid, why, prev = t["id"], reasons[t["id"]], last.get(t["id"])
        if why is not None:
            if prev and prev["status"] == "skipped" and prev.get("reason") == why:
                noop["skipped"] += 1
            else:
                skips.append((tid, why))
            continue
        if prev and prev["status"] == "done":
            noop["done"] += 1
        elif prev and prev["status"] == "failed" and not retry_failed:
            noop["failed_kept"] += 1
        else:
            run.append(tid)
    if limit is not None:
        run = run[:limit]
    return run, skips, noop


# ---------------------------------------------------------------- driver
class Driver:
    def __init__(self, source: str, status_dir: Path = STATUS_DIR, workers: int = 4,
                 min_free_gb: float = DEFAULT_MIN_FREE_GB, fetcher=None, preparer=None, disk_usage=None):
        self.source, self.dir, self.workers, self.min_free = source, Path(status_dir), max(1, workers), min_free_gb * 1e9
        self.fetcher = fetcher or _fetch
        self.preparer = preparer or _prepare
        self.disk_usage = disk_usage or (lambda: shutil.disk_usage(DISK_PATH).free)
        self.log = self.dir / f"{source}.jsonl"
        self.summary_path = self.dir / f"{source}.summary.json"
        self.lock = threading.Lock()
        self.stop = threading.Event()
        self.stop_reason: str | None = None
        self.counts = {"done": 0, "failed": 0, "skipped": 0}
        self.failures: dict[str, str] = {}

    def _append(self, rec: dict) -> None:
        rec = {"tile": rec["tile"], "status": rec["status"], **{k: v for k, v in rec.items() if k not in ("tile", "status")},
               "utc": utc_now()}
        with self.lock:
            with open(self.log, "a", encoding="utf-8") as f:
                f.write(json.dumps(rec, sort_keys=True) + "\n")

    def _request_stop(self, reason: str) -> None:
        with self.lock:
            if self.stop_reason is None:
                self.stop_reason = reason
        self.stop.set()

    def _disk_ok(self) -> bool:
        free = self.disk_usage()
        if free < self.min_free:
            self._request_stop(f"disk_low: {free / 1e9:.1f} GB free < {self.min_free / 1e9:.0f} GB")
            return False
        return True

    def _one(self, tid: str) -> None:
        if self.stop.is_set() or not self._disk_ok():
            return  # left pending: no record
        t0 = time.time()
        try:
            self.fetcher(self.source, tid)
        except BaseException as e:  # noqa: BLE001  (a failed tile must not stop the run)
            reason = f"{type(e).__name__}: {e}".replace("\n", " ")[:300]
            self._append({"tile": tid, "status": "failed", "reason": reason, "secs": round(time.time() - t0, 1)})
            with self.lock:
                self.counts["failed"] += 1
                self.failures[tid] = reason
            if isinstance(e, (KeyboardInterrupt, SystemExit)):
                self._request_stop("interrupted")
            return
        self._append({"tile": tid, "status": "done", "secs": round(time.time() - t0, 1)})
        with self.lock:
            self.counts["done"] += 1
            n = self.counts["done"] + self.counts["failed"]
        if n % 25 == 0:
            self.write_summary(final=False)

    def write_summary(self, final: bool, extra: dict | None = None) -> dict:
        last = read_status(self.log)
        tally = {"done": 0, "failed": 0, "skipped": 0}
        reasons: dict[str, int] = {}
        failed = {}
        for tid, r in last.items():
            tally[r["status"]] = tally.get(r["status"], 0) + 1
            if r["status"] == "skipped":
                reasons[r.get("reason", "")] = reasons.get(r.get("reason", ""), 0) + 1
            if r["status"] == "failed":
                failed[tid] = r.get("reason", "")
        s = {"source": self.source, "updated_utc": utc_now(), "final": final, "stop_reason": self.stop_reason,
             "tiles_recorded": len(last), "status": tally, "skipped_reasons": reasons,
             "failed": dict(sorted(failed.items())[:50]), "failed_total": len(failed),
             "free_gb": round(self.disk_usage() / 1e9, 1), "min_free_gb": self.min_free / 1e9,
             "workers": self.workers}
        s.update(extra or {})
        self.dir.mkdir(parents=True, exist_ok=True)
        tmp = self.summary_path.with_name(self.summary_path.name + ".tmp")
        tmp.write_text(canonical_json(s, indent=1) + "\n", encoding="utf-8")
        os.replace(tmp, self.summary_path)
        return s

    def run(self, tiles: list[dict], retry_failed: bool = True, only: set[str] | None = None,
            limit: int | None = None, dry_run: bool = False) -> int:
        self.dir.mkdir(parents=True, exist_ok=True)
        last = read_status(self.log)
        run, skips, noop = make_plan(self.source, tiles, last, retry_failed, only, limit)
        print(f"[fetch_bay:{self.source}] to fetch {len(run)}, new skips {len(skips)}, already done {noop['done']}, "
              f"already skipped {noop['skipped']}, failed kept {noop['failed_kept']}", flush=True)
        if dry_run:
            return 0
        for tid, why in skips:
            self._append({"tile": tid, "status": "skipped", "reason": why})
            self.counts["skipped"] += 1
        if run and self._disk_ok():
            self.preparer(self.source)
        if run and not self.stop.is_set():
            with ThreadPoolExecutor(max_workers=self.workers) as ex:
                futs = [ex.submit(self._one, tid) for tid in run]
                for f in futs:
                    f.result()
        pending = len(run) - self.counts["done"] - self.counts["failed"]
        s = self.write_summary(final=True, extra={"this_run": dict(self.counts), "pending_left": max(pending, 0)})
        print(f"[fetch_bay:{self.source}] done {s['status']['done']} failed {s['status']['failed']} "
              f"skipped {s['status']['skipped']}; pending {max(pending, 0)}; stop_reason {self.stop_reason}", flush=True)
        if self.stop_reason and self.stop_reason.startswith("disk_low"):
            return 3
        if self.stop_reason:
            return 130
        return 1 if s["status"]["failed"] else 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--source", required=True, choices=SOURCES)
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--tile", action="append", help="restrict to this tile id (repeatable)")
    ap.add_argument("--limit", type=int, help="fetch at most N pending tiles this run")
    ap.add_argument("--no-retry-failed", action="store_true", help="leave tiles whose last status is failed alone")
    ap.add_argument("--min-free-gb", type=float, default=DEFAULT_MIN_FREE_GB)
    ap.add_argument("--manifest", default=str(MANIFEST))
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--offline", action="store_true", help="forbid all network access (cache must be complete)")
    a = ap.parse_args(argv)
    apply_std(a)
    d = Driver(a.source, workers=a.workers, min_free_gb=a.min_free_gb)
    for sig in (signal.SIGINT, signal.SIGTERM):
        signal.signal(sig, lambda *_: d._request_stop("interrupted"))
    tiles = [] if a.source == "water" else load_manifest(Path(a.manifest))
    return d.run(tiles, retry_failed=not a.no_retry_failed, only=set(a.tile) if a.tile else None,
                 limit=a.limit, dry_run=a.dry_run)


if __name__ == "__main__":
    sys.exit(main())
