"""Loading gait captures for a metric run and the header-normaliser guard (C9)."""
from __future__ import annotations

import os

from ..common import InvalidInput
from ..contracts.gait_log import load_gait_log
from .segment import hysteresis, contacts_from_state


def expand_inputs(inputs):
    files = []
    for p in inputs:
        if os.path.isdir(p):
            files += sorted(os.path.join(p, f) for f in os.listdir(p) if f.endswith(".jsonl"))
        else:
            files.append(p)
    return files


def load_logs(inputs):
    files = expand_inputs(inputs)
    errs, logs = [], []
    if not files:
        from ..common import ContractError
        raise InvalidInput([ContractError("inputs", "-", "no gait_log@v1 files given", ".jsonl files", "none")])
    for f in files:
        lg, e = load_gait_log(f, collect_only=True)
        errs += e
        if lg is not None:
            logs.append(lg)
    if errs:
        raise InvalidInput(errs)
    return logs


def header_value(h, key):
    if key == "foot_length_m":
        return min(f["length_m"] for f in h["feet"])
    if key == "foot_width_m":
        return min(f["width_m"] for f in h["feet"])
    if key in ("F_on_N", "F_off_N"):
        return h["thresholds"][key]
    return h.get(key)


def check_normalisers(report, logs, reg, g):
    """Flag header normalisers that differ from the registry's reference unit; report the contact-count effect of
    emitter F_on / F_off thresholds that differ from 0.03 m g / 0.01 m g (the tool always uses the spec values)."""
    ref = reg.get("reference_unit") or {}
    tol = ref.get("tolerance_rel", 0.0)
    norm = {}
    for lg in logs:
        h = lg.header
        m = h["mass_kg"]
        vals = {k: header_value(h, k) for k in ref.get("values", {})}
        diffs = {}
        for k, rv in ref.get("values", {}).items():
            v = vals[k]
            if v is not None and rv and abs(v - rv) > tol * abs(rv):
                diffs[k] = {"header": v, "reference": rv}
        f_on_spec, f_off_spec = reg.p("F_on_mg") * m * g, reg.p("F_off_mg") * m * g
        norm[os.path.basename(lg.path)] = {
            "capture": lg.capture, "unit_id": h["unit_id"], "l_m": h["leg_length_m"], "h_m": h["com_height_stand_m"],
            "m_kg": m, "L_f_m": vals.get("foot_length_m"), "W_f_m": vals.get("foot_width_m"), "g": g,
            "T_nom_s": h["T_stride_nom_s"], "fr_cruise_nom": h["fr_cruise_nom"], "P_W": h["reactor_power_W"],
            "F_on_N_spec": f_on_spec, "F_off_N_spec": f_off_spec,
            "F_on_N_header": h["thresholds"]["F_on_N"], "F_off_N_header": h["thresholds"]["F_off_N"]}
        if diffs:
            report.flag("NON_REFERENCE_NORMALISERS", {"file": os.path.basename(lg.path), "differs": diffs})
        if (abs(h["thresholds"]["F_on_N"] - f_on_spec) > tol * f_on_spec
                or abs(h["thresholds"]["F_off_N"] - f_off_spec) > tol * f_off_spec):
            n_spec = n_hdr = 0
            for f in lg.feet:
                n_spec += len(contacts_from_state(hysteresis(f["fn"], f_on_spec, f_off_spec), 0, "", f["fn"], 0))
                n_hdr += len(contacts_from_state(hysteresis(f["fn"], h["thresholds"]["F_on_N"],
                                                            h["thresholds"]["F_off_N"]), 0, "", f["fn"], 0))
            report.flag("NON_SPEC_CONTACT_THRESHOLDS", {"file": os.path.basename(lg.path),
                                                        "contacts_with_spec_thresholds": n_spec,
                                                        "contacts_with_header_thresholds": n_hdr,
                                                        "note": "the tool uses 0.03 m g / 0.01 m g (R-001 §5)"})
        for w in lg.warnings:
            report.warn(f"{os.path.basename(lg.path)}: {w}")
    report.normalisers = norm
