#!/usr/bin/env python
"""Validator for physics_log@v1 (research/R-005 section 8.9). Exit 0 when every file is valid, 2 otherwise; prints one line per error.
Usage: python Tools/PhysicsMetrics/validate_physics_log.py <file or directory> [...]   (kaiju conda env; reads .jsonl or .jsonl.gz)"""
import gzip, json, math, os, sys

HEADER = ["schema", "run_id", "scene", "commit", "unity_version", "fixed_dt", "gravity", "frame_id", "frame_schema", "total_mass_kg", "origin_offset", "engine"]
STEP = ["run_id", "step", "t", "com", "com_vel", "ke_lin", "ke_rot", "pe", "e_total", "p", "L_com", "max_link_speed", "max_joint_speed", "clamps", "nan", "joints", "feet", "power_total_pos", "power_total_abs", "external_work"]
EVENTS = {"footfall", "footfall_loaded", "liftoff", "impact", "fall_start", "fall_end", "clamp"}

def check(path):
    errors = []
    op = gzip.open if path.endswith(".gz") else open
    with op(path, "rt") as f: lines = [l for l in f if l.strip()]
    try: rows = [json.loads(l) for l in lines]
    except ValueError as e: return ["not JSON Lines: %s" % e]
    if not rows: return ["empty file"]
    h = rows[0]
    for k in HEADER:
        if k not in h: errors.append("header: missing %s" % k)
    if h.get("schema") != "physics_log@v1": errors.append("header: schema is %r, expected physics_log@v1" % h.get("schema"))
    eng = h.get("engine", {})
    for k in ("solver_type", "links", "drives"):
        if k not in eng: errors.append("header.engine: missing %s" % k)
    for name, link in eng.get("links", {}).items():
        for k in ("linear_damping", "angular_damping", "max_depenetration", "max_joint_velocity", "sleep_threshold", "contact_offset"):
            if k not in link: errors.append("header.engine.links.%s: missing %s" % (name, k)); break
    dt = h.get("fixed_dt", 0); expect = None; n_steps = 0
    for i, r in enumerate(rows[1:], start=2):
        if "event" in r:
            if r["event"] not in EVENTS: errors.append("line %d: unknown event %r" % (i, r["event"]))
            if "step" not in r: errors.append("line %d: event without step" % i)
            continue
        for k in STEP:
            if k not in r: errors.append("line %d: step record missing %s" % (i, k)); break
        else:
            n_steps += 1
            if expect is not None and r["step"] != expect: errors.append("line %d: step %s, expected %s (gap or repeat)" % (i, r["step"], expect))
            expect = r["step"] + 1
            if abs(r["t"] - r["step"] * dt) > 1e-9: errors.append("line %d: t is not step x fixed_dt" % i)
            if r["run_id"] != h.get("run_id"): errors.append("line %d: run_id differs from the header" % i)
            if r["nan"] or any(v is None or (isinstance(v, float) and math.isnan(v)) for v in (r["e_total"], r["ke_lin"], r["pe"])): errors.append("line %d: NaN in the record" % i)
            if abs(r["e_total"] - (r["ke_lin"] + r["ke_rot"] + r["pe"])) > 1e-6 * max(1.0, abs(r["e_total"])): errors.append("line %d: e_total is not ke_lin + ke_rot + pe" % i)
        if len(errors) > 20: errors.append("... stopped after 20 errors"); break
    if n_steps == 0: errors.append("no step records")
    return errors

def main():
    paths = []
    for a in sys.argv[1:]:
        if os.path.isdir(a): paths += [os.path.join(a, f) for f in sorted(os.listdir(a)) if ".physics.jsonl" in f]
        else: paths.append(a)
    bad = 0
    for p in paths:
        errs = check(p)
        for e in errs: print("%s: %s" % (os.path.basename(p), e))
        bad += 1 if errs else 0
    print("%s: %d input(s), %d invalid [physics_log@v1]" % ("INVALID" if bad else "VALID", len(paths), bad))
    sys.exit(2 if bad else 0)

if __name__ == "__main__": main()
