#!/usr/bin/env bash
# T-005 r4 9.3b-4: criterion A-D set for one controller config (v2 reference, stick plant), all runs in parallel.
# Usage (from the project root): bash Tools/GaitOpt/run_9_3b4.sh PREFIX [key=val ...]   (preset b4 + the given --set options)
# Then: python Tools/GaitOpt/qp_proto/summ_9_3b4.py PREFIX
set -u
PY=/opt/miniconda3/envs/kaiju/bin/python; R=Tools/GaitOpt/qp_proto/run_fid.py; P="$1"; shift
EX=("$@")
C=(--preset b4 --ref Tools/GaitOpt/out/stride_fr010_v2_n40p2.json --friction stick --quiet)
L=Tools/GaitOpt/out/proto/$P.log; : > "$L"
run() { t="$1"; a="$2"; shift 2; ( out=$("$PY" "$R" --tag "${P}_$t" "${C[@]}" "$@" --set $a ${EX[@]+"${EX[@]}"} 2>&1 | tail -1); echo "$t $out" >> "$L" ) & }
# 2nd arg: run-specific --set option ("" none). Clim08aw: the controller is configured with the 80 % limit (lim_frac 0.98 x 0.8); Clim08: it is not.
run A ""; run Amir "" --mirror; run Bp "" --push 1; run Bm "" --push -1
run Cm09 "" --model_mass 0.9; run Cm11 "" --model_mass 1.1; run Cmu06 "" --mu_plant 0.6; run Clim08 "" --lim_scale 0.8; run Clim08aw lim_frac=0.784 --lim_scale 0.8; run Cdel2 "" --delay 2
run Dp "" --push 1 --dv_frac 0.3; run Dm "" --push -1 --dv_frac 0.3
wait
