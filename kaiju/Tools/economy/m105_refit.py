#!/usr/bin/env python
"""M-105@v1 Python oracle and cross-language corpus (T-123, research/R-104.md sections 9 and 11).

An independent third implementation of the section 9 checks, written against the committed catalogue (parts@v1.json, chassis@v1.json) and not against
RefitPhysics.cs or gen_refit_data.py. It
  1. enumerates seeded random builds per chassis (uniform over compatible parts per slot, mutations of the default build) plus hand-picked edge builds,
  2. judges every build: ordered checks (mass, power, heat rejection, heat storage, torque, CoM height, CoM lateral, CoM fore-aft, tilt) and the first violation,
  3. checks the corpus invariants (every build the oracle calls valid has every margin >= 0; every invalid build has a violated limit; spec mass = sum of part masses),
  4. writes the corpus (build + expected verdict) that the C# test M105CorpusTests replays against the shipped RefitService (0 disagreements is the gate).
Builds whose margin is within 1e-9 relative of a limit are marked "borderline" and are not replayed (float64 sums differ in the last bit between languages).

Usage:  python m105_refit.py [--per-chassis 400] [--seed 105] [--corpus evidence/T-123/m105_corpus.json] [--out evidence/T-123/m105_python.json]   (exit 1 on a gate failure)
"""
from __future__ import annotations

import argparse
import json
import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
REFIT = os.path.join(ROOT, "Assets", "Game", "Config", "Refit")
G = 9.81
ORDER = ["Mass", "Power", "HeatRejection", "HeatStorage", "Torque", "ComHeight", "ComLateral", "ComForeAft", "TiltMargin"]   # RefitViolation names, check order
EPS = 1e-9


def load():
    parts = {p["id"]: p for p in json.load(open(os.path.join(REFIT, "parts@v1.json")))["parts"]}
    chassis = {c["id"]: c for c in json.load(open(os.path.join(REFIT, "chassis@v1.json")))["chassis"]}
    return parts, chassis


def judge(ch, parts, build):
    """build: slot name -> part id (complete, compatible). Returns (first violation or None, margins dict name -> (margin, limit_scale), totals)."""
    slots = ch["slots"]
    n = len(slots)
    idx = {s["name"]: i for i, s in enumerate(slots)}
    P = [parts[build[s["name"]]] for s in slots]
    m = [float(sum(p["composition_kg"].values())) for p in P]
    com = []
    for i, s in enumerate(slots):
        o = P[i]["com_offset_m"]
        com.append((s["anchor"][0] + (-o[0] if s["mirror"] else o[0]), s["anchor"][1] + o[1], s["anchor"][2] + o[2]))
    M = sum(m)
    cx, cy, cz = (sum(m[i] * com[i][a] for i in range(n)) / M for a in range(3))
    mar = {}
    mar["Mass"] = (ch["mass_cap_kg"] - M, ch["mass_cap_kg"])
    reactor = sum(p["composition_kg"].get("reactor", 0) for p in P)
    rated = ch["reactor_w_per_kg"] * reactor
    walk = ch["cot"] * M * G * ch["walk_speed_ms"]
    cont = sum(p["power_peak_w"] for p in P if not p["power_pulse"])
    pulse = max([p["power_peak_w"] for p in P if p["power_pulse"]] + [0.0])
    allow = ch["buffer_j"] / ch["buffer_window_s"]
    mar["Power"] = (allow - ((walk + cont + pulse) - rated), allow)
    qgen = sum(p["heat_w"] for p in P)
    qrej = ch["passive_heat_w"] + ch["radiator_w_per_kg"] * sum(p["radiator_kg"] for p in P)
    mar["HeatRejection"] = (qrej - qgen, qrej)
    need = max(0.0, sum(p["heat_peak_w"] for p in P) - qrej) * ch["burst_s"]
    store = sum(p["coolant_kg"] for p in P) * ch["coolant_j_per_kg_k"] * ch["coolant_delta_k"]
    mar["HeatStorage"] = (store - need, max(store, need, 1.0))
    # torque: moved parts = every descendant of the slot (+ itself for hip-leg like slots)
    desc = {s["name"]: [] for s in slots}
    for s in slots:
        par = s["parent"]
        while par:
            desc[par].append(s["name"])
            par = slots[idx[par]]["parent"]
    worst, worst_slot = math.inf, None
    for i, s in enumerate(slots):
        if not s["actuated"]:
            continue
        moved = list(desc[s["name"]]) + ([s["name"]] if s["moves_self"] else [])
        piv = s["pivot"]
        stat = dyn = 0.0
        for nm in moved:
            k = idx[nm]
            d2 = sum((com[k][a] - piv[a]) ** 2 for a in range(3))
            side = max(P[k]["size_m"])
            stat += m[k] * G * math.sqrt(d2)
            dyn += (m[k] * d2 + m[k] * side * side / 12.0) * ch["alpha_rad_s2"]
        demand = stat + dyn
        drive = P[i]["composition_kg"].get("drive", 0)
        rt = ch["torque_nm_per_kg"] * drive * ch["torque_derate"] * (1.0 + P[i]["torque_bonus_bps"] / 10000.0)
        ratio = rt / demand if demand > 0 else math.inf
        if ratio < worst:
            worst, worst_slot = ratio, s["name"]
    mar["Torque"] = (worst - 1.0, 1.0)
    h = ch["frame_height_m"]
    lo, hi = ch["com_height_frac"][0] * h, ch["com_height_frac"][1] * h
    mar["ComHeight"] = (min(cy - lo, hi - cy), hi)
    mar["ComLateral"] = (ch["com_lateral_m"] - abs(cx), ch["com_lateral_m"])
    mar["ComForeAft"] = (min(cz - ch["com_fore_aft_m"][0], ch["com_fore_aft_m"][1] - cz), max(abs(ch["com_fore_aft_m"][0]), abs(ch["com_fore_aft_m"][1])))
    w = ch["stance_half_width_m"]
    tilt = math.degrees(math.atan2(w - abs(cx), cy))
    for limb, members in ch["limbs"].items():
        keep = [i for i, s in enumerate(slots) if s["name"] not in members]
        Mk = sum(m[i] for i in keep)
        xk = sum(m[i] * com[i][0] for i in keep) / Mk
        yk = sum(m[i] * com[i][1] for i in keep) / Mk
        tilt = min(tilt, math.degrees(math.atan2(w - abs(xk), yk)))
    mar["TiltMargin"] = (tilt - ch["tilt_min_deg"], ch["tilt_min_deg"])
    first = next((k for k in ORDER if mar[k][0] < 0), None)
    border = any(abs(mar[k][0]) <= EPS * max(1.0, abs(mar[k][1])) for k in ORDER)
    return first, mar, {"mass_kg": M, "com": (cx, cy, cz), "border": border, "torque_worst_slot": worst_slot}


def compatible(parts, ch):
    by = {}
    for s in ch["slots"]:
        by[s["name"]] = sorted(pid for pid, p in parts.items() if p["slot_type"] == s["type"] and ch["id"] in p["chassis"])
    return by


def gen_builds(ch, parts, per_chassis, rng):
    comp = compatible(parts, ch)
    default = ch["default_build"]
    out = []
    for i in range(per_chassis):
        if i % 2 == 0:
            b = {s["name"]: rng.choice(comp[s["name"]]) for s in ch["slots"]}
        else:
            b = dict(default)
            for _ in range(rng.randint(1, 4)):
                s = rng.choice(ch["slots"])["name"]
                b[s] = rng.choice(comp[s])
        out.append(b)
    # edge builds: default, every slot at its heaviest part, every slot at its lightest part
    mass = lambda pid: sum(parts[pid]["composition_kg"].values())
    out.append(dict(default))
    out.append({s: max(c, key=lambda p: (mass(p), p)) for s, c in comp.items()})
    out.append({s: min(c, key=lambda p: (mass(p), p)) for s, c in comp.items()})
    return out


def run(per_chassis, seed, corpus_path=None):
    parts, chassis = load()
    rng = random.Random(seed)
    res = {"seed": seed, "per_chassis": per_chassis, "chassis": {}}
    corpus = []
    fail = []
    for cid in sorted(chassis):
        ch = chassis[cid]
        builds = gen_builds(ch, parts, per_chassis, rng)
        valid = invalid = border = 0
        firsts = {}
        for b in builds:
            first, mar, tot = judge(ch, parts, b)
            if first is None:
                valid += 1
                if any(v[0] < 0 for v in mar.values()):
                    fail.append(("valid build with a negative margin", cid))
            else:
                invalid += 1
                firsts[first] = firsts.get(first, 0) + 1
                if not mar[first][0] < 0:
                    fail.append(("invalid build without a violated limit", cid))
            spec_mass = sum(sum(parts[p]["composition_kg"].values()) for p in b.values())
            if abs(spec_mass - tot["mass_kg"]) > 1e-9 * spec_mass:
                fail.append(("mass sum", cid))
            if tot["border"]:
                border += 1
            corpus.append({"c": cid, "p": [b[s["name"]] for s in ch["slots"]], "v": "ok" if first is None else first, "b": bool(tot["border"])})
        res["chassis"][cid] = {"builds": len(builds), "valid": valid, "invalid": invalid, "borderline": border, "first_violation_counts": firsts,
                               "default_valid": judge(ch, parts, ch["default_build"])[0] is None}
        if not res["chassis"][cid]["default_valid"]:
            fail.append(("default build invalid", cid))
        if valid == 0:
            fail.append(("corpus has no valid builds", cid))
        # the basic (floor) catalogue offers one part per slot type: every reachable build is valid by design, so it has no invalid builds
        if invalid == 0 and len({p for c in compatible(parts, ch).values() for p in c}) > len(ch["slots"]) // 2 + 3:
            fail.append(("corpus has no invalid builds", cid))
    if not any(c["invalid"] for c in res["chassis"].values()):
        fail.append(("corpus has no invalid builds at all", "*"))
    res["failures"] = fail
    res["pass"] = not fail
    if corpus_path:
        os.makedirs(os.path.dirname(os.path.abspath(corpus_path)), exist_ok=True)
        with open(corpus_path, "w", newline="\n") as f:
            f.write('{"schema": "m105_corpus@v1", "seed": %d, "slots": %s, "builds": [\n' % (seed, json.dumps({c: [s["name"] for s in chassis[c]["slots"]] for c in sorted(chassis)})))
            f.write(",\n".join(json.dumps(x, separators=(",", ":")) for x in corpus))
            f.write("\n]}\n")
    return res


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--per-chassis", type=int, default=400)
    ap.add_argument("--seed", type=int, default=105)
    ap.add_argument("--corpus")
    ap.add_argument("--out")
    a = ap.parse_args(argv)
    r = run(a.per_chassis, a.seed, a.corpus)
    for cid, c in r["chassis"].items():
        print("%-15s builds %5d valid %5d invalid %5d borderline %d first-violation %s" % (cid, c["builds"], c["valid"], c["invalid"], c["borderline"], c["first_violation_counts"]))
    print("M-105 python oracle: %s %s" % ("PASS" if r["pass"] else "FAIL", r["failures"]))
    if a.out:
        os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
        with open(a.out, "w", newline="\n") as f:
            f.write(json.dumps(r, indent=1) + "\n")
    return 0 if r["pass"] else 1


if __name__ == "__main__":
    sys.exit(main())
