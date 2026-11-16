#!/bin/bash
# T-113b: hold-out evidence run of the clauses C1, C2, P1, P4, P5, P6, I3 on m007_bay_real@v1 (hunt@v1, mixed, raid) and hunt_mv@v2 (C2).
# Seeds are fresh (tuning used 1001-1010 / mixed 1001-1006). Evaluation: Tools/m007_bay.py unchanged, driven in parallel by Tools/m007_bay_par.py.
# T-115h: M007_COST=v1|v2 selects bay_cost@v1 (proxy) or bay_cost@v2 (USGS liquefaction); default v2.
# Usage: Tools/m007_bay_holdout.sh <out_dir> [hunt_seeds=3001-3030] [long_seeds=3001-3010]; logs in $M007_LOGS or <out_dir>/logs (not committed)
set -euo pipefail
OUT="${1:?out dir}"; HS="${2:-3001-3030}"; LS="${3:-3001-3010}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; PY=/opt/miniconda3/envs/kaiju/bin/python
RUN="dotnet $ROOT/DotnetTests/AI.PackHarness.Run/bin/Release/net11.0/Game.AI.PackHarness.Run.dll --map real ${M007_COST:+--cost-version $M007_COST} ${M007_PACK:+--pack $M007_PACK}"   # T-113c: M007_PACK="BaitFirst=true,..." overrides PackConfig for every arm
(cd "$ROOT/DotnetTests/AI.PackHarness.Run" && dotnet build -c Release -v q >/dev/null)
LOGS="${M007_LOGS:-$OUT/logs}"; mkdir -p "$LOGS/v1" "$LOGS/mv2" "$OUT"
C=LL,LH,MH,LLH,LMH,MMH,LLMH,LMHH,LLMHH
for b in scriptedpack@1 noroles@1 nobait@1 directcharge@1 randomrole@1; do $RUN --scenario hunt --brain $b --comps $C --seeds "$HS" --out "$LOGS/v1" >/dev/null; done
for b in scriptedpack@1 noroles@1 nearestinfra@1; do $RUN --scenario mixed --brain $b --comps $C --seeds "$LS" --out "$LOGS/v1" >/dev/null; done
for b in scriptedpack@1 nearestinfra@1; do $RUN --scenario raid --brain $b --comps $C --seeds "$LS" --out "$LOGS/v1" >/dev/null; done
for b in scriptedpack@1 nofocus@1 directcharge@1; do $RUN --scenario hunt --brain $b --comps $C --seeds "$HS" --variant mv2 --out "$LOGS/mv2" >/dev/null; done
for R in 1 2; do   # T-113c: evaluator rev 1 (original reading) and rev 2 (infrastructure targets are not combat targets), side by side
  $PY "$ROOT/Tools/m007_bay_par.py" "$LOGS/v1" --rev $R --out "$OUT/report_v1_r$R.json" > "$OUT/summary_v1_r$R.txt"
  $PY "$ROOT/Tools/m007_bay_par.py" "$LOGS/mv2" --rev $R --out "$OUT/report_mv2_r$R.json" > "$OUT/summary_mv2_r$R.txt"
done
cat "$OUT"/summary_*.txt
[ "${KEEP:-0}" = 1 ] || rm -rf "$LOGS"   # ~12 GB per matrix; the reports stay
