#!/usr/bin/env python3
"""T-142: licence + attribution bundle for the player build (metric M-125, completeness 100 %).

Walks every geo manifest under one or more StreamingAssets Geo roots (committed district tiles, global layers, and by
argument the cache bake root Pipelines/geo/cache/bake/tiles/v1/), collects every shipped layer's licence, attribution
and source URL, and writes a deterministic ATTRIBUTION.md + attribution.json (no timestamps, no absolute paths, input
order does not matter). Exits non-zero, writing nothing, if any shipped layer lacks a source, or any source lacks a
licence, attribution or URL, or names a licence this script has no notice text for.

Usage:  python -I attribution.py --geo-root <dir> [--geo-root <dir> ...] --out-dir <dir>
        (--check-only validates and prints the M-125 line without writing)
"""
import argparse
import json
import os
import sys

SPEC = "attribution@v1"

# Licence id (SPDX or US-Gov-PD, as in Pipelines/geo/config/sources.json) -> notice text.
LICENCES = {
    "ODbL-1.0": {
        "name": "Open Database License 1.0",
        "url": "https://opendatacommons.org/licenses/odbl/1-0/",
        "notice": "Contains information from OpenStreetMap, made available under the Open Database License (ODbL) 1.0. "
                  "Credit: (c) OpenStreetMap contributors. Derived databases of this data remain under ODbL "
                  "(share-alike); the derived geo layers shipped with this game are available under the same licence.",
    },
    "PDDL-1.0": {
        "name": "Open Data Commons Public Domain Dedication and License 1.0",
        "url": "https://opendatacommons.org/licenses/pddl/1-0/",
        "notice": "Dedicated to the public domain under the PDDL 1.0 by the data publisher (DataSF). "
                  "No restrictions; credit given as a courtesy.",
    },
    "US-Gov-PD": {
        "name": "United States Government work, public domain",
        "url": "https://www.usa.gov/government-works",
        "notice": "Not subject to copyright protection within the United States. Credit given as a courtesy.",
    },
    "LicenseRef-facts-cited": {
        "name": "Facts cited from a published source (no data redistributed)",
        "url": "https://www.copyright.gov/circs/circ33.pdf",
        "notice": "Individual facts (for example a published building height) are cited with credit to the source. "
                  "No database, text or imagery from that source is redistributed.",
    },
    "CC0-1.0": {
        "name": "Creative Commons Zero 1.0",
        "url": "https://creativecommons.org/publicdomain/zero/1.0/",
        "notice": "Dedicated to the public domain under CC0 1.0. Credit given as a courtesy.",
    },
    "CC-BY-4.0": {
        "name": "Creative Commons Attribution 4.0",
        "url": "https://creativecommons.org/licenses/by/4.0/",
        "notice": "Licensed under CC BY 4.0. Credit is required.",
    },
}


class AttributionError(Exception):
    pass


def _blank(v):
    return not isinstance(v, str) or not v.strip()


def find_manifests(root):
    """Every manifest.json / *.manifest.json under root, sorted by relative path."""
    out = []
    for dp, dns, fns in os.walk(root):
        dns.sort()
        for fn in sorted(fns):
            if fn == "manifest.json" or fn.endswith(".manifest.json"):
                out.append(os.path.join(dp, fn))
    return out


def layers_of(manifest):
    """Yield (layer_name, sources) for a tile manifest (layers[]) or a global one (layer + sources)."""
    if isinstance(manifest.get("layers"), list):
        for i, l in enumerate(manifest["layers"]):
            yield (l.get("layer") or l.get("file") or "layer#%d" % i), l.get("sources")
    elif "layer" in manifest or "sources" in manifest:
        yield manifest.get("layer") or manifest.get("file") or "layer", manifest.get("sources")
    else:
        yield None, None  # not a geo manifest


def collect(roots):
    """Return (entries, stats, errors). entries keyed by (licence, attribution, product, url)."""
    entries, errors = {}, []
    stats = {"manifests": 0, "layers": 0, "layers_complete": 0}
    seen_tiles = set()
    for ri, root in enumerate(roots):
        root = os.path.abspath(root)
        if not os.path.isdir(root):
            errors.append("geo root is not a directory: %s" % root)
            continue
        for path in find_manifests(root):
            rel = os.path.relpath(path, root).replace(os.sep, "/")
            try:
                with open(path, encoding="utf-8") as f:
                    m = json.load(f)
            except (OSError, ValueError) as e:
                errors.append("%s: unreadable manifest (%s)" % (rel, e))
                continue
            stats["manifests"] += 1
            if isinstance(m.get("tile"), str):
                seen_tiles.add(m["tile"])
            for lname, sources in layers_of(m):
                if lname is None:
                    errors.append("%s: not a geo manifest (no layers / layer+sources)" % rel)
                    continue
                stats["layers"] += 1
                where = "%s layer '%s'" % (rel, lname)
                ok = True
                if not isinstance(sources, list) or not sources:
                    errors.append("%s: no sources (licence + attribution missing)" % where)
                    continue
                for si, s in enumerate(sources):
                    if not isinstance(s, dict):
                        errors.append("%s source #%d: not an object" % (where, si))
                        ok = False
                        continue
                    for key in ("licence", "attribution", "url"):
                        if _blank(s.get(key)):
                            errors.append("%s source #%d: missing %s" % (where, si, key))
                            ok = False
                    lic = s.get("licence")
                    if not _blank(lic) and lic not in LICENCES:
                        errors.append("%s source #%d: unknown licence '%s' (no notice text)" % (where, si, lic))
                        ok = False
                    if not ok:
                        continue
                    key = (lic, s["attribution"].strip(), (s.get("product") or "").strip(), s["url"].strip())
                    e = entries.setdefault(key, {"layers": set(), "tiles": set(), "global": False})
                    e["layers"].add(lname)
                    if isinstance(m.get("tile"), str):
                        e["tiles"].add(m["tile"])
                    else:
                        e["global"] = True
                if ok:
                    stats["layers_complete"] += 1
    if stats["manifests"] == 0 and not errors:
        errors.append("no manifests found under the given geo roots")
    stats["tiles"] = len(seen_tiles)
    return entries, stats, errors


def build(entries, stats):
    ents = []
    for (lic, attr, product, url), e in sorted(entries.items()):
        ents.append({"licence": lic, "attribution": attr, "product": product, "url": url,
                     "layers": sorted(e["layers"]), "tiles": len(e["tiles"]), "global": e["global"]})
    used = sorted({e["licence"] for e in ents})
    pct = 100.0 if stats["layers"] == 0 else round(100.0 * stats["layers_complete"] / stats["layers"], 2)
    return {
        "spec": SPEC,
        "metric": {"id": "M-125", "layers_checked": stats["layers"], "layers_complete": stats["layers_complete"],
                   "percent": pct, "manifests": stats["manifests"], "tiles": stats["tiles"]},
        "licences": [dict(id=i, **LICENCES[i]) for i in used],
        "entries": ents,
    }


def render_md(doc):
    L = ["# Data attribution", "",
         "This game uses open geographic data. Each source below is credited under its licence.", ""]
    L += ["## Licences", ""]
    for lic in doc["licences"]:
        L += ["### %s (%s)" % (lic["id"], lic["name"]), "", lic["notice"], "", "Licence text: %s" % lic["url"], ""]
    L += ["## Sources", ""]
    for lic in doc["licences"]:
        L += ["### Under %s" % lic["id"], ""]
        for e in doc["entries"]:
            if e["licence"] != lic["id"]:
                continue
            title = e["attribution"] + (" - " + e["product"] if e["product"] else "")
            scope = "global layer" if e["global"] and not e["tiles"] else "%d tile(s)" % e["tiles"]
            L += ["- %s" % title, "  - Layers: %s (%s)" % (", ".join(e["layers"]), scope), "  - Source: <%s>" % e["url"]]
        L.append("")
    m = doc["metric"]
    L += ["---", "Completeness (M-125): %d of %d shipped layers carry licence, attribution and source (%.2f %%)."
          % (m["layers_complete"], m["layers_checked"], m["percent"]), ""]
    return "\n".join(L)


def dump_json(doc):
    return json.dumps(doc, indent=2, sort_keys=True, ensure_ascii=False) + "\n"


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--geo-root", action="append", required=True, help="StreamingAssets Geo root or tile root (repeatable)")
    ap.add_argument("--out-dir", help="where ATTRIBUTION.md and attribution.json go")
    ap.add_argument("--check-only", action="store_true")
    a = ap.parse_args(argv)
    if not a.check_only and not a.out_dir:
        ap.error("--out-dir is required unless --check-only")
    entries, stats, errors = collect(a.geo_root)
    if errors:
        for e in errors:
            print("ATTRIBUTION FAIL: " + e, file=sys.stderr)
        print("M-125 FAIL: %d of %d layers complete, %d error(s)" % (stats["layers_complete"], stats["layers"], len(errors)),
              file=sys.stderr)
        return 1
    doc = build(entries, stats)
    if not a.check_only:
        os.makedirs(a.out_dir, exist_ok=True)
        with open(os.path.join(a.out_dir, "ATTRIBUTION.md"), "w", encoding="utf-8", newline="\n") as f:
            f.write(render_md(doc))
        with open(os.path.join(a.out_dir, "attribution.json"), "w", encoding="utf-8", newline="\n") as f:
            f.write(dump_json(doc))
    m = doc["metric"]
    print("M-125 PASS: %d/%d layers complete (%.2f %%) over %d manifests, %d tiles, %d distinct sources"
          % (m["layers_complete"], m["layers_checked"], m["percent"], m["manifests"], m["tiles"], len(doc["entries"])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
