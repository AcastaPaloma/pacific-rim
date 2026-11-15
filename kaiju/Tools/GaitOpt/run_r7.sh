#!/bin/bash
# T-005 r7 8a: launch one stride_opt_r7.py solve in the background. Run from the project root (kaiju/):
#   bash Tools/GaitOpt/run_r7.sh <out stem> '<R7 json>' <warm json> <fr> [extra stride_opt_r7 args]      (outputs/log in Tools/GaitOpt/out/r7/<stem>.*)
cd "$(dirname "$0")/../.." || exit 1
N=$1; R=$2; W=$3; FR=$4; shift 4
O=Tools/GaitOpt/out/r7
OMP_NUM_THREADS=${THREADS:-3} MKL_NUM_THREADS=${THREADS:-3} R7="$R" nohup /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/stride_opt_r7.py --fr $FR --lat --trans --frate 0.5 --vlift 0.5 --v3 --nodes 40 --maxiter ${MAXITER:-700} --warm $W --out $O/$N "$@" > $O/$N.log 2>&1 &
echo started $N
