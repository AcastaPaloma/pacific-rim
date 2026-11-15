"""T-106 / M-100: reproducibility comparison helpers."""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import m100_check as C  # noqa: E402


def _mk(root, files):
    d = root / "L0" / "30_36"
    d.mkdir(parents=True)
    for n, b in files.items():
        (d / n).write_bytes(b)


def _man(built):
    return json.dumps({"spec": "geo_tile@v1", "build": {"built_utc": built, "git": built, "inputs_sha256": "x"}, "layers": []}).encode()


def test_identical_trees_pass_with_volatile_build_block_and_build_info_ignored(tmp_path):
    _mk(tmp_path / "a", {"terrain.r16": b"1", "manifest.json": _man("t1"), "build_info.json": b"{1}"})
    _mk(tmp_path / "b", {"terrain.r16": b"1", "manifest.json": _man("t2"), "build_info.json": b"{2}"})
    r = C.compare_trees(tmp_path / "a", tmp_path / "b", ["L0/30_36"])
    assert r["result"] == "pass" and r["files"] == 2 and r["identical"] == 2


def test_one_differing_byte_and_a_missing_file_fail(tmp_path):
    _mk(tmp_path / "a", {"terrain.r16": b"1", "manifest.json": _man("t1"), "landmarks.geojson": b"x"})
    _mk(tmp_path / "b", {"terrain.r16": b"2", "manifest.json": _man("t1")})
    r = C.compare_trees(tmp_path / "a", tmp_path / "b", ["L0/30_36"])
    assert r["result"] == "fail" and r["different"] == ["L0/30_36/terrain.r16"] and r["only_in_one"] == ["L0/30_36/landmarks.geojson"]


def test_manifest_content_difference_fails(tmp_path):
    a, b = json.loads(_man("t1")), json.loads(_man("t1"))
    b["layers"] = [{"layer": "x"}]
    _mk(tmp_path / "a", {"manifest.json": json.dumps(a).encode()})
    _mk(tmp_path / "b", {"manifest.json": json.dumps(b).encode()})
    assert C.compare_trees(tmp_path / "a", tmp_path / "b", ["L0/30_36"])["result"] == "fail"


def test_empty_comparison_is_not_a_pass(tmp_path):
    assert C.compare_trees(tmp_path / "a", tmp_path / "b", ["L0/30_36"])["result"] == "fail"


def test_stratified_sample_is_deterministic_and_per_class():
    tiles = [{"id": f"L0_{i:02d}_00", "class": c} for i, c in enumerate(["urban"] * 20 + ["rural"] * 20 + ["water_only"] * 5)]
    a = C.stratified(tiles, "ab" * 32, {"urban": 3, "rural": 2, "water_only": 1})
    b = C.stratified(tiles, "ab" * 32, {"urban": 3, "rural": 2, "water_only": 1})
    c = C.stratified(tiles, "cd" * 32, {"urban": 3, "rural": 2, "water_only": 1})
    assert a == b and len(a) == 6 and a != c
    assert all(t in {x["id"] for x in tiles if x["class"] == "urban"} for t in a[2:5])
