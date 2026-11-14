"""calibrate: propose thresholds from founder / panel ratings (orchestrator §4.3). Never writes the registry.

Inputs: a panel_ratings@v1 file and the metric reports of the rated captures (a rating row's `capture` matches a
report's `label`). Per capture the rating is the median over blind, non-excluded raters of the overall score.
Refuses (exit 2) with fewer than 8 rated captures or misbehaving anchors (a negative anchor with median >= 6, or
positive anchors not rated above negative ones)."""
from __future__ import annotations

import difflib
import itertools
import os
import statistics

import numpy as np

from .common import REGISTRY_DIR, ContractError, InvalidInput, load_json_file, rel_path
from .ratings import load_ratings
from .registry import Registry, load_registry, registry_files

MIN_CAPTURES = 8
NEG_ANCHOR_MAX = 6


def _reports(paths):
    out = []
    for p in paths:
        if os.path.isdir(p):
            for root, _, files in os.walk(p):
                out += [os.path.join(root, f) for f in files if f == "report.json"]
        else:
            out.append(p)
    return sorted(out)


def _spearman(a, b):
    from scipy.stats import spearmanr
    if len(a) < 3 or np.std(a) == 0 or np.std(b) == 0:
        return None
    return float(spearmanr(a, b)[0])


def best_cut(values, good, op):
    """Cut minimising false passes + false fails. op '<=' : pass iff value <= cut; '>=' : pass iff value >= cut."""
    v = np.asarray(values, float)
    g = np.asarray(good, bool)
    s = np.unique(v)
    cands = list((s[:-1] + s[1:]) / 2) + [s[0] - 1e-9 * max(1, abs(s[0])), s[-1] + 1e-9 * max(1, abs(s[-1]))]
    best = None
    for c in cands:
        pred = v <= c if op in ("<=", "<") else v >= c
        fp = int((pred & ~g).sum())
        ff = int((~pred & g).sum())
        key = (fp + ff, fp, c)
        if best is None or key < best[0]:
            best = (key, c, fp, ff)
    return float(best[1]), best[2], best[3]


def calibrate(ratings_path, report_paths, registry=None, out_dir=None, pass_score=8.0):
    if out_dir and os.path.abspath(out_dir).startswith(os.path.abspath(REGISTRY_DIR)):
        raise InvalidInput([ContractError(out_dir, "--out", "calibrate never writes into the registry directory",
                                          "a directory outside Tools/Metrics/registry", out_dir)])
    rt = load_ratings(ratings_path)
    reps = [load_json_file(p) for p in _reports(report_paths)]
    if not reps:
        raise InvalidInput([ContractError("--reports", "-", "no report.json found", "reports", "none")])
    metrics = {r["metric"] for r in reps}
    if len(metrics) != 1:
        raise InvalidInput([ContractError("--reports", "metric", "reports of one metric at a time", "one metric",
                                          sorted(metrics))])
    metric = metrics.pop()
    reg = load_registry(metric, registry)
    raters = {r["rater_id"]: r for r in rt["raters"] if r["blind"]}
    rows = [x for x in rt["ratings"] if x["rater_id"] in raters]
    anchors = {}
    for x in rows:
        if x.get("anchor"):
            anchors.setdefault((x["anchor"], x["capture"]), []).append(x["score"])
    anchor_med = {k: statistics.median(v) for k, v in anchors.items()}
    pos = [v for (a, _), v in anchor_med.items() if a == "positive"]
    neg = [v for (a, _), v in anchor_med.items() if a == "negative"]
    problems = [f"negative anchor {c} median {v} >= {NEG_ANCHOR_MAX}" for (a, c), v in anchor_med.items()
                if a == "negative" and v >= NEG_ANCHOR_MAX]
    if pos and neg and min(pos) <= max(neg):
        problems.append("positive anchors not rated above negative anchors")
    if problems:
        raise InvalidInput([ContractError(rel_path(ratings_path), "anchors", "misbehaving anchors: " + "; ".join(problems),
                                          "negative <= 3 (< 6), positive >= 9", "see message")])
    by_cap = {}
    per_rater = {}
    for x in rows:
        if x.get("anchor") or x.get("scenario") not in (None, "overall"):
            continue
        by_cap.setdefault(x["capture"], []).append(x["score"])
        per_rater.setdefault(x["rater_id"], {})[x["capture"]] = x["score"]
    rep_by_label = {r["label"]: r for r in reps}
    caps = sorted(c for c in by_cap if c in rep_by_label)
    if len(caps) < MIN_CAPTURES:
        raise InvalidInput([ContractError(rel_path(ratings_path), "ratings", f"calibration needs >= {MIN_CAPTURES} rated "
                                          "captures with reports", f">= {MIN_CAPTURES}", len(caps))])
    score = {c: statistics.median(by_cap[c]) for c in caps}
    good = [score[c] >= pass_score for c in caps]
    agree = []
    for a, b in itertools.combinations(sorted(per_rater), 2):
        common = sorted(set(per_rater[a]) & set(per_rater[b]) & set(caps))
        rho = _spearman([per_rater[a][c] for c in common], [per_rater[b][c] for c in common])
        if rho is not None:
            agree.append({"raters": [a, b], "n": len(common), "spearman": rho})
    sd = {c: (statistics.pstdev(by_cap[c]) if len(by_cap[c]) > 1 else 0.0) for c in caps}
    table, proposals = [], []
    sids = [s for s in reg.ids() if reg.kind(s) in ("gating", "advisory")]
    for c in caps:
        sub = {s["id"]: s for s in rep_by_label[c]["submeasures"]}
        table.append({"capture": c, "rating": score[c], "rating_sd": sd[c],
                      **{s: sub.get(s, {}).get("value") for s in sids}})
    for s in sids:
        thr = reg.sub(s).get("thresholds") or {}
        assumed = [k for k, e in thr.items() if e["status"] == "assumed_pending_calibration"]
        ops = {rep_by_label[c]["submeasures"][[x["id"] for x in rep_by_label[c]["submeasures"]].index(s)].get("op")
               for c in caps if s in [x["id"] for x in rep_by_label[c]["submeasures"]]}
        vals = [row[s] for row in table]
        if not assumed or any(v is None or isinstance(v, bool) for v in vals):
            continue
        op = ops.pop() if len(ops) == 1 else None
        if op not in ("<=", "<", ">=", ">") or len(assumed) != 1:
            proposals.append({"submeasure": s, "key": ",".join(assumed), "note": "band or multi-threshold: review the "
                              "table by hand", "current": {k: thr[k]["value"] for k in assumed}})
            continue
        cut, fp, ff = best_cut(vals, good, op)
        cur = thr[assumed[0]]["value"]
        pred = [(v <= cur) if op in ("<=", "<") else (v >= cur) for v in vals]
        cfp = sum(1 for p, g_ in zip(pred, good) if p and not g_)
        cff = sum(1 for p, g_ in zip(pred, good) if not p and g_)
        proposals.append({"submeasure": s, "key": assumed[0], "op": op, "current": cur, "proposed": cut,
                          "false_pass": fp, "false_fail": ff, "current_false_pass": cfp, "current_false_fail": cff})
    diff_text = registry_diff(reg, [p for p in proposals if "proposed" in p], rel_path(ratings_path))
    res = {"metric": metric, "registry": reg.describe(), "captures": len(caps), "pass_score": pass_score,
           "anchors": {f"{a}:{c}": v for (a, c), v in anchor_med.items()}, "rater_agreement": agree,
           "table": table, "proposals": proposals, "registry_diff": diff_text}
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)
        from .common import dump_json
        with open(os.path.join(out_dir, "calibration.json"), "w") as f:
            f.write(dump_json(res))
        with open(os.path.join(out_dir, "registry_proposal.diff"), "w") as f:
            f.write(diff_text)
        _plot(out_dir, table, proposals, caps)
    return res


def registry_diff(reg, props, ratings_ref="ratings"):
    with open(reg.path, encoding="utf-8") as f:
        old = f.read()
    new = old
    rev = reg.revision
    major, minor = rev[1:].split(".") if "." in rev else (rev[1:], "0")
    nxt = f"v{major}.{int(minor) + 1}"
    new = new.replace(f"revision: {rev}", f"revision: {nxt}", 1)
    lines = new.split("\n")
    cur_sid = None
    for i, ln in enumerate(lines):
        st = ln.strip()
        if st.endswith(":") and st[:-1] in reg.ids():
            cur_sid = st[:-1]
        for p in props:
            if cur_sid == p["submeasure"] and st.startswith(p["key"] + ":") and "value:" in st:
                pre = ln[: ln.index("value:")]
                rest = ln[ln.index("value:"):]
                body = rest.split(",", 1)[1] if "," in rest else "}"
                body = body.replace("assumed_pending_calibration", "calibrated")
                import datetime
                import re as _re
                day = datetime.date.today().isoformat()
                body = _re.sub(r'source: "([^"]*)"',
                               lambda m_: f'source: "calibrated {day} from {ratings_ref} (previously: '
                                          f'{m_.group(1).replace("Assumed", "assumed then")})"', body)
                lines[i] = f"{pre}value: {p['proposed']:.6g},{body}"
    new = "\n".join(lines)
    name = os.path.basename(reg.path)
    tgt = f"{reg.metric}@v{major}.{int(minor) + 1}.yaml"
    return "".join(difflib.unified_diff(old.splitlines(True), new.splitlines(True), fromfile=f"registry/{name}",
                                        tofile=f"registry/{tgt} (proposed; review, then add as a new file)"))


def _plot(out_dir, table, proposals, caps):
    import matplotlib
    matplotlib.use("Agg", force=True)
    import matplotlib.pyplot as plt
    props = [p for p in proposals if "proposed" in p][:6]
    if not props:
        return
    fig, axes = plt.subplots(1, len(props), figsize=(4 * len(props), 3.2), squeeze=False)
    for ax, p in zip(axes[0], props):
        v = [row[p["submeasure"]] for row in table]
        r = [row["rating"] for row in table]
        ax.scatter(v, r, s=18)
        ax.axvline(p["current"], color="#7f8c8d", ls="--", lw=1, label="current")
        ax.axvline(p["proposed"], color="#c0392b", lw=1, label="proposed")
        ax.set_xlabel(p["submeasure"], fontsize=8); ax.set_ylabel("rating", fontsize=8)
        ax.legend(fontsize=6)
    fig.tight_layout()
    fig.savefig(os.path.join(out_dir, "calibration_value_vs_rating.png"), dpi=90, metadata={"Software": None})
    plt.close(fig)


def calibrate_cli(args):
    res = calibrate(args.ratings, args.reports, args.registry, args.out, args.pass_score)
    print(f"calibrate {res['metric']} from registry {res['registry']['revision']} on {res['captures']} captures "
          f"(pass = rating >= {res['pass_score']})")
    ag = [a["spearman"] for a in res["rater_agreement"]]
    print(f"rater agreement: {len(ag)} pairs, mean Spearman {np.mean(ag):.3f}" if ag else "rater agreement: n/a")
    print(f"anchors (median): {res['anchors']}")
    for p in res["proposals"]:
        if "proposed" in p:
            print(f"  {p['submeasure']}.{p['key']}: current {p['current']} (FP {p['current_false_pass']}, FF "
                  f"{p['current_false_fail']}) -> proposed {p['proposed']:.6g} (FP {p['false_pass']}, FF {p['false_fail']})")
        else:
            print(f"  {p['submeasure']}.{p['key']}: {p['note']}")
    print("registry diff (NOT applied; review, then add the new revision file by hand):")
    print(res["registry_diff"] or "  (no tunable sub-measure)")
    return 0
