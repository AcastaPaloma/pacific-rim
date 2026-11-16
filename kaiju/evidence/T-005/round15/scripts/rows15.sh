#!/bin/bash
# T-005 r15: re-optimise the v5 ladder rows for the 10 x 15 m sole, stance 16 m (planar GaitOpt, no Unity). Run from kaiju/. Lanes in parallel.
cd "$(dirname "$0")/../../../.." || exit 1
F=$PWD/Tools/GaitOpt/out/r15/frame_s10.json; O=Tools/GaitOpt/out/r7; export R7_FRAME=$F
row(){ # tag fr warm tmin
  R="{\"frame\":\"$F\",\"tmin\":$4}"; THREADS=2 MAXITER=${MAXITER:-600} bash Tools/GaitOpt/chain_r7.sh $1 $O/$3.json "$R" -- $2; }
( row n 0.02 d_020 2.0; row n 0.05 g_050 2.0 ) &
( row n 0.03 d_030 2.0; row n 0.07 g_070 2.0 ) &
( row n 0.09 g_090 2.0; row n 0.10 h2b 1.5 ) &
wait; echo ROWS_DONE
