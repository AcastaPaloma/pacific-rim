#!/usr/bin/env python
"""Fetch layer: faults, USGS Qfaults layers 4 + 5 over the Bay world box (global layer).

Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/fetch_faults.py [--offline]
"""
import argparse, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo.common import apply_std, parse_tile, std_args  # noqa: E402
from kaiju_geo import fetch  # noqa: E402


def main(argv=None):
    ap = std_args(argparse.ArgumentParser(description=__doc__.splitlines()[0]))
    a = ap.parse_args(argv)
    apply_std(a)
    fetch.fetch_faults()
    return 0


if __name__ == "__main__":
    sys.exit(main())
