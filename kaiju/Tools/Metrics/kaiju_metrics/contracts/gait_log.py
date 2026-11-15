"""gait_log@v1 (research/R-001.md §8): strict validation and loading into float64 arrays.

All errors are collected (file, line, key path, expected, found); any error makes the input INVALID_INPUT."""
from __future__ import annotations

import json
import math
import os
from dataclasses import dataclass, field

import numpy as np

from ..common import ErrorList, InvalidInput, rel_path, sha256_file, strict_json_loads

SCHEMA = "gait_log@v1"
MAX_ERRORS = 500

HEADER_NUM = ["H_m", "leg_length_m", "com_height_stand_m", "mass_kg", "I_yaw_kgm2", "fixed_dt_s", "physics_hz",
              "render_hz_target", "g_mps2", "mu_assumed", "reactor_power_W", "T_stride_nom_s", "fr_cruise_nom"]
HEADER_POSITIVE = ["H_m", "leg_length_m", "com_height_stand_m", "mass_kg", "I_yaw_kgm2", "fixed_dt_s", "physics_hz",
                   "render_hz_target", "g_mps2", "reactor_power_W", "T_stride_nom_s", "fr_cruise_nom"]
HEADER_STR = ["capture", "commit", "unit_id", "unit_kind", "camera_rig"]
STEP_FIELDS = ["n", "t", "com_p", "com_v", "root_p", "root_q", "yaw", "yaw_rate", "feet", "ext_f", "intent"]
FOOT_FIELDS = ["p", "q", "v", "contact", "fn", "ft", "cn"]
INTENT_FIELDS = ["move", "target", "effort", "cmd_speed_mps", "cmd_yaw_rate_rps"]
EVENT_FIELDS = {
    "contact_begin": ["foot", "contact_id", "fn", "foot_v"],
    "contact_end": ["foot", "contact_id"],
    "cam_impulse": ["foot", "contact_id", "amplitude_m", "dir", "source_n"],
    "footstep_audio": ["foot", "contact_id", "t_audio_s"],
    "intent_change": ["move", "cmd_speed_mps", "cmd_yaw_rate_rps"],
}
FRAME_FIELDS = ["f", "t_render", "n_latest", "cam_p", "cam_q", "cam_impulse_offset_m"]
G_REF = 9.81


def _num(x):
    return isinstance(x, (int, float)) and not isinstance(x, bool) and math.isfinite(x)


def _vec(x, n=3):
    return isinstance(x, list) and len(x) == n and all(_num(v) for v in x)


@dataclass
class GaitLog:
    path: str
    sha256: str
    header: dict
    n: np.ndarray
    t: np.ndarray
    com_p: np.ndarray
    com_v: np.ndarray
    root_p: np.ndarray
    root_q: np.ndarray
    yaw: np.ndarray
    yaw_rate: np.ndarray
    ext_f: np.ndarray
    cmd_speed: np.ndarray
    cmd_yaw_rate: np.ndarray
    move: list
    feet: list                      # per foot: dict of arrays p, q, v, contact, fn, ft, cn
    events: list = field(default_factory=list)
    frames: list = field(default_factory=list)
    warnings: list = field(default_factory=list)

    @property
    def capture(self):
        return self.header["capture"]

    @property
    def dt(self):
        return float(self.header["fixed_dt_s"])


def _check_header(h, E: ErrorList, line):
    w = f"line {line} header"
    if h.get("schema") != SCHEMA:
        E.add(f"{w}.schema", "wrong schema string", SCHEMA, h.get("schema"))
    for k in HEADER_STR:
        if not isinstance(h.get(k), str) or not h.get(k):
            E.add(f"{w}.{k}", "missing or not a non-empty string", "string", repr(h.get(k)))
    if h.get("unit_kind") not in ("mech", "kaiju"):
        E.add(f"{w}.unit_kind", "invalid unit_kind", "mech|kaiju", repr(h.get("unit_kind")))
    for k in HEADER_NUM:
        if not _num(h.get(k)):
            E.add(f"{w}.{k}", "missing or not a finite number", "number", repr(h.get(k)))
        elif k in HEADER_POSITIVE and h[k] <= 0:
            E.add(f"{w}.{k}", "must be > 0", "> 0", h[k])
    if _num(h.get("g_mps2")) and abs(h["g_mps2"] - G_REF) > 1e-6:
        E.add(f"{w}.g_mps2", "g must be 9.81 m/s^2 (SI units, physics truth)", G_REF, h["g_mps2"])
    if _num(h.get("fixed_dt_s")) and _num(h.get("physics_hz")) and h["fixed_dt_s"] > 0:
        if abs(h["fixed_dt_s"] * h["physics_hz"] - 1.0) > 1e-6:
            E.add(f"{w}.physics_hz", "physics_hz * fixed_dt_s must be 1 (units: s and Hz)", 1.0,
                  h["fixed_dt_s"] * h["physics_hz"])
        if h["fixed_dt_s"] > 1.0:
            E.add(f"{w}.fixed_dt_s", "implausible step (seconds expected, not ms)", "<= 1 s", h["fixed_dt_s"])
    if _num(h.get("H_m")):
        for k in ("leg_length_m", "com_height_stand_m"):
            if _num(h.get(k)) and h[k] > h["H_m"]:
                E.add(f"{w}.{k}", "longer than the unit height H_m (units?)", f"<= {h['H_m']}", h[k])
    ids = h.get("foot_ids")
    if not isinstance(ids, list) or not ids or not all(isinstance(i, str) for i in ids) or len(set(ids)) != len(ids):
        E.add(f"{w}.foot_ids", "must be a non-empty list of unique strings", "[str, ...]", repr(ids))
        ids = None
    feet = h.get("feet")
    if not isinstance(feet, list):
        E.add(f"{w}.feet", "missing feet array", "list", repr(feet))
    else:
        if ids is not None and [f.get("id") if isinstance(f, dict) else None for f in feet] != ids:
            E.add(f"{w}.feet", "feet ids must equal foot_ids in order", ids,
                  [f.get("id") if isinstance(f, dict) else None for f in feet])
        for i, f in enumerate(feet):
            if not isinstance(f, dict):
                E.add(f"{w}.feet[{i}]", "not an object", "object", type(f).__name__)
                continue
            for k in ("length_m", "width_m"):
                if not _num(f.get(k)) or f.get(k) <= 0:
                    E.add(f"{w}.feet[{i}].{k}", "missing or not > 0", "> 0", repr(f.get(k)))
            if _num(h.get("H_m")) and _num(f.get("length_m")) and f["length_m"] > h["H_m"]:
                E.add(f"{w}.feet[{i}].length_m", "foot longer than the unit (units?)", f"<= {h['H_m']}", f["length_m"])
            sp = f.get("sole_points_local")
            if not (isinstance(sp, list) and len(sp) == 5 and all(_vec(p) for p in sp)):
                E.add(f"{w}.feet[{i}].sole_points_local", "must be five [x, y, z] points", "5 x 3 numbers",
                      repr(sp)[:80])
    th = h.get("thresholds")
    if not isinstance(th, dict) or not _num(th.get("F_on_N")) or not _num(th.get("F_off_N")):
        E.add(f"{w}.thresholds", "must hold F_on_N and F_off_N", "{F_on_N, F_off_N}", repr(th)[:80])
    elif not (th["F_on_N"] > th["F_off_N"] > 0):
        E.add(f"{w}.thresholds", "need F_on_N > F_off_N > 0", "F_on > F_off > 0", repr(th))
    return ids


def _float32_like(values):
    """True when positions look float32-quantised (|x - f32(x)| tiny relative to the float32 ulp)."""
    v = np.asarray(values, dtype=np.float64)
    v = v[np.abs(v) >= 16.0]
    v = v[(v * 1024.0) != np.round(v * 1024.0)]       # dyadic values are exact in both widths: uninformative
    if len(v) < 100:
        return False, None
    f32 = v.astype(np.float32).astype(np.float64)
    ulp = np.spacing(np.abs(v).astype(np.float32)).astype(np.float64)
    r = np.abs(v - f32) / ulp
    med = float(np.median(r))
    return med < 0.05, med


def load_gait_log(path, collect_only=False):
    """Validate and load. Raises InvalidInput (all errors) unless collect_only, which returns (log|None, errors)."""
    E = ErrorList(rel_path(path))
    if not os.path.isfile(path):
        E.add("file", "file not found", "existing gait_log@v1 .jsonl", path)
        if collect_only:
            return None, E.errors
        raise InvalidInput(E.errors)
    with open(path, "rb") as f:
        raw = f.read()
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as e:
        E.add("file", "not UTF-8", "UTF-8", str(e))
        if collect_only:
            return None, E.errors
        raise InvalidInput(E.errors)
    lines = text.split("\n")
    if lines and lines[-1] == "":
        lines = lines[:-1]
    header, ids = None, None
    steps, events, frames = [], [], []
    last_step_n = None
    seen_n = set()
    begins = {}
    for ln, line in enumerate(lines, start=1):
        if len(E.errors) >= MAX_ERRORS:
            E.add(f"line {ln}", f"stopped after {MAX_ERRORS} errors")
            break
        try:
            r = strict_json_loads(line)
        except (json.JSONDecodeError, ValueError) as e:
            E.add(f"line {ln}", "not a valid JSON record (truncated file or NaN/Infinity?)", "one JSON object", str(e)[:80])
            continue
        if not isinstance(r, dict):
            E.add(f"line {ln}", "record is not an object", "object", type(r).__name__)
            continue
        rec = r.get("rec")
        if ln == 1 and rec != "header":
            E.add("line 1", "first line must be the header", "rec=header", repr(rec))
        if rec == "header":
            if header is not None:
                E.add(f"line {ln}", "more than one header", "exactly one header on line 1", "another header")
                continue
            header = r
            ids = _check_header(r, E, ln)
        elif rec == "step":
            if header is None:
                continue
            ok = True
            for k in STEP_FIELDS:
                if k not in r:
                    E.add(f"line {ln} step.{k}", "missing field", k, "absent")
                    ok = False
            if not ok:
                continue
            if not isinstance(r["n"], int) or isinstance(r["n"], bool):
                E.add(f"line {ln} step.n", "n must be an integer", "int", repr(r["n"]))
                continue
            if last_step_n is not None and r["n"] != last_step_n + 1:
                E.add(f"line {ln} step.n", "step index not contiguous (gap or reorder)", last_step_n + 1, r["n"])
            dt = header.get("fixed_dt_s")
            if not _num(r["t"]):
                E.add(f"line {ln} step.t", "not a finite number", "float s", repr(r["t"]))
            elif _num(dt) and abs(r["t"] - r["n"] * dt) > 1e-9:
                E.add(f"line {ln} step.t", "t must equal n * fixed_dt_s within 1e-9 s", r["n"] * dt, r["t"])
            for k in ("com_p", "com_v", "root_p"):
                if not _vec(r[k]):
                    E.add(f"line {ln} step.{k}", "must be a 3-vector of finite numbers", "[x, y, z]", repr(r[k])[:60])
            if not _vec(r["root_q"], 4):
                E.add(f"line {ln} step.root_q", "must be a quaternion [x, y, z, w]", "4 numbers", repr(r["root_q"])[:60])
            elif abs(math.sqrt(sum(v * v for v in r["root_q"])) - 1) > 1e-3:
                E.add(f"line {ln} step.root_q", "quaternion not unit length", "|q| = 1 +- 1e-3",
                      round(math.sqrt(sum(v * v for v in r["root_q"])), 6))
            for k in ("yaw", "yaw_rate", "ext_f"):
                if not _num(r[k]):
                    E.add(f"line {ln} step.{k}", "not a finite number", "number", repr(r[k]))
            if _num(r["yaw"]) and not (-math.pi - 1e-9 < r["yaw"] <= math.pi + 1e-9):
                E.add(f"line {ln} step.yaw", "yaw must be radians wrapped to (-pi, pi]", "(-pi, pi]", r["yaw"])
            if _num(r["ext_f"]) and r["ext_f"] < 0:
                E.add(f"line {ln} step.ext_f", "magnitude must be >= 0", ">= 0", r["ext_f"])
            it = r["intent"]
            if not isinstance(it, dict) or any(k not in it for k in INTENT_FIELDS):
                E.add(f"line {ln} step.intent", "intent must hold " + ", ".join(INTENT_FIELDS), "object", repr(it)[:60])
            elif not _num(it["cmd_speed_mps"]) or not _num(it["cmd_yaw_rate_rps"]):
                E.add(f"line {ln} step.intent", "cmd_speed_mps / cmd_yaw_rate_rps must be numbers", "numbers",
                      repr(it)[:60])
            fl = r["feet"]
            if not isinstance(fl, list) or (ids is not None and len(fl) != len(ids)):
                E.add(f"line {ln} step.feet", "feet array length must match header foot_ids",
                      len(ids) if ids else "len(foot_ids)", len(fl) if isinstance(fl, list) else repr(fl)[:40])
                continue
            for i, ft in enumerate(fl):
                wf = f"line {ln} step.feet[{i}]"
                if not isinstance(ft, dict) or any(k not in ft for k in FOOT_FIELDS):
                    E.add(wf, "foot entry must hold " + ", ".join(FOOT_FIELDS), "object", repr(ft)[:60])
                    continue
                for k in ("p", "v", "ft"):
                    if not _vec(ft[k]):
                        E.add(f"{wf}.{k}", "must be a 3-vector of finite numbers", "[x, y, z]", repr(ft[k])[:60])
                if not _vec(ft["q"], 4):
                    E.add(f"{wf}.q", "must be a quaternion [x, y, z, w]", "4 numbers", repr(ft["q"])[:60])
                elif abs(math.sqrt(sum(v * v for v in ft["q"])) - 1) > 1e-3:
                    E.add(f"{wf}.q", "quaternion not unit length", "|q| = 1 +- 1e-3",
                          round(math.sqrt(sum(v * v for v in ft["q"])), 6))
                if ft["contact"] not in (0, 1) or isinstance(ft["contact"], bool):
                    E.add(f"{wf}.contact", "contact flag must be 0 or 1", "0|1", repr(ft["contact"]))
                if not _num(ft["fn"]) or ft["fn"] < 0:
                    E.add(f"{wf}.fn", "normal force must be a finite number >= 0 (N)", ">= 0", repr(ft["fn"]))
                cn = ft["cn"]
                if cn is not None:
                    if not _vec(cn):
                        E.add(f"{wf}.cn", "contact normal must be a unit 3-vector or null", "[x, y, z] | null",
                              repr(cn)[:60])
                    elif abs(math.sqrt(sum(v * v for v in cn)) - 1) > 1e-3:
                        E.add(f"{wf}.cn", "contact normal not unit length", "|cn| = 1 +- 1e-3",
                              round(math.sqrt(sum(v * v for v in cn)), 6))
            seen_n.add(r["n"])
            last_step_n = r["n"]
            steps.append(r)
        elif rec == "event":
            if header is None:
                continue
            typ = r.get("type")
            if typ not in EVENT_FIELDS:
                E.add(f"line {ln} event.type", "unknown event type", "|".join(EVENT_FIELDS), repr(typ))
                continue
            miss = [k for k in ["n", "t"] + EVENT_FIELDS[typ] if k not in r]
            if miss:
                E.add(f"line {ln} event({typ})", "missing fields " + ", ".join(miss), "all fields", "absent")
                continue
            if r["n"] != last_step_n:
                E.add(f"line {ln} event.n", "event must follow the step record of the same n", last_step_n, r["n"])
            if "foot" in r and ids is not None and r["foot"] not in ids:
                E.add(f"line {ln} event.foot", "unknown foot id", ids, repr(r["foot"]))
            if typ == "contact_begin":
                begins[(r["n"], r["foot"], r["contact_id"])] = ln
            if typ == "cam_impulse":
                events.append((ln, r))
                continue
            events.append((ln, r))
        elif rec == "frame":
            if header is None:
                continue
            miss = [k for k in FRAME_FIELDS if k not in r]
            if miss:
                E.add(f"line {ln} frame", "missing fields " + ", ".join(miss), "all fields", "absent")
                continue
            frames.append(r)
        else:
            E.add(f"line {ln} rec", "unknown record kind", "header|step|event|frame", repr(rec))
    if header is None:
        E.add("line 1", "no header record", "rec=header on line 1", "none")
    if header is not None and not steps:
        E.add("file", "no step records", ">= 1 step", 0)
    # cam_impulse.source_n must reference a real contact_begin
    begin_n = {(k[0], k[1]) for k in begins}
    for ln, r in events:
        if r.get("type") == "cam_impulse":
            if (r["source_n"], r["foot"]) not in begin_n:
                E.add(f"line {ln} cam_impulse.source_n", "does not reference a contact_begin of that foot",
                      "n of an existing contact_begin", r["source_n"])
    if E:
        if collect_only:
            return None, E.errors
        raise InvalidInput(E.errors)
    log = _to_arrays(path, raw, header, steps, [r for _, r in events], frames, E)
    if E:
        if collect_only:
            return None, E.errors
        raise InvalidInput(E.errors)
    return (log, []) if collect_only else log


def _to_arrays(path, raw, header, steps, events, frames, E: ErrorList):
    nf = len(header["foot_ids"])
    A = lambda k: np.array([s[k] for s in steps], dtype=np.float64)
    feet = []
    for i in range(nf):
        cn = np.array([s["feet"][i]["cn"] if s["feet"][i]["cn"] is not None else [np.nan] * 3 for s in steps],
                      dtype=np.float64)
        feet.append({
            "id": header["foot_ids"][i],
            "p": np.array([s["feet"][i]["p"] for s in steps], dtype=np.float64),
            "q": np.array([s["feet"][i]["q"] for s in steps], dtype=np.float64),
            "v": np.array([s["feet"][i]["v"] for s in steps], dtype=np.float64),
            "contact": np.array([s["feet"][i]["contact"] for s in steps], dtype=np.int8),
            "fn": np.array([s["feet"][i]["fn"] for s in steps], dtype=np.float64),
            "ft": np.array([s["feet"][i]["ft"] for s in steps], dtype=np.float64),
            "cn": cn,
        })
    log = GaitLog(
        path=path, sha256=_sha(raw), header=header,
        n=np.array([s["n"] for s in steps], dtype=np.int64), t=A("t"),
        com_p=A("com_p"), com_v=A("com_v"), root_p=A("root_p"), root_q=A("root_q"),
        yaw=A("yaw"), yaw_rate=A("yaw_rate"), ext_f=A("ext_f"),
        cmd_speed=np.array([s["intent"]["cmd_speed_mps"] for s in steps], dtype=np.float64),
        cmd_yaw_rate=np.array([s["intent"]["cmd_yaw_rate_rps"] for s in steps], dtype=np.float64),
        move=[s["intent"]["move"] for s in steps], feet=feet, events=events, frames=frames)
    # float64 positions (R-001 §5: float32 spacing is up to 8 % of the slip threshold)
    pos = np.concatenate([log.com_p.ravel(), log.root_p.ravel()] + [f["p"].ravel() for f in feet])
    is32, med = _float32_like(pos)
    if is32:
        E.add("steps.*.p", "positions look float32-quantised; gait_log@v1 requires absolute float64",
              "float64 (median |x - f32(x)| / ulp32 ~ 0.25)", f"median ratio {med:.4f}")
    # unit sanity: com_v must be the derivative of com_p (catches km/h, cm, swapped fields)
    if len(log.t) >= 5:
        dpdt = (log.com_p[2:] - log.com_p[:-2]) / (2 * log.dt)
        err = np.linalg.norm(dpdt - log.com_v[1:-1], axis=1)
        scale = np.median(np.linalg.norm(log.com_v, axis=1))
        if np.median(err) > 0.05 * scale + 0.01:
            E.add("steps.*.com_v", "com_v is not the time derivative of com_p (units or frame mismatch)",
                  f"median |dp/dt - v| <= {0.05 * scale + 0.01:.3g} m/s", f"{np.median(err):.3g} m/s")
        dyaw = np.diff(np.unwrap(log.yaw)) / log.dt
        yr = 0.5 * (log.yaw_rate[1:] + log.yaw_rate[:-1])
        if np.median(np.abs(dyaw - yr)) > 0.01 + 0.05 * np.median(np.abs(yr)):
            log.warnings.append("YAW_RATE_INCONSISTENT: logged yaw_rate differs from d(yaw)/dt; the tool uses d(yaw)/dt")
    return log


def _sha(raw: bytes):
    import hashlib
    return hashlib.sha256(raw).hexdigest()


def validate_file(path):
    """Standalone validation entry: returns list of ContractError (empty when valid)."""
    _, errs = load_gait_log(path, collect_only=True)
    return errs
