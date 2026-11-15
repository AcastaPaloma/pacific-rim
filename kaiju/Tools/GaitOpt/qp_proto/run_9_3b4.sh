#!/bin/bash
# T-005 r4 9.3b-4: criteria A, B, C (and D push, mirror symmetry check) for one parameter set of the full-ID QP walker.
# Run from the project root:  bash Tools/GaitOpt/qp_proto/run_9_3b4.sh PREFIX [run_fid args, e.g. --preset b4 --set land_k0=36]
# (Clim08aw: controller told the 80 % limit; its --set must come last, so pass no --set in "$@" for that run.) Writes Tools/GaitOpt/out/proto/PREFIX_{A,Amir,Bp,Bm,Cm09,Cm11,Cmu06,Clim08,Clim08aw,Cdel2,Dp,Dm}.{json,npz}; log PREFIX.log. 4 runs in parallel.
set -u
P=$1; shift
PY="env OMP_NUM_THREADS=1 OPENBLAS_NUM_THREADS=1 VECLIB_MAXIMUM_THREADS=1 /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/qp_proto/run_fid.py --ref Tools/GaitOpt/out/stride_fr010_v2_n40p2.json --friction stick --quiet"
LOG=Tools/GaitOpt/out/proto/$P.log; : > $LOG
run() { t=$1; shift; $PY --tag ${P}_$t "$@" 2>&1 | tail -1 | sed "s/^/$t /" >> $LOG; }
run A "$@" & run Amir --mirror "$@" & run Bp --push 1 "$@" & run Bm --push -1 "$@" & wait
run Cm09 --model_mass 0.9 "$@" & run Cm11 --model_mass 1.1 "$@" & run Cmu06 --mu_plant 0.6 "$@" & run Clim08 --lim_scale 0.8 "$@" & wait
run Clim08aw --lim_scale 0.8 "$@" --set lim_frac=0.784 & run Cdel2 --delay 2 "$@" & run Dp --push 1 --dv_frac 0.30 "$@" & run Dm --push -1 --dv_frac 0.30 "$@" & wait
/opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/qp_proto/summ_9_3b4.py $P | tee -a $LOG
