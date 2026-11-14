#!/usr/bin/env python
"""Bake the global fault layer faults_bay.geojson (BayTM@v1) from USGS Qfaults layers 4 + 5 (R-004 §7).

Offshore layer 5 uses UPPER-case field names; both are normalised to
fault_name, section, linetype, slip_rate, slip_sense, age (+ mapped_scale, source_layer, objectid).
Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/bake_faults.py [--offline]
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo import fetch, vector  # noqa: E402
from kaiju_geo.bake import source_entry  # noqa: E402
from kaiju_geo.common import (BAYTM_ID, CACHE, GLOBAL_ROOT, apply_std, canonical_json, git_head,  # noqa: E402
                              sha256_bytes, std_args, utc_now, write_if_changed)


def norm(props, layer):
    if layer == 4:
        return {"fault_name": props.get("fault_name"), "section": props.get("section_name"),
                "linetype": props.get("linetype"), "slip_rate": props.get("slip_rate"),
                "slip_sense": props.get("slip_sense"), "age": props.get("age"),
                "mapped_scale": props.get("mapped_scale"), "fault_id": props.get("fault_id"),
                "source_layer": 4, "objectid": props.get("OBJECTID")}
    return {"fault_name": props.get("FAULT_NAME"), "section": (props.get("SECTION_NA") or "").strip() or None,
            "linetype": props.get("LINE_TYPE"), "slip_rate": props.get("SLIP_RATE"),
            "slip_sense": props.get("SLIP_SENSE"), "age": props.get("FLT_AGE"),
            "mapped_scale": props.get("MAPPED_SCA"), "fault_id": props.get("FAULT_ID"),
            "source_layer": 5, "objectid": props.get("OBJECTID")}


def main(argv=None):
    a = std_args(argparse.ArgumentParser(description=__doc__.splitlines()[0])).parse_args(argv)
    apply_std(a)
    metas = fetch.fetch_faults()
    import shapely
    from shapely.geometry import mapping
    feats = []
    for layer, m in zip((4, 5), metas):
        j = json.loads((CACHE / "qfaults" / f"qfaults_layer{layer}.geojson").read_text())
        for f in j["features"]:
            if not f.get("geometry"):
                continue
            g = vector.round_geom(vector._to_baytm_geom(shapely.geometry.shape(f["geometry"])))
            p = norm(f["properties"], layer)
            feats.append({"type": "Feature", "id": f"qf:{layer}:{p['objectid']}", "properties": p, "geometry": mapping(g)})
    feats.sort(key=lambda f: (f["properties"]["source_layer"], f["properties"]["objectid"]))
    data = (canonical_json({"type": "FeatureCollection", "crs": {"type": "name", "properties": {"name": BAYTM_ID}},
                            "features": feats}) + "\n").encode("utf-8")
    out = GLOBAL_ROOT / "faults_bay.geojson"
    changed = write_if_changed(out, data)
    srcs = [source_entry("qfaults", f"layer {layer}", m, {"features": m.get("features")}) for layer, m in zip((4, 5), metas)]
    man = {"spec": "geo_global@v1", "layer": "faults", "file": "faults_bay.geojson", "sha256": sha256_bytes(data),
           "count": len(feats), "crs": BAYTM_ID, "sources": srcs,
           "fields": ["fault_name", "section", "linetype", "slip_rate", "slip_sense", "age", "mapped_scale", "fault_id", "source_layer", "objectid"]}
    mp = GLOBAL_ROOT / "faults_bay.manifest.json"
    old = json.loads(mp.read_text()) if mp.exists() else None
    if old and old.get("sha256") == man["sha256"] and old.get("sources") == srcs:
        print(f"[faults] up to date: {len(feats)} features, sha256 {man['sha256'][:12]}")
        return 0
    man["build"] = dict({"script": "Pipelines/geo/bake_faults.py", "built_utc": utc_now()}, **git_head())
    write_if_changed(mp, (canonical_json(man, indent=1) + "\n").encode("utf-8"))
    print(f"[faults] {'wrote' if changed else 'unchanged'} {out.name}: {len(feats)} features")
    return 0


if __name__ == "__main__":
    sys.exit(main())
