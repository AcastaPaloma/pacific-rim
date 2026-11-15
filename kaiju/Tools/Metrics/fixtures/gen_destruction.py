"""Deterministic generators for M-050 saves (Core envelope) and M-052 destruction_dump@v1 dumps."""
import copy
import json
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
