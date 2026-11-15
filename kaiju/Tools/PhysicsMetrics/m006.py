#!/usr/bin/env python
"""M-006@v1 (research/R-005 section 8) and the T-005 side tables, computed from physics_log@v1 / gait_log@v1 files and truth_scenes.json.

Written for T-005 because T-002's Tools/Metrics covers M-001..M-004 only. Thresholds are copied from R-005 section 8 and are not adjustable here.
Usage: python Tools/PhysicsMetrics/m006.py <evidence dir> [--commit <hash>]     (kaiju conda env; reads .jsonl or .jsonl.gz)
Writes <evidence dir>/metrics/M-006.json, footfall_latency.json, reported.json and prints one line per sub-measure.
"""
import gzip, json, math, os, sys

TOOL = "Tools/PhysicsMetrics/m006.py v1"

def rows(path):
    if not os.path.exists(path) and os.path.exists(path + ".gz"): path += ".gz"
    if not os.path.exists(path): return None
    op = gzip.open if path.endswith(".gz") else open
    with op(path, "rt") as f: return [json.loads(l) for l in f if l.strip()]

def within(v, lo, hi): return v is not None and lo <= v <= hi

class Report:
    def __init__(self): self.items = []
    def add(self, sub, name, value, threshold, ok, source, note=""):
        self.items.append({"sub_measure": sub, "name": name, "value": value, "threshold": threshold, "pass": None if ok is None else bool(ok), "source": source, "note": note})
        v = "%.5g" % value if isinstance(value, (int, float)) and value is not None else str(value)
        print("%-9s %-52s %-14s %-34s %s" % (sub, name[:52], v, threshold, "PASS" if ok else ("n/a" if ok is None else "FAIL")))

def main():
    ev = sys.argv[1]; commit = sys.argv[sys.argv.index("--commit") + 1] if "--commit" in sys.argv else "unknown"
    R = Report()
    truth = json.load(open(os.path.join(ev, "truth_scenes.json")))["scenes"] if os.path.exists(os.path.join(ev, "truth_scenes.json")) else {}
    src = "truth_scenes.json"

    # ---- M-006.1
    u = truth.get("unit_sanity")
    if u:
        R.add("M-006.1", "sum of link masses vs frame total (rel)", u["mass_sum_rel_error"], "<= 0.001", u["mass_sum_rel_error"] <= 1e-3, src)
        R.add("M-006.1", "worst single link mass (rel)", u["link_mass_worst_rel_error"], "<= 0.001", u["link_mass_worst_rel_error"] <= 1e-3, src)
        R.add("M-006.1", "gravity y (m/s2), x = z = 0", u["gravity_y"], "= -9.81 exact", abs(u["gravity_y"] + 9.81) < 1e-6 and u["gravity_x"] == 0 and u["gravity_z"] == 0, src)
        R.add("M-006.1", "fixed dt (s)", u["fixed_dt"], "1/60 (R-005 R2)", abs(u["fixed_dt"] - 1 / 60) < 1e-9, src)
        R.add("M-006.1", "density over human-equivalent volume (kg/m3)", u["density_sec4_kg_m3"], "[1000, 2500]", within(u["density_sec4_kg_m3"], 1000, 2500), src)
        R.add("M-006.1", "hull density from collider boxes (kg/m3)", u["collider_box_density_kg_m3"], "[500, 2500], warn < 1025", within(u["collider_box_density_kg_m3"], 500, 2500), src, "boxes ignore fill factors; frame hull density %.0f" % u["hull_density_config_kg_m3"])
        R.add("M-006.1", "largest damping / joint friction read back", u["max_damping_readback"], "= 0", u["max_damping_readback"] == 0, src)

    # ---- M-006.2
    for key in ("drop", "drop_x2km"):
        d = truth.get(key)
        if d:
            R.add("M-006.2", key + ": energy loss / energy released", d["energy_loss_rel_of_released"], "<= dt/t + 0.5 %% = %.4f" % d["threshold_loss"], d["energy_loss_rel_of_released"] <= d["threshold_loss"], src)
            R.add("M-006.2", key + ": worst energy gain / released", d["energy_gain_worst_rel"], "<= 0.001", d["energy_gain_worst_rel"] <= 1e-3, src)
    p = truth.get("pendulum")
    if p:
        R.add("M-006.2", "knee pendulum: energy drift over 10 periods", abs(p["energy_drift_rel_of_swing"]), "<= 0.03 (A)", abs(p["energy_drift_rel_of_swing"]) <= 0.03, src, "period measured %.3f s, small-angle %.3f s" % (p["period_measured_s"], p["period_small_angle_s"]))

    # ---- M-006.3
    for key in ("impact_e0_headon", "impact_e0_offset5", "impact_e05_offset5"):
        d = truth.get(key)
        if not d or not d.get("contact_found"): continue
        R.add("M-006.3", key + ": |dp| / |p0|", d["dp_rel"], "<= 0.005", d["dp_rel"] <= 0.005, src)
        R.add("M-006.3", key + ": |dL| / |L0|", d["dL_rel"], "<= 0.02 (A)", d["dL_rel"] <= 0.02, src)
        R.add("M-006.3", key + ": KE after / KE before", d["ke_ratio"], "<= 1.001", d["ke_ratio"] <= 1.001, src)
        if key == "impact_e0_headon": R.add("M-006.3", "head-on e = 0: KE ratio", d["ke_ratio"], "0.909 +/- 3 %", abs(d["ke_ratio"] / d["ke_ratio_expected_e0_headon"] - 1) <= 0.03, src)

    # ---- M-006.4 ground pressure
    def gait(name): return rows(os.path.join(ev, "gait", name + ".gait.jsonl"))
    def phys(name): return rows(os.path.join(ev, "physics", name + ".physics.jsonl"))
    g = gait("stand")
    if g:
        h = g[0]; mg = h["mass_kg"] * h["g_mps2"]; area = h["feet"][0]["length_m"] * h["feet"][0]["width_m"]
        st = [r for r in g if r["rec"] == "step" and 8.0 <= r["t"] < 10.0]
        total = sum(sum(f["fn"] for f in r["feet"]) for r in st) / len(st)
        R.add("M-006.4", "double stance: sum Fn / (m g), t = 8..10 s", total / mg, "1 +/- 0.01", abs(total / mg - 1) <= 0.01, "gait/stand")
        for i, fid in enumerate(h["foot_ids"]):
            kpa = sum(r["feet"][i]["fn"] for r in st) / len(st) / area / 1000
            R.add("M-006.4", "double stance: foot %s pressure (kPa)" % fid, kpa, "327 +/- 5 %", abs(kpa / 327.0 - 1) <= 0.05, "gait/stand")
        allst = [r for r in g if r["rec"] == "step" and r["t"] >= 10.0]
        p_abs = phys("stand")
        if p_abs:
            ps = [r["power_total_abs"] for r in p_abs if "power_total_abs" in r and r["t"] >= 10.0]
            R.add("M-006.6", "standing still: mean sum |P| (MW), t >= 10 s", sum(ps) / len(ps) / 1e6, "<= 5 (A)", sum(ps) / len(ps) / 1e6 <= 5, "physics/stand")
    g = gait("single_stance")
    if g:
        h = g[0]; mg = h["mass_kg"] * h["g_mps2"]; area = h["feet"][0]["length_m"] * h["feet"][0]["width_m"]
        ss = [r for r in g if r["rec"] == "step" and min(f["fn"] for f in r["feet"]) == 0 and max(f["fn"] for f in r["feet"]) > 0.5 * mg]
        if ss:
            t0, t1 = ss[0]["t"], ss[-1]["t"]; hold = [r for r in ss if r["t"] >= t1 - 2.0]
            kpa = sum(max(f["fn"] for f in r["feet"]) for r in hold) / len(hold) / area / 1000
            R.add("M-006.4", "single stance: pressure (kPa), last 2 s of a %.1f s hold" % (t1 - t0), kpa, "654 +/- 2 %", abs(kpa / 654.0 - 1) <= 0.02 and t1 - t0 >= 5.0, "gait/single_stance")
    for name in ("walk_fr020",):
        g = gait(name)
        if not g: continue
        h = g[0]; mg = h["mass_kg"] * h["g_mps2"]; dt = h["fixed_dt_s"]
        steps = [r for r in g if r["rec"] == "step"]
        begins = [r for r in g if r["rec"] == "event" and r["type"] == "contact_begin" and r["foot"] == h["foot_ids"][0] and r["t"] > 25]
        strides = []
        last = None
        for b in begins:
            if last is not None and b["t"] - last["t"] > 2.0: strides.append((last["n"], b["n"]))
            if last is None or b["t"] - last["t"] > 2.0: last = b
        errs = []
        for a, b in strides:
            imp = sum(sum(f["fn"] for f in steps[n]["feet"]) for n in range(a, b)) * dt
            errs.append(imp / (mg * (b - a) * dt) - 1)
        if errs: R.add("M-006.4", name + ": worst |stride impulse / (m g T) - 1| over %d strides" % len(errs), max(abs(e) for e in errs), "<= 0.02", max(abs(e) for e in errs) <= 0.02, "gait/" + name)
        late = steps  # the whole capture
        peak = max(max(f["fn"] for f in r["feet"]) for r in late) / mg
        R.add("M-006.4", name + ": peak single-foot Fn (body weights), whole capture", peak, "[1.0, 2.0]", within(peak, 1.0, 2.0), "gait/" + name, "raw per-step contact force, no smoothing")

    # ---- M-006.5
    d = truth.get("topple_locked")
    if d and d.get("impact_found"):
        R.add("M-006.5", "locked topple: KE at impact / dPE to impact", d["ke_over_dpe_impact"], "1 +/- 0.03", abs(d["ke_over_dpe_impact"] - 1) <= 0.03, src)
        R.add("M-006.5", "locked topple: angular speed at impact (rad/s)", d["omega_impact_rps"], "0.57 +/- 5 %", abs(d["omega_impact_rps"] / 0.57 - 1) <= 0.05, src)
        R.add("M-006.5", "locked topple: time to first non-foot contact (s)", d["t_fall_s"], "8.2 +/- 10 %", abs(d["t_fall_s"] / 8.2 - 1) <= 0.10, src)
        R.add("M-006.5", "locked topple: energy released to rest (GJ)", d["released_to_rest_J"] / 1e9, "3.6 +/- 10 % (inside 3.6..4.4 per T-005 spec table)", abs(d["released_to_rest_J"] / 3.6e9 - 1) <= 0.10, src, "m g h standing = %.2f GJ" % (d["mgh_standing_J"] / 1e9))
    d = truth.get("topple_limp")
    if d and d.get("impact_found"):
        ratio = (d["ke_impact_J"] + d["dissipated_to_impact_J"]) / d["dpe_to_impact_J"]
        R.add("M-006.5", "limp topple: (KE + drive dissipation) / dPE at impact", ratio, "1 +/- 0.05 (A)", abs(ratio - 1) <= 0.05, src, "drive damping ratio %.2f of critical, logged" % d["limp_damping_ratio"])

    # ---- M-006.6 torque and power, M-006.7 explosions
    leg = ("hip", "knee", "ankle")
    for name in ("walk_fr025", "walk_fr020"):
        p = phys(name)
        if not p: continue
        hdr = p[0]; limits = {k: v["forceLimit"] for k, v in hdr["engine"]["drives"].items()}
        st = [r for r in p if "joints" in r]  # the whole capture: no window (DEF-T005-physics-2)
        worst_ratio, sat_frac, ankle_peak = 0, 0, 0
        counts = {}
        for r in st:
            for j, dofs in r["joints"].items():
                for dn, v in dofs.items():
                    key = j + "." + dn; ratio = abs(v[3]) / limits[key]
                    worst_ratio = max(worst_ratio, ratio); counts[key] = counts.get(key, 0) + (1 if v[5] else 0)
                    if j.startswith("ankle"): ankle_peak = max(ankle_peak, abs(v[3]))
        sat_frac = max(counts.values()) / len(st); worst_sat = max(counts, key=counts.get)
        pos = [r["power_total_pos"] for r in st]; walk = [r["power_total_pos"] for r in st if r["t"] >= 3.0]
        R.add("M-006.6", name + ": worst |tau| / forceLimit", worst_ratio, "<= 1", worst_ratio <= 1.0 + 1e-6, "physics/" + name)
        R.add("M-006.6", name + ": most saturated joint, share of steps", sat_frac, "<= 0.05", sat_frac <= 0.05, "physics/" + name, worst_sat)
        R.add("M-006.6", name + ": ankle peak torque (N m)", ankle_peak, "<= 1.0e9", ankle_peak <= 1.0e9 * (1 + 1e-6), "physics/" + name)
        R.add("M-006.6", name + ": mean sum positive P (MW), walking part (t >= 3 s)", sum(walk) / len(walk) / 1e6, "[30, 120]", within(sum(walk) / len(walk) / 1e6, 30, 120), "physics/" + name, "measured at the speed actually reached, not at Fr 0.25")
        R.add("M-006.6", name + ": peak sum positive P (MW), whole capture", max(pos) / 1e6, "<= 450", max(pos) / 1e6 <= 450, "physics/" + name)

    # "outside falls": every record before a capture's fall_start event; the passive topple scenes are falls from their first step
    passive_falls = ("topple_locked", "topple_limp")
    worst = {"link": (0, ""), "leg": (0, ""), "arm": (0, ""), "nan": 0, "jump": (0, "")}
    pdir = os.path.join(ev, "physics"); scanned = []
    for fn in sorted(os.listdir(pdir)) if os.path.isdir(pdir) else []:
        name = fn.split(".physics")[0]
        p = rows(os.path.join(pdir, fn))
        if not p: continue
        fall = name in passive_falls; scanned.append(name); prev = None
        for r in p:
            if r.get("event") == "fall_start": fall = True
            if "e_total" not in r: continue
            if r.get("nan"): worst["nan"] += 1
            if not fall:
                if r["max_link_speed"] > worst["link"][0]: worst["link"] = (r["max_link_speed"], name)
                for j, dofs in r.get("joints", {}).items():
                    m = max(abs(v[1]) for v in dofs.values()); k = "leg" if j.startswith(leg) else "arm"
                    if m > worst[k][0]: worst[k] = (m, name + ":" + j)
                if prev is not None and r["step"] > 1 and r.get("non_foot_contacts", 0) == 0:  # step 1 holds a scene's initial condition
                    jump = r["e_total"] - prev - r.get("external_work", 0)
                    if jump > worst["jump"][0]: worst["jump"] = (jump, name + " t=%.2f" % r["t"])
            prev = r["e_total"]
    if scanned:
        R.add("M-006.7", "NaN records in %d physics logs" % len(scanned), worst["nan"], "= 0", worst["nan"] == 0, "physics/*")
        R.add("M-006.7", "max link speed outside falls (m/s)", worst["link"][0], "<= 60", worst["link"][0] <= 60, "physics/*", worst["link"][1])
        R.add("M-006.7", "max leg joint speed outside falls (rad/s)", worst["leg"][0], "<= 3", worst["leg"][0] <= 3.0 + 1e-3, "physics/*", worst["leg"][1])
        R.add("M-006.7", "max arm / waist joint speed outside falls (rad/s)", worst["arm"][0], "<= 10", worst["arm"][0] <= 10.0, "physics/*", worst["arm"][1])
        R.add("M-006.7", "largest one-step energy gain without external work (J)", worst["jump"][0], "<= 7.8e7 (0.01 m g H)", worst["jump"][0] <= 7.8e7, "physics/*", worst["jump"][1] + "; actuator work is not external work, so walking steps count here")

    # ---- M-006.8 repeatability
    runs = [gait("repeat60_run%d" % k) for k in (1, 2, 3)]
    if all(runs):
        finals = [[r for r in g if r["rec"] == "step"][-1] for g in runs]
        div = max(math.dist(finals[0]["com_p"], f["com_p"]) for f in finals[1:])
        seqs = [[(r["n"], r["type"], r.get("foot")) for r in g if r["rec"] == "event" and r["type"] in ("contact_begin", "contact_end")] for g in runs]
        R.add("M-006.8", "3 runs of the 60 s sequence: CoM divergence at 60 s (m)", div, "<= 0.01", div <= 0.01, "gait/repeat60_run1..3")
        R.add("M-006.8", "identical footfall step indices and order (%d events)" % len(seqs[0]), 1.0 if seqs[0] == seqs[1] == seqs[2] else 0.0, "= 1", seqs[0] == seqs[1] == seqs[2], "gait/repeat60_run1..3")

    verdict = {}
    for it in R.items:
        if it["pass"] is None: continue
        verdict[it["sub_measure"]] = verdict.get(it["sub_measure"], True) and it["pass"]
    missing = [s for s in ("M-006.%d" % i for i in range(1, 9)) if s not in verdict]
    out = {"metric": "M-006@v1", "tool": TOOL, "commit": commit, "verdict": "PASS" if verdict and all(verdict.values()) and not missing else "FAIL", "sub_measure_verdicts": verdict, "not_measured": missing, "items": R.items}
    os.makedirs(os.path.join(ev, "metrics"), exist_ok=True)
    json.dump(out, open(os.path.join(ev, "metrics", "M-006.json"), "w"), indent=1)
    print("M-006@v1 verdict:", out["verdict"], {k: ("pass" if v else "FAIL") for k, v in sorted(verdict.items())}, "not measured:", missing)

    # ---- footfall event latency (C17) and reported items
    table = {}; tot = same = 0
    gdir = os.path.join(ev, "gait")
    for fn in sorted(os.listdir(gdir)) if os.path.isdir(gdir) else []:
        g = rows(os.path.join(gdir, fn)); name = fn.split(".gait")[0]
        if not g: continue
        h = g[0]; ids = h["foot_ids"]; steps = {r["n"]: r for r in g if r["rec"] == "step"}
        begins = [r for r in g if r["rec"] == "event" and r["type"] == "contact_begin"]; cams = [r for r in g if r["rec"] == "event" and r["type"] == "cam_impulse"]
        # reference: first step whose contact flag is 1 after a 0 (the log), per foot
        ref = []
        for i, fid in enumerate(ids):
            prev = 0
            for n in sorted(steps):
                c = steps[n]["feet"][i]["contact"]
                if c and not prev: ref.append((n, fid))
                prev = c
        ev_set = sorted((b["n"], b["foot"]) for b in begins)
        lat0 = len(set(ref) & set(ev_set)); cam_ok = sum(1 for c in cams if c["source_n"] == c["n"])
        table[name] = {"contacts_in_log": len(ref), "contact_begin_events": len(begins), "events_in_the_contact_step": lat0, "cam_impulses": len(cams), "cam_impulses_same_step": cam_ok, "missed": len(set(ref) - set(ev_set)), "duplicates_or_extra": len(ev_set) - len(set(ev_set)) + len(set(ev_set) - set(ref))}
        tot += len(ref); same += lat0
    json.dump({"tool": TOOL, "commit": commit, "definition": "L_step = step of the FootContact event minus first step whose logged contact flag is 1 (R-001 section 6); threshold L_step = 0 in >= 99 %, <= 1 in 100 %", "total_contacts": tot, "share_same_step": (same / tot if tot else None), "captures": table}, open(os.path.join(ev, "metrics", "footfall_latency.json"), "w"), indent=1)
    print("footfall latency: %d contacts, same-step share %s" % (tot, ("%.4f" % (same / tot)) if tot else "n/a"))

    rep = {"tool": TOOL, "commit": commit}
    g = gait("start")
    if g:
        steps = [r for r in g if r["rec"] == "step"]; t_cmd = next(r["t"] for r in steps if r["intent"]["move"] == "walk")
        t_first = next((r["t"] for r in steps if r["t"] > t_cmd and math.hypot(r["com_v"][0], r["com_v"][2]) > 0.05), None)
        t_lift = next((r["t"] for r in g if r["rec"] == "event" and r["type"] == "contact_end" and r["t"] > t_cmd), None)
        vend = sum(r["com_v"][2] for r in steps[-600:]) / 600
        t_90 = next((r["t"] for r in steps if r["t"] > t_cmd and r["com_v"][2] >= 0.9 * vend), None)
        rep["R5_latency_s"] = {"stick_to_first_com_motion_0.05mps": None if t_first is None else t_first - t_cmd, "stick_to_first_foot_lift": None if t_lift is None else t_lift - t_cmd, "stick_to_90pct_of_steady_speed": None if t_90 is None else t_90 - t_cmd, "steady_speed_mps": vend, "status": "proposed metric, unregistered: cannot fail"}
    g = gait("stop")
    if g:
        steps = [r for r in g if r["rec"] == "step"]; t_cmd = next(r["t"] for r in steps if r["intent"]["move"] == "stop")
        after = [r for r in steps if r["t"] >= t_cmd]
        t_slow = next((r["t"] for i, r in enumerate(after) if all(math.hypot(q["com_v"][0], q["com_v"][2]) < 0.3 for q in after[i:i + 120])), None)
        t_stand = next((r["t"] for r in after if r["ctl"]["phase"] == "Stand"), None)
        rep.setdefault("R5_latency_s", {})["release_to_below_0.3mps_for_2s"] = None if t_slow is None else t_slow - t_cmd
        rep["R5_latency_s"]["release_to_stand_phase"] = None if t_stand is None else t_stand - t_cmd
    g = gait("walk_scn")
    if g:
        steps = [r for r in g if r["rec"] == "step"]; last = steps[-1]; tgt = last["intent"]["target"]
        rep["C13_walk_to_target"] = {"target": tgt, "final_com_xz": [last["com_p"][0], last["com_p"][2]], "distance_m": math.hypot(last["com_p"][0] - tgt[0], last["com_p"][2] - tgt[2]), "final_phase": last["ctl"]["phase"], "limit": "one step length"}
    causes = {}
    g = gait("intent_over_limit")
    if g:
        for r in g:
            if r["rec"] == "step":
                for c in r["intent"]["limit_causes"].split(", "): causes[c] = causes.get(c, 0) + 1
        rep["C14_limit_causes_step_counts"] = causes
    ext = {}
    for fn in sorted(os.listdir(gdir)) if os.path.isdir(gdir) else []:
        g = rows(os.path.join(gdir, fn))
        if g: ext[fn.split(".gait")[0]] = max(r["ext_f"] for r in g if r["rec"] == "step")
    rep["C10_max_external_force_N_per_capture"] = ext
    json.dump(rep, open(os.path.join(ev, "metrics", "reported.json"), "w"), indent=1)

if __name__ == "__main__": main()
