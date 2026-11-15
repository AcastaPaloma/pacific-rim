#!/usr/bin/env python3
"""T-140: headless-ish smoke test of a built macOS player: boot -> composition root -> load the district -> quit.

Runs the player through the M1 slice scene (`-sliceSmoke N -sliceSmokeOut <json>`: Core boot, GameComposition.Install, the
15-tile district load, scripted frames, then Application.Quit with exit 0 only when the scene's own checks pass), with a
timeout. Passes only if: exit code 0, the log has no "[Slice] Core is not booted" / exception / error-count lines for the
composition, the smoke JSON reports tiles > 0 and buildings_registered > 0. Writes <out-dir>/smoke_result.json and exits 0/1.

Usage:  python -I smoke.py --app <X.app> --out-dir <dir> [--frames 120] [--timeout 600]
"""
import argparse
import json
import os
import subprocess
import sys
import time

FATAL = ("[Slice] Core is not booted", "NullReferenceException", "[Slice] slice.m1 is off")


def evaluate(exit_code, log_text, smoke_json):
    """List of failure strings (empty = pass)."""
    fails = []
    if exit_code != 0:
        fails.append("exit code %r (0 expected)" % (exit_code,))
    for needle in FATAL:
        if needle in log_text:
            fails.append("log contains %r" % needle)
    if "[SliceSmoke]" not in log_text:
        fails.append("log has no [SliceSmoke] summary line (the scene never reached the end of the smoke run)")
    if smoke_json is None:
        fails.append("smoke JSON missing or unreadable")
    else:
        if not smoke_json.get("tiles", 0) > 0:
            fails.append("no tile loaded (tiles=%r)" % smoke_json.get("tiles"))
        if not smoke_json.get("buildings_registered", 0) > 0:
            fails.append("no building registered with Destruction (buildings_registered=%r)" % smoke_json.get("buildings_registered"))
    return fails


def player_binary(app):
    macos = os.path.join(app, "Contents", "MacOS")
    names = [n for n in os.listdir(macos) if os.access(os.path.join(macos, n), os.X_OK)]
    if len(names) != 1:
        raise RuntimeError("expected one executable in %s, found %r" % (macos, names))
    return os.path.join(macos, names[0])


def run(app, out_dir, frames=120, timeout=600, width=1280, height=720):
    os.makedirs(out_dir, exist_ok=True)
    log = os.path.join(out_dir, "smoke_player.log")
    sj = os.path.join(out_dir, "smoke_slice.json")
    for p in (log, sj):
        if os.path.exists(p):
            os.remove(p)
    cmd = [player_binary(app), "-sliceSmoke", str(frames), "-sliceSmokeOut", sj, "-logFile", log,
           "-screen-width", str(width), "-screen-height", str(height), "-screen-fullscreen", "0", "-popupwindow"]
    t0 = time.time()
    try:
        code = subprocess.run(cmd, timeout=timeout, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode
    except subprocess.TimeoutExpired:
        code = "timeout after %ds" % timeout
    text = open(log, encoding="utf-8", errors="replace").read() if os.path.exists(log) else ""
    data = None
    if os.path.exists(sj):
        try:
            data = json.load(open(sj, encoding="utf-8"))
        except ValueError:
            data = None
    fails = evaluate(code, text, data)
    res = {"spec": "build_smoke@v1", "pass": not fails, "exit_code": code, "failures": fails, "seconds": round(time.time() - t0, 1),
           "tiles": (data or {}).get("tiles"), "buildings_registered": (data or {}).get("buildings_registered"),
           "load_seconds": (data or {}).get("load_seconds"), "log": log}
    with open(os.path.join(out_dir, "smoke_result.json"), "w", encoding="utf-8") as f:
        json.dump(res, f, indent=1)
        f.write("\n")
    return res


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--app", required=True)
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--frames", type=int, default=120)
    ap.add_argument("--timeout", type=int, default=600)
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=720)
    a = ap.parse_args(argv)
    r = run(a.app, a.out_dir, a.frames, a.timeout, a.width, a.height)
    print("SMOKE %s: exit=%s tiles=%s buildings=%s %.0fs log=%s" % ("PASS" if r["pass"] else "FAIL", r["exit_code"], r["tiles"], r["buildings_registered"], r["seconds"], r["log"]))
    for f in r["failures"]:
        print("  - " + f, file=sys.stderr)
    return 0 if r["pass"] else 1


if __name__ == "__main__":
    sys.exit(main())
