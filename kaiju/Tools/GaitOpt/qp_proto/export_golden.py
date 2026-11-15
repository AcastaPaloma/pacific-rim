"""T-005 r4 9.4b: golden ticks for the C# port (SagittalQpController). Runs criterion A with the b5 preset on the v3 reference (stick plant, delay 1) for STEPS steps and records, per tick, the
controller's inputs in the planar model (t, q, qd, M, h, sole points, Jp, Jdot qd of the points, CoM, CoM velocity, L about the CoM, measured normal forces used) and its output u (6 joint torques, N m).
Output: DotnetTests/Units.Mech.Tests/Fixtures/qp_golden.bin (float64 little endian, rows of ROW values, first row = first tick k = 0) and qp_golden.json (meta: layout, model constants, controller parameters).
Run from the project root: /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/qp_proto/export_golden.py [steps]"""
import sys, pathlib, json
import numpy as np
HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE)); sys.path.insert(0, str(HERE.parent))
import planar_model as P, planar_sim as S
from controller_fid import FIDWalker
from run_fid import PushSim, PRESETS

STEPS = float(sys.argv[1]) if len(sys.argv) > 1 else 1.4
OUTD = P.ROOT / "DotnetTests/Units.Mech.Tests/Fixtures"; OUTD.mkdir(parents=True, exist_ok=True)
LAYOUT = [("t", 1), ("q", 9), ("qd", 9), ("M", 81), ("h", 9), ("pts", 8), ("Jp", 72), ("bp", 8), ("c", 2), ("comd", 2), ("L", 1), ("fn_meas", 4), ("u", 6)]
ROW = sum(n for _, n in LAYOUT)


class Rec(FIDWalker):
    rows = []

    def __call__(self, t, q, qd, k):
        u = super().__call__(t, q, qd, k)
        if k < 0: return u
        m = self.m; coms, Jc, Jw, pts, Jp, ba, bp = m.kin(q, qd)
        M = Jc.T @ (m.MR[:, None] * Jc) + Jw.T @ (m.inertia[:, None] * Jw); h = Jc.T @ (m.MR * ba.reshape(-1)) + P.G0 * (Jc[1::2].T @ m.mass)
        c = (m.mass[:, None] * coms).sum(0) / self.mt; comd = (m.MR * (Jc @ qd)).reshape(7, 2).sum(0) / self.mt; L = float((m.AG(q) @ qd)[2])
        row = np.concatenate([[t], q, qd, M.ravel(), h, pts.ravel(), Jp.ravel(), bp.ravel(), c, comd, [L], np.asarray(self.log["fn_meas"][-1], float), u])
        assert len(row) == ROW; Rec.rows.append(row); return u


model = P.PlanarModel(); ref = P.load_reference(str(P.ROOT / "Tools/GaitOpt/out/stride_fr010_v3_n40p2.json"), model)
kw = dict(PRESETS["b5"]); ctl = Rec(ref, model, **kw)
q0 = ref.q[0].copy(); qd0 = ref.qd[0].copy(); qd0[0] *= 1.02; q0[2] += np.deg2rad(1.0)
sim = PushSim(model, push_fn=None, delay_ticks=1, friction="stick", mu=0.8)
res = sim.run(q0, qd0, ctl, STEPS * ref.ts, ref=ref)
A = np.array(Rec.rows, "<f8"); A.tofile(OUTD / "qp_golden.bin")
meta = dict(layout=LAYOUT, row=ROW, ticks=len(A), fell=bool(res["fell"]), dt=1 / 60, mtot=model.mtot, g0=P.G0, lim=model.LIM.tolist(), l=ctl.l, params={k: float(v) for k, v in ctl.p.items()}, touchdowns=[list(map(float, e)) for e in ctl.td_events],
            status1=[int(s != "ok") for s in ctl.log["status1"]], tdrop=[int(x) for x in ctl.log["tdrop"]], ms=[float(x) for x in ctl.log["mest"]], note="u = controller output (clipped); Python solver gi_qp (null-space reduced)")
json.dump(meta, open(OUTD / "qp_golden.json", "w"))
print("ticks", len(A), "fell", res["fell"], "touchdowns", meta["touchdowns"], "status1 nonok", sum(meta["status1"]), "tdrop", sum(meta["tdrop"]), "bin bytes", A.nbytes)

# reference samples of the prototype's splines (FIDWalker.refc) at a few times, with the point kinematics the controller derives from them (kin of the spline q, qd, qdd)
ts_ = [0.37, 1.9, 3.85, 4.1, ctl.ts, 5.02, 7.3, 8.7, 9.1]; samp = []
for tq in ts_:
    r = ctl.refc(tq); _, _, _, ptr, Jpr, _, bpr = model.kin(r["q"], r["qd"])
    samp.append(dict(t=tq, q=r["q"].tolist(), qd=r["qd"].tolist(), qdd=r["qdd"].tolist(), F=r["F"].ravel().tolist(), com=r["com"].tolist(), comd=r["comd"].tolist(), comdd=r["comdd"].tolist(), L=r["L"], Ldot=r["Ldot"],
                     pts=ptr.ravel().tolist(), ptv=np.einsum("pdi,i->pd", Jpr, r["qd"]).ravel().tolist(), pta=(np.einsum("pdi,i->pd", Jpr, r["qdd"]) + bpr).ravel().tolist()))
json.dump(samp, open(OUTD / "qp_ref_samples.json", "w")); print("ref samples", len(samp))

# exact reference states (check_fid.py setup: contacts = schedule, forces = reference): one tick per node of the first step, with the reference torques appended
rows = []
for k in range(ref.N):
    c = FIDWalker(ref, model, **kw); c(0, ref.q[0], ref.qd[0], -1)
    c.inc[:] = c.flags[k]; c.f_in[:] = 5 * model.W; c.t_in[:] = -10; c.Fprev = ref.F[k].copy(); c.F_meas = ref.F[k].copy(); c.t_anchor = -k * ref.h; c.t_rel[:] = -10
    q, qd = ref.q[k], ref.qd[k]; u = c(0.0, q, qd, 0); m = model
    coms, Jc, Jw, pts, Jp, ba, bp = m.kin(q, qd)
    M = Jc.T @ (m.MR[:, None] * Jc) + Jw.T @ (m.inertia[:, None] * Jw); h = Jc.T @ (m.MR * ba.reshape(-1)) + P.G0 * (Jc[1::2].T @ m.mass)
    cc = (m.mass[:, None] * coms).sum(0) / c.mt; comd = (m.MR * (Jc @ qd)).reshape(7, 2).sum(0) / c.mt; L = float((m.AG(q) @ qd)[2])
    rows.append(np.concatenate([[k], q, qd, M.ravel(), h, pts.ravel(), Jp.ravel(), bp.ravel(), cc, comd, [L], np.asarray(c.log["fn_meas"][-1], float), u, ref.u_json[k]]))
np.array(rows, "<f8").tofile(OUTD / "qp_refstate.bin")
eu = max(np.abs((r[-12:-6] - r[-6:]) / model.LIM).max() for r in rows); print("reference states", len(rows), "worst python du/LIM vs reference", eu)
