#!/bin/bash
# T-113c: I3 on the mixed variant mx2 (spawn on the Peninsula, nearest target is not the one the nearest-first control should take second) next to the original mixed scenario.
# Usage: Tools/m007_bay_i3.sh <out_dir> [seeds=3001-3010]. Evidence only (hold-out seeds); evaluates with Tools/m007_bay.py rev 2 and rev 1. Logs are deleted afterwards unless KEEP=1.
set -euo pipefail
OUT="${1:?out dir}"; SEEDS="${2:-3001-3010}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; PY=/opt/miniconda3/envs/kaiju/bin/python
RUN="dotnet $ROOT/DotnetTests/AI.PackHarness.Run/bin/Release/net11.0/Game.AI.PackHarness.Run.dll --map real"
(cd "$ROOT/DotnetTests/AI.PackHarness.Run" && dotnet build -c Release -v q >/dev/null)
LOGS="${M007_LOGS:-$OUT/logs}"; mkdir -p "$OUT"
C=LL,LH,MH,LLH,LMH,MMH,LLMH,LMHH,LLMHH
for v in orig mx2; do
  VAR=""; [ "$v" = mx2 ] && VAR="--variant mx2"
  for b in scriptedpack@1 nearestinfra@1; do
    $RUN --scenario mixed --brain $b --comps $C --seeds "$SEEDS" $VAR --out "$LOGS/$v" >/dev/null
    $RUN --scenario raid --brain $b --comps $C --seeds "$SEEDS" --out "$LOGS/$v" >/dev/null
  done
  for R in 1 2; do $PY "$ROOT/Tools/m007_bay_par.py" "$LOGS/$v" --rev $R --out "$OUT/report_${v}_r$R.json" > /dev/null; done
done
[ "${KEEP:-0}" = 1 ] || rm -rf "$LOGS"
