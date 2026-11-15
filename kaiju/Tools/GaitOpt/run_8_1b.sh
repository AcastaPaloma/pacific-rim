#!/bin/bash
# T-005 r3 step 8.1b: at most 2 trust-constr attempts. Run from anywhere: bash Tools/GaitOpt/run_8_1b.sh
cd "$(dirname "$0")/../.." || exit 1
PY=/opt/miniconda3/envs/kaiju/bin/python; O=Tools/GaitOpt/out; LOG=$O/run_8_1b.log; mkdir -p $O; : > $LOG
verdict() { $PY -c "import json,sys; print(json.load(open(sys.argv[1]))['summary']['verdict'])" "$1" 2>/dev/null; }
{
echo "== attempt 1: N=40, warm start $O/stride_fr010.json ($(date))"
$PY Tools/GaitOpt/stride_opt_tc.py --nodes 40 --maxiter 500 --out $O/stride_fr010_tc
V=$(verdict $O/stride_fr010_tc.json); echo "== attempt 1 verdict: $V"
if [ "$V" != "FEASIBLE" ]; then
  echo "== attempt 2: N=80, warm start $O/stride_fr010_tc.json ($(date))"
  $PY Tools/GaitOpt/stride_opt_tc.py --nodes 80 --maxiter 500 --warm $O/stride_fr010_tc.json --out $O/stride_fr010_tc80
  echo "== attempt 2 verdict: $(verdict $O/stride_fr010_tc80.json)"
fi
echo "== done ($(date))"
} 2>&1 | grep -v -E "FutureWarning|warnings.warn" | tee -a $LOG
