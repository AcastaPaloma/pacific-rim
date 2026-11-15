import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import build_manifest as bm  # noqa: E402

PY = sys.executable
SCRIPT = os.path.join(os.path.dirname(__file__), "..", "build_manifest.py")


def make_repo(tmp_path, steam=False):
    repo = tmp_path / "proj"
    (repo / "ProjectSettings").mkdir(parents=True)
    (repo / "ProjectSettings" / "ProjectSettings.asset").write_text("  productName: X\n  bundleVersion: 1.2.3\n")
    fl = repo / "Assets" / "Game" / "Config" / "FeatureFlags"
    fl.mkdir(parents=True)
    (fl / "Platform.json").write_text(json.dumps({"schema": 1, "flags": {"platform.steam": steam}}))
    (fl / "Core.json").write_text(json.dumps({"schema": 1, "flags": {"core.x": True}}))
    sl = repo / "Assets" / "Game" / "Slice" / "Config"
    sl.mkdir(parents=True)
    (sl / "slice_m1.flags.json").write_text(json.dumps({"flags": {"slice.m1": True}}))
    prov = repo / "Tools" / "Provenance"
    prov.mkdir(parents=True)
    (prov / "build_exclusions.json").write_text(json.dumps({"spec": "build_exclusions@v1", "exclusions": [
        {"paths": ["art/style_refs/**"]}, {"paths": ["Assets/TutorialInfo/**"]}, {"paths": ["Assets/Scenes/SampleScene.unity"]}]}))
    tiles = repo / "Assets" / "StreamingAssets" / "Geo" / "tiles" / "v1" / "L0" / "30_36"
    tiles.mkdir(parents=True)
    (tiles / "manifest.json").write_text("{}")
    subprocess.run(["git", "init", "-q", str(repo)], check=True)
    subprocess.run(["git", "-C", str(repo), "-c", "user.name=t", "-c", "user.email=t@t", "add", "-A"], check=True)
    subprocess.run(["git", "-C", str(repo), "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-qm", "x"], check=True)
    return repo


def make_app(tmp_path, name="app", extra=None):
    app = tmp_path / name / "G.app"
    (app / "Contents" / "Data").mkdir(parents=True)
    (app / "Contents" / "Data" / "a.bin").write_bytes(b"abc")
    (app / "Contents" / "Data" / "b.bin").write_bytes(b"def")
    os.symlink("a.bin", app / "Contents" / "Data" / "link")
    for k, v in (extra or {}).items():
        (app / k).parent.mkdir(parents=True, exist_ok=True)
        (app / k).write_bytes(v)
    return app


def inputs(tmp_path, result="Succeeded", scene="Assets/Game/Slice/Scenes/M1Slice.unity"):
    ub = tmp_path / "ub.json"
    ub.write_text(json.dumps({"result": result, "unity_version": "6000.6.9f1", "target": "StandaloneOSX", "development": False,
                              "scenes": [{"path": scene, "guid": "g"}]}))
    ts = tmp_path / "set.json"
    ts.write_text(json.dumps({"tiles": ["L0_30_36"]}))
    ad = tmp_path / "attr"
    ad.mkdir(exist_ok=True)
    (ad / "ATTRIBUTION.md").write_text("x")
    return ub, ts, ad


def manifest(tmp_path, repo, app, utc, **kw):
    ub, ts, ad = inputs(tmp_path, **kw)
    return bm.build_manifest(str(repo), str(app), str(ub), str(ts), str(ad), built_utc=utc)


def test_identical_builds_differ_only_in_timestamp(tmp_path):
    repo = make_repo(tmp_path)
    a = manifest(tmp_path, repo, make_app(tmp_path, "one"), "2026-01-01T00:00:00Z")
    b = manifest(tmp_path, repo, make_app(tmp_path, "two"), "2026-02-02T00:00:00Z")
    assert a["built_utc"] != b["built_utc"]
    assert bm.diff_manifests(a, b) == []
    assert a["version_stamp"].startswith("1.2.3+") and a["content"]["file_count"] == 3
    assert a["tile_set"][0]["tile"] == "L0/30_36" and a["feature_flags"]["platform.steam"] is False


def test_content_difference_is_reported_with_path(tmp_path):
    repo = make_repo(tmp_path)
    a = manifest(tmp_path, repo, make_app(tmp_path, "one"), "t")
    b = manifest(tmp_path, repo, make_app(tmp_path, "two", {"Contents/Data/a.bin": b"CHANGED"}), "t")
    d = bm.diff_manifests(a, b)
    assert any("a.bin" in k for k, _, _ in d) and any(k == "/content/sha256" for k, _, _ in d)


def test_extra_file_and_flag_change_detected(tmp_path):
    repo = make_repo(tmp_path)
    a = manifest(tmp_path, repo, make_app(tmp_path, "one"), "t")
    b = manifest(tmp_path, repo, make_app(tmp_path, "two", {"Contents/new.bin": b"x"}), "t")
    assert any("new.bin" in k for k, _, _ in bm.diff_manifests(a, b))
    repo2 = make_repo(tmp_path / "r2", steam=True)
    c = manifest(tmp_path, repo2, make_app(tmp_path, "three"), "t")
    assert any("platform.steam" in k or "feature_flags" in k for k, _, _ in bm.diff_manifests(a, c))


def test_volatile_fields_are_only_built_utc_and_guid():
    assert bm.VOLATILE == ("built_utc", "build_guid")
    assert bm.diff_manifests({"built_utc": "a", "x": 1}, {"built_utc": "b", "x": 1}) == []
    assert bm.diff_manifests({"commit": "a"}, {"commit": "b"}) == [("/commit", "a", "b")]


def test_gate_fails_when_steam_flag_on(tmp_path):
    m = manifest(tmp_path, make_repo(tmp_path, steam=True), make_app(tmp_path), "t")
    assert any("platform.steam" in e for e in bm.verify(m))


def test_gate_fails_on_excluded_scene_and_file(tmp_path):
    repo = make_repo(tmp_path)
    m = manifest(tmp_path, repo, make_app(tmp_path, extra={"Contents/Data/TutorialInfo/readme.txt": b"x"}), "t",
                 scene="Assets/Scenes/SampleScene.unity")
    errs = " ".join(bm.verify(m))
    assert "scene Assets/Scenes/SampleScene.unity" in errs and "TutorialInfo" in errs


def test_gate_passes_clean_build(tmp_path):
    m = manifest(tmp_path, make_repo(tmp_path), make_app(tmp_path), "t")
    assert bm.verify(m) == []


def test_cli_write_and_diff(tmp_path):
    repo = make_repo(tmp_path)
    ub, ts, ad = inputs(tmp_path)
    outs = []
    for i in ("one", "two"):
        out = tmp_path / (i + ".json")
        r = subprocess.run([PY, SCRIPT, "write", "--repo-root", str(repo), "--app-dir", str(make_app(tmp_path, i)), "--unity-json", str(ub),
                            "--tile-set", str(ts), "--attribution-dir", str(ad), "--out", str(out)], capture_output=True, text=True)
        assert r.returncode == 0, r.stderr
        outs.append(out)
    r = subprocess.run([PY, SCRIPT, "diff", str(outs[0]), str(outs[1])], capture_output=True, text=True)
    assert r.returncode == 0 and "M-121 PASS" in r.stdout
    m = json.loads(outs[1].read_text())
    m["content"]["sha256"] = "0" * 64
    outs[1].write_text(json.dumps(m))
    r = subprocess.run([PY, SCRIPT, "diff", str(outs[0]), str(outs[1])], capture_output=True, text=True)
    assert r.returncode == 1 and "M-121 FAIL" in r.stdout


def test_cli_write_exit_1_on_gate_failure(tmp_path):
    repo = make_repo(tmp_path, steam=True)
    ub, ts, ad = inputs(tmp_path)
    r = subprocess.run([PY, SCRIPT, "write", "--repo-root", str(repo), "--app-dir", str(make_app(tmp_path)), "--unity-json", str(ub),
                        "--tile-set", str(ts), "--attribution-dir", str(ad), "--out", str(tmp_path / "m.json")], capture_output=True, text=True)
    assert r.returncode == 1 and "BUILD GATE FAILED" in r.stderr


def test_unity_build_guid_is_normalised_and_signature_derived(tmp_path):
    repo = make_repo(tmp_path)

    def app(name, guid):
        return make_app(tmp_path, name, {
            "Contents/Resources/Data/boot.config": ("gfx=1\nbuild-guid=%s\n" % guid).encode(),
            "Contents/MacOS/G": b"head" + guid.encode() + b"blank\x00" + bytes([ord(guid[0])]) * 32 + b"tail",
            "Contents/_CodeSignature/CodeResources": guid.encode() + b"sig"})
    a = manifest(tmp_path, repo, app("one", "a" * 32), "t1")
    b = manifest(tmp_path, repo, app("two", "b" * 32), "t2")
    assert a["build_guid"] != b["build_guid"] and bm.diff_manifests(a, b) == []
    c = manifest(tmp_path, repo, make_app(tmp_path, "three", {
        "Contents/Resources/Data/boot.config": ("gfx=2\nbuild-guid=%s\n" % ("b" * 32)).encode(),
        "Contents/MacOS/G": b"head" + b"b" * 32 + b"blank\x00" + b"b" * 32 + b"tailX", "Contents/_CodeSignature/CodeResources": b"x"}), "t")
    assert bm.diff_manifests(a, c)   # a real content change is still caught
