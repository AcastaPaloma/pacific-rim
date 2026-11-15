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
    # 9.3b-4 options (defaults = 9.3b-3 behaviour)
    onset=0.0,        # 1: the complementarity onset node (Fn = 0 by construction, next node loaded) counts as scheduled-loaded in t_off (no fmax = 0 at touchdown)
    fr_margin=1.0,    # QP friction cone mu * fr_margin (<= 1 tightens; the budget mu stays the reference for all metrics)
    land=0.0, land_k0=34.0, v_td=0.3, land_post=0.0, land_lead=0.0, om_land=0.0, land_act=0.0,   # 1: swing heel (x, z) and foot pitch follow a quintic from the reference at node land_k0 to the landing state
                                                        #    (heel at the reference landing point, velocity (0, -v_td), foot pitch/rate of the reference just after landing); swing-leg posture weight x land_post meanwhile
                                                        #    land_act 1: the quintic starts from the MEASURED swing heel/pitch state (pos, vel; acc 0) on the first tick of the window (re-planned once per step)
                                                        #    the quintic ends land_lead s before ts, then the heel target keeps descending at v_td (x, pitch held); om_land > 0: swing PD frequency in that window
    start_par=0.0,    # 1: start in the mirrored step (state = ref node N, leg A new stance) for left/right symmetry tests
    fr_meas=0.0,      # 1: per-foot shear row |sum Ft_cmd| <= mu fr_margin * sum Fn_meas (last tick's read-back): the shear also fits the load the plant actually carries
    fmin_td=0.0, T_hold=0.3,   # > 0: a point keeps Fn_cmd >= min(fmin_td m g, fmax/2) for T_hold s after its first contact (no heel bounce after touchdown)
    land_xlead=0.0,   # > 0 (s): with land_act, the re-planned heel x (to rest) and foot pitch reach the landing state land_xlead s before heel z touches (vertical final approach)
    land_vclip=0.0,   # 1: the re-planned landing quintic's z channel starts from a clipped heel z-velocity (bisection) so that heel z stays >= 0 before T
    land_nounder=0.0, # 1: the re-planned landing quintic is lengthened (h/4 steps) until its heel z stays >= 0 before the end (no early impact)
    # 9.3b-5 options (defaults off = b4 behaviour)
    mass_est=0.0,     # > 0: online mass-scale estimate ms (filter gain per tick): ms -> sum Fn_meas / (m (g + dcomd_z/dt)) of the last tick (exact for the plant); M, h, W, m, L are scaled by ms
    mass_lo=0.7, mass_hi=1.5,   # clip of the estimate
    sg_floor=0.0,     # > 0 (in m g): while the swing foot has no measured contact the stance leg's points keep fmax >= min(sg_floor, rate limit) (the unload ramp cannot drop below it)
    sg_fmin=0.0,      # > 0 (in m g): and, in that phase, the stance leg's summed Fn_cmd must stay >= sg_fmin (support guard, lower bound)
    # 9.3b-6 options (defaults off = b5 behaviour)
    st_zk=0.0,        # > 0 (1/s^2): a measured-loaded point that is above the ground (rebound after the strike) is pulled down: a_z = -st_kv vz - st_zk z (the b5 heel hovered 0.16 m for 40 ticks and re-struck)
    st_kv=20.0,       # velocity gain of that pull-down (1/s)
    sh_unl=0.0,       # > 0 (s): per-point shear authority s_i = clip((t_off - tref)/sh_unl, 0, 1) scales the point's friction coefficient (and stance-accel weight): shear is gone before the normal load is
    sh_land=0.0,      # > 0 (s): per-point shear authority of a point after first contact s_i = clip((t - t_in)/sh_land, 0, 1)
    sh_wmin=0.01,     # lower bound of s_i for the stance-accel weight
    sh_floor=0.0,     # > 0 (margin m_s <= 1): per foot, the summed Fn_cmd is held >= |last tick's measured foot shear| / (mu m_s) (the plant's tangential spring cannot unload in one tick: normal load goes down only as fast as the realised shear), relaxed to the foot's fmax
)


class FIDWalker:
    def __init__(self, ref, model, **kw):
        self.ref, self.m = ref, model; p = dict(DEFAULT); p.update(kw); self.p = p
        self.l = 42.4; self.W = model.W; self.mt = model.mtot; self.h = ref.h; self.ts = ref.ts; self.n2 = ref.n2; self.T2 = ref.n2 * ref.h
        self.D = np.diag([G0, G0] + [G0 / self.l] * 7)
        self.Pinv = np.linalg.pinv(model.CJ.T); self.Nm = null_space(model.CJ)          # 9x3: Nm^T Q = 0 <=> Q in range(CJ^T)
        self._build_ref(p["sigma"]); self._build_land()
        self.flags = ref.F[:, :, 1] > p["load_thr"] * self.W                             # raw schedule (2N, 4)
        if p["onset"]: self.flags = self.flags | np.roll(self.flags, -1, axis=0)           # onset node of each loaded interval counts as loaded (9.3b-4)
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

    def _build_land(self):
        """Per parity: quintic (heel x, heel z, foot pitch) of the swing leg over step-local tau in [tau0, ts] (no Xoff; added at use)."""
        self.LQ = {}
        if not self.p["land"]: return
        m, h, ts = self.m, self.h, self.ts; tau0 = self.p["land_k0"] * h; Tl = ts - self.p["land_lead"] - tau0
        for par in (0, 1):
            i = 2 * par; fi = 5 + 3 * par
            r0 = self.refc(par * ts + tau0); _, _, _, pt0, Jp0, _, bp0 = m.kin(r0["q"], r0["qd"])
            p0 = np.array([pt0[i, 0], pt0[i, 1], r0["q"][fi]]); v0 = np.array([*(Jp0[i] @ r0["qd"]), r0["qd"][fi]]); a0 = np.array([*(Jp0[i] @ r0["qdd"] + bp0[i]), r0["qdd"][fi]])
            r1 = self.refc((par + 1) * ts); r1b = self.refc((par + 1) * ts + h); pt1 = m.points(r1["q"])
            p1 = np.array([pt1[i, 0], 0.0, r1["q"][fi]]); v1 = np.array([0.0, -self.p["v_td"], (r1b["q"][fi] - r1["q"][fi]) / h]); a1 = np.zeros(3)
            # quintic coefficients per channel, s in [0, Tl]
            T = Tl; A = np.array([[T**3, T**4, T**5], [3 * T**2, 4 * T**3, 5 * T**4], [6 * T, 12 * T**2, 20 * T**3]])
            co = np.zeros((6, 3))
            for c in range(3):
                b = np.array([p1[c] - (p0[c] + v0[c] * T + 0.5 * a0[c] * T**2), v1[c] - (v0[c] + a0[c] * T), a1[c] - a0[c]])
                co[:, c] = [p0[c], v0[c], 0.5 * a0[c], *np.linalg.solve(A, b)]
            self.LQ[par] = (tau0, Tl, co); self.LQend = getattr(self, "LQend", {}); self.LQend[par] = (p1, v1)

    def _replan_land(self, par, tau, x0, v0, xo):
        """land_act: quintic from the measured state (x0, v0) at tau to the landing state of the reference (+ Xoff), ending at ts - land_lead."""
        p1, v1 = self.LQend[par]; p1 = p1 + np.array([xo[0], 0.0, 0.0]); T = max(self.ts - self.p["land_lead"] - tau, 2 * self.h)
        for _ in range(80):
            A = np.array([[T**3, T**4, T**5], [3 * T**2, 4 * T**3, 5 * T**4], [6 * T, 12 * T**2, 20 * T**3]]); co = np.zeros((6, 3))
            for c in range(3):
                b = np.array([p1[c] - (x0[c] + v0[c] * T), v1[c] - v0[c], 0.0]); co[:, c] = [x0[c], v0[c], 0.0, *np.linalg.solve(A, b)]
            if self.p["land_vclip"] and v0[1] < 0:          # 9.3b-4 r2: z channel must not reach the ground before T (early strike with the heel still moving fast in x)
                ss = np.linspace(0, T, 41)[:-2]
                def zmin(vz):
                    b = np.array([p1[1] - (x0[1] + vz * T), v1[1] - vz, 0.0]); cz = np.array([x0[1], vz, 0.0, *np.linalg.solve(A, b)]); return np.polyval(cz[::-1], ss).min(), cz
                zm, cz = zmin(v0[1])
                if zm < 0:
                    lo, hi = v0[1], 0.0
                    for _ in range(30):
                        mid = 0.5 * (lo + hi); zm, _ = zmin(mid)
                        lo, hi = (mid, hi) if zm < 0 else (lo, mid)
                    _, cz = zmin(hi); co[:, 1] = cz
            if not self.p["land_nounder"]: break
            ss = np.linspace(0, T, 41)[:-2]; zz = np.polyval(co[::-1, 1], ss)
            if zz.min() >= 0: break
            T += 0.25 * self.h
        self.LQx = None
        if self.p["land_xlead"] > 0:                      # 9.3b-4 r2: x and pitch reach the landing state land_xlead s before z does, then hold (no tangential heel speed at strike)
            Tx = max(T - self.p["land_xlead"], 2 * self.h); Ax = np.array([[Tx**3, Tx**4, Tx**5], [3 * Tx**2, 4 * Tx**3, 5 * Tx**4], [6 * Tx, 12 * Tx**2, 20 * Tx**3]])
            for c in (0, 2):
                b = np.array([p1[c] - (x0[c] + v0[c] * Tx), 0.0 - v0[c] if c == 0 else v1[c] - v0[c], 0.0]); co[:, c] = [x0[c], v0[c], 0.0, *np.linalg.solve(Ax, b)]
            self.LQx = Tx
        co[0, 0] -= xo[0]                       # stored without Xoff (added back at use, as for the reference quintic)
        self.LQ[par] = (tau, T, co); self.land_plan = (self.t_anchor, par)

    def land_target(self, par, tau):
        """(pos, vel, acc) of [heel x, heel z, foot pitch] for the swing leg at step-local tau (clamped to the landing end)."""
        tau0, Tl, co = self.LQ[par]; s = float(np.clip(tau - tau0, 0.0, Tl)); ex = max(0.0, tau - tau0 - Tl)
        P_ = np.array([1, s, s**2, s**3, s**4, s**5]); V_ = np.array([0, 1, 2 * s, 3 * s**2, 4 * s**3, 5 * s**4]); A_ = np.array([0, 0, 2, 6 * s, 12 * s**2, 20 * s**3])
        pos, vel, acc = P_ @ co, V_ @ co, A_ @ co
        if getattr(self, "LQx", None) and self.p["land_act"]:
            sx = float(np.clip(tau - tau0, 0.0, self.LQx)); Px = np.array([1, sx, sx**2, sx**3, sx**4, sx**5]); Vx = np.array([0, 1, 2 * sx, 3 * sx**2, 4 * sx**3, 5 * sx**4]); Axx = np.array([0, 0, 2, 6 * sx, 12 * sx**2, 20 * sx**3])
            for c in (0, 2): pos[c], vel[c], acc[c] = Px @ co[:, c], Vx @ co[:, c], Axx @ co[:, c]
            if tau - tau0 > self.LQx: vel[0] = vel[2] = 0.0; acc[0] = acc[2] = 0.0
        if ex > 0: pos = pos + np.array([0.0, -self.p["v_td"] * ex, 0.0]); vel = np.array([0.0, -self.p["v_td"], 0.0]); acc = np.zeros(3)
        return pos, vel, acc

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
        self.ms = 1.0; self._comd_prev = None; self._t_prev = None
        self.t_rel = np.zeros(2) - 10.0; self.td_events = []; self.u_prev = None; self.F_meas = None   # F_meas: tick-averaged plant contact force of the last tick (set by the sim = contact-impulse read-back)
        self.log = {k: [] for k in ("t", "tref", "parity", "q", "com", "L", "F", "u", "fr_cmd", "status1", "status2", "tdrop", "ms", "resid", "loaded", "qdd", "fn_meas", "fmax", "u_act", "wrench", "wd", "iters", "mest", "dbg")}

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
            k0 = int(p["start_par"]) * self.ref.N; self.parity = int(p["start_par"])
            self.inc[:] = self.flags[k0]; self.f_in[:] = self.ref.F[k0, :, 1]; self.t_in[:] = -1.0      # start: reference contact state at node 0 (N if mirrored)
            return self.ref.at(k0 * self.h)["u"].copy()
        t0 = time.perf_counter(); W, l, mu = self.W, self.l, p["mu"]
        coms, Jc, Jw, pts, Jp, ba, bp = m.kin(q, qd)
        M = Jc.T @ (m.MR[:, None] * Jc) + Jw.T @ (m.inertia[:, None] * Jw); h = Jc.T @ (m.MR * ba.reshape(-1)) + G0 * (Jc[1::2].T @ m.mass)
        Minv = np.linalg.inv(M); c = (m.mass[:, None] * coms).sum(0) / self.mt; comd = (m.MR * (Jc @ qd)).reshape(7, 2).sum(0) / self.mt
        if p["mass_est"] > 0:                              # 9.3b-5: online mass-scale estimate from the last tick's read-back (sum Fn_meas = m_true (g + CoM z accel)); no effect before the first read-back
            if self.F_meas is not None and self._comd_prev is not None and t > self._t_prev + 1e-9:
                den = G0 + (comd[1] - self._comd_prev[1]) / (t - self._t_prev); ftot = float(self.F_meas[:, 1].sum())
                if den > 0.5 * G0 and ftot > 0.4 * self.W: self.ms = float(np.clip(self.ms + p["mass_est"] * (ftot / (self.mt * den) - self.ms), p["mass_lo"], p["mass_hi"]))
            self._comd_prev = comd.copy(); self._t_prev = t
        ms = self.ms
        if ms != 1.0: W = self.W * ms; M = M * ms; h = h * ms; Minv = Minv / ms
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
        self._dbg = np.zeros(12); st_leg = 1 - sw_leg; late = max(0.0, tau - (self.ts - 0.3 * self.h)); tref = self.parity * self.ts + min(tau, self.ts - 0.3 * self.h)
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
        sg_on = (p["sg_floor"] > 0 or p["sg_fmin"] > 0) and not any(self.inc[2 * sw_leg:2 * sw_leg + 2])       # support guard: single support, swing foot not (yet) in measured contact
        if sg_on and p["sg_floor"] > 0:
            for i in S:
                if i // 2 == st_leg: fmax[i] = max(fmax[i], min(p["sg_floor"], self.f_in[i] / W + (t - self.t_in[i]) / p["T_in"]))
        fmin = np.zeros(4)
        if p["rate_bw"] > 0:
            for i in S: fmin[i] = min(max(0.0, fn_meas[i] / W - p["rate_bw"]), fmax[i])
        if p["fmin_td"] > 0:
            for i in S:
                if t - self.t_in[i] < p["T_hold"] and self.t_in[i] >= 0: fmin[i] = max(fmin[i], min(p["fmin_td"], 0.5 * fmax[i]))
        # ---- per-point shear authority (9.3b-6): 1 = full cone, 0 = no shear (unloading points shed shear before the normal load; a point just landed starts without shear)
        sh_s = np.ones(4)
        for i in S:
            if p["sh_unl"] > 0: sh_s[i] = min(sh_s[i], float(np.clip((self.t_off(i, tref) - tref) / p["sh_unl"], 0.0, 1.0)))
            if p["sh_land"] > 0 and self.t_in[i] >= 0: sh_s[i] = min(sh_s[i], float(np.clip((t - self.t_in[i]) / p["sh_land"], 0.0, 1.0)))
        # ---- generators
        Gm = np.zeros((3, nb)); Em = np.zeros((2 * len(S), nb)); Jb = np.zeros((9, nb))
        for a, i in enumerate(S):
            for b, sg in enumerate((1.0, -1.0)):
                col = 2 * a + b; g = np.array([sg * mu * p["fr_margin"] * sh_s[i], 1.0])
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
        A = m.AG(q); Lm = float((A @ qd)[2]) * ms; Ldd = r["Ldot"] + p["kL"] * (r["L"] - Lm)
        wd = np.array([self.mt * ms * cdd[0] / W, self.mt * ms * (cdd[1] + G0) / W, Ldd / (W * l)])
        if nb:
            add(brow(Gm[0]), wd[:1], p["w_cx"]); add(brow(Gm[1]), wd[1:2], p["w_cz"]); add(brow(Gm[2]), wd[2:], p["w_L"])
            fref = np.concatenate([r["F"][i] / W for i in S]); fpv = np.concatenate([self.Fprev[i] / W for i in S])
            add(brow(Em), fref, p["w_prior"]); add(brow(Em), fpv, p["w_smooth"]); add(brow(np.eye(nb)), np.zeros(nb), p["w_breg"])
        else:   # flight: centroidal motion is ballistic; nothing to weigh
            pass
        # stance points
        for i in S:
            ya = -p["kvs"] * vp[i] - bp[i]
            if p["st_zk"] > 0 and pts[i, 1] > 0: ya = ya.copy(); ya[1] = -p["st_kv"] * vp[i, 1] - p["st_zk"] * pts[i, 1] - bp[i, 1]
            add(qrow(Jp[i] @ D / G0), ya / G0, p["w_st"] * max(sh_s[i], p["sh_wmin"]))
        # feet
        Kp, Kd = p["om_sw"] ** 2, 2 * p["om_sw"]; land_on = False; lt = None
        for leg in (0, 1):
            ns = sum(self.inc[2 * leg:2 * leg + 2])
            if ns == 2: continue
            fidx = 5 + 3 * leg; bl = 1.0
            if ns == 0:
                bl = float(np.clip((t - self.t_rel[leg]) / p["T_blend"], 0.0, 1.0)) if leg == sw_leg else 1.0
                i = 2 * leg; pr = ptr[i] + xo; vr = ptvr[i].copy(); ar = ptar[i].copy()
                lt = None; Kp_, Kd_ = Kp, Kd
                if leg == sw_leg and p["land"] and p["land_act"] and tau >= p["land_k0"] * self.h and getattr(self, "land_plan", None) != (self.t_anchor, self.parity):
                    self._replan_land(self.parity, tau, np.array([pts[i, 0], pts[i, 1], q[fidx]]), np.array([vp[i, 0], vp[i, 1], qd[fidx]]), xo)
                if leg == sw_leg and p["land"] and tau >= self.LQ[self.parity][0]:
                    lt = self.land_target(self.parity, tau); pr = lt[0][:2] + xo; vr = lt[1][:2].copy(); ar = lt[2][:2].copy()
                    if p["om_land"] > 0: Kp_, Kd_ = p["om_land"] ** 2, 2 * p["om_land"]
                if leg == sw_leg and late > 0: pr = pr + np.array([0.0, -p["late_vz"] * late]); vr = vr + np.array([0.0, -p["late_vz"]])
                a_des = ar + Kp_ * (pr - pts[i]) + Kd_ * (vr - vp[i]); self._dbg = np.array([*(pr + 0), *vr, *pts[i], *vp[i], *ar, *a_des])
                add(qrow(Jp[i] @ D / G0), (a_des - bp[i]) / G0, p["w_swing"] * bl)
            Kf, Kfd = p["om_fp"] ** 2, 2 * p["om_fp"]
            e = np.zeros(9); e[fidx] = 1.0
            a_f = qddr[fidx] + Kf * (qr[fidx] - q[fidx]) + Kfd * (qdr[fidx] - qd[fidx])
            if ns == 0 and leg == sw_leg and lt is not None:
                a_f = lt[2][2] + Kf * (lt[0][2] - q[fidx]) + Kfd * (lt[1][2] - qd[fidx]); land_on = True
            add(qrow(e @ D / (G0 / l)), np.array([a_f / (G0 / l)]), p["w_fp"] * bl)
        # trunk
        e = np.zeros(9); e[2] = 1.0; a_th = qddr[2] + p["om_th"] ** 2 * (qr[2] - q[2]) + 2 * p["om_th"] * (qdr[2] - qd[2])
        add(qrow(e @ D / (G0 / l)), np.array([a_th / (G0 / l)]), p["w_trunk"])
        # posture
        CJ = m.CJ; Kq, Kqd = p["om_post"] ** 2, 2 * p["om_post"]
        a_j = CJ @ qddr + Kq * (CJ @ qr - CJ @ q) + Kqd * (CJ @ qdr - CJ @ qd)
        wpost = np.full(6, p["w_post"]); wpost[3 * sw_leg:3 * sw_leg + 3] += p["sw_post"]
        if land_on: wpost[3 * sw_leg:3 * sw_leg + 3] *= p["land_post"]
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
            if sg_on and p["sg_fmin"] > 0:                                   # summed stance-leg Fn_cmd >= sg_fmin (relaxed to what the leg's fmax allows)
                al = [a for a, i in enumerate(S) if i // 2 == st_leg]
                if al: Cin.append(brow(sum(Em[2 * a + 1] for a in al))); din.append(np.array([min(p["sg_fmin"], sum(fmax[S[a]] for a in al))]))
            if p["sh_floor"] > 0 and self.F_meas is not None:
                for f in (0, 1):
                    al = [a for a, i in enumerate(S) if i // 2 == f]
                    if not al: continue
                    fl = abs(float(self.F_meas[2 * f:2 * f + 2, 0].sum())) / W / (mu * p["sh_floor"]); fl = min(fl, sum(fmax[S[a]] for a in al) * 0.98)
                    if fl > 1e-4: Cin.append(brow(sum(Em[2 * a + 1] for a in al))); din.append(np.array([fl]))
            if p["fr_meas"]:
                for f in (0, 1):
                    al = [a for a, i in enumerate(S) if i // 2 == f]
                    if not al: continue
                    tv = sum(Em[2 * a] for a in al); cap = max(mu * p["fr_margin"] * fn_meas[2 * f:2 * f + 2].sum() / W, 1e-3)   # floor: the +-rows never coincide (GI degeneracy)
                    Cin.append(-brow(tv)); din.append(np.array([-cap])); Cin.append(brow(tv)); din.append(np.array([-cap]))
        C = np.vstack(Cin); d = np.concatenate(din)
        U_, Sv, Vt = np.linalg.svd(E, full_matrices=True); rk = int((Sv > 1e-10 * Sv[0]).sum())
        x0 = Vt[:rk].T @ ((U_[:, :rk].T @ e_) / Sv[:rk]); Z = Vt[rk:].T
        Hz = Z.T @ H @ Z; Hz = 0.5 * (Hz + Hz.T); gz = Z.T @ (H @ x0 + gv)
        z, st, it, _ = gi_qp.solve(Hz, gz, C @ Z, d - C @ x0, 0); tdrop = 0; st1 = st; it1 = it         # st1: the full QP (all hard rows); only this counts as solver success
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
        for kk, v in (("t", t), ("tref", tref), ("parity", self.parity), ("q", q.copy()), ("com", c.copy()), ("L", Lm), ("F", F), ("u", u.copy()), ("fr_cmd", fr), ("status1", st1), ("status2", st), ("iters", it1),
                      ("tdrop", tdrop), ("ms", 1e3 * (time.perf_counter() - t0)), ("resid", float(np.abs(res).max() / (W * l))), ("loaded", sum(1 << i for i in S)), ("qdd", qdd), ("fn_meas", fn_meas),
                      ("fmax", fmax), ("mest", ms), ("dbg", getattr(self, "_dbg", np.zeros(12))), ("u_act", u_act), ("wrench", wrench), ("wd", wd)):
            Lg[kk].append(v)
        return u
