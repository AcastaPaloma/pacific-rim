#!/usr/bin/env python
"""capture.py <spec|style|lint|matrix|fidelity|encode|upscale|sheets|hud|motion|shots>  (docs/store/capture_plan.md section 5). Exit 0 pass, 2 fail.
Run with /opt/miniconda3/envs/kaiju/bin/python."""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from capture_tools import encode, fidelity, hud, lint, matrix, motion, sheets, shots, spec, style, xproc  # noqa: E402


def main(argv=None):
    ap = argparse.ArgumentParser(prog="capture.py")
    ap.add_argument("--report", help="also write the full JSON report to this file (stdout is cut at 6000 characters)")
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("spec"); p.add_argument("--assets", required=True); p.add_argument("--trailer"); p.add_argument("--demo", action="store_true")
    p = sub.add_parser("style"); p.add_argument("--set", required=True); p.add_argument("--out", required=True)
    p.add_argument("--motion", action="store_true"); p.add_argument("--no-metrics", action="store_true")
    p = sub.add_parser("fidelity"); p.add_argument("--master", required=True); p.add_argument("--video", required=True)
    p.add_argument("--cuts", default="", help="comma-separated cut frame indices"); p.add_argument("--every", type=int, default=10)
    p = sub.add_parser("encode"); p.add_argument("--frames", required=True); p.add_argument("--out", required=True)
    p.add_argument("--audio"); p.add_argument("--crf", type=int); p.add_argument("--fps", type=int, default=60)
    p.add_argument("--dry-run", action="store_true")
    p = sub.add_parser("upscale"); p.add_argument("--src", required=True); p.add_argument("--dst", required=True); p.add_argument("--scale", type=int, required=True)
    p = sub.add_parser("lint"); p.add_argument("--paths", required=True, help="a directory of capture_path@v1 JSON files, or one file")
    p = sub.add_parser("matrix"); p.add_argument("--frames", required=True, help="directory with internal-resolution *_color_internal.png frames")
    p.add_argument("--n", type=int, default=8); p.add_argument("--scales", default="1,2,3,4"); p.add_argument("--crf", default="10,14,18,23")
    p.add_argument("--pix-fmts", default="yuv420p,yuv444p"); p.add_argument("--tunes", default="none,animation"); p.add_argument("--preset", default="medium")
    p.add_argument("--settings-out", help="write the selected cell as encode_settings JSON here")
    p.add_argument("--select-pix-fmt", help="restrict the selected cell to this pixel format (the whole grid is still measured)")
    p.add_argument("--select-scale", type=int, help="restrict the selected cell to this master scale (the whole grid is still measured)")
    p.add_argument("--jobs", type=int, default=1, help="measure matrix cells in parallel")
    p.add_argument("--progress", action="store_true", help="print one line per measured cell to stderr")
    p = sub.add_parser("sheets"); p.add_argument("--frames", required=True); p.add_argument("--out", required=True)
    p.add_argument("--cols", type=int, default=4); p.add_argument("--every", type=int, default=1)
    p = sub.add_parser("hud"); p.add_argument("--captures", required=True); p.add_argument("--scenes", required=True); p.add_argument("--out", required=True)
    p = sub.add_parser("motion"); p.add_argument("--set", required=True, help="capture root holding <path_id>/<viewpoint>_<class>/sequence.json"); p.add_argument("--out", required=True); p.add_argument("--jobs", type=int, default=4)
    p = sub.add_parser("xproc"); p.add_argument("--a", required=True, help="capture root of process A"); p.add_argument("--b", required=True, help="capture root of process B (same paths, same code, a separate Unity process)")
    p = sub.add_parser("shots"); p.add_argument("--plan", required=True); p.add_argument("--paths", required=True); p.add_argument("--geo", required=True, help="StreamingAssets/Geo/tiles/v1/L0")
    p.add_argument("--out", required=True); p.add_argument("--extra", default="", help="comma-separated extra path directories (HUD, motion)"); p.add_argument("--run", help="capture_run.json of a driver run (marks captured paths)")
    a = ap.parse_args(argv)
    if a.cmd == "spec":
        r = spec.run(a.assets, a.trailer, a.demo)
    elif a.cmd == "style":
        r = style.run(a.set, a.out, a.motion, not a.no_metrics)
    elif a.cmd == "lint":
        r = lint.run(a.paths)
    elif a.cmd == "matrix":
        r = matrix.run(a.frames, a.n, tuple(int(x) for x in a.scales.split(",")), tuple(a.pix_fmts.split(",")), tuple(int(x) for x in a.crf.split(",")),
                       tuple(a.tunes.split(",")), a.preset,
                       select_pix_fmt=a.select_pix_fmt, select_scale=a.select_scale, jobs=a.jobs,
                       progress=(lambda c: print("cell", {k: c.get(k) for k in ("scale", "pix_fmt", "crf", "tune", "bytes", "min_share")}, file=sys.stderr, flush=True)) if a.progress else None)
        if r.get("selected") and a.settings_out:
            json.dump(matrix.settings_json(r), open(a.settings_out, "w"), indent=1)
    elif a.cmd == "hud":
        r = hud.run(a.captures, a.scenes, a.out)
    elif a.cmd == "xproc":
        r = xproc.run(a.a, a.b)
    elif a.cmd == "motion":
        r = motion.run(a.set, a.out, a.jobs)
    elif a.cmd == "shots":
        cap = None
        if a.run:
            cap = {(x["path_id"].split("/")[0]) for x in json.load(open(a.run))["sequences"]}
        r = shots.run(a.plan, a.paths, a.geo, a.out, [x for x in a.extra.split(",") if x], cap)
    elif a.cmd == "fidelity":
        r = fidelity.run(a.master, a.video, [int(x) for x in a.cuts.split(",") if x], a.every)
    elif a.cmd == "encode":
        r = encode.encode(a.frames, a.out, a.dry_run, fps=a.fps, audio=a.audio, crf=a.crf); r["pass"] = True
    elif a.cmd == "upscale":
        r = {"frames": encode.upscale_dir(a.src, a.dst, a.scale), "pass": True}
    else:
        r = sheets.run(a.frames, a.out, every=a.every, cols=a.cols); r["pass"] = True
    if a.report:
        with open(a.report, "w") as fh:
            json.dump(r, fh, indent=1, default=str)
    print(json.dumps(r, indent=1, default=str)[:6000])
    return 0 if r.get("pass") else 2


if __name__ == "__main__":
    sys.exit(main())
