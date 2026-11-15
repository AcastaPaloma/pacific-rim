#!/bin/bash
# T-005 r3 8.5 cycle 3: low-Froude rows (0.08..0.02) by descending continuation from Fr 0.10 (--lat); re-solve once from own output if not FEASIBLE
cd "$(dirname "$0")/../.." || exit 1
PY=/opt/miniconda3/envs/kaiju/bin/python; O=Tools/GaitOpt/out; LOG=$O/run_8_5c3.log; : > $LOG
v(){ $PY -c "import json,sys;print(json.load(open(sys.argv[1]))['summary']['verdict'])" "$1" 2>/dev/null; }
prev=$O/stride_fr010_lat.json
{ for F in 0.08 0.06 0.04 0.02; do T=$(printf "%03d" $(echo "$F*100/1" | bc)); out=$O/stride_fr${T}_lat
  echo "== fr $F warm $prev $(date +%T)"; $PY Tools/GaitOpt/stride_opt_tc.py --fr $F --lat --warm $prev --out $out
  V=$(v $out.json); echo "== verdict fr $F: $V"
  if [ "$V" != FEASIBLE ]; then $PY Tools/GaitOpt/stride_opt_tc.py --fr $F --lat --warm $out.json --out ${out}2; V=$(v ${out}2.json); echo "== verdict fr $F re-solve: $V"; [ "$V" = FEASIBLE ] && out=${out}2; fi
  [ "$V" = FEASIBLE ] && prev=$out.json || { echo "== stop at fr $F"; break; }
done; echo "== done $(date +%T)"; } 2>&1 | grep -v -E "FutureWarning|warnings.warn" >> $LOG
