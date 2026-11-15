"""M-051@v1 Damage state versus fragility (research/R-021.md §2.4, §4 M-051; R-026 C-3).

Input: `fragility_dump@v1` JSON (the exporter writes it; this docstring is the schema).
  {"format": "fragility_dump", "schema": 1, "n": <buildings per grid point, >= 1000>,
   "classes": [ {"class": "A"|"B"|"C", "hazus_type": "W1"|"C2.L"|"C2.M"|"C2.H",
       "H": <m>, "T": <s>, "theta_k": [4 medians loaded by the model], "beta": 0.4, "beta_tot": 0.64,
       "e_k_j": [4 energies E_1..E_4], "beta_e": 0.4,
       "pga_points":    [ {"pga_g": g, "counts": [n_ge1, n_ge2, n_ge3, n_ge4]}, ... ],   # fresh run per point
       "energy_points": [ {"e_del_j": J, "counts": [...]}, ... ],
       "state_decreases": <int, buildings whose state fell when intensity rose>,
       "repeat_identical": <bool, two runs of the same grid bit-equal> } ] }
PGA route prediction (R-026 C-3, the chain, not raw theta_k):
  Sa = PGA g min(2.5, 1.5/T);  Sd = Sa T^2 / 4 pi^2;  theta = Sd / (0.75 H);  P_k = Phi(ln(theta / theta_k) / beta_tot).
Energy route: P_k = Phi(ln(E / E_k) / beta_e).
Every file is validated before any verdict; empty or malformed input is INVALID_INPUT, a dump with no points is
never a pass."""
from __future__ import annotations

import csv
import math
import os

from ..common import REGISTRY_DIR, ErrorList, InvalidInput, load_json_file, sha256_file
from ..report import part

G = 9.80665
HAZUS_CSV = os.path.join(REGISTRY_DIR, "hazus_rows_mc.csv")


def phi(x):
    return 0.5 * (1.0 + math.erf(x / math.sqrt(2.0)))


def pga_prob(pga_g, T, H, theta_k, beta_tot):
    sa = pga_g * G * min(2.5, 1.5 / T)
    sd = sa * T * T / (4.0 * math.pi ** 2)
    theta = sd / (0.75 * H)
    return phi(math.log(theta / theta_k) / beta_tot)


def _pos(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v) and v > 0


def _counts(c, n):
    return (isinstance(c, list) and len(c) == 4 and all(isinstance(x, int) and not isinstance(x, bool)
            and 0 <= x <= n for x in c))


def load_hazus(path=HAZUS_CSV):
    rows = {}
    with open(path, encoding="utf-8") as f:
        for r in csv.DictReader(f):
            rows.setdefault(r["hazus_type"], {})[int(r["k"])] = (float(r["theta_k"]), float(r["beta"]))
    return rows


def load_dump(path):
    el = ErrorList(path)
    if not os.path.isfile(path) or os.path.getsize(path) == 0:
        el.add("file", "input file missing or empty", "fragility_dump.json", path)
        raise InvalidInput(el.errors)
    try:
        d = load_json_file(path)
    except Exception as e:  # noqa: BLE001
        el.add("file", f"not valid JSON ({e})", "fragility_dump.json")
        raise InvalidInput(el.errors)
    if not isinstance(d, dict) or d.get("format") != "fragility_dump" or d.get("schema") != 1:
        el.add("envelope", "not fragility_dump@v1", 'format "fragility_dump", schema 1')
        raise InvalidInput(el.errors)
    n = d.get("n")
    if not isinstance(n, int) or isinstance(n, bool) or n < 1000:
        el.add("n", "missing or < 1000 (binomial tolerance assumes N = 10,000)", ">= 1000", str(n))
        raise InvalidInput(el.errors)
    cl = d.get("classes")
    if not isinstance(cl, list) or not cl:
        el.add("classes", "non-empty list required", ">= 1 class")
        raise InvalidInput(el.errors)
    for i, c in enumerate(cl):
        w = f"classes[{i}]"
        if not isinstance(c, dict):
            el.add(w, "must be an object")
            continue
        for k in ("H", "T", "beta", "beta_tot", "beta_e"):
            if not _pos(c.get(k)):
                el.add(f"{w}.{k}", "missing or not a positive finite number", "> 0", str(c.get(k)))
        for k in ("theta_k", "e_k_j"):
            v = c.get(k)
            if not (isinstance(v, list) and len(v) == 4 and all(_pos(x) for x in v)) or \
                    (isinstance(v, list) and v != sorted(v)):
                el.add(f"{w}.{k}", "need 4 positive ascending numbers", "4 values", str(v))
        if not isinstance(c.get("hazus_type"), str):
            el.add(f"{w}.hazus_type", "missing", "string")
        for key, xk in (("pga_points", "pga_g"), ("energy_points", "e_del_j")):
            pts = c.get(key)
            if not isinstance(pts, list) or not pts:
                el.add(f"{w}.{key}", "non-empty list required", ">= 1 point")
                continue
            for j, p in enumerate(pts):
                if not isinstance(p, dict) or not _pos(p.get(xk)) or not _counts(p.get("counts"), n):
                    el.add(f"{w}.{key}[{j}]", f"needs {xk} > 0 and 4 integer counts in 0..n", "object")
        sd = c.get("state_decreases")
        if not isinstance(sd, int) or isinstance(sd, bool) or sd < 0:
            el.add(f"{w}.state_decreases", "missing or not an integer >= 0", ">= 0", str(sd))
        if not isinstance(c.get("repeat_identical"), bool):
            el.add(f"{w}.repeat_identical", "missing or not a bool", "bool")
    if el:
        raise InvalidInput(el.errors)
    return d


def run(rep, inputs, reg, ratings=None, out_dir=None, images=False, options=None):
    if not inputs:
        el = ErrorList("")
        el.add("arguments", "no input given", ">= 1 fragility_dump.json")
        raise InvalidInput(el.errors)
    dumps = []
    for p in inputs:
        dumps.append(load_dump(p))
        rep.add_input(p, sha256_file(p), "fragility_dump@v1")
    rep.add_input(HAZUS_CSV, sha256_file(HAZUS_CSV), "hazus_rows_mc.csv")
    rep.coverage["required"] = ["fragility_dump"]
    rep.coverage["provided"] = ["fragility_dump"]
    hz = load_hazus()
    win = reg.t("M-051.pga", "median_window")
    pga_dev, pga_med, en_dev, mono, table = {}, {}, {}, 0, {}
    mono_where, n_pga, n_en = [], 0, 0
    for di, d in enumerate(dumps):
        n = d["n"]
        for c in d["classes"]:
            tag = f"{di}:{c['class']}" if len(dumps) > 1 else c["class"]
            rows = hz.get(c["hazus_type"])
            for k in range(4):
                key = f"{tag}.DS{k + 1}"
                if rows is None or (k + 1) not in rows:
                    table[key] = math.inf
                else:
                    table[key] = max(abs(c["theta_k"][k] - rows[k + 1][0]), abs(c["beta"] - rows[k + 1][1]))
            prev = None
            for p in sorted(c["pga_points"], key=lambda q: q["pga_g"]):
                n_pga += 1
                f = [x / n for x in p["counts"]]
                if any(f[i] < f[i + 1] for i in range(3)):
                    mono += 1
                    mono_where.append(f"{tag} pga {p['pga_g']}: F_k not non-increasing in k")
                if prev is not None and any(a > b for a, b in zip(prev, p["counts"])):
                    mono += 1
                    mono_where.append(f"{tag} pga {p['pga_g']}: counts fell as PGA rose")
                prev = p["counts"]
                for k in range(4):
                    pr = pga_prob(p["pga_g"], c["T"], c["H"], c["theta_k"][k], c["beta_tot"])
                    dev = abs(f[k] - pr)
                    key = f"{tag}.DS{k + 1}@{p['pga_g']}g"
                    pga_dev[key] = dev
                    if abs(pr - 0.5) <= win:
                        pga_med[key] = abs(f[k] - 0.5) if abs(pr - 0.5) < 1e-12 else dev
            for p in sorted(c["energy_points"], key=lambda q: q["e_del_j"]):
                n_en += 1
                f = [x / n for x in p["counts"]]
                if any(f[i] < f[i + 1] for i in range(3)):
                    mono += 1
                    mono_where.append(f"{tag} E {p['e_del_j']:.3g}: F_k not non-increasing in k")
                for k in range(4):
                    pr = phi(math.log(p["e_del_j"] / c["e_k_j"][k]) / c["beta_e"])
                    en_dev[f"{tag}.DS{k + 1}@{p['e_del_j']:.3g}J"] = abs(f[k] - pr)
            mono += c["state_decreases"] + (0 if c["repeat_identical"] else 1)
            if c["state_decreases"]:
                mono_where.append(f"{tag}: {c['state_decreases']} state decreases")
            if not c["repeat_identical"]:
                mono_where.append(f"{tag}: two runs not identical")
    worst = lambda m: max(m, key=lambda k: m[k]) if m else None          # noqa: E731
    w = worst(pga_dev)
    wm = worst(pga_med)
    rep.add("M-051.pga", diagnostics={"where": w, "median_where": wm, "points": n_pga},
            parts=[part("max_abs_dev", pga_dev[w], "<=", reg.t("M-051.pga", "max_abs_dev"), unit="probability"),
                   part("median_dev", pga_med[wm] if wm else None, "<=", reg.t("M-051.pga", "median_dev"),
                        unit="probability")],
            note=None if wm else "no grid point within the median window of 0.5: median not measured")
    w = worst(en_dev)
    rep.add("M-051.energy", diagnostics={"where": w, "points": n_en},
            parts=[part("max_abs_dev", en_dev[w], "<=", reg.t("M-051.energy", "max_abs_dev"), unit="probability")])
    rep.add("M-051.monotone", diagnostics={"where": "; ".join(mono_where[:5]) or None},
            parts=[part("violations_max", mono, "<=", reg.t("M-051.monotone", "violations_max"), unit="count")])
    w = worst(table)
    rep.add("M-051.table", diagnostics={"where": w},
            parts=[part("table_abs_tol", table[w], "<=", reg.t("M-051.table", "table_abs_tol"), unit="absolute")])
    rep.extra["m051"] = {"classes": sorted({c["class"] for d in dumps for c in d["classes"]}),
                         "pga_points": n_pga, "energy_points": n_en}
