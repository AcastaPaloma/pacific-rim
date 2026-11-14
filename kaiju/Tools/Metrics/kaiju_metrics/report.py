"""Report model: sub-measures (value, threshold, margin, status, kind), verdict rules, JSON and summary output.

Verdict rules (C2, C7, C8, C11):
- any contract error -> INVALID_INPUT (exit 2);
- any gating sub-measure FAIL -> FAIL (exit 1);
- else any gating sub-measure NOT_RUN / PENDING, or a required input missing -> INCOMPLETE (exit 3);
- else PASS (exit 0), labelled "PASS (provisional: n assumed)" when n gating sub-measures rest on thresholds
  still `assumed_pending_calibration`.
Advisory FAILs never flip the verdict; they appear as warnings. `headless_verdict` applies the same rules with the
panel sub-measures (human / agent ratings) left out. A `draft` metric never produces FAIL (orchestrator §4.4)."""
from __future__ import annotations

import math
import os
import platform
import time

from . import TOOL_NAME, __version__
from .common import (EXIT_CODES, FAIL, INCOMPLETE, INVALID_INPUT, PASS, S_FAIL, S_NA, S_NOT_RUN, S_PASS, S_PENDING,
                     S_REPORTED, dump_json, git_commit)

REPORT_SCHEMA = "metric_report@v1"
PANEL_IDS = ("M-001.13", "M-003.f")


def judge(value, op, thr, thr_hi=None):
    """Return (status, margin) with margin > 0 on the passing side. op: <, <=, >=, >, in (closed band)."""
    if value is None or (isinstance(value, float) and not math.isfinite(value)):
        return S_NOT_RUN, None
    if op == "<":
        return (S_PASS if value < thr else S_FAIL), thr - value
    if op == "<=":
        return (S_PASS if value <= thr else S_FAIL), thr - value
    if op == ">=":
        return (S_PASS if value >= thr else S_FAIL), value - thr
    if op == ">":
        return (S_PASS if value > thr else S_FAIL), value - thr
    if op == "in":
        m = min(value - thr, thr_hi - value)
        return (S_PASS if thr <= value <= thr_hi else S_FAIL), m
    raise ValueError(op)


def part(name, value, op, thr, thr_hi=None, unit="", **extra):
    st, m = judge(value, op, thr, thr_hi)
    d = {"name": name, "value": value, "unit": unit, "op": op, "threshold": thr, "status": st, "margin": m}
    if thr_hi is not None:
        d["threshold_hi"] = thr_hi
    scale = abs(thr if op != "in" else max(abs(thr), abs(thr_hi))) or 1.0
    d["margin_rel"] = None if m is None else m / scale
    d.update(extra)
    return d


class Report:
    def __init__(self, metric, reg, label, invocation):
        self.metric = metric
        self.reg = reg
        self.label = label
        self.invocation = invocation
        self.sms = {}
        self.inputs = []
        self.coverage = {"required": [], "provided": [], "missing": [], "optional_not_provided": []}
        self.flags = []
        self.warnings = []
        self.normalisers = {}
        self.images = []
        self.contract_errors = []
        self.extra = {}
        self.t0 = time.time()
        self.mode = invocation.get("mode", "standard")

    # ------------------------------------------------------------------ building
    def add(self, sid, status=None, parts=None, value=None, unit="", diagnostics=None, note=None, info=None):
        """Record a sub-measure. With parts: status = FAIL if any part fails, NOT_RUN if any part NOT_RUN,
        else PASS; value / threshold / margin are the decisive (smallest relative margin) part."""
        if sid not in self.reg.ids():
            raise KeyError(f"{sid} is not a sub-measure of {self.reg.spec}")
        d = {"id": sid, "title": self.reg.title(sid), "kind": self.reg.kind(sid),
             "threshold_status": self.reg.threshold_status(sid), "parts": parts or [], "diagnostics": diagnostics or {},
             "note": note, "value": value, "unit": unit}
        if info is not None:
            d["info"] = info
        if parts:
            sts = [p["status"] for p in parts]
            if status is None:
                status = (S_FAIL if S_FAIL in sts else S_NOT_RUN if S_NOT_RUN in sts else
                          S_NA if all(x == S_NA for x in sts) else S_PASS)
            judged = [p for p in parts if p.get("margin_rel") is not None]
            if judged:
                dec = min(judged, key=lambda p: p["margin_rel"])
                d.update(value=dec["value"], unit=dec["unit"], op=dec["op"], threshold=dec["threshold"],
                         margin=dec["margin"], margin_rel=dec["margin_rel"], decisive_part=dec["name"])
                if "threshold_hi" in dec:
                    d["threshold_hi"] = dec["threshold_hi"]
        d["status"] = status or (S_REPORTED if d["kind"] == "info" else S_NOT_RUN)
        self.sms[sid] = d
        return d

    def add_input(self, path, sha, role=""):
        from .common import rel_path
        self.inputs.append({"path": rel_path(path), "sha256": sha, "role": role})

    def warn(self, msg):
        if msg not in self.warnings:
            self.warnings.append(msg)

    def flag(self, code, detail):
        self.flags.append({"code": code, "detail": detail})

    # ------------------------------------------------------------------ verdict
    def _verdict(self, exclude=()):
        if self.contract_errors:
            return INVALID_INPUT
        gating = [d for s, d in self.sms.items() if d["kind"] == "gating" and s not in exclude]
        if any(d["status"] == S_FAIL for d in gating):
            v = FAIL
        elif any(d["status"] in (S_NOT_RUN, S_PENDING) for d in gating) or self.coverage["missing"] or not gating:
            v = INCOMPLETE
        else:
            v = PASS
        if v == FAIL and self.reg.is_draft:
            v = INCOMPLETE
        return v

    def _assumed(self, exclude=()):
        return sorted(s for s, d in self.sms.items() if d["kind"] == "gating" and s not in exclude
                      and d["status"] == S_PASS and d["threshold_status"] == "assumed_pending_calibration")

    @staticmethod
    def _label(v, assumed, mode):
        lab = v
        if v == PASS and assumed:
            lab = f"PASS (provisional: {len(assumed)} assumed)"
        if v == PASS and mode == "fixture":
            lab += " [fixture mode: not a gating run]"
        return lab

    def finalize(self):
        for sid in self.reg.ids():
            if sid not in self.sms:
                self.add(sid, S_NOT_RUN, note="not computed (required input absent)")
        for sid, d in self.sms.items():
            if d["kind"] == "advisory" and d["status"] == S_FAIL:
                self.warn(f"ADVISORY {sid} failed: value {fmt(d.get('value'))} vs {thr_text(d)}")
        self.verdict = self._verdict()
        self.headless = self._verdict(exclude=PANEL_IDS)
        if self.reg.is_draft and any(d["status"] == S_FAIL for d in self.sms.values() if d["kind"] == "gating"):
            self.flag("DRAFT_METRIC_CANNOT_FAIL", f"{self.reg.spec} registry is draft: failures are reported, "
                                                  "the verdict is withheld (INCOMPLETE)")
        self.assumed = self._assumed()
        self.exit_code = EXIT_CODES[self.verdict]

    # ------------------------------------------------------------------ output
    def to_dict(self):
        gating_fail = [s for s, d in self.sms.items() if d["kind"] == "gating" and d["status"] == S_FAIL]
        return {
            "report_schema": REPORT_SCHEMA,
            "metric": self.metric, "metric_version": self.reg.spec, "label": self.label,
            "registry": self.reg.describe(),
            "tool": {"name": TOOL_NAME, "version": __version__, "git_commit": git_commit()},
            "invocation": self.invocation,
            "inputs": sorted(self.inputs, key=lambda x: x["path"]),
            "normalisers": self.normalisers,
            "flags": self.flags,
            "warnings": self.warnings,
            "contract_errors": [e.as_dict() for e in self.contract_errors],
            "coverage": self.coverage,
            "submeasures": [self.sms[s] for s in self.reg.ids()],
            "failing_gating": gating_fail,
            "failures_suppressed": bool(self.reg.is_draft and gating_fail),
            "assumed_thresholds_decisive": self.assumed,
            "verdict": self.verdict,
            "verdict_label": self._label(self.verdict, self.assumed, self.mode),
            "headless_verdict": self.headless,
            "headless_verdict_label": self._label(self.headless, self._assumed(PANEL_IDS), self.mode),
            "exit_code": self.exit_code,
            "images": sorted(self.images),
            "extra": self.extra,
            "run": {"wall_time_s": round(time.time() - self.t0, 3), "timestamp_utc": time.strftime(
                "%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "host": platform.node(), "python": platform.python_version()},
        }

    def summary(self, d=None):
        d = d or self.to_dict()
        L = [f"{d['metric_version']}  [{d['label']}]  registry {d['registry']['revision']} "
             f"sha256:{d['registry']['sha256'][:12]}  tool {d['tool']['version']}",
             f"VERDICT: {d['verdict_label']}   headless: {d['headless_verdict_label']}   exit {d['exit_code']}   "
             f"failing gating: {len(d['failing_gating'])}" +
             (" (suppressed: draft metric, verdict withheld)" if d.get("failures_suppressed") else "")]
        if d["contract_errors"]:
            L.append(f"CONTRACT ERRORS ({len(d['contract_errors'])}):")
            for e in d["contract_errors"][:8]:
                L.append(f"  {e['file']}:{e['where']}: {e['message']} (expected {e['expected']}; found {e['found']})")
        order = sorted(d["submeasures"], key=lambda s: (s["status"] != S_FAIL or s["kind"] != "gating",
                                                         s["status"] != S_FAIL, s["kind"] != "gating"))
        for s in order:
            if len(L) >= 30:
                L.append(f"  ... ({len(order)} sub-measures in report.json)")
                break
            tag = {"gating": "G", "advisory": "A", "info": "i"}[s["kind"]]
            prov = " seed-threshold" if s["threshold_status"] == "assumed_pending_calibration" else ""
            line = f"  {s['status']:<14} [{tag}] {s['id']:<16} {fmt(s.get('value'))} {s.get('unit') or ''}"
            if s.get("op"):
                line += f"  {thr_text(s)}  margin {fmt(s.get('margin'))}{prov}"
            if s.get("note"):
                line += f"  ({s['note']})"
            L.append(line[:200])
            if s["status"] == S_FAIL and s.get("diagnostics", {}).get("where"):
                L.append(f"      where: {str(s['diagnostics']['where'])[:180]}")
        for w in d["warnings"][:4]:
            L.append(f"  warning: {w[:180]}")
        for f in d["flags"][:3]:
            L.append(f"  flag {f['code']}: {str(f['detail'])[:160]}")
        if d["coverage"]["missing"]:
            L.append(f"  missing inputs: {', '.join(d['coverage']['missing'])[:180]}")
        L.append(f"  inputs: {len(d['inputs'])} files hashed; report.json holds every value, threshold and hash")
        return "\n".join(L[:40]) + "\n"

    def write(self, out_dir, d=None):
        d = d or self.to_dict()
        if out_dir:
            os.makedirs(out_dir, exist_ok=True)
            with open(os.path.join(out_dir, "report.json"), "w", encoding="utf-8") as f:
                f.write(dump_json(d))
            with open(os.path.join(out_dir, "summary.txt"), "w", encoding="utf-8") as f:
                f.write(self.summary(d))
        return d


def fmt(v):
    if v is None:
        return "-"
    if isinstance(v, bool):
        return str(v)
    if isinstance(v, int):
        return str(v)
    if isinstance(v, float):
        if not math.isfinite(v):
            return str(v)
        return f"{v:.4g}"
    return str(v)[:40]


def thr_text(s):
    op = s.get("op")
    if op is None:
        return ""
    if op == "in":
        return f"in [{fmt(s.get('threshold'))}, {fmt(s.get('threshold_hi'))}]"
    return f"{op} {fmt(s.get('threshold'))}"


def strip_run(d):
    """Report minus the declared run block (time, host): the part that must be byte-identical across runs."""
    return {k: v for k, v in d.items() if k != "run"}
