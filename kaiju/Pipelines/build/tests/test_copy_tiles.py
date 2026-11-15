import hashlib
import json
import os

import pytest

import copy_tiles as C


def make_tile(root, i, j, corrupt=False):
    d = os.path.join(root, "L0", "%d_%d" % (i, j))
    os.makedirs(d)
    layers = []
    for name, data in (("terrain.tif", b"T" * 1000 + bytes([i])), ("roads.geojson", b'{"x":1}')):
        with open(os.path.join(d, name), "wb") as f:
            f.write(data)
        layers.append({"file": name, "layer": name.split(".")[0], "sha256": hashlib.sha256(data).hexdigest()})
    if corrupt:
        layers[0]["sha256"] = "0" * 64
    with open(os.path.join(d, "manifest.json"), "w") as f:
        json.dump({"tile": "L0_%d_%d" % (i, j), "layers": layers}, f)
    with open(os.path.join(d, "build_info.json"), "w") as f:
        f.write("{}")
    with open(os.path.join(d, "terrain.tif.meta"), "w") as f:
        f.write("guid: x")
    return d


def tile_set(tmp, tiles):
    p = str(tmp / "set.json")
    with open(p, "w") as f:
        json.dump({"tiles": tiles}, f)
    return p


def test_copy_verifies_and_skips_meta(tmp_path):
    src = str(tmp_path / "src")
    make_tile(src, 1, 2)
    make_tile(src, 3, 4)
    make_tile(src, 9, 9)
    dest = str(tmp_path / "stage")
    rc = C.main(["--src-root", src, "--tile-set", tile_set(tmp_path, ["L0_1_2", {"tile": "L0_3_4"}]), "--dest", dest])
    assert rc == 0
    assert sorted(os.listdir(os.path.join(dest, "L0"))) == ["1_2", "3_4"]
    names = os.listdir(os.path.join(dest, "L0", "1_2"))
    assert "build_info.json" in names and "terrain.tif.meta" not in names
    rep = json.load(open(os.path.join(dest, "tiles_copied.json")))
    assert set(rep["tiles"]) == {"L0/1_2", "L0/3_4"}
    for dp, _, fns in os.walk(dest):
        for fn in fns:
            assert not os.path.islink(os.path.join(dp, fn))


def test_copy_is_deterministic_and_rerunnable(tmp_path):
    src = str(tmp_path / "src")
    make_tile(src, 1, 2)
    ts = tile_set(tmp_path, ["L0/1_2"])
    dest = str(tmp_path / "stage")
    assert C.main(["--src-root", src, "--tile-set", ts, "--dest", dest]) == 0
    a = open(os.path.join(dest, "tiles_copied.json")).read()
    assert C.main(["--src-root", src, "--tile-set", ts, "--dest", dest]) == 0
    assert open(os.path.join(dest, "tiles_copied.json")).read() == a


def test_source_hash_mismatch_fails(tmp_path, capsys):
    src = str(tmp_path / "src")
    make_tile(src, 1, 2, corrupt=True)
    assert C.main(["--src-root", src, "--tile-set", tile_set(tmp_path, ["L0_1_2"]), "--dest", str(tmp_path / "s")]) == 1
    assert "source hash" in capsys.readouterr().err


def test_missing_tile_and_bad_id_fail(tmp_path):
    src = str(tmp_path / "src")
    make_tile(src, 1, 2)
    assert C.main(["--src-root", src, "--tile-set", tile_set(tmp_path, ["L0_7_7"]), "--dest", str(tmp_path / "s")]) == 1
    assert C.main(["--src-root", src, "--tile-set", tile_set(tmp_path, ["bogus"]), "--dest", str(tmp_path / "s")]) == 1
    assert C.main(["--src-root", src, "--tile-set", tile_set(tmp_path, []), "--dest", str(tmp_path / "s")]) == 1


def test_symlinked_source_file_refused(tmp_path):
    src = str(tmp_path / "src")
    d = make_tile(src, 1, 2)
    real = str(tmp_path / "real.json")
    open(real, "w").write("{}")
    os.remove(os.path.join(d, "build_info.json"))
    os.symlink(real, os.path.join(d, "build_info.json"))
    assert C.main(["--src-root", src, "--tile-set", tile_set(tmp_path, ["L0_1_2"]), "--dest", str(tmp_path / "s")]) == 1


def test_symlinked_tile_dir_refused(tmp_path):
    src = str(tmp_path / "src")
    make_tile(str(tmp_path / "elsewhere"), 1, 2)
    os.makedirs(os.path.join(src, "L0"))
    os.symlink(str(tmp_path / "elsewhere" / "L0" / "1_2"), os.path.join(src, "L0", "1_2"))
    assert C.main(["--src-root", src, "--tile-set", tile_set(tmp_path, ["L0_1_2"]), "--dest", str(tmp_path / "s")]) == 1


def test_overwrites_stale_destination_file(tmp_path):
    src = str(tmp_path / "src")
    make_tile(src, 1, 2)
    dest = str(tmp_path / "stage")
    os.makedirs(os.path.join(dest, "L0", "1_2"))
    with open(os.path.join(dest, "L0", "1_2", "terrain.tif"), "wb") as f:
        f.write(b"stale")
    assert C.main(["--src-root", src, "--tile-set", tile_set(tmp_path, ["L0_1_2"]), "--dest", dest]) == 0
    assert open(os.path.join(dest, "L0", "1_2", "terrain.tif"), "rb").read().startswith(b"TTT")
