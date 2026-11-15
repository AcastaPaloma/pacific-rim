"""Versioned threshold registry (Tools/Metrics/registry/*.yaml). Thresholds live only here (C10).

File naming: `<metric>@<spec major>.yaml` is revision v<major>.0; a calibration writes (by hand, after review)
`<metric>@v<major>.<n>.yaml` with `revision: v<major>.<n>`. The default for a metric is its newest revision."""
from __future__ import annotations

import os
import re

import yaml

from .common import REGISTRY_DIR, rel_path, sha256_file

STATUSES = ("derived", "assumed_pending_calibration", "calibrated")
KINDS = ("gating", "advisory", "info")
METRICS = ("M-001", "M-002", "M-003", "M-004", "M-007", "M-050", "M-051", "M-052", "M-053", "M-054")


_MISSING = object()


class RegistryError(Exception):
    pass


class Registry:
    def __init__(self, path):
        self.path = os.path.abspath(path)
        with open(self.path, "rb") as f:
            raw = f.read()
        try:
            self.data = yaml.safe_load(raw.decode("utf-8"))
        except Exception as e:  # noqa: BLE001
            raise RegistryError(f"{path}: not valid YAML ({e})")
        self.sha256 = sha256_file(self.path)
        self._validate()
        self.used = {}          # (sub-measure, key) -> entry, for provisional labelling
        self.defaults_used = {}

    def _validate(self):
        d = self.data
        if not isinstance(d, dict) or d.get("registry_schema") != "metric_registry@v1":
            raise RegistryError(f"{self.path}: registry_schema must be metric_registry@v1")
        for k in ("metric", "spec", "revision", "metric_status", "submeasures"):
            if k not in d:
                raise RegistryError(f"{self.path}: missing key {k}")
        if d["metric_status"] not in ("draft", "registered"):
            raise RegistryError(f"{self.path}: metric_status must be draft | registered")
        for sid, e in d["submeasures"].items():
            if e.get("kind") not in KINDS:
                raise RegistryError(f"{self.path}: {sid}.kind must be one of {KINDS}")
            for key, t in (e.get("thresholds") or {}).items():
                self._check_entry(f"{sid}.{key}", t)
        for key, t in (d.get("procedure") or {}).items():
            self._check_entry(f"procedure.{key}", t)

    def _check_entry(self, where, t):
        if not isinstance(t, dict) or "value" not in t or "unit" not in t or "source" not in t:
            raise RegistryError(f"{self.path}: {where} needs value, unit, status, source")
        if t.get("status") not in STATUSES:
            raise RegistryError(f"{self.path}: {where}.status must be one of {STATUSES}")

    # ------------------------------------------------------------------ access
    @property
    def metric(self):
        return self.data["metric"]

    @property
    def spec(self):
        return self.data["spec"]

    @property
    def revision(self):
        return self.data["revision"]

    @property
    def is_draft(self):
        return self.data["metric_status"] == "draft"

    def t(self, sid, key):
        """Threshold value of sub-measure `sid`; records its use."""
        try:
            e = self.data["submeasures"][sid]["thresholds"][key]
        except KeyError:
            raise RegistryError(f"{rel_path(self.path)}: no threshold {sid}.{key}")
        self.used[(sid, key)] = e
        return e["value"]

    def entry(self, sid, key):
        return self.data["submeasures"][sid]["thresholds"][key]

    def p(self, key, default=_MISSING):
        """Procedure constant (spec numbers that are not pass / fail thresholds). `default` applies only to constants
        added in a later revision (re-scoring an older revision); it is reported in `defaults_used`."""
        try:
            return self.data["procedure"][key]["value"]
        except KeyError:
            if default is not _MISSING:
                self.defaults_used[key] = default
                return default
            raise RegistryError(f"{rel_path(self.path)}: no procedure constant {key}")

    def get(self, key, default=None):
        return self.data.get(key, default)

    def sub(self, sid):
        return self.data["submeasures"][sid]

    def kind(self, sid):
        return self.data["submeasures"][sid]["kind"]

    def title(self, sid):
        return self.data["submeasures"][sid]["title"]

    def ids(self):
        return list(self.data["submeasures"].keys())

    def threshold_status(self, sid):
        st = [e.get("status") for e in (self.data["submeasures"][sid].get("thresholds") or {}).values()]
        if "assumed_pending_calibration" in st:
            return "assumed_pending_calibration"
        if st and all(s == "calibrated" for s in st):
            return "calibrated"
        return "derived" if st else "none"

    def describe(self):
        d = {"path": rel_path(self.path), "spec": self.spec, "revision": self.revision, "sha256": self.sha256,
             "metric_status": self.data["metric_status"]}
        if self.defaults_used:
            d["defaults_for_later_constants"] = dict(self.defaults_used)
        return d


_REV = re.compile(r"^(M-\d{3})@v(\d+)(?:\.(\d+))?\.yaml$")


def registry_files(directory=REGISTRY_DIR):
    out = []
    for f in sorted(os.listdir(directory)):
        m = _REV.match(f)
        if m:
            out.append((m.group(1), int(m.group(2)), int(m.group(3) or 0), os.path.join(directory, f)))
    return out


def default_registry(metric, directory=REGISTRY_DIR):
    cands = [r for r in registry_files(directory) if r[0] == metric]
    if not cands:
        raise RegistryError(f"no registry file for {metric} in {rel_path(directory)}")
    cands.sort(key=lambda r: (r[1], r[2]))
    return Registry(cands[-1][3])


def load_registry(metric, path=None):
    reg = Registry(path) if path else default_registry(metric)
    if reg.metric != metric:
        raise RegistryError(f"{rel_path(reg.path)} is a registry for {reg.metric}, not {metric}")
    return reg


def find_by_sha(sha, directory=REGISTRY_DIR):
    for _, _, _, p in registry_files(directory):
        if sha256_file(p) == sha:
            return p
    return None


def list_metrics():
    out = []
    for metric, _, _, p in registry_files():
        r = Registry(p)
        out.append({"metric": metric, "spec": r.spec, "revision": r.revision, "metric_status": r.data["metric_status"],
                    "path": rel_path(p), "sha256": r.sha256,
                    "submeasures": {s: {"kind": r.kind(s), "title": r.title(s), "threshold_status": r.threshold_status(s)}
                                    for s in r.ids()}})
    return out
