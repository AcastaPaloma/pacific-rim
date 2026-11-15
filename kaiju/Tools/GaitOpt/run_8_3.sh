#!/bin/bash
# T-005 r3 step 8.3: continuation Fr 0.10 -> 0.15 -> 0.20 with lateral LIPM shear in the friction cone (--lat). Run: bash Tools/GaitOpt/run_8_3.sh
cd "$(dirname "$0")/../.." || exit 1
PY=/opt/miniconda3/envs/kaiju/bin/python; O=Tools/GaitOpt/out; LOG=$O/run_8_3.log; mkdir -p $O; : > $LOG
verdict() { $PY -c "import json,sys; print(json.load(open(sys.argv[1]))['summary']['verdict'])" "$1" 2>/dev/null; }
solve() { # fr warm out
  echo "== --fr $1 --lat, warm $2 -> $3 ($(date))"
  $PY Tools/GaitOpt/stride_opt_tc.py --fr $1 --lat --nodes 40 --maxiter 500 --warm $2 --out $3
  V=$(verdict $3.json); echo "== verdict fr $1: $V"
}
{
solve 0.10 $O/stride_fr010_tc.json $O/stride_fr010_lat
BEST=$O/stride_fr010_lat.json; V10=$(verdict $BEST)
if [ "$V10" = "FEASIBLE" ]; then
  solve 0.15 $BEST $O/stride_fr015_lat; V15=$(verdict $O/stride_fr015_lat.json)
  if [ "$V15" != "FEASIBLE" ]; then
    solve 0.125 $BEST $O/stride_fr0125_lat
    if [ "$(verdict $O/stride_fr0125_lat.json)" = "FEASIBLE" ]; then
      solve 0.15 $O/stride_fr0125_lat.json $O/stride_fr015_lat; V15=$(verdict $O/stride_fr015_lat.json)
    fi
  fi
  if [ "$V15" = "FEASIBLE" ]; then solve 0.20 $O/stride_fr015_lat.json $O/stride_fr020_lat; else echo "== 0.15 not FEASIBLE: 0.20 skipped"; fi
else echo "== Fr 0.10 --lat not FEASIBLE: continuation stopped"; fi
echo "== done ($(date))"
} 2>&1 | grep -v -E "FutureWarning|warnings.warn" | tee -a $LOG
