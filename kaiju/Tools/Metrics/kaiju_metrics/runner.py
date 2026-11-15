"""Runs one metric: registry, inputs, verdict, report files. Also the programmatic API used by tests and rescore."""
from __future__ import annotations

import importlib
import os

from .common import InvalidInput, rel_path
from .registry import load_registry
from .report import Report

METRIC_OF = {"m001": "M-001", "m002": "M-002", "m003": "M-003", "m004": "M-004", "m007": "M-007", "m050": "M-050", "m051": "M-051", "m052": "M-052", "m053": "M-053", "m054": "M-054"}


def _label(inputs, label):
    if label:
        return label
    p = os.path.normpath(inputs[0])
    b = os.path.basename(p)
    for ext in (".gait.jsonl", ".jsonl"):
        if b.endswith(ext):
            b = b[: -len(ext)]
    return b


def run_metric(cmd, inputs, registry=None, out_dir=None, label=None, ratings=None, images=True, options=None):
    """Run a metric and return (report_dict, Report). Never raises on bad input: contract errors become an
    INVALID_INPUT report (exit 2)."""
    options = dict(options or {})
    metric = METRIC_OF[cmd]
    reg = load_registry(metric, registry)
    invocation = {"command": cmd, "inputs": [rel_path(p) for p in inputs],
                  "label": _label(inputs, label), "ratings": rel_path(ratings) if ratings else None,
                  "options": {k: v for k, v in sorted(options.items())},
                  "mode": "fixture" if options.get("fixture") else "standard"}
    rep = Report(metric, reg, invocation["label"], invocation)
    try:
        rt = None
        if ratings:
            from .ratings import load_ratings
            rt = load_ratings(ratings)
            rep.add_input(ratings, _sha(ratings), "panel_ratings@v1")
        if cmd == "m007":
            from .brain import m007
            m007.run(rep, inputs, reg, options=options)
        elif cmd in ("m050", "m051", "m052", "m053", "m054"):
            mod = importlib.import_module(f".destruction.{cmd}", __package__)
            mod.run(rep, inputs, reg, options=options)
        elif cmd in ("m001", "m002"):
            from .gait.inputs import load_logs
            logs = load_logs(inputs)
            mod = importlib.import_module(f".gait.{cmd}", __package__)
            if out_dir:
                os.makedirs(out_dir, exist_ok=True)
            mod.run(rep, logs, reg, ratings=rt, out_dir=out_dir, images=images)
        else:
            mod = importlib.import_module(f".style.{cmd}", __package__)
            if out_dir:
                os.makedirs(out_dir, exist_ok=True)
            mod.run(rep, inputs, reg, ratings=rt, out_dir=out_dir, images=images, options=options)
    except InvalidInput as e:
        rep.contract_errors = e.errors
        rep.sms = {}
    rep.finalize()
    d = rep.write(out_dir)
    return d, rep


def _sha(p):
    from .common import sha256_file
    return sha256_file(p)


def run_metric_cli(cmd, args, argv):
    options = {}
    for k in ("palettes", "profiles", "viewpoints", "fixture", "jobs"):
        if hasattr(args, k) and getattr(args, k) not in (None, False):
            options[k] = getattr(args, k)
    if "jobs" in options and options["jobs"] == 1:
        del options["jobs"]
    out = args.out or os.path.join("metrics_out", f"{cmd}_{_label(args.inputs, args.label)}")
    d, rep = run_metric(cmd, args.inputs, registry=args.registry, out_dir=out, label=args.label,
                        ratings=args.ratings, images=not args.no_images, options=options)
    if args.quiet:
        print(f"{d['metric_version']} {d['verdict_label']} (exit {d['exit_code']})")
    else:
        print(rep.summary(d), end="")
        print(f"report: {os.path.join(out, 'report.json')}")
    return d["exit_code"]
