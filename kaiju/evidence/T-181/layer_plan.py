"""T-181 engine-free study (design note: Assets/Game/Rendering/Pixel/MODULE.md, "Layer slabs").
1. Slab boundaries at haze band edges for the five committed profiles (independent mirror of PixelLayerPlan.TryBuild).
2. C11 lock tolerance: R-131 c11_sim layered scheme with the slab reference depth off the surface depth by a relative error e,
   and the same with the T-180 depth-aware shift inside the slab (does combining help?).
Run: /opt/miniconda3/envs/kaiju/bin/python evidence/T-181/layer_plan.py  (writes evidence/T-181/layer_plan.json)."""
import json, math, os
import numpy as np
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
tun = json.load(open(os.path.join(ROOT, "Assets/Game/Config/Rendering/PaletteProfileTuning.json")))
IDS = ["warm_rust_sepia", "warm_amber_haze", "slate_teal", "lavender_peach", "slate_crimson"]
REFS = [124.2, 664.874, 10353.0]          # R-131 class depths: near, rig reference (T1), far
TANH = math.tan(math.radians(37.5)); TANV = TANH * 270 / 480   # 75 deg hFOV, 480x270 visible internal
OVERLAP, HYST = 0.10, 0.25

def prof(i):
    t = dict(tun["defaults"]); t.update(tun["profiles"].get(i, {})); return t
def edge(t, j):
    n = int(t["hazeBands"]); u = j / (n - 1); g = t["hazeCurveGamma"]
    return t["hazeStartM"] * (t["hazeFullM"] / t["hazeStartM"]) ** (u if g == 1.0 else u ** (1 / g))
def level(t, d):
    n = int(t["hazeBands"])
    if d <= t["hazeStartM"]: return 0.0
    x = min(1.0, math.log(d / t["hazeStartM"]) / math.log(t["hazeFullM"] / t["hazeStartM"]))
    return (n - 1) * x ** t["hazeCurveGamma"]
RAY = math.sqrt(1 + TANH ** 2 + TANV ** 2)
def boundary(t, za, zb):
    lo, hi = za * RAY * (1 + OVERLAP), zb / (1 + OVERLAP); ideal = math.sqrt(lo * hi)
    c = [(abs(math.log(edge(t, j) / ideal)), j) for j in range(1, int(t["hazeBands"])) if lo < edge(t, j) < hi]
    if not c: return ideal, -1, lo, hi
    j = min(c)[1]; return edge(t, j), j, lo, hi
plan = {}
for i in IDS:
    t = prof(i); rows = []
    for a in range(2):
        e, j, lo, hi = boundary(t, REFS[a], REFS[a + 1])
        rows.append({"boundary_m": e, "band": j, "admissible_m": [lo, hi], "level_below": math.floor(level(t, e * 0.9999)), "level_above": math.floor(level(t, e * 1.0001))})
    plan[i] = rows

K = 4; ZREF = 664.874; FR = np.arange(180); C = 0.0902 * FR + 0.6153; PH = (np.arange(16) + 0.37) / 16
def stats(fn, rho):
    w = []
    for phi in PH:
        xs = []; cm = []
        for c in C:
            b, s = fn(c, rho, phi); xs.append(K * b - s); cm.append(-K * rho * c)
        xs = np.array(xs); cm = np.array(cm); w.append(np.abs((xs - xs[0]) - (cm - cm[0])).max())
    return [float(max(w)), float(np.median(w))]
def layered(r, ds=False):
    def f(c, rho, phi):
        Sl = np.ceil(c * r) / r; rem = c - Sl; u = rho * (0 - Sl) + phi
        return np.round(u), np.round(K * (rho if ds else r) * rem)
    return f
ERR = [-0.2, -0.1, -0.05, -0.02, -0.01, -0.005, -0.0025, -0.001, 0.0, 0.001, 0.0025, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2]
tol = {}
for name, z in (("near", 124.2), ("reference", 663.0), ("far", 10353.0)):
    tol[name] = [{"rel_error": e, "layer": stats(layered(ZREF / (z * (1 + e))), ZREF / z),
                  "layer_plus_depth_shift": stats(layered(ZREF / (z * (1 + e)), True), ZREF / z)} for e in ERR]
# the T1 rig main reference (664.874 m) against the reference-class edge (663.0 m): the as-configured case
tol["reference_at_rig_depth"] = stats(layered(1.0), ZREF / 663.0)
out = {"refs_m": REFS, "tan_half": [TANH, TANV], "corner_ray": RAY, "overlap": OVERLAP, "plan": plan, "c11_lock_tolerance": tol}
json.dump(out, open(os.path.join(os.path.dirname(__file__), "layer_plan.json"), "w"), indent=1)
for i in IDS: print(i, [(round(r["boundary_m"], 1), r["band"], r["level_below"], r["level_above"]) for r in plan[i]])
for name in ("near", "reference", "far"):
    print(name, [(r["rel_error"], round(r["layer"][0], 2), round(r["layer"][1], 2)) for r in tol[name]])
print("reference at rig depth", tol["reference_at_rig_depth"])
