"""M-015@v2 Floating-origin precision at Bay scale (research/R-101.md section 9, section 6).

Input: `m015_dump@v2` JSON written by the C# FlythroughHarness (Assets/Game/World/Streaming/Host/FlythroughHarness.cs):
  {"format": "m015_dump", "schema": 2, "mode": "logic" | "unity", "label": str,
   "path_length_m", "simulated_s", "step_s", "mech_speed_mps", "steps",
   "bodies": {"mechs", "kaiju", "debris", "total"},
   "max_abs_local_m", "max_reconstruction_err_m", "round_trip_err_m", "probe_max_err_m", "probe_steps",
   "pair_change_max_m", "rebase_pair_change_max_m", "terrain_delta_max_m", "rebase_terrain_delta_max_m",
   "placed_vs_read_max_m", "rebases", "cluster_shifts", "clusters_max",
   "hold_events", "frames_below_surface", "frames_without_ground", "world_origin_mismatches", "unclaimed_chunks",
   "tile_loads", "max_loads_per_key"}
`logic` mode proves the cluster maths on a float32 stand-in world; `unity` mode reads back real Transforms in per-cluster
PhysicsScenes and raycasts the baked MeshColliders. A verdict needs a `unity` dump (otherwise INCOMPLETE: the engine
was not exercised). Every clause is judged on every dump; the worst value over dumps is reported. A missing or non-numeric
field is INVALID_INPUT, never a pass."""
from __future__ import annotations

import math
import os

from ..common import ErrorList, InvalidInput, load_json_file, sha256_file
from ..report import part

NUM = ("path_length_m", "simulated_s", "step_s", "mech_speed_mps", "steps", "max_abs_local_m", "max_reconstruction_err_m",
       "round_trip_err_m", "probe_max_err_m", "pair_change_max_m", "rebase_pair_change_max_m", "terrain_delta_max_m",
       "rebase_terrain_delta_max_m", "rebases", "hold_events", "frames_below_surface", "frames_without_ground",
       "world_origin_mismatches")


def _num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def load_dump(path):
    el = ErrorList(path)
    if not os.path.isfile(path) or os.path.getsize(path) == 0:
        el.add("file", "input file missing or empty", "m015_dump.json", path)
        raise InvalidInput(el.errors)
    try:
        d = load_json_file(path)
    except Exception as e:  # noqa: BLE001
        el.add("file", f"not valid JSON ({e})", "m015_dump.json")
        raise InvalidInput(el.errors)
    if not isinstance(d, dict) or d.get("format") != "m015_dump" or d.get("schema") != 2:
        el.add("envelope", "not m015_dump@v2", 'format "m015_dump", schema 2')
        raise InvalidInput(el.errors)
    if d.get("mode") not in ("logic", "unity"):
        el.add("mode", "must be logic or unity", "logic | unity", str(d.get("mode")))
    for k in NUM:
        if not _num(d.get(k)) or d[k] < 0:
            el.add(k, "missing, not a finite number or negative", ">= 0", str(d.get(k)))
    b = d.get("bodies")
    if not isinstance(b, dict) or not all(_num(b.get(k)) for k in ("mechs", "kaiju", "debris")):
        el.add("bodies", "needs numeric mechs, kaiju, debris", "object", str(b))
    if _num(d.get("steps")) and d["steps"] < 1:
        el.add("steps", "no steps recorded", ">= 1", str(d["steps"]))
    if el:
        raise InvalidInput(el.errors)
    return d


def run(rep, inputs, reg, ratings=None, out_dir=None, images=False, options=None):
    if not inputs:
        el = ErrorList("")
        el.add("arguments", "no input given", ">= 1 m015_dump.json")
        raise InvalidInput(el.errors)
    dumps = []
    for p in inputs:
        dumps.append((p, load_dump(p)))
        rep.add_input(p, sha256_file(p), "m015_dump@v2")
    rep.coverage["required"] = ["m015_dump (mode unity)"]
    rep.coverage["provided"] = sorted(f"{d['label']}:{d['mode']}" for _, d in dumps)
    if not any(d["mode"] == "unity" for _, d in dumps):
        rep.coverage["missing"] = ["a unity-mode dump over the route (the logic mode does not exercise the engine)"]

    def worst(key):
        return max(dumps, key=lambda pd: pd[1][key])

    def val(key):
        return worst(key)[1][key]

    def where(key):
        return worst(key)[1]["label"]

    rep.add("M-015.a", diagnostics={"where": where("max_abs_local_m")},
            parts=[part("max_abs_local_m", val("max_abs_local_m"), "<=", reg.t("M-015.a", "max_abs_local_m"), unit="m")])
    rep.add("M-015.b", diagnostics={"where": where("max_reconstruction_err_m")},
            parts=[part("max_reconstruction_err_m", val("max_reconstruction_err_m"), "<=",
                        reg.t("M-015.b", "max_reconstruction_err_m"), unit="m")])
    rt = max(max(d["round_trip_err_m"], d["probe_max_err_m"]) for _, d in dumps)
    rep.add("M-015.c", diagnostics={"where": max(dumps, key=lambda pd: max(pd[1]["round_trip_err_m"], pd[1]["probe_max_err_m"]))[1]["label"]},
            parts=[part("round_trip_err_m", rt, "<=", reg.t("M-015.c", "round_trip_err_m"), unit="m")])
    pair = max(max(d["pair_change_max_m"], d["rebase_pair_change_max_m"]) for _, d in dumps)
    terr = max(max(d["terrain_delta_max_m"], d["rebase_terrain_delta_max_m"]) for _, d in dumps)
    rep.add("M-015.d", diagnostics={"where": where("pair_change_max_m")},
            parts=[part("pair_change_max_m", pair, "<=", reg.t("M-015.d", "pair_change_max_m"), unit="m"),
                   part("terrain_delta_max_m", terr, "<=", reg.t("M-015.d", "terrain_delta_max_m"), unit="m")])
    rep.add("M-015.e", diagnostics={"where": where("hold_events")},
            parts=[part("hold_events", sum(d["hold_events"] for _, d in dumps), "<=", reg.t("M-015.e", "hold_events_max"), unit="count"),
                   part("frames_below_surface", sum(d["frames_below_surface"] for _, d in dumps), "<=",
                        reg.t("M-015.e", "frames_below_surface_max"), unit="count"),
                   part("frames_without_ground", sum(d["frames_without_ground"] for _, d in dumps), "<=",
                        reg.t("M-015.e", "frames_without_ground_max"), unit="count"),
                   part("world_origin_mismatches", sum(d["world_origin_mismatches"] for _, d in dumps), "<=", 0, unit="count")])
    # scope: every dump must reach the definition (a short route or few bodies cannot satisfy the clause)
    short = []
    for _, d in dumps:
        b = d["bodies"]
        if d["path_length_m"] < reg.p("min_path_m"):
            short.append(f"{d['label']}: path {d['path_length_m']:.0f} m")
        if b["mechs"] < reg.p("min_mechs") or b["kaiju"] < reg.p("min_kaiju") or b["debris"] < reg.p("min_debris"):
            short.append(f"{d['label']}: bodies {b['mechs']} mechs / {b['kaiju']} kaiju / {b['debris']} debris")
    rep.add("M-015.scope", diagnostics={"where": "; ".join(short) or None},
            parts=[part("dumps_below_definition", len(short), "<=", 0, unit="count"),
                   part("rebases_min", min(d["rebases"] for _, d in dumps), ">=", reg.t("M-015.scope", "rebases_min"), unit="count")])
    rep.extra["m015"] = {"dumps": [{"label": d["label"], "mode": d["mode"], "steps": d["steps"], "path_length_m": d["path_length_m"],
                                    "mech_speed_mps": d["mech_speed_mps"], "rebases": d["rebases"]} for _, d in dumps]}
