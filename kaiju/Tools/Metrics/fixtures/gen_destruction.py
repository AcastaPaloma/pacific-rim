"""Deterministic generators for M-050 saves (Core envelope) and M-052 destruction_dump@v1 dumps."""
import copy
import json
import math
import os
import random
import struct


def f64hex(x):
    return struct.pack(">d", x).hex().upper()


def make_save(n=50, seed=1, other=True):
    rng = random.Random(seed)
    recs = [{"key": "_meta", "fields": {"model_rev": 2}}]
    for i in sorted(rng.sample(range(10_000), n)):
        recs.append({"key": f"bld:osm-way:{i}", "fields": {
            "cause": rng.randrange(4), "debris_kg": {"f64": f64hex(rng.uniform(1e3, 1e6))},
            "e_cum_j": {"f64": f64hex(rng.uniform(1e5, 1e9))}, "last_step": rng.randrange(1000),
            "state": rng.randrange(1, 5)}})
    secs = [{"name": "destruction", "version": 1, "records": recs}]
    if other:
        secs.append({"name": "world", "version": 1, "records": [{"key": "clock", "fields": {"step": 77}}]})
    return {"format": "kaiju-save", "schema": 1, "build": "test", "sections": secs}


def write_json(path, obj):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, separators=(",", ":"))
    return path


def make_dump():
    g = 9.81
    b = []
    for i, (cls, h, m0, st) in enumerate([("A", 8.0, 2.0e5, 3), ("B", 25.0, 2.1e6, 4), ("C", 120.0, 3.0e7, 3),
                                          ("A", 9.0, 2.2e5, 1), ("B", 30.0, 2.5e6, 0)]):
        fd = {0: 0.0, 1: 0.01, 3: 0.25, 4: 0.3, }[st] if st in (0, 1, 3, 4) else 0.0
        deb = fd * m0
        d = {"id": f"bld:osm-way:{100 + i}", "class": cls, "H": h, "T": 0.1 * h ** 0.75 * 0.5, "state": st,
             "e_cum_j": 1.0e6 * (st + 1), "debris_kg": deb, "m0_kg": m0, "m_standing_kg": m0 - deb,
             "piece_count": 0 if deb == 0 else 24, "piece_mass_sum_kg": deb}
        if st >= 3:
            dpe = deb * g * (h / 2 - 2.0)
            d["energy"] = {"dpe_j": dpe, "w_ext_j": 1.0e6, "e_frac_j": 0.10 * dpe,
                           "e_ground_j": 0.0, "ke_ej_j": 0.05 * dpe}
            d["energy"]["e_ground_j"] = dpe + 1.0e6 - d["energy"]["e_frac_j"] - d["energy"]["ke_ej_j"] - 0.02 * dpe
        b.append(d)
    tc = [{"archetype": a, "direct": {"debris_kg": 1.0e5, "e_frac_j": 5.0e7, "dpe_j": 2.0e8},
           "two_step": {"debris_kg": 1.0e5, "e_frac_j": 5.0e7, "dpe_j": 2.0e8}} for a in ("house", "mid-rise")]
    return {"format": "destruction_dump", "schema": 1, "model_rev": 2, "buildings": b, "transition_checks": tc}


# ------------------------------------------------------------------ M-051 / M-053 / M-054 dumps
def make_fragility_dump(n=10_000, seed=3):
    """Counts are round(n * Phi) plus a deterministic +-0.003 n wobble, so the pass fixture is inside 0.015."""
    from kaiju_metrics.destruction import m051
    rng = random.Random(seed)
    theta = {"W1": [0.004, 0.0099, 0.0306, 0.075], "C2.M": [0.0027, 0.0056, 0.0154, 0.04],
             "C2.H": [0.002, 0.0042, 0.0116, 0.03]}
    classes = []
    for cls, ty, h, t, in (("A", "W1", 7.0, 0.21), ("B", "C2.M", 25.0, 0.55), ("C", "C2.H", 160.0, 2.2)):
        th = theta[ty]
        ek = [1e4 * 3 ** k for k in range(4)]
        pp, ep = [], []
        for pga in (0.05, 0.1, 0.2, 0.4, 0.8, 1.6):
            pr = [m051.pga_prob(pga, t, h, th[k], 0.64) for k in range(4)]
            pp.append({"pga_g": pga, "counts": _mono([round(n * (p + rng.uniform(-0.003, 0.003))) for p in pr], n)})
        for i in range(13):
            e = 0.1 * ek[0] * (10 * ek[3] / (0.1 * ek[0])) ** (i / 12)
            pr = [m051.phi(math.log(e / ek[k]) / 0.4) for k in range(4)]
            ep.append({"e_del_j": e, "counts": _mono([round(n * (p + rng.uniform(-0.003, 0.003))) for p in pr], n)})
        for pts in (pp, ep):                # state never decreases as intensity rises
            for a, b in zip(pts, pts[1:]):
                b["counts"] = [max(x, y) for x, y in zip(a["counts"], b["counts"])]
        classes.append({"class": cls, "hazus_type": ty, "H": h, "T": t, "theta_k": th, "beta": 0.4, "beta_tot": 0.64,
                        "e_k_j": ek, "beta_e": 0.4, "pga_points": pp, "energy_points": ep,
                        "state_decreases": 0, "repeat_identical": True})
    return {"format": "fragility_dump", "schema": 1, "n": n, "classes": classes}


def _mono(c, n):
    c = [min(n, max(0, x)) for x in c]
    for i in range(1, 4):
        c[i] = min(c[i], c[i - 1])
    return c


def make_rupture_dump(faults_path):
    """A rupture that follows the first baked LineString feature with >= 4 vertices exactly."""
    with open(faults_path, encoding="utf-8") as f:
        gj = json.load(f)
    ft = next(x for x in gj["features"] if x["geometry"]["type"] == "LineString" and len(x["geometry"]["coordinates"]) >= 4
              and x["properties"].get("section"))
    pl = ft["geometry"]["coordinates"]
    pr = ft["properties"]
    return {"format": "fault_rupture_dump", "schema": 1, "ruptures": [{
        "event_id": 1, "fault_id": str(pr["fault_id"]), "section": pr["section"], "vertex_start": 0,
        "vertex_end": len(pl) - 1, "polyline": pl,
        "slip": [{"applied_m": 1.5, "expected_m": 1.5}, {"applied_m": 2.4, "expected_m": 2.4}]}]}


def make_wc_dump():
    """Mw 7.2 default scenario (WC94 medians) plus the 1906 anchor (warn-free) fixtures."""
    mw = 7.2
    srl = 10 ** ((mw - 5.16) / 1.12)
    return {"format": "rupture_dump", "schema": 1, "ruptures": [
        {"name": "M1 default", "mw": mw, "srl_km": srl, "md_m": 10 ** (-7.03 + 1.03 * mw),
         "ad_m": 10 ** (-6.32 + 0.90 * mw), "trace_length_km": 110.0}]}
