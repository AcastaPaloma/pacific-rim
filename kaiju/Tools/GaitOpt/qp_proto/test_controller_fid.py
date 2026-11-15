"""pytest for the 9.3b-4 controller_fid options. Run from the project root: /opt/miniconda3/envs/kaiju/bin/python -m pytest -q Tools/GaitOpt/qp_proto/test_controller_fid.py"""
import sys, pathlib
import numpy as np
HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE)); sys.path.insert(0, str(HERE.parent))
import planar_model as P, planar_sim as S
from controller_fid import FIDWalker
from run_fid import PRESETS, PushSim

REF = P.ROOT / "Tools/GaitOpt/out/stride_fr010_v2_n40p2.json"
M = P.PlanarModel(); R = P.load_reference(REF, M)


def test_onset_node_counts_as_loaded():
    i = 0; tref = R.ts                                    # A heel at node N: Fn = 0 by complementarity, loaded from node N+1
    assert FIDWalker(R, M).t_off(i, tref) == tref         # old schedule: unloaded -> fmax 0 on the touchdown tick (GI degenerate)
    assert FIDWalker(R, M, onset=1.0).t_off(i, tref) > tref + R.h


def test_landing_quintic_boundary_conditions():
    c = FIDWalker(R, M, land=1.0, land_k0=36.0)
    for par in (0, 1):
        tau0, Tl, _ = c.LQ[par]; i = 2 * par; fi = 5 + 3 * par
        r0 = c.refc(par * R.ts + tau0); pt0 = M.points(r0["q"]); p, v, a = c.land_target(par, tau0)
        assert np.allclose(p, [pt0[i, 0], pt0[i, 1], r0["q"][fi]], atol=1e-9)
        p1, v1, a1 = c.land_target(par, tau0 + Tl); pe = M.points(c.refc((par + 1) * R.ts)["q"])
        assert abs(p1[0] - pe[i, 0]) < 1e-6 and abs(p1[1]) < 1e-9 and abs(v1[0]) < 1e-9 and abs(v1[1] + c.p["v_td"]) < 1e-9 and np.allclose(a1, 0, atol=1e-6)
    p0, _, _ = c.land_target(0, tau0 + Tl + 0.1); assert abs(p0[1] + 0.1 * c.p["v_td"]) < 1e-9   # past the end: keeps descending at v_td


def test_mirrored_start_is_the_mirror_trajectory():
    """Plant, controller and reference are left/right symmetric: the run started in the mirrored step is the leg-swapped run.
    Compared over the first 20 ticks at 1e-6 (a real asymmetry shows at 1e-3 or more already on tick 0). Over a full 0.6 step the two runs separate to ~7e-5, but that is float noise, not
    asymmetry: the QP is ill-conditioned (weights 1e-6..1e6; tick-0 command differs by 1e-8 relative for a 1e-16 relative perturbation of the state) and the stick-slip gait amplifies it
    (a 1e-16 relative state perturbation alone gives a 0.13 difference in q after 0.6 step, 2000x more than the mirror pair). So the horizon, not the tolerance, is limited."""
    out, u0 = [], []
    for mir in (False, True):
        k0 = R.N if mir else 0; kw = dict(PRESETS["b4"]); kw["start_par"] = float(mir)
        q0 = R.q[k0].copy(); qd0 = R.qd[k0].copy(); qd0[0] *= 1.02; q0[2] += np.deg2rad(1.0)
        c = FIDWalker(R, M, **kw)
        res = S.PlantSim(M, friction="stick").run(q0, qd0, c, 20 / 60.0, ref=None); out.append(res["q"]); u0.append(np.array(c.log["u"][0]))
    qa, qb = out; qb = qb[:, P.SW].copy(); qb[:, 0] -= R.d
    assert np.abs(qa - qb).max() < 1e-6
    ub = u0[1][[3, 4, 5, 0, 1, 2]]; assert np.abs(u0[0] - ub).max() / np.abs(u0[0]).max() < 1e-6


def test_mass_estimate_converges_to_plant_mass():
    """Controller model 1.1x: the online estimate ms settles near 1/1.1 (plant truth / controller mass) within 1 s of single support."""
    cm = P.PlanarModel(); cm.mass = M.mass * 1.1; cm.inertia = M.inertia * 1.1; cm.mtot = float(cm.mass.sum()); cm.W = cm.mtot * P.G0; cm.MR = np.repeat(cm.mass, 2)
    c = FIDWalker(R, cm, **dict(PRESETS["b4"], mass_est=0.05)); q0 = R.q[0].copy(); qd0 = R.qd[0].copy()
    PushSim(M, friction="stick").run(q0, qd0, c, 1.0, ref=None)          # PushSim feeds the contact read-back F_meas (PlantSim does not)
    assert abs(c.ms - 1 / 1.1) < 0.03
