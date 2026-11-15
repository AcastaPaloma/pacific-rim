#!/bin/bash
# T-005 r4 step 9.3b-3: reference v2 = Fr 0.10 --lat stride with ADDED transition-feasibility rows (stride_opt_tc.py --trans), N 40 and N 80 in parallel,
# both warm-started from out/stride_fr010_lat.json; each NOT CONVERGED result is re-solved up to twice from its own output. Run: bash Tools/GaitOpt/run_9_3b3.sh
# Polish (re-solve a result from its own output, same rows; used because the first passes stop at the iteration cap / xtol with torques at the limit):
#   bash Tools/GaitOpt/run_9_3b3.sh polish <nodes> <warm stem> <out stem>   (stems relative to Tools/GaitOpt/out; log out/run_9_3b3_<out stem>.log)
cd "$(dirname "$0")/../.." || exit 1
# Criterion A on reference v2 (one controller gain round, parallel; log out/proto/sweep_9_3b3.log):  bash Tools/GaitOpt/run_9_3b3.sh sweep
if [ "$1" = "sweep" ]; then
  PY=/opt/miniconda3/envs/kaiju/bin/python; R=Tools/GaitOpt/out/stride_fr010_v2_n40p2.json; L=Tools/GaitOpt/out/proto/sweep_9_3b3.log; : > $L; i=0
  for set in "sigma=0" "sigma=0 w_smooth=0.1" "sigma=0 w_smooth=1" "sigma=0 w_prior=1" "sigma=0 w_fp=1e4" "sigma=0 z_hold=0.2" "sigma=0 kvs=5" "sigma=0 om_z=1" \
             "sigma=0 T_out=0.6" "sigma=0 w_smooth=0.1 z_hold=0.2" "sigma=0 w_smooth=0.1 w_fp=1e4" "sigma=0 w_smooth=0.1 z_hold=0.2 w_fp=1e4" "sigma=1 w_smooth=0.1" "sigma=0 w_L=1e4" ; do
    i=$((i + 1)); ( r=$(OMP_NUM_THREADS=1 $PY Tools/GaitOpt/qp_proto/run_fid.py --tag A_v2_sw$i --ref $R --friction stick --quiet --set $set 2>&1 | tail -1); echo "sw$i [$set] $r" >> $L ) &
  done; wait; cat $L; exit 0
fi
if [ "$1" = "polish" ]; then
  PY=/opt/miniconda3/envs/kaiju/bin/python; O=Tools/GaitOpt/out
  { echo "== N $2 polish: warm $O/$3.json -> $O/$4 ($(date))"
    OMP_NUM_THREADS=6 MKL_NUM_THREADS=6 $PY Tools/GaitOpt/stride_opt_tc.py --fr 0.10 --lat --trans --frate 0.5 --vlift 0.5 --nodes $2 --maxiter 500 --warm $O/$3.json --out $O/$4
    echo "== N $2 polish done ($(date))"; } 2>&1 | grep -v -E "FutureWarning|warnings.warn" > $O/run_9_3b3_$4.log
  exit 0
fi
PY=/opt/miniconda3/envs/kaiju/bin/python; O=Tools/GaitOpt/out; LOG=$O/run_9_3b3.log; mkdir -p $O; : > $LOG
verdict() { $PY -c "import json,sys; print(json.load(open(sys.argv[1]))['summary']['verdict'])" "$1" 2>/dev/null; }
chain() { # nodes maxiter out
  local W=$O/stride_fr010_lat.json OUT=$3
  for pass in 1 2 3; do
    echo "== N $1 pass $pass: warm $W -> $OUT ($(date))"
    OMP_NUM_THREADS=6 MKL_NUM_THREADS=6 $PY Tools/GaitOpt/stride_opt_tc.py --fr 0.10 --lat --trans --frate 0.5 --vlift 0.5 --nodes $1 --maxiter $2 --warm $W --out $OUT
    V=$(verdict $OUT.json); echo "== N $1 pass $pass verdict: $V ($(date))"
    [ "$V" = "FEASIBLE" ] && break
    W=$OUT.json; OUT=$3_r$((pass + 1))
  done
}
{
chain 40 500 $O/stride_fr010_v2_n40 > $O/run_9_3b3_n40.log 2>&1 &
chain 80 600 $O/stride_fr010_v2_n80 > $O/run_9_3b3_n80.log 2>&1 &
wait
grep -h -E "^== |verdict" $O/run_9_3b3_n40.log $O/run_9_3b3_n80.log
echo "== done ($(date))"
} 2>&1 | grep -v -E "FutureWarning|warnings.warn" | tee -a $LOG
