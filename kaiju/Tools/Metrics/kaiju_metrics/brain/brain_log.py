"""`brain_log@v1` (research/R-024.md section 4): JSON Lines, one run per file, header first.

Rows (field names as written by Game.AI.Harness.BrainLogWriter; reason codes are the contract `ReasonCode` 0..14):
  header         schema "brain_log@v1", run_id, seed, step_seconds, brain_period_steps, brain_id, config_hash,
                 size_class{H_m, mass_t}, d_eng_m, scenario
  decision       step, t, ent, pack, role, alive, x, y, heading_rad, speed, hp, state 0..5, prev_state, reason 0..14,
                 target, tgt_visible, tgt_mem_age_s, i_heading_rad, i_speed_frac, i_target, i_action, score_cur, score_best
  target_sample  step, t, ent, tkind, alive, x, y, heading_rad, hp
  hit            step, attacker, victim, damage, victim_hp_after
  death          step, ent
IDs are `ent:<u64>`. `step` is non-decreasing. `state`: 0 Idle 1 Approach 2 Flank 3 Charge 4 Retreat 5 Withdrawn.
`reason` 11 = voluntary retarget (R-024 section 4 table, revised to the contract numbering)."""
from __future__ import annotations

import math
import os
import re
from dataclasses import dataclass, field

from ..common import ContractError, ErrorList, strict_json_loads

SCHEMA = "brain_log@v1"
ENT_RE = re.compile(r"^ent:(0|[1-9][0-9]{0,19})$")
REASON_VOLUNTARY = 11
STATE_CHARGE = 3
KINDS = ("header", "decision", "target_sample", "hit", "death")
NUM = {
    "header": ("seed", "step_seconds", "brain_period_steps", "d_eng_m"),
    "decision": ("step", "t", "pack", "alive", "x", "y", "heading_rad", "speed", "hp", "state", "prev_state", "reason",
                 "tgt_visible", "tgt_mem_age_s", "i_heading_rad", "i_speed_frac", "i_action", "score_cur", "score_best"),
    "target_sample": ("step", "t", "alive", "x", "y", "heading_rad", "hp"),
    "hit": ("step", "damage", "victim_hp_after"),
    "death": ("step",),
}
ENTS = {"decision": ("ent", "target", "i_target"), "target_sample": ("ent",), "hit": ("attacker", "victim"),
        "death": ("ent",), "header": ()}


@dataclass
class BrainLog:
    path: str
    header: dict
    decisions: list = field(default_factory=list)   # dict rows
    samples: list = field(default_factory=list)
    hits: list = field(default_factory=list)
    deaths: list = field(default_factory=list)
    order: list = field(default_factory=list)       # (kind, row) in file order for the step grouping

    @property
    def brain_id(self):
        return self.header["brain_id"]


def ent_id(s):
    return int(s[4:])


def _isnum(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def load_brain_log(path):
    """Return (BrainLog | None, [ContractError]). Every violation is listed, never raised."""
    el = ErrorList(path)
    if not os.path.isfile(path):
        el.add("file", "input file does not exist", "brain_log@v1 .jsonl", path)
        return None, el.errors
    if os.path.getsize(path) == 0:
        el.add("file", "input file is empty", "header + rows", "0 bytes")
        return None, el.errors
    with open(path, "rb") as f:
        raw = f.read()
    if b"\r" in raw:
        el.add("file", "CR in file", "LF line endings only", "\\r found")
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as e:
        el.add("file", f"not UTF-8 ({e})", "UTF-8")
        return None, el.errors
    if not text.endswith("\n"):
        el.add("file", "last line not newline-terminated (truncated file?)", "trailing \\n")
    log = None
    last_step = None
    for ln, line in enumerate(text.split("\n"), 1):
        if not line:
            continue
        try:
            row = strict_json_loads(line)
        except Exception as e:  # noqa: BLE001
            el.add(ln, f"not valid JSON ({e})", "one JSON object per line")
            if len(el.errors) > 50:
                break
            continue
        if not isinstance(row, dict) or row.get("kind") not in KINDS:
            el.add(ln, "row has no known kind", "kind in " + "/".join(KINDS), str(row.get("kind") if isinstance(row, dict) else row))
            continue
        k = row["kind"]
        if log is None:
            if k != "header":
                el.add(ln, "first row must be the header", "kind header", k)
                break
            if row.get("schema") != SCHEMA:
                el.add(ln, "wrong schema", SCHEMA, str(row.get("schema")))
            for f in ("run_id", "brain_id", "config_hash", "scenario"):
                if not isinstance(row.get(f), str) or not row.get(f):
                    el.add(f"{ln}.{f}", "missing string", "non-empty string")
            for f in NUM["header"]:
                if not _isnum(row.get(f)):
                    el.add(f"{ln}.{f}", "missing or not finite", "number", str(row.get(f)))
            if _isnum(row.get("step_seconds")) and row["step_seconds"] <= 0:
                el.add(f"{ln}.step_seconds", "must be > 0", "> 0", str(row["step_seconds"]))
            if _isnum(row.get("brain_period_steps")) and row["brain_period_steps"] < 1:
                el.add(f"{ln}.brain_period_steps", "must be >= 1", ">= 1", str(row["brain_period_steps"]))
            log = BrainLog(path, row)
            continue
        if k == "header":
            el.add(ln, "second header row", "exactly one header")
            continue
        for f in NUM[k]:
            if not _isnum(row.get(f)):
                el.add(f"{ln}.{f}", f"{k}: missing or not a finite number", "number", str(row.get(f)))
        for f in ENTS[k]:
            v = row.get(f)
            if not isinstance(v, str) or not ENT_RE.match(v):
                el.add(f"{ln}.{f}", f"{k}: not a canonical ent:<u64> id", "ent:<u64>", str(v))
        if _isnum(row.get("step")):
            if last_step is not None and row["step"] < last_step:
                el.add(f"{ln}.step", "step decreases", f">= {last_step}", str(row["step"]))
            last_step = row["step"]
        if k == "decision":
            if _isnum(row.get("state")) and row["state"] not in range(6):
                el.add(f"{ln}.state", "state out of range", "0..5", str(row["state"]))
            if _isnum(row.get("reason")) and row["reason"] not in range(15):
                el.add(f"{ln}.reason", "reason out of range", "0..14", str(row["reason"]))
            if not isinstance(row.get("role"), str):
                el.add(f"{ln}.role", "missing", "solo/centre/wing")
            if _isnum(row.get("tgt_mem_age_s")) and row["tgt_mem_age_s"] < 0:
                el.add(f"{ln}.tgt_mem_age_s", "negative", ">= 0", str(row["tgt_mem_age_s"]))
        if len(el.errors) > 200:
            el.add("file", "too many errors, stopping", "")
            break
        if not el:
            log.order.append((k, row))
            {"decision": log.decisions, "target_sample": log.samples, "hit": log.hits, "death": log.deaths}[k].append(row)
    if log is None and not el:
        el.add("file", "no header row", "kind header first")
    if log is not None and not log.decisions and not el:
        el.add("file", "no decision rows (an empty run proves nothing)", ">= 1 decision")
    return (log if not el else None), el.errors
