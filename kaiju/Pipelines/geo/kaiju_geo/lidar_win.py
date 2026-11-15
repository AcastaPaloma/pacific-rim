"""T-104: windowed first-return reads from USGS 3DEP EPT projects (generalises lidar_ref_v11 / measure_ggb inputs).
`read_window(project, bounds_baytm)` -> dict of float64 arrays x, z (BayTM) and h (NAVD88 m), plus n_return and cls; cached as
small npz under CACHE/lidar_win (git-ignored). First returns only, noise classes 7/18/22 dropped (same filter as lidar_ref_v11)."""
from __future__ import annotations

import hashlib
import json

import numpy as np

from .common import CACHE, NetworkForbidden, baytm_crs, is_offline, transformer

EPT_BASE = "https://s3-us-west-2.amazonaws.com/usgs-lidar-public/{p}/ept.json"
EXPR = "ReturnNumber == 1 && Classification != 7 && Classification != 18 && Classification != 22"


def bounds_3857(b, margin=0.0):
    x0, z0, x1, z1 = b
    t = transformer(baytm_crs(), "EPSG:3857")
    xs, ys = zip(*[t.transform(x, z) for x in (x0 - margin, x1 + margin) for z in (z0 - margin, z1 + margin)])
    return min(xs), max(xs), min(ys), max(ys)


def cache_path(project, b):
    key = hashlib.sha256(f"{project}|{[round(float(v), 1) for v in b]}".encode()).hexdigest()[:16]
    return CACHE / "lidar_win" / f"{project}__{key}.npz"


def read_window(project, b, resolution=None):
    """b = (x0, z0, x1, z1) BayTM. Returns arrays (possibly empty)."""
    dest = cache_path(project, b)
    if dest.exists():
        z = np.load(dest)
        return {k: z[k] for k in z.files}
    if is_offline():
        raise NetworkForbidden(f"{dest.name} missing from cache and offline")
    import pdal
    xa, xb, ya, yb = bounds_3857(b)
    st = {"type": "readers.ept", "filename": EPT_BASE.format(p=project), "bounds": f"([{xa:.2f},{xb:.2f}],[{ya:.2f},{yb:.2f}])", "threads": 8}
    if resolution:
        st["resolution"] = resolution
    pipe = [st, {"type": "filters.expression", "expression": EXPR},
            {"type": "filters.reprojection", "out_srs": "EPSG:26910+5703"}]
    p = pdal.Pipeline(json.dumps(pipe))
    try:
        n = p.execute()
    except RuntimeError:
        n = 0
    if n == 0 or not p.arrays:
        out = {k: np.zeros(0) for k in ("x", "z", "h", "n_return", "cls")}
    else:
        a = p.arrays[0]
        bx, bz = transformer("EPSG:26910", baytm_crs()).transform(a["X"], a["Y"])
        out = {"x": np.asarray(bx, float), "z": np.asarray(bz, float), "h": a["Z"].astype(np.float64),
               "n_return": a["NumberOfReturns"].astype(np.int8), "cls": a["Classification"].astype(np.int8)}
    dest.parent.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(dest.with_name(dest.name + ".part.npz"), **out)
    dest.with_name(dest.name + ".part.npz").replace(dest)
    return out
