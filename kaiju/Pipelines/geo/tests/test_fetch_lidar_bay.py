"""T-103b: pure helpers of fetch_lidar_bay (runs of 128 m cells, project order, coverage)."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np  # noqa: E402
from shapely.geometry import box  # noqa: E402

import fetch_lidar_bay as F  # noqa: E402


def test_runs_split_by_gap_and_max_run():
    cells = {(0, 1), (0, 2), (0, 3), (0, 4), (0, 5), (0, 9), (2, 0)}
    runs = F.runs_of(cells, 4)
    assert (0, 1, 4) in runs and (0, 5, 5) in runs and (0, 9, 9) in runs and (2, 0, 0) in runs
    assert sum(b - a + 1 for _, a, b in runs) == len(cells)


def test_project_order_newest_first():
    assert F.project_order(["CA_X_2010", "CA_Y_2021", "CA_Z_2018"]) == ["CA_Y_2021", "CA_Z_2018", "CA_X_2010"]


def test_cover_of_counts_cells_with_returns():
    cnt = np.zeros((F.N, F.N), np.int32)
    cnt[10:20, 10:20] = 3
    grid = {"bounds": [0.0, 0.0, 2048.0, 2048.0], "count": cnt}
    assert F.cover_of(box(9, 9, 21, 21), grid) == 1.0
    assert 0.0 < F.cover_of(box(5, 9, 21, 21), grid) < 1.0
    assert F.cover_of(box(100, 100, 101.5, 101.5), grid) is None
