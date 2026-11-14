"""T-006 C14: per-sequence M-004 values via kaiju_metrics' own functions (no re-implemented maths).
Usage: /opt/miniconda3/envs/kaiju/bin/python evidence/T-006/tools/m004_per_sequence.py
Not the full M-004 gate (needs 3 profiles x 2 viewpoints x S0,T1,T2,T3,Y1,Y2).
Fixed inputs: sidecar speed_internal_px_per_frame is 0 for scripted sequences, so the speed is taken from the
library's own measured_speed() (sidecar poses, ref depth 600 m) and passed in a copied motion dict; the class is the
nearest nominal of the sequence type (the library's +-10 % class tolerance is NOT met, reported as such)."""
import copy, json, math, os, subprocess, sys
ROOT = os.getcwd()
sys.path.insert(0, os.path.join(ROOT, "Tools/Metrics"))
from kaiju_metrics.contracts.palette_set import load_palette_set
from kaiju_metrics.registry import Registry
from kaiju_metrics.style.m003 import load_sets
from kaiju_metrics.style import m004

REG = os.path.join(ROOT, "Tools/Metrics/registry/M-004@v1.1.yaml")
reg = Registry(REG)
pal = load_palette_set(None)
GROUP = reg.get("threshold_group")
PLAN = {"hero_static": ("static", "S0"), "hero_pushin": ("translate", None), "hero_orbit": ("yaw", None)}
out = {"tool_commit": subprocess.check_output(["git", "rev-parse", "--short", "HEAD"], text=True).strip(),
       "registry": "M-004@v1.1", "registry_sha256": reg.sha256, "modes": {}}
for mode in ("direction", "hybrid", "screen"):
    sets = load_sets([f"evidence/T-006/captures_local/motion_{mode}"], pal)
    res = {}
    for sq in sets[0].sequences:
        typ, cls = PLAN[sq.id]
        tr, yw = m004.measured_speed(sq, typ, reg.p("ref_depth_m"))
        sp = {"static": 0.0, "translate": tr, "yaw": yw}[typ]
        note = None
        if cls is None:
            cands = {k: c["speed"] for k, c in reg.get("classes").items() if c["type"] == typ}
            cls = min(cands, key=lambda k: abs(cands[k] - sp))
            rel = abs(cands[cls] - sp) / cands[cls]
            note = f"measured {sp:.4f} px/frame; nearest nominal {cls} ({cands[cls]}), off by {rel:.0%} (> 10 % class tolerance); threshold group {GROUP[cls]}"
        s2 = copy.copy(sq); s2.meta = dict(sq.meta, motion=dict(sq.motion, speed_internal_px_per_frame=sp))
        r = m004.sequence_metrics(s2, pal.profiles[sq.profile_id], reg, cls)
        grp = GROUP[cls]
        ids = dict(zip(("S", "C", "F"), m004.GROUP_IDS[grp]))
        subs = {}
        for k, sid in ids.items():
            th = reg.get("submeasures")[sid]["thresholds"]["max"]
            v = r[k]
            subs[sid] = {"value": v, "threshold_max": th["value"], "threshold_status": th["status"],
                         "result": "NOT_MEASURED" if v is None or (isinstance(v, float) and math.isnan(v))
                         else ("pass" if v <= th["value"] else "MISS")}
        res[sq.id] = {"class": cls, "group": grp, "lag": r["lag"], "frames": r["frames"], "speed_px_per_frame": sp,
                      "note": note, "interleaved_px": r["interleaved_px"], "valid_share": r["valid_share"],
                      "S_pair_max": r["S_pair_max"], "submeasures": subs, "profile": sq.profile_id, "viewpoint": sq.viewpoint}
    out["modes"][mode] = res
os.makedirs("evidence/T-006/m004", exist_ok=True)
json.dump(out, open("evidence/T-006/m004/per_sequence.json", "w"), indent=1)
print(json.dumps({m: {s: {k: (v["value"], v["threshold_max"], v["result"]) for k, v in d["submeasures"].items()} for s, d in r.items()} for m, r in out["modes"].items()}, indent=1))
