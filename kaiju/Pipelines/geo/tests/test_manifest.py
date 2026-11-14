"""Manifest completeness and the doctored-manifest rejection (M-005.8; T-003 C9, C18)."""
import copy
import json

import pytest

from kaiju_geo import manifest
from kaiju_geo.common import TILES_ROOT

M0 = TILES_ROOT / "L0" / "30_36"


@pytest.fixture(scope="module")
def man():
    return json.loads((M0 / "manifest.json").read_text())


def test_committed_manifest_is_complete(man):
    assert manifest.validate(man, M0) == []


def test_every_layer_has_licence_and_attribution(man):
    for L in man["layers"]:
        assert L["sources"]
        for s in L["sources"]:
            assert s["licence"] in manifest.LICENCES and s["attribution"]


def test_osm_layers_carry_odbl_credit_and_derivative_flag(man):
    for L in man["layers"]:
        if L["layer"] in ("buildings", "roads", "water"):
            osm = [s for s in L["sources"] if s["licence"] == "ODbL-1.0"]
            assert osm and all("OpenStreetMap contributors" in s["attribution"] for s in osm)
            assert all(s.get("derivative_database_candidate") for s in osm)
    assert any("Open Database License" in a for a in manifest.attributions(man))


@pytest.mark.parametrize("path", [("build", "git"), ("layers", 0, "sha256"), ("layers", 3, "sources", 0, "licence"),
                                  ("layers", 0, "sources", 0, "attribution"), ("layers", 4, "sources", 0, "fetched_utc"),
                                  ("layers", 1, "sources", 1, "source_sha256"), ("bounds_baytm",)])
def test_doctored_manifest_is_rejected(man, path):
    m = copy.deepcopy(man)
    node = m
    for k in path[:-1]:
        node = node[k]
    del node[path[-1]]
    assert manifest.validate(m), f"validator accepted a manifest without {path}"


def test_tampered_output_hash_is_rejected(man):
    m = copy.deepcopy(man)
    m["layers"][3]["sha256"] = "0" * 64
    assert any("sha256 mismatch" in e for e in manifest.validate(m, M0))


def test_unknown_licence_is_rejected(man):
    m = copy.deepcopy(man)
    m["layers"][0]["sources"][0]["licence"] = "unclear"
    assert manifest.validate(m)
