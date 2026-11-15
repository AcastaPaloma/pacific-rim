#!/usr/bin/env bash
# Reproduces evidence/T-036c: Mw 7.2 San Andreas (+ Hayward geometry) over the real district, dumps, then M-050..M-054 and M-005 --pieces.
# Run from kaiju/. Needs dotnet and /opt/miniconda3/envs/kaiju/bin/python.
set -euo pipefail
D=evidence/T-036c; PY=/opt/miniconda3/envs/kaiju/bin/python
rm -rf $D/dumps $D/m05?; mkdir -p $D/dumps
KAIJU_DUMP_DIR=$PWD/$D/dumps dotnet test DotnetTests/WorldSeismic.Tests --filter DistrictQuakeRun
for m in m051:fragility_dump m052:destruction_dump m053:fault_rupture_dump m054:rupture_dump; do
  k=${m%%:*}; f=${m##*:}; $PY Tools/Metrics/metrics.py $k $D/dumps/$f.json --out $D/$k --quiet || true
done
$PY Tools/Metrics/metrics.py m050 $D/dumps/save_before.json $D/dumps/save_after.json --out $D/m050 --quiet || true
$PY Pipelines/geo/m005_check.py district --pieces $D/dumps/district_pieces.json --out $D/m005_district_pieces.json > $D/m005_district_pieces.txt || true
