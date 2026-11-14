#!/usr/bin/env python
"""Fetch references (not shipped): NGS benchmarks (M-005.2) and the 3DEP lidar first-return DSM (M-005.7 R-ind).

Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/fetch_refs.py --tile L0_30_36 [--offline]
"""
import argparse, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo.common import apply_std, parse_tile, std_args  # noqa: E402
from kaiju_geo import fetch  # noqa: E402


def main(argv=None):
    ap = std_args(argparse.ArgumentParser(description=__doc__.splitlines()[0]))
    ap.add_argument("--tile", action="append", required=True, help="tile id, e.g. L0_30_36 (repeatable)")
    ap.add_argument("--skip-lidar", action="store_true")
    a = ap.parse_args(argv)
    apply_std(a)
    for t in a.tile:
        fetch.fetch_ngs(parse_tile(t))
        if not a.skip_lidar:
            fetch.fetch_lidar_dsm(parse_tile(t))
    return 0


if __name__ == "__main__":
    sys.exit(main())
