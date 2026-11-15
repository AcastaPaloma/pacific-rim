"""M-004 over the motion sets of the T-147 driver (T-191): every `<captures>/<path_id>/<viewpoint>_<class>/sequence.json` of purpose M-004 is one sequence; the metric
needs complete S0,T1,T2,T3,Y1,Y2 classes per (profile, viewpoint) on >= 3 profiles x 2 viewpoints, so all sets are handed to one M-004 run together. The gates are the
registry's and are not changed here; the verdict is reported as is."""
from __future__ import annotations

import glob
import json
import os
import subprocess
import sys

from ._metrics import METRICS_PY
from .style import VERDICT


def motion_sets(root):
    """Set directories under root holding at least one M-004 sequence."""
    out = []
    for d in sorted(glob.glob(os.path.join(root, "*"))):
        seqs = [json.load(open(p)) for p in glob.glob(os.path.join(d, "*", "sequence.json"))]
        if any(s.get("purpose") == "M-004" for s in seqs):
            out.append(d)
    return out


def run(root, out_dir, jobs=4, extra=()):
    sets = motion_sets(root)
    if not sets:
        return {"check": "M-004", "pass": False, "reason": f"no M-004 sets under {root}"}
    os.makedirs(out_dir, exist_ok=True)
    cmd = [sys.executable, METRICS_PY, "m004", *sets, "--out", out_dir, "--no-images", "--quiet", "--jobs", str(jobs), *extra]
    p = subprocess.run(cmd, capture_output=True, text=True)
    rep = os.path.join(out_dir, "report.json")
    report = json.load(open(rep)) if os.path.exists(rep) else None
    seqs = len(glob.glob(os.path.join(root, "*", "*", "sequence.json")))
    return {"check": "M-004", "sets": [os.path.basename(s) for s in sets], "sequences": sum(len(glob.glob(os.path.join(s, "*", "sequence.json"))) for s in sets),
            "exit": p.returncode, "verdict": VERDICT.get(p.returncode, "?"), "report": out_dir if report else None,
            "tail": (p.stdout[-400:] + p.stderr[-400:]).strip(), "pass": p.returncode == 0}
