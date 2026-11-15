#!/usr/bin/env bash
# T-103 chain: older-3DEP-project fill windows (online) -> offline re-bake of the affected L0 tiles -> affected LOD tiles.
# Re-runnable: every stage resumes from its status file. Run from a worktree with KAIJU_GEO_CACHE=<integration>/Pipelines/geo/cache.
set -u
cd "$(dirname "$0")/../.." || exit 1
PY=/opt/miniconda3/envs/kaiju/bin/python
: "${KAIJU_GEO_CACHE:?set KAIJU_GEO_CACHE}"
LOG=$KAIJU_GEO_CACHE/status/dep3_fill_chain.log
echo "[chain] start $(date -u +%FT%TZ)" >> $LOG
$PY -I Pipelines/geo/fetch_dep3_fill.py --workers 4 --min-free-gb 40 >> $KAIJU_GEO_CACHE/status/dep3_fill.log 2>&1; rc=$?; echo "[chain] fetch exit $rc" >> $LOG
[ $rc -eq 3 ] && exit 3
echo "[chain] done $(date -u +%FT%TZ)" >> $LOG
$PY -I Pipelines/geo/bake_bay.py --stage l0 --dep3-fill --force --workers 6 >> $KAIJU_GEO_CACHE/status/bake_l0.log 2>&1; rc=$?; echo "[chain] l0 exit $rc" >> $LOG
[ $rc -eq 3 ] || [ $rc -eq 130 ] && exit $rc
$PY -I Pipelines/geo/bake_bay.py --stage lod --workers 4 --min-free-gb 20 >> $KAIJU_GEO_CACHE/status/bake_lod.log 2>&1; echo "[chain] lod exit $?" >> $LOG
echo "[chain] all done $(date -u +%FT%TZ)" >> $LOG
