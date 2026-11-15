"""Criterion A run (R-009 s.7): Fr 0.10, 20 steps, initial state = ref node 0 with speed x1.02 and trunk +1 deg.
Run from the project root: /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/qp_proto/run_A.py [--tag pre] [--steps 20] [--set key=val ...]"""
import sys, pathlib, json, time, argparse
import numpy as np
HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE)); sys.path.insert(0, str(HERE.parent))
import planar_model as P, planar_sim as S
from controller import QPWalker, DEFAULT


def metrics_A(res, ctl, ref, model, mu=0.4, k0=0):
    L = {k: np.array(v) for k, v in ctl.log.items() if k not in ("td_events",)}; n = len(L["t"]); l = 42.4
    out = {"fell": bool(res["fell"]), "t_end_s": float(res["t_end"]), "steps_done": float(res["t_end"] / ref.ts), "ticks": n}
    q = res["q"]; t = res["t"]
    out["mean_speed"] = float((q[-1, 0] - q[0, 0]) / max(t[-1] - t[0], 1e-9)); out["mean_speed_ratio"] = out["mean_speed"] / ref.v
    rs = [ref.at(x) for x in L["tref"]]
    czr = np.array([r["com"][1] for r in rs]); out["com_z_rms_m"] = float(np.sqrt(np.mean((L["com"][:, 1] - czr) ** 2))); out["com_z_rms_pct_l"] = 100 * out["com_z_rms_m"] / l
    thr = np.array([r["q"][2] for r in rs]); out["pelvis_pitch_rms_deg"] = float(np.rad2deg(np.sqrt(np.mean((L["q"][:, 2] - thr) ** 2))))
    td = ctl.td_events; xs = [P.PlanarModel.points(model, ref.q[k0])[2 if k0 == 0 else 0, 0]] + [e[1] for e in td]    # heel x at touchdowns (start: B heel at node 0)
    sl = np.diff(xs); out["touchdowns"] = len(td)
    out["foot_placement_rms_m"] = float(np.sqrt(np.mean((sl - ref.d) ** 2))) if len(sl) else float("nan"); out["foot_placement_rms_pct_step"] = 100 * out["foot_placement_rms_m"] / ref.d if len(sl) else float("nan")
    out["sat_ticks_pct"] = float(100 * res["sat"].any(1).mean())
    Fn, Ft = res["Fn"], res["Ft"]; ok = Fn > 1e-3 * model.W
    out["fr_plant_vs_mu04_max"] = float((np.abs(Ft)[ok] / (0.4 * Fn[ok])).max()) if ok.any() else 0.0
    out["fr_plant_vs_mu08_max"] = float((np.abs(Ft)[ok] / (0.8 * Fn[ok])).max()) if ok.any() else 0.0
    out["fr_cmd_max"] = float(L["fr_cmd"].max())
    out["solver_ok_pct"] = float(100 * np.mean((L["status1"] == "ok") & (L["status2"] == "ok"))); out["torque_rows_dropped_pct"] = float(100 * L["tdrop"].mean())
    out["ms_per_tick_p50"] = float(np.percentile(L["ms"], 50)); out["ms_per_tick_p99"] = float(np.percentile(L["ms"], 99)); out["ctrl_resid_max_over_mgl"] = float(L["resid"].max())
    # solver_ok: status1 = the full QP with all hard rows (9.3b-4: a fallback solve without torque rows never counts as success)
    passes = {"no_fall": not out["fell"], "speed_5pct": abs(out["mean_speed_ratio"] - 1) <= 0.05, "com_z_1pct": out["com_z_rms_pct_l"] <= 1.0, "pitch_3deg": out["pelvis_pitch_rms_deg"] <= 3.0,
              "foot_5pct": out["foot_placement_rms_pct_step"] <= 5.0, "sat_1pct": out["sat_ticks_pct"] <= 1.0, "friction_le_1": out["fr_plant_vs_mu04_max"] <= 1.0, "solver_100": out["solver_ok_pct"] >= 100.0}
    out["pass"] = passes; out["PASS_ALL"] = all(passes.values())
    return out


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--tag", default="pre"); ap.add_argument("--steps", type=float, default=20); ap.add_argument("--set", nargs="*", default=[])
    a = ap.parse_args(); kw = {}
    for s in a.set:
        k, v = s.split("="); kw[k] = (v.lower() == "true") if v.lower() in ("true", "false") else float(v)
    out_dir = P.ROOT / "Tools/GaitOpt/out/proto"; out_dir.mkdir(parents=True, exist_ok=True)
    model = P.PlanarModel(); ref = P.load_reference(None, model); sim = S.PlantSim(model)
    q0 = ref.q[0].copy(); qd0 = ref.qd[0].copy(); qd0[0] *= 1.02; q0[2] += np.deg2rad(1.0)
    ctl = QPWalker(ref, model, **kw); t0 = time.time()
    res = sim.run(q0, qd0, ctl, a.steps * ref.ts, ref=ref); wall = time.time() - t0
    mt = metrics_A(res, ctl, ref, model); mt["wall_s"] = wall; mt["tag"] = a.tag; mt["params"] = {k: (float(v) if not isinstance(v, (bool, type(None))) else v) for k, v in ctl.p.items()}
    L = ctl.log; np.savez_compressed(out_dir / f"A_{a.tag}.npz", t=np.array(L["t"]), phase=np.array(L["tref"]), parity=np.array(L["parity"]), q=np.array(L["q"]), com=np.array(L["com"]), L=np.array(L["L"]), F=np.array(L["F"]), u=np.array(L["u"]),
                                     sat=res["sat"], fr_cmd=np.array(L["fr_cmd"]), fr_plant=np.abs(res["Ft"]) / np.maximum(0.4 * res["Fn"], 1e-9), status1=np.array(L["status1"]), status2=np.array(L["status2"]), tdrop=np.array(L["tdrop"]), ms=np.array(L["ms"]),
                                     td_events=np.array(ctl.td_events), fell=res["fell"], t_fall=-1 if res["t_fall"] is None else res["t_fall"], q_plant=res["q"])
    json.dump(mt, open(out_dir / f"A_{a.tag}.json", "w"), indent=1)
    print(json.dumps(mt, indent=1))


if __name__ == "__main__":
    main()
