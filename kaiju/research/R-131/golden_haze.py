"""T-176 step T-176/T-178: golden rung L* and grey-albedo haze renders per profile (R-131 pixmodel), used by HazeRungModelTests."""
import json
from pixmodel import Profile
from common import PROFILES
REC = {"warm_rust_sepia": dict(hazeStartM=64.3, hazeFullM=21160.0, hazeBands=64, hazeCurveGamma=1.0),
       "warm_amber_haze": dict(hazeStartM=79.2, hazeFullM=38400.0, hazeBands=32, hazeCurveGamma=1.0),
       "slate_crimson": dict(hazeStartM=155.4, hazeFullM=16800.0, hazeBands=24, hazeCurveGamma=0.72)}
D = [40, 150, 300, 926, 998, 1150, 1500, 3000, 10000, 30000]
out = {}
for pid in PROFILES:
    P = Profile(pid, REC.get(pid))
    out[pid] = {"rungL": [round(float(x[0]), 3) for x in P.rung_lab], "bands": P.bands,
                "ground024": [round(float(P.render_grey(0.24, d)), 3) for d in D], "box0272": [round(float(P.render_grey(0.272, d)), 3) for d in D]}
    print(pid, out[pid]["rungL"])
json.dump({"distances": D, "profiles": out}, open("golden_haze.json", "w"), indent=1)
