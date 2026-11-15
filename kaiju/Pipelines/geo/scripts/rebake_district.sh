#!/bin/bash
# T-020b: fetch 3DEP lidar for the 15 district tiles, then re-bake them (default heights filled from lidar / Assessor stories).
# Usage: Pipelines/geo/scripts/rebake_district.sh [logfile]      (run from anywhere; network needed for the fetch)
set -u
HERE="$(cd "$(dirname "$0")/.." && pwd)"
PY=/opt/miniconda3/envs/kaiju/bin/python
LOG="${1:-$HERE/cache/rebake_district.log}"
mkdir -p "$(dirname "$LOG")"
TILES="L0_25_36 L0_25_37 L0_25_38 L0_26_36 L0_26_37 L0_26_38 L0_27_36 L0_27_37 L0_27_38 L0_29_35 L0_29_36 L0_30_35 L0_30_36 L0_31_35 L0_31_36"
{
  echo "== fetch lidar $(date -u +%FT%TZ)"
  for t in $TILES; do
    echo "-- fetch $t"
    "$PY" -I "$HERE/fetch_lidar_dsm.py" --tile "$t" || echo "!! lidar fetch failed for $t (bake will skip the lidar fill there)"
  done
  echo "== bake $(date -u +%FT%TZ)"
  for t in $TILES; do
    echo "-- bake $t"
    "$PY" -I "$HERE/bake_tile.py" --tile "$t" --force --threads 4 || echo "!! bake failed for $t"
  done
  echo "== done $(date -u +%FT%TZ)"
} 2>&1 | tee -a "$LOG"
