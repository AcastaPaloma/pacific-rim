"""shots.json (T-191): the shot list of docs/store/capture_plan.md section 2 as data, with a status per shot from what exists now (capture_plan.md says T-147 regenerates it).
Status values: NOW_RUNNABLE (a capture_path@v1 file exists and its requires tags and tile are satisfied), NOW_UNPATHED (the plan says capturable now but no path file defines it yet),
BLOCKED (plan blockers remain, or the path's tile / requires are missing). The raw plan status text is kept as `plan_status`."""
from __future__ import annotations

import glob
import json
import os
import re

ROW_RX = re.compile(r"^\|\s*(P[1-5]-[a-e])\b[^|]*\|(.*)\|\s*$")
SECTION_RX = re.compile(r"^###\s+(P[1-5])\s+(.*)$")
TAG_RX = re.compile(r"\bB-(walk|kaiju|M2|HUD|coop)\b")
PATH_RX = re.compile(r"^p([1-5])_([a-e])_")
TAG_OF = {"walk": "B-walk", "kaiju": "B-kaiju", "m2_streaming": "B-M2", "hud": "B-HUD", "coop": "B-coop", "logotype": "B-logotype", "mech_mesh": "B-mech_mesh"}


def parse_plan(plan_md):
    shots = []
    pillar = None
    for line in open(plan_md, encoding="utf-8"):
        line = line.rstrip("\n")
        m = SECTION_RX.match(line)
        if m:
            pillar = m.group(1)
            continue
        if not line.startswith("| P"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) != 6 or not re.match(r"^P[1-5]-[a-e]", cells[0]):
            continue
        sid = re.match(r"^(P[1-5]-[a-e])", cells[0]).group(1)
        tags = sorted(set("B-" + t for t in TAG_RX.findall(cells[5])))
        shots.append({"id": sid, "pillar": sid[:2], "label": cells[0], "location": cells[1], "path_type": cells[2].strip("`"), "duration": cells[3], "systems": cells[4],
                      "plan_status": cells[5], "plan_blockers": tags, "plan_now": cells[5].upper().startswith("NOW")})
    return shots


def tile_present(geo_root, tile):
    m = re.match(r"^L0_(\d+)_(\d+)$", tile or "")
    return bool(m) and os.path.isdir(os.path.join(geo_root, "%s_%s" % (m.group(1), m.group(2))))


def run(plan_md, paths_dir, geo_root, out_json, extra_dirs=(), captured=None):
    shots = parse_plan(plan_md)
    by_id = {s["id"]: s for s in shots}
    paths, extra = {}, []
    for d in [paths_dir, *extra_dirs]:
        for f in sorted(glob.glob(os.path.join(d, "*.json"))):
            p = json.load(open(f))
            m = PATH_RX.match(p.get("path_id", ""))
            state, tags = "runnable", []
            if p.get("requires"):
                state, tags = "blocked: " + ",".join(p["requires"]), [TAG_OF.get(r, "B-" + r) for r in p["requires"]]
            elif not tile_present(geo_root, p["viewpoint"]["tile"]):
                state, tags = "blocked: tile " + p["viewpoint"]["tile"] + " not in StreamingAssets", ["B-M2"]
            row = {"blockers": tags, "path_id": p["path_id"], "file": os.path.relpath(f, os.path.dirname(os.path.dirname(plan_md))).replace("\\", "/"), "state": state, "mode": p.get("mode"),
                   "purpose": p.get("purpose", "M-003"), "viewpoint": p["viewpoint"]["id"], "tile": p["viewpoint"]["tile"], "profile_id": p["profile_id"],
                   "captured": bool(captured and p["path_id"] in captured)}
            if m and d == paths_dir and "%s%s" % ("P" + m.group(1), "-" + m.group(2)) in by_id:
                paths.setdefault("P%s-%s" % (m.group(1), m.group(2)), []).append(row)
            else:
                extra.append(row)
    for s in shots:
        ps = paths.get(s["id"], [])
        s["paths"] = ps
        runnable = [p for p in ps if p["state"] == "runnable"]
        if runnable:
            s["status"], s["blockers"] = "NOW_RUNNABLE", []
            if s["plan_blockers"]:
                s["note"] = "plan blockers %s are superseded: the path runs on real tiles through the shipping pixel pipeline (T-147)" % ",".join(s["plan_blockers"])
        elif ps:
            s["status"], s["blockers"] = "BLOCKED", sorted({t for p in ps for t in p["blockers"]} | set(s["plan_blockers"]))
        elif s["plan_now"]:
            s["status"], s["blockers"] = "NOW_UNPATHED", []
            s["note"] = "plan: capturable now; no capture_path file defines it yet"
        else:
            s["status"], s["blockers"] = "BLOCKED", s["plan_blockers"]
    tally = {k: sum(1 for s in shots if s["status"] == k) for k in ("NOW_RUNNABLE", "NOW_UNPATHED", "BLOCKED")}
    per_pillar = {}
    for s in shots:
        g = per_pillar.setdefault(s["pillar"], {"NOW_RUNNABLE": 0, "NOW_UNPATHED": 0, "BLOCKED": 0})
        g[s["status"]] += 1
    out = {"schema": "shots@v1", "plan": os.path.relpath(plan_md, os.path.dirname(os.path.dirname(plan_md))).replace("\\", "/"), "shots": shots, "tally": tally,
           "per_pillar": per_pillar, "extra_paths": extra}
    os.makedirs(os.path.dirname(out_json) or ".", exist_ok=True)
    json.dump(out, open(out_json, "w"), indent=1)
    return {"check": "shots", "shots": len(shots), "tally": tally, "per_pillar": per_pillar, "extra_paths": len(extra), "out": out_json, "pass": len(shots) == 22}
