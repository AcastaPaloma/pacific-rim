#!/bin/bash
# T-005 r7 8a: Froude continuation of the hard-limit stride (flat feet, CoP window, F_z <= 1.5 W, T <= 3 s). Run from the project root (kaiju/):
#   bash Tools/GaitOpt/chain_r7.sh <tag> <warm json> '<R7 json>' <extra args> -- <fr1> <fr2> ...
# each stage solves Fr_i from the previous stage's json (log/out Tools/GaitOpt/out/r7/<tag>_<fr>.*), stage outputs are written even when not converged.
cd "$(dirname "$0")/../.." || exit 1
TAG=$1; WARM=$2; R=$3; shift 3
EXTRA=(); while [ "$1" != "--" ]; do EXTRA+=("$1"); shift; done; shift
O=Tools/GaitOpt/out/r7
for FR in "$@"; do
  N=${TAG}_$(printf "%03d" $(python3 -c "print(round($FR*1000))"))
  OMP_NUM_THREADS=${THREADS:-3} MKL_NUM_THREADS=${THREADS:-3} R7="$R" /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/stride_opt_r7.py --fr $FR --lat --trans --frate 0.5 --vlift 0.5 --v3 --nodes 40 --maxiter ${MAXITER:-600} --warm $WARM --out $O/$N "${EXTRA[@]}" > $O/$N.log 2>&1
  head -3 $O/$N.txt | cut -c1-260
  WARM=$O/$N.json
done
