#!/usr/bin/env python
"""capture.py <spec|style|scale|fidelity|encode|sheets>  (docs/store/capture_plan.md section 5). Exit 0 pass, 2 fail.
Run with /opt/miniconda3/envs/kaiju/bin/python."""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from capture_tools import encode, fidelity, sheets, spec, style  # noqa: E402


def main(argv=None):
    ap = argparse.ArgumentParser(prog="capture.py")
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
    p = sub.add_parser("sheets"); p.add_argument("--frames", required=True); p.add_argument("--out", required=True)
    p.add_argument("--cols", type=int, default=4); p.add_argument("--every", type=int, default=1)
    a = ap.parse_args(argv)
    if a.cmd == "spec":
        r = spec.run(a.assets, a.trailer, a.demo)
    elif a.cmd == "style":
        r = style.run(a.set, a.out, a.motion, not a.no_metrics)
    elif a.cmd == "fidelity":
        r = fidelity.run(a.master, a.video, [int(x) for x in a.cuts.split(",") if x], a.every)
    elif a.cmd == "encode":
        r = encode.encode(a.frames, a.out, a.dry_run, fps=a.fps, audio=a.audio, crf=a.crf); r["pass"] = True
    elif a.cmd == "upscale":
        r = {"frames": encode.upscale_dir(a.src, a.dst, a.scale), "pass": True}
    else:
        r = sheets.run(a.frames, a.out, every=a.every, cols=a.cols); r["pass"] = True
    print(json.dumps(r, indent=1, default=str)[:6000])
    return 0 if r.get("pass") else 2


if __name__ == "__main__":
    sys.exit(main())
