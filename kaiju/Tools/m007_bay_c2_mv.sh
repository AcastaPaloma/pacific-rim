#!/bin/bash
# T-115f: C2 on the hunt_mv@v2 scenario variant (picket line, real map). Only the hunt family is run: candidate, nofocus@1 control and directcharge@1 on the same seeds.
# Usage: Tools/m007_bay_c2_mv.sh <out_dir> [seeds=101-200] [variant=mv2]   (seeds 1-10 were used to choose the geometry; the evidence run uses fresh seeds)
# The evaluation is Tools/m007_bay.py unchanged: clause C2 is evaluated exactly as defined, other clauses of this partial run are NOT_RUN/irrelevant and are not reported.
set -euo pipefail
OUT="${1:?out dir}"; SE="${2:-101-200}"; V="${3:-mv2}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; PY=/opt/miniconda3/envs/kaiju/bin/python
RUN="dotnet $ROOT/DotnetTests/AI.PackHarness.Run/bin/Release/net11.0/Game.AI.PackHarness.Run.dll --map real --variant $V"
(cd "$ROOT/DotnetTests/AI.PackHarness.Run" && dotnet build -c Release -v q >/dev/null)
LOGS="${M007_LOGS:-$OUT/logs}"; mkdir -p "$LOGS" "$OUT"
N5="${SE#*-}"; N5=$(( ${SE%-*} + 39 )); N5=$(( N5 < ${SE#*-} ? N5 : ${SE#*-} ))
for b in scriptedpack@1 nofocus@1 directcharge@1; do
  $RUN --scenario hunt --brain $b --comps LL,LH,MH,LLH,LMH,MMH,LLMH,LMHH --seeds "$SE" --out "$LOGS" >/dev/null
  $RUN --scenario hunt --brain $b --comps LLMHH --seeds "${SE%-*}-$N5" --out "$LOGS" >/dev/null
done
set +e
$PY "$ROOT/Tools/m007_bay.py" "$LOGS" --out "$OUT/report.json" > "$OUT/summary.txt"
$PY - "$OUT/report.json" > "$OUT/c2.txt" <<'PYEOF'
import json,sys
r=json.load(open(sys.argv[1]))
for c in r["clauses"] if "clauses" in r else r.get("results",[]):
    if c["clause"] in ("C1","C2","C4"): print(json.dumps(c,indent=1))
PYEOF
cat "$OUT/c2.txt"
