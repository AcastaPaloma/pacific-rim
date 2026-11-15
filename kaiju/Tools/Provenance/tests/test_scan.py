import json
import os

import pytest

import scan

DENY = ["gundam", "pacific rim", "gipsy danger", "toho", "godzilla"]


def put(root, rel, text="x", binary=False):
    p = os.path.join(root, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "wb" if binary else "w") as f:
        f.write(text if binary else text)
    return p


@pytest.fixture
def tree(tmp_path):
    root = str(tmp_path / "kaiju")
    os.makedirs(root)
    deny = tmp_path / "deny.txt"
    deny.write_text("# c\n" + "\n".join(DENY) + "\n")
    return root, str(deny)


def report(tree, **kw):
    root, deny = tree
    return scan.build_report(root, deny, **kw)


PROV_MD = """# PROVENANCE
- **Tool:** Midjourney web app
- **AI-generated: yes** for every image
## Generation count: 3 of 16 (13 unused)
## Kept files
| File | Layer | Source |
|---|---|---|
| `a.png` | 1 | job 1 |
"""


def test_no_record_is_unknown_and_fails(tree):
    root, _ = tree
    put(root, "art/models/m/m.fbx", b"\0", binary=True)
    r = report(tree)
    assert r["m120"]["shippable"] == 1 and r["m120"]["unknown"] == 1
    assert r["m120"]["coverage_pct"] == 0.0 and r["m120"]["pass"] is False
    assert r["unknown_assets"][0]["path"] == "art/models/m/m.fbx"


def test_provenance_md_covers_listed_files_only(tree):
    root, _ = tree
    put(root, "art/concepts/c/PROVENANCE.md", PROV_MD)
    put(root, "art/concepts/c/PROMPTS.md", "p")
    put(root, "art/concepts/c/a.png", b"\x89", binary=True)
    put(root, "art/concepts/c/b.png", b"\x89", binary=True)  # not in the Kept table
    r = report(tree)
    cls = {a["path"]: a["class"] for a in r["assets"]}
    assert cls["art/concepts/c/a.png"] == "pre-generated-ai"
    assert cls["art/concepts/c/b.png"] == "unknown"
    assert r["disclosure_inputs"]["concept_generations"]["art/concepts/c"] == {"used": 3, "budget": 16}
    assert r["m120"]["pass"] is False


def test_provenance_md_needs_prompt_sheet_and_ai_flag(tree):
    root, _ = tree
    put(root, "art/c/PROVENANCE.md", PROV_MD)
    put(root, "art/c/a.png", b"\x89", binary=True)  # no PROMPTS.md next to it
    assert report(tree)["m120"]["unknown"] == 1


def test_records_json_valid_classes_pass(tree):
    root, _ = tree
    put(root, "Pipelines/gen.py", "# gen")
    put(root, "Assets/Game/A/x.mat", "m")
    put(root, "Assets/Game/A/y.png", b"\x89", binary=True)
    put(root, "Tools/Provenance/records/r.json", json.dumps({"spec": "provenance@v1", "records": [
        {"paths": ["Assets/Game/A/*.mat"], "class": "code-generated", "tool": "t", "generator": "Pipelines/gen.py"},
        {"paths": ["Assets/Game/A/y.png"], "class": "human-made", "tool": "Aseprite", "author": "founder"}]}))
    r = report(tree)
    assert r["m120"]["coverage_pct"] == 100.0 and r["m120"]["pass"] is True
    assert r["m120"]["by_class"] == {"code-generated": 1, "human-made": 1}


@pytest.mark.parametrize("rec", [
    {"paths": ["a/*.png"], "class": "magic", "tool": "t"},
    {"paths": ["a/*.png"], "class": "human-made", "tool": "t"},                       # no author
    {"paths": ["a/*.png"], "class": "code-generated", "tool": "t", "generator": "missing.py"},
    {"paths": ["a/*.png"], "class": "pre-generated-ai", "tool": "t", "prompt_sheet": "P.md", "stage": "s"},
    {"paths": [], "class": "human-made", "tool": "t", "author": "a"},
])
def test_invalid_record_leaves_asset_unknown_and_is_reported(tree, rec):
    root, _ = tree
    put(root, "Assets/a/x.png", b"\x89", binary=True)
    put(root, "Tools/Provenance/records/r.json", json.dumps({"spec": "provenance@v1", "records": [rec]}))
    r = report(tree)
    assert r["m120"]["unknown"] == 1 and len(r["m120"]["invalid_records"]) == 1 and r["m120"]["pass"] is False


def test_conflicting_records_are_unknown(tree):
    root, _ = tree
    put(root, "Assets/a/x.png", b"\x89", binary=True)
    recs = [{"paths": ["Assets/a/x.png"], "class": "human-made", "tool": "t", "author": "a"},
            {"paths": ["Assets/a/*.png"], "class": "third-party-licensed", "tool": "t", "licence": "CC0", "source": "u"}]
    put(root, "Tools/Provenance/records/r.json", json.dumps({"spec": "provenance@v1", "records": recs}))
    a = report(tree)["unknown_assets"]
    assert len(a) == 1 and "conflicting" in a[0]["reason"]


def test_sidecar_provenance_json_is_relative_to_its_folder(tree):
    root, _ = tree
    put(root, "Assets/Game/S/x.png", b"\x89", binary=True)
    put(root, "Assets/Game/S/PROVENANCE.json", json.dumps({"spec": "provenance@v1", "records": [
        {"paths": ["*.png"], "class": "human-made", "tool": "t", "author": "a"}]}))
    assert report(tree)["m120"]["pass"] is True


def geo_manifest(sources_ok=True):
    src = {"licence": "ODbL-1.0", "attribution": "OSM", "url": "https://osm.org"}
    if not sources_ok:
        src.pop("url")
    return json.dumps({"crs": "kaiju:BayTM@v1", "layers": [{"file": "roads.geojson", "layer": "roads", "sources": [src]}]})


def test_geo_data_covered_by_complete_manifest(tree):
    root, _ = tree
    d = "Assets/StreamingAssets/Geo/tiles/v1/L0/1_1/"
    put(root, d + "manifest.json", geo_manifest())
    put(root, d + "roads.geojson", "{}")
    put(root, d + "stray.bin", "x")
    put(root, "Assets/StreamingAssets/Geo/crs/c.wkt", "w")
    r = report(tree)
    cls = {a["path"].rsplit("/", 1)[-1]: a["class"] for a in r["assets"]}
    assert cls["roads.geojson"] == cls["manifest.json"] == cls["c.wkt"] == "data-derived"
    assert cls["stray.bin"] == "unknown"


def test_geo_manifest_with_missing_url_covers_nothing(tree):
    root, _ = tree
    d = "Assets/StreamingAssets/Geo/tiles/v1/L0/1_1/"
    put(root, d + "manifest.json", geo_manifest(False))
    put(root, d + "roads.geojson", "{}")
    assert report(tree)["m120"]["covered"] == 0


def test_exclusions_are_counted_not_inventoried(tree):
    root, _ = tree
    for p in ["Assets/Game/X/a.cs", "Assets/Game/X/Editor/e.png", "Assets/Game/Tools/t.unity", "Assets/Game/X/n.md",
              "Assets/Game/X/c.json", "Assets/Game/X/a.cs.meta", "Assets/Game/X/Tests/t.mat"]:
        put(root, p, "x")
    r = report(tree)
    assert r["m120"]["shippable"] == 0 and r["m120"]["pass"] is False  # empty inventory never passes
    ex = r["m120"]["excluded_from_inventory"]
    assert ex["source-code"] == 1 and ex["developer-doc"] == 1 and ex["configuration-data"] == 1
    assert report(tree, include_config=True)["m120"]["shippable"] == 1


def test_localization_text_is_inventoried(tree):
    root, _ = tree
    put(root, "Assets/Game/UI/Localization/en.json", "{}")
    r = report(tree)
    assert r["assets"][0]["kind"] == "text"


@pytest.mark.parametrize("text,hit", [
    ("The Gundam rises", True),
    ("g u n d a m", True),
    ("GUNDAM", True),
    ("my_gundam_mech", True),
    ("MyGundamMech", True),
    ("Pacific-Rim", True),
    ("pacific_rim", True),
    ("PacificRim", True),
    ("pacific   rim", True),
    ("G0dz1lla", True),
    ("g1pSy d4nger", True),
    ("Toho", True),
    ("photohouse", False),
    ("gundamental", False),
    ("pacific coast rim road", False),
    ("Tohoku", False),
])
def test_denylist_matching(text, hit):
    comp = scan.compile_denylist(DENY)
    assert bool(scan.match_text(text, comp)) is hit


def test_m126_reports_hits_with_paths_lines_and_names(tree):
    root, _ = tree
    put(root, "Assets/Game/UI/strings.cs", 'ok\nvar s = "Godzilla smash";\n')
    put(root, "art/c/PROMPTS.md", "a giant machine like Gipsy Danger\n")
    put(root, "Assets/Game/Gundam_Model/clean.txt", "fine")
    put(root, "docs/store/d.md", "clean")
    r = report(tree)["m126"]
    got = {(h["path"], h["line"], h["where"], h["term"]) for h in r["hit_list"]}
    assert ("Assets/Game/UI/strings.cs", 2, "text", "godzilla") in got
    assert ("art/c/PROMPTS.md", 1, "text", "gipsy danger") in got
    assert ("Assets/Game/Gundam_Model", 0, "name", "gundam") in got
    assert r["hits"] == 3 and r["pass"] is False


def test_m126_clean_tree_passes_and_skips_binary_and_provenance_tool(tree):
    root, _ = tree
    put(root, "Assets/a/x.png", b"\0godzilla", binary=True)
    put(root, "Tools/Provenance/denylist.txt", "godzilla\n")
    put(root, "Assets/a/ok.cs", "class A {}")
    r = report(tree)["m126"]
    assert r["hits"] == 0 and r["pass"] is True


def test_denylist_loader_ignores_comments_and_dedups(tmp_path):
    p = tmp_path / "d.txt"
    p.write_text("# x\n\nA B\na b\nc\n")
    assert scan.load_denylist(str(p)) == ["A B", "c"]


def test_shipped_denylist_has_core_terms():
    terms = [t.lower() for t in scan.load_denylist(os.path.join(scan.HERE, "denylist.txt"))]
    for t in ("godzilla", "pacific rim", "evangelion", "gundam", "jaeger", "toho"):
        assert t in terms


def test_report_is_deterministic_and_has_no_absolute_paths(tree, tmp_path):
    root, deny = tree
    put(root, "art/c/PROVENANCE.md", PROV_MD)
    put(root, "art/c/PROMPTS.md", "p")
    put(root, "art/c/a.png", b"\x89", binary=True)
    outs = []
    for i in range(2):
        out = str(tmp_path / ("o%d" % i))
        scan.main(["--root", root, "--out", out, "--denylist", deny, "--report-only"])
        with open(os.path.join(out, "provenance_report.json"), "rb") as f:
            outs.append(f.read())
    assert outs[0] == outs[1]
    assert root.encode() not in outs[0] and b"/Users" not in outs[0]


def test_exit_code_reflects_gates(tree, tmp_path):
    root, deny = tree
    put(root, "Assets/a/x.png", b"\x89", binary=True)
    assert scan.main(["--root", root, "--out", str(tmp_path / "o"), "--denylist", deny]) == 1
    assert scan.main(["--root", root, "--out", str(tmp_path / "o"), "--denylist", deny, "--report-only"]) == 0
    put(root, "Assets/a/PROVENANCE.json", json.dumps({"spec": "provenance@v1", "records": [
        {"paths": ["x.png"], "class": "human-made", "tool": "t", "author": "a"}]}))
    assert scan.main(["--root", root, "--out", str(tmp_path / "o"), "--denylist", deny]) == 0


def test_build_exclusions_remove_unshipped_paths_but_m126_still_scans_them(tree):
    root, _ = tree
    put(root, "art/style_refs/ref_01.png", "x")
    put(root, "Assets/Game/X/a.png", "x")
    put(root, "art/style_refs/notes.txt", "toho")
    assert report(tree)["m120"]["shippable"] == 2
    put(root, "Tools/Provenance/build_exclusions.json", json.dumps(
        {"spec": "build_exclusions@v1", "exclusions": [{"paths": ["art/style_refs/**"], "reason": "inputs only"}]}))
    r = report(tree)
    assert r["m120"]["shippable"] == 1 and r["m120"]["excluded_from_inventory"]["not-in-player-build"] == 1
    assert r["m126"]["hits"] == 1


def test_build_exclusions_need_a_reason(tree):
    root, _ = tree
    put(root, "Tools/Provenance/build_exclusions.json", json.dumps(
        {"spec": "build_exclusions@v1", "exclusions": [{"paths": ["a/**"]}]}))
    with pytest.raises(ValueError):
        report(tree)


# ---- T-140: build config vs exclusions
def _bs(tmp_path, scenes):
    import scan
    d = tmp_path / "ProjectSettings"
    d.mkdir(exist_ok=True)
    body = "".join("  - enabled: %d\n    path: %s\n    guid: abc\n" % (e, p) for e, p in scenes)
    (d / "EditorBuildSettings.asset").write_text("EditorBuildSettings:\n  m_Scenes:\n" + body + "  m_configObjects: {}\n")
    return scan


def test_build_scene_list_reads_enabled_only(tmp_path):
    scan = _bs(tmp_path, [(1, "Assets/A.unity"), (0, "Assets/B.unity")])
    assert scan.build_scene_list(str(tmp_path)) == ["Assets/A.unity"]


def test_build_config_flags_excluded_scene(tmp_path):
    scan = _bs(tmp_path, [(1, "Assets/Scenes/SampleScene.unity")])
    (tmp_path / "Assets/Scenes").mkdir(parents=True)
    (tmp_path / "Assets/Scenes/SampleScene.unity").write_text("x")
    (tmp_path / "Tools/Provenance").mkdir(parents=True)
    (tmp_path / "Tools/Provenance/build_exclusions.json").write_text(
        '{"spec":"build_exclusions@v1","exclusions":[{"paths":["Assets/Scenes/SampleScene.unity"],"reason":"r"}]}')
    assert any("excluded" in p for p in scan.build_config_problems(str(tmp_path)))


def test_real_repo_build_config_clean():
    import scan, os
    root = os.path.abspath(os.path.join(os.path.dirname(scan.__file__), "..", ".."))
    assert scan.build_config_problems(root) == []
    assert "Assets/Game/Slice/Scenes/M1Slice.unity" in scan.build_scene_list(root)
