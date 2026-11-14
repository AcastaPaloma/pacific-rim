#!/usr/bin/env python
"""Fetch layer: water, OSM water polygons (osmdata) and NOAA CO-OPS 9414290 tide datums.

Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/fetch_water.py [--offline]
"""
import argparse, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo.common import apply_std, parse_tile, std_args  # noqa: E402
from kaiju_geo import fetch  # noqa: E402


def main(argv=None):
    ap = std_args(argparse.ArgumentParser(description=__doc__.splitlines()[0]))
    a = ap.parse_args(argv)
    apply_std(a)
    fetch.fetch_osm_water()
    fetch.fetch_tides()
    return 0


if __name__ == "__main__":
    sys.exit(main())
