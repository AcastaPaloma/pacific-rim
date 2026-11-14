"""palette_set@v1 (research/R-002.md §3): validation, loading and the canonical profile hash.

Canonical `palette_sha256` (reconciled spec, "Readings"): SHA-256 of the UTF-8 bytes of the profile's colour list
serialised as compact JSON: lowercase "#rrggbb" strings in declared order, no spaces, e.g. ["#243a43","#2c434d"]."""
from __future__ import annotations

import hashlib
import json
import os
from dataclasses import dataclass

import numpy as np

from ..colour import ciede2000, hex_to_rgb, is_hex, srgb_u8_to_lab
from ..common import ErrorList, InvalidInput, load_json_file, rel_path

SCHEMA = "palette_set@v1"
LAB_TOL_DE00 = 1.0          # stored Lab vs Lab of the hex: 8-bit rounding of any Lab costs <= 0.88 dE00 (R-002 F1)
MAX_COLOURS = 24
RAMP_ROLES = [f"ramp_{i}" for i in range(8)]
DEFAULT_PATH = os.path.join("research", "R-002", "palettes.json")


def canonical_sha256(hex_list):
    s = json.dumps([h.lower() for h in hex_list], separators=(",", ":"), ensure_ascii=True)
    return hashlib.sha256(s.encode("utf-8")).hexdigest()


@dataclass
class Profile:
    id: str
    hexes: list
    roles: list
    rgb: np.ndarray
    lab: np.ndarray
    sha256: str
    accent_index: int | None

    @property
    def ramp_indices(self):
        return [self.roles.index(r) for r in RAMP_ROLES]


@dataclass
class PaletteSet:
    path: str
    sha256: str
    data: dict
    profiles: dict


def _check_colour(c, where, E, need_role):
    if not isinstance(c, dict):
        E.add(where, "colour entry must be an object", "{role, hex, lab}", type(c).__name__)
        return False
    if need_role and not isinstance(c.get("role"), str):
        E.add(f"{where}.role", "missing role", "ramp_0..ramp_7 | body | accent", repr(c.get("role")))
    h = c.get("hex")
    if not is_hex(h):
        E.add(f"{where}.hex", "hex must be lowercase #rrggbb", "#rrggbb", repr(h))
        return False
    lab = c.get("lab")
    if not (isinstance(lab, list) and len(lab) == 3 and all(isinstance(v, (int, float)) for v in lab)):
        E.add(f"{where}.lab", "lab must be [L, a, b]", "3 numbers", repr(lab))
        return False
    d = float(ciede2000(srgb_u8_to_lab(hex_to_rgb(h)), np.array(lab, dtype=float)))
    if d > LAB_TOL_DE00:
        E.add(f"{where}.lab", "Lab inconsistent with hex (hex is authoritative)", f"dE00 <= {LAB_TOL_DE00}",
              f"dE00 = {d:.3f}")
    return True


def check_palette_set(data, E: ErrorList):
    if not isinstance(data, dict):
        E.add("$", "top level must be an object", "object", type(data).__name__)
        return
    if data.get("schema") != SCHEMA:
        E.add("$.schema", "wrong schema string", SCHEMA, repr(data.get("schema")))
    if not isinstance(data.get("colour_space"), dict):
        E.add("$.colour_space", "missing colour_space", "{rgb, lab}", repr(data.get("colour_space")))
    if not isinstance(data.get("delta_e"), str) or "CIEDE2000" not in data.get("delta_e", ""):
        E.add("$.delta_e", "delta_e must name CIEDE2000", "CIEDE2000", repr(data.get("delta_e")))
    panels = data.get("panels")
    if not isinstance(panels, dict):
        E.add("$.panels", "missing panels object", "object", repr(panels)[:40])
        panels = {}
    for pid, p in panels.items():
        w = f"$.panels.{pid}"
        if not isinstance(p, dict) or not isinstance(p.get("palette"), list) or not p["palette"]:
            E.add(f"{w}.palette", "panel needs a non-empty palette list", "list", "absent")
            continue
        tot = 0.0
        for i, c in enumerate(p["palette"]):
            _check_colour(c, f"{w}.palette[{i}]", E, need_role=False)
            s = c.get("share") if isinstance(c, dict) else None
            if not isinstance(s, (int, float)) or not (0 <= s <= 1):
                E.add(f"{w}.palette[{i}].share", "share must be in [0, 1]", "[0, 1]", repr(s))
            else:
                tot += s
        if abs(tot - 1.0) > 0.01:
            E.add(f"{w}.palette", "shares must sum to 1 (+- 0.01)", 1.0, round(tot, 4))
    profiles = data.get("profiles")
    if not isinstance(profiles, dict) or not profiles:
        E.add("$.profiles", "missing profiles object", "object", repr(profiles)[:40])
        return
    for prid, pr in profiles.items():
        w = f"$.profiles.{prid}"
        if not isinstance(pr, dict):
            E.add(w, "profile must be an object", "object", type(pr).__name__)
            continue
        cols = pr.get("colours")
        if not isinstance(cols, list) or not cols:
            E.add(f"{w}.colours", "profile needs a colours list", "list", repr(cols)[:40])
            continue
        if len(cols) > MAX_COLOURS:
            E.add(f"{w}.colours", "profile exceeds the colour cap", f"<= {MAX_COLOURS}", len(cols))
        if pr.get("size") != len(cols):
            E.add(f"{w}.size", "size must equal the number of colours", len(cols), repr(pr.get("size")))
        for i, c in enumerate(cols):
            _check_colour(c, f"{w}.colours[{i}]", E, need_role=True)
        roles = [c.get("role") if isinstance(c, dict) else None for c in cols]
        ramp = [r for r in roles if isinstance(r, str) and r.startswith("ramp_")]
        if ramp != RAMP_ROLES:
            E.add(f"{w}.colours[].role", "ramp_0..ramp_7 must each appear once, in order", RAMP_ROLES, ramp)
        bad = [r for r in roles if not (r in RAMP_ROLES or r in ("body", "accent"))]
        if bad:
            E.add(f"{w}.colours[].role", "unknown role", "ramp_k | body | accent", bad)
        if roles.count("accent") > 1:
            E.add(f"{w}.colours[].role", "at most one accent", "<= 1", roles.count("accent"))
        hexes = [c.get("hex") for c in cols if isinstance(c, dict)]
        if len(set(hexes)) != len(hexes):
            E.add(f"{w}.colours", "duplicate hex values", "unique", len(hexes) - len(set(hexes)))
        ru = pr.get("ramp_uniform_L")
        if ru is not None:
            if not isinstance(ru, list) or len(ru) != 8:
                E.add(f"{w}.ramp_uniform_L", "must hold 8 entries", 8, repr(ru)[:40])
            else:
                for i, c in enumerate(ru):
                    _check_colour(c, f"{w}.ramp_uniform_L[{i}]", E, need_role=False)
        for m in pr.get("members", []) or []:
            if m not in panels:
                E.add(f"{w}.members", "member panel not in panels", "a panels key", m)


def load_palette_set(path=None, collect_only=False):
    path = path or os.path.join(_root(), DEFAULT_PATH)
    E = ErrorList(rel_path(path))
    data = None
    if not os.path.isfile(path):
        E.add("file", "file not found", "palette_set@v1 JSON", path)
    else:
        try:
            data = load_json_file(path)
        except Exception as e:  # noqa: BLE001 - report any decode failure as a contract error
            E.add("file", "not valid JSON", "JSON object", str(e)[:80])
    if data is not None:
        check_palette_set(data, E)
    if E:
        if collect_only:
            return None, E.errors
        raise InvalidInput(E.errors)
    profiles = {}
    for prid, pr in data["profiles"].items():
        hexes = [c["hex"] for c in pr["colours"]]
        roles = [c["role"] for c in pr["colours"]]
        rgb = np.stack([hex_to_rgb(h) for h in hexes])
        profiles[prid] = Profile(prid, hexes, roles, rgb, srgb_u8_to_lab(rgb), canonical_sha256(hexes),
                                 roles.index("accent") if "accent" in roles else None)
    from ..common import sha256_file
    ps = PaletteSet(path, sha256_file(path), data, profiles)
    return (ps, []) if collect_only else ps


def _root():
    from ..common import PROJECT_ROOT
    return PROJECT_ROOT


def validate_file(path):
    _, errs = load_palette_set(path, collect_only=True)
    return errs
