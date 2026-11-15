"""Print a per-0.1 s trace of a run_fid npz: phase, contact set, measured vs commanded normal force (m g), plant friction ratio, CoM z error, pitch error, limit activity."""
import sys, pathlib, numpy as np
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import planar_model as P
tag = sys.argv[1]; every = int(sys.argv[2]) if len(sys.argv) > 2 else 6; t0 = float(sys.argv[3]) if len(sys.argv) > 3 else 0; t1 = float(sys.argv[4]) if len(sys.argv) > 4 else 1e9
m = P.PlanarModel(); ref = P.load_reference(None, m); d = np.load(P.ROOT / f"Tools/GaitOpt/out/proto/{tag}.npz"); W = m.W
np.set_printoptions(precision=2, suppress=True, linewidth=250)
print("td", d["td_events"])
for k in range(0, len(d["t"]), every):
    t = d["t"][k]
    if t < t0 or t > t1: continue
    r = ref.at(d["phase"][k]); q = d["q"][k]
    fr = np.abs(d["Ft_plant"][k]) / np.maximum(0.4 * d["Fn_plant"][k], 1e-9) * (d["Fn_plant"][k] > 1e-3 * W)
    print(f"t={t:5.2f} ph={d['phase'][k] / ref.h:5.1f} S={int(d['loaded'][k]):04b}[::-1] fnm={d['fn_meas'][k] / W} fcmd={d['F'][k][:, 1] / W} fref={r['F'][:, 1] / W} fpl={d['Fn_plant'][k] / W} frpl={fr} "
          f"dz={d['com'][k][1] - r['com'][1]:5.2f} dth={np.rad2deg(q[2] - r['q'][2]):5.1f} ua={d['u_act'][k]} u/L={d['u'][k] / m.LIM} hipz={q[1]:5.2f}")
