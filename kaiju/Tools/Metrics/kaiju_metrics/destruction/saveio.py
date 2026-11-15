"""Reader for the Core save envelope (`kaiju-save`, schema 1) and its `destruction@v1` section.

Envelope (as in Assets/Game/World/Destruction/Tests/EditMode/Golden/destruction_v1.json):
  {"format":"kaiju-save","schema":1,"build":"...","sections":[
   {"name":"destruction","version":1,"records":[
     {"key":"_meta","fields":{"model_rev":2}},
     {"key":"bld:<osm-way|osm-relation|landmark>:<u64>","fields":{"state":3,"e_cum_j":{"f64":"<16 hex>"},
        "debris_kg":{"f64":"<16 hex>"},"last_step":585,"cause":3}}, ...]}, ...]}
Float64 fields are IEEE-754 bit patterns as 16 upper-case hex digits. Empty, missing or malformed input is an
InvalidInput error, never a pass (DEF-T002-arch-1)."""
from __future__ import annotations

import json
import os
import re

from ..common import ErrorList, InvalidInput, strict_json_loads

HEX64 = re.compile(r"^[0-9A-F]{16}$")
SECTION = "destruction"
FLOAT_FIELDS = ("e_cum_j", "debris_kg")
INT_FIELDS = ("state", "last_step", "cause")


def canonical_bytes(obj) -> bytes:
    """Canonical bytes of a JSON value: sorted keys, no whitespace, UTF-8."""
    return json.dumps(obj, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


class SaveFile:
    def __init__(self, path, data):
        self.path = path
        self.data = data
        self.sections = {s["name"]: s for s in data["sections"]}
        d = self.sections[SECTION]
        self.section = d
        self.meta = {}
        self.records = {}          # key -> fields (dict), insertion = file order
        for r in d["records"]:
            if r["key"] == "_meta":
                self.meta = r["fields"]
            else:
                self.records[r["key"]] = r["fields"]

    def section_bytes(self, name=SECTION) -> bytes:
        return canonical_bytes(self.sections[name])

    def other_sections_bytes(self) -> bytes:
        return canonical_bytes([self.sections[n] for n in sorted(self.sections) if n != SECTION])


def load_save(path) -> SaveFile:
    el = ErrorList(path)
    if not os.path.isfile(path):
        el.add("file", "input file does not exist", "a kaiju-save JSON file", path)
        raise InvalidInput(el.errors)
    with open(path, "rb") as f:
        raw = f.read()
    if not raw.strip():
        el.add("file", "input file is empty", "a kaiju-save JSON file", "0 bytes")
        raise InvalidInput(el.errors)
    try:
        data = strict_json_loads(raw.decode("utf-8"))
    except Exception as e:  # noqa: BLE001
        el.add("file", f"not valid JSON ({e})", "a kaiju-save JSON file")
        raise InvalidInput(el.errors)
    if not isinstance(data, dict) or data.get("format") != "kaiju-save" or data.get("schema") != 1:
        el.add("envelope", "not a Core save envelope", 'format "kaiju-save", schema 1',
               f"{data.get('format') if isinstance(data, dict) else type(data).__name__}")
        raise InvalidInput(el.errors)
    secs = data.get("sections")
    if not isinstance(secs, list) or not all(isinstance(s, dict) and isinstance(s.get("name"), str) for s in secs):
        el.add("sections", "sections must be a list of named sections", "list of {name, version, records}")
        raise InvalidInput(el.errors)
    names = [s["name"] for s in secs]
    if len(set(names)) != len(names):
        el.add("sections", "duplicate section names", "unique names", str(names))
    sec = next((s for s in secs if s["name"] == SECTION), None)
    if sec is None:
        el.add("sections", f"no '{SECTION}' section", f"section {SECTION}@v1", str(names))
        raise InvalidInput(el.errors)
    if sec.get("version") != 1:
        el.add("destruction.version", "unsupported section version", "1", str(sec.get("version")))
    recs = sec.get("records")
    if not isinstance(recs, list):
        el.add("destruction.records", "records must be a list", "list")
        raise InvalidInput(el.errors)
    seen = set()
    n_data = 0
    for i, r in enumerate(recs):
        w = f"destruction.records[{i}]"
        if not isinstance(r, dict) or not isinstance(r.get("key"), str) or not isinstance(r.get("fields"), dict):
            el.add(w, "record needs key (string) and fields (object)")
            continue
        k, fl = r["key"], r["fields"]
        if k in seen:
            el.add(w, "duplicate record key", "unique", k)
        seen.add(k)
        if k == "_meta":
            if not isinstance(fl.get("model_rev"), int):
                el.add(w, "_meta.model_rev must be an integer", "int", str(fl.get("model_rev")))
            continue
        n_data += 1
        for f in INT_FIELDS:
            if not isinstance(fl.get(f), int) or isinstance(fl.get(f), bool):
                el.add(f"{w}[{k}].{f}", "missing or not an integer", "int", str(fl.get(f)))
        if isinstance(fl.get("state"), int) and not 0 <= fl["state"] <= 4:
            el.add(f"{w}[{k}].state", "state out of range", "0..4", str(fl["state"]))
        for f in FLOAT_FIELDS:
            v = fl.get(f)
            if not (isinstance(v, dict) and isinstance(v.get("f64"), str) and HEX64.match(v["f64"])):
                el.add(f"{w}[{k}].{f}", "missing or not {f64: 16 upper-case hex}", '{"f64":"<16 hex>"}', str(v))
    if n_data == 0 and not el:
        el.add("destruction.records", "section holds no building records (an empty section proves nothing)",
               ">= 1 record", "0")
    if el:
        raise InvalidInput(el.errors)
    return SaveFile(path, data)
