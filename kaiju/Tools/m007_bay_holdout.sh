#!/bin/bash
# T-113b: hold-out evidence run of the clauses C1, C2, P1, P4, P5, P6, I3 on m007_bay_real@v1 (hunt@v1, mixed, raid) and hunt_mv@v2 (C2).
# Seeds are fresh (tuning used 1001-1010 / mixed 1001-1006). Evaluation: Tools/m007_bay.py unchanged, driven in parallel by Tools/m007_bay_par.py.
# Usage: Tools/m007_bay_holdout.sh <out_dir> [hunt_seeds=3001-3030] [long_seeds=3001-3010]; logs in $M007_LOGS or <out_dir>/logs (not committed)
set -euo pipefail
OUT="${1:?out dir}"; HS="${2:-3001-3030}"; LS="${3:-3001-3010}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; PY=/opt/miniconda3/envs/kaiju/bin/python
RUN="dotnet $ROOT/DotnetTests/AI.PackHarness.Run/bin/Release/net11.0/Game.AI.PackHarness.Run.dll --map real"
(cd "$ROOT/DotnetTests/AI.PackHarness.Run" && dotnet build -c Release -v q >/dev/null)
LOGS="${M007_LOGS:-$OUT/logs}"; mkdir -p "$LOGS/v1" "$LOGS/mv2" "$OUT"
C=LL,LH,MH,LLH,LMH,MMH,LLMH,LMHH,LLMHH
for b in scriptedpack@1 noroles@1 nobait@1 directcharge@1 randomrole@1; do $RUN --scenario hunt --brain $b --comps $C --seeds "$HS" --out "$LOGS/v1" >/dev/null; done
for b in scriptedpack@1 noroles@1 nearestinfra@1; do $RUN --scenario mixed --brain $b --comps $C --seeds "$LS" --out "$LOGS/v1" >/dev/null; done
for b in scriptedpack@1 nearestinfra@1; do $RUN --scenario raid --brain $b --comps $C --seeds "$LS" --out "$LOGS/v1" >/dev/null; done
for b in scriptedpack@1 nofocus@1 directcharge@1; do $RUN --scenario hunt --brain $b --comps $C --seeds "$HS" --variant mv2 --out "$LOGS/mv2" >/dev/null; done
$PY "$ROOT/Tools/m007_bay_par.py" "$LOGS/v1" --out "$OUT/report_v1.json" > "$OUT/summary_v1.txt"
$PY "$ROOT/Tools/m007_bay_par.py" "$LOGS/mv2" --out "$OUT/report_mv2.json" > "$OUT/summary_mv2.txt"
cat "$OUT/summary_v1.txt" "$OUT/summary_mv2.txt"
