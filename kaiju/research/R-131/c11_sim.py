"""C11 geometric model of the T1 translation sequence (eye70_tower, 180 frames, camera_right, 0.1 internal px/frame at the reference plane).
Units: texels at the reference plane (grid G = 1 ref texel); rho = z_ref / z (1 at the reference plane). k = 4 output px per texel.
Rendered edge (texel boundary) b = round(u + phi), u = rho*(X - S)/G with S the snapped camera; output x = k*b - shift; commanded = k*rho*(X - c)/G.
Error(i) = (x(i)-x(0)) - (cmd(i)-cmd(0)), max |.| over frames, over 16 edge phases phi (an edge's world offset is arbitrary), worst and median over phi."""
import numpy as np, json
K = 4; ZREF = 664.874
Z = {"near": 124.2, "reference": 663.0, "far": 10353.0}
FR = np.arange(180); C = 0.0902 * FR + 0.6153          # camera lateral position (ref texels); speed from the sidecar (0.36097 px/frame at the tower edge)
PH = (np.arange(16) + 0.37) / 16.0

def stats(fn, rho):
    """fn(i, c) -> (render_rho_state, shift_px): the scheme under test; returns max error px over frames for each phase."""
    worst = []
    for phi in PH:
        xs, cm = [], []
        for i, c in zip(FR, C):
            b, shift = fn(c, rho, phi)
            xs.append(K * b - shift); cm.append(K * rho * (0 - c) )   # X = 0 world offset; phase carried by phi
        xs = np.array(xs); cm = np.array(cm)
        worst.append(np.abs((xs - xs[0]) - (cm - cm[0])).max())
    return max(worst), float(np.median(worst))

def baseline(c, rho, phi):            # current: S on the ref grid (ceil), uniform whole-output-px shift of the ref remainder
    S = np.ceil(c); rem = c - S; u = rho * (0 - S) + phi
    return np.round(u), np.round(K * rem) * -1 * -1 if False else np.round(K * rem)
def finer(n):                         # snap grid G/n, same uniform shift
    def f(c, rho, phi):
        S = np.ceil(c * n) / n; rem = c - S; u = rho * (0 - S) + phi
        return np.round(u), np.round(K * rem)
    return f
def depth_shift(c, rho, phi):         # single snap at ref grid, per-pixel shift = parallax of its own depth (reprojection in Present)
    S = np.ceil(c); rem = c - S; u = rho * (0 - S) + phi
    return np.round(u), np.round(K * rho * rem)
def layered(rho_layer):               # per-layer snap: the surface sits in a layer whose reference depth is z_ref/rho_layer
    def f(c, rho, phi):
        r = rho_layer; Sl = np.ceil(c * r) / r        # layer grid = 1 texel at its own depth = 1/r ref texels... step in ref units = 1/r
        rem = c - Sl; u = rho * (0 - Sl) + phi
        return np.round(u), np.round(K * r * rem)
    return f
# NB sign: output = k*b - shift, commanded = -k*rho*c; render moves with S, so shift = k*rho_layer*rem (rem<=0) cancels (verified by the ref-plane row below).
res = {}
for name, fn in [("baseline_uniform_shift", baseline), ("depth_aware_shift_single_snap", depth_shift)] + [("finer_snap_n%d" % n, finer(n)) for n in (2, 4, 8)]:
    res[name] = {cls: stats(fn, Z and ZREF / z) for cls, z in Z.items()}
# per-layer snapping: layers exactly at the three class depths, and N log-spaced layers (worst class error over surface depth inside a layer)
res["layers_exact_at_class_depths(3)"] = {cls: stats(layered(ZREF / z), ZREF / z) for cls, z in Z.items()}
def logsp(N, zlo=30, zhi=30000):
    zs = np.exp(np.linspace(np.log(zlo), np.log(zhi), N)); return ZREF / zs
out = {}
for N in (3, 6, 12, 20, 28):
    rl = logsp(N); worst = 0; wmed = []
    for z in np.exp(np.linspace(np.log(30), np.log(30000), 90)):
        rho = ZREF / z; r = rl[np.argmin(np.abs(np.log(rl / rho)))]
        w, m = stats(layered(r), rho); worst = max(worst, w); wmed.append(m)
    out["N=%d" % N] = {"worst_px_any_depth": worst, "median_px": float(np.median(wmed))}
res["layers_log_spaced"] = out
json.dump(res, open("out_c11_sim.json", "w"), indent=1)
for k, v in res.items(): print(k, json.dumps(v))
