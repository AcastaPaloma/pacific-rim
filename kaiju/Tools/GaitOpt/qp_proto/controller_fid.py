"""T-005 r4 9.3b-2: contact-aware single full inverse-dynamics QP walker on the planar plant (offline prototype; R-009 s.2 upgrade path).
One QP per tick over x = [qdd (9, scaled by D), beta (2 friction-pyramid generators per contact point, mu_eff 0.40)]:
  hard equalities : the 3 unactuated rows of  M qdd + h = CJ^T u + Jp^T F   (u = pinv(CJ^T)(M qdd + h - Jp^T F) then realises all 9 rows)
  hard inequality : beta >= 0 (unilateral + friction pyramid), fn_i <= fmax_i (ramps), |u_j| <= lim_frac * LIM_j (12 rows, linear in x)
  soft (weighted) : centroidal wrench (CoM x, z PD + feed-forward; L about CoM), stance-point accel = -kvs v (contact points), swing heel x/z,
                    foot pitch, trunk lean, posture, force prior f_ref, smoothness to F_prev, regularisers.
Contact awareness (R-009 s.5): a point is in the QP's contact set while the MEASURED normal force says loaded (hysteresis on_bw / off_bw of m g;
  the sensor emulates the plant's contact law, i.e. what a contact-impulse read-back gives); fmax ramps to 0 over T_out before the scheduled
  unload time of that point and, after first contact, rises at most 1 m g per T_in; swing tasks on a foot blend in over T_blend after its
  measured lift-off; touchdown = measured load on the swing foot after td_min_frac of the step -> re-anchor phase to node 0, swap legs, shift x.
Reference: own periodic splines of the node data (q, CD qd, CD qdd, F, com, comd, comdd, L, Ldot), optionally Gaussian-smoothed over the
  nodes (sigma, in nodes; 0 = raw). The data file and planar_model's Reference are not changed; metrics are taken against the raw reference."""
import sys, pathlib, time
import numpy as np
from scipy.interpolate import CubicSpline
from scipy.linalg import null_space
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
import planar_model as P
import gi_qp

G0 = P.G0
DEFAULT = dict(
    mu=0.40, lim_frac=0.98,
    om_x=0.8, om_z=2.0, kL=2.0, om_sw=4.0, om_th=3.0, om_post=3.0, om_fp=4.0,
    w_cx=1e4, w_cz=1e4, w_L=1e3, w_prior=1e-2, w_smooth=1e-3, w_st=1e6, w_swing=1e3, w_fp=1e3, w_trunk=1e4, w_post=10.0, w_reg=1e-4, w_breg=1e-6,
    kvs=20.0, on_bw=0.03, off_bw=0.01, T_out=0.3, T_in=0.08, T_blend=0.2, load_thr=0.01, td_min_frac=0.6, sigma=0.0, late_vz=2.0,
    kn=2e9, zeta=0.7, fcap=4.0, predict=0.0, sw_post=0.0, meas_avg=1.0, hold_sched=1.0, z_hold=0.05, rate_bw=0.0,
)


class FIDWalker:
    def __init__(self, ref, model, **kw):
        self.ref, self.m = ref, model; p = dict(DEFAULT); p.update(kw); self.p = p
        self.l = 42.4; self.W = model.W; self.mt = model.mtot; self.h = ref.h; self.ts = ref.ts; self.n2 = ref.n2; self.T2 = ref.n2 * ref.h
        self.D = np.diag([G0, G0] + [G0 / self.l] * 7)
        self.Pinv = np.linalg.pinv(model.CJ.T); self.Nm = null_space(model.CJ)          # 9x3: Nm^T Q = 0 <=> Q in range(CJ^T)
        self._build_ref(p["sigma"])
        self.flags = ref.F[:, :, 1] > p["load_thr"] * self.W                             # raw schedule (2N, 4)
        self.reset()

    # ------------------------------------------------------------------ reference splines (controller side)
    def _build_ref(self, sig):
        r = self.ref; n2, h, T2 = self.n2, self.h, self.T2; t = r.t
        dq = np.zeros(9); dq[0] = 2 * r.d; dc = np.array([2 * r.d, 0.0])
        def smooth(y, drift):
            y = np.asarray(y, float).reshape(n2, -1); lin = np.outer(t / T2, drift); y0 = y - lin
            if sig > 0:
                k = np.arange(-int(4 * sig) - 1, int(4 * sig) + 2); w = np.exp(-0.5 * (k / sig) ** 2); w /= w.sum()
                y0 = sum(wi * np.roll(y0, -ki, axis=0) for ki, wi in zip(k, w))
            return y0 + lin
        q = smooth(r.q, dq); F = smooth(r.F.reshape(n2, 8), np.zeros(8)); com = smooth(r.com, dc); comd = smooth(r.comd, np.zeros(2)); comdd = smooth(r.comdd, np.zeros(2))
        L = smooth(r.L[:, None], np.zeros(1)); Ld = smooth(r.Ldot[:, None], np.zeros(1))
        qp = np.vstack([q[1:], q[:1] + dq]); qm = np.vstack([q[-1:] - dq, q[:-1]])
        qd = (qp - qm) / (2 * h); qdd = (qp - 2 * q + qm) / h**2                                   # CD (as the optimiser), consistent with F
        def per(y, drift):
            tt = np.append(t, T2); yy = np.vstack([y, y[:1] + drift]) - np.outer(tt / T2, drift)
            return CubicSpline(tt, yy, bc_type="periodic")
        z9, z2 = np.zeros(9), np.zeros(2)
        self.S = dict(q=(per(q, dq), dq), qd=(per(qd, z9), z9), qdd=(per(qdd, z9), z9), F=(per(F, np.zeros(8)), np.zeros(8)), com=(per(com, dc), dc),
                      comd=(per(comd, z2), z2), comdd=(per(comdd, z2), z2), L=(per(L, np.zeros(1)), np.zeros(1)), Ldot=(per(Ld, np.zeros(1)), np.zeros(1)))

    def refc(self, tq):
        nw = np.floor(tq / self.T2); tl = tq - nw * self.T2; fr = tl / self.T2 + nw
        o = {k: s(tl) + dr * fr for k, (s, dr) in self.S.items()}
        o["F"] = o["F"].reshape(4, 2); o["F"][:, 1] = np.maximum(o["F"][:, 1], 0.0); o["L"] = float(o["L"][0]); o["Ldot"] = float(o["Ldot"][0])
        return o

    def t_off(self, i, tref):
        """Scheduled unload time (>= tref) of point i from the raw node flags; tref itself if unloaded now."""
        k0 = int(np.ceil(tref / self.h - 1e-9))
        for k in range(k0, k0 + self.n2):
            if not self.flags[k % self.n2, i]: return k * self.h
        return tref + 1e9

    def reset(self):
        self.parity = 0; self.t_anchor = 0.0; self.Xoff = 0.0; self.Fprev = np.zeros((4, 2)); self.inc = np.zeros(4, bool); self.t_in = np.zeros(4); self.f_in = np.zeros(4)
        self.t_rel = np.zeros(2) - 10.0; self.td_events = []; self.u_prev = None; self.F_meas = None   # F_meas: tick-averaged plant contact force of the last tick (set by the sim = contact-impulse read-back)
        self.log = {k: [] for k in ("t", "tref", "parity", "q", "com", "L", "F", "u", "fr_cmd", "status1", "status2", "tdrop", "ms", "resid", "loaded", "qdd", "fn_meas", "fmax", "u_act", "wrench", "wd")}

    def sense(self, q, qd, Minv, pts, Jp):
        fn = np.zeros(4); kn, z = self.p["kn"], self.p["zeta"]
        for i in range(4):
            pen = -pts[i, 1]
            if pen <= 0: continue
            jn = Jp[i, 1]; me = 1.0 / (jn @ Minv @ jn); fn[i] = max(0.0, kn * pen - 2 * z * np.sqrt(kn * me) * (jn @ qd))
        return fn

    def __call__(self, t, q, qd, k):
        m, p = self.m, self.p
        if k < 0:
            self.inc[:] = self.flags[0]; self.f_in[:] = self.ref.F[0, :, 1]; self.t_in[:] = -1.0      # start: reference contact state at node 0
            return self.ref.at(0.0)["u"].copy()
        t0 = time.perf_counter(); W, l, mu = self.W, self.l, p["mu"]
        coms, Jc, Jw, pts, Jp, ba, bp = m.kin(q, qd)
        M = Jc.T @ (m.MR[:, None] * Jc) + Jw.T @ (m.inertia[:, None] * Jw); h = Jc.T @ (m.MR * ba.reshape(-1)) + G0 * (Jc[1::2].T @ m.mass)
        Minv = np.linalg.inv(M); c = (m.mass[:, None] * coms).sum(0) / self.mt; comd = (m.MR * (Jc @ qd)).reshape(7, 2).sum(0) / self.mt
        vp = np.einsum("pdi,i->pd", Jp, qd)
        # ---- measured contact with hysteresis
        fn_meas = self.F_meas[:, 1].copy() if (p["meas_avg"] and self.F_meas is not None) else self.sense(q, qd, Minv, pts, Jp)
        tref_now = self.parity * self.ts + min(t - self.t_anchor, self.ts - 0.3 * self.h)
        for i in range(4):
            if not self.inc[i] and fn_meas[i] > p["on_bw"] * W: self.inc[i] = True; self.t_in[i] = t; self.f_in[i] = fn_meas[i]
            elif self.inc[i] and fn_meas[i] < p["off_bw"] * W:
                # keep a scheduled-loaded point that is still at the ground (stance foot stays flat; a release needs the schedule or a real lift)
                if p["hold_sched"] and self.t_off(i, tref_now) > tref_now + p["T_out"] and pts[i, 1] < p["z_hold"]: continue
                self.inc[i] = False
                if not self.inc[2 * (i // 2) + (1 - i % 2)]: self.t_rel[i // 2] = t
        # ---- touchdown / phase
        sw_leg = self.parity; tau = t - self.t_anchor
        land = [i for i in (2 * sw_leg, 2 * sw_leg + 1) if self.inc[i] and self.t_in[i] > self.t_anchor + 1e-9]
        if tau > p["td_min_frac"] * self.ts and land:
            i0 = land[0]; heel = 2 * sw_leg
            self.td_events.append((t, float(pts[heel, 0]), float(pts[2 * (1 - sw_leg), 0])))
            self.parity = 1 - self.parity; self.t_anchor = t; tau = 0.0
            rq = self.refc(self.parity * self.ts)["q"]; self.Xoff = pts[i0, 0] - m.points(rq)[i0, 0]
            sw_leg = self.parity
        st_leg = 1 - sw_leg; late = max(0.0, tau - (self.ts - 0.3 * self.h)); tref = self.parity * self.ts + min(tau, self.ts - 0.3 * self.h)
        r = self.refc(tref); qr, qdr, qddr = r["q"], r["qd"], r["qdd"]
        _, _, _, ptr, Jpr, _, bpr = m.kin(qr, qdr); ptvr = np.einsum("pdi,i->pd", Jpr, qdr); ptar = np.einsum("pdi,i->pd", Jpr, qddr) + bpr
        xo = np.array([self.Xoff, 0.0])
        # ---- contact set and force caps
        S = [i for i in range(4) if self.inc[i]]; nb = 2 * len(S); nx = 9 + nb
        fmax = np.zeros(4)
        for i in S:
            to = self.t_off(i, tref); ramp = np.clip((to - tref) / p["T_out"], 0.0, 1.0)
            fmax[i] = min(p["fcap"] * ramp, self.f_in[i] / W + (t - self.t_in[i]) / p["T_in"])
            if p["rate_bw"] > 0: fmax[i] = min(fmax[i], fn_meas[i] / W + p["rate_bw"])
        fmin = np.zeros(4)
        if p["rate_bw"] > 0:
            for i in S: fmin[i] = min(max(0.0, fn_meas[i] / W - p["rate_bw"]), fmax[i])
        # ---- generators
        Gm = np.zeros((3, nb)); Em = np.zeros((2 * len(S), nb)); Jb = np.zeros((9, nb))
        for a, i in enumerate(S):
            for b, sg in enumerate((1.0, -1.0)):
                col = 2 * a + b; g = np.array([sg * mu, 1.0])
                Gm[0, col], Gm[1, col] = g; Gm[2, col] = ((pts[i, 0] - c[0]) * g[1] - (pts[i, 1] - c[1]) * g[0]) / l
                Em[2 * a:2 * a + 2, col] = g; Jb[:, col] = W * (Jp[i].T @ g)
        # ---- quadratic cost  sum w ||T x - y||^2
        H = np.zeros((nx, nx)); gv = np.zeros(nx)
        def add(T, y, w):
            nonlocal H, gv
            if w <= 0: return
            H += w * T.T @ T; gv -= w * T.T @ y
        D = self.D; Z9 = np.zeros((1, nb))
        def qrow(T):   # rows acting on qdd only
            T = np.atleast_2d(T); return np.hstack([T, np.zeros((T.shape[0], nb))])
        def brow(T):
            T = np.atleast_2d(T); return np.hstack([np.zeros((T.shape[0], 9)), T])
        # centroidal
        cref = r["com"] + xo; cdd = r["comdd"] + np.array([p["om_x"] ** 2, p["om_z"] ** 2]) * (cref - c) + 2 * np.array([p["om_x"], p["om_z"]]) * (r["comd"] - comd)
        A = m.AG(q); Lm = float((A @ qd)[2]); Ldd = r["Ldot"] + p["kL"] * (r["L"] - Lm)
        wd = np.array([self.mt * cdd[0] / W, self.mt * (cdd[1] + G0) / W, Ldd / (W * l)])
        if nb:
            add(brow(Gm[0]), wd[:1], p["w_cx"]); add(brow(Gm[1]), wd[1:2], p["w_cz"]); add(brow(Gm[2]), wd[2:], p["w_L"])
            fref = np.concatenate([r["F"][i] / W for i in S]); fpv = np.concatenate([self.Fprev[i] / W for i in S])
            add(brow(Em), fref, p["w_prior"]); add(brow(Em), fpv, p["w_smooth"]); add(brow(np.eye(nb)), np.zeros(nb), p["w_breg"])
        else:   # flight: centroidal motion is ballistic; nothing to weigh
            pass
        # stance points
        for i in S:
            add(qrow(Jp[i] @ D / G0), (-p["kvs"] * vp[i] - bp[i]) / G0, p["w_st"])
        # feet
        Kp, Kd = p["om_sw"] ** 2, 2 * p["om_sw"]
        for leg in (0, 1):
            ns = sum(self.inc[2 * leg:2 * leg + 2])
            if ns == 2: continue
            fidx = 5 + 3 * leg; bl = 1.0
            if ns == 0:
                bl = float(np.clip((t - self.t_rel[leg]) / p["T_blend"], 0.0, 1.0)) if leg == sw_leg else 1.0
                i = 2 * leg; pr = ptr[i] + xo; vr = ptvr[i].copy(); ar = ptar[i].copy()
                if leg == sw_leg and late > 0: pr = pr + np.array([0.0, -p["late_vz"] * late]); vr = vr + np.array([0.0, -p["late_vz"]])
                a_des = ar + Kp * (pr - pts[i]) + Kd * (vr - vp[i])
                add(qrow(Jp[i] @ D / G0), (a_des - bp[i]) / G0, p["w_swing"] * bl)
            Kf, Kfd = p["om_fp"] ** 2, 2 * p["om_fp"]
            e = np.zeros(9); e[fidx] = 1.0
            a_f = qddr[fidx] + Kf * (qr[fidx] - q[fidx]) + Kfd * (qdr[fidx] - qd[fidx])
            add(qrow(e @ D / (G0 / l)), np.array([a_f / (G0 / l)]), p["w_fp"] * bl)
        # trunk
        e = np.zeros(9); e[2] = 1.0; a_th = qddr[2] + p["om_th"] ** 2 * (qr[2] - q[2]) + 2 * p["om_th"] * (qdr[2] - qd[2])
        add(qrow(e @ D / (G0 / l)), np.array([a_th / (G0 / l)]), p["w_trunk"])
        # posture
        CJ = m.CJ; Kq, Kqd = p["om_post"] ** 2, 2 * p["om_post"]
        a_j = CJ @ qddr + Kq * (CJ @ qr - CJ @ q) + Kqd * (CJ @ qdr - CJ @ qd)
        wpost = np.full(6, p["w_post"]); wpost[3 * sw_leg:3 * sw_leg + 3] += p["sw_post"]
        for j in range(6): add(qrow(CJ[j] @ D / (G0 / l)), np.array([a_j[j] / (G0 / l)]), wpost[j])
        add(qrow(np.eye(9)), np.zeros(9), p["w_reg"])
        # ---- hard rows
        Qx = np.hstack([M @ D, -Jb])                                       # Q = Qx x + h
        E = self.Nm.T @ Qx; e_ = -self.Nm.T @ h; sc = np.linalg.norm(E, axis=1); E /= sc[:, None]; e_ /= sc
        Au = (self.Pinv @ Qx) / m.LIM[:, None]; cu = (self.Pinv @ h) / m.LIM; lf = p["lim_frac"]
        Cin = [np.hstack([-Au]), np.hstack([Au])]; din = [cu - lf, -lf - cu]
        if nb:
            Cin.append(brow(np.eye(nb))); din.append(np.zeros(nb))
            for a, i in enumerate(S):
                Cin.append(-brow(Em[2 * a + 1])); din.append(np.array([-fmax[i]]))
                if fmin[i] > 0: Cin.append(brow(Em[2 * a + 1])); din.append(np.array([fmin[i]]))
        C = np.vstack(Cin); d = np.concatenate(din)
        U_, Sv, Vt = np.linalg.svd(E, full_matrices=True); rk = int((Sv > 1e-10 * Sv[0]).sum())
        x0 = Vt[:rk].T @ ((U_[:, :rk].T @ e_) / Sv[:rk]); Z = Vt[rk:].T
        Hz = Z.T @ H @ Z; Hz = 0.5 * (Hz + Hz.T); gz = Z.T @ (H @ x0 + gv)
        z, st, it, _ = gi_qp.solve(Hz, gz, C @ Z, d - C @ x0, 0); tdrop = 0
        if st != "ok":
            tdrop = 1; keep = np.ones(len(d), bool); keep[:12] = False
            z, st, it, _ = gi_qp.solve(Hz, gz, (C @ Z)[keep], (d - C @ x0)[keep], 0)
        x = x0 + Z @ z; qdd = D @ x[:9]; beta = x[9:]
        F = np.zeros((4, 2))
        for a, i in enumerate(S): F[i] = W * (Em[2 * a:2 * a + 2] @ beta)
        Q = M @ qdd + h - np.einsum("pdi,pd->i", Jp, F); u = self.Pinv @ Q; res = Q - CJ.T @ u
        u_act = int((np.abs(u) > (lf - 0.005) * m.LIM).any()); u = np.clip(u, -m.LIM, m.LIM)
        fr = max([abs(F[i, 0]) / (mu * F[i, 1]) for i in range(4) if F[i, 1] > 1e-6 * W] + [0.0])
        wrench = np.array([F[:, 0].sum(), F[:, 1].sum() - W, sum((pts[i, 0] - c[0]) * F[i, 1] - (pts[i, 1] - c[1]) * F[i, 0] for i in range(4))])
        self.Fprev = F.copy()
        Lg = self.log
        for kk, v in (("t", t), ("tref", tref), ("parity", self.parity), ("q", q.copy()), ("com", c.copy()), ("L", Lm), ("F", F), ("u", u.copy()), ("fr_cmd", fr), ("status1", "ok"), ("status2", st),
                      ("tdrop", tdrop), ("ms", 1e3 * (time.perf_counter() - t0)), ("resid", float(np.abs(res).max() / (W * l))), ("loaded", sum(1 << i for i in S)), ("qdd", qdd), ("fn_meas", fn_meas),
                      ("fmax", fmax), ("u_act", u_act), ("wrench", wrench), ("wd", wd)):
            Lg[kk].append(v)
        return u
