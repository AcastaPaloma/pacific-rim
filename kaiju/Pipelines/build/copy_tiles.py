#!/usr/bin/env python3
"""T-142: copy a tile set into a player-build staging folder, verifying hashes (Q-200: the player build copies tiles
from the git-ignored cache bake; the 15 district tiles are committed).

The tile set is a JSON file {"tiles": ["L0_30_36", ...]} (entries may also be {"tile": "L0_30_36"}, {"id": ...} or a relative dir
"L0/30_36"). A bay_manifest@v1 file (T-100) is a valid tile set as it is; `--class urban rural ...` keeps only entries of those
coverage classes (entries without a class always pass; T-102 aligned this with the Bay bake). Each tile lives at <src-root>/L<level>/<i>_<j>/ with a manifest.json whose layers[].sha256 are checked
against the source before the copy and against the destination after it. Files the manifest does not hash
(build_info.json, landmarks.geojson) are copied and compared byte for byte by hash. Unity .meta files are skipped.
Files are copied with an APFS clone (cp -c) where the filesystem supports it, else a plain copy; never a symlink,
and a symlinked source is refused. Exit non-zero on any missing tile, hash mismatch or symlink.

Usage:  python -I copy_tiles.py --src-root <tiles/v1> --tile-set <set.json> --dest <staging/tiles/v1>
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys

_ID = re.compile(r"^L(\d+)[_/](\d+)_(\d+)$")


class CopyError(Exception):
    pass


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def tile_dir(entry):
    """'L0_30_36' or 'L0/30_36' -> 'L0/30_36'."""
    if isinstance(entry, dict):
        entry = entry.get("tile") or entry.get("id")
    m = _ID.match(entry) if isinstance(entry, str) else None
    if not m:
        raise CopyError("bad tile id: %r" % (entry,))
    return "L%s/%s_%s" % m.groups()


def load_tile_set(path, classes=None):
    try:
        with open(path, encoding="utf-8") as f:
            doc = json.load(f)
    except (OSError, ValueError) as e:
        raise CopyError("unreadable tile set %s: %s" % (path, e))
    tiles = doc.get("tiles") if isinstance(doc, dict) else doc
    if not isinstance(tiles, list) or not tiles:
        raise CopyError("tile set has no tiles")
    if classes:
        tiles = [t for t in tiles if not isinstance(t, dict) or "class" not in t or t["class"] in classes]
        if not tiles:
            raise CopyError("no tiles left after the --class filter %s" % (sorted(classes),))
    return sorted({tile_dir(t) for t in tiles})


def _clone(src, dst):
    if sys.platform == "darwin":
        r = subprocess.run(["cp", "-c", src, dst], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if r.returncode == 0:
            return "clone"
        if os.path.exists(dst):
            os.remove(dst)
    shutil.copyfile(src, dst)
    return "copy"


def copy_tile(src_root, dest_root, rel):
    sdir, ddir = os.path.join(src_root, rel), os.path.join(dest_root, rel)
    if os.path.islink(sdir) or not os.path.isdir(sdir):
        raise CopyError("%s: tile directory missing or a symlink" % rel)
    mpath = os.path.join(sdir, "manifest.json")
    if not os.path.isfile(mpath) or os.path.islink(mpath):
        raise CopyError("%s: manifest.json missing or a symlink" % rel)
    with open(mpath, encoding="utf-8") as f:
        manifest = json.load(f)
    expected = {}
    for l in manifest.get("layers", []):
        if not l.get("file") or not l.get("sha256"):
            raise CopyError("%s: layer without file/sha256 in manifest" % rel)
        expected[l["file"]] = l["sha256"]
    names = sorted(n for n in os.listdir(sdir) if not n.endswith(".meta") and not n.startswith("."))
    for n in expected:
        if n not in names:
            raise CopyError("%s: manifest layer file %s missing from tile" % (rel, n))
    os.makedirs(ddir, exist_ok=True)
    files, modes = {}, {"clone": 0, "copy": 0}
    for n in names:
        s, d = os.path.join(sdir, n), os.path.join(ddir, n)
        if os.path.islink(s) or not os.path.isfile(s):
            raise CopyError("%s/%s: not a regular file (symlink or directory)" % (rel, n))
        h = sha256(s)
        if n in expected and h != expected[n]:
            raise CopyError("%s/%s: source hash %s != manifest %s" % (rel, n, h[:12], expected[n][:12]))
        if os.path.lexists(d):
            os.remove(d)
        modes[_clone(s, d)] += 1
        if os.path.islink(d) or sha256(d) != h:
            raise CopyError("%s/%s: destination hash mismatch after copy" % (rel, n))
        files[n] = h
    return {"files": files, "modes": modes}


def run(src_root, tile_set, dest, classes=None):
    rels = load_tile_set(tile_set, classes)
    if not os.path.isdir(src_root):
        raise CopyError("src root not a directory: %s" % src_root)
    report, total = {}, {"clone": 0, "copy": 0}
    for rel in rels:
        r = copy_tile(src_root, dest, rel)
        report[rel] = r["files"]
        for k in total:
            total[k] += r["modes"][k]
    os.makedirs(dest, exist_ok=True)
    with open(os.path.join(dest, "tiles_copied.json"), "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps({"spec": "tiles_copied@v1", "tiles": report}, indent=2, sort_keys=True) + "\n")
    return len(rels), sum(len(v) for v in report.values()), total


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--src-root", required=True)
    ap.add_argument("--tile-set", required=True)
    ap.add_argument("--dest", required=True)
    ap.add_argument("--class", dest="classes", nargs="*", help="bay_manifest coverage classes to keep (urban rural water_only ocean_only)")
    a = ap.parse_args(argv)
    try:
        n, f, modes = run(a.src_root, a.tile_set, a.dest, a.classes)
    except CopyError as e:
        print("COPY FAIL: %s" % e, file=sys.stderr)
        return 1
    print("copied %d tiles, %d files, all hashes verified (%d cloned, %d plain)" % (n, f, modes["clone"], modes["copy"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
