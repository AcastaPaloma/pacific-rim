import importlib.util
import os

import pytest

HERE = os.path.dirname(os.path.abspath(__file__))
STEAM = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(HERE))), "Tools", "Build", "steampipe")
spec = importlib.util.spec_from_file_location("render_vdf", os.path.join(STEAM, "render_vdf.py"))
R = importlib.util.module_from_spec(spec)
spec.loader.exec_module(R)

GOOD = {"APP_ID": "1234500", "DEPOT_ID_MAC": "1234501", "DEPOT_ID_WIN": "1234502", "BUILD_DESC": "kaiju demo 0.1",
        "BUILD_OUTPUT": "/tmp/out", "CONTENT_ROOT": "/tmp/content"}


def test_renders_three_files_without_placeholders():
    files = R.render(GOOD, STEAM)
    assert sorted(files) == ["app_build_1234500.vdf", "depot_build_1234501.vdf", "depot_build_1234502.vdf"]
    app = files["app_build_1234500.vdf"]
    assert '"appid" "1234500"' in app and '"1234501" "depot_build_1234501.vdf"' in app and '"setlive" ""' in app
    mac = files["depot_build_1234501.vdf"]
    assert '"DepotID" "1234501"' in mac and '"contentroot" "/tmp/content/mac"' in mac
    assert '/tmp/content/win' in files["depot_build_1234502.vdf"]
    for t in files.values():
        assert "${" not in t
        assert t.count("{") == t.count("}")


def test_templates_use_only_known_placeholders():
    known = set(R.REQUIRED) | {"DEPOT_ID", "DEPOT_SUBDIR"}
    for n in ("app_build.vdf.template", "depot_build.vdf.template"):
        assert set(R.placeholders(open(os.path.join(STEAM, n)).read())) <= known


@pytest.mark.parametrize("key", R.REQUIRED)
def test_unset_or_empty_value_refused(key):
    v = dict(GOOD)
    del v[key]
    with pytest.raises(R.RenderError):
        R.render(v, STEAM)
    v[key] = "  "
    with pytest.raises(R.RenderError):
        R.render(v, STEAM)


def test_bad_values_refused():
    for k, bad in (("APP_ID", "abc"), ("APP_ID", "0"), ("BUILD_DESC", 'has "quote"'), ("BUILD_DESC", "a\nb"), ("DEPOT_ID_WIN", "1234501")):
        with pytest.raises(R.RenderError):
            R.render(dict(GOOD, **{k: bad}), STEAM)


def test_main_writes_nothing_when_incomplete(tmp_path, monkeypatch):
    for k in R.REQUIRED:
        monkeypatch.delenv(k, raising=False)
    assert R.main(["--out-dir", str(tmp_path / "o"), "--set", "APP_ID=1"]) == 1
    assert not (tmp_path / "o").exists()


def test_main_from_env_and_args(tmp_path, monkeypatch):
    for k, v in GOOD.items():
        monkeypatch.setenv(k, v)
    assert R.main(["--out-dir", str(tmp_path / "o"), "--set", "BUILD_DESC=override"]) == 0
    assert 'override' in (tmp_path / "o" / "app_build_1234500.vdf").read_text()
    assert R.main(["--out-dir", str(tmp_path / "p"), "--set", "NOPE=1"]) == 2


def test_script_has_no_network_or_process_imports():
    src = open(os.path.join(STEAM, "render_vdf.py")).read()
    for bad in ("subprocess", "socket", "urllib", "requests", "http.client", "os.system"):
        assert bad not in src
