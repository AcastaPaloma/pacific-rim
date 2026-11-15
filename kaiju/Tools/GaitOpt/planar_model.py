"""T-005 r4 step 9.3a: planar plant model (numpy, SI) for the offline QP prototype. Import has no side effects.

API NOTE (for step 9.3b)
  Units: SI (m, s, kg, N, N m, rad). stride_opt.py works in 1e6 kg / MN; this module multiplies back (M_si = 1e6 * M_opt).
  q = [hip_x, hip_z, th, a0, b0, f0, a1, b1, f1] (9 DOF; z is vertical; leg 0 = "A", leg 1 = "B" as in stride_opt.py;
      th = upper-body lean (+ forward, clockwise), a/b = thigh/shank angle from vertical (+ distal end ahead), f = foot pitch (+ heel up)).
  Joint angles (6): j = C_J q-style, [hip, knee, ankle] x [A, B]: hip = a+th, knee = a-b, ankle = b+f (joint_angles(q)).
  Actuation: generalised force Q = CJ^T u, u = 6 joint torques [hipA, kneeA, ankleA, hipB, kneeB, ankleB] (CJ = joint_jac(), 6x9).
      The base (x, z) is unactuated; the trunk lean th only receives the sum of the two hip torques (no separate trunk motor).
  Dynamics: M(q) qdd + h(q,qd) = CJ^T u + sum_p Jp_p^T F_p     (h = Coriolis + gravity, G positive, same as stride_opt.inv_dyn)
  Contact points p = 0 A heel, 1 A toe, 2 B heel, 3 B toe (sole points). F_p = (tangential x, normal z); ground z = 0, +x forward.
  PlanarModel(): .M(q) 9x9, .h(q,qd) 9, .bias_acc(q,qd) (links' Jdot qd), .com(q) -> (x,z,mass), .com_vel(q,qd),
      .points(q) (4,2), .point_jac(q) (4,2,9), .point_vel(q,qd), .AG(q) 3x9 centroidal momentum matrix, .AGdot_qd(q,qd) 3,
      .joint_angles(q) 6, .joint_rates(q,qd) 6, .joint_jac() 6x9, .LIM torque limits (6, N m),
      .inv_dyn_u(q,qd,qdd,F) -> 6 torques that realise qdd under contact forces F (4,2) [least-squares over the 9 rows; returns residual too].
  Centroidal rows of AG: [px, pz, L]; p = linear momentum (kg m/s), L = sagittal angular momentum about the CoM, COUNTER-CLOCKWISE positive
      in the (x right, z up) plane, L = sum I w + m (r-c) x v.   d/dt [px,pz,L] = [Fx, Fz - m g, sum (p_i - c) x F_i].
  Reference: load_reference(path=None) -> Reference with 2N periodic nodes (two mirrored steps), fields t, q, qd, qdd, F (2N,4,2),
      u_json (2N,6), com (2N,2), comd, comdd, L (about CoM, ccw), Ldot, hip-relative com x, and spline evaluators ref.at(t) -> dict.
      ref.at(t)["qdd"] = periodic spline of the node central-difference qdd (= ref.qdd at nodes), i.e. consistent with F, comdd and Ldot
      (fixed in 9.3b-2; before it was the q-spline 2nd derivative, kept as ref.at(t)["qdd_qspline"], which misses the wrench by up to 0.7 m g).
      The force-consistency check sum F = m (cdd + g) is asserted in load_reference (tolerance 5 % of m g, max residual stored in ref.fc_resid).
  Contact schedule of the reference: step 1 (nodes 0..N-1): leg B is the new stance leg (heel down from node 0), leg A toe-off at k_ds.
"""
import os, json, pathlib
import numpy as np

ROOT = pathlib.Path(__file__).resolve().parents[2]
G0 = 9.81
SW = [0, 1, 2, 6, 7, 8, 3, 4, 5]          # leg swap permutation of q (and qd, qdd)
SW4 = [2, 3, 0, 1]                        # point swap
SW6 = [3, 4, 5, 0, 1, 2]                  # joint swap


class PlanarModel:
    def __init__(self, root=ROOT):
        root = pathlib.Path(root)
        J = json.load(open(os.environ.get("R7_FRAME") or root / "Assets/Game/Config/Mech/mech_frame_balanced.json")); S = {s["id"]: s for s in J["segments"]}
        BT = json.load(open(os.environ.get("R7_BODY") or root / "Assets/Game/Config/Mech/body_tuning.json"))
        lim = {k["kind"]: k["force_limit_nm"] for k in BT["joint_kinds"]}
        self.LIM = np.array([lim["hip"], lim["knee"], lim["ankle"]] * 2)            # N m
        self.l_leg = None
        hipy = S["thigh_L"]["proximal_joint"][1]
        self.LT = hipy - S["thigh_L"]["distal_end"][1]; self.LS = S["shank_L"]["proximal_joint"][1] - S["shank_L"]["distal_end"][1]
        self.l_leg = self.LT + self.LS
        ha = J["foot_sole"]["ankle_height_m"]; self.HA = ha
        self.HEELX, self.TOEX = J["foot_sole"]["heel_z_m"], J["foot_sole"]["toe_z_m"]
        FL, FH = J["foot_sole"]["length_m"], ha
        up = [s for s in J["segments"] if s["id"] in ("pelvis", "torso", "head") or s["id"].split("_")[0] in ("upper", "forearm", "hand")]
        mU = sum(s["mass_kg"] for s in up); self.cU = sum(s["mass_kg"] * (s["com"][1] - hipy) for s in up) / mU
        IU = sum(s["mass_kg"] * (abs(s["proximal_joint"][1] - s["distal_end"][1]) ** 2 / 12 + (s["com"][1] - hipy - self.cU) ** 2) for s in up)
        mT, mS, mF = (S[k]["mass_kg"] for k in ("thigh_L", "shank_L", "foot_L"))
        self.cT = hipy - S["thigh_L"]["com"][1]; self.cS = S["shank_L"]["proximal_joint"][1] - S["shank_L"]["com"][1]
        self.cF = (S["foot_L"]["com"][2], S["foot_L"]["com"][1] - ha)
        self.mass = np.array([mU, mT, mS, mF, mT, mS, mF]); self.inertia = np.array([IU, mT * self.LT**2 / 12, mS * self.LS**2 / 12, mF * (FL**2 + FH**2) / 12] + [mT * self.LT**2 / 12, mS * self.LS**2 / 12, mF * (FL**2 + FH**2) / 12])
        self.mtot = float(self.mass.sum()); self.W = self.mtot * G0
        self.MR = np.repeat(self.mass, 2)
        # joint Jacobian: [hip, knee, ankle] per leg;  hip = a+th, knee = a-b, ankle = b+f
        CJ = np.zeros((6, 9))
        for i in (0, 1):
            a, b, f = 3 + 3 * i, 4 + 3 * i, 5 + 3 * i
            CJ[3 * i, [2, a]] = 1; CJ[3 * i + 1, [a, b]] = [1, -1]; CJ[3 * i + 2, [b, f]] = 1
        self.CJ = CJ

    # ---- kinematics and Jacobians (one pass)
    def kin(self, q, qd=None):
        """coms (7,2), Jc (14,9), Jw (7,9, ccw), pts (4,2), Jp (4,2,9); with qd also bias accelerations of coms (7,2) and of points (4,2)."""
        th = q[2]; LT, LS, cT, cS, cU = self.LT, self.LS, self.cT, self.cS, self.cU; hip = q[:2]
        Jc = np.zeros((14, 9)); Jw = np.zeros((7, 9)); Jp = np.zeros((4, 2, 9)); coms = np.zeros((7, 2)); pts = np.zeros((4, 2))
        Jc[0::2, 0] = 1; Jc[1::2, 1] = 1                                          # translation of the hip on every link
        Jp[:, 0, 0] = 1; Jp[:, 1, 1] = 1
        s, c = np.sin(th), np.cos(th); coms[0] = hip + cU * np.array([s, c]); Jc[0:2, 2] = cU * np.array([c, -s]); Jw[0, 2] = -1.0
        ba = None
        if qd is not None:
            ba = np.zeros((7, 2)); bp = np.zeros((4, 2)); ba[0] = -qd[2] ** 2 * cU * np.array([s, c])
        for i in (0, 1):
            ia, ib, ifo = 3 + 3 * i, 4 + 3 * i, 5 + 3 * i
            a, b, f = q[ia], q[ib], q[ifo]
            sa, ca, sb, cb, sf, cf = np.sin(a), np.cos(a), np.sin(b), np.cos(b), np.sin(f), np.cos(f)
            dt = np.array([sa, -ca]); ds = np.array([sb, -cb]); e = np.array([cf, -sf]); n = np.array([sf, cf])
            dta = np.array([ca, sa]); dsb = np.array([cb, sb])
            knee = hip + LT * dt; ank = knee + LS * ds
            r = 1 + 3 * i                                                           # link index of the thigh
            coms[r] = hip + cT * dt; coms[r + 1] = knee + cS * ds; coms[r + 2] = ank + self.cF[0] * e + self.cF[1] * n
            Jc[2 * r:2 * r + 2, ia] = cT * dta
            Jc[2 * r + 2:2 * r + 4, ia] = LT * dta; Jc[2 * r + 2:2 * r + 4, ib] = cS * dsb
            Jc[2 * r + 4:2 * r + 6, ia] = LT * dta; Jc[2 * r + 4:2 * r + 6, ib] = LS * dsb
            Jc[2 * r + 4:2 * r + 6, ifo] = -self.cF[0] * n + self.cF[1] * e
            Jw[r, ia] = 1; Jw[r + 1, ib] = 1; Jw[r + 2, ifo] = -1
            for k, x in enumerate((self.HEELX, self.TOEX)):
                p = 2 * i + k; pts[p] = ank + x * e - self.HA * n
                Jp[p, :, ia] = LT * dta; Jp[p, :, ib] = LS * dsb; Jp[p, :, ifo] = -x * n - self.HA * e
            if qd is not None:
                ad2, bd2, fd2 = qd[ia] ** 2, qd[ib] ** 2, qd[ifo] ** 2
                ba[r] = -ad2 * cT * dt; ba[r + 1] = -ad2 * LT * dt - bd2 * cS * ds
                ba[r + 2] = -ad2 * LT * dt - bd2 * LS * ds - fd2 * (self.cF[0] * e + self.cF[1] * n)
                for k, x in enumerate((self.HEELX, self.TOEX)):
                    bp[2 * i + k] = -ad2 * LT * dt - bd2 * LS * ds - fd2 * (x * e - self.HA * n)
        if qd is None: return coms, Jc, Jw, pts, Jp
        return coms, Jc, Jw, pts, Jp, ba, bp

    def M(self, q):
        _, Jc, Jw, _, _ = self.kin(q)
        return Jc.T @ (self.MR[:, None] * Jc) + Jw.T @ (self.inertia[:, None] * Jw)

    def h(self, q, qd):
        _, Jc, _, _, _, ba, _ = self.kin(q, qd)
        return Jc.T @ (self.MR * ba.reshape(-1)) + G0 * (Jc[1::2].T @ self.mass)

    def Mh(self, q, qd):
        """M and h in one kinematics pass (fast path for the simulator)."""
        coms, Jc, Jw, pts, Jp, ba, bp = self.kin(q, qd)
        M = Jc.T @ (self.MR[:, None] * Jc) + Jw.T @ (self.inertia[:, None] * Jw)
        h = Jc.T @ (self.MR * ba.reshape(-1)) + G0 * (Jc[1::2].T @ self.mass)
        return M, h, pts, Jp, Jc, coms

    def bias_acc(self, q, qd):
        return self.kin(q, qd)[5]

    def com(self, q):
        coms = self.kin(q)[0]
        return (self.mass[:, None] * coms).sum(0) / self.mtot

    def com_vel(self, q, qd):
        Jc = self.kin(q)[1]
        return (self.MR * (Jc @ qd)).reshape(7, 2).sum(0) / self.mtot

    def points(self, q): return self.kin(q)[3]
    def point_jac(self, q): return self.kin(q)[4]
    def point_vel(self, q, qd): return np.einsum("pdi,i->pd", self.kin(q)[4], qd)

    def AG(self, q):
        coms, Jc, Jw, _, _ = self.kin(q); c = (self.mass[:, None] * coms).sum(0) / self.mtot
        Jc3 = Jc.reshape(7, 2, 9); r = coms - c
        A = np.zeros((3, 9))
        A[0] = (self.mass[:, None] * Jc3[:, 0]).sum(0); A[1] = (self.mass[:, None] * Jc3[:, 1]).sum(0)
        A[2] = (self.inertia[:, None] * Jw).sum(0) + (self.mass[:, None] * (r[:, 0:1] * Jc3[:, 1] - r[:, 1:2] * Jc3[:, 0])).sum(0)
        return A

    def AGdot_qd(self, q, qd):
        coms, Jc, Jw, _, _, ba, _ = self.kin(q, qd); c = (self.mass[:, None] * coms).sum(0) / self.mtot; r = coms - c
        out = np.zeros(3); out[:2] = (self.mass[:, None] * ba).sum(0)
        out[2] = (self.mass * (r[:, 0] * ba[:, 1] - r[:, 1] * ba[:, 0])).sum()
        return out

    def joint_angles(self, q): return self.CJ @ q
    def joint_rates(self, q, qd): return self.CJ @ qd
    def joint_jac(self): return self.CJ

    def inv_dyn_u(self, q, qd, qdd, F):
        M, h, pts, Jp, _, _ = self.Mh(q, qd)
        Q = M @ qdd + h - np.einsum("pdi,pd->i", Jp, F)
        u, *_ = np.linalg.lstsq(self.CJ.T, Q, rcond=None)
        return u, Q - self.CJ.T @ u

    def lean_ok(self, q): return abs(q[2])


# ------------------------------------------------------------------ reference
class Reference:
    """Periodic reference (two mirrored steps, 2N nodes) with @v2 fields. See module docstring."""
    pass


def load_reference(path=None, model=None, tol_frac=0.05):
    from scipy.interpolate import CubicSpline
    model = model or PlanarModel()
    path = pathlib.Path(path) if path else ROOT / "Tools/GaitOpt/out/stride_fr010_lat.json"
    D = json.load(open(path)); sm = D["summary"]; nd = D["nodes"]; N = len(nd); h = nd[1]["t"] - nd[0]["t"]
    ts = h * N; v = sm["v"]; d = v * ts
    q1 = np.array([n["q"] for n in nd]); qd1 = np.array([n["qd"] for n in nd])
    pk = ["A_heel", "A_toe", "B_heel", "B_toe"]
    F1 = np.array([[nd[k]["contact_N"][p] for p in pk] for k in range(N)])           # (N,4,2) [ft, fn]
    uj1 = np.array([[nd[k]["tau_Nm"][j] for j in ("hipA", "kneeA", "ankleA", "hipB", "kneeB", "ankleB")] for k in range(N)])
    off = np.zeros(9); off[0] = d
    q = np.vstack([q1, q1[:, SW] + off]); qd = np.vstack([qd1, qd1[:, SW]]); F = np.concatenate([F1, F1[:, SW4]]); uj = np.vstack([uj1, uj1[:, SW6]])
    n2 = 2 * N; t = np.arange(n2) * h
    offp = np.zeros(9); offp[0] = 2 * d
    # central-difference qdd on the periodic extension (as in the optimiser)
    qp = np.vstack([q[1:], q[:1] + offp]); qm = np.vstack([q[-1:] - offp, q[:-1]])
    qdd = (qp - 2 * q + qm) / h**2
    qd_cd = (qp - qm) / (2 * h)
    r = Reference(); r.model = model; r.N = N; r.n2 = n2; r.h = h; r.ts = ts; r.v = v; r.d = d; r.t = t; r.q = q; r.qd = qd; r.qdd = qdd; r.F = F; r.u_json = uj
    r.qd_err_vs_cd = float(np.abs(qd - qd_cd).max())
    r.k_ds, r.k_ff, r.k_hr = sm["schedule"]["k_ds"], sm["schedule"]["k_ff"], sm["schedule"]["k_hr"]
    com = np.zeros((n2, 2)); comd = np.zeros((n2, 2)); comdd = np.zeros((n2, 2)); L = np.zeros(n2); Ldot = np.zeros(n2); lin = np.zeros((n2, 2))
    Tq = np.zeros(n2); Ls = np.zeros(n2)
    for k in range(n2):
        coms, Jc, Jw, pts, Jp, ba, bp = model.kin(q[k], qd[k]); c = (model.mass[:, None] * coms).sum(0) / model.mtot
        A = model.AG(q[k]); mom = A @ qd[k]; mom_d = A @ qdd[k] + model.AGdot_qd(q[k], qd[k])
        com[k] = c; comd[k] = mom[:2] / model.mtot; comdd[k] = mom_d[:2] / model.mtot; L[k] = mom[2]; Ldot[k] = mom_d[2]
        Tq[k] = sum((pts[p, 0] - c[0]) * F[k, p, 1] - (pts[p, 1] - c[1]) * F[k, p, 0] for p in range(4))        # torque about CoM from the contact forces (ccw)
    r.com, r.comd, r.comdd, r.L, r.Ldot, r.torque_com = com, comd, comdd, L, Ldot, Tq
    r.com_x_rel_hip = com[:, 0] - q[:, 0]
    # force consistency: sum F = m (cdd + g)
    need = model.mtot * np.stack([comdd[:, 0], comdd[:, 1] + G0], 1); have = F.sum(1)
    res = np.abs(have - need).max(1)
    r.fc_resid_abs = res; r.fc_resid = float(res.max() / model.W); r.fc_resid_x = float(np.abs(have - need)[:, 0].max() / model.W); r.fc_resid_z = float(np.abs(have - need)[:, 1].max() / model.W)
    r.Ldot_resid = float(np.abs(Ldot - Tq).max() / (model.W * model.l_leg))
    assert r.fc_resid <= tol_frac, f"force consistency residual {r.fc_resid:.4f} m g above {tol_frac}"
    # splines (drift removed for x), periodic over the two-step stride
    T2 = n2 * h; vavg = 2 * d / T2
    def per(y, drift=None):
        y = np.asarray(y, float); dd = np.zeros(y.shape[1]) if drift is None else np.asarray(drift, float)
        tt = np.append(t, T2); yy = np.vstack([y, y[:1] + dd]) - np.outer(tt / T2, dd)
        return CubicSpline(tt, yy, bc_type="periodic")
    dr = np.zeros(9); dr[0] = 2 * d; drc = np.array([2 * d, 0.0])
    sq = per(q, dr); sqd = per(qd); suj = per(uj); sF = per(F.reshape(n2, 8)); scom = per(com, drc); scd = per(comd); sL = per(L[:, None])
    scdd = per(comdd); sLd = per(Ldot[:, None]); sqdd = per(qdd)   # node central-difference qdd: the one consistent with F, comdd, Ldot (9.3b-2 fix)
    def at(tq):
        tq = float(tq); nw = np.floor(tq / T2); tl = tq - nw * T2; fr = tl / T2 + nw; out = {}
        out["q"] = sq(tl) + dr * fr; out["qd"] = sqd(tl); out["qdd"] = sqdd(tl); out["qdd_qspline"] = sq(tl, 2); out["u"] = suj(tl); out["F"] = sF(tl).reshape(4, 2)
        out["com"] = scom(tl) + drc * fr; out["comd"] = scd(tl); out["comdd"] = scdd(tl); out["L"] = float(sL(tl)[0]); out["Ldot"] = float(sLd(tl)[0])
        return out
    r.at = at; r.T2 = T2
    return r


def export_v2(ref, path):
    """Write the @v2 additive fields (per node, both steps) as JSON for inspection."""
    out = {"schema": "gait_reference_opt@v2-additions", "units": "SI; L ccw about CoM (kg m^2/s); com_x_rel_hip = com_x - hip_x", "nodes": ref.n2, "step_time_s": ref.ts, "v": ref.v,
           "com_x": ref.com[:, 0].tolist(), "com_z": ref.com[:, 1].tolist(), "com_x_rel_hip": ref.com_x_rel_hip.tolist(),
           "com_vx": ref.comd[:, 0].tolist(), "com_vz": ref.comd[:, 1].tolist(), "com_ax": ref.comdd[:, 0].tolist(), "com_az": ref.comdd[:, 1].tolist(),
           "ang_mom_com": ref.L.tolist(), "ang_mom_dot": ref.Ldot.tolist(), "fc_resid_over_mg": ref.fc_resid, "ldot_resid_over_mg_l": ref.Ldot_resid}
    json.dump(out, open(path, "w"))
