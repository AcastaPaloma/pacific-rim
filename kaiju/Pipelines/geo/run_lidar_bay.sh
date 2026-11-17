#!/usr/bin/env bash
# T-103b chain, resumable: per county: fetch+reduce lidar (fetch_lidar_bay.py run) -> L0 re-bake of the tiles it filled (bake_bay.py --force).
# Usage: nohup Pipelines/geo/run_lidar_bay.sh >> $KAIJU_GEO_CACHE/lidar_bay/chain.log 2>&1 &   (run from the kaiju/ dir of the worktree)
# Needs KAIJU_GEO_CACHE = the integration cache. Stops (exit 3) when free disk < 25 GB. LOD is NOT run here.
cd "$(dirname "$0")/../.." || exit 1
PY=/opt/miniconda3/envs/kaiju/bin/python
W=${KAIJU_GEO_CACHE:?}/lidar_bay
COUNTIES=("Alameda County" "Santa Clara County" "San Mateo County" "Marin County" "Solano County" "Sonoma County" "Napa County" "Contra Costa County" "San Francisco" "Santa Cruz County" "Sacramento County" "Yolo County" "San Joaquin County")
for c in "${COUNTIES[@]}"; do
  echo "[chain] $(date -u +%FT%TZ) county $c fetch"
  # T-106c: rc 1 (some tile failed) or 4 (worker pool kept breaking, e.g. out of memory) is retried up to 3 times: failed tiles are re-queued
  # by the next run, done ones skipped. Before this the chain moved on after a crash and left 172 Sonoma and 331 Contra Costa tiles unfilled.
  for attempt in 1 2 3; do
    $PY -I Pipelines/geo/fetch_lidar_bay.py run --county "$c" --workers ${WORKERS:-5} --threads 3 --min-free-gb 25; rc=$?
    [ $rc -eq 1 ] || [ $rc -eq 4 ] || break
    echo "[chain] $(date -u +%FT%TZ) county $c fetch rc=$rc, retry $attempt"
  done
  args=$($PY -I - "$c" <<'PYEOF'
import json, os, sys
W = os.environ["KAIJU_GEO_CACHE"] + "/lidar_bay/"
done_ = set(open(W + "baked.txt").read().split()) if os.path.exists(W + "baked.txt") else set()
last = {}
for l in open(W + "status.jsonl"):
    try: r = json.loads(l)
    except ValueError: continue
    last[r["tile"]] = r
print(" ".join(f"--tile {t}" for t, r in sorted(last.items()) if r["status"] == "done" and r.get("county") == sys.argv[1] and t not in done_))
PYEOF
)
  if [ -n "$args" ]; then
    echo "[chain] bake $c"
    $PY -I Pipelines/geo/bake_bay.py --stage l0 --workers 4 --force --min-free-gb 25 $args; brc=$?
    [ $brc -eq 0 ] && for t in $(echo $args | sed 's/--tile //g'); do echo $t >> $W/baked.txt; done
  fi
  [ $rc -eq 3 ] || [ $rc -eq 130 ] && { echo "[chain] stop rc=$rc"; exit $rc; }
done
echo "[chain] $(date -u +%FT%TZ) all counties done"
