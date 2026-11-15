#!/usr/bin/env python3
"""T-140: build manifest writer and manifest diff (M-121 reproducibility).

write  -> build_manifest@v1 JSON: version stamp (ProjectSettings bundleVersion + commit), feature-flag snapshot, scene list,
          tile set with each tile's manifest hash, attribution bundle hashes (T-142), build_exclusions check (T-102b/T-144),
          and a sha256 of every file the player contains. `built_utc` is the only field allowed to differ between two
          builds of one commit.
diff   -> compares two manifests, ignoring only `built_utc`; exit 0 when equal, 1 and a path list when not (M-121).

Usage:  python -I build_manifest.py write --repo-root <kaiju> --app-dir <X.app> --unity-json <f> --tile-set <set.json>
                                          --attribution-dir <dir> --out <manifest.json> [--built-utc <iso>]
        python -I build_manifest.py diff <a.json> <b.json>
"""
import argparse
import datetime
import fnmatch
import hashlib
import json
import os
import re
import subprocess
import sys

SPEC = "build_manifest@v1"
VOLATILE = ("built_utc",)


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def _sha(data):
    return hashlib.sha256(data).hexdigest()


def canon(obj):
    return json.dumps(obj, sort_keys=True, separators=(",", ":")).encode()


def hash_tree(root):
    """Every file under root as {path, size, sha256} (symlinks as {path, link}); sorted by path, POSIX separators."""
    entries = []
    for d, dirs, files in os.walk(root):
        dirs.sort()
        for n in sorted(files + [x for x in dirs if os.path.islink(os.path.join(d, x))]):
            p = os.path.join(d, n)
            rel = os.path.relpath(p, root).replace(os.sep, "/")
            if os.path.islink(p):
                entries.append({"path": rel, "link": os.readlink(p)})
            elif os.path.isfile(p):
                entries.append({"path": rel, "size": os.path.getsize(p), "sha256": sha256_file(p)})
    entries.sort(key=lambda e: e["path"])
    return entries


def flag_snapshot(repo_root):
    """All committed feature flags (Config/FeatureFlags/*.json) plus the slice-only set, and a hash over them."""
    fdir = os.path.join(repo_root, "Assets", "Game", "Config", "FeatureFlags")
    files = {}
    for n in sorted(os.listdir(fdir)):
        if n.endswith(".json"):
            with open(os.path.join(fdir, n), encoding="utf-8") as f:
                files[n] = json.load(f).get("flags", {})
    slice_p = os.path.join(repo_root, "Assets", "Game", "Slice", "Config", "slice_m1.flags.json")
    slice_flags = None
    if os.path.exists(slice_p):
        with open(slice_p, encoding="utf-8") as f:
            slice_flags = json.load(f).get("flags", {})
    merged = {}
    for fl in files.values():
        merged.update(fl)
    snap = {"committed": files, "slice_m1": slice_flags}
    return {"flags": snap, "sha256": _sha(canon(snap)), "platform.steam": merged.get("platform.steam")}


def tile_list(tile_set_path, tiles_root):
    with open(tile_set_path, encoding="utf-8") as f:
        raw = json.load(f)
    out = []
    for e in raw["tiles"]:
        if isinstance(e, dict):
            e = e.get("tile") or e.get("id")
        m = re.match(r"^L(\d+)[_/](\d+)_(\d+)$", e)
        if not m:
            raise ValueError("bad tile id %r" % (e,))
        rel = "L%s/%s_%s" % m.groups()
        mp = os.path.join(tiles_root, rel, "manifest.json")
        out.append({"tile": rel, "manifest_sha256": sha256_file(mp) if os.path.exists(mp) else None})
    out.sort(key=lambda t: t["tile"])
    return out


def exclusion_check(repo_root, scenes, app_files):
    with open(os.path.join(repo_root, "Tools", "Provenance", "build_exclusions.json"), encoding="utf-8") as f:
        ex = json.load(f)
    rules, violations = [], []
    for item in ex["exclusions"]:
        for pat in item["paths"]:
            rules.append(pat)
            token = pat[:-3] if pat.endswith("/**") else pat
            token = token.rsplit("/", 1)[-1].replace(".unity", "")
            for s in scenes:
                if fnmatch.fnmatch(s["path"], pat) or fnmatch.fnmatch(s["path"], pat + "/*"):
                    violations.append("scene " + s["path"])
            for e in app_files:
                if token and token in e["path"]:
                    violations.append("file " + e["path"])
    return {"spec": ex.get("spec"), "rules": sorted(rules), "violations": sorted(set(violations))}


def git(repo_root, *args):
    return subprocess.run(["git", "-C", repo_root] + list(args), capture_output=True, text=True, check=True).stdout.strip()


def build_manifest(repo_root, app_dir, unity_json, tile_set, attribution_dir, built_utc=None, tiles_root=None):
    with open(unity_json, encoding="utf-8") as f:
        ub = json.load(f)
    with open(os.path.join(repo_root, "ProjectSettings", "ProjectSettings.asset"), encoding="utf-8") as f:
        bv = re.search(r"^\s*bundleVersion:\s*(\S+)", f.read(), re.M).group(1)
    commit = git(repo_root, "rev-parse", "HEAD")
    dirty = bool(git(repo_root, "status", "--porcelain", "--untracked-files=no", "--", "."))
    files = hash_tree(app_dir)
    flags = flag_snapshot(repo_root)
    if tiles_root is None:
        tiles_root = os.path.join(repo_root, "Assets", "StreamingAssets", "Geo", "tiles", "v1")
    attr = {}
    for n in sorted(os.listdir(attribution_dir)):
        attr[n] = sha256_file(os.path.join(attribution_dir, n))
    content_bytes = sum(e.get("size", 0) for e in files)
    return {
        "spec": SPEC,
        "version": bv,
        "commit": commit,
        "dirty": dirty,
        "version_stamp": "%s+%s%s" % (bv, commit[:10], ".dirty" if dirty else ""),
        "built_utc": built_utc or datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "unity": {k: ub[k] for k in ("unity_version", "target", "development", "result")},
        "scenes": ub["scenes"],
        "feature_flags": flags,
        "tile_set": tile_list(tile_set, tiles_root),
        "attribution": attr,
        "exclusions": exclusion_check(repo_root, ub["scenes"], files),
        "content": {"file_count": len(files), "total_bytes": content_bytes, "sha256": _sha(canon(files)), "files": files},
    }


def verify(m):
    """Hard gates for a manifest: platform.steam off, no exclusion violation, a successful scene list."""
    errs = []
    if m["feature_flags"]["platform.steam"] is not False:
        errs.append("platform.steam must be false in a build without the SDK (got %r)" % (m["feature_flags"]["platform.steam"],))
    if m["exclusions"]["violations"]:
        errs.append("build_exclusions violations: %s" % ", ".join(m["exclusions"]["violations"]))
    if m["unity"]["result"] != "Succeeded":
        errs.append("unity build result %s" % m["unity"]["result"])
    if not m["scenes"]:
        errs.append("no scenes")
    return errs


def _flatten(o, prefix=""):
    if isinstance(o, dict):
        for k, v in o.items():
            yield from _flatten(v, prefix + "/" + k)
    elif isinstance(o, list) and o and all(isinstance(x, dict) and "path" in x for x in o):
        for x in o:
            yield from _flatten({k: v for k, v in x.items() if k != "path"}, prefix + "[" + x["path"] + "]")
    elif isinstance(o, list):
        for i, v in enumerate(o):
            yield from _flatten(v, prefix + "[%d]" % i)
    else:
        yield prefix, o


def diff_manifests(a, b, ignore=VOLATILE):
    """Differences as sorted (path, a_value, b_value); only the top-level `ignore` fields are skipped."""
    fa = {k: v for k, v in _flatten({k: v for k, v in a.items() if k not in ignore})}
    fb = {k: v for k, v in _flatten({k: v for k, v in b.items() if k not in ignore})}
    return [(k, fa.get(k, "<absent>"), fb.get(k, "<absent>")) for k in sorted(set(fa) | set(fb)) if fa.get(k, "<absent>") != fb.get(k, "<absent>")]


def main(argv=None):
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    w = sub.add_parser("write")
    for n in ("repo-root", "app-dir", "unity-json", "tile-set", "attribution-dir", "out"):
        w.add_argument("--" + n, required=True)
    w.add_argument("--built-utc")
    w.add_argument("--tiles-root")
    d = sub.add_parser("diff")
    d.add_argument("a")
    d.add_argument("b")
    args = ap.parse_args(argv)
    if args.cmd == "write":
        m = build_manifest(args.repo_root, args.app_dir, args.unity_json, args.tile_set, args.attribution_dir, args.built_utc, args.tiles_root)
        errs = verify(m)
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump(m, f, indent=1, sort_keys=True)
            f.write("\n")
        print("manifest %s: %d files, %d bytes, content %s" % (m["version_stamp"], m["content"]["file_count"], m["content"]["total_bytes"], m["content"]["sha256"][:16]))
        for e in errs:
            print("BUILD GATE FAILED: " + e, file=sys.stderr)
        return 1 if errs else 0
    with open(args.a, encoding="utf-8") as f:
        a = json.load(f)
    with open(args.b, encoding="utf-8") as f:
        b = json.load(f)
    diffs = diff_manifests(a, b)
    if not diffs:
        print("M-121 PASS: manifests identical except %s (content %s)" % (", ".join(VOLATILE), a["content"]["sha256"][:16]))
        return 0
    print("M-121 FAIL: %d differences" % len(diffs))
    for k, x, y in diffs[:50]:
        print("  %s: %s != %s" % (k, x, y))
    return 1


if __name__ == "__main__":
    sys.exit(main())
