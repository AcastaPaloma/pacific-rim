"""Consistency check of the full-ID QP at the exact reference state (contacts = schedule): forces, torques and qdd should reproduce the reference."""
import sys, pathlib; sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent)); sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import numpy as np, planar_model as P
from controller_fid import FIDWalker
np.set_printoptions(linewidth=220, suppress=True, precision=2)
kw = dict(a.split("=") for a in sys.argv[1:]); rp = kw.pop("ref", None); m = P.PlanarModel(); ref = P.load_reference(rp, m); kw = {k: float(v) for k, v in kw.items()}   # ref=path (9.3b-3)
worst = 0
for k in range(0, ref.N):
    c = FIDWalker(ref, m, **kw); c(0, ref.q[0], ref.qd[0], -1)
    c.inc[:] = c.flags[k]; c.f_in[:] = 5 * m.W; c.t_in[:] = -10; c.Fprev = ref.F[k].copy(); c.F_meas = ref.F[k].copy(); c.t_anchor = -k * ref.h; c.t_rel[:] = -10
    u = c(0.0, ref.q[k], ref.qd[k], 0); L = c.log
    eu = (u - ref.u_json[k]) / m.LIM; eF = np.abs(L["F"][0] - ref.F[k]).max() / m.W; worst = max(worst, np.abs(eu).max())
    print(f"k={k:2d} S={format(int(L['loaded'][0]), '04b')[::-1]} du/LIM={eu} |dF|/W={eF:.3f} |dqdd|={np.abs(L['qdd'][0] - ref.qdd[k]).max():6.2f} (ref {np.abs(ref.qdd[k]).max():5.1f}) st={L['status2'][0]} act={L['u_act'][0]}")
print("worst du/LIM", worst)
