#!/usr/bin/env python
"""T-103: read the OLDER 3DEP 1 m projects for the tiles whose newest project leaves holes (config/dep3_fill.json).

Per tile: kaiju_geo.fetch.fetch_3dep(tile, fill=True) -> windowed reads (same windows as the primary fetch) of every older
project of each UTM key whose newest window has nodata. Resumable (status `cache/status/dep3_fill.jsonl`, last line per tile
wins, done tiles are skipped), disk guard (clean stop, exit 3 when free space on the cache volume drops below --min-free-gb),
SIGINT/SIGTERM stop cleanly. Usage (cache from the integration checkout):
  KAIJU_GEO_CACHE=<integration>/Pipelines/geo/cache /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/fetch_dep3_fill.py [--tile L0_59_17] [--workers 4] [--min-free-gb 40]
"""
import argparse
import json
import os
import shutil
import signal
import sys
import time
from concurrent.futures import FIRST_COMPLETED, ThreadPoolExecutor, wait

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo import fetch  # noqa: E402
from kaiju_geo.common import CACHE, canonical_json, load_config, parse_tile, utc_now  # noqa: E402

STATUS = CACHE / "status" / "dep3_fill.jsonl"
SUMMARY = CACHE / "status" / "dep3_fill.summary.json"


def read_status():
    last = {}
    if STATUS.exists():
        for ln in STATUS.read_text().splitlines():
            try:
                r = json.loads(ln)
            except json.JSONDecodeError:
                continue
            last[r["tile"]] = r
    return last


def one(tid):
    t0 = time.time()
    items = fetch.fetch_3dep(parse_tile(tid), fill=True)
    fills = [m for m in items if m.get("fill")]
    return {"status": "done", "primary": len(items) - len(fills), "fill_items": len(fills),
            "fill_bytes": sum(m.get("bytes", 0) for m in fills), "secs": round(time.time() - t0, 1)}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--tile", action="append")
    ap.add_argument("--workers", type=int, default=4)
    ap.add_argument("--min-free-gb", type=float, default=40.0)
    ap.add_argument("--limit", type=int)
    a = ap.parse_args(argv)
    tiles = load_config("dep3_fill.json")["tiles"]
    if a.tile:
        tiles = [t for t in tiles if t in set(a.tile)]
    st = read_status()
    todo = [t for t in tiles if st.get(t, {}).get("status") != "done"]
    if a.limit:
        todo = todo[:a.limit]
    STATUS.parent.mkdir(parents=True, exist_ok=True)
    stop = {"why": None}
    for sg in (signal.SIGINT, signal.SIGTERM):
        signal.signal(sg, lambda *_: stop.update(why="interrupted"))
    print(f"[dep3_fill] {len(tiles)} tiles, {len(todo)} to do, workers {a.workers}", flush=True)
    done = failed = 0
    it = iter(todo)
    with ThreadPoolExecutor(a.workers) as ex:
        live = {}
        while True:
            while len(live) < a.workers and not stop["why"]:
                free = shutil.disk_usage(CACHE).free / 1e9
                if free < a.min_free_gb:
                    stop["why"] = f"disk guard: {free:.1f} GB free < {a.min_free_gb}"
                    break
                tid = next(it, None)
                if tid is None:
                    break
                live[ex.submit(one, tid)] = tid
            if not live:
                break
            fin, _ = wait(live, return_when=FIRST_COMPLETED)
            for f in fin:
                tid = live.pop(f)
                try:
                    r = f.result()
                    done += 1
                except Exception as e:  # noqa: BLE001
                    r = {"status": "failed", "error": f"{type(e).__name__}: {e}"[:300]}
                    failed += 1
                with open(STATUS, "a") as fh:
                    fh.write(canonical_json({"tile": tid, "utc": utc_now(), **r}) + "\n")
                print(f"[dep3_fill] {tid} {r['status']} {r.get('fill_items', '')}", flush=True)
    final = read_status()
    tally = {}
    for r in final.values():
        tally[r["status"]] = tally.get(r["status"], 0) + 1
    SUMMARY.write_text(canonical_json({"updated_utc": utc_now(), "tiles": len(tiles), "status": tally,
                                       "fill_bytes": sum(r.get("fill_bytes", 0) for r in final.values()),
                                       "stop_reason": stop["why"], "this_run": {"done": done, "failed": failed}}, indent=1) + "\n")
    return 3 if (stop["why"] or "").startswith("disk") else (0 if not failed else 1)


if __name__ == "__main__":
    raise SystemExit(main())
