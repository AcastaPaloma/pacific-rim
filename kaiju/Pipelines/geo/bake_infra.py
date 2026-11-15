#!/usr/bin/env python
"""T-105: F-044 `infrastructure` layer (infra@v1) for the Bay tiles, from the cached NorCal PBF (no download).
Usage: KAIJU_GEO_CACHE=<integration cache> /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/bake_infra.py
        [--out DIR] [--tile L0_30_36 ...] [--force] [--dry-run] [--min-free-gb 25]
One pass over the PBF (cached as CACHE/infra/infra_osm_v1.json, keyed by the input fingerprint), then per-tile sidecars
`infrastructure.geojson` + `infrastructure.manifest.json` in <out>/L0/<ii>_<jj>/ for every tile that OWNS features and is already baked.
Tiles without features are not touched; the geo_tile@v1 manifest is not edited, so no L0 or LOD tile is re-baked (up to date = same bytes).
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo import infra  # noqa: E402
from kaiju_geo.common import (CACHE, CONFIG, apply_std, canonical_json, load_config, parse_tile, read_meta, sha256_file,  # noqa: E402
                              std_args, write_if_changed)

OUT_ROOT = CACHE / "bake" / "tiles" / "v1"
PBF = CACHE / "osm" / "norcal-latest.osm.pbf"
CACHE_FILE = CACHE / "infra" / "infra_osm_v1.json"


def bridge_slugs() -> dict:
    """OSM way id ('w123') -> landmark slug, from the T-104 registry (bridges that are also F-041 landmarks)."""
    p = CONFIG / "landmarks_bay_registry.json"
    if not p.exists():
        return {}
    reg = json.loads(p.read_text())
    return {f"w{w}": b["slug"] for b in reg.get("bridges", []) for w in b.get("osm", {}).get("ways", [])}


def source_entry(meta: dict) -> dict:
    s = load_config("sources.json")["osm"]
    return {"product": s["product"], "item": "norcal-latest.osm.pbf", "url": meta["url"], "licence": s["licence"],
            "attribution": s["attribution"], "fetched_utc": meta["fetched_utc"], "source_sha256": meta["sha256"],
            "osm_base_utc": meta.get("osm_base_utc"), "attribution_notice": s["attribution_notice"],
            "selection": "config/infra.json (R-100 section 5)"}


def main(argv=None):
    ap = std_args(argparse.ArgumentParser(description=__doc__.splitlines()[0]))
    ap.add_argument("--out", default=None, help="tile root (default <cache>/bake/tiles/v1)")
    ap.add_argument("--tile", action="append", default=None, help="only these L0 tiles")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--min-free-gb", type=float, default=25.0)
    a = ap.parse_args(argv)
    apply_std(a)
    out = os.path.abspath(a.out) if a.out else str(OUT_ROOT)
    meta = read_meta(PBF)
    if meta is None:
        raise SystemExit(f"OSM cache missing: {PBF}")
    slugs = bridge_slugs()
    fp = infra.fingerprint(meta["sha256"], slugs)
    if CACHE_FILE.exists() and not a.force and json.loads(CACHE_FILE.read_text()).get("fingerprint") == fp:
        feats = json.loads(CACHE_FILE.read_text())["features"]
        print(f"[infra] extraction cached ({len(feats)} features)")
    else:
        feats = infra.extract(PBF, slugs)
        CACHE_FILE.parent.mkdir(parents=True, exist_ok=True)
        write_if_changed(CACHE_FILE, (canonical_json({"fingerprint": fp, "features": feats}) + "\n").encode())
    tiles = infra.by_tile(feats)
    want = set(a.tile) if a.tile else None
    src = source_entry(meta)
    stat = {"written": 0, "unchanged": 0, "no_tile_dir": 0, "removed": 0, "features": 0, "kind_counts": {}}
    for tid in sorted(tiles):
        if want and tid not in want:
            continue
        d = os.path.join(out, parse_tile(tid).folder)
        if not os.path.isdir(d):
            stat["no_tile_dir"] += 1
            continue
        data, man = infra.tile_files(tid, tiles[tid], src, fp)
        stat["features"] += len(tiles[tid])
        for f in tiles[tid]:
            k = f["properties"]["kind"]
            stat["kind_counts"][k] = stat["kind_counts"].get(k, 0) + 1
        if a.dry_run:
            continue
        if shutil.disk_usage(out).free < a.min_free_gb * 2**30:
            raise SystemExit(f"stopping: less than {a.min_free_gb} GB free")
        ch = write_if_changed(Path(d) / infra.FILE, data)
        ch = write_if_changed(Path(d) / infra.MANIFEST_FILE, man) or ch
        stat["written" if ch else "unchanged"] += 1
    if not want and not a.dry_run:  # tiles that lost all features: drop stale sidecars
        for root, _dirs, files in os.walk(os.path.join(out, "L0")):
            if infra.FILE in files and "L0_" + os.path.basename(root) not in tiles:
                for fn in (infra.FILE, infra.MANIFEST_FILE):
                    os.remove(os.path.join(root, fn))
                stat["removed"] += 1
    stat["tiles_with_features"] = len([t for t in tiles if not want or t in want])
    print(json.dumps(stat, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
