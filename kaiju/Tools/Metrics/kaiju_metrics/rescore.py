"""rescore: re-run an old report's metric on its own inputs with the registry revision it names (by SHA-256).

An old report is never silently reinterpreted under a newer registry: a different registry must be named with
--registry and --allow-different-registry, and the new report then records both revisions."""
from __future__ import annotations

import json
import os
import sys

from .common import (EXIT_CODES, INVALID_INPUT, ContractError, InvalidInput, dump_json, load_json_file, resolve_path,
                     sha256_file)
from .registry import Registry, find_by_sha
from .report import strip_run


def rescore(report_path, registry=None, allow_different=False, out_dir=None):
    old = load_json_file(report_path)
    if old.get("report_schema") != "metric_report@v1":
        raise InvalidInput([ContractError(report_path, "report_schema", "not a metric report", "metric_report@v1",
                                          repr(old.get("report_schema")))])
    want = old["registry"]["sha256"]
    if registry:
        reg_path = registry
        if sha256_file(reg_path) != want and not allow_different:
            raise InvalidInput([ContractError(report_path, "registry.sha256",
                                              "registry differs from the one the report used; pass "
                                              "--allow-different-registry to re-score under it explicitly",
                                              want, sha256_file(reg_path))])
    else:
        reg_path = find_by_sha(want)
        if reg_path is None:
            raise InvalidInput([ContractError(report_path, "registry.sha256", "the report's registry revision is not "
                                              "in Tools/Metrics/registry/ (pass it with --registry)", want, "absent")])
    errs = []
    for inp in old["inputs"]:
        p = resolve_path(inp["path"])
        if not os.path.isfile(p):
            errs.append(ContractError(report_path, f"inputs {inp['path']}", "input file missing", inp["sha256"], "absent"))
        elif sha256_file(p) != inp["sha256"]:
            errs.append(ContractError(report_path, f"inputs {inp['path']}", "input changed since the report",
                                      inp["sha256"], sha256_file(p)))
    if errs:
        raise InvalidInput(errs)
    inv = old["invocation"]
    from .runner import run_metric
    new, rep = run_metric(inv["command"], [resolve_path(p) for p in inv["inputs"]], registry=reg_path, out_dir=None,
                          label=inv["label"], ratings=resolve_path(inv["ratings"]) if inv.get("ratings") else None,
                          images=False, options=inv.get("options") or {})
    new = json.loads(dump_json(new))           # same rounding as a written report
    same_reg = Registry(reg_path).sha256 == want
    if not same_reg:
        new["rescored_from"] = {"report": report_path, "registry": old["registry"]}
    if out_dir:
        rep.write(out_dir, new)
    a, b = strip_run(old), strip_run(new)
    for d in (a, b):
        d.pop("images", None)
        for s in d.get("submeasures", []):
            s.get("diagnostics", {}).pop("heat_map", None)
    diffs = []
    if old["verdict"] != new["verdict"]:
        diffs.append(f"verdict {old['verdict']} -> {new['verdict']}")
    om = {s["id"]: s for s in old["submeasures"]}
    for s in new["submeasures"]:
        o = om.get(s["id"])
        if o is None or o["status"] != s["status"] or o.get("value") != s.get("value"):
            diffs.append(f"{s['id']}: {o and o['status']} {o and o.get('value')} -> {s['status']} {s.get('value')}")
    identical = json.dumps(a, sort_keys=True) == json.dumps(b, sort_keys=True)
    explain = explain_semantics(old, new)
    new["rescore"] = {"same_registry": same_reg, "old_semantics": explain["old"], "new_semantics": explain["new"],
                      "semantic_changes": explain["changes"], "differences": diffs}
    return old, new, diffs, identical, same_reg


def explain_semantics(old, new):
    """Tool behaviour changes between the old report's semantics and the current one (DEF-T002-arch-9).
    Reports without tool.semantics were produced by semantics 1."""
    from .report import SEMANTIC_CHANGES, SEMANTICS
    o = int(old.get("tool", {}).get("semantics", 1))
    ch = []
    for v in range(o + 1, SEMANTICS + 1):
        for k in ("all", old["metric"]):
            if k in SEMANTIC_CHANGES.get(v, {}):
                ch.append(f"semantics {v}: {SEMANTIC_CHANGES[v][k]}")
    return {"old": o, "new": SEMANTICS, "changes": ch}


def rescore_cli(args):
    old, new, diffs, identical, same_reg = rescore(args.report, args.registry, args.allow_different_registry, args.out)
    print(f"rescore {old['metric_version']} [{old['label']}] registry {old['registry']['revision']} "
          f"-> {new['registry']['revision']} ({'same' if same_reg else 'DIFFERENT'} registry)")
    print(f"old verdict {old['verdict_label']}  new verdict {new['verdict_label']}")
    sem = new["rescore"]
    if not diffs:
        print("REPRODUCED: every verdict and sub-measure identical" +
              ("; report byte-identical outside the run block" if identical else ""))
    else:
        print(f"DIFFERS ({len(diffs)}):")
        for d in diffs[:30]:
            print(f"  {d}")
        if sem["semantic_changes"]:
            print(f"CAUSE: tool semantic change, not the registry (tool semantics {sem['old_semantics']} -> "
                  f"{sem['new_semantics']}, kaiju_metrics {new['tool']['version']}"
                  f"{'; registry revision unchanged' if same_reg else '; registry ALSO differs'}):")
            for c in sem["semantic_changes"]:
                print(f"  {c}")
        elif same_reg:
            print("CAUSE: unexplained (same registry, same tool semantics): please report as a tool defect")
        else:
            print("CAUSE: registry revision differs (same tool semantics)")
    return new["exit_code"]
