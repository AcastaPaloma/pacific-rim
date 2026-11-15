"""M-007@v1 Kaiju look intelligent: the behaviour clauses C1..C4 from `brain_log@v1` (research/R-024.md sections 3, 5).

Input: brain_log@v1 files or directories (scan recursively for *.jsonl). Logs are split by header `brain_id`: the
`scripted@1` runs are the measured set, `directcharge@1` runs the null control (C1). Pooled by engagement over runs.
  C1 FR_commit (pooled commit events with >= 2 engaged / flanked) >= 0.60 and >= 2 x null; 95 % bootstrap CI over runs
  C2 focus excess FE; C3 thrash fraction and voluntary switch rate; C4 information leaks (target neither visible nor in
  memory). C5 panel (PENDING without ratings: not implemented here, human clips) and C6 win rate (deferred to a policy).
Primitives are an exact port of Game.AI.Harness.PackMetrics (kept in agreement by tests on a shared log)."""
from __future__ import annotations

import math
import os
import random

from ..common import ContractError, ErrorList, InvalidInput, sha256_file
from ..report import part
from .brain_log import REASON_VOLUNTARY, STATE_CHARGE, ent_id, load_brain_log

D_ENG_DEFAULT = 400.0


def expand_inputs(inputs):
    out = []
    for p in inputs:
        if os.path.isdir(p):
            for r, _d, fs in os.walk(p):
                out += [os.path.join(r, f) for f in fs if f.endswith(".jsonl")]
        else:
            out.append(p)
    return sorted(out)


def ff_chance(n, m):
    """E[max bin count]/n for n kaiju picking uniformly among m targets (exact enumeration over compositions)."""
    if n < 1 or m < 1:
        return 1.0
    if m ** n > 2_000_000:
        return math.nan
    tot = 0
    cnt_sum = 0
    for c in range(m ** n):
        v = c
        bins = [0] * m
        for _ in range(n):
            bins[v % m] += 1
            v //= m
        cnt_sum += max(bins)
        tot += 1
    return cnt_sum / tot / n


_FFC = {}


def _ffc(n, m):
    if (n, m) not in _FFC:
        _FFC[(n, m)] = ff_chance(n, m)
    return _FFC[(n, m)]


def _bearing(dx, dy):
    b = math.degrees(math.atan2(dy, dx))
    return b + 360.0 if b < 0 else b


def spread_deg(bearings):
    """Minimal arc containing all bearings (360 minus the largest circular gap); needs >= 2."""
    b = sorted(bearings)
    mg = 0.0
    for i in range(len(b)):
        g = (b[i + 1] - b[i]) if i + 1 < len(b) else (b[0] + 360.0 - b[i])
        mg = max(mg, g)
    return 360.0 - mg


def compute_run(log, reg, d_eng=None):
    """Primitives of one run (port of PackMetrics.Compute)."""
    flank_min_deg, fresh_s = reg.p("flank_spread_deg"), reg.p("commit_fresh_s")
    thrash_s, mem_s = reg.p("thrash_window_s"), reg.p("memory_s")
    h = log.header
    d_eng = float(d_eng if d_eng is not None else h.get("d_eng_m", D_ENG_DEFAULT))
    step_s = float(h["step_seconds"])
    period_s = float(h["brain_period_steps"]) * step_s
    out = {"commits": 0, "commits_multi": 0, "commits_flanked": 0, "multi_ticks": 0, "flanked_ticks": 0,
           "ff_ticks": 0, "sum_ff": 0.0, "sum_chance": 0.0, "voluntary": 0, "thrash": 0, "engaged_decisions": 0,
           "leaks": 0, "hits": len(log.hits), "deaths": len(log.deaths), "diag": []}
    kaiju, tpos, tstep = {}, {}, {}
    last_charge, last_switch = {}, {}
    cur_step = None
    rows = []

    def eval_step():
        if not rows:
            return
        now = rows[0]["t"]
        live_targets = sum(1 for k, s in tstep.items() if s == cur_step and tpos[k]["alive"] == 1)
        groups, per_target, with_target = {}, {}, 0
        for k in kaiju.values():
            if k["alive"] != 1 or ent_id(k["target"]) == 0:
                continue
            tg = ent_id(k["target"])
            with_target += 1
            per_target[tg] = per_target.get(tg, 0) + 1
            q = tpos.get(tg)
            if q is None:
                continue
            dx, dy = k["x"] - q["x"], k["y"] - q["y"]
            if math.hypot(dx, dy) > d_eng:
                continue
            groups.setdefault(tg, []).append(_bearing(dx, dy))
        flanked, multi = set(), set()
        for tg, b in groups.items():
            if len(b) < 2:
                continue
            multi.add(tg)
            out["multi_ticks"] += 1
            if spread_deg(b) >= flank_min_deg:
                flanked.add(tg)
                out["flanked_ticks"] += 1
        for r in rows:
            tg = ent_id(r["target"])
            if r["alive"] != 1 or tg == 0 or r["state"] != STATE_CHARGE:
                continue
            entry = r["prev_state"] != STATE_CHARGE
            fresh = tg not in last_charge or now - last_charge[tg] > fresh_s
            if entry and fresh:
                out["commits"] += 1
                if tg in multi:
                    out["commits_multi"] += 1
                    if tg in flanked:
                        out["commits_flanked"] += 1
            last_charge[tg] = now
        if with_target >= 2 and live_targets >= 2:
            out["sum_ff"] += max(per_target.values()) / with_target
            out["sum_chance"] += _ffc(with_target, live_targets)
            out["ff_ticks"] += 1
        rows.clear()

    for kind, r in log.order:
        if kind in ("hit", "death"):
            continue
        if r["step"] != cur_step:
            eval_step()
            cur_step = r["step"]
        if kind == "target_sample":
            e = ent_id(r["ent"])
            tpos[e] = r
            tstep[e] = r["step"]
            continue
        e, tg = ent_id(r["ent"]), ent_id(r["target"])
        if tg != 0 and r["tgt_visible"] == 0 and r["tgt_mem_age_s"] > mem_s:
            out["leaks"] += 1
        prev = kaiju.get(e)
        if prev is not None and ent_id(prev["target"]) != 0 and tg != 0 and ent_id(prev["target"]) != tg \
                and r["reason"] == REASON_VOLUNTARY:
            out["voluntary"] += 1
            ls = last_switch.get(e)
            if ls is not None and ls[0] == tg and r["t"] - ls[1] <= thrash_s:
                out["thrash"] += 1
            last_switch[e] = (ent_id(prev["target"]), r["t"])
        if r["alive"] == 1 and tg != 0 and tg in tpos:
            if math.hypot(r["x"] - tpos[tg]["x"], r["y"] - tpos[tg]["y"]) <= d_eng:
                out["engaged_decisions"] += 1
        kaiju[e] = r
        rows.append(r)
    eval_step()
    out["engaged_minutes"] = out["engaged_decisions"] * period_s / 60.0
    return out


def pool(runs):
    t = {k: sum(r[k] for r in runs) for k in ("commits", "commits_multi", "commits_flanked", "multi_ticks",
                                              "flanked_ticks", "ff_ticks", "sum_ff", "sum_chance", "voluntary",
                                              "thrash", "engaged_minutes", "leaks", "hits", "deaths")}
    t["fr_commit"] = t["commits_flanked"] / t["commits_multi"] if t["commits_multi"] else math.nan
    t["fr_time"] = t["flanked_ticks"] / t["multi_ticks"] if t["multi_ticks"] else math.nan
    if t["ff_ticks"]:
        ff, ch = t["sum_ff"] / t["ff_ticks"], t["sum_chance"] / t["ff_ticks"]
        t["mean_ff"], t["mean_ff_chance"] = ff, ch
        t["fe"] = (ff - ch) / (1 - ch) if ch < 1 else math.nan
    else:
        t["mean_ff"] = t["mean_ff_chance"] = t["fe"] = math.nan
    t["thrash_fraction"] = t["thrash"] / t["voluntary"] if t["voluntary"] else 0.0
    t["switch_rate"] = t["voluntary"] / t["engaged_minutes"] if t["engaged_minutes"] > 0 else math.nan
    return t


def bootstrap_ci(runs, stat, n, seed, level):
    """Percentile CI of a pooled statistic, resampling whole runs (deterministic)."""
    if not runs or n <= 0:
        return None
    rng = random.Random(seed)
    vals = []
    for _ in range(n):
        s = stat(pool([runs[rng.randrange(len(runs))] for _ in runs]))
        if not (isinstance(s, float) and math.isnan(s)):
            vals.append(s)
    if not vals:
        return None
    vals.sort()
    lo, hi = (1 - level) / 2, 1 - (1 - level) / 2
    return [vals[int(lo * (len(vals) - 1))], vals[int(round(hi * (len(vals) - 1)))]]


def _fin(x):
    return None if isinstance(x, float) and math.isnan(x) else x


def run(rep, inputs, reg, ratings=None, out_dir=None, images=False, options=None):
    files = expand_inputs(inputs)
    if not files:
        el = ErrorList("")
        el.add("arguments", "no brain_log@v1 files given", ">= 1 .jsonl")
        raise InvalidInput(el.errors)
    errs, logs = [], []
    for f in files:
        lg, e = load_brain_log(f)
        errs += e
        if lg is not None:
            logs.append(lg)
            rep.add_input(f, sha256_file(f), "brain_log@v1")
    if errs:
        raise InvalidInput(errs)
    ids = reg.p("scripted_brain_id"), reg.p("control_brain_id")
    nofocus_id = reg.p("nofocus_brain_id")
    for lg in logs:
        if lg.brain_id not in ids and lg.brain_id != nofocus_id:
            raise InvalidInput([ContractError(lg.path, "header.brain_id", "unknown brain", " or ".join(ids + (nofocus_id,)), lg.brain_id)])
    scripted = [compute_run(lg, reg) for lg in logs if lg.brain_id == ids[0]]
    control = [compute_run(lg, reg) for lg in logs if lg.brain_id == ids[1]]
    nofocus = [compute_run(lg, reg) for lg in logs if lg.brain_id == nofocus_id]
    rep.coverage["required"] = ["scripted runs", "control runs"]
    rep.coverage["provided"] = [n for n, v in (("scripted runs", scripted), ("control runs", control)) if v]
    rep.coverage["missing"] = [n for n, v in (("scripted runs", scripted), ("control runs", control)) if not v]
    n_boot, seed = int(reg.p("bootstrap_resamples")), int(reg.p("bootstrap_seed"))
    min_runs = reg.t("M-007.runs", "min_runs")
    n_s, n_c = len(scripted), len(control)
    enough = n_s >= min_runs and n_c >= min_runs
    rep.add("M-007.runs", status=None if enough else "NOT_RUN",
            parts=[part("scripted_runs", n_s, ">=", min_runs, unit="runs"),
                   part("control_runs", n_c, ">=", min_runs, unit="runs")],
            note=None if enough else
            f"M-007 needs {min_runs} seeded runs per brain (R-024 section 5); found scripted {n_s}, control {n_c}")
    ps = pool(scripted) if scripted else None
    pc = pool(control) if control else None
    level = float(reg.p("bootstrap_ci_level"))
    ci = lambda runs, f: bootstrap_ci(runs, f, n_boot, seed, level)  # noqa: E731

    def disc(stat, direction, control=control):
        """T-033b: does the scripted brain differ from the null control? CI non-overlap, `direction` is the better side
        ("higher" / "lower"). Diagnostic only; it never changes a clause status (thresholds are R-024's)."""
        if not scripted or not control:
            return None
        cs, cc = ci(scripted, stat), ci(control, stat)
        if cs is None or cc is None:
            return None
        overlap = not (cs[0] > cc[1] or cc[0] > cs[1])
        better = (cs[0] > cc[1]) if direction == "higher" else (cs[1] < cc[0])
        return {"scripted_ci95": cs, "control_ci95": cc, "ci_overlap": overlap, "scripted_better_non_overlapping": better}

    # C1
    if ps and ps["commits_multi"] > 0:
        fr = ps["fr_commit"]
        null = pc["fr_commit"] if pc and pc["commits_multi"] > 0 else None
        parts = [part("fr_commit", fr, ">=", reg.t("M-007.C1", "fr_commit_min"), unit="ratio")]
        if null is None:
            parts.append(part("fr_commit_over_null", None, ">=", reg.t("M-007.C1", "ratio_to_null_min"), unit="x"))
        else:
            # ratio test written as fr - k * null >= 0 (null may be 0)
            parts.append(part("fr_commit_over_null", fr - reg.t("M-007.C1", "ratio_to_null_min") * null, ">=", 0.0,
                              unit="ratio excess"))
        rep.add("M-007.C1", parts=parts, diagnostics={
            "commit_events": ps["commits"], "commit_events_multi": ps["commits_multi"],
            "commit_events_flanked": ps["commits_flanked"], "fr_time": _fin(ps["fr_time"]),
            "fr_commit_ci95_bootstrap": ci(scripted, lambda p: p["fr_commit"]),
            "fr_commit_null": _fin(null) if null is not None else None,
            "null_commit_events_multi": pc["commits_multi"] if pc else 0,
            "null_commit_events_flanked": pc["commits_flanked"] if pc else 0,
            "discrimination_vs_null": disc(lambda p: p["fr_commit"], "higher")})
    else:
        rep.add("M-007.C1", status="NOT_RUN", note="no commit event with >= 2 engaged kaiju in the scripted runs",
                diagnostics={"commit_events": ps["commits"] if ps else 0})
    # C2
    if ps and ps["ff_ticks"] > 0 and not math.isnan(ps["fe"]):
        rep.add("M-007.C2", parts=[part("focus_excess", ps["fe"], ">=", reg.t("M-007.C2", "fe_min"), unit="ratio")],
                diagnostics={"mean_ff": ps["mean_ff"], "mean_ff_chance": ps["mean_ff_chance"], "ff_ticks": ps["ff_ticks"],
                             "fe_ci95_bootstrap": ci(scripted, lambda p: p["fe"]),
                             "fe_null": _fin(pc["fe"]) if pc else None,
                             "discrimination_vs_null": disc(lambda p: p["fe"], "higher"),
                             "fe_nofocus": _fin(pool(nofocus)["fe"]) if nofocus else None,
                             "discrimination_vs_nofocus": disc(lambda p: p["fe"], "higher", nofocus)})
    else:
        rep.add("M-007.C2", status="NOT_RUN", note="no tick with >= 2 kaiju with a target and >= 2 live targets")
    # C3
    if ps:
        sr = ps["switch_rate"]
        rep.add("M-007.C3", parts=[
            part("thrash_fraction", ps["thrash_fraction"], "<=", reg.t("M-007.C3", "thrash_fraction_max"), unit="ratio"),
            part("switch_rate_per_kaiju_min", sr if not math.isnan(sr) else 0.0, "<=",
                 reg.t("M-007.C3", "switch_rate_max"), unit="1/kaiju-min")],
                diagnostics={"voluntary_switches": ps["voluntary"], "thrash": ps["thrash"],
                             "engaged_kaiju_minutes": ps["engaged_minutes"],
                             "thrash_fraction_null": pc["thrash_fraction"] if pc else None,
                             "switch_rate_null": _fin(pc["switch_rate"]) if pc else None,
                             "discrimination_switch_rate_vs_null": disc(lambda p: p["switch_rate"], "lower"),
                             "discrimination_thrash_fraction_vs_null": disc(lambda p: p["thrash_fraction"], "lower"),
                             "switch_rate_nofocus": _fin(pool(nofocus)["switch_rate"]) if nofocus else None,
                             "discrimination_switch_rate_vs_nofocus": disc(lambda p: p["switch_rate"], "lower", nofocus),
                             "discrimination_thrash_fraction_vs_nofocus": disc(lambda p: p["thrash_fraction"], "lower", nofocus)})
        # C4
        rep.add("M-007.C4", parts=[part("leaks", ps["leaks"], "<=", reg.t("M-007.C4", "leaks_max"), unit="ticks")],
                diagnostics={"control_leaks": pc["leaks"] if pc else None})
    else:
        rep.add("M-007.C3", status="NOT_RUN", note="no scripted runs")
        rep.add("M-007.C4", status="NOT_RUN", note="no scripted runs")
    rep.add("M-007.C5", status="PENDING", note="panel: >= 12 raters x 6 blinded clips (human step; no ratings file format yet)")
    rep.add("M-007.C6", status="NOT_APPLICABLE", spec_clause="R-024 section 5 C6: deferred, waits for a trained policy",
            note="win rate vs scripted baseline needs a policy (R-024 section 5)")
    rep.extra["m007"] = {"scripted": _strip(ps), "control": _strip(pc), "runs": {"scripted": n_s, "control": n_c, "nofocus": len(nofocus)},
                         "scripted_run_fr": [round(r["commits_flanked"] / r["commits_multi"], 6) if r["commits_multi"] else None
                                             for r in scripted]}
    for r in scripted + control:
        r.pop("diag", None)


def _strip(p):
    return None if p is None else {k: (_fin(v) if isinstance(v, float) else v) for k, v in p.items()}
