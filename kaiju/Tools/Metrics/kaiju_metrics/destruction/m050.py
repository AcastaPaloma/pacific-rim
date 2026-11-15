"""M-050@v1 Persistent destruction save round trip (research/R-021.md §4, R-026 §1).

Tool input: exactly two Core-envelope save files, `before` (the capture) and `after` (the same state after
write / read / restore / capture, or after a migration). The C# side (Core SaveCodec, de-DE / tr-TR, v1->v2
migration, stream-failure injection) produces the files; this tool only compares them:
  * canonical bytes of the `destruction@v1` section (sorted-key compact UTF-8 JSON): count of differing bytes
    (positional over the common length plus the length difference);
  * per-ID `state`, `e_cum_j` bits and `debris_kg` bits, plus record set equality (missing / extra IDs);
  * all other sections byte-identical.
FAIL lists the count and the first differing IDs (sorted ordinal)."""
from __future__ import annotations

from ..common import InvalidInput, ErrorList, sha256_file
from ..report import part
from .saveio import load_save

FIRST_N = 10


def diff_bytes(a: bytes, b: bytes) -> int:
    n = min(len(a), len(b))
    d = sum(1 for i in range(n) if a[i] != b[i])
    return d + abs(len(a) - len(b))


def compare(a, b):
    """Return dict of counts and first differing IDs between two SaveFile objects."""
    ka, kb = set(a.records), set(b.records)
    missing = sorted(ka - kb)            # in before, absent after
    extra = sorted(kb - ka)
    mism = {"state": [], "e_cum_j": [], "debris_kg": []}
    for k in sorted(ka & kb):
        fa, fb = a.records[k], b.records[k]
        if fa["state"] != fb["state"]:
            mism["state"].append(k)
        for f in ("e_cum_j", "debris_kg"):
            if fa[f]["f64"] != fb[f]["f64"]:
                mism[f].append(k)
    ids = sorted(set(missing) | set(extra) | {k for v in mism.values() for k in v})
    return {"missing": missing, "extra": extra, "mismatch": mism, "first_ids": ids[:FIRST_N],
            "n_records": (len(ka), len(kb))}


def run(rep, inputs, reg, ratings=None, out_dir=None, images=False, options=None):
    if len(inputs) != 2:
        el = ErrorList("")
        el.add("arguments", "M-050 compares exactly two save files: <before> <after>", "2 inputs",
               str(len(inputs)))
        raise InvalidInput(el.errors)
    a, b = load_save(inputs[0]), load_save(inputs[1])
    rep.add_input(inputs[0], sha256_file(inputs[0]), "save before (kaiju-save, destruction@v1)")
    rep.add_input(inputs[1], sha256_file(inputs[1]), "save after (kaiju-save, destruction@v1)")
    rep.coverage["required"] = ["before", "after"]
    rep.coverage["provided"] = ["before", "after"]
    c = compare(a, b)
    nbytes = diff_bytes(a.section_bytes(), b.section_bytes())
    other = diff_bytes(a.other_sections_bytes(), b.other_sections_bytes())
    bit_mm = len(c["mismatch"]["state"]) + len(c["mismatch"]["e_cum_j"])
    ids_note = ", ".join(c["first_ids"]) if c["first_ids"] else None
    diag = {"first_differing_ids": c["first_ids"], "missing_ids": len(c["missing"]), "extra_ids": len(c["extra"]),
            "where": ids_note, "records": {"before": c["n_records"][0], "after": c["n_records"][1]},
            "model_rev": {"before": a.meta.get("model_rev"), "after": b.meta.get("model_rev")}}
    z = reg.t("M-050.bytes", "differing_bytes_max")
    rep.add("M-050.bytes", parts=[part("differing_bytes", nbytes, "<=", z, unit="bytes")], diagnostics=diag,
            note=None if nbytes == 0 else f"first differing ids: {ids_note}")
    t = reg.t("M-050.fields", "mismatch_max")
    rep.add("M-050.fields", diagnostics=diag, parts=[
        part("state_mismatch", len(c["mismatch"]["state"]), "<=", t, unit="ids"),
        part("e_cum_j_bit_mismatch", len(c["mismatch"]["e_cum_j"]), "<=", t, unit="ids"),
        part("debris_kg_bit_mismatch", len(c["mismatch"]["debris_kg"]), "<=", t, unit="ids"),
        part("ids_missing_or_extra", len(c["missing"]) + len(c["extra"]), "<=", t, unit="ids")],
        note=None if not (bit_mm or c["missing"] or c["extra"]) else f"first differing ids: {ids_note}")
    o = reg.t("M-050.other_sections", "differing_bytes_max")
    rep.add("M-050.other_sections", parts=[part("other_sections_differing_bytes", other, "<=", o, unit="bytes")])
    rep.extra["m050"] = {"records_before": c["n_records"][0], "records_after": c["n_records"][1],
                         "differing_bytes": nbytes, "first_differing_ids": c["first_ids"]}
