import copy
import json
import os

import attribution as A

OSM = {"licence": "ODbL-1.0", "attribution": "(c) OpenStreetMap contributors (ODbL)", "url": "https://example.org/osm.pbf"}
USGS = {"licence": "US-Gov-PD", "attribution": "USGS 3DEP", "url": "https://example.org/3dep.tif", "product": "3DEP 1 m"}
SF = {"licence": "PDDL-1.0", "attribution": "City and County of San Francisco", "url": "https://example.org/sf"}


def tile_manifest(i, j, layers):
    return {"tile": "L0_%d_%d" % (i, j), "layers": [{"layer": n, "file": n + ".bin", "sources": s} for n, s in layers]}


def write(root, rel, doc):
    p = os.path.join(root, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w") as f:
        json.dump(doc, f)


def make_root(tmp, name="geo"):
    root = str(tmp / name)
    write(root, "tiles/v1/L0/1_2/manifest.json", tile_manifest(1, 2, [("terrain", [USGS]), ("roads", [OSM]), ("buildings", [OSM, SF])]))
    write(root, "tiles/v1/L0/1_3/manifest.json", tile_manifest(1, 3, [("terrain", [USGS]), ("roads", [OSM])]))
    write(root, "global/v1/faults.manifest.json", {"layer": "faults", "file": "f.geojson", "sources": [USGS]})
    return root


def run(tmp, roots, extra=()):
    args = []
    for r in roots:
        args += ["--geo-root", r]
    return A.main(args + ["--out-dir", str(tmp / "out")] + list(extra))


def test_complete_passes_and_dedupes(tmp_path, capsys):
    root = make_root(tmp_path)
    assert run(tmp_path, [root]) == 0
    doc = json.load(open(tmp_path / "out" / "attribution.json"))
    assert doc["metric"]["percent"] == 100.0
    assert doc["metric"]["layers_checked"] == 6 and doc["metric"]["tiles"] == 2
    assert [l["id"] for l in doc["licences"]] == ["ODbL-1.0", "PDDL-1.0", "US-Gov-PD"]
    assert len(doc["entries"]) == 3  # OSM, SF, USGS deduped across tiles
    usgs = next(e for e in doc["entries"] if e["licence"] == "US-Gov-PD")
    assert usgs["layers"] == ["faults", "terrain"] and usgs["tiles"] == 2 and usgs["global"] is True
    md = open(tmp_path / "out" / "ATTRIBUTION.md").read()
    assert "OpenStreetMap" in md and "share-alike" in md and "https://example.org/3dep.tif" in md
    assert "M-125 PASS" in capsys.readouterr().out


def test_deterministic_and_root_order_independent(tmp_path):
    a = make_root(tmp_path, "a")
    b = str(tmp_path / "b")
    write(b, "tiles/v1/L0/9_9/manifest.json", tile_manifest(9, 9, [("water", [OSM])]))
    out = tmp_path / "out"
    run(tmp_path, [a, b])
    first = (open(out / "ATTRIBUTION.md").read(), open(out / "attribution.json").read())
    run(tmp_path, [b, a])
    second = (open(out / "ATTRIBUTION.md").read(), open(out / "attribution.json").read())
    assert first == second
    assert str(tmp_path) not in first[0] + first[1]


def test_missing_licence_fails_and_writes_nothing(tmp_path, capsys):
    root = make_root(tmp_path)
    m = tile_manifest(1, 4, [("roads", [{k: v for k, v in OSM.items() if k != "licence"}])])
    write(root, "tiles/v1/L0/1_4/manifest.json", m)
    assert run(tmp_path, [root]) == 1
    assert not (tmp_path / "out").exists()
    err = capsys.readouterr().err
    assert "missing licence" in err and "M-125 FAIL" in err


def test_missing_attribution_url_and_empty_sources_fail(tmp_path, capsys):
    for bad in ({"licence": "ODbL-1.0", "attribution": "  ", "url": "u"}, {"licence": "ODbL-1.0", "attribution": "x"}):
        root = str(tmp_path / ("r%d" % len(bad)))
        write(root, "L0/1_1/manifest.json", tile_manifest(1, 1, [("roads", [bad])]))
        assert A.main(["--geo-root", root, "--check-only"]) == 1
    root = str(tmp_path / "empty_sources")
    write(root, "L0/1_1/manifest.json", tile_manifest(1, 1, [("roads", [])]))
    assert A.main(["--geo-root", root, "--check-only"]) == 1
    assert "no sources" in capsys.readouterr().err


def test_unknown_licence_fails(tmp_path, capsys):
    root = str(tmp_path / "r")
    bad = dict(OSM, licence="Proprietary-1.0")
    write(root, "L0/1_1/manifest.json", tile_manifest(1, 1, [("roads", [bad])]))
    assert A.main(["--geo-root", root, "--check-only"]) == 1
    assert "unknown licence" in capsys.readouterr().err


def test_no_manifests_and_bad_root_fail(tmp_path):
    (tmp_path / "empty").mkdir()
    assert A.main(["--geo-root", str(tmp_path / "empty"), "--check-only"]) == 1
    assert A.main(["--geo-root", str(tmp_path / "nope"), "--check-only"]) == 1


def test_unreadable_manifest_fails(tmp_path):
    root = tmp_path / "r" / "L0" / "1_1"
    root.mkdir(parents=True)
    (root / "manifest.json").write_text("{not json")
    assert A.main(["--geo-root", str(tmp_path / "r"), "--check-only"]) == 1


def test_check_only_writes_nothing(tmp_path):
    root = make_root(tmp_path)
    assert A.main(["--geo-root", root, "--check-only"]) == 0
    assert not (tmp_path / "out").exists()


def test_out_dir_required(tmp_path):
    import pytest
    with pytest.raises(SystemExit):
        A.main(["--geo-root", make_root(tmp_path)])


def test_facts_cited_licence_known(tmp_path):
    root = str(tmp_path / "r")
    src = {"licence": "LicenseRef-facts-cited", "attribution": "Facts cited from CTBUH", "url": "https://example.org/"}
    write(root, "L0/1_1/manifest.json", tile_manifest(1, 1, [("landmarks", [src])]))
    assert A.main(["--geo-root", root, "--check-only"]) == 0
