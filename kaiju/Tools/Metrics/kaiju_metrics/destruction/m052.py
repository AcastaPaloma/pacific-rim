"""M-052@v1 Collapse mass and energy account (research/R-021.md §3, §4; R-026 §1, §3).

Input: one or more `destruction_dump.json` files (contract `destruction_dump@v1`, written by the C# exporter,
which does not exist yet; this docstring is the schema).

destruction_dump@v1
  {"format": "destruction_dump", "schema": 1, "model_rev": <int>,
   "buildings": [ {                      # one per building, any state (non-empty list required)
       "id": "bld:<osm-way|osm-relation|landmark>:<u64>",
       "class": "A" | "B" | "C" | <archetype / Hazus class string>,
       "H": <m, > 0>, "T": <s, > 0>,
       "state": 0..4, "e_cum_j": <J, >= 0>, "debris_kg": <kg, >= 0>,   # same values as destruction@v1, decimal
       "m0_kg": <building mass M0, > 0>, "m_standing_kg": <kg, >= 0>,
       "piece_count": <int >= 0>, "piece_mass_sum_kg": <kg>,           # debris pieces spawned for this building
       "energy": { "dpe_j", "w_ext_j", "e_frac_j", "e_ground_j", "ke_ej_j" }   # J; REQUIRED when state >= 3
     }, ...],
   "transition_checks": [ {            # two-step (1->3->4) vs direct (1->4), one per archetype; REQUIRED
       "archetype": <string>,
       "direct":   {"debris_kg", "e_frac_j", "dpe_j"},
       "two_step": {"debris_kg", "e_frac_j", "dpe_j"} }, ... ] }
  `E_res = dpe_j + w_ext_j - (e_frac_j + e_ground_j + ke_ej_j)` is computed by the tool (R-021 §3 identity).

Rules checked (thresholds in registry/M-052@v1.yaml):
  mass:    |M0 - (M_standing + debris)| / M0 <= 1e-6 and debris_kg <= M0 (all buildings)
  pieces:  |sum(pieces) - debris| / debris <= 1e-6 and piece_count <= 64 (all buildings with debris > 0)
  energy (state >= 3): 0 <= E_res / dPE <= 0.05; KE_ej / dPE <= 0.10; E_frac / dPE <= 0.25 (T-024 bound, not in R-021);
           no energy creation: E_frac + E_ground + KE_ej <= dPE + W_ext
  path:    two-step vs direct totals equal within 1e-6 relative (debris_kg, e_frac_j, dpe_j)
Worst value over all buildings is reported; the first offending IDs go into `where`. Empty or missing input, an
empty building list or a missing transition_checks list is an error, never a pass (DEF-T002-arch-1)."""
from __future__ import annotations

import math
import os

from ..common import ErrorList, InvalidInput, load_json_file, sha256_file
from ..report import part

ENERGY_KEYS = ("dpe_j", "w_ext_j", "e_frac_j", "e_ground_j", "ke_ej_j")
PATH_KEYS = ("debris_kg", "e_frac_j", "dpe_j")


def _num(v, nonneg=True, positive=False):
    if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v):
        return False
    return v > 0 if positive else (v >= 0 if nonneg else True)


def load_dump(path):
    el = ErrorList(path)
    if not os.path.isfile(path):
        el.add("file", "input file does not exist", "destruction_dump.json", path)
        raise InvalidInput(el.errors)
    if os.path.getsize(path) == 0:
        el.add("file", "input file is empty", "destruction_dump.json", "0 bytes")
        raise InvalidInput(el.errors)
    try:
        d = load_json_file(path)
    except Exception as e:  # noqa: BLE001
        el.add("file", f"not valid JSON ({e})", "destruction_dump.json")
        raise InvalidInput(el.errors)
    if not isinstance(d, dict) or d.get("format") != "destruction_dump" or d.get("schema") != 1:
        el.add("envelope", "not destruction_dump@v1", 'format "destruction_dump", schema 1')
        raise InvalidInput(el.errors)
    bl = d.get("buildings")
    if not isinstance(bl, list) or not bl:
        el.add("buildings", "buildings must be a non-empty list (an empty dump proves nothing)", ">= 1 building",
               str(len(bl)) if isinstance(bl, list) else str(type(bl).__name__))
        raise InvalidInput(el.errors)
    seen = set()
    for i, b in enumerate(bl):
        if not isinstance(b, dict):
            el.add(f"buildings[{i}]", "building must be an object")
            continue
        w = f"buildings[{i}]({b.get('id')})"
        if not isinstance(b.get("id"), str) or b["id"] in seen:
            el.add(w, "id missing or duplicate", "unique string")
        seen.add(b.get("id"))
        if not isinstance(b.get("class"), str) or not b.get("class"):
            el.add(w + ".class", "missing", "string")
        for k in ("H", "T", "m0_kg"):
            if not _num(b.get(k), positive=True):
                el.add(f"{w}.{k}", "missing or not a positive finite number", "> 0", str(b.get(k)))
        for k in ("e_cum_j", "debris_kg", "m_standing_kg", "piece_mass_sum_kg"):
            if not _num(b.get(k)):
                el.add(f"{w}.{k}", "missing or not a finite number >= 0", ">= 0", str(b.get(k)))
        if not isinstance(b.get("state"), int) or isinstance(b.get("state"), bool) or not 0 <= b["state"] <= 4:
            el.add(f"{w}.state", "missing or not an integer 0..4", "0..4", str(b.get("state")))
        if not isinstance(b.get("piece_count"), int) or isinstance(b.get("piece_count"), bool) or b["piece_count"] < 0:
            el.add(f"{w}.piece_count", "missing or not an integer >= 0", ">= 0", str(b.get("piece_count")))
        if isinstance(b.get("state"), int) and b["state"] >= 3:
            e = b.get("energy")
            if not isinstance(e, dict):
                el.add(f"{w}.energy", "required when state >= 3", "object " + str(ENERGY_KEYS))
            else:
                for k in ENERGY_KEYS:
                    if not _num(e.get(k)):
                        el.add(f"{w}.energy.{k}", "missing or not a finite number >= 0", ">= 0", str(e.get(k)))
                if _num(e.get("dpe_j")) and e["dpe_j"] <= 0:
                    el.add(f"{w}.energy.dpe_j", "must be > 0 for a collapse", "> 0", str(e["dpe_j"]))
    tc = d.get("transition_checks")
    if not isinstance(tc, list) or not tc:
        el.add("transition_checks", "required non-empty list (two-step vs direct per archetype)", ">= 1 entry")
    else:
        for i, t in enumerate(tc):
            for side in ("direct", "two_step"):
                s = t.get(side) if isinstance(t, dict) else None
                if not isinstance(s, dict) or not all(_num(s.get(k)) for k in PATH_KEYS):
                    el.add(f"transition_checks[{i}].{side}", "needs finite numbers " + str(PATH_KEYS), "object")
    if el:
        raise InvalidInput(el.errors)
    return d


def _rel(a, b):
    return abs(a - b) / max(abs(a), abs(b), 1e-300)


def _first(items, n=5):
    return ", ".join(items[:n]) if items else None


def evaluate(d):
    mass, pieces, energy_res, ke, frac, create, path = {}, {}, {}, {}, {}, {}, {}
    over_m0 = []
    for b in d["buildings"]:
        i = b["id"]
        mass[i] = abs(b["m0_kg"] - (b["m_standing_kg"] + b["debris_kg"])) / b["m0_kg"]
        if b["debris_kg"] > b["m0_kg"] * (1 + 1e-9):
            over_m0.append(i)
        if b["debris_kg"] > 0 or b["piece_mass_sum_kg"] > 0 or b["piece_count"] > 0:
            pieces[i] = (_rel(b["piece_mass_sum_kg"], b["debris_kg"]), b["piece_count"])
        if b["state"] >= 3:
            e = b["energy"]
            dpe = e["dpe_j"]
            res = e["dpe_j"] + e["w_ext_j"] - (e["e_frac_j"] + e["e_ground_j"] + e["ke_ej_j"])
            energy_res[i] = res / dpe
            ke[i] = e["ke_ej_j"] / dpe
            frac[i] = e["e_frac_j"] / dpe
            create[i] = (e["e_frac_j"] + e["e_ground_j"] + e["ke_ej_j"]) - (dpe + e["w_ext_j"])
    for j, t in enumerate(d["transition_checks"]):
        path[t.get("archetype", f"#{j}")] = max(_rel(t["direct"][k], t["two_step"][k]) for k in PATH_KEYS)
    return mass, over_m0, pieces, energy_res, ke, frac, create, path


def run(rep, inputs, reg, ratings=None, out_dir=None, images=False, options=None):
    if not inputs:
        el = ErrorList("")
        el.add("arguments", "no input given", ">= 1 destruction_dump.json")
        raise InvalidInput(el.errors)
    dumps = []
    for p in inputs:
        dumps.append(load_dump(p))                 # every file validated before any verdict
        rep.add_input(p, sha256_file(p), "destruction_dump@v1")
    rep.coverage["required"] = ["destruction_dump"]
    rep.coverage["provided"] = ["destruction_dump"]
    agg = [evaluate(d) for d in dumps]

    def merged(ix):
        out = {}
        for n, a in enumerate(agg):
            for k, v in a[ix].items():
                out[k if len(agg) == 1 else f"{n}:{k}"] = v
        return out

    cols = [merged(i) for i in range(len(agg[0])) if not isinstance(agg[0][i], list)]
    mass, pieces, eres, ke, frac, create, path = cols
    over = [x for a in agg for x in a[1]]
    worst = lambda m: max(m, key=lambda k: m[k]) if m else None          # noqa: E731
    low = lambda m: min(m, key=lambda k: m[k]) if m else None            # noqa: E731
    wm = worst(mass)
    rep.add("M-052.mass", diagnostics={"where": f"worst {wm}" if wm else None, "debris_over_m0_ids": over[:10]},
            parts=[part("mass_resid_rel", mass[wm], "<=", reg.t("M-052.mass", "mass_resid_rel"), unit="relative"),
                   part("debris_over_m0", len(over), "<=", reg.t("M-052.mass", "debris_over_m0_max"), unit="ids")],
            note=None if not over else f"debris > M0: {_first(over)}")
    pr = {k: v[0] for k, v in pieces.items()}
    pc = {k: float(v[1]) for k, v in pieces.items()}
    wp, wc = worst(pr), worst(pc)
    t_sum, t_n = reg.t("M-052.pieces", "piece_sum_rel"), reg.t("M-052.pieces", "pieces_max")
    rep.add("M-052.pieces", diagnostics={"where": f"worst {wp} / {wc}" if wp else None, "buildings_with_debris": len(pr)},
            parts=[part("piece_sum_rel", pr[wp] if wp else None, "<=", t_sum, unit="relative"),
                   part("pieces_max", pc[wc] if wc else None, "<=", t_n, unit="pieces")])
    lo, hi_k, hi_f = low(eres), worst(ke), worst(frac)
    n_collapse = len(eres)
    where = None
    if lo:
        where = f"min E_res/dPE {lo}; max KE_ej/dPE {hi_k}; max E_frac/dPE {hi_f}"
    if n_collapse:
        e_lo = reg.t("M-052.energy", "e_res_over_dpe_min")
        e_hi = reg.t("M-052.energy", "e_res_over_dpe_max")
        parts = [part("e_res_over_dpe_min", eres[lo], ">=", e_lo, unit="ratio"),
                 part("e_res_over_dpe_max", eres[worst(eres)], "<=", e_hi, unit="ratio"),
                 part("ke_ej_over_dpe_max", ke[hi_k], "<=", reg.t("M-052.energy", "ke_ej_over_dpe_max"), unit="ratio"),
                 part("e_frac_over_dpe_max", frac[hi_f], "<=", reg.t("M-052.energy", "e_frac_over_dpe_max"),
                      unit="ratio"),
                 part("energy_created_j_max", max(create.values()), "<=", reg.t("M-052.energy", "energy_created_j_max"),
                      unit="J")]
        rep.add("M-052.energy", parts=parts, diagnostics={"where": where, "collapsed_buildings": n_collapse})
    else:
        rep.add("M-052.energy", status="NOT_RUN", note="no building with state >= 3 in the dump: collapse account "
                "not measured", diagnostics={"collapsed_buildings": 0})
    wpath = worst(path)
    rep.add("M-052.path", diagnostics={"where": f"worst {wpath}", "archetypes": sorted(path)},
            parts=[part("two_step_vs_direct_rel", path[wpath], "<=", reg.t("M-052.path", "two_step_vs_direct_rel"),
                        unit="relative")])
    rep.extra["m052"] = {"buildings": sum(len(d["buildings"]) for d in dumps), "collapsed": n_collapse,
                         "archetypes": sorted(path)}
