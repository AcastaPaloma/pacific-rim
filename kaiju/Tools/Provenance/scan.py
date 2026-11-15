#!/usr/bin/env python3
"""T-144: provenance scanner (metric M-120, coverage 100 %) and IP denylist scan (metric M-126, zero hits).

M-120: inventories every shippable content asset (art, models, textures, audio, fonts, materials, prefabs, scenes,
UI styles, streamed data, player-visible text) and classes it from provenance RECORDS only:
  pre-generated-ai | human-made | data-derived | code-generated | third-party-licensed | unknown
An asset with no valid record is `unknown`; any unknown makes M-120 FAIL. The 100 % threshold is fixed here.
Record sources (nothing is inferred from a file's extension or name):
  1. `PROVENANCE.md` next to the asset: the "Kept files" table lists AI-generated files (class pre-generated-ai).
  2. `provenance@v1` JSON records: `Tools/Provenance/records/*.json` and `PROVENANCE.json` sidecars anywhere.
  3. Geo data covered by a geo manifest that carries licence + attribution + url for each source (class data-derived;
     the same manifests feed Pipelines/build/attribution.py, T-142).
M-126: scans text files, file/folder names and prompt sheets against `denylist.txt` (case-insensitive, word-boundary,
separator-tolerant, camelCase-aware, leetspeak-tolerant). Zero hits required.
Output is deterministic JSON (no timestamps, no absolute paths).

Usage:  python -I scan.py [--root <kaiju dir>] [--out <dir>] [--report-only] [--include-config]
Exit 0 only if M-120 and M-126 both pass (or with --report-only).
"""
import argparse
import fnmatch
import json
import os
import re
import sys

SPEC = "provenance_scan@v1"
RECORD_SPEC = "provenance@v1"
CLASSES = ("pre-generated-ai", "human-made", "data-derived", "code-generated", "third-party-licensed")
HERE = os.path.dirname(os.path.abspath(__file__))

# Required fields per class (besides `class`, `paths`, `tool`).
REQUIRED = {
    "pre-generated-ai": ("prompt_sheet", "stage", "human_edits"),
    "human-made": ("author",),
    "data-derived": ("manifest",),
    "code-generated": ("generator",),
    "third-party-licensed": ("licence", "source"),
}
PATH_FIELDS = {"prompt_sheet", "generator", "manifest"}  # must exist on disk (relative to the kaiju root)

KIND_BY_EXT = {}
for _k, _exts in {
    "image": "png jpg jpeg tga psd exr bmp gif",
    "model": "fbx obj blend gltf glb dae",
    "audio": "wav ogg mp3 aif aiff flac",
    "video": "mp4 mov webm",
    "font": "ttf otf",
    "material": "mat",
    "prefab": "prefab",
    "scene": "unity",
    "ui": "uxml uss",
}.items():
    for _e in _exts.split():
        KIND_BY_EXT[_e] = _k
CODE_EXT = {"cs", "asmdef", "asmref", "hlsl", "shader", "cginc", "compute", "dll", "pdb", "rsp", "py", "sh"}
DOC_EXT = {"md"}
CONFIG_EXT = {"asset", "json", "inputactions", "txt", "wkt", "proj4", "yaml", "yml", "xml"}
TEXT_DIR_HINTS = ("/localization/", "/strings/", "/playertext/", "/dialogue/")
NON_SHIPPING_DIRS = ("Assets/Game/Tools/", "Assets/Packages/")
NON_SHIPPING_SEGMENTS = ("Editor", "Tests")
SKIP_NAMES = {".DS_Store", ".gitkeep", ".gitattributes"}


# ---------------------------------------------------------------- inventory
def rel(path, root):
    return os.path.relpath(path, root).replace(os.sep, "/")


def walk_files(root, top):
    base = os.path.join(root, top)
    for dp, dns, fns in os.walk(base):
        dns[:] = sorted(d for d in dns if not d.startswith("."))
        for fn in sorted(fns):
            yield rel(os.path.join(dp, fn), root)


def classify_kind(path, include_config):
    """Return (kind, reason). kind None means not inventoried (reason says why)."""
    name = path.rsplit("/", 1)[-1]
    if name in SKIP_NAMES or name.endswith(".meta"):
        return None, "meta-or-housekeeping"
    if path.startswith(NON_SHIPPING_DIRS):
        return None, "editor-tooling-not-shipped"
    segs = path.split("/")[:-1]
    if path.startswith("Assets/") and any(s in NON_SHIPPING_SEGMENTS for s in segs):
        return None, "editor-or-test-code-not-shipped"
    ext = name.rsplit(".", 1)[-1].lower() if "." in name else ""
    if path.startswith("Assets/StreamingAssets/"):
        return "data", ""
    if ext in KIND_BY_EXT:
        return KIND_BY_EXT[ext], ""
    if ext in CODE_EXT:
        return None, "source-code"
    low = "/" + path.lower()
    if ext in ("json", "txt", "csv", "tsv") and any(h in low for h in TEXT_DIR_HINTS):
        return "text", ""
    if ext in DOC_EXT:
        return None, "developer-doc"
    if ext in CONFIG_EXT:
        return ("config", "") if include_config else (None, "configuration-data")
    return "other", ""


def inventory(root, include_config):
    items, excluded = [], {}
    for top in ("Assets", "art"):
        if not os.path.isdir(os.path.join(root, top)):
            continue
        for p in walk_files(root, top):
            kind, why = classify_kind(p, include_config)
            if kind is None:
                if why not in ("meta-or-housekeeping",):
                    excluded.setdefault(why, []).append(p)
                continue
            items.append({"path": p, "kind": kind})
    return items, {k: len(v) for k, v in sorted(excluded.items())}


# ---------------------------------------------------------------- records
def glob_re(pat):
    out, i = "", 0
    while i < len(pat):
        if pat.startswith("**", i):
            out += ".*"; i += 2
        elif pat[i] == "*":
            out += "[^/]*"; i += 1
        elif pat[i] == "?":
            out += "[^/]"; i += 1
        else:
            out += re.escape(pat[i]); i += 1
    return re.compile("^" + out + "$")


def validate_record(rec, root):
    """Return a list of problems (empty = valid)."""
    bad = []
    if not isinstance(rec, dict):
        return ["record is not an object"]
    if rec.get("class") not in CLASSES:
        bad.append("class must be one of %s" % ", ".join(CLASSES))
        return bad
    paths = rec.get("paths")
    if not isinstance(paths, list) or not paths or not all(isinstance(p, str) and p for p in paths):
        bad.append("paths must be a non-empty list of globs")
    for f in ("tool",) + REQUIRED[rec["class"]]:
        v = rec.get(f)
        if not isinstance(v, str) or not v.strip():
            bad.append("missing %s" % f)
    for f in PATH_FIELDS:
        v = rec.get(f)
        if isinstance(v, str) and v.strip() and f in REQUIRED[rec["class"]] and not os.path.exists(os.path.join(root, v)):
            bad.append("%s does not exist: %s" % (f, v))
    return bad


def load_records(root):
    """Return (records, problems). Each record gains `_source` (file the record came from)."""
    files = []
    rdir = os.path.join(root, "Tools/Provenance/records")
    if os.path.isdir(rdir):
        files += [os.path.join(rdir, f) for f in sorted(os.listdir(rdir)) if f.endswith(".json")]
    for top in ("Assets", "art"):
        for p in walk_files(root, top):
            if p.endswith("/PROVENANCE.json"):
                files.append(os.path.join(root, p))
    records, problems = [], []
    for f in files:
        fr = rel(f, root)
        base = "" if fr.startswith("Tools/Provenance/records/") else fr.rsplit("/", 1)[0] + "/"
        try:
            with open(f, encoding="utf-8") as fh:
                doc = json.load(fh)
        except (OSError, ValueError) as e:
            problems.append({"source": fr, "problem": "unreadable (%s)" % e})
            continue
        if doc.get("spec") != RECORD_SPEC or not isinstance(doc.get("records"), list):
            problems.append({"source": fr, "problem": "spec must be %s with a records list" % RECORD_SPEC})
            continue
        for i, rec in enumerate(doc["records"]):
            bad = validate_record(rec, root)
            if bad:
                problems.append({"source": "%s#%d" % (fr, i), "problem": "; ".join(bad)})
                continue
            r = dict(rec)
            r["_source"] = fr
            r["_globs"] = [glob_re(base + p) for p in rec["paths"]]
            records.append(r)
    return records, problems


KEPT_ROW = re.compile(r"^\|\s*`([^`]+)`\s*\|")


def parse_provenance_md(root):
    """PROVENANCE.md files -> {asset path: record}; also generation counts per file."""
    out, gens = {}, {}
    for top in ("art", "Assets"):
        for p in walk_files(root, top):
            if not p.endswith("/PROVENANCE.md"):
                continue
            d = p.rsplit("/", 1)[0]
            with open(os.path.join(root, p), encoding="utf-8") as fh:
                text = fh.read()
            ai = re.search(r"AI-generated:\s*\**yes", text, re.I) is not None
            tool = re.search(r"^- \*\*Tool:\*\*\s*(.+)$", text, re.M)
            sheet = d + "/PROMPTS.md"
            m = re.search(r"Generation count:\s*(\d+)\s+of\s+(\d+)", text)
            if m:
                gens[d] = {"used": int(m.group(1)), "budget": int(m.group(2))}
            in_kept = False
            for line in text.splitlines():
                if line.startswith("## "):
                    in_kept = line.strip().lower().startswith("## kept files")
                    continue
                if not in_kept:
                    continue
                km = KEPT_ROW.match(line)
                if km and km.group(1) != "File":
                    f = d + "/" + km.group(1)
                    if ai and tool and os.path.exists(os.path.join(root, f)) and os.path.exists(os.path.join(root, sheet)):
                        out[f] = {"class": "pre-generated-ai", "tool": tool.group(1).strip()[:160],
                                  "prompt_sheet": sheet, "stage": "D-023 concept layer",
                                  "human_edits": "selection and crop only (see PROVENANCE.md)", "_source": p}
    return out, gens


def geo_covered(root):
    """Set of Geo data paths whose licence/attribution/url sources are complete in a geo manifest."""
    cov = set()
    base = "Assets/StreamingAssets/Geo"
    ok_tiles, saw_manifest = False, False
    for p in walk_files(root, base):
        name = p.rsplit("/", 1)[-1]
        if not (name == "manifest.json" or name.endswith(".manifest.json")):
            continue
        try:
            with open(os.path.join(root, p), encoding="utf-8") as fh:
                m = json.load(fh)
        except (OSError, ValueError):
            continue
        d = p.rsplit("/", 1)[0]
        layers = []
        if isinstance(m.get("layers"), list):
            layers = [(l.get("file"), l.get("sources")) for l in m["layers"] if isinstance(l, dict)]
        elif "sources" in m:
            layers = [(m.get("file"), m.get("sources"))]
        complete = bool(layers) and all(
            f and isinstance(s, list) and s and all(
                isinstance(x, dict) and all(isinstance(x.get(k), str) and x[k].strip() for k in ("licence", "attribution", "url"))
                for x in s)
            for f, s in layers)
        if not complete:
            continue
        saw_manifest = True
        cov.add(p)
        for f, _ in layers:
            cov.add(d + "/" + f)
        if os.path.exists(os.path.join(root, d, "build_info.json")):
            cov.add(d + "/build_info.json")
        if "crs" in m:
            ok_tiles = True
    if saw_manifest and ok_tiles:  # CRS definitions are referenced by the manifests' `crs` field
        for p in walk_files(root, base + "/crs"):
            cov.add(p)
    return cov


def assign(items, records, md_records, geo_cov):
    out = []
    for it in items:
        p = it["path"]
        hits = [r for r in records if any(g.match(p) for g in r["_globs"])]
        if p in md_records:
            hits.append(md_records[p])
        if p in geo_cov:
            hits.append({"class": "data-derived", "tool": "Pipelines/geo", "manifest": "geo manifest",
                         "_source": "geo manifests (licence+attribution+url complete)"})
        row = dict(it)
        classes = sorted({h["class"] for h in hits})
        if not hits:
            row.update({"class": "unknown", "record": None, "reason": "no provenance record"})
        elif len(classes) > 1:
            row.update({"class": "unknown", "record": None, "reason": "conflicting records: " + ", ".join(classes)})
        else:
            h = hits[0]
            row.update({"class": h["class"], "record": h["_source"], "tool": h.get("tool", "")})
            if h.get("influenced_by"):
                row["influenced_by"] = h["influenced_by"]
        out.append(row)
    return out


# ---------------------------------------------------------------- M-126 denylist
LEET = {"a": "[a4@]", "e": "[e3]", "i": "[i1!|]", "o": "[o0]", "s": "[s5$]", "t": "[t7]"}
SEP = r"[\s_\-.]*"


def load_denylist(path):
    terms = []
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if line and not line.startswith("#"):
                terms.append(line)
    seen = {}
    for t in terms:
        seen.setdefault(t.lower(), t)
    return sorted(seen.values(), key=str.lower)


def term_regex(term):
    letters = [c for c in term.lower() if not c.isspace() and c not in "-_."]
    body = SEP.join(LEET.get(c, re.escape(c)) for c in letters)
    return re.compile(r"(?<![a-z0-9])" + body + r"(?![a-z0-9])")


def normalise(text):
    return re.sub(r"([a-z])([A-Z])", r"\1 \2", text).lower()


def compile_denylist(terms):
    return [(t, term_regex(t)) for t in terms]


def match_text(text, compiled):
    n = normalise(text)
    return [t for t, rx in compiled if rx.search(n)]


TEXT_EXT = {"cs", "json", "txt", "md", "uss", "uxml", "shader", "hlsl", "cginc", "compute", "asmdef", "asmref", "asset",
            "prefab", "mat", "unity", "inputactions", "wkt", "proj4", "geojson", "csv", "tsv", "yaml", "yml", "xml",
            "py", "vdf"}
M126_ROOTS = ("Assets", "art", "docs/store")
M126_SKIP_PREFIX = ("Tools/Provenance/",)
MAX_TEXT_BYTES = 64 * 1024 * 1024


def is_text(path, full):
    ext = path.rsplit(".", 1)[-1].lower() if "." in path.rsplit("/", 1)[-1] else ""
    if ext not in TEXT_EXT:
        return False
    try:
        with open(full, "rb") as fh:
            return b"\0" not in fh.read(4096)
    except OSError:
        return False


def scan_denylist(root, compiled):
    hits, files, names = [], 0, 0
    for top in M126_ROOTS:
        if not os.path.isdir(os.path.join(root, top)):
            continue
        for dp, dns, fns in os.walk(os.path.join(root, top)):
            dns[:] = sorted(d for d in dns if not d.startswith("."))
            rd = rel(dp, root)
            if rd != "." and rd != top:  # folder names below the top
                names += 1
                for t in match_text(rd.rsplit("/", 1)[-1], compiled):
                    hits.append({"path": rd, "line": 0, "where": "name", "term": t})
            for fn in sorted(fns):
                if fn.endswith(".meta") or fn in SKIP_NAMES:
                    continue
                p = rel(os.path.join(dp, fn), root)
                if p.startswith(M126_SKIP_PREFIX):
                    continue
                names += 1
                for t in match_text(fn, compiled):
                    hits.append({"path": p, "line": 0, "where": "name", "term": t})
                full = os.path.join(root, p)
                if not is_text(p, full) or os.path.getsize(full) > MAX_TEXT_BYTES:
                    continue
                files += 1
                with open(full, encoding="utf-8", errors="replace") as fh:
                    for ln, line in enumerate(fh, 1):
                        for t in match_text(line, compiled):
                            hits.append({"path": p, "line": ln, "where": "text", "term": t})
    hits.sort(key=lambda h: (h["path"], h["line"], h["term"].lower()))
    return hits, {"text_files": files, "names": names}


# ---------------------------------------------------------------- report
def build_report(root, denylist_path, include_config=False):
    items, excluded = inventory(root, include_config)
    records, problems = load_records(root)
    md_records, gens = parse_provenance_md(root)
    geo = geo_covered(root)
    rows = assign(items, records, md_records, geo)
    n = len(rows)
    unknown = [r for r in rows if r["class"] == "unknown"]
    covered = n - len(unknown)
    by_class, by_kind = {}, {}
    for r in rows:
        by_class[r["class"]] = by_class.get(r["class"], 0) + 1
        by_kind.setdefault(r["kind"], {"total": 0, "unknown": 0})
        by_kind[r["kind"]]["total"] += 1
        by_kind[r["kind"]]["unknown"] += r["class"] == "unknown"
    pct = round(100.0 * covered / n, 2) if n else 100.0
    compiled = compile_denylist(load_denylist(denylist_path))
    hits, scanned = scan_denylist(root, compiled)
    influenced = sorted(r["path"] for r in rows if r.get("influenced_by"))
    return {
        "spec": SPEC,
        "m120": {"threshold_pct": 100.0, "shippable": n, "covered": covered, "unknown": len(unknown),
                 "coverage_pct": pct, "pass": n > 0 and not unknown and not problems,
                 "invalid_records": problems, "by_class": dict(sorted(by_class.items())),
                 "by_kind": dict(sorted(by_kind.items())), "excluded_from_inventory": excluded,
                 "include_config": include_config},
        "m126": {"threshold_hits": 0, "denylist_terms": len(compiled), "hits": len(hits), "pass": not hits,
                 "scanned": scanned, "hit_list": hits},
        "disclosure_inputs": {
            "pre_generated_ai_files": by_class.get("pre-generated-ai", 0),
            "concept_generations": gens,
            "assets_influenced_by_pre_generated_ai": influenced,
            "runtime_ai_services_called": 0,
        },
        "unknown_assets": [{"path": r["path"], "kind": r["kind"], "reason": r["reason"]} for r in unknown],
        "assets": [{k: v for k, v in r.items() if k != "reason"} for r in rows],
    }


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--root", default=os.path.abspath(os.path.join(HERE, "..", "..")))
    ap.add_argument("--out", default=None, help="directory for provenance_report.json (default: <root>/evidence/T-144)")
    ap.add_argument("--denylist", default=os.path.join(HERE, "denylist.txt"))
    ap.add_argument("--include-config", action="store_true", help="also inventory .asset/.json config data")
    ap.add_argument("--report-only", action="store_true", help="always exit 0")
    a = ap.parse_args(argv)
    root = os.path.abspath(a.root)
    rep = build_report(root, a.denylist, a.include_config)
    out = a.out or os.path.join(root, "evidence", "T-144")
    os.makedirs(out, exist_ok=True)
    name = "provenance_report.json" if not a.include_config else "provenance_report_with_config.json"
    with open(os.path.join(out, name), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(rep, fh, indent=1, sort_keys=True, ensure_ascii=False)
        fh.write("\n")
    m1, m2 = rep["m120"], rep["m126"]
    print("M-120 %s: %d/%d covered (%.2f %%), %d unknown, %d invalid records" % (
        "PASS" if m1["pass"] else "FAIL", m1["covered"], m1["shippable"], m1["coverage_pct"], m1["unknown"],
        len(m1["invalid_records"])))
    print("M-126 %s: %d hits (%d terms)" % ("PASS" if m2["pass"] else "FAIL", m2["hits"], m2["denylist_terms"]))
    for h in m2["hit_list"]:
        print("  hit: %s:%d [%s] %s" % (h["path"], h["line"], h["where"], h["term"]))
    return 0 if (a.report_only or (m1["pass"] and m2["pass"])) else 1


if __name__ == "__main__":
    sys.exit(main())
