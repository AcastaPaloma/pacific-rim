"""geo_tile@v1 manifest completeness (R-004 §3.3 rules; M-005.8; T-003 C9, C18).

The Unity importer (World.Geo `GeoManifestValidator`) applies the same rules; both refuse a tile
whose manifest lacks any field. Keep the two lists identical.
"""
from __future__ import annotations

from pathlib import Path

from .common import sha256_file

TOP_FIELDS = ("spec", "tile", "level", "i", "j", "crs", "vertical", "bounds_baytm", "bounds_wgs84",
              "origin_baytm_m", "water_level_navd88_m", "build", "layers")
BUILD_FIELDS = ("script", "git", "built_utc", "inputs_sha256")
LAYER_FIELDS = ("layer", "file", "sha256", "sources")
SOURCE_FIELDS = ("product", "item", "url", "licence", "attribution", "fetched_utc", "source_sha256")
REQUIRED_LAYERS = ("terrain", "terrain_r16", "water", "buildings", "roads", "faults")
LICENCES = ("US-Gov-PD", "ODbL-1.0", "PDDL-1.0", "CC0-1.0", "CC-BY-4.0", "LicenseRef-facts-cited")  # SPDX ids or US-Gov-PD


def _empty(v) -> bool:
    return v is None or (isinstance(v, str) and not v.strip()) or (isinstance(v, (list, dict)) and len(v) == 0)


def validate(man: dict, tile_dir: Path | None = None) -> list[str]:
    """Returns a list of problems (empty = complete). Checks output hashes when tile_dir is given."""
    errs = []
    for k in TOP_FIELDS:
        if k not in man or _empty(man[k]):
            errs.append(f"missing top-level field '{k}'")
    if man.get("spec") != "geo_tile@v1":
        errs.append(f"spec is {man.get('spec')!r}, expected 'geo_tile@v1'")
    b = man.get("build") or {}
    for k in BUILD_FIELDS:
        if _empty(b.get(k)):
            errs.append(f"build.{k} missing")
    names = []
    for i, L in enumerate(man.get("layers") or []):
        nm = L.get("layer", f"#{i}")
        names.append(nm)
        for k in LAYER_FIELDS:
            if k not in L or _empty(L[k]):
                errs.append(f"layer {nm}: missing '{k}'")
        for j, s in enumerate(L.get("sources") or []):
            for k in SOURCE_FIELDS:
                if _empty(s.get(k)):
                    errs.append(f"layer {nm} source {j}: missing '{k}'")
            if s.get("licence") and s["licence"] not in LICENCES:
                errs.append(f"layer {nm} source {j}: licence {s['licence']!r} is not an accepted SPDX id / US-Gov-PD")
        if tile_dir is not None and L.get("file") and L.get("sha256"):
            p = tile_dir / L["file"]
            if not p.exists():
                errs.append(f"layer {nm}: file {L['file']} missing")
            elif sha256_file(p) != L["sha256"]:
                errs.append(f"layer {nm}: sha256 mismatch for {L['file']}")
    for r in REQUIRED_LAYERS:
        if r not in names:
            errs.append(f"required layer '{r}' absent")
    return errs


def attributions(man: dict) -> list[str]:
    out = set()
    for L in man.get("layers", []):
        for s in L.get("sources", []):
            if s.get("attribution"):
                out.add(s["attribution"])
            if s.get("attribution_notice"):
                out.add(s["attribution_notice"])
    return sorted(out)
