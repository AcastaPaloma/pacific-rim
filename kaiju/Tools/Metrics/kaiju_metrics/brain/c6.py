"""M-007 C6 (R-028 section 11 / R-024 section 5): duplicate-format win rate of a candidate brain against `scripted@1`, plus the
edge-time diagnostic. Pure functions over loaded `brain_log@v1` runs; `m007.run` wires them in when `--candidate <brain_id>` is given.

Outcome of one run, higher is better, compared lexicographically (R-028 section 11):
  (all mechs dead, mechs killed, damage dealt to mechs, kaiju alive at end, -time of the last mech death).
Mechs are entities >= 101 (harness ids), kaiju < 101. A pair (same scenario + seed, scripted vs candidate) is won by the better
outcome; equal outcomes are ties and are excluded from the rate. More than half the pairs tied = NOT_RUN."""
from __future__ import annotations

import math

from .brain_log import ent_id

MECH_MIN_ID = 101
ROUND_DIGITS = int("6")
Z95 = 1.959963984540054


def outcome(log):
    mechs = {ent_id(r["ent"]) for r in log.samples if ent_id(r["ent"]) >= MECH_MIN_ID}
    mechs |= {ent_id(r["victim"]) for r in log.hits if ent_id(r["victim"]) >= MECH_MIN_ID}
    kaiju = {ent_id(r["ent"]) for r in log.decisions if ent_id(r["ent"]) < MECH_MIN_ID}
    dead_m = [r for r in log.deaths if ent_id(r["ent"]) >= MECH_MIN_ID]
    dead_k = {ent_id(r["ent"]) for r in log.deaths if ent_id(r["ent"]) < MECH_MIN_ID}
    dmg = sum(r["damage"] for r in log.hits if ent_id(r["victim"]) >= MECH_MIN_ID and ent_id(r["attacker"]) < MECH_MIN_ID)
    step_s = float(log.header["step_seconds"])
    all_dead = bool(mechs) and len({ent_id(r["ent"]) for r in dead_m}) >= len(mechs)
    t_win = max((r["step"] for r in dead_m), default=0) * step_s if all_dead else 0.0
    return {"all_dead": int(all_dead), "killed": len({ent_id(r["ent"]) for r in dead_m}), "damage": float(dmg),
            "kaiju_alive": len(kaiju - dead_k), "t_win_s": t_win,
            "key": (int(all_dead), len({ent_id(r["ent"]) for r in dead_m}), round(float(dmg), ROUND_DIGITS), len(kaiju - dead_k), -t_win)}


def wilson_lower(wins, n, z=Z95):
    """Lower end of the Wilson score interval for wins / n (0 for n == 0)."""
    if n <= 0:
        return 0
    p = wins / n
    den = 1 + z * z / n
    centre = p + z * z / (2 * n)
    half = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n))
    return (centre - half) / den


def pair_runs(baseline, candidate):
    """Pair logs by (scenario, seed). Returns (pairs, only_baseline, only_candidate) with pairs = [(key, base_log, cand_log)]."""
    def index(logs):
        d = {}
        for lg in logs:
            d.setdefault((lg.header.get("scenario"), int(lg.header["seed"])), lg)
        return d
    b, c = index(baseline), index(candidate)
    keys = sorted(set(b) & set(c), key=lambda k: (str(k[0]), k[1]))
    return [(k, b[k], c[k]) for k in keys], sorted(set(b) - set(c), key=str), sorted(set(c) - set(b), key=str)


def score_pairs(pairs):
    wins = losses = ties = 0
    base_win = cand_win = 0
    per_pair = []
    for key, bl, cl in pairs:
        ob, oc = outcome(bl), outcome(cl)
        base_win += ob["all_dead"]; cand_win += oc["all_dead"]
        if oc["key"] > ob["key"]:
            wins += 1; r = "candidate"
        elif oc["key"] < ob["key"]:
            losses += 1; r = "baseline"
        else:
            ties += 1; r = "tie"
        per_pair.append({"scenario": key[0], "seed": key[1], "winner": r, "baseline": ob["key"][:4], "candidate": oc["key"][:4]})
    n = wins + losses
    return {"pairs": len(pairs), "wins": wins, "losses": losses, "ties": ties, "non_tied": n,
            "pair_win_rate": (wins / n) if n else None, "wilson_lower_95": wilson_lower(wins, n) if n else None,
            "tie_fraction": (ties / len(pairs)) if pairs else None,
            "baseline_absolute_win_rate": (base_win / len(pairs)) if pairs else None,
            "candidate_absolute_win_rate": (cand_win / len(pairs)) if pairs else None, "per_pair": per_pair}


def edge_time(logs, field_half_m=2000.0, margin_frac=0.05):
    """Edge-time diagnostic: share of live-kaiju decision rows with |x| or |y| within `margin_frac` of the field edge (the
    harness clamps position at +-FieldHalfM, so a policy can park against the wall). Diagnostic only; never gates."""
    lim = field_half_m * (1.0 - margin_frac)
    tot = edge = 0
    for lg in logs:
        for r in lg.decisions:
            if not r["alive"] or ent_id(r["ent"]) >= MECH_MIN_ID:
                continue
            tot += 1
            if abs(r["x"]) >= lim or abs(r["y"]) >= lim:
                edge += 1
    return {"decisions": tot, "at_edge": edge, "fraction": (edge / tot) if tot else None, "margin_frac": margin_frac,
            "field_half_m": field_half_m}
