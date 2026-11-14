#!/usr/bin/env python
"""Fetch layer: terrain+seabed, NOAA NCEI CUDEM 1/9 arc-second topobathy windows for a tile.

Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/fetch_cudem.py --tile L0_30_36 [--offline]
"""
import argparse, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo.common import apply_std, parse_tile, std_args  # noqa: E402
from kaiju_geo import fetch  # noqa: E402


def main(argv=None):
    ap = std_args(argparse.ArgumentParser(description=__doc__.splitlines()[0]))
    ap.add_argument("--tile", action="append", required=True, help="tile id, e.g. L0_30_36 (repeatable)")
    a = ap.parse_args(argv)
    apply_std(a)
    for t in a.tile:
        fetch.fetch_cudem(parse_tile(t))
    return 0


if __name__ == "__main__":
    sys.exit(main())
