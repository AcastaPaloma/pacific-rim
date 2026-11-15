#!/bin/bash
# T-113c: dev-seed sweep of the hunt clauses (C1, P1 hunt part, P4, P5, P6) for one PackConfig override set. Never run on the hold-out seeds.
# Usage: Tools/m007_bay_dev.sh <tag> <seeds e.g. 1-12> ["BaitFirst=true,..."]  -> $M007_DEV (default /tmp) /<tag>/{logs,report.json}; prints the brief table
set -euo pipefail
TAG="${1:?tag}"; SEEDS="${2:-1-12}"; OVER="${3:-}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; PY=/opt/miniconda3/envs/kaiju/bin/python
BASE="${M007_DEV:-/tmp/m007_dev}/$TAG"; mkdir -p "$BASE/logs"
RUN="dotnet $ROOT/DotnetTests/AI.PackHarness.Run/bin/Release/net11.0/Game.AI.PackHarness.Run.dll --map real"
(cd "$ROOT/DotnetTests/AI.PackHarness.Run" && dotnet build -c Release -v q >/dev/null)
C=LL,LH,MH,LLH,LMH,MMH,LLMH,LMHH,LLMHH
for b in scriptedpack@1 noroles@1 nobait@1; do $RUN --scenario hunt --brain $b --comps $C --seeds "$SEEDS" ${OVER:+--pack "$OVER"} --out "$BASE/logs" >/dev/null; done
$PY "$ROOT/Tools/m007_bay_par.py" "$BASE/logs" --out "$BASE/report.json" >/dev/null
$PY "$ROOT/Tools/m007_brief.py" "$TAG=$BASE/report.json"
[ "${KEEP:-0}" = 1 ] || rm -rf "$BASE/logs"   # logs are ~3.6 GB per 30 seeds; the report stays
