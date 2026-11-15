"""T-111b: the RTIN mesh rasteriser of scripts/measure_lod_error.py (measurement tool, no pipeline change)."""
import importlib.util
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from kaiju_geo import lod  # noqa: E402

_spec = importlib.util.spec_from_file_location("measure_lod_error", os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "scripts", "measure_lod_error.py"))
mle = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(mle)


def field(n=65, seed=3):
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:n, 0:n]
    return 5 + 0.1 * x + 0.05 * y + 2.0 * np.sin(x / 7.0) + rng.normal(0, 0.2, (n, n))


def test_rasteriser_reproduces_the_field_when_nothing_may_be_dropped():
    h = field()
    mi, E = lod.rtin_error_pyramid(h)
    R = mle.rtin_render_heights(h, 0.0, mi, E)
    assert np.abs(R - h).max() < 1e-4                      # float32 error pyramid, all nodes are vertices


def test_plane_is_exact_at_any_threshold_and_triangle_count_matches():
    n = 65
    y, x = np.mgrid[0:n, 0:n]
    h = 3.0 + 0.2 * x - 0.1 * y
    mi, E = lod.rtin_error_pyramid(h)
    R = mle.rtin_render_heights(h, 0.5, mi, E)
    assert np.abs(R - h).max() < 1e-6 and lod.rtin_count(mi, E, 0.5) == 2


def test_error_grows_with_threshold_and_every_node_is_covered():
    h = field()
    mi, E = lod.rtin_error_pyramid(h)
    prev_n, prev_e = None, -1.0
    for thr in (0.1, 0.5, 1.5):
        R = mle.rtin_render_heights(h, thr, mi, E)
        n = lod.rtin_count(mi, E, thr)
        e = float(np.abs(R - h).max())
        assert not np.isnan(R).any() and e >= prev_e and (prev_n is None or n <= prev_n)
        prev_n, prev_e = n, e
