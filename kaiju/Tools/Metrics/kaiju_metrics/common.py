"""Shared helpers: contract errors, hashing, strict JSON, project paths, git commit."""
from __future__ import annotations

import hashlib
import json
import math
import os
import subprocess
from dataclasses import dataclass, field

PKG_DIR = os.path.dirname(os.path.abspath(__file__))
METRICS_DIR = os.path.dirname(PKG_DIR)                      # Tools/Metrics
PROJECT_ROOT = os.path.dirname(os.path.dirname(METRICS_DIR))  # kaiju/
REGISTRY_DIR = os.path.join(METRICS_DIR, "registry")

# Verdicts and exit codes (C7). Only PASS exits 0.
PASS, FAIL, INVALID_INPUT, INCOMPLETE = "PASS", "FAIL", "INVALID_INPUT", "INCOMPLETE"
EXIT_CODES = {PASS: 0, FAIL: 1, INVALID_INPUT: 2, INCOMPLETE: 3}
EXIT_TOOL_ERROR = 4
# Sub-measure statuses
S_PASS, S_FAIL, S_NOT_RUN, S_PENDING, S_NA, S_REPORTED = (
    "PASS", "FAIL", "NOT_RUN", "PENDING", "NOT_APPLICABLE", "REPORTED")


@dataclass
class ContractError:
    """One contract violation: where (file, line or key path), what was expected, what was found."""
    file: str
    where: str
    message: str
    expected: str = ""
    found: str = ""

    def as_dict(self):
        return {"file": self.file, "where": self.where, "message": self.message,
                "expected": self.expected, "found": self.found}

    def __str__(self):
        s = (f"{self.file}:{self.where}" if self.file else self.where) + f": {self.message}"
        if self.expected or self.found:
            s += f" (expected {self.expected}; found {self.found})"
        return s


class InvalidInput(Exception):
    """Raised when input violates its contract. Carries the full list of errors."""

    def __init__(self, errors):
        self.errors = list(errors)
        super().__init__("; ".join(str(e) for e in self.errors[:5]) +
                         (f" ... (+{len(self.errors) - 5} more)" if len(self.errors) > 5 else ""))


@dataclass
class ErrorList:
    file: str
    errors: list = field(default_factory=list)

    def add(self, where, message, expected="", found=""):
        self.errors.append(ContractError(self.file, str(where), message, str(expected), str(found)))

    def __bool__(self):
        return bool(self.errors)


def sha256_file(path, chunk=1 << 20):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(chunk), b""):
            h.update(b)
    return h.hexdigest()


def sha256_bytes(b: bytes) -> str:
    return hashlib.sha256(b).hexdigest()


def _reject_constant(name):
    raise ValueError(f"non-finite number {name} is not allowed")


def strict_json_loads(text):
    """json.loads that rejects NaN / Infinity tokens (contracts require finite numbers)."""
    return json.loads(text, parse_constant=_reject_constant)


def load_json_file(path):
    with open(path, "r", encoding="utf-8") as f:
        return strict_json_loads(f.read())


def dump_json(obj) -> str:
    """Deterministic JSON: sorted keys, fixed separators, finite floats rounded to 12 significant digits."""
    return json.dumps(_clean(obj), sort_keys=True, indent=1, ensure_ascii=False, allow_nan=False) + "\n"


def _clean(o):
    if isinstance(o, dict):
        return {str(k): _clean(v) for k, v in o.items()}
    if isinstance(o, (list, tuple)):
        return [_clean(v) for v in o]
    if hasattr(o, "item") and not isinstance(o, (str, bytes)):  # numpy scalar
        try:
            o = o.item()
        except Exception:
            pass
    if isinstance(o, bool) or o is None or isinstance(o, (int, str)):
        return o
    if isinstance(o, float):
        if not math.isfinite(o):
            return None
        return float(f"{o:.12g}")
    if hasattr(o, "tolist"):
        return _clean(o.tolist())
    return o


def rel_path(path):
    """Path relative to the project root when inside it (stable across checkouts), else absolute."""
    ap = os.path.abspath(path)
    try:
        rp = os.path.relpath(ap, PROJECT_ROOT)
    except ValueError:
        return ap
    return ap if rp.startswith("..") else rp.replace(os.sep, "/")


def resolve_path(path):
    """Inverse of rel_path."""
    return path if os.path.isabs(path) else os.path.join(PROJECT_ROOT, path)


_GIT = {}


def git_commit():
    """Commit of the tool checkout (cached). 'unknown' outside git."""
    if "c" not in _GIT:
        try:
            out = subprocess.run(["git", "-C", METRICS_DIR, "rev-parse", "HEAD"], capture_output=True,
                                 text=True, timeout=10)
            _GIT["c"] = out.stdout.strip() or "unknown"
        except Exception:
            _GIT["c"] = "unknown"
    return _GIT["c"]
