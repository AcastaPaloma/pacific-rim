"""T-005 r4 step 9.3a: planar plant integrator + joint-PD replay sanity.  Import has no side effects.
Run from the project root:  /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/planar_sim.py [--steps 20]

API (for 9.3b)
  sim = PlantSim(model, mu=0.8, kn=2e9, zeta=0.7, eps_v=0.02, dt=1/600, ctrl_hz=60, delay_ticks=1, lim_scale=1.0)
  res = sim.run(q0, qd0, controller, t_end, fall_fn=None, ref=None)
    controller(t, q, qd, tick) -> u (6,) joint torques [hipA,kneeA,ankleA,hipB,kneeB,ankleB], N m. Called once per tick (60 Hz) on the state at the
      tick start; its output is held (ZOH) and applied delay_ticks ticks later (default one tick). Queue pre-filled with controller(0, q0, qd0, -1).
    Torques are clipped to +-lim_scale*LIM per joint (res['sat'] counts clipped joint-ticks).
    fall: fall_fn(t, q) -> bool, default fall_detect(ref): hip z < 0.7 ref hip z(t) or |th| > 30 deg.
    res: dict of arrays per tick: t, q, qd, u_cmd, u_app, Fn (4), Ft (4), sat (6 bool), plus fell (bool), t_fall.
  Contact: per point spring-damper normal Fn = max(0, kn*pen - cn*vz) with cn = 2*zeta*sqrt(kn*m_eff) (m_eff = point effective mass from M^-1),
      friction Ft = -mu*Fn*tanh(vt/eps_v), capped at m_eff_t*|vt|/dt so one sub-step cannot reverse the slip velocity (stability).
      Semi-implicit Euler (qd then q), M and h analytic, ~0.1 ms per sub-step.
  friction="tanh" (default, the law above; all runs before 9.3b-3) or "stick" (9.3b-3, stick-slip, closer to PhysX patch friction; mu unchanged):
      each loaded point gets a tangential anchor x_a at first contact; trial Ft_p = -kt (x - x_a) - ct vt (ct = 2 zeta_t sqrt(kt m_eff_t)). One friction
      patch per foot: T = sum of the foot's trial Ft_p; if |T| > mu sum Fn the foot slips, T = sign(T) mu sum Fn, and its anchors are moved so that the
      springs alone give T (x_a = x + T/(n kt)). T is shared by the foot's points in proportion to Fn (no internal heel-toe shear). An anchor is dropped
      when its point leaves the ground (pen <= 0). Static friction up to mu Fn with no slip velocity needed. kt default = kn, zeta_t = 0.7.
"""
import numpy as np, time, json, pathlib, argparse
import planar_model as P

DT = 1.0 / 600


def fall_detect(ref, hip_frac=0.7, lean_deg=30.0):
    def f(t, q):
        zr = ref.at(t)["q"][1]
        return bool(q[1] < hip_frac * zr or abs(q[2]) > np.deg2rad(lean_deg))
    return f


class PlantSim:
    def __init__(self, model, mu=0.8, kn=2e9, zeta=0.7, eps_v=0.02, dt=DT, ctrl_hz=60, delay_ticks=1, lim_scale=1.0, mass_scale=1.0, friction="tanh", kt=None, zeta_t=0.7):
        self.m = model; self.mu = mu; self.kn = kn; self.zeta = zeta; self.eps = eps_v; self.dt = dt; self.hz = ctrl_hz
        assert friction in ("tanh", "stick"); self.friction = friction; self.kt = kn if kt is None else kt; self.zeta_t = zeta_t; self.reset_contact()
        self.sub = int(round(1.0 / (ctrl_hz * dt))); self.delay = delay_ticks; self.lim = lim_scale * model.LIM
        self.CJT = model.CJ.T

    def reset_contact(self):
        self.anc = np.full(4, np.nan); self.n_slip = 0; self.n_stick = 0           # stick-slip anchors (x) and sub-step counters (loaded points)

    def contact(self, M, pts, Jp, qd, Minv):
        F = np.zeros((4, 2)); vp = np.einsum("pdi,i->pd", Jp, qd); dt = self.dt
        for p in range(4):
            pen = -pts[p, 1]
            if pen <= 0:
                self.anc[p] = np.nan; continue
            jn = Jp[p, 1]; jt = Jp[p, 0]
            me_n = 1.0 / (jn @ Minv @ jn); me_t = 1.0 / (jt @ Minv @ jt)
            cn = 2 * self.zeta * np.sqrt(self.kn * me_n)
            fn = max(0.0, self.kn * pen - cn * vp[p, 1])
            vt = vp[p, 0]
            if self.friction == "stick":                       # trial spring-damper force per point; the patch (foot) total is limited below
                if np.isnan(self.anc[p]): self.anc[p] = pts[p, 0]
                F[p] = (-self.kt * (pts[p, 0] - self.anc[p]) - 2 * self.zeta_t * np.sqrt(self.kt * me_t) * vt, fn); continue
            ft = -self.mu * fn * np.tanh(vt / self.eps)
            cap = me_t * abs(vt) / dt
            ft = float(np.clip(ft, -cap, cap))
            F[p] = (ft, fn)
        if self.friction == "stick":                           # one friction patch per foot (as PhysX patch friction): |sum Ft| <= mu sum Fn, shared by Fn
            for f in (0, 1):
                ld = [p for p in (2 * f, 2 * f + 1) if not np.isnan(self.anc[p])]
                if not ld: continue
                T = sum(F[p, 0] for p in ld); Fs = sum(F[p, 1] for p in ld); cap = self.mu * Fs
                if abs(T) > cap:
                    T = float(np.sign(T) * cap); self.n_slip += 1
                    for p in ld: self.anc[p] = pts[p, 0] + T / len(ld) / self.kt          # re-anchor: springs alone give the capped force
                else: self.n_stick += 1
                for p in ld: F[p, 0] = T * (F[p, 1] / Fs if Fs > 0 else 0.0)
        return F

    def run(self, q0, qd0, controller, t_end, fall_fn=None, ref=None):
        m = self.m; q = np.array(q0, float); qd = np.array(qd0, float); dt = self.dt; sub = self.sub; self.reset_contact()
        if fall_fn is None and ref is not None: fall_fn = fall_detect(ref)
        u0 = np.asarray(controller(0.0, q, qd, -1), float); queue = [u0.copy() for _ in range(self.delay)]
        n_ticks = int(round(t_end * self.hz)); L = {k: [] for k in ("t", "q", "qd", "u_cmd", "u_app", "Fn", "Ft", "sat")}
        fell = False; t_fall = None; Fcur = np.zeros((4, 2))
        for k in range(n_ticks):
            t = k / self.hz
            uc = np.asarray(controller(t, q, qd, k), float); queue.append(uc)
            u_app = np.clip(queue.pop(0), -self.lim, self.lim) if self.delay > 0 else np.clip(uc, -self.lim, self.lim)
            L["t"].append(t); L["q"].append(q.copy()); L["qd"].append(qd.copy()); L["u_cmd"].append(uc); L["u_app"].append(u_app)
            Fsum = np.zeros((4, 2))
            for s in range(sub):
                M, h, pts, Jp, _, _ = m.Mh(q, qd)
                Minv = np.linalg.inv(M)
                F = self.contact(M, pts, Jp, qd, Minv)
                Q = self.CJT @ u_app + np.einsum("pdi,pd->i", Jp, F) - h
                qdd = Minv @ Q
                qd = qd + dt * qdd; q = q + dt * qd; Fsum += F
                if not (np.all(np.isfinite(q)) and np.all(np.isfinite(qd))): raise FloatingPointError(f"plant blew up at t={t + s * dt:.3f}")
            Fm = Fsum / sub; L["Fn"].append(Fm[:, 1]); L["Ft"].append(Fm[:, 0])
            L["sat"].append(np.abs(u_app) >= self.lim - 1e-9)
            if fall_fn is not None and fall_fn((k + 1) / self.hz, q):
                fell = True; t_fall = (k + 1) / self.hz; break
        out = {k: np.array(v) for k, v in L.items()}; out["fell"] = fell; out["t_fall"] = t_fall; out["t_end"] = (len(L["t"])) / self.hz
        out["q_end"] = q; out["qd_end"] = qd
        return out


# ---------------------------------------------------------------- joint PD replay (imp 2 analogue)
class PDReplay:
    """u = [ff] + Kp (j_ref - j) + Kd (jd_ref - jd); Kp = w^2 Lam_j, Kd = 2 w Lam_j (critically damped), Lam_j = 1/(CJ M^-1 CJ^T)_jj at the reference pose."""
    def __init__(self, ref, model, f_hz=1.5, ff=True, gain_scale=1.0):
        self.ref, self.m, self.w = ref, model, 2 * np.pi * f_hz; self.ff = ff; self.gs = gain_scale

    def gains(self, qr):
        M = self.m.M(qr); Lam = 1.0 / np.diag(self.m.CJ @ np.linalg.solve(M, self.m.CJ.T))
        return self.gs * self.w**2 * Lam, self.gs * 2 * self.w * Lam

    def __call__(self, t, q, qd, k):
        r = self.ref.at(max(t, 0.0)); m = self.m
        jr, jdr = m.joint_angles(r["q"]), m.joint_rates(r["q"], r["qd"]); Kp, Kd = self.gains(r["q"])
        u = Kp * (jr - m.joint_angles(q)) + Kd * (jdr - m.joint_rates(q, qd))
        return u + r["u"] if self.ff else u


def metrics(res, ref, model, label):
    t = res["t"]; q = res["q"]; n = len(t); out = {"label": label, "fell": bool(res["fell"]), "t_end_s": float(res["t_end"]), "steps": float(res["t_end"] / ref.ts)}
    Rq = np.array([ref.at(x)["q"] for x in t]); Jr = Rq @ model.CJ.T; Ja = q @ model.CJ.T
    stance_B = (np.floor(t / ref.ts).astype(int) % 2) == 0          # step 1 (and odd steps): leg B stance, leg A swing
    knee_st = np.where(stance_B, 4, 1); knee_sw = np.where(stance_B, 1, 4); idx = np.arange(n)
    err = np.rad2deg(Ja - Jr)
    out["peak_knee_err_stance_deg"] = float(np.abs(err[idx, knee_st]).max()); out["peak_knee_err_swing_deg"] = float(np.abs(err[idx, knee_sw]).max())
    out["mean_speed"] = float((q[-1, 0] - q[0, 0]) / max(t[-1] - t[0], 1e-9)) if n > 1 else 0.0; out["v_ref"] = float(ref.v)
    out["mean_speed_ratio"] = out["mean_speed"] / ref.v
    out["hip_z_rms_err_m"] = float(np.sqrt(np.mean((q[:, 1] - Rq[:, 1]) ** 2)))
    cz = np.array([model.com(x)[1] for x in q]); czr = np.array([ref.at(x)["com"][1] for x in t])
    out["com_z_rms_err_m"] = float(np.sqrt(np.mean((cz - czr) ** 2))); out["com_z_rms_err_pct_l"] = 100 * out["com_z_rms_err_m"] / model.l_leg
    out["trunk_lean_rms_err_deg"] = float(np.rad2deg(np.sqrt(np.mean((q[:, 2] - Rq[:, 2]) ** 2))))
    # best-fit time lag of knees (j_act(t) ~ j_ref(t - tau)), swing and stance separately
    def lag_of(sel_knee, mask):
        best = (1e30, 0.0)
        for tau in np.arange(0.0, 1.001, 0.02):
            jr = np.array([ref.at(x - tau)["q"] @ model.CJ.T for x in t])
            e = ((Ja - jr)[idx, sel_knee])[mask]
            if mask.sum() > 3:
                v = float(np.mean(e**2))
                if v < best[0]: best = (v, tau)
        return best[1]
    ok = t > 0.2 * ref.ts
    out["lag_fit_swing_knee_s"] = lag_of(knee_sw, ok); out["lag_fit_stance_knee_s"] = lag_of(knee_st, ok)
    out["sat_ticks_pct"] = float(100 * res["sat"].any(1).mean())
    return out


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--steps", type=float, default=20); ap.add_argument("--f_hz", type=float, default=1.5)
    ap.add_argument("--ref", default=None); a = ap.parse_args()
    out_dir = P.ROOT / "Tools/GaitOpt/out/proto"; out_dir.mkdir(parents=True, exist_ok=True)
    model = P.PlanarModel(); ref = P.load_reference(a.ref, model)
    P.export_v2(ref, out_dir / "ref_fr010_v2.json")
    print(f"reference: N={ref.N} step {ref.ts:.3f} s v {ref.v:.3f}  force-consistency max resid {ref.fc_resid:.2e} m g (x {ref.fc_resid_x:.1e}, z {ref.fc_resid_z:.1e}), Ldot resid {ref.Ldot_resid:.1e}")
    sim = PlantSim(model); q0, qd0 = ref.q[0].copy(), ref.qd[0].copy(); rows = []
    for name, ff in (("pd_ff", True), ("pd_only", False)):
        ctrl = PDReplay(ref, model, f_hz=a.f_hz, ff=ff); t0 = time.time()
        res = sim.run(q0, qd0, ctrl, a.steps * ref.ts, ref=ref)
        mt = metrics(res, ref, model, name); mt["wall_s"] = time.time() - t0; mt["substeps_per_wall_s"] = len(res["t"]) * sim.sub / mt["wall_s"]; rows.append(mt)
        np.savez_compressed(out_dir / f"pd_replay_{name}.npz", **{k: v for k, v in res.items() if isinstance(v, np.ndarray)}, fell=res["fell"], t_fall=-1 if res["t_fall"] is None else res["t_fall"])
        print(json.dumps(mt))
    json.dump(rows, open(out_dir / "pd_replay_summary.json", "w"), indent=1)


if __name__ == "__main__":
    main()
