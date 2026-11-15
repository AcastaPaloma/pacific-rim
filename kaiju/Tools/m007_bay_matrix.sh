#!/bin/bash
# T-115 step 2: run the m007_bay matrix (R-103 5.3, 5.4, 6.4) with the dotnet harness and evaluate it with Tools/m007_bay.py.
# Usage: Tools/m007_bay_matrix.sh <out_dir> [hunt_seeds=50] [long_seeds=20] [emerge_seeds=100]
# Logs go to <out_dir>/logs (large, not committed) or $M007_LOGS; the report goes to <out_dir>/report.json and summary.txt.
# T-115c: M007_MAP=flat|water selects the harness map (default flat = the T-115/T-115b baselines); M007_EXTRA='--no-route' runs the water map with straight-line routes (control).
set -euo pipefail
OUT="${1:?out dir}"; HS="${2:-50}"; LS="${3:-20}"; ES="${4:-100}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PY=/opt/miniconda3/envs/kaiju/bin/python
RUN="dotnet $ROOT/DotnetTests/AI.PackHarness.Run/bin/Release/net11.0/Game.AI.PackHarness.Run.dll"
(cd "$ROOT/DotnetTests/AI.PackHarness.Run" && dotnet build -c Release -v q >/dev/null)
LOGS="${M007_LOGS:-$OUT/logs}"; mkdir -p "$LOGS" "$OUT"
MAP="${M007_MAP:-flat}"; EXTRA="${M007_EXTRA:-}"
RUN="$RUN --map $MAP $EXTRA"
FULL=(LL LH MH LLH LMH MMH LLMH LMHH); LAST=LLMHH   # R-103 5.4: the last composition runs 40 seeds in the hunt family
join() { local IFS=,; echo "$*"; }
run_family() {  # scenario seeds40 brains...
  local sc="$1" n="$2"; shift 2
  local n5=$(( n < 40 ? n : 40 ))
  for b in "$@"; do
    $RUN --scenario "$sc" --brain "$b" --comps "$(join "${FULL[@]}")" --seeds "1-$n" --out "$LOGS" >/dev/null
    $RUN --scenario "$sc" --brain "$b" --comps "$LAST" --seeds "1-$n5" --out "$LOGS" >/dev/null
  done
}
run_family hunt "$HS" scriptedpack@1 directcharge@1 solo@1 noroles@1 nobait@1 randomrole@1 omniscient@1
# T-115e: the nofocus@1 control may take a larger sample (M007_NF_SEEDS, default = hunt seeds); extra seeds only tighten the control mean
run_family hunt "${M007_NF_SEEDS:-$HS}" nofocus@1
run_family churn "$HS" scriptedpack@1
run_family raid "$LS" scriptedpack@1 nearestinfra@1 solo@1 omniscient@1
run_family mixed "$LS" scriptedpack@1 nearestinfra@1 solo@1 noroles@1 omniscient@1
for b in scriptedpack@1 omniscient@1; do $RUN --scenario emerge --brain "$b" --comps mix --seeds "1-$ES" --out "$LOGS" >/dev/null; done
set +e
$PY "$ROOT/Tools/m007_bay.py" "$LOGS" --out "$OUT/report.json" > "$OUT/summary.txt"
echo "exit $?" >> "$OUT/summary.txt"
cat "$OUT/summary.txt"
