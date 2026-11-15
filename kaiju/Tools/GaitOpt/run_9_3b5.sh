#!/bin/bash
# T-005 r4 9.3b-5: reference v3 = v2 problem (--lat --trans, Fr 0.10) + the v3 rows (stride_opt_tc.py --v3): monotone/convex/geometric swing-heel descent, decelerating heel x,
# touchdown tangential speed <= 0.4 x normal, slower Fn transfer (0.25 BW / 0.11 s) around every complementarity node. Passes re-solve from their own output.
# Run from the project root:  bash Tools/GaitOpt/run_9_3b5.sh solve <warm stem> <out stem> [extra stride_opt_tc args]   (stems relative to Tools/GaitOpt/out; log out/run_9_3b5_<out stem>.log)
cd "$(dirname "$0")/../.." || exit 1
PY=/opt/miniconda3/envs/kaiju/bin/python; O=Tools/GaitOpt/out
if [ "$1" = "solve" ]; then
  W=$2; OUT=$3; shift 3
  { echo "== solve: warm $O/$W.json -> $O/$OUT ($(date))"
    OMP_NUM_THREADS=6 MKL_NUM_THREADS=6 $PY Tools/GaitOpt/stride_opt_tc.py --fr 0.10 --lat --trans --frate 0.5 --vlift 0.5 --v3 --nodes 40 --maxiter 500 --warm $O/$W.json --out $O/$OUT "$@"
    echo "== done ($(date))"; } 2>&1 | grep -v -E "FutureWarning|warnings.warn" > $O/run_9_3b5_$OUT.log
fi
