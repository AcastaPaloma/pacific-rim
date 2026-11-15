#!/usr/bin/env python
"""M-100 bake reproducibility sample (research/R-100.md section 7, step 2): bake a set of Bay tiles twice in fresh processes into two
empty output roots, with different --threads and a different tile order, then compare every output file byte for byte.

Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/m100_check.py run [--tile L0_30_36 ...] [--per-class urban=2 rural=2 water_only=1 ocean_only=1]
           [--work DIR] [--report FILE] [--keep]
Rules: network forbidden (cache only, as the Bay driver); build_info.json is excluded (git revision, wall time); manifest.json is compared
through `bake.manifest_content` (the build block's built_utc / git are volatile metadata by DEF-T003-arch-1), every other file byte-identical.
A difference is a bug in the bake: there is no tolerance and the sample is never widened to hide it. The tile sample is a pure function of the
manifest fingerprint (seeded), so two people draw the same tiles."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import random
import shutil
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo.common import CACHE, GLOBAL_ROOT  # noqa: E402

EXCLUDED = ("build_info.json",)


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def stratified(tiles: list[dict], fingerprint: str, per_class: dict) -> list[str]:
    """Deterministic draw per coverage class: sorted ids, random.Random(seed from the manifest fingerprint)."""
    rng = random.Random(int(fingerprint[:16], 16))
    out = []
    for cls in sorted(per_class):
        ids = sorted(t["id"] for t in tiles if t.get("class") == cls)
        out += sorted(rng.sample(ids, min(per_class[cls], len(ids))))
    return out


def compare_trees(a: Path, b: Path, tile_dirs: list[str]) -> dict:
    """Byte-compare every file of each tile folder (relative like `L0/30_36`) except build_info.json; manifest.json via manifest_content."""
    from kaiju_geo import bake
    files, same, diff, only = 0, 0, [], []
    for d in tile_dirs:
        na = {f.name for f in (a / d).iterdir() if f.is_file()} if (a / d).is_dir() else set()
        nb = {f.name for f in (b / d).iterdir() if f.is_file()} if (b / d).is_dir() else set()
        for n in sorted((na | nb) - set(EXCLUDED)):
            files += 1
            if n not in na or n not in nb:
                only.append(f"{d}/{n}")
                continue
            if n == "manifest.json":
                ok = bake.manifest_content(json.loads((a / d / n).read_text())) == bake.manifest_content(json.loads((b / d / n).read_text()))
            else:
                ok = sha(a / d / n) == sha(b / d / n)
            if ok:
                same += 1
            else:
                diff.append(f"{d}/{n}")
    return {"files": files, "identical": same, "different": diff, "only_in_one": only, "result": "pass" if files and not diff and not only else "fail"}


def _pass(out: str, tiles: list[str], threads: int) -> int:
    """Child process: bake `tiles` in the given order into `out` with the Bay plan, offline (cache only)."""
    import bake_bay as BB
    from kaiju_geo import bake
    from kaiju_geo.common import parse_tile
    BB._init_worker(out, True)
    meta = {t["id"]: t for t in BB.load_tiles()}
    for tid in tiles:
        bake.bake(parse_tile(tid), force=True, threads=threads, out_root=Path(out), plan=BB.plan_of(meta[tid]))
        print(json.dumps({"tile": tid, "threads": threads}), flush=True)
    return 0


def run(a) -> int:
    mdoc = json.loads((GLOBAL_ROOT / "bay_manifest.json").read_text())
    tiles = list(a.tile or [])
    pc = {k: int(v) for k, v in (x.split("=") for x in (a.per_class or []))}
    tiles += stratified([t for t in mdoc["tiles"] if t.get("class") != "outside"], mdoc["fingerprint"], pc)
    tiles = list(dict.fromkeys(tiles))
    if not tiles:
        raise SystemExit("no tiles: give --tile and/or --per-class")
    work = Path(a.work)
    if work.exists():
        shutil.rmtree(work)
    outs = [work / "run1", work / "run2"]
    plans = [(tiles, 1), (list(reversed(tiles)), 2)]
    t0 = time.time()
    procs = []
    for o, (order, thr) in zip(outs, plans):
        o.mkdir(parents=True)
        procs.append(subprocess.Popen([sys.executable, "-I", str(Path(__file__).resolve()), "_pass", "--out", str(o), "--threads", str(thr), *order],
                                      stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True))
    errs = [p.communicate()[1] for p in procs]
    if any(p.returncode for p in procs):
        print("bake pass failed:", *[e[-1500:] for e in errs], sep="\n")
        return 2
    from kaiju_geo.common import parse_tile
    dirs = [parse_tile(t).folder for t in tiles]
    rep = {"spec": "m100_sample@v1", "manifest_fingerprint": mdoc["fingerprint"], "tiles": tiles, "threads": [1, 2], "order": ["sorted-as-given", "reversed"],
           "seconds": round(time.time() - t0, 1), **compare_trees(outs[0], outs[1], dirs)}
    if a.report:
        Path(a.report).parent.mkdir(parents=True, exist_ok=True)
        Path(a.report).write_text(json.dumps(rep, indent=1) + "\n")
    print(json.dumps({k: rep[k] for k in ("result", "files", "identical", "different", "only_in_one", "seconds")}))
    if not a.keep:
        shutil.rmtree(work)
    return 0 if rep["result"] == "pass" else 1


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    r_ = sub.add_parser("run")
    r_.add_argument("--tile", action="append")
    r_.add_argument("--per-class", nargs="*", help="class=count, e.g. urban=2 rural=2 water_only=1 ocean_only=1")
    r_.add_argument("--work", required=True)
    r_.add_argument("--report")
    r_.add_argument("--keep", action="store_true")
    p_ = sub.add_parser("_pass")
    p_.add_argument("--out", required=True)
    p_.add_argument("--threads", type=int, default=1)
    p_.add_argument("tiles", nargs="+")
    a = ap.parse_args(argv)
    return run(a) if a.cmd == "run" else _pass(a.out, a.tiles, a.threads)


if __name__ == "__main__":
    sys.exit(main())
