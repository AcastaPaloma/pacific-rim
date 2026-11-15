"""T-005 r4 9.3b: two-stage QP walking controller on the planar plant (offline prototype).
Stage 1: QP over friction-pyramid generators (2 per loaded point) -> contact forces F*.
Stage 2: QP over qdd (9 vars; hard: stance-point accel, centroidal rows from F*; soft: swing foot, trunk lean, posture; rows for torque limits) -> u = inv_dyn_u.
Phase: ref time = parity*ts + tau; re-anchor to node 0 and swap legs at detected swing-heel touchdown; hold the last node if late."""
import sys, pathlib, time
import numpy as np
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import planar_model as P
import gi_qp

G0 = P.G0
DEFAULT = dict(
    mu=0.40,               # friction coefficient used by the stride optimisation (plant mu is 0.8)
    w_com=1e3, w_L=10.0, w_prior=1e-2, w_smooth=1e-3, w_reg1=1e-6,
    om_com=1.2 * np.sqrt(G0 / 42.4),   # CoM PD natural frequency (critically damped), rad/s
    kL=None,               # angular-momentum rate gain 1/s; None -> 2*om_com
    om_j=3.0,              # joint/swing/trunk PD natural frequency (critically damped), rad/s
    w_swing=1e2, w_trunk=1e2, w_post=1.0, w_reg2=1e-4,
    kv_stance=None,        # stance-point velocity damping in the accel equality; None -> 2*om_j
    use_torque_rows=True, meas_contact=False, pen_on=0.003, kp_stance=True, td_z=0.15, lift_z=1.0, td_min_frac=0.6, load_thr=0.01, near_ground=0.5,
)


class QPWalker:
    def __init__(self, ref, model, **kw):
        self.ref, self.m = ref, model; p = dict(DEFAULT); p.update(kw); self.p = p
        self.l = 42.4; self.W = model.W; self.mt = model.mtot
        self.om = p["om_com"]; self.kL = p["kL"] if p["kL"] is not None else 2 * self.om
        self.oj = p["om_j"]; self.kvs = p["kv_stance"] if p["kv_stance"] is not None else 2 * self.oj
        self.mu = p["mu"]; self.hnode = ref.h; self.ts = ref.ts
        self.D = np.diag([G0, G0] + [G0 / self.l] * 7)           # qdd = D x  (non-dimensional unknowns)
        self.Pinv = np.linalg.pinv(model.CJ.T)                   # 6x9: u = Pinv Q
        from scipy.interpolate import CubicSpline
        tt = np.append(ref.t, ref.n2 * ref.h); self.sqdd = CubicSpline(tt, np.vstack([ref.qdd, ref.qdd[:1]]), bc_type="periodic")   # node central-difference qdd (consistent with ref F/comdd); ref.at()['qdd'] is the q-spline 2nd derivative and is not
        self.reset()

    def reset(self):
        self.parity = 0; self.t_anchor = 0.0; self.armed = True; self.Xoff = 0.0; self.Fprev = None; self.Fprev_loaded = None
        self.log = {k: [] for k in ("t", "tref", "parity", "q", "com", "L", "F", "u", "fr_cmd", "status1", "status2", "tdrop", "ms", "resid", "loaded", "td_events", "qdd", "qddr", "wrench", "wd")}
        self.td_events = []; self.started = False

    # ------------------------------------------------------------------ reference at the controller's phase
    def ref_state(self, tau):
        tau = min(tau, self.ts - 0.3 * self.hnode)
        tref = self.parity * self.ts + tau
        return tref, self.ref.at(tref)

    def anchor_x(self, tref_node0, x_heel_actual, hs):
        rq = self.ref.at(tref_node0)["q"]
        self.Xoff = x_heel_actual - self.m.points(rq)[hs, 0]

    def __call__(self, t, q, qd, k):
        m, p, ref = self.m, self.p, self.ref
        if k < 0:
            return ref.at(0.0)["u"].copy()
        t0 = time.perf_counter()
        coms, Jc, Jw, pts, Jp, ba, bp = m.kin(q, qd)
        M = Jc.T @ (m.MR[:, None] * Jc) + Jw.T @ (m.inertia[:, None] * Jw); h = Jc.T @ (m.MR * ba.reshape(-1)) + G0 * (Jc[1::2].T @ m.mass)
        c = (m.mass[:, None] * coms).sum(0) / self.mt
        if not self.started:
            self.started = True; hs0 = 2 * (1 - self.parity); self.Xoff = 0.0   # starts at reference node 0: offset zero
        # --- touchdown detection / phase
        sw_heel = 2 * self.parity; sw_toe = sw_heel + 1; tau = t - self.t_anchor
        if pts[sw_heel, 1] > p["lift_z"]: self.armed = True
        vz_h = (Jp[sw_heel, 1] @ qd)
        if self.armed and tau > p["td_min_frac"] * self.ts and pts[sw_heel, 1] < p["td_z"] and vz_h <= 0:
            self.td_events.append((t, float(pts[sw_heel, 0]), float(pts[2 * (1 - self.parity), 0])))
            self.parity = 1 - self.parity; self.t_anchor = t; tau = 0.0; self.armed = False
            hs_new = 2 * (1 - self.parity)                       # new stance heel
            self.anchor_x(self.parity * self.ts, pts[hs_new, 0], hs_new)
            sw_heel = 2 * self.parity; sw_toe = sw_heel + 1
        tref, r = self.ref_state(tau)
        st_leg = 1 - self.parity; hs = 2 * st_leg
        xo = self.Xoff
        qr, qdr = r["q"], r["qd"]; qddr = self.sqdd(tref % (ref.n2 * ref.h))
        # --- loaded set (schedule AND measured near-ground)
        Fr = r["F"]; sched = [i for i in range(4) if Fr[i, 1] > p["load_thr"] * self.W]
        loaded = [i for i in sched if pts[i, 1] < p["near_ground"] * 3.0]   # near-ground band 1.5 m
        if p["meas_contact"]:
            loaded = sorted(set(loaded) | {i for i in range(4) if pts[i, 1] < -p["pen_on"]})
        if not loaded: loaded = sched if sched else [hs]
        # --- stage 1
        comd = (m.MR * (Jc @ qd)).reshape(7, 2).sum(0) / self.mt
        cdd_des = r["comdd"] + self.om**2 * (np.array([r["com"][0] + xo, r["com"][1]]) - c) + 2 * self.om * (r["comd"] - comd)
        A = m.AG(q); Lm = (A @ qd)[2]
        Ldot_des = r["Ldot"] + self.kL * (r["L"] - Lm)
        W, l, mu = self.W, self.l, self.mu
        wd = np.array([self.mt * cdd_des[0] / W, self.mt * (cdd_des[1] + G0) / W, Ldot_des / (W * l)])
        nb = 2 * len(loaded); Gm = np.zeros((3, nb)); Em = np.zeros((2 * len(loaded), nb)); fref = np.zeros(2 * len(loaded)); fprev = np.zeros(2 * len(loaded))
        smooth_mask = np.zeros(2 * len(loaded))
        for a, i in enumerate(loaded):
            for b, sgn in enumerate((1.0, -1.0)):
                col = 2 * a + b; ft, fn = sgn * mu, 1.0
                Gm[0, col] = ft; Gm[1, col] = fn
                Gm[2, col] = ((pts[i, 0] - c[0]) * fn - (pts[i, 1] - c[1]) * ft) / l
                Em[2 * a, col] = ft; Em[2 * a + 1, col] = fn
            fref[2 * a:2 * a + 2] = Fr[i] / W
            if self.Fprev is not None and self.Fprev_loaded is not None and i in self.Fprev_loaded:
                fprev[2 * a:2 * a + 2] = self.Fprev[i] / W; smooth_mask[2 * a:2 * a + 2] = 1
        Wr = np.diag([p["w_com"], p["w_com"], p["w_L"]])
        H1 = 2 * (Gm.T @ Wr @ Gm + p["w_prior"] * Em.T @ Em + p["w_smooth"] * Em.T @ (smooth_mask[:, None] * Em) + p["w_reg1"] * np.eye(nb))
        g1 = -2 * (Gm.T @ Wr @ wd + p["w_prior"] * Em.T @ fref + p["w_smooth"] * Em.T @ (smooth_mask * fprev))
        beta, st1, it1, _ = gi_qp.solve(H1, g1, np.eye(nb), np.zeros(nb), 0)
        if st1 != "ok": beta = np.maximum(beta, 0.0)
        fvec = Em @ beta; F = np.zeros((4, 2))
        for a, i in enumerate(loaded): F[i] = fvec[2 * a:2 * a + 2] * W
        wrench = np.array([F[:, 0].sum(), F[:, 1].sum() - self.W, sum((pts[i, 0] - c[0]) * F[i, 1] - (pts[i, 1] - c[1]) * F[i, 0] for i in range(4))])
        # --- stage 2
        D = self.D; Ad = A @ D
        rows_E = []; rhs_E = []
        v_p = np.einsum("pdi,i->pd", Jp, qd)
        loaded_set = set(loaded)
        ptr, ptvr, ptar = self._ref_points(qr, qdr, qddr)
        # stance (loaded) points: accel = reference point accel (0 for a planted foot) + damping of the velocity error; independent rows only:
        # first loaded point of a foot gives x and z, a second point on the same foot gives z only (rigid foot, rank 3)
        seen_foot = set()
        for i in loaded:
            foot = i // 2; Jd = Jp[i] @ D / G0; a_t = (ptar[i] + (self.oj**2 * (ptr[i] + np.array([xo, 0.0]) - pts[i]) if p["kp_stance"] else 0.0) + self.kvs * (ptvr[i] - v_p[i]) - bp[i]) / G0
            if foot not in seen_foot:
                rows_E += [Jd[0], Jd[1]]; rhs_E += [a_t[0], a_t[1]]; seen_foot.add(foot)
            else:
                rows_E += [Jd[1]]; rhs_E += [a_t[1]]
        sc = np.array([self.mt * G0, self.mt * G0, self.mt * G0 * l])
        agd = m.AGdot_qd(q, qd)
        for j in range(3):
            rows_E.append(Ad[j] / sc[j]); rhs_E.append((wrench[j] - agd[j]) / sc[j])
        # centroidal row RHS: A_G qdd = [Fx, Fz - mg, T] - AGdot_qd ; fix units: A_G is momentum matrix (kg m/s per rad/s), rows /sc matches wrench/sc
        E = np.array(rows_E); e = np.array(rhs_E); self._dbg = (E, e, qddr @ np.eye(9) )
        # soft tasks
        Hs = p["w_reg2"] * np.eye(9); gs = np.zeros(9)
        def add(T, tgt, w):
            nonlocal Hs, gs
            Hs = Hs + w**2 * T.T @ T; gs = gs - w**2 * T.T @ tgt
        # swing points (not loaded)
        sw_pts = [i for i in (sw_heel, sw_toe) if i not in loaded_set]
        Kp, Kd = self.oj**2, 2 * self.oj
        for i in sw_pts:
            pos_ref = ptr[i] + np.array([xo, 0.0]); a_des = ptar[i] + Kp * (pos_ref - pts[i]) + Kd * (ptvr[i] - v_p[i])
            Jd = Jp[i] @ D / G0; add(Jd, (a_des - bp[i]) / G0, p["w_swing"])
        # trunk lean
        et = np.zeros(9); et[2] = 1.0
        a_th = qddr[2] + Kp * (qr[2] - q[2]) + Kd * (qdr[2] - qd[2]); add((et @ D / (G0 / l))[None, :], np.array([a_th / (G0 / l)]), p["w_trunk"])
        # posture (6 joints)
        CJ = m.CJ; jr, jdr, jddr = CJ @ qr, CJ @ qdr, CJ @ qddr; ja, jda = CJ @ q, CJ @ qd
        a_j = jddr + Kp * (jr - ja) + Kd * (jdr - jda); add(CJ @ D / (G0 / l), a_j / (G0 / l), p["w_post"])
        # solve: eliminate equalities (SVD, rank-safe)
        U, S, Vt = np.linalg.svd(E, full_matrices=True); rk = int((S > 1e-9 * S[0]).sum())
        x0 = Vt[:rk].T @ ((U[:, :rk].T @ e) / S[:rk]); Z = Vt[rk:].T
        qdd_s = x0; st2 = "ok"; tdrop = 0
        if Z.shape[1] > 0:
            Hz = Z.T @ Hs @ Z; gz = Z.T @ (Hs @ x0 + gs)
            Cz = None
            if p["use_torque_rows"]:
                Jq = Jp; Qc = h - np.einsum("pdi,pd->i", Jp, F)
                Au = (self.Pinv @ M @ D) / m.LIM[:, None]; cu = (self.Pinv @ Qc) / m.LIM
                Cr = np.vstack([Au @ Z, -Au @ Z]); dr = np.concatenate([-1 - cu - Au @ x0, -1 + cu + Au @ x0])
                z, st2, it2, _ = gi_qp.solve(Hz, gz, Cr, dr, 0)
                if st2 != "ok":
                    tdrop = 1; z, st2, it2, _ = gi_qp.solve(Hz, gz)
            else:
                z, st2, it2, _ = gi_qp.solve(Hz, gz)
            qdd_s = x0 + Z @ z
        qdd = D @ qdd_s
        u, res = m.inv_dyn_u(q, qd, qdd, F)
        u = np.clip(u, -m.LIM, m.LIM)
        # friction ratio of the command
        fr = max([abs(F[i, 0]) / (mu * F[i, 1]) for i in range(4) if F[i, 1] > 1e-6 * self.W] + [0.0])
        self.Fprev = F.copy(); self.Fprev_loaded = list(loaded)
        L = self.log; L["t"].append(t); L["tref"].append(tref); L["parity"].append(self.parity); L["q"].append(q.copy()); L["com"].append(c.copy()); L["L"].append(Lm); L["F"].append(F.copy()); L["u"].append(u.copy())
        L["fr_cmd"].append(fr); L["status1"].append(st1); L["status2"].append(st2); L["tdrop"].append(tdrop); L["ms"].append(1e3 * (time.perf_counter() - t0)); L["resid"].append(float(np.abs(res).max() / (self.W * l))); L["loaded"].append(sum(1 << i for i in loaded)); L["qdd"].append(qdd.copy()); L["qddr"].append(qddr.copy()); L["wrench"].append(wrench.copy()); L["wd"].append(wd.copy())
        return u

    def _ref_points(self, qr, qdr, qddr):
        m = self.m; _, _, _, pts, Jp, _, bp = m.kin(qr, qdr)
        return pts, np.einsum("pdi,i->pd", Jp, qdr), np.einsum("pdi,i->pd", Jp, qddr) + bp
