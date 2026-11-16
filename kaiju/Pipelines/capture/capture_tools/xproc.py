"""T-194: cross-process determinism check. Compares two capture outputs made by two separate Unity processes.

Every sequence directory carries `hashes.sha256` (sha256 of every png, written by the driver). Two runs of the same
paths with the same code must have identical lists; a difference names the frame and the buffer (color_internal,
color_output, index, depth, mask_animated). The in-process replay of the driver cannot see this class of difference.
"""
import os


def read_hashes(path):
    out = {}
    with open(path) as fh:
        for line in fh:
            line = line.strip()
            if line:
                digest, name = line.split(None, 1)
                out[name.strip()] = digest
    return out


def sequences(root):
    """{relative sequence dir: path of its hashes.sha256}."""
    found = {}
    for dp, _dn, fn in os.walk(root):
        if "hashes.sha256" in fn:
            found[os.path.relpath(dp, root)] = os.path.join(dp, "hashes.sha256")
    return found


def buffer_of(name):
    stem = name.rsplit(".", 1)[0]
    return stem.split("_", 1)[1] if "_" in stem else stem


def compare(a_root, b_root):
    a, b = sequences(a_root), sequences(b_root)
    rows, total_files, differing = [], 0, 0
    for seq in sorted(set(a) | set(b)):
        if seq not in a or seq not in b:
            rows.append({"sequence": seq, "missing_in": "a" if seq not in a else "b", "frames": [], "buffers": {}})
            continue
        ha, hb = read_hashes(a[seq]), read_hashes(b[seq])
        names = sorted(set(ha) | set(hb))
        bad = [n for n in names if ha.get(n) != hb.get(n)]
        total_files += len(names)
        differing += len(bad)
        frames = sorted({n.split("_", 1)[0] for n in bad})
        buffers = {}
        for n in bad:
            buffers[buffer_of(n)] = buffers.get(buffer_of(n), 0) + 1
        rows.append({"sequence": seq, "files": len(names), "differing_files": len(bad), "frames": frames, "buffers": buffers})
    ok = differing == 0 and all("missing_in" not in r for r in rows) and bool(rows)
    return {"schema": "xproc_report@v1", "a": a_root, "b": b_root, "sequences": len(rows), "files": total_files,
            "differing_files": differing, "pass": ok, "rows": [r for r in rows if r.get("differing_files") or "missing_in" in r]}


def run(a_root, b_root):
    return compare(a_root, b_root)
