#!/usr/bin/env python
"""T-020b: fetch the first-return grid used to fill default building heights in the bake.

Reuses lidar_ref_v11's EPT reading (3DEP CA_SanFrancisco_1_B23, first returns, noise classes dropped, 1 m cells:
p95, max, multi-return share, count) and writes the same cache file, Pipelines/geo/cache/lidar/rind_v11__<tile>.npz
(git-ignored), which bake.py reads when present. A tile outside the project's extent yields an all-empty grid and a
"lidar unavailable" note; the bake then skips the lidar fill for it.
Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/fetch_lidar_dsm.py --tile L0_26_38 [--offline]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402

import lidar_ref_v11  # noqa: E402
from kaiju_geo.common import CACHE, parse_tile  # noqa: E402


def coverage(path):
    """Share of 1 m cells with at least one first return."""
    z = np.load(path)
    return float(np.count_nonzero(z["count_10"]) / z["count_10"].size)


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    rc = lidar_ref_v11.main(argv)
    tid = argv[argv.index("--tile") + 1] if "--tile" in argv else "L0_30_36"
    dest = CACHE / "lidar" / f"rind_v11__{parse_tile(tid).id}.npz"
    if rc == 0 and dest.exists():
        cov = coverage(dest)
        print(f"[fetch_lidar_dsm] {tid}: {cov * 100:.1f} % of 1 m cells have first returns"
              + ("" if cov > 0.0 else " -> lidar unavailable for this tile (outside the 3DEP project?); the bake will skip the lidar fill"))
    return rc


if __name__ == "__main__":
    sys.exit(main())
