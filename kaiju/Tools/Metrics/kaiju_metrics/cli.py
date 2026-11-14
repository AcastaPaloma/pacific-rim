"""Command-line interface: one subcommand per metric plus validate, calibrate, rescore, list-metrics, baseline.

Exit codes: 0 PASS, 1 FAIL, 2 INVALID_INPUT (contract violation or bad arguments), 3 INCOMPLETE
(valid input, required evidence absent), 4 tool error (a bug: please report). Only PASS exits 0."""
from __future__ import annotations

import argparse
import json
import os
import sys
import traceback

from . import __version__
from .common import EXIT_CODES, EXIT_TOOL_ERROR, INVALID_INPUT, InvalidInput, dump_json

EXIT_HELP = """exit codes:
  0  PASS             every gating sub-measure passed (may read "PASS (provisional: n assumed)")
  1  FAIL             at least one gating sub-measure failed
  2  INVALID_INPUT    input violates its contract (gait_log@v1, style_capture@v1, palette_set@v1) or bad arguments
  3  INCOMPLETE       input valid but required evidence absent (missing capture, panel ratings pending, ...)
  4  TOOL_ERROR       unexpected internal error (a bug in the tool, never a metric verdict)"""


def _common_metric_args(p, inputs_help):
    p.add_argument("inputs", nargs="+", help=inputs_help)
    p.add_argument("--registry", help="registry file (default: newest revision in Tools/Metrics/registry/)")
    p.add_argument("--out", help="directory for report.json, summary.txt and diagnostic images")
    p.add_argument("--label", help="label used to match panel ratings and calibration (default: first input stem)")
    p.add_argument("--ratings", help="panel_ratings@v1 JSON for the panel sub-measure (absent = PENDING)")
    p.add_argument("--no-images", action="store_true", help="skip diagnostic images")
    p.add_argument("--quiet", action="store_true", help="print only the one-line verdict")


def build_parser():
    ap = argparse.ArgumentParser(
        prog="metrics.py", formatter_class=argparse.RawDescriptionHelpFormatter,
        description="Headless metric tools for M-001@v1 .. M-004@v1 (T-002). Run with the kaiju conda env:\n"
                    "  /opt/miniconda3/envs/kaiju/bin/python Tools/Metrics/metrics.py <command> ...",
        epilog=EXIT_HELP)
    ap.add_argument("--version", action="version", version=f"kaiju_metrics {__version__}")
    sub = ap.add_subparsers(dest="cmd", metavar="<command>")

    def add(name, help_, inputs_help):
        p = sub.add_parser(name, help=help_, description=help_, epilog=EXIT_HELP,
                           formatter_class=argparse.RawDescriptionHelpFormatter)
        _common_metric_args(p, inputs_help)
        return p

    add("m001", "M-001@v1 feels heavy (gait logs: walk_fr010/020/030, start, stop, estop, turn90_inplace, curve)",
        "gait_log@v1 .jsonl files or directories holding them (one file per capture)")
    add("m002", "M-002@v1 no foot sliding (gait logs: the M-001 captures plus stomp; optional terrain walk)",
        "gait_log@v1 .jsonl files or directories holding them")
    p3 = add("m003", "M-003@v1 matches the style references (style_capture@v1 capture set: >= 12 frames per "
             "profile, plus a haze_cal sequence)", "capture-set directories (style_capture@v1)")
    p3.add_argument("--palettes", help="palette_set@v1 JSON (default research/R-002/palettes.json)")
    p3.add_argument("--profiles", help="comma-separated profiles required in the set (default: all profiles)")
    p4 = add("m004", "M-004@v1 pixel stability (style_capture@v1 sequences S0, T1, T2, T3, Y1, Y2 per profile and "
             "viewpoint)", "capture-set directories (style_capture@v1)")
    p4.add_argument("--palettes", help="palette_set@v1 JSON (default research/R-002/palettes.json)")
    p4.add_argument("--profiles", help="comma-separated profiles required (default: the registry's count, any 3)")
    p4.add_argument("--viewpoints", help="comma-separated viewpoints required (default: the registry's count)")
    p4.add_argument("--fixture", action="store_true", help="fixture mode: 60-frame sequences allowed (labelled)")
    p4.add_argument("--jobs", type=int, default=1, help="worker processes (results identical for any value)")

    pv = sub.add_parser("validate", help="validate a contract file (standalone; lists all errors)",
                        description="Validate gait_log@v1, style_capture@v1 or palette_set@v1 input. Exit 0 when "
                                    "valid, 2 when not. Errors: file:line/key: what, expected, found.",
                        epilog=EXIT_HELP, formatter_class=argparse.RawDescriptionHelpFormatter)
    pv.add_argument("contract", choices=["gait_log", "style_capture", "palette_set"])
    pv.add_argument("paths", nargs="+", help="files or directories to validate")
    pv.add_argument("--palettes", help="palette_set@v1 used to check style captures (default research/R-002)")
    pv.add_argument("--json", action="store_true", help="machine-readable error list on stdout")

    pc = sub.add_parser("calibrate", help="propose thresholds from founder ratings (never writes the registry)",
                        description="Calibration helper (orchestrator §4.3). Reads a panel_ratings@v1 file and the "
                                    "metric reports of the rated captures; prints rater agreement, anchor sanity, a "
                                    "value-vs-rating table and plot, proposed thresholds with false-pass / false-fail "
                                    "counts and a registry diff with a revision bump. Refuses with < 8 captures or "
                                    "misbehaving anchors.", epilog=EXIT_HELP,
                        formatter_class=argparse.RawDescriptionHelpFormatter)
    pc.add_argument("--ratings", required=True)
    pc.add_argument("--reports", nargs="+", required=True, help="report.json files (or directories holding them)")
    pc.add_argument("--registry", help="registry revision the proposal starts from (default: newest)")
    pc.add_argument("--out", help="directory for the proposal (diff, table, plot); must be outside registry/")
    pc.add_argument("--pass-score", type=float, default=8.0, help="rating that counts as a pass (default 8)")

    pr = sub.add_parser("rescore", help="re-run an old report on its own inputs and registry revision",
                        description="Re-runs the metric of an old report.json with the registry revision the report "
                                    "names (found by SHA-256) and compares verdicts. A different registry must be "
                                    "asked for explicitly with --registry and --allow-different-registry.",
                        epilog=EXIT_HELP, formatter_class=argparse.RawDescriptionHelpFormatter)
    pr.add_argument("report")
    pr.add_argument("--registry", help="registry file to use (must match the report's SHA-256 unless allowed)")
    pr.add_argument("--allow-different-registry", action="store_true")
    pr.add_argument("--out", help="directory for the new report")

    pl = sub.add_parser("list-metrics", help="list metrics, registry revisions and sub-measures")
    pl.add_argument("--json", action="store_true")

    pb = sub.add_parser("baseline", help="run the still-applicable M-003 measures on the 11 reference panels")
    pb.add_argument("--out", required=True)
    pb.add_argument("--registry")
    return ap


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    ap = build_parser()
    try:
        args = ap.parse_args(argv)
    except SystemExit as e:  # argparse: 0 for --help / --version, 2 for usage errors
        return int(e.code or 0)
    if not args.cmd:
        ap.print_help()
        return EXIT_CODES[INVALID_INPUT]
    try:
        return _dispatch(args, argv)
    except InvalidInput as e:
        print(f"INVALID_INPUT: {len(e.errors)} contract error(s)", file=sys.stderr)
        for err in e.errors[:50]:
            print(f"  {err}", file=sys.stderr)
        return EXIT_CODES[INVALID_INPUT]
    except UsageError as e:
        print(f"error: {e}", file=sys.stderr)
        return EXIT_CODES[INVALID_INPUT]
    except Exception:  # noqa: BLE001 - never a verdict: a tool bug
        print("TOOL_ERROR (bug in kaiju_metrics, not a metric verdict):", file=sys.stderr)
        traceback.print_exc(limit=5)
        return EXIT_TOOL_ERROR


class UsageError(Exception):
    pass


def _dispatch(args, argv):
    if args.cmd == "validate":
        return _validate(args)
    if args.cmd == "list-metrics":
        from .registry import list_metrics
        info = list_metrics()
        if args.json:
            print(dump_json(info), end="")
        else:
            for m in info:
                print(f"{m['spec']}  revision {m['revision']}  ({m['metric_status']})  {m['path']}")
                for sid, e in m["submeasures"].items():
                    print(f"    {sid:<22} {e['kind']:<9} {e['title']}")
        return 0
    if args.cmd in ("m001", "m002", "m003", "m004"):
        from .runner import run_metric_cli
        return run_metric_cli(args.cmd, args, argv)
    if args.cmd == "calibrate":
        from .calibrate import calibrate_cli
        return calibrate_cli(args)
    if args.cmd == "rescore":
        from .rescore import rescore_cli
        return rescore_cli(args)
    if args.cmd == "baseline":
        from .baseline import baseline_cli
        return baseline_cli(args)
    raise UsageError(f"unknown command {args.cmd}")


def _validate(args):
    from .contracts import gait_log, palette_set, style_capture
    all_errs = []
    checked = 0
    pal = None
    if args.contract == "style_capture":
        pal = palette_set.load_palette_set(args.palettes)
    for p in args.paths:
        if args.contract == "gait_log":
            files = _expand(p, ".jsonl")
            for f in files:
                all_errs += gait_log.validate_file(f)
                checked += 1
        elif args.contract == "palette_set":
            all_errs += palette_set.validate_file(p)
            checked += 1
        else:
            all_errs += style_capture.validate_path(p, pal)
            checked += 1
    if args.json:
        print(json.dumps({"contract": args.contract, "checked": checked, "valid": not all_errs,
                          "errors": [e.as_dict() for e in all_errs]}, indent=1))
    else:
        if all_errs:
            print(f"INVALID: {len(all_errs)} error(s) in {checked} input(s) [{args.contract}@v1]")
            for e in all_errs:
                print(f"  {e}")
        else:
            print(f"VALID: {checked} input(s) conform to {args.contract}@v1")
    return 0 if not all_errs else EXIT_CODES[INVALID_INPUT]


def _expand(p, ext):
    if os.path.isdir(p):
        return sorted(os.path.join(p, f) for f in os.listdir(p) if f.endswith(ext))
    return [p]


if __name__ == "__main__":
    sys.exit(main())
