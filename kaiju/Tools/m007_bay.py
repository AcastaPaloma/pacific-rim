"""M-007@v2 measurement tool (research/R-103.md section 5): reads `brain_log@v2` JSONL traces and computes every
M-007@v2 clause (C1-C6, P1-P6, I1-I4, F1) with R-103's thresholds. Self-contained (numpy, scipy only).

Usage:  python m007_bay.py <log.jsonl | dir> ... [--ratings ratings.json] [--candidate scriptedpack@1]
                           [--policy policy@1] [--out report.json]

brain_log@v2 rows (one JSON object per line, header first; additive over v1, every v1 field keeps its meaning):
  header       schema "brain_log@v2", run_id, seed, step_seconds, brain_period_steps, brain_id, scenario
               (m007_bay_hunt|churn|raid|mixed|emerge), composition ("LLH"), config_hash, [call_range_m=5000]
  decision     step, t, ent, pack (0 = solo), species L/M/H, role Anchor/Flanker/Bait/Raider/Free, goal
               hunt/raid/regroup/free, alive, x, y, heading_rad, speed, hp, [hp_max], state 0..5, prev_state,
               reason 0..14 (11 voluntary retarget), target, tgt_visible, tgt_mem_age_s, board_age_s,
               board_source (own|heard|called|relayed|map are fair; anything else is a provenance leak), claimed_by
  target_sample step, t, ent, tkind "mech", alive, x, y, heading_rad, hp
  infra        step, t, ent, id, class crane|power|bridge|airport, hp, hp_max, x, y
  hit          step, t, attacker, victim, damage, victim_hp_after
  death        step, t, ent
  call         step, t, sender, delivered_to[], delay_s, lost, dist_m, range_m
  role_change  step, t, member, from, to, reason (death|retreat|goal_change are forced, others voluntary)
  goal_change  step, t, pack, goal, target (ent or null)
  mech_target  step, t, mech, target (kaiju ent the mech aims at)
Statuses: PASS FAIL NOT_RUN PENDING NON_DISCRIMINATING PENDING_CONTROL."""
from __future__ import annotations

import argparse
import glob
import json
import math
import os
import sys
from collections import defaultdict

import numpy as np

SCHEMA = "brain_log@v2"
STATE_CHARGE, STATE_RETREAT, STATE_WITHDRAWN = 3, 4, 5
REASON_VOLUNTARY = 11
ROLES = ("Anchor", "Flanker", "Bait", "Raider", "Free")
FAIR_SOURCES = ("own", "heard", "called", "relayed", "map")
FORCED_REASONS = ("death", "retreat", "goal_change")
FIT = {  # R-103 4.2 species fit f(species, role)
    "L": {"Anchor": 0.1, "Flanker": 0.9, "Bait": 1.0, "Raider": 0.3, "Free": 0.0},
    "M": {"Anchor": 0.5, "Flanker": 0.8, "Bait": 0.4, "Raider": 0.7, "Free": 0.0},
    "H": {"Anchor": 1.0, "Flanker": 0.2, "Bait": 0.0, "Raider": 1.0, "Free": 0.0},
}
CLASS_WEIGHT = {"crane": 1.0, "power": 2.0, "bridge": 3.0, "airport": 3.0}

CFG = {
    "candidate": "scriptedpack@1", "policy": "policy@1",
    "directcharge": "directcharge@1", "nofocus": "nofocus@1", "solo": "solo@1", "noroles": "noroles@1",
    "nobait": "nobait@1", "nearestinfra": "nearestinfra@1", "randomrole": "randomrole@1",
    "omniscient": "omniscient@1",
    "d_eng": {"L": 300.0, "M": 500.0, "H": 800.0},
    "flank_spread_deg": 90.0, "commit_fresh_s": 8.0, "thrash_window_s": 12.0, "memory_s": 3.0,
    "role_thrash_window_s": 30.0, "bait_min_aim_s": 3.0, "bait_window_s": 30.0, "bait_strike_deg": 90.0,
    "engagement_gap_s": 10.0, "idle_progress_mps": 0.5, "idle_window_s": 60.0, "ab_radius_m": 1000.0,
    "id_radius_m": 5000.0, "sound_mps": 343.0, "pull_deg": 45.0, "pull_window_s": 120.0,
    "bootstrap_resamples": 2000, "bootstrap_seed": 7, "ci_level": 0.95,
    "min_runs_hint": 1,
    "T": {  # thresholds (R-103 5.2); v1 values unchanged, none lowered
        "C1.fr_commit": 0.60, "C1.ratio_null": 2.0, "C2.fe": 0.50,
        "C3.min_switches": 200, "C3.thrash": 0.10, "C3.rate": 3.0, "C4.leaks": 0,
        "C5.smart": 5.0, "C5.cheap": 3.0, "C5.raters": 12, "C5.p": 0.05,
        "C6.win": 0.55, "C6.wilson_lb": 0.50, "C6.seeds": 200,
        "P1.cov": 0.90, "P1.fit": 0.6, "P1.fit_time": 0.7,
        "P2.rate": 1.0, "P2.thrash": 0.10, "P2.min_changes": 100,
        "P3.share": 0.90, "P3.lat_s": 10.0, "P3.min_events": 30,
        "P4.rate": 0.40, "P4.ratio_null": 2.0, "P5.ratio": 1.25, "P5.lb": 1.0, "P6.share": 0.90,
        "I1.rate": 0.80, "I1.destroyed": 0.80, "I2.ab": 0.10, "I2.id": 0.20,
        "I3.ratio": 1.20, "I3.lb": 1.0, "I4.info": 0.30, "F1.share": 0.90, "F1.valid_s": 600.0, "F1.max_pack": 5,
    },
}


class InputError(Exception):
    def __init__(self, errors):
        super().__init__("; ".join(errors[:5]) + (" ..." if len(errors) > 5 else ""))
        self.errors = errors


# ---------------------------------------------------------------- loading
REQ = {
    "header": ("seed", "step_seconds", "brain_period_steps", "brain_id", "scenario", "composition", "run_id"),
    "decision": ("step", "t", "ent", "pack", "species", "role", "goal", "alive", "x", "y", "heading_rad", "speed", "hp",
                 "state", "prev_state", "reason", "target", "tgt_visible", "tgt_mem_age_s", "board_age_s", "board_source"),
    "target_sample": ("step", "t", "ent", "alive", "x", "y", "heading_rad", "hp"),
    "infra": ("step", "t", "ent", "id", "class", "hp", "hp_max", "x", "y"),
    "hit": ("step", "t", "attacker", "victim", "damage"),
    "death": ("step", "t", "ent"),
    "call": ("step", "t", "sender", "delivered_to", "delay_s", "lost", "dist_m", "range_m"),
    "role_change": ("step", "t", "member", "from", "to", "reason"),
    "goal_change": ("step", "t", "pack", "goal", "target"),
    "mech_target": ("step", "t", "mech", "target"),
}


class Run:
    def __init__(self, path, header):
        self.path, self.h = path, header
        self.dec, self.smp, self.infra, self.hits, self.deaths = [], [], [], [], []
        self.calls, self.rolech, self.goalch, self.aim = [], [], [], []

    brain = property(lambda s: s.h["brain_id"])
    scenario = property(lambda s: s.h["scenario"])
    comp = property(lambda s: s.h["composition"])
    seed = property(lambda s: s.h["seed"])
    period = property(lambda s: s.h["brain_period_steps"] * s.h["step_seconds"])


def eid(s):
    return int(s.split(":", 1)[1]) if isinstance(s, str) and s.startswith("ent:") else 0


def load_run(path):
    errs, run, last = [], None, None
    with open(path, encoding="utf-8") as f:
        for ln, line in enumerate(f, 1):
            if not line.strip():
                continue
            try:
                row = json.loads(line)
            except ValueError as e:
                errs.append(f"{path}:{ln} invalid JSON ({e})")
                continue
            k = row.get("kind") if isinstance(row, dict) else None
            if k not in REQ and k != "header":
                errs.append(f"{path}:{ln} unknown kind {k!r}")
                continue
            if run is None:
                if k != "header" or row.get("schema") != SCHEMA:
                    errs.append(f"{path}:{ln} first row must be a {SCHEMA} header")
                    break
                miss = [x for x in REQ["header"] if x not in row]
                if miss:
                    errs.append(f"{path}:{ln} header missing {miss}")
                    break
                run = Run(path, row)
                continue
            miss = [x for x in REQ[k] if x not in row]
            if miss:
                errs.append(f"{path}:{ln} {k} missing {miss}")
                continue
            if "step" in row:
                if last is not None and row["step"] < last:
                    errs.append(f"{path}:{ln} step decreases")
                last = row["step"]
            if k == "infra" and row["class"] not in CLASS_WEIGHT:
                errs.append(f"{path}:{ln} unknown infra class {row['class']!r}")
            if k == "decision" and (row["species"] not in FIT or row["role"] not in ROLES):
                errs.append(f"{path}:{ln} bad species/role")
            lst = {"decision": run.dec, "target_sample": run.smp, "infra": run.infra, "hit": run.hits,
                   "death": run.deaths, "call": run.calls, "role_change": run.rolech, "goal_change": run.goalch,
                   "mech_target": run.aim}.get(k)
            if lst is not None:
                lst.append(row)
    if run is None:
        errs.append(f"{path}: no header")
    elif not run.dec:
        errs.append(f"{path}: no decision rows")
    if errs:
        raise InputError(errs)
    return run


def expand(paths):
    out = []
    for p in paths:
        if os.path.isdir(p):
            out += glob.glob(os.path.join(p, "**", "*.jsonl"), recursive=True)
        else:
            out.append(p)
    return sorted(out)


# ---------------------------------------------------------------- primitives
def bearing(dx, dy):
    b = math.degrees(math.atan2(dy, dx))
    return b + 360.0 if b < 0 else b


def spread_deg(bs):
    b = sorted(bs)
    mg = max(((b[i + 1] if i + 1 < len(b) else b[0] + 360.0) - b[i]) for i in range(len(b)))
    return 360.0 - mg


def angdiff(a, b):
    d = abs(a - b) % 360.0
    return 360.0 - d if d > 180 else d


_FF = {}


def ff_chance(n, m):
    """E[max bin count]/n for n kaiju choosing uniformly among m targets."""
    if (n, m) in _FF:
        return _FF[(n, m)]
    if n < 1 or m < 1:
        v = 1.0
    elif m ** n > 2_000_000:
        v = math.nan
    else:
        tot = 0
        for c in range(m ** n):
            bins, x = [0] * m, c
            for _ in range(n):
                bins[x % m] += 1
                x //= m
            tot += max(bins)
        v = tot / (m ** n) / n
    _FF[(n, m)] = v
    return v


def required_slots(n, goal):
    if goal == "raid":
        return [{"Raider"}] if n < 3 else [{"Raider"}, {"Anchor"}]
    if goal != "hunt" or n < 2:
        return []
    if n == 2:
        return [{"Anchor", "Flanker"}, {"Flanker", "Bait"}]
    if n == 3:
        return [{"Anchor"}, {"Flanker"}, {"Flanker", "Bait"}]
    return [{"Anchor"}, {"Flanker"}, {"Bait"}]


def roles_covered(roles, goal):
    """True when every required slot can be given to a distinct member holding an allowed role."""
    slots = required_slots(len(roles), goal)

    def go(i, avail):
        if i == len(slots):
            return True
        for j, r in enumerate(avail):
            if r in slots[i] and go(i + 1, avail[:j] + avail[j + 1:]):
                return True
        return False
    return go(0, list(roles))


def wilson(k, n, level=0.95):
    if n <= 0:
        return (math.nan, math.nan)
    z = 1.959964 if level == 0.95 else 2.5758
    p = k / n
    d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d
    h = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return (c - h, c + h)


# ---------------------------------------------------------------- per-run computation
def compute_run(run, cfg=CFG):
    P = run.period
    d_eng = cfg["d_eng"]
    out = defaultdict(float)
    out.update(brain=run.brain, scenario=run.scenario, comp=run.comp, seed=run.seed)
    ticks = defaultdict(lambda: {"dec": [], "smp": [], "inf": []})
    for r in run.dec:
        ticks[r["step"]]["dec"].append(r)
    for r in run.smp:
        ticks[r["step"]]["smp"].append(r)
    for r in run.infra:
        ticks[r["step"]]["inf"].append(r)
    species_of = {eid(r["ent"]): r["species"] for r in run.dec}
    kaiju = set(species_of)
    mech_ids = {eid(r["ent"]) for r in run.smp}
    tpos = {}
    last_charge, kprev, last_switch = {}, {}, {}
    pack_series = defaultdict(list)            # pack -> [(t, [(ent, role, species, state)], goal)]
    member_tick = []                           # (t, pack, ent, engaged, eligible, pack_engaged)
    fit_t = defaultdict(lambda: [0, 0])        # ent -> [engaged ticks, fit ticks]
    sp_role = defaultdict(lambda: defaultdict(float))
    sp_eng = defaultdict(float)
    dist_hist = defaultdict(list)              # ent -> [(t, target, dist)]
    hit_t = defaultdict(list)
    for h in run.hits:
        hit_t[eid(h["attacker"])].append(h["t"])
    pack_goal, first_seen, packof = {}, {}, {}
    gc = sorted(run.goalch, key=lambda r: r["t"])
    gci = 0
    mech_head = defaultdict(list)
    ab_num = ab_den = id_num = id_den = 0
    emerge_pack_hist = []
    over5 = bad_merge = 0
    for step in sorted(ticks):
        tk = ticks[step]
        t = step * run.h["step_seconds"]
        while gci < len(gc) and gc[gci]["t"] <= t:
            pack_goal[gc[gci]["pack"]] = (gc[gci]["goal"], gc[gci]["target"])
            gci += 1
        for r in tk["smp"]:
            tpos[eid(r["ent"])] = {"x": r["x"], "y": r["y"], "alive": r["alive"] == 1, "mech": True}
            mech_head[eid(r["ent"])].append((t, r["x"], r["y"], r["heading_rad"]))
        for r in tk["inf"]:
            tpos[eid(r["ent"])] = {"x": r["x"], "y": r["y"], "alive": r["hp"] > 0, "mech": False}
        rows = tk["dec"]
        if not rows:
            continue
        live_t = sum(1 for k, q in tpos.items() if q["mech"] and q["alive"] and k in {eid(s["ent"]) for s in tk["smp"]})
        groups, per_target, with_target = defaultdict(list), defaultdict(int), 0
        engaged_of = {}
        for r in rows:
            e, tg = eid(r["ent"]), eid(r["target"])
            first_seen.setdefault(e, t)
            # C4 leaks: v1 information fairness + pack-channel provenance
            if tg and r["tgt_visible"] == 0 and r["tgt_mem_age_s"] > cfg["memory_s"]:
                out["leaks_v1"] += 1
            if r["board_source"] not in FAIR_SOURCES or r["board_age_s"] < 0:
                out["leaks_prov"] += 1
            q = tpos.get(tg)
            eng = False
            if r["alive"] == 1 and tg and q is not None:
                dx, dy = r["x"] - q["x"], r["y"] - q["y"]
                dist = math.hypot(dx, dy)
                eng = dist <= d_eng[r["species"]]
                dist_hist[e].append((t, tg, dist))
                if eng:
                    out["engaged_decisions"] += 1
                    groups[tg].append(bearing(dx, dy))
            engaged_of[e] = eng
            if r["alive"] == 1:
                out["alive_decisions"] += 1
                if tg:
                    with_target += 1
                    per_target[tg] += 1
            # voluntary switch / thrash
            prev = kprev.get(e)
            if prev is not None and eid(prev["target"]) and tg and eid(prev["target"]) != tg and r["reason"] == REASON_VOLUNTARY:
                out["voluntary"] += 1
                ls = last_switch.get(e)
                if ls and ls[0] == tg and t - ls[1] <= cfg["thrash_window_s"]:
                    out["thrash"] += 1
                last_switch[e] = (eid(prev["target"]), t)
            # pack id change (F1 merge check)
            pp = packof.get(e)
            if pp is not None and r["pack"] and r["pack"] != pp and r["alive"] == 1:
                mates = [m for m in rows if eid(m["ent"]) != e and m["pack"] == r["pack"] and m["alive"] == 1]
                if mates and min(math.hypot(m["x"] - r["x"], m["y"] - r["y"]) for m in mates) > run.h.get("call_range_m", 5000.0):
                    bad_merge += 1
            packof[e] = r["pack"]
            kprev[e] = r
            # species table
            if r["alive"] == 1:
                sp_role[r["species"]][r["role"]] += 1
                if eng:
                    sp_eng[r["species"]] += 1
        # C1 flank
        multi, flanked = set(), set()
        for tg, b in groups.items():
            if len(b) >= 2:
                multi.add(tg)
                if spread_deg(b) >= cfg["flank_spread_deg"]:
                    flanked.add(tg)
        for r in rows:
            tg = eid(r["target"])
            if r["alive"] != 1 or not tg or r["state"] != STATE_CHARGE:
                continue
            if r["prev_state"] != STATE_CHARGE and (tg not in last_charge or t - last_charge[tg] > cfg["commit_fresh_s"]):
                out["commits"] += 1
                if tg in multi:
                    out["commits_multi"] += 1
                    out["commits_flanked"] += tg in flanked
            last_charge[tg] = t
        # C2 focus
        if with_target >= 2 and live_t >= 2:
            out["ff_sum"] += max(per_target.values()) / with_target
            out["chance_sum"] += ff_chance(with_target, live_t)
            out["ff_ticks"] += 1
        # per pack
        bypack = defaultdict(list)
        for r in rows:
            if r["pack"]:
                bypack[r["pack"]].append(r)
        mechs_alive = [(k, q) for k, q in tpos.items() if q["mech"] and q["alive"]]
        for p, mem in bypack.items():
            live = [m for m in mem if m["alive"] == 1 and m["state"] not in (STATE_RETREAT, STATE_WITHDRAWN)]
            pack_series[p].append((t, [(eid(m["ent"]), m["role"], m["species"], m["state"]) for m in live], None))
            goals = [m["goal"] for m in live]
            majority = max(set(goals), key=goals.count) if goals else "free"
            pg = pack_goal.get(p, (majority, None))[0]
            pack_series[p][-1] = (t, pack_series[p][-1][1], pg)
            any_eng = any(engaged_of.get(eid(m["ent"])) or m["state"] == STATE_CHARGE for m in live)
            if pg in ("hunt", "raid") and any_eng:
                out["p1_ticks"] += 1
                roles = [m["role"] for m in live]
                n = len(roles)
                if (pg == "hunt" and n >= 2) or pg == "raid":
                    out["p1_den"] += 1
                    out["p1_cov"] += roles_covered(roles, pg)
                for m in live:
                    e = eid(m["ent"])
                    if engaged_of.get(e):
                        fit_t[e][0] += 1
                        fit_t[e][1] += FIT[m["species"]][m["role"]] >= cfg["T"]["P1.fit"]
            for m in mem:
                e = eid(m["ent"])
                elig = m["alive"] == 1 and m["state"] not in (STATE_RETREAT, STATE_WITHDRAWN)
                member_tick.append((t, p, e, bool(engaged_of.get(e)) and elig, elig, any_eng))
            # I2 abandonment / idle
            near = []
            for m in live:
                ds = [math.hypot(m["x"] - q["x"], m["y"] - q["y"]) for _, q in mechs_alive]
                near.append(min(ds) if ds else math.inf)
            if live and mechs_alive:
                if min(near) <= cfg["ab_radius_m"]:
                    ab_den += 1
                    transit = 0
                    for m, dmin in zip(live, near):
                        if m["goal"] == "raid" and m["speed"] > 0.2:
                            k2, q2 = min(mechs_alive, key=lambda kq: math.hypot(m["x"] - kq[1]["x"], m["y"] - kq[1]["y"]))
                            vx, vy = math.cos(m["heading_rad"]), math.sin(m["heading_rad"])
                            if vx * (q2["x"] - m["x"]) + vy * (q2["y"] - m["y"]) < 0:
                                transit += 1
                    if pg == "hunt" and transit / len(live) >= 0.5:
                        ab_num += 1
            if live and pg in ("hunt", "raid") and (not mechs_alive or min(near) > cfg["id_radius_m"]):
                prog, known = False, True
                for m in live:
                    h = dist_hist[eid(m["ent"])]
                    old = [x for x in h if x[0] <= t - cfg["idle_window_s"] + 1e-9 and x[1] == h[-1][1]]
                    if not old:
                        known = False
                        break
                    if (old[-1][2] - h[-1][2]) / max(t - old[-1][0], 1e-9) >= cfg["idle_progress_mps"]:
                        prog = True
                fight = any(m["state"] == STATE_CHARGE or any(t - P < ht <= t for ht in hit_t[eid(m["ent"])]) for m in live)
                if known:
                    id_den += 1
                    id_num += (not prog and not fight)
            # F1 pack size
            alive_n = sum(1 for m in mem if m["alive"] == 1)
            if alive_n > cfg["T"]["F1.max_pack"]:
                over5 = 1
    out.update(ab_num=ab_num, ab_den=ab_den, id_num=id_num, id_den=id_den, f1_over5=over5, f1_bad_merge=bad_merge)
    # C4 delivery-time violations
    for c in run.calls:
        if not c["lost"] and c["delivered_to"] and (c["dist_m"] > c["range_m"] or c["delay_s"] < c["dist_m"] / cfg["sound_mps"] * 0.999):
            out["leaks_delivery"] += 1
    out["leaks"] = out["leaks_v1"] + out["leaks_prov"] + out["leaks_delivery"]
    out["kaiju_minutes"] = out["engaged_decisions"] * P / 60.0
    out["member_minutes"] = out["alive_decisions"] * P / 60.0
    out["kaiju_hours"] = out["alive_decisions"] * P / 3600.0
    # P1 member fit by (species, comp)
    mf = defaultdict(lambda: [0.0, 0.0])
    for e, (n_e, n_f) in fit_t.items():
        if n_e:
            mf[species_of[e]][0] += n_f / n_e
            mf[species_of[e]][1] += 1
    out["fit_by_species"] = {s: tuple(v) for s, v in mf.items()}
    # P2 role changes
    rc = sorted(run.rolech, key=lambda r: r["t"])
    last_rc = {}
    for r in rc:
        if r["reason"] in FORCED_REASONS:
            out["role_forced"] += 1
            continue
        out["role_changes"] += 1
        ls = last_rc.get(r["member"])
        if ls and ls[0] == r["to"] and r["t"] - ls[1] <= cfg["role_thrash_window_s"]:
            out["role_thrash"] += 1
        last_rc[r["member"]] = (r["from"], r["t"])
    # P3 re-allocation latency
    losses = [(d["t"], eid(d["ent"])) for d in run.deaths if eid(d["ent"]) in kaiju]
    seen_ret = set()
    for r in run.dec:
        e = eid(r["ent"])
        if r["state"] == STATE_RETREAT and r["prev_state"] != STATE_RETREAT and e not in seen_ret:
            seen_ret.add(e)
            losses.append((r["t"], e))
    lat = []
    for tl, e in losses:
        p = next((r["pack"] for r in reversed(run.dec) if eid(r["ent"]) == e and r["t"] <= tl + 1e-9), 0)
        ser = [s for s in pack_series.get(p, []) if s[0] >= tl - 1e-9]
        if not p or not ser:
            continue
        surv = [m for m in ser[0][1] if m[0] != e]
        if len(surv) < 2:
            continue
        out["loss_events"] += 1
        L = math.inf
        for ts, mem, goal in ser:
            roles = [m[1] for m in mem if m[0] != e]
            if roles_covered(roles, goal or "hunt"):
                L = ts - tl
                break
        lat.append(L)
        out["loss_ok"] += L <= cfg["T"]["P3.lat_s"]
    out["latencies"] = lat
    # P6 participation
    by_pack = defaultdict(list)
    for rec in member_tick:
        by_pack[rec[1]].append(rec)
    for p, recs in by_pack.items():
        ts = sorted({r[0] for r in recs})
        active = sorted({r[0] for r in recs if r[5]})
        intervals, cur = [], None
        for t in active:
            if cur and t - cur[1] <= cfg["engagement_gap_s"]:
                cur[1] = t
            else:
                cur = [t, t]
                intervals.append(cur)
        for a, b in intervals:
            mem = defaultdict(lambda: [0, 0])
            for t, _, e, eng, elig, _ in recs:
                if a <= t <= b and elig:
                    mem[e][1] += 1
                    mem[e][0] += eng
            for e, (n_e, n_t) in mem.items():
                if n_t:
                    out["p6_pairs"] += 1
                    out["p6_ok"] += n_e / n_t >= 0.5
    # hit / hp accounting (S, KX)
    hp0 = {}
    for r in run.smp:
        hp0.setdefault(eid(r["ent"]), r["hp"])
    kmax = {}
    for r in run.dec:
        kmax.setdefault(eid(r["ent"]), r.get("hp_max", r["hp"]))
    mech_dmg, kai_dmg = defaultdict(float), defaultdict(float)
    sp_dmg = defaultdict(float)
    for h in run.hits:
        v = eid(h["victim"])
        if v in mech_ids:
            mech_dmg[v] += h["damage"]
            if eid(h["attacker"]) in species_of:
                sp_dmg[species_of[eid(h["attacker"])]] += h["damage"]
        elif v in kaiju:
            kai_dmg[v] += h["damage"]
    mech_tot = sum(hp0.values())
    kai_tot = sum(kmax.values())
    out["mech_frac"] = sum(min(d, hp0.get(v, d)) for v, d in mech_dmg.items()) / mech_tot if mech_tot else 0.0
    out["kaiju_loss_frac"] = sum(min(d, kmax.get(v, d)) for v, d in kai_dmg.items()) / kai_tot if kai_tot else 0.0
    # infra
    imax, ilast, icls = {}, {}, {}
    ifirst_t = {}
    for r in run.infra:
        e = eid(r["ent"])
        imax.setdefault(e, r["hp_max"])
        ilast[e] = r["hp"]
        icls[e] = r["class"]
        if r["hp"] <= imax[e] * (1 - cfg["T"]["I1.destroyed"]) + 1e-12:
            ifirst_t.setdefault(e, r["t"])
    frac = {e: min(1.0, max(0.0, 1 - ilast[e] / imax[e])) for e in imax if imax[e] > 0}
    ival = sum(CLASS_WEIGHT[icls[e]] * f for e, f in frac.items())
    imx = sum(CLASS_WEIGHT[icls[e]] for e in frac)
    out["ival"] = ival
    out["ival_max"] = imx
    out["S"] = out["mech_frac"] + 0.5 * (ival / imx if imx else 0.0) - 0.5 * out["kaiju_loss_frac"]
    # I1 raid completion
    committed = {eid(g["target"]) for g in run.goalch if g["goal"] == "raid" and g["target"]}
    has_raider = any(r["role"] == "Raider" for r in run.dec)
    if run.scenario == "m007_bay_raid" and has_raider:
        out["raid_runs"] = 1
        done = [e for e in committed if frac.get(e, 0) >= cfg["T"]["I1.destroyed"]]
        out["raid_done"] = 1 if done else 0
        out["t_first_destruction"] = min([ifirst_t[e] for e in ifirst_t], default=math.nan)
    # P4 bait and strike
    out.update(bs_stats(run, cfg, tpos_hist=None))
    # I4 feint
    raids = [g for g in gc if g["goal"] == "raid" and g["target"] and eid(g["target"]) in tpos]
    seen_p = set()
    for g in raids:
        if g["pack"] in seen_p:
            continue
        seen_p.add(g["pack"])
        out["i4_raids"] += 1
        tr, tg = g["t"], eid(g["target"])
        q = tpos[tg]
        pulled = False
        for m, hist in mech_head.items():
            def off(rec):
                return angdiff(math.degrees(rec[3]) % 360.0, bearing(q["x"] - rec[1], q["y"] - rec[2]))
            before = [x for x in hist if x[0] <= tr]
            after = [x for x in hist if tr < x[0] <= tr + cfg["pull_window_s"]]
            if before and after and off(before[-1]) > cfg["pull_deg"] and any(off(x) <= cfg["pull_deg"] for x in after):
                pulled = True
        if pulled:
            out["i4_pulled"] += 1
            if any(t_ > tr for t_ in out["bs_event_times"]):
                out["i4_pulled_bs"] += 1
    # F1 formation
    if run.scenario == "m007_bay_emerge" and first_seen:
        t_eval = max(first_seen.values()) + cfg["T"]["F1.valid_s"]
        lastrow = {}
        for r in run.dec:
            if r["t"] <= t_eval + 1e-9:
                lastrow[eid(r["ent"])] = r
        alive = [r for r in lastrow.values() if r["alive"] == 1]
        sizes = defaultdict(list)
        for r in alive:
            if r["pack"]:
                sizes[r["pack"]].append(r)
        out["f1_emerged"] = len(alive)
        for r in alive:
            mem = sizes.get(r["pack"], [])
            ok = r["pack"] and 2 <= len(mem) <= cfg["T"]["F1.max_pack"] and any(
                FIT[m["species"]][ro] >= 0.6 for m in mem for ro in ("Anchor", "Flanker", "Raider"))
            out["f1_valid"] += bool(ok)
    # species table
    out["sp_eng"] = dict(sp_eng)
    out["sp_dmg"] = dict(sp_dmg)
    out["sp_role"] = {s: dict(v) for s, v in sp_role.items()}
    out["sp_deaths"] = {s: sum(1 for _, e in losses[:0]) for s in FIT}
    for d in run.deaths:
        if eid(d["ent"]) in species_of:
            out["sp_deaths"][species_of[eid(d["ent"])]] += 1
    return out


def bs_stats(run, cfg, tpos_hist):
    """Bait-and-strike (R-103 5.1). Strict: target must hold Bait. Any: role-agnostic (for roleless control arms)."""
    res = defaultdict(float)
    res["bs_event_times"] = []
    by = defaultdict(list)
    for a in sorted(run.aim, key=lambda r: r["t"]):
        by[(a["mech"], a["target"])].append(a["t"])
    gap = 2 * run.period + 1e-9
    decs = defaultdict(list)
    for r in run.dec:
        decs[eid(r["ent"])].append(r)
    mpos = defaultdict(list)
    for r in run.smp:
        mpos[eid(r["ent"])].append((r["t"], r["x"], r["y"]))

    def at(lst, t, key=None):
        best = None
        for x in lst:
            tt = x[0] if key is None else x["t"]
            if tt <= t + 1e-9:
                best = x
            else:
                break
        return best
    for (mech, tgt), times in by.items():
        eps, cur = [], None
        for t in times:
            if cur and t - cur[1] <= gap:
                cur[1] = t
            else:
                cur = [t, t]
                eps.append(cur)
        b, m_id = eid(tgt), eid(mech)
        for a, z in eps:
            if z - a + run.period < cfg["bait_min_aim_s"] - 1e-9:
                continue
            drow = at(decs[b], a, key="t")
            is_bait = drow is not None and drow["role"] == "Bait"
            event = False
            for r in run.dec:
                e = eid(r["ent"])
                if e == b or r["pack"] != (drow["pack"] if drow else None) or not a <= r["t"] <= a + cfg["bait_window_s"]:
                    continue
                if r["state"] == STATE_CHARGE and r["prev_state"] != STATE_CHARGE and eid(r["target"]) == m_id:
                    mp = at(mpos[m_id], r["t"])
                    bd = at(decs[b], r["t"], key="t")
                    if mp and bd and angdiff(bearing(r["x"] - mp[1], r["y"] - mp[2]),
                                             bearing(bd["x"] - mp[1], bd["y"] - mp[2])) >= cfg["bait_strike_deg"]:
                        event = True
                        t_ev = r["t"]
                        break
            res["bs_opp_any"] += 1
            res["bs_ev_any"] += event
            if is_bait:
                res["bs_opp_strict"] += 1
                res["bs_ev_strict"] += event
                if event:
                    res["bs_event_times"].append(t_ev)
    return res


# ---------------------------------------------------------------- aggregation and statistics
def ssum(runs, key):
    return float(sum(r.get(key, 0.0) for r in runs))


def ratio(runs, num, den):
    d = ssum(runs, den)
    return ssum(runs, num) / d if d else math.nan


def boot_ci(runs, fn, cfg=CFG):
    """Percentile bootstrap over runs; fn(list of run dicts) -> float. Vectorised through index sampling."""
    if not runs:
        return (math.nan, math.nan)
    rng = np.random.default_rng(cfg["bootstrap_seed"])
    n = len(runs)
    idx = rng.integers(0, n, size=(cfg["bootstrap_resamples"], n))
    vals = [fn([runs[i] for i in row]) for row in idx]
    vals = np.array([v for v in vals if not math.isnan(v)])
    if not len(vals):
        return (math.nan, math.nan)
    lo = (1 - cfg["ci_level"]) / 2
    return (float(np.quantile(vals, lo)), float(np.quantile(vals, 1 - lo)))


def by_comp(runs):
    d = defaultdict(list)
    for r in runs:
        d[r["comp"]].append(r)
    return d


def meas(name, value, op, thr, ci=None, unit=""):
    ok = None if value is None or (isinstance(value, float) and math.isnan(value)) else (value >= thr if op == ">=" else value <= thr)
    return {"name": name, "value": None if ok is None else value, "op": op, "threshold": thr, "ok": ok, "unit": unit,
            "ci95": None if ci is None or any(math.isnan(c) for c in ci) else [ci[0], ci[1]]}


def comp_guard(runs, fn_ci, op, thr):
    """R-103 5.2: fail if any composition's whole 95 % CI lies on the wrong side of the threshold."""
    bad, table = [], {}
    for c, rs in sorted(by_comp(runs).items()):
        lo, hi = fn_ci(rs)
        table[c] = {"ci95": None if math.isnan(lo) else [lo, hi], "n_runs": len(rs)}
        if not math.isnan(lo) and ((op == ">=" and hi < thr) or (op == "<=" and lo > thr)):
            bad.append(c)
    return bad, table


def clause(cid, scen, parts, bad_comps=None, table=None, controls=None, note=None, status=None, info=None):
    if status is None:
        oks = [p["ok"] for p in parts]
        if any(o is None for o in oks):
            status = "NOT_RUN"
        elif all(oks) and not bad_comps:
            status = "PASS"
        else:
            status = "FAIL"
        if status == "PASS" and controls:
            if any(c["discriminates"] is False for c in controls):
                status = "NON_DISCRIMINATING"
            elif any(c["discriminates"] is None for c in controls):
                status = "PENDING_CONTROL"
    return {"clause": cid, "scenario": scen, "status": status, "measures": parts, "composition_fail": bad_comps or [],
            "per_composition": table or {}, "controls": controls or [], "note": note, "info": info or {}}


def ctl(name, value, cand, better):
    """Control discriminates when it is strictly worse than the candidate; `better` = the candidate's good direction."""
    if value is None or cand is None or (isinstance(value, float) and math.isnan(value)):
        return {"control": name, "value": None, "candidate": cand, "discriminates": None}
    worse = value < cand if better == "higher" else value > cand
    return {"control": name, "value": value, "candidate": cand, "discriminates": bool(worse)}


def pair_runs(a, b):
    """Pair run dicts of two arms on (scenario, comp, seed)."""
    kb = {(r["scenario"], r["comp"], r["seed"]): r for r in b}
    pa, pb = [], []
    for r in a:
        k = (r["scenario"], r["comp"], r["seed"])
        if k in kb:
            pa.append(r)
            pb.append(kb[k])
    return pa, pb


def paired_ratio_ci(pa, pb, num, den, cfg=CFG):
    if not pa:
        return (math.nan, math.nan)
    rng = np.random.default_rng(cfg["bootstrap_seed"])
    n = len(pa)
    A = np.array([[r.get(num, 0.0), r.get(den, 0.0)] for r in pa])
    B = np.array([[r.get(num, 0.0), r.get(den, 0.0)] for r in pb])
    idx = rng.integers(0, n, size=(cfg["bootstrap_resamples"], n))
    with np.errstate(all="ignore"):
        va = A[idx, 0].sum(1) / A[idx, 1].sum(1)
        vb = B[idx, 0].sum(1) / B[idx, 1].sum(1)
        v = va / vb
    v = v[np.isfinite(v)]
    if not len(v):
        return (math.nan, math.nan)
    lo = (1 - cfg["ci_level"]) / 2
    return (float(np.quantile(v, lo)), float(np.quantile(v, 1 - lo)))


def wil_guard(runs, num, den, op, thr):
    return comp_guard(runs, lambda rs: wilson(ssum(rs, num), ssum(rs, den)), op, thr)


def evaluate(runs, cfg=CFG, ratings=None, prims=None):
    """`prims` = precomputed `compute_run` results (the CLI streams runs so that only the per-run measures stay in memory)."""
    T = cfg["T"]
    if prims is None:
        prims = [compute_run(r, cfg) for r in runs]
    A = defaultdict(list)
    for p in prims:
        A[(p["brain"], p["scenario"])].append(p)

    def arm(brain, *scen):
        out = []
        for s in scen:
            out += A.get((brain, "m007_bay_" + s), [])
        return out
    cand = cfg["candidate"]
    res, extra = [], {}
    hunt, churn, raid, mixed, emerge = (arm(cand, s) for s in ("hunt", "churn", "raid", "mixed", "emerge"))

    # C1
    dc = arm(cfg["directcharge"], "hunt")
    if ssum(hunt, "commits_multi") > 0:
        fr = ratio(hunt, "commits_flanked", "commits_multi")
        null = ratio(dc, "commits_flanked", "commits_multi") if ssum(dc, "commits_multi") else None
        bad, tab = wil_guard(hunt, "commits_flanked", "commits_multi", ">=", T["C1.fr_commit"])
        parts = [meas("fr_commit", fr, ">=", T["C1.fr_commit"], wilson(ssum(hunt, "commits_flanked"), ssum(hunt, "commits_multi")))]
        parts.append(meas("fr_commit_minus_k_null", None if null is None else fr - T["C1.ratio_null"] * null, ">=", 0.0))
        res.append(clause("C1", "hunt", parts, bad, tab, [ctl(cfg["directcharge"], null, fr, "higher")] if null is not None else []))
    else:
        res.append(clause("C1", "hunt", [], status="NOT_RUN", note="no commit event with >= 2 engaged kaiju"))
    # C2
    nf = arm(cfg["nofocus"], "hunt")
    if hunt and nf and ssum(hunt, "ff_ticks") and ssum(nf, "ff_ticks"):
        def fe(h, n):
            a, b = ratio(h, "ff_sum", "ff_ticks"), ratio(n, "ff_sum", "ff_ticks")
            return (a - b) / (1 - b) if b < 1 else math.nan
        cs = boot_ci(hunt, lambda rs: ratio(rs, "ff_sum", "ff_ticks"), cfg)
        cn = boot_ci(nf, lambda rs: ratio(rs, "ff_sum", "ff_ticks"), cfg)
        sep = cs[0] > cn[1]
        bad, tab = [], {}
        nfc = by_comp(nf)
        for c, rs in sorted(by_comp(hunt).items()):
            if c in nfc and ssum(rs, "ff_ticks") and ssum(nfc[c], "ff_ticks"):
                lo, hi = boot_ci(rs, lambda x, n=nfc[c]: fe(x, n), cfg)
                tab[c] = {"ci95": None if math.isnan(lo) else [lo, hi], "n_runs": len(rs)}
                if not math.isnan(hi) and hi < T["C2.fe"]:
                    bad.append(c)
        val = fe(hunt, nf)
        parts = [meas("fe_vs_nofocus", val, ">=", T["C2.fe"], boot_ci(hunt, lambda rs: fe(rs, nf), cfg)),
                 meas("ff_ci_separation", 1.0 if sep else 0.0, ">=", 1.0)]
        info = {"mean_ff": ratio(hunt, "ff_sum", "ff_ticks"), "mean_ff_nofocus": ratio(nf, "ff_sum", "ff_ticks"),
                "fe_vs_uniform_chance_informational": (lambda a, c: (a - c) / (1 - c) if c < 1 else None)(
                    ratio(hunt, "ff_sum", "ff_ticks"), ratio(hunt, "chance_sum", "ff_ticks"))}
        res.append(clause("C2", "hunt", parts, bad, tab, [ctl(cfg["nofocus"], ratio(nf, "ff_sum", "ff_ticks"),
                                                                ratio(hunt, "ff_sum", "ff_ticks"), "higher")], info=info))
    else:
        res.append(clause("C2", "hunt", [], status="NOT_RUN", note="needs candidate and nofocus@1 runs with >= 2 targets"))
    # C3
    ch_nf = arm(cfg["nofocus"], "churn")
    if churn:
        sw = ssum(churn, "voluntary")
        if sw < T["C3.min_switches"] or (ch_nf and ssum(ch_nf, "voluntary") < T["C3.min_switches"]):
            res.append(clause("C3", "churn", [], status="NOT_RUN",
                              note=f"fewer than {T['C3.min_switches']} voluntary switches (candidate {int(sw)})"))
        else:
            th, rt = ratio(churn, "thrash", "voluntary"), ratio(churn, "voluntary", "kaiju_minutes")
            b1, t1 = comp_guard(churn, lambda rs: boot_ci(rs, lambda x: ratio(x, "thrash", "voluntary"), cfg), "<=", T["C3.thrash"])
            b2, _ = comp_guard(churn, lambda rs: boot_ci(rs, lambda x: ratio(x, "voluntary", "kaiju_minutes"), cfg), "<=", T["C3.rate"])
            cts = [ctl(cfg["nofocus"], ratio(ch_nf, "voluntary", "kaiju_minutes"), rt, "lower")] if ch_nf else []
            res.append(clause("C3", "churn", [meas("thrash_fraction", th, "<=", T["C3.thrash"], boot_ci(churn, lambda rs: ratio(rs, "thrash", "voluntary"), cfg)),
                                              meas("switch_rate_per_kaiju_min", rt, "<=", T["C3.rate"], boot_ci(churn, lambda rs: ratio(rs, "voluntary", "kaiju_minutes"), cfg))],
                              sorted(set(b1 + b2)), t1, cts))
    else:
        res.append(clause("C3", "churn", [], status="NOT_RUN", note="no candidate churn runs"))
    # C4 (all scenarios, candidate) plus omniscient positive control
    allc = [p for (b, s), v in A.items() if b == cand for p in v]
    om = [p for (b, s), v in A.items() if b == cfg["omniscient"] for p in v]
    if allc:
        parts = [meas("leaks", ssum(allc, "leaks"), "<=", T["C4.leaks"])]
        info = {"leaks_v1": ssum(allc, "leaks_v1"), "leaks_provenance": ssum(allc, "leaks_prov"),
                "leaks_delivery": ssum(allc, "leaks_delivery")}
        if om:
            info["omniscient_leaks"] = ssum(om, "leaks")
            if ssum(om, "leaks") == 0:
                res.append(clause("C4", "all", parts, note="omniscient@1 produced 0 leaks: instrument cannot detect cheating",
                                  status="NON_DISCRIMINATING", info=info))
            else:
                res.append(clause("C4", "all", parts, info=info))
        else:
            res.append(clause("C4", "all", parts, info=info, controls=[{"control": cfg["omniscient"], "value": None,
                                                                         "candidate": None, "discriminates": None}]))
    else:
        res.append(clause("C4", "all", [], status="NOT_RUN", note="no candidate runs"))
    # C5 panel
    res.append(panel_clause(ratings, cfg))
    # C6 win rate
    res.append(winrate_clause(prims, cfg))
    # P1
    pk = hunt + arm(cand, "mixed")
    if ssum(pk, "p1_den"):
        cov = ratio(pk, "p1_cov", "p1_den")
        bad, tab = wil_guard(pk, "p1_cov", "p1_den", ">=", T["P1.cov"])
        fits = defaultdict(lambda: [0.0, 0.0])
        for c, rs in by_comp(pk).items():
            for s in FIT:
                v = [r["fit_by_species"][s] for r in rs if s in r["fit_by_species"]]
                if v:
                    fits[(c, s)] = [sum(x[0] for x in v) / sum(x[1] for x in v), sum(x[1] for x in v)]
        bad_fit = sorted(f"{c}:{s}" for (c, s), (m, _) in fits.items() if m < T["P1.fit_time"])
        parts = [meas("role_coverage", cov, ">=", T["P1.cov"], wilson(ssum(pk, "p1_cov"), ssum(pk, "p1_den"))),
                 meas("member_fit_time_min_over_species_x_comp", min([m for m, _ in fits.values()], default=math.nan), ">=", T["P1.fit_time"])]
        rr = arm(cfg["randomrole"], "hunt", "mixed")
        cts = [ctl(cfg["randomrole"], ratio(rr, "p1_cov", "p1_den") if ssum(rr, "p1_den") else None, cov, "higher")] if rr else []
        res.append(clause("P1", "hunt,mixed", parts, sorted(set(bad + bad_fit)), tab, cts,
                          info={"fit_time_by_comp_species": {f"{c}:{s}": m for (c, s), (m, _) in fits.items()}}))
    else:
        res.append(clause("P1", "hunt,mixed", [], status="NOT_RUN", note="no engaged pack ticks"))
    # P2
    p2 = hunt + churn
    if ssum(p2, "role_changes") < T["P2.min_changes"]:
        res.append(clause("P2", "hunt,churn", [], status="NOT_RUN", note=f"fewer than {T['P2.min_changes']} role changes ({int(ssum(p2, 'role_changes'))})"))
    else:
        rate, th = ratio(p2, "role_changes", "member_minutes"), ratio(p2, "role_thrash", "role_changes")
        b1, tab = comp_guard(p2, lambda rs: boot_ci(rs, lambda x: ratio(x, "role_changes", "member_minutes"), cfg), "<=", T["P2.rate"])
        res.append(clause("P2", "hunt,churn", [meas("role_changes_per_member_min", rate, "<=", T["P2.rate"]),
                                               meas("role_thrash_fraction", th, "<=", T["P2.thrash"])], b1, tab))
    # P3
    p3 = hunt + arm(cand, "mixed")
    if ssum(p3, "loss_events") < T["P3.min_events"]:
        res.append(clause("P3", "hunt,mixed", [], status="NOT_RUN", note=f"fewer than {T['P3.min_events']} loss events ({int(ssum(p3, 'loss_events'))})"))
    else:
        sh = ratio(p3, "loss_ok", "loss_events")
        bad, tab = wil_guard(p3, "loss_ok", "loss_events", ">=", T["P3.share"])
        rr = arm(cfg["randomrole"], "hunt", "mixed")
        cts = [ctl(cfg["randomrole"], ratio(rr, "loss_ok", "loss_events") if ssum(rr, "loss_events") else None, sh, "higher")] if rr else []
        lats = [x for r in p3 for x in r["latencies"] if math.isfinite(x)]
        res.append(clause("P3", "hunt,mixed", [meas("share_Lr_le_10s", sh, ">=", T["P3.share"], wilson(ssum(p3, "loss_ok"), ssum(p3, "loss_events")))],
                          bad, tab, cts, info={"median_Lr_s": float(np.median(lats)) if lats else None}))
    # P4
    nb = arm(cfg["nobait"], "hunt")
    if ssum(hunt, "bs_opp_strict") > 0:
        r_strict = ratio(hunt, "bs_ev_strict", "bs_opp_strict")
        r_any = ratio(hunt, "bs_ev_any", "bs_opp_any")
        null = ratio(nb, "bs_ev_any", "bs_opp_any") if ssum(nb, "bs_opp_any") else None
        bad, tab = wil_guard(hunt, "bs_ev_strict", "bs_opp_strict", ">=", T["P4.rate"])
        parts = [meas("bs_rate", r_strict, ">=", T["P4.rate"], wilson(ssum(hunt, "bs_ev_strict"), ssum(hunt, "bs_opp_strict"))),
                 meas("bs_rate_minus_k_nobait", None if null is None else r_any - T["P4.ratio_null"] * null, ">=", 0.0)]
        res.append(clause("P4", "hunt", parts, bad, tab, [ctl(cfg["nobait"], null, r_any, "higher")] if null is not None else [],
                          info={"bs_rate_role_agnostic": r_any}))
    else:
        res.append(clause("P4", "hunt", [], status="NOT_RUN", note="no Bait aim opportunity (>= 3 s)"))
    # P5
    nr = arm(cfg["noroles"], "hunt", "mixed")
    pa, pb = pair_runs(p3, nr)
    if pa and ssum(pa, "kaiju_loss_frac") and ssum(pb, "kaiju_loss_frac"):
        kx = lambda rs: ratio(rs, "mech_frac", "kaiju_loss_frac")  # noqa: E731
        val = kx(pa) / kx(pb)
        ci = paired_ratio_ci(pa, pb, "mech_frac", "kaiju_loss_frac", cfg)
        bad, tab = [], {}
        for c in sorted({r["comp"] for r in pa}):
            ia = [i for i, r in enumerate(pa) if r["comp"] == c]
            lo, hi = paired_ratio_ci([pa[i] for i in ia], [pb[i] for i in ia], "mech_frac", "kaiju_loss_frac", cfg)
            tab[c] = {"ci95": None if math.isnan(lo) else [lo, hi], "n_pairs": len(ia)}
            if not math.isnan(hi) and hi < T["P5.ratio"]:
                bad.append(c)
        res.append(clause("P5", "hunt,mixed", [meas("kx_ratio_vs_noroles", val, ">=", T["P5.ratio"], ci),
                                               meas("kx_ratio_ci_lower", ci[0], ">=", T["P5.lb"])], bad, tab,
                          [ctl(cfg["noroles"], kx(pb), kx(pa), "higher")]))
    else:
        res.append(clause("P5", "hunt,mixed", [], status="NOT_RUN", note="needs paired candidate and noroles@1 runs with kaiju losses"))
    # P6
    if ssum(p3, "p6_pairs"):
        sh = ratio(p3, "p6_ok", "p6_pairs")
        bad, tab = wil_guard(p3, "p6_ok", "p6_pairs", ">=", T["P6.share"])
        res.append(clause("P6", "hunt,mixed", [meas("participation_share", sh, ">=", T["P6.share"], wilson(ssum(p3, "p6_ok"), ssum(p3, "p6_pairs")))], bad, tab))
    else:
        res.append(clause("P6", "hunt,mixed", [], status="NOT_RUN", note="no engagements"))
    # I1
    if ssum(raid, "raid_runs"):
        sh = ratio(raid, "raid_done", "raid_runs")
        bad, tab = wil_guard(raid, "raid_done", "raid_runs", ">=", T["I1.rate"])
        tfd = [r["t_first_destruction"] for r in raid if r.get("raid_runs") and not math.isnan(r.get("t_first_destruction", math.nan))]
        res.append(clause("I1", "raid", [meas("raid_completion_rate", sh, ">=", T["I1.rate"], wilson(ssum(raid, "raid_done"), ssum(raid, "raid_runs")))],
                          bad, tab, info={"median_time_to_first_destruction_s": float(np.median(tfd)) if tfd else None,
                                          "runs_without_raider_excluded": len(raid) - int(ssum(raid, "raid_runs"))}))
    else:
        res.append(clause("I1", "raid", [], status="NOT_RUN", note="no raid runs with a Raider"))
    # I2
    if mixed and ssum(mixed, "ab_den") and ssum(mixed, "id_den"):
        ab, idl = ratio(mixed, "ab_num", "ab_den"), ratio(mixed, "id_num", "id_den")
        res.append(clause("I2", "mixed", [meas("abandonment", ab, "<=", T["I2.ab"], wilson(ssum(mixed, "ab_num"), ssum(mixed, "ab_den"))),
                                          meas("idle", idl, "<=", T["I2.id"], wilson(ssum(mixed, "id_num"), ssum(mixed, "id_den")))],
                          comp_guard(mixed, lambda rs: wilson(ssum(rs, "ab_num"), ssum(rs, "ab_den")), "<=", T["I2.ab"])[0]
                          + comp_guard(mixed, lambda rs: wilson(ssum(rs, "id_num"), ssum(rs, "id_den")), "<=", T["I2.id"])[0]))
    else:
        res.append(clause("I2", "mixed", [], status="NOT_RUN", note="needs mixed runs with mech proximity and idle windows"))
    # I3
    ni = arm(cfg["nearestinfra"], "raid", "mixed")
    pa, pb = pair_runs(raid + mixed, ni)
    if pa and ssum(pa, "kaiju_hours") and ssum(pb, "ival") > 0:
        val = ratio(pa, "ival", "kaiju_hours") / ratio(pb, "ival", "kaiju_hours")
        ci = paired_ratio_ci(pa, pb, "ival", "kaiju_hours", cfg)
        res.append(clause("I3", "raid,mixed", [meas("ival_per_kaiju_hour_ratio", val, ">=", T["I3.ratio"], ci),
                                               meas("ratio_ci_lower", ci[0], ">=", T["I3.lb"])],
                          controls=[ctl(cfg["nearestinfra"], ratio(pb, "ival", "kaiju_hours"), ratio(pa, "ival", "kaiju_hours"), "higher")]))
    else:
        res.append(clause("I3", "raid,mixed", [], status="NOT_RUN", note="needs paired candidate and nearestinfra@1 runs"))
    # I4 informational
    r4 = ssum(mixed + raid, "i4_raids")
    res.append(clause("I4", "mixed", [], status="INFORMATIONAL", note="gating (>= 0.30) only after T-125 calibration",
                      info={"raids": r4, "share_pulled_then_bait_and_strike": (ssum(mixed + raid, "i4_pulled_bs") / r4) if r4 else None,
                            "share_pulled": (ssum(mixed + raid, "i4_pulled") / r4) if r4 else None}))
    # F1
    if ssum(emerge, "f1_emerged"):
        sh = ratio(emerge, "f1_valid", "f1_emerged")
        bad, tab = wil_guard(emerge, "f1_valid", "f1_emerged", ">=", T["F1.share"])
        parts = [meas("share_in_valid_pack", sh, ">=", T["F1.share"], wilson(ssum(emerge, "f1_valid"), ssum(emerge, "f1_emerged"))),
                 meas("packs_over_5_runs", ssum(emerge, "f1_over5"), "<=", 0),
                 meas("merges_beyond_call_range", ssum(emerge, "f1_bad_merge"), "<=", 0)]
        res.append(clause("F1", "emerge", parts, bad, tab))
    else:
        res.append(clause("F1", "emerge", [], status="NOT_RUN", note="no emerge runs"))
    # informational
    sp = {}
    pool = [p for (b, s), v in A.items() if b == cand for p in v]
    for s in FIT:
        sp[s] = {"engaged_decisions": sum(p["sp_eng"].get(s, 0) for p in pool), "damage_to_mechs": sum(p["sp_dmg"].get(s, 0) for p in pool),
                 "deaths": sum(p["sp_deaths"].get(s, 0) for p in pool),
                 "role_time": {ro: sum(p["sp_role"].get(s, {}).get(ro, 0) for p in pool) for ro in ROLES}}
    extra["per_species"] = sp
    oa, ob = pair_runs(om, hunt + mixed)
    if oa:
        ds = float(np.mean([a["S"] for a in oa]) - np.mean([b["S"] for b in ob]))
        extra["omniscient_gap_delta_S"] = {"value": ds, "pairs": len(oa),
                                           "note": None if ds > 0 else "fair pack >= omniscient: fairness model costs nothing; re-examine"}
    extra["runs"] = {f"{b}|{s}": len(v) for (b, s), v in sorted(A.items())}
    ok = [c for c in res if c["status"] == "PASS"]
    return {"spec": "M-007@v2", "candidate": cand, "clauses": res, "informational": extra,
            "summary": {c["clause"]: c["status"] for c in res}, "all_pass": len(ok) == len(res)}


# ---------------------------------------------------------------- C5 panel and C6 win rate
def panel_clause(ratings, cfg):
    T = cfg["T"]
    if not ratings:
        return clause("C5", "hunt,raid,mixed", [], status="PENDING", note="human panel: >= 12 raters x 8 blinded clips (T-125); no ratings file")
    from scipy.stats import wilcoxon
    arms = defaultdict(lambda: defaultdict(list))
    raters = set()
    for r in ratings:
        arms[r["arm"]][r["rater"]].append(r)
        raters.add(r["rater"])
    if len(raters) < T["C5.raters"]:
        return clause("C5", "hunt,raid,mixed", [], status="NOT_RUN", note=f"{len(raters)} raters < {T['C5.raters']}")
    cand = arms.get("candidate", {})
    sm = [x["smart"] for v in cand.values() for x in v]
    ch = [x["cheap"] for v in cand.values() for x in v]

    def per_rater(arm, item):
        return {k: float(np.mean([x[item] for x in v])) for k, v in arms.get(arm, {}).items()}

    def paired_p(a_arm, b_arm, item, alt):
        a, b = per_rater(a_arm, item), per_rater(b_arm, item)
        ks = sorted(set(a) & set(b))
        if len(ks) < 6 or all(a[k] == b[k] for k in ks):
            return math.nan
        return float(wilcoxon([a[k] for k in ks], [b[k] for k in ks], alternative=alt).pvalue)
    p_om = paired_p("omniscient@1", "candidate", "cheap", "greater")
    p_solo = paired_p("candidate", "solo@1", "smart", "greater")
    parts = [meas("median_smart", float(np.median(sm)), ">=", T["C5.smart"]), meas("median_cheap", float(np.median(ch)), "<=", T["C5.cheap"]),
             meas("p_omniscient_rated_cheaper", p_om, "<=", T["C5.p"]), meas("p_candidate_smarter_than_solo", p_solo, "<=", T["C5.p"])]
    return clause("C5", "hunt,raid,mixed", parts, note="instrument void if p_omniscient_rated_cheaper fails")


def winrate_clause(prims, cfg):
    T = cfg["T"]
    pol = [p for p in prims if p["brain"] == cfg["policy"] and p["scenario"] in ("m007_bay_hunt", "m007_bay_mixed")]
    base = [p for p in prims if p["brain"] == cfg["candidate"] and p["scenario"] in ("m007_bay_hunt", "m007_bay_mixed")]
    if not pol:
        return clause("C6", "hunt,mixed", [], status="PENDING", note="pending T-041 training: no trained-policy runs")
    pa, pb = pair_runs(pol, base)
    n = len(pa)
    if n < T["C6.seeds"]:
        return clause("C6", "hunt,mixed", [], status="NOT_RUN", note=f"{n} paired seeds < {T['C6.seeds']}")
    score = lambda ps: sum(1.0 if a["S"] > b["S"] else 0.5 if a["S"] == b["S"] else 0.0 for a, b in ps)  # noqa: E731
    w = score(zip(pa, pb)) / n
    lo, hi = wilson(w * n, n)
    bad = []
    for c in sorted({r["comp"] for r in pa}):
        ps = [(a, b) for a, b in zip(pa, pb) if a["comp"] == c]
        l2, h2 = wilson(score(ps), len(ps))
        if h2 < T["C6.win"]:
            bad.append(c)
    return clause("C6", "hunt,mixed", [meas("win_rate", w, ">=", T["C6.win"], (lo, hi)), meas("wilson_lower_bound", lo, ">=", T["C6.wilson_lb"])], bad)


# ---------------------------------------------------------------- CLI
def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("inputs", nargs="+")
    ap.add_argument("--ratings")
    ap.add_argument("--candidate", default=CFG["candidate"])
    ap.add_argument("--policy", default=CFG["policy"])
    ap.add_argument("--out")
    a = ap.parse_args(argv)
    cfg = dict(CFG, candidate=a.candidate, policy=a.policy)
    files = expand(a.inputs)
    if not files:
        print("no .jsonl files", file=sys.stderr)
        return 2
    prims, errs = [], []
    for f in files:   # stream: a Bay run is tens of MB of rows; keep only the per-run measures
        try:
            prims.append(compute_run(load_run(f), cfg))
        except InputError as e:
            errs += e.errors
    if errs:
        for x in errs[:50]:
            print(x, file=sys.stderr)
        return 2
    ratings = json.load(open(a.ratings)) if a.ratings else None
    rep = evaluate([], cfg, ratings, prims=prims)
    text = json.dumps(rep, indent=1, default=lambda o: None if isinstance(o, float) and math.isnan(o) else str(o))
    if a.out:
        open(a.out, "w").write(text)
    print(json.dumps(rep["summary"]))
    return 0 if rep["all_pass"] else 1


if __name__ == "__main__":
    sys.exit(main())
