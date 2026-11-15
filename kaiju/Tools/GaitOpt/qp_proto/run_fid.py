"""T-005 r4 9.3b-2 runs: criterion A (and B pushes) with the contact-aware full-ID QP (controller_fid.FIDWalker).
Run from the project root: /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/qp_proto/run_fid.py --tag A_fid [--steps 20] [--push +1|-1] [--set key=val ...]
A: initial state = ref node 0, hip speed x1.02, trunk +1 deg (as run_A.py). B: same start, external horizontal force on the hip of impulse m*dv (dv = 0.15 v)
over T_push = 0.1 s at mid-stance (phase tau = ts/2) of step push_step; pass = no fall and per-step speed within 5 % of v within 4 steps after the push."""
import sys, pathlib, json, time, argparse
import numpy as np
HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE)); sys.path.insert(0, str(HERE.parent))
import planar_model as P, planar_sim as S
from controller_fid import FIDWalker
from run_A import metrics_A


class PushSim(S.PlantSim):
    """PlantSim + an external horizontal hip force Fx(t) (generalised force on q[0]); same integrator and contact law (copied loop)."""
    def __init__(self, model, push_fn=None, **kw):
        super().__init__(model, **kw); self.push_fn = push_fn

    def run(self, q0, qd0, controller, t_end, fall_fn=None, ref=None):
        m = self.m; q = np.array(q0, float); qd = np.array(qd0, float); dt = self.dt; sub = self.sub; self.reset_contact()
        if fall_fn is None and ref is not None: fall_fn = S.fall_detect(ref)
        u0 = np.asarray(controller(0.0, q, qd, -1), float); queue = [u0.copy() for _ in range(self.delay)]
        n_ticks = int(round(t_end * self.hz)); L = {k: [] for k in ("t", "q", "qd", "u_cmd", "u_app", "Fn", "Ft", "sat", "fx")}; fell = False; t_fall = None
        for k in range(n_ticks):
            t = k / self.hz
            if k > 0 and hasattr(controller, "F_meas"): controller.F_meas = Fm.copy()          # read-back of the last tick's mean contact force (no effect on physics)
            uc = np.asarray(controller(t, q, qd, k), float); queue.append(uc)
            u_app = np.clip(queue.pop(0), -self.lim, self.lim) if self.delay > 0 else np.clip(uc, -self.lim, self.lim)
            fx = self.push_fn(t) if self.push_fn else 0.0
            L["t"].append(t); L["q"].append(q.copy()); L["qd"].append(qd.copy()); L["u_cmd"].append(uc); L["u_app"].append(u_app); L["fx"].append(fx)
            Fsum = np.zeros((4, 2))
            for s in range(sub):
                M, h, pts, Jp, _, _ = m.Mh(q, qd); Minv = np.linalg.inv(M); F = self.contact(M, pts, Jp, qd, Minv)
                Q = self.CJT @ u_app + np.einsum("pdi,pd->i", Jp, F) - h; Q[0] += fx
                qd = qd + dt * (Minv @ Q); q = q + dt * qd; Fsum += F
                if not (np.all(np.isfinite(q)) and np.all(np.isfinite(qd))): raise FloatingPointError(f"plant blew up at t={t + s * dt:.3f}")
            Fm = Fsum / sub; L["Fn"].append(Fm[:, 1]); L["Ft"].append(Fm[:, 0]); L["sat"].append(np.abs(u_app) >= self.lim - 1e-9)
            if fall_fn is not None and fall_fn((k + 1) / self.hz, q): fell = True; t_fall = (k + 1) / self.hz; break
        out = {k: np.array(v) for k, v in L.items()}; out["fell"] = fell; out["t_fall"] = t_fall; out["t_end"] = len(L["t"]) / self.hz; out["q_end"] = q; out["qd_end"] = qd
        return out


def extra_metrics(res, ctl, ref, model):
    Lg = ctl.log; o = {}
    o["qp_limit_row_active_ticks_pct"] = float(100 * np.mean(Lg["u_act"]))
    it = np.array(Lg["iters"]); o["gi_iters_p50"] = float(np.percentile(it, 50)); o["gi_iters_p99"] = float(np.percentile(it, 99)); o["gi_iters_max"] = int(it.max())   # deterministic cost proxy (wall ms depends on machine load)
    Fn, Ft = res["Fn"], res["Ft"]
    for thr in (1e-3, 1e-2, 5e-2):
        ok = Fn > thr * model.W; o[f"fr_plant_vs_mu04_max_Fn>{thr:g}W"] = float((np.abs(Ft)[ok] / (0.4 * Fn[ok])).max()) if ok.any() else 0.0
        o[f"fr_plant_vs_mu04_ticks_gt1_pct_Fn>{thr:g}W"] = float(100 * np.mean(((np.abs(Ft) > 0.4 * Fn) & ok).any(1)))
    td = [e[0] for e in ctl.td_events]; q = res["q"]; t = res["t"]
    cx = np.array([model.com(x)[0] for x in q]); sp = []
    for a, b in zip(td[:-1], td[1:]):
        ia, ib = int(round(a * 60)), int(round(b * 60))
        if ib < len(cx): sp.append((cx[ib] - cx[ia]) / (t[ib] - t[ia]) / ref.v)
    o["step_speed_ratio"] = [round(float(x), 4) for x in sp]; o["step_times_s"] = [round(float(b - a), 3) for a, b in zip(td[:-1], td[1:])]
    return o, sp


PRESETS = {"b3": dict(kvs=5.0),                                                   # 9.3b-3 final (A_v2_final)
           "b4": dict(kvs=5.0, onset=1.0, land=1.0, land_act=1.0, land_k0=36.0,   # 9.3b-4 final: onset fix, landing quintic from the measured state at node 36,
                      fr_meas=1.0, fr_margin=0.9),                                  #   per-foot shear <= 0.9 mu x measured load, QP cone 0.9 mu
           "b5x": dict(kvs=5.0, onset=1.0, land=1.0, land_act=1.0, land_k0=35.0, fr_meas=1.0, fr_margin=0.9,   # 9.3b-5 variant (reference v3): landing window from node 35, x/pitch lead 0.15 s, late descent 8 m/s
                       land_xlead=0.15, late_vz=8.0, mass_est=0.05, sg_floor=0.3),            #   (steady speed 0.953: B(-) fails, Clim08aw falls)
           "b5": dict(kvs=5.0, onset=1.0, land=1.0, land_act=1.0, land_k0=35.0, fr_meas=1.0, fr_margin=0.9,   # 9.3b-5 (reference v3): b4 with the landing window from node 35 (no x lead),
                      mass_est=0.05, sg_floor=0.3),                                          #   online mass-scale estimate, support guard (stance fmax floor until the swing foot is in contact)
           "b6": dict(kvs=5.0, onset=1.0, land=1.0, land_act=1.0, land_k0=35.0, fr_meas=1.0, fr_margin=0.6,   # 9.3b-6: b5 + per-point shear authority (unloading points shed shear 0.5 s before the normal load,
                      mass_est=0.05, sg_floor=0.3, sh_unl=0.5, sh_land=0.4, land_xlead=0.1, late_vz=8.0),      #   landed points start without shear 0.4 s), heel x/pitch reach the landing state 0.1 s before z (vertical strike, 8 m/s late descent), cone 0.6 mu
           "b6b": dict(kvs=5.0, onset=1.0, land=1.0, land_act=1.0, land_k0=35.0, fr_meas=1.0, fr_margin=0.9, mass_est=0.05, sg_floor=0.3,   # 9.3b-6 mild: b5 + shear authority + x lead 0.03 s, late descent 8 m/s (cone 0.9 mu as b5)
                       sh_unl=0.5, sh_land=0.4, land_xlead=0.03, late_vz=8.0),
           "b6c": dict(kvs=5.0, onset=1.0, land=1.0, land_act=1.0, land_k0=35.0, fr_meas=1.0, fr_margin=0.9, mass_est=0.05, sg_floor=0.3,   # 9.3b-6 mildest: b5 + shear authority only
                       sh_unl=0.5, sh_land=0.4)}



def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--tag", default="A_fid"); ap.add_argument("--steps", type=float, default=20); ap.add_argument("--set", nargs="*", default=[])
    ap.add_argument("--push", type=float, default=0.0); ap.add_argument("--push_step", type=int, default=4); ap.add_argument("--dv_frac", type=float, default=0.15)
    ap.add_argument("--delay", type=int, default=1); ap.add_argument("--quiet", action="store_true")
    ap.add_argument("--ref", default=None, help="reference json (default planar_model's, out/stride_fr010_lat.json)")
    ap.add_argument("--friction", default="tanh", choices=["tanh", "stick"], help="plant friction law (planar_sim.PlantSim); 9.3b-2 runs = tanh")
    ap.add_argument("--preset", default=None, choices=sorted(PRESETS), help="named parameter set (applied before --set)")
    ap.add_argument("--mirror", action="store_true", help="start in the mirrored step: ref node N state, leg A new stance (symmetry test)")
    ap.add_argument("--mu_plant", type=float, default=0.8); ap.add_argument("--lim_scale", type=float, default=1.0)
    ap.add_argument("--model_mass", type=float, default=1.0, help="controller model mass and inertia scale (plant unchanged): criterion C")
    a = ap.parse_args(); kw = dict(PRESETS[a.preset]) if a.preset else {}
    for s in a.set:
        k, v = s.split("="); kw[k] = (v.lower() == "true") if v.lower() in ("true", "false") else float(v)
    out_dir = P.ROOT / "Tools/GaitOpt/out/proto"; out_dir.mkdir(parents=True, exist_ok=True)
    model = P.PlanarModel(); ref = P.load_reference(a.ref, model)
    k0 = ref.N if a.mirror else 0
    if a.mirror: kw["start_par"] = 1.0
    q0 = ref.q[k0].copy(); qd0 = ref.qd[k0].copy(); qd0[0] *= 1.02; q0[2] += np.deg2rad(1.0)
    cmodel = model
    if a.model_mass != 1.0:
        cmodel = P.PlanarModel(); cmodel.mass = model.mass * a.model_mass; cmodel.inertia = model.inertia * a.model_mass
        cmodel.mtot = float(cmodel.mass.sum()); cmodel.W = cmodel.mtot * P.G0; cmodel.MR = np.repeat(cmodel.mass, 2)
    ctl = FIDWalker(ref, cmodel, **kw); Tp = 0.1; Fx = a.push * model.mtot * a.dv_frac * ref.v / Tp; pinfo = {}
    def push_fn(t):
        if a.push == 0 or len(ctl.td_events) != a.push_step: return 0.0
        tau = t - ctl.t_anchor
        if 0.5 * ref.ts <= tau < 0.5 * ref.ts + Tp - 1e-9:
            pinfo.setdefault("t_push", t); return Fx
        return 0.0
    sim = PushSim(model, push_fn=push_fn, delay_ticks=a.delay, friction=a.friction, mu=a.mu_plant, lim_scale=a.lim_scale); t0 = time.time()
    res = sim.run(q0, qd0, ctl, a.steps * ref.ts, ref=ref); wall = time.time() - t0
    mt = metrics_A(res, ctl, ref, model, k0=k0); ex, sp = extra_metrics(res, ctl, ref, model); mt.update(ex); mt["wall_s"] = wall; mt["tag"] = a.tag; mt["ref"] = a.ref or "Tools/GaitOpt/out/stride_fr010_lat.json"; mt["friction"] = a.friction; mt["plant_slip_substeps_pct"] = float(100 * sim.n_slip / max(1, sim.n_slip + sim.n_stick)) if a.friction == "stick" else None; mt["params"] = {k: v for k, v in ctl.p.items()}; mt["run"] = dict(preset=a.preset, mirror=a.mirror, mu_plant=a.mu_plant, lim_scale=a.lim_scale, model_mass=a.model_mass, delay=a.delay)
    if a.push:
        mt["push"] = dict(sign=a.push, dv=a.dv_frac * ref.v, impulse_Ns=float(Fx * Tp), step=a.push_step, **pinfo)
        # step index (0 = step ending at touchdown 1 ... ) of the step containing the push = push_step; recovery: some step k in push_step+1 .. push_step+4 and all after within 5 %
        k0 = a.push_step; rec = None
        for kk in range(k0, min(len(sp), k0 + 4)):
            if all(abs(x - 1) <= 0.05 for x in sp[kk:]) and kk < len(sp): rec = kk - k0 + 1; break
        mt["push"]["recovered_within_steps"] = rec; mt["push"]["PASS_B"] = (not res["fell"]) and rec is not None and rec <= 4
    L = ctl.log
    np.savez_compressed(out_dir / f"{a.tag}.npz", t=np.array(L["t"]), phase=np.array(L["tref"]), parity=np.array(L["parity"]), q=np.array(L["q"]), com=np.array(L["com"]), F=np.array(L["F"]), u=np.array(L["u"]),
                        fn_meas=np.array(L["fn_meas"]), fmax=np.array(L["fmax"]), mest=np.array(L["mest"]), dbg=np.array(L["dbg"]), loaded=np.array(L["loaded"]), u_act=np.array(L["u_act"]), sat=res["sat"], Fn_plant=res["Fn"], Ft_plant=res["Ft"],
                        status=np.array(L["status2"]), status1=np.array(L["status1"]), iters=np.array(L["iters"]), ms=np.array(L["ms"]), td_events=np.array(ctl.td_events), fell=res["fell"], q_plant=res["q"], fx=res["fx"])
    json.dump(mt, open(out_dir / f"{a.tag}.json", "w"), indent=1, default=float)
    keys = ["fell", "steps_done", "mean_speed_ratio", "com_z_rms_pct_l", "pelvis_pitch_rms_deg", "touchdowns", "foot_placement_rms_pct_step", "sat_ticks_pct", "qp_limit_row_active_ticks_pct",
            "fr_plant_vs_mu04_max", "fr_plant_vs_mu04_max_Fn>0.01W", "fr_plant_vs_mu04_ticks_gt1_pct_Fn>0.001W", "solver_ok_pct", "torque_rows_dropped_pct", "ms_per_tick_p50", "ms_per_tick_p99", "PASS_ALL"]
    print(json.dumps({k: (round(mt[k], 4) if isinstance(mt[k], float) else mt[k]) for k in keys}))
    if not a.quiet: print("pass", mt["pass"], "step speeds", mt["step_speed_ratio"], "push", mt.get("push"))


if __name__ == "__main__":
    main()
